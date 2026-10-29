using System.Drawing.Imaging;
using System.Reflection;
using ClipZero.Core;
using ClipZero.UI;

namespace ClipZero.Tests;

/// <summary>Runs only in the verified private desktop UI child, with fake settings storage.</summary>
internal static class ExplorerUndoUITests
{
    internal static void VerifyForm(MainForm form, string output)
    {
        Button disable = Descendants(form).OfType<Button>().Single(button => button.Text == "尝试关闭撤销");
        Button restore = Descendants(form).OfType<Button>().Single(button => button.Text == "恢复原设置");
        Label status = Descendants(form).OfType<Label>().Single(label => label.AccessibleName == "文件撤销设置状态");
        int disableRequests = 0;
        int restoreRequests = 0;
        form.DisableExplorerUndoRequested += () => disableRequests++;
        form.RestoreExplorerUndoRequested += () => restoreRequests++;

        form.UpdateExplorerUndo(null, false);
        Program.Equal("正在读取设置…", status.Text);
        Program.True(!disable.Enabled && !restore.Enabled);
        disable.PerformClick();
        restore.PerformClick();
        Program.Equal(0, disableRequests);
        Program.Equal(0, restoreRequests);
        Console.WriteLine("PASS Undo UI does not offer changes before status is known.");

        var available = new ExplorerUndoSnapshot(false, true, false, "未保存关闭设置");
        form.UpdateExplorerUndo(available, false);
        Program.Equal(available.Status, status.Text);
        Program.True(disable.Enabled && !restore.Enabled);
        disable.PerformClick();
        restore.PerformClick();
        Program.Equal(1, disableRequests);
        Program.Equal(0, restoreRequests);
        Console.WriteLine("PASS Undo UI close button dispatches the close action only.");

        var saved = new ExplorerUndoSnapshot(true, false, true, "关闭设置已保存，效果未确认");
        form.UpdateExplorerUndo(saved, false);
        Program.Equal(saved.Status, status.Text);
        Program.True(!disable.Enabled && restore.Enabled);
        disable.PerformClick();
        restore.PerformClick();
        Program.Equal(1, disableRequests);
        Program.Equal(1, restoreRequests);
        Save(form, output, "undo-disabled");
        Console.WriteLine("PASS Undo UI restore button dispatches the restore action only.");

        form.UpdateExplorerUndo(saved, true);
        Program.Equal("正在保存设置…", status.Text);
        Program.True(!disable.Enabled && !restore.Enabled);
        disable.PerformClick();
        restore.PerformClick();
        Program.Equal(1, disableRequests);
        Program.Equal(1, restoreRequests);
        Save(form, output, "undo-busy");
        Console.WriteLine("PASS Undo UI blocks both actions while saving.");

        var unavailable = new ExplorerUndoSnapshot(false, false, false,
            "暂时无法读取文件撤销设置", "测试用读取失败；不访问真实设置");
        form.UpdateExplorerUndo(unavailable, false);
        Program.Equal(unavailable.Status, status.Text);
        Program.True(!disable.Enabled && !restore.Enabled);
        Save(form, output, "undo-unavailable");
        Console.WriteLine("PASS Undo UI shows Chinese failure status and blocks unavailable actions.");

        form.UpdateExplorerUndo(new(true, false, false, "关闭设置已保存，效果未确认"), false);
        Program.True(!disable.Enabled && !restore.Enabled);
        Console.WriteLine("PASS Undo UI cannot restore a setting without its original backup.");
        form.UpdateExplorerUndo(available, false);
        Save(form, output, "undo-available");
    }

    internal static void VerifyTrayIntegration()
    {
        var store = new MemoryUndoStore();
        var feedback = new List<(string Title, string Message, bool Error)>();
        using var context = new TrayApplicationContext(false, () => new ExplorerUndoService(store),
            (title, message, error) => feedback.Add((title, message, error)));
        MainForm form = Field<MainForm>(context, "_form");
        form.Hide();
        // A private desktop still shares WinSta0's clipboard. Suppress the clipboard refresh
        // timer before pumping messages so this test reads and mutates only fake undo data.
        System.Windows.Forms.Timer clipboardTimer = Field<System.Windows.Forms.Timer>(context, "_refreshTimer");
        clipboardTimer.Stop();
        ToolStripMenuItem disable = Field<ToolStripMenuItem>(context, "_disableUndoItem");
        ToolStripMenuItem restore = Field<ToolStripMenuItem>(context, "_restoreUndoItem");
        Program.Equal("尝试关闭撤销", disable.Text);
        Program.Equal("恢复原设置", restore.Text);
        Program.True(!disable.Enabled && !restore.Enabled);
        PumpUntil(() => disable.Enabled && !Field<bool>(context, "_readingUndo"), clipboardTimer);
        Program.Equal(0, store.Writes);
        Program.Equal(0, store.BackupCreates);
        Program.True(store.Backup is null);
        Console.WriteLine("PASS Undo tray startup reads status without writing settings or making a backup.");

        disable.PerformClick();
        Program.True(!disable.Enabled && !restore.Enabled);
        PumpUntil(() => restore.Enabled && !Field<bool>(context, "_readingUndo"), clipboardTimer);
        Program.Equal(1, store.Writes);
        Program.Equal(1, store.BackupCreates);
        Program.True(store.Value.HasDisableSetting && store.Backup is not null && !disable.Enabled);
        Program.Equal(1, feedback.Count);
        Program.True(!feedback[0].Error && feedback[0].Message.Contains("注销", StringComparison.Ordinal));
        Program.True(!form.Visible);
        Console.WriteLine("PASS Undo tray close action backs up and saves through the integrated service.");

        restore.PerformClick();
        Program.True(!disable.Enabled && !restore.Enabled);
        PumpUntil(() => disable.Enabled && !Field<bool>(context, "_readingUndo"), clipboardTimer);
        Program.Equal(2, store.Writes);
        Program.Equal(1, store.BackupDeletes);
        Program.True(!store.Value.Exists && store.Backup is null && !restore.Enabled);
        Program.Equal(2, feedback.Count);
        Program.True(!feedback[1].Error && feedback[1].Title.Contains("恢复", StringComparison.Ordinal));
        Console.WriteLine("PASS Undo tray restore action restores original data and updates both menu items.");

        // Keep the form hidden: revealing a form normally asks for a clipboard refresh.
        // The injected presenter captures undo results without touching the user's clipboard.
        Button formDisable = Descendants(form).OfType<Button>().Single(button => button.Text == "尝试关闭撤销");
        Button formRestore = Descendants(form).OfType<Button>().Single(button => button.Text == "恢复原设置");
        Program.True(formDisable.Enabled && !formRestore.Enabled);
        ClickHidden(formDisable);
        Program.True(!formDisable.Enabled && !formRestore.Enabled);
        PumpUntil(() => formRestore.Enabled && !Field<bool>(context, "_readingUndo"), clipboardTimer);
        Program.Equal(3, store.Writes);
        Program.Equal(2, store.BackupCreates);
        Program.True(store.Value.HasDisableSetting);
        ClickHidden(formRestore);
        Program.True(!formDisable.Enabled && !formRestore.Enabled);
        PumpUntil(() => formDisable.Enabled && !Field<bool>(context, "_readingUndo"), clipboardTimer);
        Program.Equal(4, store.Writes);
        Program.Equal(2, store.BackupDeletes);
        Program.True(!store.Value.Exists && store.Backup is null);
        Program.Equal(4, feedback.Count);
        Program.True(feedback.All(result => !result.Error));
        Console.WriteLine("PASS Undo main window buttons reach the integrated service and restore exact original data.");

        VerifyManualRefresh(context, form, store, clipboardTimer);
    }

    private static void ClickHidden(Button button) =>
        (typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Button click handler is unavailable."))
        .Invoke(button, [EventArgs.Empty]);

    private static void VerifyManualRefresh(TrayApplicationContext context, MainForm form,
        MemoryUndoStore store, System.Windows.Forms.Timer clipboardTimer)
    {
        // An exhausted clipboard-read budget is a legitimate state after long lock contention.
        // Mark a refresh in flight to queue, rather than execute, any native clipboard read.
        SetField(context, "_refreshing", true);
        SetField(context, "_readFailures", 5);
        SetField(context, "_refreshAgain", false);
        ToolStripMenuItem refresh = Field<ToolStripMenuItem>(context, "_refreshItem");
        Program.Equal("刷新状态", refresh.Text);
        refresh.PerformClick();
        Program.Equal(0, Field<int>(context, "_readFailures"));
        Program.True(Field<bool>(context, "_refreshAgain"));
        PumpUntil(() => !Field<bool>(context, "_readingUndo"), clipboardTimer);

        SetField(context, "_readFailures", 5);
        SetField(context, "_refreshAgain", false);
        Control formRefresh = Descendants(form).Single(control => control.AccessibleName == "刷新状态");
        MainFormUITests.Activate(formRefresh);
        Program.Equal(0, Field<int>(context, "_readFailures"));
        Program.True(Field<bool>(context, "_refreshAgain"));
        PumpUntil(() => !Field<bool>(context, "_readingUndo"), clipboardTimer);
        Program.Equal(4, store.Writes);
        Console.WriteLine("PASS Explicit window and tray refresh renew exhausted retries without changing settings.");

        store.FailReads = true;
        refresh.PerformClick();
        PumpUntil(() => !Field<bool>(context, "_readingUndo"), clipboardTimer);
        Program.True(!Field<ToolStripMenuItem>(context, "_disableUndoItem").Enabled);
        Program.True(!Field<ToolStripMenuItem>(context, "_restoreUndoItem").Enabled);
        Label status = Descendants(form).OfType<Label>().Single(label => label.AccessibleName == "文件撤销设置状态");
        Program.True(status.Text.Contains("无法读取", StringComparison.Ordinal));
        Program.Equal(4, store.Writes);
        store.FailReads = false;
        refresh.PerformClick();
        PumpUntil(() => !Field<bool>(context, "_readingUndo"), clipboardTimer);
        Program.True(Field<ToolStripMenuItem>(context, "_disableUndoItem").Enabled);
        Program.Equal(4, store.Writes);
        Console.WriteLine("PASS Explicit refresh recovers unreadable undo settings and keeps unsafe actions disabled until recovery.");

        SetField(context, "_refreshAgain", false);
        SetField(context, "_refreshing", false);
        clipboardTimer.Stop();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }

    private static T Field<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance)
            ?? throw new InvalidOperationException($"Missing integration field {name}."));

    private static void SetField<T>(object instance, string name, T value) =>
        (instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing integration field {name}."))
        .SetValue(instance, value);

    private static void PumpUntil(Func<bool> condition, System.Windows.Forms.Timer clipboardTimer)
    {
        long deadline = Environment.TickCount64 + 4000;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            clipboardTimer.Stop();
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Program.True(condition());
    }

    private static void Save(MainForm form, string output, string name)
    {
        form.PerformLayout();
        form.Refresh();
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, form.ClientRectangle with { Size = form.Size });
        bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
    }

    private sealed class MemoryUndoStore : IExplorerUndoStore
    {
        public string UserSid => "S-1-ui-fake";
        internal ExplorerUndoValue Value { get; set; } = ExplorerUndoValue.Absent;
        internal string? Backup { get; set; }
        internal int Writes { get; private set; }
        internal int BackupCreates { get; private set; }
        internal int BackupDeletes { get; private set; }
        internal bool FailReads { get; set; }
        public IExplorerUndoSession OpenSession() => new Session(this);

        private sealed class Session(MemoryUndoStore store) : IExplorerUndoSession
        {
            public ExplorerUndoValue ReadValue() => store.FailReads
                ? throw new IOException("Synthetic undo-store read failure.") : store.Value;
            public bool BackupExists => store.Backup is not null;
            public string ReadBackup() => store.Backup ?? throw new InvalidOperationException("No test backup.");
            public void CreateBackup(string json) { Program.True(store.Backup is null); store.Backup = json; store.BackupCreates++; }
            public void WriteValue(ExplorerUndoValue value) { store.Value = value; store.Writes++; }
            public void DeleteBackup() { store.Backup = null; store.BackupDeletes++; }
            public void Dispose() { }
        }
    }
}
