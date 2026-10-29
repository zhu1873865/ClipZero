using ClipZero.Core;

namespace ClipZero.UI;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(30, 38, 49);
    private static readonly Color Muted = Color.FromArgb(99, 108, 121);
    private readonly Label _status;
    private readonly Label _detail;
    private readonly ListBox _files;
    private readonly Button _clear;
    private readonly Label _notice;
    private readonly Label _shortcut;
    private readonly Label _undoStatus;
    private readonly Button _disableUndo;
    private readonly Button _restoreUndo;
    private readonly ToolTip _undoToolTip = new();
    private readonly TableLayoutPanel _layout;
    private readonly Label _undoFeedback;
    private readonly LinkLabel _refresh;
    private bool _exiting;
    private readonly List<Font> _fonts = [];

    internal event Action? ClearRequested;
    internal event Action? RefreshRequested;
    internal event Action? ExplicitRefreshRequested;
    internal event Action? DisableExplorerUndoRequested;
    internal event Action? RestoreExplorerUndoRequested;

    internal MainForm()
    {
        Text = "ClipZero";
        Icon = IconFactory.Create(Color.FromArgb(99, 108, 121));
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = MakeFont(10);
        BackColor = Color.FromArgb(248, 249, 251);
        ForeColor = Ink;
        ClientSize = new Size(424, 536);
        MinimumSize = new Size(360, 280);
        AutoScroll = true;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 1, RowCount = 9,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(24, 20, 24, 14)
        };
        _layout = layout;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new float[] { 40, 30, 66, 96, 44, 32, 128, 0, 0 })
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Absolute, height));

        var title = MakeLabel("ClipZero", 23, Ink, FontStyle.Bold);
        title.AccessibleName = "ClipZero Windows 文件粘贴保护工具";
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(MakeLabel("Windows 文件粘贴保护工具", 10, Muted), 0, 1);

        var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var statusHeading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2, Margin = Padding.Empty
        };
        statusHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusHeading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status = MakeLabel("正在读取剪贴板…", 12, Ink, FontStyle.Bold);
        _refresh = new LinkLabel
        {
            Text = "刷新", AccessibleName = "刷新状态", Dock = DockStyle.Fill, AutoSize = true,
            MinimumSize = new Size(70, 0),
            TextAlign = ContentAlignment.MiddleRight, Font = MakeFont(9), Margin = Padding.Empty,
            LinkColor = Color.FromArgb(36, 91, 170), ActiveLinkColor = Ink,
            LinkBehavior = LinkBehavior.HoverUnderline, TabStop = true
        };
        _refresh.LinkClicked += (_, _) => ExplicitRefreshRequested?.Invoke();
        statusHeading.Controls.Add(_status, 0, 0);
        statusHeading.Controls.Add(_refresh, 1, 0);
        _detail = MakeLabel("只查看当前文件复制状态", 9, Muted);
        statusLayout.Controls.Add(statusHeading, 0, 0);
        statusLayout.Controls.Add(_detail, 0, 1);
        layout.Controls.Add(statusLayout, 0, 2);

        _files = new ListBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            BackColor = BackColor, ForeColor = Ink, IntegralHeight = false,
            SelectionMode = SelectionMode.None, HorizontalScrollbar = true,
            AccessibleName = "当前待粘贴文件名称", Margin = new Padding(0, 8, 0, 12)
        };
        layout.Controls.Add(_files, 0, 3);

        _clear = new Button
        {
            Text = "取消文件复制", Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(36, 91, 170),
            ForeColor = Color.White, Font = MakeFont(11, FontStyle.Bold),
            Margin = Padding.Empty, Enabled = false,
            AccessibleDescription = "清空当前文件剪贴板，原始文件不受影响"
        };
        _clear.FlatAppearance.BorderSize = 0;
        _clear.Click += (_, _) => ClearRequested?.Invoke();
        layout.Controls.Add(_clear, 0, 4);
        _shortcut = MakeLabel("Ctrl + Alt + Backspace  ·  托盘左键也可取消", 9, Muted);
        _shortcut.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(_shortcut, 0, 5);

        var undoLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Margin = new Padding(0, 8, 0, 0)
        };
        foreach (float height in new float[] { 22, 22, 36, 36 })
            undoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        undoLayout.Controls.Add(MakeLabel("文件撤销设置（实验性）", 10, Ink, FontStyle.Bold), 0, 0);
        _undoStatus = MakeLabel("正在读取设置…", 9, Muted);
        _undoStatus.AccessibleName = "文件撤销设置状态";
        undoLayout.Controls.Add(_undoStatus, 0, 1);
        var undoButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        undoButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        undoButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _disableUndo = MakeUndoButton("尝试关闭撤销", new Padding(0, 2, 6, 2));
        _restoreUndo = MakeUndoButton("恢复原设置", new Padding(6, 2, 0, 2));
        _disableUndo.Click += (_, _) => DisableExplorerUndoRequested?.Invoke();
        _restoreUndo.Click += (_, _) => RestoreExplorerUndoRequested?.Invoke();
        undoButtons.Controls.Add(_disableUndo, 0, 0);
        undoButtons.Controls.Add(_restoreUndo, 1, 0);
        undoLayout.Controls.Add(undoButtons, 0, 2);
        undoLayout.Controls.Add(MakeLabel("不保证隐藏菜单；灰色仅表示当前不可用\n重新登录后仍可撤销？可恢复原设置", 8.5f, Muted), 0, 3);
        _undoToolTip.SetToolTip(_disableUndo,
            "尝试修改未公开的 Windows 设置。若系统支持，会影响桌面和文件夹的全部撤销/重做（Ctrl+Z / Ctrl+Y）。不会立即隐藏菜单。");
        layout.Controls.Add(undoLayout, 0, 6);
        _undoFeedback = new Label
        {
            AccessibleName = "文件撤销操作结果", Dock = DockStyle.Top, AutoSize = true,
            Font = MakeFont(9), ForeColor = Ink, BackColor = Color.FromArgb(233, 239, 247),
            Padding = new Padding(10, 8, 10, 8), Margin = new Padding(0, 8, 0, 0), Visible = false
        };
        layout.Controls.Add(_undoFeedback, 0, 7);
        _notice = MakeLabel("关闭窗口后继续在托盘运行", 8.5f, Muted);
        _notice.Dock = DockStyle.Top;
        _notice.AutoSize = true;
        _notice.AutoEllipsis = false;
        _notice.MinimumSize = new Size(0, 26);
        _notice.Margin = new Padding(0, 8, 0, 0);
        _notice.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(_notice, 0, 8);
        Controls.Add(layout);
        layout.SizeChanged += (_, _) => UpdateWrappingWidths();
        UpdateWrappingWidths();
        FormClosing += (_, e) =>
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
        VisibleChanged += (_, _) => { if (Visible) RefreshRequested?.Invoke(); };
    }

    private Font MakeFont(float size, FontStyle style = FontStyle.Regular)
    {
        var font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, size, style);
        _fonts.Add(font);
        return font;
    }

    private Label MakeLabel(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text, Font = MakeFont(size, style), ForeColor = color,
        Dock = DockStyle.Fill, Margin = Padding.Empty, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft
    };

    private void UpdateWrappingWidths()
    {
        int width = Math.Max(120, _layout.ClientSize.Width - _layout.Padding.Horizontal);
        var maximum = new Size(width, 0);
        if (_undoFeedback.MaximumSize != maximum) _undoFeedback.MaximumSize = maximum;
        if (_notice.MaximumSize != maximum) _notice.MaximumSize = maximum;
    }

    internal void SetExplorerUndoFeedback(string message, bool error, bool requiresSignIn)
    {
        _undoFeedback.Text = message + (requiresSignIn
            ? "\n保存工作后：开始菜单 → 账户头像 → 注销，再重新登录检查效果。" : "");
        _undoFeedback.BackColor = error ? Color.FromArgb(252, 239, 231) : Color.FromArgb(233, 239, 247);
        _undoFeedback.ForeColor = error ? Color.FromArgb(151, 73, 46) : Ink;
        _undoFeedback.Visible = true;
        UpdateWrappingWidths();
        PerformLayout();
        ScrollControlIntoView(_undoFeedback);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FitToWorkingArea();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitToWorkingArea();
        UpdateWrappingWidths();
    }

    private void FitToWorkingArea()
    {
        Rectangle working = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(Width, working.Width);
        int height = Math.Min(Height, working.Height);
        if (MinimumSize.Width > working.Width || MinimumSize.Height > working.Height)
            MinimumSize = new Size(Math.Min(MinimumSize.Width, working.Width), Math.Min(MinimumSize.Height, working.Height));
        int x = Math.Clamp(Left, working.Left, working.Right - width);
        int y = Math.Clamp(Top, working.Top, working.Bottom - height);
        Bounds = new Rectangle(x, y, width, height);
    }

    private Button MakeUndoButton(string text, Padding margin)
    {
        var button = new Button
        {
            Text = text, AccessibleName = text, Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Ink,
            Font = MakeFont(9), Enabled = false, Margin = margin
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(205, 212, 222);
        return button;
    }

    internal void UpdateExplorerUndo(ExplorerUndoSnapshot? snapshot, bool busy, bool reading = false)
    {
        _undoStatus.Text = busy ? "正在保存设置…" : reading ? "正在读取设置…" : snapshot?.Status ?? "正在读取设置…";
        _undoStatus.ForeColor = snapshot?.Error is null ? Muted : Color.FromArgb(151, 73, 46);
        _disableUndo.Enabled = !busy && !reading && snapshot?.CanDisable == true;
        _restoreUndo.Enabled = !busy && !reading && snapshot?.CanRestore == true;
        _undoToolTip.SetToolTip(_undoStatus, snapshot?.Error ??
            "这里只读取设置是否已保存，无法验证 Explorer 是否采用。灰色撤销项当前不能执行，也可能只是没有可撤销操作。");
        _undoToolTip.SetToolTip(_restoreUndo, snapshot?.HasDisableSetting == true && !snapshot.CanRestore
            ? snapshot.Error ?? "没有原设置备份，无法恢复。"
            : "恢复关闭文件撤销之前的设置。");
    }

    internal void UpdateSnapshot(ClipboardSnapshot snapshot, bool clearing)
    {
        _status.Text = StatusText.Summary(snapshot);
        _status.ForeColor = snapshot.State is ClipboardState.Copied or ClipboardState.Cut or ClipboardState.Files
            ? Color.FromArgb(138, 96, 12) : Ink;
        _detail.Text = snapshot.State switch
        {
            ClipboardState.None => "文本、图片等普通剪贴板内容会保留",
            ClipboardState.Unavailable => "可能被其他程序占用；稍后再试",
            ClipboardState.Files => "操作类型未知；取消只清空剪贴板",
            _ => snapshot.FileCount > snapshot.FileNames.Count
                ? $"显示前 {snapshot.FileNames.Count} 项；取消时清空全部待粘贴文件"
                : "取消只清空剪贴板，原始文件不受影响"
        };
        _files.BeginUpdate();
        try
        {
            _files.Items.Clear();
            foreach (string name in snapshot.FileNames) _files.Items.Add(name);
        }
        finally { _files.EndUpdate(); }
        _files.Enabled = snapshot.HasFiles;
        _clear.Enabled = !clearing && StatusText.HasFiles(snapshot);
        _clear.BackColor = _clear.Enabled ? Color.FromArgb(36, 91, 170) : Color.FromArgb(225, 229, 235);
        _clear.ForeColor = _clear.Enabled ? Color.White : Muted;
        _clear.Text = clearing ? "正在取消…" : snapshot.State == ClipboardState.Cut ? "取消文件剪切" : "取消文件复制";
    }

    internal void SetNotice(string text) => _notice.Text = text;
    internal void SetHotKeyAvailability(bool available)
    {
        if (!available) _shortcut.Text = "快捷键被占用或注册失败 · 请使用托盘或按钮";
    }

    internal void Reveal()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        Activate();
    }

    internal void Exit() { _exiting = true; Close(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Icon?.Dispose();
            _undoToolTip.Dispose();
            foreach (Font font in _fonts) font.Dispose();
        }
        base.Dispose(disposing);
    }
}
