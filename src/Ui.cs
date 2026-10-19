using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace DesktopIconKeeper
{
    /// <summary>输入框 / 提示框 / 热键字符串解析。</summary>
    public static class Ui
    {
        public static string Input(IWin32Window owner, string title, string prompt, string defaultValue)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.ClientSize = new Size(420, 150);
                f.Font = new Font("Microsoft YaHei UI", 9f);

                var lbl = new Label();
                lbl.Text = prompt;
                lbl.SetBounds(16, 16, 388, 40);
                f.Controls.Add(lbl);

                var tb = new TextBox();
                tb.Text = defaultValue ?? "";
                tb.SetBounds(16, 58, 388, 26);
                tb.SelectAll();
                f.Controls.Add(tb);

                var ok = new Button();
                ok.Text = Lang.T("确定");
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(228, 100, 84, 30);
                f.Controls.Add(ok);

                var cancel = new Button();
                cancel.Text = Lang.T("取消");
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(320, 100, 84, 30);
                f.Controls.Add(cancel);

                f.AcceptButton = ok;
                f.CancelButton = cancel;

                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                return tb.Text.Trim();
            }
        }

        public static void Info(IWin32Window owner, string text, string title)
        {
            MessageBox.Show(owner, text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Warn(IWin32Window owner, string text, string title)
        {
            MessageBox.Show(owner, text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public static bool Confirm(IWin32Window owner, string text, string title)
        {
            return MessageBox.Show(owner, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        // =====================================================================
        // 热键
        // =====================================================================

        public sealed class HotkeySpec
        {
            public uint Modifiers;
            public uint VirtualKey;
            public bool Valid;

            public override string ToString()
            {
                if (!Valid) return Lang.T("(未设置)");
                var sb = new StringBuilder();
                if ((Modifiers & Native.MOD_CONTROL) != 0) sb.Append("Ctrl+");
                if ((Modifiers & Native.MOD_ALT) != 0) sb.Append("Alt+");
                if ((Modifiers & Native.MOD_SHIFT) != 0) sb.Append("Shift+");
                if ((Modifiers & Native.MOD_WIN) != 0) sb.Append("Win+");
                sb.Append(KeyName(VirtualKey));
                return sb.ToString();
            }
        }

        public static HotkeySpec ParseHotkey(string text)
        {
            var spec = new HotkeySpec();
            if (string.IsNullOrEmpty(text)) return spec;

            string[] parts = text.Split('+');
            string key = "";
            foreach (string raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                string low = p.ToLowerInvariant();
                if (low == "ctrl" || low == "control") spec.Modifiers |= Native.MOD_CONTROL;
                else if (low == "alt") spec.Modifiers |= Native.MOD_ALT;
                else if (low == "shift") spec.Modifiers |= Native.MOD_SHIFT;
                else if (low == "win") spec.Modifiers |= Native.MOD_WIN;
                else key = p;
            }

            if (key.Length == 0) return spec;
            if (key.Length == 1)
            {
                char c = char.ToUpperInvariant(key[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    spec.VirtualKey = (uint)c;
                    spec.Valid = spec.Modifiers != 0;
                    return spec;
                }
            }

            string k = key.ToUpperInvariant();
            if (k.StartsWith("F") && k.Length <= 3)
            {
                int n;
                if (int.TryParse(k.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= 24)
                {
                    spec.VirtualKey = (uint)(0x70 + n - 1);   // VK_F1
                    spec.Valid = spec.Modifiers != 0;
                    return spec;
                }
            }

            switch (k)
            {
                case "SPACE": spec.VirtualKey = 0x20; break;
                case "TAB": spec.VirtualKey = 0x09; break;
                case "ENTER": case "RETURN": spec.VirtualKey = 0x0D; break;
                case "HOME": spec.VirtualKey = 0x24; break;
                case "END": spec.VirtualKey = 0x23; break;
                case "INSERT": spec.VirtualKey = 0x2D; break;
                case "DELETE": case "DEL": spec.VirtualKey = 0x2E; break;
                default: return spec;
            }
            spec.Valid = spec.Modifiers != 0;
            return spec;
        }

        private static string KeyName(uint vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1).ToString(CultureInfo.InvariantCulture);
            switch (vk)
            {
                case 0x20: return "Space";
                case 0x09: return "Tab";
                case 0x0D: return "Enter";
                case 0x24: return "Home";
                case 0x23: return "End";
                case 0x2D: return "Insert";
                case 0x2E: return "Delete";
            }
            return "VK" + vk.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
