using System;
using System.Windows.Controls;
using System.Windows.Media;
using GameReaderCommon;
using SimHub.Plugins;
using SimSpares.AdaptiveMotion.Core;
using SimSpares.AdaptiveMotion.IO;

namespace SimSpares.AdaptiveMotion
{
    [PluginDescription("Closed-loop motion: an MPU-6050 on the seat measures what the rig really does and adapts motion gains.")]
    [PluginAuthor("Haarie / Sim-Spares")]
    [PluginName("Adaptive Motion (IMU)")]
    public class AdaptiveMotionPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        public PluginManager PluginManager { get; set; }
        public AdaptiveMotionSettings Settings { get; private set; }

        public ImageSource PictureIcon => MenuIcon.Create();
        public string LeftMenuTitle => "Adaptive Motion";

        // --- components -----------------------------------------------------------------------
        internal readonly ImuSerialLink Imu = new ImuSerialLink();
        internal readonly UdpMotionProxy Proxy = new UdpMotionProxy();
        internal readonly PlatformEstimator Estimator = new PlatformEstimator();
        internal AdaptiveGainController Controller;
        internal readonly LagEstimator PitchTracking = new LagEstimator();
        internal readonly LagEstimator RollTracking = new LagEstimator();
        internal ParkMonitor Park;
        private volatile bool _gameRunning;

        // --- latest data (written on the IMU thread, read on SimHub/UI threads) -------------------
        private readonly object _stateLock = new object();
        private PlatformState _state;
        private double _lastFrameSec = double.NaN;
        private double _frameRate;
        private volatile string _deviceInfo = "";
        private volatile string _lastMessage = "";
        private volatile float _cmdPitch, _cmdRoll;

        // level capture
        private double _levelUntil = double.NaN, _levelSumP, _levelSumR; private int _levelN;
        // neutral capture
        private double _neutralUntil = double.NaN;
        // auto-align capture
        internal readonly MountAligner Aligner = new MountAligner();
        private double _alignUntil = double.NaN;
        private bool _alignIsRest;
        // SimHub gain-event mode
        private int _gainStepsDown; private double _lastStepSec;

        public PlatformState State { get { lock (_stateLock) return _state; } }
        public double FrameRate => _frameRate;
        /// <summary>True while IMU frames keep arriving (last one less than 0.5 s ago).</summary>
        public bool SensorFresh { get { lock (_stateLock) return !double.IsNaN(_lastFrameSec) && ImuSerialLink.NowSec - _lastFrameSec < 0.5; } }
        public string DeviceInfo => _deviceInfo;
        public string LastMessage => _lastMessage;

        public void Init(PluginManager pluginManager)
        {
            Settings = this.ReadCommonSettings<AdaptiveMotionSettings>("GeneralSettings", () => new AdaptiveMotionSettings());
            Controller = new AdaptiveGainController(Settings.Controller);
            Park = new ParkMonitor(Settings.Park);
            if (Settings.ParkReferenceLearned) Park.SetReference(Settings.ParkPitchDeg, Settings.ParkRollDeg);
            Park.ReferenceLearned += (p, r) =>
            {
                Settings.ParkReferenceLearned = true; Settings.ParkPitchDeg = p; Settings.ParkRollDeg = r;
                SaveSettings();
                Note(string.Format("Parked pose learned: pitch {0:F2}°, roll {1:F2}°", p, r));
            };
            Park.Checked += (dev, shifted) =>
            {
                if (shifted) { Note(string.Format("MOUNT SHIFTED? Parked pose is {0:F2}° off the reference", dev)); this.TriggerEvent("MountShifted"); }
                this.TriggerEvent("ParkChecked");
            };

            Imu.Frame += OnImuFrame;
            Imu.Text += t =>
            {
                if (t.StartsWith("INFO"))
                {
                    _deviceInfo = t;
                    // keep the firmware's auto-calibration in line with the setting after every (re)connect
                    if (t.Contains("auto=") && t.Contains("auto=1") != Settings.AutoGyroCal) Imu.Send("AUTOCAL " + (Settings.AutoGyroCal ? "1" : "0"));
                }
                Note("Device: " + t);
            };
            Imu.Log += Note;
            Proxy.Log += Note;
            Proxy.CommandModes += (p, r) => { _cmdPitch = (float)p; _cmdRoll = (float)r; };
            Controller.TripRaised += msg => { Note("TRIP: " + msg); this.TriggerEvent("Trip"); };
            Controller.ImpactDetected += () => this.TriggerEvent("Impact");

            RegisterProperties();
            RegisterActionsAndEvents();
            ApplySettings();
            Note("Initialised");
        }

        /// <summary>(Re)start everything from Settings. Safe to call from the UI thread.</summary>
        public void ApplySettings()
        {
            try
            {
                if (Settings.MountAuto && Settings.MountAutoForward != null && Settings.MountAutoUp != null)
                    Estimator.SetMountingVectors(Settings.MountAutoForward, Settings.MountAutoUp);
                else
                    Estimator.SetMounting(Settings.MountForward, Settings.MountUp);
            }
            catch (Exception e) { Note("Mounting: " + e.Message); }
            if (Settings.LevelCaptured) Estimator.SetLevelReference(Settings.LevelPitchDeg, Settings.LevelRollDeg);
            Controller.UpdateSettings(Settings.Controller);
            Park.UpdateSettings(Settings.Park);
            if (Imu.Connected) Imu.Send("AUTOCAL " + (Settings.AutoGyroCal ? "1" : "0"));

            if (Imu.PortName != Settings.ComPort || !Imu.Connected) Imu.Start(Settings.ComPort);

            Proxy.Stop();
            if (Settings.Mode == ApplyMode.UdpProxy && Settings.ProxyTargetPort > 0)
            {
                try
                {
                    var xf = new MotionPacketTransformer(Settings.Packet, Settings.Actuators, Settings.Neutral);
                    Proxy.Start(Settings.ProxyListenPort, Settings.ProxyTargetHost, Settings.ProxyTargetPort, xf,
                                () => Controller.Snapshot, () => ProxyActive);
                }
                catch (Exception e) { Note("Proxy: " + e.Message); }
            }
        }

        public void SaveSettings() => this.SaveCommonSettings("GeneralSettings", Settings);

        /// <summary>Gains are applied to UDP packets only when every precondition holds.</summary>
        public bool ProxyActive => Settings.Mode == ApplyMode.UdpProxy && Settings.AdaptiveEnabled
                                   && Settings.LayoutVerified && Settings.LevelCaptured;

        // ======================================================================================
        // IMU thread
        // ======================================================================================
        private void OnImuFrame(ImuFrame f)
        {
            double now = ImuSerialLink.NowSec;
            var st = Estimator.Update(f, now);

            if (!double.IsNaN(_alignUntil))
            {
                Aligner.Add(f.Ax, f.Ay, f.Az);
                if (now >= _alignUntil) FinishAlignCapture();
            }

            if (!double.IsNaN(_levelUntil))
            {
                _levelSumP += Estimator.RawPitchDeg; _levelSumR += Estimator.RawRollDeg; _levelN++;
                if (now >= _levelUntil) FinishLevelCapture();
            }

            if (Settings.LevelCaptured)
            {
                Controller.Update(st);
                Park.Update(st, _gameRunning);
            }
            PitchTracking.Push(_cmdPitch, st.PitchDeg);
            RollTracking.Push(_cmdRoll, st.RollDeg);

            lock (_stateLock)
            {
                _state = st;
                if (!double.IsNaN(_lastFrameSec))
                {
                    double inst = 1.0 / Math.Max(1e-4, now - _lastFrameSec);
                    _frameRate += (inst - _frameRate) * 0.02;
                }
                _lastFrameSec = now;
            }
        }

        private void FinishLevelCapture()
        {
            if (_levelN > 50)
            {
                Settings.LevelPitchDeg = _levelSumP / _levelN;
                Settings.LevelRollDeg = _levelSumR / _levelN;
                Settings.LevelCaptured = true;
                Estimator.SetLevelReference(Settings.LevelPitchDeg, Settings.LevelRollDeg);
                // angles are now measured against a new level: the stored parked pose is meaningless
                Settings.ParkReferenceLearned = false;
                Park.ClearReference();
                SaveSettings();
                Note(string.Format("Level captured: pitch {0:F2}°, roll {1:F2}°", Settings.LevelPitchDeg, Settings.LevelRollDeg));
            }
            else Note("Level capture failed: no IMU data");
            _levelUntil = double.NaN;
        }

        // ======================================================================================
        // SimHub thread (~60 Hz)
        // ======================================================================================
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            double now = ImuSerialLink.NowSec;
            _gameRunning = data.GameRunning;
            Controller.Tick(now);

            if (!double.IsNaN(_neutralUntil) && now >= _neutralUntil)
            {
                _neutralUntil = double.NaN;
                var xf = Proxy.Transformer;
                if (xf != null && xf.EndNeutralCapture())
                {
                    Settings.Neutral = xf.Neutral;
                    SaveSettings();
                    Note("Neutral captured: " + string.Join(", ", Array.ConvertAll(Settings.Neutral, v => v.ToString("F0"))));
                }
                else Note("Neutral capture failed: no motion packets received");
            }

            if (Settings.Mode == ApplyMode.SimHubGainEvents && Settings.AdaptiveEnabled && Settings.LevelCaptured)
                StepSimHubGain(now);
        }

        // Quantise the controller's global gain into steps of SimHub's own gain control and raise events
        private void StepSimHubGain(double now)
        {
            if (now - _lastStepSec < 0.3) return;
            double step = Settings.SimHubGainStepPct / 100.0;
            double want = Controller.Snapshot.Global;
            double current = 1.0 - _gainStepsDown * step;
            if (want < current - step * 0.6 && _gainStepsDown < Settings.SimHubMaxSteps)
            { _gainStepsDown++; _lastStepSec = now; this.TriggerEvent("GainDownRequest"); }
            else if (want > current + step * 0.6 && _gainStepsDown > 0)
            { _gainStepsDown--; _lastStepSec = now; this.TriggerEvent("GainUpRequest"); }
        }

        public void End(PluginManager pluginManager)
        {
            Proxy.Stop();
            Imu.Stop();
            SaveSettings();
        }

        public Control GetWPFSettingsControl(PluginManager pluginManager) => new SettingsControl(this);

        // ======================================================================================
        // Properties / actions / events
        // ======================================================================================
        private void RegisterProperties()
        {
            this.AttachDelegate("Sensor.Connected", () => Imu.Connected && !double.IsNaN(_lastFrameSec) && ImuSerialLink.NowSec - _lastFrameSec < 0.5);
            this.AttachDelegate("Sensor.FrameRate", () => Math.Round(_frameRate, 1));
            this.AttachDelegate("Sensor.CrcErrors", () => Imu.Parser.CrcErrors);
            this.AttachDelegate("Sensor.Info", () => _deviceInfo);
            this.AttachDelegate("Sensor.CloneDie", () => (State.Flags & ImuFlags.WhoAmIUnexpected) != 0);

            this.AttachDelegate("Seat.PitchDeg", () => Math.Round(State.PitchDeg, 2));
            this.AttachDelegate("Seat.RollDeg", () => Math.Round(State.RollDeg, 2));
            this.AttachDelegate("Seat.PitchRateDps", () => Math.Round(State.PitchRateDps, 1));
            this.AttachDelegate("Seat.RollRateDps", () => Math.Round(State.RollRateDps, 1));
            this.AttachDelegate("Seat.CueSurgeG", () => Math.Round(State.CueSurgeG, 3));
            this.AttachDelegate("Seat.CueSwayG", () => Math.Round(State.CueSwayG, 3));
            this.AttachDelegate("Seat.CueHeaveG", () => Math.Round(State.CueHeaveG, 3));
            this.AttachDelegate("Seat.PeakG", () => Math.Round(State.PeakDevG, 3));
            this.AttachDelegate("Seat.VibrationRmsG", () => Math.Round(State.VibRmsG, 3));

            this.AttachDelegate("Gain.Global", () => Math.Round(Controller.Snapshot.Global, 3));
            this.AttachDelegate("Gain.Pitch", () => Math.Round(Controller.Snapshot.Pitch, 3));
            this.AttachDelegate("Gain.Roll", () => Math.Round(Controller.Snapshot.Roll, 3));
            this.AttachDelegate("Gain.Heave", () => Math.Round(Controller.Snapshot.Heave, 3));
            this.AttachDelegate("Status", () => Controller.Snapshot.Status == ControllerStatus.WaitingForSensor && SensorFresh && !Settings.LevelCaptured
                ? "NeedsLevel" : Controller.Snapshot.Status.ToString());
            this.AttachDelegate("Applying", () => ProxyActive || (Settings.Mode == ApplyMode.SimHubGainEvents && Settings.AdaptiveEnabled));
            this.AttachDelegate("Tripped", () => Controller.Tripped);
            this.AttachDelegate("ImpactCount", () => Controller.ImpactCount);
            this.AttachDelegate("Level.Pitch", () => Math.Round(Controller.LevelPitch, 2));
            this.AttachDelegate("Level.Roll", () => Math.Round(Controller.LevelRoll, 2));
            this.AttachDelegate("Level.Heave", () => Math.Round(Controller.LevelHeave, 2));
            this.AttachDelegate("Level.Cue", () => Math.Round(Controller.LevelGlobal, 2));

            this.AttachDelegate("Tracking.PitchDelayMs", () => PitchTracking.Valid ? Math.Round(PitchTracking.DelayMs, 0) : -1);
            this.AttachDelegate("Tracking.PitchCoherence", () => Math.Round(PitchTracking.Coherence, 2));
            this.AttachDelegate("Tracking.PitchDegPerUnit", () => PitchTracking.Valid ? Math.Round(PitchTracking.Scale, 2) : 0);
            this.AttachDelegate("Tracking.RollDelayMs", () => RollTracking.Valid ? Math.Round(RollTracking.DelayMs, 0) : -1);
            this.AttachDelegate("Tracking.RollCoherence", () => Math.Round(RollTracking.Coherence, 2));

            this.AttachDelegate("Sensor.Still", () => (State.Flags & ImuFlags.Still) != 0);
            this.AttachDelegate("Mount.Auto", () => Settings.MountAuto);
            this.AttachDelegate("Park.State", () => Park.State.ToString());
            this.AttachDelegate("Park.ReferenceLearned", () => Park.HasReference);
            this.AttachDelegate("Park.DeviationDeg", () => double.IsNaN(Park.LastDeviationDeg) ? -1 : Math.Round(Park.LastDeviationDeg, 2));
            this.AttachDelegate("Park.MountShifted", () => Park.MountShifted);
            this.AttachDelegate("Proxy.Forwarded", () => Proxy.Forwarded);
            this.AttachDelegate("Proxy.Active", () => ProxyActive);
            this.AttachDelegate("LastMessage", () => _lastMessage);
        }

        private void RegisterActionsAndEvents()
        {
            this.AddEvent("Trip");            // map to e.g. a sound, a dash alert, or a Motion "disable"/"park" action
            this.AddEvent("Impact");
            this.AddEvent("GainDownRequest"); // map to SimHub Motion "decrease global gain" in Controls & events
            this.AddEvent("GainUpRequest");   // map to SimHub Motion "increase global gain"
            this.AddEvent("MountShifted");    // parked pose differs from the learned one: check the IMU box / homing
            this.AddEvent("ParkChecked");

            this.AddAction("ToggleAdaptive", (a, b) => { Settings.AdaptiveEnabled = !Settings.AdaptiveEnabled; Note("Adaptive " + (Settings.AdaptiveEnabled ? "ON" : "OFF")); });
            this.AddAction("CaptureLevel", (a, b) => StartLevelCapture());
            this.AddAction("GyroCalibrate", (a, b) => Imu.Send("GYROCAL"));
            this.AddAction("ResetTrip", (a, b) => { Controller.ResetTrip(); Note("Trip reset"); });
            this.AddAction("ResetGains", (a, b) => { Controller.ResetGains(); Note("Gains reset"); });
            this.AddAction("CaptureNeutral", (a, b) => StartNeutralCapture());
            this.AddAction("RelearnParkPose", (a, b) => RelearnParkPose());
            this.AddAction("AutoAlignRest", (a, b) => StartAlignCapture(true));
            this.AddAction("AutoAlignNoseUp", (a, b) => StartAlignCapture(false));
            this.AddAction("ShowGyroBias", (a, b) => Imu.Send("BIAS"));
        }

        /// <summary>Auto-align step 1 (rig at centre, still) or step 2 (rig pitched nose-up ≥ 2°, still).</summary>
        public void StartAlignCapture(bool rest)
        {
            _alignIsRest = rest;
            Aligner.BeginCapture();
            _alignUntil = ImuSerialLink.NowSec + 1.5;
            Note(rest ? "Auto-align 1/2: capturing REST for 1.5 s — rig at centre, keep still"
                      : "Auto-align 2/2: capturing NOSE-UP for 1.5 s — rig pitched nose-up (pure pitch, ≥ 2°), keep still");
        }

        private void FinishAlignCapture()
        {
            _alignUntil = double.NaN;
            string err;
            bool ok = _alignIsRest ? Aligner.EndRest(out err) : Aligner.EndNoseUp(out err);
            if (!ok) { Note("Auto-align capture failed: " + err); return; }
            if (_alignIsRest) { Note("Auto-align: REST captured. Now pitch the rig nose-up ≥ 2°, hold it, and capture NOSE-UP."); return; }

            if (!Aligner.Solve(out var fwd, out var up, out var tilt, out err)) { Note("Auto-align failed: " + err); return; }
            Settings.MountAuto = true;
            Settings.MountAutoForward = fwd;
            Settings.MountAutoUp = up;
            Settings.LevelCaptured = true;               // "up" was measured at the level pose
            Settings.LevelPitchDeg = 0; Settings.LevelRollDeg = 0;
            Estimator.SetMountingVectors(fwd, up);
            Settings.ParkReferenceLearned = false;       // new frame of reference: re-learn the parked pose
            Park.ClearReference();
            SaveSettings();
            Note(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Auto-align done (tilt used {0:F1}°): forward = ({1:F2}, {2:F2}, {3:F2}), up = ({4:F2}, {5:F2}, {6:F2}) in sensor axes. Level set.",
                tilt, fwd[0], fwd[1], fwd[2], up[0], up[1], up[2]));
        }

        public void StartLevelCapture()
        {
            _levelSumP = _levelSumR = 0; _levelN = 0;
            _levelUntil = ImuSerialLink.NowSec + 1.5;
            Note("Capturing level for 1.5 s — keep the rig parked at its centre position");
        }

        /// <summary>Forget the parked pose; it is learned again at the next park (use after re-mounting the box).</summary>
        public void RelearnParkPose()
        {
            Settings.ParkReferenceLearned = false;
            Park.ClearReference();
            SaveSettings();
            Note("Parked pose cleared — it will be learned at the next park");
        }

        public void StartNeutralCapture()
        {
            var xf = Proxy.Transformer;
            if (xf == null || !Proxy.Running) { Note("Start the UDP proxy first"); return; }
            xf.BeginNeutralCapture();
            _neutralUntil = ImuSerialLink.NowSec + 2.0;
            Note("Capturing actuator neutral for 2 s — SimHub motion on, game idle/paused");
        }

        internal void Note(string msg)
        {
            _lastMessage = DateTime.Now.ToString("HH:mm:ss ") + msg;
            try { SimHub.Logging.Current.Info("[AdaptiveMotion] " + msg); } catch { }
        }
    }

    internal static class MenuIcon
    {
        private static ImageSource _icon;
        public static ImageSource Create()
        {
            if (_icon != null) return _icon;
            // A tilted seat line over a level line: drawn, so no resource file is needed
            var g = new GeometryGroup();
            g.Children.Add(Geometry.Parse("M2,20 L22,20"));
            g.Children.Add(Geometry.Parse("M4,15 L20,9"));
            g.Children.Add(Geometry.Parse("M12,12 L12,20"));
            var d = new GeometryDrawing(null, new Pen(Brushes.White, 2), g);
            var img = new DrawingImage(d);
            img.Freeze();
            return _icon = img;
        }
    }
}
