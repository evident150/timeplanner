using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using TimePlanner.Core;

namespace TimePlanner.App
{
    /// <summary>主程序窗口：规划今日与本周计划。</summary>
    public partial class MainWindow : Window
    {
        public readonly Store Store;
        readonly bool startHidden;
        bool reallyClosing;

        internal string Page = "today";
        internal DateTime DayAnchor = DateTime.Today;
        internal DateTime WeekAnchor = DateTime.Today;
        internal bool ShowDoneSection = true;

        Border contentHost;
        Canvas fx;
        TextBlock pageTitle;
        TextBlock pageSubtitle;
        StackPanel pageActions;
        StackPanel navList;
        StackPanel sideFoot;
        string saveNote;                            // 手动存盘后的回执（「已保存到本地 · 时刻」）：侧栏一重画就没了，得记着
        DispatcherTimer rebuildTimer;
        bool animating;
        string lastSignature = "";
        internal HintBox AddBox;
        string projectAddParent;                    // 项目页：正在给哪个节点加下級（null = 没在加）
        int projectAddKind = ProjectKind.Sub;       // 加的是小项目还是分段
        string projectRenameId;                     // 项目页：正在改名哪个节点（null = 没在改）
        // [sync-classic] 主程序：项目页的状态字段：7E464D0B92E5（整块由 sync-classic.ps1 从主线搬来，别在这一块里手改）
        string paintedPage = "";                                                    // 当前真正显示在 contentHost 里的页面
        readonly Dictionary<string, ScrollViewer> pageScrollers = new Dictionary<string, ScrollViewer>();   // 每页一个滚动区：重画只换里面的内容，位置由它自己带着

        public MainWindow(Store store, bool startHidden)
        {
            Store = store;
            this.startHidden = startHidden;

            Title = "时间规划";
            Width = 1180;
            Height = 780;
            MinWidth = 940;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Theme.B(Theme.Bg);
            Foreground = Theme.B(Theme.Text);
            FontFamily = Theme.Font;
            Icon = AppIcon.WpfIcon();
            // 小项目也是普通事项，行上补一句它属于哪个项目
            TaskRow.ProjectLabel = delegate(TaskItem t) { return ProjectLabelOf(t); };
            WindowChrome.SetWindowChrome(this, Chrome());

            Build();
            Store.Changed += OnStoreChanged;
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (!reallyClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
                else
                {
                    rebuildTimer.Stop();
                    try { Store.Flush(); } catch (Exception) { }
                }
            };
        }

        /// <summary>离屏预览用：在第一条任务行处炸一簇烟火，不碰数据。</summary>
        internal void RenderFireworkDemo()
        {
            if (fx == null) return;
            TaskRow row = Ui.Find<TaskRow>(contentHost);
            Point o = row != null ? row.CheckCenter(fx) : Ui.CenterOf(contentHost, fx);
            Fireworks.Popper(fx, o, 1.35, o.X > fx.ActualWidth * 0.55 ? -148 : -32, Fireworks.Cheers[0]);
        }

        public void ExitForReal()
        {
            reallyClosing = true;
            Close();
        }

        static WindowChrome Chrome()
        {
            WindowChrome c = new WindowChrome();
            c.CaptionHeight = 52;
            c.ResizeBorderThickness = new Thickness(6);
            c.GlassFrameThickness = new Thickness(0);
            c.CornerRadius = new CornerRadius(0);
            c.UseAeroCaptionButtons = false;
            return c;
        }

        // ---------------- 构建 ----------------

        void Build()
        {
            rebuildTimer = new DispatcherTimer();
            rebuildTimer.Interval = TimeSpan.FromMilliseconds(260);
            rebuildTimer.Tick += delegate(object s, EventArgs e)
            {
                rebuildTimer.Stop();
                animating = false;
                Refresh();
            };

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[0].Height = new GridLength(52);
            root.RowDefinitions.Add(new RowDefinition());
            root.Children.Add(BuildTitleBar());

            Grid body = new Grid();
            ColumnDefinition nav = new ColumnDefinition();
            nav.Width = new GridLength(216);
            body.ColumnDefinitions.Add(nav);
            body.ColumnDefinitions.Add(new ColumnDefinition());
            Border side = BuildSidebar();
            Grid.SetColumn(side, 0);
            body.Children.Add(side);

            Grid main = new Grid();
            main.RowDefinitions.Add(new RowDefinition());
            main.RowDefinitions[0].Height = GridLength.Auto;
            main.RowDefinitions.Add(new RowDefinition());
            Border head = BuildPageHeader();
            Grid.SetRow(head, 0);
            main.Children.Add(head);
            contentHost = new Border();
            Grid.SetRow(contentHost, 1);
            main.Children.Add(contentHost);
            Grid.SetColumn(main, 1);
            body.Children.Add(main);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            fx = new Canvas();
            fx.IsHitTestVisible = false;
            fx.ClipToBounds = false;
            Grid shell = new Grid();
            shell.Children.Add(root);
            shell.Children.Add(fx);
            Content = shell;
            Refresh();
        }

        Border BuildTitleBar()
        {
            Grid g = new Grid();
            g.Background = Theme.B(Theme.BgAlt);
            ColumnDefinition a = new ColumnDefinition();
            a.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(a);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition());

            StackPanel brand = new StackPanel();
            brand.Orientation = Orientation.Horizontal;
            brand.Margin = new Thickness(16, 0, 0, 0);
            brand.VerticalAlignment = VerticalAlignment.Center;
            Border logo = Ui.Round(9, Theme.B(Theme.Accent));
            logo.Width = 28;
            logo.Height = 28;
            Path mark = Ui.IconPath("check", 15, Theme.B(Colors.White), 2.0);
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
            logo.Child = mark;
            brand.Children.Add(logo);
            TextBlock name = Ui.Txt("时间规划", 14, Theme.B(Theme.Text), true);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(10, 0, 0, 0);
            brand.Children.Add(name);
            TextBlock en = Ui.Txt("TimePlanner", 11.5, Theme.B(Theme.TextFaint), false);
            en.VerticalAlignment = VerticalAlignment.Center;
            en.Margin = new Thickness(8, 1, 0, 0);
            brand.Children.Add(en);
            Grid.SetColumn(brand, 0);
            g.Children.Add(brand);

            StackPanel wins = new StackPanel();
            wins.Orientation = Orientation.Horizontal;
            wins.HorizontalAlignment = HorizontalAlignment.Right;
            wins.VerticalAlignment = VerticalAlignment.Center;
            wins.Margin = new Thickness(0, 0, 6, 0);
            wins.Children.Add(WindowButton("min"));
            wins.Children.Add(WindowButton("max"));
            wins.Children.Add(WindowButton("close"));
            Grid.SetColumn(wins, 2);
            g.Children.Add(wins);

            Border wrap = Ui.Round(0, Theme.B(Theme.BgAlt));
            wrap.Child = g;
            return wrap;
        }

        Border WindowButton(string kind)
        {
            Border b = Ui.Round(7, Theme.Transparent);
            b.Width = 36;
            b.Height = 30;
            b.Margin = new Thickness(2, 0, 2, 0);
            b.Cursor = Cursors.Hand;
            WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            Path p;
            if (kind == "min") p = Ui.IconPath("winmin", 11, Theme.B(Theme.TextMuted), 1.3);
            else if (kind == "max") p = Ui.IconPath("winmax", 10, Theme.B(Theme.TextMuted), 1.3);
            else p = Ui.IconPath("close", 11, Theme.B(Theme.TextMuted), 1.4);
            p.HorizontalAlignment = HorizontalAlignment.Center;
            p.VerticalAlignment = VerticalAlignment.Center;
            b.Child = p;

            b.MouseEnter += delegate(object s, MouseEventArgs e)
            {
                b.Background = Theme.B(kind == "close" ? Theme.Danger : Theme.PanelHi);
                p.Stroke = Theme.B(kind == "close" ? Colors.White : Theme.Text);
            };
            b.MouseLeave += delegate(object s, MouseEventArgs e)
            {
                b.Background = Theme.Transparent;
                p.Stroke = Theme.B(Theme.TextMuted);
            };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (kind == "min") WindowState = WindowState.Minimized;
                else if (kind == "max") WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                else Hide();
            };
            return b;
        }

        Border BuildSidebar()
        {
            Border side = new Border();
            side.Background = Theme.B(Theme.BgAlt);
            side.Padding = new Thickness(12, 14, 12, 12);

            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions.Add(new RowDefinition());
            g.RowDefinitions[0].Height = GridLength.Auto;
            g.RowDefinitions[1].Height = GridLength.Auto;

            navList = new StackPanel();
            TextBlock planLabel = Ui.Txt("计划", 11, Theme.B(Theme.TextFaint), true);
            planLabel.Margin = new Thickness(11, 0, 0, 6);
            navList.Children.Add(planLabel);
            navList.Children.Add(NavItem("today", "list", "今日"));
            navList.Children.Add(NavItem("week", "calendar", "本周"));
            navList.Children.Add(NavItem("project", "layers", "项目档案"));
            navList.Children.Add(NavItem("done", "check", "已完成"));
            navList.Children.Add(NavItem("settings", "gear", "设置"));
            navList.Children.Add(NavItem("account", "refresh", "云端共用"));
            AccountAuto.Start(this);          // 自动同步跟着主程序走，跟用户停在哪一页无关
            Grid.SetRow(navList, 0);
            g.Children.Add(navList);

            sideFoot = new StackPanel();
            sideFoot.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(sideFoot, 1);
            g.Children.Add(sideFoot);
            side.Child = g;
            return side;
        }

        Border NavItem(string key, string icon, string label)
        {
            Border b = Ui.Round(10, Theme.Transparent);
            b.Height = 40;
            b.Margin = new Thickness(0, 2, 0, 0);
            b.Padding = new Thickness(11, 0, 11, 0);
            b.Tag = key;
            b.Cursor = Cursors.Hand;

            Grid g = new Grid();
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(c0);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinition c2 = new ColumnDefinition();
            c2.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(c2);

            Path p = Ui.IconPath(icon, 16, Theme.B(Theme.TextMuted), 1.5);
            p.VerticalAlignment = VerticalAlignment.Center;
            p.Margin = new Thickness(0, 0, 11, 0);
            Grid.SetColumn(p, 0);
            g.Children.Add(p);

            TextBlock t = Ui.Txt(label, 13.5, Theme.B(Theme.TextMuted), false);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);

            TextBlock badge = Ui.Txt("", 11.5, Theme.B(Theme.TextFaint), false);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.Name = "badge_" + key;
            Grid.SetColumn(badge, 2);
            g.Children.Add(badge);

            b.Child = g;
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                SelectPage(key);
            };
            return b;
        }

        Border BuildPageHeader()
        {
            Border head = new Border();
            head.Padding = new Thickness(26, 22, 26, 10);
            Grid g = new Grid();
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(1, GridUnitType.Star);
            g.ColumnDefinitions.Add(c0);
            g.ColumnDefinitions.Add(new ColumnDefinition());

            StackPanel left = new StackPanel();
            pageTitle = Ui.Txt("", 23, Theme.B(Theme.Text), true);
            pageSubtitle = Ui.Txt("", 12.5, Theme.B(Theme.TextMuted), false);
            pageSubtitle.Margin = new Thickness(0, 5, 0, 0);
            left.Children.Add(pageTitle);
            left.Children.Add(pageSubtitle);
            Grid.SetColumn(left, 0);
            g.Children.Add(left);

            pageActions = new StackPanel();
            pageActions.Orientation = Orientation.Horizontal;
            pageActions.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pageActions, 1);
            g.Children.Add(pageActions);

            head.Child = g;
            return head;
        }

        // ---------------- 刷新 ----------------

        public void RebuildAll()
        {
            Build();
        }

        public void SelectPage(string key)
        {
            Page = key;
            Refresh();
        }

        void OnStoreChanged()
        {
            if (animating)
            {
                rebuildTimer.Stop();
                rebuildTimer.Start();
                return;
            }
            SyncRefresh();
        }

        /// <summary>内容签名：数据没变就不用重建整页，避免另一端的通知造成无谓重排。</summary>
        string Signature()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Settings st = Store.Settings;
            sb.Append(Page).Append('|').Append(DayAnchor.Date.Ticks).Append('|').Append(WeekAnchor.Date.Ticks).Append('|')
              .Append(ShowDoneSection).Append('|').Append(st.Accent).Append('|').Append(st.WeekStartMonday).Append('|')
              .Append(st.WidgetVisible).Append('|').Append(st.WidgetShowDone).Append('|').Append(st.WidgetWidth).Append('|')
              .Append(st.WidgetHeight).Append('|').Append(st.WidgetScale).Append('|').Append(st.KeepDoneDays).Append('|')
              .Append(st.AutoStart).Append('|').Append(st.PauseOnFullscreenEnabled).Append('|')
              .Append(Store.WriteError).Append('|');
            List<TaskItem> list = Store.Data.Tasks;
            for (int i = 0; i < list.Count; i++)
            {
                TaskItem t = list[i];
                sb.Append(t.Id).Append(':').Append(t.Done ? '1' : '0').Append(':').Append(t.Priority).Append(':')
                  .Append(t.Sort).Append(':').Append(t.Date.Date.Ticks).Append(':').Append(t.Title).Append(':').Append(t.Tag).Append(':').Append(t.Note)
                  .Append(':').Append(t.ProjectId).Append(';');
            }
            // 项目树也进签名：展开 / 收起、改名、挪次序、改了份数之后这一页都要重排
            List<ProjectNode> projs = Store.Data.Projects;
            for (int i = 0; i < projs.Count; i++)
            {
                ProjectNode n = projs[i];
                sb.Append(n.Id).Append(':').Append(n.ParentId).Append(':').Append(n.Kind).Append(':').Append(n.Sort)
                  .Append(':').Append(n.IsOpen ? '1' : '0').Append(':').Append(n.Title).Append(':').Append(n.ItemId)
                  .Append(':').Append(n.Steps).Append(':').Append(n.Reached).Append(';');
            }
            return sb.ToString();
        }

        internal void SyncRefresh()
        {
            if (Signature() == lastSignature) return;
            Refresh();
        }

        /// <summary>先播放勾选动画，稍后再重建列表。</summary>
        internal void AnimatedRefresh()
        {
            animating = true;
            rebuildTimer.Stop();
            rebuildTimer.Start();
        }

        public void Refresh()
        {
            lastSignature = Signature();
            bool addFocused = AddBox != null && AddBox.Box.IsKeyboardFocusWithin;
            PaintNav();
            PaintSideFoot();
            UIElement body;
            if (Page == "week") { WeekAnchor = TaskQuery.WeekStart(WeekAnchor, Store.Settings.WeekStartMonday); body = BuildWeekPage(); }
            else if (Page == "project") body = BuildProjectPage();
            else if (Page == "done") body = BuildDonePage();
            else if (Page == "settings") body = BuildSettingsPage();
            else if (Page == "account") body = BuildAccountPage();
            else body = BuildTodayPage();
            UIElement view = KeepScroll(Page, body);        // 只换正文、留着滚动区：重画、打字回车都不会跳回顶部
            if (!object.ReferenceEquals(contentHost.Child, view)) contentHost.Child = view;
            paintedPage = Page;

            if (addFocused && AddBox != null) AddBox.FocusInput();
        }

        /// <summary>重建页面前记住滚动位置（拖动任务改期、勾选完成后页面不该跳回顶部）。</summary>
        // 页面正文都裹在 Ui.Scroll 里：重画的时候只把里面的内容换掉，滚动区本身留着接着用，位置由它自己带着。
        // 以前是每趟都新建一个滚动区、靠「先读回位置、画完再设回去」：新滚动区要等布局量过内容才认
        // VerticalOffset，量之前读回来是 0，赶上一趟刷新就把记下的位置覆盖成 0 —— 于是打完字一回车
        //（Store 改动一次 + Submitted 里再刷一次）整页跳回顶部。留着滚动区就没这回事。
        UIElement KeepScroll(string page, UIElement body)
        {
            ScrollViewer fresh = body as ScrollViewer;
            if (fresh == null) { pageScrollers.Remove(page); return body; }
            ScrollViewer keep;
            if (!pageScrollers.TryGetValue(page, out keep) || keep == null)
            {
                pageScrollers[page] = fresh;
                return fresh;
            }
            UIElement inner = fresh.Content as UIElement;
            if (inner != null)
            {
                fresh.Content = null;           // 先撒手再交出去，免得撞上「已经是别人的子元素」
                keep.Content = inner;
            }
            return keep;
        }
        // [sync-classic] 主程序：每页留一个滚动区（重画只换正文）：65BFF7ADB895（整块由 sync-classic.ps1 从主线搬来，别在这一块里手改）
        void PaintNav()
        {
            foreach (object o in navList.Children)
            {
                Border b = o as Border;
                if (b == null || !(b.Tag is string)) continue;
                string key = (string)b.Tag;
                bool on = key == Page;
                b.Background = Theme.B(on ? Theme.AccentSoft : Colors.Transparent);
                Grid g = (Grid)b.Child;
                Path p = (Path)g.Children[0];
                TextBlock t = (TextBlock)g.Children[1];
                TextBlock badge = (TextBlock)g.Children[2];
                p.Stroke = Theme.B(on ? Theme.Accent : Theme.TextMuted);
                t.Foreground = Theme.B(on ? Theme.Text : Theme.TextMuted);
                t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                badge.Foreground = Theme.B(on ? Theme.Accent : Theme.TextFaint);
                if (key == "today") badge.Text = CountText(DateTime.Today);
                else if (key == "week")
                {
                    DateTime ws = WeekRangeStart();
                    int openWeek = 0;
                    var wl = TaskQuery.InRange(Store.Data.Tasks, ws, ws.AddDays(6));
                    for (int i = 0; i < wl.Count; i++) if (!wl[i].Done) openWeek++;
                    badge.Text = openWeek == 0 ? "" : openWeek.ToString();
                }
                else if (key == "project")
                {
                    int openProj = ProjectTree.OpenCount(Store.Data);
                    badge.Text = openProj == 0 ? "" : openProj.ToString();
                }
                else badge.Text = "";
            }
        }

        string CountText(DateTime day)
        {
            var list = TaskQuery.ForDay(Store.Data.Tasks, day);
            int open = 0;
            for (int i = 0; i < list.Count; i++) if (!list[i].Done) open++;
            return open == 0 ? "" : open.ToString();
        }

        DateTime WeekRangeStart()
        {
            return TaskQuery.WeekStart(WeekAnchor, Store.Settings.WeekStartMonday);
        }

        void PaintSideFoot()
        {
            sideFoot.Children.Clear();
            DateTime today = DateTime.Today;
            var list = TaskQuery.ForDay(Store.Data.Tasks, today);
            int done = 0;
            for (int i = 0; i < list.Count; i++) if (list[i].Done) done++;
            int total = list.Count;
            double ratio = total == 0 ? 0 : (double)done / total;

            Border card = Ui.Round(12, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
            card.Padding = new Thickness(13, 12, 13, 13);
            card.Margin = new Thickness(0, 0, 0, 8);
            StackPanel sp = new StackPanel();
            TextBlock lbl = Ui.Txt("今日进度", 11.5, Theme.B(Theme.TextFaint), false);
            sp.Children.Add(lbl);
            TextBlock num = Ui.Txt(string.Format("{0} / {1}", done, total), 20, Theme.B(Theme.Text), true);
            num.Margin = new Thickness(0, 3, 0, 8);
            sp.Children.Add(num);
            MiniBar bar = new MiniBar(6, Theme.Success);
            bar.Margin = new Thickness(0, 0, 0, 0);
            sp.Children.Add(bar);
            card.Child = sp;
            bar.SetRatio(ratio, false);
            sideFoot.Children.Add(card);

            Border widget = Ui.TextButton(Store.Settings.WidgetVisible ? "隐藏桌面插件" : "显示桌面插件", delegate()
            {
                bool next = !Store.Settings.WidgetVisible;
                Store.UpdateSettings(delegate(Settings st) { st.WidgetVisible = next; });
            }, false);
            widget.HorizontalAlignment = HorizontalAlignment.Stretch;
            ((TextBlock)widget.Child).HorizontalAlignment = HorizontalAlignment.Center;
            sideFoot.Children.Add(widget);
            // 手动存盘：改动平时是自动落盘的（Store.ScheduleSave），这里给个「现在就写」的按钮 ——
            // 点一下立刻写文件，顺手把写去哪儿、写没写成摆在明面上，省得数据在不在本地全靠猜。
            Border save = Ui.TextButton("保存计划", delegate()
            {
                Store.Flush();
                saveNote = Store.WriteError == null && !Store.ReadOnly
                    ? "已保存到本地 · " + Fmt.Clock(DateTime.Now)
                    : null;                            // 写不下去的话，下面那块红字会说，不报假喜
                PaintSideFoot();
            }, false);
            save.HorizontalAlignment = HorizontalAlignment.Stretch;
            save.Margin = new Thickness(0, 8, 0, 0);
            ((TextBlock)save.Child).HorizontalAlignment = HorizontalAlignment.Center;
            Ui.Tip(save, "把当前计划立刻写进本地文件：\n" + Store.DataFile);
            sideFoot.Children.Add(save);
            if (saveNote != null)
            {
                TextBlock note = Ui.Txt(saveNote, 11, Theme.B(Theme.TextFaint), false);
                note.HorizontalAlignment = HorizontalAlignment.Center;
                note.Margin = new Thickness(0, 6, 0, 0);
                sideFoot.Children.Add(note);
            }

            // 存盘失败必须让用户看见：否则改动只在内存里，一重启就没了。
            string err = Store.WriteError;
            if (err != null)
            {
                Border warn = Ui.Round(12, Theme.B(Theme.Panel), Theme.B(Theme.Danger), 1);
                warn.Padding = new Thickness(13, 10, 13, 10);
                warn.Margin = new Thickness(0, 8, 0, 0);
                TextBlock wt = Ui.Txt("⚠ 存盘失败，改动还在内存里（正在重试）\n" + err, 11, Theme.B(Theme.Danger), false);
                wt.TextWrapping = TextWrapping.Wrap;
                warn.Child = wt;
                sideFoot.Children.Add(warn);
            }
        }
    }
}
