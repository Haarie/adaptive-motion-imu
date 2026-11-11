using System;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>Fixed-size ring buffer of (time, a, b) samples for the trend chart. Oldest first.</summary>
    public sealed class TrendBuffer
    {
        private readonly double[] _t, _a, _b;
        private int _head, _count;
        public int Capacity { get; }
        public int Count => _count;

        public TrendBuffer(int capacity)
        {
            Capacity = Math.Max(2, capacity);
            _t = new double[Capacity]; _a = new double[Capacity]; _b = new double[Capacity];
        }

        public void Add(double t, double a, double b)
        {
            _t[_head] = t; _a[_head] = a; _b[_head] = b;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>i = 0 is the oldest sample.</summary>
        public void Get(int i, out double t, out double a, out double b)
        {
            if (i < 0 || i >= _count) throw new ArgumentOutOfRangeException(nameof(i));
            int k = (_head - _count + i + Capacity) % Capacity;
            t = _t[k]; a = _a[k]; b = _b[k];
        }

        public void Clear() { _head = 0; _count = 0; }
    }

    /// <summary>Pure mapping helpers for the visualizer (screen y grows downwards).</summary>
    public static class VisualMath
    {
        /// <summary>Fraction of v between lo and hi, clamped to [0, 1].</summary>
        public static double Frac(double v, double lo, double hi)
        {
            if (double.IsNaN(v) || hi <= lo) return 0;
            double f = (v - lo) / (hi - lo);
            return f < 0 ? 0 : (f > 1 ? 1 : f);
        }

        /// <summary>
        /// g-g plot of the specific force measured in the seat: +surge (forward) is drawn up,
        /// +sway (rig +Y = left) is drawn left. The point is clamped to the plot radius;
        /// <paramref name="clipped"/> says it was.
        /// </summary>
        public static void GgPoint(double surgeG, double swayG, double maxG, double radius,
                                   out double dx, out double dy, out bool clipped)
        {
            double x = -swayG / maxG * radius;
            double y = -surgeG / maxG * radius;
            double r = Math.Sqrt(x * x + y * y);
            clipped = r > radius;
            if (clipped) { x *= radius / r; y *= radius / r; }
            dx = x; dy = y;
        }

        /// <summary>
        /// Attitude indicator: rotation (degrees, WPF clockwise-positive) and vertical shift (px) of the horizon.
        /// +roll (right side down) tilts the horizon counter-clockwise; +pitch (nose up) moves it down.
        /// The shift is clamped so the horizon never leaves the instrument entirely.
        /// </summary>
        public static void Horizon(double rollDeg, double pitchDeg, double pxPerDeg, double radius,
                                   out double rotateDeg, out double shiftPx)
        {
            rotateDeg = -rollDeg;
            double s = pitchDeg * pxPerDeg;
            double lim = radius * 0.9;
            shiftPx = s > lim ? lim : (s < -lim ? -lim : s);
        }

        /// <summary>A nice symmetric axis range that covers the hard limit with some headroom.</summary>
        public static double NiceRange(double needed)
        {
            double[] steps = { 1, 2, 2.5, 5, 10, 15, 20, 25, 30, 45, 60, 90 };
            foreach (var s in steps) if (s >= needed) return s;
            return Math.Ceiling(needed / 10) * 10;
        }
    }
}
