using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;

namespace DesktopIconKeeper
{
    public class RestoreReport
    {
        public int Total;
        public int Applied;        // 成功写入
        public int Missing;        // 快照里有、当前桌面没有（文件被删/改名）
        public int Unmatched;      // 当前桌面有、快照里没有（新图标）
        public int Mismatched;     // 写入后回读仍不一致（被系统纠正）
        public int Ignored;
        public bool AutoArrangeBlocked;
        public bool Scaled;
        public List<string> Notes = new List<string>();

        public bool Ok { get { return !AutoArrangeBlocked && Mismatched == 0; } }

        public string Summary()
        {
            if (AutoArrangeBlocked)
                return Lang.T("还原被阻止：桌面开启了“自动排列图标”，任何坐标写入都会被系统立刻重排。");
            var sb = new StringBuilder();
            sb.Append(Lang.T("已就位 ")).Append(Applied).Append('/').Append(Total).Append(Lang.T(" 个图标"));
            if (Scaled) sb.Append(Lang.T("（已按布局比例缩放）"));
            if (Missing > 0) sb.Append("；").Append(Missing).Append(Lang.T(" 个快照中的图标已不存在"));
            if (Unmatched > 0) sb.Append("；").Append(Unmatched).Append(Lang.T(" 个新图标未记录，保持原位"));
            if (Mismatched > 0) sb.Append("；").Append(Mismatched).Append(Lang.T(" 个被系统重新吸附"));
            if (Ignored > 0) sb.Append("；").Append(Ignored).Append(Lang.T(" 个在忽略列表中"));
            return sb.ToString();
        }
    }

    /// <summary>
    /// 采集 / 还原 / 校验。所有还原都走“按名称匹配 + 写坐标 + 回读校验”三步。
    /// </summary>
    public static class IconEngine
    {
        public const int VerifyDelayMs = 260;

        // ---- 过渡动画 ----
        // 还原时让图标"滑"到目标位置，而不是瞬移。纯视觉，不影响最终坐标。
        public static bool AnimateMoves = true;
        public static int AnimateFrames = 8;
        public static int AnimateFrameDelayMs = 14;

        // =====================================================================
        // 采集
        // =====================================================================

        public static Snapshot Capture(DesktopListView lv, string name, string kind)
        {
            if (lv == null || !lv.IsUsable)
                throw new InvalidOperationException(string.IsNullOrEmpty(DesktopListView.LastOpenError)
                    ? Lang.T("桌面图标视图不可用。") : DesktopListView.LastOpenError);

            var s = new Snapshot();
            s.Name = name;
            s.Kind = kind;
            s.Created = DateTime.Now;
            s.Layout = LayoutProbe.Capture();
            s.View = LayoutProbe.CaptureView(lv);
            s.Icons = lv.ReadAll();
            return s;
        }

        // =====================================================================
        // 还原
        // =====================================================================

        public static RestoreReport Restore(DesktopListView lv, Snapshot snap, AppSettings settings, bool scaleAcrossLayouts)
        {
            var rep = new RestoreReport();
            if (lv == null || !lv.IsUsable)
            {
                rep.Notes.Add(string.IsNullOrEmpty(DesktopListView.LastOpenError)
                    ? Lang.T("桌面图标视图不可用。") : DesktopListView.LastOpenError);
                rep.AutoArrangeBlocked = true;
                return rep;
            }
            if (snap == null || snap.Icons == null || snap.Icons.Count == 0)
            {
                rep.Notes.Add(Lang.T("快照为空，没有可还原的图标。"));
                return rep;
            }

            // --- 1. 自动排列必须关闭，否则写进去就被重排 ---
            if (lv.AutoArrange)
            {
                bool turnedOff = false;
                if (settings != null && settings.AutoDisableAutoArrange)
                {
                    turnedOff = lv.DisableAutoArrange();
                    if (turnedOff) LayoutProbe.PersistAutoArrangeOff();
                }
                if (!turnedOff)
                {
                    rep.AutoArrangeBlocked = true;
                    rep.Notes.Add(Lang.T("请先关闭桌面“查看 → 自动排列图标”，或在设置中允许本工具自动关闭。"));
                    return rep;
                }
                rep.Notes.Add(Lang.T("已自动关闭“自动排列图标”。"));
            }

            // --- 2. 建立当前桌面的 名称 -> 索引列表 映射 ---
            int count = lv.GetItemCount();
            var index = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string n = lv.GetItemText(i);
                if (string.IsNullOrEmpty(n)) continue;
                List<int> lst;
                if (!index.TryGetValue(n, out lst)) { lst = new List<int>(); index[n] = lst; }
                lst.Add(i);
            }

            // --- 3. 跨布局缩放 ---
            bool doScale = false;
            double sx = 1.0, sy = 1.0;
            if (scaleAcrossLayouts && snap.Layout != null && snap.Layout.VW > 0 && snap.Layout.VH > 0)
            {
                var cur = LayoutProbe.Capture();
                if (cur.Signature != snap.Layout.Signature && cur.VW > 0 && cur.VH > 0)
                {
                    sx = (double)cur.VW / snap.Layout.VW;
                    sy = (double)cur.VH / snap.Layout.VH;
                    doScale = true;
                    rep.Scaled = true;
                    rep.Notes.Add(string.Format(CultureInfo.InvariantCulture,
                        Lang.T("布局指纹不一致（快照 {0} → 当前 {1}），坐标按 {2:0.###}x / {3:0.###}y 缩放。"),
                        snap.Layout.ShortSig(), cur.ShortSig(), sx, sy));
                }
            }

            // --- 4. 逐个写入 ---
            var used = new Dictionary<string, int>(StringComparer.Ordinal);   // 名称 -> 已消费到第几个
            var touched = new List<KeyValuePair<int, IconEntry>>();
            var matchedIndexes = new HashSet<int>();
            var pending = new List<PendingMove>();

            foreach (var ic in snap.Icons)
            {
                rep.Total++;

                if (settings != null && settings.IsIgnored(ic.Name)) { rep.Ignored++; continue; }

                List<int> candidates;
                if (!index.TryGetValue(ic.Name, out candidates)) { rep.Missing++; continue; }

                int consumed;
                if (!used.TryGetValue(ic.Name, out consumed)) consumed = 0;
                int pick = ic.Occurrence < candidates.Count ? ic.Occurrence : -1;
                if (pick < 0)
                {
                    // 同名数量变了，退而求其次取下一个没用过的
                    if (consumed < candidates.Count) pick = consumed;
                }
                if (pick < 0) { rep.Missing++; continue; }

                used[ic.Name] = consumed + 1;
                int idx = candidates[pick];
                matchedIndexes.Add(idx);

                int tx = ic.X, ty = ic.Y;
                if (doScale)
                {
                    tx = (int)Math.Round(ic.X * sx);
                    ty = (int)Math.Round(ic.Y * sy);
                }

                Point now = lv.GetItemPosition(idx);
                pending.Add(new PendingMove { Index = idx, CurX = now.X, CurY = now.Y, TgtX = tx, TgtY = ty });
                touched.Add(new KeyValuePair<int, IconEntry>(idx, ic));
                rep.Applied++;
            }

            ApplyMoves(lv, pending, rep);

            // --- 5. 未记录的新图标：可选挪到末尾空位 ---
            for (int i = 0; i < count; i++)
            {
                if (matchedIndexes.Contains(i)) continue;
                string n = lv.GetItemText(i);
                if (settings != null && settings.IsIgnored(n)) { continue; }
                rep.Unmatched++;
            }

            if (settings != null && settings.ParkUnmatchedIcons && rep.Unmatched > 0)
                ParkUnmatched(lv, matchedIndexes, snap.View, rep);

            // --- 6. 回读校验 ---
            Thread.Sleep(VerifyDelayMs);
            foreach (var kv in touched)
            {
                Point p = lv.GetItemPosition(kv.Key);
                int tx = kv.Value.X, ty = kv.Value.Y;
                if (doScale)
                {
                    tx = (int)Math.Round(kv.Value.X * sx);
                    ty = (int)Math.Round(kv.Value.Y * sy);
                }
                if (Math.Abs(p.X - tx) > 1 || Math.Abs(p.Y - ty) > 1) rep.Mismatched++;
            }
            if (rep.Mismatched > 0)
                rep.Notes.Add(Lang.T("部分图标被系统重新吸附到网格（通常因为“对齐到网格”或图标间距不同）。"));

            return rep;
        }

        /// <summary>一次待执行的图标位移。</summary>
        private class PendingMove
        {
            public int Index;
            public int CurX, CurY;      // 当前位置（会随移动更新）
            public int TgtX, TgtY;      // 目标位置
        }

        /// <summary>
        /// 分帧把图标从当前位置插值到目标位置。
        ///
        /// 注意这只是"好看"，不负责正确性：中间帧会被"对齐到网格"吸附，
        /// 所以动画跑完必须回读真实坐标，交给后面的拓扑排序去精确落位。
        /// </summary>
        private static void AnimateIfEnabled(DesktopListView lv, List<PendingMove> pending)
        {
            if (!AnimateMoves || pending == null || pending.Count == 0) return;

            // 图标太多时每帧要发上百条跨进程消息，动画只会更卡
            int frames = pending.Count > 120 ? 4 : AnimateFrames;
            if (frames < 2) return;

            var from = new List<Point>(pending.Count);
            foreach (PendingMove m in pending) from.Add(new Point(m.CurX, m.CurY));

            for (int f = 1; f <= frames; f++)
            {
                double t = (double)f / frames;
                for (int i = 0; i < pending.Count; i++)
                {
                    PendingMove m = pending[i];
                    int x = (int)Math.Round(from[i].X + (m.TgtX - from[i].X) * t);
                    int y = (int)Math.Round(from[i].Y + (m.TgtY - from[i].Y) * t);
                    lv.SetItemPosition(m.Index, x, y);
                }
                if (f < frames) Thread.Sleep(AnimateFrameDelayMs);
            }

            // 回读真实位置 —— 中间帧被吸附过，不能假定已经到了终点
            foreach (PendingMove m in pending)
            {
                Point p = lv.GetItemPosition(m.Index);
                m.CurX = p.X;
                m.CurY = p.Y;
            }
        }

        /// <summary>
        /// 按依赖顺序写入图标位置。
        ///
        /// 直接 for 循环逐个 SetItemPosition 会出错：如果 A 要去的格子正被 B 占着、
        /// 而 B 还没搬走，Windows 会把 B 挤开（实测表现是整列上移一格），
        /// 结果 A 到位了，B 却永远回不到原位 —— 还原看起来成功但布局是错的。
        ///
        /// 所以这里反复扫描：每一轮只搬目标格子当前没人占的图标，直到搬完。
        /// 真遇到环（A 要 B 的位、B 要 A 的位）才把其中一个暂存到屏幕下方打破僵局。
        /// </summary>
        private static void ApplyMoves(DesktopListView lv, List<PendingMove> pending, RestoreReport rep)
        {
            AnimateIfEnabled(lv, pending);

            int guard = pending.Count * pending.Count + 16;
            int parked = 0;

            while (pending.Count > 0 && guard-- > 0)
            {
                bool progressed = false;

                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    PendingMove m = pending[i];

                    if (m.CurX == m.TgtX && m.CurY == m.TgtY) { pending.RemoveAt(i); progressed = true; continue; }

                    bool blocked = false;
                    for (int j = 0; j < pending.Count; j++)
                    {
                        if (j == i) continue;
                        PendingMove o = pending[j];
                        if (o.CurX == m.TgtX && o.CurY == m.TgtY) { blocked = true; break; }
                    }
                    if (blocked) continue;

                    lv.SetItemPosition(m.Index, m.TgtX, m.TgtY);
                    m.CurX = m.TgtX;
                    m.CurY = m.TgtY;
                    pending.RemoveAt(i);
                    progressed = true;
                }

                if (progressed || pending.Count == 0) continue;

                // 成环了：把队首先挪到屏幕下方暂存，腾出格子
                PendingMove head = pending[0];
                int parkY = 20000 + parked * 98;
                lv.SetItemPosition(head.Index, head.CurX, parkY);
                head.CurY = parkY;
                parked++;
            }

            // 兜底：万一 guard 耗尽还没搬完，宁可重叠也不能把图标丢在暂存区
            if (pending.Count > 0)
            {
                foreach (PendingMove m in pending) lv.SetItemPosition(m.Index, m.TgtX, m.TgtY);
                pending.Clear();
            }

            if (parked > 0 && rep != null)
                rep.Notes.Add(Lang.T("有 ") + parked + Lang.T(" 个图标目标位置互相占用，已先暂存再落位。"));
        }

        /// <summary>把没记录的图标排到已占用网格之后。</summary>
        private static void ParkUnmatched(DesktopListView lv, HashSet<int> matched, ViewInfo view, RestoreReport rep)
        {
            int count = lv.GetItemCount();
            if (count <= 0) return;

            int cellW = view != null && view.SpacingX > 0 ? view.SpacingX : 75;
            int cellH = view != null && view.SpacingY > 0 ? view.SpacingY : 98;

            // 找当前已占用的最大行
            int maxX = 0, maxY = 0;
            for (int i = 0; i < count; i++)
            {
                if (!matched.Contains(i)) continue;
                Point p = lv.GetItemPosition(i);
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            int startX = maxX + cellW;
            int startY = 0;

            int slot = 0;
            for (int i = 0; i < count; i++)
            {
                if (matched.Contains(i)) continue;
                lv.SetItemPosition(i, startX, startY + slot * cellH);
                slot++;
            }
            if (slot > 0) rep.Notes.Add(Lang.T("未记录的 ") + slot + Lang.T(" 个图标已排到桌面右侧空位。"));
        }

        // =====================================================================
        // 校验 / 差异
        // =====================================================================

        public class DiffItem
        {
            public string Name;
            public string State;   // 新增 / 缺失 / 移动 / 不变
            public Point From;
            public Point To;
        }

        public static List<DiffItem> Diff(Snapshot a, Snapshot b)
        {
            var result = new List<DiffItem>();
            var mapA = new Dictionary<string, IconEntry>(StringComparer.Ordinal);
            var mapB = new Dictionary<string, IconEntry>(StringComparer.Ordinal);
            if (a != null) foreach (var i in a.Icons) mapA[i.Key] = i;
            if (b != null) foreach (var i in b.Icons) mapB[i.Key] = i;

            foreach (var kv in mapB)
            {
                IconEntry oa;
                if (!mapA.TryGetValue(kv.Key, out oa))
                {
                    result.Add(new DiffItem { Name = kv.Value.Name, State = Lang.T("新增"), To = new Point(kv.Value.X, kv.Value.Y) });
                }
                else if (Math.Abs(oa.X - kv.Value.X) > 1 || Math.Abs(oa.Y - kv.Value.Y) > 1)
                {
                    result.Add(new DiffItem
                    {
                        Name = kv.Value.Name,
                        State = Lang.T("移动"),
                        From = new Point(oa.X, oa.Y),
                        To = new Point(kv.Value.X, kv.Value.Y)
                    });
                }
                else
                {
                    result.Add(new DiffItem { Name = kv.Value.Name, State = Lang.T("不变"), To = new Point(kv.Value.X, kv.Value.Y) });
                }
            }
            foreach (var kv in mapA)
            {
                if (!mapB.ContainsKey(kv.Key))
                    result.Add(new DiffItem { Name = kv.Value.Name, State = Lang.T("缺失"), From = new Point(kv.Value.X, kv.Value.Y) });
            }

            result.Sort(delegate(DiffItem x, DiffItem y)
            {
                int r = StateRank(x.State).CompareTo(StateRank(y.State));
                if (r != 0) return r;
                return string.Compare(x.Name, y.Name, StringComparison.CurrentCulture);
            });
            return result;
        }

        private static int StateRank(string s)
        {
            if (s == Lang.T("移动")) return 0;
            if (s == Lang.T("新增")) return 1;
            if (s == Lang.T("缺失")) return 2;
            return 3;
        }

        public static string Describe(RestoreReport r)
        {
            var sb = new StringBuilder(r.Summary());
            foreach (string n in r.Notes) sb.Append("\n· ").Append(n);
            return sb.ToString();
        }
    }
}
