using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using TimePlanner.Core;

namespace TimePlanner.App
{
    /// <summary>编辑任务弹窗。</summary>
    public class EditDialog : Window
    {
        public TaskItem Result;
        public bool DeleteRequested;

        readonly TaskItem draft;
        readonly HintBox titleBox;
        readonly HintBox tagBox;
        readonly TextBox noteBox;
        readonly TextBlock dateText;
        readonly Border[] prioCells;
        int priority;
        DateTime day;
        readonly bool monday;

        public EditDialog(TaskItem task, bool monday)
        {
            draft = task;
            Result = task.Clone();
            priority = task.Priority;
            day = task.Date.Date;
            this.monday = monday;

            Title = "编辑任务";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Theme.B(Colors.Transparent);
            Width = 470;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            FontFamily = Theme.Font;

            Border card = Ui.Round(16, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
            card.Effect = Ui.Shadow(26, 0.5, 4);
            card.Padding = new Thickness(22, 18, 22, 20);

            StackPanel root = new StackPanel();

            // 标题栏
            Grid head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            TextBlock t = Ui.Txt("编辑任务", 15, Theme.B(Theme.Text), true);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 0);
            head.Children.Add(t);
            Border close = Ui.IconButton("close", 28, 12, delegate() { DialogResult = false; }, "关闭");
            close.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(close, 1);
            head.Children.Add(close);
            head.Margin = new Thickness(0, 0, 0, 16);
            root.Children.Add(head);

            // 标题输入
            Border titleWrap = Ui.Round(10, Theme.B(Theme.PanelSoft), Theme.B(Theme.Border), 1);
            titleWrap.Padding = new Thickness(12, 9, 12, 10);
            titleBox = new HintBox("任务内容", 14);
            titleBox.Box.Text = task.Title;
            titleBox.Submitted = delegate(string s) { Save(); };
            titleWrap.Child = titleBox;
            root.Children.Add(titleWrap);

            // 优先级 + 标签
            StackPanel mid = new StackPanel();
            mid.Orientation = Orientation.Horizontal;
            mid.Margin = new Thickness(0, 12, 0, 0);

            prioCells = new Border[3];
            string[] names = new string[] { "普通", "重要", "紧急" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                Color c = i == 0 ? Theme.TextMuted : Theme.PriorityColor(i);
                Border cell = Ui.Round(8, Theme.Transparent, Theme.B(Theme.Border), 1);
                cell.Padding = new Thickness(11, 5, 11, 6);
                cell.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
                cell.Cursor = Cursors.Hand;
                cell.Child = Ui.Txt(names[i], 12, Theme.B(c), false);
                cell.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    priority = idx;
                    PaintPriority();
                };
                prioCells[i] = cell;
                mid.Children.Add(cell);
            }
            PaintPriority();
            root.Children.Add(mid);

            // 日期
            Grid dateRow = new Grid();
            dateRow.Margin = new Thickness(0, 12, 0, 0);
            dateRow.ColumnDefinitions.Add(new ColumnDefinition());
            dateRow.ColumnDefinitions.Add(new ColumnDefinition());
            StackPanel dl = new StackPanel();
            dl.Orientation = Orientation.Horizontal;
            dl.Children.Add(Ui.IconPath("calendar", 14, Theme.B(Theme.TextMuted), 1.4));
            dateText = Ui.Txt(Fmt.DayTitle(day), 13, Theme.B(Theme.Text), false);
            dateText.VerticalAlignment = VerticalAlignment.Center;
            dateText.Margin = new Thickness(8, 0, 0, 0);
            dl.Children.Add(dateText);
            Grid.SetColumn(dl, 0);
            dateRow.Children.Add(dl);

            StackPanel dr = new StackPanel();
            dr.Orientation = Orientation.Horizontal;
            dr.HorizontalAlignment = HorizontalAlignment.Right;
            dr.Children.Add(SmallArrow("left", delegate() { day = day.AddDays(-1); dateText.Text = Fmt.DayTitle(day); }));
            Border todayChip = Ui.Chip("今天", false, delegate() { day = DateTime.Today; dateText.Text = Fmt.DayTitle(day); }, Theme.Accent);
            todayChip.Margin = new Thickness(2, 0, 2, 0);
            dr.Children.Add(todayChip);
            Border tomChip = Ui.Chip("明天", false, delegate() { day = DateTime.Today.AddDays(1); dateText.Text = Fmt.DayTitle(day); }, Theme.Accent);
            tomChip.Margin = new Thickness(0, 0, 2, 0);
            dr.Children.Add(tomChip);
            dr.Children.Add(SmallArrow("right", delegate() { day = day.AddDays(1); dateText.Text = Fmt.DayTitle(day); }));
            Grid.SetColumn(dr, 1);
            dateRow.Children.Add(dr);
            root.Children.Add(dateRow);

            // 标签
            Border tagWrap = Ui.Round(10, Theme.B(Theme.PanelSoft), Theme.B(Theme.Border), 1);
            tagWrap.Padding = new Thickness(12, 7, 12, 8);
            tagWrap.Margin = new Thickness(0, 12, 0, 0);
            tagBox = new HintBox("标签（可选，如：工作 / 学习）", 12.5);
            tagBox.Box.Text = task.Tag;
            tagWrap.Child = tagBox;
            root.Children.Add(tagWrap);

            // 备注
            Border noteWrap = Ui.Round(10, Theme.B(Theme.PanelSoft), Theme.B(Theme.Border), 1);
            noteWrap.Padding = new Thickness(10, 8, 10, 8);
            noteWrap.Margin = new Thickness(0, 10, 0, 0);
            noteBox = new TextBox();
            noteBox.Background = Theme.B(Colors.Transparent);
            noteBox.Foreground = Theme.B(Theme.Text);
            noteBox.CaretBrush = Theme.B(Theme.Accent);
            noteBox.BorderThickness = new Thickness(0);
            noteBox.FontFamily = Theme.Font;
            noteBox.FontSize = 12.5;
            noteBox.AcceptsReturn = true;
            noteBox.TextWrapping = TextWrapping.Wrap;
            noteBox.Height = 58;
            noteBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            noteBox.Text = task.Note;
            noteWrap.Child = noteBox;
            root.Children.Add(noteWrap);

            // 底部按钮
            Grid foot = new Grid();
            foot.Margin = new Thickness(0, 18, 0, 0);
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            Border del = Ui.TextButton("删除", delegate()
            {
                DeleteRequested = true;
                DialogResult = true;
            }, false);
            Grid.SetColumn(del, 0);
            foot.Children.Add(del);
            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(Ui.TextButton("取消", delegate() { DialogResult = false; }, false));
            Border save = Ui.TextButton("保存", delegate() { Save(); }, true);
            save.Margin = new Thickness(8, 0, 0, 0);
            right.Children.Add(save);
            Grid.SetColumn(right, 1);
            foot.Children.Add(right);
            root.Children.Add(foot);

            card.Child = root;
            Content = card;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) DialogResult = false;
            };
            Loaded += delegate(object s, RoutedEventArgs e)
            {
                titleBox.FocusInput();
                titleBox.Box.SelectAll();
            };
        }

        Border SmallArrow(string icon, Action onClick)
        {
            Border b = Ui.Round(8, Theme.B(Theme.PanelSoft), Theme.B(Theme.Border), 1);
            b.Width = 28;
            b.Height = 26;
            b.Child = Ui.IconPath(icon, 11, Theme.B(Theme.TextMuted), 1.5);
            ((Path)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((Path)b.Child).VerticalAlignment = VerticalAlignment.Center;
            Ui.Click(b, onClick, Theme.B(Theme.PanelHi), Theme.B(Theme.PanelSoft));
            return b;
        }

        void PaintPriority()
        {
            for (int i = 0; i < prioCells.Length; i++)
            {
                bool on = i == priority;
                Color c = i == 0 ? Theme.TextMuted : Theme.PriorityColor(i);
                prioCells[i].Background = Theme.B(on ? Theme.Alpha(c, 0.16) : Colors.Transparent);
                prioCells[i].BorderBrush = Theme.B(on ? Theme.Alpha(c, 0.7) : Theme.Border);
                TextBlock t = (TextBlock)prioCells[i].Child;
                t.Foreground = Theme.B(on ? c : Theme.TextMuted);
                t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        void Save()
        {
            string title = titleBox.Box.Text == null ? "" : titleBox.Box.Text.Trim();
            if (title.Length == 0) { DialogResult = false; return; }
            Result.Title = title;
            Result.Tag = tagBox.Box.Text == null ? "" : tagBox.Box.Text.Trim();
            Result.Note = noteBox.Text == null ? "" : noteBox.Text.Trim();
            Result.Priority = priority;
            Result.Date = day.Date;
            Result.Done = draft.Done;
            Result.DoneAt = draft.DoneAt;
            DialogResult = true;
        }
    }
}
