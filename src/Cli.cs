using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 命令行模式。可被计划任务 / 批处理调用：
    ///   DesktopIconKeeper.exe save 工作布局
    ///   DesktopIconKeeper.exe restore --last
    ///   DesktopIconKeeper.exe list
    ///   DesktopIconKeeper.exe doctor
    /// </summary>
    public static class Cli
    {
        public const int ExitOk = 0;
        public const int ExitWarn = 1;      // 完成但有偏差
        public const int ExitNoTarget = 2;  // 找不到桌面视图
        public const int ExitNotFound = 3;  // 找不到快照
        public const int ExitBlocked = 4;   // 自动排列阻止了还原
        public const int ExitUsage = 5;

        public static int Run(string[] args)
        {
            ConsoleHost.Attach();

            try
            {
                return Dispatch(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(Lang.T("执行失败：") + ex.Message);
                if (ex is UnauthorizedAccessException)
                    Console.Error.WriteLine(Lang.T("提示：数据目录不可写。可在 exe 同目录放一个 portable.txt，改为绿色模式把数据写到程序旁边。"));
                return ExitUsage;
            }
        }

        private static int Dispatch(string[] args)
        {
            string cmd = args[0].TrimStart('-', '/').ToLowerInvariant();
            var store = new SnapshotStore(AppDomain.CurrentDomain.BaseDirectory);
            var settings = AppSettings.Load(store.SettingsFile);

            switch (cmd)
            {
                case "help": case "h": case "?": return Help();
                case "version": case "v":
                    Console.WriteLine(Lang.T("桌面图标位置记忆工具 v") + Program.Version);
                    return ExitOk;
                case "save": case "record": case "capture": return Save(store, args);
                case "restore": case "load": case "apply": return Restore(store, settings, args);
                case "list": case "ls": return List(store);
                case "diff": return Diff(store, args);
                case "doctor": case "diag": case "check": return Doctor(store, settings);
                case "uicheck": return UiCheck.Run(store, settings);
                case "denied-preview": return DeniedPreview();
                case "settings-preview": return SettingsPreview(store, settings);
                case "icons": return Icons(args);
                case "delete": case "rm": return Delete(store, args);
                case "export": return Export(store, args);
                default:
                    Console.Error.WriteLine(Lang.T("未知命令：") + args[0]);
                    Help();
                    return ExitUsage;
            }
        }

        /// <summary>
        /// 只显示设置窗口，用于排版预览和截图回归。
        /// </summary>
        private static int SettingsPreview(SnapshotStore store, AppSettings settings)
        {
            System.Windows.Forms.Application.EnableVisualStyles();
            using (var f = new SettingsForm(settings, store)) f.ShowDialog();
            return ExitOk;
        }

        /// <summary>
        /// icons [hide|show|toggle|status] —— 隐藏 / 显示桌面图标。
        ///
        /// 只切换 SysListView32 的 WS_VISIBLE，不动文件也不动快照，
        /// 所以随时可以来回切，和桌面右键 → 查看 → 显示桌面图标等价。
        /// </summary>
        private static int Icons(string[] args)
        {
            string what = args.Length > 1 ? args[1].TrimStart('-', '/').ToLowerInvariant() : "status";

            using (DesktopListView lv = DesktopListView.Open())
            {
                if (lv == null || !lv.IsUsable)
                {
                    Console.Error.WriteLine(string.IsNullOrEmpty(DesktopListView.LastOpenError)
                        ? Lang.T("桌面图标视图不可用。")
                        : DesktopListView.LastOpenError);
                    return ExitNoTarget;
                }

                bool hidden = lv.IconsHidden;

                if (what == "hide" || what == "off") { lv.SetIconsHidden(true); hidden = true; }
                else if (what == "show" || what == "on") { lv.SetIconsHidden(false); hidden = false; }
                else if (what == "toggle") { hidden = !hidden; lv.SetIconsHidden(hidden); }
                else if (what != "status")
                {
                    Console.Error.WriteLine(Lang.T("用法：icons [hide|show|toggle|status]"));
                    return ExitUsage;
                }

                Console.WriteLine(hidden ? Lang.T("桌面图标：已隐藏") : Lang.T("桌面图标：已显示"));
                return ExitOk;
            }
        }

        /// <summary>
        /// 只显示「权限不足」对话框，用于排版预览和截图回归。
        /// 不读不写桌面，纯粹是界面工装。
        /// </summary>
        private static int DeniedPreview()
        {
            string detail;
            using (DesktopListView probe = DesktopListView.Open()) { }
            detail = DesktopListView.LastOpenError;
            if (string.IsNullOrEmpty(detail))
                detail = Lang.T("（当前环境权限正常，以下是排版预览。）\n\n") +
                         Lang.T("无法打开 explorer.exe (pid 0) 的进程句柄，Win32 错误码 5。\n\n") +
                         Lang.T("完整性级别：本工具 Low (0x1000)，explorer High (0x3000)。\n\n") +
                         Lang.T("本工具运行在比 explorer 更低的权限里，无法读写它的内存。\n") +
                         Lang.T("常见原因：从沙箱 / 受限环境启动，或 UAC 提权后 explorer 被提升。");

            System.Windows.Forms.Application.EnableVisualStyles();
            using (var f = new AccessDeniedForm(detail, Elevation.ElevationWorthTrying))
                f.ShowDialog();
            return ExitOk;
        }

        private static int Help()
        {
            Console.WriteLine(Lang.T("桌面图标位置记忆工具 v") + Program.Version + Lang.T(" —— 命令行模式"));
            Console.WriteLine();
            Console.WriteLine(Lang.T("  save <名称>            记录当前桌面图标布局"));
            Console.WriteLine(Lang.T("  restore <名称>         还原指定快照"));
            Console.WriteLine(Lang.T("  restore --last         还原最近一次手动快照"));
            Console.WriteLine(Lang.T("  list                   列出所有快照"));
            Console.WriteLine(Lang.T("  diff <A> <B>           对比两个快照（名称，或 --current 表示当前桌面）"));
            Console.WriteLine(Lang.T("  delete <名称>          删除快照"));
            Console.WriteLine(Lang.T("  export <名称> <文件>   导出快照为 JSON"));
            Console.WriteLine(Lang.T("  doctor                 输出环境诊断（布局 / 句柄 / 图标数 / 视图状态）"));
            Console.WriteLine(Lang.T("  icons [hide|show|toggle|status]  隐藏 / 显示桌面图标"));
            Console.WriteLine(Lang.T("  uicheck                界面自检（构建所有窗口并检查控件是否越界）"));
            Console.WriteLine(Lang.T("  version                显示版本"));
            Console.WriteLine();
            Console.WriteLine(Lang.T("不带参数运行则打开图形界面。"));
            Console.WriteLine();
            Console.WriteLine(Lang.T("退出码：0 成功 / 1 完成但有偏差 / 2 桌面视图不可用 / 3 找不到快照 / 4 被自动排列阻止 / 5 用法错误"));
            return ExitOk;
        }

        // =====================================================================

        private static int Save(SnapshotStore store, string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine(Lang.T("用法：save <名称>"));
                return ExitUsage;
            }
            string name = args[1];

            using (DesktopListView lv = DesktopListView.Open())
            {
                if (lv == null || !lv.IsUsable)
                {
                    Console.Error.WriteLine(string.IsNullOrEmpty(DesktopListView.LastOpenError)
                        ? Lang.T("无法访问桌面图标视图。") : DesktopListView.LastOpenError);
                    return ExitNoTarget;
                }

                Snapshot s = IconEngine.Capture(lv, name, SnapshotKind.Manual);
                if (s.Icons.Count == 0)
                {
                    Console.Error.WriteLine(Lang.T("没有捕获到任何图标，请确认桌面图标处于显示状态。"));
                    return ExitWarn;
                }
                store.Save(s);

                Console.WriteLine(Lang.T("已记录：") + name);
                Console.WriteLine(Lang.T("  图标数量 ") + s.Icons.Count);
                Console.WriteLine(Lang.T("  显示器布局 ") + s.Layout.Describe() + Lang.T("  指纹 ") + s.Layout.ShortSig());
                Console.WriteLine(Lang.T("  图标间距 ") + s.View.SpacingX + " x " + s.View.SpacingY +
                                  Lang.T("，自动排列 ") + (s.View.AutoArrange ? Lang.T("开") : Lang.T("关")));
                return ExitOk;
            }
        }

        private static int Restore(SnapshotStore store, AppSettings settings, string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine(Lang.T("用法：restore <名称> | restore --last"));
                return ExitUsage;
            }
            string want = args[1];

            List<Snapshot> all = store.LoadAll();
            Snapshot target = null;
            if (want == "--last" || want == "-l")
            {
                foreach (Snapshot s in all) { if (s.Kind == SnapshotKind.Manual) { target = s; break; } }
                if (target == null && all.Count > 0) target = all[0];
            }
            else
            {
                foreach (Snapshot s in all)
                {
                    if (string.Equals(s.Name, want, StringComparison.OrdinalIgnoreCase)) { target = s; break; }
                }
            }

            if (target == null)
            {
                Console.Error.WriteLine(Lang.T("找不到快照：") + want);
                if (all.Count == 0) Console.Error.WriteLine(Lang.T("（当前没有任何快照，先用 save 记录一个）"));
                else
                {
                    Console.Error.WriteLine(Lang.T("现有快照："));
                    foreach (Snapshot s in all) Console.Error.WriteLine("  " + s.Name);
                }
                return ExitNotFound;
            }

            using (DesktopListView lv = DesktopListView.Open())
            {
                if (lv == null || !lv.IsUsable)
                {
                    Console.Error.WriteLine(string.IsNullOrEmpty(DesktopListView.LastOpenError)
                        ? Lang.T("无法访问桌面图标视图。") : DesktopListView.LastOpenError);
                    return ExitNoTarget;
                }

                try
                {
                    Snapshot undo = IconEngine.Capture(lv, Lang.T("还原前 ") + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), SnapshotKind.Undo);
                    store.SaveUndo(undo);
                }
                catch (Exception) { }

                RestoreReport rep = IconEngine.Restore(lv, target, settings, settings.ScaleAcrossLayouts);
                Console.WriteLine(IconEngine.Describe(rep));

                if (rep.AutoArrangeBlocked) return ExitBlocked;
                return rep.Mismatched > 0 ? ExitWarn : ExitOk;
            }
        }

        private static int List(SnapshotStore store)
        {
            List<Snapshot> all = store.LoadAll();
            if (all.Count == 0)
            {
                Console.WriteLine(Lang.T("（没有快照）"));
                return ExitOk;
            }

            Console.WriteLine("{0,-28} {1,-6} {2,-20} {3,-18} {4,5}  {5}",
                Lang.T("名称"), Lang.T("类型"), Lang.T("记录时间"), Lang.T("布局"), Lang.T("图标"), Lang.T("自动还原"));
            Console.WriteLine(new string('-', 100));
            foreach (Snapshot s in all)
            {
                Console.WriteLine("{0,-28} {1,-6} {2,-20} {3,-18} {4,5}  {5}",
                    Pad(s.Name, 28), s.KindText, s.CreatedText,
                    s.Layout != null ? s.Layout.Describe() : "-",
                    s.Icons.Count,
                    s.BoundAutoRestore ? Lang.T("绑定") : "");
            }
            Console.WriteLine();
            Console.WriteLine(Lang.T("数据目录：") + store.RootDir);
            return ExitOk;
        }

        private static int Diff(SnapshotStore store, string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine(Lang.T("用法：diff <A> <B>   （--current 表示当前桌面）"));
                return ExitUsage;
            }

            Snapshot a = Resolve(store, args[1]);
            Snapshot b = Resolve(store, args[2]);
            if (a == null || b == null) return ExitNotFound;

            List<IconEngine.DiffItem> diffs = IconEngine.Diff(a, b);
            foreach (IconEngine.DiffItem d in diffs)
            {
                if (d.State == Lang.T("不变")) continue;
                if (d.State == Lang.T("移动"))
                    Console.WriteLine(Lang.T("移动  {0,-40} ({1},{2}) -> ({3},{4})"), Pad(d.Name, 40), d.From.X, d.From.Y, d.To.X, d.To.Y);
                else if (d.State == Lang.T("新增"))
                    Console.WriteLine(Lang.T("新增  {0,-40} ({1},{2})"), Pad(d.Name, 40), d.To.X, d.To.Y);
                else
                    Console.WriteLine(Lang.T("缺失  {0,-40} ({1},{2})"), Pad(d.Name, 40), d.From.X, d.From.Y);
            }
            return ExitOk;
        }

        private static Snapshot Resolve(SnapshotStore store, string name)
        {
            if (name == "--current" || name == "-c")
            {
                using (DesktopListView lv = DesktopListView.Open())
                {
                    if (lv == null || !lv.IsUsable)
                    {
                        Console.Error.WriteLine(Lang.T("无法访问桌面图标视图。"));
                        return null;
                    }
                    return IconEngine.Capture(lv, Lang.T("当前桌面"), SnapshotKind.Manual);
                }
            }
            foreach (Snapshot s in store.LoadAll())
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            Console.Error.WriteLine(Lang.T("找不到快照：") + name);
            return null;
        }

        private static int Delete(SnapshotStore store, string[] args)
        {
            if (args.Length < 2) { Console.Error.WriteLine(Lang.T("用法：delete <名称>")); return ExitUsage; }
            foreach (Snapshot s in store.LoadAll())
            {
                if (string.Equals(s.Name, args[1], StringComparison.OrdinalIgnoreCase))
                {
                    if (!store.Delete(s))
                    {
                        Console.Error.WriteLine(Lang.T("删除失败（文件可能被占用）：") + s.Name);
                        return ExitWarn;
                    }
                    Console.WriteLine(Lang.T("已删除：") + s.Name);
                    return ExitOk;
                }
            }
            Console.Error.WriteLine(Lang.T("找不到快照：") + args[1]);
            return ExitNotFound;
        }

        private static int Export(SnapshotStore store, string[] args)
        {
            if (args.Length < 3) { Console.Error.WriteLine(Lang.T("用法：export <名称> <文件路径>")); return ExitUsage; }
            foreach (Snapshot s in store.LoadAll())
            {
                if (string.Equals(s.Name, args[1], StringComparison.OrdinalIgnoreCase))
                {
                    store.Export(s, args[2]);
                    Console.WriteLine(Lang.T("已导出：") + Path.GetFullPath(args[2]));
                    return ExitOk;
                }
            }
            Console.Error.WriteLine(Lang.T("找不到快照：") + args[1]);
            return ExitNotFound;
        }

        private static int Doctor(SnapshotStore store, AppSettings settings)
        {
            Console.WriteLine(Lang.T("=== 桌面图标位置记忆工具 · 环境诊断 ==="));
            Console.WriteLine(Lang.T("版本         v") + Program.Version + "  (" + (Environment.Is64BitProcess ? "x64" : "x86") + Lang.T(" 进程)"));
            Console.WriteLine(Lang.T("系统         ") + Environment.OSVersion.VersionString);
            Console.WriteLine(Lang.T("数据目录     ") + store.RootDir + (store.Portable ? Lang.T("  [绿色模式]") : ""));
            Console.WriteLine(Lang.T("快照目录     ") + store.SnapshotDir);
            Console.WriteLine(Lang.T("快照数量     ") + store.LoadAll().Count);

            int selfRid = Integrity.SelfRid;
            Console.WriteLine(Lang.T("本进程完整性 ") + (selfRid < 0 ? Lang.T("读取失败") : Integrity.NameOf(selfRid) + " (0x" + selfRid.ToString("X4", CultureInfo.InvariantCulture) + ")"));
            Console.WriteLine("UAC          " + (Elevation.UacEnabled ? Lang.T("已开启") : Lang.T("已关闭（runas 提权不生效）")));
            bool trayOk = Elevation.TrayReachable();
            Console.WriteLine(Lang.T("托盘图标     ") + (trayOk ? Lang.T("可注册") : Lang.T("不可用（低完整性下会被 UIPI 拦，关闭窗口将直接退出）")));
            if (selfRid >= 0 && selfRid < Integrity.Medium)
                Console.WriteLine(Lang.T("             低完整性环境，可能无法访问高完整性的 explorer；若确实失败请以管理员身份运行。"));
            Console.WriteLine();

            LayoutInfo li = LayoutProbe.Capture();
            Console.WriteLine(Lang.T("--- 显示器布局 ---"));
            Console.WriteLine(li.Detail());
            Console.WriteLine();

            Console.WriteLine(Lang.T("--- 桌面图标视图 ---"));
            IntPtr lv = DesktopListView.FindDesktopListView(out string err);
            Console.WriteLine(Lang.T("SysListView32 句柄   0x") + lv.ToInt64().ToString("X", CultureInfo.InvariantCulture));
            if (lv == IntPtr.Zero)
            {
                Console.WriteLine(Lang.T("状态                不可用"));
                if (!string.IsNullOrEmpty(err)) Console.WriteLine(err);
                return ExitNoTarget;
            }

            using (DesktopListView view = DesktopListView.Open())
            {
                if (view == null || !view.IsUsable)
                {
                    Console.WriteLine(Lang.T("状态                打开失败"));
                    Console.WriteLine(DesktopListView.LastOpenError);
                    return ExitNoTarget;
                }

                int count = view.GetItemCount();
                var sp = view.GetItemSpacing();
                int expRid = Integrity.GetProcessRid((uint)view.ProcessId);
                Console.WriteLine(Lang.T("explorer 进程号     ") + view.ProcessId);
                Console.WriteLine(Lang.T("explorer 完整性     ") + (expRid < 0 ? Lang.T("读取失败") : Integrity.NameOf(expRid) + " (0x" + expRid.ToString("X4", CultureInfo.InvariantCulture) + ")"));
                Console.WriteLine(Lang.T("图标数量            ") + count);
                Console.WriteLine(Lang.T("图标间距            ") + sp.Width + " x " + sp.Height);
                Console.WriteLine(Lang.T("自动排列图标        ") + (view.AutoArrange ? Lang.T("开（会阻止还原）") : Lang.T("关")));
                Console.WriteLine(Lang.T("对齐到网格          ") + (LayoutProbe.CaptureView(view).SnapToGrid ? Lang.T("开") : Lang.T("关")));
                Console.WriteLine();

                Console.WriteLine(Lang.T("--- 前 10 个图标 ---"));
                var icons = view.ReadAll();
                int n = Math.Min(10, icons.Count);
                for (int i = 0; i < n; i++)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,4}  {1,-40} ({2,5}, {3,5})",
                        i + 1, Pad(icons[i].Name, 40), icons[i].X, icons[i].Y));
                if (icons.Count > n) Console.WriteLine(Lang.T("  … 其余 ") + (icons.Count - n) + Lang.T(" 个省略"));

                Console.WriteLine();
                if (view.AutoArrange)
                {
                    Console.WriteLine(Lang.T("结论：可以读取，但还原会被“自动排列图标”阻止。"));
                    Console.WriteLine(Lang.T("      在设置里勾选“还原时自动关闭桌面自动排列”，或在桌面右键 → 查看 中手动关闭。"));
                    return ExitWarn;
                }
                Console.WriteLine(Lang.T("结论：读写通道正常，可以记录与还原。"));
                return ExitOk;
            }
        }

        private static string Pad(string s, int width)
        {
            if (s == null) s = "";
            int w = 0;
            foreach (char c in s) w += c > 0x2E80 ? 2 : 1;
            if (w >= width) return s;
            return s + new string(' ', width - w);
        }
    }

    /// <summary>让 WinExe 也能输出到调用它的控制台（或被重定向的管道）。</summary>
    internal static class ConsoleHost
    {
        private static bool _attached;

        public static void Attach()
        {
            if (_attached) return;
            _attached = true;

            bool redirected = false;
            try { redirected = Console.IsOutputRedirected; }
            catch (Exception) { }

            if (!redirected)
            {
                if (!Native.AttachConsole(Native.ATTACH_PARENT_PROCESS))
                    Native.AllocConsole();
                try { Console.OutputEncoding = new UTF8Encoding(false); }
                catch (Exception) { }
            }

            // 无论哪种情况都用无 BOM 的 UTF-8 写标准输出，保证中文不乱码
            try
            {
                var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                stdout.AutoFlush = true;
                Console.SetOut(stdout);

                var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
                stderr.AutoFlush = true;
                Console.SetError(stderr);
            }
            catch (Exception) { }
        }
    }
}
