using System;
using System.Collections.Generic;
using TimePlanner.Core;

/// <summary>
/// 合并逻辑的自检：只碰内存里的对象，不读 %APPDATA%、不发网络、不写任何文件。
/// 跑法：powershell -NoProfile -ExecutionPolicy Bypass -File tools\\cloud-selftest.ps1
/// </summary>
internal static class CloudSelfTest
{
    static int fails;

    static void Check(bool ok, string what)
    {
        if (ok) Console.WriteLine("  ok    " + what);
        else { fails++; Console.WriteLine("  FAIL  " + what); }
    }

    static CloudTask Row(string id, string title, string day, int rev)
    {
        CloudTask r = new CloudTask();
        r.Id = id;
        r.Title = title;
        r.Date = day;
        r.Rev = rev;
        r.Note = "";
        r.Tag = "";
        r.ProjectId = "";
        return r;
    }

    static CloudLedger NewLedger()
    {
        CloudLedger L = new CloudLedger();
        L.Ensure();
        return L;
    }

    static int Main()
    {
        Console.WriteLine("云端合并自检");

        // ---- 1. 云端有新任务，本地收下 ----
        AppData d = new AppData();
        d.Normalize();
        CloudLedger L = NewLedger();
        List<CloudTask> rows = new List<CloudTask>();
        rows.Add(Row("a", "买菜", "2026-10-08", 1));

        MergeReport rep = NetMerge.Apply(d, rows, new List<CloudProject>(), L);
        Check(rep.Added == 1 && d.Tasks.Count == 1 && d.Tasks[0].Title == "买菜", "云端新任务收进本地");
        Check(L.Tasks.ContainsKey("a") && L.Tasks["a"].Rev == 1 && L.Tasks["a"].H == NetSync.HashTask(d.Tasks[0]),
              "账本记下指纹与版本号");

        // ---- 2. 只有云端动过，本地跟着更新 ----
        rows[0].Title = "买菜改成买肉";
        rows[0].Rev = 2;
        rep = NetMerge.Apply(d, rows, new List<CloudProject>(), L);
        Check(rep.Updated == 1 && rep.Conflicts == 0 && d.Tasks[0].Title == "买菜改成买肉", "只云端变：本地跟着更新");

        // ---- 3. 两边都动了，先留本地，版本号顶平 ----
        d.Tasks[0].Title = "本地改的";
        rows[0].Title = "云端也改了";
        rows[0].Rev = 3;
        rep = NetMerge.Apply(d, rows, new List<CloudProject>(), L);
        Check(rep.Conflicts == 1 && d.Tasks[0].Title == "本地改的", "两边都改：先留本地那份");
        Check(L.Tasks["a"].Rev == 3, "冲突后版本号顶到与云端齐平（下次推就能盖过去）");

        // ---- 4. 云端软删，本地跟着删，并记墓碑 ----
        CloudTask dead = Row("a", "本地改的", "2026-10-08", 4);
        dead.DeletedAt = "2026-10-08T10:00:00";
        List<CloudTask> one = new List<CloudTask>();
        one.Add(dead);
        rep = NetMerge.Apply(d, one, new List<CloudProject>(), L);
        Check(rep.Removed == 1 && d.Tasks.Count == 0, "云端软删：本地也删掉");
        Check(L.Tomb.ContainsKey("a") && !L.Tasks.ContainsKey("a"), "墓碑记下、账本清掉");

        // ---- 5. 我们删过，云端又救活了，以活着的为准 ----
        CloudTask alive = Row("a", "又活过来了", "2026-10-08", 5);
        one.Clear();
        one.Add(alive);
        rep = NetMerge.Apply(d, one, new List<CloudProject>(), L);
        Check(d.Tasks.Count == 1 && d.Tasks[0].Title == "又活过来了" && L.Tomb.Count == 0,
              "云端复活：以活着的为准，墓碑撤销（不跟人抢）");

        // ---- 6. 云端删掉大项目，子树连挂着的任务一起摘掉 ----
        AppData d2 = new AppData();
        d2.Normalize();
        TaskItem leaf = TaskItem.Create("小项目的一件事");
        d2.Tasks.Add(leaf);
        ProjectNode big = new ProjectNode();
        big.Id = "p1"; big.ParentId = ""; big.Kind = ProjectKind.Big; big.Title = "大项目"; big.Sort = 0;
        ProjectNode sub = new ProjectNode();
        sub.Id = "p2"; sub.ParentId = "p1"; sub.Kind = ProjectKind.Sub; sub.ItemId = leaf.Id;
        sub.Title = leaf.Title; sub.Sort = 0;
        d2.Projects.Add(big);
        d2.Projects.Add(sub);
        leaf.ProjectId = "p2";
        d2.Normalize();
        Check(d2.Projects.Count == 2 && d2.Tasks.Count == 1, "自检用的项目树搭好了");

        CloudLedger L2 = NewLedger();
        CloudProject deadProject = new CloudProject();
        deadProject.Id = "p1";
        deadProject.Rev = 2;
        deadProject.DeletedAt = "2026-10-08T11:00:00";
        List<CloudProject> deadProjects = new List<CloudProject>();
        deadProjects.Add(deadProject);
        rep = NetMerge.Apply(d2, new List<CloudTask>(), deadProjects, L2);
        Check(d2.Projects.Count == 0, "云端删掉大项目：子树一起摘掉");
        Check(d2.Tasks.Count == 0, "挂在子树上的那条任务也没了（不会留下一条孤儿）");

        Console.WriteLine(fails == 0 ? "全部通过" : (fails + " 项没过"));
        return fails == 0 ? 0 : 1;
    }
}
