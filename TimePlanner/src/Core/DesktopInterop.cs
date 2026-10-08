using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace TimePlanner.Core
{
    /// <summary>窗口层级/位置、开机自启等 Win32 交互。</summary>
    public static class DesktopInterop
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumProc cb, IntPtr param);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        delegate bool EnumProc(IntPtr hWnd, IntPtr param);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;
        const uint SWP_SHOWWINDOW = 0x0040;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        /// <summary>取鼠标屏幕坐标（物理像素）。</summary>
        public static void CursorPos(out int x, out int y)
        {
            POINT p;
            GetCursorPos(out p);
            x = p.X;
            y = p.Y;
        }

        public static IntPtr Handle(Window w)
        {
            return new WindowInteropHelper(w).Handle;
        }

        /// <summary>用屏幕坐标摆放窗口。</summary>
        public static void PlaceScreen(Window w, double left, double top)
        {
            IntPtr hwnd = Handle(w);
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(left), (int)Math.Round(top), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        public static void ToBottom(Window w)
        {
            IntPtr hwnd = Handle(w);
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, new IntPtr(1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }

        public static RECT ScreenRect(Window w)
        {
            RECT r = new RECT();
            IntPtr hwnd = Handle(w);
            if (hwnd != IntPtr.Zero) GetWindowRect(hwnd, out r);
            return r;
        }

        public static void ShowHandle(IntPtr hwnd, bool show)
        {
            if (hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, show ? 9 : 6);
        }

        const int SW_HIDE = 0;
        const int SW_SHOWNOACTIVATE = 4;

        /// <summary>显示/隐藏窗口但不抢焦点（插件全屏适配用）。</summary>
        public static void SetVisible(Window w, bool visible)
        {
            IntPtr hwnd = Handle(w);
            if (hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
        }

        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        /// <summary>当前是否有全屏程序（游戏 / 独占全屏 / 演示 / 全屏视频）占着屏幕。</summary>
        public static bool FullscreenAppActive()
        {
            try
            {
                int state;
                if (SHQueryUserNotificationState(out state) != 0) return false;
                // 2=忙碌(全屏程序) 3=独占全屏游戏 4=演示模式；其它状态说明用户没有被全屏程序占住
                if (state != 2 && state != 3 && state != 4) return false;
                return ForegroundCoversScreen();
            }
            catch (Exception) { return false; }
        }

        /// <summary>前台窗口是不是铺满了它所在的显示器（普通最大化的窗口也会命中，所以和上面的状态一起判断）。</summary>
        static bool ForegroundCoversScreen()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            RECT r;
            if (!GetWindowRect(h, out r)) return false;
            if (r.Right - r.Left <= 0) return false;
            IntPtr mon = MonitorFromWindow(h, 2);
            if (mon == IntPtr.Zero) return false;
            MONITORINFO mi = new MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(mon, ref mi)) return false;
            RECT m = mi.rcMonitor;
            return r.Left <= m.Left + 1 && r.Top <= m.Top + 1 && r.Right >= m.Right - 1 && r.Bottom >= m.Bottom - 1;
        }

        public static void ActivateHandle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            SetForegroundWindow(hwnd);
        }

        /// <summary>
        /// 找某个进程里「看得见」的那个顶层窗口（面积最大的一个）。
        ///
        /// 为什么不用 Process.MainWindowHandle：插件是 ShowInTaskbar=false 的无边框窗口，
        /// WPF 会把它挂在一个隐藏的宿主窗口上，MainWindowHandle 直接返回 0，找不着人。
        /// 这里自己枚举一次，好叫醒那些还没挂信号灯的老版本插件。
        /// </summary>
        public static IntPtr FindTopWindow(int pid)
        {
            IntPtr best = IntPtr.Zero;
            long bestArea = -1;
            try
            {
                EnumWindows(delegate(IntPtr h, IntPtr param)
                {
                    uint wpid;
                    GetWindowThreadProcessId(h, out wpid);
                    if ((int)wpid != pid) return true;
                    if (!IsWindowVisible(h)) return true;
                    RECT r;
                    if (!GetWindowRect(h, out r)) return true;
                    long area = (long)(r.Right - r.Left) * (long)(r.Bottom - r.Top);
                    if (area > bestArea) { bestArea = area; best = h; }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { }
            return best;
        }

        /// <summary>标记为工具窗口：不出现在 Alt+Tab 与任务栏中。</summary>
        public static void MakeToolWindow(Window w)
        {
            IntPtr hwnd = Handle(w);
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern int GetWindowLong32(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        static extern IntPtr GetWindowLong64(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        static extern IntPtr SetWindowLong64(IntPtr hWnd, int index, IntPtr value);

        static int GetWindowLong(IntPtr hWnd, int index)
        {
            if (IntPtr.Size == 8) return (int)GetWindowLong64(hWnd, index).ToInt64();
            return GetWindowLong32(hWnd, index);
        }

        static void SetWindowLong(IntPtr hWnd, int index, int value)
        {
            if (IntPtr.Size == 8) SetWindowLong64(hWnd, index, new IntPtr(value));
            else SetWindowLong32(hWnd, index, value);
        }

        public static void Activate(Window w)
        {
            IntPtr hwnd = Handle(w);
            if (hwnd == IntPtr.Zero) return;
            SetForegroundWindow(hwnd);
        }

        // ---- 开机自启 ----

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>1.7 及以前所有版本共用的那一个自启条目名。</summary>
        const string LegacyRunName = "TimePlanner";

        /// <summary>本版本自己的自启条目（名字带版本和目录，两个版本各占一条，互相不覆盖）。</summary>
        public static bool AutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(Install.AutoStartName);
                    if (v != null && v.ToString().Length > 0) return true;
                    // 老版本用的是一条公共条目。只要它指的正是我这个目录，就算我这条已经开着——
                    // 不然升级后设置页会显示成「关」，而登录时其实照样会启动，白让人糊涂。
                    object legacy = k.GetValue(LegacyRunName);
                    if (legacy == null) return false;
                    string exe = ExePathOf(legacy.ToString());
                    return exe != null && string.Equals(Path.GetDirectoryName(exe), Install.Dir, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception) { return false; }
        }

        public static void SetAutoStart(bool on, string exePath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (on)
                    {
                        k.SetValue(Install.AutoStartName, "\"" + exePath + "\" --tray");
                        // 老版本只注册了公共的那一个名字。要是那条指的正是我自己（说明这次是
                        // 原地升级），就撤掉，免得以后登录时把自己启动两遍；指向别的版本目录的
                        // 条目不动——那是另一条线自己的自启，归它管。
                        object legacy = k.GetValue(LegacyRunName);
                        if (legacy != null && SameDir(legacy.ToString(), exePath)) k.DeleteValue(LegacyRunName, false);
                    }
                    else
                    {
                        k.DeleteValue(Install.AutoStartName, false);
                    }
                }
            }
            catch (Exception) { }
        }

        /// <summary>从自启条目里那句命令行里抠出 exe 路径（"C:\...\TimePlanner.exe" --tray）。</summary>
        static string ExePathOf(string command)
        {
            if (command == null) return null;
            string c = command.Trim();
            if (c.Length == 0) return null;
            if (c.StartsWith("\""))
            {
                int end = c.IndexOf('"', 1);
                c = end > 1 ? c.Substring(1, end - 1) : c.Substring(1);
            }
            else
            {
                int sp = c.IndexOf(' ');
                if (sp > 0) c = c.Substring(0, sp);
            }
            c = c.Trim();
            return c.Length == 0 ? null : c;
        }

        /// <summary>那条自启指的是不是我这个目录（老版本换新版 exe 名字时也能对上）。</summary>
        static bool SameDir(string command, string exePath)
        {
            string a = ExePathOf(command);
            string b = exePath;
            if (a == null || b == null) return false;
            try
            {
                return string.Equals(Path.GetDirectoryName(a), Path.GetDirectoryName(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// 除了我这条，还有哪些 TimePlanner 的自启条目（别别的版本注册的）。
        /// 设置页拿它提个醒：不然用户会以为「我只开了一个自启」，其实两三个版本都在跟着开机。
        /// </summary>
        public static string[] OtherAutoStartNames()
        {
            List<string> names = new List<string>();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return names.ToArray();
                    string[] all = k.GetValueNames();
                    for (int i = 0; i < all.Length; i++)
                    {
                        string n = all[i];
                        if (n == null || n.Length == 0) continue;
                        if (string.Equals(n, Install.AutoStartName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (n.StartsWith("TimePlanner", StringComparison.OrdinalIgnoreCase)) names.Add(n);
                    }
                }
            }
            catch (Exception) { }
            return names.ToArray();
        }

        [DllImport("psapi.dll")]
        static extern bool EmptyWorkingSet(IntPtr process);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        /// <summary>
        /// 把工作集（真正占着物理内存的那部分）交还一部分给系统。
        /// 托盘里的主程序和桌面挂件大部分时间什么都不做，没必要一直占着这些页；
        /// 之后要用到时系统会按需调回来，代价只是那一下略慢（都是文件页，不会丢数据）。
        /// </summary>
        public static void TrimWorkingSet()
        {
            try { EmptyWorkingSet(GetCurrentProcess()); } catch (Exception) { }
        }
    }
}
