using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using ClipZero.Core;

namespace ClipZero.Interop;

/// <summary>Win32 clipboard access; does not perform filesystem operations or read other formats.</summary>
public sealed class NativeClipboardGateway : IClipboardGateway
{
    private const uint FileDropFormat = 15; // CF_HDROP
    private static readonly uint DropEffectFormat = RegisterClipboardFormatW("Preferred DropEffect");
    private readonly nint _ownerWindow;

    public NativeClipboardGateway(nint ownerWindow)
    {
        if (ownerWindow == 0)
        {
            throw new ArgumentException("A valid window handle is required for EmptyClipboard.", nameof(ownerWindow));
        }

        _ownerWindow = ownerWindow;
    }

    public uint Sequence => GetClipboardSequenceNumber();

    // Used by the test assembly to exercise Shell32 parsing on private HGLOBAL allocations,
    // without changing the user's clipboard when isolated window stations are unavailable.
    internal static ClipboardFiles ParseFileDrop(nint handle) => NativeSession.ParseFileDrop(handle);

    public bool TryOpen(out IClipboardSession? session)
    {
        if (!OpenClipboard(_ownerWindow))
        {
            session = null;
            return false;
        }

        session = new NativeSession();
        return true;
    }

    private sealed class NativeSession : IClipboardSession
    {
        private const int MaximumPathLength = 32767;
        private bool _disposed;

        public uint Sequence
        {
            get { ThrowIfDisposed(); return GetClipboardSequenceNumber(); }
        }

        public bool ContainsFileDrop
        {
            get { ThrowIfDisposed(); return IsClipboardFormatAvailable(FileDropFormat); }
        }

        public ClipboardFiles ReadFiles()
        {
            ThrowIfDisposed();
            nint handle = GetClipboardData(FileDropFormat);
            if (handle == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "File data could not be read.");
            }

            return ParseFileDrop(handle);
        }

        internal static ClipboardFiles ParseFileDrop(nint handle)
        {
            ValidateFileDrop(handle);
            uint count = DragQueryFileW(handle, uint.MaxValue, null, 0);
            if (count == 0 || count > int.MaxValue)
            {
                throw new InvalidDataException("The file drop list is empty or invalid.");
            }

            int previewCount = Math.Min((int)count, ClipboardFiles.MaximumPreviewCount);
            var names = new string[previewCount];
            for (uint index = 0; index < previewCount; index++)
            {
                uint length = DragQueryFileW(handle, index, null, 0);
                if (length == 0 || length > MaximumPathLength)
                {
                    throw new InvalidDataException("The file drop list contains an invalid path.");
                }

                var path = new StringBuilder(checked((int)length + 1));
                if (DragQueryFileW(handle, index, path, (uint)path.Capacity) != length)
                {
                    throw new InvalidDataException("The file drop path could not be read completely.");
                }

                string displayPath = path.ToString().TrimEnd('\\', '/');
                int separator = Math.Max(displayPath.LastIndexOf('\\'), displayPath.LastIndexOf('/'));
                names[index] = separator >= 0 ? displayPath[(separator + 1)..] : displayPath;
                if (names[index].Length == 0)
                {
                    names[index] = path.ToString();
                }
            }

            return new ClipboardFiles((int)count, names);
        }

        public uint? ReadDropEffect()
        {
            ThrowIfDisposed();
            if (DropEffectFormat == 0 || !IsClipboardFormatAvailable(DropEffectFormat))
            {
                return null;
            }

            nint handle = GetClipboardData(DropEffectFormat);
            if (handle == 0 || GlobalSize(handle) < 4)
            {
                return null;
            }

            nint data = GlobalLock(handle);
            if (data == 0)
            {
                return null;
            }

            try
            {
                return unchecked((uint)Marshal.ReadInt32(data));
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }

        public bool Empty()
        {
            ThrowIfDisposed();
            return EmptyClipboard();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                CloseClipboard();
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        private static void ValidateFileDrop(nint handle)
        {
            // Reject malformed global memory before asking Shell32 to parse it. DROPFILES is
            // 20 bytes, followed by a double-null-terminated ANSI or UTF-16 path list.
            nuint size = GlobalSize(handle);
            if (size < 22 || size > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("File drop memory is too small.");
            }

            nint data = GlobalLock(handle);
            if (data == 0)
            {
                throw new InvalidDataException("File drop memory could not be locked.");
            }

            try
            {
                uint offset = unchecked((uint)Marshal.ReadInt32(data));
                bool wide = Marshal.ReadInt32(data, 16) != 0;
                int terminatorBytes = wide ? 4 : 2;
                if (offset < 20 || (nuint)offset > size - (nuint)terminatorBytes ||
                    (wide && offset % 2 != 0))
                {
                    throw new InvalidDataException("File drop memory has an invalid path offset.");
                }

                // Check every path, including entries beyond the display preview, in one pass.
                // Do not retain paths or trust allocator padding beyond the actual path list.
                var buffer = new byte[4096];
                bool previousWasNull = false;
                int stride = wide ? 2 : 1;
                int pathStart = (int)offset;
                int pathLength = 0;
                for (int position = (int)offset; (nuint)position < size;)
                {
                    int bytes = (int)Math.Min((nuint)buffer.Length, size - (nuint)position);
                    bytes -= bytes % stride;
                    if (bytes == 0)
                    {
                        break;
                    }

                    Marshal.Copy(data + position, buffer, 0, bytes);
                    for (int index = 0; index < bytes; index += stride)
                    {
                        bool isNull = buffer[index] == 0 && (!wide || buffer[index + 1] == 0);
                        if (isNull && previousWasNull)
                        {
                            return;
                        }

                        if (isNull)
                        {
                            // ANSI byte count is an upper bound on the resulting UTF-16 length.
                            // Only long ANSI entries need a conversion-length query; no string is
                            // allocated, and valid multibyte paths are not rejected for byte length.
                            if (!wide && pathLength > MaximumPathLength)
                            {
                                int unicodeLength = MultiByteToWideChar(0, 0, data + pathStart, pathLength, 0, 0);
                                if (unicodeLength == 0 || unicodeLength > MaximumPathLength)
                                {
                                    throw new InvalidDataException("The file drop list contains an invalid path.");
                                }
                            }

                            pathLength = 0;
                            pathStart = position + index + stride;
                        }
                        else
                        {
                            pathLength++;
                            if (wide && pathLength > MaximumPathLength)
                            {
                                throw new InvalidDataException("The file drop list contains an invalid path.");
                            }
                        }

                        previousWasNull = isNull;
                    }

                    position += bytes;
                }

                throw new InvalidDataException("File drop memory is not terminated.");
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint ownerWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string format);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFileW(nint fileDrop, uint fileIndex, StringBuilder? fileName, uint capacity);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint GlobalSize(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int MultiByteToWideChar(uint codePage, uint flags, nint source, int sourceLength,
        nint destination, int destinationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);
}
