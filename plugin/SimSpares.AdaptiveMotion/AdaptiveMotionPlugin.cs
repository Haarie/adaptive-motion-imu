using System;
using System.Linq;
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
        internal readonly DataLogger Logger = new DataLogger();
        internal readonly UpdateService Updater = new UpdateService();
        private System.Threading.CancellationTokenSource _fwCts;
        private volatile bool _gameRunning;
        // logging
        private double _nextLogSec;
        private bool _autoLogging;
        private volatile string _gameName = "", _carName = "", _trackName = "", _gear = "";
        private double _speedKmh = double.NaN, _rpm = double.NaN, _gSurge = double.NaN, _gSway = double.NaN, _gHeave = double.NaN;

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
        // verification (test mode)
        internal readonly object VerifyLock = new object();
        private VerificationRun _verifyRun;            // running test (null when idle)
        private VerificationRun _lastRun;              // last finished/aborted test, for the Verify tab
        private volatile bool _verifyDone;
        private double[] _verifyNeutral;
        private double _verifyDoneSec = double.NaN;
        private volatile bool _verifyHoldEnd;
        private string _fingerprint; private double _fingerprintSec = double.NaN;

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
            Logger.Log += Note;
            Updater.Log += Note;
            Proxy.CommandModes += (p, r) => { _cmdPitch = (float)p; _cmdRoll = (float)r; };
            Controller.TripRaised += msg => { Note("TRIP: " + msg); this.TriggerEvent("Trip"); };
            Controller.ImpactDetected += () => { this.TriggerEvent("Impact"); LogEvent("IMPACT"); };

            RegisterProperties();
            RegisterActionsAndEvents();
            ApplySettings();
            CheckSimHubMotionAtStart();
            Note("Initialised, plugin " + UpdateService.InstalledPluginVersion);
            if (Settings.UpdateCheckOnStart)
                System.Threading.Tasks.Task.Delay(15000).ContinueWith(_ => CheckForUpdates());
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
                    _otherNoted = false; _templateSaved = false;
                    var xf = new MotionPacketTransformer(Settings.Packet, Settings.Actuators, Settings.Neutral);
                    Proxy.Start(Settings.ProxyListenPort, Settings.ProxyTargetHost, Settings.ProxyTargetPort, xf,
                                () => Controller.Snapshot, () => ProxyActive);
                    Proxy.SeedTemplate(SavedOrSynthesizedTemplate());
                }
                catch (Exception e) { Note("Proxy: " + e.Message); }
            }
        }

        public void SaveSettings() => this.SaveCommonSettings("GeneralSettings", Settings);

        /// <summary>Gains are applied to UDP packets only when every precondition holds.</summary>
        public bool ProxyActive => Settings.Mode == ApplyMode.UdpProxy && Settings.AdaptiveEnabled
                                   && Settings.LevelCaptured && VerificationValid && !Verifying;

        // ======================================================================================
        // Verification (static test: commanded pose vs measured seat attitude)
        // ======================================================================================
        /// <summary>Everything that changes how SimHub's numbers become seat motion. A change invalidates the report.</summary>
        public string Fingerprint
        {
            get
            {
                double now = ImuSerialLink.NowSec;
                if (_fingerprint == null || double.IsNaN(_fingerprintSec) || now - _fingerprintSec > 1.0)
                {
                    var s = Settings;
                    _fingerprint = VerificationReport.Fingerprint(s.Rig, s.Actuators, s.Packet, s.Neutral, s.MountForward, s.MountUp, s.MountAuto,
                        s.MountAutoForward, s.MountAutoUp, s.LevelCaptured, s.LevelPitchDeg, s.LevelRollDeg, s.ProxyTargetHost, s.ProxyTargetPort);
                    _fingerprintSec = now;
                }
                return _fingerprint;
            }
        }

        /// <summary>True while a passed verification exists for exactly the current settings.</summary>
        public bool VerificationValid => Settings.LastVerification != null && Settings.LastVerification.ValidFor(Fingerprint);
        public bool Verifying { get { lock (VerifyLock) return _verifyRun != null; } }
        public VerificationRun RunForDisplay { get { lock (VerifyLock) return _verifyRun ?? _lastRun; } }

        /// <summary>Why the test cannot start right now (empty list = ready).</summary>
        public System.Collections.Generic.List<string> VerifyBlockers()
        {
            var b = new System.Collections.Generic.List<string>();
            var s = Settings;
            if (!SensorFresh) b.Add("the IMU box is not sending data");
            if (!s.LevelCaptured) b.Add("capture the level first (rig parked at its centre)");
            string geo = s.Rig.Problem(s.Actuators, s.Packet);
            if (geo != null) b.Add(geo);
            if (s.Mode != ApplyMode.UdpProxy) b.Add("set the mode to UDP proxy (the test drives the rig through the proxy)");
            else if (!Proxy.Running) b.Add("the UDP proxy is not running: check the controller host/port and Save & apply");
            else if (!Proxy.HasTemplate)
                b.Add("the packet format is not known yet: start SimHub motion once (a game or SimHub's motion test) with SimHub sending to 127.0.0.1:" + s.ProxyListenPort);
            else if (!double.IsNaN(Proxy.LastPacketSec) && ImuSerialLink.NowSec - Proxy.LastPacketSec < 1 && !Verifying)
                b.Add("SimHub is sending motion right now: stop the game / motion test so only the test drives the rig");
            if (s.Neutral == null) b.Add("set the neutral (Setup step 5: Use centre)");
            if (_gameRunning) b.Add("stop the game: the test only runs with the game closed or idle in the menu");
            if (geo == null && s.Neutral != null && s.Neutral.Length >= s.Rig.Count)
            {
                double need = 0, frac = Math.Min(1, s.Verify.MaxFractionPct / 100);
                foreach (var z in s.Rig.HeightsMm(s.Rig.MaxPitchDeg * frac, 0)) need = Math.Max(need, Math.Abs(z));
                foreach (var z in s.Rig.HeightsMm(0, s.Rig.MaxRollDeg * frac)) need = Math.Max(need, Math.Abs(z));
                double room = s.Rig.HeadroomMm(s.Neutral, s.Packet.MaxValue);
                if (need > room * 0.98)
                    b.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "the largest test pose needs {0:F0} mm of stroke but only {1:F0} mm is left around neutral: check the stroke and limits", need, room));
            }
            return b;
        }

        public bool StartVerification(out string why)
        {
            var blockers = VerifyBlockers();
            if (blockers.Count > 0) { why = string.Join("; ", blockers); return false; }
            try
            {
                lock (VerifyLock)
                {
                    if (_verifyRun != null) { why = "a test is already running"; return false; }
                    _verifyNeutral = (double[])Settings.Neutral.Clone();
                    _verifyRun = new VerificationRun(Settings.Rig.MaxPitchDeg, Settings.Rig.MaxRollDeg, Settings.Verify.Clone());
                    _verifyDone = false;
                    _verifyDoneSec = double.NaN;
                    _verifyHoldEnd = false;
                }
                Proxy.BeginOverride(VerifyTick);
                LogEvent("VERIFY START");
                Note(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Verification started: pitch up to ±{0:F1}°, roll up to ±{1:F1}°. Stay clear of the rig; Stop parks it.",
                    Settings.Rig.MaxPitchDeg * Math.Min(1, Settings.Verify.MaxFractionPct / 100), Settings.Rig.MaxRollDeg * Math.Min(1, Settings.Verify.MaxFractionPct / 100)));
                why = null;
                return true;
            }
            catch (Exception e)
            {
                lock (VerifyLock) _verifyRun = null;
                why = e.Message;
                return false;
            }
        }

        public void StopVerification(string reason = "stopped by user")
        {
            lock (VerifyLock)
            {
                if (_verifyRun == null) return;
                if (_verifyRun.Done) _verifyHoldEnd = true;   // already back at centre: end the hold now
                else _verifyRun.RequestStop(reason);
            }
        }

        // Called by the proxy's test sender every 10 ms: advance the run and return the actuator values to send.
        private double[] VerifyTick()
        {
            lock (VerifyLock)
            {
                var run = _verifyRun;
                if (run == null) return null;
                var st = State;
                bool fresh = SensorFresh;
                run.Update(ImuSerialLink.NowSec, fresh ? st.PitchDeg : double.NaN, fresh ? st.RollDeg : double.NaN);
                if (run.Done)
                {
                    // Hold the rig at centre for a moment before handing it back (with SimHub idle,
                    // the controller parks the rig when the packets stop).
                    double now = ImuSerialLink.NowSec;
                    if (double.IsNaN(_verifyDoneSec)) _verifyDoneSec = now;
                    if (_verifyHoldEnd || now - _verifyDoneSec >= run.Options.HoldSec) _verifyDone = true;
                }
                var rig = Settings.Rig;
                return rig.HeightsToValues(rig.HeightsMm(run.CommandPitchDeg, run.CommandRollDeg), _verifyNeutral, Settings.Packet.MaxValue);
            }
        }

        // SimHub thread: finish a run (report, files) once the rig is back at zero
        private void FinishVerificationIfDone()
        {
            if (!_verifyDone) return;
            VerificationRun run;
            lock (VerifyLock) { run = _verifyRun; _verifyRun = null; _lastRun = run; _verifyDone = false; }
            Proxy.EndOverride();
            if (run == null) return;
            var rep = VerificationReport.From(run, Fingerprint, UpdateService.InstalledPluginVersion.ToString(3), _deviceInfo);
            Settings.LastVerification = rep;
            SaveSettings();
            string file = SaveVerificationFiles(run, rep);
            LogEvent("VERIFY " + rep.Overall.ToUpperInvariant());
            Note("Verification " + (rep.Passed ? "PASSED" : rep.Overall.ToUpperInvariant()) + ": " + string.Join(" ", rep.Findings) + (file != null ? "  Report: " + file : ""));
        }

        private string SaveVerificationFiles(VerificationRun run, VerificationReport rep)
        {
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Adaptive Motion IMU", "verification");
                System.IO.Directory.CreateDirectory(dir);
                string stem = System.IO.Path.Combine(dir, "verify_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture));
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var r = Settings.Rig;
                var txt = new System.Text.StringBuilder();
                txt.AppendLine("Adaptive Motion IMU - rig verification");
                txt.AppendLine("date      " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv));
                txt.AppendLine("result    " + (rep.Passed ? "PASSED" : rep.Overall));
                txt.AppendLine("plugin    " + rep.Plugin + "   device " + rep.Firmware);
                txt.AppendLine(string.Format(inv, "limits    pitch ±{0:F1}°  roll ±{1:F1}°   tolerance max({2} %, {3}°)", rep.LimitPitchDeg, rep.LimitRollDeg, run.Options.TolPct, run.Options.TolMinDeg));
                txt.AppendLine(string.Format(inv, "rig       layout {0}, stroke {1} mm, track {2} mm, wheelbase {3} mm", Settings.Actuators, r.StrokeMm, r.TrackMm, r.WheelbaseMm));
                txt.AppendLine(string.Format(inv, "scale     pitch {0:F3}  roll {1:F3}   max cross-talk {2:F2}°   zero {3:F2}/{4:F2}°", rep.PitchScale, rep.RollScale, rep.MaxCrossTalkDeg, rep.ZeroPitchDeg, rep.ZeroRollDeg));
                txt.AppendLine("settings  " + rep.SettingsHash);
                txt.AppendLine();
                foreach (var f in rep.Findings) txt.AppendLine("- " + f);
                System.IO.File.WriteAllText(stem + ".txt", txt.ToString());
                System.IO.File.WriteAllText(stem + ".csv", run.Csv());
                return stem + ".txt";
            }
            catch (Exception e) { Note("Could not save the verification report: " + e.Message); return null; }
        }

        /// <summary>What SimHub currently commands, in degrees (NaN when the geometry or packets are missing).</summary>
        public void CommandedPose(out double pitchDeg, out double rollDeg)
        {
            pitchDeg = rollDeg = double.NaN;
            lock (VerifyLock)
                if (_verifyRun != null) { pitchDeg = _verifyRun.CommandPitchDeg; rollDeg = _verifyRun.CommandRollDeg; return; }
            var xf = Proxy.Transformer;
            var s = Settings;
            if (xf == null || !Proxy.Running || xf.MotionPackets == 0 || s.Rig.Problem(s.Actuators, s.Packet) != null) return;
            try
            {
                var neutral = s.Neutral ?? MotionPacketTransformer.DefaultNeutral(s.Packet);
                s.Rig.Pose(s.Rig.ValuesToHeights(xf.LastAxes, neutral, s.Packet.MaxValue), out pitchDeg, out rollDeg, out _);
            }
            catch { pitchDeg = rollDeg = double.NaN; }
        }

        // ======================================================================================
        // SimHub motion settings import
        // ======================================================================================
        public static string SimHubMotionSettingsPath =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "MotionPlugin.GeneralSettingsV2.json");

        public SimHubMotionImport ReadSimHubMotion()
        {
            string f = SimHubMotionSettingsPath;
            if (!System.IO.File.Exists(f)) return new SimHubMotionImport { Error = "SimHub's motion settings file was not found (" + f + ")" };
            try { return SimHubMotionImport.FromJson(System.IO.File.ReadAllText(f), System.IO.File.GetLastWriteTime(f)); }
            catch (Exception e) { return new SimHubMotionImport { Error = "reading SimHub's motion settings failed: " + e.Message }; }
        }

        /// <summary>Fill rig dimensions, stroke, limits and the controller address from SimHub's motion settings.</summary>
        public bool ImportFromSimHub(bool quiet)
        {
            var r = ReadSimHubMotion();
            if (!r.Ok) { if (!quiet) Note("Import from SimHub: " + r.Error); return false; }
            var s = Settings;
            s.Actuators = r.Layout;
            r.ApplyTo(s.Rig);
            if (r.Packet != null)
            {
                r.Packet.TextHex = s.Packet.Text && r.Packet.Text && s.Packet.TextPrefix == r.Packet.TextPrefix ? s.Packet.TextHex : r.Packet.TextHex; // keep a detected hex flag
                s.Packet.CopyFrom(r.Packet);
                s.Neutral = null;   // different packet format → capture neutral again
            }
            if (s.Packet.AxisCount < s.Rig.Count) s.Packet.AxisCount = s.Rig.Count;
            bool local = r.OutputHost == "127.0.0.1" || r.OutputHost.Equals("localhost", StringComparison.OrdinalIgnoreCase);
            if (r.OutputHost != "" && !local) { s.ProxyTargetHost = r.OutputHost; s.ProxyTargetPort = r.OutputPort; }
            SaveSettings();
            ApplySettings();
            var msg = "Imported from " + r.Summary() + "." + (r.Packet != null ? " Packet format: " + r.Packet.Describe() + "." : "");
            if (local && r.OutputPort == s.ProxyListenPort) msg += " SimHub already sends to the plugin.";
            if (r.Notes.Count > 0) msg += " " + string.Join(" ", r.Notes);
            msg += " Changed something in SimHub's motion settings? Restart SimHub first: it saves them when it closes.";
            Note(msg);
            return true;
        }

        /// <summary>The last real SimHub motion packet (saved), or one built from the format, or null.</summary>
        public byte[] SavedOrSynthesizedTemplate()
        {
            try
            {
                if (!string.IsNullOrEmpty(Settings.PacketTemplate))
                {
                    var t = Convert.FromBase64String(Settings.PacketTemplate);
                    if (MotionPacketCodec.IsMotionPacket(Settings.Packet, t, t.Length)) return t;
                }
            }
            catch { }
            return MotionPacketCodec.Synthesize(Settings.Packet);
        }

        /// <summary>True once SimHub's packets have matched the format (this session or saved earlier).</summary>
        public bool RouteConfirmed
        {
            get
            {
                var xf = Proxy.Transformer;
                if (xf != null && xf.MotionPackets > 0) return true;
                try
                {
                    if (string.IsNullOrEmpty(Settings.PacketTemplate)) return false;
                    var t = Convert.FromBase64String(Settings.PacketTemplate);
                    return MotionPacketCodec.IsMotionPacket(Settings.Packet, t, t.Length);
                }
                catch { return false; }
            }
        }

        private bool _templateSaved;
        private void SaveTemplateOnce()
        {
            var xf = Proxy.Transformer;
            if (_templateSaved || xf == null || xf.MotionPackets == 0 || Verifying) return;
            var t = Proxy.TemplateCopy;
            if (t == null || !MotionPacketCodec.IsMotionPacket(Settings.Packet, t, t.Length)) return;
            _templateSaved = true;
            Settings.PacketTemplate = Convert.ToBase64String(t);
            SaveSettings();
        }

        /// <summary>
        /// Neutral = centre of the output range for the rig's actuators (where SimHub puts them at zero motion);
        /// other axes keep the value SimHub sends for them.
        /// </summary>
        public void UseCentreNeutral()
        {
            var l = Settings.Packet;
            var n = MotionPacketTransformer.DefaultNeutral(l);
            var t = SavedOrSynthesizedTemplate();
            if (t != null)
            {
                var axes = new double[l.AxisCount];
                try { MotionPacketCodec.Decode(l, t, axes); for (int i = Math.Max(1, Settings.Rig.Count); i < n.Length; i++) n[i] = axes[i]; } catch { }
            }
            Settings.Neutral = n;
            SaveSettings();
            ApplySettings();
            Note("Neutral set to the centre of the range: " + string.Join(", ", Array.ConvertAll(n, v => v.ToString("F0"))));
        }

        /// <summary>Take the packet format from the last packet SimHub sent that did not match the current format.</summary>
        public bool DetectPacketFormat(out string message)
        {
            var xf = Proxy.Transformer;
            var pkt = xf?.LastOtherPacket;
            if (pkt == null) { message = "no packets from SimHub seen yet: check that SimHub sends to 127.0.0.1:" + Settings.ProxyListenPort + " with motion on"; return false; }
            var l = MotionPacketCodec.DetectText(pkt, pkt.Length, Settings.Packet.MaxValue > 255 ? Settings.Packet.MaxValue : 65535);
            if (l == null) { message = "the last packet is binary (" + MotionPacketCodec.Sample(pkt, pkt.Length) + "): set header bytes, axes and bytes per axis on the Advanced tab"; return false; }
            Settings.Packet.CopyFrom(l);
            Settings.Neutral = null;
            SaveSettings();
            ApplySettings();
            message = "Packet format detected: " + l.Describe() + ". Now capture the neutral (step 5).";
            Note(message);
            return true;
        }

        private bool _otherNoted;
        private void NoteUnknownPackets()
        {
            var xf = Proxy.Transformer;
            if (_otherNoted || xf == null || xf.MotionPackets > 0 || xf.OtherPackets < 20 || xf.LastOtherSample == null) return;
            _otherNoted = true;
            Note("Packets from SimHub do not match the packet format (" + Settings.Packet.Describe() + "). Last packet: " + xf.LastOtherSample + "  → Setup step 4: Detect format.");
        }

        /// <summary>At start: fill an empty rig from SimHub, or warn when SimHub's values changed since.</summary>
        private void CheckSimHubMotionAtStart()
        {
            try
            {
                if (Settings.Rig.Count == 0) { ImportFromSimHub(true); return; }
                var r = ReadSimHubMotion();
                if (!r.Ok || Settings.Rig.Source != "SimHub") return;
                var diff = r.Differences(Settings.Rig, Settings.Actuators);
                if (diff.Count > 0) Note("SimHub's motion settings differ from the plugin (" + string.Join(", ", diff) + "): open Setup step 3 and press Import from SimHub, then verify again.");
            }
            catch (Exception e) { Note("SimHub motion settings check: " + e.Message); }
        }

        /// <summary>Copy the SimHub limits into the plugin's own envelope (gain reduction starts there, trips a bit beyond).</summary>
        public void ApplyRigLimitsToController()
        {
            var r = Settings.Rig; var c = Settings.Controller;
            if (!(r.MaxPitchDeg > 0) || !(r.MaxRollDeg > 0)) { Note("Enter the SimHub pitch and roll limits first"); return; }
            c.PitchLimitDeg = r.MaxPitchDeg; c.RollLimitDeg = r.MaxRollDeg;
            c.PitchHardDeg = Math.Round(r.MaxPitchDeg * 1.2 + 1, 1); c.RollHardDeg = Math.Round(r.MaxRollDeg * 1.2 + 1, 1);
            Controller.UpdateSettings(c);
            SaveSettings();
            Note(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Plugin limits set from SimHub: pitch {0}°, roll {1}° (hard trip {2}° / {3}°)", c.PitchLimitDeg, c.RollLimitDeg, c.PitchHardDeg, c.RollHardDeg));
        }

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

            if (Logger.Active && now >= _nextLogSec)
            {
                double period = 1.0 / Math.Max(1, Math.Min(250, Settings.LogRateHz));
                _nextLogSec = (now - _nextLogSec > period) ? now + period : _nextLogSec + period;
                Logger.Write(LogRow(st, f, now, null));
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
            bool wasRunning = _gameRunning;
            _gameRunning = data.GameRunning;
            Controller.Tick(now);
            ReadGameData(data);
            if (_gameRunning && Verifying) StopVerification("a game started");
            if (Verifying && !Proxy.Running) { lock (VerifyLock) { _lastRun = _verifyRun; _verifyRun = null; } Note("Verification stopped: the proxy stopped"); }
            FinishVerificationIfDone();
            NoteUnknownPackets();
            SaveTemplateOnce();
            if (Settings.LogAutoStart)
            {
                if (_gameRunning && !wasRunning && !Logger.Active) { StartLog(); _autoLogging = true; }
                else if (!_gameRunning && wasRunning && _autoLogging) { StopLog(); _autoLogging = false; }
            }

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

            if (Settings.Mode == ApplyMode.SimHubGainEvents && Settings.AdaptiveEnabled && Settings.LevelCaptured && VerificationValid)
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
            lock (VerifyLock) _verifyRun = null;
            Proxy.EndOverride();
            Logger.Stop();
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
            this.AttachDelegate("Applying", () => ProxyActive || (Settings.Mode == ApplyMode.SimHubGainEvents && Settings.AdaptiveEnabled && VerificationValid));
            this.AttachDelegate("Verify.Valid", () => VerificationValid);
            this.AttachDelegate("Verify.Running", () => Verifying);
            this.AttachDelegate("Verify.Result", () => Settings.LastVerification?.Overall ?? "");
            this.AttachDelegate("Command.PitchDeg", () => { CommandedPose(out var p, out _); return double.IsNaN(p) ? 0 : Math.Round(p, 2); });
            this.AttachDelegate("Command.RollDeg", () => { CommandedPose(out _, out var r); return double.IsNaN(r) ? 0 : Math.Round(r, 2); });
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
            this.AttachDelegate("Update.InstalledVersion", () => UpdateService.InstalledPluginVersion.ToString(3));
            this.AttachDelegate("Update.LatestVersion", () => Updater.Latest?.Plugin?.Version?.ToString() ?? "");
            this.AttachDelegate("Update.Available", () => PluginUpdateAvailable);
            this.AttachDelegate("Update.FirmwareInstalled", () => UpdateService.ParseFirmwareVersion(_deviceInfo)?.ToString() ?? "");
            this.AttachDelegate("Update.FirmwareLatest", () => Updater.Latest?.Firmware?.Version?.ToString() ?? "");
            this.AttachDelegate("Update.FirmwareAvailable", () => FirmwareUpdateAvailable);
            this.AttachDelegate("Log.Active", () => Logger.Active);
            this.AttachDelegate("Log.File", () => Logger.FilePath);
            this.AttachDelegate("Log.Rows", () => Logger.Rows);
            this.AttachDelegate("Log.Seconds", () => Math.Round(Logger.Seconds(ImuSerialLink.NowSec), 0));
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
            this.AddAction("LogStart", (a, b) => StartLog());
            this.AddAction("LogStop", (a, b) => { StopLog(); _autoLogging = false; });
            this.AddAction("VerifyStart", (a, b) => { if (!StartVerification(out var why)) Note("Verification cannot start: " + why); });
            this.AddAction("VerifyStop", (a, b) => StopVerification());
            this.AddAction("LogToggle", (a, b) => { if (Logger.Active) { StopLog(); _autoLogging = false; } else StartLog(); });
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

        // ======================================================================================
        // Data logging
        // ======================================================================================
        internal static readonly string[] LogColumns =
        {
            "time_s", "device_us", "pitch_deg", "roll_deg", "pitch_rate_dps", "roll_rate_dps", "yaw_rate_dps",
            "cue_surge_g", "cue_sway_g", "cue_heave_g", "lin_surge_g", "lin_sway_g", "lin_heave_g",
            "peak_g", "vib_rms_g", "flags", "gain_global", "gain_pitch", "gain_roll", "gain_heave", "status",
            "level_global", "level_pitch", "level_roll", "level_heave", "applying", "cmd_pitch", "cmd_roll",
            "cmd_pitch_deg", "cmd_roll_deg", "game_running", "speed_kmh", "gear", "rpm", "game_accel_surge", "game_accel_sway", "game_accel_heave", "event"
        };

        public void StartLog()
        {
            string tag = string.Join("_", new[] { _gameName, _carName, _trackName }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var c = Settings.Controller;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string[] comments =
            {
                "Adaptive Motion IMU log - Sim-Spares",
                "started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv) + (tag.Length > 0 ? "  " + tag.Replace("_", " / ") : ""),
                "device " + _deviceInfo,
                string.Format(inv, "mode {0}, adaptive {1}, level captured {2} (pitch {3:F2}, roll {4:F2}), rate {5} Hz",
                    Settings.Mode, Settings.AdaptiveEnabled, Settings.LevelCaptured, Settings.LevelPitchDeg, Settings.LevelRollDeg, Settings.LogRateHz),
                string.Format(inv, "limits pitch {0} roll {1} deg, rate {2} deg/s, heave {3} g, cue {4} g, vib {5} g, knee {6}, min gain {7}, impact {8} g, trip {9}/{10} deg",
                    c.PitchLimitDeg, c.RollLimitDeg, c.RateLimitDps, c.HeaveLimitG, c.CueLimitG, c.VibLimitG, c.Knee, c.MinGain, c.ImpactG, c.PitchHardDeg, c.RollHardDeg),
                "flags: 1 I2C error, 2 gyro calibrated, 4 accel sat, 8 gyro sat, 16 clone IMU, 32 frames dropped, 64 tilt correction gated, 128 still",
                "level_* = fraction of each limit in use (1 = at the limit); events (TRIP, IMPACT, messages) are rows with only time_s and event"
            };
            _nextLogSec = 0;
            Logger.Start(Settings.LogFolder, tag, string.Join(",", LogColumns), comments, ImuSerialLink.NowSec);
        }

        public void StopLog() => Logger.Stop();

        private void LogEvent(string text)
        {
            if (!Logger.Active) return;
            var sb = new System.Text.StringBuilder();
            sb.Append(Logger.Seconds(ImuSerialLink.NowSec).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(',', LogColumns.Length - 1);
            sb.Append('"').Append(text.Replace("\"", "'")).Append('"');
            Logger.Write(sb.ToString());
        }

        private string LogRow(PlatformState st, ImuFrame f, double now, string evt)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var g = Controller.Snapshot;
            var sb = new System.Text.StringBuilder(320);
            Action<double, string> n = (v, fmt) => { if (!double.IsNaN(v) && !double.IsInfinity(v)) sb.Append(v.ToString(fmt, inv)); sb.Append(','); };
            n(Logger.Seconds(now), "F4");
            sb.Append(f.DeviceMicros).Append(',');
            n(st.PitchDeg, "F3"); n(st.RollDeg, "F3");
            n(st.PitchRateDps, "F2"); n(st.RollRateDps, "F2"); n(st.YawRateDps, "F2");
            n(st.CueSurgeG, "F4"); n(st.CueSwayG, "F4"); n(st.CueHeaveG, "F4");
            n(st.LinSurgeG, "F4"); n(st.LinSwayG, "F4"); n(st.LinHeaveG, "F4");
            n(st.PeakDevG, "F3"); n(st.VibRmsG, "F4");
            sb.Append((int)st.Flags).Append(',');
            n(g.Global, "F3"); n(g.Pitch, "F3"); n(g.Roll, "F3"); n(g.Heave, "F3");
            sb.Append(g.Status).Append(',');
            n(Controller.LevelGlobal, "F3"); n(Controller.LevelPitch, "F3"); n(Controller.LevelRoll, "F3"); n(Controller.LevelHeave, "F3");
            sb.Append(ProxyActive ? 1 : 0).Append(',');
            n(_cmdPitch, "F3"); n(_cmdRoll, "F3");
            CommandedPose(out var cp, out var cr); n(cp, "F3"); n(cr, "F3");
            sb.Append(_gameRunning ? 1 : 0).Append(',');
            if (Settings.LogGameData && _gameRunning)
            {
                n(_speedKmh, "F1"); sb.Append(_gear.Replace(",", "")).Append(','); n(_rpm, "F0");
                n(_gSurge, "F3"); n(_gSway, "F3"); n(_gHeave, "F3");
            }
            else sb.Append(",,,,,,");
            if (evt != null) sb.Append('"').Append(evt).Append('"');
            return sb.ToString();
        }

        // Game values are read by name (reflection) so a SimHub version without one of them still loads the plugin.
        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.PropertyInfo> _propCache =
            new System.Collections.Generic.Dictionary<string, System.Reflection.PropertyInfo>();
        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            string key = o.GetType().FullName + "." + name;
            if (!_propCache.TryGetValue(key, out var pi)) { pi = o.GetType().GetProperty(name); _propCache[key] = pi; }
            try { return pi?.GetValue(o, null); } catch { return null; }
        }
        private static double Num(object v)
        {
            if (v == null) return double.NaN;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); } catch { return double.NaN; }
        }

        private void ReadGameData(GameData data)
        {
            if (!data.GameRunning) return;
            object nd = Prop(data, "NewData");
            _gameName = Convert.ToString(Prop(data, "GameName")) ?? "";
            if (nd == null) return;
            _carName = Convert.ToString(Prop(nd, "CarModel")) ?? "";
            _trackName = Convert.ToString(Prop(nd, "TrackName")) ?? "";
            _speedKmh = Num(Prop(nd, "SpeedKmh"));
            _rpm = Num(Prop(nd, "Rpms"));
            _gear = Convert.ToString(Prop(nd, "Gear")) ?? "";
            _gSurge = Num(Prop(nd, "AccelerationSurge"));
            _gSway = Num(Prop(nd, "AccelerationSway"));
            _gHeave = Num(Prop(nd, "AccelerationHeave"));
        }

        // ======================================================================================
        // Updates
        // ======================================================================================
        public bool PluginUpdateAvailable => UpdateService.IsNewer(Updater.Latest?.Plugin?.Version, UpdateService.InstalledPluginVersion);
        public Version FirmwareVersion => UpdateService.ParseFirmwareVersion(_deviceInfo);
        public bool FirmwareUpdateAvailable => FirmwareVersion != null && UpdateService.IsNewer(Updater.Latest?.Firmware?.Version, FirmwareVersion);
        public bool FirmwareUpdating => _fwCts != null;

        public System.Threading.Tasks.Task CheckForUpdates() =>
            Updater.CheckAsync(Settings.UpdateRepo, Settings.UpdateIncludePrerelease).ContinueWith(t =>
            {
                if (PluginUpdateAvailable) Note("Plugin update available: " + Updater.Latest.Plugin.Version + " (Update tab)");
                if (FirmwareUpdateAvailable) Note("Firmware update available: " + Updater.Latest.Firmware.Version + " (Update tab)");
            });

        /// <summary>Starts the elevated swap helper; on success the caller closes SimHub.</summary>
        public bool InstallPluginUpdate(bool rollback, out string error)
        {
            SaveSettings();
            bool ok = rollback ? Updater.PrepareRollback(out error) : Updater.PreparePluginInstall(Updater.Latest?.Plugin, out error);
            if (ok) Logger.Stop();
            return ok;
        }

        /// <summary>Firmware update in the background: box into UF2 mode, copy, reconnect, confirm the version.</summary>
        public void StartFirmwareUpdate()
        {
            var a = Updater.Latest?.Firmware;
            if (a == null) { Note("No firmware in the latest release - check for updates first"); return; }
            if (_fwCts != null) return;
            _fwCts = new System.Threading.CancellationTokenSource();
            var ct = _fwCts.Token;
            var before = FirmwareVersion;
            System.Threading.Tasks.Task.Run(() =>
            {
                Updater.Busy = true;
                try
                {
                    Func<bool> enter = () =>
                    {
                        bool can = Imu.Connected && before != null && before >= new Version(1, 3, 1);
                        if (can) { Imu.Send("BOOTLOADER"); System.Threading.Thread.Sleep(300); }
                        Imu.Stop();
                        return can;
                    };
                    string err;
                    if (!Updater.FlashFirmware(a, enter, ct, out err)) { Imu.Start(Settings.ComPort); return; }
                    _deviceInfo = "";
                    Imu.Start(Settings.ComPort);
                    var until = DateTime.UtcNow.AddSeconds(20);
                    while (DateTime.UtcNow < until && FirmwareVersion == null) { System.Threading.Thread.Sleep(500); if (Imu.Connected) Imu.Send("INFO"); }
                    var now = FirmwareVersion;
                    if (now != null && !UpdateService.IsNewer(a.Version, now))
                        Updater.Progress = "Firmware updated: " + (before?.ToString() ?? "?") + " → " + now + ". Calibration is kept.";
                    else
                        Updater.Progress = "Firmware written, but the box reports " + (now?.ToString() ?? "nothing yet") + ". Check the COM port in Settings.";
                    Note(Updater.Progress);
                }
                catch (Exception e) { Note("Firmware update: " + e.Message); }
                finally { Updater.Busy = false; _fwCts = null; }
            });
        }

        public void CancelFirmwareUpdate() { try { _fwCts?.Cancel(); } catch { } }

        internal void Note(string msg)
        {
            LogEvent(msg);
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
