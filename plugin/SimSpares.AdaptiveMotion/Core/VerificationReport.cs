using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>
    /// Result of the last verification, stored with the settings. Closed loop is only allowed while a
    /// passed report exists whose <see cref="SettingsHash"/> still matches the current configuration.
    /// </summary>
    public class VerificationReport
    {
        public DateTime WhenUtc;
        public bool Passed;
        public string Overall = "";
        public string SettingsHash = "";
        public string Plugin = "", Firmware = "";
        public double LimitPitchDeg, LimitRollDeg;
        public double PitchScale = double.NaN, RollScale = double.NaN, MaxCrossTalkDeg;
        public double ZeroPitchDeg = double.NaN, ZeroRollDeg = double.NaN;
        public List<VerifyStepRecord> Steps = new List<VerifyStepRecord>();
        public List<string> Findings = new List<string>();

        public static VerificationReport From(VerificationRun run, string hash, string plugin, string firmware)
        {
            var zero = run.Steps.FirstOrDefault(s => s.TargetDeg == 0);
            var r = new VerificationReport
            {
                WhenUtc = DateTime.UtcNow,
                Passed = run.Phase == VerifyPhase.Finished && run.Overall == VerifyGrade.Pass,
                Overall = run.Phase == VerifyPhase.Aborted ? "Aborted" : run.Overall.ToString(),
                SettingsHash = hash, Plugin = plugin ?? "", Firmware = firmware ?? "",
                LimitPitchDeg = run.LimitPitchDeg, LimitRollDeg = run.LimitRollDeg,
                PitchScale = run.Scale(VerifyAxis.Pitch), RollScale = run.Scale(VerifyAxis.Roll),
                MaxCrossTalkDeg = run.MaxCrossTalkDeg,
                ZeroPitchDeg = zero?.MeasuredDeg ?? double.NaN, ZeroRollDeg = zero?.CrossDeg ?? double.NaN,
                Findings = run.Findings(),
            };
            foreach (var s in run.Steps)
                r.Steps.Add(new VerifyStepRecord { Axis = s.Axis.ToString(), TargetDeg = s.TargetDeg, MeasuredDeg = s.MeasuredDeg, CrossDeg = s.CrossDeg, Grade = s.Grade.ToString(), CrossGrade = s.CrossGrade.ToString() });
            return r;
        }

        public bool ValidFor(string currentHash) => Passed && !string.IsNullOrEmpty(SettingsHash) && SettingsHash == currentHash;

        /// <summary>Fingerprint of everything that changes how commands become seat motion.</summary>
        public static string Fingerprint(params object[] parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts) sb.Append(Describe(p)).Append('|');
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
        }

        private static string Describe(object o)
        {
            if (o == null) return "null";
            if (o is double d) return Math.Round(d, 3).ToString(CultureInfo.InvariantCulture);
            if (o is double[] a) return string.Join(",", a.Select(v => Math.Round(v, 3).ToString(CultureInfo.InvariantCulture)));
            if (o is string s) return s;
            if (o is Enum || o is bool || o is int) return o.ToString();
            var t = o.GetType();
            var fields = t.GetFields().OrderBy(f => f.Name).Select(f => f.Name + "=" + Describe(f.GetValue(o)));
            return t.Name + "{" + string.Join(";", fields) + "}";
        }
    }

    public class VerifyStepRecord
    {
        public string Axis = "", Grade = "", CrossGrade = "";
        public double TargetDeg, MeasuredDeg, CrossDeg;
    }
}
