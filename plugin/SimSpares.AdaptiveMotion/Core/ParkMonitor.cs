using System;

namespace SimSpares.AdaptiveMotion.Core
{
    public class ParkSettings
    {
        public double SettleSec = 1.0;       // extra wait after the firmware reports "still" (it already needs 2 s)
        public double AverageSec = 2.0;      // average the parked pose over this long
        public double ToleranceDeg = 0.5;    // parked pose may differ this much from the reference
    }

    public enum ParkState { Moving, Settling, Measuring, Parked }

    /// <summary>
    /// Park check: when no game runs and the IMU reports the rig still, average the parked pitch/roll and
    /// compare them with the parked pose learned the first time. A difference means the IMU box shifted on
    /// its mount, or the rig homed/parked somewhere else. It only warns: it never changes the level
    /// reference (park is not necessarily level/centre). Gyro bias is learned in the firmware meanwhile.
    /// Not thread-safe: call Update from the IMU thread only.
    /// </summary>
    public sealed class ParkMonitor
    {
        private ParkSettings _s;
        private double _t0, _sumP, _sumR;
        private int _n;

        public ParkState State { get; private set; } = ParkState.Moving;
        public bool HasReference { get; private set; }
        public double RefPitchDeg { get; private set; }
        public double RefRollDeg { get; private set; }
        public double LastPitchDeg { get; private set; } = double.NaN;
        public double LastRollDeg { get; private set; } = double.NaN;
        public double LastDeviationDeg { get; private set; } = double.NaN;
        public bool MountShifted { get; private set; }
        public long Checks { get; private set; }

        /// <summary>First parked pose stored (pitch, roll). Persist it.</summary>
        public event Action<double, double> ReferenceLearned;
        /// <summary>A park check finished: deviation in degrees, shifted yes/no.</summary>
        public event Action<double, bool> Checked;

        public ParkMonitor(ParkSettings s) { _s = s ?? new ParkSettings(); }

        public void UpdateSettings(ParkSettings s) { _s = s ?? _s; }

        public void SetReference(double pitchDeg, double rollDeg)
        {
            RefPitchDeg = pitchDeg; RefRollDeg = rollDeg; HasReference = true; MountShifted = false;
        }

        /// <summary>Forget the parked pose; the next park learns it again (after re-mounting or re-levelling).</summary>
        public void ClearReference()
        {
            HasReference = false; MountShifted = false; LastDeviationDeg = double.NaN;
            if (State == ParkState.Parked) State = ParkState.Moving;   // re-measure on the next still period
        }

        public void Update(PlatformState st, bool gameRunning)
        {
            bool still = (st.Flags & ImuFlags.Still) != 0 && (st.Flags & ImuFlags.I2cError) == 0 && !gameRunning;
            double t = st.TimeSec;
            if (!still) { State = ParkState.Moving; return; }

            switch (State)
            {
                case ParkState.Moving:
                    State = ParkState.Settling; _t0 = t;
                    break;
                case ParkState.Settling:
                    if (t - _t0 >= _s.SettleSec) { State = ParkState.Measuring; _t0 = t; _sumP = _sumR = 0; _n = 0; }
                    break;
                case ParkState.Measuring:
                    _sumP += st.PitchDeg; _sumR += st.RollDeg; _n++;
                    if (t - _t0 >= _s.AverageSec && _n > 0) Finish();
                    break;
                case ParkState.Parked:
                    break;   // one check per park; moving again re-arms it
            }
        }

        private void Finish()
        {
            State = ParkState.Parked;
            LastPitchDeg = _sumP / _n;
            LastRollDeg = _sumR / _n;
            Checks++;
            if (!HasReference)
            {
                SetReference(LastPitchDeg, LastRollDeg);
                LastDeviationDeg = 0;
                ReferenceLearned?.Invoke(LastPitchDeg, LastRollDeg);
                return;
            }
            double dp = LastPitchDeg - RefPitchDeg, dr = LastRollDeg - RefRollDeg;
            LastDeviationDeg = Math.Sqrt(dp * dp + dr * dr);
            MountShifted = LastDeviationDeg > _s.ToleranceDeg;   // latched until the next park check or a reset
            Checked?.Invoke(LastDeviationDeg, MountShifted);
        }
    }
}
