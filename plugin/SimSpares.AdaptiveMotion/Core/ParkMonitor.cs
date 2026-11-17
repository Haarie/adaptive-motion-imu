using System;

namespace SimSpares.AdaptiveMotion.Core
{
    public class ParkSettings
    {
        public double SettleSec = 1.0;       // extra wait after the firmware reports "still" (it already needs 2 s)
        public double AverageSec = 2.0;      // average the parked pose over this long
        public double ToleranceDeg = 0.5;    // parked pose may differ this much from the reference
        public double SeatedMaxDeg = 1.5;    // a driver in the seat may tilt the parked pose up to this much (seat/frame flex)
        public double AfterSessionSec = 180; // a park this soon after a game session is assumed to have the driver still seated
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
        /// <summary>Parked pose with the driver still in the seat (learned on the first park after a session).</summary>
        public bool HasSeatedReference { get; private set; }
        public double SeatedPitchDeg { get; private set; }
        public double SeatedRollDeg { get; private set; }
        /// <summary>Set by the caller: true while this park follows a game session closely (driver probably seated).</summary>
        public bool AfterSession { get; set; }
        /// <summary>True when the last check matched the seated reference rather than the empty one.</summary>
        public bool LastWasSeated { get; private set; }
        public long Checks { get; private set; }

        /// <summary>First parked pose stored (pitch, roll). Persist it.</summary>
        public event Action<double, double> ReferenceLearned;
        /// <summary>Parked pose with the driver seated stored (pitch, roll). Persist it.</summary>
        public event Action<double, double> SeatedReferenceLearned;
        /// <summary>A park check finished: deviation in degrees, shifted yes/no.</summary>
        public event Action<double, bool> Checked;

        public ParkMonitor(ParkSettings s) { _s = s ?? new ParkSettings(); }

        public void UpdateSettings(ParkSettings s) { _s = s ?? _s; }

        public void SetReference(double pitchDeg, double rollDeg)
        {
            RefPitchDeg = pitchDeg; RefRollDeg = rollDeg; HasReference = true; MountShifted = false;
        }

        public void SetSeatedReference(double pitchDeg, double rollDeg)
        {
            SeatedPitchDeg = pitchDeg; SeatedRollDeg = rollDeg; HasSeatedReference = true;
        }

        /// <summary>Forget the parked pose; the next park learns it again (after re-mounting or re-levelling).</summary>
        public void ClearReference()
        {
            HasReference = false; HasSeatedReference = false; MountShifted = false; LastDeviationDeg = double.NaN;
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
            double dEmpty = Dist(LastPitchDeg - RefPitchDeg, LastRollDeg - RefRollDeg);
            double dSeated = HasSeatedReference ? Dist(LastPitchDeg - SeatedPitchDeg, LastRollDeg - SeatedRollDeg) : double.PositiveInfinity;
            // The driver's weight tilts the seat a little: right after a session, a park that is off the empty
            // pose by a plausible seat-flex amount becomes the "seated" reference instead of a mount warning.
            if (dEmpty > _s.ToleranceDeg && dSeated > _s.ToleranceDeg && !HasSeatedReference && AfterSession && dEmpty <= _s.SeatedMaxDeg)
            {
                SetSeatedReference(LastPitchDeg, LastRollDeg);
                dSeated = 0;
                SeatedReferenceLearned?.Invoke(LastPitchDeg, LastRollDeg);
            }
            LastWasSeated = dSeated < dEmpty;
            LastDeviationDeg = Math.Min(dEmpty, dSeated);
            MountShifted = LastDeviationDeg > _s.ToleranceDeg;   // latched until the next park check or a reset
            Checked?.Invoke(LastDeviationDeg, MountShifted);
        }

        private static double Dist(double a, double b) => Math.Sqrt(a * a + b * b);
    }
}
