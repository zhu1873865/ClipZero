using System.Drawing.Drawing2D;
using ClipZero.Interop;

namespace ClipZero.UI;

internal static class IconFactory
{
    internal static Icon Create(Color color, int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size / 32f, size / 32f);
            using var fill = new SolidBrush(color);
            using var pen = new Pen(Color.White, 2.2f) { LineJoin = LineJoin.Round };
            graphics.FillEllipse(fill, 1, 1, 30, 30);
            graphics.DrawRectangle(pen, 10, 9, 12, 16);
            graphics.DrawLine(pen, 13, 7, 19, 7);
            graphics.DrawLine(pen, 13, 14, 19, 14);
            graphics.DrawLine(pen, 13, 18, 19, 18);
        }
        nint handle = bitmap.GetHicon();
        try
        {
            using Icon borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally { NativeMethods.DestroyIcon(handle); }
    }
}
