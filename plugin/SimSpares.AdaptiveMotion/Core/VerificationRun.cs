using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SimSpares.AdaptiveMotion.Core
{
    public enum VerifyAxis { Pitch, Roll }
    public enum VerifyGrade { Pending, Pass, Warn, Fail }
    public enum VerifyPhase { Idle, Moving, Settling, Measuring, Returning, Finished, Aborted }

    /// <summary>Test options. Poses are fractions of the SimHub limits, never more than the limit itself.</summary>
    public class VerifyOptions
    {
        public double[] Fractions = { 0.25, 0.5, 0.75, 1.0 };
        public double MaxFractionPct = 100;   // cap for the largest pose (% of the SimHub limit)
        public double RampDps = 4;            // how fast the test moves between poses (deg/s)
        public double SettleSec = 1.5;        // wait after arriving before measuring
        public double MeasureSec = 1.0;       // averaging window
        public double TolPct = 5, TolMinDeg = 0.3;     // green: within max(5 %, 0.3°)
        public double WarnPct = 10, WarnMinDeg = 0.6;  // amber: within max(10 %, 0.6°)
        public double CrossTalkMaxDeg = 0.5;           // the other axis must stay within this
        public double AbortOvershootDeg = 3;           // abort when the seat goes this far past the limit
        public double AbortWrongWayDeg = 2;            // abort when the seat moves this far the WRONG way
        public double HoldSec = 5;                     // keep the rig at centre this long after the test, then hand it back
        public double LeadInSec = 6;                   // controller unpark + blend into live data before the first measurement (nullCAT: 3 s + 2 s)

        public VerifyOptions Clone() { var o = (VerifyOptions)MemberwiseClone(); o.Fractions = (double[])Fractions.Clone(); return o; }
    }

    public class VerifyStep
    {
        public VerifyAxis Axis;
        public double Fraction;          // of the limit (signed); 0 = the zero check
        public double TargetDeg;         // commanded on the test axis
        public double MeasuredDeg = double.NaN;   // measured on the test axis
        public double CrossDeg = double.NaN;      // measured on the other axis
        public double ErrorDeg => MeasuredDeg - TargetDeg;
        public VerifyGrade Grade = VerifyGrade.Pending;
        public VerifyGrade CrossGrade = VerifyGrade.Pending;

        public string Label => TargetDeg == 0 ? "Zero" : string.Format(CultureInfo.InvariantCulture, "{0} {1:+0.0;-0.0}°", Axis, TargetDeg);
    }

    /// <summary>
    /// Static verification: moves the rig through a series of pitch/roll poses (fractions of the SimHub
    /// limits), waits for it to settle, averages what the IMU measures and grades every pose.
    /// Pure logic: the caller feeds time and the measured attitude and sends <see cref="CommandPitchDeg"/> /
    /// <see cref="CommandRollDeg"/> to the rig. Thread-safety is the caller's job (one lock around it).
    /// </summary>
    public sealed class VerificationRun
    {
        public readonly VerifyOptions Options;
        public readonly double LimitPitchDeg, LimitRollDeg;
        public readonly List<VerifyStep> Steps = new List<VerifyStep>();

        public VerifyPhase Phase { get; private set; } = VerifyPhase.Idle;
        public int Index { get; private set; } = -1;
        public double CommandPitchDeg { get; private set; }
        public double CommandRollDeg { get; private set; }
        public string AbortReason { get; private set; }
        public bool Done => Phase == VerifyPhase.Finished || Phase == VerifyPhase.Aborted;
        public VerifyStep Current => Index >= 0 && Index < Steps.Count ? Steps[Index] : null;
        public double Progress => Steps.Count == 0 ? 0 : Math.Min(1, Steps.Count(s => s.Grade != VerifyGrade.Pending) / (double)Steps.Count);

        private double _lastSec = double.NaN, _phaseStart, _sumMain, _sumCross;
        private int _n;
        private bool _stopRequested;
        private string _pendingAbort;

        public VerificationRun(double limitPitchDeg, double limitRollDeg, VerifyOptions options)
        {
            if (!(limitPitchDeg > 0) || !(limitRollDeg > 0)) throw new ArgumentException("pitch and roll limits must be > 0");
            Options = options ?? new VerifyOptions();
            LimitPitchDeg = limitPitchDeg; LimitRollDeg = limitRollDeg;
            double cap = Math.Max(0.05, Math.Min(1.0, Options.MaxFractionPct / 100.0));
            Steps.Add(new VerifyStep { Axis = VerifyAxis.Pitch, Fraction = 0, TargetDeg = 0 });
            foreach (var axis in new[] { VerifyAxis.Pitch, VerifyAxis.Roll })
            {
                double lim = axis == VerifyAxis.Pitch ? limitPitchDeg : limitRollDeg;
                foreach (var f0 in Options.Fractions.Select(f => Math.Min(f, cap)).Distinct().OrderBy(f => f))
                    foreach (var sign in new[] { 1.0, -1.0 })
                        Steps.Add(new VerifyStep { Axis = axis, Fraction = sign * f0, TargetDeg = Math.Round(sign * f0 * lim, 2) });
            }
        }

        /// <summary>Stop: the rig is ramped back to zero, then the run ends as Aborted.</summary>
        public void RequestStop(string reason = "stopped by user") { if (!Done && !_stopRequested) { _stopRequested = true; _pendingAbort = reason; } }

        /// <summary>Hard abort (sensor lost etc.): still ramps back to zero, the caller keeps sending commands until Done.</summary>
        public void Abort(string reason) => RequestStop(reason);

        /// <summary>Advance the run. Call at a steady rate (≥ 50 Hz) with the latest measured attitude.</summary>
        public void Update(double nowSec, double measuredPitchDeg, double measuredRollDeg)
        {
            if (Done) return;
            double dt = double.IsNaN(_lastSec) ? 0 : Math.Max(0, Math.Min(0.2, nowSec - _lastSec));
            _lastSec = nowSec;
            if (Phase == VerifyPhase.Idle) { Index = 0; Begin(VerifyPhase.Moving, nowSec); }

            // safety: the seat must not run away from the command
            if (Phase != VerifyPhase.Returning)
            {
                CheckRunaway(VerifyAxis.Pitch, measuredPitchDeg, CommandPitchDeg, LimitPitchDeg);
                CheckRunaway(VerifyAxis.Roll, measuredRollDeg, CommandRollDeg, LimitRollDeg);
            }
            if (_stopRequested && Phase != VerifyPhase.Returning) Begin(VerifyPhase.Returning, nowSec);

            if (Phase == VerifyPhase.Returning)
            {
                bool home = RampTo(0, 0, dt);
                if (home)
                {
                    if (_pendingAbort != null) { AbortReason = _pendingAbort; Phase = VerifyPhase.Aborted; }
                    else Phase = VerifyPhase.Finished;
                }
                return;
            }

            var st = Current;
            double tp = st.Axis == VerifyAxis.Pitch ? st.TargetDeg : 0;
            double tr = st.Axis == VerifyAxis.Roll ? st.TargetDeg : 0;
            switch (Phase)
            {
                case VerifyPhase.Moving:
                    if (RampTo(tp, tr, dt)) Begin(VerifyPhase.Settling, nowSec);
                    break;
                case VerifyPhase.Settling:
                    if (nowSec - _phaseStart >= Options.SettleSec + (Index == 0 ? Options.LeadInSec : 0)) { _sumMain = _sumCross = 0; _n = 0; Begin(VerifyPhase.Measuring, nowSec); }
                    break;
                case VerifyPhase.Measuring:
                    _sumMain += st.Axis == VerifyAxis.Pitch ? measuredPitchDeg : measuredRollDeg;
                    _sumCross += st.Axis == VerifyAxis.Pitch ? measuredRollDeg : measuredPitchDeg;
                    _n++;
                    if (nowSec - _phaseStart >= Options.MeasureSec && _n > 0)
                    {
                        st.MeasuredDeg = _sumMain / _n;
                        st.CrossDeg = _sumCross / _n;
                        Grade(st);
                        if (Index + 1 < Steps.Count) { Index++; Begin(VerifyPhase.Moving, nowSec); }
                        else Begin(VerifyPhase.Returning, nowSec);
                    }
                    break;
            }
        }

        private void CheckRunaway(VerifyAxis axis, double measured, double command, double limit)
        {
            if (double.IsNaN(measured)) { Abort("no sensor data"); return; }
            if (Math.Abs(measured) > limit + Options.AbortOvershootDeg)
                Abort(string.Format(CultureInfo.InvariantCulture, "{0} went to {1:F1}° (limit {2:F1}°)", axis, measured, limit));
            else if (Math.Abs(command) > Options.AbortWrongWayDeg + 1 && Math.Sign(measured) != Math.Sign(command) && Math.Abs(measured) > Options.AbortWrongWayDeg)
                Abort(string.Format(CultureInfo.InvariantCulture, "{0} moves the wrong way (commanded {1:F1}°, measured {2:F1}°)", axis, command, measured));
        }

        private void Begin(VerifyPhase p, double now) { Phase = p; _phaseStart = now; }

        private bool RampTo(double pitch, double roll, double dt)
        {
            double step = Options.RampDps * dt;
            CommandPitchDeg = Approach(CommandPitchDeg, pitch, step);
            CommandRollDeg = Approach(CommandRollDeg, roll, step);
            return CommandPitchDeg == pitch && CommandRollDeg == roll;
        }

        private static double Approach(double v, double target, double step)
        {
            if (Math.Abs(target - v) <= step) return target;
            return v + Math.Sign(target - v) * step;
        }

        public VerifyGrade GradeError(double target, double error)
        {
            double a = Math.Abs(error), t = Math.Abs(target);
            if (a <= Math.Max(t * Options.TolPct / 100, Options.TolMinDeg)) return VerifyGrade.Pass;
            if (a <= Math.Max(t * Options.WarnPct / 100, Options.WarnMinDeg)) return VerifyGrade.Warn;
            return VerifyGrade.Fail;
        }

        private void Grade(VerifyStep s)
        {
            s.Grade = GradeError(s.TargetDeg, s.ErrorDeg);
            double c = Math.Abs(s.CrossDeg);
            s.CrossGrade = c <= Options.CrossTalkMaxDeg ? VerifyGrade.Pass : c <= 2 * Options.CrossTalkMaxDeg ? VerifyGrade.Warn : VerifyGrade.Fail;
            if (s.TargetDeg == 0)   // zero check: both axes must read ~0
            {
                s.Grade = Worst(GradeError(0, s.MeasuredDeg), GradeError(0, s.CrossDeg));
                s.CrossGrade = VerifyGrade.Pass;
            }
        }

        public static VerifyGrade Worst(VerifyGrade a, VerifyGrade b) => (VerifyGrade)Math.Max((int)a, (int)b);

        // ----------------------------------------------------------------------------------------
        // Summary
        // ----------------------------------------------------------------------------------------
        /// <summary>Slope of measured vs commanded on an axis (1.00 = exact), through the origin.</summary>
        public double Scale(VerifyAxis axis)
        {
            double sxy = 0, sxx = 0;
            foreach (var s in Steps.Where(s => s.Axis == axis && s.TargetDeg != 0 && !double.IsNaN(s.MeasuredDeg)))
            { sxy += s.TargetDeg * s.MeasuredDeg; sxx += s.TargetDeg * s.TargetDeg; }
            return sxx > 0 ? sxy / sxx : double.NaN;
        }

        public double MaxCrossTalkDeg => Steps.Where(s => s.TargetDeg != 0 && !double.IsNaN(s.CrossDeg)).Select(s => Math.Abs(s.CrossDeg)).DefaultIfEmpty(0).Max();

        public VerifyGrade Overall
        {
            get
            {
                if (Phase == VerifyPhase.Aborted) return VerifyGrade.Fail;
                if (Steps.Any(s => s.Grade == VerifyGrade.Pending)) return VerifyGrade.Pending;
                var g = VerifyGrade.Pass;
                foreach (var s in Steps) g = Worst(Worst(g, s.Grade), s.CrossGrade);
                return g;
            }
        }

        /// <summary>Plain-language findings that point at the likely cause.</summary>
        public List<string> Findings()
        {
            var f = new List<string>();
            if (Phase == VerifyPhase.Aborted) f.Add("Test stopped: " + AbortReason + ".");
            foreach (var axis in new[] { VerifyAxis.Pitch, VerifyAxis.Roll })
            {
                double k = Scale(axis);
                if (double.IsNaN(k)) continue;
                string ax = axis.ToString().ToLowerInvariant();
                if (k < 0)
                    f.Add(string.Format("{0} moves the opposite way: check the mounting setting (forward/up) or the actuator direction.", axis));
                else if (Math.Abs(k - 0.5) < 0.08 || Math.Abs(k - 2.0) < 0.25)
                    f.Add(string.Format(CultureInfo.InvariantCulture, "{0} is off by a factor of about {1:F1}: check the ball-screw lead / steps per mm in the controller and the stroke setting in SimHub.", axis, k < 1 ? 0.5 : 2.0));
                else if (Math.Abs(k - 1) > Options.TolPct / 100)
                    f.Add(string.Format(CultureInfo.InvariantCulture, "{0} comes out at {1:F0} % of the command: check the actuator distances and stroke (SimHub rig dimensions) against the real rig.", axis, k * 100));

                // limit reachable?
                var top = Steps.Where(s => s.Axis == axis && Math.Abs(Math.Abs(s.Fraction) - 1) < 1e-9 && !double.IsNaN(s.MeasuredDeg)).ToList();
                var mid = Steps.Where(s => s.Axis == axis && Math.Abs(s.Fraction) > 0 && Math.Abs(s.Fraction) < 1 && !double.IsNaN(s.MeasuredDeg)).ToList();
                if (top.Count > 0 && mid.Count > 0 && mid.All(s => s.Grade != VerifyGrade.Fail))
                {
                    foreach (var t in top.Where(t => t.Grade == VerifyGrade.Fail && Math.Abs(t.MeasuredDeg) < Math.Abs(t.TargetDeg)))
                        f.Add(string.Format(CultureInfo.InvariantCulture, "{0} limit {1:+0.0;-0.0}° is not reachable (seat stops at {2:+0.0;-0.0}°): lower the {3} limit in SimHub to about {4:F1}°.",
                            axis, t.TargetDeg, t.MeasuredDeg, ax, Math.Floor(Math.Abs(t.MeasuredDeg) * 10) / 10));
                }
            }
            var zero = Steps.FirstOrDefault(s => s.TargetDeg == 0);
            if (zero != null && zero.Grade == VerifyGrade.Fail)
                f.Add(string.Format(CultureInfo.InvariantCulture, "At zero the seat reads pitch {0:F1}°, roll {1:F1}°: capture the level again with the rig at its centre position.", zero.MeasuredDeg, zero.CrossDeg));
            if (MaxCrossTalkDeg > Options.CrossTalkMaxDeg && !f.Any(x => x.Contains("opposite")))
                f.Add(string.Format(CultureInfo.InvariantCulture, "Pitch and roll influence each other (up to {0:F1}°): the box is probably not square to the rig. Run Auto-align, or check the actuator order in SimHub.", MaxCrossTalkDeg));
            if (f.Count == 0 && Overall == VerifyGrade.Pass) f.Add("Every pose is within tolerance: commanded and measured motion match.");
            return f;
        }

        public string Csv()
        {
            var sb = new StringBuilder("axis,fraction,target_deg,measured_deg,error_deg,cross_deg,grade,cross_grade\n");
            foreach (var s in Steps)
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0},{1:F2},{2:F2},{3:F3},{4:F3},{5:F3},{6},{7}\n",
                    s.Axis, s.Fraction, s.TargetDeg, s.MeasuredDeg, s.ErrorDeg, s.CrossDeg, s.Grade, s.CrossGrade);
            return sb.ToString();
        }
    }
}
