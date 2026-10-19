using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace DesktopIconKeeper
{
    public class AppSettings
    {
        public bool AutoBackupOnDisplayChange = true;
        public bool AutoRestoreOnDisplayChange = true;
        public bool RestoreOnStartup = false;
        public bool ScaleAcrossLayouts = false;
        public bool ParkUnmatchedIcons = false;
        public bool StartMinimized = false;
        public bool AutoStartWithWindows = false;
        public bool ShowBalloonTips = true;
        public bool AutoDisableAutoArrange = true;

        public string HotkeySave = "Ctrl+Alt+S";
        public string HotkeyRestore = "Ctrl+Alt+R";

        /// <summary>"auto" / "zh-CN" / "en-US"</summary>
        public string Language = Lang.Auto;

        /// <summary>还原时给图标加过渡动画。</summary>
        public bool AnimateRestore = true;

        /// <summary>关机 / 注销前自动存一份快照。</summary>
        public bool AutoBackupOnShutdown = true;

        public List<string> IgnoreNames = new List<string>();

        public static AppSettings Load(string path)
        {
            var s = new AppSettings();
            if (!File.Exists(path)) return s;
            try
            {
                var o = J.Obj(Json.Parse(File.ReadAllText(path, Encoding.UTF8)));
                s.AutoBackupOnDisplayChange = Get(o, "autoBackupOnDisplayChange", s.AutoBackupOnDisplayChange);
                s.AutoRestoreOnDisplayChange = Get(o, "autoRestoreOnDisplayChange", s.AutoRestoreOnDisplayChange);
                s.RestoreOnStartup = Get(o, "restoreOnStartup", s.RestoreOnStartup);
                s.ScaleAcrossLayouts = Get(o, "scaleAcrossLayouts", s.ScaleAcrossLayouts);
                s.ParkUnmatchedIcons = Get(o, "parkUnmatchedIcons", s.ParkUnmatchedIcons);
                s.StartMinimized = Get(o, "startMinimized", s.StartMinimized);
                s.AutoStartWithWindows = Get(o, "autoStartWithWindows", s.AutoStartWithWindows);
                s.ShowBalloonTips = Get(o, "showBalloonTips", s.ShowBalloonTips);
                s.AutoDisableAutoArrange = Get(o, "autoDisableAutoArrange", s.AutoDisableAutoArrange);
                s.HotkeySave = J.Str(o.ContainsKey("hotkeySave") ? o["hotkeySave"] : null, s.HotkeySave);
                s.HotkeyRestore = J.Str(o.ContainsKey("hotkeyRestore") ? o["hotkeyRestore"] : null, s.HotkeyRestore);
                s.Language = J.Str(o.ContainsKey("language") ? o["language"] : null, s.Language);
                s.AnimateRestore = Get(o, "animateRestore", s.AnimateRestore);
                s.AutoBackupOnShutdown = Get(o, "autoBackupOnShutdown", s.AutoBackupOnShutdown);

                s.IgnoreNames = new List<string>();
                foreach (var v in J.Arr(o.ContainsKey("ignoreNames") ? o["ignoreNames"] : null))
                {
                    string n = J.Str(v, "");
                    if (!string.IsNullOrEmpty(n)) s.IgnoreNames.Add(n);
                }
            }
            catch (Exception) { }
            return s;
        }

        private static bool Get(Dictionary<string, object> o, string key, bool def)
        {
            return J.Bool(o.ContainsKey(key) ? o[key] : null, def);
        }

        public void Save(string path)
        {
            var o = J.NewObj();
            o["autoBackupOnDisplayChange"] = AutoBackupOnDisplayChange;
            o["autoRestoreOnDisplayChange"] = AutoRestoreOnDisplayChange;
            o["restoreOnStartup"] = RestoreOnStartup;
            o["scaleAcrossLayouts"] = ScaleAcrossLayouts;
            o["parkUnmatchedIcons"] = ParkUnmatchedIcons;
            o["startMinimized"] = StartMinimized;
            o["autoStartWithWindows"] = AutoStartWithWindows;
            o["showBalloonTips"] = ShowBalloonTips;
            o["autoDisableAutoArrange"] = AutoDisableAutoArrange;
            o["hotkeySave"] = HotkeySave;
            o["hotkeyRestore"] = HotkeyRestore;
            o["language"] = Language;
            o["animateRestore"] = AnimateRestore;
            o["autoBackupOnShutdown"] = AutoBackupOnShutdown;

            var arr = J.NewArr();
            foreach (string n in IgnoreNames) arr.Add(n);
            o["ignoreNames"] = arr;

            SnapshotStore.AtomicWrite(path, Json.Write(o, true));
        }

        public bool IsIgnored(string name)
        {
            if (IgnoreNames == null || IgnoreNames.Count == 0) return false;
            foreach (string n in IgnoreNames)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>开机自启（HKCU Run，不需要管理员）。</summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DesktopIconKeeper";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    return k.GetValue(ValueName) != null;
                }
            }
            catch (Exception) { return false; }
        }

        public static void Set(bool enabled, string exePath)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (enabled) k.SetValue(ValueName, "\"" + exePath + "\" --tray", RegistryValueKind.String);
                    else k.DeleteValue(ValueName, false);
                }
            }
            catch (Exception) { }
        }
    }
}
