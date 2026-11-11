using System;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>Tuning for the adaptive controller. All limits are measured at the seat by the IMU.</summary>
    public class ControllerSettings
    {
        // Comfort / mechanical envelope
        public double PitchLimitDeg = 8.0;      // compress pitch above Knee * this
        public double RollLimitDeg = 8.0;
        public double RateLimitDps = 60.0;      // angular rate envelope (pitch & roll)
        public double HeaveLimitG = 0.6;        // vertical cue envelope
        public double CueLimitG = 0.8;          // horizontal cue (surge/sway vector) envelope
        public double VibLimitG = 0.35;         // HP vibration RMS envelope (resonance/chatter)

        // Compressor shape
        public double Knee = 0.85;              // start compressing at 85 % of a limit
        public double ReleaseBelow = 0.70;      // only recover when below 70 %
        public double AttackSec = 0.15;
        public double ReleaseSec = 4.0;
        public double MinGain = 0.30;           // never compress below this

        // Impact / end-stop detector
        public double ImpactG = 1.5;            // peak | |a|-1g | inside one IMU window
        public double ImpactStep = 0.10;        // -10 % global gain per impact
        public double ImpactHoldSec = 2.0;      // no recovery for this long after an impact

        // Hard trip (still not a safety system — keep the hardware e-stop!)
        public double PitchHardDeg = 14.0;
        public double RollHardDeg = 14.0;
        public bool TripAutoReset = false;

        // Sensor health
        public double StaleSec = 0.25;
        public double FailsafeGain = 0.60;
    }

    public enum Channel { Global = 0, Pitch = 1, Roll = 2, Heave = 3 }

    public enum ControllerStatus { WaitingForSensor, Ok, Compressing, ImpactHold, SensorLost, Tripped }

    /// <summary>Immutable snapshot published to other threads.</summary>
    public sealed class GainSnapshot
    {
        public readonly double Global, Pitch, Roll, Heave;
        public readonly ControllerStatus Status;
        public GainSnapshot(double g, double p, double r, double h, ControllerStatus s)
        { Global = g; Pitch = p; Roll = r; Heave = h; Status = s; }
        public static readonly GainSnapshot Unity = new GainSnapshot(1, 1, 1, 1, ControllerStatus.WaitingForSensor);
        public double Combined(Channel c)
        {
            switch (c)
            {
                case Channel.Pitch: return Global * Pitch;
                case Channel.Roll: return Global * Roll;
                case Channel.Heave: return Global * Heave;
                default: return Global;
            }
        }
    }

    /// <summary>
    /// Feedback "motion compressor": measures what the seat actually does and pulls per-channel gains
    /// down (fast) when it approaches the envelope, and back up to 1.0 (slowly) when it doesn't.
    /// Gains are always within [MinGain, 1] — the plugin can only ever attenuate SimHub's output.
    /// </summary>
    public sealed class AdaptiveGainController
    {
        private readonly object _lock = new object();
        private ControllerSettings _s;
        private readonly double[] _g = { 1, 1, 1, 1 };
        private readonly double[] _level = new double[4];
        private double _lastFrameTime = double.NaN, _lastTickTime = double.NaN;
        private double _impactHoldUntil = double.NegativeInfinity;
        private double _tripClearSince = double.NaN;
        private ControllerStatus _status = ControllerStatus.WaitingForSensor;
        private volatile GainSnapshot _snap = GainSnapshot.Unity;

        public AdaptiveGainController(ControllerSettings s) { _s = s ?? new ControllerSettings(); }

        public GainSnapshot Snapshot => _snap;
        public long ImpactCount { get; private set; }
        public long TripCount { get; private set; }
        public bool Tripped { get; private set; }
        public double LevelPitch => _level[1];
        public double LevelRoll => _level[2];
        public double LevelHeave => _level[3];
        public double LevelGlobal => _level[0];

        public event Action<string> TripRaised;   // raised on the IMU thread; keep handlers short
        public event Action ImpactDetected;

        public void UpdateSettings(ControllerSettings s) { lock (_lock) _s = s; }

        public void ResetTrip()
        {
            lock (_lock) { Tripped = false; _tripClearSince = double.NaN; Publish(); }
        }

        public void ResetGains()
        {
            lock (_lock) { for (int i = 0; i < 4; i++) _g[i] = 1; _impactHoldUntil = double.NegativeInfinity; Publish(); }
        }

        /// <summary>Call for every IMU frame (≈250 Hz).</summary>
        public void Update(PlatformState st)
        {
            Action<string> trip = null; Action impact = null; string tripMsg = null;
            lock (_lock)
            {
                var s = _s;
                double t = st.TimeSec;
                double dt = double.IsNaN(_lastFrameTime) ? 0.004 : Clamp(t - _lastFrameTime, 0.0005, 0.1);
                _lastFrameTime = t;

                if ((st.Flags & ImuFlags.I2cError) != 0) { ApplyFailsafe(dt); _status = ControllerStatus.SensorLost; Publish(); return; }

                // --- hard trip --------------------------------------------------------------
                bool overHard = Math.Abs(st.PitchDeg) > s.PitchHardDeg || Math.Abs(st.RollDeg) > s.RollHardDeg;
                if (overHard && !Tripped)
                {
                    Tripped = true; TripCount++;
                    tripMsg = string.Format("Tilt {0:F1}/{1:F1} deg exceeded hard limit", st.PitchDeg, st.RollDeg);
                    trip = TripRaised;
                }
                if (Tripped)
                {
                    for (int i = 0; i < 4; i++) _g[i] = s.MinGain;
                    if (!overHard)
                    {
                        if (double.IsNaN(_tripClearSince)) _tripClearSince = t;
                        if (s.TripAutoReset && t - _tripClearSince > 1.0) { Tripped = false; _tripClearSince = double.NaN; }
                    }
                    else _tripClearSince = double.NaN;
                    _status = ControllerStatus.Tripped;
                    Publish();
                }
                else
                {
                    // --- impact / end-stop ------------------------------------------------------
                    if (st.PeakDevG > s.ImpactG)
                    {
                        _g[0] = Math.Max(s.MinGain, _g[0] * (1 - s.ImpactStep));
                        _impactHoldUntil = t + s.ImpactHoldSec;
                        ImpactCount++;
                        impact = ImpactDetected;
                    }

                    // --- envelope levels (1.0 == at the limit) ----------------------------------
                    _level[1] = Math.Max(Math.Abs(st.PitchDeg) / s.PitchLimitDeg, Math.Abs(st.PitchRateDps) / s.RateLimitDps);
                    _level[2] = Math.Max(Math.Abs(st.RollDeg) / s.RollLimitDeg, Math.Abs(st.RollRateDps) / s.RateLimitDps);
                    _level[3] = Math.Max(Math.Abs(st.CueHeaveG) / s.HeaveLimitG, st.VibRmsG / s.VibLimitG);
                    _level[0] = Math.Sqrt(st.CueSurgeG * st.CueSurgeG + st.CueSwayG * st.CueSwayG) / s.CueLimitG;

                    bool hold = t < _impactHoldUntil;
                    bool compressing = false;
                    double aA = 1 - Math.Exp(-dt / s.AttackSec);
                    double aR = 1 - Math.Exp(-dt / s.ReleaseSec);
                    for (int i = 0; i < 4; i++)
                    {
                        double lvl = _level[i];
                        if (lvl > s.Knee)
                        {
                            // measured level already includes the current gain: scale it back onto the knee
                            double target = Math.Max(s.MinGain, _g[i] * s.Knee / lvl);
                            _g[i] += (target - _g[i]) * aA;
                            compressing = true;
                        }
                        else if (lvl < s.ReleaseBelow && !hold)
                        {
                            _g[i] += (1.0 - _g[i]) * aR;
                        }
                        _g[i] = Clamp(_g[i], s.MinGain, 1.0);
                    }
                    _status = hold ? ControllerStatus.ImpactHold : compressing ? ControllerStatus.Compressing : ControllerStatus.Ok;
                    Publish();
                }
            }
            if (trip != null) trip(tripMsg);
            if (impact != null) impact();
        }

        /// <summary>Call from SimHub's DataUpdate (≈60 Hz) to detect a silent sensor.</summary>
        public void Tick(double now)
        {
            lock (_lock)
            {
                double dt = double.IsNaN(_lastTickTime) ? 0.016 : Clamp(now - _lastTickTime, 0.0, 0.2);
                _lastTickTime = now;
                if (double.IsNaN(_lastFrameTime)) { _status = ControllerStatus.WaitingForSensor; Publish(); return; }
                if (now - _lastFrameTime > _s.StaleSec)
                {
                    ApplyFailsafe(dt);
                    if (!Tripped) _status = ControllerStatus.SensorLost;
                    Publish();
                }
            }
        }

        // Ramp (over ~1 s) down to the failsafe gain; never ramp *up* while blind.
        private void ApplyFailsafe(double dt)
        {
            double a = 1 - Math.Exp(-dt / 0.35);
            for (int i = 0; i < 4; i++)
                if (_g[i] > _s.FailsafeGain) _g[i] += (_s.FailsafeGain - _g[i]) * a;
        }

        private void Publish() => _snap = new GainSnapshot(_g[0], _g[1], _g[2], _g[3], _status);

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
