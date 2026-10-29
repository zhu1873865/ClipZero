namespace ClipZero.Core;

/// <summary>All checks and destructive clipboard operations occur within one exclusive native session.</summary>
public sealed class ClipboardService(IClipboardGateway gateway)
{
    private readonly IClipboardGateway _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));

    public ClipboardSnapshot ReadSnapshot()
    {
        try
        {
            if (!_gateway.TryOpen(out IClipboardSession? session) || session is null)
            {
                return ClipboardSnapshot.Unavailable(_gateway.Sequence, "剪贴板暂时被其他程序占用。");
            }

            using (session)
            {
                return ReadLocked(session);
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return ClipboardSnapshot.Unavailable(_gateway.Sequence, "暂时无法读取剪贴板。");
        }
    }

    /// <summary>
    /// Clears only the exact file clipboard state the caller observed. A new copy, text content,
    /// delayed-rendering sequence change, invalid HDROP, or unavailable lock always prevents clearing.
    /// </summary>
    public ClearResult Clear(uint expectedSequence)
    {
        try
        {
            if (!_gateway.TryOpen(out IClipboardSession? session) || session is null)
            {
                ClipboardSnapshot busy = ClipboardSnapshot.Unavailable(_gateway.Sequence, "剪贴板暂时被其他程序占用，请稍后重试。");
                return new(ClearOutcome.Busy, busy, busy.Error);
            }

            using (session)
            {
                if (session.Sequence != expectedSequence)
                {
                    return new(ClearOutcome.Changed, ReadLocked(session), "剪贴板已变化，请查看当前状态后再试。");
                }

                if (!session.ContainsFileDrop)
                {
                    return new(ClearOutcome.NoFiles, ClipboardSnapshot.Empty(session.Sequence));
                }

                ClipboardSnapshot current = ReadLocked(session);

                // GetClipboardData may trigger delayed rendering. Recheck after every read and
                // immediately before EmptyClipboard while still holding the OpenClipboard lock.
                if (session.Sequence != expectedSequence)
                {
                    return new(ClearOutcome.Changed, current, "读取期间剪贴板已变化，本次没有清空。");
                }

                if (!current.HasFiles)
                {
                    return new(ClearOutcome.Failed, current, current.Error ?? "文件复制状态无法安全确认，本次没有清空。");
                }

                if (!session.Empty())
                {
                    return new(ClearOutcome.Failed, current, "清空失败，请稍后重试。");
                }

                return new(ClearOutcome.Cleared, ClipboardSnapshot.Empty(session.Sequence));
            }
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            ClipboardSnapshot unavailable = ClipboardSnapshot.Unavailable(_gateway.Sequence, "暂时无法操作剪贴板，请稍后重试。");
            return new(ClearOutcome.Failed, unavailable, unavailable.Error);
        }
    }

    private static ClipboardSnapshot ReadLocked(IClipboardSession session)
    {
        uint sequence = session.Sequence;
        if (!session.ContainsFileDrop)
        {
            return ClipboardSnapshot.Empty(sequence);
        }

        ClipboardFiles files = session.ReadFiles();
        uint? effect = session.ReadDropEffect();
        uint afterRead = session.Sequence;
        if (sequence != afterRead)
        {
            return ClipboardSnapshot.Unavailable(afterRead, "剪贴板读取期间发生变化，请稍后重试。");
        }

        ClipboardState state = (effect.GetValueOrDefault() & 7) switch
        {
            1 => ClipboardState.Copied,
            2 => ClipboardState.Cut,
            _ => ClipboardState.Files
        };
        return new(state, sequence, files.Count, files.FileNames);
    }

    private static bool IsRecoverable(Exception exception) => exception is not
        (OutOfMemoryException or StackOverflowException or AccessViolationException);
}
