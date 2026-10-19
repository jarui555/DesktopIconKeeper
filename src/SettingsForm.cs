using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace DesktopIconKeeper
{
    public sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly SnapshotStore _store;

        private CheckBox _chkAutoBackup, _chkAutoRestore, _chkStartupRestore, _chkScale, _chkPark,
                         _chkStartMin, _chkAutoStart, _chkBalloon, _chkAutoDisableArrange,
                         _chkAnimate, _chkShutdownBackup;
        private TextBox _tbHotkeySave, _tbHotkeyRestore, _tbIgnore;
        private ComboBox _cboLanguage;
        private Label _lblQq;
        private Label _lblDataDir;

        /// <summary>QQ 交流群。想改号只改这里一处。</summary>
        public const string QqGroup = "606140489";

        public SettingsForm(AppSettings settings, SnapshotStore store)
        {
            _settings = settings;
            _store = store;
            Build();
        }

        private void Build()
        {
            Text = Lang.T("设置");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 826);
            Font = new Font("Microsoft YaHei UI", 9f);

            int y = 16;

            var g1 = new GroupBox();
            g1.Text = Lang.T("自动行为");
            g1.SetBounds(16, y, 528, 178);
            Controls.Add(g1);

            _chkAutoBackup = MakeCheck(Lang.T("显示器布局变化时自动建立备份（每个布局各存一份）"), _settings.AutoBackupOnDisplayChange, 22, 24, 490);
            g1.Controls.Add(_chkAutoBackup);

            _chkAutoRestore = MakeCheck(Lang.T("切回已知布局时自动还原（优先用绑定该布局的快照）"), _settings.AutoRestoreOnDisplayChange, 22, 50, 490);
            g1.Controls.Add(_chkAutoRestore);

            _chkStartupRestore = MakeCheck(Lang.T("程序启动时自动还原最近一次手动快照"), _settings.RestoreOnStartup, 22, 76, 490);
            g1.Controls.Add(_chkStartupRestore);

            _chkAutoDisableArrange = MakeCheck(Lang.T("还原时自动关闭桌面「自动排列图标」"), _settings.AutoDisableAutoArrange, 22, 102, 490);
            g1.Controls.Add(_chkAutoDisableArrange);

            _chkShutdownBackup = MakeCheck(Lang.T("关机 / 注销前自动备份一次（图标乱序多半是关机时造成的）"), _settings.AutoBackupOnShutdown, 22, 128, 490);
            g1.Controls.Add(_chkShutdownBackup);

            y += 190;

            var g2 = new GroupBox();
            g2.Text = Lang.T("还原选项");
            g2.SetBounds(16, y, 528, 112);
            Controls.Add(g2);

            _chkScale = MakeCheck(Lang.T("布局指纹不一致时，按分辨率比例缩放坐标"), _settings.ScaleAcrossLayouts, 22, 24, 490);
            g2.Controls.Add(_chkScale);

            _chkPark = MakeCheck(Lang.T("把未记录的图标排到桌面右侧空位（否则保持原位）"), _settings.ParkUnmatchedIcons, 22, 50, 490);
            g2.Controls.Add(_chkPark);

            _chkAnimate = MakeCheck(Lang.T("还原时给图标加过渡动画（纯视觉，不影响最终坐标）"), _settings.AnimateRestore, 22, 76, 490);
            g2.Controls.Add(_chkAnimate);

            y += 124;

            var g3 = new GroupBox();
            g3.Text = Lang.T("全局热键");
            g3.SetBounds(16, y, 528, 88);
            Controls.Add(g3);

            var l1 = new Label();
            l1.Text = Lang.T("记录：");
            l1.SetBounds(22, 30, 48, 24);
            g3.Controls.Add(l1);

            _tbHotkeySave = new TextBox();
            _tbHotkeySave.Text = _settings.HotkeySave;
            _tbHotkeySave.SetBounds(72, 28, 140, 26);
            g3.Controls.Add(_tbHotkeySave);

            var l2 = new Label();
            l2.Text = Lang.T("还原：");
            // 62px 才放得下英文的 "Restore:"，48px 会折成 "Restor/e:"
            l2.SetBounds(222, 30, 62, 24);
            g3.Controls.Add(l2);

            _tbHotkeyRestore = new TextBox();
            _tbHotkeyRestore.Text = _settings.HotkeyRestore;
            _tbHotkeyRestore.SetBounds(286, 28, 140, 26);
            g3.Controls.Add(_tbHotkeyRestore);

            var hint = new Label();
            hint.ForeColor = Color.DimGray;
            hint.SetBounds(22, 58, 490, 22);
            hint.Text = Lang.T("格式示例：Ctrl+Alt+S、Ctrl+Shift+F5、Win+Alt+D。修改后立即生效。");
            g3.Controls.Add(hint);

            y += 100;

            var g4 = new GroupBox();
            g4.Text = Lang.T("启动与提示");
            g4.SetBounds(16, y, 528, 112);
            Controls.Add(g4);

            _chkAutoStart = MakeCheck(Lang.T("开机自动启动（最小化到托盘）"), _settings.AutoStartWithWindows, 22, 24, 490);
            g4.Controls.Add(_chkAutoStart);

            _chkStartMin = MakeCheck(Lang.T("启动后直接最小化到托盘"), _settings.StartMinimized, 22, 50, 490);
            g4.Controls.Add(_chkStartMin);

            _chkBalloon = MakeCheck(Lang.T("还原 / 自动备份时显示托盘气泡提示"), _settings.ShowBalloonTips, 22, 76, 490);
            g4.Controls.Add(_chkBalloon);

            y += 124;

            var g5 = new GroupBox();
            g5.Text = Lang.T("忽略列表（每行一个图标名，不参与记录与还原）");
            g5.SetBounds(16, y, 528, 96);
            Controls.Add(g5);

            _tbIgnore = new TextBox();
            _tbIgnore.Multiline = true;
            _tbIgnore.ScrollBars = ScrollBars.Vertical;
            _tbIgnore.SetBounds(22, 22, 484, 62);
            if (_settings.IgnoreNames != null) _tbIgnore.Lines = _settings.IgnoreNames.ToArray();
            g5.Controls.Add(_tbIgnore);

            y += 108;

            var g6 = new GroupBox();
            g6.Text = Lang.T("界面与语言");
            g6.SetBounds(16, y, 528, 76);
            Controls.Add(g6);

            var lLang = new Label();
            lLang.Text = Lang.T("语言：");
            // 66px 才放得下英文的 "Language:"，48px 会折成两行
            lLang.SetBounds(22, 30, 66, 24);
            g6.Controls.Add(lLang);

            _cboLanguage = new ComboBox();
            _cboLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboLanguage.SetBounds(92, 27, 158, 26);
            _cboLanguage.Items.Add(Lang.T("自动（跟随系统）"));
            _cboLanguage.Items.Add("简体中文");
            _cboLanguage.Items.Add("English");
            if (string.Equals(_settings.Language, Lang.EnUs, StringComparison.OrdinalIgnoreCase))
                _cboLanguage.SelectedIndex = 2;
            else if (string.Equals(_settings.Language, Lang.ZhCn, StringComparison.OrdinalIgnoreCase))
                _cboLanguage.SelectedIndex = 1;
            else
                _cboLanguage.SelectedIndex = 0;
            g6.Controls.Add(_cboLanguage);

            _lblQq = new Label();
            _lblQq.Text = Lang.T("QQ 交流群：") + QqGroup;
            _lblQq.SetBounds(262, 30, 250, 24);
            _lblQq.ForeColor = Color.FromArgb(0, 102, 204);
            _lblQq.Cursor = Cursors.Hand;
            _lblQq.Font = new Font(Font, FontStyle.Underline);
            _lblQq.Click += OnCopyQq;
            g6.Controls.Add(_lblQq);

            y += 88;

            _lblDataDir = new Label();
            _lblDataDir.SetBounds(16, y, 528, 20);
            _lblDataDir.ForeColor = Color.DimGray;
            _lblDataDir.Text = Lang.T("数据目录：") + _store.RootDir + (_store.Portable ? Lang.T("（绿色模式）") : "");
            Controls.Add(_lblDataDir);

            var ok = new Button();
            ok.Text = Lang.T("保存");
            ok.SetBounds(360, y + 30, 84, 30);
            ok.Click += OnOk;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = Lang.T("取消");
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(452, y + 30, 84, 30);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>点一下群号就复制到剪贴板，省得手抄。</summary>
        private void OnCopyQq(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(QqGroup);
                _lblQq.Text = Lang.T("已复制群号：") + QqGroup;
                var t = new Timer();
                t.Interval = 1600;
                t.Tick += delegate
                {
                    t.Stop();
                    t.Dispose();
                    try { _lblQq.Text = Lang.T("QQ 交流群：") + QqGroup; }
                    catch (Exception) { }
                };
                t.Start();
            }
            catch (Exception) { }
        }

        private static CheckBox MakeCheck(string text, bool value, int x, int y, int w)        {
            var c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.SetBounds(x, y, w, 24);
            return c;
        }

        private void OnOk(object sender, EventArgs e)
        {
            var hs = Ui.ParseHotkey(_tbHotkeySave.Text);
            var hr = Ui.ParseHotkey(_tbHotkeyRestore.Text);
            if (!hs.Valid || !hr.Valid)
            {
                Ui.Warn(this, Lang.T("热键格式不正确。请使用类似 Ctrl+Alt+S 的写法（必须包含至少一个修饰键）。"), Lang.T("热键无效"));
                return;
            }
            if (hs.Modifiers == hr.Modifiers && hs.VirtualKey == hr.VirtualKey)
            {
                Ui.Warn(this, Lang.T("两个热键不能相同。"), Lang.T("热键冲突"));
                return;
            }

            _settings.AutoBackupOnDisplayChange = _chkAutoBackup.Checked;
            _settings.AutoRestoreOnDisplayChange = _chkAutoRestore.Checked;
            _settings.RestoreOnStartup = _chkStartupRestore.Checked;
            _settings.AutoDisableAutoArrange = _chkAutoDisableArrange.Checked;
            _settings.ScaleAcrossLayouts = _chkScale.Checked;
            _settings.ParkUnmatchedIcons = _chkPark.Checked;
            _settings.HotkeySave = _tbHotkeySave.Text.Trim();
            _settings.HotkeyRestore = _tbHotkeyRestore.Text.Trim();
            _settings.StartMinimized = _chkStartMin.Checked;
            _settings.ShowBalloonTips = _chkBalloon.Checked;
            _settings.AnimateRestore = _chkAnimate.Checked;
            _settings.AutoBackupOnShutdown = _chkShutdownBackup.Checked;

            _settings.Language = _cboLanguage.SelectedIndex == 2 ? Lang.EnUs
                               : _cboLanguage.SelectedIndex == 1 ? Lang.ZhCn
                               : Lang.Auto;
            IconEngine.AnimateMoves = _settings.AnimateRestore;

            _settings.IgnoreNames = new System.Collections.Generic.List<string>();
            foreach (string line in _tbIgnore.Lines)
            {
                string t = line.Trim();
                if (t.Length > 0) _settings.IgnoreNames.Add(t);
            }

            bool autoStart = _chkAutoStart.Checked;
            if (autoStart != AutoStart.IsEnabled())
                AutoStart.Set(autoStart, Application.ExecutablePath);
            _settings.AutoStartWithWindows = autoStart;

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>两个布局的差异视图。</summary>
    public sealed class DiffForm : Form
    {
        public DiffForm(Snapshot a, Snapshot b, string labelA, string labelB)
        {
            Text = Lang.T("布局对比");
            ClientSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = AppIcon.Get();
            MinimumSize = new Size(560, 400);

            var list = new ListView();
            list.View = View.Details;
            list.FullRowSelect = true;
            list.GridLines = false;
            list.Dock = DockStyle.Fill;
            list.Columns.Add(Lang.T("图标"), 300);
            list.Columns.Add(Lang.T("状态"), 80);
            list.Columns.Add(labelA, 170);
            list.Columns.Add(labelB, 170);

            var diffs = IconEngine.Diff(a, b);
            int moved = 0, added = 0, gone = 0, same = 0;

            foreach (var d in diffs)
            {
                var it = new ListViewItem(d.Name);
                it.SubItems.Add(d.State);
                it.SubItems.Add(d.State == Lang.T("新增") ? "-" : "(" + d.From.X + ", " + d.From.Y + ")");
                it.SubItems.Add(d.State == Lang.T("缺失") ? "-" : "(" + d.To.X + ", " + d.To.Y + ")");

                if (d.State == Lang.T("移动")) { it.ForeColor = Color.FromArgb(200, 110, 0); moved++; }
                else if (d.State == Lang.T("新增")) { it.ForeColor = Color.FromArgb(0, 130, 60); added++; }
                else if (d.State == Lang.T("缺失")) { it.ForeColor = Color.FromArgb(190, 40, 40); gone++; }
                else { it.ForeColor = Color.Gray; same++; }

                list.Items.Add(it);
            }

            var strip = new StatusStrip();
            var lbl = new ToolStripStatusLabel();
            lbl.Text = string.Format(CultureInfo.InvariantCulture,
                Lang.T("移动 {0} · 新增 {1} · 缺失 {2} · 未变 {3}   （{4}  →  {5}）"),
                moved, added, gone, same, labelA, labelB);
            strip.Items.Add(lbl);

            Controls.Add(list);
            Controls.Add(strip);
        }
    }
}
