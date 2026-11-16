using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>
    /// Describes SimHub's "Generic UDP" motion-update packet as configured in SimHub. Two kinds:
    /// <list type="bullet">
    /// <item>Binary: header bytes, then N axes of 8- or 16-bit values (&lt;Axis1a&gt;&lt;Axis1b&gt;…), then trailer bytes.</item>
    /// <item>Text: a comma-separated line such as <c>NULLCAT,&lt;Axis1&gt;,&lt;Axis2&gt;,…</c> (decimal or hex numbers).</item>
    /// </list>
    /// Packets that do not match (startup/shutdown commands) are forwarded untouched.
    /// </summary>
    public class PacketLayout
    {
        public bool Text = false;           // text command, e.g. NULLCAT,<Axis1>,<Axis2>,…
        public string TextPrefix = "";      // fixed first field(s) before the axes ("NULLCAT"), empty = none
        public bool TextHex = false;        // axis values written as hexadecimal
        public int HeaderBytes = 0;
        public int AxisCount = 4;
        public int BytesPerAxis = 2;        // 1 (8-bit) or 2 (10/12/16-bit)
        public bool BigEndian = true;       // <AxisNa><AxisNb> order in your SimHub command
        public int MaxValue = 65535;        // 255, 1023, 4095 or 65535
        public int TrailerBytes = 0;

        public int MotionPacketLength => Text ? -1 : HeaderBytes + AxisCount * BytesPerAxis + TrailerBytes;

        public string Describe() => Text
            ? string.Format(CultureInfo.InvariantCulture, "text \"{0}{1}<Axis1>,…,<Axis{2}>\", {3}, max {4}", TextPrefix, TextPrefix == "" ? "" : ",", AxisCount, TextHex ? "hex" : "decimal", MaxValue)
            : string.Format(CultureInfo.InvariantCulture, "binary, {0} header byte(s), {1} axes × {2} byte(s){3}, {4} trailer byte(s), max {5}",
                HeaderBytes, AxisCount, BytesPerAxis, BytesPerAxis == 2 ? (BigEndian ? " high byte first" : " low byte first") : "", TrailerBytes, MaxValue);

        public void CopyFrom(PacketLayout o)
        {
            Text = o.Text; TextPrefix = o.TextPrefix ?? ""; TextHex = o.TextHex; HeaderBytes = o.HeaderBytes; AxisCount = o.AxisCount;
            BytesPerAxis = o.BytesPerAxis; BigEndian = o.BigEndian; MaxValue = o.MaxValue; TrailerBytes = o.TrailerBytes;
        }

        /// <summary>Max value for SimHub's axis resolution (8, 10, 12 or 16 bit).</summary>
        public static int MaxForBits(int bits) => bits <= 0 || bits > 16 ? 65535 : (1 << bits) - 1;

        /// <summary>
        /// Layout from SimHub's update command, e.g. "NULLCAT,&lt;Axis1&gt;,…,&lt;Axis10&gt;" or "S&lt;Axis1a&gt;&lt;Axis1b&gt;…E".
        /// Returns null when the command has no axis placeholders.
        /// </summary>
        public static PacketLayout FromSimHubCommand(string cmd, int bits)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            var bin = System.Text.RegularExpressions.Regex.Matches(cmd, @"<Axis(\d+)([ab])>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (bin.Count > 0)
            {
                int first = cmd.IndexOf('<'), last = cmd.LastIndexOf('>');
                var l = new PacketLayout { Text = false, HeaderBytes = first, TrailerBytes = cmd.Length - last - 1 };
                var axes = new HashSet<string>(); bool twoBytes = false, bigEndian;
                foreach (System.Text.RegularExpressions.Match m in bin)
                {
                    axes.Add(m.Groups[1].Value);
                    if (m.Groups[2].Value.ToLowerInvariant() == "b") twoBytes = true;
                }
                var firstAxis = bin[0];
                bigEndian = firstAxis.Groups[2].Value.ToLowerInvariant() == "a";
                l.AxisCount = axes.Count; l.BytesPerAxis = twoBytes ? 2 : 1; l.BigEndian = bigEndian;
                l.MaxValue = twoBytes ? MaxForBits(bits < 9 ? 16 : bits) : 255;
                return l;
            }
            var txt = System.Text.RegularExpressions.Regex.Matches(cmd, @"<Axis(\d+)>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (txt.Count == 0) return null;
            string prefix = cmd.Substring(0, cmd.IndexOf('<')).TrimEnd(',', ' ', ';');
            return new PacketLayout { Text = true, TextPrefix = prefix, AxisCount = txt.Count, MaxValue = MaxForBits(bits), BytesPerAxis = 2 };
        }
    }

    public static class MotionPacketCodec
    {
        public static bool IsMotionPacket(PacketLayout l, byte[] p, int len)
        {
            if (!l.Text) return len == l.MotionPacketLength;
            return TryParseText(l, p, len, null, out _, out _);
        }

        /// <summary>Text packets: "PREFIX,v1,v2,…" (+ optional line end). Values go into <paramref name="axes"/> when given.</summary>
        public static bool TryParseText(PacketLayout l, byte[] p, int len, double[] axes, out string suffix, out int[] widths)
        {
            suffix = ""; widths = null;
            if (len < 3 || len > 1024) return false;
            for (int i = 0; i < len; i++) if (p[i] < 9 || p[i] > 126) return false;
            string s = Encoding.ASCII.GetString(p, 0, len);
            int end = s.Length;
            while (end > 0 && (s[end - 1] == '\r' || s[end - 1] == '\n' || s[end - 1] == ' ' || s[end - 1] == ';')) end--;
            suffix = s.Substring(end);
            string body = s.Substring(0, end);
            if (l.TextPrefix != "")
            {
                if (!body.StartsWith(l.TextPrefix + ",", StringComparison.Ordinal)) return false;
                body = body.Substring(l.TextPrefix.Length + 1);
            }
            var tok = body.Split(',');
            if (tok.Length != l.AxisCount) return false;
            widths = new int[tok.Length];
            for (int i = 0; i < tok.Length; i++)
            {
                string t = tok[i].Trim();
                if (IsPlaceholder(t)) { widths[i] = -1; if (axes != null && i < axes.Length) axes[i] = 0; continue; }   // e.g. "<Axis10>" SimHub left unfilled: kept as text
                long v;
                bool ok = l.TextHex ? long.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
                                    : long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
                if (!ok) return false;
                widths[i] = t.Length > 1 && t[0] == '0' ? t.Length : 0;   // keep zero padding
                if (axes != null && i < axes.Length) axes[i] = v;
            }
            return true;
        }

        /// <summary>
        /// Guess a text layout from a live packet (e.g. "NULLCAT,32767,32767,…"). Null when the packet is not text.
        /// </summary>
        public static PacketLayout DetectText(byte[] p, int len, int maxValue)
        {
            if (len < 3 || len > 1024) return null;
            for (int i = 0; i < len; i++) if (p[i] < 9 || p[i] > 126) return null;
            string s = Encoding.ASCII.GetString(p, 0, len).TrimEnd('\r', '\n', ' ', ';');
            var tok = s.Split(',');
            if (tok.Length < 2) return null;
            int first = 0;
            var prefix = new List<string>();
            while (first < tok.Length && !IsNumber(tok[first].Trim(), true) && !IsPlaceholder(tok[first].Trim())) { prefix.Add(tok[first]); first++; }
            int n = tok.Length - first;
            if (n < 2) return null;
            bool hex = false;
            for (int i = first; i < tok.Length; i++)
            {
                string t = tok[i].Trim();
                if (IsPlaceholder(t)) continue;
                if (!IsNumber(t, true)) return null;
                if (!IsNumber(t, false)) hex = true;
            }
            return new PacketLayout { Text = true, TextPrefix = string.Join(",", prefix), AxisCount = n, TextHex = hex, MaxValue = maxValue, BytesPerAxis = 2 };
        }

        /// <summary>An axis placeholder SimHub did not fill in (it sends "&lt;Axis10&gt;" literally when it has no 10th axis).</summary>
        public static bool IsPlaceholder(string t) => t.Length > 2 && t[0] == '<' && t[t.Length - 1] == '>';

        private static bool IsNumber(string t, bool allowHex)
        {
            if (t.Length == 0) return false;
            foreach (char c in t)
                if (!(char.IsDigit(c) || (allowHex && ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))))) return false;
            return true;
        }

        /// <summary>Printable copy of a packet for messages (text as text, binary as hex bytes).</summary>
        public static string Sample(byte[] p, int len)
        {
            bool text = len > 0;
            for (int i = 0; i < len; i++) if (p[i] < 9 || p[i] > 126) { text = false; break; }
            if (text) return Encoding.ASCII.GetString(p, 0, Math.Min(len, 120)).TrimEnd('\r', '\n');
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(len, 40); i++) sb.Append(p[i].ToString("X2")).Append(' ');
            return sb.ToString().TrimEnd() + (len > 40 ? " …" : "") + " (" + len + " bytes)";
        }

        /// <summary>A packet for this layout without having seen one (text layouts, or binary without header/trailer).</summary>
        public static byte[] Synthesize(PacketLayout l)
        {
            if (l.Text)
            {
                var sb = new StringBuilder();
                if (l.TextPrefix != "") sb.Append(l.TextPrefix).Append(',');
                for (int i = 0; i < l.AxisCount; i++) { if (i > 0) sb.Append(','); sb.Append('0'); }
                return Encoding.ASCII.GetBytes(sb.ToString());
            }
            return l.HeaderBytes == 0 && l.TrailerBytes == 0 ? new byte[l.MotionPacketLength] : null;
        }

        /// <summary>Build a packet with new axis values, using <paramref name="template"/> for everything else.</summary>
        public static byte[] Build(PacketLayout l, double[] axes, byte[] template, int len)
        {
            if (!l.Text)
            {
                var b = new byte[len];
                Buffer.BlockCopy(template, 0, b, 0, len);
                Encode(l, axes, b);
                return b;
            }
            TryParseText(l, template, len, null, out var suffix, out var widths);
            string[] orig = null;
            {
                string ts = Encoding.ASCII.GetString(template, 0, len);
                ts = ts.Substring(0, ts.Length - suffix.Length);
                if (l.TextPrefix != "" && ts.StartsWith(l.TextPrefix + ",", StringComparison.Ordinal)) ts = ts.Substring(l.TextPrefix.Length + 1);
                orig = ts.Split(',');
            }
            var sb = new StringBuilder();
            if (l.TextPrefix != "") sb.Append(l.TextPrefix).Append(',');
            for (int i = 0; i < l.AxisCount; i++)
            {
                if (widths != null && i < widths.Length && widths[i] == -1 && orig != null && i < orig.Length)
                { if (i > 0) sb.Append(','); sb.Append(orig[i]); continue; }   // unfilled placeholder: send back as received
                long v = (long)Math.Round(axes[i]);
                if (v < 0) v = 0; if (v > l.MaxValue) v = l.MaxValue;
                string t = l.TextHex ? v.ToString("X", CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);
                if (widths != null && i < widths.Length && widths[i] > t.Length) t = t.PadLeft(widths[i], '0');
                if (i > 0) sb.Append(',');
                sb.Append(t);
            }
            sb.Append(suffix);
            return Encoding.ASCII.GetBytes(sb.ToString());
        }

        public static void Decode(PacketLayout l, byte[] p, double[] axes)
        {
            if (l.Text) { TryParseText(l, p, p.Length, axes, out _, out _); return; }
            for (int i = 0; i < l.AxisCount; i++)
            {
                int o = l.HeaderBytes + i * l.BytesPerAxis;
                axes[i] = l.BytesPerAxis == 1 ? p[o]
                    : l.BigEndian ? (p[o] << 8) | p[o + 1] : p[o] | (p[o + 1] << 8);
            }
        }

        public static void Encode(PacketLayout l, double[] axes, byte[] p)
        {
            for (int i = 0; i < l.AxisCount; i++)
            {
                int v = (int)Math.Round(axes[i]);
                if (v < 0) v = 0; if (v > l.MaxValue) v = l.MaxValue;
                int o = l.HeaderBytes + i * l.BytesPerAxis;
                if (l.BytesPerAxis == 1) p[o] = (byte)v;
                else if (l.BigEndian) { p[o] = (byte)(v >> 8); p[o + 1] = (byte)v; }
                else { p[o] = (byte)v; p[o + 1] = (byte)(v >> 8); }
            }
        }
    }

    /// <summary>
    /// Pure packet transform (no sockets) so it can be unit-tested: decode → scale around neutral → encode.
    /// Guarantees: (1) passthrough is byte-identical, (2) no actuator ends up further from neutral than the
    /// furthest actuator in the original packet, (3) values stay inside [0, MaxValue].
    /// </summary>
    public sealed class MotionPacketTransformer
    {
        private readonly PacketLayout _l;
        private readonly ActuatorModalScaler _scaler;
        private readonly double[] _axes, _dev;
        private double[] _neutral;
        private readonly double[] _neutralAcc;
        private int _neutralN;

        public MotionPacketTransformer(PacketLayout layout, ActuatorLayout actuatorLayout, double[] neutral = null)
        {
            _l = layout;
            _scaler = new ActuatorModalScaler(actuatorLayout);
            _axes = new double[layout.AxisCount];
            _dev = new double[layout.AxisCount];
            _neutralAcc = new double[layout.AxisCount];
            _neutral = neutral != null && neutral.Length == layout.AxisCount ? (double[])neutral.Clone() : DefaultNeutral(layout);
        }

        public PacketLayout Layout => _l;
        public double[] LastAxes => (double[])_axes.Clone();
        public double[] Neutral => (double[])_neutral.Clone();
        public double PitchMode => _scaler.LastPitchMode / (_l.MaxValue / 2.0);   // fraction of half range
        public double RollMode => _scaler.LastRollMode / (_l.MaxValue / 2.0);
        public long MotionPackets { get; private set; }
        public long OtherPackets { get; private set; }
        public bool CapturingNeutral { get; private set; }

        public static double[] DefaultNeutral(PacketLayout l)
        {
            var n = new double[l.AxisCount];
            for (int i = 0; i < n.Length; i++) n[i] = l.MaxValue / 2.0;
            return n;
        }

        public void BeginNeutralCapture() { Array.Clear(_neutralAcc, 0, _neutralAcc.Length); _neutralN = 0; CapturingNeutral = true; }

        public bool EndNeutralCapture()
        {
            CapturingNeutral = false;
            if (_neutralN < 10) return false;
            var n = new double[_neutralAcc.Length];
            for (int i = 0; i < n.Length; i++) n[i] = _neutralAcc[i] / _neutralN;
            _neutral = n;
            return true;
        }

        /// <summary>Last packet that did not match the layout (for the setup page), or null.</summary>
        public string LastOtherSample { get; private set; }
        public byte[] LastOtherPacket { get; private set; }

        /// <summary>
        /// Transform any packet (binary or text). Returns the bytes to send: the original array when nothing
        /// changed, otherwise a new or modified one.
        /// </summary>
        public byte[] ProcessAny(byte[] p, int len, GainSnapshot g, bool active, out bool motion)
        {
            if (!_l.Text) { motion = Process(p, len, g, active); return p; }
            if (!MotionPacketCodec.TryParseText(_l, p, len, _axes, out _, out _)) { motion = false; NoteOther(p, len); return p; }
            motion = true;
            var outAxes = Transform(g, active);
            return outAxes == null ? p : MotionPacketCodec.Build(_l, outAxes, p, len);
        }

        private void NoteOther(byte[] p, int len)
        {
            OtherPackets++;
            if (OtherPackets < 5 || OtherPackets % 50 == 0) { LastOtherSample = MotionPacketCodec.Sample(p, len); var c = new byte[len]; Buffer.BlockCopy(p, 0, c, 0, len); LastOtherPacket = c; }
        }

        /// <summary>Transform in place (binary layouts). Returns true if the packet was a motion packet.</summary>
        public bool Process(byte[] p, int len, GainSnapshot g, bool active)
        {
            if (_l.Text) { bool m; var o = ProcessAny(p, len, g, active, out m); if (o != p && o.Length == len) Buffer.BlockCopy(o, 0, p, 0, len); return m; }
            if (!MotionPacketCodec.IsMotionPacket(_l, p, len)) { NoteOther(p, len); return false; }
            MotionPacketCodec.Decode(_l, p, _axes);
            var outAxes = Transform(g, active);
            if (outAxes != null) MotionPacketCodec.Encode(_l, outAxes, p);
            return true;
        }

        // decoded axes in _axes → output axes (null = pass through unchanged)
        private double[] Transform(GainSnapshot g, bool active)
        {
            MotionPackets++;
            if (CapturingNeutral)
            {
                for (int i = 0; i < _axes.Length; i++) _neutralAcc[i] += _axes[i];
                _neutralN++;
            }

            double maxDev = 0;
            for (int i = 0; i < _axes.Length; i++)
            {
                _dev[i] = _axes[i] - _neutral[i];
                maxDev = Math.Max(maxDev, Math.Abs(_dev[i]));
            }
            // Always decompose so the lag estimator sees commanded modes, even when passive
            _scaler.Apply(_dev, _dev.Length, active ? g : GainSnapshot.Unity);
            if (!active) return null;   // bytes untouched

            var outAxes = new double[_axes.Length];
            for (int i = 0; i < _axes.Length; i++)
            {
                double d = Math.Max(-maxDev, Math.Min(maxDev, _dev[i]));
                outAxes[i] = _neutral[i] + d;
            }
            return outAxes;
        }
    }
}
