using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DesktopIconKeeper
{
    /// <summary>
    /// 极简 JSON 读写。对象模型：Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null。
    /// 自己实现是为了输出带缩进的可读快照文件，且不引入任何外部依赖。
    /// </summary>
    public static class Json
    {
        // ================= 解析 =================

        public static object Parse(string text)
        {
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                break;
            }
        }

        private static void Expect(string s, ref int i, string lit)
        {
            if (i + lit.Length > s.Length || string.CompareOrdinal(s, i, lit, 0, lit.Length) != 0)
                throw new FormatException("JSON 解析失败 @ " + i + "，期望 " + lit);
            i += lit.Length;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string k = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 期望 ':' @ " + i);
                i++;
                d[k] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 意外结束");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("JSON 期望 ',' 或 '}' @ " + i);
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var a = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 意外结束");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return a; }
                throw new FormatException("JSON 期望 ',' 或 ']' @ " + i);
            }
        }

        private static string ParseString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 期望字符串 @ " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON 字符串未闭合");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("JSON 转义未闭合");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 转义不完整");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException("JSON 未知转义 \\" + e);
                }
            }
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            if (i == start) throw new FormatException("JSON 期望值 @ " + start);
            string t = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("JSON 数字无效: " + t);
            return d;
        }

        // ================= 输出 =================

        public static string Write(object value, bool pretty)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, pretty, 0);
            if (pretty) sb.Append('\n');
            return sb.ToString();
        }

        private static void NewLine(StringBuilder sb, int depth, bool pretty)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        private static void WriteValue(StringBuilder sb, object v, bool pretty, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is bool) { sb.Append(((bool)v) ? "true" : "false"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is int || v is long || v is short || v is byte) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is double || v is float || v is decimal)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var map = v as IDictionary<string, object>;
            if (map != null) { WriteObject(sb, map, pretty, depth); return; }
            var list = v as IList<object>;
            if (list != null) { WriteArray(sb, list, pretty, depth); return; }
            WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        private static void WriteObject(StringBuilder sb, IDictionary<string, object> map, bool pretty, int depth)
        {
            if (map.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            bool first = true;
            foreach (var kv in map)
            {
                if (!first) sb.Append(',');
                first = false;
                NewLine(sb, depth + 1, pretty);
                WriteString(sb, kv.Key);
                sb.Append(':');
                if (pretty) sb.Append(' ');
                WriteValue(sb, kv.Value, pretty, depth + 1);
            }
            NewLine(sb, depth, pretty);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IList<object> list, bool pretty, int depth)
        {
            if (list.Count == 0) { sb.Append("[]"); return; }
            // 全是标量时压成一行，可读性更好
            bool scalar = true;
            for (int i = 0; i < list.Count; i++)
            {
                object o = list[i];
                if (o is IDictionary<string, object> || o is IList<object>) { scalar = false; break; }
            }
            sb.Append('[');
            if (scalar && pretty)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    WriteValue(sb, list[i], pretty, depth + 1);
                }
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    NewLine(sb, depth + 1, pretty);
                    WriteValue(sb, list[i], pretty, depth + 1);
                }
                NewLine(sb, depth, pretty);
            }
            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == 0x7F) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    /// <summary>读写 JSON 对象模型的语法糖，避免到处写强制转换。</summary>
    public static class J
    {
        public static Dictionary<string, object> NewObj() { return new Dictionary<string, object>(StringComparer.Ordinal); }
        public static List<object> NewArr() { return new List<object>(); }

        public static Dictionary<string, object> Obj(object o)
        {
            var d = o as Dictionary<string, object>;
            return d ?? NewObj();
        }

        public static List<object> Arr(object o)
        {
            var l = o as List<object>;
            return l ?? NewArr();
        }

        public static string Str(object o, string def)
        {
            if (o == null) return def;
            var s = o as string;
            return s ?? Convert.ToString(o, CultureInfo.InvariantCulture);
        }

        public static int Int(object o, int def)
        {
            if (o == null) return def;
            try { return (int)Math.Round(Convert.ToDouble(o, CultureInfo.InvariantCulture)); }
            catch { return def; }
        }

        public static bool Bool(object o, bool def)
        {
            if (o == null) return def;
            if (o is bool) return (bool)o;
            string s = o as string;
            if (s != null) return s.Equals("true", StringComparison.OrdinalIgnoreCase);
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture) != 0; }
            catch { return def; }
        }
    }
}
