using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SIV.Updater;

internal static class Program
{
    private static StreamWriter? _logWriter;

    private const int FileSystemRetryMaxAttempts = 15;
    private static readonly TimeSpan FileSystemRetryDelay = TimeSpan.FromSeconds(2);
    private const int DownloadRetryMaxAttempts = 3;
    private static readonly TimeSpan DownloadRetryDelay = TimeSpan.FromSeconds(3);
    private const string ManifestFileName = "siv-release-manifest.json";
    private const long DiskSpaceSafetyMarginBytes = 128L * 1024 * 1024;
    private const string PreservedUpdaterRelativePath = "runtime/updater";

    static async Task<int> Main(string[] args)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SIV", "updater", "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"updater-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        _logWriter = new StreamWriter(logFile, append: true) { AutoFlush = true };

        string? tempZip = null;
        string? stageDir = null;

        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());

            var appDir = GetArg(args, "--app-dir");
            var downloadUrl = GetArg(args, "--download-url");
            var expectedHash = GetArg(args, "--expected-hash");
            var appPid = GetArg(args, "--app-pid");
            var waitForPid = GetArg(args, "--wait-for-pid");
            bool relocated = args.Contains("--relocated");

            if (string.IsNullOrEmpty(appDir) || string.IsNullOrEmpty(downloadUrl))
            {
                Log("ERROR: Required arguments: --app-dir <path> --download-url <url>");
                Log("Optional: --expected-hash <sha256> --app-pid <pid>");
                return 1;
            }

            if (!relocated)
            {
                Log("Relocating updater to temp directory...");
                var tempDir = Path.Combine(Path.GetTempPath(), $"SIV-Updater-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                var currentExe = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Cannot determine current executable path.");
                var tempExe = Path.Combine(tempDir, Path.GetFileName(currentExe));
                File.Copy(currentExe, tempExe, overwrite: true);

                var relocateStartInfo = new ProcessStartInfo
                {
                    FileName = tempExe,
                    WorkingDirectory = tempDir,
                    UseShellExecute = false
                };

                foreach (var arg in args)
                    relocateStartInfo.ArgumentList.Add(arg);

                relocateStartInfo.ArgumentList.Add("--wait-for-pid");
                relocateStartInfo.ArgumentList.Add(Environment.ProcessId.ToString());
                relocateStartInfo.ArgumentList.Add("--relocated");

                Process.Start(relocateStartInfo);
                return 0;
            }

            appDir = Path.GetFullPath(appDir);
            var appParentDir = Directory.GetParent(appDir)?.FullName
                ?? throw new InvalidOperationException($"Cannot determine parent directory for '{appDir}'.");

            EnsureDirectoryWriteAccess(appParentDir);
            LogProtectedInstallLocation(appDir);

            if (!string.IsNullOrEmpty(waitForPid) && int.TryParse(waitForPid, out var originalUpdaterPid))
            {
                Log($"Waiting for original updater process (PID {originalUpdaterPid}) to exit...");
                WaitForProcessExit(originalUpdaterPid, TimeSpan.FromSeconds(30), "original updater process");
                await Task.Delay(1000);
            }

            if (!string.IsNullOrEmpty(appPid) && int.TryParse(appPid, out var pid))
            {
                Log($"Waiting for main app (PID {pid}) to exit...");
                WaitForProcessExit(pid, TimeSpan.FromSeconds(30), "main app");

                Log("Waiting for file handles to be released...");
                await Task.Delay(3000);
            }

            EnsureInstalledAppProcessesStopped(appDir);

            Log($"Downloading update from: {downloadUrl}");
            tempZip = Path.Combine(Path.GetTempPath(), $"SIV-update-{Guid.NewGuid():N}.zip");

            await DownloadFileWithRetriesAsync(downloadUrl, tempZip);

            if (!string.IsNullOrEmpty(expectedHash))
            {
                Log("Verifying release archive SHA256 hash...");
                var actualHash = await ComputeSha256Async(tempZip);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Release archive hash mismatch. Expected '{expectedHash}', got '{actualHash}'.");
                }
                Log("Release archive hash verified.");
            }
            else
            {
                Log("WARNING: No expected archive hash provided by the update feed. Continuing with manifest validation only.");
            }

            stageDir = CreateSiblingWorkspacePath(appParentDir, Path.GetFileName(appDir), ".stage");
            Directory.CreateDirectory(stageDir);
            Log($"Extracting update to staging directory: {stageDir}");

            ValidateZipEntries(tempZip, stageDir);
            ZipFile.ExtractToDirectory(tempZip, stageDir, overwriteFiles: true);
            RemoveZoneIdentifiers(stageDir);

            EnsureRequiredReleaseFiles(stageDir);

            var releaseManifest = await LoadReleaseManifestAsync(stageDir);
            if (releaseManifest is null)
                Log($"WARNING: '{ManifestFileName}' is missing. Continuing without per-file manifest validation.");
            else
                await ValidateReleaseManifestAsync(stageDir, releaseManifest);

            PreserveInstalledUpdaterPayload(appDir, stageDir);

            var stageSize = GetDirectorySize(stageDir);
            Log($"Prepared staged release ({stageSize / 1024 / 1024} MB).");
            EnsureSufficientDiskSpace(appParentDir, DiskSpaceSafetyMarginBytes);

            Log($"Installing update to: {appDir}");
            await InstallStagedReleaseAsync(appDir, stageDir, releaseManifest);
            stageDir = null;

            var (launcherExe, workingDirectory) = ResolveLaunchTarget(appDir);
            if (launcherExe is not null)
            {
                Log($"Launching updated app: {launcherExe}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = launcherExe,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false
                });
            }
            else
            {
                Log("WARNING: No executable found to launch after update!");
            }

            Log("Update completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex}");
            return 1;
        }
        finally
        {
            TryDeleteFile(tempZip);
            TryDeleteDirectory(stageDir);
            _logWriter?.Dispose();
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task DownloadFileWithRetriesAsync(string downloadUrl, string destinationPath)
    {
        Exception? lastError = null;

        for (int attempt = 1; attempt <= DownloadRetryMaxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);

                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("SIV-Updater/2.0");

                using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                EnsureSufficientDiskSpace(Path.GetTempPath(), (totalBytes ?? 0) + DiskSpaceSafetyMarginBytes);

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = File.Create(destinationPath);

                var buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    downloaded += bytesRead;

                    if (totalBytes > 0)
                    {
                        var pct = (double)downloaded / totalBytes.Value * 100;
                        Console.Write($"\rDownloading: {pct:F1}% ({downloaded / 1024 / 1024} MB)");
                    }
                }

                Console.WriteLine();
                Log("Download complete.");
                return;
            }
            catch (Exception ex) when (IsTransientDownloadFailure(ex) && attempt < DownloadRetryMaxAttempts)
            {
                lastError = ex;
                Console.WriteLine();
                Log($"Download attempt {attempt}/{DownloadRetryMaxAttempts} failed: {ex.Message}");
                await Task.Delay(DownloadRetryDelay);
            }
        }

        throw new InvalidOperationException("Failed to download the update package.", lastError);
    }

    private static bool IsTransientDownloadFailure(Exception ex)
    {
        return ex is HttpRequestException or IOException or TaskCanceledException;
    }

    private static void RemoveZoneIdentifiers(string directory)
    {
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            var zoneFile = file + ":Zone.Identifier";
            try
            {
                if (File.Exists(zoneFile))
                    File.Delete(zoneFile);
            }
            catch { }
        }
    }

    private static async Task<ReleaseManifest?> LoadReleaseManifestAsync(string stageDir)
    {
        var manifestPath = Path.Combine(stageDir, ManifestFileName);
        if (!File.Exists(manifestPath))
            return null;

        await using var stream = File.OpenRead(manifestPath);
        return await JsonSerializer.DeserializeAsync(stream, UpdaterJsonContext.Default.ReleaseManifest);
    }

    private static async Task ValidateReleaseManifestAsync(string stageDir, ReleaseManifest manifest)
    {
        if (manifest.Format != 1)
            throw new InvalidOperationException($"Unsupported release manifest format '{manifest.Format}'.");

        if (!string.Equals(manifest.RuntimeIdentifier, "win-x64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Unsupported runtime identifier '{manifest.RuntimeIdentifier}' in release manifest.");

        if (manifest.Files.Count == 0)
            throw new InvalidOperationException("Release manifest does not contain any files.");

        var stageRoot = AppendDirectorySeparator(Path.GetFullPath(stageDir));
        var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Path))
                throw new InvalidOperationException("Release manifest contains an empty path entry.");

            var normalizedRelativePath = NormalizeRelativePath(file.Path);
            if (!expectedFiles.Add(normalizedRelativePath))
                throw new InvalidOperationException($"Duplicate file '{normalizedRelativePath}' in release manifest.");

            var fullPath = Path.GetFullPath(Path.Combine(stageDir, normalizedRelativePath));
            if (!fullPath.StartsWith(stageRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Release manifest path escapes staging directory: '{file.Path}'.");

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Manifest file '{normalizedRelativePath}' is missing from the staged release.", fullPath);

            var info = new FileInfo(fullPath);
            if (info.Length != file.Size)
            {
                throw new InvalidOperationException(
                    $"Manifest size mismatch for '{normalizedRelativePath}'. Expected {file.Size}, got {info.Length}.");
            }

            var actualHash = await ComputeSha256Async(fullPath);
            if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Manifest hash mismatch for '{normalizedRelativePath}'. Expected '{file.Sha256}', got '{actualHash}'.");
            }
        }

        var actualFiles = Directory
            .GetFiles(stageDir, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(stageDir, path)))
            .Where(path => !path.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!actualFiles.SetEquals(expectedFiles))
        {
            var extraFiles = actualFiles.Except(expectedFiles, StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray();
            var missingFiles = expectedFiles.Except(actualFiles, StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray();

            throw new InvalidOperationException(
                $"Staged release does not match the manifest. Missing: [{string.Join(", ", missingFiles)}]. Extra: [{string.Join(", ", extraFiles)}].");
        }

        Log($"Validated {manifest.Files.Count} files against the release manifest.");
    }

    private static void PreserveInstalledUpdaterPayload(string appDir, string stageDir)
    {
        var currentUpdaterDir = Path.Combine(appDir, "runtime", "updater");
        if (!Directory.Exists(currentUpdaterDir))
            return;

        var stagedUpdaterDir = Path.Combine(stageDir, "runtime", "updater");
        TryDeleteDirectory(stagedUpdaterDir);
        Directory.CreateDirectory(stagedUpdaterDir);
        CopyDirectoryContents(currentUpdaterDir, stagedUpdaterDir);
        Log("Preserved the installed updater payload. Release updater files were ignored for this update.");
    }

    private static void EnsureRequiredReleaseFiles(string stageDir)
    {
        var requiredFiles = new[]
        {
            "SIV.exe",
            Path.Combine("runtime", "SIV.App.exe"),
            Path.Combine("runtime", "updater", "SIV.Updater.exe"),
        };

        foreach (var relativePath in requiredFiles)
        {
            var fullPath = Path.Combine(stageDir, relativePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Required release file is missing: '{relativePath}'.", fullPath);
        }
    }

    private static void CopyDirectoryContents(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destinationDir, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destinationFile = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static async Task InstallStagedReleaseAsync(string appDir, string stagedDir, ReleaseManifest? manifest)
    {
        Directory.CreateDirectory(appDir);

        var stagedFiles = Directory
            .GetFiles(stagedDir, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(stagedDir, path)))
            .Where(path => !IsPreservedRelativePath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stagedDirectories = Directory
            .GetDirectories(stagedDir, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(stagedDir, path)))
            .Where(path => !IsPreservedRelativePath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relativeDirectory in stagedDirectories.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var destinationDirectory = Path.Combine(appDir, relativeDirectory);
            Directory.CreateDirectory(destinationDirectory);
        }

        foreach (var relativeFile in stagedFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var sourceFile = Path.Combine(stagedDir, relativeFile);
            var destinationFile = Path.Combine(appDir, relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

            await RetryFileSystemOperationAsync(
                $"copy '{relativeFile}'",
                () => File.Copy(sourceFile, destinationFile, overwrite: true));
        }

        var installedFiles = Directory
            .GetFiles(appDir, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(appDir, path)))
            .Where(path => !IsPreservedRelativePath(path))
            .ToArray();

        var staleFiles = installedFiles
            .Where(path => !stagedFiles.Contains(path))
            .OrderByDescending(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var staleFile in staleFiles)
        {
            var fullPath = Path.Combine(appDir, staleFile);
            await RetryFileSystemOperationAsync(
                $"delete stale file '{staleFile}'",
                () => File.Delete(fullPath));
        }

        var installedDirectories = Directory
            .GetDirectories(appDir, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelativePath(Path.GetRelativePath(appDir, path)))
            .Where(path => !IsPreservedRelativePath(path))
            .OrderByDescending(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var relativeDirectory in installedDirectories)
        {
            var fullPath = Path.Combine(appDir, relativeDirectory);
            if (Directory.Exists(fullPath) &&
                !Directory.EnumerateFileSystemEntries(fullPath).Any() &&
                !stagedDirectories.Contains(relativeDirectory))
            {
                await RetryFileSystemOperationAsync(
                    $"delete stale directory '{relativeDirectory}'",
                    () => Directory.Delete(fullPath, recursive: false));
            }
        }

        if (manifest is not null)
            await ValidateInstalledReleaseAsync(appDir, manifest);
    }

    private static async Task RetryFileSystemOperationAsync(string operationName, Action operation)
    {
        Exception? lastError = null;

        for (int attempt = 1; attempt <= FileSystemRetryMaxAttempts; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                if (attempt == FileSystemRetryMaxAttempts)
                    break;

                Log($"{operationName} failed on attempt {attempt}/{FileSystemRetryMaxAttempts}: {ex.Message}");
                await Task.Delay(FileSystemRetryDelay);
            }
        }

        throw new InvalidOperationException($"Failed to {operationName}.", lastError);
    }

    private static void ValidateZipEntries(string zipPath, string destinationRoot)
    {
        var rootWithSeparator = AppendDirectorySeparator(Path.GetFullPath(destinationRoot));

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
                continue;

            var normalizedEntryPath = entry.FullName.Replace('\\', '/');
            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, normalizedEntryPath));
            if (!destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The update archive contains an invalid entry path: '{entry.FullName}'.");
            }
        }
    }

    private static void EnsureDirectoryWriteAccess(string directory)
    {
        Directory.CreateDirectory(directory);

        var probePath = Path.Combine(directory, $".siv-update-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.WriteByte(0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Updater does not have write access to '{directory}'. Install SIV into a user-writable directory or run it with elevated permissions.",
                ex);
        }
        finally
        {
            TryDeleteFile(probePath);
        }
    }

    private static void EnsureSufficientDiskSpace(string path, long requiredBytes)
    {
        if (requiredBytes <= 0)
            return;

        var root = Path.GetPathRoot(Path.GetFullPath(path))
            ?? throw new InvalidOperationException($"Cannot determine drive root for '{path}'.");
        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < requiredBytes)
        {
            throw new InvalidOperationException(
                $"Not enough free disk space on '{root}'. Required {requiredBytes / 1024 / 1024} MB, available {drive.AvailableFreeSpace / 1024 / 1024} MB.");
        }
    }

    private static long GetDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        long totalBytes = 0;
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            totalBytes += new FileInfo(file).Length;

        return totalBytes;
    }

    private static string CreateSiblingWorkspacePath(string parentDirectory, string appFolderName, string suffix)
    {
        return Path.Combine(parentDirectory, $"{appFolderName}{suffix}-{Guid.NewGuid():N}");
    }

    private static (string? ExecutablePath, string WorkingDirectory) ResolveLaunchTarget(string appDir)
    {
        var launcherExe = Path.Combine(appDir, "SIV.exe");
        if (File.Exists(launcherExe))
            return (launcherExe, appDir);

        var runtimeExe = Path.Combine(appDir, "runtime", "SIV.App.exe");
        if (File.Exists(runtimeExe))
            return (runtimeExe, Path.GetDirectoryName(runtimeExe)!);

        var rootAppExe = Path.Combine(appDir, "SIV.App.exe");
        if (File.Exists(rootAppExe))
            return (rootAppExe, appDir);

        return (null, appDir);
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static bool IsPreservedRelativePath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return normalized.Equals(PreservedUpdaterRelativePath, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(PreservedUpdaterRelativePath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ValidateInstalledReleaseAsync(string appDir, ReleaseManifest manifest)
    {
        foreach (var file in manifest.Files)
        {
            var relativePath = NormalizeRelativePath(file.Path);
            if (IsPreservedRelativePath(relativePath))
                continue;

            var fullPath = Path.Combine(appDir, relativePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Installed file '{relativePath}' is missing after update.", fullPath);

            var info = new FileInfo(fullPath);
            if (info.Length != file.Size)
            {
                throw new InvalidOperationException(
                    $"Installed file size mismatch for '{relativePath}'. Expected {file.Size}, got {info.Length}.");
            }

            var actualHash = await ComputeSha256Async(fullPath);
            if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Installed file hash mismatch for '{relativePath}'. Expected '{file.Sha256}', got '{actualHash}'.");
            }
        }

        Log("Validated installed release files after in-place synchronization.");
    }

    private static string AppendDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void LogProtectedInstallLocation(string appDir)
    {
        var protectedRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => AppendDirectorySeparator(Path.GetFullPath(path)))
        .ToArray();

        var normalizedAppDir = AppendDirectorySeparator(Path.GetFullPath(appDir));
        if (protectedRoots.Any(root => normalizedAppDir.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
        {
            Log("WARNING: SIV is installed under Program Files. Updates may fail without elevated permissions.");
        }
    }

    private static void WaitForProcessExit(int pid, TimeSpan timeout, string description)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.WaitForExit(timeout))
            {
                Log($"WARNING: {description} did not exit within {timeout.TotalSeconds:F0} seconds. Attempting to kill...");
                try { proc.Kill(entireProcessTree: true); proc.WaitForExit(5000); }
                catch { /* best effort */ }
            }
        }
        catch (ArgumentException)
        {
            Log($"{description} already exited.");
        }
    }

    private static void EnsureInstalledAppProcessesStopped(string appDir)
    {
        var trackedExecutables = new[]
        {
            Path.GetFullPath(Path.Combine(appDir, "SIV.exe")),
            Path.GetFullPath(Path.Combine(appDir, "runtime", "SIV.App.exe")),
            Path.GetFullPath(Path.Combine(appDir, "SIV.App.exe")),
        };

        var seenPids = new HashSet<int>();

        foreach (var executablePath in trackedExecutables)
        {
            var processName = Path.GetFileNameWithoutExtension(executablePath);
            if (string.IsNullOrWhiteSpace(processName))
                continue;

            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (!seenPids.Add(process.Id))
                        continue;

                    if (process.Id == Environment.ProcessId)
                        continue;

                    string? processPath = null;
                    try
                    {
                        processPath = process.MainModule?.FileName;
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(processPath))
                        continue;

                    if (!string.Equals(
                            Path.GetFullPath(processPath),
                            executablePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    Log($"Detected running installed process '{Path.GetFileName(processPath)}' (PID {process.Id}). Attempting to stop it...");

                    try
                    {
                        if (!process.WaitForExit(1000))
                        {
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(5000);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"WARNING: Failed to stop process PID {process.Id}: {ex.Message}");
                    }
                }
            }
        }

        Log("Verified that no installed SIV app processes are still running.");
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        _logWriter?.WriteLine(line);
    }
}
