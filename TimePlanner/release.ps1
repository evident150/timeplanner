<#
  时间规划 · 一键发版（release.ps1）
  ------------------------------------------------------------------
  1. 版本号只认 version.txt（用 -Version sp2 可以直接改写它；特别版这条线的编号是 sp1、sp2 …）
  2. 编译前先停掉「本目录里」正在运行的主程序/插件，发完再自动拉起来
     （只按路径认亲：别处装的别的版本、以及经典版，都不动）
  3. 调 build.ps1 编译到 dist\（版本号写进 exe 的「关于」页）
  4. 离屏渲染截图到 outputs\screenshots\
  5. 铺出 outputs\TimePlanner-<版本>-special\ 并打 -app.zip / -source.zip（都带上 CHANGELOG.md）
  这里是「特别版」那条线（主线 main，标签 special/v<版本>）；经典版是另一条线，见 sync-classic.ps1。
  更新日志在仓库根的 CHANGELOG.md：发版前请先把这一版写进去
  6. 自动删掉 outputs\ 里其它版本的目录和压缩包（只认 outputs 正下方、
     名字是 TimePlanner-<版本> 的目录 / TimePlanner-<版本>-*.zip，
     screenshots\、README.md 和经典版（*-classic*）不碰；加 -DryRun 可以只看不删）
  7. 把 dist\ 里的 exe 铺到「固定运行目录」（默认 ..\..\TimePlanner-special\，见 AGENTS.md）：
     在那儿跑着的实例先停、旧 exe 备份成 *.bak-<时间戳>、铺完再拉起来。exe 只放这一个地方，
     日期留给日志 / 产物 / 快照那些目录用。-InstallDir 换地方，-NoInstall 只打包不铺。

  用法：
    powershell -ExecutionPolicy Bypass -File release.ps1
    powershell -ExecutionPolicy Bypass -File release.ps1 -Version 1.5
    powershell -ExecutionPolicy Bypass -File release.ps1 -DryRun
    powershell -ExecutionPolicy Bypass -File release.ps1 -NoInstall    （只打包，不铺固定目录）
#>
param(
    [string]$Version,
    [switch]$SkipBuild,
    [switch]$SkipShots,
    [switch]$DryRun,
    [string]$InstallDir = "",
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"

$tp      = $PSScriptRoot
$root    = Split-Path -Parent $tp
$outputs = Join-Path $root "outputs"
$dist    = Join-Path $tp   "dist"
$verTxt  = Join-Path $tp   "version.txt"
$utf8    = New-Object System.Text.UTF8Encoding($false)

if ($Version) {
    if ($Version -notmatch '^(\d+(\.\d+)*|sp\d+)$') { throw "版本号格式不对：$Version（数字如 1.5.1，或特别版的 sp1、sp2）" }
    [System.IO.File]::WriteAllText($verTxt, $Version + "`r`n", $utf8)
}
$ver = ""
if (Test-Path $verTxt) { $ver = (Get-Content $verTxt -Raw).Trim() }
if (-not $ver) { throw "version.txt 是空的" }
if ($ver -notmatch '^(\d+(\.\d+)*|sp\d+)$') { throw "version.txt 里的版本号格式不对：$ver" }

$name   = "TimePlanner-" + $ver + "-special"
$appZip = $name + "-app.zip"
$srcZip = $name + "-source.zip"

Write-Host ""
Write-Host ("=== 发版 " + $name + " ===") -ForegroundColor Cyan

# ---------- 1. 停掉运行中的实例（编译时 exe 被占用会 CS0016 失败） ----------
# 只停从本目录跑起来的：别处装的别的版本（比如经典版）还在用，不该被发版连坐。
$procs = @(Get-Process TimePlanner, TimePlanner.Widget -ErrorAction SilentlyContinue | Where-Object {
    try { $_.Path -and $_.Path.StartsWith($tp, [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
})
$wasRunning = $procs.Count -gt 0
if ($wasRunning -and -not $DryRun) {
    Write-Host ("  停止实例：" + (($procs | ForEach-Object { $_.ProcessName + "(" + $_.Id + ")" }) -join ", "))
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
}

# ---------- 2. 编译 ----------
if (-not $SkipBuild) {
    Write-Host "  编译 dist\ ..."
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $tp "build.ps1")
    if ($LASTEXITCODE -ne 0) { throw "构建失败" }
} else {
    Write-Host "  跳过编译（-SkipBuild）"
}

# ---------- 3. 截图 ----------
if (-not $SkipShots) {
    Write-Host "  离屏渲染截图 ..."
    $tmp = Join-Path $root "work\release-shots"
    if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
    $tmpMain   = Join-Path $tmp "main"
    $tmpWidget = Join-Path $tmp "widget"
    New-Item -ItemType Directory -Force -Path $tmpMain, $tmpWidget | Out-Null
    & (Join-Path $dist "TimePlanner.exe")        --render $tmpMain   | Out-Null
    & (Join-Path $dist "TimePlanner.Widget.exe") --render $tmpWidget | Out-Null

    $shots = Join-Path $outputs "screenshots"
    if (Test-Path $shots) { Remove-Item -LiteralPath $shots -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $shots | Out-Null
    $map = @(
        @((Join-Path $tmpMain   "main-today.png"),     "main-today.png"),
        @((Join-Path $tmpMain   "main-week.png"),      "main-week.png"),
        @((Join-Path $tmpMain   "main-project.png"),   "main-project.png"),
        @((Join-Path $tmpMain   "main-done.png"),      "main-done.png"),
        @((Join-Path $tmpMain   "main-settings.png"),  "main-settings.png"),
        @((Join-Path $tmpMain   "main-firework.png"),  "main-firework-1.png"),
        @((Join-Path $tmpMain   "main-firework2.png"), "main-firework-2.png"),
        @((Join-Path $tmpWidget "widget.png"),         "widget.png"),
        @((Join-Path $tmpWidget "widget-menu.png"),    "widget-menu.png"),
        @((Join-Path $tmpWidget "widget-fx1.png"),     "widget-firework-1.png"),
        @((Join-Path $tmpWidget "widget-fx2.png"),     "widget-firework-2.png")
    )
    foreach ($m in $map) {
        if (Test-Path -LiteralPath $m[0]) { Copy-Item -LiteralPath $m[0] -Destination (Join-Path $shots $m[1]) -Force }
        else { Write-Warning ("  缺截图：" + $m[0]) }
    }
} else {
    Write-Host "  跳过截图（-SkipShots）"
}

# ---------- 4. 文档里的版本号跟着 version.txt 走 ----------
$usPath = Join-Path $tp "dist\使用说明.txt"
if (Test-Path $usPath) {
    $us = [System.IO.File]::ReadAllText($usPath, [System.Text.Encoding]::UTF8)
    $us = [regex]::Replace($us, '(?m)^时间规划 TimePlanner \S+', ("时间规划 TimePlanner " + $ver))
    [System.IO.File]::WriteAllText($usPath, $us, $utf8)
}
if (Test-Path (Join-Path $tp "README.md")) {
    Copy-Item -LiteralPath (Join-Path $tp "README.md") -Destination (Join-Path $tp "dist\README.md") -Force
    Copy-Item -LiteralPath (Join-Path $tp "README.md") -Destination (Join-Path $outputs "README.md") -Force
}

# ---------- 5. 铺交付目录 ----------
$outDir = Join-Path $outputs $name
if (Test-Path $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
foreach ($f in @("TimePlanner.exe", "TimePlanner.Widget.exe", "使用说明.txt", "README.md", "启动时间规划.cmd")) {
    Copy-Item -LiteralPath (Join-Path $dist $f) -Destination (Join-Path $outDir $f) -Force
}
$changeLog = Join-Path $root "CHANGELOG.md"
if (Test-Path $changeLog) { Copy-Item -LiteralPath $changeLog -Destination (Join-Path $outDir "CHANGELOG.md") -Force }
Write-Host ("  铺出 outputs\" + $name)

# ---------- 5.5 刷新仓库里的免编译目录 exe\（GitHub 上直接下载就能跑，不用编译） ----------
$exeDir = Join-Path $root "exe"
if (-not (Test-Path -LiteralPath $exeDir)) { New-Item -ItemType Directory -Force -Path $exeDir | Out-Null }
$exeFiles = 0
foreach ($f in @("TimePlanner.exe", "TimePlanner.Widget.exe", "使用说明.txt", "启动时间规划.cmd")) {
    $src = Join-Path $dist $f
    if (-not (Test-Path -LiteralPath $src)) { Write-Warning ("    缺文件：" + $f); continue }
    Copy-Item -LiteralPath $src -Destination (Join-Path $exeDir $f) -Force
    $exeFiles++
}
if (Test-Path $changeLog) { Copy-Item -LiteralPath $changeLog -Destination (Join-Path $exeDir "CHANGELOG.md") -Force }
Write-Host ("  刷新 exe\（" + $exeFiles + " 个文件，免编译包）")

# ---------- 6. 打包 ----------
Push-Location $outDir
& tar.exe -a -c -f (Join-Path $outputs $appZip) README.md CHANGELOG.md TimePlanner.exe TimePlanner.Widget.exe 使用说明.txt 启动时间规划.cmd
$tarApp = $LASTEXITCODE
Pop-Location
if ($tarApp -ne 0) { throw "打应用包失败" }

$pack = Join-Path $root "work\release-src"
if (Test-Path $pack) { Remove-Item -LiteralPath $pack -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pack | Out-Null
Copy-Item -LiteralPath (Join-Path $tp "src")   -Destination $pack -Recurse -Force
Copy-Item -LiteralPath (Join-Path $tp "tools") -Destination $pack -Recurse -Force
foreach ($f in @("build.ps1", "release.ps1", "version.txt", "README.md")) {
    Copy-Item -LiteralPath (Join-Path $tp $f) -Destination $pack -Force
}
Copy-Item -LiteralPath (Join-Path $tp "assets\app.ico") -Destination (Join-Path $pack "app.ico") -Force
$srcExtra = @()
if (Test-Path $changeLog) { Copy-Item -LiteralPath $changeLog -Destination (Join-Path $pack "CHANGELOG.md") -Force; $srcExtra += "CHANGELOG.md" }
& tar.exe -a -c -f (Join-Path $outputs $srcZip) -C $pack src tools build.ps1 release.ps1 version.txt README.md app.ico $srcExtra
if ($LASTEXITCODE -ne 0) { throw "打源码包失败" }

# ---------- 7. 删掉其它版本（这就是「自动删除上一版本」） ----------
$outFull = (Resolve-Path -LiteralPath $outputs).Path
$keep = @($name, $appZip, $srcZip)
$deleted = 0
foreach ($item in @(Get-ChildItem -LiteralPath $outFull)) {
    if ($keep -contains $item.Name) { continue }
    if ($item.Name -notmatch '^TimePlanner-(\d+(\.\d+)*|sp\d+)') { continue }
    if ($item.Name -like '*-classic*') { continue }   # 经典版是另一条线，不归这里清
    if ((Split-Path -Parent $item.FullName) -ne $outFull) { continue }
    if ($DryRun) { Write-Host ("    [干跑] 会删除 " + $item.Name) -ForegroundColor Yellow; continue }
    Remove-Item -LiteralPath $item.FullName -Recurse -Force
    Write-Host ("    已删除旧版本 " + $item.Name) -ForegroundColor DarkGray
    $deleted++
}
if ($deleted -eq 0 -and -not $DryRun) { Write-Host "    没有需要清理的旧版本" }

# ---------- 8. 铺到固定运行目录（exe 只放这一个地方，别散在日期目录里） ----------
# 这台机器上的约定（见 AGENTS.md）：特别版 ..\..\TimePlanner-special\，经典版 ..\..\TimePlanner-1.5-classic\。
$specialDir = $InstallDir
if (-not $specialDir) { $specialDir = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) "TimePlanner-special" }
$installedHere = $false
if ($NoInstall) {
    Write-Host "  跳过固定运行目录（-NoInstall）"
} elseif (-not (Test-Path -LiteralPath $specialDir)) {
    Write-Warning ("  固定运行目录不存在，没铺：" + $specialDir)
} elseif (-not $DryRun) {
    $there = @(Get-Process TimePlanner, TimePlanner.Widget -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($specialDir, [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
    })
    if ($there.Count -gt 0) {
        Write-Host ("  停掉固定目录里的实例：" + (($there | ForEach-Object { $_.ProcessName + "(" + $_.Id + ")" }) -join ", "))
        $there | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        $installedHere = $true
    }
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    foreach ($f in @("TimePlanner.exe", "TimePlanner.Widget.exe", "使用说明.txt", "README.md", "启动时间规划.cmd")) {
        $s = Join-Path $dist $f
        if (-not (Test-Path -LiteralPath $s)) { Write-Warning ("    缺文件：" + $f); continue }
        $d = Join-Path $specialDir $f
        if ((Test-Path -LiteralPath $d) -and $f.EndsWith(".exe")) { Copy-Item -LiteralPath $d -Destination ($d + ".bak-" + $stamp) -Force }
        Copy-Item -LiteralPath $s -Destination $d -Force
    }
    if (Test-Path $changeLog) { Copy-Item -LiteralPath $changeLog -Destination (Join-Path $specialDir "CHANGELOG.md") -Force }
    Write-Host ("  铺到 " + $specialDir + "（旧 exe 备份成 *.bak-" + $stamp + "）")
}

# ---------- 9. 把刚才停掉的实例拉起来 ----------
if ($wasRunning -and -not $DryRun) {
    Start-Process -FilePath (Join-Path $dist "TimePlanner.exe") -ArgumentList "--tray" -WindowStyle Hidden
    Write-Host "  已重新启动（托盘，dist）"
}
if ($installedHere) {
    Start-Process -FilePath (Join-Path $specialDir "TimePlanner.exe") -ArgumentList "--tray" -WindowStyle Hidden
    Write-Host ("  已重新启动（托盘，" + $specialDir + "）")
}

Write-Host ""
Write-Host ("完成：" + $name + "（特别版）") -ForegroundColor Green
Write-Host ("  outputs\" + $name + "\")
Write-Host ("  outputs\" + $appZip)
Write-Host ("  outputs\" + $srcZip)
Write-Host "  outputs\screenshots\"
if (-not $NoInstall -and (Test-Path -LiteralPath $specialDir)) { Write-Host ("  " + $specialDir + "\") }

