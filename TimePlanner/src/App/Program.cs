using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using TimePlanner.Core;

namespace TimePlanner.App
{
    public static class Program
    {
        static Mutex instanceMutex;
        static EventWaitHandle showSignal;
        static Store store;
        static MainWindow window;
        static TrayIcon tray;
        static string appliedAccent;
        static bool lastWidgetVisible;

        [STAThread]
        public static void Main(string[] args)
        {
            string renderDir = ArgValue(args, "--render");
            if (renderDir != null)
            {
                try { Previewer.Run(renderDir); }
                catch (Exception ex) { Previewer.Log(ex); throw; }
                return;
            }

            bool createdNew;
            instanceMutex = new Mutex(true, Install.AppMutex, out createdNew);
            if (!createdNew)
            {
                ActivateExisting();
                return;
            }

            // 先把信号灯挂上：主程序多半时间缩在托盘里，插件要叫它显形只能靠这个。
            showSignal = AppSignal.Create(AppSignal.ShowMain);

            Diagnostics.HookCrash();
            Diagnostics.WatchUi();
            store = new Store();
            store.Load();
            store.StartWatching();
            appliedAccent = store.Settings.Accent;
            Theme.SetAccent(appliedAccent);

            bool trayOnly = Has(args, "--tray");

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                // 以前这里会弹一个模态框，窗口不在前台时看起来就像整个程序卡死了。
                e.Handled = true;
                Diagnostics.Log("异常", e.Exception == null ? "未知" : e.Exception.ToString());
            };

            window = new MainWindow(store, trayOnly);
            window.Show();
            if (trayOnly) window.Hide();
            AppSignal.Pump(showSignal, delegate() { DispatchShowMain(); });

            tray = new TrayIcon(store, window, delegate() { ShowMain(); }, delegate(bool all) { Exit(all); });
            ScheduleTrim();
            store.Changed += OnStoreChanged;
            lastWidgetVisible = store.Settings.WidgetVisible;
            WidgetLauncher.Sync(lastWidgetVisible);

            app.Run();
        }

        static void OnStoreChanged()
        {
            if (store.Settings.Accent != appliedAccent)
            {
                appliedAccent = store.Settings.Accent;
                Theme.SetAccent(appliedAccent);
                window.RebuildAll();
            }
            if (tray != null) tray.Sync();
            if (store.Settings.WidgetVisible != lastWidgetVisible)
            {
                lastWidgetVisible = store.Settings.WidgetVisible;
                WidgetLauncher.Sync(lastWidgetVisible);
            }
        }

        /// <summary>
        /// 主程序大部分时间缩在托盘里，只在没露脸的时候定时把工作集还给系统。
        /// 窗口开着的时候不动它——那是用户正在看的界面，收了也会马上再调回来。
        /// </summary>
        static void ScheduleTrim()
        {
            System.Windows.Threading.DispatcherTimer t = new System.Windows.Threading.DispatcherTimer();
            t.Interval = TimeSpan.FromSeconds(20);
            t.Tick += delegate(object s, EventArgs e)
            {
                bool idle = window == null || !window.IsVisible || window.WindowState == WindowState.Minimized;
                if (!idle) return;
                DesktopInterop.TrimWorkingSet();
            };
            t.Start();
        }

        /// <summary>信号是后台线程收的，显形得回 UI 线程做。</summary>
        static void DispatchShowMain()
        {
            MainWindow w = window;
            if (w == null) return;
            try { w.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(delegate() { ShowMain(); })); }
            catch (Exception) { }
        }

        static void ShowMain()
        {
            if (window == null) return;
            window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        static void Exit(bool includeWidget)
        {
            try { store.Flush(); } catch (Exception) { }
            if (includeWidget) WidgetLauncher.Kill();
            if (tray != null) tray.Dispose();
            if (window != null) window.ExitForReal();
            Application app = Application.Current;
            if (app != null) app.Shutdown();
        }

        static void ActivateExisting()
        {
            // 主程序可能正缩在托盘里，这时它的主窗口句柄是 0，按句柄找不着。
            // 信号灯靠谱得多，先按一下；主程序是老版本（不认识这盏灯）再退回原来的办法。
            if (AppSignal.Request(AppSignal.ShowMain)) return;
            try
            {
                // 只看这一份安装里的同名进程：别的版本目录里的主程序不归我叫唤。
                Process[] all = Install.Siblings(Process.GetCurrentProcess().ProcessName);
                for (int i = 0; i < all.Length; i++)
                {
                    IntPtr h = all[i].MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        DesktopInterop.ShowHandle(h, true);
                        DesktopInterop.ActivateHandle(h);
                        break;
                    }
                }
            }
            catch (Exception) { }
        }

        static bool Has(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string ArgValue(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length ? args[i + 1] : ".";
            return null;
        }
    }

    /// <summary>托盘图标与菜单：Win32 Shell_NotifyIcon + 自绘深色菜单，不再把 WinForms 拖进进程。</summary>
    public class TrayIcon : IDisposable
    {
        readonly NativeTray ni;
        readonly Store store;
        readonly Action showMain;
        readonly Action<bool> exit;
        bool balloonShown;

        public TrayIcon(Store store, MainWindow window, Action showMain, Action<bool> exit)
        {
            this.store = store;
            this.showMain = showMain;
            this.exit = exit;

            ni = new NativeTray(AppIcon.Tray().Handle, "时间规划 · 今日计划与桌面插件", 1);
            ni.DoubleClick += delegate() { showMain(); };
            ni.RightClick += delegate() { OpenMenu(); };

            Sync();
        }

        public void Sync()
        {
            ni.Text = string.Format("时间规划 · 今日 {0}", Summary());
        }

        void OpenMenu()
        {
            ContextMenu menu = BuildMenu(store, showMain, exit);
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        /// <summary>托盘右键菜单（离屏预览也用这一份，保证截图和真正弹出来的完全一致）。</summary>
        public static ContextMenu BuildMenu(Store store, Action showMain, Action<bool> exit)
        {
            ContextMenu menu = MenuSkin.Create();
            MenuSkin.Add(menu, "list", "打开主程序", delegate() { showMain(); }, false, false);
            bool on = store.Settings.WidgetVisible;
            MenuSkin.Add(menu, "desktop", on ? "隐藏桌面插件" : "显示桌面插件", delegate()
            {
                store.UpdateSettings(delegate(Settings st) { st.WidgetVisible = !st.WidgetVisible; });
            }, on, false);
            MenuSkin.Line(menu);
            MenuSkin.Add(menu, "refresh", "立即同步数据", delegate() { store.Reload(); }, false, false);
            MenuSkin.Add(menu, "expand", "打开数据文件夹", delegate()
            {
                try { Process.Start("explorer.exe", "\"" + Store.DataDir + "\""); } catch (Exception) { }
            }, false, false);
            MenuSkin.Line(menu);
            MenuSkin.Add(menu, "close", "退出主程序（保留桌面插件）", delegate() { exit(false); }, false, false);
            MenuSkin.Add(menu, "close", "全部退出", delegate() { exit(true); }, false, true);
            return menu;
        }

        public void NotifyHidden()
        {
            if (balloonShown) return;
            balloonShown = true;
            try { ni.Balloon("时间规划仍在运行", "程序已最小化到托盘，双击图标可以重新打开。"); } catch (Exception) { }
            DesktopInterop.TrimWorkingSet();     // 已经缩进托盘了，驻留内存还给系统
        }

        string Summary()
        {
            System.Collections.Generic.List<TaskItem> list = TaskQuery.ForDay(store.Data.Tasks, DateTime.Today);
            int done = 0;
            for (int i = 0; i < list.Count; i++) if (list[i].Done) done++;
            return string.Format("{0}/{1} 已完成", done, list.Count);
        }

        public void Dispose()
        {
            try { ni.Dispose(); } catch (Exception) { }
        }
    }

    /// <summary>桌面插件进程的启动、关闭。</summary>
    public static class WidgetLauncher
    {
        static bool killed;

        public static string ExePath
        {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TimePlanner.Widget.exe"); }
        }

        static bool runningCache;
        static DateTime runningChecked = DateTime.MinValue;

        /// <summary>进程探测要枚举全部进程，比较慢，缓存 2 秒避免界面频繁刷新时反复调用。</summary>
        public static bool IsRunning()
        {
            if ((DateTime.Now - runningChecked).TotalSeconds < 2) return runningCache;
            runningChecked = DateTime.Now;
            try { runningCache = Install.Siblings("TimePlanner.Widget").Length > 0; }
            catch (Exception) { runningCache = false; }
            return runningCache;
        }

        public static void Sync(bool visible)
        {
            if (visible)
            {
                killed = false;
                EnsureRunning();
            }
            else if (!killed)
            {
                killed = true;
                Kill();
            }
        }

        public static void EnsureRunning()
        {
            if (IsRunning()) return;
            if (!System.IO.File.Exists(ExePath)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(ExePath);
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(ExePath);
                Process.Start(psi);
            }
            catch (Exception) { }
        }

        public static void Kill()
        {
            try
            {
                // 只关自己这份安装的插件：别的版本目录里的插件由它自己那份主程序管。
                Process[] all = Install.Siblings("TimePlanner.Widget");
                for (int i = 0; i < all.Length; i++)
                {
                    try
                    {
                        // 先礼貌关闭，让插件把位置/设置写完再退出，超时才强杀。
                        all[i].CloseMainWindow();
                        if (!all[i].WaitForExit(700)) all[i].Kill();
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>离屏渲染主程序各页面，用于自动检查界面效果。</summary>
    public static class Previewer
    {
        public static void Log(Exception ex)
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "timeplanner-render-error.txt"), ex.ToString());
            }
            catch (Exception) { }
        }

        public static void Demo(Store store) { LoadDemo(store); }

        static void LoadDemo(Store store)
        {
            store.Data.Tasks.Clear();
            if (store.Data.Projects == null) store.Data.Projects = new System.Collections.Generic.List<ProjectNode>();
            store.Data.Projects.Clear();
            DateTime today = DateTime.Today;
            DateTime mon = TaskQuery.WeekStart(today, true);
            AddDemo(store, "把上周末没写完的方案收个尾", today.AddDays(-1), 1, "工作", false);
            AddDemo(store, "整理季度汇报的框架和关键数据", today, 2, "工作", false);
            AddDemo(store, "和产品同步下周排期", today, 1, "工作", false);
            AddDemo(store, "读 30 页《深度工作》", today, 0, "学习", false);
            AddDemo(store, "晚饭后散步 30 分钟", today, 0, "生活", true);
            AddDemo(store, "早上把周报发给组长", today, 0, "工作", true);
            AddDemo(store, "健身：上肢训练", mon.AddDays(1), 1, "生活", false);
            AddDemo(store, "准备周会材料", mon.AddDays(1), 0, "工作", true);
            AddDemo(store, "整理客户反馈清单", mon.AddDays(2), 0, "工作", false);
            AddDemo(store, "英语口语练习 20 分钟", mon.AddDays(2), 0, "学习", false);
            AddDemo(store, "陪家人看电影", mon.AddDays(4), 0, "生活", false);
            AddDemo(store, "复盘本月目标完成情况", mon.AddDays(5), 1, "复盘", false);
            LoadDemoProjects(store);
        }

        /// <summary>示例项目：一个大项目带分段，另一个直接挂小项目（小项目在数据里就是普通事项，截图里它和普通任务混在一列）。</summary>
        static void LoadDemoProjects(Store store)
        {
            ProjectNode thesis = store.AddProject("", ProjectKind.Big, "毕业设计");
            ProjectNode stage = store.AddProject(thesis.Id, ProjectKind.Stage, "开题阶段");
            store.AddProject(stage.Id, ProjectKind.Sub, "查 20 篇相关文献 #论文 !");
            ProjectNode draft = store.AddProject(stage.Id, ProjectKind.Sub, "写完开题报告初稿 #论文 !!");
            // 大项目不写细目也能记进度：整个项目分成 8 份、已办 3 份（拖横条记的）
            store.SetProjectSteps(thesis.Id, 8);
            store.SetProjectReached(thesis.Id, 3);
            store.AddProject(thesis.Id, ProjectKind.Sub, "和导师约一次面谈 #论文");

            ProjectNode body = store.AddProject("", ProjectKind.Big, "体重管理");
            ProjectNode aerobic = store.AddProject(body.Id, ProjectKind.Sub, "每周三次有氧 #生活");
            store.AddProject(body.Id, ProjectKind.Sub, "把晚餐的碳水减半 #生活");

            // 让一条小项目是「已竟」，进度上看得出来
            TaskItem t = ProjectTree.ItemOf(store.Data, draft);
            if (t != null) { t.Done = true; t.DoneAt = DateTime.Now.AddHours(-2); }
            // 另一条挪到本周后半，看它和普通任务排在一起的样子
            TaskItem t2 = ProjectTree.ItemOf(store.Data, aerobic);
            if (t2 != null) t2.Date = TaskQuery.WeekStart(DateTime.Today, true).AddDays(3);
        }

        static void AddDemo(Store store, string title, DateTime day, int prio, string tag, bool done)
        {
            TaskItem t = TaskItem.Create(title, day);
            t.Priority = prio;
            t.Tag = tag;
            t.Done = done;
            t.DoneAt = done ? (DateTime?)day.AddHours(11) : null;
            store.Data.Tasks.Add(t);
        }

        public static void Run(string dir)
        {
            // 渲染只为出截图：数据目录指到临时目录 + 内置示例数据，
            // 连读都不读用户那份 data.json，免得把人家真实写在里面的计划渲染进要提交的截图。
            Store.DataDirOverride = Install.PreviewDataDir;
            Store store = new Store();
            store.ReadOnly = true;
            store.Load();
            LoadDemo(store);
            Theme.SetAccent(store.Settings.Accent);
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            MainWindow w = new MainWindow(store, true);
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -4000;
            w.Top = -4000;
            w.ShowInTaskbar = false;
            w.Show();

            string[] pages = new string[] { "today", "week", "project", "done", "settings" };
            for (int i = 0; i < pages.Length; i++)
            {
                w.SelectPage(pages[i]);
                w.UpdateLayout();
                Preview.Capture(w, System.IO.Path.Combine(dir, "main-" + pages[i] + ".png"), Theme.B(Theme.Bg));
            }
            w.SelectPage("today");
            w.UpdateLayout();
            Preview.Settle(60);
            w.RenderFireworkDemo();
            Preview.Settle(150);
            Preview.Capture(w, System.IO.Path.Combine(dir, "main-firework.png"), Theme.B(Theme.Bg));
            Preview.Settle(300);
            Preview.Capture(w, System.IO.Path.Combine(dir, "main-firework2.png"), Theme.B(Theme.Bg));

            // 每张图都盖一枚「示例数据」角标
            string[] shots = new string[] { "main-today.png", "main-week.png", "main-project.png", "main-done.png", "main-settings.png", "main-firework.png", "main-firework2.png" };
            for (int i = 0; i < shots.Length; i++) Preview.Sample(System.IO.Path.Combine(dir, shots[i]));

            w.Hide();
            Console.WriteLine("rendered " + pages.Length + " pages to " + dir);
            app.Shutdown();
        }
    }
}
