using System;
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
            return t;
        }

        public TaskItem Clone()
        {
            TaskItem t = new TaskItem();
            t.Id = Id; t.Title = Title; t.Note = Note; t.Date = Date;
            t.Done = Done; t.DoneAt = DoneAt; t.Priority = Priority;
            t.Tag = Tag; t.Sort = Sort; t.CreatedAt = CreatedAt;
            return t;
        }

        /// <summary>补全反序列化后可能缺失的字段。</summary>
        public void Normalize()
        {
            if (Id == null || Id.Length == 0) Id = Guid.NewGuid().ToString("N");
            if (Title == null) Title = "";
            if (Note == null) Note = "";
            if (Tag == null) Tag = "";
            Date = Date.Date;
            if (Priority < 0 || Priority > 2) Priority = 0;
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

        public static AppData CreateDefault()
        {
            AppData d = new AppData();
            d.Version = 1;
            d.Settings = Settings.CreateDefault();
            d.Tasks = new List<TaskItem>();
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
        }
    }
}
