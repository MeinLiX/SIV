using System.Diagnostics;

var appExe = Path.Combine(AppContext.BaseDirectory, "runtime", "SIV.App.exe");

if (!File.Exists(appExe))
    return;

Process.Start(new ProcessStartInfo
{
    FileName = appExe,
    UseShellExecute = false
});
