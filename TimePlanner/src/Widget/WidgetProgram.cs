using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimePlanner.Core;

namespace TimePlanner.Widget
{
    public static class WidgetProgram
    {
        [STAThread]
        public static void Main(string[] args)
        {
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
            Mutex single = new Mutex(true, @"Local\TimePlanner.Widget.SingleInstance", out created);
            if (!created) return;

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
            app.Run();
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
            DateTime today = DateTime.Today;
            string[] titles = new string[] { "整理季度汇报的框架和关键数据", "和产品同步下周排期", "读 30 页《深度工作》", "晚饭后散步 30 分钟", "早上把周报发给组长" };
            int[] prios = new int[] { 2, 1, 0, 0, 0 };
            string[] tags = new string[] { "工作", "工作", "学习", "生活", "工作" };
            for (int i = 0; i < titles.Length; i++)
            {
                TaskItem t = TaskItem.Create(titles[i], today);
                t.Priority = prios[i];
                t.Tag = tags[i];
                if (i >= 3) { t.Done = true; t.DoneAt = DateTime.Now.AddHours(-2); }
                store.Data.Tasks.Add(t);
            }
        }

        public static void Run(string dir)
        {
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

            Console.WriteLine("rendered widget to " + dir);
            app.Shutdown();
        }
    }
}
