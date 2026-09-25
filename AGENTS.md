# AGENTS.md · 时间规划 TimePlanner

## 推送与发版：等用户发话

- **改动默认先在经典版生效**（用户 2026-09-23 定的）。每改完一处，就跑
  `powershell -NoProfile -ExecutionPolicy Bypass -File sync-classic.ps1`：把内核文件与
  `[需要移植]` 的界面改动同步进 `work/classic` 并编译验证 —— 经典版这条线始终是「最新、能跑」的那条。
- **特别版只在用户点名时才动**：没说就别改 `TimePlanner/version.txt`、别跑 `release.ps1`、别打包。
  特别版的编号由用户统一规定，现在是 `sp2`、`sp3` …（见 `CHANGELOG.md`）。
- **不要主动 commit / push。** 小改动只在本地改、本地编译验证，攒着就行。
- 用户说「推新版」时，才一次性把这些一起更新（两条线各自发各自的，**经典版在前**）：
  1. **先写 `CHANGELOG.md`**（仓库根，唯一的更新日志）：两条线各自的版本段落 + 每条标 `[内核]` /
     `[特别版]` / `[经典版]` / `[需要移植]`。三份 README/使用说明里有摘要，日志是源头。
  2. 经典版：`powershell -File sync-classic.ps1 -Version <经典版号> -Release`（没指定就 +0.0.1；铺
     `outputs/TimePlanner-<版本>-classic/` + 打 zip；`[需要移植]` 的界面改动脚本自动搬）。
  3. 特别版（用户点名了才做）：改 `TimePlanner/version.txt`（`sp1`、`sp2` …），
     跑 `TimePlanner/release.ps1`：编译 + 出截图 + 铺 `outputs/TimePlanner-<版本>-special/`
     + 打 zip + 刷新仓库根目录的 `exe/` 免编译包（都会带上 `CHANGELOG.md`）
  4. `git add -A` + commit，提交信息开头写「时间规划 TimePlanner <版本> 经典版」；
     特别版也一起发了就写「时间规划 TimePlanner <特别版号> 特别版 + <经典版号> 经典版」
  5. 打 tag：经典版 `classic/v<版本>`，特别版 `special/sp<n>`，推 `main` 和 tag
  6. 建 GitHub 发行版（详下）
- 用户说过的原话：「以后小更新不推，我会告诉你什么时候推新版，然后一起全部更新。」
  「以后该电脑的改动优先在经典版生效，特殊版有我统一规定生效。」

## 建 GitHub 发行版（仓库 evident150/timeplanner）

- 本机没装 `gh`，用机器上已存的 git 凭据换 token（跟 push 用的是同一套，不用用户给）：
  `"protocol=https`nhost=github.com`n`n" | git credential fill`，取 `password=` 那一行。
- 建：`POST https://api.github.com/repos/evident150/timeplanner/releases`，
  body 用 UTF-8 字节 + `Content-Type: application/json; charset=utf-8`（中文别走 query string）。
- 传附件：`POST https://uploads.github.com/repos/evident150/timeplanner/releases/<id>/assets?name=<名字>`，
  `Content-Type: application/octet-stream` + `-InFile`。
- **附件名用 ASCII**：带中文的名字会被 GitHub 丢成 `TimePlanner-1.5-.zip`（上传成功但要再 PATCH 改名）。
- 标题写清楚是哪条线（「特别版 sp1」/「经典版 1.5.1」），正文贴上 `CHANGELOG.md` 里对应那段。
- 建发行版时按版本从低到高建，最后一条就是「最新」；或显式给 `make_latest`。
- 旧标签 `v1.5` 别删（发行版和外面的链接还指着它）；`v1.6` / `v1.7` 已经改名成
  `special/v1.6` / `special/v1.7`：**2026-09-24 已经办完** —— 远端旧名删了
  （`git push origin :refs/tags/v1.6 :refs/tags/v1.7`），那两条老发行版也 PATCH 成新标签名了，
  远端现在有 `v1.5`、`classic/v1.5.2`、`classic/v1.5.3`、`special/sp1`、`special/sp2`、`special/v1.6`、`special/v1.7`。
  以后再改标签名，记得把指着它的发行版一起 PATCH 过去，别让发行版悬空。

## 两条产品线（版本隔离）

- **当前版本（2026-09-25）**：特别版最新 `sp2`（标签 `special/sp2`，交付物 `TimePlanner-sp2-special-app.zip`，
  也是 GitHub 上的「最新」）；经典版最新 `1.5.3`（标签 `classic/v1.5.3`，交付物 `TimePlanner-1.5.3-classic.zip`）。
- **两条线、两个版本序列，各走各的**：
  **特别版**（本仓库主线 `main`）= 圣旨皮肤 + 小人，序列 1.6 → 1.7 → sp1 → sp2 …（1.8 就是 sp1），
  标签 `special/sp<n>`（1.6、1.7 那两个老标签还是 `special/v1.6`、`special/v1.7`），
  交付物 `TimePlanner-sp<n>-special`；
  **经典版** = 1.5 那套深色界面，序列 1.5 → 1.5.1 …，标签 `classic/v<版本>`
  （`v1.5` 是它以前的名字，同一个提交），交付物 `TimePlanner-<版本>-classic`。
  跟进方式见 `CHANGELOG.md` 与 `sync-classic.ps1`。
- **运行隔离不能写死名字**：单实例互斥体、进程间信号灯、开机自启条目一律用
  `src/Core/Install.cs` 里的 `Install.AppMutex / WidgetMutex / SignalMain / SignalWidget /
  AutoStartName`（= 版别 + 安装目录短哈希；**故意不含版本号**——原地换新版 exe 仍是同一个身份，
  升级时不会新旧两个一起跑）。找同名进程一律走 `Install.Siblings`，
  别再用 `Process.GetProcessesByName` 裸查名字 —— 那会误杀别的版本目录里的插件。
- **exe 只有一个运行位置（2026-09-25 用户定的）**：经典版 `C:\Users\24889\Documents\Codex\TimePlanner-1.5-classic\`、
  特别版 `C:\Users\24889\Documents\Codex\TimePlanner-special\` —— 都在工作区外面、目录名不带日期也不带版本号。
  发版 / 更新完就把 exe 铺过去：特别版 `release.ps1`（第 8 步，默认就铺 `TimePlanner-special`，
  `-InstallDir` 换地方、`-NoInstall` 只打包不铺）；经典版 `sync-classic.ps1 -InstallDir "<目录>"`（第 7 步）。
  两边都是「停掉那个目录里跑着的实例 → 旧 exe 备份成 `*.bak-<时间戳>` → 铺 → 拉起来」。
  日期只用来开日志 / 产物 / 快照那些目录，**exe 别放日期目录里**（`2026-09-15\wo-x\TimePlanner\dist`
  只是编译产物，不是给人长期双击的地方）。
- 数据只有一份（`%APPDATA%\TimePlanner\data.json`），两条线共用，**别做按版本分家的数据目录**。
- 经典版源码树：`work/classic`（`git worktree`，detached 在 `classic/v1.5`）。同步脚本会往里拷
  内核文件、迁移入口、再把 `[需要移植]` 的界面改动按文字替换搬过去（脚本第 3.5 节）；那棵树里的
  改动**永远不提交**，重建就是 `git worktree remove --force work/classic`。
  新搬一处界面改动就加一段 `Swap`：锚点用目标文件里的原文，跑两遍结果要一样（幂等），
  文件行尾由 `Swap` 自己按目标文件对齐，别手拼 \r\n。

## 环境与构建

- 工作区根 = git 仓库根：`C:\Users\24889\Documents\Codex\2026-09-15\wo-x`，项目在 `TimePlanner\`。
- 原生 WPF + .NET Framework 4.8 + `csc.exe`（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）：
  **`/langversion:5`**，不能用 lambda `=>`、字符串插值、自动属性、`?.`、`nameof`、`out var`；零第三方依赖、无 XAML。
- 编译：`cd TimePlanner; powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` → `TimePlanner\dist\`。
  经典版：`build.ps1 -Edition classic`（写进 AppVersion 的版别标记，隔离名字里也用它）。
  **有实例在跑会报 CS0016**，但别按进程名一把杀：先只关本目录那份
  （`Get-Process ... | Where-Object { $_.Path -like "$PWD*" } | Stop-Process -Force`），
  用户可能正开着别的版本（比如经典版）。
- 发版（特别版线）：`powershell -NoProfile -ExecutionPolicy Bypass -File release.ps1`（只停本目录里
  正在跑的实例，结束时把主程序拉回托盘）；经典版线走 `sync-classic.ps1`，两条线版本号互不覆盖。
- 出图核对：`Start-Process -FilePath "$PWD\dist\TimePlanner.exe" -ArgumentList '--render',"<目录>" -Wait -WindowStyle Hidden`
  （必须 `Start-Process`，直接 `&` 调用不生效）。插件同理。
- 用户数据 `%APPDATA%\TimePlanner\data.json` **禁止改动**（`--render` 是只读的）。

## 这台机器上的坑

- 源码行尾/编码不统一：**改之前先 `git ls-files --eol <文件>`**，按原样写回（有的 CRLF、有的 LF、有的带 BOM）。
- PowerShell 的递归 `Remove-Item` 常被策略拒绝；删目录用 Node 的 `fs.rmSync`。
- 别用 `git archive | tar -x` 这种管道（PowerShell 会把二进制流搅坏，报 bad header checksum）：
  先 `git archive --format=tar -o x.tar <提交>` 再 `tar -xf x.tar -C <目录>`。
- `csc` 的报错是 GBK，重定向时注意乱码。
- 交付物：`outputs/` 里只留当前版本；`exe/` 是提交进 git 的免编译包，别往里塞旧版。
