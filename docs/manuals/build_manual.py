"""Adaptive Motion IMU - Build Manual (PDF)."""
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from mstyle import *
from diagrams import WiringDiagram, SystemDiagram, AxesDiagram

D = os.path.join(ROOT, "docs")
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(D, "manuals", "Adaptive_Motion_IMU_Build_Manual.pdf")
cover_p = ParagraphStyle("cv", fontName="Body", fontSize=9.5, leading=14, textColor=colors.HexColor("#C9D0CB"))
cover_h = ParagraphStyle("cvh", fontName="RajSB", fontSize=12, leading=15, textColor=GREEN, spaceAfter=4)

story = [
    Paragraph("What you build", cover_h),
    Paragraph("A small sensor box that bolts to the seat frame of the motion rig. It measures what the seat "
              "really does - tilt, rotation rates, the g-forces you feel, vibration and end-stop knocks - and "
              "streams it 250 times a second over USB to the Adaptive Motion plugin in SimHub.", cover_p),
    Spacer(1, 6),
    Paragraph("This manual covers: parts, printing the enclosure, wiring, flashing the firmware, bench testing and "
              "calibration, and mounting it on the rig. The SimHub side has its own manual.", cover_p),
]

body = []
body += [H1("Contents"), toc_block([
    ("Overview", "how the box fits into the motion system"),
    ("Parts and tools", "bill of materials, what to have on the bench"),
    ("Print the enclosure", "files, PrusaSlicer project, MMU3 colours"),
    ("Wiring", "four wires, no level shifter"),
    ("Assembly", "inserts, sensor, board, lid"),
    ("Flash the firmware", "UF2 drag-and-drop, COM ports"),
    ("Bench test and calibration", "check script, drift log, 6-position calibration"),
    ("Mount on the rig", "80x40 / 40x40 profile, M6 bolts, orientation"),
    ("Troubleshooting", "LED codes and common problems"),
    ("Reference", "serial commands, files"),
])]

# 1 Overview
body += [PageBreak(), H1("1  Overview"),
    P("SimHub turns game telemetry into actuator positions. It never sees what the rig actually does. The Adaptive Motion "
      "IMU closes that loop: an MPU-6050 motion sensor bolted to the seat frame measures the real movement, and the "
      "SimHub plugin uses it to <b>turn motion down</b> when the seat approaches limits you set (tilt, rate, g-force, "
      "vibration, end-stop impacts) and to release it again when things calm down."),
    SystemDiagram(),
    Spacer(1, 6),
    table([["Part", "What it does"],
           ["ProMicro nRF52840", "Reads the sensor 500x/s, filters it, estimates tilt (Mahony filter), learns the gyro drift while the rig is parked and sends 250 frames/s over USB-C."],
           ["GY-521 (MPU-6050)", "3-axis accelerometer + 3-axis gyroscope, on I2C at 3.3 V."],
           ["3D-printed box", "Holds both boards, carries the M6 flange for the profile, Sim-Spares logo inlaid in the lid."],
           ["SimHub plugin", "Shows the live view, computes per-axis gains and applies them (see the plugin manual)."]],
          [0.25, 0.75]),
    callout("note", "Board choice", "The design started on an Arduino Micro (5 V). The board actually used is a "
            "<b>ProMicro nRF52840</b> (nice!nano v2 clone, USB-C, 3.3 V). It runs at the same 3.3 V as the sensor, "
            "so no level shifter is needed. The enclosure files are made for this board."),
]

# 2 Parts
body += [H1("2  Parts and tools"), H2("Bill of materials"),
    table([["Item", "Specification", "Qty", "Note"],
           ["Microcontroller", "ProMicro NRF52840 (nice!nano v2 clone), USB-C, UF2 bootloader", "1", "Bootloader with SoftDevice S140 6.1.1"],
           ["IMU", "GY-521 breakout, MPU-6050", "1 (+2 spare)", "Clones vary: keep the one that tests best"],
           ["USB cable", "USB-C data cable, shielded, ~2 m", "1", "Not a charge-only cable"],
           ["Hook-up wire", "26 AWG silicone: red, black, green, yellow", "4 x ~8 cm", "Solder directly, no Dupont jumpers"],
           ["Heat-set inserts", "M3 x 5.7 mm brass, 4.0 mm hole", "4", "Lid lugs"],
           ["Lid screws", "M3 x 10 ISO 7380 (button head) or DIN 912", "4", ""],
           ["Sensor screws", "M2.5 x 6 self-tapping for plastic", "2", "Snug, not tight (see Assembly)"],
           ["Mount bolts", "M6 x 16 DIN 912 + DIN 125 washer", "2", "Black oxide 12.9 or A2"],
           ["T-nuts", "M6 slot-8 drop-in (30/40 series)", "2", "For the cross-member's slot"],
           ["Cable ties", "2.5 mm wide", "2", "USB strain relief"],
           ["Filament", "PETG: black + green, orange, white", "~30 g", "Base and lid black; three inlay colours"]],
          [0.18, 0.42, 0.13, 0.27]),
    Spacer(1, 4),
    P("Estimated cost about <b>EUR 29</b> (board already owned). The live BOM sheet has the prices."),
    H2("Tools"),
    table([["", ""],
           ["Soldering iron (fine tip), solder, flux", "Heat-set insert tip or the soldering iron at ~220 C"],
           ["Small screwdrivers, 5 mm hex key (M6), 2 mm hex key (M3 button head)", "Multimeter (3.3 V check)"],
           ["Prusa MK3.5S + MMU3 (or any printer; single colour works too)", "Windows PC with SimHub and the .NET SDK"]],
          [0.5, 0.5], header=False),
]

# 3 Print
body += [PageBreak(), H1("3  Print the enclosure"),
    P("All print files are in the <b>enclosure</b> folder of the project. The quickest way is the ready-made PrusaSlicer project:"),
    table([["File", "What it is"],
           ["adaptive_motion_imu_print.3mf", "PrusaSlicer project: base + lid with its three inlays, filaments already assigned"],
           ["base.stl", "Base for the side mount (flange with two M6 slots, 32 mm apart)"],
           ["base_across.stl / base_along.stl", "Alternative bases: across a vertical upright / ears at the ends"],
           ["lid.stl + lid_inlay_green/orange/white.stl", "Lid and the logo/text inlays (load together, same coordinates)"],
           ["lid_inlay.stl", "All artwork as one part, if you print the inlay in a single colour"]],
          [0.42, 0.58], mono_cols=(0,)),
    H2("Print settings"),
    steps(["Open <b>adaptive_motion_imu_print.3mf</b> in PrusaSlicer and choose <b>load as project</b>.",
           "Select your printer profile <b>Original Prusa MK3.5S MMU3</b> and PETG filament profiles.",
           "Check the filament slots: <b>1</b> black (lid + base), <b>2</b> green #A6FF4D, <b>3</b> orange #FF7A29, <b>4</b> white #F3F5F1. "
           "For a grey base, right-click the base and pick slot 5.",
           "Press <b>A</b> to arrange (this also makes room for the wipe tower).",
           "Layer height 0.2 mm, first layer 0.2 mm, perimeter generator <b>Arachne</b> (needed for the small text). "
           "No supports needed. Slice and check the first three layers in the preview: that is where the colour changes are."]),
    callout("tip", "Why face-down?", "The lid prints with its top on the bed. The logo and text are the first 0.6 mm, so the "
            "MMU3 only swaps colours during three layers and the artwork comes out with the bed's surface finish."),
    figure(os.path.join(D, "lid_artwork.png"), 0.72, "The lid: steering-wheel logo, SIM-SPARES.COM and the UP marking (side-mount version)."),
]

# 4 Wiring
body += [PageBreak(), H1("4  Wiring"),
    P("Four wires, board straight to sensor. Both run at 3.3 V, so <b>no level shifter</b>. Solder 26 AWG silicone wire "
      "directly to the pads; skip header pins and Dupont jumpers, the rig's vibration shakes those loose. Keep each wire under 10 cm."),
    WiringDiagram(),
    Spacer(1, 4),
    table([["GY-521 pin", "Board pad (silkscreen)", "Position", "Wire"],
           ["VCC", "VCC", "right edge, 4th pad from the USB end (after B+, GND, RST)", "red"],
           ["GND", "GND", "left edge, 3rd pad", "black"],
           ["SDA", "1.00", "left edge, 9th pad", "green"],
           ["SCL", "0.11", "left edge, 10th pad", "yellow"],
           ["XDA, XCL, AD0, INT", "-", "not connected (AD0 has a pull-down on the module: address 0x68)", "-"]],
          [0.18, 0.2, 0.48, 0.14]),
    callout("warn", "Keep pads 5-8 free", "The lid has two ribs that press on the board's edges over pads 5 to 8 on both sides. "
            "Keep solder blobs off those pads, and route the wires away from them."),
    callout("note", "About VCC", "The board's VCC pad is switched by pin P0.13. Clones disagree on which level switches it on; "
            "the firmware tries both and keeps the one where the sensor answers. Never power the GY-521 from B+ (battery / 5 V side)."),
]

# 5 Assembly
body += [PageBreak(), H1("5  Assembly"),
    figure(os.path.join(D, "enclosure_fit.png"), 0.82, "How the parts fit: top view (numbered) and exploded view.", max_h=150 * mm),
    steps(["Press the four <b>M3 heat-set inserts</b> into the lugs on the outside of the base (iron at ~220 C, press straight, let cool).",
           ["Screw the <b>GY-521</b> onto its two bosses with the M2.5 x 6 screws, header edge resting on the support bar.",
            "<font color='#C2510F'><b>Snug, not tight.</b></font> The MPU-6050 reacts to a bent board: on the bench the Z axis shifted by about 0.2 g "
            "between assembly states. Calibrate in the final, closed state."],
           "Lay the <b>ProMicro</b> on its two edge ledges between the rails, USB-C against the end wall cutout.",
           "Solder the four wires (section 4) if you haven't yet, and tuck them along the floor.",
           "Fit the lid (logo up, the UP arrow pointing away from the flange) and tighten the four M3 screws evenly. The ribs under the lid clamp the board."]),
]

# 6 Flash
body += [PageBreak(), H1("6  Flash the firmware"),
    P("The firmware file is <b>AdaptiveMotionIMU_promicro_nrf52840.uf2</b> (in the <b>firmware</b> folder). No IDE needed."),
    steps(["Plug the box into the PC with the USB-C cable.",
           "Quickly touch <b>RST</b> to <b>GND</b> twice (within half a second). A drive called <b>NICENANO</b> appears.",
           "Open <b>INFO_UF2.TXT</b> on that drive. It must say <b>SoftDevice: S140 version 6.1.1</b>; if not, stop and check the board.",
           "Copy the <b>.uf2</b> file onto the drive. The drive disappears and the board restarts with the firmware "
           "(a Windows message that the copy failed or the drive was ejected is normal).",
           "Open Device Manager &gt; Ports: the board now shows <b>two COM ports</b>."]),
    callout("note", "Two COM ports", ["The board package gives the board a <b>data port</b> and a <b>maintenance port</b>. "
            "Use the one that answers (COM16 on the bench PC) for the scripts and the plugin. Leave the other alone: "
            "nothing is sent there, and some tools reset the board into update mode through it.",
            "Bonus: later updates can also come straight from the Arduino IDE (board package ArduinoNRF, board "
            "<i>AliExpress ProMicro nRF52840</i>) without the double tap."]),
    H2("Status LED"),
    table([["LED", "Meaning"],
           ["Blinks ~2x per second", "Sensor found, waiting for a program to open the data port"],
           ["On", "Sensor found and a program (script or SimHub) is connected"],
           ["Blinks fast", "No sensor on I2C: check the four wires (SDA/SCL swapped is the usual one) and 3.3 V on the GY-521"]],
          [0.3, 0.7]),
]

# 7 Bench
body += [PageBreak(), H1("7  Bench test and calibration"),
    P("Two PowerShell scripts in the <b>firmware/tools</b> folder do the checks; they need nothing installed. Close SimHub and "
      "any serial monitor first (only one program can use the port). Run them from the folder they are in:"),
    H2("7.1  Quick check"),
    code("powershell -ExecutionPolicy Bypass -File imu_check.ps1 -Port COM16"),
    table([["Line", "Good result (bench values)"],
           ["device: INFO", "fw=1.3.0 who=0x68 out=250 dlpf=3 cal=1 auto=1"],
           ["IMU frames", "about 250/s, CRC errors 0"],
           ["|a| at rest", "OK (about 1.00 g once calibrated)"],
           ["flags", "gyro-calibrated, and 'still' after the box lies still for 2 s"]],
          [0.25, 0.75]),
    P("Live view while you tilt the box by hand (prints a line every 2 s, -Csv logs every frame to a file):", "p"),
    code("powershell -ExecutionPolicy Bypass -File imu_check.ps1 -Port COM16 -Live 30"),
    H2("7.2  Drift log (optional)"),
    P("Shows whether the sensor drifts while it warms up. Unplug the box for 15 minutes, plug it in, leave it flat and run:"),
    code("powershell -ExecutionPolicy Bypass -File imu_cal.ps1 -Port COM16 -Diag 20"),
    P("On the bench the sensor warmed only 0.5 C in 20 minutes and its readings moved less than 0.01 g and 0.13 deg/s: stable. "
      "A sensor whose readings wander regardless of temperature is a dud: swap in a spare GY-521."),
    PageBreak(),
    H2("7.3  Six-position accelerometer calibration"),
    P("Corrects each axis's zero offset and scale (MPU-6050s are often several % off). Do it with the box <b>closed and finished</b>."),
    code("powershell -ExecutionPolicy Bypass -File imu_cal.ps1 -Port COM16 -AccCal"),
    steps(["The script asks for six positions: lid up, lid down, USB end up, USB end down, flange side up, flange side down. Order doesn't matter.",
           "Place the box, let go, press Enter. Prop it so it doesn't rock; within about 5 deg is fine. It re-asks if the box moved.",
           "At the end it sends the correction to the box (saved on the board) and checks: <b>|a| now</b> should be 1.000 +- 0.02 g."]),
    table([["Axis", "Bench offset", "Bench scale"], ["X", "+0.029 g", "0.996"], ["Y", "-0.007 g", "0.994"], ["Z", "+0.166 g", "0.986"]],
          [0.3, 0.35, 0.35]),
    Spacer(1, 6),
    callout("tip", "Gyro drift is automatic", "Whenever the box has been still for 2 s, the firmware refines its gyro zero point in the "
            "background and saves it on the board. Parked rig = calibrated gyro. No button needed."),
]

# 8 Mount
body += [PageBreak(), H1("8  Mount on the rig"),
    figure(os.path.join(D, "enclosure_render.png"), 0.86, "Side mount on a black 80x40 profile: flange and M6 bolts in the lower slot.", max_h=95 * mm),
    steps(["Slide two <b>M6 slot-8 T-nuts</b> into the horizontal slot of the cross-member's vertical side face, about 32 mm apart.",
           "Hold the box with the <b>lid facing the front of the rig</b> and the <b>UP arrow pointing up</b>; the flange sits on the slot.",
           "Fit the <b>M6 x 16</b> bolts with washers and tighten. The slots give +-3 mm play to line up with the T-nuts.",
           "Run the USB-C cable to the SimHub PC and fix it to the cable-tie tab and the frame. Use the same USB port on the PC every time "
           "so the COM number stays the same.",
           "Continue in the <b>plugin manual</b>: capture the level with the rig at centre, then learn the parked pose."]),
    AxesDiagram(),
    P("The box doesn't have to be perfectly square: the plugin's <b>Auto-align</b> works out the axes from two short captures. "
      "With the box mounted as above, the defaults (forward = +Z, up = +X) are already right.", "small"),
]

# 9 Troubleshooting
body += [PageBreak(), H1("9  Troubleshooting"),
    table([["Symptom", "Cause / fix"],
           ["Check script: 'Nothing received' on a COM port", "Firmware not flashed (see section 6), or that's the maintenance port: try the other COM port."],
           ["No NICENANO drive after the double tap", "Tap faster (both within 0.5 s). Try another USB cable: charge-only cables have no data lines."],
           ["LED blinks fast / I2C-ERROR flag / who=0x00", "Sensor not reached: SDA and SCL swapped, a cold joint, or no 3.3 V on the GY-521 VCC pin."],
           ["|a| at rest far from 1.00 g", "Uncalibrated: run the 6-position calibration. If it changes a lot after tightening screws, the GY-521 is stressed: loosen slightly and recalibrate."],
           ["Gyro shows 1-5 deg/s at rest", "Normal before the drift correction has run: leave the box still for a few seconds."],
           ["Tilt drifts slowly while lying still", "Tilt correction gated: check |a| (calibrate). Firmware 1.3 learns the resting |a| automatically."],
           ["'clone-die' flag", "WHO_AM_I is not 0x68: a clone chip. Usually works; prefer a genuine one if it's noisy."],
           ["'ERR unknown' in the log", "Another program (e.g. SimHub's Arduino detection) sent bytes to the port. Harmless; plugin 1.0.1+ clears it."],
           ["Script: 'Cannot open COMxx'", "Another program has the port open: close SimHub, the Arduino IDE serial monitor and other scripts."]],
          [0.36, 0.64]),
]

# 10 Reference
body += [H1("10  Reference"), H2("Serial commands (data port, one per line)"),
    table([["Command", "Effect"],
           ["PING", "PONG"],
           ["INFO", "firmware version, sensor ID, output rate, filter, calibration state"],
           ["BIAS", "gyro zero point (0.001 deg/s), number of updates since boot, still flag"],
           ["GYROCAL", "forced 2 s gyro calibration (rig must be still)"],
           ["AUTOCAL 0 / 1", "background gyro learning off / on (default on)"],
           ["DIAG", "temperature, raw accel (mg) and gyro (mdps) means, learned resting |a|"],
           ["ACC / ACCCAL ...", "show / set the accelerometer calibration (ACCCAL 0 removes it)"],
           ["DLPF n / ODR n", "sensor low-pass 0..6 (default 3) / output divider (default 2 = 250 Hz)"],
           ["STREAM 0 / 1, DEFAULTS, REBOOT", "pause/resume frames, factory settings, restart"]],
          [0.32, 0.68], mono_cols=(0,)),
    P("Full frame format: <b>firmware/PROTOCOL.md</b>.", "small"),
    H2("Project files"),
    table([["Folder", "Contents"],
           ["firmware/", "AdaptiveMotionIMU.ino, the .uf2, PROTOCOL.md, tools/imu_check.ps1, tools/imu_cal.ps1"],
           ["enclosure/", "OpenSCAD source, STLs, PrusaSlicer project, fonts, render scripts"],
           ["plugin/", "SimHub plugin source and install.ps1"],
           ["docs/", "pictures and these manuals"]],
          [0.22, 0.78], mono_cols=(0,)),
]

doc = ManualDoc(OUT, "Adaptive Motion IMU\nBuild Manual", "Sensor box: print, wire, flash, calibrate, mount",
                "Build manual v1.0 · firmware 1.3.0 · November 2026", "Sim racing motion · closed loop")
doc.build(story + start(body))
print("wrote", OUT)
