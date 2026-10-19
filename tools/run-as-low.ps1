param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [string]$Arguments = '',
    [int]$Rid = 0x1000,          # 0x1000=Low, 0x2000=Medium, 0x0000=Untrusted
    [switch]$Wait
)

$ErrorActionPreference = 'Stop'

# 复现"沙箱 / 受限令牌"场景：复制当前进程令牌，把完整性级别改成 Low，
# 再用它启动目标进程。用于验证 DesktopIconKeeper 的权限自愈路径。
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public class LowLauncher
{
    [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr p, uint a, out IntPtr t);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool DuplicateTokenEx(IntPtr t, uint a, IntPtr sa, int lvl, int type, out IntPtr dup);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool SetTokenInformation(IntPtr t, int cls, IntPtr info, int len);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool AllocateAndInitializeSid(byte[] auth, byte count, int s1, int s2, int s3, int s4, int s5, int s6, int s7, int s8, out IntPtr sid);
    [DllImport("advapi32.dll")] static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern bool CreateProcessWithTokenW(IntPtr token, uint flags, string app, string cmd, uint creationFlags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct STARTUPINFO {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    const uint TOKEN_QUERY = 0x0008, TOKEN_DUPLICATE = 0x0002, TOKEN_ADJUST_DEFAULT = 0x0080;
    const int TokenIntegrityLevel = 25;
    const int SecurityImpersonation = 2, TokenPrimary = 1;
    const uint MAXIMUM_ALLOWED = 0x02000000;
    const uint CREATE_NEW_CONSOLE = 0x00000010;
    const uint SE_GROUP_INTEGRITY = 0x00000020;

    public static int Launch(string exe, string args, int rid, bool wait, out string error)
    {
        error = "";
        IntPtr tok = IntPtr.Zero, dup = IntPtr.Zero, sid = IntPtr.Zero, label = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ADJUST_DEFAULT, out tok))
            { error = "OpenProcessToken err=" + Marshal.GetLastWin32Error(); return -1; }

            if (!DuplicateTokenEx(tok, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out dup))
            { error = "DuplicateTokenEx err=" + Marshal.GetLastWin32Error(); return -1; }

            // S-1-16-<rid>
            byte[] auth = new byte[] { 0,0,0,0,0,16 };   // SECURITY_MANDATORY_LABEL_AUTHORITY
            if (!AllocateAndInitializeSid(auth, 1, rid, 0,0,0,0,0,0,0, out sid))
            { error = "AllocateAndInitializeSid err=" + Marshal.GetLastWin32Error(); return -1; }

            // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES { PSID; DWORD Attributes; } }
            // x64 下结构体带 4 字节对齐填充 = 16 字节；SetTokenInformation 要的是结构体大小
            int labelSize = IntPtr.Size == 8 ? 16 : 8;
            label = Marshal.AllocHGlobal(labelSize);
            Marshal.WriteIntPtr(label, 0, sid);
            Marshal.WriteInt32(label, IntPtr.Size, (int)SE_GROUP_INTEGRITY);

            if (!SetTokenInformation(dup, TokenIntegrityLevel, label, labelSize))
            { error = "SetTokenInformation err=" + Marshal.GetLastWin32Error(); return -1; }

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            si.lpDesktop = @"winsta0\default";

            PROCESS_INFORMATION pi;
            string cmd = string.IsNullOrEmpty(args) ? "\"" + exe + "\"" : "\"" + exe + "\" " + args;
            if (!CreateProcessWithTokenW(dup, 0, null, cmd, CREATE_NEW_CONSOLE, IntPtr.Zero,
                                         System.IO.Path.GetDirectoryName(exe), ref si, out pi))
            { error = "CreateProcessWithTokenW err=" + Marshal.GetLastWin32Error(); return -1; }

            CloseHandle(pi.hThread);
            if (wait)
            {
                WaitForSingleObject(pi.hProcess, 60000);
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
            if (label != IntPtr.Zero) Marshal.FreeHGlobal(label);
            if (sid != IntPtr.Zero) FreeSid(sid);
            if (dup != IntPtr.Zero) CloseHandle(dup);
            if (tok != IntPtr.Zero) CloseHandle(tok);
        }
    }

    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
}
'@ -Language CSharp

$exePath = (Resolve-Path $Exe).Path
$err = ''
$result = [LowLauncher]::Launch($exePath, $Arguments, $Rid, [bool]$Wait, [ref]$err)
if ($err) { Write-Output "启动失败: $err"; exit 1 }

if ($Wait) { Write-Output "退出码: $result" } else { Write-Output "已以 RID=0x$($Rid.ToString('X4')) 启动，pid=$result" }
