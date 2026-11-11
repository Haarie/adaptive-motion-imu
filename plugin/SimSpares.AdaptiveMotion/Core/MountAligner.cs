using System;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>
    /// Auto-align: finds how the IMU sits in the rig from two still captures, so the box can be bolted at
    /// any angle (vertical upright, skewed profile, upside down...).
    ///   1. Rest: rig parked at its centre/level pose → gravity gives the rig's UP axis in sensor coordinates.
    ///   2. Nose-up: rig pitched nose-up (≥ 2°, pure pitch) and held → gravity tips towards the rig's FORWARD
    ///      axis, so the part of the change perpendicular to UP is FORWARD.
    /// Left = Up × Forward. The result feeds PlatformEstimator.SetMountingVectors; level is implied.
    /// </summary>
    public sealed class MountAligner
    {
        private readonly double[] _sum = new double[3];
        private int _n;

        public double[] Rest { get; private set; }
        public double[] NoseUp { get; private set; }

        public void BeginCapture() { _sum[0] = _sum[1] = _sum[2] = 0; _n = 0; }

        /// <summary>Feed specific-force samples (g, sensor frame) while the rig holds still.</summary>
        public void Add(double ax, double ay, double az) { _sum[0] += ax; _sum[1] += ay; _sum[2] += az; _n++; }

        public int Samples => _n;

        public bool EndRest(out string error) => End(out error, v => Rest = v);
        public bool EndNoseUp(out string error) => End(out error, v => NoseUp = v);

        private bool End(out string error, Action<double[]> store)
        {
            error = null;
            if (_n < 50) { error = "not enough IMU samples (is the sensor connected?)"; return false; }
            var m = new[] { _sum[0] / _n, _sum[1] / _n, _sum[2] / _n };
            double g = Norm(m);
            if (g < 0.8 || g > 1.2) { error = string.Format("|a| = {0:F2} g: the rig was not still", g); return false; }
            store(Scale(m, 1 / g));
            return true;
        }

        /// <summary>Compute the mounting from the two captures. Returns forward/up unit vectors in sensor coordinates.</summary>
        public bool Solve(out double[] forward, out double[] up, out double tiltDeg, out string error)
        {
            forward = up = null; tiltDeg = 0; error = null;
            if (Rest == null || NoseUp == null) { error = "capture Rest and Nose-up first"; return false; }
            up = (double[])Rest.Clone();
            double c = Dot(NoseUp, up);
            tiltDeg = Math.Acos(Math.Max(-1, Math.Min(1, c))) * 180 / Math.PI;
            if (tiltDeg < 2.0) { error = string.Format("nose-up tilt was only {0:F1}°, need at least 2°", tiltDeg); return false; }
            if (tiltDeg > 30.0) { error = string.Format("tilt of {0:F1}° is implausible for a pitch move", tiltDeg); return false; }
            var f = new[] { NoseUp[0] - c * up[0], NoseUp[1] - c * up[1], NoseUp[2] - c * up[2] };
            forward = Scale(f, 1 / Norm(f));
            return true;
        }

        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        private static double Norm(double[] a) => Math.Sqrt(Dot(a, a));
        private static double[] Scale(double[] a, double k) => new[] { a[0] * k, a[1] * k, a[2] * k };
    }
}
