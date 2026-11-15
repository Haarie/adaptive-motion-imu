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
    Paragraph("Adaptive Motion reads the IMU box on the seat and shows, live, what the rig really does. Once commissioned it "
              "acts as a <b>motion compressor</b>: it turns SimHub's motion down when the seat nears the limits you set, and "
              "releases it again when things are calm. It only ever reduces motion, never adds to it.", cover_p),
    Spacer(1, 6),
    Paragraph("This manual covers installing the plugin, the live view, every setting, commissioning on the rig, and the "
              "properties, actions and events you can use in dashboards and Controls &amp; events.", cover_p),
]

body = [H1("Contents"), toc_block([
    ("How it works", "the closed loop and the three modes"),
    ("Install", ".NET SDK, install.ps1, enabling the plugin"),
    ("First connection", "COM port, checking the sensor"),
    ("Live view", "every panel explained"),
    ("Settings reference", "sensor, envelope, proxy, gain events"),
    ("Commissioning on the rig", "level, park pose, monitor laps, limits"),
    ("Applying gains", "UDP proxy step by step, or SimHub gain events"),
    ("Data logging", "record sessions to CSV in a folder of your choice"),
    ("Updates", "plugin and firmware updates from the Update tab"),
    ("Dashboards and controls", "properties, actions, events"),
    ("Troubleshooting", "common problems"),
    ("Safety", "what the plugin is and is not"),
])]

# 1
body += [PageBreak(), H1("1  How it works"),
    SystemDiagram(), Spacer(1, 4),
    P("Every 4 ms the IMU box reports the seat's tilt, rotation rates, the force you feel (cue), vibration and impacts. The plugin "
      "compares those with an <b>envelope</b> (limits you set) and computes four gains between the minimum gain and 1.0: "
      "<b>Global</b>, <b>Pitch</b>, <b>Roll</b> and <b>Heave</b>."),
    table([["Behaviour", "Default", "What it means"],
           ["Knee", "0.85", "Gains start dropping when a quantity passes 85 % of its limit"],
           ["Attack", "0.15 s", "How fast a gain comes down"],
           ["Release", "4 s", "How slowly it comes back up once things are calm"],
           ["Impact", "1.5 g", "A knock above this (end stop, kerb) takes 10 % off Global for 2 s"],
           ["Hard trip", "14 deg", "Pitch or roll beyond this drops all gains to the minimum until it is reset"],
           ["Sensor lost", "0.6", "If frames stop, gains ramp to this value (never up while blind)"]],
          [0.18, 0.14, 0.68]),
    H2("Three modes"),
    table([["Mode", "What happens"],
           ["MonitorOnly (default)", "Measures and shows everything, publishes properties. Changes nothing. Use this first."],
           ["UdpProxy (recommended)", "SimHub sends its motion packets to the plugin (127.0.0.1); the plugin rescales each actuator around "
            "neutral by the gains and forwards the packet to the motion controller (nullCAT). Fine-grained per axis."],
           ["SimHubGainEvents (coarse)", "No packet path change: the plugin raises GainDownRequest / GainUpRequest events that you map to "
            "SimHub's own Motion gain controls. Global gain only, in steps."]],
          [0.28, 0.72]),
]

# 2
body += [PageBreak(), H1("2  Install"),
    P("Install on the PC where SimHub runs. You need the <b>.NET SDK</b> (any version 6 to 10; check with <font name='Mono'>dotnet --list-sdks</font>). "
      "The .NET Framework 4.8 parts are downloaded automatically during the first build."),
    steps(["Unpack the project (amimu-src.zip) into a folder, for example <b>Documents\\Adaptive Motion IMU</b>.",
           "Open PowerShell in that folder and run the two commands shown below the steps.",
           "Close SimHub when the script asks (also from the tray icon), and allow the administrator prompt: the DLL is copied into "
           "<b>C:\\Program Files (x86)\\SimHub</b>.",
           "Start SimHub. It asks to enable the new plugin <b>Adaptive Motion (IMU)</b>: answer yes.",
           "The left menu now has <b>Adaptive Motion</b> with two tabs: <b>Live view</b> and <b>Settings</b>."]),
    code("Expand-Archive amimu-src.zip -DestinationPath . -Force\n"
         "powershell -ExecutionPolicy Bypass -File AdaptiveMotionIMU-src\\plugin\\install.ps1"),
    callout("note", "Other SimHub folder", "Add <font name='Mono'>-SimHubDir \"D:\\SimHub\"</font> to the install command. "
            "To update the plugin later, run the same two lines with the new zip."),
    H1("3  First connection"),
    steps(["Open <b>Adaptive Motion &gt; Settings</b>.",
           "Set <b>COM port</b> to the box's data port. The board shows two COM ports; the data port is the one that "
           "answers (COM16 on the bench PC), the other is a maintenance port.",
           "The <b>Live</b> block at the top should read <b>connected, ~250 fps, crc-err 0</b>, and a line like "
           "<font name='Mono'>Device: INFO fw=1.3.0 who=0x68 ...</font>.",
           "Mode stays <b>MonitorOnly</b> until the rig is commissioned (section 6)."]),
    figure(os.path.join(SC, "simhub_settings_1.png"), 0.7, "Settings tab: Live block, sensor and calibration buttons, mode and envelope.", max_h=78 * mm),
]

# 4
body += [PageBreak(), H1("4  Live view"),
    figure(os.path.join(SC, "simhub_liveview.png"), 1.0, "Live view on the bench (box on the desk, level not captured yet)."),
    table([["Panel", "What it shows"],
           ["Header badges", "Controller status (IDLE: CAPTURE LEVEL, OK, COMPRESSING, IMPACTHOLD, SENSORLOST, TRIPPED), mode "
            "(MONITOR ONLY / PASSTHROUGH / APPLYING), frame rate, STILL, MOUNT SHIFTED, CLONE IMU."],
           ["Attitude", "Artificial horizon of the seat: pitch ladder and roll scale. Yellow dashes = your pitch/roll limit, red = hard trip."],
           ["Horizontal cue", "The force felt in the seat (surge up/down, sway left/right) with a 2 s trail. Dashed ring = horizontal cue limit."],
           ["Rates, heave, vibration, impacts", "Pitch and roll rate, heave cue, vibration RMS and the peak |a|-1 g per frame with the impact count. "
            "Ticks mark the limits."],
           ["Gains", "The four gains the controller applies right now. The red line is the minimum gain."],
           ["Last 10 s", "Pitch (blue) and roll (orange) against the limit (yellow) and trip (red) lines."],
           ["Buttons", "POP OUT WINDOW (e.g. for a second screen), CAPTURE LEVEL, RESET TRIP, TOGGLE ADAPTIVE."]],
          [0.24, 0.76]),
]

# 5 settings reference
def sect(title, rows):
    return [H3(title), table([["Setting", "Default", "Meaning"]] + rows, [0.3, 0.13, 0.57])]

body += [PageBreak(), H1("5  Settings reference")]
body += sect("Sensor (Arduino + MPU-6050)", [
    ["COM port", "-", "The box's data port."],
    ["Use auto-aligned mounting", "off", "Use the axes found by Auto-align instead of the two manual pickers below."],
    ["Auto-align 1: rest", "button", "Rig at centre, still: captures 'down'."],
    ["Auto-align 2: nose-up held", "button", "Rig pitched nose-up 2 deg or more and held (pure pitch): captures 'forward'. Sets level too."],
    ["Sensor axis forward / up", "+Z / +X", "Manual mounting: which sensor axis points to the front of the rig and which points up. Right for the side mount as built."],
    ["Device DLPF (0-6)", "3", "Sensor low-pass: 3 = 44 Hz, 4.9 ms delay. Lower = faster but noisier."],
    ["Device output divider", "2", "Frame rate = 500 / n Hz (2 = 250 Hz)."],
    ["Learn gyro bias whenever still", "on", "Firmware refines the gyro zero point whenever the rig is still for 2 s."],
    ["Park check tolerance", "0.5 deg", "How far the parked pose may differ from the learned one before MOUNT SHIFTED."],
])
body += [table([["Button", "Use"],
                ["Capture level (rig at centre)", "Rig powered, at its centre position, no game: stores the zero for pitch, roll and heave. Required before the controller runs."],
                ["Re-learn parked pose", "After (re-)mounting the box: the next park becomes the new reference."],
                ["Show gyro bias", "Prints the firmware's current gyro zero point in the Live block."],
                ["Gyro calibrate (rig still)", "Forces a 2 s gyro calibration. Normally not needed (automatic)."],
                ["Send DLPF/ODR to device", "Writes the two device settings to the box (saved there)."],
                ["Ping", "Connection test: the box answers PONG."]], [0.3, 0.7])]
body += sect("What the plugin is allowed to do", [
    ["Mode", "MonitorOnly", "MonitorOnly, UdpProxy or SimHubGainEvents (section 1)."],
    ["Adaptive enabled", "on", "Master switch for applying gains (also the TOGGLE ADAPTIVE button / ToggleAdaptive action)."],
])
body += sect("Envelope measured at the seat", [
    ["Pitch limit / Roll limit", "8 / 8 deg", "Tilt at which that axis is at 100 % of its limit."],
    ["Angular rate limit", "60 deg/s", "Pitch and roll rate limit."],
    ["Heave cue limit", "0.6 g", "Vertical force change felt in the seat."],
    ["Horizontal cue limit", "0.8 g", "Surge/sway force felt in the seat."],
    ["Vibration RMS limit", "0.35 g", "Above this the Global gain comes down (rumble, resonance)."],
    ["Knee", "0.85", "Fraction of a limit where compression starts."],
    ["Attack / Release", "0.15 / 4 s", "Speed of gain reduction / recovery."],
    ["Minimum gain", "0.3", "Gains never go below this."],
    ["Impact threshold", "1.5 g", "Peak |a|-1 g that counts as an impact (-10 % Global, 2 s hold)."],
    ["Hard pitch / roll trip", "14 / 14 deg", "Beyond this: all gains to minimum, Trip event."],
    ["Trip auto-reset", "off", "Off: a trip stays until RESET TRIP. On: clears 1 s after the seat is back inside the hard limits."],
    ["Failsafe gain (sensor lost)", "0.6", "Target gain when frames stop."],
])
body += sect("UDP proxy", [
    ["Listen port", "33740", "Point SimHub's UDP motion output at 127.0.0.1 on this port."],
    ["Controller host / port", "nullcat.local / 0", "Where SimHub sent the packets before (the motion controller). Port 0 = not set: enter it."],
    ["Header bytes / Trailer bytes", "0 / 0", "Bytes before the first axis / after the last one, passed through unchanged."],
    ["Axis count", "4", "Number of axes in each packet."],
    ["Bytes per axis", "2", "1 or 2."],
    ["High byte first", "on", "Byte order for 2-byte axes (Axis1a = MSB)."],
    ["Max value", "65535", "Full-scale value of one axis (255, 1023, 4095, 65535)."],
    ["Actuator layout", "GlobalOnly", "GlobalOnly, TwoLeftRight, ThreeFrontPairRear or FourCorner: lets the plugin split pitch/roll/heave per actuator."],
    ["Capture neutral", "button", "Motion on, game idle: stores each axis's rest value (the point gains scale around)."],
    ["I verified the decoded axes", "off", "Gains are only applied after you tick this (section 7)."],
])
body += sect("Data logging (CSV)", [
    ["Save logs in folder", "Documents\\Adaptive Motion IMU\\logs", "Where the CSV files go. <b>Browse…</b> picks a folder (new folders can be made in the dialog)."],
    ["Rows per second", "100", "1-250. 250 = every sensor frame (about 2 MB per minute)."],
    ["Start/stop automatically with the game", "off", "On: a new file starts when a game starts running and closes when it stops."],
    ["Include game data", "on", "Adds speed, gear, rpm and the game's own accelerations next to the measured values."],
    ["Start log / Stop log", "button", "Manual start/stop (also on the Live view bar). <b>Open log folder</b> opens it in Explorer."],
])
body += sect("SimHub gain-event mode", [
    ["One SimHub gain step", "5 %", "How much one SimHub Motion gain step changes the gain (match your SimHub setting)."],
    ["Max steps down", "8", "Never request more than this many steps below the starting gain."],
])
body += [P("Press <b>SAVE &amp; APPLY</b> after changing settings. RESET TRIP clears a trip, RESET GAINS sets all gains back to 1.0.", "small")]

# 6 commissioning
body += [PageBreak(), H1("6  Commissioning on the rig"),
    P("Do these steps once, after the box is bolted to the rig (build manual, section 8). Stay in <b>MonitorOnly</b>."),
    steps(["Connect the USB cable to the same PC port as on the bench, so the COM number stays the same. Check the Live block: connected, ~250 fps.",
           ["<b>Capture level</b>: rig powered and at its <b>centre</b> position, no game running, nobody in the seat. Press "
            "<b>CAPTURE LEVEL (RIG AT CENTRE)</b>. The badge changes from IDLE: CAPTURE LEVEL to OK.",
            "Never capture at the park position: a parked rig is usually not level."],
           "<b>Park pose</b>: park the rig and press <b>RE-LEARN PARKED POSE</b>. From now on every park is compared with it; "
           "a difference above 0.5 deg shows MOUNT SHIFTED (box knocked, or the rig parked/homed differently).",
           "<b>Optional, Auto-align</b>: only if the box is not square to the rig. Rig at centre: AUTO-ALIGN 1. Then pitch the rig "
           "nose-up by 2 deg or more (manual move in SimHub/nullCAT), hold, AUTO-ALIGN 2. Tick <i>Use auto-aligned mounting</i>.",
           "<b>Check the signs</b>: brake hard in a game: the cue dot goes to <b>brake</b> and pitch goes nose-down. Take a left corner: "
           "the dot moves right (you are pushed outwards). If a sign is wrong, the mounting axes are wrong: fix them before going on.",
           "<b>Monitor laps</b>: drive a few laps on a track you know, including hard braking and kerbs. Watch the live view: which "
           "quantity reaches its limit line first, and where? Note the impact count after kerbs.",
           "<b>Set the limits</b>: put each limit just above what normal driving reaches, so compression only happens on the "
           "extremes (big kerbs, crashes, end-stop hits). Keep the hard trips well beyond normal driving."]),
    callout("tip", "Good starting point", "Most rigs end up with pitch/roll limits 1-2 deg above the largest normal tilt, and the "
            "impact threshold just above the biggest kerb. If the gains drop on every corner, the limits are too low."),
]

# 7 applying
body += [PageBreak(), H1("7  Applying gains"),
    H2("7.1  UDP proxy (recommended)"),
    P("The plugin sits between SimHub's motion output and the controller. Get each step right before the next one: the proxy "
      "only applies gains when <b>Mode = UdpProxy</b>, <b>Adaptive enabled</b>, <b>level captured</b> and <b>axes verified</b> are all true. "
      "The live view header then shows <b>APPLYING</b>."),
    steps(["In SimHub's Motion output that drives nullCAT, note the <b>target host and port</b> and the <b>packet format</b> "
           "(bytes per axis, byte order, max value, header/trailer).",
           "In Adaptive Motion &gt; Settings &gt; UDP proxy: enter that host and port as <b>Controller host / port</b>, and the packet format "
           "fields exactly as SimHub uses them. Choose the <b>Actuator layout</b> that matches your rig (GlobalOnly if unsure).",
           "Change SimHub's output target to <b>127.0.0.1</b> port <b>33740</b> (the Listen port). Set Mode to <b>UdpProxy</b> but leave "
           "<i>I verified the decoded axes</i> unticked: packets are forwarded unchanged (PASSTHROUGH). Check the rig still moves normally.",
           "With motion on and the game idle, press <b>CAPTURE NEUTRAL</b>.",
           "Verify the decoding: the Live block's Proxy line shows the decoded axes. Move the rig with SimHub's motion test and "
           "check each axis moves the way you expect (axis 1 = actuator 1, and so on).",
           "Tick <b>I verified the decoded axes move correctly</b> and SAVE &amp; APPLY. The header shows APPLYING.",
           "Drive with the hardware e-stop at hand. Watch the gains: they should only drop on the extremes you set the limits for."]),
    callout("warn", "Before you rely on it", "Check that nullCAT parks safely when UDP packets stop (stop SimHub's output, or set the proxy "
            "to a wrong port): the plugin can crash or the PC can hang like any software."),
    H2("7.2  SimHub gain events (alternative)"),
    P("No changes to the packet path. Set Mode to <b>SimHubGainEvents</b>, then in SimHub <b>Controls and events</b> map:"),
    table([["Plugin event", "Map to"],
           ["AdaptiveMotionPlugin.GainDownRequest", "SimHub Motion: decrease global gain"],
           ["AdaptiveMotionPlugin.GainUpRequest", "SimHub Motion: increase global gain"],
           ["AdaptiveMotionPlugin.Trip", "e.g. Motion: disable / park, plus a sound or dash alert"]],
          [0.5, 0.5], mono_cols=(0,)),
    P("Set <b>One SimHub gain step</b> to the step size SimHub uses, so the plugin knows how many steps to request.", "small"),
]

# 8 logging
body += [PageBreak(), H1("8  Data logging"),
    P("The plugin can record everything it measures and does to a CSV file, for checking a session afterwards, tuning the "
      "limits, or comparing the seat with what the game asked for. Files open in Excel, LibreOffice or Python."),
    steps(["Settings &gt; <b>Data logging</b>: choose the folder with <b>Browse…</b> (default <b>Documents\\Adaptive Motion IMU\\logs</b>).",
           "Start a recording with <b>START LOG</b> (Live view bar or Settings), a mapped button (action <b>LogToggle</b>), or tick "
           "<i>Start/stop automatically with the game</i>.",
           "While recording, the live view shows a red <b>● REC</b> badge and the Settings page shows the time, rows and file name.",
           "<b>STOP LOG</b> closes the file. <b>Open log folder</b> shows it in Explorer."]),
    P("File name: <font name='Mono'>AMIMU_2026-11-15_16-40-12_assettocorsa_ks_porsche_911_spa.csv</font> (date, time, game, car, track). "
      "The first lines start with <b>#</b> and record the session: firmware, mode, level and all limits."),
    table([["Columns", "Content"],
           ["time_s, device_us", "Seconds since the start of the log; the box's own microsecond clock"],
           ["pitch_deg, roll_deg, *_rate_dps", "Seat tilt and rotation rates"],
           ["cue_*_g, lin_*_g", "Force felt in the seat (incl. tilt) and the platform's own acceleration (gravity removed)"],
           ["peak_g, vib_rms_g, flags", "Impact peak, vibration, sensor flags (bit list in the file header)"],
           ["gain_*, status, level_*", "What the controller applied, its state, and how much of each limit was in use"],
           ["applying, cmd_pitch, cmd_roll", "Proxy applying gains; what SimHub commanded (proxy mode)"],
           ["game_running, speed_kmh, gear, rpm, game_accel_*", "Game data at that moment (when enabled)"],
           ["event", "Rows with only time_s and event: TRIP, IMPACT, level capture, messages"]],
          [0.38, 0.62], mono_cols=(0,)),
    callout("tip", "Send it over", "A log of a few laps with hard braking and kerbs is the best way to set the limits: "
            "share the CSV and the peaks per corner can be read straight from it."),
]

# 9 updates
body += [PageBreak(), H1("9  Updates"),
    P("The <b>Update</b> tab (next to Live view and Settings) shows the installed and latest versions of the plugin and of the "
      "box's firmware, and installs both. Updates come from the project's public GitHub repository "
      "(<font name='Mono'>updates/stable/manifest.json</font>); every file is checked against its SHA-256 checksum before it is used."),
    table([["Item", "What it shows / does"],
           ["Installed / Latest version", "Plugin version running now, and the newest one offered. 'update available' when newer."],
           ["Check for updates", "Asks GitHub for the latest versions (also automatic 15 s after SimHub starts, if enabled)."],
           ["Download and install", "Downloads and verifies the new plugin, asks once for administrator permission, then offers "
            "<b>Close SimHub now</b>. SimHub closes, the plugin file is replaced and SimHub starts again by itself."],
           ["Roll back to previous version", "Puts the plugin file from before the last update back (same restart)."],
           ["Box firmware / Latest firmware", "Version reported by the box (INFO) and the newest firmware."],
           ["Update firmware", "Rig parked and still. The box restarts into update mode (firmware 1.3.1 and newer; older: "
            "double-tap RST to GND when asked), the plugin copies the new firmware, waits for the box to come back and "
            "confirms the new version. Calibration is kept."],
           ["Release notes", "What changed in the latest version."],
           ["Options", "Repository (owner/name), check on start, beta channel (test versions)."]],
          [0.3, 0.7]),
    callout("note", "Good to know", ["A live view badge <b>UPDATE</b> appears when a newer plugin or firmware is available. "
            "Properties <font name='Mono'>Update.Available</font> and <font name='Mono'>Update.FirmwareAvailable</font> can drive a dash alert.",
            "The installer log is kept in <font name='Mono'>%LOCALAPPDATA%\\SimSpares\\AdaptiveMotion\\updates\\install_plugin.log</font>."]),
    callout("warn", "Firmware updates", "Never unplug the box while the NICENANO drive is visible. If an update is interrupted, "
            "double-tap RST to GND and press Update firmware again: the bootloader itself is never touched."),
]

# 10 dashboards
body += [PageBreak(), H1("10  Dashboards and controls"),
    P("All values are SimHub properties under <font name='Mono'>AdaptiveMotionPlugin.</font> and can be used in Dash Studio, "
      "ShakeIt, LED profiles and NCalc formulas."),
    table([["Property", "Meaning"],
           ["Sensor.Connected / FrameRate / CrcErrors", "Link state, frames per second, checksum errors"],
           ["Sensor.Still / CloneDie / Info", "Rig still, non-genuine IMU, last INFO line"],
           ["Seat.PitchDeg / RollDeg", "Seat tilt relative to the captured level"],
           ["Seat.PitchRateDps / RollRateDps", "Rotation rates"],
           ["Seat.CueSurgeG / CueSwayG / CueHeaveG", "Force felt in the seat"],
           ["Seat.PeakG / VibrationRmsG", "Impact peak and vibration level"],
           ["Gain.Global / Pitch / Roll / Heave", "Current gains (0.3 .. 1.0)"],
           ["Status / Applying / Tripped / ImpactCount", "Controller state"],
           ["Level.Pitch / Roll / Heave / Cue", "Fraction of each limit in use (1.0 = at the limit)"],
           ["Tracking.PitchDelayMs / PitchCoherence / RollDelayMs ...", "Measured delay between SimHub's command and the seat's response"],
           ["Park.State / ReferenceLearned / DeviationDeg / MountShifted", "Park check"],
           ["Proxy.Active / Proxy.Forwarded", "Proxy applying, packets forwarded"],
           ["Mount.Auto / LastMessage", "Auto-align in use, last message from the plugin"],
           ["Log.Active / Log.File / Log.Rows / Log.Seconds", "Recording state, file, rows written, duration"],
           ["Update.InstalledVersion / LatestVersion / Available", "Plugin update state"],
           ["Update.FirmwareInstalled / FirmwareLatest / FirmwareAvailable", "Firmware update state"]],
          [0.5, 0.5], mono_cols=(0,)),
    H2("Actions (Controls and events)"),
    table([["Action", "Does"],
           ["ToggleAdaptive", "Adaptive on/off (good on a wheel button)"],
           ["CaptureLevel / CaptureNeutral", "Same as the buttons"],
           ["ResetTrip / ResetGains", "Clear a trip / gains back to 1.0"],
           ["RelearnParkPose", "New park reference"],
           ["AutoAlignRest / AutoAlignNoseUp", "The two auto-align captures"],
           ["GyroCalibrate / ShowGyroBias", "Forced gyro calibration / show the gyro zero point"],
           ["LogStart / LogStop / LogToggle", "Start, stop or toggle a CSV recording"]],
          [0.42, 0.58], mono_cols=(0,)),
    H2("Events"),
    table([["Event", "Raised when"],
           ["Trip", "Hard pitch/roll limit exceeded"],
           ["Impact", "A knock above the impact threshold"],
           ["GainDownRequest / GainUpRequest", "Gain-event mode wants one step down / up"],
           ["MountShifted", "Parked pose differs from the learned one"],
           ["ParkChecked", "A park check finished (OK or not)"]],
          [0.42, 0.58], mono_cols=(0,)),
]

# 9 troubleshooting + 10 safety
body += [PageBreak(), H1("11  Troubleshooting"),
    table([["Symptom", "Cause / fix"],
           ["Plugin not in the left menu", "SimHub didn't load it: re-run install.ps1 with SimHub closed, then accept 'enable plugin' at start-up."],
           ["Build fails", "Copy the errors from the PowerShell window. Check dotnet --list-sdks shows an SDK; the first build needs internet (NuGet)."],
           ["Live block: not connected", "Wrong COM port (try the other one), or another program (check script, Arduino IDE) has the port open."],
           ["'Device: ERR unknown' once at start", "Another program probed the port; harmless."],
           ["Badge IDLE: CAPTURE LEVEL", "The controller only runs after CAPTURE LEVEL with the rig at centre."],
           ["Pitch/roll not ~0 at centre", "Level not captured, or captured at park position: capture again at centre."],
           ["Wrong signs (brake shows accel, etc.)", "Mounting axes wrong: check forward = +Z, up = +X, or run Auto-align."],
           ["MOUNT SHIFTED", "The box moved on the profile, or the rig parks differently: check the bolts, then RE-LEARN PARKED POSE."],
           ["Gains drop on every corner", "Limits too low for your rig and driving: raise them (section 6, step 7)."],
           ["Header never shows APPLYING", "All four needed: Mode UdpProxy, Adaptive enabled, level captured, axes verified."],
           ["Rig doesn't move in proxy mode", "SimHub's output not pointed at 127.0.0.1:33740, or controller host/port wrong. Proxy.Forwarded should count up."],
           ["SENSORLOST", "No frames for a moment: USB cable or hub. Use a direct port and a shielded cable away from motor wiring."],
           ["Log start failed", "Folder not writable (e.g. inside Program Files) or disk full: pick another folder with Browse…."],
           ["Update check failed: no update channel found", "The repository is private or nothing is published yet."],
           ["Update: administrator permission was refused", "The plugin lives in the SimHub folder: accept the Windows prompt, or run install.ps1."]],
          [0.33, 0.67]),
    H1("12  Safety"),
    callout("warn", "This is not a safety system", [
        "The plugin only ever <b>attenuates</b>: gains stay between the minimum gain and 1.0, and it never moves an actuator further from "
        "neutral than SimHub's own packet did.",
        "It runs on a Windows PC and can stop or crash. Keep the <b>hardware e-stop</b> within reach, keep nullCAT's own limits and "
        "end-stop protection configured, and verify that the controller parks safely when UDP packets stop before using the proxy."]),
]

doc = ManualDoc(OUT, "Adaptive Motion IMU\nSimHub Plugin", "Install, live view, settings, commissioning",
                "Plugin manual v1.2 · plugin 1.2 · firmware 1.3.1 · November 2026", "Sim racing motion · closed loop")
doc.build(story + start(body))
print("wrote", OUT)
