using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using TimePlanner.Core;

namespace TimePlanner.Widget
{
    public static class WidgetProgram
    {
        // 单实例的互斥体必须挂在静态字段上：只放局部变量的话，Release 下它一旦不再被引用
        // 就会被 GC 收掉，句柄一关，这道「只许开一个插件」的闸门就悄悄失效了——
        // 表现就是点两下桌面冒出两个插件，改数据时互相打架。
        static Mutex instanceMutex;

        [STAThread]
        public static void Main(string[] args)
        {
            // 桌面插件只是一块静态小面板，GPU 那套（d3d9 / D3DCompiler / 显卡驱动）常驻要几十 MB。
            // 软件渲染对它够用，省下的内存比掉的那点性能划算。
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            string dir = ArgValue(args, "--render");
            if (dir != null)
            {
                try
                {
                    Renderer.Run(dir);
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(dir, "render-error.txt"), ex.ToString()); }
                    catch (Exception) { }
                }
                return;
            }

            bool created;
            instanceMutex = new Mutex(true, Install.WidgetMutex, out created);
            if (!created)
            {
                // 已经有一个插件在跑了（多半是又点了一次，或者另一个版本目录里的插件）。
                // 以前这里是静默退出，看着就像「点了没反应」。现在按灯把那个插件叫到前面来。
                if (AppSignal.Request(AppSignal.ShowWidget)) return;
                // 灯没挂上：对面多半是老版本插件（不认识这盏灯）。退回老办法——自己枚举
                // 它的窗口把它显形（尽力而为，贴在桌面模式下面板本来就在最下层，效果有限）。
                if (!ActivateRunningWidget())
                {
                    // 实在找不着（比如对面把窗口藏起来了、或者旧版目录已删）就记一笔，
                    // 免得以后又变成「点了没反应」这种没法查的问题。
                    Diagnostics.Log("插件", "已有一个插件在跑，但叫不出来（多半是别的版本目录里的旧版插件）");
                }
                return;
            }

            Store store = new Store();
            store.Load();
            store.StartWatching();
            Diagnostics.HookCrash();
            Theme.SetAccent(store.Settings.Accent);
            if (!store.Settings.WidgetVisible) return;

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                e.Handled = true;
            };

            WidgetWindow w = new WidgetWindow(store);
            w.Show();
            Diagnostics.WatchUi();

            // 别人（比如另一个版本目录里的插件）按灯时，把这个已经开着的插件显形。
            AppSignal.Pump(AppSignal.Create(AppSignal.ShowWidget), delegate()
            {
                try
                {
                    w.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                        new Action(delegate() { w.ShowWidget(); }));
                }
                catch (Exception) { }
            });

            app.Run();
        }

        /// <summary>把已经在跑的那个插件窗口显形，成功返回 true（用在「对面是没挂信号灯的老版本」这条路上）。</summary>
        static bool ActivateRunningWidget()
        {
            try
            {
                // 只认同一份安装里的插件（同目录），别的版本目录里的插件不归我管。
                Process[] all = Install.Siblings("TimePlanner.Widget");
                for (int i = 0; i < all.Length; i++)
                {
                    IntPtr h = DesktopInterop.FindTopWindow(all[i].Id);
                    if (h == IntPtr.Zero) continue;
                    DesktopInterop.ShowHandle(h, true);
                    DesktopInterop.ActivateHandle(h);
                    return true;
                }
            }
            catch (Exception) { }
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

    /// <summary>离屏渲染插件外观。</summary>
    public static class Renderer
    {
        static void Demo(Store store)
        {
            store.Data.Tasks.Clear();
            if (store.Data.Projects == null) store.Data.Projects = new System.Collections.Generic.List<ProjectNode>();
            store.Data.Projects.Clear();
            DateTime today = DateTime.Today;
            string[] titles = new string[] { "把上周末没写完的方案收个尾", "整理季度汇报的框架和关键数据", "和产品同步下周排期", "读 30 页《深度工作》", "晚饭后散步 30 分钟", "早上把周报发给组长" };
            int[] prios = new int[] { 1, 2, 1, 0, 0, 0 };
            string[] tags = new string[] { "工作", "工作", "工作", "学习", "生活", "工作" };
            for (int i = 0; i < titles.Length; i++)
            {
                TaskItem t = TaskItem.Create(titles[i], i == 0 ? today.AddDays(-1) : today);
                t.Priority = prios[i];
                t.Tag = tags[i];
                if (i >= 4) { t.Done = true; t.DoneAt = DateTime.Now.AddHours(-2); }
                store.Data.Tasks.Add(t);
            }
            // 项目里的小项目也是普通事项，插件这一列里它和任务混在一起，只多一枚「归属」小标签
            ProjectNode big = store.AddProject("", ProjectKind.Big, "毕业设计");
            big.Steps = 6;                       // 分了份的项目在插件上另起一行，只写「几分之几」
            big.Reached = 2;
            ProjectNode stage = store.AddProject(big.Id, ProjectKind.Stage, "开题阶段");
            stage.Steps = 3;
            stage.Reached = 1;
            store.AddProject(stage.Id, ProjectKind.Sub, "查 20 篇相关文献 #论文 !");
            store.AddProject(big.Id, ProjectKind.Sub, "和导师约一次面谈 #论文");
        }

        public static void Run(string dir)
        {
            // 同主程序：渲染只看示例数据，数据目录指到临时目录，不碰用户的 data.json
            Store.DataDirOverride = Install.PreviewDataDir;
            Store store = new Store();
            store.ReadOnly = true;
            store.Load();
            Demo(store);
            Theme.SetAccent(store.Settings.Accent);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            WidgetWindow w = new WidgetWindow(store);
            w.RenderMode = true;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -4000;
            w.Top = -4000;
            w.Opacity = 1;
            w.Show();
            w.UpdateLayout();

            LinearGradientBrush bg = new LinearGradientBrush(
                Color.FromRgb(0x18, 0x25, 0x40), Color.FromRgb(0x0B, 0x0F, 0x18), new Point(0, 0), new Point(1, 1));

            Preview.Capture(w, Path.Combine(dir, "widget.png"), bg);

            // 完成任务的小烟火：抓两帧看中间状态
            w.RenderFireworkDemo();
            Preview.Settle(150);
            w.UpdateLayout();
            Preview.Capture(w, Path.Combine(dir, "widget-fx1.png"), bg);
            Preview.Settle(300);
            Preview.Capture(w, Path.Combine(dir, "widget-fx2.png"), bg);

            // 右键菜单皮肤：菜单不允许挂进任何可视树，手动测量排布后直接离屏渲染
            ContextMenu menu = w.MenuForRender();
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(new Point(0, 0), menu.DesiredSize));
            menu.UpdateLayout();
            Preview.Capture(menu, Path.Combine(dir, "widget-menu.png"), bg);
            Console.WriteLine("menu size " + menu.DesiredSize.Width + "x" + menu.DesiredSize.Height);

            // 盖「示例数据」角标（菜单那张只有皮肤、没有任务文字，不用盖）
            string[] shots = new string[] { "widget.png", "widget-fx1.png", "widget-fx2.png" };
            for (int i = 0; i < shots.Length; i++) Preview.Sample(Path.Combine(dir, shots[i]));

            Console.WriteLine("rendered widget to " + dir);
            app.Shutdown();
        }
    }
}
