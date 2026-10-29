using ClipZero.Core;

namespace ClipZero.UI;

internal static class StatusText
{
    internal static bool HasFiles(ClipboardSnapshot snapshot) =>
        snapshot.State is ClipboardState.Copied or ClipboardState.Cut or ClipboardState.Files;

    internal static string Summary(ClipboardSnapshot snapshot) => snapshot.State switch
    {
        ClipboardState.Copied => $"已复制 {snapshot.FileCount} 个文件／文件夹",
        ClipboardState.Cut => $"已剪切 {snapshot.FileCount} 个文件／文件夹",
        ClipboardState.Files => $"待粘贴 {snapshot.FileCount} 个文件／文件夹",
        ClipboardState.Unavailable => "状态暂时无法读取",
        _ => "当前无待粘贴文件"
    };
}
