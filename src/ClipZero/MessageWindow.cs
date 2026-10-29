using ClipZero.Interop;

namespace ClipZero;

internal sealed class MessageWindow : NativeWindow, IDisposable
{
    internal const uint ShowWindowMessage = 0x8001;
    private const int HotKeyId = 1;
    private bool _disposed;

    internal event Action? ClipboardUpdated;
    internal event Action? ClearRequested;
    internal event Action? ShowRequested;
    internal bool ListenerRegistered { get; }
    internal bool HotKeyRegistered { get; }

    internal MessageWindow()
    {
        // A hidden top-level window allows FindWindow for second-launch activation.
        // It is never shown and has no taskbar button.
        CreateHandle(new CreateParams
        {
            Caption = Program.WindowTitle,
            ExStyle = 0x80 // WS_EX_TOOLWINDOW
        });
        ListenerRegistered = NativeMethods.AddClipboardFormatListener(Handle);
        HotKeyRegistered = NativeMethods.RegisterHotKey(Handle, HotKeyId, 0x4000 | 0x0001 | 0x0002, 0x08);
    }

    protected override void WndProc(ref Message message)
    {
        switch ((uint)message.Msg)
        {
            case 0x031D: ClipboardUpdated?.Invoke(); break; // WM_CLIPBOARDUPDATE
            case 0x0312 when message.WParam == HotKeyId: ClearRequested?.Invoke(); break;
            case ShowWindowMessage: ShowRequested?.Invoke(); break;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (HotKeyRegistered) NativeMethods.UnregisterHotKey(Handle, HotKeyId);
        if (ListenerRegistered) NativeMethods.RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }
}
