using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace ClipZero.Tests;

/// <summary>
/// Launches the actual development EXE or portable bundle on a private desktop. The application
/// naturally reads current file-copy metadata and undo status, but no clear operation,
/// settings change, or keyboard input is sent.
/// Every enumerated and messaged window is checked against a process created by this test.
/// </summary>
internal static class PortableSmokeTests
{
    internal static int Run(bool development = false)
    {
        string mode = development ? "Development" : "Portable";
        string executable = Path.GetFullPath(development
            ? Path.Combine(Environment.CurrentDirectory, "src", "ClipZero", "bin", "Release", "net10.0-windows", "ClipZero.exe")
            : Path.Combine(Environment.CurrentDirectory, "artifacts", "publish", "win-x64", "self-contained", "ClipZero.exe"));
        if (!File.Exists(executable))
        {
            Console.WriteLine(development
                ? "SKIP Development: build ClipZero.slnx in Release first."
                : "SKIP Portable: build the win-x64 self-contained bundle first.");
            return 0;
        }

        using var mutex = new Mutex(false, ClipZero.Program.MutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            Console.WriteLine($"SKIP {mode}: an existing ClipZero instance owns the session mutex.");
            return 0;
        }
        mutex.ReleaseMutex();

        nint desktop = 0;
        ProcessInformation first = default;
        ProcessInformation second = default;
        bool firstStarted = false;
        bool secondStarted = false;
        try
        {
            string desktopName = $"ClipZero{mode}Tests_" + Guid.NewGuid().ToString("N");
            desktop = CreateDesktopW(desktopName, null, 0, 0, 0x000F01FF, 0);
            if (desktop == 0) { throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create private smoke-test desktop."); }
            var stationName = new StringBuilder(256);
            if (!GetUserObjectInformationW(GetProcessWindowStation(), 2, stationName, 512, out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            string desktopPath = stationName + "\\" + desktopName;
            first = Launch(executable, desktopPath, showWindow: development);
            firstStarted = true;
            WaitUntil(() => FindWindow(desktop, first.ProcessId, ClipZero.Program.WindowTitle) != 0,
                20_000, $"The {mode} EXE did not create its message window.", first.Process);
            WaitUntil(() => FindWindow(desktop, first.ProcessId, "ClipZero") != 0,
                5_000, $"The {mode} EXE did not create its main window.", first.Process);
            nint main = FindWindow(desktop, first.ProcessId, "ClipZero");
            if (development)
            {
                WaitUntil(() => IsWindowVisible(main), 5_000,
                    "The development --show launch did not reveal its main window.", first.Process);
                Console.WriteLine("PASS Development Release EXE starts with --show on a private desktop using the local .NET runtime.");
                ShowWindow(main, 0); // Hide only the verified test-owned window before the second-launch check.
                Program.True(!IsWindowVisible(main));
            }
            else
            {
                Program.True(!IsWindowVisible(main));
                Console.WriteLine("PASS Portable compressed self-contained EXE starts in tray mode on a private desktop.");
            }

            second = Launch(executable, desktopPath);
            secondStarted = true;
            if (WaitForSingleObject(second.Process, 5_000) != 0)
            {
                throw new TimeoutException($"The second {mode} launch did not exit.");
            }
            Program.Equal(0u, ExitCode(second.Process));
            Program.Equal(259u, ExitCode(first.Process));
            WaitUntil(() => IsWindowVisible(main), 5_000,
                "The second launch did not reveal the original main window.", first.Process);
            Program.Equal(1, CountWindows(desktop, first.ProcessId, ClipZero.Program.WindowTitle));
            Console.WriteLine($"PASS {mode} second launch exits and reveals the existing window without another instance.");

            if (!PostThreadMessageW(first.ThreadId, 0x0012, 0, 0)) // WM_QUIT only; never WM_HOTKEY or clear.
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not request graceful test-process exit.");
            }
            if (WaitForSingleObject(first.Process, 5_000) != 0)
            {
                throw new TimeoutException($"The {mode} process did not exit after WM_QUIT.");
            }
            Program.Equal(0u, ExitCode(first.Process));
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException)
            {
                throw new InvalidOperationException($"The {mode} process abandoned its single-instance mutex.");
            }
            Program.True(acquired);
            mutex.ReleaseMutex();
            Console.WriteLine($"PASS {mode} application exits normally and releases its mutex.");
            Console.WriteLine($"{mode}: 3/3 passed; no clipboard clear, settings change, or keyboard input sent.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAIL {mode}: {exception}");
            return 1;
        }
        finally
        {
            if (secondStarted) { CloseOwnedProcess(second); }
            if (firstStarted) { CloseOwnedProcess(first); }
            if (desktop != 0) { CloseDesktop(desktop); }
        }
    }

    private static ProcessInformation Launch(string executable, string desktop, bool showWindow = false)
    {
        var startup = new StartupInformation { Size = (uint)Marshal.SizeOf<StartupInformation>(), Desktop = desktop };
        if (!CreateProcessW(executable, new StringBuilder($"\"{executable}\"" + (showWindow ? " --show" : "")), 0, 0, false, 0,
            0, Path.GetDirectoryName(executable)!, ref startup, out ProcessInformation process))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not launch the test executable.");
        }
        return process;
    }

    private static void WaitUntil(Func<bool> condition, int milliseconds, string error, nint process)
    {
        long deadline = Environment.TickCount64 + milliseconds;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            if (ExitCode(process) != 259) { throw new InvalidOperationException(error + " Process exited early."); }
            Thread.Sleep(25);
        }
        if (!condition()) { throw new TimeoutException(error); }
    }

    private static nint FindWindow(nint desktop, uint processId, string title)
    {
        nint found = 0;
        EnumDesktopWindows(desktop, (window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != processId) { return true; }
            var text = new StringBuilder(256);
            GetWindowTextW(window, text, text.Capacity);
            if (string.Equals(text.ToString(), title, StringComparison.Ordinal))
            {
                found = window;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private static int CountWindows(nint desktop, uint processId, string title)
    {
        int count = 0;
        EnumDesktopWindows(desktop, (window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == processId)
            {
                var text = new StringBuilder(256);
                GetWindowTextW(window, text, text.Capacity);
                if (string.Equals(text.ToString(), title, StringComparison.Ordinal)) { count++; }
            }
            return true;
        }, 0);
        return count;
    }

    private static uint ExitCode(nint process)
    {
        if (!GetExitCodeProcess(process, out uint code)) { throw new Win32Exception(Marshal.GetLastWin32Error()); }
        return code;
    }

    private static void CloseOwnedProcess(ProcessInformation process)
    {
        if (WaitForSingleObject(process.Process, 0) == 0x102)
        {
            PostThreadMessageW(process.ThreadId, 0x0012, 0, 0);
            if (WaitForSingleObject(process.Process, 1000) == 0x102)
            {
                TerminateProcess(process.Process, 1);
                WaitForSingleObject(process.Process, 1000);
            }
        }
        CloseHandle(process.Thread);
        CloseHandle(process.Process);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInformation
    {
        public uint Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2Count;
        public nint Reserved2, StdInput, StdOutput, StdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }
    private delegate bool EnumWindowCallback(nint window, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDesktopW(string name, string? device, nint mode, uint flags, uint access, nint attributes);
    [DllImport("user32.dll")] private static extern nint GetProcessWindowStation();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder information, uint length, out uint needed);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, nint environment,
        string directory, ref StartupInformation startup, out ProcessInformation process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(nint process, out uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(nint process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDesktopWindows(nint desktop, EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostThreadMessageW(uint threadId, uint message, nint wParam, nint lParam);
}
