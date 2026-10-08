using System;
using System.Threading;

namespace TimePlanner.Core
{
    /// <summary>
    /// 程序之间的一盏信号灯。
    ///
    /// 主程序平时缩在托盘里，窗口一 Hide 掉，Process.MainWindowHandle 就是 0，
    /// 外面（插件右键「打开主程序」、或者用户又点了一次快捷方式）想把它叫出来时，
    /// 按句柄找根本找不到人，看起来就是「点了没反应」。这里用命名事件代替句柄：
    /// 守着的那一端等人来按灯，按灯的那一端不必知道对面窗口在哪儿。
    ///
    /// 插件同理：已经有一个插件在跑时（比如从另一个版本目录又点了一次），
    /// 新起的那个只会静默退出，看起来也像坏了——所以也给它一盏灯。
    /// </summary>
    public static class AppSignal
    {
        /// <summary>请主程序把主窗口显形（名字按「哪一份安装」区分，见 Install）。</summary>
        public static string ShowMain { get { return Install.SignalMain; } }

        /// <summary>请已经在跑的那个桌面插件把窗口显形。</summary>
        public static string ShowWidget { get { return Install.SignalWidget; } }

        /// <summary>挂灯。早到的信号会自己攒着，等 Pump 起来再处理。</summary>
        public static EventWaitHandle Create(string name)
        {
            try { return new EventWaitHandle(false, EventResetMode.AutoReset, name); }
            catch (Exception) { return null; }
        }

        /// <summary>守灯：后台线程收到信号就调 handler（handler 里记得切回 UI 线程）。</summary>
        public static void Pump(EventWaitHandle signal, Action handler)
        {
            if (signal == null || handler == null) return;
            Thread t = new Thread(delegate()
            {
                while (true)
                {
                    try { signal.WaitOne(); }
                    catch (Exception) { return; }
                    try { handler(); }
                    catch (Exception) { }
                }
            });
            t.IsBackground = true;
            t.Name = "TimePlanner.Signal";
            t.Start();
        }

        /// <summary>按灯：对面没在跑（灯没挂上）时返回 false。</summary>
        public static bool Request(string name)
        {
            try
            {
                EventWaitHandle h = EventWaitHandle.OpenExisting(name);
                try { return h.Set(); }
                finally { h.Close(); }
            }
            catch (Exception) { return false; }
        }
    }
}
