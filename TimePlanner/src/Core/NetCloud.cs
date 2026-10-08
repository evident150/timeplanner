using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace TimePlanner.Core
{
    /// <summary>云端出错时抛这个，界面直接把 Message 显示出来。</summary>
    public class CloudException : Exception
    {
        public CloudException(string msg) : base(msg) { }
    }

    /// <summary>net.json：云端地址与公开密钥（跟着数据目录走，不进仓库、不进 exe）。</summary>
    [DataContract]
    public class CloudConfig
    {
        [DataMember(Name = "url", Order = 0)] public string Url;
        [DataMember(Name = "anonKey", Order = 1)] public string AnonKey;
        [DataMember(Name = "note", Order = 2)] public string Note;
    }

    /// <summary>登录状态。整块用 DPAPI 加密后才落盘，明文令牌不留在磁盘上。</summary>
    [DataContract]
    public class CloudSession
    {
        [DataMember(Name = "access", Order = 0)] public string Access;
        [DataMember(Name = "refresh", Order = 1)] public string Refresh;
        /// <summary>access 到期的 Unix 秒（提前一分钟就算过期，免得卡在边界上）。</summary>
        [DataMember(Name = "expires", Order = 2)] public long Expires;
        [DataMember(Name = "email", Order = 3)] public string Email;
        [DataMember(Name = "uid", Order = 4)] public string Uid;
        [DataMember(Name = "ws", Order = 5)] public string WorkspaceId;
        [DataMember(Name = "wsName", Order = 6)] public string WorkspaceName;
        [DataMember(Name = "invite", Order = 7)] public string Invite;
        [DataMember(Name = "auto", Order = 8)] public bool Auto;
        [DataMember(Name = "lastSync", Order = 9)] public string LastSync;
    }

    [DataContract]
    public class WsInfo
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "invite")] public string Invite;
        [DataMember(Name = "role")] public string Role;
    }

    [DataContract]
    public class CloudMember
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "email")] public string Email;
        [DataMember(Name = "role")] public string Role;
    }

    /// <summary>云端的一行任务。字段名与建表脚本一致，日期一律用字符串过网，免得时区来回换算。</summary>
    [DataContract]
    public class CloudTask
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "workspace_id")] public string WorkspaceId;
        [DataMember(Name = "title")] public string Title;
        [DataMember(Name = "note")] public string Note;
        [DataMember(Name = "date")] public string Date;
        [DataMember(Name = "done")] public bool Done;
        [DataMember(Name = "done_at")] public string DoneAt;
        [DataMember(Name = "priority")] public int Priority;
        [DataMember(Name = "tag")] public string Tag;
        [DataMember(Name = "sort")] public int Sort;
        [DataMember(Name = "project_id")] public string ProjectId;
        [DataMember(Name = "rev")] public int Rev;
        [DataMember(Name = "updated_at")] public string UpdatedAt;
        [DataMember(Name = "deleted_at")] public string DeletedAt;
        [DataMember(Name = "created_by")] public string CreatedBy;
        [DataMember(Name = "completed_by")] public string CompletedBy;
    }

    [DataContract]
    public class CloudProject
    {
        [DataMember(Name = "id")] public string Id;
        [DataMember(Name = "workspace_id")] public string WorkspaceId;
        [DataMember(Name = "parent_id")] public string ParentId;
        [DataMember(Name = "kind")] public int Kind;
        [DataMember(Name = "item_id")] public string ItemId;
        [DataMember(Name = "sort")] public int Sort;
        [DataMember(Name = "open")] public bool Open;
        [DataMember(Name = "title")] public string Title;
        [DataMember(Name = "steps")] public int Steps;
        [DataMember(Name = "reached")] public int Reached;
        [DataMember(Name = "rev")] public int Rev;
        [DataMember(Name = "updated_at")] public string UpdatedAt;
        [DataMember(Name = "deleted_at")] public string DeletedAt;
        [DataMember(Name = "created_by")] public string CreatedBy;
    }

    /// <summary>
    /// 「多用户共用一份计划」的网络客户端。
    /// 三条底线：
    ///   1. 没配置 / 没登录 / 断网时，一切入口都安静退化成「本地照常用」，绝不拦着用户写计划；
    ///   2. 配置、令牌、同步账本全在 %APPDATA%\TimePlanner\cloud\ 下，不进仓库也不进 exe；
    ///   3. 数据文件 data.json 由 Store 独家读写，这里只走 Store 的公开接口。
    /// </summary>
    public static class NetCloud
    {
        static CloudConfig _config;
        static bool _configTried;
        static DateTime _configStamp;
        static CloudSession _session;
        static bool _sessionTried;

        public static string CloudDir
        {
            get
            {
                string d = Path.Combine(Store.DataDir, "cloud");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string ConfigPath { get { return Path.Combine(CloudDir, "net.json"); } }
        public static string SessionPath { get { return Path.Combine(CloudDir, "session.bin"); } }

        /// <summary>exe 旁边那份 net.json（便携用）：发给别人「exe + net.json」两个文件就能连。</summary>
        public static string PortablePath
        {
            get
            {
                try
                {
                    string d = AppDomain.CurrentDomain.BaseDirectory;
                    return d == null ? null : Path.Combine(d, "net.json");
                }
                catch (Exception) { return null; }
            }
        }

        static CloudConfig ReadConfigFile(string path)
        {
            try { return Json<CloudConfig>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (Exception) { return null; }
        }

        /// <summary>模板里那两行占位：填的还是它，就等于没填。</summary>
        static bool Placeholder(CloudConfig c)
        {
            if (c == null) return false;
            string u = c.Url == null ? string.Empty : c.Url.ToLowerInvariant();
            string k = c.AnonKey == null ? string.Empty : c.AnonKey;
            return u.Contains("xxxxxxxx") || k.Contains("粘到") || k.Contains("anon public key");
        }

        /// <summary>没配好时给用户一句话，比让他对着 DNS 报错猜要好。</summary>
        public static string ConfigProblem
        {
            get
            {
                CloudConfig c = ReadConfigFile(ConfigPath);
                if (c == null || Placeholder(c))
                {
                    CloudConfig p = PortablePath == null ? null : ReadConfigFile(PortablePath);
                    if (p != null) c = p;
                }
                if (Placeholder(c)) return "net.json 还是模板占位（xxxxxxxx.supabase.co）—— 把真 url 和 anonKey 填进去，再点「重新读取配置」";
                if (c != null && !IsBlank(c.Url) && !IsBlank(c.AnonKey)) return "地址填了却读不到：" + ConfigPath;
                return "还没填云端地址 —— 填两行就能用：" + ConfigPath + "（或者把 net.json 摆在 exe 旁边）";
            }
        }

        // ---------------- 配置 ----------------

        /// <summary>读到了且填全了才算配置好；没配好时所有联网功能一律显示「去填地址」。</summary>
        public static CloudConfig Config
        {
            get
            {
                // 文件被改过（用户刚把 url / anonKey 填进去）就自动重读，
                // 不用他再想「我明明填了，怎么还报 xxxxxxxx.supabase.co」。
                try
                {
                    FileInfo fi = new FileInfo(ConfigPath);
                    string pp = PortablePath;
                    FileInfo pf = null;
                    if (pp != null)
                    {
                        FileInfo t = new FileInfo(pp);
                        if (t.Exists && !string.Equals(t.FullName, fi.FullName, StringComparison.OrdinalIgnoreCase)) pf = t;
                    }
                    DateTime stamp = fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue;
                    if (pf != null && pf.LastWriteTimeUtc > stamp) stamp = pf.LastWriteTimeUtc;
                    if (!_configTried || stamp != _configStamp)
                    {
                        _configTried = true;
                        _configStamp = stamp;
                        CloudConfig dc = fi.Exists ? ReadConfigFile(fi.FullName) : null;
                        // 数据目录里还是模板（或压根没有）就退到 exe 旁边那份；
                        // 别人拿到「exe + net.json」两个文件直接就能连。
                        if ((dc == null || Placeholder(dc)) && pf != null) dc = ReadConfigFile(pf.FullName);
                        _config = dc;
                    }
                }
                catch (Exception) { _config = null; }
                if (_config == null || IsBlank(_config.Url) || IsBlank(_config.AnonKey) || Placeholder(_config)) return null;
                return _config;
            }
        }

        public static bool Configured { get { return Config != null; } }

        /// <summary>用户填完 net.json 后点「重新读取配置」用。</summary>
        public static void ReloadConfig()
        {
            _configTried = false;
            _config = null;
        }

        /// <summary>登录了但还没选工作区时也得敢问一句。</summary>
        public static bool HasWorkspace
        {
            get { CloudSession s = Session; return s != null && !IsBlank(s.WorkspaceId); }
        }

        /// <summary>改自己的昵称（成员列表里显示的就是它）。</summary>
        public static void RenameSelf(string name)
        {
            if (IsBlank(name)) return;
            string uid = Session == null ? null : Session.Uid;
            if (IsBlank(uid)) return;
            Request("PATCH", Root + "/rest/v1/profiles?id=eq." + uid,
                    "{\"name\":\"" + Esc(name) + "\"}", Token(), "return=minimal");
        }

        /// <summary>写出一个带占位符的 net.json —— 用户只要把两行填上就能用。</summary>
        public static string WriteConfigTemplate()
        {
            if (File.Exists(ConfigPath)) return ConfigPath;
            CloudConfig c = new CloudConfig();
            c.Url = "https://xxxxxxxx.supabase.co";
            c.AnonKey = "把 Supabase 的 anon public key 粘到这里";
            c.Note = "url 与 anonKey 在 Supabase 控制台 Project Settings → API。anonKey 是公开的，可以随 exe 分发；service_role 那把绝对不能填进来。";
            File.WriteAllText(ConfigPath, Pretty(Json(c)), new UTF8Encoding(false));
            _configTried = false;
            return ConfigPath;
        }

        static bool IsBlank(string s) { return s == null || s.Trim().Length == 0; }

        // ---------------- 登录状态 ----------------

        public static CloudSession Session
        {
            get
            {
                if (!_sessionTried)
                {
                    _sessionTried = true;
                    try
                    {
                        if (File.Exists(SessionPath))
                        {
                            byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(SessionPath), null, DataProtectionScope.CurrentUser);
                            _session = Json<CloudSession>(Encoding.UTF8.GetString(raw));
                        }
                    }
                    catch (Exception) { _session = null; }
                }
                return _session;
            }
        }

        public static bool SignedIn { get { return Session != null && !IsBlank(Session.Refresh); } }

        public static void SaveSession()
        {
            try
            {
                if (_session == null) { if (File.Exists(SessionPath)) File.Delete(SessionPath); return; }
                byte[] raw = Encoding.UTF8.GetBytes(Json(_session));
                File.WriteAllBytes(SessionPath, ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) { Diagnostics.Log("云端", "登录状态存不下来：" + ex.Message); }
        }

        public static void SignOut()
        {
            _session = null;
            _sessionTried = true;
            SaveSession();
        }

        // ---------------- 注册 / 登录 ----------------

        public static void SignUp(string email, string password, string name)
        {
            NeedConfig();
            NeedCreds(email, password);
            string body = "{\"email\":\"" + Esc(email) + "\",\"password\":\"" + Esc(password) + "\",\"data\":{\"name\":\"" + Esc(name) + "\"}}";
            string resp = Request("POST", Root + "/auth/v1/signup", body, null, null);
            TokenResponse tr = null;
            try { tr = Json<TokenResponse>(resp); } catch (Exception) { }
            if (tr == null || IsBlank(tr.Access))
            {
                // 后台开着「邮箱确认」时，注册不会直接给令牌。
                throw new CloudException("注册成功，但 Supabase 那边要求先点邮箱确认链接。想直接用，就去后台 Authentication → Providers → Email 里关掉 Confirm email。");
            }
            Adopt(tr, email);
        }

        public static void SignIn(string email, string password)
        {
            NeedConfig();
            NeedCreds(email, password);
            string body = "{\"email\":\"" + Esc(email) + "\",\"password\":\"" + Esc(password) + "\"}";
            string resp = Request("POST", Root + "/auth/v1/token?grant_type=password", body, null, null);
            Adopt(Json<TokenResponse>(resp), email);
        }

        /// <summary>access 快过期就续一次。refresh token 轮换只在真正需要时触发，别每步都刷。</summary>
        public static string EnsureToken()
        {
            CloudSession s = Session;
            if (s == null || IsBlank(s.Refresh)) throw new CloudException("还没登录");
            long now = UnixNow();
            if (!IsBlank(s.Access) && now < s.Expires) return s.Access;
            string body = "{\"refresh_token\":\"" + Esc(s.Refresh) + "\"}";
            string resp = Request("POST", Root + "/auth/v1/token?grant_type=refresh_token", body, null, null);
            TokenResponse tr = Json<TokenResponse>(resp);
            if (tr == null || IsBlank(tr.Access)) throw new CloudException("登录状态已过期，请重新登录");
            Adopt(tr, s.Email);
            return _session.Access;
        }

        static void Adopt(TokenResponse tr, string email)
        {
            if (tr == null) throw new CloudException("服务端没返回令牌");
            // 走 Session 属性，不是裸字段：进程刚起来时裸字段还是 null，
            // 那样会把上次存下来的工作区一起丢掉（还顺手把 session.bin 覆盖成没工作区的）。
            CloudSession s = Session;
            if (s == null) s = new CloudSession();
            s.Access = tr.Access;
            if (!IsBlank(tr.Refresh)) s.Refresh = tr.Refresh;
            int life = tr.ExpiresIn > 0 ? tr.ExpiresIn : 3600;
            s.Expires = UnixNow() + life - 60;
            if (tr.User != null && !IsBlank(tr.User.Email)) s.Email = tr.User.Email;
            else if (!IsBlank(email)) s.Email = email;
            // 换了账号就别把上一条计划接着用：那条是别人名下的，推不上去也拉不下来。
            if (tr.User != null && !IsBlank(tr.User.Id) && !IsBlank(s.Uid) && s.Uid != tr.User.Id)
            {
                s.WorkspaceId = "";
                s.WorkspaceName = "";
                s.Invite = "";
                NetSync.ResetLedger();
            }
            if (tr.User != null) s.Uid = tr.User.Id;
            if (_session == null) s.Auto = true;
            _session = s;
            _sessionTried = true;
            SaveSession();
        }

        // ---------------- 工作区 ----------------

        public static WsInfo CreateWorkspace(string name)
        {
            WsInfo w = Json<WsInfo>(Rpc("tp_create_workspace", "{\"nm\":\"" + Esc(name) + "\"}"));
            UseWorkspace(w);
            return w;
        }

        public static WsInfo JoinWorkspace(string code)
        {
            WsInfo w = Json<WsInfo>(Rpc("tp_join_workspace", "{\"code\":\"" + Esc(code) + "\"}"));
            UseWorkspace(w);
            return w;
        }

        /// <summary>
        /// 登录 / 注册之后自动挂回自己的计划：只有一条就直接进，一条都没有就建一条（默认叫「我的计划」）；
        /// 已经挂着的不动，有好几条的也不替人猜 —— 返回 null，让他去「我的计划」列表里自己挑。
        /// </summary>
        public static WsInfo EnsureWorkspace(string defaultName)
        {
            if (HasWorkspace) return null;
            List<WsInfo> mine = MyWorkspaces();
            if (mine.Count == 1) { UseWorkspace(mine[0]); return mine[0]; }
            if (mine.Count > 1) return null;
            return CreateWorkspace(defaultName);
        }

        public static List<WsInfo> MyWorkspaces()
        {
            string resp = Rpc("tp_my_workspaces", "{}");
            List<WsInfo> list = null;
            try { list = Json<List<WsInfo>>(resp); } catch (Exception) { }
            if (list == null) list = new List<WsInfo>();
            return list;
        }

        public static List<CloudMember> Members()
        {
            string ws = CurrentWorkspace();
            string resp = Rpc("tp_members", "{\"ws\":\"" + Esc(ws) + "\"}");
            List<CloudMember> list = null;
            try { list = Json<List<CloudMember>>(resp); } catch (Exception) { }
            if (list == null) list = new List<CloudMember>();
            return list;
        }

        /// <summary>把工作区钉在当前登录状态上；换工作区就得整份重新对齐，所以清掉同步账本。</summary>
        public static void UseWorkspace(WsInfo w)
        {
            CloudSession s = Session;
            if (s == null) throw new CloudException("还没登录");
            if (w == null || IsBlank(w.Id)) throw new CloudException("工作区信息不对");
            bool changed = s.WorkspaceId != w.Id;
            s.WorkspaceId = w.Id;
            s.WorkspaceName = w.Name;
            s.Invite = w.Invite;
            SaveSession();
            if (changed) NetSync.ResetLedger();
        }

        public static string CurrentWorkspace()
        {
            CloudSession s = Session;
            if (s == null || IsBlank(s.WorkspaceId)) throw new CloudException("还没选工作区");
            return s.WorkspaceId;
        }

        public static void TouchLastSync()
        {
            CloudSession s = Session;
            if (s == null) return;
            s.LastSync = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            SaveSession();
        }

        // ---------------- 数据读写（REST） ----------------

        public static void UpsertTasks(List<CloudTask> rows)
        {
            if (rows == null || rows.Count == 0) return;
            Request("POST", Root + "/rest/v1/tasks?on_conflict=id", Json(rows), Token(),
                    "resolution=merge-duplicates,return=minimal");
        }

        public static void UpsertProjects(List<CloudProject> rows)
        {
            if (rows == null || rows.Count == 0) return;
            Request("POST", Root + "/rest/v1/projects?on_conflict=id", Json(rows), Token(),
                    "resolution=merge-duplicates,return=minimal");
        }

        /// <summary>软删：只写 deleted_at，行还在。真删会让另一台机器上的副本下次又长回来。</summary>
        public static void SoftDelete(string table, List<string> ids)
        {
            if (ids == null || ids.Count == 0) return;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Esc(ids[i])).Append('"');
            }
            string q = Root + "/rest/v1/" + table + "?workspace_id=eq." + CurrentWorkspace() + "&id=in.(" + sb + ")";
            Request("PATCH", q, "{\"deleted_at\":\"" + Iso(DateTime.Now) + "\"}", Token(), "return=minimal");
        }

        public static List<CloudTask> FetchTasks()
        {
            string q = Root + "/rest/v1/tasks?workspace_id=eq." + CurrentWorkspace() + "&select=*&limit=5000";
            List<CloudTask> list = null;
            try { list = Json<List<CloudTask>>(Request("GET", q, null, Token(), null)); } catch (Exception) { }
            if (list == null) list = new List<CloudTask>();
            return list;
        }

        public static List<CloudProject> FetchProjects()
        {
            string q = Root + "/rest/v1/projects?workspace_id=eq." + CurrentWorkspace() + "&select=*&limit=5000";
            List<CloudProject> list = null;
            try { list = Json<List<CloudProject>>(Request("GET", q, null, Token(), null)); } catch (Exception) { }
            if (list == null) list = new List<CloudProject>();
            return list;
        }

        // ---------------- 底下这层 ----------------

        /// <summary>
        /// 用户在控制台里复制到的往往是 \".../rest/v1/\" 这种带尾巴的地址，这里统一削回根；
        /// 填错格式不该是用户的错。
        /// </summary>
        static string Root
        {
            get
            {
                string u = (Config.Url == null ? "" : Config.Url.Trim());
                u = u.TrimEnd('/');
                if (u.EndsWith("/rest/v1", StringComparison.OrdinalIgnoreCase)) u = u.Substring(0, u.Length - 8);
                else if (u.EndsWith("/auth/v1", StringComparison.OrdinalIgnoreCase)) u = u.Substring(0, u.Length - 8);
                return u.TrimEnd('/');
            }
        }

        static void NeedConfig()
        {
            if (!Configured) throw new CloudException("还没填云端地址，先打开 cloud 文件夹里的 net.json 填好 url 和 anonKey");
        }


        /// <summary>空邮箱空密码交上去，Supabase 当成「匿名登录」并回一句看不懂的话，不如在这里拦住。</summary>
        static void NeedCreds(string email, string password)
        {
            if (IsBlank(email) || IsBlank(password)) throw new CloudException("先把邮箱和密码都填上，再点一次");
            if (email.IndexOf('@') < 0) throw new CloudException("邮箱看着不对（少了 @）：" + email);
        }
        static string Token() { return EnsureToken(); }

        static string Rpc(string fn, string body)
        {
            return Request("POST", Root + "/rest/v1/rpc/" + fn, body, Token(), null);
        }

        /// <summary>只探一下能不能连上（不登录、不碰数据）：给「测试连接」按钮用。</summary>
        public static string Ping()
        {
            NeedConfig();
            Request("GET", Root + "/auth/v1/health", null, null, null);
            return "连上了：" + new Uri(Root).Host;
        }

        static string Request(string method, string url, string body, string token, string prefer)
        {
            // 只加协议，绝不写 ServerCertificateValidationCallback 放行 —— 那等于把自己的密码送人。
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.UserAgent = "TimePlanner/" + AppVersion.Number;
            req.Headers["apikey"] = Config.AnonKey;
            if (!IsBlank(token)) req.Headers["Authorization"] = "Bearer " + token;
            if (!IsBlank(prefer)) req.Headers["Prefer"] = prefer;
            req.Accept = "application/json";
            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                req.ContentType = "application/json; charset=utf-8";
                req.ContentLength = bytes.Length;
                using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            }
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException ex)
            {
                string detail = "";
                if (ex.Response != null)
                {
                    try
                    {
                        using (StreamReader sr = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                            detail = sr.ReadToEnd();
                    }
                    catch (Exception) { }
                }
                throw new CloudException(Say(detail, ex.Message));
            }
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\') { sb.Append('\\').Append(c); continue; }
                if (c == '\n') { sb.Append("\\n"); continue; }
                if (c == '\r') { sb.Append("\\r"); continue; }
                if (c == '\t') { sb.Append("\\t"); continue; }
                if (c < ' ') { sb.Append(' '); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Iso(DateTime d) { return d.ToString("yyyy-MM-ddTHH:mm:ss"); }
        public static string Day(DateTime d) { return d.ToString("yyyy-MM-dd"); }

        public static DateTime ParseDay(string s, DateTime fallback)
        {
            DateTime d;
            if (!IsBlank(s) && DateTime.TryParse(s, out d)) return d.Date;
            return fallback.Date;
        }

        public static DateTime? ParseIso(string s)
        {
            DateTime d;
            if (!IsBlank(s) && DateTime.TryParse(s, out d)) return d;
            return null;
        }

        static long UnixNow() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; }

        public static string Json(object o)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(o.GetType());
            using (MemoryStream ms = new MemoryStream())
            {
                ser.WriteObject(ms, o);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static T Json<T>(string text)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(text)))
            {
                return (T)ser.ReadObject(ms);
            }
        }

        /// <summary>只为了让 net.json 看着像人写的（一行一个字段），不是必须的。</summary>
        static string Pretty(string json)
        {
            StringBuilder sb = new StringBuilder();
            bool inStr = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) inStr = !inStr;
                if (!inStr && c == ',') { sb.Append(',').Append('\n'); continue; }
                if (!inStr && c == '{') { sb.Append('{').Append('\n'); continue; }
                if (!inStr && c == '}') { sb.Append('\n').Append('}'); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>把服务端的报错抽成人话：PostgREST 的字段名试一圈，抽不出来就退回原文。</summary>
        static string Say(string body, string fallback)
        {
            string s = Pick(body, "error_description");
            if (IsBlank(s)) s = Pick(body, "message");
            if (IsBlank(s)) s = Pick(body, "msg");
            if (IsBlank(s)) s = Pick(body, "error");
            if (IsBlank(s)) s = Pick(body, "hint");
            if (IsBlank(s)) s = fallback;
            if (s == null) s = "云端请求失败";
            if (s.Length > 200) s = s.Substring(0, 200);
            return s;
        }

        static string Pick(string body, string key)
        {
            if (body == null) return null;
            string k = "\"" + key + "\"";
            int i = body.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) return null;
            i = body.IndexOf(':', i + k.Length);
            if (i < 0) return null;
            i++;
            while (i < body.Length && (body[i] == ' ' || body[i] == '\t')) i++;
            if (i >= body.Length) return null;
            if (body[i] == '"')
            {
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < body.Length && body[i] != '"')
                {
                    if (body[i] == '\\' && i + 1 < body.Length)
                    {
                        i++;
                        if (body[i] == 'n') sb.Append('\n');
                        else if (body[i] == 't') sb.Append('\t');
                        else sb.Append(body[i]);
                    }
                    else sb.Append(body[i]);
                    i++;
                }
                return sb.ToString();
            }
            int j = i;
            while (j < body.Length && body[j] != ',' && body[j] != '}') j++;
            return body.Substring(i, j - i).Trim();
        }

        [DataContract]
        public class TokenResponse
        {
            [DataMember(Name = "access_token")] public string Access;
            [DataMember(Name = "refresh_token")] public string Refresh;
            [DataMember(Name = "expires_in")] public int ExpiresIn;
            [DataMember(Name = "user")] public UserInfo User;
        }

        [DataContract]
        public class UserInfo
        {
            [DataMember(Name = "id")] public string Id;
            [DataMember(Name = "email")] public string Email;
        }
    }
}
