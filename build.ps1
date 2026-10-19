# 一键编译：src\*.cs -> dist\DesktopIconKeeper.exe
#
# 目标框架 .NET Framework 4.8（Win10 1809+ / Win11 自带），
# 因此产物是单个几百 KB 的 exe，目标机器无需安装任何运行时。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File build.ps1
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Clean

param(
    [switch]$Clean,
    [switch]$SkipIcon
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$srcDir = Join-Path $root 'src'
$buildDir = Join-Path $root 'build'
$distDir = Join-Path $root 'dist'
$outExe = Join-Path $distDir 'DesktopIconKeeper.exe'
$iconPath = Join-Path $buildDir 'app.ico'

if ($Clean) {
    foreach ($d in @($buildDir, $distDir)) {
        if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    }
    Write-Output "cleaned."
}

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

# ---------------------------------------------------------------- 编译器
function Find-Csc {
    $candidates = @()

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -products * -property installationPath 2>$null
        if ($vsPath) {
            $candidates += (Join-Path $vsPath 'MSBuild\Current\Bin\Roslyn\csc.exe')
        }
    }

    $candidates += 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
    $candidates += 'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe'
    $candidates += 'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe'
    $candidates += 'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'

    # .NET SDK 自带的 Roslyn
    $dotnetRoot = 'C:\Program Files\dotnet\sdk'
    if (Test-Path $dotnetRoot) {
        $sdk = Get-ChildItem $dotnetRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($sdk) { $candidates += (Join-Path $sdk.FullName 'Roslyn\bincore\csc.dll') }
    }

    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c) -and $c.EndsWith('.exe')) { return @{ Path = $c; IsDotnet = $false } }
    }

    # 最后退回 .NET Framework 自带的旧编译器（C# 5）
    $fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (Test-Path $fw) { return @{ Path = $fw; IsDotnet = $false } }

    throw "找不到 C# 编译器 (csc.exe)。请安装 Visual Studio 2022 或 .NET SDK。"
}

# ---------------------------------------------------------------- 引用程序集
function Find-RefDir {
    $roots = @(
        'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8',
        'C:\Program Files\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8',
        'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2',
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
    )
    foreach ($r in $roots) {
        if ((Test-Path (Join-Path $r 'mscorlib.dll')) -and (Test-Path (Join-Path $r 'System.Windows.Forms.dll'))) {
            return $r
        }
    }
    throw "找不到 .NET Framework 引用程序集目录。"
}

# ---------------------------------------------------------------- 图标
if (-not $SkipIcon -or -not (Test-Path $iconPath)) {
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\make-icon.ps1') -OutFile $iconPath | Out-Null
    } catch {
        Write-Output "warning: 图标生成失败，将编译为无自定义图标的版本。 ($($_.Exception.Message))"
    }
}

# ---------------------------------------------------------------- 编译
$csc = Find-Csc
$refDir = Find-RefDir

$sources = Get-ChildItem (Join-Path $srcDir '*.cs') | ForEach-Object { $_.FullName }
if ($sources.Count -eq 0) { throw "src 目录下没有 .cs 文件。" }

$refs = @('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll') |
        ForEach-Object { Join-Path $refDir $_ }

Write-Output "compiler : $($csc.Path)"
Write-Output "refs     : $refDir"
Write-Output "sources  : $($sources.Count) 个"

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/debug-',
    '/langversion:7.3',
    '/nostdlib+',
    '/utf8output',
    '/codepage:65001',
    "/out:$outExe",
    "/win32manifest:$(Join-Path $srcDir 'app.manifest')"
)

if (Test-Path $iconPath) { $cscArgs += "/win32icon:$iconPath" }

foreach ($r in $refs) { $cscArgs += "/r:$r" }
$cscArgs += $sources

& $csc.Path @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }

$fi = Get-Item $outExe
Write-Output ""
Write-Output ("OK  ->  {0}" -f $fi.FullName)
Write-Output ("      {0:N0} bytes ({1:N1} KB)" -f $fi.Length, ($fi.Length / 1KB))
