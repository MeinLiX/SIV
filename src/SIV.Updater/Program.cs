using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SIV.Updater;

internal static class Program
{
    private static StreamWriter? _logWriter;

    static async Task<int> Main(string[] args)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SIV", "updater", "logs");
        Directory.CreateDirectory(logDir);
        var logFile = Path.Combine(logDir, $"updater-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        _logWriter = new StreamWriter(logFile, append: true) { AutoFlush = true };

        try
        {
            var appDir = GetArg(args, "--app-dir");
            var downloadUrl = GetArg(args, "--download-url");
            var expectedHash = GetArg(args, "--expected-hash");
            var appPid = GetArg(args, "--app-pid");
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
                var tempDir = Path.Combine(Path.GetTempPath(), "SIV-Updater");
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                var currentExe = Environment.ProcessPath
                    ?? throw new InvalidOperationException("Cannot determine current executable path.");
                var tempExe = Path.Combine(tempDir, Path.GetFileName(currentExe));
                File.Copy(currentExe, tempExe, overwrite: true);

                var escapedArgs = args
                    .Append("--relocated")
                    .Select(a => a.Contains(' ') ? $"\"{a}\"" : a);

                Process.Start(new ProcessStartInfo
                {
                    FileName = tempExe,
                    Arguments = string.Join(' ', escapedArgs),
                    UseShellExecute = false
                });
                return 0;
            }

            if (!string.IsNullOrEmpty(appPid) && int.TryParse(appPid, out var pid))
            {
                Log($"Waiting for main app (PID {pid}) to exit...");
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (!proc.WaitForExit(TimeSpan.FromSeconds(30)))
                    {
                        Log("WARNING: Main app did not exit within 30 seconds, proceeding anyway.");
                    }
                }
                catch (ArgumentException)
                {
                    Log("Main app already exited.");
                }
            }

            Log($"Downloading update from: {downloadUrl}");
            var tempZip = Path.Combine(Path.GetTempPath(), $"SIV-update-{Guid.NewGuid():N}.zip");

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SIV-Updater/1.0");

            using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = File.Create(tempZip))
            {
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
            }
            Log("Download complete.");

            if (!string.IsNullOrEmpty(expectedHash))
            {
                Log("Verifying SHA256 hash...");
                var actualHash = await ComputeSha256Async(tempZip);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"HASH MISMATCH! Expected: {expectedHash}, Got: {actualHash}");
                    File.Delete(tempZip);
                    return 1;
                }
                Log("Hash verified successfully.");
            }
            else
            {
                Log("WARNING: No expected hash provided, skipping verification.");
            }

            var extractDir = Path.Combine(Path.GetTempPath(), $"SIV-extract-{Guid.NewGuid():N}");
            Log($"Extracting to: {extractDir}");
            ZipFile.ExtractToDirectory(tempZip, extractDir, overwriteFiles: true);
            File.Delete(tempZip);

            Log($"Installing update to: {appDir}");
            CopyDirectory(extractDir, appDir);

            try { Directory.Delete(extractDir, true); }
            catch { /* best effort */ }

            var appExe = Path.Combine(appDir, "runtime", "SIV.App.exe");
            if (!File.Exists(appExe))
                appExe = Path.Combine(appDir, "SIV.App.exe");

            if (File.Exists(appExe))
            {
                Log($"Launching updated app: {appExe}");
                Process.Start(new ProcessStartInfo
                {
                    FileName = appExe,
                    WorkingDirectory = appDir,
                    UseShellExecute = true
                });
            }
            else
            {
                Log("WARNING: SIV.App.exe not found after update!");
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

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destFile = Path.Combine(destination, relative);

            try
            {
                File.Copy(file, destFile, overwrite: true);
            }
            catch (IOException)
            {
                Log($"WARNING: Skipping locked file: {relative}");
            }
        }
    }

    private static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        Console.WriteLine(line);
        _logWriter?.WriteLine(line);
    }
}
