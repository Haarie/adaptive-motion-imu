using System;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>How SimHub's actuator outputs map onto platform motion modes.</summary>
    public enum ActuatorLayout
    {
        /// <summary>No modal split: every axis gets the Global gain only.</summary>
        GlobalOnly,
        /// <summary>2 actuators, Axis1 = left, Axis2 = right (2DOF seat mover / rotary).</summary>
        TwoLeftRight,
        /// <summary>3 actuators, Axis1 = front-left, Axis2 = front-right, Axis3 = rear.</summary>
        ThreeFrontPairRear,
        /// <summary>4 actuators, Axis1..4 = FL, FR, RL, RR (4-corner).</summary>
        FourCorner
    }

    /// <summary>
    /// Decomposes the actuator vector (deviation from neutral) into heave/pitch/roll(/warp) modes,
    /// scales each mode by its gain, and recomposes. Linear small-motion approximation: exact for
    /// linear actuators around neutral, a good approximation for rotary ones.
    /// Axes beyond the layout (traction-loss, surge, belts…) get the Global gain.
    /// </summary>
    public sealed class ActuatorModalScaler
    {
        public ActuatorLayout Layout { get; }
        private readonly double[,] _m;      // modes = M * x
        private readonly double[,] _mInv;   // x = Minv * modes
        private readonly Channel[] _modeChannel;
        public int LayoutAxes { get; }

        public ActuatorModalScaler(ActuatorLayout layout)
        {
            Layout = layout;
            switch (layout)
            {
                case ActuatorLayout.TwoLeftRight:
                    // mode0 = mean (pitch on a seat mover), mode1 = half difference (roll)
                    _m = new double[,] { { 0.5, 0.5 }, { 0.5, -0.5 } };
                    _modeChannel = new[] { Channel.Pitch, Channel.Roll };
                    break;
                case ActuatorLayout.ThreeFrontPairRear:
                    _m = new double[,]
                    {
                        { 1 / 3.0, 1 / 3.0, 1 / 3.0 },   // heave
                        { 0.5, 0.5, -1.0 },              // pitch (front avg - rear)
                        { 0.5, -0.5, 0.0 }               // roll  (left - right)/2
                    };
                    _modeChannel = new[] { Channel.Heave, Channel.Pitch, Channel.Roll };
                    break;
                case ActuatorLayout.FourCorner:
                    _m = new double[,]
                    {
                        { 0.25, 0.25, 0.25, 0.25 },      // heave
                        { 0.25, 0.25, -0.25, -0.25 },    // pitch
                        { 0.25, -0.25, 0.25, -0.25 },    // roll
                        { 0.25, -0.25, -0.25, 0.25 }     // warp (twist) -> global
                    };
                    _modeChannel = new[] { Channel.Heave, Channel.Pitch, Channel.Roll, Channel.Global };
                    break;
                default:
                    _m = new double[0, 0];
                    _modeChannel = new Channel[0];
                    break;
            }
            LayoutAxes = _m.GetLength(0);
            _mInv = LayoutAxes > 0 ? Invert(_m) : new double[0, 0];
        }

        /// <summary>Last decomposed modes (for the lag estimator), in "fraction of half-range".</summary>
        public double LastPitchMode { get; private set; }
        public double LastRollMode { get; private set; }

        /// <summary>
        /// Scale <paramref name="dev"/> (actuator deviations from neutral) in place.
        /// </summary>
        public void Apply(double[] dev, int count, GainSnapshot g)
        {
            int n = Math.Min(count, LayoutAxes);
            if (n == LayoutAxes && n > 0)
            {
                var modes = new double[n];
                for (int r = 0; r < n; r++)
                {
                    double s = 0;
                    for (int c = 0; c < n; c++) s += _m[r, c] * dev[c];
                    modes[r] = s;
                }
                for (int r = 0; r < n; r++)
                {
                    if (_modeChannel[r] == Channel.Pitch) LastPitchMode = modes[r];
                    if (_modeChannel[r] == Channel.Roll) LastRollMode = modes[r];
                    modes[r] *= g.Combined(_modeChannel[r]);
                }
                for (int r = 0; r < n; r++)
                {
                    double s = 0;
                    for (int c = 0; c < n; c++) s += _mInv[r, c] * modes[c];
                    dev[r] = s;
                }
            }
            else n = 0;
            for (int i = n; i < count; i++) dev[i] *= g.Global;
        }

        private static double[,] Invert(double[,] a)
        {
            int n = a.GetLength(0);
            var m = new double[n, 2 * n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++) m[r, c] = a[r, c];
                m[r, n + r] = 1;
            }
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int r = col + 1; r < n; r++) if (Math.Abs(m[r, col]) > Math.Abs(m[piv, col])) piv = r;
                if (Math.Abs(m[piv, col]) < 1e-12) throw new InvalidOperationException("Singular layout matrix");
                if (piv != col) for (int c = 0; c < 2 * n; c++) { var t = m[col, c]; m[col, c] = m[piv, c]; m[piv, c] = t; }
                double d = m[col, col];
                for (int c = 0; c < 2 * n; c++) m[col, c] /= d;
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = m[r, col];
                    if (f == 0) continue;
                    for (int c = 0; c < 2 * n; c++) m[r, c] -= f * m[col, c];
                }
            }
            var inv = new double[n, n];
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) inv[r, c] = m[r, n + c];
            return inv;
        }
    }
}
