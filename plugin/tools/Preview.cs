using System; using System.Reflection; using System.Windows; using System.Windows.Media;
using SimSpares.AdaptiveMotion.Core; using SimSpares.AdaptiveMotion.UI;
namespace SimSpares.AdaptiveMotion {
  static class Preview {
    static void Set(object o, string f, object v) => o.GetType().GetField(f, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, v);
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
      return 0;
    }
  }
}
