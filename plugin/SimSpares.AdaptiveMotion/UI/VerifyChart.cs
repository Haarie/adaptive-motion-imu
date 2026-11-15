using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SimSpares.AdaptiveMotion.Core;

namespace SimSpares.AdaptiveMotion.UI
{
    /// <summary>
    /// Verify tab graphic: one bar per test pose (measured attitude, coloured by tolerance) against a tick
    /// for the commanded value, plus summary cards and a live "commanded vs measured" strip.
    /// Drawn with a DrawingContext on a 900 x 560 design canvas, scaled to fit (same approach as the live view).
    /// </summary>
    public class VerifyChart : FrameworkElement
    {
        private const double DesignW = 900, DesignH = 560;
        private readonly AdaptiveMotionPlugin _p;
        private readonly DispatcherTimer _timer;
        private double _pixelsPerDip = 1.0;

        private static readonly Brush Bg = Frozen(0x1B, 0x1D, 0x21), PanelB = Frozen(0x24, 0x27, 0x2D), TrackB = Frozen(0x33, 0x37, 0x3E);
        private static readonly Brush Ink = Frozen(0xE8, 0xEA, 0xED), Quiet = Frozen(0x9A, 0xA0, 0xA6);
        private static readonly Brush Good = Frozen(0x3F, 0xB9, 0x50), Warn = Frozen(0xE3, 0xB3, 0x41), Bad = Frozen(0xE5, 0x48, 0x4D), Busy = Frozen(0x4F, 0xA3, 0xF7);
        private static readonly Brush PitchC = Frozen(0x4F, 0xA3, 0xF7), RollC = Frozen(0xF5, 0x8A, 0x3C);
        private static readonly Pen GridPen = FrozenPen(0x3A, 0x3F, 0x47, 1), TickPen = FrozenPen(0xE8, 0xEA, 0xED, 2.5), ZeroPen = FrozenPen(0x6B, 0x71, 0x78, 1);
        private static readonly Typeface Face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface FaceBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public VerifyChart(AdaptiveMotionPlugin plugin)
        {
            _p = plugin;
            MinWidth = 300; MinHeight = 190;
            SnapsToDevicePixels = true;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += (o, e) => InvalidateVisual();
            Loaded += (o, e) =>
            {
                try { _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { _pixelsPerDip = 1.0; }
                _timer.Start();
            };
            Unloaded += (o, e) => _timer.Stop();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) ? DesignW : availableSize.Width;
            double h = double.IsInfinity(availableSize.Height) ? w * DesignH / DesignW : availableSize.Height;
            double s = Math.Min(w / DesignW, h / DesignH);
            return new Size(DesignW * s, DesignH * s);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double s = Math.Min(ActualWidth / DesignW, ActualHeight / DesignH);
            if (s <= 0 || double.IsNaN(s)) return;
            dc.PushTransform(new TranslateTransform((ActualWidth - DesignW * s) / 2, (ActualHeight - DesignH * s) / 2));
            dc.PushTransform(new ScaleTransform(s, s));
            try { Draw(dc); }
            catch (Exception ex) { Text(dc, "Chart error: " + ex.Message, 20, 20, 13, Bad, false); }
            dc.Pop(); dc.Pop();
        }

        private void Draw(DrawingContext dc)
        {
            var run = _p.RunForDisplay;
            var rep = _p.Settings.LastVerification;
            bool running = _p.Verifying;
            dc.DrawRoundedRectangle(Bg, null, new Rect(0, 0, DesignW, DesignH), 10, 10);

            // ---- header + status pill
            Text(dc, "Rig verification: commanded vs measured", 16, 12, 17, Ink, true);
            string status; Brush sb;
            if (running && run != null) { status = string.Format(CultureInfo.InvariantCulture, "RUNNING  {0:F0} %", run.Progress * 100); sb = Busy; }
            else if (rep == null) { status = "NOT RUN YET"; sb = Quiet; }
            else if (!_p.VerificationValid && rep.Passed) { status = "SETTINGS CHANGED: RUN AGAIN"; sb = Warn; }
            else { status = rep.Passed ? "PASSED" : rep.Overall == "Warn" ? "CLOSE, NOT PASSED" : rep.Overall == "Aborted" ? "STOPPED" : "FAILED"; sb = rep.Passed ? Good : rep.Overall == "Warn" ? Warn : Bad; }
            double pw = Measure(status, 13, true) + 24;
            dc.DrawRoundedRectangle(sb, null, new Rect(DesignW - 16 - pw, 10, pw, 26), 13, 13);
            Text(dc, status, DesignW - 16 - pw + 12, 14, 13, Bg, true);

            // ---- summary cards
            double pScale = run?.Scale(VerifyAxis.Pitch) ?? rep?.PitchScale ?? double.NaN;
            double rScale = run?.Scale(VerifyAxis.Roll) ?? rep?.RollScale ?? double.NaN;
            double xt = run != null ? run.MaxCrossTalkDeg : rep?.MaxCrossTalkDeg ?? double.NaN;
            var zero = run?.Steps.FirstOrDefault(x => x.TargetDeg == 0);
            double zp = zero?.MeasuredDeg ?? rep?.ZeroPitchDeg ?? double.NaN, zr = zero?.CrossDeg ?? rep?.ZeroRollDeg ?? double.NaN;
            var tol = (run?.Options ?? _p.Settings.Verify);
            Card(dc, 16, "Pitch scale", Fmt(pScale, "F3"), ScaleBrush(pScale, tol));
            Card(dc, 236, "Roll scale", Fmt(rScale, "F3"), ScaleBrush(rScale, tol));
            Card(dc, 456, "Max cross-talk", double.IsNaN(xt) || (run == null && rep == null) ? "--" : xt.ToString("F2", CultureInfo.InvariantCulture) + "°",
                 double.IsNaN(xt) || (run == null && rep == null) ? Ink : xt <= tol.CrossTalkMaxDeg ? Good : xt <= 2 * tol.CrossTalkMaxDeg ? Warn : Bad);
            Card(dc, 676, "Zero (pitch / roll)", double.IsNaN(zp) ? "--" : string.Format(CultureInfo.InvariantCulture, "{0:F2}° / {1:F2}°", zp, zr),
                 double.IsNaN(zp) ? Ink : Math.Max(Math.Abs(zp), Math.Abs(zr)) <= tol.TolMinDeg ? Good : Bad);

            // ---- live strip: what is commanded now vs what the seat does
            var st = _p.State;
            _p.CommandedPose(out var cp, out var cr);
            dc.DrawRoundedRectangle(PanelB, null, new Rect(16, 112, 868, 54), 8, 8);
            Text(dc, "Now", 28, 120, 12, Quiet, true);
            double limP = Math.Max(1, _p.Settings.Rig.MaxPitchDeg), limR = Math.Max(1, _p.Settings.Rig.MaxRollDeg);
            LiveBar(dc, 70, 118, 390, "Pitch", cp, st.PitchDeg, limP, PitchC);
            LiveBar(dc, 486, 118, 390, "Roll", cr, st.RollDeg, limR, RollC);

            // ---- per-pose rows
            dc.DrawRoundedRectangle(PanelB, null, new Rect(16, 176, 868, 372), 8, 8);
            var steps = run?.Steps.Select(x => new Row(x.Label, x.Axis, x.TargetDeg, x.MeasuredDeg, x.CrossDeg, x.Grade, x.CrossGrade, run.Current == x && running)).ToList()
                     ?? rep?.Steps.Select(x => new Row(x.TargetDeg == 0 ? "Zero" : string.Format(CultureInfo.InvariantCulture, "{0} {1:+0.0;-0.0}°", x.Axis, x.TargetDeg),
                            x.Axis == "Roll" ? VerifyAxis.Roll : VerifyAxis.Pitch, x.TargetDeg, x.MeasuredDeg, x.CrossDeg, Parse(x.Grade), Parse(x.CrossGrade), false)).ToList();
            if (steps == null || steps.Count == 0)
            {
                Text(dc, "Press Run test. The rig moves through ¼, ½, ¾ and full pitch and roll of your SimHub limits", 32, 196, 13, Quiet, false);
                Text(dc, "and every pose is compared with what the IMU measures on the seat.", 32, 216, 13, Quiet, false);
                Text(dc, string.Format(CultureInfo.InvariantCulture, "Green: within {0} % or {1}°.  Amber: within {2} % or {3}°.  Red: outside.  Cross-talk: the other axis stays within {4}°.",
                    tol.TolPct, tol.TolMinDeg, tol.WarnPct, tol.WarnMinDeg, tol.CrossTalkMaxDeg), 32, 246, 13, Quiet, false);
                return;
            }
            Text(dc, "Pose", 28, 184, 11, Quiet, true);
            Text(dc, "measured (bar) vs commanded (tick)", 300, 184, 11, Quiet, true);
            Text(dc, "measured", 702, 184, 11, Quiet, true);
            Text(dc, "cross", 800, 184, 11, Quiet, true);
            double rowH = Math.Min(20, 340.0 / steps.Count);
            double y = 202;
            double bx = 150, bw = 530;
            foreach (var r in steps)
            {
                double lim = r.Axis == VerifyAxis.Pitch ? limP : limR;
                double range = lim * 1.15;
                if (r.Current) dc.DrawRoundedRectangle(TrackB, null, new Rect(20, y - 1, 860, rowH), 3, 3);
                Text(dc, r.Label, 28, y + rowH / 2 - 8, 12, r.Axis == VerifyAxis.Pitch || r.Target == 0 ? Ink : Ink, false);
                double mid = bx + bw / 2;
                dc.DrawRectangle(TrackB, null, new Rect(bx, y + rowH * 0.2, bw, rowH * 0.6));
                dc.DrawLine(ZeroPen, new Point(mid, y + 1), new Point(mid, y + rowH - 1));
                if (!double.IsNaN(r.Measured))
                {
                    double xm = mid + Clamp(r.Measured / range, -1, 1) * bw / 2;
                    dc.DrawRectangle(GradeBrush(r.Grade), null, new Rect(Math.Min(mid, xm), y + rowH * 0.28, Math.Max(1.5, Math.Abs(xm - mid)), rowH * 0.44));
                }
                double xc = mid + Clamp(r.Target / range, -1, 1) * bw / 2;
                dc.DrawLine(TickPen, new Point(xc, y + 1), new Point(xc, y + rowH - 1));
                if (!double.IsNaN(r.Measured))
                {
                    Text(dc, string.Format(CultureInfo.InvariantCulture, "{0:+0.00;-0.00}° ({1:+0.00;-0.00})", r.Measured, r.Measured - r.Target), 700, y + rowH / 2 - 8, 12, GradeBrush(r.Grade), false);
                    if (r.Target != 0)
                    {
                        dc.DrawEllipse(GradeBrush(r.CrossGrade), null, new Point(806, y + rowH / 2), 4, 4);
                        Text(dc, string.Format(CultureInfo.InvariantCulture, "{0:F2}°", Math.Abs(r.Cross)), 816, y + rowH / 2 - 8, 12, Quiet, false);
                    }
                }
                y += rowH;
            }
        }

        private sealed class Row
        {
            public readonly string Label; public readonly VerifyAxis Axis; public readonly double Target, Measured, Cross;
            public readonly VerifyGrade Grade, CrossGrade; public readonly bool Current;
            public Row(string l, VerifyAxis a, double t, double m, double c, VerifyGrade g, VerifyGrade cg, bool cur)
            { Label = l; Axis = a; Target = t; Measured = m; Cross = c; Grade = g; CrossGrade = cg; Current = cur; }
        }

        private void LiveBar(DrawingContext dc, double x, double y, double w, string label, double cmd, double meas, double lim, Brush color)
        {
            Text(dc, label, x, y, 12, color, true);
            double bx = x + 44, bw = w - 150, mid = bx + bw / 2, range = lim * 1.15;
            dc.DrawRectangle(TrackB, null, new Rect(bx, y + 4, bw, 12));
            dc.DrawLine(ZeroPen, new Point(mid, y + 2), new Point(mid, y + 18));
            if (!double.IsNaN(meas))
            {
                double xm = mid + Clamp(meas / range, -1, 1) * bw / 2;
                dc.DrawRectangle(color, null, new Rect(Math.Min(mid, xm), y + 6, Math.Max(1.5, Math.Abs(xm - mid)), 8));
            }
            if (!double.IsNaN(cmd)) { double xc = mid + Clamp(cmd / range, -1, 1) * bw / 2; dc.DrawLine(TickPen, new Point(xc, y + 1), new Point(xc, y + 19)); }
            Text(dc, string.Format(CultureInfo.InvariantCulture, "cmd {0}  seat {1}", double.IsNaN(cmd) ? "--" : cmd.ToString("F1", CultureInfo.InvariantCulture) + "°",
                double.IsNaN(meas) ? "--" : meas.ToString("F1", CultureInfo.InvariantCulture) + "°"), bx + bw + 8, y + 2, 12, Ink, false);
            Text(dc, string.Format(CultureInfo.InvariantCulture, "±{0:F1}° limit", lim), bx, y + 22, 10, Quiet, false);
        }

        private void Card(DrawingContext dc, double x, string label, string value, Brush valueBrush)
        {
            dc.DrawRoundedRectangle(PanelB, null, new Rect(x, 46, 208, 56), 8, 8);
            Text(dc, label, x + 12, 52, 12, Quiet, false);
            Text(dc, value, x + 12, 70, 20, valueBrush, true);
        }

        private static Brush ScaleBrush(double k, VerifyOptions o) =>
            double.IsNaN(k) ? Ink : Math.Abs(k - 1) <= o.TolPct / 100 ? Good : Math.Abs(k - 1) <= o.WarnPct / 100 ? Warn : Bad;
        private static Brush GradeBrush(VerifyGrade g) => g == VerifyGrade.Pass ? Good : g == VerifyGrade.Warn ? Warn : g == VerifyGrade.Fail ? Bad : Quiet;
        private static VerifyGrade Parse(string s) { VerifyGrade g; return Enum.TryParse(s, out g) ? g : VerifyGrade.Pending; }
        private static string Fmt(double v, string f) => double.IsNaN(v) ? "--" : v.ToString(f, CultureInfo.InvariantCulture);
        private static double Clamp(double v, double a, double b) => v < a ? a : v > b ? b : v;

        private void Text(DrawingContext dc, string s, double x, double y, double size, Brush b, bool bold) =>
            dc.DrawText(new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? FaceBold : Face, size, b, _pixelsPerDip), new Point(x, y));
        private double Measure(string s, double size, bool bold) =>
            new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? FaceBold : Face, size, Ink, _pixelsPerDip).Width;

        private static Brush Frozen(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
        private static Pen FrozenPen(byte r, byte g, byte b, double t) { var p = new Pen(new SolidColorBrush(Color.FromRgb(r, g, b)), t); p.Freeze(); return p; }
    }
}
