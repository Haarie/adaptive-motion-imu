# AdaptiveMotionIMU

Closed-loop motion for SimHub: an MPU-6050 on the seat (Arduino Micro) measures what the rig really does,
and a SimHub plugin attenuates motion when the seat approaches limits you set.

```
firmware/
  AdaptiveMotionIMU/AdaptiveMotionIMU.ino   sketch (Arduino IDE 2: Board = Arduino Micro)
  AdaptiveMotionIMU_micro.hex / _nano.hex   prebuilt images (ArduinoCore-avr, -Os)
  PROTOCOL.md                               frame + command reference
  build_check.sh                            compile with plain avr-gcc (CI / no IDE)
  host_test/                                runs the real sketch on a PC with a fake MPU, writes frames.bin
plugin/SimSpares.AdaptiveMotion/
  SimSpares.AdaptiveMotion.csproj           net48, references SimHub from C:\Program Files (x86)\SimHub
  AdaptiveMotionPlugin.cs                   SimHub glue (properties, actions, events, settings)
  SettingsControl.cs                        settings page (code-built WPF)
  Core/                                     SimHub-independent logic (tested)
  IO/                                       serial link + UDP proxy
  Tests/CoreTests.cs                        65 tests
  UI/MotionVisualizer.cs                    live view (horizon, g-g, meters, gains, 10 s trend)
  UI/VisualizerWindow.cs                    pop-out window for a second screen
plugin/tools/check.sh                       Mono: tests + stub compile + live-view preview render: mcs -out:t.exe Tests/CoreTests.cs Core/*.cs && mono t.exe frames.bin
enclosure/
  adaptive_motion_imu_enclosure.scad        parametric source
  base.stl, lid.stl, lid_inlay.stl            print-ready (lid + inlay share coordinates for the MMU3)
  preview.png, preview_base.png
  fit_figure.py                             annotated fit picture -> docs/enclosure_fit.png (parts "fit" / "exploded")
```

## Build the plugin (RYZEN-Racing)
1. Install the .NET Framework 4.8 Developer Pack and the .NET SDK (or Visual Studio 2022).
2. Close SimHub. `cd plugin/SimSpares.AdaptiveMotion && dotnet build -c Release`
   (DLL is copied into the SimHub folder; run the shell as admin or pass `-p:DeployToSimHub=false` and copy by hand).
3. Start SimHub → enable the plugin when asked → left menu "Adaptive Motion".

## Live view
SimHub → Adaptive Motion → **Live view** tab (or **Pop out window** for a second screen / the rig display):

![live view](docs/visualizer_preview.png)

- **Attitude**: artificial horizon of the seat (pitch ladder, roll scale), yellow = comfort limit, red = hard trip.
- **Horizontal cue**: g-g plot of the force felt in the seat with a 2 s trail; dashed ring = cue limit.
- **Meters**: pitch/roll rate, heave cue, vibration RMS, peak |a|−1 g (impact threshold) and the impact count.
- **Gains**: what the controller currently applies (red line = minimum gain).
- **Last 10 s**: pitch and roll trend against the limits.
- Header badges: controller status, mode, sensor rate, STILL, MOUNT SHIFTED, CLONE IMU, LEVEL NOT CAPTURED.
(The preview image is rendered off-Windows through an SVG stand-in, so fonts differ slightly from SimHub.)

## Mounting
Default `mount_style = "side"`: the box lies along a horizontal 30/40-series slot-8 cross-member, bolted to
its vertical side face; a 6 mm flange along the box's long side carries two M6 bolts 32 mm apart (±3 mm play),
both in that face's horizontal slot. The box sits beside the slot (above it, or below it if you flip it).
Hardware: 2 × M6 × 16 DIN 912 + washers, 2 × M6 slot-8 T-nuts. Print `base.stl` (side), or
`base_across.stl` (box across a vertical upright) / `base_along.stl` (ears at the ends) for other mounts.
The box does not have to be square to the rig: use **Auto-align** (below).

![mounting](docs/enclosure_mounting.png)

## Calibration
- **Auto-align (any mounting angle)**: 1) rig at centre, still → *Auto-align 1: rest*; 2) pitch the rig
  nose-up ≥ 2° (pure pitch, e.g. SimHub/nullCAT manual move) and hold → *Auto-align 2: nose-up*.
  The plugin derives the rig's up and forward axes in sensor coordinates (and sets level). Re-run it
  whenever the box is re-mounted. The manual axis pickers remain for square mounts.
- **Gyro bias** is learned automatically by the firmware whenever the rig is still for 2 s (AUTOCAL, on by default),
  so it follows the sensor as it warms up. `GyroCalibrate` remains for a forced 2 s calibration.
- **Level** is captured once, with the rig at its *centre* position (`CaptureLevel`) — never automatically,
  because the park position is not necessarily level.
- **Park check**: when no game runs and the rig is still, the plugin averages the parked pose and compares it with
  the pose learned at the first park. A difference above 0.5° raises the `MountShifted` event / `Park.MountShifted`
  property (IMU box moved, or the rig homed/parked elsewhere). It warns only. `RelearnParkPose` after re-mounting.

## Safety
The plugin only ever attenuates (gains in [MinGain, 1]) and never moves an actuator further from neutral than
SimHub's own packet did. It is NOT a safety system: keep the hardware e-stop and verify nullCAT parks on
UDP loss before using the proxy.

See the design doc for the full investigation, control law and bring-up plan.
