using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace TimePlanner.Core
{
    /// <summary>账本里的一条：上次推上去的内容指纹 + 那一行的版本号 + 是谁勾完的。</summary>
    [DataContract]
    public class CloudRec
    {
        [DataMember(Name = "h")] public string H;
        [DataMember(Name = "rev")] public int Rev;
        [DataMember(Name = "by")] public string By;
    }

    /// <summary>
    /// 同步账本。**故意不写进 data.json**：那份文件是经典版 / 特别版共用的，
    /// 往里加字段会被另一条线的旧版本在下次保存时抹掉，账本一丢就等于要全量重来。
    /// </summary>
    [DataContract]
    public class CloudLedger
    {
        [DataMember(Name = "tasks")] public Dictionary<string, CloudRec> Tasks;
        [DataMember(Name = "projects")] public Dictionary<string, CloudRec> Projects;
        /// <summary>已经在云端打过删除标记的 id。有人又在别处把它救活了，就以活着的为准。</summary>
        [DataMember(Name = "tomb")] public Dictionary<string, string> Tomb;

        public void Ensure()
        {
            if (Tasks == null) Tasks = new Dictionary<string, CloudRec>();
            if (Projects == null) Projects = new Dictionary<string, CloudRec>();
            if (Tomb == null) Tomb = new Dictionary<string, string>();
        }
    }

    /// <summary>合并结果，用来给用户一句人话。</summary>
    public class MergeReport
    {
        public int Added;
        public int Updated;
        public int Removed;
        public int Conflicts;

        public string Describe()
        {
            if (Added == 0 && Updated == 0 && Removed == 0 && Conflicts == 0) return "云端没有新东西，本地也没动";
            StringBuilder sb = new StringBuilder();
            if (Added > 0) sb.Append("收下 ").Append(Added).Append(" 条新的");
            if (Updated > 0) { if (sb.Length > 0) sb.Append('，'); sb.Append("更新 ").Append(Updated).Append(" 条"); }
            if (Removed > 0) { if (sb.Length > 0) sb.Append('，'); sb.Append("别人删的 ").Append(Removed).Append(" 条本地也删了"); }
            if (Conflicts > 0) { if (sb.Length > 0) sb.Append('；'); sb.Append(Conflicts).Append(" 条两边都改了，先留你这份，下次推上去"); }
            return sb.ToString();
        }
    }

    /// <summary>同步的方向。上传只管推，下载只管拉。</summary>
    public enum SyncWay { Both, Upload, Download }

    /// <summary>
    /// 一趟同步分三段，是为了躲开两个坑：
    ///   · 网络绝不能放在界面线程上（一卡就是十几秒，抗卡死那套全废）；
    ///   · 后台线程绝不能去读界面线程正在改的列表（会撞出「集合已修改」）。
    /// 于是：Prepare / Finish 都在界面线程碰数据，Send 只在后台碰网络，中间不交叉。
    /// </summary>
    public class SyncJob
    {
        public SyncWay Way;
        public List<CloudTask> PushTasks = new List<CloudTask>();
        public List<CloudProject> PushProjects = new List<CloudProject>();
        public List<string> GoneTasks = new List<string>();
        public List<string> GoneProjects = new List<string>();
        public Dictionary<string, string> TaskHash = new Dictionary<string, string>();
        public Dictionary<string, string> ProjectHash = new Dictionary<string, string>();
        public List<CloudTask> RemoteTasks;
        public List<CloudProject> RemoteProjects;
        public MergeReport Report = new MergeReport();
        public int Pushed;
    }

    /// <summary>
    /// 多人共用一份计划的同步。
    /// 推：只推「本地变过」的（跟账本里的指纹比），从不整份覆盖云端；
    /// 拉：整份拉回来按 id + 版本号合，**永远不真删**，删除一律走软删标记；
    /// 保险：合并前先给 data.json 留一份快照；云端空、本地满、账本又空时直接拒收。
    ///
    /// ponytail: 拉取是全量，没做增量水位。一份家庭 / 小团队的计划也就几百行、几十 KB，
    /// 全量最省事也最不会漏；真大到卡了再加 updated_at 水位。
    /// ponytail: 服务端没做乐观锁强制，同一行两人同时改按后到的赢。勾选本身幂等，
    /// 两边真撞上会提示「两边都改了」，不会闷声覆盖。
    /// </summary>
    public static class NetSync
    {
        static CloudLedger _ledger;

        public static string LedgerPath { get { return Path.Combine(NetCloud.CloudDir, "ledger.json"); } }

        public static CloudLedger Ledger
        {
            get
            {
                if (_ledger == null)
                {
                    try
                    {
                        if (File.Exists(LedgerPath))
                            _ledger = NetCloud.Json<CloudLedger>(File.ReadAllText(LedgerPath, Encoding.UTF8));
                    }
                    catch (Exception) { _ledger = null; }
                    if (_ledger == null) _ledger = new CloudLedger();
                    _ledger.Ensure();
                }
                return _ledger;
            }
        }

        static void SaveLedger()
        {
            try { File.WriteAllText(LedgerPath, NetCloud.Json(Ledger), new UTF8Encoding(false)); }
            catch (Exception ex) { Diagnostics.Log("云端", "账本存不下来：" + ex.Message); }
        }

        /// <summary>换工作区就清账本 —— 拿 A 的账本去合 B 的数据，只会越合越乱。</summary>
        public static void ResetLedger()
        {
            _ledger = new CloudLedger();
            _ledger.Ensure();
            SaveLedger();
        }

        // ---------------- 指纹 ----------------

        public static string HashTask(TaskItem t)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(t.Title).Append('\u0001').Append(t.Note).Append('\u0001')
              .Append(t.Date.Date.Ticks).Append('\u0001').Append(t.Done ? '1' : '0').Append('\u0001')
              .Append(t.DoneAt.HasValue ? t.DoneAt.Value.Ticks.ToString() : "").Append('\u0001')
              .Append(t.Priority).Append('\u0001').Append(t.Tag).Append('\u0001')
              .Append(t.Sort).Append('\u0001').Append(t.ProjectId);
            return Digest(sb.ToString());
        }

        public static string HashProject(ProjectNode n)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(n.ParentId).Append('\u0001').Append(n.Kind).Append('\u0001').Append(n.ItemId).Append('\u0001')
              .Append(n.Sort).Append('\u0001').Append(n.Open != false ? '1' : '0').Append('\u0001')
              .Append(n.Title).Append('\u0001').Append(n.Steps).Append('\u0001').Append(n.Reached);
            return Digest(sb.ToString());
        }

        static string Digest(string s)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                StringBuilder sb = new StringBuilder(h.Length * 2);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------------- 第一段：准备（界面线程） ----------------

        /// <summary>把「本地要推什么」算出来。只在界面线程调用。</summary>
        public static SyncJob Prepare(Store store, SyncWay way)
        {
            SyncJob job = new SyncJob();
            job.Way = way;
            if (!NetCloud.Configured) throw new CloudException("还没填云端地址");
            if (!NetCloud.SignedIn) throw new CloudException("还没登录");
            NetCloud.CurrentWorkspace();          // 没选工作区就在这里拦下

            // 先把待写的改动落盘，等下那份快照才是最新的。
            try { store.Flush(); } catch (Exception) { }

            if (way == SyncWay.Download)
            {
                SnapshotBeforeMerge();
                return job;
            }

            CloudLedger L = Ledger;
            AppData d = store.Data;
            string ws = NetCloud.CurrentWorkspace();
            string me = NetCloud.Session == null ? null : NetCloud.Session.Uid;

            for (int i = 0; i < d.Tasks.Count; i++)
            {
                TaskItem t = d.Tasks[i];
                string h = HashTask(t);
                job.TaskHash[t.Id] = h;
                CloudRec rec;
                bool have = L.Tasks.TryGetValue(t.Id, out rec);
                if (have && rec.H == h) continue;                 // 没变过，别白推
                CloudTask row = ToCloud(t, ws, me, have ? rec : null);
                row.Rev = (have ? rec.Rev : 0) + 1;
                job.PushTasks.Add(row);
            }
            foreach (KeyValuePair<string, CloudRec> kv in L.Tasks)
                if (!job.TaskHash.ContainsKey(kv.Key)) job.GoneTasks.Add(kv.Key);   // 本地没了 = 被删了

            for (int i = 0; i < d.Projects.Count; i++)
            {
                ProjectNode n = d.Projects[i];
                string h = HashProject(n);
                job.ProjectHash[n.Id] = h;
                CloudRec rec;
                bool have = L.Projects.TryGetValue(n.Id, out rec);
                if (have && rec.H == h) continue;
                CloudProject row = ToCloud(n, ws, me);
                row.Rev = (have ? rec.Rev : 0) + 1;
                job.PushProjects.Add(row);
            }
            foreach (KeyValuePair<string, CloudRec> kv in L.Projects)
                if (!job.ProjectHash.ContainsKey(kv.Key)) job.GoneProjects.Add(kv.Key);

            if (way == SyncWay.Both) SnapshotBeforeMerge();
            return job;
        }

        // ---------------- 第二段：发（后台线程，别碰 store） ----------------

        public static void Send(SyncJob job)
        {
            if (job.Way != SyncWay.Download)
            {
                NetCloud.UpsertTasks(job.PushTasks);
                NetCloud.SoftDelete("tasks", job.GoneTasks);
                NetCloud.UpsertProjects(job.PushProjects);
                NetCloud.SoftDelete("projects", job.GoneProjects);
                job.Pushed = job.PushTasks.Count + job.PushProjects.Count;
            }
            if (job.Way != SyncWay.Upload)
            {
                job.RemoteTasks = NetCloud.FetchTasks();
                job.RemoteProjects = NetCloud.FetchProjects();
            }
        }

        // ---------------- 第三段：收（界面线程） ----------------

        public static MergeReport Finish(Store store, SyncJob job)
        {
            CloudLedger L = Ledger;

            for (int i = 0; i < job.PushTasks.Count; i++)
            {
                CloudRec rec = new CloudRec();
                rec.H = job.TaskHash[job.PushTasks[i].Id];
                rec.Rev = job.PushTasks[i].Rev;
                rec.By = job.PushTasks[i].CompletedBy;
                L.Tasks[job.PushTasks[i].Id] = rec;
            }
            for (int i = 0; i < job.GoneTasks.Count; i++)
            {
                L.Tasks.Remove(job.GoneTasks[i]);
                L.Tomb[job.GoneTasks[i]] = DateTime.Now.ToString("s");
            }
            for (int i = 0; i < job.PushProjects.Count; i++)
            {
                CloudRec rec = new CloudRec();
                rec.H = job.ProjectHash[job.PushProjects[i].Id];
                rec.Rev = job.PushProjects[i].Rev;
                L.Projects[job.PushProjects[i].Id] = rec;
            }
            for (int i = 0; i < job.GoneProjects.Count; i++)
            {
                L.Projects.Remove(job.GoneProjects[i]);
                L.Tomb[job.GoneProjects[i]] = DateTime.Now.ToString("s");
            }

            if (job.Way == SyncWay.Upload)
            {
                SaveLedger();
                NetCloud.TouchLastSync();
                return job.Report;
            }

            // 保险栓：云端空、本地满、账本也空 —— 八成是进错工作区或者地址配错了。
            // 这时候要是老实「以云端为准」，本地那堆计划就全没了。
            AppData local = store.Data;
            if (job.RemoteTasks.Count == 0 && local.Tasks.Count > 0 && L.Tasks.Count == 0)
                throw new CloudException("云端这份工作区是空的，本地却有 " + local.Tasks.Count + " 条任务。"
                    + "先别下载 —— 确认一下是不是进错了工作区（云端地址填错也会这样）。");

            MergeHolder hold = new MergeHolder();
            store.Mutate(delegate(AppData d)
            {
                hold.Report = NetMerge.Apply(d, job.RemoteTasks, job.RemoteProjects, L);
            });
            job.Report = hold.Report;
            SaveLedger();
            NetCloud.TouchLastSync();
            return job.Report;
        }

        class MergeHolder { public MergeReport Report; }

        /// <summary>合并前留一份 data.json：多人共用时「谁改的」经常说不清，能倒回去比什么都强。</summary>
        static void SnapshotBeforeMerge()
        {
            try
            {
                string src = Store.DataFile;
                if (!File.Exists(src)) return;
                string dir = Path.Combine(Store.DataDir, "snapshots");
                Directory.CreateDirectory(dir);
                string full = Path.Combine(dir, "data-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-cloud.json");
                File.Copy(src, full, true);
                string[] files = Directory.GetFiles(dir, "data-*.json");
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length - 12; i++) { try { File.Delete(files[i]); } catch (Exception) { } }
            }
            catch (Exception) { }
        }

        static CloudTask ToCloud(TaskItem t, string ws, string me, CloudRec seen)
        {
            CloudTask r = new CloudTask();
            r.Id = t.Id;
            r.WorkspaceId = ws;
            r.Title = t.Title;
            r.Note = t.Note;
            r.Date = NetCloud.Day(t.Date);
            r.Done = t.Done;
            r.DoneAt = t.DoneAt.HasValue ? NetCloud.Iso(t.DoneAt.Value) : null;
            r.Priority = t.Priority;
            r.Tag = t.Tag;
            r.Sort = t.Sort;
            r.ProjectId = t.ProjectId;
            r.CreatedBy = me;
            // 谁勾完的：账本里记着就沿用（可能是别人的功劳）；没记过又已经是完成态，那就是我勾的。
            if (!t.Done) r.CompletedBy = null;
            else if (seen != null && seen.By != null) r.CompletedBy = seen.By;
            else r.CompletedBy = me;
            return r;
        }

        static CloudProject ToCloud(ProjectNode n, string ws, string me)
        {
            CloudProject r = new CloudProject();
            r.Id = n.Id;
            r.WorkspaceId = ws;
            r.ParentId = n.ParentId;
            r.Kind = n.Kind;
            r.ItemId = n.ItemId;
            r.Sort = n.Sort;
            r.Open = n.Open != false;
            r.Title = n.Title;
            r.Steps = n.Steps;
            r.Reached = n.Reached;
            r.CreatedBy = me;
            return r;
        }
    }

    /// <summary>
    /// 纯函数式的合并：只动传进来的 AppData 和账本，不碰磁盘、不发网络。
    /// 单独拎出来是为了能跑自检（tools\cloud-selftest.ps1）。
    /// </summary>
    public static class NetMerge
    {
        public static MergeReport Apply(AppData d, List<CloudTask> tasks, List<CloudProject> projects, CloudLedger L)
        {
            MergeReport rep = new MergeReport();
            L.Ensure();

            Dictionary<string, TaskItem> mine = new Dictionary<string, TaskItem>();
            for (int i = 0; i < d.Tasks.Count; i++) mine[d.Tasks[i].Id] = d.Tasks[i];

            for (int i = 0; i < tasks.Count; i++)
            {
                CloudTask row = tasks[i];

                if (!string.IsNullOrEmpty(row.DeletedAt))
                {
                    TaskItem dead;
                    if (mine.TryGetValue(row.Id, out dead)) { d.Tasks.Remove(dead); mine.Remove(row.Id); rep.Removed++; }
                    L.Tasks.Remove(row.Id);
                    L.Tomb[row.Id] = row.DeletedAt;
                    continue;
                }
                // 我们这边删过、云端又活了过来 —— 有人在别处恢复了，以活着的为准。
                if (L.Tomb.ContainsKey(row.Id)) L.Tomb.Remove(row.Id);

                TaskItem cur;
                if (!mine.TryGetValue(row.Id, out cur))
                {
                    TaskItem fresh = FromCloud(row);
                    d.Tasks.Add(fresh);
                    mine[row.Id] = fresh;
                    L.Tasks[row.Id] = Seen(row, NetSync.HashTask(fresh));
                    rep.Added++;
                    continue;
                }

                CloudRec rec;
                bool have = L.Tasks.TryGetValue(row.Id, out rec);
                string localHash = NetSync.HashTask(cur);
                bool localChanged = !have || rec.H != localHash;
                bool remoteChanged = !have || rec.Rev != row.Rev;
                if (!remoteChanged) continue;

                if (localChanged)
                {
                    // 两边都动了：留本地，把版本号顶到比云端高，下次推就能盖过去。
                    rep.Conflicts++;
                    CloudRec bump = have ? rec : new CloudRec();
                    if (bump.Rev < row.Rev) bump.Rev = row.Rev;
                    if (bump.H == null) bump.H = localHash;
                    L.Tasks[row.Id] = bump;
                    continue;
                }

                // 只有云端动过、本地没动 —— 这才允许覆盖，而且只覆盖字段，不换对象引用。
                CopyInto(cur, row);
                L.Tasks[row.Id] = Seen(row, NetSync.HashTask(cur));
                rep.Updated++;
            }

            Dictionary<string, ProjectNode> myProj = new Dictionary<string, ProjectNode>();
            for (int i = 0; i < d.Projects.Count; i++) myProj[d.Projects[i].Id] = d.Projects[i];

            for (int i = 0; i < projects.Count; i++)
            {
                CloudProject row = projects[i];
                if (!string.IsNullOrEmpty(row.DeletedAt))
                {
                    ProjectNode dead;
                    if (myProj.TryGetValue(row.Id, out dead))
                    {
                        Store.RemoveProjectSubtree(d, dead.Id);      // 连子节点带挂着的任务一起摘
                        myProj.Clear();
                        for (int k = 0; k < d.Projects.Count; k++) myProj[d.Projects[k].Id] = d.Projects[k];
                        rep.Removed++;
                    }
                    L.Projects.Remove(row.Id);
                    L.Tomb[row.Id] = row.DeletedAt;
                    continue;
                }
                if (L.Tomb.ContainsKey(row.Id)) L.Tomb.Remove(row.Id);

                ProjectNode cur;
                if (!myProj.TryGetValue(row.Id, out cur))
                {
                    ProjectNode fresh = FromCloud(row);
                    d.Projects.Add(fresh);
                    myProj[row.Id] = fresh;
                    L.Projects[row.Id] = SeenP(row, NetSync.HashProject(fresh));
                    rep.Added++;
                    continue;
                }

                CloudRec rec;
                bool have = L.Projects.TryGetValue(row.Id, out rec);
                string localHash = NetSync.HashProject(cur);
                bool localChanged = !have || rec.H != localHash;
                bool remoteChanged = !have || rec.Rev != row.Rev;
                if (!remoteChanged) continue;

                if (localChanged)
                {
                    rep.Conflicts++;
                    CloudRec bump = have ? rec : new CloudRec();
                    if (bump.Rev < row.Rev) bump.Rev = row.Rev;
                    if (bump.H == null) bump.H = localHash;
                    L.Projects[row.Id] = bump;
                    continue;
                }

                CopyInto(cur, row);
                L.Projects[row.Id] = SeenP(row, NetSync.HashProject(cur));
                rep.Updated++;
            }

            return rep;
        }

        static CloudRec Seen(CloudTask row, string hash)
        {
            CloudRec rec = new CloudRec();
            rec.H = hash;
            rec.Rev = row.Rev;
            rec.By = row.CompletedBy;
            return rec;
        }

        static CloudRec SeenP(CloudProject row, string hash)
        {
            CloudRec rec = new CloudRec();
            rec.H = hash;
            rec.Rev = row.Rev;
            return rec;
        }

        static TaskItem FromCloud(CloudTask r)
        {
            TaskItem t = new TaskItem();
            t.Id = r.Id;
            t.CreatedAt = DateTime.Now;
            CopyInto(t, r);
            return t;
        }

        static void CopyInto(TaskItem t, CloudTask r)
        {
            t.Title = r.Title;
            t.Note = r.Note;
            t.Date = NetCloud.ParseDay(r.Date, t.Date == default(DateTime) ? DateTime.Today : t.Date);
            t.Done = r.Done;
            t.DoneAt = NetCloud.ParseIso(r.DoneAt);
            t.Priority = r.Priority;
            t.Tag = r.Tag;
            t.Sort = r.Sort;
            t.ProjectId = r.ProjectId;
            t.Normalize();
        }

        static ProjectNode FromCloud(CloudProject r)
        {
            ProjectNode n = new ProjectNode();
            n.Id = r.Id;
            CopyInto(n, r);
            return n;
        }

        static void CopyInto(ProjectNode n, CloudProject r)
        {
            n.ParentId = r.ParentId;
            n.Kind = r.Kind;
            n.ItemId = r.ItemId;
            n.Sort = r.Sort;
            n.Open = r.Open;
            n.Title = r.Title;
            n.Steps = r.Steps;
            n.Reached = r.Reached;
            n.Normalize();
        }
    }
}
