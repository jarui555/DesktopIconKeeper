using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DesktopIconKeeper
{
    /// <summary>桌面上的一个图标：名称 + 同名序号 + 坐标（ListView 客户区坐标）。</summary>
    public class IconEntry
    {
        public string Name = "";
        public int Occurrence;   // 同名图标中的第几个，0 起
        public int X;
        public int Y;

        public string Key { get { return Name + "\u0001" + Occurrence.ToString(CultureInfo.InvariantCulture); } }
        public override string ToString() { return Name + " (" + X + "," + Y + ")"; }
    }

    public class MonitorInfo
    {
        public string Device = "";
        public bool Primary;
        public int X, Y, W, H, Dpi;

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}x{1}@({2},{3}){4}{5}",
                W, H, X, Y, Primary ? Lang.T(" 主屏") : "", Dpi != 96 ? " " + Dpi + "dpi" : "");
        }
    }

    public class LayoutInfo
    {
        public int VX, VY, VW, VH;
        public List<MonitorInfo> Monitors = new List<MonitorInfo>();
        public string Signature = "";

        public string Describe()
        {
            if (Monitors.Count == 0) return VW + "x" + VH;
            var sb = new StringBuilder();
            for (int i = 0; i < Monitors.Count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(Monitors[i].W).Append('x').Append(Monitors[i].H);
            }
            sb.Append(" (").Append(Monitors.Count).Append(Lang.T("屏)"));
            return sb.ToString();
        }

        public string ShortSig()
        {
            if (string.IsNullOrEmpty(Signature)) return "--------";
            return Signature.Length >= 8 ? Signature.Substring(0, 8) : Signature;
        }

        public string Detail()
        {
            var sb = new StringBuilder();
            sb.Append(Lang.T("虚拟桌面 ")).Append(VW).Append('x').Append(VH)
              .Append(" @(").Append(VX).Append(',').Append(VY).Append(")\n");
            sb.Append(Lang.T("布局指纹 ")).Append(ShortSig()).Append('\n');
            for (int i = 0; i < Monitors.Count; i++)
                sb.Append(Lang.T("  显示器 ")).Append(i + 1).Append(": ").Append(Monitors[i].Device).Append("  ")
                  .Append(Monitors[i].Describe()).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }
    }

    public class ViewInfo
    {
        public int SpacingX = 75;
        public int SpacingY = 98;
        public bool AutoArrange;
        public bool SnapToGrid;
    }

    public class Snapshot
    {
        public int Version = 1;
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        public DateTime Created = DateTime.Now;
        public string Kind = SnapshotKind.Manual;
        public bool BoundAutoRestore;
        public string Note = "";
        public LayoutInfo Layout = new LayoutInfo();
        public ViewInfo View = new ViewInfo();
        public List<IconEntry> Icons = new List<IconEntry>();

        public string CreatedText
        {
            get { return Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture); }
        }

        public string KindText
        {
            get
            {
                if (Kind == SnapshotKind.Auto) return Lang.T("自动");
                if (Kind == SnapshotKind.Undo) return Lang.T("撤销点");
                return Lang.T("手动");
            }
        }
    }

    public static class SnapshotKind
    {
        public const string Manual = "manual";
        public const string Auto = "auto";
        public const string Undo = "undo";
    }

    /// <summary>Snapshot &lt;-&gt; JSON 对象模型的双向映射。</summary>
    public static class Codec
    {
        public static Dictionary<string, object> ToJson(Snapshot s)
        {
            var o = J.NewObj();
            o["version"] = (double)s.Version;
            o["id"] = s.Id;
            o["name"] = s.Name;
            o["created"] = s.Created.ToString("o", CultureInfo.InvariantCulture);
            o["kind"] = s.Kind;
            o["boundAutoRestore"] = s.BoundAutoRestore;
            if (!string.IsNullOrEmpty(s.Note)) o["note"] = s.Note;

            var lay = J.NewObj();
            lay["virtualScreen"] = Rect(s.Layout.VX, s.Layout.VY, s.Layout.VW, s.Layout.VH);
            lay["signature"] = s.Layout.Signature;
            var mons = J.NewArr();
            foreach (var m in s.Layout.Monitors)
            {
                var mo = J.NewObj();
                mo["device"] = m.Device;
                mo["primary"] = m.Primary;
                mo["x"] = (double)m.X; mo["y"] = (double)m.Y;
                mo["w"] = (double)m.W; mo["h"] = (double)m.H;
                mo["dpi"] = (double)m.Dpi;
                mons.Add(mo);
            }
            lay["monitors"] = mons;
            o["layout"] = lay;

            var view = J.NewObj();
            view["spacingX"] = (double)s.View.SpacingX;
            view["spacingY"] = (double)s.View.SpacingY;
            view["autoArrange"] = s.View.AutoArrange;
            view["snapToGrid"] = s.View.SnapToGrid;
            o["view"] = view;

            var arr = J.NewArr();
            foreach (var ic in s.Icons)
            {
                var io = J.NewObj();
                io["name"] = ic.Name;
                io["occurrence"] = (double)ic.Occurrence;
                io["x"] = (double)ic.X;
                io["y"] = (double)ic.Y;
                arr.Add(io);
            }
            o["icons"] = arr;
            return o;
        }

        private static Dictionary<string, object> Rect(int x, int y, int w, int h)
        {
            var o = J.NewObj();
            o["x"] = (double)x; o["y"] = (double)y; o["w"] = (double)w; o["h"] = (double)h;
            return o;
        }

        public static Snapshot FromJson(object root)
        {
            var o = J.Obj(root);
            var s = new Snapshot();
            s.Version = J.Int(o.ContainsKey("version") ? o["version"] : null, 1);
            s.Id = J.Str(o.ContainsKey("id") ? o["id"] : null, Guid.NewGuid().ToString("N"));
            s.Name = J.Str(o.ContainsKey("name") ? o["name"] : null, Lang.T("未命名"));
            string created = J.Str(o.ContainsKey("created") ? o["created"] : null, "");
            DateTime dt;
            if (!DateTime.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
                dt = DateTime.Now;
            s.Created = dt;
            s.Kind = J.Str(o.ContainsKey("kind") ? o["kind"] : null, SnapshotKind.Manual);
            s.BoundAutoRestore = J.Bool(o.ContainsKey("boundAutoRestore") ? o["boundAutoRestore"] : null, false);
            s.Note = J.Str(o.ContainsKey("note") ? o["note"] : null, "");

            var lay = J.Obj(o.ContainsKey("layout") ? o["layout"] : null);
            var vs = J.Obj(lay.ContainsKey("virtualScreen") ? lay["virtualScreen"] : null);
            s.Layout.VX = J.Int(vs.ContainsKey("x") ? vs["x"] : null, 0);
            s.Layout.VY = J.Int(vs.ContainsKey("y") ? vs["y"] : null, 0);
            s.Layout.VW = J.Int(vs.ContainsKey("w") ? vs["w"] : null, 0);
            s.Layout.VH = J.Int(vs.ContainsKey("h") ? vs["h"] : null, 0);
            s.Layout.Signature = J.Str(lay.ContainsKey("signature") ? lay["signature"] : null, "");
            foreach (var mo in J.Arr(lay.ContainsKey("monitors") ? lay["monitors"] : null))
            {
                var md = J.Obj(mo);
                var m = new MonitorInfo();
                m.Device = J.Str(md.ContainsKey("device") ? md["device"] : null, "");
                m.Primary = J.Bool(md.ContainsKey("primary") ? md["primary"] : null, false);
                m.X = J.Int(md.ContainsKey("x") ? md["x"] : null, 0);
                m.Y = J.Int(md.ContainsKey("y") ? md["y"] : null, 0);
                m.W = J.Int(md.ContainsKey("w") ? md["w"] : null, 0);
                m.H = J.Int(md.ContainsKey("h") ? md["h"] : null, 0);
                m.Dpi = J.Int(md.ContainsKey("dpi") ? md["dpi"] : null, 96);
                s.Layout.Monitors.Add(m);
            }

            var view = J.Obj(o.ContainsKey("view") ? o["view"] : null);
            s.View.SpacingX = J.Int(view.ContainsKey("spacingX") ? view["spacingX"] : null, 75);
            s.View.SpacingY = J.Int(view.ContainsKey("spacingY") ? view["spacingY"] : null, 98);
            s.View.AutoArrange = J.Bool(view.ContainsKey("autoArrange") ? view["autoArrange"] : null, false);
            s.View.SnapToGrid = J.Bool(view.ContainsKey("snapToGrid") ? view["snapToGrid"] : null, true);

            foreach (var io in J.Arr(o.ContainsKey("icons") ? o["icons"] : null))
            {
                var id = J.Obj(io);
                var ic = new IconEntry();
                ic.Name = J.Str(id.ContainsKey("name") ? id["name"] : null, "");
                ic.Occurrence = J.Int(id.ContainsKey("occurrence") ? id["occurrence"] : null, 0);
                ic.X = J.Int(id.ContainsKey("x") ? id["x"] : null, 0);
                ic.Y = J.Int(id.ContainsKey("y") ? id["y"] : null, 0);
                s.Icons.Add(ic);
            }
            return s;
        }

        public static string Serialize(Snapshot s) { return Json.Write(ToJson(s), true); }
        public static Snapshot Deserialize(string text) { return FromJson(Json.Parse(text)); }
    }
}
