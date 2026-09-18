using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TimePlanner.Core
{
    /// <summary>圆形勾选框：点击切换完成状态，带缩放/颜色动画。</summary>
    public class CircleCheck : Border
    {
        readonly Ellipse ring;
        readonly Path mark;
        readonly ScaleTransform markScale;
        bool _done;

        public string TaskId;
        public Action Toggled;

        public CircleCheck(double size)
        {
            Width = size + 8;
            Height = size + 8;
            Background = Theme.B(Colors.Transparent);
            Cursor = Cursors.Hand;

            double d = size;
            ring = new Ellipse();
            ring.Width = d;
            ring.Height = d;
            ring.StrokeThickness = 1.6;
            ring.Stroke = Theme.B(Theme.BorderHi);
            ring.Fill = Theme.B(Theme.PanelSoft);
            ring.HorizontalAlignment = HorizontalAlignment.Center;
            ring.VerticalAlignment = VerticalAlignment.Center;

            markScale = new ScaleTransform(0.4, 0.4);
            mark = Ui.IconPath("check", d * 0.62, Theme.B(Theme.OnAccent), 2.0);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Opacity = 0;
            mark.RenderTransformOrigin = new Point(0.5, 0.5);
            mark.RenderTransform = markScale;

            Grid g = new Grid();
            g.Background = Theme.B(Colors.Transparent);
            g.Children.Add(ring);
            g.Children.Add(mark);
            Child = g;

            MouseEnter += delegate(object s, MouseEventArgs e) { if (!_done) ring.Stroke = Theme.B(Theme.Accent); };
            MouseLeave += delegate(object s, MouseEventArgs e) { if (!_done) ring.Stroke = Theme.B(Theme.BorderHi); };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (Toggled != null) Toggled();
            };
        }

        public bool IsDone { get { return _done; } }

        public void SetDone(bool done, bool animate)
        {
            _done = done;
            Color target = done ? Theme.Accent : Theme.PanelSoft;
            Color stroke = done ? Theme.Accent : Theme.BorderHi;
            int ms = animate ? 220 : 0;

            ColorAnimation fillA = new ColorAnimation(target, TimeSpan.FromMilliseconds(ms));
            ring.Fill = new SolidColorBrush(done ? Theme.Accent : Theme.PanelSoft);
            ring.Fill.BeginAnimation(SolidColorBrush.ColorProperty, fillA);

            ColorAnimation strokeA = new ColorAnimation(stroke, TimeSpan.FromMilliseconds(ms));
            ring.Stroke = new SolidColorBrush(stroke);
            ring.Stroke.BeginAnimation(SolidColorBrush.ColorProperty, strokeA);

            DoubleAnimation fade = new DoubleAnimation(done ? 1 : 0, TimeSpan.FromMilliseconds(ms));
            mark.BeginAnimation(UIElement.OpacityProperty, fade);

            double to = done ? 1 : 0.4;
            BackEase ease = new BackEase();
            ease.EasingMode = EasingMode.EaseOut;
            ease.Amplitude = 0.5;
            DoubleAnimation sx = new DoubleAnimation(to, TimeSpan.FromMilliseconds(animate ? 320 : 0));
            sx.EasingFunction = ease;
            DoubleAnimation sy = new DoubleAnimation(to, TimeSpan.FromMilliseconds(animate ? 320 : 0));
            sy.EasingFunction = ease;
            markScale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            markScale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }
    }

    /// <summary>圆角进度条。</summary>
    public class MiniBar : Border
    {
        readonly Border fill;
        double _ratio;

        public MiniBar(double height, Color barColor)
        {
            Height = height;
            CornerRadius = new CornerRadius(height / 2);
            Background = Theme.B(Theme.Blend(Theme.PanelHi, Theme.Text, 0.10));
            ClipToBounds = true;
            fill = new Border();
            fill.CornerRadius = new CornerRadius(height / 2);
            fill.Background = Theme.B(barColor);
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fill.Width = 0;
            Child = fill;
            SizeChanged += delegate(object s, SizeChangedEventArgs e) { Apply(false); };
        }

        public double Ratio { get { return _ratio; } }

        public void SetRatio(double r, bool animate)
        {
            _ratio = Math.Max(0, Math.Min(1, r));
            Apply(animate);
        }

        void Apply(bool animate)
        {
            double w = ActualWidth * _ratio;
            if (w <= 0) w = 0.0001;
            if (animate && ActualWidth > 1)
            {
                DoubleAnimation a = new DoubleAnimation(w, TimeSpan.FromMilliseconds(320));
                CubicEase e = new CubicEase();
                e.EasingMode = EasingMode.EaseOut;
                a.EasingFunction = e;
                fill.BeginAnimation(FrameworkElement.WidthProperty, a);
            }
            else
            {
                fill.BeginAnimation(FrameworkElement.WidthProperty, null);
                fill.Width = w;
            }
        }
    }

    /// <summary>带占位提示的输入框，回车提交。</summary>
    public class HintBox : Grid
    {
        public readonly TextBox Box;
        readonly TextBlock hint;
        public Action<string> Submitted;

        public HintBox(string watermark, double fontSize)
        {
            Background = Theme.B(Colors.Transparent);
            hint = Ui.Txt(watermark, fontSize, Theme.B(Theme.TextFaint), false);
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.IsHitTestVisible = false;
            hint.Margin = new Thickness(2, 0, 0, 0);

            Box = new TextBox();
            Box.Background = Theme.B(Colors.Transparent);
            Box.Foreground = Theme.B(Theme.Text);
            Box.CaretBrush = Theme.B(Theme.Accent);
            Box.BorderThickness = new Thickness(0);
            Box.FontFamily = Theme.Font;
            Box.FontSize = fontSize;
            Box.Padding = new Thickness(0);
            Box.VerticalContentAlignment = VerticalAlignment.Center;
            Box.SelectionBrush = Theme.B(Theme.AccentSoft);

            Children.Add(hint);
            Children.Add(Box);
            Box.TextChanged += delegate(object s, TextChangedEventArgs e) { hint.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            Box.GotFocus += delegate(object s, RoutedEventArgs e) { hint.Visibility = Visibility.Collapsed; };
            Box.LostFocus += delegate(object s, RoutedEventArgs e) { hint.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    string text = Box.Text;
                    Box.Text = "";
                    e.Handled = true;
                    if (Submitted != null && text != null && text.Trim().Length > 0) Submitted(text.Trim());
                }
                else if (e.Key == Key.Escape)
                {
                    Box.Text = "";
                    Keyboard.ClearFocus();
                }
            };
        }

        public void FocusInput()
        {
            Box.Focus();
            Box.CaretIndex = Box.Text.Length;
        }
    }

    /// <summary>任务行（主程序与桌面插件共用）。</summary>
    public class TaskRow : Border
    {
        public readonly TaskItem Item;
        public Action<TaskItem> ToggleRequested;
        public Action<TaskItem> EditRequested;
        public Action<TaskItem> DeleteRequested;
        public Action<TaskItem> DeferRequested;
        public Action<TaskItem> DoubleClickRequested;
        public Action<TaskRow, TaskItem> DragRequested;
        Point dragStart;
        bool dragArmed;

        readonly CircleCheck check;
        readonly TextBlock title;
        readonly Border bar;
        readonly StackPanel actions;
        readonly bool compact;
        readonly Border tagPill;
        readonly Border deferButton;
        readonly Grid root;

        public TaskRow(TaskItem item, bool compact)
        {
            Item = item;
            this.compact = compact;
            CornerRadius = new CornerRadius(compact ? 9 : 11);
            Background = Theme.B(compact ? Theme.Panel : Theme.PanelSoft);
            Padding = new Thickness(compact ? 6 : 8, compact ? 6 : 8, compact ? 8 : 10, compact ? 6 : 8);

            root = new Grid();
            ColumnDefinition cBar = new ColumnDefinition();
            cBar.Width = GridLength.Auto;
            root.ColumnDefinitions.Add(cBar);
            ColumnDefinition cCheck = new ColumnDefinition();
            cCheck.Width = GridLength.Auto;
            root.ColumnDefinitions.Add(cCheck);
            ColumnDefinition c = new ColumnDefinition(); c.Width = new GridLength(1, GridUnitType.Star);
            root.ColumnDefinitions.Add(c);
            ColumnDefinition a = new ColumnDefinition();
            a.Width = GridLength.Auto;
            root.ColumnDefinitions.Add(a);

            bar = new Border();
            bar.Width = 3;
            bar.CornerRadius = new CornerRadius(2);
            bar.VerticalAlignment = VerticalAlignment.Stretch;
            bar.Margin = new Thickness(0, 1, 0, 1);
            Grid.SetColumn(bar, 0);
            root.Children.Add(bar);

            check = new CircleCheck(compact ? 15 : 17);
            check.TaskId = item.Id;
            check.VerticalAlignment = VerticalAlignment.Center;
            check.Margin = new Thickness(4, 0, compact ? 4 : 7, 0);
            check.Toggled = delegate() { if (ToggleRequested != null) ToggleRequested(Item); };
            Grid.SetColumn(check, 1);
            root.Children.Add(check);

            StackPanel content = new StackPanel();
            content.VerticalAlignment = VerticalAlignment.Center;
            content.Margin = new Thickness(0, 1, 0, 1);
            title = Ui.Txt(item.Title, compact ? 12.5 : 14, Theme.B(Theme.Text), false);
            title.TextWrapping = TextWrapping.Wrap;
            content.Children.Add(title);

            StackPanel meta = new StackPanel();
            meta.Orientation = Orientation.Horizontal;
            meta.Margin = new Thickness(0, 4, 0, 0);
            tagPill = Ui.Pill("", Theme.Accent, Theme.AccentSoft);
            tagPill.Margin = new Thickness(0, 0, 6, 0);
            meta.Children.Add(tagPill);
            content.Children.Add(meta);
            Grid.SetColumn(content, 2);
            root.Children.Add(content);

            actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.VerticalAlignment = VerticalAlignment.Center;
            actions.Opacity = 0;
            if (!compact)
            {
                actions.Children.Add(Ui.IconButton("pencil", 26, 13, delegate() { if (EditRequested != null) EditRequested(Item); }, "编辑"));
                deferButton = Ui.IconButton("arrow-right", 26, 13, delegate() { if (DeferRequested != null) DeferRequested(Item); }, "推到明天");
                actions.Children.Add(deferButton);
                actions.Children.Add(Ui.IconButton("trash", 26, 13, delegate() { if (DeleteRequested != null) DeleteRequested(Item); }, "删除"));
            }
            Grid.SetColumn(actions, 3);
            root.Children.Add(actions);

            Child = root;

            MouseEnter += delegate(object s, MouseEventArgs e)
            {
                Background = Theme.B(Item.Done ? Theme.PanelHi : Theme.Panel);
                Ui.AnimateOpacity(actions, 1, 140);
            };
            MouseLeave += delegate(object s, MouseEventArgs e)
            {
                Background = Theme.B(compact ? Theme.Panel : Theme.PanelSoft);
                Ui.AnimateOpacity(actions, 0, 140);
            };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                dragArmed = false;
                if (e.ClickCount >= 2 && DoubleClickRequested != null) DoubleClickRequested(Item);
            };
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (IsNoDrag(e.OriginalSource as DependencyObject)) { dragArmed = false; return; }
                dragStart = e.GetPosition(this);
                dragArmed = true;
            };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
                Point p = e.GetPosition(this);
                if (Math.Abs(p.X - dragStart.X) < 7 && Math.Abs(p.Y - dragStart.Y) < 7) return;
                dragArmed = false;
                if (DragRequested != null) DragRequested(this, Item);
            };

            Refresh(false);
        }

        /// <summary>勾选框中心相对于某个可视元素的位置（完成时在那放小烟火）。</summary>
        public Point CheckCenter(Visual relativeTo)
        {
            try
            {
                if (check != null && check.ActualWidth > 0.5)
                    return check.TransformToVisual(relativeTo).Transform(new Point(check.ActualWidth / 2, check.ActualHeight / 2));
                return TransformToVisual(relativeTo).Transform(new Point(22, ActualHeight / 2));
            }
            catch (Exception)
            {
                return new Point(0, 0);
            }
        }

        /// <summary>勾选框、按钮上不触发拖动。</summary>
        static bool IsNoDrag(DependencyObject src)
        {
            while (src != null)
            {
                if (src is CircleCheck) return true;
                if (src is Border && ((Border)src).Cursor == Cursors.Hand) return true;
                src = VisualTreeHelper.GetParent(src);
            }
            return false;
        }

        public void Refresh(bool animate)        {
            check.SetDone(Item.Done, animate);
            title.Text = Item.Title;
            if (Item.Done)
            {
                title.TextDecorations = TextDecorations.Strikethrough;
                title.Foreground = Theme.B(Theme.TextFaint);
                title.FontWeight = FontWeights.Normal;
            }
            else
            {
                title.TextDecorations = null;
                title.Foreground = Theme.B(Item.Priority > 0 ? Theme.Text : Theme.Text);
                title.FontWeight = Item.Priority >= PriorityLevel.Urgent ? FontWeights.SemiBold : FontWeights.Normal;
            }
            if (deferButton != null) deferButton.Visibility = Item.Done ? Visibility.Collapsed : Visibility.Visible;
            bar.Background = Theme.B(Item.Priority > 0 ? Theme.PriorityColor(Item.Priority) : Theme.Alpha(Theme.BorderHi, 0.7));
            bar.Width = Item.Priority > 0 ? 3 : 2;

            if (Item.Tag != null && Item.Tag.Length > 0)
            {
                tagPill.Visibility = Visibility.Visible;
                ((TextBlock)tagPill.Child).Text = Item.Tag;
            }
            else
            {
                tagPill.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>分段选择器。</summary>
    public class Segment : Border
    {
        readonly Border[] cells;
        readonly string[] labels;
        int _index;
        public Action<int> Selected;

        public Segment(string[] labels, int index, Action<int> onSelect)
        {
            this.labels = labels;
            _index = index;
            Selected = onSelect;
            CornerRadius = new CornerRadius(9);
            Background = Theme.B(Theme.PanelSoft);
            BorderBrush = Theme.B(Theme.Border);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(3);
            HorizontalAlignment = HorizontalAlignment.Left;

            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            cells = new Border[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                Border b = Ui.Round(7, Theme.Transparent);
                b.Padding = new Thickness(11, 5, 11, 6);
                b.Margin = new Thickness(i == 0 ? 0 : 2, 0, 0, 0);
                b.Cursor = Cursors.Hand;
                b.Child = Ui.Txt(labels[i], 12.5, Theme.B(Theme.TextMuted), false);
                b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    SetIndex(idx, true);
                };
                cells[i] = b;
                sp.Children.Add(b);
            }
            Child = sp;
            Paint();
        }

        public int Index { get { return _index; } }

        public void SetIndex(int i, bool notify)
        {
            if (i < 0 || i >= cells.Length) return;
            _index = i;
            Paint();
            if (notify && Selected != null) Selected(i);
        }

        void Paint()
        {
            for (int i = 0; i < cells.Length; i++)
            {
                bool on = i == _index;
                cells[i].Background = Theme.B(on ? Theme.Accent : Colors.Transparent);
                TextBlock t = (TextBlock)cells[i].Child;
                t.Foreground = Theme.B(on ? Theme.OnAccent : Theme.TextMuted);
                t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
    }
}
