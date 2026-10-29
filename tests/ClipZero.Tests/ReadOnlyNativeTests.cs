using System.Runtime.InteropServices;
using System.Text;
using ClipZero.Core;
using ClipZero.Interop;

namespace ClipZero.Tests;

/// <summary>Tests private global memory and application windows without clipboard mutations.</summary>
internal static class ReadOnlyNativeTests
{
    internal static int Run()
    {
        int total = 0;
        int failures = 0;
        void Test(string title, Action body)
        {
            total++;
            try { body(); Console.WriteLine($"PASS Safe native/UI {title}"); }
            catch (Exception exception) { failures++; Console.WriteLine($"FAIL Safe native/UI {title}: {exception}"); }
        }

        Test("Unicode copied file and folder names parse via DragQueryFileW", () =>
        {
            ClipboardFiles files = Parse(FileDrop([@"C:\目录\文件.txt", @"C:\目录\文件夹"], true));
            Program.Equal(2, files.Count);
            Program.Equal("文件.txt", files.FileNames[0]);
            Program.Equal("文件夹", files.FileNames[1]);
        });
        Test("ANSI file drop remains compatible", () =>
        {
            ClipboardFiles files = Parse(FileDrop([@"C:\test\ansi.txt"], false));
            Program.Equal(1, files.Count);
            Program.Equal("ansi.txt", files.FileNames[0]);
        });
        Test("all files are counted but only bounded names are retained", () =>
        {
            ClipboardFiles files = Parse(FileDrop(Enumerable.Range(0, 100).Select(i => $@"C:\test\{i}.txt").ToArray(), true));
            Program.Equal(100, files.Count);
            Program.Equal(8, files.FileNames.Count);
        });
        Test("invalid header offset is refused", () =>
        {
            byte[] bytes = FileDrop([@"C:\test.txt"], true);
            BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 0);
            MustReject(bytes);
        });
        Test("truncated memory is refused", () => MustReject(new byte[4]));
        Test("missing double terminator is refused", () =>
        {
            byte[] bytes = FileDrop([@"C:\test.txt"], true);
            Array.Fill(bytes, (byte)0x41, bytes.Length - 4, 4);
            MustReject(bytes);
        });
        Test("allocation padding after a valid terminator is allowed", () =>
        {
            byte[] bytes = FileDrop([@"C:\test.txt"], true);
            int size = bytes.Length;
            Array.Resize(ref bytes, size + 8);
            Array.Fill(bytes, (byte)0x41, size, 8);
            Program.Equal(1, Parse(bytes).Count);
        });
        Test("empty file drop is refused", () => MustReject(FileDrop([], true)));
        AppNativeTests.Run(Test, Environment.CurrentDirectory, manipulateClipboard: false);
        Console.WriteLine($"Safe native/UI: {total - failures}/{total} passed; user clipboard untouched.");
        return failures;
    }

    private static void MustReject(byte[] bytes)
    {
        try { Parse(bytes); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected malformed file memory to be rejected.");
    }

    private static ClipboardFiles Parse(byte[] bytes)
    {
        nint memory = GlobalAlloc(0x42, (nuint)bytes.Length);
        if (memory == 0) { throw new InvalidOperationException("Could not allocate test memory."); }
        try
        {
            nint data = GlobalLock(memory);
            if (data == 0) { throw new InvalidOperationException("Could not lock test memory."); }
            try { Marshal.Copy(bytes, 0, data, bytes.Length); }
            finally { GlobalUnlock(memory); }
            return NativeClipboardGateway.ParseFileDrop(memory);
        }
        finally { GlobalFree(memory); }
    }

    private static byte[] FileDrop(string[] paths, bool wide)
    {
        string list = string.Join('\0', paths) + "\0\0";
        byte[] names = (wide ? Encoding.Unicode : Encoding.ASCII).GetBytes(list);
        var bytes = new byte[20 + names.Length];
        BitConverter.GetBytes(20u).CopyTo(bytes, 0);
        BitConverter.GetBytes(wide ? 1 : 0).CopyTo(bytes, 16);
        names.CopyTo(bytes, 20);
        return bytes;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint handle);
}
