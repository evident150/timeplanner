using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TimePlanner.Core
{
    /// <summary>
    /// 份数进度条：一格一份，点 / 拖到第几格就是「几份之几」。
    /// 大项目分了份以后用它记进度，不必再一条条写小项目 / 分段；松手才写盘（拖动中只重画格子）。
    /// </summary>
    public class StepBar : Border
    {
        readonly int steps;
        readonly double pitch;
        readonly List<Border> cells = new List<Border>();
        int done;
        bool dragging;

        /// <summary>每变一格叫一次；final = true 表示松手（这时候才值得写盘）。</summary>
        public Action<int, bool> Changed;

        public StepBar(int steps, int done, double width)
        {
            this.steps = steps < 1 ? 1 : (steps > ProjectNode.MaxSteps ? ProjectNode.MaxSteps : steps);
            double gap = this.steps > 24 ? 1.2 : 3;
            double cellW = (width - gap * (this.steps - 1)) / this.steps;
            if (cellW < 4)
            {
                gap = 1.2;
                cellW = (width - gap * (this.steps - 1)) / this.steps;
            }
            if (cellW < 2.5) cellW = 2.5;
            pitch = cellW + gap;

            Height = 20;
            Width = this.steps * cellW + gap * (this.steps - 1);
            Background = Theme.B(Colors.Transparent);
            Cursor = Cursors.Hand;

            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.VerticalAlignment = VerticalAlignment.Center;
            double h = this.steps > 24 ? 7 : 9;
            for (int i = 0; i < this.steps; i++)
            {
                Border c = new Border();
                c.Width = cellW;
                c.Height = h;
                c.CornerRadius = new CornerRadius(h / 2);
                c.Background = Theme.B(Theme.PanelHi);
                c.Margin = new Thickness(i == 0 ? 0 : gap, 0, 0, 0);
                cells.Add(c);
                sp.Children.Add(c);
            }
            Child = sp;
            Ui.Tip(this, "点 / 拖到第几格 = 这个项目办到第几份");

            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                dragging = true;
                e.Handled = true;
                CaptureMouse();
                Apply(e.GetPosition(this).X, false);
            };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragging) return;
                e.Handled = true;
                Apply(e.GetPosition(this).X, false);
            };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (!dragging) return;
                dragging = false;
                e.Handled = true;
                try { ReleaseMouseCapture(); } catch (Exception) { }
                Apply(e.GetPosition(this).X, true);
            };

            SetDone(done, false);
        }

        public int Steps { get { return steps; } }
        public int Done { get { return done; } }

        void Apply(double x, bool final)
        {
            int k = (int)Math.Floor(x / pitch) + 1;
            if (k < 0) k = 0;
            if (k > steps) k = steps;
            SetDone(k, false);
            if (Changed != null) Changed(done, final);
        }

        public void SetDone(int value, bool animate)
        {
            done = value < 0 ? 0 : (value > steps ? steps : value);
            Color on = done >= steps ? Theme.Success : Theme.Accent;
            for (int i = 0; i < cells.Count; i++)
                cells[i].Background = Theme.B(i < done ? on : Theme.PanelHi);
        }
    }

    /// <summary>大项目的份数步进器：⊖ 减一份、⊕ 加一份（0 = 不分份）。</summary>
    public class PartsStepper : Border
    {
        readonly TextBlock num;
        readonly Border minus;
        readonly Border plus;
        int steps;

        public Action<int> Changed;

        public PartsStepper(int steps)
        {
            this.steps = steps < 0 ? 0 : steps;
            Height = 22;
            Padding = new Thickness(2, 0, 4, 0);
            CornerRadius = new CornerRadius(11);
            Background = Theme.B(Theme.PanelHi);
            VerticalAlignment = VerticalAlignment.Center;

            minus = MakeKey("winmin");
            minus.Margin = new Thickness(0, 0, 1, 0);
            num = Ui.Txt("0", 12, Theme.B(Theme.TextMuted), true);
            num.VerticalAlignment = VerticalAlignment.Center;
            num.TextAlignment = TextAlignment.Center;
            num.Width = 16;
            plus = MakeKey("plus");
            plus.Margin = new Thickness(1, 0, 0, 0);

            StackPanel tail = new StackPanel();
            tail.Orientation = Orientation.Horizontal;
            tail.VerticalAlignment = VerticalAlignment.Center;
            TextBlock unit = Ui.Txt("份", 10.5, Theme.B(Theme.TextFaint), false);
            unit.VerticalAlignment = VerticalAlignment.Center;
            unit.Margin = new Thickness(1, 1, 0, 0);
            tail.Children.Add(unit);

            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(minus);
            sp.Children.Add(num);
            sp.Children.Add(tail);
            sp.Children.Add(plus);
            Child = sp;

            // 整块把鼠标事件吃掉（含冒泡）：外层那一行「点名字 = 展开 / 收起」别把这一块也算进去，
            // 否则按下时捕获会被外层抢走，⊕ / ⊖ 的点击根本到不了这儿 —— 点半天份数也没变。
            MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };
            MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { e.Handled = true; };

            Ui.Click(minus, delegate() { Step(-1); }, Theme.B(Theme.PanelSoft), Theme.B(Colors.Transparent));
            Ui.Click(plus, delegate() { Step(1); }, Theme.B(Theme.PanelSoft), Theme.B(Colors.Transparent));
            Ui.Tip(this, "把这个项目分成几份：⊖ 减、⊕ 加（0 = 不分份）；分好之后拖左边的横条记已完成");
            Set(steps);
        }

        Border MakeKey(string icon)
        {
            Border b = Ui.Round(9, Theme.B(Colors.Transparent));
            b.Width = 18;
            b.Height = 18;
            b.VerticalAlignment = VerticalAlignment.Center;
            Path p = Ui.IconPath(icon, 10.5, Theme.B(Theme.TextMuted), 1.4);
            p.HorizontalAlignment = HorizontalAlignment.Center;
            p.VerticalAlignment = VerticalAlignment.Center;
            b.Child = p;
            return b;
        }

        public int Steps { get { return steps; } }

        void Step(int delta)
        {
            int to = steps + delta;
            if (to < 0) to = 0;
            if (to > ProjectNode.MaxSteps) to = ProjectNode.MaxSteps;
            if (to == steps) return;
            steps = to;
            Set(steps);
            if (Changed != null) Changed(steps);
        }

        public void Set(int value)
        {
            steps = value;
            num.Text = steps.ToString();
            num.Foreground = Theme.B(steps > 0 ? Theme.Text : Theme.TextFaint);
        }
    }
}
