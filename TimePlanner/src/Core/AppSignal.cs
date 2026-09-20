using System;
using System.Threading;

namespace TimePlanner.Core
{
    /// <summary>
    /// 主程序与插件之间的一盏信号灯。
    ///
    /// 主程序平时缩在托盘里，窗口一 Hide 掉，Process.MainWindowHandle 就是 0，
    /// 外面（插件右键「打开主程序」、或者用户又点了一次快捷方式）想把它叫出来时，
    /// 按句柄找根本找不到人，看起来就是「点了没反应」。这里用一个命名事件代替句柄：
    /// 主程序守着自己那盏灯，谁想让它显形就往灯上按一下。
    /// </summary>
    public static class AppSignal
    {
        const string ShowName = @"Local\TimePlanner.App.ShowMain";

        /// <summary>主程序侧：把灯挂上。早到的信号会自己攒着，等 Pump 起来再处理。</summary>
        public static EventWaitHandle Create()
        {
            try { return new EventWaitHandle(false, EventResetMode.AutoReset, ShowName); }
            catch (Exception) { return null; }
        }

        /// <summary>主程序侧：后台线程守着灯，收到信号就调 handler（handler 里记得切回 UI 线程）。</summary>
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
            t.Name = "TimePlanner.ShowSignal";
            t.Start();
        }

        /// <summary>任意一侧：请主程序显形。主程序没在跑（或信号灯没挂上）时返回 false。</summary>
        public static bool RequestShow()
        {
            try
            {
                EventWaitHandle h = EventWaitHandle.OpenExisting(ShowName);
                try { return h.Set(); }
                finally { h.Close(); }
            }
            catch (Exception) { return false; }
        }
    }
}
