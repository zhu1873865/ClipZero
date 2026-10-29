using System.Drawing.Imaging;
using System.Reflection;
using ClipZero.Core;
using ClipZero.UI;

namespace ClipZero.Tests;

/// <summary>Exercises real WinForms controls on the verified private desktop; no clipboard or registry access.</summary>
internal static class MainFormUITests
{
    internal static void VerifyActionsAndLayout(MainForm form, string output)
    {
        Button clear = Descendants(form).OfType<Button>().Single(button =>
            button.AccessibleDescription == "清空当前文件剪贴板，原始文件不受影响");
        Control refresh = Descendants(form).Single(control => control.AccessibleName == "刷新状态");
        ListBox names = Descendants(form).OfType<ListBox>().Single();
        int clearRequests = 0;
        int explicitRefreshRequests = 0;
        int visibleRefreshRequests = 0;
        form.ClearRequested += () => clearRequests++;
        form.ExplicitRefreshRequested += () => explicitRefreshRequests++;
        form.RefreshRequested += () => visibleRefreshRequests++;

        form.UpdateSnapshot(new(ClipboardState.Copied, 2, 100,
            Enumerable.Range(1, 8).Select(number => $"很长的文件名称，需要横向查看完整内容-{number}.txt").ToArray()), false);
        Program.Equal(8, names.Items.Count);
        clear.PerformClick();
        Program.Equal(1, clearRequests);
        form.UpdateSnapshot(new(ClipboardState.Cut, 2, 1, ["待剪切文件夹"]), true);
        Program.True(!clear.Enabled);
        clear.PerformClick();
        Program.Equal(1, clearRequests);
        form.UpdateSnapshot(new(ClipboardState.None, 3, 0, []), false);
        Program.Equal(0, names.Items.Count);
        Program.True(!clear.Enabled);
        clear.PerformClick();
        Program.Equal(1, clearRequests);
        Console.WriteLine("PASS Clipboard UI forgets names, dispatches enabled actions, and blocks duplicate clears while busy.");

        form.UpdateSnapshot(new(ClipboardState.Unavailable, 4, 0, []), false);
        Activate(refresh);
        Program.Equal(1, explicitRefreshRequests);
        Program.Equal(0, visibleRefreshRequests);
        form.Hide();
        form.Show();
        Application.DoEvents();
        Program.Equal(1, explicitRefreshRequests);
        Program.Equal(1, visibleRefreshRequests);
        Console.WriteLine("PASS Explicit refresh is available for unreadable states and is distinct from reopening the window.");

        form.SetExplorerUndoFeedback("设置已保存。", false, true);
        Label feedback = Descendants(form).OfType<Label>().Single(label => label.AccessibleName == "文件撤销操作结果");
        Program.True(feedback.Visible && feedback.Text.Contains("设置已保存", StringComparison.Ordinal));
        Program.True(feedback.Text.Contains("注销", StringComparison.Ordinal));
        string savedFeedback = feedback.Text;
        form.UpdateExplorerUndo(new(true, false, true, "关闭设置已保存，效果未确认"), false);
        form.UpdateSnapshot(new(ClipboardState.None, 5, 0, []), false);
        form.SetNotice("当前无待粘贴文件。");
        Program.Equal(savedFeedback, feedback.Text);
        Save(form, output, "undo-feedback");
        form.SetExplorerUndoFeedback("保存未完成，请稍后重试。", true, false);
        Program.True(feedback.Text.Contains("保存未完成", StringComparison.Ordinal));
        Program.True(!feedback.Text.Contains("注销", StringComparison.Ordinal));
        Console.WriteLine("PASS Undo result stays visible through unrelated refreshes and gives sign-in guidance only after a saved change.");

        Size original = form.ClientSize;
        int scaledWidth = (int)Math.Ceiling(424 * form.DeviceDpi / 96d);
        int scaledHeight = (int)Math.Ceiling(320 * form.DeviceDpi / 96d);
        form.ClientSize = new Size(scaledWidth, scaledHeight);
        Program.True(form.ClientSize.Height <= scaledHeight + 8);
        form.UpdateSnapshot(new(ClipboardState.Copied, 6, 1, ["small-window.txt"]), false);
        form.UpdateExplorerUndo(new(false, true, true, "已恢复原设置"), false);
        VerifyReachableActions(form);
        Save(form, output, "compact-scrolled");
        form.ClientSize = original;
        Console.WriteLine("PASS A 320 logical-pixel client height keeps every enabled action reachable by scrolling.");

        RenderScaledLayouts(output);
    }

    private static void VerifyReachableActions(MainForm form)
    {
        form.PerformLayout();
        Application.DoEvents();
        foreach (Control button in Descendants(form).Where(control => control.Enabled && control is Button or LinkLabel))
        {
            ScrollableControl? viewport = Ancestors(button).OfType<ScrollableControl>().FirstOrDefault(parent => parent.AutoScroll);
            viewport?.ScrollControlIntoView(button);
            form.PerformLayout();
            Application.DoEvents();
            Rectangle bounds = button.RectangleToScreen(button.ClientRectangle);
            Rectangle client = (viewport ?? form).RectangleToScreen((viewport ?? form).ClientRectangle);
            Rectangle formClient = form.RectangleToScreen(form.ClientRectangle);
            if (!button.Visible || !client.Contains(bounds) || !formClient.Contains(bounds))
                throw new InvalidOperationException($"Action '{button.Text}' is clipped and cannot be reached in a {form.ClientSize} client area.");
            Size caption = TextRenderer.MeasureText(button.Text, button.Font,
                Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (button.ClientSize.Width < caption.Width || button.ClientSize.Height < caption.Height)
                throw new InvalidOperationException($"Action '{button.Text}' does not fit its caption: client {button.ClientSize}, text {caption}, font {button.Font.SizeInPoints} pt, physical {form.DeviceDpi} DPI.");
        }
    }

    private static void RenderScaledLayouts(string output)
    {
        foreach (int targetDpi in new[] { 96, 144, 192 })
        {
            using var form = new MainForm();
            form.Show();
            Application.DoEvents();
            float factor = targetDpi / (float)form.DeviceDpi;
            var originalFonts = Descendants(form).Prepend(form)
                .Select(control => (Control: control, Family: control.Font.FontFamily,
                    Size: control.Font.SizeInPoints, Style: control.Font.Style)).ToArray();
            var fonts = new List<Font>();
            try
            {
                // This intentionally simulates geometry and text sizes. A private desktop
                // cannot change the physical monitor DPI, so these are not native DPI tests.
                form.Scale(new SizeF(factor, factor));
                foreach (var original in originalFonts)
                {
                    var font = new Font(original.Family, original.Size * factor, original.Style);
                    fonts.Add(font);
                    original.Control.Font = font;
                }
                form.ClientSize = new Size((int)Math.Ceiling(424 * targetDpi / 96d),
                    (int)Math.Ceiling(320 * targetDpi / 96d));
                form.UpdateSnapshot(new(ClipboardState.Copied, 7, 3,
                    ["项目资料", "会议记录.txt", "设计草稿.png"]), false);
                form.UpdateExplorerUndo(new(false, true, true, "已恢复原设置"), false);
                form.SetExplorerUndoFeedback("设置已保存。", false, true);
                VerifyReachableActions(form);
                Save(form, output, $"compact-simulated-{targetDpi}dpi");
                Console.WriteLine($"PASS Simulated {targetDpi} DPI geometry/text layout keeps actions reachable; physical monitor is {form.DeviceDpi} DPI.");
                form.Exit();
            }
            finally { foreach (Font font in fonts) font.Dispose(); }
        }
    }

    internal static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in Descendants(child)) yield return nested;
        }
    }

    internal static void Activate(Control control)
    {
        if (control is Button button)
        {
            button.PerformClick();
            return;
        }
        if (control is LinkLabel link)
        {
            (typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Link action is unavailable."))
                .Invoke(link, [new LinkLabelLinkClickedEventArgs(link.Links[0])]);
            return;
        }
        throw new InvalidOperationException($"Unsupported action control {control.GetType().Name}.");
    }

    private static IEnumerable<Control> Ancestors(Control control)
    {
        for (Control? parent = control.Parent; parent is not null; parent = parent.Parent) yield return parent;
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
}
