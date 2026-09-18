using System;
using System.IO;
using System.Text;
using System.Windows.Threading;

namespace TimePlanner.Core
{
    /// <summary>轻量诊断：卡顿与异常都写进日志，方便定位“卡死”。</summary>
    public static class Diagnostics
    {
        static readonly object _gate = new object();
        static int _watching;

        public static string LogPath
        {
            get { return Path.Combine(Store.DataDir, "timeplanner.log"); }
        }

        public static void Log(string tag, string message)
        {
            try
            {
                lock (_gate)
                {
                    string path = LogPath;
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 256 * 1024) File.Delete(path);
                    string line = string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}", DateTime.Now, tag, message);
                    File.AppendAllText(path, line + "\r\n", Encoding.UTF8);
                }
            }
            catch (Exception) { }
        }

        /// <summary>监控界面线程：心跳间隔明显超时说明界面被卡住，记一条日志。</summary>
        public static void WatchUi()
        {
            if (_watching == 1) return;
            _watching = 1;
            DispatcherTimer t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(250);
            DateTime last = DateTime.Now;
            t.Tick += delegate(object s, EventArgs e)
            {
                DateTime now = DateTime.Now;
                double ms = (now - last).TotalMilliseconds;
                last = now;
                if (ms > 1200) Log("卡顿", string.Format("界面线程被阻塞约 {0} 毫秒", (int)ms));
            };
            t.Start();
        }

        /// <summary>后台线程崩掉时不再让进程静默消失，而是留下日志。</summary>
        public static void HookCrash()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log("崩溃", e.ExceptionObject == null ? "未知异常" : e.ExceptionObject.ToString());
            };
        }
    }
}
