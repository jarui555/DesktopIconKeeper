using System;
using System.Collections.Generic;
using System.Globalization;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 双语支持。
    ///
    /// key 直接用中文原文，英文查表 —— 代码里不用维护一堆无意义的 ID，
    /// 漏翻的地方会原样显示中文，一眼就能发现，不会静默变成空白。
    /// </summary>
    public static class Lang
    {
        public const string ZhCn = "zh-CN";
        public const string EnUs = "en-US";
        public const string Auto = "auto";

        private static string _current = ZhCn;

        private static readonly Dictionary<string, string> En =
            new Dictionary<string, string>(StringComparer.Ordinal);

        static Lang()
        {
            LangTable.Build(En);
        }

        public static string Current
        {
            get { return _current; }
            set { _current = string.Equals(value, EnUs, StringComparison.OrdinalIgnoreCase) ? EnUs : ZhCn; }
        }

        public static bool IsEnglish { get { return _current == EnUs; } }

        /// <summary>按系统界面语言挑一个默认值。</summary>
        public static string DetectSystem()
        {
            try
            {
                CultureInfo ci = CultureInfo.CurrentUICulture;
                if (ci != null && ci.TwoLetterISOLanguageName == "zh") return ZhCn;
                return EnUs;
            }
            catch (Exception) { return ZhCn; }
        }

        /// <summary>把设置里的 auto / zh-CN / en-US 解析成实际语言。</summary>
        public static string Resolve(string setting)
        {
            if (string.IsNullOrEmpty(setting) || string.Equals(setting, Auto, StringComparison.OrdinalIgnoreCase))
                return DetectSystem();
            return string.Equals(setting, EnUs, StringComparison.OrdinalIgnoreCase) ? EnUs : ZhCn;
        }

        public static string T(string zh)
        {
            if (zh == null) return string.Empty;
            if (_current != EnUs) return zh;
            string en;
            return En.TryGetValue(zh, out en) ? en : zh;
        }

        public static string T(string zh, params object[] args)
        {
            string f = T(zh);
            if (args == null || args.Length == 0) return f;
            try { return string.Format(CultureInfo.CurrentCulture, f, args); }
            catch (FormatException) { return f; }
        }
    }
}
