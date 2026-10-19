using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopIconKeeper
{
    /// <summary>程序图标。优先取 exe 自身嵌入的图标，失败退回系统默认。</summary>
    public static class AppIcon
    {
        private static Icon _cached;

        public static Icon Get()
        {
            if (_cached != null) return _cached;
            try
            {
                _cached = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception) { }

            if (_cached == null)
            {
                try { _cached = Icon.ExtractAssociatedIcon(typeof(AppIcon).Assembly.Location); }
                catch (Exception) { }
            }

            if (_cached == null) _cached = SystemIcons.Application;
            return _cached;
        }
    }
}
