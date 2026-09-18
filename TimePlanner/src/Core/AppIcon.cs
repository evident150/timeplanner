using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DColor = System.Drawing.Color;
using DPen = System.Drawing.Pen;
using DGradient = System.Drawing.Drawing2D.LinearGradientBrush;

namespace TimePlanner.Core
{
    /// <summary>程序图标：用 GDI+ 现画，避免依赖外部资源文件。</summary>
    public static class AppIcon
    {
        static System.Drawing.Icon _tray;
        static ImageSource _wpf;

        public static System.Drawing.Icon Tray()
        {
            if (_tray == null)
            {
                using (Bitmap bmp = Draw(32))
                {
                    IntPtr h = bmp.GetHicon();
                    _tray = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();
                }
            }
            return _tray;
        }

        public static ImageSource WpfIcon()
        {
            if (_wpf == null)
            {
                using (Bitmap bmp = Draw(64))
                {
                    IntPtr h = bmp.GetHicon();
                    _wpf = Imaging.CreateBitmapSourceFromHIcon(h, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    _wpf.Freeze();
                }
            }
            return _wpf;
        }

        public static Bitmap Draw(int size)
        {
            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(System.Drawing.Color.Transparent);

                float pad = size * 0.06f;
                RectangleF rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
                float radius = size * 0.26f;

                using (GraphicsPath path = RoundedRect(rect, radius))
                {
                    DColor top = DColor.FromArgb(Theme.Accent.R, Theme.Accent.G, Theme.Accent.B);
                    DColor bottom = DColor.FromArgb(Theme.AccentDeep.R, Theme.AccentDeep.G, Theme.AccentDeep.B);
                    using (DGradient lb = new DGradient(
                        new RectangleF(0, 0, size, size), top, bottom, 90f))
                    {
                        g.FillPath(lb, path);
                    }
                    using (DPen p = new DPen(System.Drawing.Color.FromArgb(70, 255, 255, 255), Math.Max(1f, size * 0.02f)))
                    {
                        g.DrawPath(p, path);
                    }
                }

                // 对勾
                float cx = size * 0.5f, cy = size * 0.53f;
                PointF[] pts = new PointF[]
                {
                    new PointF(size * 0.29f, cy + size * 0.01f),
                    new PointF(size * 0.44f, cy + size * 0.15f),
                    new PointF(size * 0.73f, cy - size * 0.17f)
                };
                using (DPen pen = new DPen(System.Drawing.Color.White, size * 0.115f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, pts);
                }
            }
            return bmp;
        }

        static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2f;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>导出多尺寸 .ico（供 exe 图标 / 快捷方式使用）。</summary>
        public static void SaveIco(string path)
        {
            int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
            List<byte[]> blobs = new List<byte[]>();
            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bmp = Draw(sizes[i]))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    blobs.Add(ms.ToArray());
                }
            }
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i] >= 256 ? 0 : sizes[i];
                    w.Write((byte)s);
                    w.Write((byte)s);
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(blobs[i].Length);
                    w.Write(offset);
                    offset += blobs[i].Length;
                }
                for (int i = 0; i < blobs.Count; i++) w.Write(blobs[i]);
            }
        }
    }
}
