using System;
using System.Threading;
using System.Windows.Forms;

namespace DesktopIconKeeper
{
    internal static class Program
    {
        public const string Version = "1.3.0";
        private const string MutexName = @"Local\DesktopIconKeeper.SingleInstance";
        private const string ShowEventName = @"Local\DesktopIconKeeper.Show";

        /// <summary>第二个实例喊「把窗口亮出来」用的信号。</summary>
        public const string ShowRequestName = @"Local\DesktopIconKeeper.ShowRequest";

        /// <summary>第一个实例回「我已经亮了」用的信号。</summary>
        public const string ShowAckName = @"Local\DesktopIconKeeper.ShowAck";

        /// <summary>已运行的实例收到这条广播就把主窗口亮出来。</summary>
        public static readonly uint ShowMessage = Native.RegisterWindowMessage("DesktopIconKeeper.ShowMain");

        /// <summary>让已运行的实例确认我还活着，并且我已经把窗口显示出来了。</summary>
        public static void AckShowRequest()
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(ShowEventName))
                    ev.Set();
            }
            catch (Exception) { }
        }

        /// <summary>
        /// 叫醒已经跑着的实例，让它把窗口亮出来。成功返回 true。
        ///
        /// 走两条路：
        ///   1) 命名事件握手 —— 可靠。窗口消息会被 UIPI 拦，窗口隐藏时也不保证送达，
        ///      而同步对象不受这些限制，跨进程跨完整性都能用。
        ///   2) 兜底广播窗口消息 —— 兼容还没有监听线程的旧版本实例。
        /// </summary>
        private static bool WakeExistingInstance()
        {
            // 1) 命名事件握手
            try
            {
                using (var req = EventWaitHandle.OpenExisting(ShowRequestName))
                using (var ack = new EventWaitHandle(false, EventResetMode.AutoReset, ShowAckName))
                {
                    ack.Reset();
                    req.Set();
                    if (ack.WaitOne(2500)) return true;
                }
            }
            catch (Exception) { }

            // 2) 兜底：窗口消息 + 旧版 ack 事件
            try
            {
                using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    ev.Reset();
                    Native.PostMessage(Native.HWND_BROADCAST, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    if (ev.WaitOne(1200)) return true;
                }
            }
            catch (Exception) { }

            return false;
        }

        /// <summary>提权重启后带上的标记，防止提权失败 → 再提权死循环。</summary>
        public const string ElevateFlag = "--elevate-attempt";

        [STAThread]
        private static int Main(string[] args)
        {
            // 语言要赶在任何输出之前定下来 —— 界面和命令行都靠它。
            // 这里顺手把数据目录探出来，RunGui 就不用再探一遍。
            SnapshotStore store = null;
            try
            {
                store = new SnapshotStore(AppDomain.CurrentDomain.BaseDirectory);
                Lang.Current = Lang.Resolve(AppSettings.Load(store.SettingsFile).Language);
            }
            catch (Exception) { }

            // 有参数且不是 GUI 开关 → 走命令行
            if (args.Length > 0)
            {
                string a0 = args[0].TrimStart('-', '/').ToLowerInvariant();
                if (a0 != "tray" && a0 != "minimized" && a0 != "gui" && a0 != "elevate-attempt")
                    return Cli.Run(args);
            }

            return RunGui(args, store);
        }

        private static int RunGui(string[] args, SnapshotStore store)
        {
            bool startMinimized = false;
            bool elevateAttempted = false;
            foreach (string a in args)
            {
                string s = a.TrimStart('-', '/').ToLowerInvariant();
                if (s == "tray" || s == "minimized") startMinimized = true;
                if (s == "elevate-attempt") elevateAttempted = true;
            }

            // 权限自愈：先实测能不能打开桌面视图，确认坏了才重启提权。
            // 只凭低完整性就重启是错的 —— 有时候低完整性也能干活，
            // 而无谓的重启只会让窗口闪一下。
            if (!elevateAttempted)
            {
                bool usable;
                using (DesktopListView probe = DesktopListView.Open())
                    usable = probe != null && probe.IsUsable;

                if (!usable && DesktopListView.LastAccessDenied && Elevation.ElevationWorthTrying &&
                    Elevation.RestartElevated(BuildElevateArgs(args)))
                {
                    return 0;
                }
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 已经有一个实例在跑。先礼后兵：
                    //   1) 叫它把窗口亮出来；
                    //   2) 它要是装死（比如托盘图标注册失败、窗口藏起来找不回来），
                    //      就给用户一个结束它的选项 —— 不能只会说请先退出。
                    if (WakeExistingInstance())
                        return 0;   // 旧实例已经亮出来了，这次启动就到此为止

                    var answer = MessageBox.Show(
                        Lang.T("已经有一个「桌面图标位置记忆工具」在运行，但它的窗口和托盘图标都找不到了。\n\n") +
                        Lang.T("这通常是因为它被以较低权限启动，系统拒绝了它的托盘图标。\n\n") +
                        Lang.T("要结束那个实例并启动新的吗？"),
                        Lang.T("已在运行但不可见"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);

                    if (answer != DialogResult.Yes) return 0;

                    int killed = KillOtherInstances();
                    if (killed <= 0)
                    {
                        MessageBox.Show(
                            Lang.T("无法结束旧实例（可能权限不足）。\n\n") +
                            Lang.T("请打开任务管理器，结束所有 DesktopIconKeeper.exe 后重试。"),
                            Lang.T("结束失败"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 0;
                    }

                    // 旧实例已退出，重新抢一次锁
                    return RunGui(args, store);
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (store == null) store = new SnapshotStore(AppDomain.CurrentDomain.BaseDirectory);
                var settings = AppSettings.Load(store.SettingsFile);

                Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
                {
                    LogCrash(store, e.Exception);
                    MessageBox.Show(Lang.T("发生未处理的错误：\n\n") + e.Exception.Message + Lang.T("\n\n详情已写入 ") +
                        System.IO.Path.Combine(store.RootDir, "crash.log"), Lang.T("桌面图标位置记忆工具"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                {
                    LogCrash(store, e.ExceptionObject as Exception);
                };

                if (startMinimized || settings.StartMinimized)
                {
                    var form = new MainForm(store, settings);

                    // 关键：Application.Run(form) 自己会把窗口显示出来，
                    // 所以在它之前调 Hide() 是白费 —— 窗口照样会弹出来。
                    // 必须等 Shown 之后才藏，这时才藏得住。
                    form.Shown += delegate
                    {
                        if (form.TrayAvailable)
                        {
                            // 托盘确实能用，才允许它缩进托盘
                            form.ShowInTaskbar = false;
                            form.WindowState = FormWindowState.Minimized;
                            form.HideToTray();
                        }
                        else
                        {
                            // 托盘用不了 → 留在任务栏最小化，至少点得开
                            form.ShowInTaskbar = true;
                            form.WindowState = FormWindowState.Minimized;
                        }
                    };

                    Application.Run(form);
                    return 0;
                }

                using (var form = new MainForm(store, settings))
                {
                    Application.Run(form);
                }
                return 0;
            }
        }

        /// <summary>结束其它同名的 DesktopIconKeeper 进程，返回成功结束的个数。</summary>
        private static int KillOtherInstances()
        {
            int self = (int)Native.GetCurrentProcessId();
            int killed = 0;
            try
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("DesktopIconKeeper"))
                {
                    if (p.Id == (int)self) continue;
                    try
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                        killed++;
                    }
                    catch (Exception) { }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception) { }

            if (killed > 0) System.Threading.Thread.Sleep(400);   // 等内核回收互斥锁
            return killed;
        }

        private static string BuildElevateArgs(string[] args)
        {
            var sb = new System.Text.StringBuilder(ElevateFlag);
            foreach (string a in args) sb.Append(' ').Append(a);
            return sb.ToString();
        }

        private static void LogCrash(SnapshotStore store, Exception ex)
        {
            if (ex == null) return;
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(store.RootDir, "crash.log"),
                    "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====\n" + ex + "\n\n",
                    new System.Text.UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }
}
