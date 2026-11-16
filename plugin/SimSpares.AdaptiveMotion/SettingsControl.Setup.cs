using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SimSpares.AdaptiveMotion.Core;
using SimSpares.AdaptiveMotion.UI;

namespace SimSpares.AdaptiveMotion
{
    /// <summary>Setup (guided) and Verify tabs. The full settings page stays available as "Advanced".</summary>
    public partial class SettingsControl
    {
        private readonly List<Action> _setupRefresh = new List<Action>();
        private TabControl _tabs;
        private TabItem _verifyTab;
        private TextBlock _vBlockers, _vFindings;
        private Button _vRun, _vStop;

        private static readonly Brush OkBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly Brush TodoBrush = new SolidColorBrush(Color.FromRgb(0xE3, 0xB3, 0x41));
        private static readonly Brush BadBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));

        // =====================================================================================
        // Setup tab
        // =====================================================================================
        private TabItem _setupTab;

        private void RebuildSetupTab()
        {
            _setupRefresh.Clear();
            if (_setupTab != null) _setupTab.Content = BuildSetupTab();
        }

        private FrameworkElement BuildSetupTab()
        {
            var s = _p.Settings;
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Work through the steps from top to bottom. Every step shows a green tick when it is done. " +
                       "Closed loop stays locked until the verification test (step 6) has passed for exactly these settings. " +
                       "All other options are on the Advanced tab.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left
            });

            // 1 ---------------------------------------------------------------------------------
            var port = new ComboBox { IsEditable = true, Width = 140, Text = s.ComPort };
            RefreshPorts(port);
            port.DropDownOpened += (o, e) => RefreshPorts(port);
            port.LostFocus += (o, e) => s.ComPort = port.Text.Trim();
            port.SelectionChanged += (o, e) => { if (port.SelectedItem != null) s.ComPort = port.SelectedItem.ToString(); };
            root.Children.Add(Step("1. Connect the IMU box",
                () => _p.SensorFresh ? Done(string.Format(CultureInfo.InvariantCulture, "Connected on {0}, {1:F0} frames/s. {2}", s.ComPort, _p.FrameRate, _p.DeviceInfo))
                                     : Todo("Plug in the box and choose its COM port (the data port, not the maintenance port)."),
                Row("COM port", port),
                Buttons(Btn("Connect", () => { _p.SaveSettings(); _p.ApplySettings(); }))));

            // 2 ---------------------------------------------------------------------------------
            root.Children.Add(Step("2. Mounting direction",
                () => s.MountAuto ? Done("Auto-aligned: works at any mounting angle.")
                                  : Done(string.Format("Manual: sensor {0} points forward, {1} points up. Side mount with the lid to the front is the default; use Auto-align for anything else.", s.MountForward, s.MountUp)),
                Row("Sensor axis pointing forward", EnumBox(s.MountForward, v => { s.MountForward = v; s.MountAuto = false; })),
                Row("Sensor axis pointing up", EnumBox(s.MountUp, v => { s.MountUp = v; s.MountAuto = false; })),
                Buttons(Btn("Save mounting", () => { _p.SaveSettings(); _p.ApplySettings(); _p.Note("Mounting saved"); }),
                        Btn("Auto-align 1: rest (rig at centre)", () => _p.StartAlignCapture(true)),
                        Btn("Auto-align 2: nose-up held (≥ 2°)", () => _p.StartAlignCapture(false)))));

            // 3 ---------------------------------------------------------------------------------
            var rig = s.Rig;
            var layout = EnumBox(s.Actuators, v => s.Actuators = v);
            root.Children.Add(Step("3. Rig dimensions and limits (from SimHub's motion settings)",
                () =>
                {
                    string pr = rig.Problem(s.Actuators, s.Packet);
                    return pr == null
                        ? Done(string.Format(CultureInfo.InvariantCulture, "{0} actuators, {1} × {2} mm, stroke {3} mm, limits pitch ±{4}°, roll ±{5}° ({6}).",
                            rig.Count, rig.WheelbaseMm, rig.TrackMm, rig.StrokeMm, rig.MaxPitchDeg, rig.MaxRollDeg, rig.Source == "SimHub" ? "imported from SimHub" : "entered by hand"))
                        : Todo("Missing: " + pr + ". Press Import from SimHub.");
                },
                Buttons(Btn("Import from SimHub", () => { _p.ImportFromSimHub(false); RebuildSetupTab(); })),
                Hint("Reads rig type, length, width, stroke, pitch/roll limits and the UDP output address from SimHub. SimHub saves its motion settings when it closes: after changing them there, restart SimHub before importing. You can still adjust the values below by hand."),
                Row("Actuator layout", layout),
                Row("Left-right distance between actuators (mm)", Num(rig.TrackMm, v => rig.TrackMm = v)),
                Row("Front-rear distance between actuators (mm)", Num(rig.WheelbaseMm, v => rig.WheelbaseMm = v)),
                Row("2 actuators only: distance behind the pivot (mm)", Num(rig.PivotOffsetMm, v => rig.PivotOffsetMm = v)),
                Row("Actuator stroke (mm, full output range)", Num(rig.StrokeMm, v => rig.StrokeMm = v)),
                Row("Higher output value = actuator up", Check(rig.HigherValueIsUp, v => rig.HigherValueIsUp = v)),
                Row("Pitch limit in SimHub (± deg)", Num(rig.MaxPitchDeg, v => rig.MaxPitchDeg = v)),
                Row("Roll limit in SimHub (± deg)", Num(rig.MaxRollDeg, v => rig.MaxRollDeg = v)),
                Buttons(Btn("Save rig", () => { rig.FillFromLayout(s.Actuators); rig.Source = "manual"; _p.SaveSettings(); _p.ApplySettings(); _p.Note("Rig saved: " + (rig.Problem(s.Actuators, s.Packet) ?? "complete")); }),
                        Btn("Use these limits for the plugin too", () => { rig.FillFromLayout(s.Actuators); _p.ApplyRigLimitsToController(); }))));

            // 4 ---------------------------------------------------------------------------------
            root.Children.Add(Step("4. Route SimHub's motion output through the plugin",
                () =>
                {
                    if (s.Mode != ApplyMode.UdpProxy) return Todo("Set the mode to UDP proxy (motion passes through unchanged until closed loop is enabled).");
                    if (!_p.Proxy.Running) return Todo("Enter the controller host and port SimHub used before, then Save.");
                    var xf = _p.Proxy.Transformer;
                    bool fresh = !double.IsNaN(_p.Proxy.LastPacketSec) && IO.ImuSerialLink.NowSec - _p.Proxy.LastPacketSec < 2;
                    if (xf != null && xf.MotionPackets == 0 && xf.OtherPackets > 0 && fresh)
                        return Bad("SimHub's packets arrive, but they do not match the packet format. Expected: " + s.Packet.Describe() + ". Received: "
                                   + (xf.LastOtherSample ?? "?") + ". Press Detect format (or Import from SimHub in step 3). Motion still passes through unchanged.");
                    if (xf == null || xf.MotionPackets == 0 || !fresh)
                        return Todo(string.Format(CultureInfo.InvariantCulture, "In SimHub's Generic UDP output, send to 127.0.0.1 port {0} and switch motion on. No packets yet.", s.ProxyListenPort));
                    return Done(string.Format(CultureInfo.InvariantCulture, "Receiving SimHub motion ({0}) and forwarding to {1}:{2}. {3}",
                        s.Packet.Describe(), s.ProxyTargetHost, s.ProxyTargetPort, _p.ProxyActive ? "Closed loop ACTIVE." : "Passing through unchanged."));
                },
                Row("Controller host", Txt(s.ProxyTargetHost, v => s.ProxyTargetHost = v)),
                Row("Controller port (SimHub's old target port)", IntBox(s.ProxyTargetPort, v => s.ProxyTargetPort = v)),
                Row("Plugin listens on 127.0.0.1 port", IntBox(s.ProxyListenPort, v => s.ProxyListenPort = v)),
                Buttons(Btn("Use UDP proxy and save", () => { s.Mode = ApplyMode.UdpProxy; _p.SaveSettings(); _p.ApplySettings(); _p.Note("UDP proxy on (pass-through)"); }),
                        Btn("Detect format", () => { string m; _p.DetectPacketFormat(out m); if (!m.StartsWith("Packet format detected")) _p.Note("Detect format: " + m); })),
                Hint("Packet format = how SimHub writes the actuator positions into each UDP packet (its \"update command\"). " +
                     "Text like NULLCAT,<Axis1>,<Axis2>,… sends the numbers as readable text; binary like <Axis1a><Axis1b>… sends two bytes per actuator. " +
                     "The plugin has to read exactly that format to know where the rig is commanded to; Import from SimHub (step 3) or Detect format sets it for you. " +
                     "Current: " + s.Packet.Describe() + ".")));

            // 5 ---------------------------------------------------------------------------------
            root.Children.Add(Step("5. Neutral and level (rig parked at its centre, motion on, game closed)",
                () =>
                {
                    if (s.Neutral == null && !s.LevelCaptured) return Todo("Capture the actuator neutral, then the level.");
                    if (s.Neutral == null) return Todo("Capture the actuator neutral.");
                    if (!s.LevelCaptured) return Todo("Capture the level.");
                    return Done(string.Format(CultureInfo.InvariantCulture, "Neutral {0}; level pitch {1:F2}°, roll {2:F2}°.",
                        string.Join(" / ", s.Neutral.Select(v => v.ToString("F0", CultureInfo.InvariantCulture))), s.LevelPitchDeg, s.LevelRollDeg));
                },
                Buttons(Btn("Capture neutral", () => _p.StartNeutralCapture()),
                        Btn("Capture level", () => _p.StartLevelCapture()))));

            // 6 ---------------------------------------------------------------------------------
            root.Children.Add(Step("6. Verify: commanded vs measured",
                () =>
                {
                    var rep = s.LastVerification;
                    if (_p.Verifying) return Todo("Test running…");
                    if (_p.VerificationValid) return Done(string.Format(CultureInfo.InvariantCulture, "Passed on {0:yyyy-MM-dd HH:mm} (pitch ×{1:F3}, roll ×{2:F3}).", rep.WhenUtc.ToLocalTime(), rep.PitchScale, rep.RollScale));
                    if (rep != null && rep.Passed) return Todo("Settings changed since the last pass: run the test again.");
                    if (rep != null) return Bad("Last test: " + rep.Overall + ". " + string.Join(" ", rep.Findings));
                    return Todo("Not run yet.");
                },
                Buttons(Btn("Open the Verify tab", () => { if (_tabs != null && _verifyTab != null) _tabs.SelectedItem = _verifyTab; }))));

            // 7 ---------------------------------------------------------------------------------
            root.Children.Add(Step("7. Closed loop",
                () =>
                {
                    if (!_p.VerificationValid) return Todo("Locked until step 6 has passed. Motion passes through unchanged.");
                    if (!s.AdaptiveEnabled) return Todo("Verified. Switch closed loop on when you are ready.");
                    return _p.ProxyActive || s.Mode == ApplyMode.SimHubGainEvents ? Done("Closed loop is ON.") : Todo("Closed loop is enabled but not active: check steps 4 and 5.");
                },
                Row("Closed loop enabled", Check(s.AdaptiveEnabled, v => { s.AdaptiveEnabled = v; _p.SaveSettings(); }))));

            s.SetupWizardSeen = true;
            return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private struct StepState { public Brush Brush; public string Mark, Text; }
        private static StepState Done(string t) => new StepState { Brush = OkBrush, Mark = "✓", Text = t };
        private static StepState Todo(string t) => new StepState { Brush = TodoBrush, Mark = "●", Text = t };
        private static StepState Bad(string t) => new StepState { Brush = BadBrush, Mark = "✗", Text = t };

        private FrameworkElement Step(string title, Func<StepState> state, params FrameworkElement[] content)
        {
            var box = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var mark = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Width = 22, VerticalAlignment = VerticalAlignment.Center };
            head.Children.Add(mark);
            head.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            box.Children.Add(head);
            var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 2, 0, 4), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
            box.Children.Add(status);
            var body = new StackPanel { Margin = new Thickness(22, 0, 0, 0) };
            foreach (var c in content) body.Children.Add(c);
            box.Children.Add(body);
            _setupRefresh.Add(() =>
            {
                StepState st;
                try { st = state(); } catch (Exception e) { st = Bad(e.Message); }
                mark.Text = st.Mark; mark.Foreground = st.Brush;
                if (status.Text != st.Text) status.Text = st.Text;
            });
            return new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)), BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 8, 0, 10), Child = box
            };
        }

        private static TextBlock Hint(string t) => new TextBlock { Text = t, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Left };

        // =====================================================================================
        // Verify tab
        // =====================================================================================
        private FrameworkElement BuildVerifyTab()
        {
            var s = _p.Settings;
            var o = s.Verify;
            var dock = new DockPanel { Margin = new Thickness(8) };

            var top = new StackPanel();
            _vRun = Btn("Run test", () =>
            {
                if (MessageBox.Show("The plugin will now move the rig through pitch and roll poses up to " +
                        string.Format(CultureInfo.InvariantCulture, "±{0:F1}° pitch and ±{1:F1}° roll", s.Rig.MaxPitchDeg * Math.Min(1, o.MaxFractionPct / 100), s.Rig.MaxRollDeg * Math.Min(1, o.MaxFractionPct / 100)) +
                        ".\n\nStay at the rig with the e-stop in reach and keep everyone clear. Stop returns the rig to centre.\n\nStart the test?",
                        "Adaptive Motion: verification", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
                if (!_p.StartVerification(out var why)) _p.Note("Verification cannot start: " + why);
            });
            _vStop = Btn("Stop (return to centre)", () => _p.StopVerification());
            top.Children.Add(Buttons(_vRun, _vStop,
                Btn("Open report folder", () =>
                {
                    string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Adaptive Motion IMU", "verification");
                    try { System.IO.Directory.CreateDirectory(dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
                    catch (Exception e) { _p.Note("Cannot open folder: " + e.Message); }
                })));
            var opts = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            opts.Children.Add(Inline("Largest pose (% of limit)", Num(o.MaxFractionPct, v => { o.MaxFractionPct = Math.Max(10, Math.Min(100, v)); _p.SaveSettings(); })));
            opts.Children.Add(Inline("Speed (deg/s)", Num(o.RampDps, v => { o.RampDps = Math.Max(0.5, Math.Min(15, v)); _p.SaveSettings(); })));
            opts.Children.Add(Inline("Tolerance (%)", Num(o.TolPct, v => { o.TolPct = Math.Max(1, Math.Min(20, v)); _p.SaveSettings(); })));
            opts.Children.Add(Inline("Min tolerance (deg)", Num(o.TolMinDeg, v => { o.TolMinDeg = Math.Max(0.1, Math.Min(2, v)); _p.SaveSettings(); })));
            top.Children.Add(opts);
            _vBlockers = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = TodoBrush, Margin = new Thickness(0, 4, 0, 4) };
            top.Children.Add(_vBlockers);
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);

            _vFindings = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            DockPanel.SetDock(_vFindings, Dock.Bottom);
            dock.Children.Add(_vFindings);
            dock.Children.Add(new VerifyChart(_p));
            return dock;
        }

        private static FrameworkElement Inline(string label, FrameworkElement editor)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            p.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            editor.Width = 60;
            p.Children.Add(editor);
            return p;
        }

        private void RefreshSetupAndVerify()
        {
            foreach (var a in _setupRefresh) a();
            if (_vBlockers == null) return;
            bool running = _p.Verifying;
            _vRun.IsEnabled = !running;
            _vStop.IsEnabled = running;
            var blockers = running ? new List<string>() : _p.VerifyBlockers();
            string bt = blockers.Count == 0 ? "" : "Before the test can run: " + string.Join("; ", blockers) + ".";
            if (_vBlockers.Text != bt) _vBlockers.Text = bt;
            var run = _p.RunForDisplay;
            var findings = run != null && run.Done ? run.Findings() : !running && _p.Settings.LastVerification != null ? _p.Settings.LastVerification.Findings : null;
            string ft = findings == null ? (running ? "Test running. Stop returns the rig to centre." : "") : string.Join("\n", findings.Select(f => "• " + f));
            if (_vFindings.Text != ft) _vFindings.Text = ft;
        }
    }
}
