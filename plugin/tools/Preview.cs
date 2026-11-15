using System; using System.Reflection; using System.Windows; using System.Windows.Media;
using SimSpares.AdaptiveMotion.Core; using SimSpares.AdaptiveMotion.UI;
namespace SimSpares.AdaptiveMotion {
  static class Preview {
    static void Set(object o, string f, object v) => o.GetType().GetField(f, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, v);
    // Simulated verification run on a 660 mm 4-corner rig; rollScale 0.5 = the ball-screw lead mistake
    static void VerifyPreview(AdaptiveMotionPlugin p, string file, double scale) {
      var r = p.Settings.Rig; r.TrackMm = 660; r.WheelbaseMm = 520; r.StrokeMm = 225; r.MaxPitchDeg = 9; r.MaxRollDeg = 15;
      p.Settings.Actuators = ActuatorLayout.FourCorner; r.FillFromLayout(ActuatorLayout.FourCorner);
      var run = new VerificationRun(9, 15, new VerifyOptions { RampDps = 20, SettleSec = 0.3, MeasureSec = 0.2 });
      var rnd = new Random(3); double mp = 0, mr = 0, t = 0;
      for (int i = 0; i < 40000 && !run.Done; i++) {
        t += 0.01;
        double tp = run.CommandPitchDeg * scale * 1.004 + 0.02 * run.CommandRollDeg + 0.03, tr = run.CommandRollDeg * scale * 0.985 - 0.02;
        mp += (tp - mp) * 0.2 + (rnd.NextDouble() - 0.5) * 0.04; mr += (tr - mr) * 0.2 + (rnd.NextDouble() - 0.5) * 0.04;
        run.Update(t, mp, mr);
      }
      Set(p, "_lastRun", run);
      p.Settings.LastVerification = VerificationReport.From(run, p.Fingerprint, "1.3.0", "INFO fw=1.3.1");
      Set(p, "_state", new PlatformState { PitchDeg = 0.04, RollDeg = -0.03 });
      var chart = new VerifyChart(p) { ActualWidth = 900, ActualHeight = 560 };
      var dc = new DrawingContext();
      typeof(VerifyChart).GetMethod("OnRender", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(chart, new object[] { dc });
      System.IO.File.WriteAllText(file, "<svg xmlns='http://www.w3.org/2000/svg' width='900' height='560' viewBox='0 0 900 560'>\n" + dc.Sb + "</svg>");
      Console.WriteLine("verify preview " + file + ": " + run.Overall + " / " + string.Join(" | ", run.Findings()));
    }
    static int Main(string[] a) {
      var p = new AdaptiveMotionPlugin(); p.Init(new SimHub.Plugins.PluginManager());
      p.Settings.LevelCaptured = true;
      double t = 0; MotionVisualizer.Now = () => t;
      var viz = new MotionVisualizer(p) { ActualWidth = 900, ActualHeight = 540 };
      var sample = typeof(MotionVisualizer).GetMethod("Sample", BindingFlags.NonPublic | BindingFlags.Instance);
      PlatformState st = default(PlatformState);
      for (int i = 0; i < 300; i++) {
        t = i / 30.0;
        st = new PlatformState {
          PitchDeg = 5.5 * Math.Sin(0.9 * t) - 1.5 * Math.Sin(2.3 * t), RollDeg = 9.5 * Math.Sin(0.55 * t + 0.4),
          PitchRateDps = 28 * Math.Cos(0.9 * t), RollRateDps = 31 * Math.Cos(0.55 * t),
          CueSurgeG = -0.55 * Math.Sin(0.9 * t), CueSwayG = 0.5 * Math.Sin(0.55 * t + 0.4), CueHeaveG = 0.18 * Math.Sin(3 * t),
          VibRmsG = 0.12 + 0.05 * Math.Sin(t), PeakDevG = 0.6 + 0.3 * Math.Abs(Math.Sin(1.7 * t)), TimeSec = t };
        p.Controller.Update(st);
        Set(p, "_state", st);
        sample.Invoke(viz, null);
      }
      Set(p, "_frameRate", 249.6); Set(p, "_lastFrameSec", IO.ImuSerialLink.NowSec);
      var dc = new DrawingContext();
      typeof(MotionVisualizer).GetMethod("OnRender", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(viz, new object[] { dc });
      System.IO.File.WriteAllText(a[0], "<svg xmlns='http://www.w3.org/2000/svg' width='900' height='540' viewBox='0 0 900 540'>\n" + dc.Sb + "</svg>");
      Console.WriteLine("ok status=" + p.Controller.Snapshot.Status + " roll gain=" + p.Controller.Snapshot.Roll.ToString("F2"));
      for (int k = 1; k + 1 < a.Length; k += 2) VerifyPreview(p, a[k], double.Parse(a[k + 1], System.Globalization.CultureInfo.InvariantCulture));
      return 0;
    }
  }
}
