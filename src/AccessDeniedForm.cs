using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 权限不足时给用户的出路选择器。
    ///
    /// 不用 MessageBox 的原因：按钮只能是是/否，而这里的两个动作
    /// （去资源管理器打开 / 提权重启）语义完全不同，且提权重启在
    /// UAC 关闭的机器上根本无效，不能混在一个是里。
    /// </summary>
    public sealed class AccessDeniedForm : Form
    {
        public bool OpenExplorerRequested { get; private set; }
        public bool ElevateRequested { get; private set; }

        public AccessDeniedForm(string detail, bool canElevate)
        {
            Text = Lang.T("权限不足 · 桌面图标位置记忆工具");
            Font = new Font("Microsoft YaHei UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 462);
            BackColor = Color.White;

            var txt = new TextBox();
            txt.Multiline = true;
            txt.ReadOnly = true;
            txt.ScrollBars = ScrollBars.Vertical;
            txt.BorderStyle = BorderStyle.FixedSingle;
            txt.BackColor = Color.FromArgb(250, 250, 250);
            txt.Font = new Font("Microsoft YaHei UI", 9f);
            txt.SetBounds(16, 16, 608, 376);
            txt.Text = (detail ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            Controls.Add(txt);

            const int y = 408;
            const int h = 34;

            var btnClose = new Button();
            btnClose.Text = Lang.T("关闭");
            btnClose.SetBounds(536, y, 88, h);
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);

            if (canElevate)
            {
                var btnElevate = new Button();
                btnElevate.Text = Lang.T("以管理员身份重启");
                btnElevate.SetBounds(384, y, 140, h);
                btnElevate.Click += delegate
                {
                    ElevateRequested = true;
                    DialogResult = DialogResult.OK;
                    Close();
                };
                Controls.Add(btnElevate);
            }

            var btnOpen = new Button();
            btnOpen.Text = Lang.T("在资源管理器中打开");
            btnOpen.SetBounds(canElevate ? 212 : 384, y, 160, h);
            btnOpen.Font = new Font(Font, FontStyle.Bold);
            btnOpen.Click += delegate
            {
                OpenExplorerRequested = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(btnOpen);

            AcceptButton = btnOpen;
            CancelButton = btnClose;
        }
    }
}
