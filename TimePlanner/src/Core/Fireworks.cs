using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TimePlanner.Core
{
    /// <summary>
    /// 完成任务的礼花筒：一束彩纸/彩带从勾选框炸开，边翻转边被抛出去、再落下来，同时飘一句鼓励的话。
    /// 刻意不用 Effect（半透明窗口上特效是软件渲染，很贵），只用位移/旋转/透明度动画，放完自动清理。
    /// </summary>
    public static class Fireworks
    {
        static readonly Random Rnd = new Random();

        /// <summary>完成一条任务时，左下角小人替圣上挑一句贺辞。</summary>
        public static readonly string[] Cheers = new string[]
        {
            "办得漂亮，朕心甚慰", "此事既了，甚合朕意", "卿甚合朕意", "好，朕准了",
            "爱卿辛苦，朕记你一功", "又了一桩心事", "如此勤勉，朕放心", "好好好，就这么办",
            "朕看好你", "事无大小，办妥便佳"
        };

        /// <summary>当天任务全部完成时，小人说的是这句。</summary>
        public const string AllDone = "今日诸事皆了，朕心大悦";

        /// <summary>没事的时候小人挂在嘴边的一句。</summary>
        public const string Silent = "朕在此候着";

        /// <summary>贺辞的落款。</summary>
        public const string CheerSign = "—— 钦 此";

        static Geometry _ribbon;
        static Geometry _tail;

        /// <summary>对话框左侧那条指着人的小尾巴（给窗体的对话框共用）。</summary>
        public static Geometry Tail()
        {
            if (_tail == null)
            {
                Geometry g = Geometry.Parse("M0,8 L11,0 L11,16 Z");
                g.Freeze();
                _tail = g;
            }
            return _tail;
        }

        static Geometry Ribbon()
        {
            if (_ribbon == null)
            {
                Geometry g = Geometry.Parse("M 0,3.4 Q 3.3,0 6.6,3.4 Q 9.9,6.8 13.2,3.4");
                g.Freeze();
                _ribbon = g;
            }
            return _ribbon;
        }

        public static string PickCheer()
        {
            return Cheers[Rnd.Next(Cheers.Length)];
        }

        /// <summary>彩纸的扇形张角（度）：礼花筒是「一束」喷出去，不是四面开花。</summary>
        const double Spread = 32;

        /// <summary>实际喷射半径：窗口不够大时收住，别让彩纸飞出窗口被裁掉（桌面插件比主程序窄得多）。</summary>
        static double Reach(Canvas layer, Point origin, double power, double aim)
        {
            double r = 196 * power;
            if (layer == null || layer.ActualWidth < 2 || layer.ActualHeight < 2) return r;
            double roomRight = layer.ActualWidth - 10 - origin.X;
            double roomLeft = origin.X - 10;
            double roomUp = origin.Y - 10;
            double[] angs = new double[] { aim - Spread, aim + Spread };
            for (int i = 0; i < angs.Length; i++)
            {
                double a = angs[i] * Math.PI / 180;
                double dx = Math.Cos(a);
                double dy = Math.Sin(a);
                if (dx > 0.25) r = Math.Min(r, roomRight / dx);
                else if (dx < -0.25) r = Math.Min(r, roomLeft / -dx);
                if (dy < -0.25) r = Math.Min(r, roomUp / (-dy * 0.78));
            }
            if (r < 78) r = 78;
            return r;
        }

        /// <summary>
        /// 在 layer（盖在卡片之上、不吃点击的 Canvas）的 origin 处放一筒礼花。
        /// aim 是喷射方向（角度：0 = 向右，-90 = 向上），power 控制大小（桌面插件用 1~1.3 就够）。
        /// </summary>
        public static void Popper(Canvas layer, Point origin, double power, double aim, string cheer)
        {
            if (layer == null) return;
            if (power < 0.5) power = 0.5;
            if (power > 2) power = 2;

            int count = (int)Math.Round(21 * power) + 9;
            if (count < 18) count = 18;
            if (count > 42) count = 42;
            double radius = Reach(layer, origin, power, aim);
            int dur = (int)Math.Round(1180 + 260 * power);

            List<UIElement> made = new List<UIElement>();
            int life = dur;

            Color[] palette = new Color[7];
            palette[0] = Theme.Accent;
            palette[1] = Theme.Blend(Theme.Accent, Colors.White, 0.55);
            palette[2] = Theme.Success;
            palette[3] = Theme.Warning;
            palette[4] = Theme.Purple;
            palette[5] = Theme.C("#FF7A9C");
            palette[6] = Theme.C("#7CD8FF");

            CubicEase out8 = new CubicEase();
            out8.EasingMode = EasingMode.EaseOut;
            QuadraticEase out4 = new QuadraticEase();
            out4.EasingMode = EasingMode.EaseOut;
            QuadraticEase in6 = new QuadraticEase();
            in6.EasingMode = EasingMode.EaseIn;

            // 炮口的一点亮光
            double fs = 13 * power;
            Ellipse flash = new Ellipse();
            flash.Width = fs;
            flash.Height = fs;
            flash.Fill = Theme.B(Theme.Alpha(palette[1], 0.9));
            flash.IsHitTestVisible = false;
            flash.RenderTransformOrigin = new Point(0.5, 0.5);
            Canvas.SetLeft(flash, origin.X - fs / 2);
            Canvas.SetTop(flash, origin.Y - fs / 2);
            ScaleTransform fscale = new ScaleTransform(0.35, 0.35);
            flash.RenderTransform = fscale;
            layer.Children.Add(flash);
            made.Add(flash);
            fscale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(2.1, 340, out8));
            fscale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(2.1, 340, out8));
            flash.BeginAnimation(UIElement.OpacityProperty, Anim(0, 360, out8));

            // 礼花筒本体：斜举的小纸筒，喷完就收
            int tube = (int)Math.Round(25 * power);
            int mouth = (int)Math.Round(10 * power);
            int tail = (int)Math.Round(3.2 * power);
            Path cone = new Path();
            cone.Data = Geometry.Parse("M " + tube + ",-" + mouth + " L " + tube + "," + mouth
                + " L 0," + tail + " L 0,-" + tail + " Z");
            cone.Fill = Theme.B(Theme.Alpha(palette[0], 0.3));
            cone.Stroke = Theme.B(Theme.Alpha(palette[1], 0.8));
            cone.StrokeThickness = 1.2;
            cone.StrokeLineJoin = PenLineJoin.Round;
            cone.IsHitTestVisible = false;
            cone.RenderTransformOrigin = new Point(1, 0.5);     // 以筒口为轴心
            Canvas.SetLeft(cone, origin.X - tube);
            Canvas.SetTop(cone, origin.Y - mouth);
            ScaleTransform csc = new ScaleTransform(1, 1);
            RotateTransform crot = new RotateTransform(aim);
            TranslateTransform ctr = new TranslateTransform(0, 0);
            TransformGroup ctg = new TransformGroup();
            ctg.Children.Add(csc);
            ctg.Children.Add(crot);
            ctg.Children.Add(ctr);
            cone.RenderTransform = ctg;
            layer.Children.Add(cone);
            made.Add(cone);
            double kick = 9 * power;
            double kickAng = aim * Math.PI / 180;
            csc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 150, out8));
            csc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 150, out8));
            ctr.BeginAnimation(TranslateTransform.XProperty, Anim(-Math.Cos(kickAng) * kick, 430, out8));
            ctr.BeginAnimation(TranslateTransform.YProperty, Anim(-Math.Sin(kickAng) * kick, 430, out8));
            cone.BeginAnimation(UIElement.OpacityProperty, Anim(0.95, 70, out8));
            DoubleAnimation coneGone = Anim(0, 240, out8);
            coneGone.BeginTime = TimeSpan.FromMilliseconds(330);
            cone.BeginAnimation(UIElement.OpacityProperty, coneGone);

            // 彩纸、彩带
            for (int i = 0; i < count; i++)
            {
                double roll = Rnd.NextDouble();
                FrameworkElement el;
                if (roll < 0.28)
                {
                    double d = (5.2 + Rnd.NextDouble() * 3.0) * power;
                    Ellipse dot = new Ellipse();
                    dot.Width = d;
                    dot.Height = d;
                    el = dot;
                }
                else if (roll < 0.74)
                {
                    Rectangle chip = new Rectangle();
                    chip.Width = (4.0 + Rnd.NextDouble() * 2.4) * power;
                    chip.Height = (8.4 + Rnd.NextDouble() * 5.0) * power;
                    chip.RadiusX = 1.6;
                    chip.RadiusY = 1.6;
                    el = chip;
                }
                else
                {
                    Path ribbon = new Path();
                    ribbon.Data = Ribbon();
                    ribbon.StrokeThickness = 2.6 * power;
                    ribbon.StrokeStartLineCap = PenLineCap.Round;
                    ribbon.StrokeEndLineCap = PenLineCap.Round;
                    ribbon.Width = 15.0 * power;
                    ribbon.Height = 7.6 * power;
                    ribbon.Stretch = Stretch.Fill;
                    el = ribbon;
                }

                Brush brush = Theme.B(palette[Rnd.Next(palette.Length)]);
                Shape shape = el as Shape;
                if (shape != null) shape.Fill = brush;
                Path rb = el as Path;
                if (rb != null) { rb.Fill = null; rb.Stroke = brush; }

                el.IsHitTestVisible = false;
                el.RenderTransformOrigin = new Point(0.5, 0.5);

                double ang = (aim + (Rnd.NextDouble() - 0.5) * (Spread * 2)) * Math.PI / 180;
                double dist = radius * (0.34 + Rnd.NextDouble() * 0.72);
                double dx = Math.Cos(ang) * dist;
                double dyPeak = Math.Sin(ang) * dist * 0.78;
                double fall = (28 + Rnd.NextDouble() * 54) * power;
                double spin = (Rnd.NextDouble() < 0.5 ? -1 : 1) * (200 + Rnd.NextDouble() * 620);

                Canvas.SetLeft(el, origin.X - el.Width / 2);
                Canvas.SetTop(el, origin.Y - el.Height / 2);

                RotateTransform rot = new RotateTransform(0);
                TranslateTransform tr = new TranslateTransform(0, 0);
                TransformGroup tg = new TransformGroup();
                tg.Children.Add(rot);
                tg.Children.Add(tr);
                el.RenderTransform = tg;
                layer.Children.Add(el);
                made.Add(el);

                // 横向：先快后慢（用缓一点的曲线，让彩纸撒得开）
                DoubleAnimation ax = Anim(dx, dur, out4);
                tr.BeginAnimation(TranslateTransform.XProperty, ax);

                // 纵向：抛上去，再落下来
                DoubleAnimationUsingKeyFrames ay = new DoubleAnimationUsingKeyFrames();
                ay.Duration = TimeSpan.FromMilliseconds(dur);
                LinearDoubleKeyFrame k0 = new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0));
                EasingDoubleKeyFrame k1 = new EasingDoubleKeyFrame(dyPeak, KeyTime.FromPercent(0.42));
                k1.EasingFunction = out8;
                EasingDoubleKeyFrame k2 = new EasingDoubleKeyFrame(dyPeak + fall, KeyTime.FromPercent(1));
                k2.EasingFunction = in6;
                ay.KeyFrames.Add(k0);
                ay.KeyFrames.Add(k1);
                ay.KeyFrames.Add(k2);
                tr.BeginAnimation(TranslateTransform.YProperty, ay);

                // 翻转
                DoubleAnimation ar = Anim(spin, dur, null);
                rot.BeginAnimation(RotateTransform.AngleProperty, ar);

                // 最后 35% 淡出
                DoubleAnimation fade = Anim(0, (int)(dur * 0.35), in6);
                fade.BeginTime = TimeSpan.FromMilliseconds(dur * 0.65);
                el.BeginAnimation(UIElement.OpacityProperty, fade);
            }

            // 鼓励的话
            if (!string.IsNullOrEmpty(cheer))
            {
                int msgLife = CheerPill(layer, origin, cheer, made);
                if (msgLife > life) life = msgLife;
            }

            ScheduleCleanup(layer, made, life + 160);
        }

        /// <summary>飘一句贺辞：宣纸小笺从勾选框旁边弹出来、轻轻上浮，最后淡出。返回它的总时长。</summary>
        static int CheerPill(Canvas layer, Point origin, string text, List<UIElement> made)
        {
            Grid pill = new Grid();
            ColumnDefinition tailCol = new ColumnDefinition();
            tailCol.Width = new GridLength(11);
            pill.ColumnDefinitions.Add(tailCol);
            pill.ColumnDefinitions.Add(new ColumnDefinition());

            Path tail = new Path();
            tail.Data = Tail();
            tail.Stretch = Stretch.Fill;
            tail.Width = 11;
            tail.Height = 16;
            tail.Fill = Theme.B(Theme.Ivory);
            tail.Stroke = Theme.B(Theme.IvoryLine);
            tail.StrokeThickness = 1.1;
            tail.VerticalAlignment = VerticalAlignment.Center;
            tail.Margin = new Thickness(0, 7, 0, 0);
            pill.Children.Add(tail);

            Border note = Ui.Round(8, Theme.B(Theme.Ivory), Theme.B(Theme.IvoryLine), 1.3);
            note.Padding = new Thickness(12, 7, 13, 8);
            StackPanel lines = new StackPanel();
            TextBlock main = Ui.Txt(text, 14.5, Theme.B(Theme.IvoryInk), true);
            main.FontFamily = Theme.FontTitle;
            main.TextWrapping = TextWrapping.Wrap;
            lines.Children.Add(main);
            TextBlock sign = Ui.Txt(CheerSign, 10.5, Theme.B(Theme.TextFaint), false);
            sign.HorizontalAlignment = HorizontalAlignment.Right;
            sign.Margin = new Thickness(0, 3, 0, 0);
            lines.Children.Add(sign);
            note.Child = lines;
            Grid.SetColumn(note, 1);
            pill.Children.Add(note);

            pill.IsHitTestVisible = false;
            pill.RenderTransformOrigin = new Point(0.5, 0.5);
            pill.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            double w = pill.DesiredSize.Width;
            double h = pill.DesiredSize.Height;
            double lw = layer.ActualWidth;
            double lh = layer.ActualHeight;

            double x = origin.X - w / 2;
            if (lw > 1)
            {
                if (lw < 460) x = (lw - w) / 2;                 // 插件这种窄面板：居中更好看
                else x = origin.X + 14;                         // 宽面板：贴着勾选框，像从它冒出来
                if (x + w > lw - 8) x = lw - 8 - w;
                if (x < 8) x = 8;
            }
            double y = origin.Y - h - 34;
            if (lh > 1 && y < 8) y = origin.Y + 30;
            Canvas.SetLeft(pill, x);
            Canvas.SetTop(pill, y);

            ScaleTransform sc = new ScaleTransform(0.7, 0.7);
            TranslateTransform tr = new TranslateTransform(0, 10);
            TransformGroup tg = new TransformGroup();
            tg.Children.Add(sc);
            tg.Children.Add(tr);
            pill.RenderTransform = tg;
            layer.Children.Add(pill);
            made.Add(pill);

            BackEase pop = new BackEase();
            pop.EasingMode = EasingMode.EaseOut;
            pop.Amplitude = 0.7;

            sc.BeginAnimation(ScaleTransform.ScaleXProperty, Anim(1, 320, pop));
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, Anim(1, 320, pop));
            tr.BeginAnimation(TranslateTransform.YProperty, Anim(-12, 420, EaseOut()));

            DoubleAnimation appear = Anim(1, 130, EaseOut());
            pill.BeginAnimation(UIElement.OpacityProperty, appear);

            int life = 1250;
            DoubleAnimation gone = Anim(0, 340, EaseOut());
            gone.BeginTime = TimeSpan.FromMilliseconds(life - 340);
            pill.BeginAnimation(UIElement.OpacityProperty, gone);
            return life;
        }

        static IEasingFunction _out;
        static IEasingFunction EaseOut()
        {
            if (_out == null)
            {
                CubicEase e = new CubicEase();
                e.EasingMode = EasingMode.EaseOut;
                _out = e;
            }
            return _out;
        }

        static DoubleAnimation Anim(double to, int ms, IEasingFunction ease)
        {
            DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            if (ease != null) a.EasingFunction = ease;
            return a;
        }

        /// <summary>动画放完就把这些元素摘掉并停掉动画，别让它们一直挂在可视树上。</summary>
        static void ScheduleCleanup(Canvas layer, List<UIElement> made, int afterMs)
        {
            DispatcherTimer t = new DispatcherTimer(DispatcherPriority.Normal);
            t.Interval = TimeSpan.FromMilliseconds(afterMs);
            t.Tick += delegate(object s, EventArgs e)
            {
                t.Stop();
                for (int i = 0; i < made.Count; i++)
                {
                    UIElement el = made[i];
                    el.BeginAnimation(UIElement.OpacityProperty, null);
                    TransformGroup tg = el.RenderTransform as TransformGroup;
                    if (tg != null)
                    {
                        for (int k = 0; k < tg.Children.Count; k++) Stop(tg.Children[k]);
                    }
                    else
                    {
                        Stop(el.RenderTransform);
                    }
                    layer.Children.Remove(el);
                }
                made.Clear();
            };
            t.Start();
        }

        static void Stop(Transform t)
        {
            RotateTransform ro = t as RotateTransform;
            if (ro != null)
            {
                ro.BeginAnimation(RotateTransform.AngleProperty, null);
                return;
            }
            TranslateTransform tr = t as TranslateTransform;
            if (tr != null)
            {
                tr.BeginAnimation(TranslateTransform.XProperty, null);
                tr.BeginAnimation(TranslateTransform.YProperty, null);
                return;
            }
            ScaleTransform sc = t as ScaleTransform;
            if (sc != null)
            {
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            }
        }
    }
}
