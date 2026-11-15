using System;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>
    /// The rig as SimHub sees it: where each actuator sits, how far it travels for the full output range,
    /// and the pitch/roll limits configured in SimHub's motion settings. Used to turn SimHub's actuator
    /// values into a commanded pose (degrees) and, for the verification test, a pose into actuator values.
    /// Rig frame: +X forward, +Y left, +Z up, millimetres from the centre of rotation.
    /// +Pitch = nose up, +Roll = right side down (same convention as <see cref="PlatformState"/>).
    /// </summary>
    public class RigGeometry
    {
        public double[] ActuatorX = null;   // mm forward of the centre of rotation, one per actuator (layout order)
        public double[] ActuatorY = null;   // mm left of the centre of rotation
        public double StrokeMm = 0;         // travel that corresponds to output 0 … MaxValue
        public bool HigherValueIsUp = true; // a larger output value extends the actuator (seat goes up)
        public double MaxPitchDeg = 0;      // SimHub motion "Limits": the most this rig is allowed to pitch
        public double MaxRollDeg = 0;
        public string Source = "manual";    // "manual" or "SimHub"

        // helper inputs used by the setup page to fill the arrays above
        public double TrackMm = 0;          // left-right distance between actuators (centre to centre)
        public double WheelbaseMm = 0;      // front-rear distance between actuators
        public double PivotOffsetMm = 0;    // 2-actuator rigs: actuators sit this far BEHIND the front pivot

        public int Count => ActuatorX == null ? 0 : ActuatorX.Length;

        /// <summary>Fill the actuator positions for a standard layout from track / wheelbase.</summary>
        public void FillFromLayout(ActuatorLayout layout)
        {
            double t = TrackMm / 2, w = WheelbaseMm / 2;
            switch (layout)
            {
                case ActuatorLayout.TwoLeftRight:
                    ActuatorX = new[] { -PivotOffsetMm, -PivotOffsetMm };
                    ActuatorY = new[] { t, -t };
                    break;
                case ActuatorLayout.ThreeFrontPairRear:
                    ActuatorX = new[] { w, w, -w };
                    ActuatorY = new[] { t, -t, 0.0 };
                    break;
                case ActuatorLayout.FourCorner:
                    ActuatorX = new[] { w, w, -w, -w };
                    ActuatorY = new[] { t, -t, t, -t };
                    break;
                default:
                    ActuatorX = null; ActuatorY = null;
                    break;
            }
        }

        /// <summary>Returns null when the geometry is usable, otherwise what is missing.</summary>
        public string Problem(ActuatorLayout layout, PacketLayout packet)
        {
            if (layout == ActuatorLayout.GlobalOnly) return "choose the actuator layout (2, 3 or 4 actuators)";
            int n = layout == ActuatorLayout.TwoLeftRight ? 2 : layout == ActuatorLayout.ThreeFrontPairRear ? 3 : 4;
            if (Count != n || ActuatorY == null || ActuatorY.Length != n) return "enter the actuator distances";
            if (packet == null || packet.AxisCount < n) return "SimHub sends fewer axes than the layout needs";
            if (!(StrokeMm > 0)) return "enter the actuator stroke";
            if (!(MaxPitchDeg > 0) || !(MaxRollDeg > 0)) return "enter the pitch and roll limits from SimHub";
            double spanY = 0, spanX = 0;
            for (int i = 0; i < n; i++) for (int j = 0; j < n; j++)
            {
                spanY = Math.Max(spanY, Math.Abs(ActuatorY[i] - ActuatorY[j]));
                spanX = Math.Max(spanX, Math.Abs(ActuatorX[i] - ActuatorX[j]));
            }
            if (spanY < 50) return "left-right distance between actuators is missing";
            if (n >= 3 && spanX < 50) return "front-rear distance between actuators is missing";
            if (n == 2 && Math.Abs(ActuatorX[0]) < 20) return "2-actuator rig: enter how far the actuators sit behind the pivot";
            return null;
        }

        /// <summary>Actuator heights (mm, + = up) for a pose. Rigid plate, vertical actuators.</summary>
        public double[] HeightsMm(double pitchDeg, double rollDeg, double heaveMm = 0)
        {
            double tp = Math.Tan(pitchDeg * Math.PI / 180), tr = Math.Tan(rollDeg * Math.PI / 180);
            var z = new double[Count];
            for (int i = 0; i < z.Length; i++) z[i] = ActuatorX[i] * tp + ActuatorY[i] * tr + heaveMm;
            return z;
        }

        /// <summary>
        /// Pose from actuator heights: least-squares plane z = a·x + b·y (+ c with 3 or more actuators).
        /// </summary>
        public void Pose(double[] heightsMm, out double pitchDeg, out double rollDeg, out double heaveMm)
        {
            int n = Count;
            bool withHeave = n >= 3;
            int k = withHeave ? 3 : 2;
            var ata = new double[k, k]; var atb = new double[k];
            for (int i = 0; i < n; i++)
            {
                var row = withHeave ? new[] { ActuatorX[i], ActuatorY[i], 1.0 } : new[] { ActuatorX[i], ActuatorY[i] };
                for (int r = 0; r < k; r++)
                {
                    atb[r] += row[r] * heightsMm[i];
                    for (int c = 0; c < k; c++) ata[r, c] += row[r] * row[c];
                }
            }
            var sol = Solve(ata, atb);
            pitchDeg = Math.Atan(sol[0]) * 180 / Math.PI;
            rollDeg = Math.Atan(sol[1]) * 180 / Math.PI;
            heaveMm = withHeave ? sol[2] : 0;
        }

        /// <summary>Output values (SimHub units) → actuator heights (mm) around the captured neutral.</summary>
        public double[] ValuesToHeights(double[] values, double[] neutral, int maxValue)
        {
            var z = new double[Count];
            double k = StrokeMm / maxValue * (HigherValueIsUp ? 1 : -1);
            for (int i = 0; i < z.Length; i++) z[i] = (values[i] - neutral[i]) * k;
            return z;
        }

        /// <summary>Actuator heights (mm) → output values; other axes (beyond the layout) stay at neutral.</summary>
        public double[] HeightsToValues(double[] heightsMm, double[] neutral, int maxValue)
        {
            var v = (double[])neutral.Clone();
            double k = maxValue / StrokeMm * (HigherValueIsUp ? 1 : -1);
            for (int i = 0; i < Count && i < v.Length; i++) v[i] = Math.Max(0, Math.Min(maxValue, neutral[i] + heightsMm[i] * k));
            return v;
        }

        /// <summary>Largest |height| any actuator can still move from neutral without leaving 0 … MaxValue.</summary>
        public double HeadroomMm(double[] neutral, int maxValue)
        {
            double mm = double.MaxValue, k = StrokeMm / maxValue;
            for (int i = 0; i < Count && i < neutral.Length; i++)
                mm = Math.Min(mm, Math.Min(neutral[i], maxValue - neutral[i]) * k);
            return mm;
        }

        public RigGeometry Clone()
        {
            var g = (RigGeometry)MemberwiseClone();
            g.ActuatorX = ActuatorX == null ? null : (double[])ActuatorX.Clone();
            g.ActuatorY = ActuatorY == null ? null : (double[])ActuatorY.Clone();
            return g;
        }

        private static double[] Solve(double[,] a, double[] b)
        {
            int n = b.Length;
            var m = new double[n, n + 1];
            for (int r = 0; r < n; r++) { for (int c = 0; c < n; c++) m[r, c] = a[r, c]; m[r, n] = b[r]; }
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
                if (Math.Abs(m[piv, col]) < 1e-12) throw new InvalidOperationException("actuator positions do not define a plane");
                if (piv != col) for (int c = 0; c <= n; c++) { var t = m[col, c]; m[col, c] = m[piv, c]; m[piv, c] = t; }
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = m[r, col] / m[col, col];
                    for (int c = col; c <= n; c++) m[r, c] -= f * m[col, c];
                }
            }
            var x = new double[n];
            for (int r = 0; r < n; r++) x[r] = m[r, n] / m[r, r];
            return x;
        }
    }
}
