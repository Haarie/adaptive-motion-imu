# AdaptiveMotionIMU serial protocol v1

Transport: USB CDC (ProMicro nRF52840 / Arduino Micro: baud rate ignored; Nano: 1 000 000 baud, 8N1).
The host **must assert DTR** (`SerialPort.DtrEnable = true`); the firmware only reports "host open" while DTR is high.
Never open the port at **1200 baud**: that is the "reset into bootloader" touch on many native-USB boards.

## Frame (device → host), little-endian

| Offset | Size | Field |
|---|---|---|
| 0 | 1 | `0xA5` sync |
| 1 | 1 | `0x5A` sync |
| 2 | 1 | type: `0x01` IMU, `0x02` text |
| 3 | 1 | payload length N |
| 4 | 1 | sequence number (wraps) |
| 5 | N | payload |
| 5+N | 1 | CRC-8 (poly 0x07, init 0x00) over bytes 2 … 4+N |

### IMU payload (type 0x01, N = 30), one per `ODR` window (default 250 Hz)

| Offset | Type | Unit | Field |
|---|---|---|---|
| 0 | u32 | µs | device time (`micros()`) |
| 4 | i16×3 | mg | mean specific force ax, ay, az (sensor frame) |
| 10 | i16×3 | 0.1 °/s | mean angular rate gx, gy, gz (bias-corrected) |
| 16 | i16×4 | Q14 | attitude quaternion q0..q3 (Mahony, sensor frame → level) |
| 24 | u16 | mg | peak \| \|a\| − 1 g \| inside the window (impact / end-stop detector) |
| 26 | u16 | mg | RMS of 8 Hz high-passed acceleration (vibration / resonance) |
| 28 | u8 | — | flags (below) |
| 29 | u8 | — | dropped-frame counter (saturating) |

Flags: `0x01` I2C error · `0x02` gyro calibrated · `0x04` accel saturated · `0x08` gyro saturated ·
`0x10` WHO_AM_I ≠ 0x68 (clone die) · `0x20` frames dropped (host too slow) · `0x40` accel tilt correction gated (>50 % of window) ·
`0x80` still: the rig has been still for ≥ 2 s (and, with AUTOCAL on, the gyro bias is being refined).

### Text payload (type 0x02)
ASCII, no terminator, ≤ 60 bytes. Replies to commands, e.g. `PONG`, `OK GYROCAL`, `ERR GYROCAL motion`,
`INFO fw=1.1.0 who=0x68 out=250 dlpf=3 cal=1 auto=1`, `BIAS 1994 -12 35 n=36 still=1`.

## Commands (host → device)
ASCII lines terminated by `\n`.

| Command | Effect |
|---|---|
| `PING` | → `PONG` |
| `INFO` | firmware/sensor info |
| `GYROCAL` | 2 s gyro bias calibration, saved to EEPROM. Rig must be parked; refuses if it moves. |
| `DLPF n` | MPU DLPF 0..6 (0 = 260 Hz/0 ms … 3 = 44 Hz/4.9 ms … 6 = 5 Hz/19 ms). Saved. |
| `ODR n` | output divider 1..50 (frame rate = 500/n Hz). Saved. |
| `STREAM 0/1` | pause / resume IMU frames |
| `DEFAULTS` | factory settings |
| `REBOOT` | watchdog reset |
| `AUTOCAL 0/1` | background gyro-bias learning while still (default on). Saved. |
| `BIAS` | current gyro bias in 0.001 °/s, number of still-window updates since boot, still flag |

## Background gyro-bias learning (fw 1.1)
Every 0.5 s window is "still" when: gyro peak-to-peak < 1.5 °/s per axis, | |a| − 1 g | < 0.03 g, and the gravity
direction turned < 0.1° since the previous window (so a slow tilt is never learned as bias; yaw can't be checked).
After 4 still windows in a row (2 s) the `still` flag is set and each further window blends 25 % of its mean raw
gyro into the bias (max 0.25 °/s per step, |bias| ≤ 8 °/s). The bias is saved to EEPROM once per boot and then at
most every 10 min. Resolution: a rotation slower than ~0.2 °/s about a horizontal axis is indistinguishable from bias.
