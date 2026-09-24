﻿﻿using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace TimePlanner.Core
{
    /// <summary>一条任务/计划。</summary>
    [DataContract]
    public class TaskItem
    {
        [DataMember(Name = "id", Order = 0)] public string Id;
        [DataMember(Name = "title", Order = 1)] public string Title;
        [DataMember(Name = "note", Order = 2)] public string Note;
        [DataMember(Name = "date", Order = 3)] public DateTime Date;
        [DataMember(Name = "done", Order = 4)] public bool Done;
        [DataMember(Name = "doneAt", Order = 5)] public DateTime? DoneAt;
        [DataMember(Name = "priority", Order = 6)] public int Priority;
        [DataMember(Name = "tag", Order = 7)] public string Tag;
        [DataMember(Name = "sort", Order = 8)] public int Sort;
        [DataMember(Name = "createdAt", Order = 9)] public DateTime CreatedAt;
        /// <summary>属于哪个项目节点（空 = 普通任务）。有值 = 这是一条「小项目」的事项，跟着项目树一起管理。</summary>
        [DataMember(Name = "projectId", Order = 10)] public string ProjectId;

        public DateTime Day { get { return Date.Date; } }

        public static TaskItem Create(string title)
        {
            return Create(title, DateTime.Today);
        }

        public static TaskItem Create(string title, DateTime day)
        {
            TaskItem t = new TaskItem();
            t.Id = Guid.NewGuid().ToString("N");
            t.Title = (title == null ? "" : title.Trim());
            t.Note = "";
            t.Date = day.Date;
            t.Done = false;
            t.DoneAt = null;
            t.Priority = 0;
            t.Tag = "";
            t.Sort = 0;
            t.CreatedAt = DateTime.Now;
            t.ProjectId = "";
            return t;
        }

        public TaskItem Clone()
        {
            TaskItem t = new TaskItem();
            t.Id = Id; t.Title = Title; t.Note = Note; t.Date = Date;
            t.Done = Done; t.DoneAt = DoneAt; t.Priority = Priority;
            t.Tag = Tag; t.Sort = Sort; t.CreatedAt = CreatedAt; t.ProjectId = ProjectId;
            return t;
        }

        /// <summary>补全反序列化后可能缺失的字段。</summary>
        public void Normalize()
        {
            if (Id == null || Id.Length == 0) Id = Guid.NewGuid().ToString("N");
            if (Title == null) Title = "";
            if (Note == null) Note = "";
            if (Tag == null) Tag = "";
            if (ProjectId == null) ProjectId = "";
            Date = Date.Date;
            if (Priority < 0 || Priority > 2) Priority = 0;
        }
    }

    /// <summary>项目节点的级别。</summary>
    public static class ProjectKind
    {
        public const int Big = 0;     // 大项目：顶层容器，下面挂小项目或分段
        public const int Sub = 1;     // 小项目：最低一级，本身就是一条事项
        public const int Stage = 2;   // 分段：大项目内部的分组

        public static string Label(int k)
        {
            if (k == Big) return "大项目";
            if (k == Stage) return "分段";
            return "小项目";
        }

        /// <summary>容器只用来归拢下級，自己不出现在任务栏里。</summary>
        public static bool IsContainer(int k) { return k != Sub; }
    }

    /// <summary>
    /// 项目树上的一个节点。两种角色：
    ///   · 容器（大项目 / 分段）—— 只归拢下級，自己不是待办；
    ///   · 小项目（最低一级）—— 底下挂一条普通事项（ItemId），于是它会像任务一样
    ///     出现在「今日 / 本周」和桌面插件里，可以勾选、可以拖动改期。
    /// 节点的名字就取那条事项的标题（单一来源，不会两边对不上）。
    /// </summary>
    [DataContract]
    public class ProjectNode
    {
        [DataMember(Name = "id", Order = 0)] public string Id;
        [DataMember(Name = "parentId", Order = 1)] public string ParentId;
        [DataMember(Name = "kind", Order = 2)] public int Kind;
        [DataMember(Name = "itemId", Order = 3)] public string ItemId;
        [DataMember(Name = "sort", Order = 4)] public int Sort;
        /// <summary>项目页里是否展开（界面状态，顺手存下来）。</summary>
        [DataMember(Name = "open", Order = 5)] public bool? Open;
        /// <summary>节点名（也是它那条事项的标题，两边由 Store 一起改，不会对不上）。</summary>
        [DataMember(Name = "title", Order = 6)] public string Title;
        /// <summary>大项目分几份（0 = 没分份）。分了份就能在项目页拖横条记「几分之几」，不必一条条写小项目。</summary>
        [DataMember(Name = "steps", Order = 7)] public int Steps;
        /// <summary>已经办到的份数（0..Steps）。</summary>
        [DataMember(Name = "reached", Order = 8)] public int Reached;

        /// <summary>一份一段最多分多少份（防止手写数据分出一万格）。</summary>
        public const int MaxSteps = 64;

        public bool IsOpen { get { return Open != false; } }
        public bool IsContainer { get { return ProjectKind.IsContainer(Kind); } }
        /// <summary>是不是按份数计（大项目分了几份，或分段那「办完了」的 1 份）。</summary>
        public bool HasSteps { get { return Kind != ProjectKind.Sub && Steps > 0; } }

        public static ProjectNode Create(string parentId, int kind)
        {
            ProjectNode n = new ProjectNode();
            n.Id = Guid.NewGuid().ToString("N");
            n.ParentId = parentId == null ? "" : parentId;
            n.Kind = kind;
            n.Title = "";
            n.ItemId = "";
            n.Sort = 0;
            n.Open = true;
            return n;
        }

        public void Normalize()
        {
            if (Id == null || Id.Length == 0) Id = Guid.NewGuid().ToString("N");
            if (ParentId == null) ParentId = "";
            if (ItemId == null) ItemId = "";
            if (Title == null) Title = "";
            if (Kind != ProjectKind.Big && Kind != ProjectKind.Sub && Kind != ProjectKind.Stage) Kind = ProjectKind.Sub;
            if (Kind == ProjectKind.Big) ParentId = "";        // 大项目只可能在顶层
            if (Open == null) Open = true;
            if (Steps < 0) Steps = 0;
            if (Steps > MaxSteps) Steps = MaxSteps;
            if (Reached < 0) Reached = 0;
            if (Reached > Steps) Reached = Steps;
            if (Kind == ProjectKind.Sub) { Steps = 0; Reached = 0; }        // 小项目本身就是一条事项，不分份
        }
    }

    public static class PriorityLevel
    {
        public const int Normal = 0;
        public const int Important = 1;
        public const int Urgent = 2;

        public static string Label(int p)
        {
            if (p >= Urgent) return "紧急";
            if (p == Important) return "重要";
            return "普通";
        }
    }

    [DataContract]
    public class Settings
    {
        /// <summary>桌面插件的窗口层级：desktop(贴在桌面层) / topmost(总在最前) / normal(普通窗口)。</summary>
        [DataMember(Name = "widgetMode", Order = 0)] public string WidgetMode;
        [DataMember(Name = "widgetVisible", Order = 1)] public bool WidgetVisible;
        [DataMember(Name = "widgetOpacity", Order = 2)] public double WidgetOpacity;
        [DataMember(Name = "widgetLeft", Order = 3)] public double WidgetLeft;
        [DataMember(Name = "widgetTop", Order = 4)] public double WidgetTop;
        [DataMember(Name = "widgetShowDone", Order = 6)] public bool WidgetShowDone;
        [DataMember(Name = "widgetScale", Order = 7)] public double WidgetScale;
        /// <summary>插件固定宽度（0 = 跟随内容自适应）。</summary>
        [DataMember(Name = "widgetWidth", Order = 12)] public double WidgetWidth;
        /// <summary>插件固定高度（0 = 跟随内容自适应）。</summary>
        [DataMember(Name = "widgetHeight", Order = 13)] public double WidgetHeight;
        [DataMember(Name = "weekStartMonday", Order = 8)] public bool WeekStartMonday;
        [DataMember(Name = "autoStart", Order = 9)] public bool AutoStart;
        [DataMember(Name = "accent", Order = 10)] public string Accent;
        /// <summary>完成任务的自动归档：超过 N 天的已完成任务在“已完成”页提示清理。</summary>
        [DataMember(Name = "keepDoneDays", Order = 11)] public int KeepDoneDays;
        /// <summary>全屏游戏/演示时自动隐藏插件，避免抢合成造成掉帧；数据里没写时按开启处理。</summary>
        [DataMember(Name = "widgetPauseFullscreen", Order = 14)] public bool? PauseOnFullscreen;

        public bool PauseOnFullscreenEnabled { get { return PauseOnFullscreen != false; } }

        public static Settings CreateDefault()
        {
            Settings s = new Settings();
            s.WidgetMode = "desktop";
            s.WidgetVisible = true;
            s.WidgetOpacity = 0.94;
            s.WidgetLeft = double.NaN;
            s.WidgetTop = double.NaN;
            s.WidgetShowDone = true;
            s.WidgetScale = 1.0;
            s.WidgetWidth = 0;
            s.WidgetHeight = 0;
            s.WeekStartMonday = true;
            s.AutoStart = false;
            s.Accent = "blue";
            s.KeepDoneDays = 30;
            s.PauseOnFullscreen = true;
            return s;
        }

        public void Normalize()
        {
            Settings d = CreateDefault();
            if (WidgetMode != "desktop" && WidgetMode != "topmost" && WidgetMode != "normal") WidgetMode = d.WidgetMode;
            if (WidgetOpacity <= 0.2 || WidgetOpacity > 1) WidgetOpacity = d.WidgetOpacity;
            if (double.IsInfinity(WidgetLeft) || Math.Abs(WidgetLeft) > 100000) WidgetLeft = double.NaN;
            if (double.IsInfinity(WidgetTop) || Math.Abs(WidgetTop) > 100000) WidgetTop = double.NaN;
            if (double.IsNaN(WidgetWidth) || double.IsInfinity(WidgetWidth)) WidgetWidth = 0;
            if (double.IsNaN(WidgetHeight) || double.IsInfinity(WidgetHeight)) WidgetHeight = 0;
            if (WidgetScale < 0.7 || WidgetScale > 1.6) WidgetScale = d.WidgetScale;
            if (WidgetWidth > 0 && (WidgetWidth < 220 || WidgetWidth > 2000)) WidgetWidth = 0;
            if (WidgetHeight > 0 && (WidgetHeight < 140 || WidgetHeight > 2000)) WidgetHeight = 0;
            if (Accent == null || Accent.Length == 0) Accent = d.Accent;
            // 老数据里没有这个字段时（null）按开启处理，并写成明确的值存下来。
            if (PauseOnFullscreen == null) PauseOnFullscreen = true;
            if (KeepDoneDays <= 0) KeepDoneDays = d.KeepDoneDays;
        }
    }

    [DataContract]
    public class AppData
    {
        [DataMember(Name = "version", Order = 0)] public int Version;
        [DataMember(Name = "settings", Order = 1)] public Settings Settings;
        [DataMember(Name = "tasks", Order = 2)] public List<TaskItem> Tasks;
        /// <summary>项目树：大项目 / 分段 / 小项目（小项目挂在 Tasks 里当普通事项）。</summary>
        [DataMember(Name = "projects", Order = 3)] public List<ProjectNode> Projects;

        public static AppData CreateDefault()
        {
            AppData d = new AppData();
            d.Version = 1;
            d.Settings = Settings.CreateDefault();
            d.Tasks = new List<TaskItem>();
            d.Projects = new List<ProjectNode>();
            return d;
        }

        public void Normalize()
        {
            if (Version <= 0) Version = 1;
            if (Settings == null) Settings = Settings.CreateDefault();
            Settings.Normalize();
            if (Tasks == null) Tasks = new List<TaskItem>();
            for (int i = 0; i < Tasks.Count; i++)
            {
                if (Tasks[i] == null) { Tasks.RemoveAt(i); i--; continue; }
                Tasks[i].Normalize();
            }
            if (Projects == null) Projects = new List<ProjectNode>();
            for (int i = 0; i < Projects.Count; i++)
            {
                if (Projects[i] == null) { Projects.RemoveAt(i); i--; continue; }
                Projects[i].Normalize();
            }
            ProjectTree.Repair(this);
        }
    }
}
