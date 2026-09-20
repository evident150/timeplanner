using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TimePlanner.Core
{
    /// <summary>
    /// 托盘图标，直接调 Shell_NotifyIcon。
    /// 以前用的是 System.Windows.Forms.NotifyIcon：为了放一个图标要把整套 WinForms
    /// （约 17 MB 程序集 + 一份 GDI+ 状态）加载进进程，这里全都不需要了。
    /// </summary>
    public class NativeTray : IDisposable
    {
        const int WM_TRAY = 0x8000 + 1;      // WM_APP + 1
        const int WM_LBUTTONUP = 0x0202;
        const int WM_LBUTTONDBLCLK = 0x0203;
        const int WM_RBUTTONUP = 0x0205;

        const int NIM_ADD = 0x00;
        const int NIM_MODIFY = 0x01;
        const int NIM_DELETE = 0x02;
        const int NIF_MESSAGE = 0x01;
        const int NIF_ICON = 0x02;
        const int NIF_TIP = 0x04;
        const int NIF_INFO = 0x10;
        const int NIIF_INFO = 0x01;

        const int WS_POPUP = unchecked((int)0x80000000);
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern uint RegisterWindowMessage(string message);

        readonly HwndSource sink;
        readonly IntPtr icon;
        readonly int id;
        readonly uint taskbarCreated;
        string tip;
        bool added;

        /// <summary>双击图标。</summary>
        public event Action DoubleClick;
        /// <summary>右键点图标（菜单由调用方负责弹）。</summary>
        public event Action RightClick;

        /// <summary>收消息用的隐藏窗口句柄（自检用）。</summary>
        public IntPtr Handle { get { return sink == null ? IntPtr.Zero : sink.Handle; } }

        public NativeTray(IntPtr hIcon, string tooltip, int iconId)
        {
            icon = hIcon;
            id = iconId;
            tip = tooltip == null ? "" : tooltip;

            HwndSourceParameters p = new HwndSourceParameters("TimePlanner.TraySink");
            p.WindowStyle = WS_POPUP;                                       // 顶层但不显示
            p.ExtendedWindowStyle = WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;    // 不进任务栏、不抢焦点
            p.Width = 0;
            p.Height = 0;
            sink = new HwndSource(p);
            sink.AddHook(WndProc);

            taskbarCreated = RegisterWindowMessage("TaskbarCreated");
            Add();
        }

        /// <summary>鼠标悬停提示（可以写「今日 2/6 已完成」这种实时进度）。</summary>
        public string Text
        {
            set
            {
                tip = value == null ? "" : value;
                if (added) Modify(NIF_TIP);
            }
        }

        void Add()
        {
            NOTIFYICONDATA d = Data();
            d.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            d.uCallbackMessage = WM_TRAY;
            added = Shell_NotifyIcon(NIM_ADD, ref d);

            // 托盘图标是主程序唯一的入口，放不上去就得留下线索，别让人对着一个没有图标的进程干瞪眼
            if (added)
            {
                int where = Probe();
                Diagnostics.Log("托盘", where == 0 ? "图标已注册，自检：显示在托盘上"
                    : (where == 1 ? "图标已注册，自检：收在「隐藏的图标」浮层里" : "图标已注册，自检未找到（" + where + "）"));
            }
            else
            {
                Diagnostics.Log("托盘", "图标注册失败，将无法从托盘打开主程序");
            }
        }

        void Modify(int flags)
        {
            NOTIFYICONDATA d = Data();
            d.uFlags = flags;
            Shell_NotifyIcon(NIM_MODIFY, ref d);
        }

        /// <summary>气泡提示（相当于以前的 ShowBalloonTip）。</summary>
        public void Balloon(string title, string body)
        {
            if (!added) return;
            NOTIFYICONDATA d = Data();
            d.uFlags = NIF_INFO;
            d.szInfoTitle = title == null ? "" : title;
            d.szInfo = body == null ? "" : body;
            d.dwInfoFlags = NIIF_INFO;
            Shell_NotifyIcon(NIM_MODIFY, ref d);
        }

        /// <summary>
        /// 自检：问一下资源管理器这个图标现在在不在。
        /// 0 = 显示在托盘上，1 = 被收进了「隐藏的图标」浮层，其它 = 没找到。
        /// </summary>
        public int Probe()
        {
            if (!added) return -1;
            NOTIFYICONIDENTIFIER q = new NOTIFYICONIDENTIFIER();
            q.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
            q.hWnd = sink.Handle;
            q.uID = id;
            RECT r;
            return Shell_NotifyIconGetRect(ref q, out r);
        }

        NOTIFYICONDATA Data()
        {
            NOTIFYICONDATA d = new NOTIFYICONDATA();
            d.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
            d.hWnd = sink.Handle;
            d.uID = id;
            d.hIcon = icon;
            d.szTip = tip;
            return d;
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // 资源管理器崩溃/重启后会广播 TaskbarCreated，得把图标重新加回去
            if (taskbarCreated != 0 && (uint)msg == taskbarCreated)
            {
                added = false;
                Add();
                handled = true;
                return IntPtr.Zero;
            }
            if (msg == WM_TRAY)
            {
                int evt = lParam.ToInt32();
                if (evt == WM_LBUTTONDBLCLK)
                {
                    if (DoubleClick != null) DoubleClick();
                    handled = true;
                }
                else if (evt == WM_RBUTTONUP)
                {
                    if (RightClick != null) RightClick();
                    handled = true;
                }
                else if (evt == WM_LBUTTONUP)
                {
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            try
            {
                if (added)
                {
                    NOTIFYICONDATA d = Data();
                    Shell_NotifyIcon(NIM_DELETE, ref d);
                    added = false;
                }
            }
            catch (Exception) { }
            try { if (sink != null) sink.Dispose(); } catch (Exception) { }
        }
    }
}
