using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ClipZero.Core;
using ClipZero.Interop;

namespace ClipZero.Tests;

/// <summary>
/// Native integration tests run in a separate, noninteractive WINDOW STATION. Creating only
/// a separate desktop would still share the user's clipboard and is deliberately not used.
/// The child verifies its station before any clipboard access. No fallback touches WinSta0.
/// </summary>
internal static class NativeTests
{
    internal static void RunUIRender()
    {
        string desktopName = "ClipZeroUITests_" + Guid.NewGuid().ToString("N");
        string report = Path.Combine(Path.GetTempPath(), desktopName + ".txt");
        nint desktop = CreateDesktopW(desktopName, null, 0, 0, 0x000F01FF, 0);
        if (desktop == 0) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create private desktop for UI rendering."); }
        ProcessInformation process = default;
        bool started = false;
        try
        {
            var station = new StringBuilder(256);
            if (!GetUserObjectInformationW(GetProcessWindowStation(), 2, station, 512, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("No test executable path.");
            string command = $"\"{executable}\" " +
                (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    ? $"\"{Assembly.GetExecutingAssembly().Location}\" " : "") +
                $"--ui-render {desktopName} \"{report}\"";
            var startup = new StartupInformation { Size = (uint)Marshal.SizeOf<StartupInformation>(), Desktop = station + "\\" + desktopName };
            if (!CreateProcessW(executable, new StringBuilder(command), 0, 0, false, 0x08000000,
                0, Environment.CurrentDirectory, ref startup, out process))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not launch private UI child.");
            }
            started = true;
            uint wait = WaitForSingleObject(process.Process, 15_000);
            if (wait != 0)
            {
                TerminateProcess(process.Process, 1);
                WaitForSingleObject(process.Process, 1000);
                string detail = File.Exists(report) ? File.ReadAllText(report) : "No child log.";
                throw new TimeoutException("Private UI child timed out. " + detail);
            }
            if (!GetExitCodeProcess(process.Process, out uint exitCode) || exitCode != 0)
            {
                string detail = File.Exists(report) ? File.ReadAllText(report) : "No child log.";
                throw new InvalidOperationException("Private UI child failed. " + detail);
            }
        }
        finally
        {
            if (started) { CloseHandle(process.Thread); CloseHandle(process.Process); }
            CloseDesktop(desktop);
            if (File.Exists(report)) { File.Delete(report); }
        }
    }

    internal static int RunUIChild(string expectedDesktop, string report)
    {
        using var writer = new StreamWriter(report) { AutoFlush = true };
        Console.SetOut(writer);
        try
        {
            var desktop = new StringBuilder(256);
            if (!expectedDesktop.StartsWith("ClipZeroUITests_", StringComparison.Ordinal) ||
                !GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), 2, desktop, 512, out _) ||
                !string.Equals(desktop.ToString(), expectedDesktop, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Refusing to show a test form outside its private desktop.");
            }
            AppNativeTests.RenderStates();
            return 0;
        }
        catch (Exception exception) { Console.WriteLine(exception); return 1; }
    }

    internal static int RunIsolated()
    {
        string runName = "ClipZeroTests_" + Guid.NewGuid().ToString("N");
        string report = Path.Combine(Path.GetTempPath(), runName + ".txt");
        nint original = GetProcessWindowStation();
        // Named stations require administrator membership. NULL lets Windows derive a name
        // from the logon session. CREATE_ONLY guarantees we never reuse somebody else's station.
        nint station = CreateWindowStationW(null, 1, 0x02000000, 0); // CWF_CREATE_ONLY, MAXIMUM_ALLOWED
        if (station == 0)
        {
            Console.WriteLine($"SKIP Native: could not create isolated window station ({Marshal.GetLastWin32Error()}).");
            return 0;
        }

        nint desktop = 0;
        ProcessInformation process = default;
        bool processStarted = false;
        try
        {
            var stationBuffer = new StringBuilder(256);
            if (!GetUserObjectInformationW(station, 2, stationBuffer, 512, out _) ||
                stationBuffer.ToString().Equals("WinSta0", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("SKIP Native: failed to verify a new noninteractive station.");
                return 0;
            }
            string stationName = stationBuffer.ToString();
            if (!SetProcessWindowStation(station))
            {
                Console.WriteLine($"SKIP Native: could not select isolated station ({Marshal.GetLastWin32Error()}).");
                return 0;
            }

            desktop = CreateDesktopW("Default", null, 0, 0, 0x000F01FF, 0);
            if (!SetProcessWindowStation(original))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not restore original window station.");
            }

            if (desktop == 0)
            {
                Console.WriteLine($"SKIP Native: could not create isolated desktop ({Marshal.GetLastWin32Error()}).");
                return 0;
            }

            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("No process executable path.");
            string assembly = Assembly.GetExecutingAssembly().Location;
            string command = $"\"{executable}\" " +
                (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    ? $"\"{assembly}\" " : "") +
                $"--native-child {stationName} \"{report}\"";
            var startup = new StartupInformation { Size = (uint)Marshal.SizeOf<StartupInformation>(), Desktop = stationName + "\\Default" };
            if (!CreateProcessW(executable, new StringBuilder(command), 0, 0, false, 0x08000000,
                    0, Environment.CurrentDirectory, ref startup, out process))
            {
                Console.WriteLine($"SKIP Native: isolated child launch failed ({Marshal.GetLastWin32Error()}).");
                return 0;
            }

            processStarted = true;
            if (WaitForSingleObject(process.Process, 30_000) != 0)
            {
                TerminateProcess(process.Process, 1);
                Console.WriteLine("FAIL Native: isolated child timed out.");
                return 1;
            }

            if (File.Exists(report))
            {
                foreach (string line in File.ReadLines(report)) { Console.WriteLine(line); }
            }

            if (!GetExitCodeProcess(process.Process, out uint code) || code != 0)
            {
                Console.WriteLine($"FAIL Native: isolated child exit code {code}.");
                return 1;
            }

            return 0;
        }
        finally
        {
            SetProcessWindowStation(original);
            if (processStarted) { CloseHandle(process.Thread); CloseHandle(process.Process); }
            if (desktop != 0) { CloseDesktop(desktop); }
            CloseWindowStation(station);
            if (File.Exists(report)) { File.Delete(report); }
        }
    }

    internal static int RunChild(string expectedStation, string report)
    {
        using var writer = new StreamWriter(report) { AutoFlush = true };
        Console.SetOut(writer);
        var name = new StringBuilder(256);
        if (!GetUserObjectInformationW(GetProcessWindowStation(), 2, name, 512, out _) ||
            !string.Equals(name.ToString(), expectedStation, StringComparison.Ordinal) ||
            expectedStation.Equals("WinSta0", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("FAIL Native: refusing clipboard access outside the expected isolated window station.");
            return 1;
        }

        using var window = new ClipboardWindow();
        var gateway = new NativeClipboardGateway(window.Handle);
        var service = new ClipboardService(gateway);
        string fixture = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(report));
        Directory.CreateDirectory(fixture);
        string file = Path.Combine(fixture, "原始文件.txt");
        string second = Path.Combine(fixture, "second.txt");
        string folder = Path.Combine(fixture, "文件夹");
        File.WriteAllText(file, "Original test contents.", Encoding.UTF8);
        File.WriteAllText(second, "Second test contents.", Encoding.UTF8);
        Directory.CreateDirectory(folder);
        byte[] originalBytes = File.ReadAllBytes(file);
        DateTime originalWrite = File.GetLastWriteTimeUtc(file);
        int failures = 0;
        int total = 0;

        void Test(string title, Action body)
        {
            total++;
            try { body(); Console.WriteLine($"PASS Native {title}"); }
            catch (Exception exception) { failures++; Console.WriteLine($"FAIL Native {title}: {exception}"); }
        }

        try
        {
            Test("single file copy and clear", () =>
            {
                SetFiles(window.Handle, [file], 1);
                ClipboardSnapshot snapshot = service.ReadSnapshot();
                Program.Equal(ClipboardState.Copied, snapshot.State);
                Program.Equal(1, snapshot.FileCount);
                Program.Equal(Path.GetFileName(file), snapshot.FileNames[0]);
                Program.Equal(ClearOutcome.Cleared, service.Clear(snapshot.Sequence).Outcome);
                Program.True(!IsClipboardFormatAvailable(15));
            });
            Test("multiple files and folder", () =>
            {
                SetFiles(window.Handle, [file, second, folder], 1);
                ClipboardSnapshot snapshot = service.ReadSnapshot();
                Program.Equal(3, snapshot.FileCount);
                Program.Equal("文件夹", snapshot.FileNames[2]);
                Program.Equal(ClearOutcome.Cleared, service.Clear(snapshot.Sequence).Outcome);
            });
            Test("cut cancellation preserves original file and folder", () =>
            {
                SetFiles(window.Handle, [file, folder], 2);
                ClipboardSnapshot snapshot = service.ReadSnapshot();
                Program.Equal(ClipboardState.Cut, snapshot.State);
                Program.Equal(ClearOutcome.Cleared, service.Clear(snapshot.Sequence).Outcome);
                Program.True(originalBytes.SequenceEqual(File.ReadAllBytes(file)));
                Program.Equal(originalWrite, File.GetLastWriteTimeUtc(file));
                Program.True(Directory.Exists(folder));
            });
            Test("ordinary text is preserved", () =>
            {
                SetFormats(window.Handle, [(13u, Encoding.Unicode.GetBytes("test text\0"))]);
                uint sequence = gateway.Sequence;
                Program.Equal(ClipboardState.None, service.ReadSnapshot().State);
                Program.Equal(ClearOutcome.NoFiles, service.Clear(sequence).Outcome);
                Program.Equal(sequence, gateway.Sequence);
                Program.True(IsClipboardFormatAvailable(13));
            });
            Test("newer files are preserved after stale clear request", () =>
            {
                SetFiles(window.Handle, [file], 1);
                uint observed = service.ReadSnapshot().Sequence;
                SetFiles(window.Handle, [second], 1);
                Program.Equal(ClearOutcome.Changed, service.Clear(observed).Outcome);
                Program.Equal("second.txt", service.ReadSnapshot().FileNames[0]);
            });
            Test("unknown drop effect remains clearable", () =>
            {
                SetFiles(window.Handle, [folder], null);
                ClipboardSnapshot snapshot = service.ReadSnapshot();
                Program.Equal(ClipboardState.Files, snapshot.State);
                Program.Equal(ClearOutcome.Cleared, service.Clear(snapshot.Sequence).Outcome);
            });
            Test("mixed file and text formats clear together", () =>
            {
                SetFormats(window.Handle, [(15u, FileDrop([file])), (13u, Encoding.Unicode.GetBytes("mixed\0"))]);
                ClipboardSnapshot snapshot = service.ReadSnapshot();
                Program.Equal(ClearOutcome.Cleared, service.Clear(snapshot.Sequence).Outcome);
                Program.True(!IsClipboardFormatAvailable(13));
                Program.True(!IsClipboardFormatAvailable(15));
            });
            Test("malformed file data is refused", () =>
            {
                SetFormats(window.Handle, [(15u, new byte[4])]);
                uint sequence = gateway.Sequence;
                Program.Equal(ClipboardState.Unavailable, service.ReadSnapshot().State);
                Program.Equal(ClearOutcome.Failed, service.Clear(sequence).Outcome);
                Program.True(IsClipboardFormatAvailable(15));
            });
            Test("clipboard lock contention recovers", () =>
            {
                SetFiles(window.Handle, [file], 1);
                uint sequence = service.ReadSnapshot().Sequence;
                using var ready = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                Exception? workerError = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        if (!OpenClipboard(0)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                        try { ready.Set(); release.Wait(TimeSpan.FromSeconds(5)); }
                        finally { CloseClipboard(); }
                    }
                    catch (Exception exception) { workerError = exception; ready.Set(); }
                });
                thread.Start();
                try
                {
                    Program.True(ready.Wait(TimeSpan.FromSeconds(5)));
                    if (workerError is not null) { throw workerError; }
                    Program.Equal(ClipboardState.Unavailable, service.ReadSnapshot().State);
                    Program.Equal(ClearOutcome.Busy, service.Clear(sequence).Outcome);
                }
                finally { release.Set(); thread.Join(); }
                Program.Equal(ClearOutcome.Cleared, service.Clear(sequence).Outcome);
            });
            Test("clipboard listener receives native update", () =>
            {
                Application.DoEvents();
                int before = window.UpdateCount;
                SetFiles(window.Handle, [file], 1);
                for (int attempt = 0; attempt < 20 && window.UpdateCount == before; attempt++)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                Program.True(window.UpdateCount > before);
                Program.Equal(ClearOutcome.Cleared, service.Clear(service.ReadSnapshot().Sequence).Outcome);
            });
            Test("normal clipboard operations continue after service disposal", () =>
            {
                SetFiles(window.Handle, [second], 1);
                Program.Equal(1, service.ReadSnapshot().FileCount);
                SetFormats(window.Handle, [(13u, Encoding.Unicode.GetBytes("normal copy\0"))]);
                Program.True(IsClipboardFormatAvailable(13));
            });
            AppNativeTests.Run(Test, fixture);
        }
        finally
        {
            // Every fixture path was created by this child. No recursive filesystem operation.
            if (File.Exists(file)) { File.Delete(file); }
            if (File.Exists(second)) { File.Delete(second); }
            if (Directory.Exists(folder)) { Directory.Delete(folder); }
            if (Directory.Exists(fixture)) { Directory.Delete(fixture); }
        }

        Console.WriteLine($"Native: {total - failures}/{total} passed in isolated window station.");
        return failures == 0 ? 0 : 1;
    }

    private static void SetFiles(nint window, string[] paths, uint? effect)
    {
        var formats = new List<(uint Format, byte[] Data)> { (15, FileDrop(paths)) };
        if (effect.HasValue)
        {
            uint effectFormat = RegisterClipboardFormatW("Preferred DropEffect");
            Program.True(effectFormat != 0);
            formats.Add((effectFormat, BitConverter.GetBytes(effect.Value)));
        }

        SetFormats(window, formats);
    }

    private static byte[] FileDrop(string[] paths)
    {
        byte[] names = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
        var bytes = new byte[20 + names.Length];
        BitConverter.GetBytes(20u).CopyTo(bytes, 0);
        BitConverter.GetBytes(1).CopyTo(bytes, 16);
        names.CopyTo(bytes, 20);
        return bytes;
    }

    private static void SetFormats(nint window, IEnumerable<(uint Format, byte[] Data)> formats)
    {
        if (!OpenClipboard(window)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try
        {
            if (!EmptyClipboard()) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
            foreach ((uint format, byte[] bytes) in formats)
            {
                nint memory = GlobalAlloc(0x42, (nuint)bytes.Length);
                if (memory == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                bool transferred = false;
                try
                {
                    nint data = GlobalLock(memory);
                    if (data == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                    try { Marshal.Copy(bytes, 0, data, bytes.Length); }
                    finally { GlobalUnlock(memory); }
                    if (SetClipboardData(format, memory) == 0) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
                    transferred = true;
                }
                finally { if (!transferred) { GlobalFree(memory); } }
            }
        }
        finally { CloseClipboard(); }
    }

    private sealed class ClipboardWindow : NativeWindow, IDisposable
    {
        internal ClipboardWindow()
        {
            CreateHandle(new CreateParams { Caption = "ClipZero isolated integration tests" });
            if (!AddClipboardFormatListener(Handle)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        }
        internal int UpdateCount { get; private set; }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x031D) { UpdateCount++; }
            base.WndProc(ref message);
        }
        public void Dispose() { RemoveClipboardFormatListener(Handle); DestroyHandle(); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInformation
    {
        public uint Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2Count;
        public nint Reserved2, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowStationW(string? name, uint flags, uint access, nint attributes);
    [DllImport("user32.dll")] private static extern nint GetProcessWindowStation();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint threadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWindowStation(nint station);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDesktopW(string name, string? device, nint mode, uint flags, uint access, nint attributes);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseWindowStation(nint station);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder information, uint length, out uint needed);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment,
        string directory, ref StartupInformation startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(nint process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string format);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AddClipboardFormatListener(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveClipboardFormatListener(nint window);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint handle);
}
