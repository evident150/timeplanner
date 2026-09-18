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
