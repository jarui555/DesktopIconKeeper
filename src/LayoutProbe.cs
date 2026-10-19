using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 显示器布局指纹。换分辨率 / 插拔显示器 / 改主屏都会让指纹变化，
    /// 快照按指纹归类，还原前先比对，避免把 A 布局的坐标糊到 B 布局上。
    /// </summary>
    public static class LayoutProbe
    {
        private const string BagsKey = @"Software\Microsoft\Windows\Shell\Bags\1\Desktop";

        // FOLDERFLAGS
        private const int FWF_AUTOARRANGE = 0x00000001;
        private const int FWF_SNAPTOGRID = 0x00000004;

        public static LayoutInfo Capture()
        {
            var li = new LayoutInfo();
            li.VX = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            li.VY = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            li.VW = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
            li.VH = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);

            var monitors = new List<MonitorInfo>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr hMon, IntPtr hdc, ref Native.RECT r, IntPtr data)
            {
                var mi = new Native.MONITORINFOEX();
                mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (Native.GetMonitorInfo(hMon, ref mi))
                {
                    var m = new MonitorInfo();
                    m.Device = mi.szDevice ?? "";
                    m.Primary = (mi.dwFlags & 1) != 0;   // MONITORINFOF_PRIMARY
                    m.X = mi.rcMonitor.Left;
                    m.Y = mi.rcMonitor.Top;
                    m.W = mi.rcMonitor.Right - mi.rcMonitor.Left;
                    m.H = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                    m.Dpi = Native.SafeGetDpiForMonitor(hMon);
                    monitors.Add(m);
                }
                return true;
            }, IntPtr.Zero);

            // 几何优先排序，保证指纹稳定（设备名插拔顺序变化时不影响）
            monitors.Sort(delegate(MonitorInfo a, MonitorInfo b)
            {
                int c = a.X.CompareTo(b.X);
                if (c != 0) return c;
                c = a.Y.CompareTo(b.Y);
                if (c != 0) return c;
                c = a.W.CompareTo(b.W);
                if (c != 0) return c;
                return a.H.CompareTo(b.H);
            });

            li.Monitors = monitors;
            li.Signature = ComputeSignature(li);
            return li;
        }

        private static string ComputeSignature(LayoutInfo li)
        {
            var sb = new StringBuilder();
            sb.Append("VS:").Append(li.VX).Append(',').Append(li.VY).Append(',')
              .Append(li.VW).Append(',').Append(li.VH).Append('|');
            for (int i = 0; i < li.Monitors.Count; i++)
            {
                var m = li.Monitors[i];
                sb.Append(m.X).Append(',').Append(m.Y).Append(',').Append(m.W).Append(',').Append(m.H)
                  .Append(',').Append(m.Dpi).Append(',').Append(m.Primary ? 1 : 0).Append('|');
            }

            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder();
                for (int i = 0; i < 10; i++) hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>读取桌面视图标志。自动排列从 ListView 样式读，对齐网格从 Bags 注册表读。</summary>
        public static ViewInfo CaptureView(DesktopListView lv)
        {
            var v = new ViewInfo();
            if (lv != null && lv.IsUsable)
            {
                Size sp = lv.GetItemSpacing();
                v.SpacingX = sp.Width;
                v.SpacingY = sp.Height;
                v.AutoArrange = lv.AutoArrange;
            }

            v.SnapToGrid = true;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(BagsKey, false))
                {
                    if (k != null)
                    {
                        object o = k.GetValue("FFlags");
                        if (o is int)
                        {
                            int flags = (int)o;
                            v.AutoArrange = (flags & FWF_AUTOARRANGE) != 0 || v.AutoArrange;
                            v.SnapToGrid = (flags & FWF_SNAPTOGRID) != 0;
                        }
                    }
                }
            }
            catch (Exception) { }

            return v;
        }

        /// <summary>把“自动排列关闭”写回注册表，让设置跨 explorer 重启保留。</summary>
        public static bool PersistAutoArrangeOff()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(BagsKey))
                {
                    if (k == null) return false;
                    object o = k.GetValue("FFlags");
                    int flags = (o is int) ? (int)o : 0;
                    flags &= ~FWF_AUTOARRANGE;
                    k.SetValue("FFlags", flags, RegistryValueKind.DWord);
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>显示器变化后的“布局已稳定”判定。</summary>
    public static class LayoutStabilizer
    {
        /// <summary>轮询坐标指纹，连续两次一致即认为 explorer 重排结束。</summary>
        public static bool WaitStable(DesktopListView lv, int timeoutMs, int pollMs)
        {
            if (lv == null || !lv.IsUsable) return false;
            string prev = null;
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                System.Threading.Thread.Sleep(pollMs);
                elapsed += pollMs;
                string cur = lv.PositionHash();
                if (prev != null && cur == prev) return true;
                prev = cur;
            }
            return false;
        }
    }
}
