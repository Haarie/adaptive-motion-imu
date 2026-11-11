using System;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>
    /// Describes SimHub's "Generic UDP" motion-update packet as you configured it in SimHub, e.g.
    /// header bytes, then N axes of 8- or 16-bit binary values, then optional trailer bytes.
    /// Packets of any other length (startup/shutdown commands) are forwarded untouched.
    /// </summary>
    public class PacketLayout
    {
        public int HeaderBytes = 0;
        public int AxisCount = 4;
        public int BytesPerAxis = 2;        // 1 (8-bit) or 2 (10/12/16-bit)
        public bool BigEndian = true;       // <AxisNa><AxisNb> order in your SimHub command
        public int MaxValue = 65535;        // 255, 1023, 4095 or 65535
        public int TrailerBytes = 0;

        public int MotionPacketLength => HeaderBytes + AxisCount * BytesPerAxis + TrailerBytes;
    }

    public static class MotionPacketCodec
    {
        public static bool IsMotionPacket(PacketLayout l, byte[] p, int len) => len == l.MotionPacketLength;

        public static void Decode(PacketLayout l, byte[] p, double[] axes)
        {
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

        /// <summary>Transform in place. Returns true if the packet was a motion packet.</summary>
        public bool Process(byte[] p, int len, GainSnapshot g, bool active)
        {
            if (!MotionPacketCodec.IsMotionPacket(_l, p, len)) { OtherPackets++; return false; }
            MotionPackets++;
            MotionPacketCodec.Decode(_l, p, _axes);
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
            if (!active) return true;   // bytes untouched

            var outAxes = new double[_axes.Length];
            for (int i = 0; i < _axes.Length; i++)
            {
                double d = Math.Max(-maxDev, Math.Min(maxDev, _dev[i]));
                outAxes[i] = _neutral[i] + d;
            }
            MotionPacketCodec.Encode(_l, outAxes, p);
            return true;
        }
    }
}
