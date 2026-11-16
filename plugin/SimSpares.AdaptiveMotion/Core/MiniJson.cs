using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>
    /// Small JSON reader (objects → Dictionary&lt;string, object&gt;, arrays → List&lt;object&gt;, numbers → double).
    /// Used to read SimHub's own settings files without depending on a JSON library.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            if (s.Length > 0 && s[0] == '﻿') i = 1;
            var v = Value(s, ref i);
            return v;
        }

        /// <summary>Path lookup: "Devices[0].Geometry.BaseGeometry.Settings.RigWidth". Null when missing.</summary>
        public static object Get(object root, string path)
        {
            object cur = root;
            foreach (var raw in path.Split('.'))
            {
                if (cur == null) return null;
                string key = raw; int idx = -1;
                int b = raw.IndexOf('[');
                if (b >= 0) { key = raw.Substring(0, b); idx = int.Parse(raw.Substring(b + 1, raw.IndexOf(']') - b - 1), CultureInfo.InvariantCulture); }
                if (key.Length > 0)
                {
                    var d = cur as Dictionary<string, object>;
                    if (d == null || !d.TryGetValue(key, out cur)) return null;
                }
                if (idx >= 0)
                {
                    var l = cur as List<object>;
                    if (l == null || idx >= l.Count) return null;
                    cur = l[idx];
                }
            }
            return cur;
        }

        public static double Num(object root, string path, double fallback = double.NaN) => Get(root, path) is double d ? d : fallback;
        public static bool? Bool(object root, string path) => Get(root, path) is bool b ? b : (bool?)null;
        public static string Str(object root, string path) => Get(root, path) as string;

        private static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end of JSON");
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i++] != ':') throw new FormatException("':' expected at " + i);
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    char e = s[i++];
                    if (e == '}') return d;
                    if (e != ',') throw new FormatException("',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    char e = s[i++];
                    if (e == ']') return l;
                    if (e != ',') throw new FormatException("',' or ']' expected at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Starts(s, i, "true")) { i += 4; return true; }
            if (Starts(s, i, "false")) { i += 5; return false; }
            if (Starts(s, i, "null")) { i += 4; return null; }
            if (Starts(s, i, "NaN")) { i += 3; return double.NaN; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("unexpected '" + c + "' at " + i);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("string expected at " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }

        private static bool Starts(string s, int i, string w) => string.CompareOrdinal(s, i, w, 0, w.Length) == 0;
        private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
    }
}
