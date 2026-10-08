using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TimePlanner.Core
{
    /// <summary>
    /// 这一份「安装」的身份：凡是要跟别的副本区分开的名字，都从这里取。
    ///
    /// 以前单实例的互斥体、进程间信号灯、开机自启的注册表项都是写死的名字
    /// （Local\TimePlanner.App.SingleInstance、TimePlanner），于是同一台机器上
    /// 装了 1.5 经典版又装 1.7 特别版时，两边互相抢：后开的那个被静默挡掉、
    /// 插件也叫不出来，看着像程序坏了。
    ///
    /// 现在这些名字都带上「我是谁」＝版别 + 安装目录短哈希：
    ///   · 同一个目录重复点   → 还是只开一个（照旧把已经开着的那个叫到前面）；原地换新版
    ///   · 不同目录 / 不同版本 → 各开各的，互不打扰
    /// 数据文件仍然只有一份（%APPDATA%\TimePlanner\data.json），换版本不会丢数据。
    /// </summary>
    public static class Install
    {
        static string _dir;
        static string _key;

        /// <summary>这份程序所在目录（结尾不带反斜杠）。</summary>
        public static string Dir
        {
            get
            {
                if (_dir == null)
                {
                    string d = AppDomain.CurrentDomain.BaseDirectory;
                    if (d == null) d = "";
                    if (d.Length > 1) d = d.TrimEnd('\\', '/');
                    _dir = d;
                }
                return _dir;
            }
        }

        /// <summary>目录名（自启条目里给人在任务管理器里看的那个名字）。</summary>
        public static string FolderName
        {
            get
            {
                string d = Dir;
                int i = d.LastIndexOf('\\');
                string name = (i >= 0 && i + 1 < d.Length) ? d.Substring(i + 1) : d;
                return name.Length == 0 ? "TimePlanner" : name;
            }
        }

        /// <summary>
        /// 身份短哈希：目录 + 版别一起算。
        ///
        /// 故意不带版本号：同一个目录里把 exe 换成新版，身份不变，还是「只开一个」，
        /// 升级时不会新旧两个一起跑。不同版本本来就是不同目录（1.5 经典版、1.7 特别版 …），
        /// 所以「不同版本各开各的」照样成立。
        /// </summary>
        public static string Key
        {
            get
            {
                if (_key == null)
                {
                    string raw = Dir.ToLowerInvariant() + "|" + AppVersion.EditionTag;
                    byte[] hash;
                    using (SHA1 sha = SHA1.Create())
                    {
                        hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                    }
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < 6; i++) sb.Append(hash[i].ToString("x2"));
                    _key = sb.ToString();
                }
                return _key;
            }
        }

        public static string AppMutex { get { return @"Local\TimePlanner.App." + Key; } }
        public static string WidgetMutex { get { return @"Local\TimePlanner.Widget." + Key; } }
        public static string SignalMain { get { return @"Local\TimePlanner.App.Show." + Key; } }
        public static string SignalWidget { get { return @"Local\TimePlanner.Widget.Show." + Key; } }

        /// <summary>
        /// 开机自启的注册表项名：每条线、每个目录各占一条，互相不覆盖。
        /// 名字里同样不放版本号：原地升级复用同一条，免得攒下一串「TimePlanner 1.7…」的旧条目。
        /// </summary>
        public static string AutoStartName
        {
            get { return "TimePlanner " + AppVersion.EditionLabel + "（" + FolderName + "）"; }
        }

        /// <summary>这个进程是不是这一份安装里的（按 exe 所在目录认亲）。</summary>
        public static bool IsSibling(Process p)
        {
            try
            {
                if (p == null || p.HasExited) return false;
                string path = p.MainModule == null ? null : p.MainModule.FileName;
                if (path == null) return false;
                return string.Equals(Path.GetDirectoryName(path), Dir, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                // 拿不到路径（比如对面是管理员权限跑的）就当不是自己人，宁可不动它。
                return false;
            }
        }

        /// <summary>按进程名找出这一份安装里的其它进程（同目录才算，不碰别的版本）。</summary>
        public static Process[] Siblings(string processName)
        {
            List<Process> mine = new List<Process>();
            try
            {
                Process cur = Process.GetCurrentProcess();
                Process[] all = Process.GetProcessesByName(processName);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].Id == cur.Id) continue;
                    if (IsSibling(all[i])) mine.Add(all[i]);
                }
            }
            catch (Exception) { }
            return mine.ToArray();
        }

        /// <summary>出图（--render）用的临时数据目录：按身份分开，两个版本同时出图也不打架。</summary>
        public static string PreviewDataDir
        {
            get { return Path.Combine(Path.GetTempPath(), "TimePlanner-preview-" + Key); }
        }
    }
}