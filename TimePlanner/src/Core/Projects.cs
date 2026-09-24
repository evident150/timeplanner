﻿using System;
using System.Collections.Generic;

namespace TimePlanner.Core
{
    /// <summary>
    /// 项目树的几把螺丝刀：查节点、算进度、把「小项目」和那条普通事项串起来。
    /// 数据本身就是 AppData.Projects 里一个扁平列表（靠 ParentId 连成树），这里只是只读的查询 + 一处修复。
    ///
    /// 三条规矩：
    ///   1. 大项目（Big）只可能在顶层，是容器；
    ///   2. 分段（Stage）只能挂在项目下面，也是容器；
    ///   3. 小项目（Sub）是最低一级，永远不带下級，底下挂一条普通事项（ItemId）——
    ///      所以它会像任务一样出现在今日 / 本周和桌面插件里。
    /// </summary>
    public static class ProjectTree
    {
        public static List<ProjectNode> All(AppData d)
        {
            if (d == null) return new List<ProjectNode>();
            if (d.Projects == null) d.Projects = new List<ProjectNode>();
            return d.Projects;
        }

        public static ProjectNode ById(AppData d, string id)
        {
            if (id == null || id.Length == 0) return null;
            List<ProjectNode> all = All(d);
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Id == id) return all[i];
            return null;
        }

        /// <summary>某个父节点下的子节点，按手工顺序排好。</summary>
        public static List<ProjectNode> Children(AppData d, string parentId)
        {
            string pid = parentId == null ? "" : parentId;
            List<ProjectNode> list = new List<ProjectNode>();
            List<ProjectNode> all = All(d);
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ParentId == pid) list.Add(all[i]);
            list.Sort(CompareNode);
            return list;
        }

        public static List<ProjectNode> Roots(AppData d)
        {
            return Children(d, "");
        }

        static int CompareNode(ProjectNode x, ProjectNode y)
        {
            if (x.Sort != y.Sort) return x.Sort.CompareTo(y.Sort);
            return string.CompareOrdinal(x.Id, y.Id);
        }

        public static bool HasChildren(AppData d, string id)
        {
            if (id == null || id.Length == 0) return false;
            List<ProjectNode> all = All(d);
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ParentId == id) return true;
            return false;
        }

        public static int NextSort(AppData d, string parentId)
        {
            string pid = parentId == null ? "" : parentId;
            int max = -1;
            List<ProjectNode> all = All(d);
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ParentId == pid && all[i].Sort > max) max = all[i].Sort;
            return max + 1;
        }

        /// <summary>节点对应的那条事项（小项目才有）。</summary>
        public static TaskItem ItemOf(AppData d, ProjectNode n)
        {
            if (d == null || n == null || n.ItemId == null || n.ItemId.Length == 0) return null;
            List<TaskItem> tasks = d.Tasks;
            if (tasks == null) return null;
            for (int i = 0; i < tasks.Count; i++)
                if (tasks[i] != null && tasks[i].Id == n.ItemId) return tasks[i];
            return null;
        }

        /// <summary>反查：这条事项属于哪个节点（普通任务返回 null）。</summary>
        public static ProjectNode NodeOfItem(AppData d, string itemId)
        {
            if (itemId == null || itemId.Length == 0) return null;
            List<ProjectNode> all = All(d);
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ItemId == itemId) return all[i];
            return null;
        }

        public static string TitleOf(AppData d, ProjectNode n)
        {
            if (n == null) return "";
            if (n.Title != null && n.Title.Length > 0) return n.Title;
            TaskItem it = ItemOf(d, n);
            return it == null ? "" : it.Title;
        }

        /// <summary>从顶层到这一级的名字，例如「毕业设计 / 开题」。</summary>
        public static string Path(AppData d, string nodeId, bool includeSelf)
        {
            List<string> names = new List<string>();
            ProjectNode n = ById(d, nodeId);
            if (!includeSelf && n != null) n = ById(d, n.ParentId);
            int guard = 0;
            while (n != null && guard++ < 16)
            {
                names.Insert(0, TitleOf(d, n));
                n = ById(d, n.ParentId);
            }
            return string.Join(" / ", names.ToArray()).Trim();
        }

        /// <summary>
        /// 这一级的完成情况：自己分的那几份 + 底下所有「小项目」和它们各自分的份。
        /// 例：分段「第一章」分 8 份、已办 3 份，那它自己就是 3/8；大项目再往上累加。
        /// </summary>
        public static void Count(AppData d, string nodeId, out int total, out int done)
        {
            total = 0; done = 0;
            AddOwn(ById(d, nodeId), ref total, ref done);
            CountInto(d, nodeId, ref total, ref done, 0);
        }

        /// <summary>只数「小项目」（页头统计这类地方用，不含大项目那几份）。</summary>
        public static void CountSubs(AppData d, string nodeId, out int total, out int done)
        {
            total = 0; done = 0;
            SubsInto(d, nodeId, ref total, ref done, 0);
        }

        static void SubsInto(AppData d, string nodeId, ref int total, ref int done, int guard)
        {
            if (guard > 16) return;
            List<ProjectNode> kids = Children(d, nodeId);
            for (int i = 0; i < kids.Count; i++)
            {
                ProjectNode kid = kids[i];
                if (kid.Kind == ProjectKind.Sub)
                {
                    total++;
                    TaskItem it = ItemOf(d, kid);
                    if (it != null && it.Done) done++;
                }
                else SubsInto(d, kid.Id, ref total, ref done, guard + 1);
            }
        }

        /// <summary>节点自己那几份（大项目分了几份，或分段那「办完了」的 1 份）。</summary>
        static void AddOwn(ProjectNode n, ref int total, ref int done)
        {
            if (n == null || n.Kind == ProjectKind.Sub || n.Steps <= 0) return;
            int steps = n.Steps > ProjectNode.MaxSteps ? ProjectNode.MaxSteps : n.Steps;
            int reached = n.Reached < 0 ? 0 : n.Reached;
            if (reached > steps) reached = steps;
            total += steps;
            done += reached;
        }

        static void CountInto(AppData d, string nodeId, ref int total, ref int done, int guard)
        {
            if (guard > 16) return;
            List<ProjectNode> kids = Children(d, nodeId);
            for (int i = 0; i < kids.Count; i++)
            {
                ProjectNode kid = kids[i];
                if (kid.Kind == ProjectKind.Sub)
                {
                    total++;
                    TaskItem it = ItemOf(d, kid);
                    if (it != null && it.Done) done++;
                }
                else
                {
                    AddOwn(kid, ref total, ref done);
                    CountInto(d, kid.Id, ref total, ref done, guard + 1);
                }
            }
        }

        /// <summary>这个节点是不是「全办完了」：自己分的份满了，底下的小项目也全勾了。</summary>
        public static bool IsDone(AppData d, string nodeId)
        {
            ProjectNode n = ById(d, nodeId);
            if (n == null) return false;
            List<ProjectNode> leaves = LeavesUnder(d, nodeId);
            if (n.Steps <= 0 && leaves.Count == 0) return false;      // 底下什么都没有的不算办完（点一下才会记成 1 份）
            if (n.Steps > 0 && n.Reached < n.Steps) return false;
            for (int i = 0; i < leaves.Count; i++)
            {
                TaskItem it = ItemOf(d, leaves[i]);
                if (it != null && !it.Done) return false;
            }
            return true;
        }

        /// <summary>子树里所有小项目（按树上的顺序）。</summary>
        public static List<ProjectNode> LeavesUnder(AppData d, string nodeId)
        {
            List<ProjectNode> list = new List<ProjectNode>();
            LeavesInto(d, nodeId, list, 0);
            return list;
        }

        static void LeavesInto(AppData d, string nodeId, List<ProjectNode> into, int guard)
        {
            if (guard > 16) return;
            List<ProjectNode> kids = Children(d, nodeId);
            for (int i = 0; i < kids.Count; i++)
            {
                if (kids[i].Kind == ProjectKind.Sub) into.Add(kids[i]);
                else LeavesInto(d, kids[i].Id, into, guard + 1);
            }
        }

        /// <summary>同名的小项目全算上，用来给「今日 / 本周」的角标计数。</summary>
        public static int OpenCount(AppData d)
        {
            int n = 0;
            List<TaskItem> tasks = d == null ? null : d.Tasks;
            if (tasks == null) return 0;
            for (int i = 0; i < tasks.Count; i++)
            {
                TaskItem t = tasks[i];
                if (t == null || t.ProjectId == null || t.ProjectId.Length == 0 || t.Done) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// 数据自检：把手工改坏 / 版本错位造成的不一致掰回来。载入时调一次，不改动正常数据。
        ///   · 父节点没了的 → 挂回顶层；
        ///   · 小项目没有自己的事项 → 补一条（标题沿用节点名）；
        ///   · 小项目底下居然有下級 → 当成「分段」，它那条事项退回成普通任务；
        ///   · 事项指着不存在的项目 → 当成普通任务。
        /// </summary>
        public static void Repair(AppData d)
        {
            if (d == null) return;
            List<ProjectNode> all = All(d);
            List<TaskItem> tasks = d.Tasks == null ? new List<TaskItem>() : d.Tasks;

            // 父节点没了就挂回顶层
            for (int i = 0; i < all.Count; i++)
            {
                ProjectNode n = all[i];
                if (n.ParentId.Length == 0) continue;
                if (ById(d, n.ParentId) == null) n.ParentId = "";
            }

            // 小项目：底下不该有下級；也必须有自己的事项
            for (int i = 0; i < all.Count; i++)
            {
                ProjectNode n = all[i];
                if (n.Kind != ProjectKind.Sub) { n.ItemId = ""; continue; }
                if (HasChildren(d, n.Id))
                {
                    n.Kind = ProjectKind.Stage;
                    TaskItem orphan = ItemOf(d, n);
                    if (orphan != null) { orphan.ProjectId = ""; n.ItemId = ""; }
                    continue;
                }
                TaskItem it = ItemOf(d, n);
                if (it == null)
                {
                    it = TaskItem.Create(TitleOf(d, n), DateTime.Today);
                    it.ProjectId = n.Id;
                    n.ItemId = it.Id;
                    tasks.Add(it);
                }
                else it.ProjectId = n.Id;
            }

            if (d.Tasks == null) d.Tasks = tasks;

            // 份数：别让手工改坏的数据分出一万格，也别让「已办份数」超过总份数
            for (int i = 0; i < all.Count; i++)
            {
                ProjectNode n = all[i];
                if (n.Steps < 0) n.Steps = 0;
                if (n.Steps > ProjectNode.MaxSteps) n.Steps = ProjectNode.MaxSteps;
                if (n.Reached < 0) n.Reached = 0;
                if (n.Reached > n.Steps) n.Reached = n.Steps;
                if (n.Kind == ProjectKind.Sub) { n.Steps = 0; n.Reached = 0; }        // 小项目本身就是一条事项
            }

            // 事项指着不存在的项目（或指着容器）→ 退回成普通任务
            for (int i = 0; i < d.Tasks.Count; i++)
            {
                TaskItem t = d.Tasks[i];
                if (t == null || t.ProjectId == null || t.ProjectId.Length == 0) continue;
                ProjectNode owner = ById(d, t.ProjectId);
                if (owner == null || owner.Kind != ProjectKind.Sub) t.ProjectId = "";
            }
        }
    }
}
