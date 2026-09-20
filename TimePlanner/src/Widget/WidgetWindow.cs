using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TimePlanner.Core;

namespace TimePlanner.Widget
{
    /// <summary>摆在桌面上的任务面板：直接勾选完成、拖动边缘自由缩放。</summary>
    public class WidgetWindow : Window
    {
        const double CardWidth = 300;
        const double AutoListMaxHeight = 330;
        const int EdgeLeft = 1, EdgeRight = 2, EdgeTop = 4, EdgeBottom = 8;

        readonly Store store;
        DateTime day = DateTime.Today;

        Border card;
        Grid rootHost;
        Canvas fx;
        MenuSkin.MenuRow miDesktop, miTopmost, miNormal, miToday;
        Grid weekStrip;
        StackPanel listHost;
        ScrollViewer listScroll;
        TextBlock dayTitle;
        TextBlock dateLine;
        TextBlock progressText;
        MiniBar bar;
        HintBox addBox;
        bool showDone;

        DispatcherTimer rebuild;
        DispatcherTimer fullscreenTimer;
        DispatcherTimer trimTimer;
        bool animating;
        bool hiddenByFullscreen;
        string lastSignature = "";
        string mode = "desktop";
        double scale = 1;
        bool placed;
        string appliedAccent;
        public bool RenderMode;

        bool resizing;
        int resizeEdge;
        int startCursorX, startCursorY;
        double startW, startH, startL, startT;

        public WidgetWindow(Store store)
        {
            this.store = store;
            Title = "时间规划 · 桌面插件";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Theme.B(Colors.Transparent);
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            FontFamily = Theme.Font;
            Icon = AppIcon.WpfIcon();
            showDone = store.Settings.WidgetShowDone;
            appliedAccent = store.Settings.Accent;

            rootHost = new Grid();
            fx = new Canvas();
            fx.IsHitTestVisible = false;
            fx.ClipToBounds = false;

            rebuild = new DispatcherTimer();
            rebuild.Interval = TimeSpan.FromMilliseconds(260);
            rebuild.Tick += delegate(object s, EventArgs e)
            {
                rebuild.Stop();
                animating = false;
                Refresh();
            };

            Build();
            ApplySettings();
            Refresh();
            StartFullscreenWatch();
            StartIdleTrim();
            Loaded += delegate(object s, RoutedEventArgs e)
            {
                if (!RenderMode) DesktopInterop.MakeToolWindow(this);
                EnsureLayer();
                ApplyPosition();
            };
            Deactivated += delegate(object s, EventArgs e)
            {
                if (mode == "desktop" && !RenderMode) DesktopInterop.ToBottom(this);
            };
            store.Changed += OnStoreChanged;
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { SavePosition(); store.Flush(); };
        }

        // ---------------- 外观 ----------------

        void Build()
        {
            card = Ui.Round(16, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
            card.Width = CardWidth;
            card.Effect = Ui.Shadow(18, 0.45, 4);

            Grid root = new Grid();
            root.Margin = new Thickness(14, 12, 14, 12);
            for (int i = 0; i < 5; i++) root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[0].Height = GridLength.Auto;
            root.RowDefinitions[1].Height = GridLength.Auto;
            root.RowDefinitions[2].Height = GridLength.Auto;
            root.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
            root.RowDefinitions[4].Height = GridLength.Auto;

            UIElement head = BuildHeader();
            Grid.SetRow(head, 0);
            root.Children.Add(head);
            UIElement strip = BuildWeekStrip();
            Grid.SetRow(strip, 1);
            root.Children.Add(strip);
            UIElement prog = BuildProgress();
            Grid.SetRow(prog, 2);
            root.Children.Add(prog);
            UIElement list = BuildList();
            Grid.SetRow(list, 3);
            root.Children.Add(list);
            UIElement foot = BuildFooter();
            Grid.SetRow(foot, 4);
            root.Children.Add(foot);
            card.Child = root;

            Grid host = new Grid();
            host.Margin = new Thickness(14);
            host.Children.Add(card);
            AddResizeHandles(host);
            rootHost.Children.Clear();
            rootHost.Children.Add(host);
            rootHost.Children.Add(fx);
            Content = rootHost;

            ContextMenu = BuildMenu();
        }

        /// <summary>右键菜单：一次建好，只在弹出时同步「当前是哪种模式」的标记。</summary>
        ContextMenu BuildMenu()
        {
            ContextMenu menu = MenuSkin.Create();
            MenuSkin.Add(menu, "desktop", "打开主程序", delegate() { OpenMain(); }, false, false);
            MenuSkin.Add(menu, "pencil", "编辑今天", delegate() { OpenMain(); }, false, false);
            MenuSkin.Line(menu);
            miDesktop = MenuSkin.Add(menu, "layers", "贴在桌面上", delegate() { SetMode("desktop"); }, false, false);
            miTopmost = MenuSkin.Add(menu, "pin", "始终置顶", delegate() { SetMode("topmost"); }, false, false);
            miNormal = MenuSkin.Add(menu, "winmax", "普通窗口", delegate() { SetMode("normal"); }, false, false);
            MenuSkin.Line(menu);
            miToday = MenuSkin.Add(menu, "calendar", "只显示今天", delegate() { day = DateTime.Today; Refresh(); }, false, false);
            MenuSkin.Add(menu, "expand", "恢复自适应大小", delegate()
            {
                store.UpdateSettings(delegate(Settings s2) { s2.WidgetWidth = 0; s2.WidgetHeight = 0; });
                ApplyWindowSize();
            }, false, false);
            MenuSkin.Add(menu, "refresh", "刷新", delegate() { store.Reload(); Refresh(); }, false, false);
            MenuSkin.Line(menu);
            MenuSkin.Add(menu, "eye", "隐藏插件", delegate() { HideWidget(); }, false, false);
            MenuSkin.Add(menu, "close", "退出插件", delegate() { Shutdown(); }, false, true);
            SyncMenu();
            menu.Opened += delegate(object s, RoutedEventArgs e) { SyncMenu(); };
            return menu;
        }

        /// <summary>把「当前模式 / 是否只看今天」的标记同步到菜单上。</summary>
        void SyncMenu()
        {
            if (miDesktop != null) miDesktop.SetActive(mode == "desktop");
            if (miTopmost != null) miTopmost.SetActive(mode == "topmost");
            if (miNormal != null) miNormal.SetActive(mode == "normal");
            if (miToday != null) miToday.SetActive(day.Date == DateTime.Today);
        }

        Border BuildHeader()
        {
            Grid g = new Grid();
            g.Background = Theme.B(Colors.Transparent);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());

            StackPanel left = new StackPanel();
            dayTitle = Ui.Txt("今天", 14, Theme.B(Theme.Text), true);
            dateLine = Ui.Txt("", 11.5, Theme.B(Theme.TextFaint), false);
            dateLine.Margin = new Thickness(0, 2, 0, 0);
            left.Children.Add(dayTitle);
            left.Children.Add(dateLine);
            left.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(left, 0);
            g.Children.Add(left);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(SmallIcon("left", delegate() { day = day.AddDays(-1); Refresh(); }, "前一天"));
            Border back = SmallIcon("calendar", delegate() { day = DateTime.Today; Refresh(); }, "回到今天");
            back.Margin = new Thickness(2, 0, 2, 0);
            right.Children.Add(back);
            right.Children.Add(SmallIcon("right", delegate() { day = day.AddDays(1); Refresh(); }, "后一天"));
            right.Children.Add(SmallIcon("close", delegate() { HideWidget(); }, "隐藏插件"));
            Grid.SetColumn(right, 1);
            g.Children.Add(right);

            g.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount >= 2) { OpenMain(); return; }
                if (e.ClickCount == 1 && e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); }
                    catch (Exception) { }
                    SavePosition();
                }
            };
            g.Cursor = Cursors.SizeAll;
            Ui.Tip(g, "拖动这里可以移动插件，双击打开主程序；拖动边缘可以缩放");

            Border wrap = new Border();
            wrap.Child = g;
            wrap.Margin = new Thickness(0, 0, 0, 10);
            return wrap;
        }

        Border SmallIcon(string icon, Action onClick, string tip)
        {
            Border b = Ui.Round(8, Theme.Transparent);
            b.Width = 26;
            b.Height = 26;
            b.Child = Ui.IconPath(icon, 12, Theme.B(Theme.TextMuted), 1.4);
            ((Path)b.Child).HorizontalAlignment = HorizontalAlignment.Center;
            ((Path)b.Child).VerticalAlignment = VerticalAlignment.Center;
            Ui.Click(b, onClick, Theme.B(Theme.PanelHi), Theme.Transparent);
            Ui.Tip(b, tip);
            return b;
        }

        Border BuildWeekStrip()
        {
            weekStrip = new Grid();
            for (int i = 0; i < 7; i++) weekStrip.ColumnDefinitions.Add(new ColumnDefinition());
            weekStrip.Margin = new Thickness(0, 0, 0, 12);
            Border wrap = new Border();
            wrap.Child = weekStrip;
            return wrap;
        }

        Border BuildProgress()
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            progressText = Ui.Txt("", 11.5, Theme.B(Theme.TextMuted), false);
            progressText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(progressText, 0);
            g.Children.Add(progressText);

            StackPanel right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;
            Border toggle = Ui.Chip("已完成", false, delegate()
            {
                showDone = !showDone;
                Refresh();
            }, Theme.Success);
            right.Children.Add(toggle);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);

            StackPanel sp = new StackPanel();
            sp.Children.Add(g);
            bar = new MiniBar(5, Theme.Accent);
            bar.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(bar);

            Border wrap = new Border();
            wrap.Child = sp;
            wrap.Margin = new Thickness(0, 0, 0, 12);
            return wrap;
        }

        Border BuildList()
        {
            listHost = new StackPanel();
            listScroll = Ui.Scroll(listHost);
            listScroll.MaxHeight = AutoListMaxHeight;
            listScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            Border wrap = new Border();
            wrap.Child = listScroll;
            return wrap;
        }

        Border BuildFooter()
        {
            StackPanel sp = new StackPanel();
            Border line = new Border();
            line.Height = 1;
            line.Background = Theme.B(Theme.Border);
            line.Margin = new Thickness(0, 12, 0, 11);
            sp.Children.Add(line);

            Border addWrap = Ui.Round(9, Theme.B(Theme.PanelSoft), Theme.B(Theme.Border), 1);
            addWrap.Padding = new Thickness(10, 7, 10, 8);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());
            Path plus = Ui.IconPath("plus", 12, Theme.B(Theme.Accent), 1.6);
            ((Path)plus).VerticalAlignment = VerticalAlignment.Center;
            plus.Margin = new Thickness(0, 0, 7, 0);
            Grid.SetColumn(plus, 0);
            g.Children.Add(plus);
            addBox = new HintBox("添加任务，回车保存", 12.5);
            addBox.Submitted = delegate(string text) { store.Add(text, day); AnimatedRefresh(); };
            Grid.SetColumn(addBox, 1);
            g.Children.Add(addBox);
            addWrap.Child = g;
            sp.Children.Add(addWrap);
            return new Border { Child = sp };
        }

        // ---------------- 拖边缘缩放 ----------------

        void AddResizeHandles(Grid host)
        {
            host.Children.Add(EdgeHandle(EdgeTop, HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, 7, new Thickness(13, 0, 13, 0), Cursors.SizeNS));
            host.Children.Add(EdgeHandle(EdgeBottom, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, 7, new Thickness(13, 0, 13, 0), Cursors.SizeNS));
            host.Children.Add(EdgeHandle(EdgeLeft, HorizontalAlignment.Left, VerticalAlignment.Stretch, 7, double.NaN, new Thickness(0, 13, 0, 13), Cursors.SizeWE));
            host.Children.Add(EdgeHandle(EdgeRight, HorizontalAlignment.Right, VerticalAlignment.Stretch, 7, double.NaN, new Thickness(0, 13, 0, 13), Cursors.SizeWE));
            host.Children.Add(EdgeHandle(EdgeTop | EdgeLeft, HorizontalAlignment.Left, VerticalAlignment.Top, 14, 14, new Thickness(0), Cursors.SizeNWSE));
            host.Children.Add(EdgeHandle(EdgeTop | EdgeRight, HorizontalAlignment.Right, VerticalAlignment.Top, 14, 14, new Thickness(0), Cursors.SizeNESW));
            host.Children.Add(EdgeHandle(EdgeLeft | EdgeBottom, HorizontalAlignment.Left, VerticalAlignment.Bottom, 14, 14, new Thickness(0), Cursors.SizeNESW));
            host.Children.Add(EdgeHandle(EdgeRight | EdgeBottom, HorizontalAlignment.Right, VerticalAlignment.Bottom, 14, 14, new Thickness(0), Cursors.SizeNWSE));

            Path grip = new Path();
            grip.Data = Geometry.Parse("M 1,9 L 9,1 M 7,12 L 12,7");
            grip.Stroke = Theme.B(Theme.TextFaint);
            grip.StrokeThickness = 1.3;
            grip.StrokeStartLineCap = PenLineCap.Round;
            grip.StrokeEndLineCap = PenLineCap.Round;
            grip.Stretch = Stretch.None;
            grip.IsHitTestVisible = false;
            grip.HorizontalAlignment = HorizontalAlignment.Right;
            grip.VerticalAlignment = VerticalAlignment.Bottom;
            grip.Margin = new Thickness(0, 0, 4, 4);
            host.Children.Add(grip);
            Ui.Tip(grip, "拖动右下角可以调整插件大小");
        }

        Border EdgeHandle(int edge, HorizontalAlignment h, VerticalAlignment v, double w, double hh, Thickness margin, Cursor cursor)
        {
            Border b = new Border();
            b.Background = Theme.B(Colors.Transparent);
            b.HorizontalAlignment = h;
            b.VerticalAlignment = v;
            b.Width = w;
            b.Height = hh;
            b.Margin = margin;
            b.Cursor = cursor;
            b.Tag = edge;
            b.MouseLeftButtonDown += BeginResize;
            b.MouseMove += OnResizeMove;
            b.MouseLeftButtonUp += EndResize;
            return b;
        }

        void BeginResize(object sender, MouseButtonEventArgs e)
        {
            Border b = sender as Border;
            if (b == null || RenderMode || resizing) return;
            resizeEdge = (int)b.Tag;
            resizing = true;
            b.CaptureMouse();
            if (card != null) card.Effect = null;

            int cx, cy;
            DesktopInterop.CursorPos(out cx, out cy);
            startCursorX = cx;
            startCursorY = cy;
            startW = ActualWidth;
            startH = ActualHeight;
            startL = Left;
            startT = Top;

            SizeToContent stc = SizeToContent;
            if ((resizeEdge & (EdgeLeft | EdgeRight)) != 0) stc = stc & ~SizeToContent.Width;
            if ((resizeEdge & (EdgeTop | EdgeBottom)) != 0) stc = stc & ~SizeToContent.Height;
            SizeToContent = stc;
            Width = startW;
            Height = startH;
            ApplyCardSizing();
            e.Handled = true;
        }

        void OnResizeMove(object sender, MouseEventArgs e)
        {
            if (!resizing) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishResize(sender as Border);
                return;
            }

            int cx, cy;
            DesktopInterop.CursorPos(out cx, out cy);
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double dx = (cx - startCursorX) / dpi.DpiScaleX;
            double dy = (cy - startCursorY) / dpi.DpiScaleY;

            double minW = CardWidth * scale + 28;
            if (minW < 240) minW = 240;
            double minH = 150 * scale + 28;
            if (minH < 150) minH = 150;
            double maxW = SystemParameters.WorkArea.Width;
            double maxH = SystemParameters.WorkArea.Height;

            if ((resizeEdge & (EdgeLeft | EdgeRight)) != 0)
            {
                double w = (resizeEdge & EdgeRight) != 0 ? startW + dx : startW - dx;
                if (w < minW) w = minW;
                if (w > maxW) w = maxW;
                Width = w;
                if ((resizeEdge & EdgeLeft) != 0) Left = startL + startW - w;
            }
            if ((resizeEdge & (EdgeTop | EdgeBottom)) != 0)
            {
                double h = (resizeEdge & EdgeBottom) != 0 ? startH + dy : startH - dy;
                if (h < minH) h = minH;
                if (h > maxH) h = maxH;
                Height = h;
                if ((resizeEdge & EdgeTop) != 0) Top = startT + startH - h;
            }
            e.Handled = true;
        }

        void EndResize(object sender, MouseButtonEventArgs e)
        {
            FinishResize(sender as Border);
            if (e != null) e.Handled = true;
        }

        void FinishResize(Border handle)
        {
            if (!resizing) return;
            resizing = false;
            if (card != null) card.Effect = Ui.Shadow(18, 0.45, 4);
            if (handle != null)
            {
                try { handle.ReleaseMouseCapture(); }
                catch (Exception) { }
            }
            SizeToContent stc = SizeToContent;
            bool fixedW = (stc & SizeToContent.Width) == (SizeToContent)0;
            bool fixedH = (stc & SizeToContent.Height) == (SizeToContent)0;
            double w = fixedW ? Width : 0;
            double h = fixedH ? Height : 0;
            ApplyCardSizing();
            store.UpdateSettings(delegate(Settings s2) { s2.WidgetWidth = w; s2.WidgetHeight = h; });
            SavePosition();
        }

        // ---------------- 数据 ----------------

        void OnStoreChanged()
        {
            if (store.Settings.Accent != appliedAccent)
            {
                appliedAccent = store.Settings.Accent;
                Theme.SetAccent(appliedAccent);
                Build();
                Refresh();
                ApplySettings();
                return;
            }
            if (!store.Settings.WidgetVisible)
            {
                Shutdown();
                return;
            }
            ApplySettings();
            if (animating)
            {
                rebuild.Stop();
                rebuild.Start();
                return;
            }
            SyncRefresh();
        }

        void AnimatedRefresh()
        {
            animating = true;
            rebuild.Stop();
            rebuild.Start();
        }

        // ---------------- 全屏适配 ----------------

        /// <summary>定时看看是不是有全屏程序（游戏/演示/全屏视频）在独占屏幕。</summary>
        void StartFullscreenWatch()
        {
            if (RenderMode) return;
            fullscreenTimer = new DispatcherTimer();
            fullscreenTimer.Interval = TimeSpan.FromSeconds(4);
            fullscreenTimer.Tick += delegate(object s, EventArgs e) { CheckFullscreen(); };
            fullscreenTimer.Start();
        }

        /// <summary>
        /// 闲着的时候把工作集还给系统。桌面插件一天里绝大多数时间只是摆在那儿，
        /// 没必要一直占着几十 MB 物理内存；要用到时系统按需调回来，都是文件页，不会丢数据。
        /// </summary>
        void StartIdleTrim()
        {
            if (RenderMode) return;
            trimTimer = new DispatcherTimer();
            trimTimer.Interval = TimeSpan.FromSeconds(12);
            trimTimer.Tick += delegate(object s, EventArgs e)
            {
                // 正在交互（拖动缩放、勾选、放烟火）时不动它，免得那一下发涩
                // 指针在插件上、正在拖动缩放、正在放烟火的时候都不动它，免得那一下发涩
                if (!IsLoaded || resizing || animating || IsMouseOver) return;
                if (fx != null && fx.Children.Count > 0) return;
                DesktopInterop.TrimWorkingSet();
            };
            trimTimer.Start();
        }

        /// <summary>全屏时把自己藏起来：既避免和全屏画面抢合成导致游戏掉帧，也不打扰观看。</summary>
        void CheckFullscreen()
        {
            if (RenderMode || !IsLoaded) return;
            bool pause = store.Settings.PauseOnFullscreenEnabled && DesktopInterop.FullscreenAppActive();
            if (pause == hiddenByFullscreen) return;
            hiddenByFullscreen = pause;
            if (pause)
            {
                DesktopInterop.SetVisible(this, false);
                DesktopInterop.TrimWorkingSet();
                Diagnostics.Log("全屏适配", "检测到全屏程序，桌面插件已暂时隐藏");
            }
            else
            {
                DesktopInterop.SetVisible(this, true);
                Opacity = store.Settings.WidgetOpacity;
                EnsureLayer();
                ApplyPosition();
                Diagnostics.Log("全屏适配", "全屏程序已退出，桌面插件恢复显示");
            }
        }

        /// <summary>把当前要显示的内容压成签名，内容没变就不重建界面。</summary>
        string Signature()
        {
            StringBuilder sb = new StringBuilder();
            Settings st = store.Settings;
            sb.Append(day.Ticks).Append('|').Append(showDone).Append('|').Append(st.WeekStartMonday).Append('|');
            sb.Append(st.WidgetShowDone).Append('|').Append(st.WidgetWidth).Append('|').Append(st.WidgetHeight).Append('|');
            List<TaskItem> list = store.Data.Tasks;
            for (int i = 0; i < list.Count; i++)
            {
                TaskItem t = list[i];
                sb.Append(t.Id).Append(':').Append(t.Done ? '1' : '0').Append(':').Append(t.Priority).Append(':')
                  .Append(t.Sort).Append(':').Append(t.Date.Date.Ticks).Append(':').Append(t.Title).Append(':').Append(t.Tag).Append(';');
            }
            return sb.ToString();
        }

        /// <summary>数据变了才重建：另一端频繁通知时省掉无谓的重排重绘。</summary>
        void SyncRefresh()
        {
            if (Signature() == lastSignature) return;
            Refresh();
        }

        public void Refresh()
        {
            lastSignature = Signature();
            DateTime d = day.Date;
            bool isToday = d == DateTime.Today;
            List<TaskItem> all = TaskQuery.ForDay(store.Data.Tasks, d);
            List<TaskItem> open = all.Where(t => !t.Done).ToList();
            List<TaskItem> done = all.Where(t => t.Done).ToList();

            dayTitle.Text = isToday ? "今天" : Fmt.Relative(d);
            bool fixedSize = (SizeToContent & SizeToContent.Width) == (SizeToContent)0;
            dateLine.Text = Fmt.DateCN(d) + " " + Fmt.Weekday(d) + (isToday ? "" : "　·　点日历图标回到今天");
            int pct = all.Count == 0 ? 0 : (int)Math.Round(done.Count * 100.0 / all.Count);
            progressText.Text = all.Count == 0 ? "还没有安排" : string.Format("已完成 {0}/{1}　·　{2}%", done.Count, all.Count, pct);
            bar.SetRatio(all.Count == 0 ? 0 : (double)done.Count / all.Count, false);

            weekStrip.Children.Clear();
            DateTime ws = TaskQuery.WeekStart(DateTime.Today, store.Settings.WeekStartMonday);
            for (int i = 0; i < 7; i++)
            {
                DateTime wd = ws.AddDays(i);
                List<TaskItem> list = TaskQuery.ForDay(store.Data.Tasks, wd);
                int dn = list.Count(t => t.Done);
                bool sel = wd.Date == d;
                bool today = wd.Date == DateTime.Today;

                Border cell = Ui.Round(8, Theme.B(sel ? Theme.Accent : Colors.Transparent),
                    Theme.B(today && !sel ? Theme.Alpha(Theme.Accent, 0.55) : Colors.Transparent), 1);
                cell.Height = 34;
                cell.Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0);
                cell.HorizontalAlignment = HorizontalAlignment.Stretch;
                StackPanel cs = new StackPanel();
                cs.VerticalAlignment = VerticalAlignment.Center;
                TextBlock wdLabel = Ui.Txt(Fmt.Weekday(wd).Substring(1), 11.5, Theme.B(sel ? Theme.OnAccent : (today ? Theme.Accent : Theme.TextFaint)), sel || today);
                wdLabel.HorizontalAlignment = HorizontalAlignment.Center;
                cs.Children.Add(wdLabel);
                Ellipse dot = new Ellipse();
                dot.Width = 4;
                dot.Height = 4;
                dot.Margin = new Thickness(0, 3, 0, 0);
                dot.HorizontalAlignment = HorizontalAlignment.Center;
                if (list.Count == 0) dot.Fill = Theme.B(Colors.Transparent);
                else if (dn == list.Count) dot.Fill = Theme.B(Theme.Success);
                else dot.Fill = Theme.B(sel ? Theme.OnAccent : Theme.Accent);
                cs.Children.Add(dot);
                cell.Child = cs;
                DateTime target = wd;
                Ui.Click(cell, delegate() { day = target; Refresh(); }, Theme.B(Theme.PanelHi), cell.Background as SolidColorBrush);
                Ui.Tip(cell, string.Format("{0} · {1} 项任务", Fmt.DateCN(wd), list.Count));
                Grid.SetColumn(cell, i);
                weekStrip.Children.Add(cell);
            }

            listHost.Children.Clear();
            if (open.Count == 0 && done.Count == 0)
            {
                StackPanel empty = new StackPanel();
                empty.Margin = new Thickness(0, 6, 0, 6);
                TextBlock e1 = Ui.Txt(isToday ? "今天还没有任务" : "这天没有任务", 12.5, Theme.B(Theme.TextFaint), false);
                e1.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Children.Add(e1);
                TextBlock e2 = Ui.Txt("在下面输入框添加一条吧", 11, Theme.B(Theme.TextFaint), false);
                e2.HorizontalAlignment = HorizontalAlignment.Center;
                e2.Margin = new Thickness(0, 4, 0, 0);
                empty.Children.Add(e2);
                listHost.Children.Add(empty);
            }
            else
            {
                for (int i = 0; i < open.Count; i++)
                {
                    TaskRow row = MakeRow(open[i]);
                    row.Margin = new Thickness(0, 0, 0, 6);
                    listHost.Children.Add(row);
                }
                if (open.Count == 0 && done.Count > 0)
                {
                    TextBlock allDone = Ui.Txt("全部完成 🎉", 12.5, Theme.B(Theme.Success), true);
                    allDone.HorizontalAlignment = HorizontalAlignment.Center;
                    allDone.Margin = new Thickness(0, 4, 0, 4);
                    listHost.Children.Add(allDone);
                }
                if (done.Count > 0 && showDone)
                {
                    for (int i = 0; i < done.Count; i++)
                    {
                        TaskRow row = MakeRow(done[i]);
                        row.Margin = new Thickness(0, 0, 0, 6);
                        listHost.Children.Add(row);
                    }
                }
            }
        }

        TaskRow MakeRow(TaskItem t)
        {
            TaskRow r = new TaskRow(t, true);
            r.ToggleRequested = delegate(TaskItem it)
            {
                bool now = !it.Done;
                it.Done = now;
                it.DoneAt = now ? (DateTime?)DateTime.Now : null;
                r.Refresh(true);
                if (now) Celebrate(r, it);
                AnimatedRefresh();
                store.SetDone(it.Id, now);
            };
            r.DoubleClickRequested = delegate(TaskItem it) { OpenMain(); };
            return r;
        }

        /// <summary>完成一条任务时放一筒礼花；一天的任务全部完成时再补一筒大的。</summary>
        void Celebrate(TaskRow row, TaskItem item)
        {
            if (fx == null) return;
            bool all = TaskQuery.AllDone(store.Data.Tasks, item.Date);
            Point o = row.CheckCenter(fx);
            double aim = o.X > fx.ActualWidth * 0.55 ? -146 : -34;
            Fireworks.Popper(fx, o, all ? 1.5 : 1.25, aim, Fireworks.PickCheer());
            if (all) Fireworks.Popper(fx, Ui.CenterOf(card, fx), 1.6, -90, Fireworks.AllDone);
        }

        /// <summary>离屏预览用：造一份和右键弹出时一模一样的菜单（ContextMenu 不允许挂进可视树，只能单独测量渲染）。</summary>
        internal ContextMenu MenuForRender()
        {
            return BuildMenu();
        }

        /// <summary>离屏预览用：在第一条任务行处炸一簇，不碰任何数据。</summary>
        internal void RenderFireworkDemo()
        {
            if (fx == null) return;
            TaskRow row = Ui.Find<TaskRow>(listHost);
            Point o = row != null ? row.CheckCenter(fx) : Ui.CenterOf(card, fx);
            Fireworks.Popper(fx, o, 1.3, o.X > fx.ActualWidth * 0.55 ? -146 : -40, Fireworks.Cheers[0]);
        }

        // ---------------- 窗口层级 / 位置 / 尺寸 ----------------

        void ApplySettings()
        {
            Settings st = store.Settings;
            bool modeChanged = st.WidgetMode != mode;
            mode = st.WidgetMode;
            scale = st.WidgetScale;
            Opacity = st.WidgetOpacity;
            ScaleTransform old = card.LayoutTransform as ScaleTransform;
            if (old == null || old.ScaleX != scale) card.LayoutTransform = new ScaleTransform(scale, scale);
            if (!resizing) ApplyWindowSize();
            if (modeChanged || !placed) EnsureLayer();
        }

        void ApplyWindowSize()
        {
            Settings st = store.Settings;
            bool fixedW = st.WidgetWidth > 1;
            bool fixedH = st.WidgetHeight > 1;
            SizeToContent stc = SizeToContent;
            stc = fixedW ? (stc & ~SizeToContent.Width) : (stc | SizeToContent.Width);
            stc = fixedH ? (stc & ~SizeToContent.Height) : (stc | SizeToContent.Height);
            SizeToContent = stc;
            if (fixedW) Width = st.WidgetWidth;
            if (fixedH) Height = st.WidgetHeight;
            ApplyCardSizing();
        }

        void ApplyCardSizing()
        {
            if (card == null) return;
            bool fixedW = (SizeToContent & SizeToContent.Width) == (SizeToContent)0;
            bool fixedH = (SizeToContent & SizeToContent.Height) == (SizeToContent)0;
            card.Width = fixedW ? double.NaN : CardWidth;
            if (listScroll != null) listScroll.MaxHeight = fixedH ? double.PositiveInfinity : AutoListMaxHeight;
        }

        void EnsureLayer()
        {
            if (RenderMode) { Topmost = false; return; }
            if (mode == "topmost")
            {
                Topmost = true;
            }
            else
            {
                Topmost = false;
                if (mode == "desktop") DesktopInterop.ToBottom(this);
            }
        }

        void ApplyPosition()
        {
            Settings st = store.Settings;
            double left = st.WidgetLeft;
            double top = st.WidgetTop;
            double w = ActualWidth > 1 ? ActualWidth : CardWidth;
            double h = ActualHeight > 1 ? ActualHeight : 200;
            if (double.IsNaN(left) || double.IsNaN(top))
            {
                Rect wa = SystemParameters.WorkArea;
                left = wa.Right - w - 26;
                top = wa.Bottom - h - 26;
            }
            Rect area = SystemParameters.WorkArea;
            left = Math.Max(area.Left, Math.Min(left, area.Right - 60));
            top = Math.Max(area.Top, Math.Min(top, area.Bottom - 60));

            Left = left;
            Top = top;
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            DesktopInterop.PlaceScreen(this, left * dpi.DpiScaleX, top * dpi.DpiScaleY);
            placed = true;
        }

        void SavePosition()
        {
            DesktopInterop.RECT r = DesktopInterop.ScreenRect(this);
            if (r.Right - r.Left <= 0) return;
            Settings st = store.Settings;
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double l = r.Left / dpi.DpiScaleX;
            double t = r.Top / dpi.DpiScaleY;
            if (Math.Abs(st.WidgetLeft - l) < 0.5 && Math.Abs(st.WidgetTop - t) < 0.5) return;
            store.UpdateSettings(delegate(Settings s2) { s2.WidgetLeft = l; s2.WidgetTop = t; });
        }

        void SetMode(string m)
        {
            store.UpdateSettings(delegate(Settings s2) { s2.WidgetMode = m; });
        }

        void HideWidget()
        {
            SavePosition();
            store.UpdateSettings(delegate(Settings s2) { s2.WidgetVisible = false; });
        }

        void Shutdown()
        {
            try { SavePosition(); } catch (Exception) { }
            try { store.Flush(); } catch (Exception) { }
            Application app = Application.Current;
            if (app != null) app.Shutdown();
            else Close();
        }

        static void OpenMain()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("TimePlanner");
                for (int i = 0; i < ps.Length; i++)
                {
                    IntPtr h = ps[i].MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        DesktopInterop.ShowHandle(h, true);
                        DesktopInterop.ActivateHandle(h);
                        return;
                    }
                }
                string exe = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TimePlanner.exe");
                if (System.IO.File.Exists(exe)) Process.Start(exe);
            }
            catch (Exception) { }
        }
    }
}
