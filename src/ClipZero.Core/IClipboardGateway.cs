namespace ClipZero.Core;

/// <summary>Opening grants exclusive clipboard access until the returned session is disposed.</summary>
public interface IClipboardGateway
{
    uint Sequence { get; }
    bool TryOpen(out IClipboardSession? session);
}

public interface IClipboardSession : IDisposable
{
    uint Sequence { get; }
    bool ContainsFileDrop { get; }
    ClipboardFiles ReadFiles();
    uint? ReadDropEffect();
    bool Empty();
}
