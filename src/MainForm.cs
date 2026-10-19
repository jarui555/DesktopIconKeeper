using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DesktopIconKeeper
{
    public sealed class MainForm : Form
    {
        private const int HotkeySave = 0x4B01;
        private const int HotkeyRestore = 0x4B02;

        private readonly SnapshotStore _store;
        private readonly AppSettings _settings;
        private readonly AutoWatcher _watcher;

        private List<Snapshot> _snaps = new List<Snapshot>();

        private ToolStrip _tool;
        private ToolStripButton _btnCapture, _btnRestore, _btnUndo, _btnRename, _btnDelete,
                                _btnDiff, _btnImport, _btnExport, _btnSettings, _btnRefresh, _btnOpenDir,
                                _btnToggleIcons;
        private ToolStripStatusLabel _statusLayout;
        private ListView _list;
        private PreviewPanel _preview;
        private TextBox _detail;
        private StatusStrip _strip;
        private ToolStripStatusLabel _statusMain;
        private NotifyIcon _tray;
        private bool _trayOk;
        private bool _reallyClose;
        private bool _showListenerStarted;
        private bool _hotkeysRegistered;
        private readonly bool _headless;

        public MainForm(SnapshotStore store, AppSettings settings) : this(store, settings, false)
        {
        }

        /// <param name="headless">true 时不建托盘、不启自动监听，仅用于界面自检。</param>
        public MainForm(SnapshotStore store, AppSettings settings, bool headless)
        {
            _store = store;
            _settings = settings;
            _headless = headless;

            IconEngine.AnimateMoves = settings.AnimateRestore;

            if (!_headless)
            {
                _watcher = new AutoWatcher(this, store, settings);
                _watcher.ActionTaken += delegate(string m) { Balloon(Lang.T("桌面图标位置记忆"), m); Reload(); };
                _watcher.Failed += delegate(string m) { Balloon(Lang.T("桌面图标位置记忆 · 失败"), m); };
                _watcher.Changed += delegate(string m) { Reload(); };

                // 关机 / 注销前抢存一份。很多图标乱序是关机时 explorer 写盘造成的，
                // 所以这一次备份往往是最有用的那一份。
                Microsoft.Win32.SystemEvents.SessionEnding += OnSessionEnding;
            }

            BuildUi();
            if (!_headless) BuildTray();

            Load += delegate
            {
                Reload();
                UpdateStatus();
                if (_watcher != null)
                {
                    _watcher.Prime();
                    Reload();
                }
                if (_settings.RestoreOnStartup) RestoreMostRecent(silent: true);

                // 监听线程要等消息泵转起来才能 BeginInvoke，所以排到队列里再启动
                if (!_headless) BeginInvoke(new Action(StartShowListener));
            };
        }

        /// <summary>
        /// 起一个后台线程等「另一个实例喊我把窗口亮出来」的信号。
        ///
        /// 为什么不用窗口消息：HWND_BROADCAST 发的是 WM_USER 区间的消息，
        /// 低完整性进程发的会被 UIPI 静默丢弃；窗口藏在托盘里时也不保证送达。
        /// 命名事件没有这些毛病 —— 同步对象不受 UIPI 管辖。
        /// </summary>
        private void StartShowListener()
        {
            if (_showListenerStarted || _headless) return;
            _showListenerStarted = true;

            var th = new Thread(delegate()
            {
                try
                {
                    using (var req = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowRequestName))
                    using (var ack = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowAckName))
                    {
                        // 清掉可能残留的 signaled 状态。否则上一个实例被强杀时留下的信号
                        // 会让这里 WaitOne 立刻返回，刚缩进托盘又自己弹出来。
                        req.Reset();
                        ack.Reset();

                        while (true)
                        {
                            req.WaitOne();
                            try
                            {
                                BeginInvoke(new Action(delegate
                                {
                                    ShowMain();
                                    try { ack.Set(); } catch (Exception) { }
                                }));
                            }
                            catch (Exception) { }
                        }
                    }
                }
                catch (Exception) { }
            });
            th.IsBackground = true;
            th.Name = "DesktopIconKeeper.ShowListener";
            th.Start();
        }

        // =====================================================================
        // 界面
        // =====================================================================

        private void BuildUi()
        {
            Text = Lang.T("桌面图标位置记忆工具");
            ClientSize = new Size(960, 620);
            MinimumSize = new Size(760, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            Icon = AppIcon.Get();

            _tool = new ToolStrip();
            _tool.GripStyle = ToolStripGripStyle.Hidden;
            _tool.ImageScalingSize = new Size(18, 18);
            _tool.Padding = new Padding(6, 4, 6, 4);
            _tool.RenderMode = ToolStripRenderMode.System;

            _btnCapture = MakeButton(Lang.T("记录当前布局"), Lang.T("把当前桌面图标的位置存成一个快照  (") + _settings.HotkeySave + ")", OnCapture);
            _btnCapture.Font = new Font(Font, FontStyle.Bold);
            _btnRestore = MakeButton(Lang.T("还原选中布局"), Lang.T("把选中的快照写回桌面  (") + _settings.HotkeyRestore + ")", OnRestore);
            _btnRestore.Font = new Font(Font, FontStyle.Bold);
            _btnUndo = MakeButton(Lang.T("撤销上次还原"), Lang.T("把桌面恢复到上一次还原之前的状态"), OnUndo);
            _btnRename = MakeButton(Lang.T("重命名"), Lang.T("给选中的快照改名 / 设置自动还原绑定"), OnRename);
            _btnDelete = MakeButton(Lang.T("删除"), Lang.T("删除选中的快照"), OnDelete);
            _btnDiff = MakeButton(Lang.T("对比"), Lang.T("选中 1 个：与当前桌面对比；选中 2 个：两个快照互相对比"), OnDiff);
            _btnImport = MakeButton(Lang.T("导入"), Lang.T("从 JSON 文件导入快照"), OnImport);
            _btnExport = MakeButton(Lang.T("导出"), Lang.T("把选中的快照导出为 JSON 文件"), OnExport);
            _btnRefresh = MakeButton(Lang.T("刷新"), Lang.T("重新读取快照列表"), delegate { Reload(); });
            _btnToggleIcons = MakeButton(Lang.T("隐藏图标"),
                Lang.T("隐藏 / 显示桌面图标（等同于桌面右键 → 查看 → 显示桌面图标）"), OnToggleIcons);
            _btnOpenDir = MakeButton(Lang.T("数据目录"), Lang.T("打开快照存放目录"), OnOpenDataDir);
            _btnSettings = MakeButton(Lang.T("设置"), Lang.T("热键 / 自动备份 / 自动还原 / 忽略列表"), OnSettings);

            _tool.Items.Add(_btnCapture);
            _tool.Items.Add(_btnRestore);
            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(_btnUndo);
            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(_btnRename);
            _tool.Items.Add(_btnDelete);
            _tool.Items.Add(_btnDiff);
            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(_btnImport);
            _tool.Items.Add(_btnExport);
            _tool.Items.Add(_btnRefresh);
            _tool.Items.Add(_btnToggleIcons);
            _tool.Items.Add(_btnOpenDir);
            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(_btnSettings);

            _statusLayout = new ToolStripStatusLabel();
            _statusLayout.Alignment = ToolStripItemAlignment.Right;
            _statusLayout.ForeColor = Color.DimGray;
            _tool.Items.Add(_statusLayout);

            _list = new ListView();
            _list.View = View.Details;
            _list.Dock = DockStyle.Fill;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = true;
            _list.GridLines = false;
            _list.Columns.Add(Lang.T("名称"), 240);
            _list.Columns.Add(Lang.T("类型"), 64);
            _list.Columns.Add(Lang.T("记录时间"), 150);
            _list.Columns.Add(Lang.T("布局"), 170);
            _list.Columns.Add(Lang.T("图标"), 56, HorizontalAlignment.Right);
            _list.Columns.Add(Lang.T("自动还原"), 76);
            _list.SelectedIndexChanged += delegate { OnSelectionChanged(); };
            _list.DoubleClick += delegate { OnRestore(null, EventArgs.Empty); };

            _preview = new PreviewPanel();
            _preview.Dock = DockStyle.Fill;

            _detail = new TextBox();
            _detail.Multiline = true;
            _detail.ReadOnly = true;
            _detail.ScrollBars = ScrollBars.Vertical;
            _detail.BorderStyle = BorderStyle.None;
            _detail.BackColor = Color.FromArgb(245, 246, 248);
            _detail.Font = new Font("Consolas", 9f);
            _detail.Dock = DockStyle.Fill;

            var right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.ColumnCount = 1;
            right.RowCount = 2;
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
            right.Controls.Add(_preview, 0, 0);
            right.Controls.Add(_detail, 0, 1);

            var split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            // 必须先给 Size 再设 Panel1MinSize / SplitterDistance，
            // 否则默认宽度 150 会让 SplitterDistance 的合法区间为空并抛异常。
            split.Size = new Size(ClientSize.Width, ClientSize.Height);
            split.Panel1MinSize = 300;
            split.Panel2MinSize = 240;
            split.SplitterDistance = 520;
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(right);

            _strip = new StatusStrip();
            _statusMain = new ToolStripStatusLabel("");
            _statusMain.Spring = true;
            _statusMain.TextAlign = ContentAlignment.MiddleLeft;
            _statusMain.Click += delegate { OnStatusMainClick(); };
            _strip.Items.Add(_statusMain);

            // 停靠顺序：先加 Fill，再加边缘条 —— 后加的边缘条先占位
            Controls.Add(split);
            Controls.Add(_tool);
            Controls.Add(_strip);
        }

        private ToolStripButton MakeButton(string text, string tip, EventHandler handler)
        {
            var b = new ToolStripButton();
            b.Text = text;
            b.ToolTipText = tip;
            b.DisplayStyle = ToolStripItemDisplayStyle.Text;
            b.Padding = new Padding(6, 2, 6, 2);
            b.Click += handler;
            return b;
        }

        private void BuildTray()
        {
            // 托盘图标注册受 UIPI 管：低完整性进程往 explorer 的 Shell_TrayWnd
            // 发消息会被静默丢弃，NotifyIcon 不报错但托盘里啥也没有。
            // 所以先问一句托盘够得着吗，够不着就别指望它。
            _trayOk = Elevation.TrayReachable();

            _tray = new NotifyIcon();
            _tray.Icon = AppIcon.Get();
            _tray.Text = Lang.T("桌面图标位置记忆工具");
            try { _tray.Visible = true; }
            catch (Exception) { _trayOk = false; }

            var menu = new ContextMenuStrip();
            menu.Items.Add(Lang.T("记录当前布局"), null, OnCapture);
            menu.Items.Add(Lang.T("还原最近一次记录"), null, delegate { RestoreMostRecent(false); });
            menu.Items.Add(Lang.T("撤销上次还原"), null, OnUndo);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("打开主界面"), null, delegate { ShowMain(); });
            menu.Items.Add(Lang.T("设置"), null, OnSettings);
            menu.Items.Add(new ToolStripSeparator());
            var miElevate = new ToolStripMenuItem(Lang.T("以管理员身份重启"));
            miElevate.Click += delegate { OfferElevate(); };
            menu.Items.Add(miElevate);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("退出"), null, delegate { _reallyClose = true; Close(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowMain(); };
        }

        public void ShowMain()
        {
            ShowInTaskbar = true;          // 之前可能被最小化到托盘关掉了
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            Reload();
        }

        public void HideToTray()
        {
            if (!TrayAvailable) { ShowMain(); return; }   // 托盘不可用就别藏
            Hide();
        }

        /// <summary>托盘图标是否真的注册成功了。false 时绝对不能把窗口藏起来。</summary>
        public bool TrayAvailable
        {
            get { return _tray != null && _trayOk; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyClose && e.CloseReason == CloseReason.UserClosing)
            {
                if (!TrayAvailable)
                {
                    // 没有托盘图标可以回去 —— 藏起来等于让用户永远找不到它。
                    // 这种情况就老老实实退出。
                    e.Cancel = false;
                    _reallyClose = true;
                    base.OnFormClosing(e);
                    return;
                }

                e.Cancel = true;
                Hide();
                if (_settings.ShowBalloonTips)
                    Balloon(Lang.T("仍在后台运行"), Lang.T("程序已最小化到托盘，全局热键依然有效。右键托盘图标可退出。"));
                return;
            }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            Microsoft.Win32.SystemEvents.SessionEnding -= OnSessionEnding;
            UnregisterHotkeys();
            if (_watcher != null) _watcher.Dispose();
            base.OnFormClosing(e);
        }

        private void Balloon(string title, string text)
        {
            if (!_settings.ShowBalloonTips || _tray == null) return;
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text.Length > 250 ? text.Substring(0, 250) + "…" : text;
                _tray.ShowBalloonTip(4000);
            }
            catch (Exception) { }
        }

        // =====================================================================
        // 全局热键
        // =====================================================================

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotkeys();
            base.OnHandleDestroyed(e);
        }

        private void RegisterHotkeys()
        {
            UnregisterHotkeys();
            int ok = 0;
            var a = Ui.ParseHotkey(_settings.HotkeySave);
            if (a.Valid && Native.RegisterHotKey(Handle, HotkeySave, a.Modifiers | Native.MOD_NOREPEAT, a.VirtualKey)) ok++;
            var b = Ui.ParseHotkey(_settings.HotkeyRestore);
            if (b.Valid && Native.RegisterHotKey(Handle, HotkeyRestore, b.Modifiers | Native.MOD_NOREPEAT, b.VirtualKey)) ok++;
            _hotkeysRegistered = ok > 0;
            if (ok < 2)
                _statusMain.Text = Lang.T("提示：部分全局热键被其它程序占用，可在“设置”里更换。");
        }

        private void UnregisterHotkeys()
        {
            if (!_hotkeysRegistered) return;
            try
            {
                Native.UnregisterHotKey(Handle, HotkeySave);
                Native.UnregisterHotKey(Handle, HotkeyRestore);
            }
            catch (Exception) { }
            _hotkeysRegistered = false;
        }

        public void RebindHotkeys()
        {
            _btnCapture.ToolTipText = Lang.T("把当前桌面图标的位置存成一个快照  (") + _settings.HotkeySave + ")";
            _btnRestore.ToolTipText = Lang.T("把选中的快照写回桌面  (") + _settings.HotkeyRestore + ")";
            RegisterHotkeys();
        }

        protected override void WndProc(ref Message m)
        {
            // 另一个实例启动时会广播这条消息，让我们把窗口亮出来
            if (Program.ShowMessage != 0 && m.Msg == (int)Program.ShowMessage)
            {
                Program.AckShowRequest();
                ShowMain();
                return;
            }

            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HotkeySave) { QuickCapture(); return; }
                if (id == HotkeyRestore) { RestoreMostRecent(false); return; }
            }
            base.WndProc(ref m);
        }

        // =====================================================================
        // 列表
        // =====================================================================

        private void Reload()
        {
            string keep = null;
            if (_list.SelectedItems.Count > 0)
            {
                var s = _list.SelectedItems[0].Tag as Snapshot;
                if (s != null) keep = s.Id;
            }

            _snaps = _store.LoadAll();

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (Snapshot s in _snaps)
            {
                var it = new ListViewItem(s.Name);
                it.SubItems.Add(s.KindText);
                it.SubItems.Add(s.CreatedText);
                it.SubItems.Add(s.Layout != null ? s.Layout.Describe() : "-");
                it.SubItems.Add(s.Icons.Count.ToString(CultureInfo.InvariantCulture));
                it.SubItems.Add(s.BoundAutoRestore ? Lang.T("已绑定") : "");
                if (s.Kind == SnapshotKind.Auto) it.ForeColor = Color.DimGray;
                it.Tag = s;
                _list.Items.Add(it);
                if (keep != null && s.Id == keep) it.Selected = true;
            }
            _list.EndUpdate();

            if (_list.Items.Count > 0 && _list.SelectedItems.Count == 0)
                _list.Items[0].Selected = true;

            OnSelectionChanged();
            UpdateStatus();
        }

        private Snapshot Selected()
        {
            if (_list.SelectedItems.Count == 0) return null;
            return _list.SelectedItems[0].Tag as Snapshot;
        }

        private void OnSelectionChanged()
        {
            Snapshot s = Selected();
            if (s == null) { _preview.Clear(); _detail.Text = ""; return; }

            _preview.SetData(s.Layout, s.Icons);

            var sb = new StringBuilder();
            sb.AppendLine(Lang.T("名称      ") + s.Name);
            sb.AppendLine(Lang.T("类型      ") + s.KindText + (s.BoundAutoRestore ? Lang.T("（已绑定到该显示器布局）") : ""));
            sb.AppendLine(Lang.T("记录时间  ") + s.CreatedText);
            sb.AppendLine(Lang.T("图标数量  ") + s.Icons.Count.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(Lang.T("图标间距  ") + s.View.SpacingX + " x " + s.View.SpacingY);
            sb.AppendLine(Lang.T("自动排列  ") + (s.View.AutoArrange ? Lang.T("开") : Lang.T("关")) +
                          Lang.T("      对齐网格  ") + (s.View.SnapToGrid ? Lang.T("开") : Lang.T("关")));
            sb.AppendLine();
            sb.AppendLine(Lang.T("--- 显示器布局 ---"));
            sb.AppendLine(s.Layout != null ? s.Layout.Detail() : Lang.T("(无)"));
            sb.AppendLine();
            sb.AppendLine(Lang.T("--- 图标坐标（前 60 个）---"));
            int n = Math.Min(60, s.Icons.Count);
            for (int i = 0; i < n; i++)
            {
                IconEntry ic = s.Icons[i];
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0,4}  {1,-40} ({2,5}, {3,5})\n", i + 1, Trim(ic.Name, 40), ic.X, ic.Y);
            }
            if (s.Icons.Count > n) sb.AppendLine(Lang.T("… 其余 ") + (s.Icons.Count - n) + Lang.T(" 个省略"));
            _detail.Text = sb.ToString();
        }

        private static string Trim(string s, int max)
        {
            if (s == null) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        private void UpdateStatus()
        {
            string cur;
            try
            {
                LayoutInfo li = LayoutProbe.Capture();
                cur = li.Describe() + Lang.T("  ·  指纹 ") + li.ShortSig();
            }
            catch (Exception) { cur = Lang.T("布局信息不可用"); }

            int count = -1;
            bool auto = false;
            bool hidden = false;
            string err = null;
            using (DesktopListView lv = DesktopListView.Open())
            {
                if (lv != null && lv.IsUsable)
                {
                    count = lv.GetItemCount();
                    auto = lv.AutoArrange;
                    hidden = lv.IconsHidden;
                }
                else
                {
                    err = DesktopListView.LastOpenError;
                }
            }

            if (_btnToggleIcons != null)
            {
                _btnToggleIcons.Text = hidden ? Lang.T("显示图标") : Lang.T("隐藏图标");
                _btnToggleIcons.Enabled = count >= 0;
            }

            if (count >= 0)
            {
                _statusLayout.Text = cur + "  ·  " + count + Lang.T(" 个图标  ·  自动排列：") + (auto ? Lang.T("开") : Lang.T("关"));
                _statusMain.Text = Lang.T("就绪  ·  快照 ") + _snaps.Count + Lang.T(" 个  ·  数据目录 ") + _store.RootDir;
            }
            else
            {
                _statusLayout.Text = cur;
                _statusMain.Text = Lang.T("无法访问桌面图标：") + (err ?? Lang.T("未知原因")).Replace("\n", " ");
            }

            // 权限不足是唯一一个用户点一下就能解决的故障，单独给一条醒目提示 + 一键提权。
            if (count < 0 && DesktopListView.LastAccessDenied)
            {
                _statusMain.ForeColor = Color.FromArgb(190, 30, 30);
                _statusMain.Text = Elevation.ElevationWorthTrying
                    ? Lang.T("权限不足：本工具运行在比 explorer 更低的权限里。点击此处以管理员身份重启 →")
                    : Lang.T("权限不足：本工具运行在比 explorer 更低的权限里，且本机 UAC 已关闭。请直接在资源管理器里双击 exe。");
                _statusMain.IsLink = true;
                _statusMain.ToolTipText = (err ?? "").Replace("\n", " ");
                return;
            }

            _statusMain.IsLink = false;
            _statusMain.ToolTipText = "";

            if (auto)
            {
                _statusMain.ForeColor = Color.FromArgb(190, 90, 0);
                _statusMain.Text = Lang.T("桌面开启了“自动排列图标”，还原会失败 —— 点击「设置」可让本工具自动关闭它。");
            }
            else
            {
                _statusMain.ForeColor = Color.Black;
            }
        }

        // =====================================================================
        // 动作
        // =====================================================================

        private void OnStatusMainClick()
        {
            if (!DesktopListView.LastAccessDenied) return;
            OfferElevate();
        }

        /// <summary>
        /// 权限不足时的出路选择器。
        /// 关键：UAC 关闭时 runas 提权是空操作，此时不该再问要不要提权重启——
        /// 那只会让用户点一个注定失败的按钮。这种情况只给去资源管理器打开。
        /// </summary>
        private bool OfferElevate()
        {
            string detail = string.IsNullOrEmpty(DesktopListView.LastOpenError)
                ? Lang.T("无法访问桌面图标视图。")
                : DesktopListView.LastOpenError;

            bool canElevate = Elevation.ElevationWorthTrying;

            using (var dlg = new AccessDeniedForm(detail, canElevate))
            {
                dlg.ShowDialog(this);

                if (dlg.OpenExplorerRequested)
                {
                    if (!Elevation.OpenInExplorer())
                        Ui.Warn(this, Lang.T("无法打开资源管理器。请手动找到 exe 所在目录并双击运行。"), Lang.T("打开失败"));
                    return false;
                }

                if (!dlg.ElevateRequested) return false;
            }

            if (Elevation.RestartElevated(Program.ElevateFlag))
            {
                _reallyClose = true;
                Close();
                return true;
            }

            Ui.Warn(this, Lang.T("提权失败（可能被 UAC 或安全软件拒绝）。\n请手动右键 exe → 以管理员身份运行。"), Lang.T("提权失败"));
            return false;
        }

        private void OnCapture(object sender, EventArgs e) { QuickCapture(); }

        /// <summary>
        /// 动手之前先确认桌面视图能打开。
        /// 返回 false 表示已经给过用户提示（并可能已引导去提权 / 去资源管理器）。
        /// </summary>
        private bool CanReachDesktop()
        {
            bool usable;
            using (DesktopListView probe = DesktopListView.Open())
                usable = probe != null && probe.IsUsable;

            if (usable) return true;

            if (DesktopListView.LastAccessDenied) { OfferElevate(); return false; }

            Ui.Warn(this, string.IsNullOrEmpty(DesktopListView.LastOpenError)
                ? Lang.T("无法访问桌面图标视图。") : DesktopListView.LastOpenError, Lang.T("桌面不可用"));
            return false;
        }

        private void QuickCapture()
        {
            // 先探权限：否则用户辛苦输完快照名才被告知没权限，白忙一场。
            if (!CanReachDesktop()) return;

            string def = DateTime.Now.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture) + Lang.T(" 的布局");
            string name = Ui.Input(this, Lang.T("记录当前布局"), Lang.T("给这个布局起个名字："), def);
            if (string.IsNullOrEmpty(name)) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                using (DesktopListView lv = DesktopListView.Open())
                {
                    if (lv == null || !lv.IsUsable)
                    {
                        if (DesktopListView.LastAccessDenied) { OfferElevate(); return; }
                        Ui.Warn(this, string.IsNullOrEmpty(DesktopListView.LastOpenError)
                            ? Lang.T("无法访问桌面图标视图。") : DesktopListView.LastOpenError, Lang.T("记录失败"));
                        return;
                    }

                    Snapshot s = IconEngine.Capture(lv, name, SnapshotKind.Manual);
                    if (s.Icons.Count == 0)
                    {
                        Ui.Warn(this, Lang.T("当前桌面没有捕获到任何图标。请确认桌面图标处于显示状态。"), Lang.T("记录失败"));
                        return;
                    }
                    _store.Save(s);
                    Reload();
                    Balloon(Lang.T("已记录布局"), "「" + name + Lang.T("」已保存，") + s.Icons.Count + Lang.T(" 个图标，") + s.Layout.Describe() + "。");
                }
            }
            catch (Exception ex)
            {
                Ui.Warn(this, Lang.T("记录失败：") + ex.Message, Lang.T("记录失败"));
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnRestore(object sender, EventArgs e)
        {
            Snapshot s = Selected();
            if (s == null) { Ui.Info(this, Lang.T("请先在列表里选中一个快照。"), Lang.T("还原")); return; }
            DoRestore(s, false);
        }

        private void RestoreMostRecent(bool silent)
        {
            Reload();
            Snapshot s = null;
            foreach (Snapshot x in _snaps)
            {
                if (x.Kind == SnapshotKind.Manual) { s = x; break; }
            }
            if (s == null && _snaps.Count > 0) s = _snaps[0];
            if (s == null)
            {
                if (!silent) Ui.Info(this, Lang.T("还没有任何快照，请先「记录当前布局」。"), Lang.T("还原"));
                return;
            }
            DoRestore(s, silent);
        }

        private void DoRestore(Snapshot s, bool silent)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                using (DesktopListView lv = DesktopListView.Open())
                {
                    if (lv == null || !lv.IsUsable)
                    {
                        if (DesktopListView.LastAccessDenied) { if (!silent) OfferElevate(); return; }
                        if (!silent) Ui.Warn(this, string.IsNullOrEmpty(DesktopListView.LastOpenError)
                            ? Lang.T("无法访问桌面图标视图。") : DesktopListView.LastOpenError, Lang.T("还原失败"));
                        return;
                    }

                    // 撤销点
                    try
                    {
                        Snapshot undo = IconEngine.Capture(lv, Lang.T("还原前 ") + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), SnapshotKind.Undo);
                        _store.SaveUndo(undo);
                    }
                    catch (Exception) { }

                    RestoreReport rep = IconEngine.Restore(lv, s, _settings, _settings.ScaleAcrossLayouts);
                    _watcher.MarkRestored();

                    if (rep.AutoArrangeBlocked)
                    {
                        Ui.Warn(this, IconEngine.Describe(rep), Lang.T("还原被阻止"));
                        return;
                    }

                    if (!silent)
                    {
                        string msg = "「" + s.Name + "」\n\n" + IconEngine.Describe(rep);
                        if (rep.Mismatched > 0)
                            Ui.Warn(this, msg, Lang.T("还原完成（有偏差）"));
                        else
                            Ui.Info(this, msg, Lang.T("还原完成"));
                    }
                    else
                    {
                        Balloon(Lang.T("已还原布局"), s.Name + " · " + rep.Summary());
                    }
                    Reload();
                }
            }
            catch (Exception ex)
            {
                if (!silent) Ui.Warn(this, Lang.T("还原失败：") + ex.Message, Lang.T("还原失败"));
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnUndo(object sender, EventArgs e)
        {
            Snapshot undo = _store.LoadUndo();
            if (undo == null)
            {
                Ui.Info(this, Lang.T("没有可用的撤销点。撤销点会在每次还原前自动创建。"), Lang.T("撤销"));
                return;
            }

            if (!Ui.Confirm(this, Lang.T("把桌面恢复到 ") + undo.CreatedText + Lang.T(" 的状态？\n（共 ") + undo.Icons.Count + Lang.T(" 个图标）"), Lang.T("撤销上次还原")))
                return;

            Cursor = Cursors.WaitCursor;
            try
            {
                using (DesktopListView lv = DesktopListView.Open())
                {
                    if (lv == null || !lv.IsUsable)
                    {
                        Ui.Warn(this, Lang.T("无法访问桌面图标视图。"), Lang.T("撤销失败"));
                        return;
                    }
                    RestoreReport rep = IconEngine.Restore(lv, undo, _settings, false);
                    _watcher.MarkRestored();
                    _store.ClearUndo();
                    Ui.Info(this, IconEngine.Describe(rep), Lang.T("撤销完成"));
                    Reload();
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnRename(object sender, EventArgs e)
        {
            Snapshot s = Selected();
            if (s == null) { Ui.Info(this, Lang.T("请先选中一个快照。"), Lang.T("重命名")); return; }

            using (var f = new RenameForm(s, _settings))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                s.Name = f.SnapshotName;
                s.BoundAutoRestore = f.BoundAutoRestore;
                _store.Save(s);
                Reload();
            }
        }

        private void OnDelete(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count == 0) { Ui.Info(this, Lang.T("请先选中要删除的快照。"), Lang.T("删除")); return; }
            var targets = new List<Snapshot>();
            foreach (ListViewItem it in _list.SelectedItems) targets.Add((Snapshot)it.Tag);

            if (!Ui.Confirm(this, Lang.T("确定删除 ") + targets.Count + Lang.T(" 个快照？此操作不可恢复。"), Lang.T("删除快照"))) return;
            int failed = 0;
            foreach (Snapshot s in targets) if (!_store.Delete(s)) failed++;
            Reload();
            if (failed > 0)
                Ui.Warn(this, Lang.T("删除失败（文件可能被占用）：") + failed + Lang.T(" 个"), Lang.T("错误"));
        }

        private void OnDiff(object sender, EventArgs e)
        {
            if (_list.SelectedItems.Count >= 2)
            {
                var a = (Snapshot)_list.SelectedItems[0].Tag;
                var b = (Snapshot)_list.SelectedItems[1].Tag;
                new DiffForm(a, b, Lang.T("快照「") + a.Name + "」", Lang.T("快照「") + b.Name + "」").ShowDialog(this);
                return;
            }

            Snapshot s = Selected();
            if (s == null) { Ui.Info(this, Lang.T("请先选中一个快照。"), Lang.T("对比")); return; }

            Cursor = Cursors.WaitCursor;
            try
            {
                using (DesktopListView lv = DesktopListView.Open())
                {
                    if (lv == null || !lv.IsUsable)
                    {
                        Ui.Warn(this, Lang.T("无法访问桌面图标视图。"), Lang.T("对比失败"));
                        return;
                    }
                    Snapshot now = IconEngine.Capture(lv, Lang.T("当前桌面"), SnapshotKind.Manual);
                    new DiffForm(s, now, Lang.T("快照「") + s.Name + "」", Lang.T("当前桌面")).ShowDialog(this);
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OnImport(object sender, EventArgs e)
        {
            using (var d = new OpenFileDialog())
            {
                d.Title = Lang.T("导入快照");
                d.Filter = Lang.T("快照 JSON (*.json)|*.json|所有文件 (*.*)|*.*");
                d.InitialDirectory = _store.SnapshotDir;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Snapshot s = _store.Import(d.FileName);
                    Reload();
                    Ui.Info(this, Lang.T("已导入「") + s.Name + Lang.T("」，共 ") + s.Icons.Count + Lang.T(" 个图标。"), Lang.T("导入成功"));
                }
                catch (Exception ex)
                {
                    Ui.Warn(this, Lang.T("导入失败：") + ex.Message, Lang.T("导入失败"));
                }
            }
        }

        private void OnExport(object sender, EventArgs e)
        {
            Snapshot s = Selected();
            if (s == null) { Ui.Info(this, Lang.T("请先选中要导出的快照。"), Lang.T("导出")); return; }

            using (var d = new SaveFileDialog())
            {
                d.Title = Lang.T("导出快照");
                d.Filter = Lang.T("快照 JSON (*.json)|*.json");
                d.FileName = s.Name + ".json";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _store.Export(s, d.FileName);
                    Ui.Info(this, Lang.T("已导出到：\n") + d.FileName, Lang.T("导出成功"));
                }
                catch (Exception ex)
                {
                    Ui.Warn(this, Lang.T("导出失败：") + ex.Message, Lang.T("导出失败"));
                }
            }
        }

        private void OnOpenDataDir(object sender, EventArgs e)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + _store.RootDir + "\""); }
            catch (Exception ex) { Ui.Warn(this, Lang.T("无法打开目录：") + ex.Message, Lang.T("错误")); }
        }

        /// <summary>
        /// 关机 / 注销前抢存一份快照。
        ///
        /// 系统只给几秒钟，所以这里不弹窗、不重试、失败就算了 ——
        /// 绝不能因为备份失败而拖住关机。
        /// </summary>
        private void OnSessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs e)
        {
            if (!_settings.AutoBackupOnShutdown) return;
            try
            {
                using (DesktopListView lv = DesktopListView.Open())
                {
                    if (lv == null || !lv.IsUsable) return;
                    Snapshot snap = IconEngine.Capture(lv, Lang.T("关机前自动备份"), SnapshotKind.Auto);
                    _store.Save(snap);
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 隐藏 / 显示桌面图标。只是切换 SysListView32 的 WS_VISIBLE，
        /// 不动文件、不动快照，所以随时可以来回切。
        /// </summary>
        private void OnToggleIcons(object sender, EventArgs e)
        {
            try
            {
                bool nowHidden;
                using (DesktopListView lv = DesktopListView.Open())
                {
                    if (lv == null || !lv.IsUsable)
                    {
                        Ui.Warn(this,
                            string.IsNullOrEmpty(DesktopListView.LastOpenError)
                                ? Lang.T("桌面图标视图不可用。")
                                : DesktopListView.LastOpenError,
                            Lang.T("桌面不可用"));
                        return;
                    }
                    nowHidden = !lv.IconsHidden;
                    lv.SetIconsHidden(nowHidden);
                }
                UpdateStatus();
                if (_settings.ShowBalloonTips)
                    Balloon(Lang.T("桌面图标位置记忆"), nowHidden ? Lang.T("已隐藏桌面图标") : Lang.T("已显示桌面图标"));
            }
            catch (Exception ex)
            {
                Ui.Warn(this, Lang.T("执行失败：") + ex.Message, Lang.T("错误"));
            }
        }

        private void OnSettings(object sender, EventArgs e)
        {
            using (var f = new SettingsForm(_settings, _store))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                _settings.Save(_store.SettingsFile);
                RebindHotkeys();
                UpdateStatus();
                Reload();
            }
        }

        /// <summary>托盘菜单用：还原最近一次手动快照（silent 时不弹框）。</summary>
        public void RestoreLatest(bool silent)
        {
            RestoreMostRecent(silent);
        }
    }

    /// <summary>快照属性编辑：改名 + 绑定自动还原。</summary>
    public sealed class RenameForm : Form
    {
        public string SnapshotName { get; private set; }
        public bool BoundAutoRestore { get; private set; }

        public RenameForm(Snapshot s, AppSettings settings)
        {
            Text = Lang.T("快照属性");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 230);
            Font = new Font("Microsoft YaHei UI", 9f);

            var lbl = new Label();
            lbl.Text = Lang.T("名称：");
            lbl.SetBounds(16, 20, 60, 24);
            Controls.Add(lbl);

            var tb = new TextBox();
            tb.Text = s.Name;
            tb.SetBounds(78, 18, 366, 26);
            Controls.Add(tb);

            var chk = new CheckBox();
            chk.Text = Lang.T("绑定到当前显示器布局，切回该布局时自动还原");
            chk.Checked = s.BoundAutoRestore;
            chk.SetBounds(78, 58, 366, 26);
            Controls.Add(chk);

            var info = new Label();
            info.ForeColor = Color.DimGray;
            info.SetBounds(78, 90, 366, 60);
            info.Text = Lang.T("快照记录的布局：") + (s.Layout != null ? s.Layout.Describe() : "-") +
                        Lang.T("\n指纹：") + (s.Layout != null ? s.Layout.ShortSig() : "-") +
                        Lang.T("\n当前显示器布局：") + LayoutProbe.Capture().Describe() + " · " + LayoutProbe.Capture().ShortSig();
            Controls.Add(info);

            var ok = new Button();
            ok.Text = Lang.T("确定");
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(268, 176, 84, 30);
            ok.Click += delegate
            {
                SnapshotName = tb.Text.Trim();
                BoundAutoRestore = chk.Checked;
            };
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = Lang.T("取消");
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(360, 176, 84, 30);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
            SnapshotName = s.Name;
            BoundAutoRestore = s.BoundAutoRestore;
        }
    }
}
