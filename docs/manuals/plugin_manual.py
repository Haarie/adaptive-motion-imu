"""Adaptive Motion IMU - SimHub Plugin Manual (PDF)."""
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from mstyle import *
from diagrams import SystemDiagram

D = os.path.join(ROOT, "docs")
SC = os.path.join(D, "screens")
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(D, "manuals", "Adaptive_Motion_IMU_SimHub_Plugin_Manual.pdf")
cover_p = ParagraphStyle("cv", fontName="Body", fontSize=9.5, leading=14, textColor=colors.HexColor("#C9D0CB"))
cover_h = ParagraphStyle("cvh", fontName="RajSB", fontSize=12, leading=15, textColor=GREEN, spaceAfter=4)

story = [
    Paragraph("What the plugin does", cover_h),
    Paragraph("Adaptive Motion reads the IMU box on the motion platform and measures, live, what the rig really does. It checks "
              "that the rig follows SimHub's commands (verification), and once verified it can close the loop: it softens SimHub's "
              "motion when the platform nears the limits you set in SimHub, and releases it again when things are calm. "
              "It only ever reduces motion, never adds to it.", cover_p),
    Spacer(1, 6),
    Paragraph("This manual covers installing the plugin, the Setup wizard, the verification test, the nullCAT link, the live view, "
              "every setting, data logging, updates, and the properties, actions and events for dashboards.", cover_p),
]

body = [H1("Contents"), toc_block([
    ("How it works", "measure, verify, then close the loop"),
    ("Install", "first install; later updates come from the Update tab"),
    ("Setup", "the seven steps of the Setup tab"),
    ("Verify", "commanded vs measured, with the rig empty"),
    ("nullCAT link", "rig state, automatic unpark, keep-alive"),
    ("Level, driver offset and park check", "the zero points and how they are checked"),
    ("Live view", "every panel and badge"),
    ("Advanced settings", "full settings reference"),
    ("Data logging", "session CSV files and the level history"),
    ("Updates", "plugin and firmware from the Update tab"),
    ("Dashboards and controls", "properties, actions, events"),
    ("Troubleshooting", "common problems"),
    ("Safety", "what the plugin is and is not"),
])]

# 1
body += [PageBreak(), H1("1  How it works"),
    SystemDiagram(), Spacer(1, 4),
    P("SimHub sends its motion packets to the plugin (127.0.0.1) instead of straight to the controller; the plugin forwards them "
      "to nullCAT. Every 4 ms the IMU box reports the platform's tilt, rotation rates, the force felt in the seat (cue), vibration and "
      "impacts. The plugin uses that in three stages:"),
    table([["Stage", "What happens"],
           ["1  Measure", "Always. The live view shows what the rig does; packets pass through unchanged."],
           ["2  Verify", "A short automatic test with the rig empty: the plugin drives the rig to known poses and compares commanded "
            "with measured tilt. Closed loop stays locked until it passes, and locks again when a relevant setting changes."],
           ["3  Closed loop", "The plugin compares the measured motion with an <b>envelope</b> (SimHub's pitch/roll limits plus rate, "
            "cue, heave and vibration limits) and turns four gains down where needed: <b>Global</b>, <b>Pitch</b>, <b>Roll</b>, <b>Heave</b>."]],
          [0.2, 0.8]),
    table([["Behaviour", "Default", "What it means"],
           ["Limits", "SimHub's", "Pitch and roll limits follow SimHub's motion limits (Axis limiting)"],
           ["Knee", "0.85", "Gains start dropping when a quantity passes 85 % of its limit"],
           ["Attack / Release", "0.15 s / 4 s", "How fast a gain comes down / how slowly it comes back"],
           ["Impact", "1.5 g", "A knock above this (end stop, kerb) takes 10 % off Global for 2 s"],
           ["Hard trip", "1.2 × limit + 1°", "Pitch or roll beyond this drops all gains to the minimum; always below the rig's mechanical maximum"],
           ["Sensor lost", "0.6", "If frames stop, gains ramp to this value (never up while blind)"]],
          [0.18, 0.2, 0.62]),
    callout("note", "Other modes", "Advanced &gt; Mode also offers <b>MonitorOnly</b> (measure only, no packet path) and "
            "<b>SimHubGainEvents</b> (no proxy: the plugin raises GainDownRequest / GainUpRequest events that you map to SimHub's "
            "global motion gain; coarse, global only). The Setup wizard uses the UDP proxy."),
]

# 2
body += [PageBreak(), H1("2  Install"),
    P("Install once on the PC where SimHub runs. You need the <b>.NET SDK</b> (any version 6 to 10; check with "
      "<font name='Mono'>dotnet --list-sdks</font>). Later versions are installed from the plugin's own <b>Update</b> tab (section 10)."),
    steps(["Unpack the project (amimu-src.zip) into a folder, for example <b>Documents\\Adaptive Motion IMU</b>.",
           "Open PowerShell in that folder and run the two commands below the steps.",
           "Close SimHub when the script asks (also from the tray icon), and allow the administrator prompt: the DLL is copied into "
           "<b>C:\\Program Files (x86)\\SimHub</b>.",
           "Start SimHub. It asks to enable the new plugin <b>Adaptive Motion (IMU)</b>: answer yes.",
           "The left menu now has <b>Adaptive Motion</b> (closed-loop icon) with the tabs <b>Live view</b>, <b>Setup</b>, <b>Verify</b>, "
           "<b>Advanced</b> and <b>Update</b>. On the first start the Setup tab opens."]),
    code("Expand-Archive amimu-src.zip -DestinationPath . -Force\n"
         "powershell -ExecutionPolicy Bypass -File AdaptiveMotionIMU-src\\plugin\\install.ps1"),
    callout("note", "Other SimHub folder", "Add <font name='Mono'>-SimHubDir \"D:\\SimHub\"</font> to the install command."),
]

# 3 Setup
body += [PageBreak(), H1("3  Setup"),
    P("The Setup tab walks through everything in order. Each step shows a status: <b>✓</b> green = done, <b>●</b> amber = to do "
      "(with what is missing), <b>✗</b> red = a problem (with the reason). Work from the top; later steps depend on earlier ones."),
    table([["Step", "What to do", "Green when"],
           ["1  Connect the IMU box", "Choose the box's <b>data</b> COM port (the one that answers; the other is a maintenance port), press Connect.",
            "Frames arrive (about 250 per second)."],
           ["2  Mounting direction", "Mounted as in the build manual (side mount, lid to the front): leave the defaults and press Save mounting. "
            "Box at another angle: Auto-align 1 (rig active at its centre, not parked, still), then Auto-align 2 (rig held nose-up ≥ 2°).",
            "Mounting saved or auto-aligned."],
           ["3  Rig dimensions and limits", "Press <b>Import from SimHub</b>: rig type, length, width, stroke, pitch/roll limits and SimHub's "
            "UDP output address are read from SimHub's motion settings.",
            "Geometry and limits complete."],
           ["4  Route SimHub's motion output", "Press <b>Use UDP proxy and save</b>. The plugin points SimHub at 127.0.0.1:33740 and forwards to "
            "the controller address SimHub used before. Start SimHub's motion test briefly so packets arrive.",
            "Packets arrive and match the packet format."],
           ["5  Neutral, level, driver offset", "<b>Use centre for neutral</b>, then <b>Capture level at centre (rig EMPTY)</b>; optionally "
            "<b>Capture driver offset at centre</b> with you in the seat (section 6). The line <b>Rig: ACTIVE / PARKED</b> shows nullCAT's state.",
            "Neutral and level set."],
           ["6  Verify", "Run the test on the Verify tab (section 4).", "The last test passed with the current settings."],
           ["7  Closed loop", "Tick <b>Closed loop enabled</b>.", "The plugin is applying gains (live view: APPLYING)."]],
          [0.22, 0.53, 0.25]),
    H2("Step 4: packet format"),
    P("The packet format is how SimHub writes the actuator positions into each UDP packet: its <i>update command</i>. For nullCAT this "
      "is a text line such as <font name='Mono'>NULLCAT,&lt;Axis1&gt;,…,&lt;Axis10&gt;</font> with 16-bit values (0…65535). "
      "Import from SimHub sets it; <b>Detect format</b> reads it from a packet that arrived. If SimHub leaves a placeholder unfilled "
      "(for example <font name='Mono'>&lt;Axis10&gt;</font> on a 4-actuator rig) the plugin passes it through unchanged."),
    callout("note", "After changing SimHub's motion settings", "SimHub only writes its motion settings when it closes. After changing the "
            "rig size, stroke or limits in SimHub: restart SimHub, then press <b>Import from SimHub</b> again, and run the verification again."),
    H2("Step 5: centre capture"),
    P("<b>Capture level at centre</b> and <b>Capture driver offset at centre</b> open a safety dialog with tick boxes. Then the plugin "
      "brings the rig out of park to its centre (<b>rig active at its centre, not parked</b>: the position SimHub uses for zero), waits for "
      "the controller to come online (6 s) and for the rig to stand still (1.5 s), captures for 1.5 s, holds at centre for 5 s and hands "
      "the rig back. If nullCAT is parked, tick <b>Unpark nullCAT automatically</b> or unpark it yourself first (section 5). "
      "<b>Stop</b> ends a capture without saving."),
]

# 4 Verify
body += [PageBreak(), H1("4  Verify"),
    callout("warn", "Nobody in the rig during the test", "The test moves the rig on its own and does not react to a person in the seat. "
            "The seat is empty, nobody is within reach of the moving parts, and the e-stop is within your reach. Stop returns the rig "
            "to centre; use the e-stop if anything looks wrong."),
    P("The test drives the rig itself through the proxy (SimHub's packets are held back meanwhile) and compares each commanded pose with "
      "what the IMU measures: zero, then pitch at ±¼, ±½, ±¾ and the full pose, then roll the same way."),
    steps(["Close the game. SimHub may stay open, but must not be sending motion.",
           "Choose the range with the button: <b>HALF MOTION 50 %</b> (green) or <b>FULL MOTION 100 %</b> (orange) of SimHub's limits. "
           "Every SimHub start begins in half motion: run half first, then full.",
           "Press <b>Run test</b>. If something is missing, a window lists it. Otherwise the start dialog shows pitch, roll and speed and "
           "the safety check: tick all three boxes, then <b>START TEST</b>.",
           "The rig unparks (automatically if ticked), waits 6 s for the controller, then moves at 4°/s through the poses. "
           "Each pose: 1.5 s to settle, 1 s to measure.",
           "Afterwards the rig holds at centre for 5 s (<i>Hold at centre after test</i>), then moves to its PARK position: SimHub sends no "
           "motion while idle, so the controller parks the rig. Stay clear until it has stopped moving."]),
    figure(os.path.join(D, "verify_preview_pass.png"), 0.92, "Verify tab after a passed test: result, scale, cross-talk, zero and each pose with its commanded mark.", max_h=96 * mm),
    table([["Check", "Pass (green)", "Close (amber)"],
           ["Each pose", "within max(5 %, 0.3°)", "within max(10 %, 0.6°)"],
           ["Cross-talk (the other axis)", "≤ 0.5°", "-"],
           ["Aborts", "moving the wrong way by 2°, or 3° past the limit: the test stops and returns to centre", ""]],
          [0.3, 0.38, 0.32]),
    P("The findings explain a failure in plain words, for example <i>Pitch is off by a factor of about 0.5: check the ball-screw lead / "
      "steps per mm in the controller and the stroke setting in SimHub</i>. Reports are saved as "
      "<font name='Mono'>Documents\\Adaptive Motion IMU\\verification\\verify_*.txt</font> and <font name='Mono'>.csv</font> "
      "(<b>Open report folder</b>)."),
    callout("note", "When the test must run again", "A pass is tied to a fingerprint of the relevant settings: rig geometry, limits, neutral, "
            "level, mounting, packet format, controller address. Change one and closed loop locks until the test passes again. "
            "The driver offset and the test options are not part of it."),
]

# 5 nullCAT
body += [PageBreak(), H1("5  nullCAT link"),
    P("Besides the UDP motion packets (port 4444), the plugin talks to nullCAT's web API (port 8080, the same as the web interface and "
      "the Stream Deck plugin). It uses the controller address from step 4 unless Advanced sets another web host."),
    table([["Function", "What it does"],
           ["Rig state (always, read-only)", "Reads /api/status every 2 s. Shown in Setup step 5 (<b>Rig: ACTIVE (not parked)</b> green, "
            "<b>Rig: PARKED</b> amber, e-stop / homing / needs homing red, state unknown when not reachable), on the Verify tab and as a "
            "badge in the live view. A test or centre capture does not start while nullCAT is parked, e-stopped or homing."],
           ["Unpark automatically (optional, off)", "Tick box in Setup step 5 and on the Verify tab. When nullCAT is parked, the plugin sends "
            "/api/unpark itself, but only after you confirmed the safety dialog. nullCAT still refuses it during an e-stop or before homing."],
           ["Keep-alive", "While the plugin drives the rig (test, centre capture) it alternates the command by one count (about 3 µm) per packet: "
            "nullCAT only unparks and stays online on changing data, a constant pose counts as stale."]],
          [0.3, 0.7]),
    callout("note", "How nullCAT parks", ["nullCAT parks the actuators near their end stops after 15 s without new data "
            "(SimHub sends nothing while idle), and only unparks on an operator action: web interface, Stream Deck, run button, or the "
            "plugin's automatic unpark.",
            "Homing is very repeatable (end stop found within about 0.2 mm, 0.01° of tilt), so a parked rig is a good reference: "
            "the park check uses it (section 6)."]),
]

# 6 level
body += [PageBreak(), H1("6  Level, driver offset and park check"),
    table([["Zero point", "Captured how", "Used for"],
           ["Neutral", "Use centre (middle of the output range: 32768 for 16 bit). No movement, rig state does not matter.",
            "The pose the plugin drives to for centre, test and capture."],
           ["Level", "Capture level at centre, rig EMPTY.", "Zero for all measured tilt; reference for the verification test."],
           ["Driver offset", "Capture driver offset at centre, you in your driving position.",
            "Subtracted only while a game runs, so your driving position reads 0°. On the test rig it measured 0.0-0.1°: optional."],
           ["Park pose", "Learned automatically the first time the rig is parked after a level capture (not right after a session).",
            "Every later park is compared with it."]],
          [0.16, 0.42, 0.42]),
    H2("Park check and MOUNT SHIFTED"),
    P("Whenever nothing commands motion (no SimHub packets, no test or capture) and the rig stands still for 2 s, the plugin compares the "
      "pose with the learned park pose (and with park pose + driver offset). More than <b>0.5°</b> off both shows <b>MOUNT SHIFTED</b> "
      "in the live view and in the SimHub log. It is a warning only: it does not change the motion."),
    steps(["Check the IMU box and its bracket for play, and that it is fixed to the platform (not the seat).",
           "Capture the level at centre again, then run the verification test (half, then full).",
           "If it keeps returning after power-up without anything having moved, look at the level history below."]),
    H2("Level history and temperature"),
    P("The plugin reads the MPU-6050's temperature every 10 s (firmware 1.3 and later). Every level capture, park check and learned park "
      "pose is appended to <font name='Mono'>Documents\\Adaptive Motion IMU\\level_history.csv</font> with the raw sensor tilt and the "
      "temperature. A shift that follows temperature is the sensor; one that doesn't is mechanical. At start the SimHub log also shows "
      "whether an accelerometer calibration is stored in the box (<font name='Mono'>ACC o=… s=…</font>; build manual 7.3)."),
]

# 7 live view
body += [PageBreak(), H1("7  Live view"),
    figure(os.path.join(D, "visualizer_preview.png"), 1.0, "Live view (preview render; on the rig the first badge shows ACTIVE or PARKED)."),
    table([["Panel", "What it shows"],
           ["Header badges", "Rig state from nullCAT (ACTIVE, PARKED, E-STOP, HOMING, NOT HOMED, LOOP OFF, RIG ?), controller status "
            "(IDLE: CAPTURE LEVEL, OK, COMPRESSING, IMPACTHOLD, SENSORLOST, TRIPPED), mode (MONITOR ONLY / PASSTHROUGH / APPLYING), "
            "frame rate, STILL, MOUNT SHIFTED, CLONE IMU, ● REC, UPDATE."],
           ["Attitude", "Artificial horizon of the platform: pitch ladder and roll scale. Yellow dashes = limit, red = hard trip."],
           ["Horizontal cue", "The force felt in the seat (surge, sway) with a 2 s trail. Dashed ring = horizontal cue limit."],
           ["Rates, heave, vibration, impacts", "Pitch and roll rate, heave cue, vibration RMS, peak |a|-1 g and the impact count."],
           ["Gains", "The four gains applied right now. The red line is the minimum gain."],
           ["Last 10 s", "Pitch (blue) and roll (orange) against the limit and trip lines."],
           ["Buttons", "POP OUT WINDOW, CAPTURE LEVEL, RESET TRIP, TOGGLE ADAPTIVE, START/STOP LOG."]],
          [0.24, 0.76]),
]

# 8 advanced
def sect(title, rows):
    return [H3(title), table([["Setting", "Default", "Meaning"]] + rows, [0.3, 0.15, 0.55])]

body += [PageBreak(), H1("8  Advanced settings")]
body += sect("Sensor", [
    ["COM port", "-", "The box's data port."],
    ["Use auto-aligned mounting", "off", "Use the axes found by Auto-align instead of the manual pickers."],
    ["Sensor axis forward / up", "+Z / +X", "Manual mounting; right for the side mount as built."],
    ["Device DLPF / output divider", "3 / 2", "Sensor low-pass (3 = 44 Hz, 4.9 ms) / frame rate 500/n Hz."],
    ["Learn gyro bias whenever still", "on", "Firmware refines the gyro zero point whenever the rig is still for 2 s."],
    ["Park check tolerance", "0.5°", "How far a park may differ before MOUNT SHIFTED."],
    ["Buttons", "", "Capture level here (rig active at its centre, not parked), Re-learn parked pose, Show gyro bias, Gyro calibrate, "
     "Send DLPF/ODR to device, Ping. Prefer the centre capture in Setup step 5."],
])
body += sect("What the plugin is allowed to do", [
    ["Mode", "MonitorOnly", "MonitorOnly, UdpProxy or SimHubGainEvents. Setup step 4 sets UdpProxy."],
    ["Adaptive enabled", "on", "Closed loop on/off; only takes effect once the test has passed (same as Setup step 7 and the TOGGLE ADAPTIVE button)."],
])
body += sect("Envelope", [
    ["Pitch/roll limits follow SimHub", "on", "Limits = SimHub's motion limits, set at every start and import. Typing a limit turns it off."],
    ["Pitch / roll limit", "SimHub's", "Tilt at which that axis is at 100 %."],
    ["Angular rate / heave cue / horizontal cue", "60°/s / 0.6 g / 0.8 g", "Rate and force limits measured at the seat."],
    ["Vibration RMS limit", "0.35 g", "Above this the Global gain comes down."],
    ["Knee, attack, release, minimum gain", "0.85, 0.15 s, 4 s, 0.3", "Compressor behaviour (section 1)."],
    ["Impact threshold", "1.5 g", "Peak |a|-1 g that counts as an impact."],
    ["Hard pitch / roll trip", "auto", "1.2 × limit + 1°, capped 0.3° below the rig's mechanical maximum."],
    ["Trip auto-reset / failsafe gain", "off / 0.6", "Clear a trip automatically / gain when frames stop."],
])
body += sect("UDP proxy and nullCAT", [
    ["Listen port", "33740", "SimHub's motion output points at 127.0.0.1 on this port."],
    ["Controller host / port", "from SimHub", "Where the packets go (nullCAT: 192.168.50.1 / 4444 on the test rig)."],
    ["nullCAT web API: read status", "on", "Rig state from /api/status."],
    ["nullCAT web host / port", "empty / 8080", "Empty = the controller host. Use nullcat.local if the IP is not accepted."],
    ["Unpark nullCAT automatically", "off", "Section 5."],
    ["Packet format", "from SimHub", "Text command (prefix, hexadecimal) or binary (header bytes, axis count, bytes per axis, byte order, max value, trailer)."],
    ["Actuator layout", "from SimHub", "GlobalOnly, TwoLeftRight, ThreeFrontPairRear or FourCorner."],
])
body += sect("Data logging and gain events", [
    ["Save logs in folder", "Documents\\Adaptive Motion IMU\\logs", "Browse… picks another folder."],
    ["Rows per second", "100", "1-250."],
    ["Start/stop automatically with the game", "off", "A new file for every session."],
    ["Include game data", "on", "Speed, gear, rpm and the game's accelerations."],
    ["One SimHub gain step / max steps down", "5 % / 8", "Only for SimHubGainEvents mode."],
])

# 9 logging
body += [PageBreak(), H1("9  Data logging"),
    P("The plugin records what it measures and does to a CSV file, for checking a session, tuning limits, or comparing the seat with "
      "what SimHub commanded. Start with START LOG, the action LogToggle, or automatically with the game. The live view shows ● REC."),
    P("File name: <font name='Mono'>AMIMU_2026-11-21_21-19-41_AssettoCorsa_Lotus_Evora_GTC_zolder_pascalwb.csv</font> "
      "(date, time, game, car, track). The first lines start with <b>#</b>: firmware, mode, level, driver offset and all limits."),
    table([["Columns", "Content"],
           ["time_s, device_us", "Seconds since the start; the box's microsecond clock"],
           ["pitch_deg, roll_deg, *_rate_dps", "Platform tilt (driver offset applied while the game runs) and rotation rates"],
           ["cue_*_g, lin_*_g", "Force felt in the seat and the platform's own acceleration"],
           ["peak_g, vib_rms_g, flags", "Impact peak, vibration, sensor flags"],
           ["gain_*, status, level_*", "Applied gains, controller state, fraction of each limit in use"],
           ["applying, cmd_pitch, cmd_roll, cmd_pitch_deg, cmd_roll_deg", "Proxy applying; SimHub's command (normalised and in degrees)"],
           ["game_running, speed_kmh, gear, rpm, game_accel_*", "Game data (when enabled)"],
           ["imu_temp_c", "MPU-6050 temperature (firmware 1.3 and later)"],
           ["event", "Rows with only time_s and event: TRIP, IMPACT, test start/end, unpark, messages"]],
          [0.42, 0.58], mono_cols=(0,)),
    P("Next to the logs: <font name='Mono'>verification\\</font> (test reports) and <font name='Mono'>level_history.csv</font> (section 6)."),
    callout("tip", "Send it over", "A log of a few laps with hard braking and kerbs, with closed loop off and then on, shows how close the "
            "rig gets to its limits per corner and how much the plugin softened."),
]

# 10 updates
body += [PageBreak(), H1("10  Updates"),
    P("The <b>Update</b> tab shows the installed and latest versions of the plugin and of the box's firmware, and installs both. "
      "Updates come from the project's GitHub repository; every file is checked against its SHA-256 checksum."),
    table([["Item", "What it shows / does"],
           ["Check now", "Asks GitHub for the latest versions (also 15 s after SimHub starts, if enabled)."],
           ["Download and install", "Downloads and verifies the plugin, asks once for administrator permission, then <b>Close SimHub now</b> "
            "(green button): SimHub closes, the file is replaced and SimHub starts again."],
           ["Roll back", "Puts the previous plugin file back (same restart)."],
           ["Update firmware", "Rig parked and still. The box restarts into update mode (firmware 1.3.1 and newer), the plugin copies the "
            "firmware and confirms the new version. Calibration is kept."],
           ["Beta channel", "Test versions. The stable channel gets a version once it has proven itself on the beta channel."]],
          [0.3, 0.7]),
    callout("warn", "Firmware updates", "Never unplug the box while the NICENANO drive is visible. If an update is interrupted, "
            "double-tap RST to GND and press Update firmware again: the bootloader itself is never touched."),
]

# 11 dashboards
body += [PageBreak(), H1("11  Dashboards and controls"),
    P("All values are SimHub properties under <font name='Mono'>AdaptiveMotionPlugin.</font>"),
    table([["Property", "Meaning"],
           ["Sensor.Connected / FrameRate / CrcErrors / Still / CloneDie / Info", "Link and sensor state"],
           ["Seat.PitchDeg / RollDeg / PitchRateDps / RollRateDps", "Platform tilt and rotation rates"],
           ["Seat.CueSurgeG / CueSwayG / CueHeaveG / PeakG / VibrationRmsG", "Forces, impact peak, vibration"],
           ["Command.PitchDeg / RollDeg", "What SimHub commands, in degrees"],
           ["Gain.Global / Pitch / Roll / Heave", "Current gains"],
           ["Status / Applying / Tripped / ImpactCount", "Controller state"],
           ["Level.Pitch / Roll / Heave / Cue", "Fraction of each limit in use"],
           ["Verify.Valid / Running / Result", "Verification state"],
           ["Tracking.PitchDelayMs / PitchCoherence / RollDelayMs ...", "Delay between command and seat"],
           ["Park.State / ReferenceLearned / DeviationDeg / MountShifted", "Park check"],
           ["Proxy.Active / Proxy.Forwarded, Mount.Auto, LastMessage", "Proxy, mounting, last message"],
           ["Log.Active / File / Rows / Seconds", "Recording"],
           ["Update.* (InstalledVersion, Available, FirmwareAvailable ...)", "Update state"]],
          [0.55, 0.45], mono_cols=(0,)),
    H2("Actions"),
    table([["Action", "Does"],
           ["ToggleAdaptive", "Closed loop on/off"],
           ["CaptureLevelAtCentre / CaptureDriverOffset", "Centre capture (the plugin moves the rig; the dialog is skipped, so map with care) / driver offset"],
           ["CaptureLevel / CaptureNeutral", "Capture here / neutral from SimHub"],
           ["VerifyStart / VerifyStop", "Start (rig must be empty) / stop the verification test"],
           ["ResetTrip / ResetGains / RelearnParkPose", "Clear a trip / gains to 1.0 / new park reference"],
           ["AutoAlignRest / AutoAlignNoseUp, GyroCalibrate / ShowGyroBias", "Mounting and gyro"],
           ["LogStart / LogStop / LogToggle", "CSV recording"]],
          [0.48, 0.52], mono_cols=(0,)),
    H2("Events"),
    table([["Event", "Raised when"],
           ["Trip / Impact", "Hard limit exceeded / a knock above the impact threshold"],
           ["MountShifted / ParkChecked", "Park pose differs / a park check finished"],
           ["GainDownRequest / GainUpRequest", "Gain-event mode wants one step down / up"]],
          [0.48, 0.52], mono_cols=(0,)),
]

# 12 troubleshooting
body += [PageBreak(), H1("12  Troubleshooting"),
    table([["Symptom", "Cause / fix"],
           ["Plugin not in the left menu", "Re-run install.ps1 with SimHub closed, then accept 'enable plugin' at start-up."],
           ["Step 1 not green", "Wrong COM port (try the other one), or another program has the port open."],
           ["Step 4: packets arrive but don't match", "Press Detect format, or Import from SimHub again. The step shows what was expected and what arrived."],
           ["Step 4: no packets", "SimHub only sends while a game or the motion test runs: start the motion test briefly."],
           ["Test or capture does not start", "A window lists why: nullCAT parked (unpark, or tick Unpark automatically), SimHub still sending, level missing…"],
           ["Rig: state unknown / RIG ?", "nullCAT's web API not reachable: check the address, or enter nullcat.local as web host (Advanced)."],
           ["Test: off by a factor of about 0.5 or 2", "Ball-screw lead / steps per mm in the controller, or the stroke in SimHub."],
           ["Test: moves the wrong way", "Actuator order or direction differs between SimHub and the controller."],
           ["TRIP right after raising SimHub's limits", "Restart SimHub and Import from SimHub so the plugin's limits follow; then verify again."],
           ["MOUNT SHIFTED", "Section 6: check the box, capture the level at centre, verify again; watch level_history.csv."],
           ["Closed loop has no noticeable effect", "Expected when the rig stays below 85 % of the limits. SimHub's own soft limiter may already soften the peaks."],
           ["Header never shows APPLYING", "Setup step 7 explains which step is missing (verification, level, proxy)."],
           ["SENSORLOST", "USB cable or hub: use a direct port and a shielded cable away from motor wiring."],
           ["Update: administrator permission refused", "Accept the Windows prompt, or run install.ps1."]],
          [0.33, 0.67]),
    H1("13  Safety"),
    callout("warn", "This is not a safety system", [
        "In closed loop the plugin only ever <b>attenuates</b>: gains stay between the minimum gain and 1.0, and it never moves an actuator "
        "further from neutral than SimHub's own packet did.",
        "During the verification test and centre captures the plugin <b>drives the rig itself</b>: nobody in the seat for the test, "
        "nobody within reach, e-stop within reach.",
        "It runs on a Windows PC and can stop or crash. Keep the <b>hardware e-stop</b> within reach and nullCAT's own limits and end-stop "
        "protection configured; nullCAT parks the rig when packets stop."]),
]

doc = ManualDoc(OUT, "Adaptive Motion IMU\nSimHub Plugin", "Setup, verification, closed loop, nullCAT",
                "Plugin manual v2.0 · plugin 1.3.21 · firmware 1.3.1 · November 2026", "Sim racing motion · closed loop")
doc.build(story + start(body))
print("wrote", OUT)
