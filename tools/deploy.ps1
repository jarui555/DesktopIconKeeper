# 把 exe 部署到"能正常跑"的位置。
#
# 为什么要这一步：
#   DSH 的 default-workspace 目录被打上了 Low 完整性标签（MIC），
#   该目录下所有文件都继承 Low 标签。而**带 Low 标签的文件启动的进程，
#   令牌会被强制设为 Low** —— 不管谁启动它（双击、快捷方式、explorer、cmd 都一样）。
#   结果是工具被 UIPI 挡在 explorer 外面，读不到桌面图标。
#
#   所以编译产物不能直接在 workspace 里运行，必须复制到 Medium 标签的目录。
#
# 用法：powershell -ExecutionPolicy Bypass -File tools\deploy.ps1

param(
    [string]$Dest = (Join-Path $env:LOCALAPPDATA 'DesktopIconKeeper'),
    [switch]$NoShortcut
)

$ErrorActionPreference = 'Stop'

$src = Join-Path $PSScriptRoot '..\dist\DesktopIconKeeper.exe'
if (-not (Test-Path $src)) { throw "找不到 $src，先跑 build.ps1" }
$src = (Resolve-Path $src).Path

New-Item -ItemType Directory -Path $Dest -Force | Out-Null
$dest = Join-Path $Dest 'DesktopIconKeeper.exe'
Copy-Item $src $dest -Force

Write-Host "已部署 -> $dest"
Write-Host ("  大小   " + (Get-Item $dest).Length.ToString('N0') + " bytes")

# 校验完整性标签：必须是 Medium（无标签）或更高，绝不能是 Low
$acl = (icacls $dest 2>&1 | Out-String)
$m = [regex]::Match($acl, 'Mandatory Label\\(\w+)')
$label = if ($m.Success) { $m.Groups[1].Value } else { 'Medium（无标签）' }
Write-Host ("  完整性 " + $label)

if ($label -match '^(Low|Untrusted)') {
    Write-Host ""
    Write-Host "警告：目标目录仍带低完整性标签，从这里启动会被降权。" -ForegroundColor Red
    Write-Host "     换一个目录：-Dest 'C:\Tools\DesktopIconKeeper'" -ForegroundColor Yellow
    exit 2
}

# 桌面快捷方式
if (-not $NoShortcut) {
    $lnkPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '桌面图标位置记忆工具.lnk'
    $sh = New-Object -ComObject WScript.Shell
    $lnk = $sh.CreateShortcut($lnkPath)
    $lnk.TargetPath = $dest
    $lnk.WorkingDirectory = $Dest
    $lnk.IconLocation = "$dest,0"
    $lnk.Description = '一键记录 / 还原桌面图标布局'
    $lnk.Save()
    Write-Host "  快捷方式 $lnkPath"
}

Write-Host ""
Write-Host "自检："
$out = Join-Path $env:TEMP 'dik-deploy-doctor.txt'
$p = Start-Process -FilePath $dest -ArgumentList 'doctor' -Wait -PassThru -NoNewWindow `
                   -RedirectStandardOutput $out -RedirectStandardError ($out + '.err')
$txt = [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8)
$txt -split "`n" | Where-Object { $_ -match '本进程完整性|托盘图标|图标数量|结论' } | ForEach-Object { Write-Host ("  " + $_.Trim()) }

if ($txt -match '本进程完整性\s+High') { Write-Host "`n可以正常使用。" -ForegroundColor Green }
elseif ($txt -match '本进程完整性\s+Medium') { Write-Host "`n可以正常使用（explorer 若也是 Medium 就没问题）。" -ForegroundColor Green }
else { Write-Host "`n仍然被降权，检查部署目录的完整性标签。" -ForegroundColor Red; exit 2 }
