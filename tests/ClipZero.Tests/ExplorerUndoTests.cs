using System.Text.Json;
using ClipZero.Core;

namespace ClipZero.Tests;

internal static class ExplorerUndoTests
{
    public static int Run()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Undo status reads without making a backup or registry change", () =>
            {
                var fake = new FakeStore();
                ExplorerUndoSnapshot snapshot = new ExplorerUndoService(fake).GetStatus();
                Program.True(snapshot.CanDisable && !snapshot.CanRestore && !snapshot.HasDisableSetting);
                Program.True(fake.Backup is null);
                Program.Equal(0, fake.Writes);
                Program.Equal(0, fake.BackupCreates);
            }),
            ("Undo disable backs up before writing and verifies the new value", () =>
            {
                var fake = new FakeStore();
                ExplorerUndoResult result = new ExplorerUndoService(fake).Disable();
                Program.True(result.Succeeded && result.Snapshot.HasDisableSetting && result.Snapshot.CanRestore);
                Program.True(result.RequiresSignIn);
                Program.Equal("read,backup,read,write,read", string.Join(',', fake.Events));
                Program.True(fake.Backup is not null);
            }),
            ("Undo restore removes an originally absent value and deletes backup after verification", () =>
            {
                var fake = new FakeStore();
                var service = new ExplorerUndoService(fake);
                service.Disable();
                fake.Events.Clear();
                ExplorerUndoResult result = service.Restore();
                Program.True(result.Succeeded && !result.Snapshot.HasDisableSetting);
                Program.True(result.RequiresSignIn);
                Program.True(!fake.Value.Exists && fake.Backup is null);
                Program.Equal("read,read,write,read,delete", string.Join(',', fake.Events));
            }),
            ("Undo restore preserves all original registry kinds and exact data", () =>
            {
                ExplorerUndoValue[] originals =
                [
                    ExplorerUndoValue.Create("DWord", int.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ExplorerUndoValue.Create("QWord", long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    ExplorerUndoValue.Create("String", "中文 100% "),
                    ExplorerUndoValue.Create("ExpandString", "%TEMP%\\中文"),
                    ExplorerUndoValue.Create("MultiString", new[] { "", "第一项", "%TEMP%" }),
                    ExplorerUndoValue.Create("MultiString", Array.Empty<string>()),
                    ExplorerUndoValue.Create("Binary", "AP8QAQ=="),
                    ExplorerUndoValue.Create("None", "")
                ];
                foreach (ExplorerUndoValue original in originals)
                {
                    var fake = new FakeStore { Value = original };
                    var service = new ExplorerUndoService(fake);
                    Program.True(service.Disable().Succeeded);
                    Program.True(service.Restore().Succeeded);
                    Program.True(fake.Value.SameAs(original));
                }
            }),
            ("Undo repeated disable preserves the first backup", () =>
            {
                var fake = new FakeStore { Value = ExplorerUndoValue.Create("DWord", "12") };
                var service = new ExplorerUndoService(fake);
                service.Disable();
                string? originalBackup = fake.Backup;
                ExplorerUndoResult repeated = service.Disable();
                Program.True(repeated.Succeeded && !repeated.RequiresSignIn);
                Program.Equal(originalBackup, fake.Backup);
                Program.Equal(1, fake.BackupCreates);
                Program.Equal(1, fake.Writes);
            }),
            ("Undo already disabled without backup is preserved and cannot restore", () =>
            {
                var fake = new FakeStore { Value = ExplorerUndoValue.Disabled };
                var service = new ExplorerUndoService(fake);
                ExplorerUndoResult result = service.Disable();
                Program.True(result.Succeeded && !result.Snapshot.CanRestore);
                Program.True(!result.RequiresSignIn);
                Program.Equal(0, fake.Writes);
                Program.Equal(0, fake.BackupCreates);
                Program.Equal(ExplorerUndoOutcome.NoBackup, service.Restore().Outcome);
            }),
            ("Undo restore without backup makes no changes", () =>
            {
                var fake = new FakeStore();
                Program.Equal(ExplorerUndoOutcome.NoBackup, new ExplorerUndoService(fake).Restore().Outcome);
                Program.Equal(0, fake.Writes);
                Program.Equal(0, fake.BackupDeletes);
            }),
            ("Undo external change during backup blocks the write and retains backup", () =>
            {
                var fake = new FakeStore();
                fake.AfterCreate = () => fake.Value = ExplorerUndoValue.Create("DWord", "99");
                ExplorerUndoResult result = new ExplorerUndoService(fake).Disable();
                Program.Equal(ExplorerUndoOutcome.Changed, result.Outcome);
                Program.Equal(0, fake.Writes);
                Program.True(fake.Backup is not null && !result.Snapshot.CanRestore);
            }),
            ("Undo external change after disable blocks both restore and disable", () =>
            {
                var fake = new FakeStore();
                var service = new ExplorerUndoService(fake);
                service.Disable();
                string? backup = fake.Backup;
                fake.Value = ExplorerUndoValue.Create("DWord", "99");
                Program.Equal(ExplorerUndoOutcome.Changed, service.Restore().Outcome);
                Program.Equal(ExplorerUndoOutcome.Changed, service.Disable().Outcome);
                Program.Equal(1, fake.Writes);
                Program.Equal(backup, fake.Backup);
                Program.True(!service.GetStatus().CanDisable && !service.GetStatus().CanRestore);
            }),
            ("Undo restore rechecks immediately before mutation", () =>
            {
                var fake = new FakeStore();
                var service = new ExplorerUndoService(fake);
                service.Disable();
                fake.AfterReadBackup = () => fake.Value = ExplorerUndoValue.Create("DWord", "8");
                Program.Equal(ExplorerUndoOutcome.Changed, service.Restore().Outcome);
                Program.Equal(1, fake.Writes);
                Program.True(fake.Backup is not null);
            }),
            ("Undo backup failure prevents any registry write", () =>
            {
                var fake = new FakeStore { FailCreate = true };
                Program.Equal(ExplorerUndoOutcome.Failed, new ExplorerUndoService(fake).Disable().Outcome);
                Program.Equal(0, fake.Writes);
            }),
            ("Undo registry write failure retains the exact backup", () =>
            {
                var fake = new FakeStore { FailWrite = true };
                Program.Equal(ExplorerUndoOutcome.Failed, new ExplorerUndoService(fake).Disable().Outcome);
                Program.True(fake.Backup is not null && !fake.Value.Exists);
                Program.Equal(0, fake.BackupDeletes);
            }),
            ("Undo failed disable can retry with the original immutable backup", () =>
            {
                foreach (ExplorerUndoValue original in new[] { ExplorerUndoValue.Absent, ExplorerUndoValue.Create("DWord", "12") })
                {
                    var fake = new FakeStore { Value = original, FailWrite = true };
                    var service = new ExplorerUndoService(fake);
                    ExplorerUndoResult failed = service.Disable();
                    string? backup = fake.Backup;
                    Program.Equal(ExplorerUndoOutcome.Failed, failed.Outcome);
                    Program.True(failed.Snapshot.CanDisable && failed.Snapshot.CanRestore && failed.Snapshot.Error is null);
                    Program.Equal("上次操作未完成，可以重试或恢复", failed.Snapshot.Status);
                    fake.FailWrite = false;
                    Program.True(service.Disable().Succeeded);
                    Program.True(fake.Value.HasDisableSetting && service.GetStatus().CanRestore);
                    Program.Equal(backup, fake.Backup);
                    Program.Equal(1, fake.BackupCreates);
                    Program.Equal(1, fake.Writes);
                }
            }),
            ("Undo failed disable can restore by cleaning up without writing the original again", () =>
            {
                foreach (ExplorerUndoValue original in new[] { ExplorerUndoValue.Absent, ExplorerUndoValue.Create("DWord", "12") })
                {
                    var fake = new FakeStore { Value = original, FailWrite = true };
                    var service = new ExplorerUndoService(fake);
                    Program.Equal(ExplorerUndoOutcome.Failed, service.Disable().Outcome);
                    fake.Events.Clear();
                    ExplorerUndoResult restored = service.Restore();
                    Program.True(restored.Succeeded && restored.Snapshot.CanDisable && !restored.Snapshot.CanRestore);
                    Program.True(!restored.RequiresSignIn);
                    Program.True(!restored.Message.Contains("注销", StringComparison.Ordinal));
                    Program.True(!restored.Message.Contains("重新登录", StringComparison.Ordinal));
                    Program.True(fake.Value.SameAs(original) && fake.Backup is null);
                    Program.Equal("read,read,delete", string.Join(',', fake.Events));
                    Program.Equal(0, fake.Writes);
                    Program.Equal(1, fake.BackupDeletes);
                }
            }),
            ("Undo cleanup rechecks the original value before deleting its backup", () =>
            {
                var fake = new FakeStore { FailWrite = true };
                var service = new ExplorerUndoService(fake);
                service.Disable();
                string? backup = fake.Backup;
                fake.AfterReadBackup = () => fake.Value = ExplorerUndoValue.Create("DWord", "8");
                Program.Equal(ExplorerUndoOutcome.Changed, service.Restore().Outcome);
                Program.Equal(backup, fake.Backup);
                Program.Equal(0, fake.Writes);
                Program.Equal(0, fake.BackupDeletes);
                Program.True(fake.Value.SameAs(ExplorerUndoValue.Create("DWord", "8")));
            }),
            ("Undo retry rechecks the original value before writing with a retained backup", () =>
            {
                var original = ExplorerUndoValue.Create("DWord", "12");
                var fake = new FakeStore { Value = original, FailWrite = true };
                var service = new ExplorerUndoService(fake);
                service.Disable();
                string? backup = fake.Backup;
                fake.FailWrite = false;
                fake.AfterReadBackup = () => fake.Value = ExplorerUndoValue.Create("DWord", "8");
                Program.Equal(ExplorerUndoOutcome.Changed, service.Disable().Outcome);
                Program.Equal(backup, fake.Backup);
                Program.Equal(1, fake.BackupCreates);
                Program.Equal(0, fake.Writes);
                Program.Equal(0, fake.BackupDeletes);
            }),
            ("Undo failed disable verification is never reported as success", () =>
            {
                var fake = new FakeStore { IgnoreWrite = true };
                Program.Equal(ExplorerUndoOutcome.Failed, new ExplorerUndoService(fake).Disable().Outcome);
                Program.True(fake.Backup is not null);
            }),
            ("Undo failed restore verification never deletes the backup", () =>
            {
                var fake = new FakeStore();
                var service = new ExplorerUndoService(fake);
                service.Disable();
                fake.IgnoreWrite = true;
                Program.Equal(ExplorerUndoOutcome.Failed, service.Restore().Outcome);
                Program.Equal(0, fake.BackupDeletes);
                Program.True(fake.Backup is not null);
            }),
            ("Undo backup deletion failure is surfaced and retains original backup", () =>
            {
                var fake = new FakeStore();
                var service = new ExplorerUndoService(fake);
                service.Disable();
                fake.FailDelete = true;
                Program.Equal(ExplorerUndoOutcome.Failed, service.Restore().Outcome);
                Program.True(!fake.Value.Exists && fake.Backup is not null);
            }),
            ("Undo failed backup deletion can retry restore without rewriting the restored value", () =>
            {
                foreach (ExplorerUndoValue original in new[] { ExplorerUndoValue.Absent, ExplorerUndoValue.Create("DWord", "12") })
                {
                    var fake = new FakeStore { Value = original };
                    var service = new ExplorerUndoService(fake);
                    service.Disable();
                    string? backup = fake.Backup;
                    fake.FailDelete = true;
                    ExplorerUndoResult failed = service.Restore();
                    Program.Equal(ExplorerUndoOutcome.Failed, failed.Outcome);
                    Program.True(failed.Snapshot.CanDisable && failed.Snapshot.CanRestore);
                    Program.True(fake.Value.SameAs(original));
                    Program.Equal(backup, fake.Backup);
                    Program.Equal(2, fake.Writes);
                    fake.FailDelete = false;
                    fake.Events.Clear();
                    ExplorerUndoResult restored = service.Restore();
                    Program.True(restored.Succeeded && restored.Snapshot.CanDisable && !restored.Snapshot.CanRestore);
                    Program.True(!restored.RequiresSignIn);
                    Program.True(!restored.Message.Contains("注销", StringComparison.Ordinal));
                    Program.True(!restored.Message.Contains("重新登录", StringComparison.Ordinal));
                    Program.Equal("read,read,delete", string.Join(',', fake.Events));
                    Program.Equal(2, fake.Writes);
                    Program.Equal(1, fake.BackupCreates);
                    Program.Equal(1, fake.BackupDeletes);
                    Program.True(fake.Backup is null && fake.Value.SameAs(original));
                }
            }),
            ("Undo backup with an originally disabled value can restore without rewriting zero", () =>
            {
                var fake = new FakeStore
                {
                    Value = ExplorerUndoValue.Disabled,
                    Backup = JsonSerializer.Serialize(new
                    {
                        SchemaVersion = 1,
                        RegistryHive = "CurrentUser",
                        RegistryPath = ExplorerUndoService.RegistryPath,
                        RegistryUserSid = "S-1-test",
                        ValueName = ExplorerUndoService.ValueName,
                        Original = new { Exists = true, Kind = "DWord", Data = "0" }
                    })
                };
                var service = new ExplorerUndoService(fake);
                Program.True(service.GetStatus().CanRestore && !service.GetStatus().CanDisable);
                string? backup = fake.Backup;
                Program.True(service.Disable().Succeeded);
                Program.Equal(backup, fake.Backup);
                Program.True(service.Restore().Succeeded && fake.Value.HasDisableSetting && fake.Backup is null);
                Program.Equal(0, fake.Writes);
                Program.Equal(1, fake.BackupDeletes);
            }),
            ("Undo invalid or cross-account backup blocks mutations", () =>
            {
                var template = new FakeStore();
                new ExplorerUndoService(template).Disable();
                string valid = template.Backup!;
                string[] invalids =
                [
                    "{ invalid json",
                    "null",
                    valid.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":2", StringComparison.Ordinal),
                    valid.Replace("CurrentUser", "LocalMachine", StringComparison.Ordinal),
                    valid.Replace("S-1-test", "S-1-other", StringComparison.Ordinal),
                    valid.Replace("MaxUndoItems", "OtherSetting", StringComparison.Ordinal),
                    valid.Replace("Explorer\\\\Advanced", "Explorer\\\\Other", StringComparison.Ordinal),
                    valid.Replace("\"Exists\":false", "\"Exists\":\"false\"", StringComparison.Ordinal),
                    valid.Replace("\"Kind\":null", "\"Kind\":\"DWord\"", StringComparison.Ordinal)
                ];
                foreach (string invalid in invalids)
                {
                    var fake = new FakeStore { Value = ExplorerUndoValue.Disabled, Backup = invalid };
                    var service = new ExplorerUndoService(fake);
                    Program.True(service.GetStatus().Error is not null);
                    Program.Equal(ExplorerUndoOutcome.Failed, service.Disable().Outcome);
                    Program.Equal(ExplorerUndoOutcome.Failed, service.Restore().Outcome);
                    Program.Equal(0, fake.Writes);
                    Program.Equal(invalid, fake.Backup);
                }
            }),
            ("Undo valid legacy PowerShell backup remains compatible", () =>
            {
                var fake = new FakeStore
                {
                    Value = ExplorerUndoValue.Disabled,
                    Backup = JsonSerializer.Serialize(new
                    {
                        SchemaVersion = 1,
                        RegistryHive = "CurrentUser",
                        RegistryPath = ExplorerUndoService.RegistryPath,
                        RegistryUserSid = "S-1-test",
                        ValueName = ExplorerUndoService.ValueName,
                        CreatedUtc = "2026-10-14T00:00:00.0000000Z",
                        Original = new { Exists = true, Kind = "ExpandString", Data = "%TEMP%\\中文" }
                    })
                };
                Program.True(new ExplorerUndoService(fake).Restore().Succeeded);
                Program.True(fake.Value.SameAs(ExplorerUndoValue.Create("ExpandString", "%TEMP%\\中文")));
            }),
            ("Undo storage contention is contained without mutation", () =>
            {
                var fake = new FakeStore { FailOpen = true };
                var service = new ExplorerUndoService(fake);
                Program.True(service.GetStatus().Error is not null);
                Program.Equal(ExplorerUndoOutcome.Failed, service.Disable().Outcome);
                Program.Equal(ExplorerUndoOutcome.Failed, service.Restore().Outcome);
                Program.Equal(0, fake.Writes);
            })
        };
        int failures = 0;
        foreach ((string name, Action run) in tests)
        {
            try { run(); Console.WriteLine($"PASS {name}"); }
            catch (Exception exception) { failures++; Console.WriteLine($"FAIL {name}: {exception.Message}"); }
        }
        Console.WriteLine($"Explorer undo: {tests.Length - failures}/{tests.Length} passed (fake storage only).");
        return failures;
    }

    private sealed class FakeStore : IExplorerUndoStore
    {
        public string UserSid => "S-1-test";
        public ExplorerUndoValue Value { get; set; } = ExplorerUndoValue.Absent;
        public string? Backup { get; set; }
        public List<string> Events { get; } = [];
        public int Writes { get; private set; }
        public int BackupCreates { get; private set; }
        public int BackupDeletes { get; private set; }
        public bool FailOpen { get; init; }
        public bool FailCreate { get; init; }
        public bool FailWrite { get; set; }
        public bool FailDelete { get; set; }
        public bool IgnoreWrite { get; set; }
        public Action? AfterCreate { get; set; }
        public Action? AfterReadBackup { get; set; }
        private bool _open;

        public IExplorerUndoSession OpenSession()
        {
            if (FailOpen || _open) throw new IOException("Synthetic store lock contention.");
            _open = true;
            return new Session(this);
        }

        private sealed class Session(FakeStore owner) : IExplorerUndoSession
        {
            public ExplorerUndoValue ReadValue() { owner.Events.Add("read"); return owner.Value; }
            public bool BackupExists => owner.Backup is not null;
            public string ReadBackup()
            {
                string backup = owner.Backup!;
                owner.AfterReadBackup?.Invoke();
                return backup;
            }
            public void CreateBackup(string json)
            {
                owner.Events.Add("backup");
                if (owner.FailCreate || owner.Backup is not null) throw new IOException("Synthetic backup failure.");
                owner.Backup = json;
                owner.BackupCreates++;
                owner.AfterCreate?.Invoke();
            }
            public void WriteValue(ExplorerUndoValue value)
            {
                owner.Events.Add("write");
                if (owner.FailWrite) throw new IOException("Synthetic registry write failure.");
                owner.Writes++;
                if (!owner.IgnoreWrite) owner.Value = value;
            }
            public void DeleteBackup()
            {
                owner.Events.Add("delete");
                if (owner.FailDelete) throw new IOException("Synthetic backup deletion failure.");
                owner.Backup = null;
                owner.BackupDeletes++;
            }
            public void Dispose() => owner._open = false;
        }
    }
}
