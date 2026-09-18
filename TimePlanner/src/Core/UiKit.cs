using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace TimePlanner.Core
{
    /// <summary>通用 UI 小工具：圆角、图标、可点击区域、滚动条样式等。</summary>
    public static class Ui
    {
        public static readonly double Radius = 12;

        public static Border Round(double radius, Brush fill, Brush stroke, double thickness)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(radius);
            b.Background = fill;
            if (stroke != null)
            {
                b.BorderBrush = stroke;
                b.BorderThickness = new Thickness(thickness);
            }
            return b;
        }

        public static Border Round(double radius, Brush fill)
        {
            return Round(radius, fill, null, 0);
        }


        /// <summary>给任意 Border 增加点击/hover 行为。</summary>
        public static Border Click(Border b, Action onClick, Brush hoverBg, Brush normalBg)
        {
            if (hoverBg == null) hoverBg = Theme.B(Theme.PanelHi);
            b.Cursor = Cursors.Hand;
            bool inside = false;
            bool down = false;
            b.MouseEnter += delegate(object s, MouseEventArgs e)
            {
                inside = true;
                if (b.Background != null) b.Background = hoverBg;
            };
            b.MouseLeave += delegate(object s, MouseEventArgs e)
            {
                inside = false;
                down = false;
                if (b.Background != null) b.Background = normalBg;
            };
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                down = true;
                b.CaptureMouse();
            };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                bool was = down;
                down = false;
                try { b.ReleaseMouseCapture(); } catch (Exception) { }
                if (was && inside && onClick != null)
                {
                    e.Handled = true;
                    onClick();
                }
            };
            return b;
        }

        public static TextBlock Txt(string text, double size, Brush brush, bool bold)
        {
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = size;
            t.Foreground = brush;
            t.FontFamily = Theme.Font;
            t.TextWrapping = TextWrapping.NoWrap;
            if (bold) t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        public static void Tip(DependencyObject el, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Border box = Round(7, Theme.B("#0A0C11"), Theme.B(Theme.Border), 1);
            box.Padding = new Thickness(9, 5, 9, 6);
            box.Margin = new Thickness(0, 0, 0, 4);
            box.Child = Txt(text, 12, Theme.B(Theme.Text), false);
            ToolTip tp = new ToolTip();
            tp.Content = box;
            tp.Background = Brushes.Transparent;
            tp.BorderThickness = new Thickness(0);
            tp.Padding = new Thickness(0);
            tp.HasDropShadow = false;
            ToolTipService.SetInitialShowDelay(box, 250);
            ToolTipService.SetShowDuration(box, 12000);
            ToolTipService.SetToolTip(el, tp);
        }

        /// <summary>在可视树里找第一个指定类型的元素。</summary>
        public static T Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            T hit = root as T;
            if (hit != null) return hit;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                T found = Find<T>(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>元素中心相对某个可视元素的位置（当动画爆点用）。</summary>
        public static Point CenterOf(FrameworkElement el, Visual relativeTo)
        {
            try
            {
                return el.TransformToVisual(relativeTo).Transform(new Point(el.ActualWidth / 2, el.ActualHeight / 2));
            }
            catch (Exception)
            {
                return new Point(0, 0);
            }
        }

        public static readonly Dictionary<string, Geometry> IconCache = new Dictionary<string, Geometry>();

        public static Geometry Icon(string key)
        {
            Geometry g;
            if (IconCache.TryGetValue(key, out g)) return g;
            string data;
            switch (key)
            {
                case "check": data = "M 2.2,7.4 L 5.6,10.8 L 11.8,3.4"; break;
                case "plus": data = "M 7,1.6 L 7,12.4 M 1.6,7 L 12.4,7"; break;
                case "close": data = "M 2.4,2.4 L 11.6,11.6 M 11.6,2.4 L 2.4,11.6"; break;
                case "pencil": data = "M 1.6,12.4 L 1.6,9.4 L 9.4,1.6 L 12.4,4.6 L 4.6,12.4 Z"; break;
                case "trash": data = "M 1.8,3.8 L 12.2,3.8 M 5.2,3.8 L 5.2,1.8 L 8.8,1.8 L 8.8,3.8 M 3.4,3.8 L 4.1,13 L 9.9,13 L 10.6,3.8"; break;
                case "left": data = "M 9.2,2.4 L 4,7 L 9.2,11.6"; break;
                case "right": data = "M 4.8,2.4 L 10,7 L 4.8,11.6"; break;
                case "down": data = "M 2.4,4.8 L 7,9.4 L 11.6,4.8"; break;
                case "up": data = "M 2.4,9.4 L 7,4.8 L 11.6,9.4"; break;
                case "calendar": data = "M 1.8,3.4 L 12.2,3.4 L 12.2,12.6 L 1.8,12.6 Z M 1.8,6.2 L 12.2,6.2 M 4.6,1.4 L 4.6,4 M 9.4,1.4 L 9.4,4"; break;
                case "clock": data = "M 7,1.6 A 5.4,5.4 0 1 0 7.01,1.6 Z M 7,4 L 7,7.4 L 9.4,9"; break;
                case "arrow-right": data = "M 2,7 L 11,7 M 7.4,3.4 L 11,7 L 7.4,10.6"; break;
                case "arrow-left": data = "M 12,7 L 3,7 M 6.6,3.4 L 3,7 L 6.6,10.6"; break;
                case "gear": data = "M 2,4.2 L 12,4.2 M 2,7 L 12,7 M 2,9.8 L 12,9.8 M 5.2,2.9 A 1.3,1.3 0 1 0 5.21,2.9 Z M 9.2,5.7 A 1.3,1.3 0 1 0 9.21,5.7 Z M 5.6,9.8 A 1.3,1.3 0 1 0 5.61,9.8 Z"; break;
                case "layers": data = "M 1.6,7 L 7,9.8 L 12.4,7 M 1.6,4.4 L 7,1.6 L 12.4,4.4 M 1.8,10.4 L 7,13 L 12.2,10.4"; break;
                case "pin": data = "M 7,1.4 L 7,6.2 M 3.6,6.2 L 10.4,6.2 L 10.4,10.2 L 3.6,10.2 Z M 7,10.2 L 7,13"; break;
                case "desktop": data = "M 1.4,2.6 L 12.6,2.6 L 12.6,9.8 L 1.4,9.8 Z M 4.6,12.6 L 9.4,12.6 M 7,9.8 L 7,12.6"; break;
                case "winmin": data = "M 2.4,7 L 11.6,7"; break;
                case "winmax": data = "M 2.6,2.6 L 11.4,2.6 L 11.4,11.4 L 2.6,11.4 Z"; break;
                case "list": data = "M 3,3.6 L 3.01,3.6 M 6,3.6 L 12.6,3.6 M 3,7 L 3.01,7 M 6,7 L 12.6,7 M 3,10.4 L 3.01,10.4 M 6,10.4 L 12.6,10.4"; break;
                case "refresh": data = "M 12,7.4 A 5,5 0 1 1 10.6,3.9 M 11.4,1.4 L 11.4,4.4 L 8.4,4.4"; break;
                case "flag": data = "M 3,1.6 L 3,13 M 3,2.4 L 11.4,2.4 L 9.4,5 L 11.4,7.6 L 3,7.6"; break;
                case "eye": data = "M 1.2,7 C 3.6,3.6 10.4,3.6 12.8,7 C 10.4,10.4 3.6,10.4 1.2,7 Z M 7,5.4 A 1.6,1.6 0 1 0 7.01,5.4 Z"; break;
                case "expand": data = "M 2.6,8.4 L 2.6,12.4 L 6.6,12.4 M 12.4,6.6 L 12.4,2.6 L 8.4,2.6 M 2.6,12.4 L 6.4,8.6 M 12.4,2.6 L 8.6,6.4"; break;
                default: data = "M 2,2 L 12,2 L 12,12 L 2,12 Z"; break;
            }
            g = Geometry.Parse(data);
            g.Freeze();
            IconCache[key] = g;
            return g;
        }

        public static Path IconPath(string key, double size, Brush stroke, double thickness)
        {
            Path p = new Path();
            p.Data = Icon(key);
            p.Stroke = stroke;
            p.StrokeThickness = thickness;
            p.StrokeStartLineCap = PenLineCap.Round;
            p.StrokeEndLineCap = PenLineCap.Round;
            p.StrokeLineJoin = PenLineJoin.Round;
            p.Fill = null;
            p.Width = size;
            p.Height = size;
            p.Stretch = Stretch.Uniform;
            p.SnapsToDevicePixels = true;
            return p;
        }

        /// <summary>图标按钮（圆角小方块 + hover）。</summary>
        public static Border IconButton(string icon, double box, double iconSize, Action onClick, string tip)
        {
            Border b = Round(box / 3.2, Theme.Transparent);
            b.Width = box;
            b.Height = box;
            Path p = IconPath(icon, iconSize, Theme.B(Theme.TextMuted), 1.4);
            p.HorizontalAlignment = HorizontalAlignment.Center;
            p.VerticalAlignment = VerticalAlignment.Center;
            b.Child = p;
            Click(b, onClick, Theme.B(Theme.PanelHi), Theme.Transparent);
            b.MouseEnter += delegate(object s, MouseEventArgs e) { p.Stroke = Theme.B(Theme.Text); };
            b.MouseLeave += delegate(object s, MouseEventArgs e) { p.Stroke = Theme.B(Theme.TextMuted); };
            if (tip != null) Tip(b, tip);
            return b;
        }

        /// <summary>文字按钮。</summary>
        public static Border TextButton(string text, Action onClick, bool primary)
        {
            Border b = Round(9, primary ? Theme.B(Theme.Accent) : Theme.B(Theme.PanelHi),
                primary ? null : Theme.B(Theme.Border), 1);
            b.Padding = new Thickness(13, 7, 13, 8);
            b.Child = Txt(text, 13, primary ? Theme.B(Theme.OnAccent) : Theme.B(Theme.Text), true);
            b.Cursor = Cursors.Hand;
            if (primary) Click(b, onClick, Theme.B(Theme.AccentDeep), Theme.B(Theme.Accent));
            else Click(b, onClick, Theme.B(Theme.Border), Theme.B(Theme.PanelHi));
            return b;
        }

        /// <summary>小圆点/胶囊标签。</summary>
        public static Border Pill(string text, Color fg, Color bg)
        {
            Border b = Round(6, Theme.B(bg));
            b.Padding = new Thickness(6, 1.5, 6, 2.5);
            b.VerticalAlignment = VerticalAlignment.Center;
            b.Child = Txt(text, 11, Theme.B(fg), false);
            return b;
        }

        public static Border Chip(string text, bool active, Action onClick, Color accent)
        {
            Border b = Round(8, active ? Theme.B(Theme.Alpha(accent, 0.20)) : Theme.B(Theme.PanelSoft),
                active ? Theme.B(Theme.Alpha(accent, 0.55)) : Theme.B(Theme.Border), 1);
            b.Padding = new Thickness(10, 4.5, 10, 5.5);
            b.Child = Txt(text, 12, active ? Theme.B(accent) : Theme.B(Theme.TextMuted), active);
            b.Cursor = Cursors.Hand;
            Click(b, onClick, Theme.B(Theme.PanelHi), active ? Theme.B(Theme.Alpha(accent, 0.20)) : Theme.B(Theme.PanelSoft));
            return b;
        }

        public static ScrollViewer Scroll(UIElement child)
        {
            ScrollViewer sv = new ScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            sv.PanningMode = PanningMode.VerticalOnly;
            sv.Content = child;
            sv.Resources[typeof(ScrollBar)] = ScrollStyle();
            return sv;
        }

        static Style _scrollStyle;

        public static Style ScrollStyle()
        {
            if (_scrollStyle != null) return _scrollStyle;
            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ScrollBar}'>" +
                "<Setter Property='Width' Value='10'/>" +
                "<Setter Property='Margin' Value='3,0,0,0'/>" +
                "<Setter Property='Background' Value='Transparent'/>" +
                "<Setter Property='Template'><Setter.Value>" +
                "<ControlTemplate TargetType='{x:Type ScrollBar}'>" +
                "<Grid Background='Transparent'>" +
                "<Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'>" +
                "<Track.Thumb>" +
                "<Thumb Background='#3A4152'>" +
                "<Thumb.Template><ControlTemplate TargetType='{x:Type Thumb}'>" +
                "<Border CornerRadius='5' Margin='2,0,2,0' Background='{TemplateBinding Background}'/>" +
                "</ControlTemplate></Thumb.Template>" +
                "</Thumb>" +
                "</Track.Thumb>" +
                "</Track>" +
                "</Grid>" +
                "</ControlTemplate>" +
                "</Setter.Value></Setter>" +
                "</Style>";
            _scrollStyle = (Style)System.Windows.Markup.XamlReader.Parse(xaml);
            return _scrollStyle;
        }
        public static DropShadowEffect Shadow(double blur, double opacity, double depth)
        {
            DropShadowEffect e = new DropShadowEffect();
            e.BlurRadius = blur;
            e.ShadowDepth = depth;
            e.Direction = 270;
            e.Opacity = opacity;
            e.Color = Colors.Black;
            return e;
        }

        public static void AnimateOpacity(UIElement el, double to, int ms)
        {
            DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            a.EasingFunction = new CubicEase();
            ((CubicEase)a.EasingFunction).EasingMode = EasingMode.EaseOut;
            el.BeginAnimation(UIElement.OpacityProperty, a);
        }
    }
}
