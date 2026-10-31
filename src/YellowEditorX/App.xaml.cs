using System.IO;
using System.Windows;

namespace YellowEditorX;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // YellowEditorX.exe --selftest <workspace copy> <report file>: runs the editor on copied data, scripted.
        if (e.Args.Length >= 3 && e.Args[0] == "--selftest")
        {
            var w = new MainWindow(new Settings
            {
                OpenDbs = YellowEditorX.Core.Workspace.Scan(e.Args[1]).Select(d => d.Path).ToList(),
                ReadOnly = true,
                GitHubSync = false,
                UpdateCheck = false,
            });
            if (!w.Ready) { Shutdown(1); return; }
            w.Show();
            w.Dispatcher.InvokeAsync(async () =>
            {
                string report;
                try { report = await w.RunSelfTest(Path.GetDirectoryName(Path.GetFullPath(e.Args[2]))!); }
                catch (Exception ex) { report = "CRASH " + ex; }
                File.WriteAllText(e.Args[2], report);
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }
        var main = new MainWindow();
        if (main.Ready) main.Show();
        else Shutdown(1);
    }
}
