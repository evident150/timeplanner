using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TimePlanner.Core;

namespace TimePlanner.App
{
    public partial class MainWindow
    {
        // ---------------- 任务拖动改期 ----------------

        const string DragFormat = "TimePlanner.TaskId";

        /// <summary>按住任务行拖动时进入拖拽，可投放到其它日期。</summary>
        void BeginTaskDrag(TaskRow row, TaskItem t)
        {
            try
            {
                DataObject data = new DataObject(DragFormat, t.Id);
                DragDrop.DoDragDrop(row, data, DragDropEffects.Move);
            }
            catch (Exception) { }
        }

        /// <summary>把日期卡片设成拖放目标，放下后任务改到该日期。</summary>
        void MakeDropTarget(Border target, DateTime day, Action<Border, bool> highlight)
        {
            target.AllowDrop = true;
            target.DragOver += delegate(object s, DragEventArgs e)
            {
                if (!e.Data.GetDataPresent(DragFormat))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }
                e.Effects = DragDropEffects.Move;
                if (highlight != null) highlight(target, true);
                e.Handled = true;
            };
            target.DragLeave += delegate(object s, DragEventArgs e)
            {
                if (highlight != null) highlight(target, false);
            };
            target.Drop += delegate(object s, DragEventArgs e)
            {
                if (highlight != null) highlight(target, false);
                string id = e.Data.GetData(DragFormat) as string;
                if (id == null || id.Length == 0) return;
                Store.MoveTo(id, day);
                e.Handled = true;
            };
        }

        /// <summary>设置页里显示插件当前尺寸。</summary>
        static string SizeText(Settings st)
        {
            if (st.WidgetWidth <= 1 && st.WidgetHeight <= 1) return "当前：跟随内容自适应";
            string w = st.WidgetWidth > 1 ? ((int)Math.Round(st.WidgetWidth)).ToString() : "自适应";
            string h = st.WidgetHeight > 1 ? ((int)Math.Round(st.WidgetHeight)).ToString() : "自适应";
            return string.Format("当前：宽 {0} × 高 {1}", w, h);
        }

        // ---------------- 今日 ----------------

        UIElement BuildTodayPage()
        {
            DateTime day = DayAnchor.Date;
            List<TaskItem> all = TaskQuery.ForDay(Store.Data.Tasks, day);
            List<TaskItem> open = all.Where(t => !t.Done).ToList();
            List<TaskItem> done = all.Where(t => t.Done).ToList();
            // 压在过去的未竟之事：它们只是静静留在旧日期里，今天这页空空的，看着特别像「数据没了」。
            List<TaskItem> overdue = Store.Data.Tasks.Where(t => !t.Done && t.Date.Date < day).ToList();

            pageTitle.Text = day == DateTime.Today ? "今 日 圣 旨" : Fmt.Relative(day) + " 之 圣 旨";
            pageSubtitle.Text = Fmt.Full(day) + string.Format("　·　共 {0} 事，已竟 {1} 事", all.Count, done.Count);
            pageActions.Children.Clear();

            pageActions.Children.Add(NavArrow("left", delegate() { DayAnchor = day.AddDays(-1); Refresh(); }, null));
            if (day != DateTime.Today)
            {
                Border today = Ui.Chip("回到今日", false, delegate() { DayAnchor = DateTime.Today; Refresh(); }, Theme.Accent);
                today.Margin = new Thickness(6, 0, 0, 0);
                pageActions.Children.Add(today);
            }
            pageActions.Children.Add(NavArrow("right", delegate() { DayAnchor = day.AddDays(1); Refresh(); }, null));
            if (open.Count > 0)
            {
                Border defer = Ui.TextButton("今日事明日毕", delegate()
                {
                    // 先换锚点再动数据：Store 改完会立刻通知重排，
                    // 锚点没换的话这一下刷的还是今天（已经空了的那天）。
                    DayAnchor = day.AddDays(1);
                    Store.Defer(day, day.AddDays(1));
                }, false);
                defer.Margin = new Thickness(10, 0, 0, 0);
                pageActions.Children.Add(defer);
            }

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(26, 0, 26, 26);

            // 旧日期里还压着没办完的事：明说一句，并给个一键顺延，免得看着像数据没了。
            if (day == DateTime.Today && overdue.Count > 0)
            {
                Border late = Ui.Round(12, Theme.B(Theme.Alpha(Theme.Accent, 0.10)), Theme.B(Theme.Alpha(Theme.Accent, 0.42)), 1);
                late.Padding = new Thickness(15, 9, 12, 9);
                late.Margin = new Thickness(0, 0, 0, 14);
                StackPanel row = new StackPanel();
                row.Orientation = Orientation.Horizontal;
                TextBlock lateTxt = Ui.Txt(string.Format("此前尚余 {0} 事未竟，还压在过去的日期里", overdue.Count),
                    13, Theme.B(Theme.TextMuted), false);
                lateTxt.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(lateTxt);
                Border roll = Ui.Chip("全部挪到今天", true, delegate()
                {
                    DayAnchor = DateTime.Today;
                    Store.RollOver(DateTime.Today, DateTime.Today);
                }, Theme.Accent);
                roll.Margin = new Thickness(12, 0, 0, 0);
                roll.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(roll);
                late.Child = row;
                sp.Children.Add(late);
            }

            // 进度
            Border progress = Ui.Round(14, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
            progress.Padding = new Thickness(18, 15, 18, 16);
            progress.Margin = new Thickness(0, 0, 0, 16);
            Grid pg = new Grid();
            pg.ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinition pc = new ColumnDefinition();
            pc.Width = new GridLength(230);
            pg.ColumnDefinitions.Add(pc);

            StackPanel left = new StackPanel();
            StackPanel line = new StackPanel();
            line.Orientation = Orientation.Horizontal;
            int pct = all.Count == 0 ? 0 : (int)Math.Round(done.Count * 100.0 / all.Count);
            TextBlock big = Ui.Txt(pct.ToString(), 30, Theme.B(Theme.Text), true);
            line.Children.Add(big);
            TextBlock pctSign = Ui.Txt("%", 14, Theme.B(Theme.TextFaint), false);
            pctSign.VerticalAlignment = VerticalAlignment.Bottom;
            pctSign.Margin = new Thickness(3, 0, 10, 5);
            line.Children.Add(pctSign);
            TextBlock tip = Ui.Txt(all.Count == 0 ? "今天还没有安排" : (open.Count == 0 ? "今天的任务全部完成 🎉" : string.Format("还剩 {0} 项待完成", open.Count)),
                13, Theme.B(Theme.TextMuted), false);
            tip.VerticalAlignment = VerticalAlignment.Bottom;
            tip.Margin = new Thickness(0, 0, 0, 6);
            line.Children.Add(tip);
            left.Children.Add(line);
            MiniBar bar = new MiniBar(7, Theme.Success);
            bar.Margin = new Thickness(0, 10, 0, 0);
            left.Children.Add(bar);
            Grid.SetColumn(left, 0);
            pg.Children.Add(left);
            bar.SetRatio(all.Count == 0 ? 0 : (double)done.Count / all.Count, false);

            StackPanel right = new StackPanel();
            right.VerticalAlignment = VerticalAlignment.Center;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(StatLine("待竟", open.Count.ToString(), Theme.Accent));
            right.Children.Add(StatLine("已竟", done.Count.ToString(), Theme.Success));
            Grid.SetColumn(right, 1);
            pg.Children.Add(right);
            progress.Child = pg;
            sp.Children.Add(progress);

            sp.Children.Add(AddRow(day, "落笔记事：写下" + Fmt.Relative(day) + "之事，回车即录"));

            sp.Children.Add(ListHeader("未竟之事", open.Count, null));
            if (open.Count == 0)
            {
                sp.Children.Add(EmptyState("check", "没有未竟之事", "在上面的输入框写下你想完成的事情"));
            }
            else
            {
                for (int i = 0; i < open.Count; i++)
                {
                    TaskRow row = MakeRow(open[i], false);
                    row.Margin = new Thickness(0, 0, 0, 8);
                    sp.Children.Add(row);
                }
            }

            if (done.Count > 0)
            {
                Border head = ListHeader("已竟之事", done.Count, delegate()
                {
                    ShowDoneSection = !ShowDoneSection;
                    Refresh();
                });
                head.Tag = ShowDoneSection;
                sp.Children.Add(head);
                if (ShowDoneSection)
                {
                    for (int i = 0; i < done.Count; i++)
                    {
                        TaskRow row = MakeRow(done[i], false);
                        row.Margin = new Thickness(0, 0, 0, 8);
                        sp.Children.Add(row);
                    }
                }
            }

            ScrollViewer sv = Ui.Scroll(sp);
            sv.Padding = new Thickness(0);
            return sv;
        }

        Border NavArrow(string icon, Action onClick, string tip)
        {
            Border b = Ui.Round(9, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
            b.Width = 32;
            b.Height = 30;
            b.Margin = new Thickness(0, 0, 6, 0);
            Path p = Ui.IconPath(icon, 12, Theme.B(Theme.TextMuted), 1.5);
            p.HorizontalAlignment = HorizontalAlignment.Center;
            p.VerticalAlignment = VerticalAlignment.Center;
            b.Child = p;
            Ui.Click(b, onClick, Theme.B(Theme.PanelHi), Theme.B(Theme.Panel));
            if (tip != null) Ui.Tip(b, tip);
            return b;
        }

        Grid StatLine(string label, string value, Color color)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 2, 0, 2);
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = new GridLength(1, GridUnitType.Star);
            g.ColumnDefinitions.Add(c0);
            g.ColumnDefinitions.Add(new ColumnDefinition());
            TextBlock l = Ui.Txt(label, 12, Theme.B(Theme.TextMuted), false);
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(l, 0);
            g.Children.Add(l);
            TextBlock v = Ui.Txt(value, 15, Theme.B(color), true);
            v.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        Border ListHeader(string title, int count, Action toggle)
        {
            Border b = Ui.Round(8, Theme.Transparent);
            b.Padding = new Thickness(2, 0, 2, 0);
            b.Margin = new Thickness(0, 8, 0, 9);
            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.Children.Add(Ui.Txt(title, 12.5, Theme.B(Theme.TextMuted), true));
            TextBlock c = Ui.Txt(" · " + count.ToString(), 12.5, Theme.B(Theme.TextFaint), false);
            c.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(c);
            if (toggle != null)
            {
                Path chev = Ui.IconPath(ShowDoneSection ? "down" : "right", 11, Theme.B(Theme.TextFaint), 1.5);
                chev.VerticalAlignment = VerticalAlignment.Center;
                chev.Margin = new Thickness(6, 1, 0, 0);
                sp.Children.Add(chev);
                Ui.Click(b, toggle, Theme.Transparent, Theme.Transparent);
            }
            b.Child = sp;
            return b;
        }

        Border EmptyState(string icon, string title, string hint)
        {
            Border b = Ui.Round(12, Theme.B(Theme.PanelSoft), Theme.B(Theme.Border), 1);
            b.Padding = new Thickness(18, 26, 18, 26);
            StackPanel sp = new StackPanel();
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            Path p = Ui.IconPath(icon, 26, Theme.B(Theme.TextFaint), 1.4);
            p.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(p);
            TextBlock t = Ui.Txt(title, 13.5, Theme.B(Theme.TextMuted), false);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(t);
            if (hint != null)
            {
                TextBlock h = Ui.Txt(hint, 11.5, Theme.B(Theme.TextFaint), false);
                h.HorizontalAlignment = HorizontalAlignment.Center;
                h.Margin = new Thickness(0, 5, 0, 0);
                h.TextWrapping = TextWrapping.Wrap;
                h.TextAlignment = TextAlignment.Center;
                sp.Children.Add(h);
            }
            b.Child = sp;
            return b;
        }

        /// <summary>带 #标签 / ! 优先级的快速输入框。</summary>
        Border AddRow(DateTime day, string watermark)
        {
            Border wrap = Ui.Round(11, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
            wrap.Padding = new Thickness(12, 9, 12, 10);
            wrap.Margin = new Thickness(0, 0, 0, 16);
            Grid g = new Grid();
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(c0);
            g.ColumnDefinitions.Add(new ColumnDefinition());

            Path plus = Ui.IconPath("plus", 13, Theme.B(Theme.Accent), 1.6);
            plus.VerticalAlignment = VerticalAlignment.Center;
            plus.Margin = new Thickness(0, 0, 9, 0);
            Grid.SetColumn(plus, 0);
            g.Children.Add(plus);

            HintBox box = new HintBox(watermark + "　（!! 紧急，! 重要，#标签）", 13);
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Submitted = delegate(string text)
            {
                Store.Add(text, day);
                AnimatedRefresh();
            };
            box.Box.GotFocus += delegate(object s, RoutedEventArgs e) { AddBox = box; };
            Grid.SetColumn(box, 1);
            g.Children.Add(box);
            wrap.Child = g;
            if (day == DayAnchor.Date) AddBox = box;
            return wrap;
        }

        // ---------------- 本周 ----------------

        UIElement BuildWeekPage()
        {
            DateTime start = TaskQuery.WeekStart(WeekAnchor, Store.Settings.WeekStartMonday);
            DateTime end = start.AddDays(6);
            List<TaskItem> week = TaskQuery.InRange(Store.Data.Tasks, start, end);
            int done = week.Count(t => t.Done);

            pageTitle.Text = start == TaskQuery.WeekStart(DateTime.Today, Store.Settings.WeekStartMonday) ? "本 周 奏 章" : "一 周 奏 章";
            pageSubtitle.Text = Fmt.Range(start, end) + string.Format("　·　共 {0} 事，已竟 {1} 事　·　按住任务拖到别的日期即可改期", week.Count, done);
            pageActions.Children.Clear();
            pageActions.Children.Add(NavArrow("left", delegate() { WeekAnchor = start.AddDays(-7); Refresh(); }, "上一周"));
            DateTime thisWeek = TaskQuery.WeekStart(DateTime.Today, Store.Settings.WeekStartMonday);
            if (start != thisWeek)
            {
                Border b = Ui.Chip("回到本周", false, delegate() { WeekAnchor = DateTime.Today; Refresh(); }, Theme.Accent);
                b.Margin = new Thickness(0, 0, 6, 0);
                pageActions.Children.Add(b);
            }
            pageActions.Children.Add(NavArrow("right", delegate() { WeekAnchor = start.AddDays(7); Refresh(); }, "下一周"));

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(26, 0, 26, 26);

            // 一周概览
            Grid strip = new Grid();
            for (int i = 0; i < 7; i++) strip.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < 7; i++)
            {
                DateTime d = start.AddDays(i);
                List<TaskItem> list = TaskQuery.ForDay(Store.Data.Tasks, d);
                int dn = list.Count(t => t.Done);
                bool isToday = d == DateTime.Today;
                Border cell = Ui.Round(11, Theme.B(isToday ? Theme.AccentSoft : Theme.Panel),
                    isToday ? Theme.B(Theme.Alpha(Theme.Accent, 0.6)) : Theme.B(Theme.Border), 1);
                cell.Padding = new Thickness(10, 9, 10, 10);
                cell.Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0);
                StackPanel cs = new StackPanel();
                TextBlock wd = Ui.Txt(Fmt.Weekday(d), 11.5, Theme.B(isToday ? Theme.Accent : Theme.TextFaint), isToday);
                cs.Children.Add(wd);
                TextBlock dd = Ui.Txt(d.Day.ToString(), 18, Theme.B(Theme.Text), true);
                dd.Margin = new Thickness(0, 2, 0, 4);
                cs.Children.Add(dd);
                TextBlock cnt = Ui.Txt(list.Count == 0 ? "空闲" : string.Format("{0}/{1}", dn, list.Count), 11, Theme.B(Theme.TextMuted), false);
                cs.Children.Add(cnt);
                MiniBar mb = new MiniBar(4, isToday ? Theme.Accent : Theme.Success);
                mb.Margin = new Thickness(0, 7, 0, 0);
                cs.Children.Add(mb);
                cell.Child = cs;
                mb.SetRatio(list.Count == 0 ? 0 : (double)dn / list.Count, false);
                DateTime target = d;
                Ui.Click(cell, delegate() { DayAnchor = target; SelectPage("today"); }, Theme.B(Theme.PanelHi), cell.Background as SolidColorBrush);
                Ui.Tip(cell, "查看 " + Fmt.DateCN(d) + " 的安排；也可以把任务直接拖到这一格改期");
                DateTime cellDay = d;
                bool cellToday = isToday;
                MakeDropTarget(cell, cellDay, delegate(Border cb, bool on)
                {
                    cb.BorderBrush = Theme.B(on ? Theme.Accent : (cellToday ? Theme.Alpha(Theme.Accent, 0.6) : Theme.Border));
                });
                Grid.SetColumn(cell, i);
                strip.Children.Add(cell);
            }
            strip.Margin = new Thickness(0, 0, 0, 18);
            sp.Children.Add(strip);

            for (int i = 0; i < 7; i++)
            {
                DateTime d = start.AddDays(i);
                List<TaskItem> list = TaskQuery.ForDay(Store.Data.Tasks, d);
                List<TaskItem> openList = list.Where(t => !t.Done).ToList();
                bool isToday = d == DateTime.Today;

                Border group = Ui.Round(13, Theme.B(isToday ? Theme.Panel : Theme.PanelSoft), Theme.B(Theme.Border), 1);
                group.Padding = new Thickness(14, 12, 14, 14);
                group.Margin = new Thickness(0, 0, 0, 10);
                StackPanel gs = new StackPanel();

                Grid gh = new Grid();
                gh.ColumnDefinitions.Add(new ColumnDefinition());
                gh.ColumnDefinitions.Add(new ColumnDefinition());
                StackPanel titleRow = new StackPanel();
                titleRow.Orientation = Orientation.Horizontal;
                TextBlock dayTitle = Ui.Txt(Fmt.Weekday(d) + " " + Fmt.DateCN(d), 13.5, Theme.B(Theme.Text), true);
                titleRow.Children.Add(dayTitle);
                if (isToday)
                {
                    Border tag = Ui.Pill("今天", Theme.Accent, Theme.AccentSoft);
                    tag.Margin = new Thickness(7, 0, 0, 0);
                    titleRow.Children.Add(tag);
                }
                else if (d < DateTime.Today && openList.Count > 0)
                {
                    Border tag = Ui.Pill("已过期", Theme.Warning, Theme.Alpha(Theme.Warning, 0.16));
                    tag.Margin = new Thickness(7, 0, 0, 0);
                    titleRow.Children.Add(tag);
                }
                Grid.SetColumn(titleRow, 0);
                gh.Children.Add(titleRow);
                TextBlock gcount = Ui.Txt(string.Format("{0}/{1}", list.Count - openList.Count, list.Count), 12, Theme.B(Theme.TextFaint), false);
                gcount.HorizontalAlignment = HorizontalAlignment.Right;
                gcount.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(gcount, 1);
                gh.Children.Add(gcount);
                gs.Children.Add(gh);

                StackPanel rows = new StackPanel();
                rows.Margin = new Thickness(0, 10, 0, 0);
                if (list.Count == 0)
                {
                    rows.Children.Add(Ui.Txt("暂无安排", 12, Theme.B(Theme.TextFaint), false));
                }
                else
                {
                    for (int k = 0; k < list.Count; k++)
                    {
                        TaskRow row = MakeRow(list[k], false);
                        Ui.Tip(row, "按住这一行拖到别的日期即可改期");
                        row.Margin = new Thickness(0, 0, 0, 6);
                        rows.Children.Add(row);
                    }
                }
                gs.Children.Add(rows);

                Border add = AddRow(d, "落笔记事：" + Fmt.Weekday(d));
                add.Margin = new Thickness(0, 6, 0, 0);
                gs.Children.Add(add);

                group.Child = gs;
                DateTime dropDay = d;
                bool dropToday = isToday;
                MakeDropTarget(group, dropDay, delegate(Border gb, bool on)
                {
                    gb.BorderBrush = Theme.B(on ? Theme.Accent : Theme.Border);
                    gb.Background = Theme.B(on ? Theme.Alpha(Theme.Accent, 0.09) : (dropToday ? Theme.Panel : Theme.PanelSoft));
                });
                sp.Children.Add(group);
            }

            ScrollViewer sv = Ui.Scroll(sp);
            return sv;
        }

        // ---------------- 已完成 ----------------

        UIElement BuildDonePage()
        {
            List<TaskItem> done = Store.Data.Tasks.Where(t => t.Done).ToList();
            done.Sort(delegate(TaskItem a, TaskItem b)
            {
                DateTime da = a.DoneAt.HasValue ? a.DoneAt.Value : a.Date;
                DateTime db = b.DoneAt.HasValue ? b.DoneAt.Value : b.Date;
                return db.CompareTo(da);
            });

            pageTitle.Text = "已 竟 之 事";
            pageSubtitle.Text = string.Format("共 {0} 事已竟（勾选框可撤销）", done.Count);
            pageActions.Children.Clear();
            int old = Store.Data.Tasks.Count(t => t.Done && t.Date.Date < DateTime.Today.AddDays(-30));
            if (old > 0)
            {
                Border clean = Ui.TextButton(string.Format("清理 {0} 条旧记录", old), delegate()
                {
                    Store.ClearDoneBefore(DateTime.Today.AddDays(-30));
                }, false);
                pageActions.Children.Add(clean);
            }

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(26, 0, 26, 26);
            if (done.Count == 0)
            {
                sp.Children.Add(EmptyState("check", "还没有完成记录", "在主程序或桌面插件里勾选任务后，这里会留下记录"));
            }
            else
            {
                string lastKey = "";
                for (int i = 0; i < done.Count; i++)
                {
                    TaskItem t = done[i];
                    DateTime when = t.DoneAt.HasValue ? t.DoneAt.Value.Date : t.Date.Date;
                    string key = when.ToString("yyyy-MM-dd");
                    if (key != lastKey)
                    {
                        lastKey = key;
                        Border head = ListHeader(Fmt.Relative(when) + "完成", 0, null);
                        ((StackPanel)head.Child).Children.Clear();
                        ((StackPanel)head.Child).Children.Add(Ui.Txt(Fmt.Full(when), 12.5, Theme.B(Theme.TextFaint), true));
                        sp.Children.Add(head);
                    }
                    TaskRow row = MakeRow(t, false);
                    row.Margin = new Thickness(0, 0, 0, 8);
                    sp.Children.Add(row);
                }
            }
            return Ui.Scroll(sp);
        }

        // ---------------- 设置 ----------------

        UIElement BuildSettingsPage()
        {
            Settings st = Store.Settings;
            pageTitle.Text = "钦 此 设 置";
            pageSubtitle.Text = "桌面插件外观、数据与启动选项";
            pageActions.Children.Clear();

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(26, 0, 26, 26);
            sp.MaxWidth = 760;
            sp.HorizontalAlignment = HorizontalAlignment.Left;

            // 桌面插件
            StackPanel widget = new StackPanel();
            int modeIdx = st.WidgetMode == "topmost" ? 1 : (st.WidgetMode == "normal" ? 2 : 0);
            Segment mode = new Segment(new string[] { "贴在桌面", "始终置顶", "普通窗口" }, modeIdx, delegate(int i)
            {
                string m = i == 1 ? "topmost" : (i == 2 ? "normal" : "desktop");
                Store.UpdateSettings(delegate(Settings s2) { s2.WidgetMode = m; });
            });
            widget.Children.Add(Cards.Row("窗口层级", "贴在桌面：插件待在所有窗口最下层，不遮挡工作，点击它会浮到前面、点别处再回到最底层；始终置顶：永远显示在最前面；普通窗口：和其他窗口一样参与前后排序。", mode));

            StackPanel opacityLine = new StackPanel();
            opacityLine.Orientation = Orientation.Horizontal;
            SliderBar opacity = new SliderBar((st.WidgetOpacity - 0.4) / 0.6, 168);
            TextBlock opacityVal = Ui.Txt(Percent(st.WidgetOpacity), 12, Theme.B(Theme.TextMuted), false);
            opacityVal.VerticalAlignment = VerticalAlignment.Center;
            opacityVal.Margin = new Thickness(10, 0, 0, 0);
            opacityLine.Children.Add(opacity);
            opacityLine.Children.Add(opacityVal);
            opacity.Changed = delegate(double v, bool final)
            {
                double real = 0.4 + v * 0.6;
                opacityVal.Text = Percent(real);
                Store.UpdateSettings(delegate(Settings s2) { s2.WidgetOpacity = real; });
            };
            widget.Children.Add(Cards.Row("不透明度", null, opacityLine));

            StackPanel scaleLine = new StackPanel();
            scaleLine.Orientation = Orientation.Horizontal;
            SliderBar scale = new SliderBar((st.WidgetScale - 0.8) / 0.6, 168);
            TextBlock scaleVal = Ui.Txt(Percent(st.WidgetScale), 12, Theme.B(Theme.TextMuted), false);
            scaleVal.VerticalAlignment = VerticalAlignment.Center;
            scaleVal.Margin = new Thickness(10, 0, 0, 0);
            scaleLine.Children.Add(scale);
            scaleLine.Children.Add(scaleVal);
            scale.Changed = delegate(double v, bool final)
            {
                double real = 0.8 + v * 0.6;
                scaleVal.Text = Percent(real);
                Store.UpdateSettings(delegate(Settings s2) { s2.WidgetScale = real; });
            };
            widget.Children.Add(Cards.Row("插件缩放", null, scaleLine));

            StackPanel sizeLine = new StackPanel();
            sizeLine.Orientation = Orientation.Horizontal;
            TextBlock sizeVal = Ui.Txt(SizeText(st), 12, Theme.B(Theme.TextMuted), false);
            sizeVal.VerticalAlignment = VerticalAlignment.Center;
            sizeVal.Margin = new Thickness(0, 0, 10, 0);
            sizeLine.Children.Add(sizeVal);
            sizeLine.Children.Add(Ui.TextButton("恢复自适应", delegate()
            {
                Store.UpdateSettings(delegate(Settings s2) { s2.WidgetWidth = 0; s2.WidgetHeight = 0; });
                Refresh();
            }, false));
            widget.Children.Add(Cards.Row("插件大小", "把鼠标移到插件边缘或右下角拖动即可自由缩放，宽度和高度分开记忆；也可以在这里或插件的右键菜单里恢复自适应。", sizeLine));

            Switch showDone = new Switch(st.WidgetShowDone);
            showDone.Changed = delegate(bool on) { Store.UpdateSettings(delegate(Settings s2) { s2.WidgetShowDone = on; }); };
            widget.Children.Add(Cards.Row("在插件里显示已完成任务", null, showDone));

            Switch fullscreen = new Switch(st.PauseOnFullscreenEnabled);
            fullscreen.Changed = delegate(bool on) { Store.UpdateSettings(delegate(Settings s2) { s2.PauseOnFullscreen = on; }); };
            widget.Children.Add(Cards.Row("全屏游戏、演示时自动隐藏插件",
                "检测到独占全屏的程序（游戏、演示、全屏视频）时先把自己收起来，退出全屏后自动恢复。可以避免插件和全屏画面抢渲染而掉帧、卡顿。", fullscreen));

            StackPanel runLine = new StackPanel();
            runLine.Orientation = Orientation.Horizontal;
            TextBlock runState = Ui.Txt(WidgetLauncher.IsRunning() ? "插件正在运行" : "插件未运行", 12, Theme.B(WidgetLauncher.IsRunning() ? Theme.Success : Theme.TextFaint), false);
            runState.VerticalAlignment = VerticalAlignment.Center;
            runState.Margin = new Thickness(0, 0, 10, 0);
            runLine.Children.Add(runState);
            runLine.Children.Add(Ui.TextButton(st.WidgetVisible ? "隐藏插件" : "显示插件", delegate()
            {
                Store.UpdateSettings(delegate(Settings s2) { s2.WidgetVisible = !s2.WidgetVisible; });
                Refresh();
            }, false));
            Border restart = Ui.TextButton("重启插件", delegate()
            {
                WidgetLauncher.Kill();
                System.Threading.Thread.Sleep(200);
                WidgetLauncher.EnsureRunning();
                Refresh();
            }, false);
            restart.Margin = new Thickness(8, 0, 0, 0);
            runLine.Children.Add(restart);
            widget.Children.Add(Cards.Row("插件进程", null, runLine));
            sp.Children.Add(Cards.Panel("桌面插件", "桌面上的任务小面板，显示今日任务并可直接勾选。", widget));

            // 外观
            StackPanel look = new StackPanel();
            StackPanel swatches = new StackPanel();
            swatches.Orientation = Orientation.Horizontal;
            for (int i = 0; i < Theme.AccentKeys.Length; i++)
            {
                string key = Theme.AccentKeys[i];
                string hex = AccentHex(key);
                bool on = key == st.Accent;
                Border sw = Ui.Round(9, Theme.B(Theme.C(hex)), on ? Theme.B(Colors.White) : Theme.B(Colors.Transparent), on ? 2 : 0);
                sw.Width = 30;
                sw.Height = 30;
                sw.Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0);
                sw.Child = Ui.IconPath("check", 13, Theme.B(on ? Colors.White : Colors.Transparent), 2);
                ((Path)sw.Child).HorizontalAlignment = HorizontalAlignment.Center;
                ((Path)sw.Child).VerticalAlignment = VerticalAlignment.Center;
                Ui.Click(sw, delegate() { Store.UpdateSettings(delegate(Settings s2) { s2.Accent = key; }); }, Theme.B(Colors.Transparent), Theme.B(Colors.Transparent));
                swatches.Children.Add(sw);
            }
            look.Children.Add(Cards.Row("主题色", null, swatches));

            Switch monday = new Switch(st.WeekStartMonday);
            monday.Changed = delegate(bool on) { Store.UpdateSettings(delegate(Settings s2) { s2.WeekStartMonday = on; }); };
            look.Children.Add(Cards.Row("一周从周一开始", null, monday));

            Switch auto = new Switch(st.AutoStart);
            auto.Changed = delegate(bool on)
            {
                Store.UpdateSettings(delegate(Settings s2) { s2.AutoStart = on; });
                DesktopInterop.SetAutoStart(on, System.Reflection.Assembly.GetEntryAssembly().Location);
            };
            look.Children.Add(Cards.Row("开机自动启动", "登录后自动在托盘运行，并按设置显示桌面插件。本版本单独占一条自启，别的版本不受影响。", auto));

            string[] otherAuto = DesktopInterop.OtherAutoStartNames();
            if (otherAuto.Length > 0)
            {
                // 只提醒，不动别人的注册表：那是另一条产品线的开机自启，归它自己管。
                StackPanel note = new StackPanel();
                note.Orientation = Orientation.Horizontal;
                TextBlock warn = Ui.Txt(string.Format("另有 {0} 个 TimePlanner 版本也设了开机自启", otherAuto.Length), 12, Theme.B(Theme.Warning), false);
                warn.VerticalAlignment = VerticalAlignment.Center;
                note.Children.Add(warn);
                Ui.Tip(note, "登录时会一起起来的版本：" + string.Join("、", otherAuto) + "\n（想只留一个，就去那个版本的设置里关掉它的自启）");
                look.Children.Add(Cards.Row("其它版本的自启", null, note));
            }
            sp.Children.Add(Cards.Panel("外观与启动", null, look));

            // 数据
            StackPanel data = new StackPanel();
            TextBlock path = Ui.Txt(Store.DataFile, 12, Theme.B(Theme.TextMuted), false);
            path.TextWrapping = TextWrapping.Wrap;
            StackPanel dataLine = new StackPanel();
            dataLine.Orientation = Orientation.Horizontal;
            dataLine.Children.Add(Ui.TextButton("打开数据目录", delegate() { OpenDataDir(); }, false));
            Border reload = Ui.TextButton("重新载入数据", delegate() { Store.Reload(); Refresh(); }, false);
            reload.Margin = new Thickness(8, 0, 0, 0);
            dataLine.Children.Add(reload);
            Border clean = Ui.TextButton("清理 30 天前的已竟之事", delegate() { Store.ClearDoneBefore(DateTime.Today.AddDays(-30)); }, false);
            clean.Margin = new Thickness(8, 0, 0, 0);
            dataLine.Children.Add(clean);
            data.Children.Add(path);
            dataLine.Margin = new Thickness(0, 12, 0, 0);
            data.Children.Add(dataLine);
            data.Children.Add(Ui.Txt("主程序与桌面插件共用同一个 JSON 数据文件，任意一端修改都会实时同步。", 11.5, Theme.B(Theme.TextFaint), false));
            sp.Children.Add(Cards.Panel("数据", null, data));

            // 关于
            StackPanel about = new StackPanel();
            about.Children.Add(Ui.Txt(AppVersion.Display + "　·　原生 WPF（.NET Framework 4.8），无外部依赖", 12, Theme.B(Theme.TextMuted), false));
            TextBlock keys = Ui.Txt("使用提示：\n· 输入框支持「!!」紧急、「!」重要、「#标签」快速标记\n· 桌面插件：拖动标题栏移动位置，双击标题栏打开主程序，右键查看更多操作\n· 完成任务时会放一筒礼花，并在插件左下角由小人说一句贺辞；当天全部完成还会再补一筒大的\n· 主程序点 ✕ 会隐藏到托盘，托盘菜单可退出", 12, Theme.B(Theme.TextMuted), false);
            keys.Margin = new Thickness(0, 8, 0, 0);
            keys.TextWrapping = TextWrapping.Wrap;
            about.Children.Add(keys);
            sp.Children.Add(Cards.Panel("关于", null, about));

            return Ui.Scroll(sp);
        }

        static string Percent(double v)
        {
            return ((int)Math.Round(v * 100)).ToString() + "%";
        }

        static string AccentHex(string key)
        {
            switch (key)
            {
                case "blue": return "#2F5A7A";
                case "cyan": return "#2E6E78";
                case "green": return "#3E7A4A";
                case "amber": return "#BE8A2C";
                case "violet": return "#7A4B6B";
                default: return "#B23A2A";
            }
        }

        void OpenDataDir()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Store.DataDir + "\""); }
            catch (Exception) { }
        }

        /// <summary>完成一条任务时放一筒礼花；一天的任务全部完成时再补一筒大的。</summary>
        void Celebrate(TaskRow row, TaskItem item)
        {
            if (fx == null) return;
            bool all = TaskQuery.AllDone(Store.Data.Tasks, item.Date);
            Point o = row.CheckCenter(fx);
            double aim = o.X > fx.ActualWidth * 0.55 ? -148 : -32;
            Fireworks.Popper(fx, o, all ? 1.7 : 1.35, aim, Fireworks.PickCheer());
            if (all) Fireworks.Popper(fx, Ui.CenterOf(contentHost, fx), 1.9, -90, Fireworks.AllDone);
        }

        // ---------------- 项目档案 ----------------
        //
        // 三档结构：大项目（顶层容器）→ 分段（大项目内部的分期）/ 小项目（最低一级）。
        // 小项目底下挂一条普通事项（TaskItem.ProjectId 指回节点），于是它天生就出现在
        // 今日 / 本周 / 桌面插件里，和普通任务并排，勾选、拖动改期、顺延、礼花都走同一条路。

        /// <summary>项目 / 分段整段办完时也放一筒礼花 —— 跟任务勾选同一个礼花筒、同一批鼓励语。</summary>
        void CelebrateNode(FrameworkElement anchor)
        {
            if (fx == null || anchor == null) return;
            Point o = Ui.CenterOf(anchor, fx);
            double aim = o.X > fx.ActualWidth * 0.55 ? -148 : -32;
            Fireworks.Popper(fx, o, 1.35, aim, Fireworks.PickCheer());
        }

        /// <summary>行上那句归属：这条小项目属于「大项目 / 分段」。</summary>
        string ProjectLabelOf(TaskItem t)
        {
            if (t == null || t.ProjectId == null || t.ProjectId.Length == 0) return "";
            ProjectNode n = ProjectTree.ById(Store.Data, t.ProjectId);
            if (n == null) return "";
            string path = ProjectTree.Path(Store.Data, n.ParentId, true);
            if (path.Length == 0) return "";
            return path.Length > 18 ? path.Substring(0, 17) + "…" : path;
        }

        UIElement BuildProjectPage()
        {
            AppData data = Store.Data;
            List<ProjectNode> roots = ProjectTree.Roots(data);
            int total;
            int done;
            ProjectTree.CountSubs(data, null, out total, out done);

            pageTitle.Text = "项 目 档 案";
            pageSubtitle.Text = roots.Count == 0
                ? "尚无项目　·　先立一个大项目，再往里添小项目"
                : string.Format("大项目 {0} 个　·　小项目 {1} 项，已竟 {2} 项", roots.Count, total, done);
            pageActions.Children.Clear();
            if (roots.Count > 0)
            {
                pageActions.Children.Add(Ui.TextButton("全部展开", delegate() { Store.SetAllProjectsOpen(true); }, false));
                Border fold = Ui.TextButton("全部收起", delegate() { Store.SetAllProjectsOpen(false); }, false);
                fold.Margin = new Thickness(8, 0, 0, 0);
                pageActions.Children.Add(fold);
            }

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(26, 0, 26, 26);
            sp.Children.Add(AddRowProject());

            if (roots.Count == 0)
            {
                sp.Children.Add(EmptyState("layers", "还没有项目",
                    "大项目是顶层容器，比如「毕业设计」；往里加小项目或分段，小项目就会和任务一起出现在桌面插件上"));
                return Ui.Scroll(sp);
            }

            for (int i = 0; i < roots.Count; i++) sp.Children.Add(BuildProjectCard(roots[i], i, roots.Count));
            return Ui.Scroll(sp);
        }

        /// <summary>顶上那个「新建大项目」输入框。</summary>
        Border AddRowProject()
        {
            Border wrap = Ui.Round(11, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
            wrap.Padding = new Thickness(12, 9, 12, 10);
            wrap.Margin = new Thickness(0, 0, 0, 16);

            Grid g = new Grid();
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(c0);
            g.ColumnDefinitions.Add(new ColumnDefinition());

            Path plus = Ui.IconPath("plus", 13, Theme.B(Theme.Accent), 1.6);
            plus.VerticalAlignment = VerticalAlignment.Center;
            plus.Margin = new Thickness(0, 0, 9, 0);
            Grid.SetColumn(plus, 0);
            g.Children.Add(plus);

            HintBox box = new HintBox("新建大项目：写下名目，回车即录　（如「毕业设计」）", 13);
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Submitted = delegate(string text)
            {
                Store.AddProject("", ProjectKind.Big, text);
                Refresh();
            };
            Grid.SetColumn(box, 1);
            g.Children.Add(box);

            wrap.Child = g;
            return wrap;
        }

        /// <summary>一个大项目：表头一行 + 展开后的分段 / 小项目。</summary>
        Border BuildProjectCard(ProjectNode n, int index, int total)
        {
            Border card = Ui.Round(13, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
            card.Padding = new Thickness(14, 11, 12, 12);
            card.Margin = new Thickness(0, 0, 0, 12);

            StackPanel body = new StackPanel();
            body.Children.Add(ProjectRow(n, 0, index, total));

            if (n.IsOpen)
            {
                List<ProjectNode> kids = ProjectTree.Children(Store.Data, n.Id);
                if (kids.Count == 0 && projectAddParent != n.Id)
                    body.Children.Add(ProjectHint("还没有下級：点 ＋ 添小项目、用「分段」把项目分期，或用 ⊖ ⊕ 直接把它分成几份（再拖横条记进度）", 28));
                for (int i = 0; i < kids.Count; i++)
                {
                    ProjectNode kid = kids[i];
                    if (kid.IsContainer) body.Children.Add(BuildStageBlock(kid, i, kids.Count));
                    else body.Children.Add(ProjectSubRow(kid, 28));
                }
                if (projectAddParent == n.Id)
                    body.Children.Add(ProjectComposer(n.Id, projectAddKind, null, null,
                        projectAddKind == ProjectKind.Stage ? "分段名目，回车即录　（如「开题阶段」）" : "小项目名目，回车即录　（可写「查文献 #论文 !!」）", 28));
            }
            card.Child = body;
            return card;
        }

        /// <summary>大项目里的分段：浅一层的一行 + 它下面那几条小项目。</summary>
        UIElement BuildStageBlock(ProjectNode n, int index, int total)
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(ProjectRow(n, 28, index, total));
            if (n.IsOpen)
            {
                List<ProjectNode> kids = ProjectTree.Children(Store.Data, n.Id);
                if (kids.Count == 0 && projectAddParent != n.Id)
                    sp.Children.Add(ProjectHint("这一段还没内容：点 ＋ 添小项目", 52));
                for (int i = 0; i < kids.Count; i++) sp.Children.Add(ProjectSubRow(kids[i], 52));
                if (projectAddParent == n.Id)
                    sp.Children.Add(ProjectComposer(n.Id, ProjectKind.Sub, null, null, "小项目名目，回车即录", 52));
            }
            return sp;
        }

        /// <summary>容器（大项目 / 分段）那一行：完成勾、展开箭头、名目、进度、行内操作。</summary>
        StackPanel ProjectRow(ProjectNode n, double indent, int index, int total)
        {
            AppData data = Store.Data;
            int subTotal;
            int subDone;
            ProjectTree.Count(data, n.Id, out subTotal, out subDone);
            bool big = n.Kind == ProjectKind.Big;
            bool nodeDone = ProjectTree.IsDone(data, n.Id);
            string nodeId = n.Id;

            StackPanel wrap = new StackPanel();
            wrap.Margin = new Thickness(indent, 0, 0, 5);

            Grid g = new Grid();
            ColumnDefinition c0 = new ColumnDefinition();
            c0.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(c0);
            ColumnDefinition c1 = new ColumnDefinition();
            c1.Width = new GridLength(1, GridUnitType.Star);
            g.ColumnDefinitions.Add(c1);
            ColumnDefinition c2 = new ColumnDefinition();
            c2.Width = GridLength.Auto;
            g.ColumnDefinitions.Add(c2);

            // 完成勾 = 任务行上那枚圆勾（同一个控件、同一套动画）：点一下整段办完，再点撤销，办完放礼花。
            CircleCheck doneCheck = new CircleCheck(big ? 17 : 15);
            doneCheck.VerticalAlignment = VerticalAlignment.Center;
            doneCheck.Margin = new Thickness(0, 0, 2, 0);
            doneCheck.SetDone(nodeDone, false);
            Ui.Tip(doneCheck, big ? "整个项目完成 / 撤销完成" : "这一段完成 / 撤销完成");
            Border doneRef = doneCheck;
            doneCheck.Toggled = delegate()
            {
                bool now = Store.ToggleProjectDone(nodeId);
                if (now) CelebrateNode(doneRef);
                AnimatedRefresh();
            };

            StackPanel head = new StackPanel();
            head.Orientation = Orientation.Horizontal;
            head.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(doneCheck);

            Border arrow = Ui.Round(7, Theme.Transparent);
            arrow.Width = 22;
            arrow.Height = 22;
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Margin = new Thickness(0, 0, 6, 0);
            Path chev = Ui.IconPath(n.IsOpen ? "down" : "right", 11, Theme.B(Theme.TextFaint), 1.5);
            chev.HorizontalAlignment = HorizontalAlignment.Center;
            chev.VerticalAlignment = VerticalAlignment.Center;
            arrow.Child = chev;
            Ui.Click(arrow, delegate() { Store.ToggleProjectOpen(nodeId); }, Theme.Transparent, Theme.Transparent);
            Ui.Tip(arrow, n.IsOpen ? "收起" : "展开");
            head.Children.Add(arrow);
            Grid.SetColumn(head, 0);
            g.Children.Add(head);

            StackPanel title = new StackPanel();
            title.Orientation = Orientation.Horizontal;
            title.VerticalAlignment = VerticalAlignment.Center;
            Path icon = Ui.IconPath(big ? "layers" : "list", big ? 15 : 13,
                Theme.B(nodeDone ? Theme.Success : (big ? Theme.Accent : Theme.TextFaint)), 1.5);
            icon.VerticalAlignment = VerticalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 8, 0);
            title.Children.Add(icon);
            TextBlock name = Ui.Txt(ProjectTree.TitleOf(data, n), big ? 15.5 : 13.5,
                Theme.B(nodeDone ? Theme.TextFaint : Theme.Text), big && !nodeDone);
            if (nodeDone) name.TextDecorations = TextDecorations.Strikethrough;      // 办完了名字划掉，跟任务行一个样
            name.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(name);

            bool partsHere = n.Kind != ProjectKind.Sub;                   // 大项目 / 分段都能分份（小项目本身就是一条事项）
            int ownSteps = n.Steps;
            int ownReached = n.Reached;
            int ownBaseDone = subDone - ownReached;      // 不含自己那几份的已完成数
            bool hadBar = partsHere && ownSteps > 0;

            Border prog = null;
            TextBlock progText = null;
            StepBar bar = null;
            if (hadBar)
            {
                bar = new StepBar(ownSteps, ownReached, 150);
                bar.Margin = new Thickness(10, 0, 0, 0);
                bar.VerticalAlignment = VerticalAlignment.Center;
                title.Children.Add(bar);
            }
            if (subTotal > 0)
            {
                bool allDone = subDone >= subTotal;
                prog = Ui.Pill(string.Format("已竟 {0}/{1}", subDone, subTotal),
                    allDone ? Theme.Success : Theme.TextMuted,
                    Theme.Alpha(allDone ? Theme.Success : Theme.TextMuted, 0.13));
                prog.Margin = new Thickness(10, 0, 0, 0);
                progText = (TextBlock)prog.Child;
                title.Children.Add(prog);
            }
            if (bar != null)
            {
                // 拖的时候只重画格子 + 改胶囊文字，松手才写盘
                StepBar barRef = bar;
                Border progRef = prog;
                TextBlock progTextRef = progText;
                int baseDone = ownBaseDone;
                int allTotal = subTotal;
                barRef.Changed = delegate(int k, bool final)
                {
                    if (progTextRef != null)
                    {
                        int nowDone = baseDone + k;
                        bool allDone = nowDone >= allTotal;
                        progTextRef.Text = string.Format("已竟 {0}/{1}", nowDone, allTotal);
                        progTextRef.Foreground = Theme.B(allDone ? Theme.Success : Theme.TextMuted);
                        if (progRef != null) progRef.Background = Theme.B(Theme.Alpha(allDone ? Theme.Success : Theme.TextMuted, 0.13));
                    }
                    if (final) Store.SetProjectReached(nodeId, k);
                };
            }
            if (partsHere)
            {
                PartsStepper stepper = new PartsStepper(ownSteps);
                stepper.Margin = new Thickness(8, 0, 0, 0);
                stepper.Changed = delegate(int v) { Store.SetProjectSteps(nodeId, v); };
                title.Children.Add(stepper);
            }
            Border titleHit = Ui.Round(8, Theme.Transparent);
            titleHit.Padding = new Thickness(2, 3, 6, 3);
            titleHit.Child = title;
            Ui.Click(titleHit, delegate() { Store.ToggleProjectOpen(nodeId); }, Theme.B(Theme.PanelHi), Theme.Transparent);
            Grid.SetColumn(titleHit, 1);
            g.Children.Add(titleHit);

            StackPanel acts = new StackPanel();
            acts.Orientation = Orientation.Horizontal;
            acts.VerticalAlignment = VerticalAlignment.Center;
            acts.Opacity = 0.62;
            acts.Children.Add(Ui.IconButton("plus", 26, 13, delegate()
            {
                projectAddParent = nodeId; projectAddKind = ProjectKind.Sub;
                projectRenameId = null;
                Store.SetProjectOpen(nodeId, true);
                Refresh();
            }, "添小项目"));
            if (big)
            {
                acts.Children.Add(Ui.IconButton("layers", 26, 13, delegate()
                {
                    projectAddParent = nodeId; projectAddKind = ProjectKind.Stage;
                    projectRenameId = null;
                    Store.SetProjectOpen(nodeId, true);
                    Refresh();
                }, "将项目分段"));
            }
            acts.Children.Add(Ui.IconButton("pencil", 26, 13, delegate()
            {
                projectRenameId = nodeId; projectAddParent = null;
                Refresh();
            }, "改名"));
            if (index > 0) acts.Children.Add(Ui.IconButton("up", 26, 13, delegate() { Store.MoveProject(nodeId, -1); }, "上移"));
            if (index < total - 1) acts.Children.Add(Ui.IconButton("down", 26, 13, delegate() { Store.MoveProject(nodeId, 1); }, "下移"));
            acts.Children.Add(Ui.IconButton("trash", 26, 13, delegate()
            {
                projectRenameId = null; projectAddParent = null;
                Store.DeleteProject(nodeId);      // 直接删（连下級和它们的事项一起）
            }, "删除（连同下面的一起）"));
            Grid.SetColumn(acts, 2);
            g.Children.Add(acts);

            wrap.Children.Add(g);

            if (projectRenameId == nodeId)
                wrap.Children.Add(ProjectComposer(null, ProjectKind.Sub, nodeId, ProjectTree.TitleOf(data, n), "改名，回车即录", indent + 28));
            return wrap;
        }

        /// <summary>最低一级的小项目：就是一条普通事项，勾选、拖动、顺延、删除都跟任务一致。</summary>
        UIElement ProjectSubRow(ProjectNode n, double indent)
        {
            TaskItem it = ProjectTree.ItemOf(Store.Data, n);
            if (it == null) return ProjectHint("这一格没有对应的事项，同步时会自动补一条", indent);
            TaskRow row = MakeRow(it, false);
            row.Margin = new Thickness(indent, 0, 0, 8);
            return row;
        }

        /// <summary>项目页里的行内输入框：新建下級（nodeId 为空就是改名）。</summary>
        Border ProjectComposer(string parentId, int kind, string nodeId, string initial, string watermark, double indent)
        {
            Border wrap = Ui.Round(10, Theme.B(Theme.PanelSoft), Theme.B(Theme.AccentSoft), 1);
            wrap.Padding = new Thickness(10, 5, 10, 6);
            wrap.Margin = new Thickness(indent, 4, 0, 6);

            string renaming = nodeId;
            HintBox box = new HintBox(watermark, 12.5);
            if (initial != null && initial.Length > 0) box.Box.Text = initial;
            box.Submitted = delegate(string text)
            {
                if (renaming != null) { projectRenameId = null; Store.RenameProject(renaming, text); }
                else { projectAddParent = null; Store.AddProject(parentId, kind, text); }
                Refresh();
            };
            box.Box.LostFocus += delegate(object s, RoutedEventArgs e)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(delegate() { CancelProjectInput(renaming, parentId); }));
            };
            wrap.Child = box;
            wrap.Loaded += delegate(object s, RoutedEventArgs e)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,
                    new Action(delegate() { box.FocusInput(); }));
            };
            return wrap;
        }

        /// <summary>行内输入框失焦就撤掉，别在页面上留一个半截的格子。</summary>
        void CancelProjectInput(string nodeId, string parentId)
        {
            bool changed = false;
            if (nodeId != null && projectRenameId == nodeId) { projectRenameId = null; changed = true; }
            else if (nodeId == null && projectAddParent == parentId) { projectAddParent = null; changed = true; }
            if (changed) Refresh();
        }

        Border ProjectHint(string text, double indent)
        {
            Border b = Ui.Round(9, Theme.Transparent);
            b.Margin = new Thickness(indent, 2, 0, 8);
            TextBlock t = Ui.Txt(text, 12, Theme.B(Theme.TextFaint), false);
            t.TextWrapping = TextWrapping.Wrap;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            return b;
        }

        // ---------------- 共用 ----------------

        TaskRow MakeRow(TaskItem t, bool compact)
        {
            TaskRow r = new TaskRow(t, compact);
            r.ToggleRequested = delegate(TaskItem it)
            {
                bool now = !it.Done;
                it.Done = now;
                it.DoneAt = now ? (DateTime?)DateTime.Now : null;
                r.Refresh(true);
                if (now) Celebrate(r, it);
                AnimatedRefresh();
                Store.SetDone(it.Id, now);
            };
            r.EditRequested = delegate(TaskItem it) { EditTask(it); };
            r.DeleteRequested = delegate(TaskItem it) { Store.Delete(it.Id); };
            r.DeferRequested = delegate(TaskItem it) { Store.MoveTo(it.Id, it.Date.AddDays(1)); };
            r.DoubleClickRequested = delegate(TaskItem it) { EditTask(it); };
            r.DragRequested = delegate(TaskRow row, TaskItem it) { BeginTaskDrag(row, it); };
            return r;
        }

        void EditTask(TaskItem task)
        {
            EditDialog dlg = new EditDialog(task.Clone(), Store.Settings.WeekStartMonday);
            dlg.Owner = this;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            bool? ok = dlg.ShowDialog();
            if (ok != true) return;
            if (dlg.DeleteRequested) { Store.Delete(task.Id); return; }
            Store.Update(dlg.Result);
        }
    }
}
