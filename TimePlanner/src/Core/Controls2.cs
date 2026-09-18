using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TimePlanner.Core
{
    /// <summary>开关。</summary>
    public class Switch : Border
    {
        readonly Border knob;
        readonly TranslateTransform shift;
        bool _on;
        public Action<bool> Changed;

        public Switch(bool on)
        {
            Width = 42;
            Height = 24;
            CornerRadius = new CornerRadius(12);
            Cursor = Cursors.Hand;
            Background = Theme.B(Theme.PanelHi);
            BorderThickness = new Thickness(1);
            BorderBrush = Theme.B(Theme.Border);

            shift = new TranslateTransform(0, 0);
            knob = new Border();
            knob.Width = 16;
            knob.Height = 16;
            knob.CornerRadius = new CornerRadius(8);
            knob.Background = Theme.B(Theme.TextMuted);
            knob.HorizontalAlignment = HorizontalAlignment.Left;
            knob.VerticalAlignment = VerticalAlignment.Center;
            knob.Margin = new Thickness(3, 0, 0, 0);
            knob.RenderTransform = shift;
            Child = knob;

            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Set(!_on, true);
                if (Changed != null) Changed(_on);
            };
            Set(on, false);
        }

        public bool IsOn { get { return _on; } }

        public void Set(bool on, bool animate)
        {
            _on = on;
            double to = on ? 17 : 0;
            int ms = animate ? 180 : 0;
            DoubleAnimation a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            CubicEase e = new CubicEase();
            e.EasingMode = EasingMode.EaseOut;
            a.EasingFunction = e;
            shift.BeginAnimation(TranslateTransform.XProperty, a);
            Background = Theme.B(on ? Theme.Accent : Theme.PanelHi);
            BorderBrush = Theme.B(on ? Theme.Accent : Theme.Border);
            knob.Background = Theme.B(on ? Colors.White : Theme.TextMuted);
        }
    }

    /// <summary>轻量滑杆（0..1）。</summary>
    public class SliderBar : Border
    {
        readonly Border fill;
        readonly Border knob;
        readonly TranslateTransform shift;
        double _value;
        readonly double trackWidth = 168;
        bool dragging;

        public Action<double, bool> Changed;

        public SliderBar(double value, double width)
        {
            trackWidth = width;
            Height = 22;
            Background = Theme.B(Colors.Transparent);
            Cursor = Cursors.Hand;
            Width = width;

            Border track = new Border();
            track.Height = 5;
            track.CornerRadius = new CornerRadius(3);
            track.Background = Theme.B(Theme.PanelHi);
            track.VerticalAlignment = VerticalAlignment.Center;
            track.Width = width;

            fill = new Border();
            fill.Height = 5;
            fill.CornerRadius = new CornerRadius(3);
            fill.Background = Theme.B(Theme.Accent);
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.VerticalAlignment = VerticalAlignment.Center;
            fill.Width = 0;

            shift = new TranslateTransform(0, 0);
            knob = new Border();
            knob.Width = 14;
            knob.Height = 14;
            knob.CornerRadius = new CornerRadius(7);
            knob.Background = Theme.B(Theme.Text);
            knob.HorizontalAlignment = HorizontalAlignment.Left;
            knob.VerticalAlignment = VerticalAlignment.Center;
            knob.RenderTransform = shift;

            Grid g = new Grid();
            g.Children.Add(track);
            g.Children.Add(fill);
            g.Children.Add(knob);
            Child = g;

            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                dragging = true;
                CaptureMouse();
                Apply(e.GetPosition(this).X / trackWidth, false);
            };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (dragging) Apply(e.GetPosition(this).X / trackWidth, false);
            };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (!dragging) return;
                dragging = false;
                ReleaseMouseCapture();
                Apply(e.GetPosition(this).X / trackWidth, true);
            };
            SetValue(value, false);
        }

        public double Value { get { return _value; } }

        void Apply(double v, bool final)
        {
            SetValue(v, false);
            if (Changed != null) Changed(_value, final);
        }

        public void SetValue(double v, bool animate)
        {
            _value = Math.Max(0, Math.Min(1, v));
            double w = trackWidth * _value;
            fill.Width = Math.Max(1, w);
            double knobX = w - knob.Width / 2;
            knobX = Math.Max(-2, Math.Min(trackWidth - knob.Width + 2, knobX));
            if (animate)
            {
                DoubleAnimation a = new DoubleAnimation(knobX, TimeSpan.FromMilliseconds(120));
                shift.BeginAnimation(TranslateTransform.XProperty, a);
            }
            else
            {
                shift.BeginAnimation(TranslateTransform.XProperty, null);
                shift.X = knobX;
            }
        }
    }

    public static class Cards
    {
        /// <summary>带标题的设置卡片。</summary>
        public static Border Panel(string title, string subtitle, UIElement content)
        {
            Border card = Ui.Round(14, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
            card.Padding = new Thickness(18, 16, 18, 18);
            card.Margin = new Thickness(0, 0, 0, 14);

            StackPanel sp = new StackPanel();
            if (title != null)
            {
                sp.Children.Add(Ui.Txt(title, 14.5, Theme.B(Theme.Text), true));
            }
            if (subtitle != null)
            {
                TextBlock sub = Ui.Txt(subtitle, 12, Theme.B(Theme.TextFaint), false);
                sub.Margin = new Thickness(0, 5, 0, 0);
                sub.TextWrapping = TextWrapping.Wrap;
                sp.Children.Add(sub);
            }
            if (content != null)
            {
                content.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 14, 0, 0));
                sp.Children.Add(content);
            }
            card.Child = sp;
            return card;
        }

        /// <summary>设置项一行：左标题 + 右控件。</summary>
        public static Grid Row(string label, string hint, UIElement control)
        {
            Grid g = new Grid();
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(1, GridUnitType.Star);
            g.ColumnDefinitions.Add(c0);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Margin = new Thickness(0, 0, 0, 12);

            StackPanel sp = new StackPanel();
            sp.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(Ui.Txt(label, 13, Theme.B(Theme.Text), false));
            if (hint != null)
            {
                TextBlock h = Ui.Txt(hint, 11.5, Theme.B(Theme.TextFaint), false);
                h.Margin = new Thickness(0, 3, 0, 0);
                h.TextWrapping = TextWrapping.Wrap;
                h.MaxWidth = 420;
                h.HorizontalAlignment = HorizontalAlignment.Left;
                sp.Children.Add(h);
            }
            Grid.SetColumn(sp, 0);
            g.Children.Add(sp);
            if (control != null)
            {
                control.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                Grid.SetColumn(control, 1);
                g.Children.Add(control);
            }
            return g;
        }
    }
}
