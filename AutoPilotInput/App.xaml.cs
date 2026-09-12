using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;

namespace AutoPilotInput;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    public static bool IsPreview { get; private set; }
    public static string? PreviewPath { get; private set; }
    public static bool PreviewLight { get; private set; }
    public static bool VerifyUi { get; private set; }
    public static string PreviewScenario { get; private set; } = "dashboard";
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string value);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    public static uint ShowMessage => RegisterWindowMessage("AutoPilotInput.Show.v1");
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var render = Array.IndexOf(e.Args, "--render");
        IsPreview = render >= 0;
        PreviewPath = IsPreview && render + 1 < e.Args.Length ? Path.GetFullPath(e.Args[render + 1]) : null;
        PreviewLight = e.Args.Contains("--light");
        VerifyUi = IsPreview && e.Args.Contains("--verify");
        var scenario = Array.IndexOf(e.Args, "--scenario");
        if (scenario >= 0 && scenario + 1 < e.Args.Length) PreviewScenario = e.Args[scenario + 1];
        if (!IsPreview)
        {
            _mutex = new Mutex(true, "Local\\AutoPilotInput.v1", out var created);
            if (!created) { PostMessage((IntPtr)0xFFFF, ShowMessage, IntPtr.Zero, IntPtr.Zero); _mutex.Dispose(); _mutex = null; Shutdown(); return; }
        }
        DispatcherUnhandledException += (_, args) =>
        {
            try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "AutoPilotInput-error.log"), DateTime.Now + " " + args.Exception + Environment.NewLine); } catch { }
            if (IsPreview) { args.Handled = true; Shutdown(1); return; }
            // Unexpected failures terminate the automation instead of silently continuing input.
            if (MainWindow is MainWindow window) window.EmergencyStop();
            System.Windows.MessageBox.Show(args.Exception.Message, "KeyTempo", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        new MainWindow().Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutex is not null) { _mutex.ReleaseMutex(); _mutex.Dispose(); }
        base.OnExit(e);
    }
}
