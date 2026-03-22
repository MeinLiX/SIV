using System.Diagnostics;

var runtimeDir = Path.Combine(AppContext.BaseDirectory, "runtime");
var appExe = Path.Combine(runtimeDir, "SIV.App.exe");

if (!File.Exists(appExe))
    return;

Process.Start(new ProcessStartInfo
{
    FileName = appExe,
    WorkingDirectory = runtimeDir,
    UseShellExecute = false
});
