using System.Drawing.Imaging;
using ClipZero.Core;
using ClipZero.Interop;
using ClipZero.UI;

namespace ClipZero.Tests;

internal static class AppNativeTests
{
    // Real clipboard writes are restricted to the verified isolated window-station child.
    // The safe local mode posts synthetic window messages and never reads or writes clipboard data.
    internal static void Run(Action<string, Action> test, string fixture, bool manipulateClipboard = true)
    {
        test(manipulateClipboard ? "application message window receives clipboard notifications and activation"
            : "application message window handles update messages and activation", () =>
        {
            using var window = new MessageWindow();
            Program.True(window.ListenerRegistered);
            bool updated = false;
            bool shown = false;
            window.ClipboardUpdated += () => updated = true;
            window.ShowRequested += () => shown = true;
            Program.Equal(window.Handle, NativeMethods.FindWindow(null, ClipZero.Program.WindowTitle));
            Program.True(NativeMethods.PostMessage(window.Handle, MessageWindow.ShowWindowMessage, 0, 0));
            if (manipulateClipboard)
            {
                var gateway = new NativeClipboardGateway(window.Handle);
                Program.True(gateway.TryOpen(out IClipboardSession? session));
                using (session) { Program.True(session!.Empty()); }
            }
            else
            {
                Program.True(NativeMethods.PostMessage(window.Handle, 0x031D, 0, 0));
            }
            PumpUntil(() => updated && shown);
        });

        test("hotkey failure is reported; conflict and unregister tested when available", () =>
        {
            var first = new MessageWindow();
            try
            {
                if (!first.HotKeyRegistered)
                {
                    using var unavailableForm = new MainForm();
                    unavailableForm.SetHotKeyAvailability(false);
                    Console.WriteLine("SKIP Hotkey success/conflict/unregister branches: initial shortcut is unavailable on this desktop; failure handling passed.");
                    return;
                }
                // RegisterHotKey permits some duplicate registrations within one thread.
                // A separate STA models a genuinely competing application registration.
                using var ready = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                bool conflictRegistered = false;
                Exception? workerError = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        using var conflict = new MessageWindow();
                        conflictRegistered = conflict.HotKeyRegistered;
                        ready.Set();
                        release.Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception exception) { workerError = exception; ready.Set(); }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                try
                {
                    Program.True(ready.Wait(TimeSpan.FromSeconds(5)));
                    if (workerError is not null) { throw workerError; }
                    if (conflictRegistered)
                    {
                        throw new InvalidOperationException("A competing thread unexpectedly registered the occupied hotkey.");
                    }
                }
                finally { release.Set(); thread.Join(); }
            }
            finally { first.Dispose(); }
            using var afterExit = new MessageWindow();
            if (!afterExit.HotKeyRegistered)
            {
                throw new InvalidOperationException("The hotkey was not released when its owner exited.");
            }
        });

        test("clipboard worker dispatches to a separate STA thread", () =>
        {
            int caller = Environment.CurrentManagedThreadId;
            using var worker = new ClipboardWorker();
            var result = worker.InvokeAsync(handle => (handle, Environment.CurrentManagedThreadId, Thread.CurrentThread.GetApartmentState()));
            PumpUntil(() => result.IsCompleted);
            var (handle, thread, apartment) = result.GetAwaiter().GetResult();
            Program.True(handle != 0 && thread != caller);
            Program.Equal(ApartmentState.STA, apartment);
        });

        test("main window renders states, dispatches actions, and keeps controls reachable when compressed", NativeTests.RunUIRender);
    }

    internal static void RenderStates()
    {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Console.WriteLine("UI constructing form.");
            using var form = new MainForm();
            Console.WriteLine("UI showing form on private desktop.");
            form.Show();
            Console.WriteLine("UI form shown.");
            Application.DoEvents();
            form.UpdateExplorerUndo(new(false, true, false, "未保存关闭设置"), false);
            string output = Path.Combine(Environment.CurrentDirectory, "artifacts", "screenshots");
            Directory.CreateDirectory(output);
            foreach ((ClipboardState state, string name) in new[]
            {
                (ClipboardState.Copied, "copied"), (ClipboardState.Cut, "cut"),
                (ClipboardState.None, "empty"), (ClipboardState.Unavailable, "unavailable")
            })
            {
                bool files = state is ClipboardState.Copied or ClipboardState.Cut;
                form.UpdateSnapshot(new(state, 1, files ? 3 : 0,
                    files ? ["项目资料", "会议记录.txt", "设计草稿.png"] : []), false);
                form.PerformLayout();
                form.Refresh();
                Application.DoEvents();
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, form.ClientRectangle with { Size = form.Size });
                bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                Console.WriteLine($"UI rendered {name}.");
                ListBox list = MainFormUITests.Descendants(form).OfType<ListBox>().Single();
                Program.Equal(files ? 3 : 0, list.Items.Count);
                Button button = MainFormUITests.Descendants(form).OfType<Button>().Single(button =>
                    button.AccessibleDescription == "清空当前文件剪贴板，原始文件不受影响");
                Program.Equal(files, button.Enabled);
            }
            ExplorerUndoUITests.VerifyForm(form, output);
            MainFormUITests.VerifyActionsAndLayout(form, output);
            ExplorerUndoUITests.VerifyTrayIntegration();
            form.SetHotKeyAvailability(false);
            form.Exit();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        long deadline = Environment.TickCount64 + 5000;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Program.True(condition());
    }

}
