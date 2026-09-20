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
                Border defer = Ui.TextButton("未竟之事，顺延明日", delegate()
                {
                    Store.Defer(day, day.AddDays(1));
                    DayAnchor = day.AddDays(1);
                }, false);
                defer.Margin = new Thickness(10, 0, 0, 0);
                pageActions.Children.Add(defer);
            }

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(26, 0, 26, 26);

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
            look.Children.Add(Cards.Row("开机自动启动", "登录后自动在托盘运行，并按设置显示桌面插件。", auto));
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
