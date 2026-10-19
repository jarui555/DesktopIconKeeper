using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 权限自愈：本工具必须跨进程读写 explorer.exe 的内存，
    /// 所以本进程完整性级别 ≥ explorer 完整性级别是硬前提。
    ///
    /// 现实中这条前提会以两种方式被破坏：
    ///   1) 本工具被沙箱 / AppContainer / 受限令牌以 Low 完整性启动，而 explorer 是 High；
    ///   2) 用户以普通权限运行，而 explorer 恰好被提升过。
    /// 两种情况都表现为 OpenProcess 返回 ERROR_ACCESS_DENIED(5)，
    /// 一个没有上下文的错误码 5对用户毫无帮助，所以这里负责把它翻译成可执行的动作。
    /// </summary>
    public static class Elevation
    {
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_INVALID_PARAMETER = 87;
        public const int ERROR_PARTIAL_COPY = 299;

        // ---- ShellExecuteEx ----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public string lpVerb;
            public string lpFile;
            public string lpParameters;
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
        private const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
        private const int SW_SHOWNORMAL = 1;

        // ---- SeDebugPrivilege ----
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string system, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
            ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public long Luid;
            public int Attributes;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const int SE_PRIVILEGE_ENABLED = 0x00000002;

        /// <summary>
        /// 尝试启用 SeDebugPrivilege。管理员令牌里这个特权默认是存在但禁用的，
        /// 显式启用后可以打开原本被拒绝的进程句柄。
        /// </summary>
        public static bool TryEnableDebugPrivilege()
        {
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle,
                        TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                    return false;

                long luid;
                if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out luid)) return false;

                var tp = new TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Luid = luid;
                tp.Attributes = SE_PRIVILEGE_ENABLED;

                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)) return false;

                // AdjustTokenPrivileges 即使成功返回也可能一个特权都没改，必须查 LastError
                return Marshal.GetLastWin32Error() == 0;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (token != IntPtr.Zero) Native.CloseHandle(token);
            }
        }

        /// <summary>UAC 是否开启。关闭时 runas 动词会退化成普通启动，提权等于空操作。</summary>
        public static bool UacEnabled
        {
            get
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"))
                    {
                        if (k == null) return true;
                        object v = k.GetValue("EnableLUA");
                        if (v == null) return true;
                        return Convert.ToInt32(v) != 0;
                    }
                }
                catch (Exception)
                {
                    return true;   // 读不到就按能提权处理，让 RestartElevated 自己去失败
                }
            }
        }

        /// <summary>
        /// 提权重启是否值得一试。
        /// 两个否条件：
        ///   已经是高完整性 —— 再提也不会更高；
        ///   UAC 关闭 —— runas 不生效，只会让窗口白闪一次。
        /// </summary>
        public static bool ElevationWorthTrying
        {
            get
            {
                int rid = Integrity.SelfRid;
                if (rid >= Integrity.High) return false;
                return UacEnabled;
            }
        }

        /// <summary>
        /// 在资源管理器里选中本 exe。用户双击就能以正常令牌启动，
        /// 这是绕开沙箱降权最省事的一条路。
        /// </summary>
        public static bool OpenInExplorer()
        {
            string path = Application.ExecutablePath;
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.Arguments = "/select,\"" + path + "\"";
                psi.UseShellExecute = true;
                Process.Start(psi);
                return true;
            }
            catch (Exception)
            {
                try
                {
                    Process.Start("explorer.exe", "\"" + System.IO.Path.GetDirectoryName(path) + "\"");
                    return true;
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>用 runas 动词重启自己（会触发 UAC 提示）。返回是否成功发起。</summary>
        public static bool RestartElevated(string arguments)
        {
            try
            {
                var sei = new SHELLEXECUTEINFO();
                sei.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
                sei.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_FLAG_NO_UI;
                sei.lpVerb = "runas";
                sei.lpFile = Application.ExecutablePath;
                sei.lpParameters = arguments;
                sei.lpDirectory = AppDomain.CurrentDomain.BaseDirectory;
                sei.nShow = SW_SHOWNORMAL;

                if (!ShellExecuteEx(ref sei)) return false;

                if (sei.hProcess != IntPtr.Zero) Native.CloseHandle(sei.hProcess);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 托盘图标能不能注册成功。
        ///
        /// Shell_NotifyIcon 也是往 explorer 的 Shell_TrayWnd 发消息，同样受 UIPI 管。
        /// 低完整性下它会**静默失败** —— NotifyIcon.Visible 设成 true 也不报错，
        /// 托盘里就是没有图标。这时候如果还把主窗口藏起来，用户就彻底失去入口了。
        /// </summary>
        public static bool TrayReachable()
        {
            try
            {
                IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero) return false;

                uint pid;
                Native.GetWindowThreadProcessId(tray, out pid);
                if (pid == 0) return false;

                int selfRid = Integrity.SelfRid;
                int trayRid = Integrity.GetProcessRid(pid);
                if (selfRid < 0 || trayRid < 0) return true;   // 读不到就别拦着
                return selfRid >= trayRid;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// UIPI（User Interface Privilege Isolation）拦截的说明。
        ///
        /// 这是比错误码 5更阴险的一种失败：进程句柄能拿到、内存能读写，
        /// 但发往高完整性窗口的 LVM_* 消息会被静默丢弃，SendMessage 一律返回 0。
        /// 结果就是桌面看起来有 0 个图标，用户点记录会存下一个空快照还以为成功了。
        /// </summary>
        public static string ExplainUipi(uint explorerPid, int selfRid, int expRid)
        {
            var sb = new StringBuilder();
            sb.Append(Lang.T("无法读写桌面图标：本工具 ")).Append(Integrity.NameOf(selfRid))
              .Append(" (0x").Append(selfRid.ToString("X4", CultureInfo.InvariantCulture)).Append(")，")
              .Append("explorer.exe (pid ").Append(explorerPid).Append(") ")
              .Append(Integrity.NameOf(expRid))
              .Append(" (0x").Append(expRid.ToString("X4", CultureInfo.InvariantCulture)).Append(")。");

            sb.Append(Lang.T("\n\n进程句柄能打开，但 Windows 的 UIPI 会把本工具发往 explorer 的窗口消息"));
            sb.Append(Lang.T("\n（LVM_GETITEMCOUNT / LVM_SETITEMPOSITION 等）静默丢弃 —— 全部返回 0。"));
            sb.Append(Lang.T("\n继续下去只会得到\"桌面有 0 个图标\"的假结果，甚至存下一个空快照，"));
            sb.Append(Lang.T("\n所以这里直接停下，不做任何读写。"));

            sb.Append(Lang.T("\n\n解决办法（按可靠程度排序）："));
            sb.Append(Lang.T("\n  1. 在资源管理器里直接双击 exe —— explorer 会用自己的高权限令牌启动它；"));
            sb.Append(Lang.T("\n  2. 右键 exe → 以管理员身份运行；"));
            if (UacEnabled)
                sb.Append(Lang.T("\n  3. 点下面的「以管理员身份重启」。"));
            else
                sb.Append(Lang.T("\n  3.（本机 UAC 已关闭，runas 提权不会生效，请用上面两条。）"));

            return sb.ToString();
        }

        /// <summary>
        /// 把错误码 5翻译成带完整性级别对比的可执行建议。
        /// </summary>
        public static string Explain(uint explorerPid, int win32Error)
        {
            var sb = new StringBuilder();

            int selfRid = Integrity.SelfRid;
            int expRid = Integrity.GetProcessRid(explorerPid);

            sb.Append(Lang.T("无法打开 explorer.exe (pid ")).Append(explorerPid)
              .Append(Lang.T(") 的进程句柄，Win32 错误码 ")).Append(win32Error).Append("。");

            if (selfRid >= 0 && expRid >= 0)
            {
                sb.Append(Lang.T("\n\n完整性级别：本工具 ")).Append(Integrity.NameOf(selfRid))
                  .Append(" (0x").Append(selfRid.ToString("X4")).Append(")")
                  .Append("，explorer ").Append(Integrity.NameOf(expRid))
                  .Append(" (0x").Append(expRid.ToString("X4")).Append(")。");

                if (selfRid < expRid)
                {
                    sb.Append(Lang.T("\n\n本工具运行在比 explorer 更低的权限里，无法读写它的内存。"));
                    sb.Append(Lang.T("\n常见原因：从沙箱 / 受限环境启动，或 UAC 提权后 explorer 被提升。"));

                    sb.Append(Lang.T("\n\n解决办法（按可靠程度排序）："));
                    sb.Append(Lang.T("\n  1. 在资源管理器里直接双击 exe —— 不要在沙箱 / 受限面板里启动；"));
                    sb.Append(Lang.T("\n  2. 右键 exe → 以管理员身份运行；"));

                    if (UacEnabled)
                        sb.Append(Lang.T("\n  3. 点下面的「以管理员身份重启」。"));
                    else
                        sb.Append(Lang.T("\n  3.（本机 UAC 已关闭，runas 提权不会生效，请用上面两条。）"));
                }
                else
                {
                    sb.Append(Lang.T("\n\n两边级别看起来够用，仍被拒绝多半是安全软件拦截。"));
                    sb.Append(Lang.T("\n可尝试：以管理员身份重启，或把本工具加入杀软白名单。"));
                }
            }
            else
            {
                sb.Append(Lang.T("\n\n无法读取完整性级别（自身 ")).Append(selfRid).Append("，explorer ").Append(expRid).Append("）。");
                sb.Append(Lang.T("\n请右键本工具选择“以管理员身份运行”。"));
            }

            if (win32Error == ERROR_ACCESS_DENIED && Integrity.SelfIsLow)
            {
                sb.Append(Lang.T("\n\n检测到本进程处于低完整性（沙箱）。这是当前故障的直接原因。"));
            }

            return sb.ToString();
        }
    }
}
