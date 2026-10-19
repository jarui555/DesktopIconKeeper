using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 显示器布局变化监听。
    ///
    /// 逻辑：
    ///   1) 变化后先等 explorer 把图标重排完（坐标指纹连续两次一致）；
    ///   2) 为新布局建/更新自动备份；
    ///   3) 若该布局有绑定的手动快照或已有自动备份，自动还原。
    ///
    /// 另有一个 60 秒的巡检定时器，把当前布局的最新摆放记进自动备份，
    /// 这样用户随手挪动过的位置也能被记住。刚还原完的 5 分钟内不覆盖，
    /// 避免把还原结果又当成用户的新摆放存回去。
    /// </summary>
    public sealed class AutoWatcher : IDisposable
    {
        private readonly Control _owner;
        private readonly SnapshotStore _store;
        private readonly AppSettings _settings;

        private readonly Timer _debounce;
        private readonly Timer _patrol;

        private bool _busy;
        private DateTime _lastRestoreUtc = DateTime.MinValue;
        private string _lastSignature = "";

        public event Action<string> ActionTaken;
        public event Action<string> Failed;

        /// <summary>自动还原前后触发，让主界面刷新列表。</summary>
        public event Action<string> Changed;

        public AutoWatcher(Control owner, SnapshotStore store, AppSettings settings)
        {
            _owner = owner;
            _store = store;
            _settings = settings;

            _debounce = new Timer();
            _debounce.Interval = 2500;
            _debounce.Tick += OnDebounceTick;

            _patrol = new Timer();
            _patrol.Interval = 60000;
            _patrol.Tick += OnPatrolTick;

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _patrol.Start();
        }

        public void Dispose()
        {
            try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; }
            catch (Exception) { }
            _debounce.Stop();
            _patrol.Stop();
            _debounce.Dispose();
            _patrol.Dispose();
        }

        public void MarkRestored()
        {
            _lastRestoreUtc = DateTime.UtcNow;
        }

        // =====================================================================
        // 事件入口
        // =====================================================================

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            try
            {
                _owner.BeginInvoke((MethodInvoker)delegate
                {
                    _debounce.Stop();
                    _debounce.Start();
                });
            }
            catch (Exception) { }
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            _debounce.Stop();
            HandleDisplayChange();
        }

        private void OnPatrolTick(object sender, EventArgs e)
        {
            if (_busy) return;
            if (!_settings.AutoBackupOnDisplayChange) return;
            if ((DateTime.UtcNow - _lastRestoreUtc).TotalMinutes < 5) return;

            using (DesktopListView lv = DesktopListView.Open())
            {
                if (lv == null || !lv.IsUsable) return;
                try
                {
                    var cur = LayoutProbe.Capture();
                    var existing = _store.FindAutoFor(cur.Signature);
                    if (existing == null) return;   // 首次见到交给变化处理器建

                    var icons = lv.ReadAll();
                    if (icons.Count != existing.Icons.Count) return;   // 数量变了，不覆盖
                    if (SameLayout(existing.Icons, icons)) return;     // 没变化

                    // 布局确实被挪动过，记下来
                    var snap = IconEngine.Capture(lv, existing.Name, SnapshotKind.Auto);
                    snap.Id = existing.Id;
                    snap.Created = existing.Created;
                    _store.Save(snap);
                    Raise(Changed, null);
                }
                catch (Exception) { }
            }
        }

        private static bool SameLayout(System.Collections.Generic.List<IconEntry> a, System.Collections.Generic.List<IconEntry> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Name != b[i].Name) return false;
                if (Math.Abs(a[i].X - b[i].X) > 1 || Math.Abs(a[i].Y - b[i].Y) > 1) return false;
            }
            return true;
        }

        // =====================================================================
        // 主流程
        // =====================================================================

        private void HandleDisplayChange()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                using (DesktopListView lv = DesktopListView.Open())
                {
                    if (lv == null || !lv.IsUsable)
                    {
                        Raise(Failed, Lang.T("显示器变化后无法访问桌面图标视图：") +
                            (string.IsNullOrEmpty(DesktopListView.LastOpenError) ? Lang.T("未知原因") : DesktopListView.LastOpenError));
                        return;
                    }

                    LayoutStabilizer.WaitStable(lv, 8000, 500);

                    var cur = LayoutProbe.Capture();
                    string sig = cur.Signature;
                    bool configChanged = sig != _lastSignature;
                    _lastSignature = sig;

                    // --- 1. 自动备份 ---
                    if (_settings.AutoBackupOnDisplayChange)
                    {
                        var auto = _store.FindAutoFor(sig);
                        if (auto == null)
                        {
                            var snap = IconEngine.Capture(lv, Lang.T("自动备份 ") + cur.Describe(), SnapshotKind.Auto);
                            _store.Save(snap);
                            Raise(ActionTaken, Lang.T("检测到显示器布局变化（") + cur.Describe() + Lang.T("），已建立自动备份。"));
                            Raise(Changed, null);
                        }
                    }

                    if (!_settings.AutoRestoreOnDisplayChange) return;

                    // --- 2. 自动还原 ---
                    Snapshot target = _store.FindBoundFor(sig);
                    if (target == null) target = _store.FindAutoFor(sig);
                    if (target == null) return;

                    // 当前摆放已经和快照一致就不用动
                    var nowIcons = lv.ReadAll();
                    if (SameLayout(target.Icons, nowIcons)) return;

                    // 先存撤销点
                    try
                    {
                        var undo = IconEngine.Capture(lv, Lang.T("自动还原前 ") + DateTime.Now.ToString("HH:mm:ss"), SnapshotKind.Undo);
                        _store.SaveUndo(undo);
                    }
                    catch (Exception) { }

                    var rep = IconEngine.Restore(lv, target, _settings, _settings.ScaleAcrossLayouts);
                    MarkRestored();
                    Raise(Changed, null);

                    if (rep.AutoArrangeBlocked)
                        Raise(Failed, Lang.T("自动还原失败：") + rep.Summary());
                    else
                        Raise(ActionTaken, Lang.T("已自动还原布局「") + target.Name + "」：" + rep.Summary());
                }
            }
            catch (Exception ex)
            {
                Raise(Failed, Lang.T("自动处理显示器变化时出错：") + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private void Raise(Action<string> handler, string msg)
        {
            if (handler == null) return;
            try { handler(msg); }
            catch (Exception) { }
        }

        /// <summary>供主界面在启动时同步一次当前指纹。</summary>
        public void Prime()
        {
            try
            {
                var cur = LayoutProbe.Capture();
                _lastSignature = cur.Signature;

                if (!_settings.AutoBackupOnDisplayChange) return;
                using (DesktopListView lv = DesktopListView.Open())
                {
                    if (lv == null || !lv.IsUsable) return;
                    if (_store.FindAutoFor(cur.Signature) != null) return;
                    var snap = IconEngine.Capture(lv, Lang.T("自动备份 ") + cur.Describe(), SnapshotKind.Auto);
                    _store.Save(snap);
                    Raise(Changed, null);
                }
            }
            catch (Exception) { }
        }
    }
}
