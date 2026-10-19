# 启动 GUI -> 截屏 -> 关闭 GUI，用于验证界面渲染
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\dist\DesktopIconKeeper.exe'),
    [string]$Out = (Join-Path $PSScriptRoot '..\build\ui-shot.png'),
    [int]$Width = 1400
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$exePath = (Resolve-Path $Exe).Path

# 用 ShellExecute 启动，避免子进程继承本进程的标准输出管道
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $exePath
$psi.Arguments = 'gui'
$psi.UseShellExecute = $true
$psi.WorkingDirectory = (Split-Path $exePath)
$proc = [System.Diagnostics.Process]::Start($psi)

Start-Sleep -Seconds 4

try {
    $procs = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if (-not $procs) { Write-Output "warning: 进程已退出，界面可能启动失败（检查 data\crash.log）" }
    else { Write-Output ("window title: '{0}'  responding={1}" -f $procs.MainWindowTitle, $procs.Responding) }

    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $g.Dispose()

    $h = [int]($b.Height * $Width / $b.Width)
    $small = New-Object System.Drawing.Bitmap($Width, $h)
    $g2 = [System.Drawing.Graphics]::FromImage($small)
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.DrawImage($bmp, 0, 0, $Width, $h)
    $g2.Dispose()

    $outDir = Split-Path -Parent $Out
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
    $small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $small.Dispose()
    $bmp.Dispose()
    Write-Output ("saved: {0} ({1} bytes)" -f $Out, (Get-Item $Out).Length)
}
finally {
    Get-Process DesktopIconKeeper -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Output "gui closed"
}
