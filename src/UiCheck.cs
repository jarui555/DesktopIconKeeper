using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 界面自检：把所有窗口在不显示的情况下构建出来，强制布局，
    /// 然后逐个控件检查是否越出窗体客户区。
    /// 这类"控件跑到窗口外面"的布局 bug 只有跑一次才能发现。
    /// </summary>
    public static class UiCheck
    {
        private class Finding
        {
            public string Form;
            public string Path;
            public string Bounds;
            public string Problem;
        }

        public static int Run(SnapshotStore store, AppSettings settings)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var findings = new List<Finding>();
            int checkedForms = 0, checkedControls = 0;

            Console.WriteLine("=== 界面自检 ===");
            Console.WriteLine();

            // 先验证检测器本身有效：造一个故意越界的窗体，必须被抓到
            bool detectorOk = NegativeControl();
            Console.WriteLine("  检测器自校验：" + (detectorOk ? "通过（能识别越界控件）" : "失败（检测逻辑无效）"));
            if (!detectorOk)
            {
                Console.WriteLine("结论：检测器失效，自检结果不可信。");
                return Cli.ExitUsage;
            }
            Console.WriteLine();

            checkedForms++; Check(new MainForm(store, settings, true), findings, ref checkedControls);
            checkedForms++; Check(new SettingsForm(settings, store), findings, ref checkedControls);

            Snapshot demo = MakeDemoSnapshot();
            checkedForms++; Check(new RenameForm(demo, settings), findings, ref checkedControls);
            checkedForms++; Check(new DiffForm(demo, demo, "A", "B"), findings, ref checkedControls);

            // 权限不足对话框的两种形态都要检查：能提权（3 按钮）/ 不能提权（2 按钮）
            var fElev = new AccessDeniedForm(SampleDeniedText, true);
            var fNoElev = new AccessDeniedForm(SampleDeniedText, false);

            // 必须先数按钮：Check() 会把窗体 Dispose 掉，之后 Controls 就空了
            int nElev = CountButtons(fElev);
            int nNoElev = CountButtons(fNoElev);

            checkedForms++; Check(fElev, findings, ref checkedControls);
            checkedForms++; Check(fNoElev, findings, ref checkedControls);
            if (nElev != 3)
                findings.Add(new Finding
                {
                    Form = "AccessDeniedForm(可提权)",
                    Path = "按钮数量",
                    Bounds = "-",
                    Problem = "有 " + nElev + " 个按钮，应为 3（打开 / 提权 / 关闭）"
                });
            if (nNoElev != 2)
                findings.Add(new Finding
                {
                    Form = "AccessDeniedForm(不可提权)",
                    Path = "按钮数量",
                    Bounds = "-",
                    Problem = "有 " + nNoElev + " 个按钮，应为 2（打开 / 关闭）；UAC 关闭时不该露出提权按钮"
                });
            Console.WriteLine("  AccessDeniedForm 按钮：可提权 " + nElev + " 个，不可提权 " + nNoElev + " 个");

            Console.WriteLine();
            Console.WriteLine("窗体 " + checkedForms + " 个，控件 " + checkedControls + " 个");

            if (findings.Count == 0)
            {
                Console.WriteLine("结论：所有控件都在窗口范围内。");
                return Cli.ExitOk;
            }

            Console.WriteLine();
            Console.WriteLine("发现 " + findings.Count + " 处越界：");
            Console.WriteLine("{0,-14} {1,-46} {2,-22} {3}", "窗体", "控件路径", "位置/尺寸", "问题");
            Console.WriteLine(new string('-', 110));
            foreach (Finding f in findings)
            {
                Console.WriteLine("{0,-14} {1,-46} {2,-22} {3}",
                    Clip(f.Form, 14), Clip(f.Path, 46), Clip(f.Bounds, 22), f.Problem);
            }
            return Cli.ExitWarn;
        }

        /// <summary>造一个越界控件，确认检测器真的能抓到。</summary>
        private static bool NegativeControl()
        {
            using (var f = new Form())
            {
                f.ClientSize = new Size(200, 100);
                var b = new Button();
                b.SetBounds(180, 90, 100, 50);   // 故意伸出右下角
                f.Controls.Add(b);

                f.CreateControl();
                f.PerformLayout();

                var list = new List<Finding>();
                int counter = 0;
                Walk(f, f, "NegativeControl", 0, 0, f.ClientSize, list, ref counter);
                return list.Count > 0;
            }
        }

        private static void Check(Form form, List<Finding> findings, ref int counter)
        {
            string name = form.GetType().Name;
            form.CreateControl();
            form.PerformLayout();

            Size client = form.ClientSize;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-14} 客户区 {1}x{2}  {3}", name, client.Width, client.Height, form.Text));

            Walk(form, form, name, 0, 0, client, findings, ref counter);

            form.Dispose();
        }

        private static void Walk(Control root, Control parent, string formName, int ox, int oy,
                                 Size client, List<Finding> findings, ref int counter)
        {
            foreach (Control c in parent.Controls)
            {
                counter++;
                int x = ox + c.Left;
                int y = oy + c.Top;

                // 注意：不能按 Control.Visible 过滤。
                // 未 Show() 过的窗体，其所有子控件的 Visible 都是 false，
                // 加了过滤条件整个检查就是空跑（这一点由 NegativeControl 兜底验证）。
                string path = BuildPath(root, c);

                if (x < 0 || y < 0)
                {
                    findings.Add(new Finding
                    {
                        Form = formName,
                        Path = path,
                        Bounds = x + "," + y + " " + c.Width + "x" + c.Height,
                        Problem = "位置为负，跑出左上角"
                    });
                }
                else if (x + c.Width > client.Width || y + c.Height > client.Height)
                {
                    findings.Add(new Finding
                    {
                        Form = formName,
                        Path = path,
                        Bounds = x + "," + y + " " + c.Width + "x" + c.Height,
                        Problem = "右下角越界（客户区 " + client.Width + "x" + client.Height + "）"
                    });
                }

                Walk(root, c, formName, x, y, client, findings, ref counter);
            }
        }

        private static string BuildPath(Control root, Control c)
        {
            var stack = new Stack<string>();
            Control cur = c;
            while (cur != null && cur != root)
            {
                string label = cur.GetType().Name;
                if (cur is Button) label += "(" + ((Button)cur).Text + ")";
                else if (cur is CheckBox) label += "(" + Clip(((CheckBox)cur).Text, 14) + ")";
                else if (cur is GroupBox) label += "(" + ((GroupBox)cur).Text + ")";
                else if (cur is Label) label += "(" + Clip(((Label)cur).Text, 10) + ")";
                stack.Push(label);
                cur = cur.Parent;
            }
            return string.Join("/", stack.ToArray());
        }

        private static string Clip(string s, int max)
        {
            if (s == null) return "";
            s = s.Replace("\n", " ");
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        private static int CountButtons(Control root)
        {
            int n = 0;
            if (root is Button) n++;
            foreach (Control c in root.Controls) n += CountButtons(c);
            return n;
        }

        /// <summary>权限不足对话框的样例正文，长度接近真实内容，用来验证排版不会被撑破。</summary>
        private const string SampleDeniedText =
            "无法打开 explorer.exe (pid 60940) 的进程句柄，Win32 错误码 5。\n\n" +
            "完整性级别：本工具 Low (0x1000)，explorer High (0x3000)。\n\n" +
            "本工具运行在比 explorer 更低的权限里，无法读写它的内存。\n" +
            "常见原因：从沙箱 / 受限环境启动，或 UAC 提权后 explorer 被提升。\n\n" +
            "解决办法（按可靠程度排序）：\n" +
            "  1. 在资源管理器里直接双击 exe —— 不要在沙箱 / 受限面板里启动；\n" +
            "  2. 右键 exe → 以管理员身份运行；\n" +
            "  3.（本机 UAC 已关闭，runas 提权不会生效，请用上面两条。）\n\n" +
            "检测到本进程处于低完整性（沙箱）。这是当前故障的直接原因。";

        private static Snapshot MakeDemoSnapshot()
        {
            var s = new Snapshot();
            s.Name = "自检样例";
            s.Kind = SnapshotKind.Manual;
            s.Created = DateTime.Now;
            s.Layout = new LayoutInfo();
            s.Layout.VX = 0; s.Layout.VY = 0; s.Layout.VW = 1920; s.Layout.VH = 1080;
            s.Layout.Signature = "0123456789";
            s.Layout.Monitors.Add(new MonitorInfo { Device = @"\\.\DISPLAY1", Primary = true, X = 0, Y = 0, W = 1920, H = 1080, Dpi = 96 });
            s.View = new ViewInfo();
            s.Icons.Add(new IconEntry { Name = "此电脑", Occurrence = 0, X = 13, Y = 2 });
            s.Icons.Add(new IconEntry { Name = "回收站", Occurrence = 0, X = 13, Y = 100 });
            s.Icons.Add(new IconEntry { Name = "一个很长的图标名称用来测试列宽是否够用", Occurrence = 0, X = 88, Y = 2 });
            return s;
        }
    }
}
