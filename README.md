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
  Tests/CoreTests.cs                        50 tests: mcs -out:t.exe Tests/CoreTests.cs Core/*.cs && mono t.exe frames.bin
enclosure/
  adaptive_motion_imu_enclosure.scad        parametric source
  base.stl, lid.stl, lid_inlay.stl            print-ready (lid + inlay share coordinates for the MMU3)
  preview.png, preview_base.png
```

## Build the plugin (RYZEN-Racing)
1. Install the .NET Framework 4.8 Developer Pack and the .NET SDK (or Visual Studio 2022).
2. Close SimHub. `cd plugin/SimSpares.AdaptiveMotion && dotnet build -c Release`
   (DLL is copied into the SimHub folder; run the shell as admin or pass `-p:DeployToSimHub=false` and copy by hand).
3. Start SimHub → enable the plugin when asked → left menu "Adaptive Motion".

## Calibration
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
