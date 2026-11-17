using System;

namespace SimSpares.AdaptiveMotion.Core
{
    public enum Axis { PosX, NegX, PosY, NegY, PosZ, NegZ }

    /// <summary>
    /// What the IMU says the seat is doing, in the rig frame.
    /// Rig frame (ISO 8855 style): +X forward, +Y left, +Z up.
    /// +Pitch = nose up, +Roll = right side down. Angles in degrees, forces in g.
    /// </summary>
    public struct PlatformState
    {
        public double PitchDeg, RollDeg;            // tilt relative to the captured level reference
        public double PitchRateDps, RollRateDps, YawRateDps;
        public double CueSurgeG, CueSwayG, CueHeaveG;   // specific force felt in the seat (incl. tilt)
        public double LinSurgeG, LinSwayG, LinHeaveG;   // gravity removed (platform's own acceleration)
        public double PeakDevG, VibRmsG;
        public ImuFlags Flags;
        public double TimeSec;                       // host clock
    }

    /// <summary>Converts sensor-frame IMU frames into rig-frame state. Thread-safe for one producer.</summary>
    public sealed class PlatformEstimator
    {
        private double[,] _r = Identity();        // sensor -> rig
        private double _refPitch, _refRoll;
        private volatile bool _driverActive;
        private double _drvPitch, _drvRoll;
        public bool LevelCaptured { get; private set; }

        /// <summary>
        /// Seat attitude with the driver in the seat, relative to the (empty-rig) level. Subtracted only while
        /// <see cref="DriverOffsetActive"/> is set (driving), so the driver's normal position reads 0°.
        /// </summary>
        public void SetDriverOffset(double pitchDeg, double rollDeg) { _drvPitch = pitchDeg; _drvRoll = rollDeg; }
        public bool DriverOffsetActive { get => _driverActive; set => _driverActive = value; }

        public void SetMounting(Axis sensorForward, Axis sensorUp)
        {
            var f = Vec(sensorForward);
            var u = Vec(sensorUp);
            if (Math.Abs(Dot(f, u)) > 1e-9) throw new ArgumentException("Forward and up axes must be perpendicular");
            var l = Cross(u, f);                   // left = up x forward
            // Rows of R are the rig axes expressed in sensor coordinates
            _r = new double[3, 3]
            {
                { f[0], f[1], f[2] },
                { l[0], l[1], l[2] },
                { u[0], u[1], u[2] }
            };
            LevelCaptured = false; _refPitch = _refRoll = 0;
        }

        /// <summary>
        /// Arbitrary mounting (from MountAligner): rig forward and up as vectors in sensor coordinates.
        /// Forward is re-orthogonalised against up. The level reference is reset to zero because "up"
        /// was measured at the level pose.
        /// </summary>
        public void SetMountingVectors(double[] forward, double[] up)
        {
            if (forward == null || up == null || forward.Length != 3 || up.Length != 3) throw new ArgumentException("need two 3-vectors");
            var u = Normalize(up);
            double d = Dot(forward, u);
            var f = Normalize(new[] { forward[0] - d * u[0], forward[1] - d * u[1], forward[2] - d * u[2] });
            var l = Cross(u, f);
            _r = new double[3, 3] { { f[0], f[1], f[2] }, { l[0], l[1], l[2] }, { u[0], u[1], u[2] } };
            _refPitch = _refRoll = 0; LevelCaptured = true;
        }

        private static double[] Normalize(double[] v)
        {
            double n = Math.Sqrt(Dot(v, v));
            if (n < 1e-9) throw new ArgumentException("zero vector");
            return new[] { v[0] / n, v[1] / n, v[2] / n };
        }

        public void SetLevelReference(double pitchDeg, double rollDeg)
        {
            _refPitch = pitchDeg; _refRoll = rollDeg; LevelCaptured = true;
        }

        /// <summary>Raw (un-levelled) pitch/roll of the last frame, used for level capture.</summary>
        public double RawPitchDeg { get; private set; }
        public double RawRollDeg { get; private set; }

        public PlatformState Update(ImuFrame f, double hostTimeSec)
        {
            // "Up" (gravity reaction) direction in sensor frame from the quaternion (Mahony convention)
            double ux = 2 * (f.Q1 * f.Q3 - f.Q0 * f.Q2);
            double uy = 2 * (f.Q0 * f.Q1 + f.Q2 * f.Q3);
            double uz = f.Q0 * f.Q0 - f.Q1 * f.Q1 - f.Q2 * f.Q2 + f.Q3 * f.Q3;
            var up = Mul(_r, ux, uy, uz);
            var acc = Mul(_r, f.Ax, f.Ay, f.Az);
            var gyr = Mul(_r, f.Gx, f.Gy, f.Gz);

            double rawPitch = Rad2Deg(Math.Atan2(up[0], Math.Sqrt(up[1] * up[1] + up[2] * up[2])));
            double rawRoll = Rad2Deg(Math.Atan2(up[1], up[2]));
            RawPitchDeg = rawPitch; RawRollDeg = rawRoll;

            return new PlatformState
            {
                PitchDeg = rawPitch - _refPitch - (_driverActive ? _drvPitch : 0),
                RollDeg = WrapDeg(rawRoll - _refRoll - (_driverActive ? _drvRoll : 0)),
                PitchRateDps = -gyr[1],
                RollRateDps = gyr[0],
                YawRateDps = gyr[2],
                CueSurgeG = acc[0], CueSwayG = acc[1], CueHeaveG = acc[2] - 1.0,
                LinSurgeG = acc[0] - up[0], LinSwayG = acc[1] - up[1], LinHeaveG = acc[2] - up[2],
                PeakDevG = f.PeakDevG, VibRmsG = f.VibRmsG, Flags = f.Flags, TimeSec = hostTimeSec
            };
        }

        private static double WrapDeg(double a) { while (a > 180) a -= 360; while (a < -180) a += 360; return a; }
        private static double Rad2Deg(double r) => r * 180.0 / Math.PI;
        private static double[,] Identity() => new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        private static double[] Mul(double[,] m, double x, double y, double z) => new[]
        {
            m[0,0]*x + m[0,1]*y + m[0,2]*z, m[1,0]*x + m[1,1]*y + m[1,2]*z, m[2,0]*x + m[2,1]*y + m[2,2]*z
        };
        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        private static double[] Cross(double[] a, double[] b) => new[]
        {
            a[1]*b[2] - a[2]*b[1], a[2]*b[0] - a[0]*b[2], a[0]*b[1] - a[1]*b[0]
        };
        private static double[] Vec(Axis a)
        {
            switch (a)
            {
                case Axis.PosX: return new double[] { 1, 0, 0 };
                case Axis.NegX: return new double[] { -1, 0, 0 };
                case Axis.PosY: return new double[] { 0, 1, 0 };
                case Axis.NegY: return new double[] { 0, -1, 0 };
                case Axis.PosZ: return new double[] { 0, 0, 1 };
                default: return new double[] { 0, 0, -1 };
            }
        }
    }
}
