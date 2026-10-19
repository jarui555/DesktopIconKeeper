using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 迷你桌面地图：把虚拟桌面按比例缩放到面板里，每个图标画一个小方块。
    /// 鼠标悬浮显示图标名。
    /// </summary>
    public sealed class PreviewPanel : Control
    {
        private LayoutInfo _layout;
        private List<IconEntry> _icons = new List<IconEntry>();
        private readonly ToolTip _tip = new ToolTip();

        private int _hoverIndex = -1;
        private readonly List<Rectangle> _hitRects = new List<Rectangle>();

        public PreviewPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(32, 34, 38);
            _tip.InitialDelay = 200;
            _tip.ReshowDelay = 100;
        }

        public void SetData(LayoutInfo layout, List<IconEntry> icons)
        {
            _layout = layout;
            _icons = icons ?? new List<IconEntry>();
            _hoverIndex = -1;
            Invalidate();
        }

        public void Clear()
        {
            _layout = null;
            _icons = new List<IconEntry>();
            _hoverIndex = -1;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = -1;
            for (int i = 0; i < _hitRects.Count; i++)
            {
                if (_hitRects[i].Contains(e.Location)) { hit = i; break; }
            }
            if (hit != _hoverIndex)
            {
                _hoverIndex = hit;
                if (hit >= 0 && hit < _icons.Count)
                {
                    IconEntry ic = _icons[hit];
                    _tip.Show(ic.Name + "  (" + ic.X + ", " + ic.Y + ")", this, e.X + 12, e.Y + 12, 2500);
                }
                else
                {
                    _tip.Hide(this);
                }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverIndex = -1;
            _tip.Hide(this);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (_layout == null || _layout.VW <= 0 || _layout.VH <= 0)
            {
                using (var br = new SolidBrush(Color.FromArgb(140, 145, 155)))
                using (var f = new Font("Microsoft YaHei UI", 9f))
                {
                    var sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(Lang.T("选中一个快照查看布局预览"), f, br, ClientRectangle, sf);
                }
                return;
            }

            const int pad = 10;
            int availW = Math.Max(10, Width - pad * 2);
            int availH = Math.Max(10, Height - pad * 2);
            double scale = Math.Min((double)availW / _layout.VW, (double)availH / _layout.VH);

            int drawW = (int)Math.Round(_layout.VW * scale);
            int drawH = (int)Math.Round(_layout.VH * scale);
            int ox = pad + (availW - drawW) / 2;
            int oy = pad + (availH - drawH) / 2;

            // 显示器框
            using (var monPen = new Pen(Color.FromArgb(90, 96, 108), 1f))
            using (var primaryPen = new Pen(Color.FromArgb(120, 160, 220), 1.4f))
            using (var monFill = new SolidBrush(Color.FromArgb(24, 26, 30)))
            {
                foreach (MonitorInfo m in _layout.Monitors)
                {
                    var r = new Rectangle(
                        ox + (int)Math.Round((m.X - _layout.VX) * scale),
                        oy + (int)Math.Round((m.Y - _layout.VY) * scale),
                        Math.Max(2, (int)Math.Round(m.W * scale)),
                        Math.Max(2, (int)Math.Round(m.H * scale)));
                    g.FillRectangle(monFill, r);
                    g.DrawRectangle(m.Primary ? primaryPen : monPen, r);
                }
            }

            // 网格参考
            int cell = _layout.Monitors.Count > 0 ? 75 : 75;
            int cellPx = (int)Math.Round(cell * scale);
            if (cellPx >= 6)
            {
                using (var gridPen = new Pen(Color.FromArgb(20, 255, 255, 255)))
                {
                    for (int x = 0; x <= drawW; x += cellPx)
                        g.DrawLine(gridPen, ox + x, oy, ox + x, oy + drawH);
                    for (int y = 0; y <= drawH; y += cellPx)
                        g.DrawLine(gridPen, ox, oy + y, ox + drawW, oy + y);
                }
            }

            // 图标
            _hitRects.Clear();
            int blockW = Math.Max(4, (int)Math.Round(48 * scale));
            int blockH = Math.Max(4, (int)Math.Round(48 * scale));

            using (var normal = new SolidBrush(Color.FromArgb(120, 190, 255)))
            using (var hover = new SolidBrush(Color.FromArgb(255, 190, 80)))
            {
                for (int i = 0; i < _icons.Count; i++)
                {
                    IconEntry ic = _icons[i];
                    int px = ox + (int)Math.Round((ic.X - _layout.VX) * scale);
                    int py = oy + (int)Math.Round((ic.Y - _layout.VY) * scale);
                    var r = new Rectangle(px, py, blockW, blockH);
                    g.FillRectangle(i == _hoverIndex ? hover : normal, r);
                    _hitRects.Add(r);
                }
            }

            // 坐标轴信息
            using (var br = new SolidBrush(Color.FromArgb(150, 155, 165)))
            using (var f = new Font("Consolas", 8f))
            {
                string info = _layout.Describe() + "   " + _icons.Count.ToString(CultureInfo.InvariantCulture) + Lang.T(" 个图标");
                g.DrawString(info, f, br, pad, Height - 20);
            }
        }
    }
}
