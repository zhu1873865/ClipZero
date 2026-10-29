using ClipZero.Core;
using ClipZero.Interop;
using ClipZero.UI;

namespace ClipZero;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly MainForm _form;
    private readonly MessageWindow _messages;
    private readonly ClipboardWorker _worker;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _clearItem;
    private readonly ToolStripMenuItem _disableUndoItem;
    private readonly ToolStripMenuItem _restoreUndoItem;
    private readonly Func<ExplorerUndoService> _undoServiceFactory;
    private readonly Action<string, string, bool>? _undoResultPresenter;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Icon _idleIcon;
    private readonly Icon _filesIcon;
    private readonly Icon _unknownIcon;
    private ClipboardSnapshot _snapshot = new(ClipboardState.Unavailable, 0, 0, [], "正在读取");
    private bool _refreshing;
    private bool _refreshAgain;
    private bool _clearing;
    private bool _exiting;
    private bool _disposed;
    private int _readFailures;
    private ExplorerUndoSnapshot? _undoSnapshot;
    private bool _readingUndo;
    private bool _changingUndo;
    private int _undoVersion;
    private string? _lastNotification;
    private long _lastNotificationTime;

    internal TrayApplicationContext(bool showWindow, Func<ExplorerUndoService>? undoServiceFactory = null,
        Action<string, string, bool>? undoResultPresenter = null)
    {
        _undoServiceFactory = undoServiceFactory ?? (() => new ExplorerUndoService(new WindowsExplorerUndoStore()));
        _undoResultPresenter = undoResultPresenter;
        _form = new MainForm();
        // Create a UI handle/synchronization context even in tray-only mode.
        _ = _form.Handle;
        _worker = new ClipboardWorker();
        _messages = new MessageWindow();
        _idleIcon = IconFactory.Create(Color.FromArgb(112, 121, 133));
        _filesIcon = IconFactory.Create(Color.FromArgb(216, 159, 37));
        _unknownIcon = IconFactory.Create(Color.FromArgb(169, 109, 75));
        _menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("正在读取剪贴板…", null, (_, _) => ShowStatus());
        _refreshItem = new ToolStripMenuItem("刷新状态", null, (_, _) => RefreshAllStatuses());
        _clearItem = new ToolStripMenuItem("取消文件复制", null, (_, _) => _ = ClearAsync());
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripMenuItem("打开窗口", null, (_, _) => _form.Reveal()));
        _menu.Items.Add(_refreshItem);
        _menu.Items.Add(_clearItem);
        _menu.Items.Add(new ToolStripSeparator());
        _disableUndoItem = new ToolStripMenuItem("尝试关闭撤销", null, (_, _) => _ = ChangeExplorerUndoAsync(restore: false)) { Enabled = false };
        _restoreUndoItem = new ToolStripMenuItem("恢复原设置", null, (_, _) => _ = ChangeExplorerUndoAsync(restore: true)) { Enabled = false };
        var undoMenu = new ToolStripMenuItem("文件撤销设置（实验性）");
        undoMenu.DropDownItems.Add(_disableUndoItem);
        undoMenu.DropDownItems.Add(_restoreUndoItem);
        _menu.Items.Add(undoMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出 ClipZero", null, (_, _) => ExitThread()));
        _menu.Opening += (_, _) => { RequestRefresh(); _ = RefreshExplorerUndoAsync(); };
        _tray = new NotifyIcon
        {
            Icon = _idleIcon, Text = "ClipZero · 正在读取剪贴板",
            ContextMenuStrip = _menu, Visible = true
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _ = ClearAsync(); };
        _form.ClearRequested += () => _ = ClearAsync();
        _form.RefreshRequested += RefreshAllStatuses;
        _form.ExplicitRefreshRequested += RefreshAllStatuses;
        _form.DisableExplorerUndoRequested += () => _ = ChangeExplorerUndoAsync(restore: false);
        _form.RestoreExplorerUndoRequested += () => _ = ChangeExplorerUndoAsync(restore: true);
        _messages.ClipboardUpdated += () => { _readFailures = 0; RequestRefresh(); };
        _messages.ClearRequested += () => _ = ClearAsync();
        _messages.ShowRequested += _form.Reveal;
        _form.SetHotKeyAvailability(_messages.HotKeyRegistered);
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 80 };
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); _ = RefreshAsync(); };
        ApplySnapshot(_snapshot);
        RequestRefresh();
        // Queue the read after the UI message loop starts. Startup never changes settings.
        _form.BeginInvoke((Action)(() => _ = RefreshExplorerUndoAsync()));
        if (showWindow || !_messages.ListenerRegistered) _form.Reveal();
        if (!_messages.ListenerRegistered)
            Notify("剪贴板监听注册失败", "请退出后重新运行。窗口仍可在打开时刷新状态。", ToolTipIcon.Warning);
        else if (!_messages.HotKeyRegistered)
            Notify("快捷键不可用", "Ctrl+Alt+Backspace 被占用或注册失败；仍可使用托盘和按钮。", ToolTipIcon.Warning);
    }

    private async Task RefreshExplorerUndoAsync()
    {
        if (_exiting || _readingUndo || _changingUndo) return;
        int version = _undoVersion;
        _readingUndo = true;
        try
        {
            ExplorerUndoSnapshot snapshot = await Task.Run(() => _undoServiceFactory().GetStatus());
            if (!_exiting && !_changingUndo && version == _undoVersion) ApplyExplorerUndo(snapshot);
        }
        catch (Exception)
        {
            if (!_exiting && !_changingUndo && version == _undoVersion)
                ApplyExplorerUndo(new(false, false, false, "暂时无法读取文件撤销设置", "请点击刷新状态，稍后重试。"));
        }
        finally
        {
            _readingUndo = false;
            if (!_exiting && !_changingUndo && version != _undoVersion) _ = RefreshExplorerUndoAsync();
        }
    }

    private async Task ChangeExplorerUndoAsync(bool restore)
    {
        if (_exiting || _changingUndo) return;
        _undoVersion++;
        _changingUndo = true;
        ApplyExplorerUndo(_undoSnapshot);
        try
        {
            ExplorerUndoResult result = await Task.Run(() => restore ? _undoServiceFactory().Restore() : _undoServiceFactory().Disable());
            if (_exiting) return;
            _changingUndo = false;
            ApplyExplorerUndo(result.Snapshot);
            string message = result.Message;
            PresentExplorerUndoResult(restore ? "恢复原设置" : "尝试关闭撤销", message,
                error: !result.Succeeded, requiresSignIn: result.RequiresSignIn);
        }
        catch (Exception)
        {
            if (!_exiting)
            {
                _changingUndo = false;
                ApplyExplorerUndo(new(false, false, false, "暂时无法确认文件撤销设置", "请点击刷新状态，稍后重试。"));
                PresentExplorerUndoResult("文件撤销设置", "操作未完成，请保留原设置备份，点击刷新状态后重试。",
                    error: true, requiresSignIn: false);
            }
        }
        finally
        {
            _changingUndo = false;
            if (!_exiting) { ApplyExplorerUndo(_undoSnapshot); _ = RefreshExplorerUndoAsync(); }
        }
    }

    private void PresentExplorerUndoResult(string title, string message, bool error, bool requiresSignIn)
    {
        if (_undoResultPresenter is not null)
        {
            string presentedMessage = message + (requiresSignIn
                ? "\n保存工作后：开始菜单 → 账户头像 → 注销，再重新登录检查效果。" : "");
            _undoResultPresenter(title, presentedMessage, error);
            return;
        }

        // The result remains visible in the window even when Windows suppresses tray balloons.
        _form.SetExplorerUndoFeedback(message, error, requiresSignIn);
        _form.Reveal();
        if (error) Notify(title, "操作未完成，请查看窗口中的提示。", ToolTipIcon.Warning);
    }

    private void ApplyExplorerUndo(ExplorerUndoSnapshot? snapshot)
    {
        _undoSnapshot = snapshot;
        _form.UpdateExplorerUndo(snapshot, _changingUndo);
        _disableUndoItem.Enabled = !_changingUndo && snapshot?.CanDisable == true;
        _restoreUndoItem.Enabled = !_changingUndo && snapshot?.CanRestore == true;
    }

    private void RequestRefresh()
    {
        if (_exiting) return;
        if (_refreshing || _clearing) { _refreshAgain = true; return; }
        _refreshTimer.Stop();
        _refreshTimer.Interval = 80;
        _refreshTimer.Start();
    }

    private void RefreshAllStatuses()
    {
        _readFailures = 0;
        RequestRefresh();
        _ = RefreshExplorerUndoAsync();
    }

    private async Task RefreshAsync()
    {
        if (_exiting) return;
        if (_refreshing || _clearing) { _refreshAgain = true; return; }
        _refreshing = true;
        try
        {
            ClipboardSnapshot snapshot = await _worker.InvokeAsync(handle => new ClipboardService(new NativeClipboardGateway(handle)).ReadSnapshot());
            if (_exiting) return;
            // A read that finished during an explicit clear must not re-enable the button.
            if (!_clearing) ApplySnapshot(snapshot);
            if (snapshot.State == ClipboardState.Unavailable && ++_readFailures < 5)
            {
                _refreshTimer.Interval = 80 << _readFailures;
                _refreshTimer.Start();
            }
            else if (snapshot.State != ClipboardState.Unavailable) _readFailures = 0;
        }
        catch (Exception)
        {
            if (!_exiting) ApplySnapshot(new(ClipboardState.Unavailable, 0, 0, [], "读取失败"));
        }
        finally
        {
            _refreshing = false;
            if (_refreshAgain && !_clearing && !_exiting) { _refreshAgain = false; RequestRefresh(); }
        }
    }

    private async Task ClearAsync()
    {
        if (_exiting || _clearing) return;
        if (!_snapshot.HasFiles)
        {
            bool unknown = _snapshot.State == ClipboardState.Unavailable;
            _readFailures = 0;
            RequestRefresh();
            if (unknown) Notify("ClipZero", "状态暂时无法读取，请稍后再试。", ToolTipIcon.Warning);
            return;
        }

        // Capture the user-visible version once. Never update it during retries.
        uint expectedSequence = _snapshot.Sequence;
        _clearing = true;
        _refreshTimer.Stop();
        ApplySnapshot(_snapshot);
        try
        {
            ClearResult result = await _worker.InvokeAsync(handle => new ClipboardService(new NativeClipboardGateway(handle)).Clear(expectedSequence));
            for (int attempt = 0; result.Outcome == ClearOutcome.Busy && attempt < 3 && !_exiting; attempt++)
            {
                await Task.Delay(80 << attempt);
                // Queued reads may observe newer contents, but the original token stays fixed.
                if (_exiting) return;
                result = await _worker.InvokeAsync(handle => new ClipboardService(new NativeClipboardGateway(handle)).Clear(expectedSequence));
            }
            if (_exiting) return;
            _clearing = false;
            ApplySnapshot(result.Snapshot);
            string message = result.Outcome switch
            {
                ClearOutcome.Cleared => "已取消待粘贴文件，原始文件不受影响。",
                ClearOutcome.NoFiles => "当前无待粘贴文件。",
                ClearOutcome.Changed => "剪贴板内容已变化，本次未清空。请查看最新状态后重试。",
                ClearOutcome.Busy => "剪贴板被其他程序占用，请稍后重试。",
                _ => "无法清空剪贴板，请稍后重试。"
            };
            bool failed = result.Outcome is ClearOutcome.Busy or ClearOutcome.Failed;
            _form.SetNotice(message);
            if (result.Outcome == ClearOutcome.Changed) _form.Reveal();
            if (failed)
                Notify("ClipZero", message, ToolTipIcon.Warning);
        }
        catch (Exception)
        {
            if (!_exiting)
            {
                ApplySnapshot(ClipboardSnapshot.Unavailable(_snapshot.Sequence, "清空未完成，请重新读取状态。"));
                Notify("清空未完成", "请点击刷新状态后重试。", ToolTipIcon.Warning);
            }
        }
        finally
        {
            _clearing = false;
            if (!_exiting) { ApplySnapshot(_snapshot); _readFailures = 0; RequestRefresh(); }
        }
    }

    private void ApplySnapshot(ClipboardSnapshot snapshot)
    {
        _snapshot = snapshot;
        _form.UpdateSnapshot(snapshot, _clearing);
        _statusItem.Text = StatusText.Summary(snapshot);
        _clearItem.Enabled = snapshot.HasFiles && !_clearing;
        _clearItem.Text = snapshot.State == ClipboardState.Cut ? "取消文件剪切" : "取消文件复制";
        _tray.Icon = snapshot.HasFiles ? _filesIcon : snapshot.State == ClipboardState.Unavailable ? _unknownIcon : _idleIcon;
        _tray.Text = "ClipZero · " + StatusText.Summary(snapshot);
    }

    private void ShowStatus()
    {
        RefreshAllStatuses();
        _form.Reveal();
    }

    private void Notify(string title, string text, ToolTipIcon icon)
    {
        _form.SetNotice(text);
        string notification = title + "\n" + text;
        long now = Environment.TickCount64;
        if (notification == _lastNotification && now - _lastNotificationTime < 5000) return;
        _lastNotification = notification;
        _lastNotificationTime = now;
        _tray.ShowBalloonTip(3500, title, text, icon);
    }

    protected override void ExitThreadCore()
    {
        if (_exiting) return;
        _exiting = true;
        _refreshTimer.Stop();
        _tray.Visible = false;
        _form.Exit();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _exiting = true;
            _refreshTimer.Dispose();
            _messages.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _menu.Dispose();
            _form.Dispose();
            _worker.Dispose();
            _idleIcon.Dispose();
            _filesIcon.Dispose();
            _unknownIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
