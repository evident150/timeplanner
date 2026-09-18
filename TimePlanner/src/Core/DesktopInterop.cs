using System;
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
        const string RunName = "TimePlanner";

        public static bool AutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RunName);
                    return v != null && v.ToString().Length > 0;
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
                    if (on) k.SetValue(RunName, "\"" + exePath + "\" --tray");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch (Exception) { }
        }
    }
}
