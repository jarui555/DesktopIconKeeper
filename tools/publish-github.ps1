#Requires -Version 5.1
<#
.SYNOPSIS
    一键把 DesktopIconKeeper 开源到 GitHub：建仓库 → 推送 → 打 tag → 发 Release。

.DESCRIPTION
    GitHub 从 2021 年 8 月起不再接受「账号 + 密码」做 Git 认证，
    必须用 Personal Access Token（PAT）。

    获取方式（30 秒）：
      1. 打开 https://github.com/settings/tokens?type=beta
      2. Generate new token
      3. 勾选 repo 权限（Fine-grained token 只需给目标仓库 Contents: Read and write）
      4. 生成后复制 ghp_ / github_pat_ 开头的那串

    脚本不会把 token 写进 .git/config —— 推送用的是一次性 URL。

.PARAMETER Token
    GitHub Personal Access Token。不传则读环境变量 GITHUB_TOKEN。

.PARAMETER Owner
    账号或组织名，默认 jarui555。

.PARAMETER Repo
    仓库名，默认 DesktopIconKeeper。

.PARAMETER Tag
    要发的版本 tag，默认 v1.3.0。

.PARAMETER Private
    建成私有仓库（默认公开）。

.EXAMPLE
    .\tools\publish-github.ps1 -Token ghp_xxxxxxxxxxxxxxxxxxxx

.EXAMPLE
    $env:GITHUB_TOKEN = 'ghp_xxx'; .\tools\publish-github.ps1
#>
param(
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$Owner = 'jarui555',
    [string]$Repo = 'DesktopIconKeeper',
    [string]$Tag = 'v1.3.0',
    [switch]$Private
)

# git 会把正常信息也写到 stderr（例如 "No such remote"），在 Stop 模式下会被
# 当成致命错误直接打断脚本。所以这里用 Continue，成败一律看 $LASTEXITCODE。
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

function Say([string]$m) { Write-Host $m }
function Ok([string]$m) { Write-Host "  [OK]   $m" -ForegroundColor Green }
function Bad([string]$m) { Write-Host "  [FAIL] $m" -ForegroundColor Red }
function Step([string]$m) { Write-Host "`n== $m" -ForegroundColor Cyan }

# ---------------------------------------------------------------- 前置检查
if ([string]::IsNullOrWhiteSpace($Token)) {
    Bad '缺少 Token'
    Say ''
    Say '  GitHub 已不支持用账号密码推送。请先生成 Personal Access Token：'
    Say '    https://github.com/settings/tokens?type=beta'
    Say '  然后：'
    Say '    .\tools\publish-github.ps1 -Token ghp_xxxxxxxx'
    exit 2
}

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not (Test-Path (Join-Path $root '.git'))) { Bad "这里不是 git 仓库：$root"; exit 2 }

    $headers = @{
        Authorization          = "Bearer $Token"
        Accept                 = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
        'User-Agent'           = 'DesktopIconKeeper-publisher'
    }

    # ------------------------------------------------------------ 1. 验 token
    Step '1/6  验证 Token'
    try {
        $me = Invoke-RestMethod -Uri 'https://api.github.com/user' -Headers $headers -TimeoutSec 30
    }
    catch {
        Bad 'Token 无效或已过期：' + $_.Exception.Message
        Say '  重新生成：https://github.com/settings/tokens?type=beta'
        exit 3
    }
    Ok "已登录：$($me.login)"

    if ($me.login -ne $Owner) {
        Say "  注意：token 属于 $($me.login)，目标是 $Owner —— 如果这是组织仓库就继续。"
    }

    # ------------------------------------------------------------ 2. 建仓库
    Step '2/6  创建远程仓库'
    $repoUrl = "https://api.github.com/repos/$Owner/$Repo"
    $exists = $false
    try {
        $null = Invoke-RestMethod -Uri $repoUrl -Headers $headers -TimeoutSec 30
        $exists = $true
        Ok "仓库已存在：$Owner/$Repo"
    }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -ne 404) {
            Bad '查询仓库失败：' + $_.Exception.Message
            exit 4
        }
    }

    if (-not $exists) {
        $body = @{
            name        = $Repo
            description = 'Windows 桌面图标位置记忆工具 — 一键记录/还原图标布局。单文件 exe，中英双语。'
            homepage    = ''
            private     = [bool]$Private
            has_issues  = $true
            has_wiki    = $false
            has_projects = $false
        } | ConvertTo-Json

        # 先按当前用户建；失败再按组织建
        $created = $null
        try {
            $created = Invoke-RestMethod -Uri 'https://api.github.com/user/repos' -Method Post -Headers $headers -Body $body -ContentType 'application/json' -TimeoutSec 30
        }
        catch {
            try {
                $created = Invoke-RestMethod -Uri "https://api.github.com/orgs/$Owner/repos" -Method Post -Headers $headers -Body $body -ContentType 'application/json' -TimeoutSec 30
            }
            catch {
                Bad '建仓库失败：' + $_.Exception.Message
                exit 4
            }
        }
        Ok "已创建：$($created.full_name)  ($($created.visibility))"
    }

    # ------------------------------------------------------------ 3. 推送 main
    Step '3/6  推送 main 分支'
    $clean = "https://github.com/$Owner/$Repo.git"
    $authUrl = "https://x-access-token:$Token@github.com/$Owner/$Repo.git"

    & git remote remove origin 2>$null
    & git remote add origin $clean
    if ($LASTEXITCODE -ne 0) { Bad '设置 remote 失败'; exit 5 }

    $branch = (& git rev-parse --abbrev-ref HEAD).Trim()
    Say "  当前分支：$branch"

    $env:GIT_TERMINAL_PROMPT = '0'
    & git push $authUrl "${branch}:${branch}" 2>&1 | ForEach-Object { "  $_" }
    if ($LASTEXITCODE -ne 0) { Bad '推送失败'; exit 5 }
    Ok '已推送'

    & git branch --set-upstream-to="origin/$branch" $branch 2>$null | Out-Null

    # ------------------------------------------------------------ 4. 打 tag
    Step "4/6  打 tag $Tag"
    & git rev-parse $Tag 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Say "  tag 已存在，跳过"
    } else {
        & git tag -a $Tag -m "DesktopIconKeeper $Tag"
        Ok "已创建 $Tag"
    }
    & git push $authUrl $Tag 2>&1 | ForEach-Object { "  $_" }
    if ($LASTEXITCODE -ne 0) { Say '  (tag 推送可能已存在，继续)' } else { Ok 'tag 已推送' }

    # ------------------------------------------------------------ 5. 发 Release
    Step "5/6  创建 Release $Tag"
    $exe = Join-Path $root 'dist\DesktopIconKeeper.exe'
    $releaseBody = @"
## DesktopIconKeeper $Tag

Windows 桌面图标位置记忆工具 —— 一键记录、一键还原图标布局。

### 直接下载

下载下面的 DesktopIconKeeper.exe，**双击即用**，不需要安装、不需要 .NET 运行时。

> 建议放到 %LOCALAPPDATA%\DesktopIconKeeper\ 再运行。
> 如果放在被标记为「低完整性」的目录（某些沙箱/同步盘），程序会被强制降权，
> 读写桌面和托盘图标都会失败 —— doctor 命令可以诊断这种情况。

### 本版内容

- 图标过渡动画（分帧插值，最终坐标不受影响）
- 一键隐藏 / 显示全部桌面图标
- 关机 / 注销前自动备份
- 中英双语界面，跟随系统语言
- 修复：Application.Run 强制显示窗口导致缩托盘失效
- 修复：广播消息叫不醒托盘实例，改用命名事件握手

### 校验

---
SHA256  $(if (Test-Path $exe) { (Get-FileHash $exe -Algorithm SHA256).Hash } else { '(构建后生成)' })
---
"@

    $relBody = @{ tag_name = $Tag; name = "DesktopIconKeeper $Tag"; body = $releaseBody; draft = $false; prerelease = $false } | ConvertTo-Json
    $release = $null
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Owner/$Repo/releases" -Method Post -Headers $headers -Body $relBody -ContentType 'application/json' -TimeoutSec 30
        Ok "Release 已创建：$($release.html_url)"
    }
    catch {
        Say '  Release 创建失败（可能已存在）：' + $_.Exception.Message
    }

    # ------------------------------------------------------------ 6. 传 exe
    Step '6/6  上传 exe 到 Release'
    if ($null -eq $release) {
        Say '  跳过（没有可用的 Release）'
    }
    elseif (-not (Test-Path $exe)) {
        Say "  跳过（找不到 $exe，先跑 build.ps1）"
    }
    else {
        $uploadUrl = ($release.upload_url -replace '\{.*\}', '') + "?name=DesktopIconKeeper.exe"
        try {
            $bytes = [System.IO.File]::ReadAllBytes($exe)
            $asset = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $headers `
                     -Body $bytes -ContentType 'application/octet-stream' -TimeoutSec 120
            Ok "已上传：$($asset.name)  ($([math]::Round($asset.size/1KB,1)) KB)"
        }
        catch {
            Say '  上传失败：' + $_.Exception.Message
            Say "  可以手动拖到 $($release.html_url) 的附件区"
        }
    }

    # ------------------------------------------------------------ 完成
    Say ''
    Say ('=' * 62)
    Ok "仓库：https://github.com/$Owner/$Repo"
    Ok "Release：https://github.com/$Owner/$Repo/releases/tag/$Tag"
    Say ('=' * 62)
    Say ''
    Say '  安全提醒：这个 token 已经用过了，用完请到'
    Say '    https://github.com/settings/tokens'
    Say '  把它删掉或轮换，别留在命令历史里。'
    Say ''
}
finally {
    Pop-Location
    $env:GIT_TERMINAL_PROMPT = $null
}
