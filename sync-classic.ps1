<#
  sync-classic.ps1 —— 把主线的改动同步到经典版（经典版序列）并编译。

  两条产品线（见根目录 CHANGELOG.md），各自一个版本序列：
    特别版 special = 主线 main，标签 special/v<版本>（special/v1.6、special/v1.7 …）
                    圣旨皮肤 + 小人，交付物叫 TimePlanner-<版本>-special
    经典版 classic = 标签 classic/v1.5（＝以前的 v1.5），版本 1.5、1.5.1 …
                    深色卡片界面，交付物叫 TimePlanner-<版本>-classic

  两条线的版本号各走各的，互不覆盖；运行也完全隔离（各自的单实例 / 信号灯 / 自启条目）。

  两者共用同一份数据（%APPDATA%\TimePlanner\data.json），换版本不丢数据（数据结构从 1.5 起没变过）。
  数据读写、信号灯、版本隔离、Win32 这些内核是两条线共用的，所以主线改了东西，
  经典版照着 CHANGELOG.md 同步一下就能跟上。

  这个脚本做的事：
    1. 需要时把经典版源码树（classic/v1.5）检出到 work\classic
    2. 把内核文件 + build.ps1 拷过去（这些文件在两条线里就是同一份）
    3. 把 CHANGELOG.md 里标 [内核] / [需要移植] 的改动都落到经典版源码里（幂等）；界面里整块
       搬的（项目页 / 示例数据）按内容指纹整段重铺，指纹注释跟着块走，别手改那几块
    4. 用 -Edition classic 编译出 work\classic\TimePlanner\dist
    5. 可选 -Release：铺 outputs\TimePlanner-<版本>-classic\ 并打 zip
    6. 可选 -InstallDir：把新 exe 覆盖到指定安装目录（旧的先备份成 *.bak-<时间戳>）

  例：
    powershell -NoProfile -ExecutionPolicy Bypass -File sync-classic.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File sync-classic.ps1 -Version 1.5.1 -Release
    powershell -NoProfile -ExecutionPolicy Bypass -File sync-classic.ps1 -Version 1.5.1 -Release -InstallDir "D:\TimePlanner-1.5-classic"

  编译报错，多半是有界面改动搬不过来（文字替换 / 区块位置对不上）：去 CHANGELOG.md 找对应条目手工搬一次，
  顺手把修法补进下面第 3.5 节，下次就能自动跟上了。
#>
param(
    [string]$ClassicTag = "classic/v1.5",
    [string]$Version = "",
    [switch]$Release,
    [string]$InstallDir = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$classic = Join-Path $root "work\classic"
$tp = Join-Path $classic "TimePlanner"
$dist = Join-Path $tp "dist"

function Step([string]$t) { Write-Host (""); Write-Host ("== " + $t) -ForegroundColor Cyan }
function Note([string]$t) { Write-Host ("   " + $t) }
function Fail([string]$m) { throw $m }

# 只关掉这份目录里正在跑的实例：csc 写 exe 时被占用会报 CS0016。
# 按路径认亲，不碰别的版本（用户可能正开着特别版）。
function StopOwn([string]$dir) {
    Get-Process TimePlanner, TimePlanner.Widget -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ($_.Path -and $_.Path.StartsWith($dir, [System.StringComparison]::OrdinalIgnoreCase)) {
                Note ("关掉正在运行的 " + $_.Path)
                $_.Kill()
            }
        } catch { }
    }
    Start-Sleep -Milliseconds 300
}

# ---------- 1. 经典版源码树 ----------
Step "经典版源码树"
$want = (& git -C $root rev-parse ($ClassicTag + "^{commit}")).Trim()
if (-not (Test-Path (Join-Path $tp "version.txt"))) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $classic) | Out-Null
    & git -C $root worktree add --detach $classic $ClassicTag
    if ($LASTEXITCODE -ne 0) { Fail ("检出 " + $ClassicTag + " 到 " + $classic + " 失败") }
}
$have = (& git -C $classic rev-parse HEAD).Trim()
if ($have -ne $want) {
    Fail ("work\classic 现在停在 " + $have.Substring(0, 8) + "，不是经典版 " + $ClassicTag + "（" + $want.Substring(0, 8) + "）。" + [Environment]::NewLine +
          "  确认可以重建的话：git worktree remove --force work/classic，再跑一次。")
}
Note ($classic + "  @ " + $have.Substring(0, 8))

# ---------- 2. 拷内核 ----------
Step "同步内核文件"
$coreFiles = @("Models.cs", "Store.cs", "Projects.cs", "Install.cs", "AppSignal.cs", "DesktopInterop.cs", "Diagnostics.cs")
foreach ($f in $coreFiles) {
    $src = Join-Path $root ("TimePlanner\src\Core\" + $f)
    if (-not (Test-Path $src)) { Fail ("主线里找不到 " + $f) }
    Copy-Item -LiteralPath $src -Destination (Join-Path $tp ("src\Core\" + $f)) -Force
}
Copy-Item -LiteralPath (Join-Path $root "TimePlanner\build.ps1") -Destination (Join-Path $tp "build.ps1") -Force
Note ($coreFiles.Count.ToString() + " 个内核文件 + build.ps1")

# 两条线共用的自绘控件：整份拷（新文件，不存在「经典版写法不同」的问题）
$uiFiles = @("Steps.cs")
foreach ($f in $uiFiles) {
    $src = Join-Path $root ("TimePlanner\src\Core\" + $f)
    if (-not (Test-Path $src)) { Fail ("主线里找不到 " + $f) }
    Copy-Item -LiteralPath $src -Destination (Join-Path $tp ("src\Core\" + $f)) -Force
}
Note ($uiFiles.Count.ToString() + " 个共用控件")

$utf8bom = New-Object System.Text.UTF8Encoding($true)
# 经典版源码树里的文件行尾不统一：有的整份 CRLF、有的整份 LF，还有的同一份里两种混着来
# （Controls.cs 就是这样）。所以锚点的两种行尾写法都试一遍，谁对得上算谁的。
function Norm([string]$s, [string]$nl, [string]$crlf, [string]$lf) {
    return $s.Replace($crlf, $lf).Replace($lf, $nl)
}
# -Soft：树上看不到老写法也不算错（用来「升级已经同步过的树」：新版和老版都在树上时，
# 老写法上次同步就被换掉了，所以先用「上一版的新写法」换一遍，再由下面按老写法兜底）。
function Swap([string]$rel, [string]$old, [string]$new, [string]$what, [string]$marker = "", [switch]$Soft) {
    $path = Join-Path $tp $rel
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $crlf = [string][char]13 + [string][char]10
    $lf = [string][char]10
    if ($marker -eq "") { $marker = $new }
    foreach ($nl in @($crlf, $lf)) {
        $o = Norm $old $nl $crlf $lf
        $n = Norm $new $nl $crlf $lf
        $m = Norm $marker $nl $crlf $lf
        if ($text.Contains($m)) { Note ($rel + "：已经是新写法（" + $what + "）"); return }
        if ($text.Contains($o)) {
            [System.IO.File]::WriteAllText($path, $text.Replace($o, $n), $utf8bom)
            Note ($rel + "：迁移 " + $what)
            return
        }
    }
    if ($Soft) { Note ($rel + "：" + $what + " —— 这棵树上找不到老写法，跳过"); return }
    Fail ($rel + " 里既没有老写法也没有新写法（" + $what + "），经典版源码动过了？")
}

# ---------- 整块搬迁的两个帮手 ----------
# 有些改动是「整块」落到经典版界面文件里的（项目页、示例数据这种），逐行文字替换不好使：
# 这块在经典版里从此就是独立的一份，主线再改就替换不到老写法了。所以给每块算一个内容指纹当路标：
# 指纹在文件里 = 这块已经是最新的；指纹对不上 = 整段重铺一遍（幂等，而且主线一改就自动跟上）。
function Marker([string]$what, [string]$new) {
    $sha = [System.Security.Cryptography.SHA1]::Create()
    $body = [System.Text.Encoding]::UTF8.GetBytes($what + [string][char]10 + $new.Replace([string][char]13, ""))
    $hex = [System.BitConverter]::ToString($sha.ComputeHash($body)).Replace("-", "")
    return ("        // [sync-classic] " + $what + "：" + $hex.Substring(0, 12) + "（整块由 sync-classic.ps1 从主线搬来，别在这一块里手改）")
}

# 把 $rel 里 $startMark 那一行（连它上面 $backUp 行）到 $endMark 那一行（不含）整段换成 $new。
# 目标里找不到 $startMark → 这块还没搬过，把 $new 插在 $endMark 前面。
function Region([string]$rel, [string]$startMark, [string]$endMark, [string]$new, [string]$what, [int]$backUp = 0) {
    $path = Join-Path $tp $rel
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $crlf = [string][char]13 + [string][char]10
    $lf = [string][char]10
    $tag = Marker $what $new
    if ($text.Contains($tag)) { Note ($rel + "：" + $what + " 已是最新"); return }
    $nl = $lf
    if ($text.Contains($crlf)) { $nl = $crlf }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($l in (Norm $text $lf $crlf $lf).Split([char]10)) { $lines.Add($l) }
    $ei = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].StartsWith($endMark)) { $ei = $i; break } }
    if ($ei -lt 0) { Fail ($rel + " 里找不到「" + $endMark + "」，没法定位该把 " + $what + " 放哪儿") }
    $si = -1
    for ($i = 0; $i -lt $ei; $i++) { if ($lines[$i].StartsWith($startMark)) { $si = $i; break } }
    $block = New-Object System.Collections.Generic.List[string]
    foreach ($l in (Norm $new $lf $crlf $lf).Split([char]10)) { $block.Add($l) }
    $block.Add($tag)
    if ($si -lt 0) {
        $lines.InsertRange($ei, $block)
        Note ($rel + "：补上 " + $what)
    } else {
        $from = $si - $backUp
        if ($from -lt 0) { $from = 0 }
        $lines.RemoveRange($from, $ei - $from)
        $lines.InsertRange($from, $block)
        Note ($rel + "：重铺 " + $what)
    }
    [System.IO.File]::WriteAllText($path, ($lines -join $nl), $utf8bom)
}

# ---------- 3. 入口迁移（幂等） ----------
Step "入口迁移：互斥体 / 找自己人 改用 Install"
$wpPath = Join-Path $tp "src\Widget\WidgetProgram.cs"
$wpText = [System.IO.File]::ReadAllText($wpPath, [System.Text.Encoding]::UTF8)
if (-not $wpText.Contains("static Mutex instanceMutex;")) {
    $anchor = "        [STAThread]"
    if (-not $wpText.Contains($anchor)) { Fail "WidgetProgram.cs 里找不到 [STAThread]，没法插入单实例字段" }
    $field = "        // 单实例的互斥体必须挂在静态字段上：只放局部变量的话，Release 下它一旦不再被引用" + [Environment]::NewLine +
            "        // 就会被 GC 收掉，句柄一关，这道闸门就悄悄失效了（点两下会冒出两个插件）。" + [Environment]::NewLine +
            "        static Mutex instanceMutex;" + [Environment]::NewLine + [Environment]::NewLine +
            "        [STAThread]"
    [System.IO.File]::WriteAllText($wpPath, $wpText.Replace($anchor, $field), $utf8bom)
    Note "WidgetProgram.cs：补上 static Mutex instanceMutex"
} else { Note "WidgetProgram.cs：单实例字段已就位" }

Swap 'src\App\Program.cs' '@"Local\TimePlanner.App.SingleInstance"' "Install.AppMutex" "主程序单实例"
Swap 'src\Widget\WidgetProgram.cs' 'Mutex single = new Mutex(true, @"Local\TimePlanner.Widget.SingleInstance", out created);' "instanceMutex = new Mutex(true, Install.WidgetMutex, out created);" "插件单实例"
Swap "src\App\Program.cs" "Process.GetProcessesByName(cur.ProcessName)" "Install.Siblings(cur.ProcessName)" "找自己那份主程序"
Swap 'src\App\Program.cs' 'Process.GetProcessesByName("TimePlanner.Widget")' 'Install.Siblings("TimePlanner.Widget")' "找自己那份插件"
Swap 'src\Widget\WidgetWindow.cs' 'Process.GetProcessesByName("TimePlanner")' 'Install.Siblings("TimePlanner")' "插件里找主程序"

# 出图（--render）也要只看临时目录 + 内置示例数据，别去读用户真实的 data.json。
$previewOld = "            store.ReadOnly = true;"
$previewNew = "            // 出图只用临时目录 + 内置示例数据，连读都不读用户那份 data.json" + [Environment]::NewLine +
              "            Store.DataDirOverride = Install.PreviewDataDir;" + [Environment]::NewLine +
              $previewOld
Swap "src\App\Program.cs" $previewOld $previewNew "出图不碰用户数据" "Store.DataDirOverride = Install.PreviewDataDir;"
Swap "src\Widget\WidgetProgram.cs" $previewOld $previewNew "出图不碰用户数据" "Store.DataDirOverride = Install.PreviewDataDir;"

# ---------- 3.5 界面迁移（CHANGELOG.md 里标 [需要移植] 的条目） ----------
# 这些改动落在界面文件里，两条线各写各的，所以在这儿按文字替换搬一次（幂等）。
Step "界面迁移：旧账不再隐形（今日页横幅 / 插件顶端提示 / 右键顺延）"
$nl = [Environment]::NewLine
function Blk([string[]]$a) { return ($a -join $nl) }

# 主程序今日页：把「压在过去的未竟之事」认出来
$mOverdueOld = Blk @(
    '            List<TaskItem> done = all.Where(t => t.Done).ToList();',
    '',
    '            pageTitle.Text = day == DateTime.Today ? "今天" : Fmt.Relative(day) + "的安排";')
$mOverdueNew = Blk @(
    '            List<TaskItem> done = all.Where(t => t.Done).ToList();',
    '            // 压在过去的未竟之事：它们只是静静留在旧日期里，这页空空的，看着特别像「数据没了」。',
    '            List<TaskItem> overdue = Store.Data.Tasks.Where(t => !t.Done && t.Date.Date < day).ToList();',
    '',
    '            pageTitle.Text = day == DateTime.Today ? "今天" : Fmt.Relative(day) + "的安排";')
Swap 'src\App\MainWindow.Pages.cs' $mOverdueOld $mOverdueNew "今日页：认出压在过去的未竟之事"

# 主程序今日页：顶部横幅 + 一键顺延
$mBannerOld = Blk @(
    '            StackPanel sp = new StackPanel();',
    '            sp.Margin = new Thickness(26, 0, 26, 26);',
    '',
    '            // 进度')
$mBannerNew = Blk @(
    '            StackPanel sp = new StackPanel();',
    '            sp.Margin = new Thickness(26, 0, 26, 26);',
    '',
    '            // 旧日期里还压着没办完的事：明说一句，并给个一键顺延，免得看着像数据没了。',
    '            if (day == DateTime.Today && overdue.Count > 0)',
    '            {',
    '                Border late = Ui.Round(12, Theme.B(Theme.Alpha(Theme.Accent, 0.10)), Theme.B(Theme.Alpha(Theme.Accent, 0.42)), 1);',
    '                late.Padding = new Thickness(15, 9, 12, 9);',
    '                late.Margin = new Thickness(0, 0, 0, 14);',
    '                StackPanel row = new StackPanel();',
    '                row.Orientation = Orientation.Horizontal;',
    '                TextBlock lateTxt = Ui.Txt(string.Format("此前尚余 {0} 事未竟，还压在过去的日期里", overdue.Count),',
    '                    13, Theme.B(Theme.TextMuted), false);',
    '                lateTxt.VerticalAlignment = VerticalAlignment.Center;',
    '                row.Children.Add(lateTxt);',
    '                Border roll = Ui.Chip("全部挪到今天", true, delegate()',
    '                {',
    '                    DayAnchor = DateTime.Today;',
    '                    Store.RollOver(DateTime.Today, DateTime.Today);',
    '                }, Theme.Accent);',
    '                roll.Margin = new Thickness(12, 0, 0, 0);',
    '                roll.VerticalAlignment = VerticalAlignment.Center;',
    '                row.Children.Add(roll);',
    '                late.Child = row;',
    '                sp.Children.Add(late);',
    '            }',
    '',
    '            // 进度')
Swap 'src\App\MainWindow.Pages.cs' $mBannerOld $mBannerNew "今日页：旧账横幅 + 「全部挪到今天」"

# 插件：认出压在过去的未竟之事
$wOverdueOld = Blk @(
    '            List<TaskItem> done = all.Where(t => t.Done).ToList();',
    '',
    '            dayTitle.Text = isToday ? "今天" : Fmt.Relative(d);')
$wOverdueNew = Blk @(
    '            List<TaskItem> done = all.Where(t => t.Done).ToList();',
    '            // 这一天之前还没办完的事：用来提示「不是数据没了，是都留在前面几天了」',
    '            List<TaskItem> overdue = store.Data.Tasks.Where(t => !t.Done && t.Date.Date < d).ToList();',
    '',
    '            dayTitle.Text = isToday ? "今天" : Fmt.Relative(d);')
Swap 'src\Widget\WidgetWindow.cs' $wOverdueOld $wOverdueNew "插件：认出压在过去的未竟之事"

# 插件：列表顶上的提示（不管这天有没有安排都显示）
$wChipOld = Blk @(
    '            listHost.Children.Clear();',
    '            if (open.Count == 0 && done.Count == 0)',
    '            {',
    '                StackPanel empty = new StackPanel();',
    '                empty.Margin = new Thickness(0, 6, 0, 6);',
    '                TextBlock e1 = Ui.Txt(isToday ? "今天还没有任务" : "这天没有任务", 12.5, Theme.B(Theme.TextFaint), false);')
$wChipNew = Blk @(
    '            listHost.Children.Clear();',
    '',
    '            // 压在过去的未竟之事：以前它们只是静静留在旧日期里，今天这一栏空空的，',
    '            // 看着特别像「数据丢了」。所以不管这天有没有安排，都在顶部说明一句，并给一键顺延。',
    '            if (overdue.Count > 0)',
    '            {',
    '                Border roll = Ui.Chip(string.Format("此前尚余 {0} 事未竟 · 挪到{1}", overdue.Count, isToday ? "今日" : "此日"),',
    '                    false, delegate() { RollOverToDay(); }, Theme.Accent);',
    '                roll.HorizontalAlignment = HorizontalAlignment.Center;',
    '                roll.Margin = new Thickness(0, 0, 0, 7);',
    '                Ui.Tip(roll, string.Format("把 {0} 件压在过去的未竟之事一并顺延到这一天", overdue.Count));',
    '                listHost.Children.Add(roll);',
    '            }',
    '',
    '            // 分了份的大项目 / 分段也摆在插件上：只写「几分之几」，点一下那枚勾就办完 1 份。',
    '            List<ProjectNode> parts = PartNodes();',
    '            for (int i = 0; i < parts.Count; i++) listHost.Children.Add(MakePartRow(parts[i]));',
    '',
    '            if (open.Count == 0 && done.Count == 0 && parts.Count == 0)',
    '            {',
    '                StackPanel empty = new StackPanel();',
    '                empty.Margin = new Thickness(0, 6, 0, 6);',
    '                TextBlock e1 = Ui.Txt(!isToday ? "这天没有任务" : (overdue.Count > 0 ? "今天还没有安排" : "今天还没有任务"), 12.5, Theme.B(Theme.TextFaint), false);')
Swap 'src\Widget\WidgetWindow.cs' $wChipOld $wChipNew "插件：顶端旧账提示"

# 插件右键菜单：两个入口
$wMenuOld = Blk @(
    '            miToday = MenuSkin.Add(menu, "calendar", "只显示今天", delegate() { day = DateTime.Today; Refresh(); }, false, false);',
    '            MenuSkin.Add(menu, "expand", "恢复自适应大小", delegate()')
$wMenuNew = Blk @(
    '            miToday = MenuSkin.Add(menu, "calendar", "只显示今天", delegate() { day = DateTime.Today; Refresh(); }, false, false);',
    '            MenuSkin.Add(menu, "arrow-right", "今日事明日毕", delegate() { DeferAll(); }, false, false);',
    '            MenuSkin.Add(menu, "clock", "过期未竟挪到今天", delegate() { RollOverToToday(); }, false, false);',
    '            MenuSkin.Add(menu, "expand", "恢复自适应大小", delegate()')
Swap 'src\Widget\WidgetWindow.cs' $wMenuOld $wMenuNew "插件右键：今日事明日毕 / 过期未竟挪到今天"

# 插件：菜单项用到的那几个方法
$wMethOld = Blk @(
    '            if (miToday != null) miToday.SetActive(day.Date == DateTime.Today);',
    '        }',
    '',
    '        Border BuildHeader()')
$wMethNew = Blk @(
    '            if (miToday != null) miToday.SetActive(day.Date == DateTime.Today);',
    '        }',
    '',
    '        /// <summary>今日事明日毕：把这一天的未竟之事整体挪到第二天，顺便跟过去看看落哪儿了。</summary>',
    '        void DeferAll()',
    '        {',
    '            DateTime from = day.Date;',
    '            day = from.AddDays(1);',
    '            store.Defer(from, day);',
    '            AnimatedRefresh();',
    '        }',
    '',
    '        /// <summary>把今天之前没办完的事一并顺延到今天，并跳过去看。</summary>',
    '        void RollOverToToday()',
    '        {',
    '            DateTime today = DateTime.Today;',
    '            store.RollOver(today, today);',
    '            day = today;',
    '            AnimatedRefresh();',
    '        }',
    '',
    '        /// <summary>把正在看的这一天之前没办完的事一并顺延到这一天。</summary>',
    '        void RollOverToDay()',
    '        {',
    '            DateTime target = day.Date;',
    '            store.RollOver(target, target);',
    '            AnimatedRefresh();',
    '        }',
    '',
    '        Border BuildHeader()')
Swap 'src\Widget\WidgetWindow.cs' $wMethOld $wMethNew "插件：顺延用的三个方法"


# ---------- 3.5b 界面迁移：项目档案（大项目 / 分段 / 小项目） ----------
Step "界面迁移：项目档案（大项目 / 分段 / 小项目）"

# 任务行：小项目也是普通事项，行里补一枚「归属」小标签
$cHookOld = Blk @(
    '        public Action<TaskRow, TaskItem> DragRequested;',
    '        Point dragStart;')
$cHookNew = Blk @(
    '        public Action<TaskRow, TaskItem> DragRequested;',
    '',
    '        /// <summary>',
    '        /// 小项目在行上补一句归属（「毕业设计 / 开题」）。主程序和桌面插件各自挂上自己的数据源；',
    '        /// 没挂或者这条不是项目的事项，就什么都不显示。',
    '        /// </summary>',
    '        public static Func<TaskItem, string> ProjectLabel;',
    '',
    '        Point dragStart;')
Swap 'src\Core\Controls.cs' $cHookOld $cHookNew "任务行：项目归属钩子"

$cPillFieldOld = Blk @(
'        readonly Border tagPill;'
'        readonly Border deferButton;')
$cPillFieldNew = Blk @(
'        readonly Border tagPill;'
'        readonly Border projPill;'
'        readonly Border deferButton;')
Swap 'src\Core\Controls.cs' $cPillFieldOld $cPillFieldNew "任务行：归属小标签字段"

$cPillMakeOld = Blk @(
'            tagPill = Ui.Pill("", Theme.Accent, Theme.AccentSoft);'
'            tagPill.Margin = new Thickness(0, 0, 6, 0);'
'            meta.Children.Add(tagPill);'
'            content.Children.Add(meta);')
$cPillMakeNew = Blk @(
'            tagPill = Ui.Pill("", Theme.Accent, Theme.AccentSoft);'
'            tagPill.Margin = new Thickness(0, 0, 6, 0);'
'            meta.Children.Add(tagPill);'
'            projPill = Ui.Pill("", Theme.TextMuted, Theme.Alpha(Theme.TextMuted, 0.13));'
'            projPill.Margin = new Thickness(0, 0, 6, 0);'
'            meta.Children.Add(projPill);'
'            content.Children.Add(meta);')
Swap 'src\Core\Controls.cs' $cPillMakeOld $cPillMakeNew "任务行：归属小标签"

$cPillPaintOld = Blk @(
'            if (Item.Tag != null && Item.Tag.Length > 0)'
'            {'
'                tagPill.Visibility = Visibility.Visible;'
'                ((TextBlock)tagPill.Child).Text = Item.Tag;'
'            }'
'            else'
'            {'
'                tagPill.Visibility = Visibility.Collapsed;'
'            }'
'        }')
$cPillPaintNew = Blk @(
'            if (Item.Tag != null && Item.Tag.Length > 0)'
'            {'
'                tagPill.Visibility = Visibility.Visible;'
'                ((TextBlock)tagPill.Child).Text = Item.Tag;'
'            }'
'            else'
'            {'
'                tagPill.Visibility = Visibility.Collapsed;'
'            }'
''
'            string proj = ProjectLabel == null ? "" : ProjectLabel(Item);'
'            if (proj != null && proj.Length > 0)'
'            {'
'                projPill.Visibility = Visibility.Visible;'
'                ((TextBlock)projPill.Child).Text = proj;'
'            }'
'            else'
'            {'
'                projPill.Visibility = Visibility.Collapsed;'
'            }'
'        }')
Swap 'src\Core\Controls.cs' $cPillPaintOld $cPillPaintNew "任务行：归属小标签刷新"

$mHookOld = '            Icon = AppIcon.WpfIcon();'
$mHookNew = Blk @(
'            Icon = AppIcon.WpfIcon();'
'            // 小项目也是普通事项，行上补一句它属于哪个项目'
'            TaskRow.ProjectLabel = delegate(TaskItem t) { return ProjectLabelOf(t); };')
Swap 'src\App\MainWindow.cs' $mHookOld $mHookNew "主程序：项目归属钩子"

$mFieldsNew = Blk @(
'        string projectAddParent;                    // 项目页：正在给哪个节点加下級（null = 没在加）'
'        int projectAddKind = ProjectKind.Sub;       // 加的是小项目还是分段'
'        string projectRenameId;                     // 项目页：正在改名哪个节点（null = 没在改）')
Region 'src\App\MainWindow.cs' '        string projectAddParent;' '        string paintedPage' $mFieldsNew "主程序：项目页的状态字段"

$mNavOld = '            navList.Children.Add(NavItem("week", "calendar", "本周"));'
$mNavNew = Blk @(
'            navList.Children.Add(NavItem("week", "calendar", "本周"));'
'            navList.Children.Add(NavItem("project", "layers", "项目档案"));')
Swap 'src\App\MainWindow.cs' $mNavOld $mNavNew "主程序：导航里加「项目档案」"

$mDispatchOld = Blk @(
'            if (Page == "week") { WeekAnchor = TaskQuery.WeekStart(WeekAnchor, Store.Settings.WeekStartMonday); body = BuildWeekPage(); }'
'            else if (Page == "done") body = BuildDonePage();')
$mDispatchNew = Blk @(
'            if (Page == "week") { WeekAnchor = TaskQuery.WeekStart(WeekAnchor, Store.Settings.WeekStartMonday); body = BuildWeekPage(); }'
'            else if (Page == "project") body = BuildProjectPage();'
'            else if (Page == "done") body = BuildDonePage();')
Swap 'src\App\MainWindow.cs' $mDispatchOld $mDispatchNew "主程序：切到项目页"

$mSignOld = Blk @(
'                  .Append(t.Sort).Append('':'').Append(t.Date.Date.Ticks).Append('':'').Append(t.Title).Append('':'').Append(t.Tag).Append('':'').Append(t.Note).Append('';'');'
'            }'
'            return sb.ToString();')
$mSignNew = Blk @(
'                  .Append(t.Sort).Append('':'').Append(t.Date.Date.Ticks).Append('':'').Append(t.Title).Append('':'').Append(t.Tag).Append('':'').Append(t.Note)'
'                  .Append('':'').Append(t.ProjectId).Append('';'');'
'            }'
'            // 项目树也进签名：展开 / 收起、改名、挪次序、改了份数之后这一页都要重排'
'            List<ProjectNode> projs = Store.Data.Projects;'
'            for (int i = 0; i < projs.Count; i++)'
'            {'
'                ProjectNode n = projs[i];'
'                sb.Append(n.Id).Append('':'').Append(n.ParentId).Append('':'').Append(n.Kind).Append('':'').Append(n.Sort)'
'                  .Append('':'').Append(n.IsOpen ? ''1'' : ''0'').Append('':'').Append(n.Title).Append('':'').Append(n.ItemId)'
'                  .Append('':'').Append(n.Steps).Append('':'').Append(n.Reached).Append('';'');'
'            }'
'            return sb.ToString();'
)
$mSignPrev = Blk @(
'                  .Append(t.Sort).Append('':'').Append(t.Date.Date.Ticks).Append('':'').Append(t.Title).Append('':'').Append(t.Tag).Append('':'').Append(t.Note)'
'                  .Append('':'').Append(t.ProjectId).Append('';'');'
'            }'
'            // 项目树也进签名：展开 / 收起、改名、挪次序之后这一页要重排'
'            List<ProjectNode> projs = Store.Data.Projects;'
'            for (int i = 0; i < projs.Count; i++)'
'            {'
'                ProjectNode n = projs[i];'
'                sb.Append(n.Id).Append('':'').Append(n.ParentId).Append('':'').Append(n.Kind).Append('':'').Append(n.Sort)'
'                  .Append('':'').Append(n.IsOpen ? ''1'' : ''0'').Append('':'').Append(n.Title).Append('':'').Append(n.ItemId).Append('';'');'
'            }'
'            return sb.ToString();')

# 签名里后来又加了「份数」两列：已经同步过的树装的是上一版写法，先把它按上一版换成新版
Swap 'src\App\MainWindow.cs' $mSignPrev $mSignNew "主程序：签名里补上份数与已办" -Soft
Swap 'src\App\MainWindow.cs' $mSignOld $mSignNew "主程序：签名里加上项目树"

$mBadgeOld = '                else badge.Text = "";'
$mBadgeNew = Blk @(
'                else if (key == "project")'
'                {'
'                    int openProj = ProjectTree.OpenCount(Store.Data);'
'                    badge.Text = openProj == 0 ? "" : openProj.ToString();'
'                }'
'                else badge.Text = "";')
Swap 'src\App\MainWindow.cs' $mBadgeOld $mBadgeNew "主程序：项目档案的角标"

# 写盘失败要看得见：签名里带上写盘错误，侧栏才能自己亮红字、修好之后自己消失
$mSignErrOld = "              .Append(st.AutoStart).Append('|').Append(st.PauseOnFullscreenEnabled).Append('|');"
$mSignErrNew = Blk @(
"              .Append(st.AutoStart).Append('|').Append(st.PauseOnFullscreenEnabled).Append('|')"
"              .Append(Store.WriteError).Append('|');")
Swap 'src\App\MainWindow.cs' $mSignErrOld $mSignErrNew "主程序：签名里带上存盘错误"

$mWarnOld = '            sideFoot.Children.Add(widget);'
$mWarnNew = Blk @(
'            sideFoot.Children.Add(widget);'
''
'            // 存盘失败必须让用户看见：否则改动只在内存里，一重启就没了。'
'            string err = Store.WriteError;'
'            if (err != null)'
'            {'
'                Border warn = Ui.Round(12, Theme.B(Theme.Panel), Theme.B(Theme.Danger), 1);'
'                warn.Padding = new Thickness(13, 10, 13, 10);'
'                warn.Margin = new Thickness(0, 8, 0, 0);'
'                TextBlock wt = Ui.Txt("⚠ 存盘失败，改动还在内存里（正在重试）\n" + err, 11, Theme.B(Theme.Danger), false);'
'                wt.TextWrapping = TextWrapping.Wrap;'
'                warn.Child = wt;'
'                sideFoot.Children.Add(warn);'
'            }')
Swap 'src\App\MainWindow.cs' $mWarnOld $mWarnNew "主程序：存盘失败的侧栏红字"


$wHookOld = '            showDone = store.Settings.WidgetShowDone;'
$wHookNew = Blk @(
'            showDone = store.Settings.WidgetShowDone;'
'            // 小项目也是普通事项，行上补一句它属于哪个项目'
'            TaskRow.ProjectLabel = delegate(TaskItem t) { return ProjectLabelOf(t); };')
Swap 'src\Widget\WidgetWindow.cs' $wHookOld $wHookNew "插件：项目归属钩子"

$wSignOld = Blk @(
'                  .Append(t.Sort).Append('':'').Append(t.Date.Date.Ticks).Append('':'').Append(t.Title).Append('':'').Append(t.Tag).Append('';'');')
$wSignNew = Blk @(
'                  .Append(t.Sort).Append('':'').Append(t.Date.Date.Ticks).Append('':'').Append(t.Title).Append('':'').Append(t.Tag)'
'                  .Append('':'').Append(t.ProjectId).Append('';'');')
Swap 'src\Widget\WidgetWindow.cs' $wSignOld $wSignNew "插件：签名里加上项目归属"

$wLabelOld = '        /// <summary>把当前要显示的内容压成签名，内容没变就不重建界面。</summary>'
$wLabelNew = Blk @(
'        /// <summary>行上那句归属：这条小项目属于「大项目 / 分段」。</summary>'
'        string ProjectLabelOf(TaskItem t)'
'        {'
'            if (t == null || t.ProjectId == null || t.ProjectId.Length == 0) return "";'
'            ProjectNode n = ProjectTree.ById(store.Data, t.ProjectId);'
'            if (n == null) return "";'
'            string path = ProjectTree.Path(store.Data, n.ParentId, true);'
'            if (path.Length == 0) return "";'
'            return path.Length > 14 ? path.Substring(0, 13) + "…" : path;'
'        }'
''
'        /// <summary>把当前要显示的内容压成签名，内容没变就不重建界面。</summary>')
Swap 'src\Widget\WidgetWindow.cs' $wLabelOld $wLabelNew "插件：项目归属取值"


# 主程序项目页：整块从主线搬 —— 经典版源码树（classic/v1.5）里根本没有这一段，
# 第一次是插在「共用」段前面；以后这段在主线改了，Region 的指纹对不上就整块重铺。
$projNew = @'
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

'@
Region 'src\App\MainWindow.Pages.cs' '        // ---------------- 项目档案 ----------------' '        // ---------------- 共用 ----------------' $projNew "项目档案页"


# 主程序：出图 / 演示用的示例数据里也放两个项目，两条线的截图看着一致
$pDemoOld = Blk @(
'        static void LoadDemo(Store store)'
'        {'
'            store.Data.Tasks.Clear();'
'            DateTime today = DateTime.Today;')
$pDemoNew = Blk @(
'        static void LoadDemo(Store store)'
'        {'
'            store.Data.Tasks.Clear();'
'            if (store.Data.Projects == null) store.Data.Projects = new System.Collections.Generic.List<ProjectNode>();'
'            store.Data.Projects.Clear();'
'            DateTime today = DateTime.Today;')
Swap 'src\App\Program.cs' $pDemoOld $pDemoNew "示例数据：先清掉旧项目"

$pDemoCallOld = Blk @(
'            AddDemo(store, "复盘本月目标完成情况", mon.AddDays(5), 1, "复盘", false);'
'        }')
$pDemoCallNew = Blk @(
'            AddDemo(store, "复盘本月目标完成情况", mon.AddDays(5), 1, "复盘", false);'
'            LoadDemoProjects(store);'
'        }')
Swap 'src\App\Program.cs' $pDemoCallOld $pDemoCallNew "示例数据：LoadDemo 里叫上项目"

# 示例项目这块也整块从主线搬（方法连注释一起），Region 按指纹判断要不要重铺
$pProjDemoNew = @'
        /// <summary>示例项目：一个大项目带分段，另一个直接挂小项目（小项目在数据里就是普通事项，截图里它和普通任务混在一列）。</summary>
        static void LoadDemoProjects(Store store)
        {
            ProjectNode thesis = store.AddProject("", ProjectKind.Big, "毕业设计");
            ProjectNode stage = store.AddProject(thesis.Id, ProjectKind.Stage, "开题阶段");
            store.AddProject(stage.Id, ProjectKind.Sub, "查 20 篇相关文献 #论文 !");
            ProjectNode draft = store.AddProject(stage.Id, ProjectKind.Sub, "写完开题报告初稿 #论文 !!");
            // 大项目不写细目也能记进度：整个项目分成 8 份、已办 3 份（拖横条记的）
            store.SetProjectSteps(thesis.Id, 8);
            store.SetProjectReached(thesis.Id, 3);
            store.AddProject(thesis.Id, ProjectKind.Sub, "和导师约一次面谈 #论文");

            ProjectNode body = store.AddProject("", ProjectKind.Big, "体重管理");
            ProjectNode aerobic = store.AddProject(body.Id, ProjectKind.Sub, "每周三次有氧 #生活");
            store.AddProject(body.Id, ProjectKind.Sub, "把晚餐的碳水减半 #生活");

            // 让一条小项目是「已竟」，进度上看得出来
            TaskItem t = ProjectTree.ItemOf(store.Data, draft);
            if (t != null) { t.Done = true; t.DoneAt = DateTime.Now.AddHours(-2); }
            // 另一条挪到本周后半，看它和普通任务排在一起的样子
            TaskItem t2 = ProjectTree.ItemOf(store.Data, aerobic);
            if (t2 != null) t2.Date = TaskQuery.WeekStart(DateTime.Today, true).AddDays(3);
        }

'@
Region 'src\App\Program.cs' '        static void LoadDemoProjects(Store store)' '        static void AddDemo(Store store, string title, DateTime day, int prio, string tag, bool done)' $pProjDemoNew "示例数据：加上项目" 1

$pPagesOld = '            string[] pages = new string[] { "today", "week", "done", "settings" };'
$pPagesNew = '            string[] pages = new string[] { "today", "week", "project", "done", "settings" };'
Swap 'src\App\Program.cs' $pPagesOld $pPagesNew "出图：多渲染一页项目档案"


# 插件：示例数据里也放两条项目事项，插件截图和主程序对得上
$wDemoOld = Blk @(
'        static void Demo(Store store)'
'        {'
'            store.Data.Tasks.Clear();'
'            DateTime today = DateTime.Today;')
$wDemoNew = Blk @(
'        static void Demo(Store store)'
'        {'
'            store.Data.Tasks.Clear();'
'            if (store.Data.Projects == null) store.Data.Projects = new System.Collections.Generic.List<ProjectNode>();'
'            store.Data.Projects.Clear();'
'            DateTime today = DateTime.Today;')
Swap 'src\Widget\WidgetProgram.cs' $wDemoOld $wDemoNew "插件示例数据：先清掉旧项目"

$wProjDemoOld = Blk @(
'                if (i >= 3) { t.Done = true; t.DoneAt = DateTime.Now.AddHours(-2); }'
'                store.Data.Tasks.Add(t);'
'            }'
'        }')
$wProjDemoNew = Blk @(
'                if (i >= 3) { t.Done = true; t.DoneAt = DateTime.Now.AddHours(-2); }'
'                store.Data.Tasks.Add(t);'
'            }'
'            // 项目里的小项目也是普通事项，插件这一列里它和任务混在一起，只多一枚「归属」小标签'
'            ProjectNode big = store.AddProject("", ProjectKind.Big, "毕业设计");'
'            big.Steps = 6;                       // 分了份的项目在插件上另起一行，只写「几分之几」'
'            big.Reached = 2;'
'            ProjectNode stage = store.AddProject(big.Id, ProjectKind.Stage, "开题阶段");'
'            stage.Steps = 3;'
'            stage.Reached = 1;'
'            store.AddProject(stage.Id, ProjectKind.Sub, "查 20 篇相关文献 #论文 !");'
'            store.AddProject(big.Id, ProjectKind.Sub, "和导师约一次面谈 #论文");'
'        }')
Swap 'src\Widget\WidgetProgram.cs' $wProjDemoOld $wProjDemoNew "插件示例数据：加上项目事项"

# 插件：分了份的大项目 / 分段也摆在插件上，只写「几分之几」，点一下那枚勾办完 1 份
$wPartSigOld = Blk @(
'                  .Append('':'').Append(t.ProjectId).Append('';'');'
'            }'
'            return sb.ToString();'
)
$wPartSigNew = Blk @(
'                  .Append('':'').Append(t.ProjectId).Append('';'');'
'            }'
'            // 分了份的项目也进签名：在插件上点掉 1 份之后，份数那一行得跟着重画'
'            List<ProjectNode> projs = ProjectTree.All(store.Data);'
'            for (int i = 0; i < projs.Count; i++)'
'            {'
'                ProjectNode pn = projs[i];'
'                if (pn == null) continue;'
'                sb.Append(pn.Id).Append('':'').Append(pn.Kind).Append('':'').Append(pn.Steps).Append('':'')'
'                  .Append(pn.Reached).Append('':'').Append(pn.Title).Append('';'');'
'            }'
'            return sb.ToString();'
)
Swap 'src\Widget\WidgetWindow.cs' $wPartSigOld $wPartSigNew "插件：签名里加上份数与已办"


$wPartMkOld = Blk @(
'        TaskRow MakeRow(TaskItem t)'
'        {'
)
$wPartMkNew = Blk @(
'        /// <summary>'
'        /// 插件上要露脸的项目行：分了份的大项目 / 分段（份数为 0 的按小项目算，不在这儿出现），'
'        /// 按树上的次序排，下級缩进 —— 跟主程序项目页一个读法。办满份数的跟任务一样，交给「已竟之事」那个开关管。'
'        /// </summary>'
'        List<ProjectNode> PartNodes()'
'        {'
'            List<ProjectNode> list = new List<ProjectNode>();'
'            AddPartNodes("", 0, list);'
'            return list;'
'        }'
''
'        void AddPartNodes(string parentId, int depth, List<ProjectNode> into)'
'        {'
'            if (depth > 8) return;'
'            List<ProjectNode> kids = ProjectTree.Children(store.Data, parentId);'
'            for (int i = 0; i < kids.Count; i++)'
'            {'
'                ProjectNode n = kids[i];'
'                // 办满份数的跟任务一样：整个节点真办完了（份满 + 底下小项目也勾了）才收起来'
'                if (n.HasSteps && (showDone || !ProjectTree.IsDone(store.Data, n.Id))) into.Add(n);'
'                AddPartNodes(n.Id, depth + 1, into);'
'            }'
'        }'
''
'        int DepthOf(ProjectNode n)'
'        {'
'            int depth = 0;'
'            string pid = n.ParentId;'
'            while (pid != null && pid.Length > 0 && depth < 16)'
'            {'
'                ProjectNode up = ProjectTree.ById(store.Data, pid);'
'                if (up == null) break;'
'                depth++;'
'                pid = up.ParentId;'
'            }'
'            return depth;'
'        }'
''
'        /// <summary>'
'        /// 分了份的项目 / 分段在插件上的那一行：只写「几分之几」，不画横条 —— 插件就这么点地方。'
'        /// 那枚勾跟任务行是同一个控件：点一下只办完 1 份（办满最后一份时才放礼花），'
'        /// 办满之后再点一下 = 撤销重来，跟任务勾选一个脾气。'
'        /// </summary>'
'        Border MakePartRow(ProjectNode node)'
'        {'
'            string id = node.Id;'
'            int steps = node.Steps;'
'            bool big = node.Kind == ProjectKind.Big;'
'            bool filled = node.Reached >= steps;'
''
'            Grid g = new Grid();'
'            g.ColumnDefinitions.Add(new ColumnDefinition());'
'            ColumnDefinition cName = new ColumnDefinition();'
'            cName.Width = new GridLength(1, GridUnitType.Star);'
'            g.ColumnDefinitions.Add(cName);'
'            g.ColumnDefinitions.Add(new ColumnDefinition());'
''
'            CircleCheck check = new CircleCheck(15);'
'            check.VerticalAlignment = VerticalAlignment.Center;'
'            check.Margin = new Thickness(4, 0, 4, 0);'
'            check.SetDone(filled, false);'
'            Ui.Tip(check, string.Format("{0}：办完 1 份（现在 {1}/{2}）；办满之后再点 = 撤销重来",'
'                ProjectTree.TitleOf(store.Data, node), node.Reached, steps));'
'            CircleCheck checkRef = check;'
'            check.Toggled = delegate()'
'            {'
'                ProjectNode cur = ProjectTree.ById(store.Data, id);'
'                if (cur == null || cur.Steps <= 0) return;'
'                bool full = cur.Reached >= cur.Steps;'
'                int next = full ? 0 : cur.Reached + 1;'
'                store.SetProjectReached(id, next);'
'                if (!full && next >= cur.Steps) CelebrateNode(checkRef);'
'                Refresh();'
'            };'
'            Grid.SetColumn(check, 0);'
'            g.Children.Add(check);'
''
'            StackPanel title = new StackPanel();'
'            title.Orientation = Orientation.Horizontal;'
'            title.VerticalAlignment = VerticalAlignment.Center;'
'            Path icon = Ui.IconPath(big ? "layers" : "list", 12,'
'                Theme.B(filled ? Theme.Success : Theme.Accent), 1.4);'
'            icon.VerticalAlignment = VerticalAlignment.Center;'
'            icon.Margin = new Thickness(0, 0, 6, 0);'
'            title.Children.Add(icon);'
'            TextBlock name = Ui.Txt(ProjectTree.TitleOf(store.Data, node), 12.5,'
'                Theme.B(filled ? Theme.TextFaint : Theme.Text), big);'
'            name.VerticalAlignment = VerticalAlignment.Center;'
'            name.TextTrimming = TextTrimming.CharacterEllipsis;'
'            if (filled) name.TextDecorations = TextDecorations.Strikethrough;'
'            title.Children.Add(name);'
'            Grid.SetColumn(title, 1);'
'            g.Children.Add(title);'
''
'            TextBlock frac = Ui.Txt(string.Format("{0}/{1}", node.Reached, steps), 12.5,'
'                Theme.B(filled ? Theme.Success : Theme.TextMuted), true);'
'            frac.VerticalAlignment = VerticalAlignment.Center;'
'            frac.Margin = new Thickness(6, 0, 2, 0);'
'            Grid.SetColumn(frac, 2);'
'            g.Children.Add(frac);'
''
'            Border row = Ui.Round(9, Theme.B(Theme.Panel));'
'            row.Padding = new Thickness(6, 6, 8, 6);'
'            row.Margin = new Thickness(DepthOf(node) * 12, 0, 0, 6);      // 下級往里缩，一眼看出隶属'
'            row.Child = g;'
'            return row;'
'        }'
''
'        /// <summary>项目 / 分段在插件上办完一份（或办满）时也放一筒礼花 —— 跟任务勾选同一批鼓励语。</summary>'
'        void CelebrateNode(FrameworkElement anchor)'
'        {'
'            if (fx == null || anchor == null) return;'
'            Point o = Ui.CenterOf(anchor, fx);'
'            double aim = o.X > fx.ActualWidth * 0.55 ? -146 : -34;'
'            Fireworks.Popper(fx, o, 1.25, aim, null);'
'            Say(Fireworks.PickCheer());'
'        }'
''
'        TaskRow MakeRow(TaskItem t)'
'        {'
)
# 路标用块里那句 PartNodes()：后面还有两条小 swap 会改这块里的字（注释里的开关名、经典版的贺辞走法），
# 用块内不变的这行当指纹，改过之后照样认得出「这块已经搬过了」。
Swap 'src\Widget\WidgetWindow.cs' $wPartMkOld $wPartMkNew "插件：项目行的画法与 1/n" '        List<ProjectNode> PartNodes()'

# 经典版插件没有小人：贺辞交给礼花筒那颗彩纸上的那句话（跟经典版任务行的 Celebrate 一个走法）
$wPartCheerOld = Blk @(
'        /// <summary>项目 / 分段在插件上办完一份（或办满）时也放一筒礼花 —— 跟任务勾选同一批鼓励语。</summary>'
'        void CelebrateNode(FrameworkElement anchor)'
'        {'
'            if (fx == null || anchor == null) return;'
'            Point o = Ui.CenterOf(anchor, fx);'
'            double aim = o.X > fx.ActualWidth * 0.55 ? -146 : -34;'
'            Fireworks.Popper(fx, o, 1.25, aim, null);'
'            Say(Fireworks.PickCheer());'
'        }'
)
$wPartCheerNew = Blk @(
'        /// <summary>项目 / 分段在插件上办完一份（或办满）时也放一筒礼花 —— 跟任务勾选同一批鼓励语。</summary>'
'        void CelebrateNode(FrameworkElement anchor)'
'        {'
'            if (fx == null || anchor == null) return;'
'            Point o = Ui.CenterOf(anchor, fx);'
'            double aim = o.X > fx.ActualWidth * 0.55 ? -146 : -34;'
'            Fireworks.Popper(fx, o, 1.25, aim, Fireworks.PickCheer());'
'        }'
)
Swap 'src\Widget\WidgetWindow.cs' $wPartCheerOld $wPartCheerNew "插件：办完 1 份的礼花（经典版不走小人）"

# 那句注释按经典版的说法：经典版顶上那个开关叫「已完成」
$wPartDocOld = '        /// 按树上的次序排，下級缩进 —— 跟主程序项目页一个读法。办满份数的跟任务一样，交给「已竟之事」那个开关管。'
$wPartDocNew = '        /// 按树上的次序排，下級缩进 —— 跟主程序项目页一个读法。办满份数的跟任务一样，交给「已完成」那个开关管。'
Swap 'src\Widget\WidgetWindow.cs' $wPartDocOld $wPartDocNew "插件：项目行注释按经典版的说法"


# ---------- 4. 版本号 ----------
if ($Version -ne "") {
    Step ("版本号 -> " + $Version)
    [System.IO.File]::WriteAllText((Join-Path $tp "version.txt"), $Version, (New-Object System.Text.UTF8Encoding($false)))
}
if ($Release -and $Version -eq "") { Fail "-Release 要同时给 -Version（例如 -Version 1.5.1）" }

# ---------- 5. 编译 ----------
if (-not $SkipBuild) {
    Step "编译（-Edition classic）"
    StopOwn $tp
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $tp "build.ps1") -Edition classic
    if ($LASTEXITCODE -ne 0) { Fail "经典版编译失败（多半是 CHANGELOG.md 里 [需要移植] 的条目还没搬）" }
}

# ---------- 6. 铺 outputs + 打包 ----------
if ($Release) {
    Step "铺 outputs 并打包"
    $name = "TimePlanner-" + $Version + "-classic"
    $out = Join-Path $root ("outputs\" + $name)
    if (-not (Test-Path $out)) { New-Item -ItemType Directory -Force -Path $out | Out-Null }
    $pack = @()
    foreach ($f in @("TimePlanner.exe", "TimePlanner.Widget.exe")) {
        $s = Join-Path $dist $f
        if (-not (Test-Path $s)) { Fail ("dist 里没有 " + $f) }
        Copy-Item -LiteralPath $s -Destination (Join-Path $out $f) -Force
        $pack += $f
    }
    Copy-Item -LiteralPath (Join-Path $tp "README.md") -Destination (Join-Path $out "README.md") -Force
    Copy-Item -LiteralPath (Join-Path $root "CHANGELOG.md") -Destination (Join-Path $out "CHANGELOG.md") -Force
    $pack += "README.md"; $pack += "CHANGELOG.md"
    $zip = Join-Path $root ("outputs\" + $name + ".zip")
    if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
    Push-Location $out
    & tar.exe -a -c -f $zip $pack
    Pop-Location
    Note ("交付目录：" + $out)
    Note ("压缩包：" + $zip)
}

# ---------- 7. 覆盖到安装目录 ----------
if ($InstallDir -ne "") {
    Step "更新安装目录"
    if (-not (Test-Path $InstallDir)) { Fail ("安装目录不存在：" + $InstallDir) }
    StopOwn $InstallDir
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    foreach ($f in @("TimePlanner.exe", "TimePlanner.Widget.exe")) {
        $src = Join-Path $dist $f
        if (-not (Test-Path $src)) { Fail ("dist 里没有 " + $f + "，先编译") }
        $dst = Join-Path $InstallDir $f
        if (Test-Path $dst) { Copy-Item -LiteralPath $dst -Destination ($dst + ".bak-" + $stamp) -Force }
        Copy-Item -LiteralPath $src -Destination $dst -Force
        Note ("覆盖 " + $dst)
    }
    Copy-Item -LiteralPath (Join-Path $root "CHANGELOG.md") -Destination (Join-Path $InstallDir "CHANGELOG.md") -Force
    Note ("旧的 exe 备份成了 *.bak-" + $stamp + "（想退回去就改名换回来）")
}

Step "完成"
Note ("经典版产物：" + $dist)
Note "CHANGELOG.md 里的 [内核] 和 [需要移植] 条目都已经落进这份源码了（脚本幂等，重复跑没事）。"

# 收尾给个明确的返回码（免得最后一条编译命令留下的 $LASTEXITCODE 把 rc 搞乱）
exit 0