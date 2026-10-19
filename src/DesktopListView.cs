using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 桌面图标 ListView 的跨进程访问器。
    ///
    /// 句柄链：Progman -> SHELLDLL_DefView -> SysListView32
    /// 部分 Windows 版本桌面 ListView 挂在 WorkerW 下，所以带 EnumWindows 兜底。
    ///
    /// 跨进程约束：LVM_GETITEMPOSITION / LVM_GETITEMTEXTW 的指针参数必须是 explorer.exe
    /// 地址空间内的地址，因此用 VirtualAllocEx 在目标进程里开一块中转缓冲。
    /// </summary>
    public sealed class DesktopListView : IDisposable
    {
        private const int RemoteSize = 8192;
        private const int TextOffset = 1024;   // 文本缓冲起点
        private const int TextChars = 260;     // 单个图标名上限（MAX_PATH）

        public IntPtr Handle { get; private set; }
        public int ProcessId { get; private set; }

        private IntPtr _proc = IntPtr.Zero;
        private IntPtr _remote = IntPtr.Zero;
        private bool _disposed;

        /// <summary>最近一次打开失败的原因，供 UI 直接展示。</summary>
        public static string LastOpenError = "";

        /// <summary>最近一次失败是否为权限不足（决定 UI 要不要给以管理员身份重启按钮）。</summary>
        public static bool LastAccessDenied;

        /// <summary>explorer.exe 的进程号，权限诊断用。</summary>
        public static uint LastExplorerPid;

        private DesktopListView() { }

        // =====================================================================
        // 打开 / 释放
        // =====================================================================

        public static DesktopListView Open()
        {
            LastOpenError = "";
            LastAccessDenied = false;

            IntPtr lv = FindDesktopListView(out LastOpenError);
            if (lv == IntPtr.Zero) return null;

            var o = new DesktopListView();
            o.Handle = lv;

            uint pid;
            Native.GetWindowThreadProcessId(lv, out pid);
            o.ProcessId = (int)pid;
            LastExplorerPid = pid;

            if (pid == 0)
            {
                LastOpenError = Lang.T("无法取得桌面 ListView 所属进程。");
                return null;
            }

            int openErr;
            uint usedMask;
            o._proc = Native.OpenProcessAny(pid, out openErr, out usedMask);

            // 少要几个权限还不行？试着启用 SeDebugPrivilege 再要一次。
            if (o._proc == IntPtr.Zero && openErr == Elevation.ERROR_ACCESS_DENIED &&
                Elevation.TryEnableDebugPrivilege())
            {
                o._proc = Native.OpenProcessAny(pid, out openErr, out usedMask);
            }

            if (o._proc == IntPtr.Zero)
            {
                LastOpenError = Elevation.Explain(pid, openErr);
                LastAccessDenied = (openErr == Elevation.ERROR_ACCESS_DENIED);
                return null;
            }

            // 句柄拿到了不等于消息能通。UIPI 只看完整性级别：
            // 发送方低于窗口所属进程时，LVM_* 一律被静默丢弃、返回 0。
            // 有 SeDebugPrivilege 时 OpenProcess 照样成功，所以这一关必须单独查。
            int selfRid = Integrity.SelfRid;
            int expRid = Integrity.GetProcessRid(pid);
            if (selfRid >= 0 && expRid >= 0 && selfRid < expRid)
            {
                LastOpenError = Elevation.ExplainUipi(pid, selfRid, expRid);
                LastAccessDenied = true;
                o.Dispose();
                return null;
            }

            LastAccessDenied = false;

            o._remote = Native.VirtualAllocEx(o._proc, IntPtr.Zero, (IntPtr)RemoteSize,
                Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);

            if (o._remote == IntPtr.Zero)
            {
                LastOpenError = Lang.T("在 explorer.exe 内分配中转缓冲失败，Win32 错误码 ") + Marshal.GetLastWin32Error() + "。";
                o.Dispose();
                return null;
            }

            return o;
        }

        /// <summary>句柄仍有效且进程仍存活。</summary>
        public bool IsUsable
        {
            get
            {
                if (_disposed || Handle == IntPtr.Zero || _proc == IntPtr.Zero || _remote == IntPtr.Zero) return false;
                return Native.IsWindow(Handle);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_remote != IntPtr.Zero && _proc != IntPtr.Zero)
            {
                Native.VirtualFreeEx(_proc, _remote, IntPtr.Zero, Native.MEM_RELEASE);
                _remote = IntPtr.Zero;
            }
            if (_proc != IntPtr.Zero)
            {
                Native.CloseHandle(_proc);
                _proc = IntPtr.Zero;
            }
            Handle = IntPtr.Zero;
        }

        // =====================================================================
        // 句柄定位
        // =====================================================================

        public static IntPtr FindDesktopListView(out string error)
        {
            error = "";

            IntPtr progman = Native.FindWindow(Native.CLASS_PROGMAN, null);
            if (progman != IntPtr.Zero)
            {
                IntPtr defView = Native.FindWindowEx(progman, IntPtr.Zero, Native.CLASS_DEFVIEW, null);
                if (defView != IntPtr.Zero)
                {
                    IntPtr lv = Native.FindWindowEx(defView, IntPtr.Zero, Native.CLASS_LISTVIEW, null);
                    if (lv != IntPtr.Zero) return lv;
                }
            }

            // 兜底：桌面可能挂在某个 WorkerW 下
            IntPtr found = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                IntPtr defView = Native.FindWindowEx(hWnd, IntPtr.Zero, Native.CLASS_DEFVIEW, null);
                if (defView == IntPtr.Zero) return true;
                IntPtr lv = Native.FindWindowEx(defView, IntPtr.Zero, Native.CLASS_LISTVIEW, null);
                if (lv == IntPtr.Zero) return true;
                found = lv;
                return false;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero) return found;

            error = Lang.T("找不到桌面图标视图 (SysListView32)。可能原因：\n") +
                    Lang.T("  · 桌面图标已被隐藏（桌面右键 → 查看 → 显示桌面图标）\n") +
                    Lang.T("  · explorer.exe 未运行或正在重启\n") +
                    Lang.T("  · 桌面被 Fences 等第三方工具接管");
            return IntPtr.Zero;
        }

        // =====================================================================
        // 读取
        // =====================================================================

        public int GetItemCount()
        {
            if (!IsUsable) return -1;
            return (int)Native.SendMessage(Handle, Native.LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        public Point GetItemPosition(int index)
        {
            if (!IsUsable) return Point.Empty;
            Native.SendMessage(Handle, Native.LVM_GETITEMPOSITION, (IntPtr)index, _remote);
            byte[] buf = new byte[8];
            IntPtr read;
            if (!Native.ReadProcessMemory(_proc, _remote, buf, 8, out read)) return Point.Empty;
            return new Point(BitConverter.ToInt32(buf, 0), BitConverter.ToInt32(buf, 4));
        }

        public string GetItemText(int index)
        {
            if (!IsUsable) return "";

            var item = new Native.LVITEMW();
            item.mask = Native.LVIF_TEXT;
            item.iItem = index;
            item.iSubItem = 0;
            item.pszText = (IntPtr)(_remote.ToInt64() + TextOffset);
            item.cchTextMax = TextChars;

            int size = Marshal.SizeOf(typeof(Native.LVITEMW));
            byte[] raw = new byte[size];
            IntPtr tmp = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(item, tmp, false);
                Marshal.Copy(tmp, raw, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(tmp);
            }

            IntPtr written;
            if (!Native.WriteProcessMemory(_proc, _remote, raw, size, out written)) return "";

            Native.SendMessage(Handle, Native.LVM_GETITEMTEXTW, (IntPtr)index, _remote);

            byte[] text = new byte[TextChars * 2];
            IntPtr read;
            if (!Native.ReadProcessMemory(_proc, (IntPtr)(_remote.ToInt64() + TextOffset), text, text.Length, out read))
                return "";

            // 中转缓冲是复用的：短名字后面会残留上一次的长名字。
            // 必须在第一个 NUL 处截断，不能只 TrimEnd('\0')。
            string s = Encoding.Unicode.GetString(text);
            int z = s.IndexOf('\0');
            if (z >= 0) s = s.Substring(0, z);
            return s;
        }

        /// <summary>返回 (水平间距, 垂直间距)。</summary>
        public Size GetItemSpacing()
        {
            if (!IsUsable) return new Size(75, 98);
            int v = (int)Native.SendMessage(Handle, Native.LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero);
            int cx = v & 0xFFFF;
            int cy = (v >> 16) & 0xFFFF;
            if (cx <= 0) cx = 75;
            if (cy <= 0) cy = 98;
            return new Size(cx, cy);
        }

        // =====================================================================
        // 写入
        // =====================================================================

        /// <summary>写入图标坐标。LVM_SETITEMPOSITION 的坐标是 16 位，超范围会被钳制。</summary>
        public void SetItemPosition(int index, int x, int y)
        {
            if (!IsUsable) return;
            int cx = x < 0 ? 0 : (x > 32767 ? 32767 : x);
            int cy = y < 0 ? 0 : (y > 32767 ? 32767 : y);
            IntPtr lp = (IntPtr)((cy << 16) | (cx & 0xFFFF));
            Native.SendMessage(Handle, Native.LVM_SETITEMPOSITION, (IntPtr)index, lp);
        }

        // =====================================================================
        // 视图状态
        // =====================================================================

        /// <summary>桌面图标当前是否被隐藏。</summary>
        public bool IconsHidden
        {
            get { return IsUsable && !Native.IsWindowVisible(Handle); }
        }

        /// <summary>
        /// 隐藏 / 显示桌面图标。
        ///
        /// 等价于桌面右键 → 查看 → 显示桌面图标。只是把 SysListView32 的
        /// WS_VISIBLE 拿掉，不动任何文件，也不影响已保存的快照。
        /// </summary>
        public bool SetIconsHidden(bool hidden)
        {
            if (!IsUsable) return false;
            // 显示时用 SW_SHOWNA：别把焦点从用户正在用的窗口上抢走
            Native.ShowWindow(Handle, hidden ? Native.SW_HIDE : Native.SW_SHOWNA);
            return true;
        }

        public bool AutoArrange
        {
            get
            {
                if (!IsUsable) return false;
                int style = Native.GetWindowLong(Handle, Native.GWL_STYLE);
                return (style & Native.LVS_AUTOARRANGE) != 0;
            }
        }

        /// <summary>
        /// 关闭“自动排列图标”。自动排列开着时任何写入都会被系统立刻重排，还原必须先关掉它。
        /// </summary>
        public bool DisableAutoArrange()
        {
            if (!IsUsable) return false;
            int style = Native.GetWindowLong(Handle, Native.GWL_STYLE);
            if ((style & Native.LVS_AUTOARRANGE) == 0) return true;
            Native.SetWindowLong(Handle, Native.GWL_STYLE, style & ~Native.LVS_AUTOARRANGE);
            return !AutoArrange;
        }

        /// <summary>快照当前所有图标的名称 + 坐标，顺序即 ListView 顺序。</summary>
        public System.Collections.Generic.List<IconEntry> ReadAll()
        {
            var list = new System.Collections.Generic.List<IconEntry>();
            int count = GetItemCount();
            if (count <= 0) return list;

            var seen = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string name = GetItemText(i);
                if (string.IsNullOrEmpty(name)) name = Lang.T("(未知图标 #") + i.ToString(CultureInfo.InvariantCulture) + ")";
                Point p = GetItemPosition(i);

                int occ;
                if (!seen.TryGetValue(name, out occ)) occ = 0;
                seen[name] = occ + 1;

                var e = new IconEntry();
                e.Name = name;
                e.Occurrence = occ;
                e.X = p.X;
                e.Y = p.Y;
                list.Add(e);
            }
            return list;
        }

        /// <summary>只读坐标，用于稳定性检测与还原校验。</summary>
        public string PositionHash()
        {
            var sb = new StringBuilder();
            int count = GetItemCount();
            for (int i = 0; i < count; i++)
            {
                Point p = GetItemPosition(i);
                sb.Append(p.X).Append(',').Append(p.Y).Append(';');
            }
            return sb.ToString();
        }
    }
}
