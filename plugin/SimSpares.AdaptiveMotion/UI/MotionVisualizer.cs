using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SimSpares.AdaptiveMotion.Core;

namespace SimSpares.AdaptiveMotion.UI
{
    /// <summary>
    /// Live picture of what the seat IMU measures and what the controller does with it, drawn with
    /// DrawingContext (no XAML, no third-party controls). Laid out on a 900 x 540 design canvas and
    /// scaled uniformly to whatever space it gets, so it works in the settings tab and a pop-out window.
    ///   - artificial horizon (pitch/roll) with limit and hard-trip marks
    ///   - g-g plot of the felt horizontal cue with a 2 s trail and the cue limit ring
    ///   - meters: pitch/roll rate, heave cue, vibration RMS, peak g (impact threshold)
    ///   - controller gains (global/pitch/roll/heave) and status
    ///   - 10 s pitch/roll trend with limit lines
    /// </summary>
    public class MotionVisualizer : FrameworkElement
    {
        private const double DesignW = 900, DesignH = 540;
        private const double TrendSeconds = 10, TrailSeconds = 2, TickHz = 30;

        private readonly AdaptiveMotionPlugin _p;
        private readonly DispatcherTimer _timer;
        private readonly TrendBuffer _trend = new TrendBuffer((int)(TrendSeconds * TickHz) + 2);
        private readonly TrendBuffer _trail = new TrendBuffer((int)(TrailSeconds * TickHz) + 2);

        // palette (dark, readable on SimHub's dark theme and on a rig screen)
        private static readonly Brush Bg = Frozen(Color.FromRgb(0x1B, 0x1D, 0x21));
        private static readonly Brush Panel = Frozen(Color.FromRgb(0x24, 0x27, 0x2D));
        private static readonly Brush Ink = Frozen(Color.FromRgb(0xE8, 0xEA, 0xED));
        private static readonly Brush Quiet = Frozen(Color.FromRgb(0x9A, 0xA0, 0xA6));
        private static readonly Brush Sky = Frozen(Color.FromRgb(0x2E, 0x6C, 0xA8));
        private static readonly Brush Ground = Frozen(Color.FromRgb(0x77, 0x51, 0x2E));
        private static readonly Brush Accent = Frozen(Color.FromRgb(0xF2, 0xB1, 0x34));
        private static readonly Brush PitchC = Frozen(Color.FromRgb(0x4F, 0xA3, 0xF7));
        private static readonly Brush RollC = Frozen(Color.FromRgb(0xF5, 0x8A, 0x3C));
        private static readonly Brush Good = Frozen(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly Brush Warn = Frozen(Color.FromRgb(0xE3, 0xB3, 0x41));
        private static readonly Brush Bad = Frozen(Color.FromRgb(0xE5, 0x48, 0x4D));
        private static readonly Brush Track = Frozen(Color.FromRgb(0x33, 0x37, 0x3E));
        private static readonly Pen GridPen = FrozenPen(Color.FromRgb(0x3A, 0x3F, 0x47), 1);
        private static readonly Pen InkPen = FrozenPen(Color.FromRgb(0xE8, 0xEA, 0xED), 2);
        private static readonly Pen LimitPen = FrozenDashed(Color.FromRgb(0xE3, 0xB3, 0x41), 1.2);
        private static readonly Pen HardPen = FrozenDashed(Color.FromRgb(0xE5, 0x48, 0x4D), 1.2);
        private static readonly Typeface Face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface FaceBold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private double _pixelsPerDip = 1.0;

        public MotionVisualizer(AdaptiveMotionPlugin plugin)
        {
            _p = plugin;
            MinWidth = 300; MinHeight = 180;
            SnapsToDevicePixels = true;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000.0 / TickHz) };
            _timer.Tick += (o, e) => { Sample(); InvalidateVisual(); };
            Loaded += (o, e) =>
            {
                try { _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { _pixelsPerDip = 1.0; }
                _timer.Start();
            };
            Unloaded += (o, e) => _timer.Stop();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            // Keep the design aspect ratio inside whatever we are offered
            double w = double.IsInfinity(availableSize.Width) ? DesignW : availableSize.Width;
            double h = double.IsInfinity(availableSize.Height) ? w * DesignH / DesignW : availableSize.Height;
            double s = Math.Min(w / DesignW, h / DesignH);
            return new Size(DesignW * s, DesignH * s);
        }

        private void Sample()
        {
            var st = _p.State;
            double t = ImuSerialLink_Now();
            _trend.Add(t, st.PitchDeg, st.RollDeg);
            _trail.Add(t, st.CueSurgeG, st.CueSwayG);
        }

        /// <summary>Clock used for the trend/trail; replaceable for previews and tests.</summary>
        internal static Func<double> Now = () => IO.ImuSerialLink.NowSec;
        private static double ImuSerialLink_Now() => Now();

        protected override void OnRender(DrawingContext dc)
        {
            double s = Math.Min(ActualWidth / DesignW, ActualHeight / DesignH);
            if (s <= 0 || double.IsNaN(s)) return;
            double ox = (ActualWidth - DesignW * s) / 2, oy = (ActualHeight - DesignH * s) / 2;
            dc.PushTransform(new TranslateTransform(ox, oy));
            dc.PushTransform(new ScaleTransform(s, s));
            try { Draw(dc); }
            catch (Exception ex) { Text(dc, "Visualizer error: " + ex.Message, 20, 20, 13, Bad, false); }
            dc.Pop(); dc.Pop();
        }

        // =========================================================================================
        private void Draw(DrawingContext dc)
        {
            var st = _p.State;
            var g = _p.Controller.Snapshot;
            var c = _p.Settings.Controller;
            bool connected = _p.SensorFresh;

            dc.DrawRoundedRectangle(Bg, null, new Rect(0, 0, DesignW, DesignH), 10, 10);
            DrawHeader(dc, st, g, connected);

            // panels
            dc.DrawRoundedRectangle(Panel, null, new Rect(12, 44, 276, 300), 8, 8);
            dc.DrawRoundedRectangle(Panel, null, new Rect(300, 44, 276, 300), 8, 8);
            dc.DrawRoundedRectangle(Panel, null, new Rect(588, 44, 300, 300), 8, 8);
            dc.DrawRoundedRectangle(Panel, null, new Rect(12, 356, 876, 172), 8, 8);

            DrawHorizon(dc, new Point(150, 180), 112, st, c, connected);
            DrawGg(dc, new Point(438, 180), 112, c, connected);
            DrawMeters(dc, st, c, connected);
            DrawGains(dc, g, c);
            DrawTrend(dc, new Rect(56, 384, 816, 128), c);
        }

        private void DrawHeader(DrawingContext dc, PlatformState st, GainSnapshot g, bool connected)
        {
            Text(dc, "Adaptive Motion — seat IMU", 16, 10, 17, Ink, true);
            Brush statusBrush = g.Status == ControllerStatus.Ok ? Good
                : g.Status == ControllerStatus.Tripped || g.Status == ControllerStatus.SensorLost ? Bad : Warn;
            string mode = _p.ProxyActive ? "APPLYING" : _p.Settings.Mode == ApplyMode.MonitorOnly ? "MONITOR ONLY" : "PASSTHROUGH";
            double x = 300;
            x = Badge(dc, g.Status.ToString().ToUpperInvariant(), x, 10, statusBrush);
            x = Badge(dc, mode, x + 8, 10, _p.ProxyActive ? Accent : Track);
            x = Badge(dc, connected ? string.Format(CultureInfo.InvariantCulture, "{0:F0} Hz", _p.FrameRate) : "NO SENSOR", x + 8, 10, connected ? Track : Bad);
            if ((st.Flags & ImuFlags.Still) != 0) x = Badge(dc, "STILL", x + 8, 10, Track);
            if (_p.Park.MountShifted) x = Badge(dc, "MOUNT SHIFTED", x + 8, 10, Bad);
            if ((st.Flags & ImuFlags.WhoAmIUnexpected) != 0) x = Badge(dc, "CLONE IMU", x + 8, 10, Warn);
            if (!_p.Settings.LevelCaptured) Badge(dc, "LEVEL NOT CAPTURED", x + 8, 10, Warn);
        }

        // ---- attitude indicator ------------------------------------------------------------------
        private void DrawHorizon(DrawingContext dc, Point ctr, double r, PlatformState st, ControllerSettings c, bool live)
        {
            double range = VisualMath.NiceRange(Math.Max(c.PitchHardDeg, c.RollHardDeg) * 1.15);
            double pxPerDeg = r / range;
            VisualMath.Horizon(live ? st.RollDeg : 0, live ? st.PitchDeg : 0, pxPerDeg, r, out double rot, out double shift);

            var clip = new EllipseGeometry(ctr, r, r);
            dc.PushClip(clip);
            dc.PushTransform(new RotateTransform(rot, ctr.X, ctr.Y));
            dc.PushTransform(new TranslateTransform(0, shift));
            dc.DrawRectangle(Sky, null, new Rect(ctr.X - 3 * r, ctr.Y - 3 * r, 6 * r, 3 * r));
            dc.DrawRectangle(Ground, null, new Rect(ctr.X - 3 * r, ctr.Y, 6 * r, 3 * r));
            dc.DrawLine(InkPen, new Point(ctr.X - 3 * r, ctr.Y), new Point(ctr.X + 3 * r, ctr.Y));
            // pitch ladder every 5 deg, labels every 10
            for (int d = -(int)range; d <= (int)range; d += 5)
            {
                if (d == 0) continue;
                double y = ctr.Y - d * pxPerDeg;
                double half = d % 10 == 0 ? 28 : 14;
                dc.DrawLine(GridPenLight, new Point(ctr.X - half, y), new Point(ctr.X + half, y));
                if (d % 10 == 0) Text(dc, Math.Abs(d).ToString(CultureInfo.InvariantCulture), ctr.X + half + 4, y - 7, 10, Ink, false);
            }
            // pitch limit (yellow) and hard trip (red) on the ladder
            foreach (var sgn in new[] { 1, -1 })
            {
                double yl = ctr.Y - sgn * c.PitchLimitDeg * pxPerDeg;
                double yh = ctr.Y - sgn * c.PitchHardDeg * pxPerDeg;
                dc.DrawLine(LimitPen, new Point(ctr.X - 48, yl), new Point(ctr.X + 48, yl));
                dc.DrawLine(HardPen, new Point(ctr.X - 60, yh), new Point(ctr.X + 60, yh));
            }
            dc.Pop(); dc.Pop(); dc.Pop();

            // roll scale on the bezel: ticks every 10 deg, limit + hard marks
            var bezel = new Pen(Track, 6);
            dc.DrawEllipse(null, bezel, ctr, r + 3, r + 3);
            for (int d = -30; d <= 30; d += 10) Tick(dc, ctr, r, d, 9, GridPenLight);
            Tick(dc, ctr, r, c.RollLimitDeg, 12, LimitPen); Tick(dc, ctr, r, -c.RollLimitDeg, 12, LimitPen);
            Tick(dc, ctr, r, c.RollHardDeg, 14, HardPen); Tick(dc, ctr, r, -c.RollHardDeg, 14, HardPen);
            // roll pointer (rotates with the horizon)
            dc.PushTransform(new RotateTransform(rot, ctr.X, ctr.Y));
            dc.DrawGeometry(Accent, null, Triangle(new Point(ctr.X, ctr.Y - r + 2), 7, 10));
            dc.Pop();

            // fixed seat symbol
            var sym = new Pen(Accent, 3);
            dc.DrawLine(sym, new Point(ctr.X - 46, ctr.Y), new Point(ctr.X - 14, ctr.Y));
            dc.DrawLine(sym, new Point(ctr.X + 14, ctr.Y), new Point(ctr.X + 46, ctr.Y));
            dc.DrawEllipse(Accent, null, ctr, 3.5, 3.5);

            string val = live
                ? string.Format(CultureInfo.InvariantCulture, "Pitch {0,6:F2}°   Roll {1,6:F2}°", st.PitchDeg, st.RollDeg)
                : "no sensor data";
            TextCentered(dc, val, ctr.X, ctr.Y + r + 16, 13, live ? Ink : Quiet, true);
            Text(dc, "Attitude", 24, 52, 12, Quiet, true);
        }

        private static readonly Pen GridPenLight = FrozenPen(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF), 1.2);

        private static void Tick(DrawingContext dc, Point ctr, double r, double deg, double len, Pen pen)
        {
            // 0 deg at the top, positive roll (right side down) shown counter-clockwise like the horizon
            double a = (-deg - 90) * Math.PI / 180.0;
            var p1 = new Point(ctr.X + Math.Cos(a) * r, ctr.Y + Math.Sin(a) * r);
            var p2 = new Point(ctr.X + Math.Cos(a) * (r - len), ctr.Y + Math.Sin(a) * (r - len));
            dc.DrawLine(pen, p1, p2);
        }

        // ---- g-g plot -----------------------------------------------------------------------------
        private void DrawGg(DrawingContext dc, Point ctr, double r, ControllerSettings c, bool live)
        {
            double maxG = Math.Max(1.0, Math.Ceiling(c.CueLimitG * 1.25 * 4) / 4);
            for (double ring = 0.25; ring <= maxG + 1e-9; ring += 0.25)
            {
                double rr = ring / maxG * r;
                dc.DrawEllipse(null, GridPen, ctr, rr, rr);
                if (Math.Abs(ring % 0.5) < 1e-9)
                    Text(dc, ring.ToString("0.0", CultureInfo.InvariantCulture) + " g", ctr.X + rr * 0.707 + 3, ctr.Y + rr * 0.707 + 1, 10, Quiet, false);
            }
            dc.DrawLine(GridPen, new Point(ctr.X - r, ctr.Y), new Point(ctr.X + r, ctr.Y));
            dc.DrawLine(GridPen, new Point(ctr.X, ctr.Y - r), new Point(ctr.X, ctr.Y + r));
            double lim = c.CueLimitG / maxG * r;
            dc.DrawEllipse(null, LimitPen, ctr, lim, lim);
            Text(dc, "accel", ctr.X + 5, ctr.Y - r + 2, 10, Quiet, false);
            Text(dc, "brake", ctr.X + 5, ctr.Y + r - 15, 10, Quiet, false);
            Text(dc, "L", ctr.X - r - 12, ctr.Y - 7, 10, Quiet, false);
            Text(dc, "R", ctr.X + r + 4, ctr.Y - 7, 10, Quiet, false);

            if (live && _trail.Count > 1)
            {
                Point? prev = null;
                for (int i = 0; i < _trail.Count; i++)
                {
                    _trail.Get(i, out _, out double surge, out double sway);
                    VisualMath.GgPoint(surge, sway, maxG, r, out double dx, out double dy, out _);
                    var pt = new Point(ctr.X + dx, ctr.Y + dy);
                    if (prev.HasValue)
                    {
                        byte alpha = (byte)(40 + 180 * i / _trail.Count);
                        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0xF2, 0xB1, 0x34)), 2), prev.Value, pt);
                    }
                    prev = pt;
                }
                var st = _p.State;
                VisualMath.GgPoint(st.CueSurgeG, st.CueSwayG, maxG, r, out double x, out double y, out bool clipped);
                dc.DrawEllipse(clipped ? Bad : Accent, new Pen(Bg, 2), new Point(ctr.X + x, ctr.Y + y), 7, 7);
                double mag = Math.Sqrt(st.CueSurgeG * st.CueSurgeG + st.CueSwayG * st.CueSwayG);
                TextCentered(dc, string.Format(CultureInfo.InvariantCulture, "Cue {0:F2} g  (surge {1:+0.00;-0.00}  sway {2:+0.00;-0.00})", mag, st.CueSurgeG, st.CueSwayG),
                             ctr.X, ctr.Y + r + 16, 12, Ink, true);
            }
            Text(dc, "Horizontal cue felt in the seat", 312, 52, 12, Quiet, true);
        }

        // ---- meters -------------------------------------------------------------------------------
        private void DrawMeters(DrawingContext dc, PlatformState st, ControllerSettings c, bool live)
        {
            Text(dc, "Rates, heave, vibration, impacts", 600, 52, 12, Quiet, true);
            double x = 600, w = 160, y = 92, h = 12, step = 34;
            CenterBar(dc, "Pitch rate", st.PitchRateDps, c.RateLimitDps * 1.5, c.RateLimitDps, "°/s", x, y, w, h, live, PitchC); y += step;
            CenterBar(dc, "Roll rate", st.RollRateDps, c.RateLimitDps * 1.5, c.RateLimitDps, "°/s", x, y, w, h, live, RollC); y += step;
            CenterBar(dc, "Heave cue", st.CueHeaveG, c.HeaveLimitG * 1.5, c.HeaveLimitG, "g", x, y, w, h, live, Ink); y += step;
            FillBar(dc, "Vibration RMS", st.VibRmsG, c.VibLimitG * 1.5, c.VibLimitG, "g", x, y, w, h, live); y += step;
            FillBar(dc, string.Format(CultureInfo.InvariantCulture, "Peak |a|-1g   impacts {0}", _p.Controller.ImpactCount),
                    st.PeakDevG, c.ImpactG * 1.5, c.ImpactG, "g", x, y, w, h, live);
        }

        private void CenterBar(DrawingContext dc, string label, double v, double range, double limit, string unit,
                               double x, double y, double w, double h, bool live, Brush fill)
        {
            Text(dc, label, x, y - 15, 11, Quiet, false);
            var track = new Rect(x, y, w, h);
            dc.DrawRoundedRectangle(Track, null, track, 3, 3);
            double mid = x + w / 2;
            if (live)
            {
                double f = VisualMath.Frac(Math.Abs(v), 0, range) * (w / 2);
                var bar = v >= 0 ? new Rect(mid, y, f, h) : new Rect(mid - f, y, f, h);
                dc.DrawRectangle(Math.Abs(v) > limit ? Warn : fill, null, bar);
            }
            double lx = VisualMath.Frac(limit, 0, range) * (w / 2);
            dc.DrawLine(LimitPen, new Point(mid + lx, y - 3), new Point(mid + lx, y + h + 3));
            dc.DrawLine(LimitPen, new Point(mid - lx, y - 3), new Point(mid - lx, y + h + 3));
            dc.DrawLine(GridPenLight, new Point(mid, y - 2), new Point(mid, y + h + 2));
            Text(dc, live ? v.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) + " " + unit : "--", x + w + 8, y - 2, 12, Ink, true);
        }

        private void FillBar(DrawingContext dc, string label, double v, double range, double limit, string unit,
                             double x, double y, double w, double h, bool live)
        {
            Text(dc, label, x, y - 15, 11, Quiet, false);
            dc.DrawRoundedRectangle(Track, null, new Rect(x, y, w, h), 3, 3);
            if (live)
            {
                double f = VisualMath.Frac(v, 0, range) * w;
                dc.DrawRectangle(v > limit ? Bad : v > limit * 0.85 ? Warn : Good, null, new Rect(x, y, f, h));
            }
            double lx = x + VisualMath.Frac(limit, 0, range) * w;
            dc.DrawLine(LimitPen, new Point(lx, y - 3), new Point(lx, y + h + 3));
            Text(dc, live ? v.ToString("0.00", CultureInfo.InvariantCulture) + " " + unit : "--", x + w + 8, y - 2, 12, Ink, true);
        }

        // ---- gains --------------------------------------------------------------------------------
        private void DrawGains(DrawingContext dc, GainSnapshot g, ControllerSettings c)
        {
            double x0 = 600, yTop = 266, h = 58, bw = 34, gap = 36;
            Text(dc, "Gains", x0, yTop - 18, 11, Quiet, false);
            var names = new[] { "Glob", "Pitch", "Roll", "Heave" };
            var vals = new[] { g.Global, g.Pitch, g.Roll, g.Heave };
            for (int i = 0; i < 4; i++)
            {
                double x = x0 + i * (bw + gap);
                dc.DrawRoundedRectangle(Track, null, new Rect(x, yTop, bw, h), 3, 3);
                double fh = VisualMath.Frac(vals[i], 0, 1) * h;
                Brush b = vals[i] > 0.97 ? Good : vals[i] > c.MinGain + 0.05 ? Warn : Bad;
                dc.DrawRectangle(b, null, new Rect(x, yTop + h - fh, bw, fh));
                double ym = yTop + h - VisualMath.Frac(c.MinGain, 0, 1) * h;
                dc.DrawLine(HardPen, new Point(x - 3, ym), new Point(x + bw + 3, ym));
                bool onBar = fh > h / 2 + 8;
                TextCentered(dc, vals[i].ToString("0.00", CultureInfo.InvariantCulture), x + bw / 2, yTop + h / 2 - 9, 12, onBar ? Bg : Ink, true);
                TextCentered(dc, names[i], x + bw / 2, yTop + h + 2, 10, Quiet, false);
            }
        }

        // ---- trend --------------------------------------------------------------------------------
        private void DrawTrend(DrawingContext dc, Rect area, ControllerSettings c)
        {
            Text(dc, "Last 10 s", 24, 362, 12, Quiet, true);
            Text(dc, "pitch", 110, 362, 12, PitchC, true);
            Text(dc, "roll", 152, 362, 12, RollC, true);
            double range = VisualMath.NiceRange(Math.Max(c.PitchHardDeg, c.RollHardDeg) * 1.15);
            Func<double, double> Y = v => area.Top + area.Height / 2 - v / range * (area.Height / 2);
            dc.DrawLine(GridPen, new Point(area.Left, Y(0)), new Point(area.Right, Y(0)));
            foreach (var sgn in new[] { 1, -1 })
            {
                double lim = Math.Max(c.PitchLimitDeg, c.RollLimitDeg);
                double hard = Math.Max(c.PitchHardDeg, c.RollHardDeg);
                dc.DrawLine(LimitPen, new Point(area.Left, Y(sgn * lim)), new Point(area.Right, Y(sgn * lim)));
                dc.DrawLine(HardPen, new Point(area.Left, Y(sgn * hard)), new Point(area.Right, Y(sgn * hard)));
                Text(dc, (sgn * range).ToString("+0;-0", CultureInfo.InvariantCulture) + "°", area.Left - 40, Y(sgn * range) - 7, 10, Quiet, false);
            }
            Text(dc, "0°", area.Left - 40, Y(0) - 7, 10, Quiet, false);
            if (_trend.Count < 2) return;

            _trend.Get(_trend.Count - 1, out double tNow, out _, out _);
            Func<double, double> X = t => area.Right - (tNow - t) / TrendSeconds * area.Width;
            var pitchPen = new Pen(PitchC, 1.8); var rollPen = new Pen(RollC, 1.8);
            var pg = new StreamGeometry(); var rg = new StreamGeometry();
            using (var pc = pg.Open())
            using (var rc = rg.Open())
            {
                bool started = false;
                for (int i = 0; i < _trend.Count; i++)
                {
                    _trend.Get(i, out double t, out double pitch, out double roll);
                    if (tNow - t > TrendSeconds) continue;
                    var pp = new Point(X(t), Y(Clamp(pitch, range)));
                    var rp = new Point(X(t), Y(Clamp(roll, range)));
                    if (!started) { pc.BeginFigure(pp, false, false); rc.BeginFigure(rp, false, false); started = true; }
                    else { pc.LineTo(pp, true, false); rc.LineTo(rp, true, false); }
                }
            }
            pg.Freeze(); rg.Freeze();
            dc.DrawGeometry(null, pitchPen, pg);
            dc.DrawGeometry(null, rollPen, rg);
        }

        private static double Clamp(double v, double r) => v > r ? r : (v < -r ? -r : v);

        // ---- text & shapes ------------------------------------------------------------------------
        private FormattedText Fmt(string s, double size, Brush b, bool bold) =>
            new FormattedText(s ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? FaceBold : Face, size, b, _pixelsPerDip);

        private void Text(DrawingContext dc, string s, double x, double y, double size, Brush b, bool bold) =>
            dc.DrawText(Fmt(s, size, b, bold), new Point(x, y));

        private void TextCentered(DrawingContext dc, string s, double cx, double y, double size, Brush b, bool bold)
        {
            var ft = Fmt(s, size, b, bold);
            dc.DrawText(ft, new Point(cx - ft.Width / 2, y));
        }

        private double Badge(DrawingContext dc, string s, double x, double y, Brush fill)
        {
            var ft = Fmt(s, 11, fill == Track ? Ink : Bg, true);
            var rect = new Rect(x, y + 2, ft.Width + 14, 20);
            dc.DrawRoundedRectangle(fill, null, rect, 10, 10);
            dc.DrawText(ft, new Point(x + 7, y + 4));
            return rect.Right;
        }

        private static Geometry Triangle(Point tip, double halfW, double h)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(tip, true, true);
                c.LineTo(new Point(tip.X - halfW, tip.Y + h), true, false);
                c.LineTo(new Point(tip.X + halfW, tip.Y + h), true, false);
            }
            g.Freeze();
            return g;
        }

        private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        private static Pen FrozenPen(Color c, double w) { var p = new Pen(new SolidColorBrush(c), w); p.Freeze(); return p; }
        private static Pen FrozenDashed(Color c, double w)
        {
            var p = new Pen(new SolidColorBrush(c), w) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
            p.Freeze();
            return p;
        }
    }
}
