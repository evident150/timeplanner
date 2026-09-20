param(
    [string]$Out = "dist",
    [switch]$SkipIcon
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $root

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc" }
$gacRoot = "C:\Windows\Microsoft.NET\assembly"
$work = Join-Path $root "work\build"
if (-not (Test-Path $work)) { New-Item -ItemType Directory -Force -Path $work | Out-Null }
$dist = Join-Path $root $Out
if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Force -Path $dist | Out-Null }

function Find-Ref([string]$name) {
    $all = Get-ChildItem $gacRoot -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue
    $msil = $all | Where-Object { $_.FullName -like "*GAC_MSIL*" } | Select-Object -First 1
    if ($msil) { return $msil.FullName }
    $any = $all | Select-Object -First 1
    if (-not $any) { throw "找不到引用程序集: $name" }
    return $any.FullName
}

$refNames = @("System", "System.Core", "System.Xml", "System.Drawing", "System.Runtime.Serialization",
              "PresentationCore", "PresentationFramework",
              "WindowsBase", "System.Xaml")
$refArgs = @()
foreach ($n in $refNames) { $refArgs += "/r:" + (Find-Ref $n) }

$manifest = Join-Path $work "app.manifest"
@"
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="TimePlanner.app" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
      <supportedOS Id="{1f676c76-80e1-4239-95bb-83d0f6d0da78}" />
      <supportedOS Id="{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}" />
      <supportedOS Id="{35138b9a-5d96-4fbd-8e2d-a2440225f93a}" />
      <supportedOS Id="{e2011457-1546-43c5-a5fe-008deee3d3f0}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">permonitorv2,permonitor,system</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
"@ | Set-Content -Encoding UTF8 $manifest

# 版本号只有一个来源：version.txt（发版脚本 release.ps1 会读它、并在需要时改写它）
$version = "1.0"
$verTxt = Join-Path $root "version.txt"
if (Test-Path $verTxt) { $version = (Get-Content $verTxt -Raw).Trim() }
if (-not $version) { $version = "1.0" }
$verFile = Join-Path $work "AppVersion.cs"
@"
namespace TimePlanner.Core
{
    public static class AppVersion
    {
        public const string Number = "$version";
        public const string Display = "版本 $version";
    }
}
"@ | Set-Content -Encoding UTF8 $verFile

$core = @(Get-ChildItem (Join-Path $root "src\Core\*.cs") | ForEach-Object { $_.FullName }) + $verFile
$app = Get-ChildItem (Join-Path $root "src\App\*.cs") | ForEach-Object { $_.FullName }
$widget = Get-ChildItem (Join-Path $root "src\Widget\*.cs") | ForEach-Object { $_.FullName }

$icon = Join-Path $root "assets\app.ico"
if (-not $SkipIcon -or -not (Test-Path $icon)) {
    $mk = Join-Path $work "makeicon.exe"
    & $csc /nologo /noconfig /target:exe /langversion:5 /codepage:65001 /out:$mk $refArgs $core (Join-Path $root "tools\MakeIcon.cs")
    if ($LASTEXITCODE -ne 0) { throw "图标生成器编译失败" }
    & $mk $icon
    if ($LASTEXITCODE -ne 0) { throw "图标生成失败" }
}

$art = Join-Path $root "assets\minister.png"
$resArgs = @()
if (Test-Path $art) { $resArgs += "/resource:" + $art }

$common = @("/nologo", "/noconfig", "/langversion:5", "/codepage:65001", "/platform:anycpu",
            "/optimize+", "/warn:4",
            "/win32manifest:$manifest",
            "/win32icon:$icon")

$appExe = Join-Path $dist "TimePlanner.exe"
& $csc @common /target:winexe /out:$appExe $refArgs $resArgs $core $app
if ($LASTEXITCODE -ne 0) { throw "主程序编译失败" }

$widgetExe = Join-Path $dist "TimePlanner.Widget.exe"
& $csc @common /target:winexe /out:$widgetExe $refArgs $resArgs $core $widget
if ($LASTEXITCODE -ne 0) { throw "桌面插件编译失败" }

Write-Host ""
Write-Host ("构建完成:  版本 " + $version) -ForegroundColor Green
Get-ChildItem $dist | ForEach-Object { Write-Host ("  {0,-28} {1,10:N0} KB" -f $_.Name, ($_.Length / 1KB)) }
