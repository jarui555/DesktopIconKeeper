param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Arguments = '',
    [int]$ExplorerPid = 0,
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'

# 借 explorer.exe 的令牌启动目标进程。
#
# 用途：验证 DesktopIconKeeper 在"正常桌面环境"下的行为。
# DSH 沙箱会给非白名单 exe 挂 Low 完整性标签，导致本工具被 UIPI 挡住，
# 没法在本机复现"用户双击启动"这条路径。复制 explorer 的令牌就能绕开，
# 拿到一个和用户双击等价的 High 进程。
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public class HighLauncher
{
    [DllImport("kernel32.dll", SetLastError=true)] public static extern IntPtr OpenProcess(uint a, bool i, uint p);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr p, uint a, out IntPtr t);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool DuplicateTokenEx(IntPtr t, uint a, IntPtr sa, int lvl, int type, out IntPtr dup);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool LookupPrivilegeValueW(string sys, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool AdjustTokenPrivileges(IntPtr t, bool dis, ref TP newState, int len, IntPtr prev, IntPtr retLen);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CreateProcessAsUserW(IntPtr token, string app, string cmd, IntPtr pa, IntPtr ta,
        bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [StructLayout(LayoutKind.Sequential)]
    struct TP { public int Count; public long Luid; public int Attributes; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct STARTUPINFO {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    const uint PROCESS_QUERY_INFORMATION = 0x0400;
    const uint TOKEN_QUERY = 0x0008, TOKEN_DUPLICATE = 0x0002, TOKEN_ASSIGN_PRIMARY = 0x0001;
    const int SecurityImpersonation = 2, TokenPrimary = 1;
    const uint MAXIMUM_ALLOWED = 0x02000000;
    const uint CREATE_NEW_CONSOLE = 0x00000010;
    const int SE_PRIVILEGE_ENABLED = 0x00000002;

    static bool Enable(string name)
    {
        IntPtr tok;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | 0x0020, out tok)) return false;
        try
        {
            long luid;
            if (!LookupPrivilegeValueW(null, name, out luid)) return false;
            var tp = new TP { Count = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            return AdjustTokenPrivileges(tok, false, ref tp, Marshal.SizeOf(typeof(TP)), IntPtr.Zero, IntPtr.Zero)
                   && Marshal.GetLastWin32Error() == 0;
        }
        finally { CloseHandle(tok); }
    }

    public static int Launch(uint explorerPid, string exe, string args, bool wait, out string error)
    {
        error = "";
        IntPtr hp = IntPtr.Zero, tok = IntPtr.Zero, dup = IntPtr.Zero;
        try
        {
            Enable("SeAssignPrimaryTokenPrivilege");
            Enable("SeIncreaseQuotaPrivilege");

            hp = OpenProcess(PROCESS_QUERY_INFORMATION, false, explorerPid);
            if (hp == IntPtr.Zero) { error = "OpenProcess(explorer) err=" + Marshal.GetLastWin32Error(); return -1; }

            if (!OpenProcessToken(hp, TOKEN_QUERY | TOKEN_DUPLICATE, out tok))
            { error = "OpenProcessToken err=" + Marshal.GetLastWin32Error(); return -1; }

            if (!DuplicateTokenEx(tok, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out dup))
            { error = "DuplicateTokenEx err=" + Marshal.GetLastWin32Error(); return -1; }

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            si.lpDesktop = @"winsta0\default";

            PROCESS_INFORMATION pi;
            string cmd = string.IsNullOrEmpty(args) ? "\"" + exe + "\"" : "\"" + exe + "\" " + args;
            if (!CreateProcessAsUserW(dup, null, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_NEW_CONSOLE,
                                      IntPtr.Zero, System.IO.Path.GetDirectoryName(exe), ref si, out pi))
            { error = "CreateProcessAsUser err=" + Marshal.GetLastWin32Error(); return -1; }

            CloseHandle(pi.hThread);
            if (wait)
            {
                WaitForSingleObject(pi.hProcess, 120000);
                uint code;
                GetExitCodeProcess(pi.hProcess, out code);
                CloseHandle(pi.hProcess);
                return (int)code;
            }
            CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }
        finally
        {
            if (dup != IntPtr.Zero) CloseHandle(dup);
            if (tok != IntPtr.Zero) CloseHandle(tok);
            if (hp != IntPtr.Zero) CloseHandle(hp);
        }
    }

    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
}
'@ -Language CSharp

if ($ExplorerPid -le 0) {
    $ExplorerPid = (Get-Process explorer | Sort-Object StartTime -Descending | Select-Object -First 1).Id
}
if ($ExplorerPid -le 0) { Write-Output '找不到 explorer.exe'; exit 1 }

$exePath = (Resolve-Path $Exe).Path
$err = ''
$result = [HighLauncher]::Launch([uint32]$ExplorerPid, $exePath, $Arguments, [bool]$Wait, [ref]$err)
if ($err) { Write-Output "启动失败: $err"; exit 1 }

if ($Wait) { Write-Output "退出码: $result" }
else { Write-Output "已借 explorer(pid=$ExplorerPid) 令牌启动，pid=$result" }
