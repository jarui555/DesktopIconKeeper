# 实机自检：记录 -> 打乱 -> 还原 -> 校验
# 用法：powershell -ExecutionPolicy Bypass -File tools\selftest.ps1
#
# 注意：本工具是 winexe（GUI 子系统），PowerShell 不会等待它结束，
# 所以这里统一用 Start-Process -Wait 拿退出码和输出。

param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\dist\DesktopIconKeeper.exe'),
    [string]$Name = '自检-基线'
)

$ErrorActionPreference = 'Stop'
$tmp = Join-Path $env:TEMP 'dik-selftest'
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

function Invoke-Dik {
    param([string[]]$Arguments)
    $out = Join-Path $tmp 'out.txt'
    $err = Join-Path $tmp 'err.txt'
    $p = Start-Process -FilePath $Exe -ArgumentList $Arguments -Wait -PassThru -NoNewWindow `
                       -RedirectStandardOutput $out -RedirectStandardError $err
    $stdout = ''
    $stderr = ''
    if (Test-Path $out) { $stdout = [System.IO.File]::ReadAllText($out, [System.Text.Encoding]::UTF8) }
    if (Test-Path $err) { $stderr = [System.IO.File]::ReadAllText($err, [System.Text.Encoding]::UTF8) }
    return [pscustomobject]@{ Code = $p.ExitCode; Out = $stdout; Err = $stderr }
}

function Count-Diff {
    param([string]$A, [string]$B)
    $r = Invoke-Dik @('diff', $A, $B)
    $n = 0
    foreach ($line in ($r.Out -split "`r?`n")) {
        if ($line -match '^(移动|新增|缺失)') { $n++ }
    }
    return $n
}

$results = New-Object System.Collections.ArrayList
function Check {
    param([string]$Title, [bool]$Ok, [string]$Detail)
    [void]$results.Add([pscustomobject]@{ 项 = $Title; 结果 = $(if ($Ok) { 'PASS' } else { 'FAIL' }); 说明 = $Detail })
    $mark = if ($Ok) { '[PASS]' } else { '[FAIL]' }
    Write-Host ("{0} {1}  {2}" -f $mark, $Title, $Detail)
}

Write-Host "=== 桌面图标位置记忆工具 · 实机自检 ==="
Write-Host "目标程序：$((Resolve-Path $Exe).Path)"
Write-Host ""

# --- 0. 环境 ---
$r = Invoke-Dik @('doctor')
Check '环境诊断' ($r.Code -eq 0) ("exit=" + $r.Code)
$iconLine = ($r.Out -split "`r?`n" | Where-Object { $_ -match '图标数量' } | Select-Object -First 1)
Write-Host "        $iconLine"
if ($r.Out -match '自动排列图标\s+开') {
    Write-Host "        警告：桌面开启了自动排列，本自检会先跳过写入验证"
}

# --- 0a. 安全闸门（必须先过）---
# 铁律：工具读不到桌面时，绝对不能去挪图标。
# 否则打乱生效了却还原不了，用户的桌面就真被搞乱了。
$gateFail = $null
if ($r.Code -eq 2) {
    $gateFail = '桌面视图打不开（权限不足），自检中止'
} elseif ($iconLine -match '图标数量\s+0\b') {
    $gateFail = '读到的图标数为 0，无法确认能读就能写，自检中止'
} elseif ($r.Out -match 'UIPI') {
    $gateFail = 'UIPI 拦截了窗口消息，自检中止'
}
if ($gateFail) {
    Write-Host ""
    Write-Host "=============================================="
    Write-Host " 自检中止：$gateFail"
    Write-Host " 没有对桌面做任何修改。"
    Write-Host "=============================================="
    $r.Out -split "`r?`n" | Where-Object { $_ -match '完整性|UIPI|解决办法|  \d\.' } | ForEach-Object { Write-Host "   $_" }
    exit 2
}

# --- 0b. 界面自检 ---
$u = Invoke-Dik @('uicheck')
$uLine = ($u.Out -split "`r?`n" | Where-Object { $_ -match '^窗体 ' }) -join ''
Check '界面自检' ($u.Code -eq 0) ("exit=" + $u.Code + " " + $uLine)

# --- 0c. 权限降级诊断（复现沙箱 / 受限令牌场景）---
# 注意：低完整性进程写不进 %TEMP%（那里带 Medium 标签），输出必须落在工作区内。
$lowDir = Join-Path (Split-Path $PSScriptRoot -Parent) 'build'
if (-not (Test-Path $lowDir)) { New-Item -ItemType Directory -Path $lowDir -Force | Out-Null }
$lowOut = Join-Path $lowDir 'low-doctor.txt'
Remove-Item $lowOut -Force -ErrorAction SilentlyContinue
$lowCmd = '/c ""' + (Resolve-Path $Exe).Path + '" doctor > "' + $lowOut + '" 2>&1"'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-as-low.ps1') `
    -Exe $env:ComSpec -Arguments $lowCmd -Rid 0x1000 -Wait | Out-Null
$lowText = if (Test-Path $lowOut) { [System.IO.File]::ReadAllText($lowOut, [System.Text.Encoding]::UTF8) } else { '' }
$lowOk = ($lowText -match '本进程完整性\s+Low') -and
         ($lowText -match '完整性级别：本工具 Low.*explorer High') -and
         ($lowText -match '解决办法')
Check '降权诊断准确' $lowOk ($(if ($lowOk) { 'Low 进程被正确识别并给出对比' } else { '诊断输出不符合预期' }))

# --- 1. 记录 ---
$r = Invoke-Dik @('save', $Name)
Check '记录布局' ($r.Code -eq 0) ("exit=" + $r.Code + " " + (($r.Out -split "`r?`n")[0]))

# --- 2. 打乱桌面 ---
$src = @'
using System;
using System.Runtime.InteropServices;
public class DikTest {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
  public delegate bool EnumProc(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint a, bool i, uint p);
  [DllImport("kernel32.dll")] public static extern IntPtr VirtualAllocEx(IntPtr p, IntPtr a, IntPtr s, uint t, uint pr);
  [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr p, IntPtr a, byte[] b, int s, out IntPtr r);
  [DllImport("kernel32.dll")] public static extern bool VirtualFreeEx(IntPtr p, IntPtr a, IntPtr s, uint t);
  public static IntPtr Lv() {
    IntPtr pm = FindWindow("Progman", null);
    IntPtr dv = FindWindowEx(pm, IntPtr.Zero, "SHELLDLL_DefView", null);
    if (dv != IntPtr.Zero) { IntPtr l = FindWindowEx(dv, IntPtr.Zero, "SysListView32", null); if (l != IntPtr.Zero) return l; }
    IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr p) {
      IntPtr d = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
      if (d == IntPtr.Zero) return true;
      IntPtr l = FindWindowEx(d, IntPtr.Zero, "SysListView32", null);
      if (l == IntPtr.Zero) return true;
      found = l; return false;
    }, IntPtr.Zero);
    return found;
  }
  public static string Move(int dx, int dy) {
    IntPtr lv = Lv(); if (lv == IntPtr.Zero) return "lv not found";
    uint pid; GetWindowThreadProcessId(lv, out pid);
    IntPtr h = OpenProcess(0x0008|0x0010|0x0020|0x0400, false, pid);
    IntPtr mem = VirtualAllocEx(h, IntPtr.Zero, (IntPtr)64, 0x3000, 0x04);
    int n = (int)SendMessage(lv, 0x1004, IntPtr.Zero, IntPtr.Zero);
    int moved = 0;
    for (int i = 0; i < n; i++) {
      SendMessage(lv, 0x1010, (IntPtr)i, mem);
      byte[] b = new byte[8]; IntPtr rd; ReadProcessMemory(h, mem, b, 8, out rd);
      int x = BitConverter.ToInt32(b,0) + dx; int y = BitConverter.ToInt32(b,4) + dy;
      if (x < 0) x = 0; if (y < 0) y = 0;
      SendMessage(lv, 0x100F, (IntPtr)i, (IntPtr)((y << 16) | (x & 0xFFFF)));
      moved++;
    }
    VirtualFreeEx(h, mem, IntPtr.Zero, 0x8000);
    return "moved=" + moved;
  }
}
'@
Add-Type -TypeDefinition $src -Language CSharp

$mv = [DikTest]::Move(75, 98)
Write-Host "        打乱桌面：$mv"
Start-Sleep -Milliseconds 900

$diffAfterScramble = Count-Diff $Name '--current'
Check '打乱生效（差异 > 0）' ($diffAfterScramble -gt 0) ("差异条数 = " + $diffAfterScramble)

# --- 3. 还原 ---
$r = Invoke-Dik @('restore', $Name)
Check '还原布局' ($r.Code -eq 0) ("exit=" + $r.Code + " " + (($r.Out -split "`r?`n")[0]))

# --- 4. 校验 ---
Start-Sleep -Milliseconds 400
$diffAfterRestore = Count-Diff $Name '--current'
Check '还原后完全一致（差异 = 0）' ($diffAfterRestore -eq 0) ("差异条数 = " + $diffAfterRestore)

# --- 5. 撤销点 ---
# 数据目录有两种：优先 %APPDATA%，写不进去才退回 exe 旁边的 data\（绿色模式）
$undoCandidates = @(
    (Join-Path $env:APPDATA 'DesktopIconKeeper\_undo.json'),
    (Join-Path $Exe '..\data\_undo.json')
)
$undo = $undoCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
Check '撤销点已生成' ($null -ne $undo) ($(if ($undo) { (Get-Item $undo).Length.ToString() + ' bytes  ' + $undo } else { '未找到' }))

Write-Host ""
Write-Host "=== 汇总 ==="
$results | Format-Table -AutoSize | Out-String | Write-Host
$failedItems = @($results | Where-Object { $_.'结果' -eq 'FAIL' })
if ($failedItems.Count -gt 0) {
    Write-Host ("失败 " + $failedItems.Count + " 项：" + (($failedItems | ForEach-Object { $_.'项' }) -join '、'))
    exit 1
}
Write-Host "全部通过"
exit 0
