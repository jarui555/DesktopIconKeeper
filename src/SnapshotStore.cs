using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 快照仓库。默认落在 %APPDATA%\DesktopIconKeeper\snapshots\，
    /// exe 同目录存在 portable.txt 时改为绿色模式（数据放 exe 旁边的 data\）。
    /// 所有写入都是“临时文件 + 替换”，避免崩溃时把快照写坏。
    /// </summary>
    public sealed class SnapshotStore
    {
        public string RootDir { get; private set; }
        public string SnapshotDir { get; private set; }
        public string UndoFile { get; private set; }
        public string SettingsFile { get; private set; }
        public bool Portable { get; private set; }

        public SnapshotStore(string exeDir)
        {
            Portable = File.Exists(Path.Combine(exeDir, "portable.txt"));
            RootDir = Portable
                ? Path.Combine(exeDir, "data")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopIconKeeper");

            SnapshotDir = Path.Combine(RootDir, "snapshots");
            UndoFile = Path.Combine(RootDir, "_undo.json");
            SettingsFile = Path.Combine(RootDir, "settings.json");

            if (!TryEnsure(SnapshotDir))
            {
                // %APPDATA% 不可写（受限账户 / 安全软件拦截）时退回 exe 同目录
                Portable = true;
                RootDir = Path.Combine(exeDir, "data");
                SnapshotDir = Path.Combine(RootDir, "snapshots");
                UndoFile = Path.Combine(RootDir, "_undo.json");
                SettingsFile = Path.Combine(RootDir, "settings.json");
                TryEnsure(SnapshotDir);
            }
        }

        private static bool TryEnsure(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                // 目录存在不代表能写：受限账户、安全软件、沙箱都可能只拦写文件。
                // 真正落一个探针文件再删掉，才能确定这个位置可用。
                string probe = Path.Combine(dir, ".write-probe");
                File.WriteAllText(probe, "ok", new UTF8Encoding(false));
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // =====================================================================
        // 读写
        // =====================================================================

        public List<Snapshot> LoadAll()
        {
            var list = new List<Snapshot>();
            string[] files;
            try { files = Directory.GetFiles(SnapshotDir, "*.json"); }
            catch (Exception) { return list; }

            foreach (string f in files)
            {
                try
                {
                    string text = File.ReadAllText(f, Encoding.UTF8);
                    Snapshot s = Codec.Deserialize(text);
                    s.Note = string.IsNullOrEmpty(s.Note) ? f : s.Note;
                    list.Add(s);
                }
                catch (Exception)
                {
                    // 单个文件坏了不影响其它快照
                }
            }

            list.Sort(delegate(Snapshot a, Snapshot b) { return b.Created.CompareTo(a.Created); });
            return list;
        }

        public string PathFor(Snapshot s)
        {
            string safe = Sanitize(s.Name);
            if (string.IsNullOrEmpty(safe)) safe = "snapshot";
            return Path.Combine(SnapshotDir, s.Created.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                                              + "-" + safe + "-" + ShortId(s.Id) + ".json");
        }

        public void Save(Snapshot s)
        {
            string path = null;
            // 若同 Id 已存在则覆盖原文件，避免重命名后留下孤儿文件
            foreach (string f in Directory.GetFiles(SnapshotDir, "*.json"))
            {
                try
                {
                    string text = File.ReadAllText(f, Encoding.UTF8);
                    Snapshot old = Codec.Deserialize(text);
                    if (old.Id == s.Id) { path = f; break; }
                }
                catch (Exception) { }
            }
            if (path == null) path = PathFor(s);

            AtomicWrite(path, Codec.Serialize(s));
        }

        /// <summary>
        /// 删除快照。返回是否真的删掉了。
        ///
        /// 以前这里静默吞异常，文件被占用时会一边报"已删除"一边留着文件，
        /// 所以现在必须回查一次磁盘。
        /// </summary>
        public bool Delete(Snapshot s)
        {
            foreach (string f in Directory.GetFiles(SnapshotDir, "*.json"))
            {
                try
                {
                    Snapshot old = Codec.Deserialize(File.ReadAllText(f, Encoding.UTF8));
                    if (old.Id != s.Id) continue;
                    File.Delete(f);
                    return !File.Exists(f);
                }
                catch (Exception) { }
            }
            return false;
        }

        /// <summary>找出绑定到指定显示器布局、且开启了自动还原的手动快照。</summary>
        public Snapshot FindBoundFor(string signature)
        {
            foreach (var s in LoadAll())
            {
                if (s.Kind != SnapshotKind.Manual) continue;
                if (!s.BoundAutoRestore) continue;
                if (s.Layout != null && s.Layout.Signature == signature) return s;
            }
            return null;
        }

        /// <summary>该显示器布局下系统自动留存的备份。</summary>
        public Snapshot FindAutoFor(string signature)
        {
            foreach (var s in LoadAll())
            {
                if (s.Kind != SnapshotKind.Auto) continue;
                if (s.Layout != null && s.Layout.Signature == signature) return s;
            }
            return null;
        }

        // =====================================================================
        // 撤销点
        // =====================================================================

        public void SaveUndo(Snapshot s)
        {
            s.Kind = SnapshotKind.Undo;
            AtomicWrite(UndoFile, Codec.Serialize(s));
        }

        public Snapshot LoadUndo()
        {
            if (!File.Exists(UndoFile)) return null;
            try { return Codec.Deserialize(File.ReadAllText(UndoFile, Encoding.UTF8)); }
            catch (Exception) { return null; }
        }

        public void ClearUndo()
        {
            try { if (File.Exists(UndoFile)) File.Delete(UndoFile); }
            catch (Exception) { }
        }

        // =====================================================================
        // 导入导出
        // =====================================================================

        public Snapshot Import(string path)
        {
            Snapshot s = Codec.Deserialize(File.ReadAllText(path, Encoding.UTF8));
            s.Id = Guid.NewGuid().ToString("N");
            s.Kind = SnapshotKind.Manual;
            Save(s);
            return s;
        }

        public void Export(Snapshot s, string path)
        {
            AtomicWrite(path, Codec.Serialize(s));
        }

        // =====================================================================
        // 工具
        // =====================================================================

        public static void AtomicWrite(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, null);
                    return;
                }
                catch (Exception)
                {
                    // File.Replace 在跨卷或权限异常时失败，退回删除+移动
                    try { File.Delete(path); } catch (Exception) { }
                }
            }
            File.Move(tmp, path);
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "0000";
            return id.Length > 4 ? id.Substring(0, 4) : id;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder();
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in name)
            {
                bool ok = true;
                for (int i = 0; i < bad.Length; i++) if (bad[i] == c) { ok = false; break; }
                if (ok && c != ' ') sb.Append(c);
            }
            if (sb.Length > 32) sb.Length = 32;
            return sb.ToString();
        }
    }
}
