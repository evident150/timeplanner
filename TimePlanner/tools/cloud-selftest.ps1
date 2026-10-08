# 云端合并逻辑的自检：编译 tools\CloudSelfTest.cs 并跑一遍。
# 只碰内存对象，不读 %APPDATA%、不发网络、不写任何文件。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Definition)
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc" }
$gacRoot = "C:\Windows\Microsoft.NET\assembly"

function Find-Ref([string]$name) {
    $all = Get-ChildItem $gacRoot -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue
    $msil = $all | Where-Object { $_.FullName -like "*GAC_MSIL*" } | Select-Object -First 1
    if ($msil) { return $msil.FullName }
    $any = $all | Select-Object -First 1
    if (-not $any) { throw "找不到引用程序集: $name" }
    return $any.FullName
}

$refNames = @("System", "System.Core", "System.Xml", "System.Drawing", "System.Runtime.Serialization",
              "System.Security", "PresentationCore", "PresentationFramework",
              "WindowsBase", "System.Xaml")
$refArgs = @()
foreach ($n in $refNames) { $refArgs += "/r:" + (Find-Ref $n) }

$work = Join-Path $root "work\cloud-selftest"
New-Item -ItemType Directory -Force -Path $work | Out-Null

# AppVersion 平时是 build.ps1 编译期生成的，自检也补一份凑数
$verFile = Join-Path $work "AppVersion.cs"
@"
namespace TimePlanner.Core
{
    public static class AppVersion
    {
        public const string Number = "selftest";
        public const string EditionTag = "special";
        public const string EditionLabel = "特别版";
        public const string Display = "版本 selftest 特别版";
    }
}
"@ | Set-Content -Encoding UTF8 $verFile

$core = @(Get-ChildItem (Join-Path $root "src\Core\*.cs") | ForEach-Object { $_.FullName }) + $verFile
$out = Join-Path $work "cloud-selftest.exe"
& $csc /nologo /noconfig /target:exe /langversion:5 /codepage:65001 /out:$out $refArgs $core (Join-Path $root "tools\CloudSelfTest.cs")
if ($LASTEXITCODE -ne 0) { throw "自检编译失败" }
& $out
exit $LASTEXITCODE
