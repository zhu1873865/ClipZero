using System.Globalization;
using System.Text.Json;

namespace ClipZero.Core;

public enum ExplorerUndoOutcome { Succeeded, Changed, NoBackup, Failed }

/// <summary>Describes the stored setting only; it cannot verify Explorer's undo behavior.</summary>
public sealed record ExplorerUndoSnapshot(
    bool HasDisableSetting, bool CanDisable, bool CanRestore, string Status, string? Error = null);

public sealed record ExplorerUndoResult(
    ExplorerUndoOutcome Outcome, ExplorerUndoSnapshot Snapshot, string Message, bool RequiresSignIn = false)
{
    public bool Succeeded => Outcome == ExplorerUndoOutcome.Succeeded;
}

/// <summary>A platform-independent, exact representation of a registry value.</summary>
public sealed class ExplorerUndoValue
{
    private ExplorerUndoValue(bool exists, string? kind, JsonElement data)
    {
        Exists = exists;
        Kind = kind;
        Data = data;
    }

    public bool Exists { get; }
    public string? Kind { get; }
    public JsonElement Data { get; }
    // A matching registry value is not proof that this Windows build applies it.
    public bool HasDisableSetting => Exists && Kind == "DWord" && Data.GetString() == "0";
    public static ExplorerUndoValue Absent { get; } = new(false, null, JsonSerializer.SerializeToElement<object?>(null));
    public static ExplorerUndoValue Disabled { get; } = Create("DWord", "0");

    public static ExplorerUndoValue Create(string kind, object data) =>
        Parse(JsonSerializer.SerializeToElement(new { Exists = true, Kind = kind, Data = data }));

    public bool SameAs(ExplorerUndoValue other) => Exists == other.Exists && Kind == other.Kind &&
        Data.GetRawText() == other.Data.GetRawText();

    internal static ExplorerUndoValue Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("Exists", out JsonElement exists) ||
            exists.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !value.TryGetProperty("Kind", out JsonElement kind) ||
            !value.TryGetProperty("Data", out JsonElement data))
        {
            throw new InvalidDataException("备份的原设置格式不正确。");
        }
        if (!exists.GetBoolean())
        {
            if (kind.ValueKind != JsonValueKind.Null || data.ValueKind != JsonValueKind.Null)
                throw new InvalidDataException("不存在的原设置包含意外数据。");
            return Absent;
        }
        if (kind.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("备份缺少注册表数据类型。");
        string type = kind.GetString()!;
        object normalized;
        if (type == "MultiString")
        {
            if (data.ValueKind != JsonValueKind.Array ||
                data.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.String))
                throw new InvalidDataException("备份中的字符串数组不正确。");
            normalized = data.EnumerateArray().Select(entry => entry.GetString()!).ToArray();
        }
        else
        {
            if (data.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("备份中的原设置必须使用字符串保存。");
            string text = data.GetString()!;
            normalized = type switch
            {
                "String" or "ExpandString" => text,
                "DWord" => int.Parse(text, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                "QWord" => long.Parse(text, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                "Binary" or "None" => Convert.ToBase64String(Convert.FromBase64String(text)),
                _ => throw new InvalidDataException("备份包含不支持的注册表数据类型。")
            };
        }
        return new ExplorerUndoValue(true, type, JsonSerializer.SerializeToElement(normalized));
    }
}
