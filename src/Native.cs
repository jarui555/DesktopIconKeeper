using System;
using System.Runtime.InteropServices;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 全部 P/Invoke 集中在此。所有跨进程读写都走 explorer.exe 内部的中转缓冲。
    /// </summary>
    internal static class Native
    {
        public const string CLASS_PROGMAN = "Progman";
        public const string CLASS_DEFVIEW = "SHELLDLL_DefView";
        public const string CLASS_LISTVIEW = "SysListView32";

        // ---- ListView 消息 ----
        public const uint LVM_FIRST = 0x1000;
        public const uint LVM_GETITEMCOUNT = LVM_FIRST + 4;      // 0x1004
        public const uint LVM_SETITEMPOSITION = LVM_FIRST + 15;  // 0x100F
        public const uint LVM_GETITEMPOSITION = LVM_FIRST + 16;  // 0x1010
        public const uint LVM_REDRAWITEMS = LVM_FIRST + 21;      // 0x1015
        public const uint LVM_GETITEMSPACING = LVM_FIRST + 51;   // 0x1033
        public const uint LVM_GETITEMTEXTW = LVM_FIRST + 115;    // 0x1073

        public const uint LVIF_TEXT = 0x0001;

        // ---- 窗口样式 ----
        public const int GWL_STYLE = -16;
        public const int LVS_AUTOARRANGE = 0x0100;

        // ---- 窗口消息 ----
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_SETTINGCHANGE = 0x001A;

        // ---- 进程权限 ----
        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint PROCESS_VM_READ = 0x0010;
        public const uint PROCESS_VM_WRITE = 0x0020;
        public const uint PROCESS_QUERY_INFORMATION = 0x0400;

        // ---- 内存分配 ----
        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_RESERVE = 0x2000;
        public const uint MEM_RELEASE = 0x8000;
        public const uint PAGE_READWRITE = 0x0004;

        // ---- 系统度量 ----
        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;

        // ---- 热键修饰符 ----
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // =====================================================================
        // 结构体
        // =====================================================================

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
            public POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        /// <summary>
        /// LVITEMW，x64 下 88 字节。pszText 指向目标进程内的缓冲区。
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct LVITEMW
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

        // =====================================================================
        // user32
        // =====================================================================

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW", SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        /// <summary>广播到本会话所有顶层窗口。</summary>
        public static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int SW_SHOWNA = 8;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // =====================================================================
        // kernel32
        // =====================================================================

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentProcessId();

        /// <summary>
        /// 依次尝试多种访问掩码。explorer 被加固或本进程被降权时，
        /// 少要几个权限往往就能过 —— 我们只需要读/写目标内存，不需要查询信息。
        /// </summary>
        public static IntPtr OpenProcessAny(uint pid, out int lastError, out uint usedMask)
        {
            uint[] masks = new uint[]
            {
                PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION,
                PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE,
                PROCESS_VM_READ | PROCESS_VM_WRITE,
            };

            lastError = 0;
            usedMask = 0;
            foreach (uint m in masks)
            {
                IntPtr h = OpenProcess(m, false, pid);
                if (h != IntPtr.Zero)
                {
                    usedMask = m;
                    return h;
                }
                lastError = Marshal.GetLastWin32Error();
            }
            return IntPtr.Zero;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        public static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        public static extern bool FreeConsole();

        public const int ATTACH_PARENT_PROCESS = -1;

        // =====================================================================
        // shcore（Win8.1+，缺失时静默降级）
        // =====================================================================

        [DllImport("shcore.dll", SetLastError = true)]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        public const int MDT_EFFECTIVE_DPI = 0;

        public static int SafeGetDpiForMonitor(IntPtr hMonitor)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out x, out y) == 0 && x > 0)
                    return (int)x;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            catch (Exception) { }
            return 96;
        }
    }

    /// <summary>
    /// 完整性级别（Mandatory Integrity Control）工具。
    ///
    /// 低完整性进程无法 OpenProcess 一个高完整性进程，会拿到 ERROR_ACCESS_DENIED(5)。
    /// 典型场景：沙箱 / AppContainer / 受限令牌里启动本工具，而 explorer 跑在 High。
    /// 本工具需要跨进程读写 explorer 的内存，所以必须能把自己的级别报出来，
    /// 否则用户只能看到一个没有上下文的错误码 5。
    /// </summary>
    public static class Integrity
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint TOKEN_QUERY = 0x0008;
        private const int TokenIntegrityLevel = 25;

        public const int Untrusted = 0x0000;
        public const int Low = 0x1000;
        public const int Medium = 0x2000;
        public const int MediumPlus = 0x2100;
        public const int High = 0x3000;
        public const int System = 0x4000;
        public const int Protected = 0x5000;

        public static string NameOf(int rid)
        {
            if (rid >= Protected) return "Protected";
            if (rid >= System) return "System";
            if (rid >= High) return "High";
            if (rid >= MediumPlus) return "Medium+";
            if (rid >= Medium) return "Medium";
            if (rid >= Low) return "Low";
            return "Untrusted";
        }

        /// <summary>返回目标进程的完整性 RID；失败返回 -1。</summary>
        public static int GetProcessRid(uint pid)
        {
            IntPtr h = Native.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return -1;

            IntPtr token = IntPtr.Zero;
            IntPtr buf = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(h, TOKEN_QUERY, out token)) return -1;

                int size;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out size);
                if (size <= 0) return -1;

                buf = Marshal.AllocHGlobal(size);
                int got;
                if (!GetTokenInformation(token, TokenIntegrityLevel, buf, size, out got)) return -1;

                // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes; } }
                // RID 是 SID 的最后一个子认证值：SID 头 8 字节 + 4*(n-1)
                IntPtr sid = Marshal.ReadIntPtr(buf, 0);
                if (sid == IntPtr.Zero) return -1;
                int subCount = Marshal.ReadByte(sid, 1);
                if (subCount < 1) return -1;
                return Marshal.ReadInt32(sid, 8 + 4 * (subCount - 1));
            }
            catch (Exception)
            {
                return -1;
            }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                if (token != IntPtr.Zero) Native.CloseHandle(token);
                Native.CloseHandle(h);
            }
        }

        /// <summary>本进程的完整性 RID。</summary>
        public static int SelfRid
        {
            get { return GetProcessRid(Native.GetCurrentProcessId()); }
        }

        public static bool SelfIsLow
        {
            get
            {
                int rid = SelfRid;
                return rid >= 0 && rid < Medium;
            }
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
                                                       IntPtr tokenInformation, int tokenInformationLength,
                                                       out int returnLength);
    }
}
