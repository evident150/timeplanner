using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows;

namespace TimePlanner.Core
{
    /// <summary>日期/文案格式化工具。</summary>
    public static class Fmt
    {
        static readonly string[] Short = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
        static readonly string[] Long = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        public static string Weekday(DateTime d) { return Short[(int)d.DayOfWeek]; }
        public static string WeekdayLong(DateTime d) { return Long[(int)d.DayOfWeek]; }
        public static string DateCN(DateTime d) { return string.Format("{0}月{1}日", d.Month, d.Day); }
        public static string Full(DateTime d) { return string.Format("{0}年{1}月{2}日 {3}", d.Year, d.Month, d.Day, WeekdayLong(d)); }
        public static string Clock(DateTime d) { return string.Format("{0:HH:mm}", d); }

        public static string Relative(DateTime day)
        {
            int diff = (int)(day.Date - DateTime.Today).TotalDays;
            if (diff == 0) return "今天";
            if (diff == 1) return "明天";
            if (diff == -1) return "昨天";
            if (diff == 2) return "后天";
            if (diff > 0) return string.Format("{0}天后", diff);
            return string.Format("{0}天前", -diff);
        }

        /// <summary>「今天 · 9月15日 周二」这样的标题。</summary>
        public static string DayTitle(DateTime day)
        {
            return string.Format("{0} · {1} {2}", Relative(day), DateCN(day), Weekday(day));
        }

        public static string Range(DateTime start, DateTime end)
        {
            if (start.Month == end.Month) return string.Format("{0}月{1}日 – {2}日", start.Month, start.Day, end.Day);
            return string.Format("{0}月{1}日 – {2}月{3}日", start.Month, start.Day, end.Month, end.Day);
        }
    }

    /// <summary>任务的排序与筛选。</summary>
    public static class TaskQuery
    {
        public static int WeekStartOffset(bool monday)
        {
            DayOfWeek first = monday ? DayOfWeek.Monday : DayOfWeek.Sunday;
            int off = (int)DateTime.Today.DayOfWeek - (int)first;
            if (off < 0) off += 7;
            return off;
        }

        public static DateTime WeekStart(DateTime anyDay, bool monday)
        {
            int off = (int)anyDay.DayOfWeek - (int)(monday ? DayOfWeek.Monday : DayOfWeek.Sunday);
            if (off < 0) off += 7;
            return anyDay.Date.AddDays(-off);
        }

        public static List<TaskItem> ForDay(IEnumerable<TaskItem> all, DateTime day)
        {
            DateTime d = day.Date;
            List<TaskItem> list = all.Where(t => t.Date.Date == d).ToList();
            list.Sort(Compare);
            return list;
        }

        /// <summary>这一天有任务且全部完成（用来判定“全部完成”的小庆祝）。</summary>
        public static bool AllDone(IEnumerable<TaskItem> all, DateTime day)
        {
            DateTime d = day.Date;
            bool any = false;
            foreach (TaskItem t in all)
            {
                if (t.Date.Date != d) continue;
                any = true;
                if (!t.Done) return false;
            }
            return any;
        }

        public static List<TaskItem> InRange(IEnumerable<TaskItem> all, DateTime from, DateTime to)
        {
            DateTime a = from.Date, b = to.Date;
            List<TaskItem> list = all.Where(t => t.Date.Date >= a && t.Date.Date <= b).ToList();
            list.Sort(Compare);
            return list;
        }

        /// <summary>未完成在前，其次是优先级、手工顺序、创建时间。</summary>
        public static int Compare(TaskItem x, TaskItem y)
        {
            if (x.Done != y.Done) return x.Done ? 1 : -1;
            if (x.Priority != y.Priority) return y.Priority.CompareTo(x.Priority);
            if (x.Sort != y.Sort) return x.Sort.CompareTo(y.Sort);
            if (x.Date.Date != y.Date.Date) return x.Date.Date.CompareTo(y.Date.Date);
            return x.CreatedAt.CompareTo(y.CreatedAt);
        }

        /// <summary>"标题 #标签 !! " 这种快速输入解析。</summary>
        public static void ParseQuickAdd(string raw, out string title, out string tag, out int priority)
        {
            title = raw == null ? "" : raw.Trim();
            tag = "";
            priority = 0;
            if (title.Length == 0) return;

            // 结尾的 ! / !! 表示优先级
            int bangs = 0;
            while (title.Length > 0 && title[title.Length - 1] == '!')
            {
                bangs++;
                title = title.Substring(0, title.Length - 1).TrimEnd();
            }
            if (bangs >= 2) priority = PriorityLevel.Urgent;
            else if (bangs == 1) priority = PriorityLevel.Important;

            // #标签
            string[] parts = title.Split(' ');
            List<string> keep = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length > 1 && p[0] == '#' && tag.Length == 0)
                {
                    tag = p.Substring(1);
                    continue;
                }
                keep.Add(p);
            }
            title = string.Join(" ", keep.ToArray()).Trim();
        }
    }

    /// <summary>数据仓库：JSON 落盘 + 跨进程热同步（主程序与桌面插件共用）。</summary>
    public class Store
    {
        readonly object _gate = new object();
        AppData _data;
        string _stamp = "";
        FileSystemWatcher _watcher;
        Timer _debounce;
        Timer _writeTimer;
        bool _dirty;
        bool _loading;

        /// <summary>改动后延迟写盘的毫秒数：把密集操作合并成一次写盘，界面不必等磁盘。</summary>
        const int WriteDelay = 180;

        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimePlanner");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string DataFile { get { return Path.Combine(DataDir, "data.json"); } }

        /// <summary>数据被本进程或另一个进程改动后触发（可能来自后台线程）。</summary>
        public event Action Changed;

        public AppData Data
        {
            get { lock (_gate) { return _data; } }
        }

        /// <summary>只读模式（离屏渲染等）：置 true 后一律不写盘，免得动到用户的真实数据。</summary>
        public bool ReadOnly;

        public Settings Settings { get { return Data.Settings; } }

        public void Load()
        {
            lock (_gate)
            {
                _loading = true;
                try
                {
                    if (File.Exists(DataFile))
                    {
                        try
                        {
                            _data = ReadFile(DataFile);
                        }
                        catch (Exception)
                        {
                            string bad = DataFile + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                            try { File.Copy(DataFile, bad, true); } catch (Exception) { }
                            _data = null;
                        }
                    }
                    if (_data == null)
                    {
                        _data = AppData.CreateDefault();
                        Seed(_data);
                    }
                    _data.Normalize();
                    _stamp = Stamp();
                }
                finally { _loading = false; }
            }
            if (!File.Exists(DataFile)) Save();
        }

        static AppData ReadFile(string path)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppData));
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                AppData d = (AppData)ser.ReadObject(fs);
                if (d == null) throw new InvalidDataException("empty data");
                return d;
            }
        }

        /// <summary>首次运行给一点上手示例。</summary>
        static void Seed(AppData d)
        {
            DateTime today = DateTime.Today;
            string[] tips = new string[]
            {
                "点圆圈勾选完成，桌面插件与主程序实时同步",
                "双击标题栏打开主程序，规划整周安排",
                "按住标题栏可以拖动这个桌面插件"
            };
            for (int i = 0; i < tips.Length; i++)
            {
                TaskItem t = TaskItem.Create(tips[i], today);
                t.Sort = i;
                t.Tag = i == 0 ? "上手" : "";
                d.Tasks.Add(t);
            }
            DateTime monday = TaskQuery.WeekStart(today, true);
            TaskItem w = TaskItem.Create("规划本周重点（在「本周」页添加）", monday.AddDays(1));
            w.Sort = 0;
            w.Tag = "示例";
            d.Tasks.Add(w);
        }

        string Stamp()
        {
            try
            {
                FileInfo fi = new FileInfo(DataFile);
                if (!fi.Exists) return "";
                return fi.LastWriteTimeUtc.Ticks.ToString() + ":" + fi.Length.ToString();
            }
            catch (Exception) { return ""; }
        }

        /// <summary>立即写盘（同步）。界面线程请用 Mutate 触发的延后写盘，别直接调它。</summary>
        public void Save()
        {
            if (ReadOnly) return;
            AppData snapshot;
            lock (_gate) { snapshot = _data; }
            if (snapshot == null) return;
            if (WriteFile(snapshot)) lock (_gate) { _dirty = false; _stamp = Stamp(); }
        }

        /// <summary>延后写盘：短时间内的多次改动只落盘一次，且始终在后台线程完成。</summary>
        void ScheduleSave()
        {
            Timer t = _writeTimer;
            if (t == null)
            {
                lock (_gate)
                {
                    if (_writeTimer == null)
                    {
                        _writeTimer = new Timer(OnWriteTimer, null, Timeout.Infinite, Timeout.Infinite);
                    }
                    t = _writeTimer;
                }
            }
            lock (_gate) { _dirty = true; }
            try { t.Change(WriteDelay, Timeout.Infinite); }
            catch (Exception) { Save(); }
        }

        void OnWriteTimer(object state)
        {
            try
            {
                bool dirty;
                lock (_gate) { dirty = _dirty; }
                if (!dirty) return;
                Save();
                lock (_gate) { dirty = _dirty; }
                // 写盘失败（多数是另一个进程正占用）时稍后重试，不丢改动。
                if (dirty) { try { _writeTimer.Change(900, Timeout.Infinite); } catch (Exception) { } }
            }
            catch (Exception) { }
        }

        /// <summary>退出前把未落盘的改动立刻写出。</summary>
        public void Flush()
        {
            Timer t = _writeTimer;
            if (t != null) { try { t.Change(Timeout.Infinite, Timeout.Infinite); } catch (Exception) { } }
            bool dirty;
            lock (_gate) { dirty = _dirty; }
            if (dirty) Save();
        }

        bool WriteFile(AppData snapshot)
        {
            string tmp = DataFile + ".tmp";
            using (Mutex m = new Mutex(false, @"Local\TimePlanner.Data.Write"))
            {
                bool held = false;
                try
                {
                    try { held = m.WaitOne(1200); }
                    catch (AbandonedMutexException) { held = true; }
                    if (!held) return false;
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(AppData));
                    using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        ser.WriteObject(fs, snapshot);
                        fs.Flush();
                    }
                    if (File.Exists(DataFile))
                    {
                        try { File.Replace(tmp, DataFile, null, true); }
                        catch (Exception)
                        {
                            File.Copy(tmp, DataFile, true);
                            try { File.Delete(tmp); } catch (Exception) { }
                        }
                    }
                    else
                    {
                        File.Move(tmp, DataFile);
                    }
                }
                catch (Exception)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                    return false;
                }
                finally
                {
                    if (held) { try { m.ReleaseMutex(); } catch (Exception) { } }
                }
            }
            return true;
        }

        /// <summary>修改数据 + 延后落盘 + 通知两端界面（不会阻塞界面线程）。</summary>
        public void Mutate(Action<AppData> fn)
        {
            lock (_gate) { fn(_data); }
            ScheduleSave();
            RaiseChanged();
        }

        public void SaveAndRaise()
        {
            Save();
            RaiseChanged();
        }

        public void Reload()
        {
            AppData fresh;
            try { fresh = File.Exists(DataFile) ? ReadFile(DataFile) : null; }
            catch (Exception) { return; }
            if (fresh == null) return;
            fresh.Normalize();
            lock (_gate) { _data = fresh; _stamp = Stamp(); }
            RaiseChanged();
        }

        public void RaiseChanged()
        {
            Action h = Changed;
            if (h == null) return;
            Application app = Application.Current;
            if (app != null && app.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                try { app.Dispatcher.BeginInvoke(h); }
                catch (Exception) { }
            }
            else
            {
                h();
            }
        }

        /// <summary>监听另一个进程写入的数据文件。</summary>
        public void StartWatching()
        {
            if (_watcher != null) return;
            try
            {
                FileSystemWatcher w = new FileSystemWatcher(DataDir, "data.json");
                w.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime;
                w.Changed += OnFsEvent;
                w.Created += OnFsEvent;
                w.Renamed += OnFsEvent;
                w.Deleted += OnFsEvent;
                w.EnableRaisingEvents = true;
                _watcher = w;
                Timer t = new Timer(OnDebounce, null, Timeout.Infinite, Timeout.Infinite);
                _debounce = t;
            }
            catch (Exception) { }
        }

        void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            Timer t = _debounce;
            if (t == null) return;
            try { t.Change(220, Timeout.Infinite); } catch (Exception) { }
        }

        void OnDebounce(object state)
        {
            if (_loading) return;
            // 自己有还没落盘的改动时先写出去，免得刚勾选的勾被对方进程的旧数据覆盖。
            bool dirty;
            lock (_gate) { dirty = _dirty; }
            if (dirty) { Save(); return; }
            string now = Stamp();
            bool changed;
            lock (_gate) { changed = (now != _stamp); }
            if (changed) Reload();
        }

        // ---- 常用任务操作 ----

        public TaskItem Add(string title, DateTime day)
        {
            string clean; string tag; int prio;
            TaskQuery.ParseQuickAdd(title, out clean, out tag, out prio);
            if (clean.Length == 0) return null;
            TaskItem t = null;
            Mutate(delegate(AppData d)
            {
                t = TaskItem.Create(clean, day);
                t.Tag = tag;
                t.Priority = prio;
                t.Sort = NextSort(d, day);
                d.Tasks.Add(t);
            });
            return t;
        }

        static int NextSort(AppData d, DateTime day)
        {
            int max = -1;
            for (int i = 0; i < d.Tasks.Count; i++)
            {
                TaskItem t = d.Tasks[i];
                if (t.Date.Date == day.Date && t.Sort > max) max = t.Sort;
            }
            return max + 1;
        }

        public void Toggle(string id)
        {
            Mutate(delegate(AppData d)
            {
                TaskItem t = Find(d, id);
                if (t == null) return;
                t.Done = !t.Done;
                t.DoneAt = t.Done ? (DateTime?)DateTime.Now : null;
            });
        }

        public void SetDone(string id, bool done)
        {
            Mutate(delegate(AppData d)
            {
                TaskItem t = Find(d, id);
                if (t == null) return;
                t.Done = done;
                t.DoneAt = done ? (DateTime?)DateTime.Now : null;
            });
        }

        public void Update(TaskItem edited)
        {
            Mutate(delegate(AppData d)
            {
                TaskItem t = Find(d, edited.Id);
                if (t == null) return;
                t.Title = edited.Title;
                t.Note = edited.Note;
                t.Tag = edited.Tag;
                t.Priority = edited.Priority;
                t.Date = edited.Date.Date;
            });
        }

        public void Delete(string id)
        {
            Mutate(delegate(AppData d)
            {
                for (int i = 0; i < d.Tasks.Count; i++)
                {
                    if (d.Tasks[i].Id == id) { d.Tasks.RemoveAt(i); return; }
                }
            });
        }

        public void MoveTo(string id, DateTime day)
        {
            Mutate(delegate(AppData d)
            {
                TaskItem t = Find(d, id);
                if (t == null) return;
                t.Date = day.Date;
                t.Sort = NextSort(d, day);
            });
        }

        public void Defer(DateTime fromDay, DateTime toDay)
        {
            Mutate(delegate(AppData d)
            {
                for (int i = 0; i < d.Tasks.Count; i++)
                {
                    TaskItem t = d.Tasks[i];
                    if (!t.Done && t.Date.Date == fromDay.Date)
                    {
                        t.Date = toDay.Date;
                        t.Sort = NextSort(d, toDay) + i;
                    }
                }
            });
        }

        public void ClearDoneBefore(DateTime day)
        {
            Mutate(delegate(AppData d)
            {
                for (int i = d.Tasks.Count - 1; i >= 0; i--)
                {
                    TaskItem t = d.Tasks[i];
                    if (t.Done && t.Date.Date < day.Date) d.Tasks.RemoveAt(i);
                }
            });
        }

        static TaskItem Find(AppData d, string id)
        {
            for (int i = 0; i < d.Tasks.Count; i++)
                if (d.Tasks[i].Id == id) return d.Tasks[i];
            return null;
        }

        public void UpdateSettings(Action<Settings> fn)
        {
            Mutate(delegate(AppData d) { fn(d.Settings); d.Settings.Normalize(); });
        }
    }
}
