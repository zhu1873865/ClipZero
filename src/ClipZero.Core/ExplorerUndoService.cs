using System.Text.Json;

namespace ClipZero.Core;

/// <summary>Opt-in setting changes with exact backup, recheck, verification, and restoration.</summary>
public sealed class ExplorerUndoService(IExplorerUndoStore store)
{
    public const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string ValueName = "MaxUndoItems";

    public ExplorerUndoSnapshot GetStatus()
    {
        try
        {
            using IExplorerUndoSession session = store.OpenSession();
            ExplorerUndoValue value = session.ReadValue();
            ExplorerUndoValue? original = session.BackupExists ? ReadOriginal(session.ReadBackup()) : null;
            return Snapshot(value, original);
        }
        catch (Exception exception)
        {
            return new(false, false, false, "暂时无法读取文件撤销设置", exception.Message);
        }
    }

    public ExplorerUndoResult Disable() => Execute(disable: true);
    public ExplorerUndoResult Restore() => Execute(disable: false);

    private ExplorerUndoResult Execute(bool disable)
    {
        try
        {
            using IExplorerUndoSession session = store.OpenSession();
            ExplorerUndoValue before = session.ReadValue();
            ExplorerUndoValue? original = session.BackupExists ? ReadOriginal(session.ReadBackup()) : null;
            if (original is not null)
            {
                // A failed write can leave the original value in place. A failed backup
                // deletion can leave it already restored. Neither case loses the backup
                // or requires us to overwrite a setting that another operation changed.
                if (!before.HasDisableSetting && !before.SameAs(original)) return Changed(before, original);
                if (disable && before.HasDisableSetting)
                    return new(ExplorerUndoOutcome.Succeeded, Snapshot(before, original), "实验性关闭设置已经保存，效果未确认；原设置备份保持不变。");
                if (!disable && before.SameAs(original))
                {
                    ExplorerUndoValue beforeCleanup = session.ReadValue();
                    if (!beforeCleanup.SameAs(before)) return Changed(beforeCleanup, original);
                    session.DeleteBackup();
                    return new(ExplorerUndoOutcome.Succeeded, Snapshot(beforeCleanup, null),
                        "原设置已保留，原备份已清理，没有再次改写设置。");
                }
            }
            else if (!disable)
            {
                return new(ExplorerUndoOutcome.NoBackup, Snapshot(before, null), "没有找到原设置备份，无法恢复。没有修改任何设置。");
            }
            else if (before.HasDisableSetting)
            {
                return new(ExplorerUndoOutcome.Succeeded, Snapshot(before, null), "已存在实验性关闭设置，效果未确认。没有原设置备份，本次没有修改设置。");
            }

            ExplorerUndoValue desired;
            if (disable)
            {
                if (original is null)
                {
                    original = before;
                    session.CreateBackup(CreateBackup(original));
                }
                desired = ExplorerUndoValue.Disabled;
            }
            else
            {
                desired = original!;
            }

            // Keep the backup if another program changes the setting while it is being saved.
            ExplorerUndoValue current = session.ReadValue();
            if (!current.SameAs(before)) return Changed(current, original);
            session.WriteValue(desired);
            ExplorerUndoValue after = session.ReadValue();
            if (!after.SameAs(desired))
                return new(ExplorerUndoOutcome.Failed, Snapshot(after, original), "无法确认设置已经保存。原设置备份已保留。");
            if (!disable)
            {
                session.DeleteBackup();
                original = null;
            }
            return new(ExplorerUndoOutcome.Succeeded, Snapshot(after, original), disable
                ? "实验性关闭设置已保存，原设置已备份。这不代表撤销菜单已隐藏或禁用。"
                : "已恢复原来的设置。", RequiresSignIn: true);
        }
        catch (Exception exception)
        {
            return new(ExplorerUndoOutcome.Failed, GetStatus(), $"操作未完成，原有备份不会被覆盖。{exception.Message}");
        }
    }

    private static ExplorerUndoResult Changed(ExplorerUndoValue value, ExplorerUndoValue? original) =>
        new(ExplorerUndoOutcome.Changed, Snapshot(value, original), "设置已被其他操作改变。本次没有覆盖设置，备份已保留。");

    private static ExplorerUndoSnapshot Snapshot(ExplorerUndoValue value, ExplorerUndoValue? original)
    {
        bool originalStillPresent = original is not null && value.SameAs(original);
        bool conflict = original is not null && !value.HasDisableSetting && !originalStillPresent;
        string status = conflict ? "设置已改变，原备份已保留" : originalStillPresent && !value.HasDisableSetting
            ? "上次操作未完成，可以重试或恢复" : value.HasDisableSetting
            ? "关闭设置已保存，效果未确认" : "未保存关闭设置";
        return new(value.HasDisableSetting, !value.HasDisableSetting && !conflict,
            original is not null && !conflict, status,
            conflict ? "当前设置与备份后的关闭设置不一致，无法自动覆盖或恢复。" : null);
    }

    private string CreateBackup(ExplorerUndoValue original) => JsonSerializer.Serialize(new
    {
        SchemaVersion = 1,
        RegistryHive = "CurrentUser",
        RegistryPath,
        RegistryUserSid = store.UserSid,
        ValueName,
        CreatedUtc = DateTime.UtcNow.ToString("o"),
        Original = new { original.Exists, original.Kind, original.Data }
    });

    private ExplorerUndoValue ReadOriginal(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement backup = document.RootElement;
        if (backup.ValueKind != JsonValueKind.Object ||
            !backup.TryGetProperty("SchemaVersion", out JsonElement schema) ||
            !schema.TryGetInt32(out int version) || version != 1 ||
            !Matches(backup, "RegistryHive", "CurrentUser") ||
            !Matches(backup, "RegistryPath", RegistryPath) ||
            !Matches(backup, "RegistryUserSid", store.UserSid) ||
            !Matches(backup, "ValueName", ValueName) ||
            !backup.TryGetProperty("Original", out JsonElement original))
        {
            throw new InvalidDataException("备份不属于当前账户的 ClipZero 文件撤销设置。本次不会修改设置。");
        }
        return ExplorerUndoValue.Parse(original);
    }

    private static bool Matches(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String && value.GetString() == expected;
}
