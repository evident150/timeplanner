using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TimePlanner.Core
{
    /// <summary>把界面离屏渲染成 PNG（用于自动质检截图）。</summary>
    public static class Preview
    {
        public static void Capture(FrameworkElement el, string path, Brush backdrop)
        {
            el.UpdateLayout();
            Pump();
            el.UpdateLayout();

            int w = (int)Math.Ceiling(el.ActualWidth > 1 ? el.ActualWidth : el.Width);
            int h = (int)Math.Ceiling(el.ActualHeight > 1 ? el.ActualHeight : el.Height);
            if (w <= 1) w = 320;
            if (h <= 1) h = 240;

            RenderTargetBitmap rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(el);

            if (backdrop == null)
            {
                Save(rtb, path);
                return;
            }

            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(backdrop, null, new Rect(0, 0, w, h));
                dc.DrawImage(rtb, new Rect(0, 0, w, h));
            }
            RenderTargetBitmap rtb2 = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb2.Render(dv);
            Save(rtb2, path);
        }

        /// <summary>
        /// 给截图右下角盖一枚「示例数据」角标。
        /// 截图里的任务名都来自内置示例数据，盖个章，免得看的人（包括我自己）把它
        /// 当成用户真实写在里面的计划。
        /// </summary>
        public static void Sample(string path)
        {
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream(bytes))
                using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(ms))
                {
                    string text = bmp.Width < 420 ? "示例数据" : "示例数据 · 界面预览";
                    float fs = bmp.Width < 420 ? 10.5f : 13f;
                    using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                    using (System.Drawing.Font font = new System.Drawing.Font("Microsoft YaHei", fs, System.Drawing.GraphicsUnit.Pixel))
                    using (System.Drawing.SolidBrush box = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(180, 26, 16, 10)))
                    using (System.Drawing.Pen edge = new System.Drawing.Pen(System.Drawing.Color.FromArgb(150, 214, 178, 96), 1f))
                    using (System.Drawing.SolidBrush ink = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(235, 244, 232, 206)))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        System.Drawing.SizeF sz = g.MeasureString(text, font);
                        float bw = sz.Width + 16f;
                        float bh = sz.Height + 9f;
                        float x = bmp.Width - bw - 10f;
                        float y = bmp.Height - bh - 10f;
                        g.FillRectangle(box, x, y, bw, bh);
                        g.DrawRectangle(edge, x, y, bw, bh);
                        g.DrawString(text, font, ink, x + 8f, y + 4.5f);
                    }
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            catch (Exception) { }
        }

        static void Save(BitmapSource bmp, string path)
        {
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (System.IO.FileStream fs = System.IO.File.Create(path))
            {
                enc.Save(fs);
            }
        }

        /// <summary>让动画时钟往前走 ms 毫秒，同时把渲染管线跑起来（离屏抓动画中间帧用）。</summary>
        public static void Settle(int ms)
        {
            Dispatcher d = Dispatcher.CurrentDispatcher;
            DateTime end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end)
            {
                d.Invoke(DispatcherPriority.Background, new Action(delegate() { }));
                System.Threading.Thread.Sleep(12);
            }
        }

        static void Pump()
        {
            Dispatcher d = Dispatcher.CurrentDispatcher;
            d.Invoke(DispatcherPriority.Loaded, new Action(delegate() { }));
            d.Invoke(DispatcherPriority.Render, new Action(delegate() { }));
        }
    }
}
