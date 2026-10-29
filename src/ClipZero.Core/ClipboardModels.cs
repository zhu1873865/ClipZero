using System.Collections.ObjectModel;

namespace ClipZero.Core;

public enum ClipboardState
{
    None,
    Copied,
    Cut,
    Files,
    Unavailable
}

public enum ClearOutcome
{
    Cleared,
    NoFiles,
    Changed,
    Busy,
    Failed
}

/// <summary>Only the current bounded display-name preview is kept; full paths are not retained.</summary>
public sealed class ClipboardFiles
{
    public const int MaximumPreviewCount = 8;
    public const int MaximumDisplayNameLength = 512;

    public ClipboardFiles(int count, IEnumerable<string> fileNames)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        ArgumentNullException.ThrowIfNull(fileNames);
        Count = count;
        FileNames = Array.AsReadOnly(fileNames.Take(Math.Min(count, MaximumPreviewCount))
            .Select(name => name.Length > MaximumDisplayNameLength
                ? name[..MaximumDisplayNameLength] + "…"
                : name)
            .ToArray());

        if (FileNames.Count == 0 || FileNames.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("File data must contain at least one valid display name.", nameof(fileNames));
        }
    }

    public int Count { get; }
    public ReadOnlyCollection<string> FileNames { get; }
}

public sealed record ClipboardSnapshot(
    ClipboardState State,
    uint Sequence,
    int FileCount,
    IReadOnlyList<string> FileNames,
    string? Error = null)
{
    public bool HasFiles => State is ClipboardState.Copied or ClipboardState.Cut or ClipboardState.Files;

    public static ClipboardSnapshot Empty(uint sequence) => new(ClipboardState.None, sequence, 0, Array.Empty<string>());

    public static ClipboardSnapshot Unavailable(uint sequence, string error) =>
        new(ClipboardState.Unavailable, sequence, 0, Array.Empty<string>(), error);
}

public sealed record ClearResult(ClearOutcome Outcome, ClipboardSnapshot Snapshot, string? Error = null);
