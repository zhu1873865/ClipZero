using ClipZero.UI;

namespace ClipZero;

internal static class Program
{
    // A local-session mutex matches the scope of the Windows clipboard and tray.
    internal const string MutexName = @"Local\ClipZero.SingleInstance.v1";
    internal const string WindowTitle = "ClipZero.MessageWindow.91cf9366-240d-4dcb-a84a-e7ff9a97ed57";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(false, MutexName);
        bool acquired;
        try { acquired = instance.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            // Post a message: a second launch never touches the clipboard.
            nint window = 0;
            // The first process may still be constructing its hidden message window.
            for (int attempt = 0; attempt < 20 && window == 0; attempt++)
            {
                window = Interop.NativeMethods.FindWindow(null, WindowTitle);
                if (window == 0) Thread.Sleep(50);
            }
            if (window != 0)
            {
                Interop.NativeMethods.AllowSetForegroundWindow(uint.MaxValue);
                Interop.NativeMethods.PostMessage(window, MessageWindow.ShowWindowMessage, 0, 0);
            }
            return;
        }

        try
        {
            using var context = new TrayApplicationContext(args.Contains("--show", StringComparer.OrdinalIgnoreCase));
            Application.Run(context);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"ClipZero 无法启动。\n{exception.Message}", "ClipZero", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { instance.ReleaseMutex(); }
    }
}
