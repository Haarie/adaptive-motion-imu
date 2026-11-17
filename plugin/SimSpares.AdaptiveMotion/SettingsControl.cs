using System;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SimSpares.AdaptiveMotion.Core;
using SimSpares.AdaptiveMotion.UI;

namespace SimSpares.AdaptiveMotion
{
    /// <summary>
    /// Settings page, built in code (no XAML) to keep the project to plain .cs files.
    /// Edits go into the plugin's Settings object; "Save &amp; apply" persists and restarts serial/proxy.
    /// </summary>
    public partial class SettingsControl : UserControl
    {
        private readonly AdaptiveMotionPlugin _p;
        private readonly TextBlock _live = Mono();
        private readonly TextBlock _msg = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        private readonly DispatcherTimer _timer;
        private Button _logBtnLive, _logBtnSettings;
        private readonly TextBlock _logStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

        public SettingsControl(AdaptiveMotionPlugin plugin)
        {
            _p = plugin;
            var s = plugin.Settings;
            var root = new StackPanel { Margin = new Thickness(12) };

            // ---------------- Live --------------------------------------------------------------
            root.Children.Add(Header("Live"));
            root.Children.Add(_live);
            root.Children.Add(_msg);

            // ---------------- Sensor ------------------------------------------------------------
            root.Children.Add(Header("Sensor (Arduino + MPU-6050)"));
            var port = new ComboBox { IsEditable = true, Width = 140, Text = s.ComPort };
            RefreshPorts(port);
            port.DropDownOpened += (o, e) => RefreshPorts(port);
            port.LostFocus += (o, e) => s.ComPort = port.Text.Trim();
            port.SelectionChanged += (o, e) => { if (port.SelectedItem != null) s.ComPort = port.SelectedItem.ToString(); };
            root.Children.Add(Row("COM port", port));
            root.Children.Add(Row("Use auto-aligned mounting (any angle)", Check(s.MountAuto, v => s.MountAuto = v)));
            root.Children.Add(Buttons(
                Btn("Auto-align 1: rest (rig at centre)", () => _p.StartAlignCapture(true)),
                Btn("Auto-align 2: nose-up held (≥ 2°)", () => _p.StartAlignCapture(false))));
            root.Children.Add(Row("…or manual: sensor axis pointing forward (side mount: +Z lid to front, -Z lid to rear)", EnumBox(s.MountForward, v => s.MountForward = v)));
            root.Children.Add(Row("…or manual: sensor axis pointing up", EnumBox(s.MountUp, v => s.MountUp = v)));
            root.Children.Add(Row("Device DLPF (0-6)", IntBox(s.DeviceDlpf, v => s.DeviceDlpf = v)));
            root.Children.Add(Row("Device output divider (500/n Hz)", IntBox(s.DeviceOutputDivider, v => s.DeviceOutputDivider = v)));
            root.Children.Add(Row("Learn gyro bias whenever still", Check(s.AutoGyroCal, v => s.AutoGyroCal = v)));
            root.Children.Add(Row("Park check tolerance (deg)", Num(s.Park.ToleranceDeg, v => s.Park.ToleranceDeg = v)));
            root.Children.Add(Buttons(
                Btn("Capture level (rig at centre)", () => _p.StartLevelCapture()),
                Btn("Re-learn parked pose", () => _p.RelearnParkPose()),
                Btn("Show gyro bias", () => _p.Imu.Send("BIAS")),
                Btn("Gyro calibrate (rig still)", () => _p.Imu.Send("GYROCAL")),
                Btn("Send DLPF/ODR to device", () => { _p.Imu.Send("DLPF " + s.DeviceDlpf); _p.Imu.Send("ODR " + s.DeviceOutputDivider); _p.Imu.Send("INFO"); }),
                Btn("Ping", () => _p.Imu.Send("PING"))));

            // ---------------- Mode --------------------------------------------------------------
            root.Children.Add(Header("What the plugin is allowed to do"));
            root.Children.Add(Row("Mode", EnumBox(s.Mode, v => s.Mode = v)));
            root.Children.Add(Row("Adaptive enabled", Check(s.AdaptiveEnabled, v => s.AdaptiveEnabled = v)));

            // ---------------- Logging -----------------------------------------------------------
            root.Children.Add(Header("Data logging (CSV)"));
            var folderBox = Txt(string.IsNullOrWhiteSpace(s.LogFolder) ? IO.DataLogger.DefaultFolder : s.LogFolder, v => s.LogFolder = v);
            folderBox.Width = 420;
            var folderRow = new StackPanel { Orientation = Orientation.Horizontal };
            folderRow.Children.Add(folderBox);
            var browse = Btn("Browse…", () =>
            {
                using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dlg.Description = "Folder for Adaptive Motion log files";
                    dlg.ShowNewFolderButton = true;
                    string cur = folderBox.Text.Trim();
                    if (System.IO.Directory.Exists(cur)) dlg.SelectedPath = cur;
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        folderBox.Text = dlg.SelectedPath; s.LogFolder = dlg.SelectedPath; _p.SaveSettings();
                        _p.Note("Log folder: " + dlg.SelectedPath);
                    }
                }
            });
            browse.Margin = new Thickness(6, 0, 0, 0);
            folderRow.Children.Add(browse);
            root.Children.Add(Row("Save logs in folder", folderRow));
            root.Children.Add(Row("Rows per second (1-250)", IntBox(s.LogRateHz, v => s.LogRateHz = Math.Max(1, Math.Min(250, v)))));
            root.Children.Add(Row("Start/stop automatically with the game", Check(s.LogAutoStart, v => s.LogAutoStart = v)));
            root.Children.Add(Row("Include game data (speed, gear, rpm, game accelerations)", Check(s.LogGameData, v => s.LogGameData = v)));
            _logBtnSettings = Btn("Start log", ToggleLog);
            root.Children.Add(Buttons(
                _logBtnSettings,
                Btn("Open log folder", () =>
                {
                    string dir = string.IsNullOrWhiteSpace(s.LogFolder) ? IO.DataLogger.DefaultFolder : s.LogFolder;
                    try { System.IO.Directory.CreateDirectory(dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
                    catch (Exception e) { _p.Note("Cannot open folder: " + e.Message); }
                })));
            root.Children.Add(_logStatus);

            // ---------------- Envelope ----------------------------------------------------------
            var c = s.Controller;
            root.Children.Add(Header("Envelope measured at the seat"));
            root.Children.Add(Row("Pitch/roll limits follow SimHub", Check(s.ControllerLimitsFollowSimHub, v => s.ControllerLimitsFollowSimHub = v)));
            root.Children.Add(Row("Pitch limit (deg)", Num(c.PitchLimitDeg, v => { c.PitchLimitDeg = v; s.ControllerLimitsFollowSimHub = false; })));
            root.Children.Add(Row("Roll limit (deg)", Num(c.RollLimitDeg, v => { c.RollLimitDeg = v; s.ControllerLimitsFollowSimHub = false; })));
            root.Children.Add(Row("Angular rate limit (deg/s)", Num(c.RateLimitDps, v => c.RateLimitDps = v)));
            root.Children.Add(Row("Heave cue limit (g)", Num(c.HeaveLimitG, v => c.HeaveLimitG = v)));
            root.Children.Add(Row("Horizontal cue limit (g)", Num(c.CueLimitG, v => c.CueLimitG = v)));
            root.Children.Add(Row("Vibration RMS limit (g)", Num(c.VibLimitG, v => c.VibLimitG = v)));
            root.Children.Add(Row("Knee (fraction of limit)", Num(c.Knee, v => c.Knee = v)));
            root.Children.Add(Row("Attack (s)", Num(c.AttackSec, v => c.AttackSec = v)));
            root.Children.Add(Row("Release (s)", Num(c.ReleaseSec, v => c.ReleaseSec = v)));
            root.Children.Add(Row("Minimum gain", Num(c.MinGain, v => c.MinGain = Clamp01(v))));
            root.Children.Add(Row("Impact threshold (g)", Num(c.ImpactG, v => c.ImpactG = v)));
            root.Children.Add(Row("Hard pitch trip (deg)", Num(c.PitchHardDeg, v => c.PitchHardDeg = v)));
            root.Children.Add(Row("Hard roll trip (deg)", Num(c.RollHardDeg, v => c.RollHardDeg = v)));
            root.Children.Add(Row("Trip auto-reset", Check(c.TripAutoReset, v => c.TripAutoReset = v)));
            root.Children.Add(Row("Failsafe gain (sensor lost)", Num(c.FailsafeGain, v => c.FailsafeGain = Clamp01(v))));

            // ---------------- UDP proxy ---------------------------------------------------------
            var pk = s.Packet;
            root.Children.Add(Header("UDP proxy (SimHub Generic UDP → plugin → controller)"));
            root.Children.Add(Row("Listen port (point SimHub here, 127.0.0.1)", IntBox(s.ProxyListenPort, v => s.ProxyListenPort = v)));
            root.Children.Add(Row("Controller host", Txt(s.ProxyTargetHost, v => s.ProxyTargetHost = v)));
            root.Children.Add(Row("Controller port", IntBox(s.ProxyTargetPort, v => s.ProxyTargetPort = v)));
            root.Children.Add(Row("Text command (e.g. NULLCAT,<Axis1>,<Axis2>,…)", Check(pk.Text, v => pk.Text = v)));
            root.Children.Add(Row("  text: fixed first field (e.g. NULLCAT)", Txt(pk.TextPrefix, v => pk.TextPrefix = v)));
            root.Children.Add(Row("  text: values in hexadecimal", Check(pk.TextHex, v => pk.TextHex = v)));
            root.Children.Add(Row("Header bytes before Axis1 (binary)", IntBox(pk.HeaderBytes, v => pk.HeaderBytes = v)));
            root.Children.Add(Row("Axis count", IntBox(pk.AxisCount, v => pk.AxisCount = v)));
            root.Children.Add(Row("Bytes per axis (1/2)", IntBox(pk.BytesPerAxis, v => pk.BytesPerAxis = v == 1 ? 1 : 2)));
            root.Children.Add(Row("High byte first (Axis1a = MSB)", Check(pk.BigEndian, v => pk.BigEndian = v)));
            root.Children.Add(Row("Max value (255/1023/4095/65535)", IntBox(pk.MaxValue, v => pk.MaxValue = v)));
            root.Children.Add(Row("Trailer bytes after last axis", IntBox(pk.TrailerBytes, v => pk.TrailerBytes = v)));
            root.Children.Add(Row("Actuator layout", EnumBox(s.Actuators, v => s.Actuators = v)));
            root.Children.Add(Buttons(Btn("Capture neutral (motion on, game idle)", () => _p.StartNeutralCapture())));
            root.Children.Add(new TextBlock { Text = "Gains are only applied after the Verify test has passed for these exact settings (Setup step 6).", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });

            root.Children.Add(Header("SimHub gain-event mode"));
            root.Children.Add(Row("One SimHub gain step (%)", Num(s.SimHubGainStepPct, v => s.SimHubGainStepPct = v)));
            root.Children.Add(Row("Max steps down", IntBox(s.SimHubMaxSteps, v => s.SimHubMaxSteps = v)));

            root.Children.Add(Buttons(
                Btn("Save & apply", () => { _p.SaveSettings(); _p.ApplySettings(); _p.Note("Settings saved"); }),
                Btn("Reset trip", () => _p.Controller.ResetTrip()),
                Btn("Reset gains", () => _p.Controller.ResetGains())));

            // Tab 1: live visualizer (+ pop-out); Tab 2: the settings page above
            var liveGrid = new DockPanel { Margin = new Thickness(8) };
            var liveBar = Buttons(
                Btn("Pop out window", () => VisualizerWindow.ShowFor(_p)),
                Btn("Capture level (rig at centre)", () => _p.StartLevelCapture()),
                Btn("Reset trip", () => _p.Controller.ResetTrip()),
                Btn("Toggle adaptive", () => { s.AdaptiveEnabled = !s.AdaptiveEnabled; _p.Note("Adaptive " + (s.AdaptiveEnabled ? "ON" : "OFF")); }),
                _logBtnLive = Btn("Start log", ToggleLog));
            DockPanel.SetDock(liveBar, Dock.Top);
            liveGrid.Children.Add(liveBar);
            liveGrid.Children.Add(new MotionVisualizer(_p));

            var tabs = _tabs = new TabControl();
            bool firstRun = !s.SetupWizardSeen;
            var liveTab = new TabItem { Header = "Live view", Content = liveGrid };
            var setupTab = _setupTab = new TabItem { Header = "Setup", Content = BuildSetupTab() };
            _verifyTab = new TabItem { Header = "Verify", Content = BuildVerifyTab() };
            tabs.Items.Add(liveTab);
            tabs.Items.Add(setupTab);
            tabs.Items.Add(_verifyTab);
            tabs.Items.Add(new TabItem { Header = "Advanced", Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            tabs.Items.Add(new TabItem { Header = "Update", Content = BuildUpdateTab() });
            tabs.SelectedItem = firstRun ? setupTab : liveTab;
            Content = tabs;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += (o, e) => Refresh();
            Loaded += (o, e) => _timer.Start();
            Unloaded += (o, e) => _timer.Stop();
        }

        private void Refresh()
        {
            var st = _p.State;
            var g = _p.Controller.Snapshot;
            var xf = _p.Proxy.Transformer;
            string axes = xf == null ? "-" : string.Join(" ", xf.LastAxes.Select(v => v.ToString("F0", CultureInfo.InvariantCulture)));
            _live.Text = string.Format(CultureInfo.InvariantCulture,
                "Sensor   {0}  {1:F0} fps  crc-err {2}  {3}\n" +
                "Seat     pitch {4,6:F2}°  roll {5,6:F2}°   rate p {6,6:F1} r {7,6:F1} °/s\n" +
                "Cue (g)  surge {8,6:F3}  sway {9,6:F3}  heave {10,6:F3}   peak {11:F2}  vib {12:F3}\n" +
                "Levels   pitch {13:F2}  roll {14:F2}  heave {15:F2}  cue {16:F2}\n" +
                "Gains    global {17:F2}  pitch {18:F2}  roll {19:F2}  heave {20:F2}   [{21}]  impacts {22}\n" +
                "Tracking pitch {23} ms (r={24:F2}, {25:F1}°/unit)   roll {26} ms (r={27:F2})\n" +
                "Proxy    {28}  fwd {29}  axes [{30}]\n" +
                "Park     {31}  still {32}  ref {33}  last dev {34}°  {35}",
                _p.Imu.Connected ? "connected" : "NOT connected", _p.FrameRate, _p.Imu.Parser.CrcErrors, _p.DeviceInfo,
                st.PitchDeg, st.RollDeg, st.PitchRateDps, st.RollRateDps,
                st.CueSurgeG, st.CueSwayG, st.CueHeaveG, st.PeakDevG, st.VibRmsG,
                _p.Controller.LevelPitch, _p.Controller.LevelRoll, _p.Controller.LevelHeave, _p.Controller.LevelGlobal,
                g.Global, g.Pitch, g.Roll, g.Heave, g.Status, _p.Controller.ImpactCount,
                _p.PitchTracking.Valid ? _p.PitchTracking.DelayMs.ToString("F0") : "--", _p.PitchTracking.Coherence, _p.PitchTracking.Scale,
                _p.RollTracking.Valid ? _p.RollTracking.DelayMs.ToString("F0") : "--", _p.RollTracking.Coherence,
                _p.ProxyActive ? "APPLYING" : (_p.Proxy.Running ? "passthrough" : "off"), _p.Proxy.Forwarded, axes,
                _p.Park.State, (st.Flags & ImuFlags.Still) != 0 ? "yes" : "no",
                _p.Park.HasReference ? string.Format(CultureInfo.InvariantCulture, "{0:F2}/{1:F2}", _p.Park.RefPitchDeg, _p.Park.RefRollDeg) : "not learned",
                double.IsNaN(_p.Park.LastDeviationDeg) ? "--" : _p.Park.LastDeviationDeg.ToString("F2", CultureInfo.InvariantCulture),
                _p.Park.MountShifted ? "MOUNT SHIFTED" : "");
            _msg.Text = _p.LastMessage;
            RefreshUpdateTab();
            RefreshSetupAndVerify();

            var lg = _p.Logger;
            string lbl = lg.Active ? "Stop log" : "Start log";
            if (_logBtnLive != null && (string)_logBtnLive.Content != lbl) _logBtnLive.Content = lbl;
            if (_logBtnSettings != null && (string)_logBtnSettings.Content != lbl) _logBtnSettings.Content = lbl;
            _logStatus.Text = lg.Active
                ? string.Format(CultureInfo.InvariantCulture, "● Recording {0:F0} s, {1} rows → {2}", lg.Seconds(IO.ImuSerialLink.NowSec), lg.Rows, lg.FilePath)
                : (string.IsNullOrEmpty(lg.FilePath) ? "Not recording." : "Not recording. Last file: " + lg.FilePath);
        }

        // ---------------- Update tab ----------------------------------------------------------
        private TextBlock _uPluginInstalled, _uPluginLatest, _uFwInstalled, _uFwLatest, _uProgress, _uChecked;
        private TextBox _uNotes;
        private Button _uCheck, _uInstall, _uRollback, _uCloseSimHub, _uFlash, _uCancelFlash, _uReleasePage;

        private FrameworkElement BuildUpdateTab()
        {
            var s = _p.Settings;
            var r = new StackPanel { Margin = new Thickness(12) };
            Func<TextBlock> val = () => new TextBlock { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

            r.Children.Add(Header("Plugin (SimHub)"));
            r.Children.Add(Row("Installed version", _uPluginInstalled = val()));
            r.Children.Add(Row("Latest version", _uPluginLatest = val()));
            _uCheck = Btn("Check for updates", () => { _p.CheckForUpdates(); });
            _uInstall = Btn("Download and install", () =>
            {
                string err;
                if (_p.InstallPluginUpdate(false, out err)) _uCloseSimHub.Visibility = Visibility.Visible;
                else _p.Note("Plugin update: " + err);
            });
            _uRollback = Btn("Roll back to previous version", () =>
            {
                string err;
                if (_p.InstallPluginUpdate(true, out err)) _uCloseSimHub.Visibility = Visibility.Visible;
                else _p.Note("Roll back: " + err);
            });
            _uReleasePage = Btn("Open release page", () =>
            {
                var url = _p.Updater.Latest?.HtmlUrl;
                if (string.IsNullOrEmpty(url)) url = "https://github.com/" + s.UpdateRepo + "/releases";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            });
            r.Children.Add(Buttons(_uCheck, _uInstall, _uRollback, _uReleasePage));
            _uCloseSimHub = Btn("Close SimHub now (it restarts by itself)", () =>
            {
                _p.SaveSettings();
                Application.Current.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
            });
            // Sim-Spares green with carbon text, so the last step of an update stands out
            _uCloseSimHub.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA6, 0xFF, 0x4D));
            _uCloseSimHub.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0A, 0x0C, 0x0B));
            _uCloseSimHub.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x7A, 0x29));
            _uCloseSimHub.BorderThickness = new Thickness(2);
            _uCloseSimHub.FontWeight = FontWeights.Bold;
            _uCloseSimHub.Padding = new Thickness(14, 6, 14, 6);
            _uCloseSimHub.Visibility = Visibility.Collapsed;
            r.Children.Add(_uCloseSimHub);
            r.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 2, 0, 0),
                Text = "Install asks once for administrator permission (the plugin lives in the SimHub folder). The new DLL is copied " +
                       "after SimHub has closed, the old one is kept for 'Roll back', and SimHub starts again automatically." });

            r.Children.Add(Header("Firmware (IMU box)"));
            r.Children.Add(Row("Box firmware", _uFwInstalled = val()));
            r.Children.Add(Row("Latest firmware", _uFwLatest = val()));
            _uFlash = Btn("Update firmware", () => _p.StartFirmwareUpdate());
            _uCancelFlash = Btn("Cancel", () => _p.CancelFirmwareUpdate());
            r.Children.Add(Buttons(_uFlash, _uCancelFlash));
            r.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 2, 0, 0),
                Text = "Rig parked and still. From firmware 1.3.1 the box restarts into update mode by itself; older firmware: double-tap " +
                       "RST to GND when asked. The NICENANO drive appears briefly, the new firmware is copied and checked, and the box " +
                       "reconnects. Accelerometer calibration and gyro learning are kept." });

            r.Children.Add(Header("Status"));
            r.Children.Add(_uProgress = new TextBlock { TextWrapping = TextWrapping.Wrap });
            r.Children.Add(_uChecked = new TextBlock { Opacity = 0.75, Margin = new Thickness(0, 2, 0, 0) });

            r.Children.Add(Header("Release notes"));
            r.Children.Add(_uNotes = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                MinHeight = 120, MaxHeight = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            r.Children.Add(Header("Options"));
            var repo = Txt(s.UpdateRepo, v => s.UpdateRepo = v); repo.Width = 280;
            r.Children.Add(Row("GitHub repository (owner/name)", repo));
            r.Children.Add(Row("Check for updates when SimHub starts", Check(s.UpdateCheckOnStart, v => { s.UpdateCheckOnStart = v; _p.SaveSettings(); })));
            r.Children.Add(Row("Beta channel (test versions)", Check(s.UpdateIncludePrerelease, v => { s.UpdateIncludePrerelease = v; _p.SaveSettings(); })));
            return new ScrollViewer { Content = r, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private void RefreshUpdateTab()
        {
            if (_uPluginInstalled == null) return;
            var u = _p.Updater; var L = u.Latest;
            _uPluginInstalled.Text = IO.UpdateService.InstalledPluginVersion.ToString(3);
            _uPluginLatest.Text = L?.Plugin?.Version == null ? (u.LastCheck == DateTime.MinValue ? "not checked yet" : "-")
                : L.Plugin.Version + (_p.PluginUpdateAvailable ? "   ← update available" : "   (up to date)");
            var fw = _p.FirmwareVersion;
            _uFwInstalled.Text = fw == null ? (_p.Imu.Connected ? "connected, version unknown" : "box not connected") : fw.ToString();
            _uFwLatest.Text = L?.Firmware?.Version == null ? "-" : L.Firmware.Version + (_p.FirmwareUpdateAvailable ? "   ← update available" : (fw != null ? "   (up to date)" : ""));
            bool busy = u.Busy;
            _uCheck.IsEnabled = !busy;
            _uInstall.IsEnabled = !busy && _p.PluginUpdateAvailable;
            _uRollback.IsEnabled = !busy && IO.UpdateService.HasBackup;
            _uFlash.IsEnabled = !busy && L?.Firmware != null && !_p.FirmwareUpdating;
            _uFlash.Content = _p.FirmwareUpdateAvailable || fw == null ? "Update firmware" : "Re-install firmware";
            _uCancelFlash.Visibility = _p.FirmwareUpdating ? Visibility.Visible : Visibility.Collapsed;
            _uProgress.Text = string.IsNullOrEmpty(u.LastError) || busy ? u.Progress : "Last check failed: " + u.LastError;
            _uChecked.Text = u.LastCheck == DateTime.MinValue ? "" : "Last check " + u.LastCheck.ToString("HH:mm:ss") +
                (L != null ? "  ·  release " + L.Tag + (L.Published > DateTime.MinValue ? " (" + L.Published.ToString("yyyy-MM-dd") + ")" : "") : "");
            string notes = L?.Notes ?? "";
            if (_uNotes.Text != notes) _uNotes.Text = notes;
        }

        private void ToggleLog()
        {
            // take a folder typed but not yet confirmed (no focus change)
            if (_p.Logger.Active) _p.StopLog(); else { _p.SaveSettings(); _p.StartLog(); }
        }

        // ---------------- tiny UI helpers -----------------------------------------------------
        private static TextBlock Mono() => new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12 };
        private static TextBlock Header(string t) => new TextBlock { Text = t, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 4) };

        private static FrameworkElement Row(string label, FrameworkElement editor)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(editor, 1);
            g.Children.Add(l); g.Children.Add(editor);
            return g;
        }

        private static Panel Buttons(params Button[] bs)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var b in bs) w.Children.Add(b);
            return w;
        }

        private Button Btn(string text, Action a)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 4), Padding = new Thickness(10, 3, 10, 3) };
            b.Click += (o, e) => { try { a(); } catch (Exception ex) { _p.Note(ex.Message); } };
            return b;
        }

        private static TextBox Num(double v, Action<double> set)
        {
            var t = new TextBox { Width = 90, Text = v.ToString(CultureInfo.InvariantCulture) };
            t.LostFocus += (o, e) =>
            {
                if (double.TryParse(t.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) set(d);
            };
            return t;
        }

        private static TextBox IntBox(int v, Action<int> set)
        {
            var t = new TextBox { Width = 90, Text = v.ToString(CultureInfo.InvariantCulture) };
            t.LostFocus += (o, e) => { if (int.TryParse(t.Text, out var i)) set(i); };
            return t;
        }

        private static TextBox Txt(string v, Action<string> set)
        {
            var t = new TextBox { Width = 160, Text = v ?? "" };
            t.LostFocus += (o, e) => set(t.Text.Trim());
            return t;
        }

        private static CheckBox Check(bool v, Action<bool> set)
        {
            var c = new CheckBox { IsChecked = v, VerticalAlignment = VerticalAlignment.Center };
            c.Checked += (o, e) => set(true);
            c.Unchecked += (o, e) => set(false);
            return c;
        }

        private static ComboBox EnumBox<T>(T v, Action<T> set) where T : struct
        {
            var c = new ComboBox { Width = 180, ItemsSource = Enum.GetValues(typeof(T)), SelectedItem = v };
            c.SelectionChanged += (o, e) => { if (c.SelectedItem is T x) set(x); };
            return c;
        }

        private static void RefreshPorts(ComboBox c)
        {
            var current = c.Text;
            c.ItemsSource = SerialPort.GetPortNames().OrderBy(n => n).ToArray();
            c.Text = current;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
