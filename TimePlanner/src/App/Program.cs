using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using WF = System.Windows.Forms;
using TimePlanner.Core;

namespace TimePlanner.App
{
    public static class Program
    {
        static Mutex instanceMutex;
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
            instanceMutex = new Mutex(true, @"Local\TimePlanner.App.SingleInstance", out createdNew);
            if (!createdNew)
            {
                ActivateExisting();
                return;
            }

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

            tray = new TrayIcon(store, window, delegate() { ShowMain(); }, delegate(bool all) { Exit(all); });
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
            try
            {
                Process cur = Process.GetCurrentProcess();
                Process[] all = Process.GetProcessesByName(cur.ProcessName);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].Id == cur.Id) continue;
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

    /// <summary>托盘图标与菜单。</summary>
    public class TrayIcon : IDisposable
    {
        readonly WF.NotifyIcon ni;
        readonly WF.ToolStripMenuItem widgetItem;
        readonly Store store;
        readonly MainWindow window;
        bool balloonShown;

        public TrayIcon(Store store, MainWindow window, Action showMain, Action<bool> exit)
        {
            this.store = store;
            this.window = window;

            ni = new WF.NotifyIcon();
            ni.Icon = AppIcon.Tray();
            ni.Text = "时间规划 · 今日计划与桌面插件";
            ni.Visible = true;
            ni.DoubleClick += delegate(object s, EventArgs e) { showMain(); };

            WF.ContextMenuStrip menu = new WF.ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.Items.Add("打开主程序", null, delegate(object s, EventArgs e) { showMain(); });
            widgetItem = new WF.ToolStripMenuItem("显示桌面插件");
            widgetItem.CheckOnClick = false;
            widgetItem.Click += delegate(object s, EventArgs e)
            {
                store.UpdateSettings(delegate(Settings st) { st.WidgetVisible = !st.WidgetVisible; });
            };
            menu.Items.Add(widgetItem);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("立即同步数据", null, delegate(object s, EventArgs e) { store.Reload(); });
            menu.Items.Add("打开数据文件夹", null, delegate(object s, EventArgs e)
            {
                try { Process.Start("explorer.exe", "\"" + TimePlanner.Core.Store.DataDir + "\""); } catch (Exception) { }
            });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("退出主程序（保留桌面插件）", null, delegate(object s, EventArgs e) { exit(false); });
            menu.Items.Add("全部退出", null, delegate(object s, EventArgs e) { exit(true); });
            ni.ContextMenuStrip = menu;

            Sync();
        }

        public void Sync()
        {
            bool on = store.Settings.WidgetVisible;
            widgetItem.Checked = on;
            widgetItem.Text = on ? "隐藏桌面插件" : "显示桌面插件";
            ni.Text = string.Format("时间规划 · 今日 {0}", Summary());
        }

        public void NotifyHidden()
        {
            if (balloonShown) return;
            balloonShown = true;
            try { ni.ShowBalloonTip(2600, "时间规划仍在运行", "程序已最小化到托盘，双击图标可以重新打开。", WF.ToolTipIcon.Info); }
            catch (Exception) { }
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
            try { ni.Visible = false; ni.Dispose(); } catch (Exception) { }
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
            try { runningCache = Process.GetProcessesByName("TimePlanner.Widget").Length > 0; }
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
                Process[] all = Process.GetProcessesByName("TimePlanner.Widget");
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
            DateTime today = DateTime.Today;
            DateTime mon = TaskQuery.WeekStart(today, true);
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

            string[] pages = new string[] { "today", "week", "done", "settings" };
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

            w.Hide();
            Console.WriteLine("rendered " + pages.Length + " pages to " + dir);
            app.Shutdown();
        }
    }
}
