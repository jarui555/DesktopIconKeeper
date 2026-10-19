using System;
using System.Collections.Generic;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 中文原文 → 英文译文。
    ///
    /// 只放英文这一列：中文就是 key，不用维护。漏翻的条目会原样显示中文，
    /// 一眼就能看出来，不会静默变成空白。
    /// </summary>
    internal static class LangTable
    {
        internal static void Build(Dictionary<string, string> en)
        {
            // ---- 命令行帮助 ----
            A(en, "  delete <名称>          删除快照", "  delete <name>          Delete a snapshot");
            A(en, "  diff <A> <B>           对比两个快照（名称，或 --current 表示当前桌面）", "  diff <A> <B>           Compare two snapshots (a name, or --current for the live desktop)");
            A(en, "  doctor                 输出环境诊断（布局 / 句柄 / 图标数 / 视图状态）", "  doctor                 Print environment diagnostics (layout / handle / icon count / view state)");
            A(en, "  export <名称> <文件>   导出快照为 JSON", "  export <name> <file>   Export a snapshot as JSON");
            A(en, "  list                   列出所有快照", "  list                   List all snapshots");
            A(en, "  restore <名称>         还原指定快照", "  restore <name>         Restore a snapshot");
            A(en, "  restore --last         还原最近一次手动快照", "  restore --last         Restore the most recent manual snapshot");
            A(en, "  save <名称>            记录当前桌面图标布局", "  save <name>            Save the current desktop icon layout");
            A(en, "  uicheck                界面自检（构建所有窗口并检查控件是否越界）", "  uicheck                UI self-check (build every form, verify no control overflows)");
            A(en, "  version                显示版本", "  version                Show version");
            A(en, " —— 命令行模式", " — command-line mode");
            A(en, "不带参数运行则打开图形界面。", "Run without arguments to open the GUI.");
            A(en, "退出码：0 成功 / 1 完成但有偏差 / 2 桌面视图不可用 / 3 找不到快照 / 4 被自动排列阻止 / 5 用法错误",
                 "Exit codes: 0 ok / 1 done with deviations / 2 desktop view unavailable / 3 snapshot not found / 4 blocked by auto-arrange / 5 usage error");
            A(en, "用法：delete <名称>", "Usage: delete <name>");
            A(en, "用法：diff <A> <B>   （--current 表示当前桌面）", "Usage: diff <A> <B>   (--current means the live desktop)");
            A(en, "用法：export <名称> <文件路径>", "Usage: export <name> <file path>");
            A(en, "用法：restore <名称> | restore --last", "Usage: restore <name> | restore --last");
            A(en, "用法：save <名称>", "Usage: save <name>");
            A(en, "未知命令：", "Unknown command: ");

            // ---- doctor 输出 ----
            A(en, "=== 桌面图标位置记忆工具 · 环境诊断 ===", "=== Desktop Icon Keeper · Environment diagnostics ===");
            A(en, "版本         v", "Version      v");
            A(en, "系统         ", "System       ");
            A(en, "数据目录     ", "Data dir     ");
            A(en, "快照目录     ", "Snapshot dir ");
            A(en, "快照数量     ", "Snapshots    ");
            A(en, "本进程完整性 ", "Process integrity   ");
            A(en, "托盘图标     ", "Tray icon    ");
            A(en, "已开启", "on");
            A(en, "已关闭（runas 提权不生效）", "off (runas elevation does nothing)");
            A(en, "可注册", "available");
            A(en, "不可用（低完整性下会被 UIPI 拦，关闭窗口将直接退出）", "unavailable (UIPI blocks it at low integrity; closing the window will exit)");
            A(en, "             低完整性环境，可能无法访问高完整性的 explorer；若确实失败请以管理员身份运行。",
                 "             Low integrity: the high-integrity explorer may be unreachable. Run as administrator if it fails.");
            A(en, "--- 显示器布局 ---", "--- display layout ---");
            A(en, "虚拟桌面 ", "Virtual desktop ");
            A(en, "布局指纹 ", "Layout signature ");
            A(en, " 主屏", " primary");
            A(en, "  显示器 ", "  monitor ");
            A(en, "--- 桌面图标视图 ---", "--- desktop icon view ---");
            A(en, "SysListView32 句柄   0x", "SysListView32 hwnd  0x");
            A(en, "explorer 进程号     ", "explorer pid        ");
            A(en, "explorer 完整性     ", "explorer integrity  ");
            A(en, "状态                不可用", "State               unavailable");
            A(en, "状态                打开失败", "State               open failed");
            A(en, "图标数量            ", "Icon count          ");
            A(en, "图标间距            ", "Icon spacing        ");
            A(en, "自动排列图标        ", "Auto arrange icons  ");
            A(en, "对齐到网格          ", "Snap to grid        ");
            A(en, "开（会阻止还原）", "On (blocks restore)");
            A(en, "--- 前 10 个图标 ---", "--- first 10 icons ---");
            A(en, "--- 图标坐标（前 60 个）---", "--- icon positions (first 60) ---");
            A(en, "  … 其余 ", "  … and ");
            A(en, "  个省略", " more omitted");
            A(en, "（当前环境权限正常，以下是排版预览。）\n\n", "(Permissions are fine here — the following is a layout preview.)\n\n");
            A(en, "结论：读写通道正常，可以记录与还原。", "Result: read/write channel is healthy — save and restore will work.");
            A(en, "结论：可以读取，但还原会被“自动排列图标”阻止。", "Result: readable, but restore will be blocked by \"auto arrange icons\".");
            A(en, "请先关闭桌面“查看 → 自动排列图标”，或在设置中允许本工具自动关闭。",
                 "Turn off desktop \"View → Auto arrange icons\", or let this tool do it in Settings.");
            A(en, "已自动关闭“自动排列图标”。", "Turned off \"auto arrange icons\".");
            A(en, "（绿色模式）", "(portable mode)");
            A(en, "  [绿色模式]", "  [Portable]");
            A(en, "  ·  指纹 ", "  ·  signature ");
            A(en, "  · explorer.exe 未运行或正在重启\n", "  · explorer.exe is not running or is restarting\n");
            A(en, "  · 桌面被 Fences 等第三方工具接管", "  · the desktop is taken over by a third-party tool such as Fences");
            A(en, "  · 桌面图标已被隐藏（桌面右键 → 查看 → 显示桌面图标）\n",
                 "  · desktop icons are hidden (right-click desktop → View → Show desktop icons)\n");
            A(en, "找不到桌面图标视图 (SysListView32)。可能原因：\n", "Cannot find the desktop icon view (SysListView32). Likely causes:\n");
            A(en, "桌面图标视图不可用。", "Desktop icon view unavailable.");
            A(en, "无法访问桌面图标视图。", "Cannot access the desktop icon view.");
            A(en, "无法访问桌面图标：", "Cannot access desktop icons: ");
            A(en, "无法取得桌面 ListView 所属进程。", "Cannot determine which process owns the desktop ListView.");
            A(en, "在 explorer.exe 内分配中转缓冲失败，Win32 错误码 ", "Failed to allocate a relay buffer inside explorer.exe, Win32 error ");

            // ---- 权限 / UIPI 诊断 ----
            A(en, "无法打开 explorer.exe (pid ", "Cannot open the explorer.exe (pid ");
            A(en, ") 的进程句柄，Win32 错误码 ", ") process handle, Win32 error ");
            A(en, "无法打开 explorer.exe (pid 0) 的进程句柄，Win32 错误码 5。\n\n",
                 "Cannot open the explorer.exe (pid 0) process handle, Win32 error 5.\n\n");
            A(en, "无法读写桌面图标：本工具 ", "Cannot read/write desktop icons: this tool ");
            A(en, "\n\n完整性级别：本工具 ", "\n\nIntegrity: this tool ");
            A(en, "\n\n无法读取完整性级别（自身 ", "\n\nCannot read integrity levels (self ");
            A(en, "完整性级别：本工具 Low (0x1000)，explorer High (0x3000)。\n\n",
                 "Integrity: this tool Low (0x1000), explorer High (0x3000).\n\n");
            A(en, "本工具运行在比 explorer 更低的权限里，无法读写它的内存。\n",
                 "This tool runs at a lower integrity level than explorer and cannot read its memory.\n");
            A(en, "\n\n本工具运行在比 explorer 更低的权限里，无法读写它的内存。",
                 "\n\nThis tool runs at a lower integrity level than explorer and cannot read its memory.");
            A(en, "\n\n进程句柄能打开，但 Windows 的 UIPI 会把本工具发往 explorer 的窗口消息",
                 "\n\nThe process handle opens fine, but Windows UIPI silently drops every window message this tool");
            A(en, "\n（LVM_GETITEMCOUNT / LVM_SETITEMPOSITION 等）静默丢弃 —— 全部返回 0。",
                 "\n(LVM_GETITEMCOUNT / LVM_SETITEMPOSITION etc.) sends to explorer — they all return 0.");
            A(en, "\n继续下去只会得到\"桌面有 0 个图标\"的假结果，甚至存下一个空快照，",
                 "\nContinuing would only produce a fake \"0 icons on the desktop\" result, or even save an empty snapshot,");
            A(en, "\n所以这里直接停下，不做任何读写。", "\nso it stops here and touches nothing.");
            A(en, "\n\n检测到本进程处于低完整性（沙箱）。这是当前故障的直接原因。",
                 "\n\nThis process is running at low integrity (sandboxed). That is the direct cause of the failure.");
            A(en, "\n常见原因：从沙箱 / 受限环境启动，或 UAC 提权后 explorer 被提升。",
                 "\nCommon causes: launched from a sandbox / restricted environment, or explorer was elevated by UAC.");
            A(en, "常见原因：从沙箱 / 受限环境启动，或 UAC 提权后 explorer 被提升。",
                 "Common causes: launched from a sandbox / restricted environment, or explorer was elevated by UAC.");
            A(en, "\n\n解决办法（按可靠程度排序）：", "\n\nFixes, most reliable first:");
            A(en, "\n  1. 在资源管理器里直接双击 exe —— explorer 会用自己的高权限令牌启动它；",
                 "\n  1. Double-click the exe in File Explorer — explorer launches it with its own higher-privilege token;");
            A(en, "\n  1. 在资源管理器里直接双击 exe —— 不要在沙箱 / 受限面板里启动；",
                 "\n  1. Double-click the exe in File Explorer — do not launch it from a sandbox / restricted panel;");
            A(en, "\n  2. 右键 exe → 以管理员身份运行；", "\n  2. Right-click the exe → Run as administrator;");
            A(en, "\n  3. 点下面的「以管理员身份重启」。", "\n  3. Click \"Restart as administrator\" below.");
            A(en, "\n  3.（本机 UAC 已关闭，runas 提权不会生效，请用上面两条。）",
                 "\n  3. (UAC is off on this machine, so runas elevation does nothing — use the two options above.)");
            A(en, "\n\n两边级别看起来够用，仍被拒绝多半是安全软件拦截。",
                 "\n\nBoth levels look sufficient, so the denial is most likely security software.");
            A(en, "\n可尝试：以管理员身份重启，或把本工具加入杀软白名单。",
                 "\nTry: restart as administrator, or whitelist this tool in your antivirus.");
            A(en, "\n请右键本工具选择“以管理员身份运行”。", "\nRight-click this tool and choose \"Run as administrator\".");
            A(en, "本进程完整性级别 ≥ explorer 完整性级别", "This tool's integrity level ≥ explorer's");
            A(en, "错误码 5", "error 5");
            A(en, "低完整性", "Low integrity");
            A(en, "读取失败", "read failed");
            A(en, "未知原因", "unknown cause");
            A(en, "没权限", "no permission");
            A(en, "能提权", "can elevate");
            A(en, "要不要提权重启", "elevate or not");
            A(en, "托盘够得着吗", "tray reachable");
            A(en, "用户点一下就能解决", "one click fixes it");

            // ---- 主界面 ----
            A(en, "桌面图标位置记忆工具", "Desktop Icon Keeper");
            A(en, "桌面图标位置记忆", "Desktop Icon Keeper");
            A(en, "桌面图标位置记忆工具 v", "Desktop Icon Keeper v");
            A(en, "桌面图标位置记忆 · 失败", "Desktop Icon Keeper · failed");
            A(en, "记录当前布局", "Save current layout");
            A(en, "还原选中布局", "Restore selected layout");
            A(en, "还原最近一次记录", "Restore most recent");
            A(en, "撤销上次还原", "Undo the last restore");
            A(en, "删除选中的快照", "Delete the selected snapshot");
            A(en, "重命名", "Rename");
            A(en, "导出", "Export");
            A(en, "导入", "Import");
            A(en, "对比", "Compare");
            A(en, "刷新", "Refresh");
            A(en, "设置", "Settings");
            A(en, "退出", "Quit");
            A(en, "撤销", "Undo");
            A(en, "还原", "Restore");
            A(en, "保存", "Save");
            A(en, "删除", "Delete");
            A(en, "关闭", "Close");
            A(en, "取消", "Cancel");
            A(en, "确定", "OK");
            A(en, "是", "Yes");
            A(en, "开", "On");
            A(en, "关", "Off");
            A(en, "自动", "Auto");
            A(en, "名称", "Name");
            A(en, "类型", "Type");
            A(en, "状态", "State");
            A(en, "布局", "Layout");
            A(en, "图标", "Icon");
            A(en, "记录时间", "Recorded");
            A(en, "记录时间  ", "Recorded  ");
            A(en, "名称      ", "Name      ");
            A(en, "类型      ", "Type      ");
            A(en, "已绑定", "bound");
            A(en, "绑定", "Bound");
            A(en, "手动", "manual");
            A(en, "自动备份 ", "Auto-backup ");
            A(en, "自动还原", "Auto-restore");
            A(en, "自动排列  ", "Auto-arrange  ");
            A(en, "自动行为", "Automatic behaviour");
            A(en, "就绪  ·  快照 ", "Ready  ·  snapshots ");
            A(en, "  个  ·  数据目录 ", "  icons  ·  data dir ");
            A(en, "把当前桌面图标的位置存成一个快照  (", "Save the current icon positions as a snapshot  (");
            A(en, "把选中的快照写回桌面  (", "Write the selected snapshot back to the desktop  (");
            A(en, "把桌面恢复到上一次还原之前的状态", "Restore the desktop to the state before the last restore");
            A(en, "给选中的快照改名 / 设置自动还原绑定", "Rename the selected snapshot / set its auto-restore binding");
            A(en, "把选中的快照导出为 JSON 文件", "Export the selected snapshot as a JSON file");
            A(en, "删除选中的快照", "Delete the selected snapshot");
            A(en, "重新读取快照列表", "Reload the snapshot list");
            A(en, "打开快照存放目录", "Open the snapshot folder");
            A(en, "选中 1 个：与当前桌面对比；选中 2 个：两个快照互相对比",
                 "Select 1: compare with the live desktop; select 2: compare the two snapshots");
            A(en, "选中一个快照查看布局预览", "Select a snapshot to preview its layout");
            A(en, "还没有任何快照，请先「记录当前布局」。", "No snapshots yet — click \"Save current layout\" first.");
            A(en, "快照「", "Snapshot \"");
            A(en, "」已保存，", "\" saved, ");
            A(en, "」，共 ", "\" — ");
            A(en, "  个图标  ·  自动排列：", " icons  ·  auto-arrange: ");
            A(en, "  个图标）", " icons)");
            A(en, "  个图标，", " icons, ");
            A(en, "  个图标。", " icons.");
            A(en, "  个图标", " icons");
            A(en, "已记录：", "Saved: ");
            A(en, "记录：", "Save: ");
            A(en, "还原：", "Restore: ");
            A(en, "还原前 ", "Before restore ");
            A(en, "自动还原前 ", "Before auto-restore ");
            A(en, "已还原布局", "Layout restored");
            A(en, "已记录布局", "Layout saved");
            A(en, "还原完成", "Restore complete");
            A(en, "还原完成（有偏差）", "Restore complete (with deviations)");
            A(en, "还原失败", "Restore failed");
            A(en, "还原失败：", "Restore failed: ");
            A(en, "记录失败", "Save failed");
            A(en, "记录失败：", "Save failed: ");
            A(en, "撤销完成", "Undo complete");
            A(en, "撤销失败", "Undo failed");
            A(en, "撤销点", "Undo point");
            A(en, "没有可用的撤销点。撤销点会在每次还原前自动创建。",
                 "No undo point available. One is created automatically before every restore.");
            A(en, "还原被阻止", "Restore blocked");
            A(en, "还原被阻止：桌面开启了“自动排列图标”，任何坐标写入都会被系统立刻重排。",
                 "Restore blocked: \"auto arrange icons\" is on, so Windows immediately re-sorts any position write.");
            A(en, "桌面开启了“自动排列图标”，还原会失败 —— 点击「设置」可让本工具自动关闭它。",
                 "\"Auto arrange icons\" is on and restore will fail — open Settings to let this tool turn it off.");
            A(en, "请先选中一个快照。", "Select a snapshot first.");
            A(en, "请先在列表里选中一个快照。", "Select a snapshot in the list first.");
            A(en, "请先选中要删除的快照。", "Select a snapshot to delete first.");
            A(en, "请先选中要导出的快照。", "Select a snapshot to export first.");
            A(en, "确定删除 ", "Delete ");
            A(en, "  个快照？此操作不可恢复。", " snapshot(s)? This cannot be undone.");
            A(en, "  个快照中的图标已不存在", " icon(s) in that snapshot no longer exist");
            A(en, "快照为空，没有可还原的图标。", "The snapshot is empty — nothing to restore.");
            A(en, "当前桌面没有捕获到任何图标。请确认桌面图标处于显示状态。",
                 "No icons captured from the current desktop. Make sure desktop icons are visible.");
            A(en, "没有捕获到任何图标，请确认桌面图标处于显示状态。",
                 "No icons captured. Make sure desktop icons are visible.");
            A(en, "桌面看起来有 0 个图标", "the desktop looks like it has 0 icons");
            A(en, "桌面不可用", "Desktop unavailable");
            A(en, "打开失败", "open failed");
            A(en, "不可用", "unavailable");
            A(en, "布局信息不可用", "Layout info unavailable");
            A(en, "已导出：", "Exported: ");
            A(en, "已导出到：\n", "Exported to:\n");
            A(en, "导出成功", "Export succeeded");
            A(en, "导出失败", "Export failed");
            A(en, "导出失败：", "Export failed: ");
            A(en, "导出快照", "Export snapshot");
            A(en, "已导入「", "Imported \"");
            A(en, "导入成功", "Import succeeded");
            A(en, "导入失败", "Import failed");
            A(en, "导入失败：", "Import failed: ");
            A(en, "导入快照", "Import snapshot");
            A(en, "已删除：", "Deleted: ");
            A(en, "删除快照", "Delete snapshot");
            A(en, "对比失败", "Compare failed");
            A(en, "布局对比", "Layout diff");
            A(en, "快照属性", "Snapshot properties");
            A(en, "快照记录的布局：", "Layout recorded by the snapshot:");
            A(en, "给这个布局起个名字：", "Name this layout:");
            A(en, "名称：", "Name: ");
            A(en, "未命名", "Untitled");
            A(en, "工作布局", "Work layout");
            A(en, "绑定到当前显示器布局，切回该布局时自动还原",
                 "Bind to the current display layout; auto-restore when switching back to it");
            A(en, "（已绑定到该显示器布局）", "(bound to this display layout)");
            A(en, "（已按布局比例缩放）", "(scaled to the layout ratio)");
            A(en, "当前桌面", "Current desktop");
            A(en, "把桌面恢复到 ", "Restore the desktop to ");
            A(en, "  的布局", " layout");
            A(en, "  的状态？\n（共 ", " state?\n(");
            A(en, "  个图标）", " icons)");
            A(en, "移动 {0} · 新增 {1} · 缺失 {2} · 未变 {3}   （{4}  →  {5}）",
                 "moved {0} · added {1} · missing {2} · unchanged {3}   ({4}  →  {5})");
            A(en, "移动  {0,-40} ({1},{2}) -> ({3},{4})", "moved    {0,-40} ({1},{2}) -> ({3},{4})");
            A(en, "新增  {0,-40} ({1},{2})", "added    {0,-40} ({1},{2})");
            A(en, "缺失  {0,-40} ({1},{2})", "missing  {0,-40} ({1},{2})");
            A(en, "移动", "moved");
            A(en, "新增", "added");
            A(en, "缺失", "missing");
            A(en, "不变", "unchanged");
            A(en, "未知图标 #", "unknown icon #");
            A(en, "(未知图标 #", "(unknown icon #");
            A(en, "(无)", "(none)");
            A(en, "（没有快照）", "(no snapshots)");
            A(en, "（当前没有任何快照，先用 save 记录一个）", "(No snapshots yet — run save first)");
            A(en, "现有快照：", "Existing snapshots: ");
            A(en, "快照 JSON (*.json)|*.json", "Snapshot JSON (*.json)|*.json");
            A(en, "快照 JSON (*.json)|*.json|所有文件 (*.*)|*.*", "Snapshot JSON (*.json)|*.json|All files (*.*)|*.*");
            A(en, "找不到快照：", "Snapshot not found: ");
            A(en, "执行失败：", "Failed: ");
            A(en, "错误", "Error");
            A(en, "发生未处理的错误：\n\n", "An unhandled error occurred:\n\n");
            A(en, "\n\n详情已写入 ", "\n\nDetails written to ");
            A(en, "打开主界面", "Show main window");
            A(en, "最小化到托盘", "Minimize to tray");
            A(en, "仍在后台运行", "Still running");
            A(en, "程序已最小化到托盘，全局热键依然有效。右键托盘图标可退出。",
                 "Minimized to the tray; global hotkeys still work. Right-click the tray icon to quit.");
            A(en, "提示：部分全局热键被其它程序占用，可在“设置”里更换。",
                 "Note: some global hotkeys are taken by other programs; change them in Settings.");
            A(en, "提示：数据目录不可写。可在 exe 同目录放一个 portable.txt，改为绿色模式把数据写到程序旁边。",
                 "Note: the data directory is not writable. Drop a portable.txt next to the exe to switch to portable mode.");
            A(en, "数据目录：", "Data dir: ");
            A(en, "无法打开目录：", "Cannot open the folder: ");

            // ---- 设置窗体 ----
            A(en, "启动与提示", "Startup & notifications");
            A(en, "热键 / 自动备份 / 自动还原 / 忽略列表", "Hotkeys / auto-backup / auto-restore / ignore list");
            A(en, "还原选项", "Restore options");
            A(en, "全局热键", "Global hotkeys");
            A(en, "启动后直接最小化到托盘", "Minimize to tray on startup");
            A(en, "开机自动启动（最小化到托盘）", "Start with Windows (minimized to tray)");
            A(en, "还原 / 自动备份时显示托盘气泡提示", "Show tray balloons on restore / auto-backup");
            A(en, "显示器布局变化时自动建立备份（每个布局各存一份）",
                 "Auto-backup on display layout change (one per layout)");
            A(en, "切回已知布局时自动还原（优先用绑定该布局的快照）",
                 "Auto-restore when returning to a known layout (prefer snapshots bound to it)");
            A(en, "布局指纹不一致时，按分辨率比例缩放坐标",
                 "Scale coordinates by resolution ratio when the layout signature differs");
            A(en, "还原时自动关闭桌面「自动排列图标」", "Turn off \"auto arrange icons\" when restoring");
            A(en, "把未记录的图标排到桌面右侧空位（否则保持原位）",
                 "Park unrecorded icons right of the desktop (else leave as is)");
            A(en, "程序启动时自动还原最近一次手动快照", "Restore the most recent manual snapshot on startup");
            A(en, "忽略列表（每行一个图标名，不参与记录与还原）",
                 "Ignore list (one icon name per line; excluded from save and restore)");
            A(en, "格式示例：Ctrl+Alt+S、Ctrl+Shift+F5、Win+Alt+D。修改后立即生效。",
                 "Examples: Ctrl+Alt+S, Ctrl+Shift+F5, Win+Alt+D. Takes effect immediately.");
            A(en, "两个热键不能相同。", "The two hotkeys must differ.");
            A(en, "热键冲突", "Hotkey conflict");
            A(en, "热键无效", "Invalid hotkey");
            A(en, "热键格式不正确。请使用类似 Ctrl+Alt+S 的写法（必须包含至少一个修饰键）。",
                 "Invalid hotkey format. Use something like Ctrl+Alt+S (at least one modifier required).");

            // ---- 权限不足对话框 ----
            A(en, "权限不足 · 桌面图标位置记忆工具", "Permission denied · Desktop Icon Keeper");
            A(en, "以管理员身份重启", "Restart as administrator");
            A(en, "在资源管理器中打开", "Open in File Explorer");
            A(en, "去资源管理器打开", "Open in File Explorer");
            A(en, "权限不足：本工具运行在比 explorer 更低的权限里。点击此处以管理员身份重启 →",
                 "Permission denied: this tool runs at a lower integrity level than explorer. Click here to restart as administrator →");
            A(en, "权限不足：本工具运行在比 explorer 更低的权限里，且本机 UAC 已关闭。请直接在资源管理器里双击 exe。",
                 "Permission denied: this tool runs at a lower integrity level than explorer and UAC is off. Double-click the exe directly in File Explorer.");
            A(en, "无法打开资源管理器。请手动找到 exe 所在目录并双击运行。",
                 "Cannot open File Explorer. Locate the exe folder manually and double-click it.");
            A(en, "提权失败", "Elevation failed");
            A(en, "提权失败 → 再提权", "Elevation failed → retry");
            A(en, "提权失败（可能被 UAC 或安全软件拒绝）。\n请手动右键 exe → 以管理员身份运行。",
                 "Elevation failed (refused by UAC or security software).\nRight-click the exe → Run as administrator manually.");
            A(en, "提权重启", "Restart elevated");

            // ---- 单实例 ----
            A(en, "请先退出", "Please exit first");
            A(en, "已在运行但不可见", "Running but invisible");
            A(en, "已经有一个「桌面图标位置记忆工具」在运行，但它的窗口和托盘图标都找不到了。\n\n",
                 "Another Desktop Icon Keeper is already running, but neither its window nor its tray icon can be found.\n\n");
            A(en, "这通常是因为它被以较低权限启动，系统拒绝了它的托盘图标。\n\n",
                 "This usually happens because it was launched at a lower privilege and Windows refused its tray icon.\n\n");
            A(en, "要结束那个实例并启动新的吗？", "Terminate that instance and start a new one?");
            A(en, "结束它", "Terminate it");
            A(en, "结束失败", "Failed to terminate");
            A(en, "无法结束旧实例（可能权限不足）。\n\n",
                 "Cannot terminate the old instance (possibly insufficient permission).\n\n");
            A(en, "请打开任务管理器，结束所有 DesktopIconKeeper.exe 后重试。",
                 "Open Task Manager, end every DesktopIconKeeper.exe, then try again.");
            A(en, "我还活着，并且我已经把窗口显示出来了", "still alive and window shown");

            // ---- 引擎 / 自动行为 ----
            A(en, "部分图标被系统重新吸附到网格（通常因为“对齐到网格”或图标间距不同）。",
                 "Some icons were snapped back to the grid by Windows (usually \"snap to grid\" or a different icon spacing).");
            A(en, "布局指纹不一致（快照 {0} → 当前 {1}），坐标按 {2:0.###}x / {3:0.###}y 缩放。",
                 "Layout signature differs (snapshot {0} → current {1}); coordinates scaled by {2:0.###}x / {3:0.###}y.");
            A(en, "已就位 ", "placed ");
            A(en, "有 ", "");
            A(en, "  个图标目标位置互相占用，已先暂存再落位。",
                 " icons had conflicting targets and were parked before landing.");
            A(en, "  个图标已排到桌面右侧空位。", " unrecorded icon(s) parked to the right of the desktop.");
            A(en, "  个新图标未记录，保持原位", " new icon(s) not recorded, left in place");
            A(en, "  个在忽略列表中", " in the ignore list");
            A(en, "未记录的 ", "Unrecorded ");
            A(en, "目标格子当前没人占", "the target cell is free");
            A(en, "用户的新摆放", "user's new arrangement");
            A(en, "看起来成功", "looked successful");
            A(en, "检测到显示器布局变化（", "Display layout change detected (");
            A(en, "），已建立自动备份。", "), automatic backup created.");
            A(en, "已自动还原布局「", "Auto-restored layout \"");
            A(en, "自动处理显示器变化时出错：", "Error while handling a display change: ");
            A(en, "自动还原失败：", "Auto-restore failed: ");
            A(en, "显示器变化后无法访问桌面图标视图：", "Cannot access the desktop icon view after the display change: ");
            A(en, "\n当前显示器布局：", "\nCurrent display layout:");
            A(en, "\n指纹：", "\nSignature:");
            A(en, "  指纹 ", "  signature ");
            A(en, "  个图标  ·  自动排列：", " icons  ·  auto-arrange: ");
            A(en, "，自动排列 ", ", auto-arrange ");
            A(en, "  进程)", " process)");
            A(en, "  屏)", ")");
            // ---- 新增：动画 / 隐藏图标 / 关机备份 / 语言 / QQ 群 ----
            A(en, "还原时给图标加过渡动画（纯视觉，不影响最终坐标）",
                 "Animate icons when restoring (visual only; final positions are unaffected)");
            A(en, "关机 / 注销前自动备份一次（图标乱序多半是关机时造成的）",
                 "Auto-backup before shutdown / sign-out (most scrambling happens then)");
            A(en, "关机前自动备份", "Auto-backup before shutdown");
            A(en, "隐藏图标", "Hide icons");
            A(en, "显示图标", "Show icons");
            A(en, "隐藏 / 显示桌面图标（等同于桌面右键 → 查看 → 显示桌面图标）",
                 "Hide / show desktop icons (same as desktop right-click → View → Show desktop icons)");
            A(en, "已隐藏桌面图标", "Desktop icons hidden");
            A(en, "已显示桌面图标", "Desktop icons shown");
            A(en, "界面与语言", "Interface & language");
            A(en, "语言：", "Language: ");
            A(en, "自动（跟随系统）", "Auto (follow system)");
            A(en, "QQ 交流群：", "QQ group: ");
            A(en, "已复制群号：", "Group number copied: ");

            // ---- 空格数对齐修正（这些 key 的前导/尾随空格必须和源码里一模一样） ----
            A(en, "      对齐网格  ", "      Snap to grid  ");
            A(en, "      在设置里勾选“还原时自动关闭桌面自动排列”，或在桌面右键 → 查看 中手动关闭。",
                 "      Tick \"Turn off auto-arrange when restoring\" in Settings, or turn it off via desktop right-click → View.");
            A(en, "  图标间距 ", "  Icon spacing ");
            A(en, "  图标数量 ", "  Icon count ");
            A(en, "  显示器布局 ", "  Display layout ");
            A(en, " 的布局", " layout");
            A(en, " 的状态？\n（共 ", " state?\n(");
            A(en, " 个  ·  数据目录 ", "  icons  ·  data dir ");
            A(en, " 个被系统重新吸附", " snapped back to grid by Windows");
            A(en, " 个快照？此操作不可恢复。", " snapshot(s)? This cannot be undone.");
            A(en, " 个快照中的图标已不存在", " icon(s) in that snapshot no longer exist");
            A(en, " 个省略", " more omitted");
            A(en, " 个图标", " icons");
            A(en, " 个图标  ·  自动排列：", " icons  ·  auto-arrange: ");
            A(en, " 个图标）", " icons)");
            A(en, " 个图标，", " icons, ");
            A(en, " 个图标。", " icons.");
            A(en, " 个图标目标位置互相占用，已先暂存再落位。",
                 " icon(s) had conflicting targets and were parked before landing.");
            A(en, " 个图标已排到桌面右侧空位。", " unrecorded icon(s) parked to the right of the desktop.");
            A(en, " 个新图标未记录，保持原位", " new icon(s) not recorded, left in place");
            A(en, " 个在忽略列表中", " in the ignore list");
            A(en, " 进程)", " process)");
            A(en, "(未设置)", "(not set)");
            A(en, "… 其余 ", "… and ");
            A(en, "从 JSON 文件导入快照", "Import a snapshot from a JSON file");
            A(en, "屏)", ")");
            A(en, "数据目录", "Data dir");
            A(en, "图标间距  ", "Icon spacing  ");
            A(en, "图标数量  ", "Icon count  ");

            A(en, "用法：icons [hide|show|toggle|status]", "Usage: icons [hide|show|toggle|status]");
            A(en, "桌面图标：已隐藏", "Desktop icons: hidden");
            A(en, "桌面图标：已显示", "Desktop icons: shown");

            A(en, "删除失败（文件可能被占用）：", "Delete failed (the file may be in use): ");

            A(en, "  icons [hide|show|toggle|status]  隐藏 / 显示桌面图标",
                 "  icons [hide|show|toggle|status]  Hide / show desktop icons");

            A(en, " 个", " ");
        }

        private static void A(Dictionary<string, string> en, string zh, string english)
        {
            if (!en.ContainsKey(zh)) en[zh] = english;
        }
    }
}
