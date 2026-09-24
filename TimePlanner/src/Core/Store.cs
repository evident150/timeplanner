﻿using System;
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
        string _writeError;
        string _loggedWriteError = "";

        /// <summary>改动后延迟写盘的毫秒数：把密集操作合并成一次写盘，界面不必等磁盘。</summary>
        const int WriteDelay = 180;

        /// <summary>
        /// 离屏渲染时把数据目录指到别处用。
        /// 渲染只为出截图，一律走这份覆盖值，连读都不读用户的真实数据文件——
        /// 免得哪天示例数据没盖全，把用户自己的任务渲染进要提交的截图里。
        /// </summary>
        public static string DataDirOverride;

        public static string DataDir
        {
            get
            {
                if (DataDirOverride != null) return DataDirOverride;
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

        /// <summary>最近一次写盘失败的原因（null = 正常）。界面据此亮红字——此前写盘失败是静默的，改动只留在内存里，一重启就没了。</summary>
        public string WriteError { get { lock (_gate) { return _writeError; } } }

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
                    // 主文件读不出来（写坏了、被误删、被杀毒软件吞了）时退回到上一次写盘前的备份，
                    // 总比直接甩给用户一份空的默认数据强。
                    if (_data == null && File.Exists(DataFile + ".bak"))
                    {
                        try
                        {
                            _data = ReadFile(DataFile + ".bak");
                            Diagnostics.Log("数据", "data.json 读不出来，已从 data.json.bak 恢复");
                        }
                        catch (Exception) { _data = null; }
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
                        // 覆盖前把当前这份留成 .bak：万一新写的内容有问题、或者文件被别的程序弄坏，
                        // 下次启动还能顺着 .bak 回到上一个可用状态。
                        try { File.Copy(DataFile, DataFile + ".bak", true); } catch (Exception) { }
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
                catch (Exception ex)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                    MarkWriteError(ex.Message);
                    return false;
                }
                finally
                {
                    if (held) { try { m.ReleaseMutex(); } catch (Exception) { } }
                }
            }
            ClearWriteError();
            Snapshot();
            return true;
        }

        /// <summary>写盘失败：立刻记日志（同一句只记一次，重试时不刷屏），并通知界面亮红字。</summary>
        void MarkWriteError(string why)
        {
            bool first;
            lock (_gate)
            {
                first = _loggedWriteError != why;
                _loggedWriteError = why;
                _writeError = why;
            }
            if (first)
            {
                Diagnostics.Log("数据", "存盘失败，改动还在内存里（会自动重试）：" + why);
                RaiseChanged();
            }
        }

        void ClearWriteError()
        {
            bool had;
            lock (_gate) { had = _writeError != null; _writeError = null; _loggedWriteError = ""; }
            if (had) RaiseChanged();
        }

        /// <summary>每成功写一次就留一份带时间戳的快照，只保留最近 12 份；数据被写坏或被谁改乱了还能往回倒。</summary>
        void Snapshot()
        {
            try
            {
                string dir = Path.Combine(DataDir, "snapshots");
                Directory.CreateDirectory(dir);
                string full = Path.Combine(dir, "data-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                if (!File.Exists(full)) File.Copy(DataFile, full, true);
                string[] files = Directory.GetFiles(dir, "data-*.json");
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length - 12; i++) { try { File.Delete(files[i]); } catch (Exception) { } }
            }
            catch (Exception) { }
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
                // 小项目的名字在项目树上还有一份，编辑弹窗改完这里跟上
                ProjectNode owner = ProjectTree.NodeOfItem(d, t.Id);
                if (owner != null) owner.Title = t.Title;
            });
        }

        public void Delete(string id)
        {
            Mutate(delegate(AppData d)
            {
                for (int i = 0; i < d.Tasks.Count; i++)
                {
                    if (d.Tasks[i].Id != id) continue;
                    // 项目里的事项：删掉它，项目树上那一格也一起删（不然会留下空壳）
                    ProjectNode owner = ProjectTree.NodeOfItem(d, id);
                    if (owner != null) { RemoveProjectSubtree(d, owner.Id); return; }
                    d.Tasks.RemoveAt(i);
                    return;
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

        /// <summary>
        /// 把某天之前还没办完的事一并顺延到目标日（通常是今天），返回挪了几条。
        /// 「昨天没做完的今天接着做」是常态，光靠一天天手动拖太费劲；
        /// 顺延一下，也不至于让「今天空着」看起来像数据丢了。
        /// </summary>
        public int RollOver(DateTime beforeDay, DateTime toDay)
        {
            int moved = 0;
            Mutate(delegate(AppData d)
            {
                for (int i = 0; i < d.Tasks.Count; i++)
                {
                    TaskItem t = d.Tasks[i];
                    if (!t.Done && t.Date.Date < beforeDay.Date)
                    {
                        t.Date = toDay.Date;
                        t.Sort = NextSort(d, toDay) + i;
                        moved++;
                    }
                }
            });
            return moved;
        }

        public void ClearDoneBefore(DateTime day)
        {
            Mutate(delegate(AppData d)
            {
                for (int i = d.Tasks.Count - 1; i >= 0; i--)
                {
                    TaskItem t = d.Tasks[i];
                    if (t.ProjectId != null && t.ProjectId.Length > 0) continue;   // 项目的事项由项目页自己管
                    if (t.Done && t.Date.Date < day.Date) d.Tasks.RemoveAt(i);
                }
            });
        }

        // ---- 项目树（大项目 / 分段 / 小项目） ----
        //
        // 小项目底下挂一条普通事项（TaskItem.ProjectId = 节点 id）：于是它天生就出现在
        // 今日 / 本周 / 插件里，勾选、拖动改期、顺延、礼花全都跟任务走同一条路。
        // 节点名和那条事项的标题由这里一起改，别在别处只动一边。

        /// <summary>在某个节点下新建项目（parentId 空 = 新建大项目）。返回新节点。</summary>
        public ProjectNode AddProject(string parentId, int kind, string rawTitle)
        {
            string title; string tag; int prio;
            TaskQuery.ParseQuickAdd(rawTitle, out title, out tag, out prio);
            if (title.Length == 0) return null;
            ProjectNode made = null;
            Mutate(delegate(AppData d)
            {
                string pid = parentId == null ? "" : parentId;
                ProjectNode parent = pid.Length == 0 ? null : ProjectTree.ById(d, pid);
                int realKind;
                if (pid.Length == 0) realKind = ProjectKind.Big;
                else if (parent == null || parent.Kind == ProjectKind.Sub) return;      // 小项目下面不加下級
                else if (parent.Kind == ProjectKind.Stage) realKind = ProjectKind.Sub;  // 分段里只放小项目
                else realKind = kind == ProjectKind.Stage ? ProjectKind.Stage : ProjectKind.Sub;

                made = ProjectNode.Create(pid, realKind);
                made.Title = title;
                made.Sort = ProjectTree.NextSort(d, pid);
                d.Projects.Add(made);
                if (realKind == ProjectKind.Sub) AttachItem(d, made, tag, prio);
            });
            return made;
        }

        /// <summary>给小项目挂上它自己的事项（标题就是节点名）。</summary>
        static void AttachItem(AppData d, ProjectNode n, string tag, int prio)
        {
            TaskItem t = TaskItem.Create(n.Title, DateTime.Today);
            t.Tag = tag == null ? "" : tag;
            t.Priority = prio;
            t.Sort = NextSort(d, DateTime.Today);
            t.ProjectId = n.Id;
            d.Tasks.Add(t);
            n.ItemId = t.Id;
        }

        /// <summary>改名（顺带支持 「…#标签 !!」的写法，会落到它那条事项上）。</summary>
        public void RenameProject(string id, string rawTitle)
        {
            string title; string tag; int prio;
            TaskQuery.ParseQuickAdd(rawTitle, out title, out tag, out prio);
            if (title.Length == 0) return;
            Mutate(delegate(AppData d)
            {
                ProjectNode n = ProjectTree.ById(d, id);
                if (n == null) return;
                n.Title = title;
                TaskItem it = ProjectTree.ItemOf(d, n);
                if (it == null) return;
                it.Title = title;
                if (tag.Length > 0) it.Tag = tag;
                if (prio > 0) it.Priority = prio;
            });
        }

        /// <summary>删掉一个项目：它下面的分段 / 小项目和那些事项一起删。</summary>
        public void DeleteProject(string id)
        {
            Mutate(delegate(AppData d)
            {
                if (ProjectTree.ById(d, id) == null) return;
                RemoveProjectSubtree(d, id);
            });
        }

        /// <summary>同级里往上（-1）/ 往下（+1）挪一格。</summary>
        public void MoveProject(string id, int delta)
        {
            Mutate(delegate(AppData d)
            {
                ProjectNode n = ProjectTree.ById(d, id);
                if (n == null) return;
                List<ProjectNode> sib = ProjectTree.Children(d, n.ParentId);
                int idx = -1;
                for (int i = 0; i < sib.Count; i++) if (sib[i].Id == id) { idx = i; break; }
                int to = idx + delta;
                if (idx < 0 || to < 0 || to >= sib.Count) return;
                ProjectNode tmp = sib[idx];
                sib[idx] = sib[to];
                sib[to] = tmp;
                for (int i = 0; i < sib.Count; i++) sib[i].Sort = i;   // 顺手把乱掉的排序值理顺
            });
        }

        /// <summary>项目页里展开 / 收起一个容器。</summary>
        public void ToggleProjectOpen(string id)
        {
            Mutate(delegate(AppData d)
            {
                ProjectNode n = ProjectTree.ById(d, id);
                if (n == null) return;
                n.Open = !n.IsOpen;
            });
        }

        /// <summary>指定展开 / 收起（点了「加小项目」就把这一格摊开，不然输入框没处放）。</summary>
        public void SetProjectOpen(string id, bool open)
        {
            Mutate(delegate(AppData d)
            {
                ProjectNode n = ProjectTree.ById(d, id);
                if (n == null) return;
                n.Open = open;
            });
        }

        /// <summary>项目页的「全部展开 / 全部收起」。</summary>
        public void SetAllProjectsOpen(bool open)
        {
            Mutate(delegate(AppData d)
            {
                List<ProjectNode> all = ProjectTree.All(d);
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].IsContainer) all[i].Open = open;
            });
        }

        /// <summary>给「大项目 / 分段」那一行定份数：0 = 不分份，回到按小项目算（小项目本身就是一条事项，不分份）。</summary>
        public void SetProjectSteps(string id, int steps)
        {
            Mutate(delegate(AppData d)
            {
                ProjectNode n = ProjectTree.ById(d, id);
                if (n == null || n.Kind == ProjectKind.Sub) return;
                if (steps < 0) steps = 0;
                if (steps > ProjectNode.MaxSteps) steps = ProjectNode.MaxSteps;
                n.Steps = steps;
                if (n.Reached > steps) n.Reached = steps;
            });
        }

        /// <summary>拖横条：这个大项目已经办到第几份。</summary>
        public void SetProjectReached(string id, int reached)
        {
            Mutate(delegate(AppData d)
            {
                ProjectNode n = ProjectTree.ById(d, id);
                if (n == null || n.Steps <= 0) return;
                if (reached < 0) reached = 0;
                if (reached > n.Steps) reached = n.Steps;
                n.Reached = reached;
            });
        }

        /// <summary>
        /// 容器那一行的「完成」小按钮 —— 跟任务行上的勾选框一个意思：点一下整段办完，再点撤销。
        ///   · 底下的小项目：全部勾上（连完成时间一起记）/ 全部撤销；
        ///   · 自己分的份数（大项目那几份、分段那「办完了」的 1 份）：置满 / 清零；
        ///   · 底下什么都没有的空容器：记成「1 份、已办」，免得点一下没反应。
        /// 返回点完之后是不是「办完了」（界面照这个放礼花）。
        /// </summary>
        public bool ToggleProjectDone(string id)
        {
            bool now = false;
            Mutate(delegate(AppData d)
            {
                ProjectNode n = ProjectTree.ById(d, id);
                if (n == null) return;
                now = !ProjectTree.IsDone(d, id);
                List<ProjectNode> leaves = ProjectTree.LeavesUnder(d, id);
                for (int i = 0; i < leaves.Count; i++)
                {
                    TaskItem it = ProjectTree.ItemOf(d, leaves[i]);
                    if (it == null) continue;
                    it.Done = now;
                    it.DoneAt = now ? (DateTime?)DateTime.Now : null;
                }
                // 分段那「1 份」只是「办完了」的记号（分段的份数不给用户调）：撤销时连这条记号一起撤掉，
                // 免得一个空分段撤销完还永远挂着 1 份、把它爹的「已竟 x / y」越算越大。
                bool justMark = n.Kind != ProjectKind.Big && n.Steps <= 1 && leaves.Count == 0;
                if (n.Steps > 0)
                {
                    if (now) n.Reached = n.Steps;
                    else if (justMark) { n.Steps = 0; n.Reached = 0; }
                    else n.Reached = 0;
                }
                else if (now && leaves.Count == 0) { n.Steps = 1; n.Reached = 1; }
            });
            return now;
        }

        static void RemoveProjectSubtree(AppData d, string nodeId)
        {
            List<ProjectNode> kids = ProjectTree.Children(d, nodeId);
            for (int i = 0; i < kids.Count; i++) RemoveProjectSubtree(d, kids[i].Id);
            ProjectNode n = ProjectTree.ById(d, nodeId);
            if (n == null) return;
            TaskItem it = ProjectTree.ItemOf(d, n);
            if (it != null)
            {
                for (int i = 0; i < d.Tasks.Count; i++)
                    if (d.Tasks[i].Id == it.Id) { d.Tasks.RemoveAt(i); break; }
            }
            for (int i = d.Projects.Count - 1; i >= 0; i--)
                if (d.Projects[i].Id == nodeId) d.Projects.RemoveAt(i);
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
