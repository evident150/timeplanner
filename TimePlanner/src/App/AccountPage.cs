using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TimePlanner.Core;

namespace TimePlanner.App
{
    public partial class MainWindow
    {
        /// <summary>
        /// 「云端共用」页。这一页只做门面和触发：
        /// 网络与合并全在 TimePlanner.Core 里，界面不直接碰网，也不在后台线程碰数据。
        /// </summary>
        UIElement BuildAccountPage()
        {
            pageTitle.Text = "云 端 共 用";
            pageSubtitle.Text = CloudSummary();
            pageActions.Children.Clear();
            pageActions.Children.Add(Ui.TextButton("重新读取配置", delegate()
            {
                NetCloud.ReloadConfig();
                AccountAuto.Status = "已重新读 net.json";
                Refresh();
            }, false));
            // 连不上时先点这个：它把最原始的错误原样报出来，省得靠猜。
            pageActions.Children.Add(Ui.TextButton("测试连接", delegate()
            {
                AccountAuto.Run(this, delegate() { return NetCloud.Ping(); });
            }, false));
            if (NetCloud.SignedIn && NetCloud.HasWorkspace)
                pageActions.Children.Add(Ui.TextButton("立即同步", delegate() { AccountAuto.SyncNow(this, SyncWay.Both); }, true));

            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(26, 0, 26, 26);
            sp.MaxWidth = 760;
            sp.HorizontalAlignment = HorizontalAlignment.Left;

            // 最近一次的结果 / 正在忙 —— 放最上面，一进来就看得见
            if (AccountAuto.Busy || !Blank(AccountAuto.Status))
            {
                Border line = Ui.Round(10, Theme.B(Theme.Panel), Theme.B(Theme.Border), 1);
                line.Padding = new Thickness(14, 10, 14, 10);
                line.Margin = new Thickness(0, 0, 0, 14);
                line.Child = Para(AccountAuto.Busy ? "正在和云端对齐…" : AccountAuto.Status, AccountAuto.Busy);
                sp.Children.Add(line);
            }

            if (!NetCloud.Configured)
            {
                sp.Children.Add(BuildCloudSetupCard());
            }
            else if (!NetCloud.SignedIn)
            {
                sp.Children.Add(BuildSignInCard());
            }
            else
            {
                if (!NetCloud.HasWorkspace) sp.Children.Add(BuildWorkspaceCard());
                else
                {
                    sp.Children.Add(BuildSyncCard());
                    sp.Children.Add(BuildMembersCard());
                }
                if (AccountAuto.Plans != null && AccountAuto.Plans.Count > 0) sp.Children.Add(BuildPlansCard());
                sp.Children.Add(BuildAccountCard());
            }

            StackPanel wrap = new StackPanel();
            wrap.Children.Add(sp);
            return Ui.Scroll(wrap);
        }

        string CloudSummary()
        {
            if (AccountAuto.Busy) return "正在和云端对齐…";
            if (!NetCloud.Configured) return NetCloud.ConfigProblem;
            string host = HostOf(NetCloud.Config.Url);
            if (!NetCloud.SignedIn) return "登录之后，就能和家里人 / 同事共用同一份计划　·　当前地址：" + host;
            if (!NetCloud.HasWorkspace) return "已登录，还差一步：建一个工作区，或用邀请码加入　·　" + host;
            string ws = NetCloud.Session.WorkspaceName;
            string at = NetCloud.Session.LastSync;
            return (Blank(ws) ? "共用中" : "共用中：" + ws)
                + (Blank(at) ? "" : "　·　上次同步 " + at) + "　·　" + host;
        }

        /// <summary>把 URL 缩成主机名：页面上显示一行，好让人一眼看出程序读的是哪份配置。</summary>
        static string HostOf(string url)
        {
            if (Blank(url)) return "（地址是空的）";
            try { return new Uri(url.Trim()).Host; }
            catch (Exception) { return url.Trim(); }
        }

        // ---------------- 小工具 ----------------

        /// <summary>登录 / 注册完之后那句尾巴：挂上了哪条计划，或者为什么没挂上。</summary>
        static string PlanTail(WsInfo mine)
        {
            if (mine != null) return "；已挂上「" + mine.Name + "」";
            if (NetCloud.HasWorkspace) return "";
            return "；你有好几条计划，到下面「我的计划」里挑一条";
        }

        internal static bool Blank(string s) { return s == null || s.Trim().Length == 0; }

        static TextBlock Para(string text, bool faint)
        {
            TextBlock t = Ui.Txt(text, 12.5, Theme.B(faint ? Theme.TextFaint : Theme.TextMuted), false);
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        Border Note(string text)
        {
            Border b = Ui.Round(10, Theme.B(Theme.PanelHi), null, 0);
            b.Padding = new Thickness(12, 9, 12, 9);
            b.Margin = new Thickness(0, 12, 0, 0);
            b.Child = Para(text, true);
            return b;
        }

        // ---------------- 登录区的放大版控件 ----------------
        // 邮箱、密码是要看清、慢慢敲的东西，单独放大一号；
        // 别的卡片照原来的尺寸走，公共控件（UiKit）一个都不动。

        const double LoginFont = 15.5;

        static HintBox BigBox(string watermark)
        {
            HintBox b = new HintBox(watermark, LoginFont);
            b.MinWidth = 260;
            b.MinHeight = 34;
            return b;
        }

        static PasswordBox BigPwd()
        {
            PasswordBox p = new PasswordBox();
            p.Background = Theme.B(Colors.Transparent);
            p.Foreground = Theme.B(Theme.Text);
            p.CaretBrush = Theme.B(Theme.Accent);
            p.BorderThickness = new Thickness(0);
            p.FontFamily = Theme.Font;
            p.FontSize = LoginFont;
            p.Padding = new Thickness(0);
            p.VerticalContentAlignment = VerticalAlignment.Center;
            p.MinWidth = 260;
            p.MinHeight = 34;
            return p;
        }

        /// <summary>比 Ui.TextButton 大一号的按钮，只给登录区用。</summary>
        static Border BigButton(string text, Action onClick, bool primary)
        {
            Border b = Ui.Round(10, primary ? Theme.B(Theme.Accent) : Theme.B(Theme.PanelHi),
                primary ? null : Theme.B(Theme.Border), 1);
            b.Padding = new Thickness(22, 11, 22, 12);
            b.Margin = new Thickness(0, 0, 10, 0);
            b.Child = Ui.Txt(text, LoginFont, primary ? Theme.B(Theme.OnAccent) : Theme.B(Theme.Text), true);
            b.Cursor = Cursors.Hand;
            Ui.Click(b, onClick, primary ? Theme.B(Theme.AccentDeep) : Theme.B(Theme.Border),
                primary ? Theme.B(Theme.Accent) : Theme.B(Theme.PanelHi));
            return b;
        }

        // ---------------- 各自的卡片 ----------------

        Border BuildCloudSetupCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(Para("地址和 anonKey 都在 Supabase 控制台的 Project Settings → API 里。"
                + "anonKey 是公开的，跟着 exe 发出去也没关系 —— 真正拦人的是服务端那套 RLS 规则。", false));
            body.Children.Add(Note("千万别把 service_role 那把填进来：它能绕过所有权限，等于把所有人的计划挂到公网上。"));
            body.Children.Add(Note("建表脚本在仓库的 TimePlanner\\tools\\supabase-schema.sql，"
                + "Supabase 控制台 → SQL Editor 里整段粘进去跑一次。"));

            StackPanel acts = new StackPanel();
            acts.Orientation = Orientation.Horizontal;
            acts.Margin = new Thickness(0, 14, 0, 0);
            acts.Children.Add(Ui.TextButton("生成 net.json 模板", delegate()
            {
                try
                {
                    string p = NetCloud.WriteConfigTemplate();
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + p + "\"");
                    AccountAuto.Status = "模板已生成：" + p + "　填好 url 和 anonKey，再点右上角「重新读取配置」";
                }
                catch (Exception ex) { AccountAuto.Status = "打不开：" + ex.Message; }
                Refresh();
            }, true));
            acts.Children.Add(Ui.TextButton("打开 cloud 文件夹", delegate()
            {
                try { System.Diagnostics.Process.Start("explorer.exe", NetCloud.CloudDir); }
                catch (Exception ex) { AccountAuto.Status = "打不开：" + ex.Message; }
                Refresh();
            }, false));
            body.Children.Add(acts);
            return Cards.Panel("这一步要你动手", "两分钟的事，做完就再也不用管了", body);
        }

        Border BuildSignInCard()
        {
            StackPanel body = new StackPanel();
            HintBox email = BigBox("邮箱");
            PasswordBox pwd = BigPwd();
            HintBox nick = BigBox("昵称（只有注册时用得上）");
            body.Children.Add(Cards.Row("邮箱", null, email));
            body.Children.Add(Cards.Row("密码", null, pwd));
            body.Children.Add(Cards.Row("昵称", "成员列表里显示的就是它", nick));

            StackPanel acts = new StackPanel();
            acts.Orientation = Orientation.Horizontal;
            acts.Margin = new Thickness(0, 16, 0, 0);
            acts.Children.Add(BigButton("登录", delegate()
            {
                string mail = email.Box.Text.Trim();
                string pass = pwd.Password;
                AccountAuto.Run(this, delegate()
                {
                    NetCloud.SignIn(mail, pass);
                    if (!Blank(nick.Box.Text)) NetCloud.RenameSelf(nick.Box.Text.Trim());
                    return "登录成功：" + mail;
                });
            }, true));
            acts.Children.Add(BigButton("注册新账号", delegate()
            {
                string mail = email.Box.Text.Trim();
                string pass = pwd.Password;
                string nm = nick.Box.Text.Trim();
                AccountAuto.RunThenSync(this, delegate()
                {
                    NetCloud.SignUp(mail, pass, Blank(nm) ? null : nm);
                    WsInfo mine = NetCloud.EnsureWorkspace("我的计划");
                    return "注册成功，已登录" + PlanTail(mine);
                });
            }, false));
            body.Children.Add(acts);
            pwd.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                string mail = email.Box.Text.Trim();
                string typed = pwd.Password;
                AccountAuto.RunThenSync(this, delegate()
                {
                    NetCloud.SignIn(mail, typed);
                    WsInfo mine = NetCloud.EnsureWorkspace("我的计划");
                    return "登录成功：" + mail + PlanTail(mine);
                });
            };
            return Cards.Panel("登录 / 注册", "账号在云端，本地这份计划照旧，不会因为登录就串了", body);
        }

        Border BuildWorkspaceCard()
        {
            StackPanel body = new StackPanel();
            HintBox wsName = new HintBox("给这份共用计划起个名字", 13);
            HintBox code = new HintBox("别人给你的邀请码", 13);
            body.Children.Add(Cards.Row("新建", "建好后把邀请码发给要一起用的人", wsName));
            body.Children.Add(Cards.Row("加入", "同一串邀请码 = 同一份计划", code));

            StackPanel acts = new StackPanel();
            acts.Orientation = Orientation.Horizontal;
            acts.Margin = new Thickness(0, 14, 0, 0);
            acts.Children.Add(Ui.TextButton("新建工作区", delegate()
            {
                string nm = wsName.Box.Text.Trim();
                AccountAuto.RunThenSync(this, delegate()
                {
                    WsInfo w = NetCloud.CreateWorkspace(nm);
                    return "「" + w.Name + "」建好了，邀请码：" + w.Invite;
                });
            }, true));
            acts.Children.Add(Ui.TextButton("用邀请码加入", delegate()
            {
                string c = code.Box.Text.Trim();
                AccountAuto.RunThenSync(this, delegate()
                {
                    WsInfo w = NetCloud.JoinWorkspace(c);
                    return "已加入「" + w.Name + "」，正在把两边的计划对齐";
                });
            }, false));
            body.Children.Add(acts);
            body.Children.Add(Note("建好或加入之后会自动对齐一次：本地已经有的任务会推上去，不会因为入伙就没了。"));
            return Cards.Panel("选一份共用计划", "一个工作区 = 一份大家共用的计划", body);
        }

        Border BuildSyncCard()
        {
            CloudSession s = NetCloud.Session;
            StackPanel body = new StackPanel();

            string invite = s.Invite;
            HintBox code = new HintBox("（还没读到邀请码）", 13);
            code.Box.Text = Blank(invite) ? "" : invite;
            code.Box.IsReadOnly = true;
            StackPanel line = new StackPanel();
            line.Orientation = Orientation.Horizontal;
            line.Children.Add(code);
            Border copy = Ui.TextButton("复制", delegate()
            {
                try { Clipboard.SetText(Blank(invite) ? "" : invite); AccountAuto.Status = "邀请码已复制，发给要一起用的人就行"; }
                catch (Exception ex) { AccountAuto.Status = "复制失败：" + ex.Message; }
                Refresh();
            }, false);
            copy.Margin = new Thickness(10, 0, 0, 0);
            line.Children.Add(copy);
            body.Children.Add(Cards.Row("邀请码", "同一串邀请码 = 同一份计划", line));

            Switch auto = new Switch(s.Auto);
            auto.Changed = delegate(bool on)
            {
                NetCloud.Session.Auto = on;
                NetCloud.SaveSession();
                AccountAuto.Status = on ? "自动同步开着：每 20 秒悄悄对一次" : "自动同步关了，只能手动点「立即同步」";
                Refresh();
            };
            body.Children.Add(Cards.Row("自动同步", "20 秒对一次；关掉就只能手动同步", auto));

            StackPanel acts = new StackPanel();
            acts.Orientation = Orientation.Horizontal;
            acts.Margin = new Thickness(0, 14, 0, 0);
            acts.Children.Add(Ui.TextButton("立即同步", delegate() { AccountAuto.SyncNow(this, SyncWay.Both); }, true));
            acts.Children.Add(Ui.TextButton("只上传", delegate() { AccountAuto.SyncNow(this, SyncWay.Upload); }, false));
            acts.Children.Add(Ui.TextButton("只下载", delegate() { AccountAuto.SyncNow(this, SyncWay.Download); }, false));
            body.Children.Add(acts);

            body.Children.Add(Note("只推你改过的那几条，从不整份盖掉云端；下载之前先给 data.json 留一份快照（snapshots 目录）；"
                + "别人删掉的条目本地也会跟着删，但你本地改过的那条永远留你的。"));
            return Cards.Panel("共用这一份计划", "两边都动过同一条时，先留你这边的，下一次同步再推上去", body);
        }

        Border BuildMembersCard()
        {
            StackPanel body = new StackPanel();
            List<CloudMember> list = AccountAuto.Members;
            if (list == null || list.Count == 0)
            {
                body.Children.Add(Para("还没读到成员名单 —— 点一次「立即同步」就能看到。", true));
            }
            else
            {
                string me = NetCloud.Session == null ? null : NetCloud.Session.Uid;
                for (int i = 0; i < list.Count; i++)
                {
                    CloudMember m = list[i];
                    string label = Blank(m.Name) ? m.Email : m.Name;
                    if (Blank(label)) label = "成员";
                    string role = m.Role == "owner" ? "发起人" : "成员";
                    bool isMe = m.Id == me;
                    TextBlock row = Ui.Txt((isMe ? "· " : "　") + label + "　（" + role + (isMe ? "，就是你" : "") + "）",
                                           12.5, Theme.B(isMe ? Theme.Text : Theme.TextMuted), isMe);
                    row.Margin = new Thickness(0, 3, 0, 3);
                    row.TextWrapping = TextWrapping.Wrap;
                    body.Children.Add(row);
                }
            }
            return Cards.Panel("一起用的人", "按加入顺序排，昵称在账号里改", body);
        }

        /// <summary>「我的计划」：登录后自动挂上的那条，以及以后可能开出来的别的条。</summary>
        Border BuildPlansCard()
        {
            StackPanel body = new StackPanel();
            List<WsInfo> list = AccountAuto.Plans;
            string cur = NetCloud.Session == null ? "" : NetCloud.Session.WorkspaceId;
            for (int i = 0; i < list.Count; i++)
            {
                WsInfo w = list[i];
                bool here = w.Id == cur;
                StackPanel line = new StackPanel();
                line.Orientation = Orientation.Horizontal;
                line.Margin = new Thickness(0, 3, 0, 3);
                TextBlock name = Ui.Txt((here ? "· " : "") + (Blank(w.Name) ? "（没起名字）" : w.Name)
                    + "　" + (w.Role == "owner" ? "我发起的" : "别人拉我进来的"),
                    13, Theme.B(here ? Theme.Text : Theme.TextMuted), here);
                name.VerticalAlignment = VerticalAlignment.Center;
                line.Children.Add(name);
                if (!here)
                {
                    WsInfo target = w;
                    Border go = Ui.TextButton("进入", delegate()
                    {
                        AccountAuto.RunThenSync(this, delegate()
                        {
                            NetCloud.UseWorkspace(target);
                            return "已切到「" + target.Name + "」";
                        });
                    }, false);
                    go.Margin = new Thickness(10, 0, 0, 0);
                    line.Children.Add(go);
                }
                body.Children.Add(line);
            }
            body.Children.Add(Note("同一串邀请码 = 同一份计划：把当前这条的邀请码发给要一起做的人，"
                + "他那边「用邀请码加入」之后，两边都能加任务、勾进度。"
                + "切到另一条计划时，本机现有的任务会跟着带过去对齐。"));
            return Cards.Panel("我的计划", "登录后自动挂回你自己的这条；换台电脑登录也是它", body);
        }

        Border BuildAccountCard()
        {
            StackPanel body = new StackPanel();
            body.Children.Add(Para("退出只清掉本机的登录状态；云端那份计划和你本地的任务都原样留着，不会少一条。", true));
            StackPanel acts = new StackPanel();
            acts.Orientation = Orientation.Horizontal;
            acts.Margin = new Thickness(0, 12, 0, 0);
            acts.Children.Add(Ui.TextButton("退出登录", delegate()
            {
                NetCloud.SignOut();
                AccountAuto.Members = null;
                AccountAuto.Plans = null;
                AccountAuto.Status = "已退出登录，本地计划照常用";
                Refresh();
            }, false));
            body.Children.Add(acts);
            return Cards.Panel("当前账号", NetCloud.Session == null ? "" : NetCloud.Session.Email, body);
        }
    }

    /// <summary>
    /// 网络活儿全从这儿派出去，理由只有一条：**网络不能上界面线程**。
    ///
    /// 一趟同步固定是「界面线程准备 → 后台线程发网络 → 界面线程合并」：
    /// 准备和合并都要读 / 写 Store，必须在界面线程（别人正在改列表时去读，会撞出「集合已修改」）；
    /// 中间的 HTTP 必须在后台，不然一卡十几秒，抗卡死那套全废。
    /// </summary>
    internal static class AccountAuto
    {
        public static string Status = "";
        public static List<CloudMember> Members;
        public static List<WsInfo> Plans;
        static bool _busy;
        static DispatcherTimer _timer;
        static MainWindow _win;

        public static bool Busy { get { return _busy; } }

        /// <summary>在主窗口起来的时候挂一次，之后自动同步就跟在不在这一页无关了。</summary>
        public static void Start(MainWindow win)
        {
            _win = win;
            if (_timer != null) return;
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(20);
            _timer.Tick += delegate(object s, EventArgs e) { Tick(); };
            _timer.Start();
        }

        static void Tick()
        {
            if (_busy || _win == null) return;
            if (!NetCloud.Configured || !NetCloud.SignedIn) return;
            CloudSession ses = NetCloud.Session;
            if (ses == null || !ses.Auto || !NetCloud.HasWorkspace) return;
            SyncNow(_win, SyncWay.Both);
        }

        public static void SyncNow(MainWindow win, SyncWay way)
        {
            if (Busy) { Status = "上一次还没跑完，稍等一下"; win.Refresh(); return; }

            SyncJob job;
            try { job = NetSync.Prepare(win.Store, way); }        // 界面线程：只读本地，不碰网
            catch (Exception ex) { Status = "失败：" + ex.Message; win.Refresh(); return; }

            _busy = true;
            Status = "";
            win.Refresh();

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                string err = null;
                List<CloudMember> mem = null;
                List<WsInfo> plans = null;
                try { NetSync.Send(job); }                          // 后台线程：只发网络，不碰 store
                catch (Exception ex) { err = ex.Message; }
                if (err == null)
                {
                    try { mem = NetCloud.Members(); } catch (Exception) { }
                    try { plans = NetCloud.MyWorkspaces(); } catch (Exception) { }
                }
                win.Dispatcher.BeginInvoke(new Action(delegate()
                {
                    string msg = null;
                    if (err == null)
                    {
                        try
                        {
                            MergeReport rep = NetSync.Finish(win.Store, job);   // 界面线程：合并 + 落盘
                            msg = rep.Describe();
                            if (job.Pushed > 0) msg = "送上去 " + job.Pushed + " 条；" + msg;
                            if (way == SyncWay.Upload) msg = "只上传：" + msg;
                            else if (way == SyncWay.Download) msg = "只下载：" + msg;
                        }
                        catch (Exception ex) { err = ex.Message; }
                    }
                    if (mem != null) Members = mem;
                    if (plans != null) Plans = plans;
                    _busy = false;
                    Status = err != null ? "失败：" + err : (msg == null ? "好了" : msg);
                    if (err != null) Diagnostics.Log("云端", "同步失败：" + err);
                    win.Refresh();
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>登录 / 注册 / 建工作区这类「不碰 Store」的活儿走这条。</summary>
        public static void Run(MainWindow win, Func<string> work) { Run(win, work, false); }

        /// <summary>干完顺手把这条计划对齐一次 —— 登录、注册、建 / 加入计划都该立刻对不对。</summary>
        public static void RunThenSync(MainWindow win, Func<string> work) { Run(win, work, true); }

        public static void Run(MainWindow win, Func<string> work, bool thenSync)
        {
            if (Busy) { Status = "上一次还没跑完，稍等一下"; win.Refresh(); return; }
            _busy = true;
            Status = "";
            win.Refresh();

            System.Threading.Thread t = new System.Threading.Thread(delegate()
            {
                string msg = null;
                string err = null;
                try { msg = work(); }
                catch (Exception ex) { err = ex.Message; }
                win.Dispatcher.BeginInvoke(new Action(delegate()
                {
                    _busy = false;
                    Status = err != null ? "失败：" + err : (msg == null ? "好了" : msg);
                    if (err != null) Diagnostics.Log("云端", "失败：" + err);
                    win.Refresh();
                    if (thenSync && err == null && NetCloud.SignedIn && NetCloud.HasWorkspace) SyncNow(win, SyncWay.Both);
                }));
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
