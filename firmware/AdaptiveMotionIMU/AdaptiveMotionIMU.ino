/*
  AdaptiveMotionIMU — rig-mounted IMU for the SimHub "Adaptive Motion" plugin
  -----------------------------------------------------------------------------
  Target   : "ProMicro NRF52840" (nice!nano v2 clone, nRF52840, USB-C, 3.3 V logic),
             built with the ArduinoNRF core, board "AliExpress ProMicro nRF52840".
             Still builds for Arduino Micro / Pro Micro (ATmega32U4) and Nano.
  Sensor   : MPU-6050 (GY-521 breakout) on I2C @ 400 kHz.
  Output   : framed binary packets over USB CDC (see PROTOCOL.md), 250 Hz default.

  Pipeline : MPU-6050 sampled at 500 Hz (DLPF on, 1 kHz / (1+SMPLRT_DIV))
             -> gyro bias removal (EEPROM)
             -> Mahony AHRS (accel-gated, so surge/brake cues don't corrupt tilt)
             -> per-window stats (mean accel/gyro, peak |a|-1g, high-pass vibration RMS)
             -> frame every OUT_DIV samples, CRC-8.

  No external libraries: register-level driver, only Wire/EEPROM/watchdog.

  BOARD NOTES
    ProMicro nRF52840 : SDA = pad "1.00" (D6), SCL = pad "0.11" (D7) — the core's
                        default, hardware-verified I2C pins. GY-521 VCC -> board "VCC"
                        (3.3 V, switched by P0.13; the firmware switches it on and
                        auto-detects the polarity). No level shifter: all 3.3 V.
    Micro / Pro Micro : SDA = D2, SCL = D3, MPU INT -> D7 (optional, polled anyway).
    Nano              : SDA = A4, SCL = A5, MPU INT -> D2.
    GY-521 pull-ups go to its own 3.3 V LDO. A 5 V AVR reads that as HIGH in practice
    but it is out of spec (VIH = 0.7*VCC = 3.5 V); fit the BSS138 level shifter from
    the BOM for a rig that vibrates all day.

  License: MIT. (c) 2026 Haarie / Sim-Spares.
*/

#include <Arduino.h>
#include <Wire.h>
// Arduino.h on some cores defines min/max as macros, which breaks <cmath>.
#pragma push_macro("min")
#pragma push_macro("max")
#undef min
#undef max
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stddef.h>
#pragma pop_macro("max")
#pragma pop_macro("min")
#include <EEPROM.h>

// ----------------------------------------------------------------------------
// Platform layer: watchdog, persistent storage, reboot, sensor power
// ----------------------------------------------------------------------------
#if defined(ARDUINO_ARCH_AVR)
  #include <avr/wdt.h>
  static inline void wdtStart() { wdt_enable(WDTO_250MS); }
  static inline void wdtFeed()  { wdt_reset(); }
  static inline void wdtBoot()  { MCUSR = 0; wdt_disable(); }
  static inline void hwReboot() { wdt_enable(WDTO_15MS); for (;;) {} }
  static inline void storeBegin() {}
  static inline void storeCommit() {}
  static inline bool hostPresent() {
  #if defined(USBCON)
    return (bool)Serial;            // DTR asserted by the host
  #else
    return true;
  #endif
  }
  static inline void sensorPowerOn(uint8_t) {}
  #define HAS_SENSOR_POWER 0
#elif defined(NRF52_SERIES) || defined(ARDUINO_ARCH_NRF52)
  #include <Watchdog.h>
  // The nRF52 WDT cannot be stopped once started. 1 s covers a flash page erase
  // (~85 ms) during a settings save with lots of margin.
  static inline void wdtStart() { Watchdog.begin(1000); }
  static inline void wdtFeed()  { Watchdog.feed(); }
  static inline void wdtBoot()  { if (Watchdog.running()) Watchdog.feed(); }
  static inline void hwReboot() {
    delay(20);
    __asm volatile ("dsb 0xF" ::: "memory");
    *(volatile uint32_t *)0xE000ED0CUL = 0x05FA0004UL;   // SCB->AIRCR = SYSRESETREQ
    __asm volatile ("dsb 0xF" ::: "memory");
    for (;;) {}
  }
  static inline void storeBegin()  { EEPROM.begin(); }   // flash-emulated EEPROM
  static inline void storeCommit() { EEPROM.commit(); }
  static inline bool hostPresent() { return Serial.dtr(); }
  // Board pin "VCC" is switched by P0.13. Clones differ in polarity, so mpuInit()
  // tries both levels and keeps the one where the MPU answers.
  static inline void sensorPowerOn(uint8_t level) {
    pinMode(PIN_EXT_VCC, OUTPUT);
    digitalWrite(PIN_EXT_VCC, level);
  }
  #define HAS_SENSOR_POWER 1
#else
  #error "Unsupported board: use the ArduinoNRF core (ProMicro nRF52840) or an AVR board"
#endif

// ----------------------------------------------------------------------------
// Build configuration
// ----------------------------------------------------------------------------
#define FW_VERSION        "1.3.0"
#define SERIAL_BAUD       1000000UL   // ignored by native USB CDC, used on Nano
#define MPU_ADDR          0x68        // AD0 low. 0x69 if AD0 tied high.
#define SAMPLE_HZ         500         // internal fusion rate
#define DEFAULT_OUT_DIV   2           // 500/2 = 250 Hz frames to the PC
#define DEFAULT_DLPF      3           // 44 Hz accel / 42 Hz gyro, ~4.9 ms group delay
#define ACCEL_FS          1           // 0=±2g 1=±4g 2=±8g 3=±16g  (end-stop hits exceed 2 g)
#define GYRO_FS           1           // 0=±250 1=±500 2=±1000 3=±2000 dps
#define LED_PIN           LED_BUILTIN

// Mahony gains. Low Kp + accel gating: we trust the gyro during cues.
#define MAHONY_KP         0.6f
#define MAHONY_KI         0.0f
#define ACC_GATE_LO       0.90f       // only use accel for tilt correction when
#define ACC_GATE_HI       1.10f       //  0.9 g < |a| < 1.1 g

// High-pass corner for the vibration metric (first order), Hz
#define VIB_HP_HZ         8.0f

// Stillness detector + background gyro-bias learning (rig parked / standing still)
#define STILL_WIN         250         // samples per window (0.5 s @ 500 Hz)
#define STILL_WINDOWS     4           // consecutive still windows before "still" (2 s)
#define STILL_GYRO_PP_DPS 1.5f        // max peak-to-peak gyro per axis inside a window
#define STILL_ACC_PP_G    0.03f       // max peak-to-peak of |a| inside a window (vibration / motion)
#define STILL_ACC_ABS_G   0.25f       // |a| must still be within 1 g +- this (uncalibrated MPU-6050s can be ~20 % off)
#define GREF_ALPHA        0.2f        // per still window: how fast gRef follows the resting |a|
#define STILL_TILT_DEG    0.10f       // max gravity-direction change between windows (rejects slow rotation)
#define BIAS_ALPHA        0.25f       // blend of each new still-window mean into the bias
#define BIAS_MAX_STEP_DPS 0.25f       // max bias change per window (slew limit)
#define BIAS_MAX_ABS_DPS  8.0f        // never learn a bias larger than this
#define BIAS_SAVE_MS      600000UL    // EEPROM write at most every 10 min (first one right away)

// ----------------------------------------------------------------------------
// MPU-6050 registers
// ----------------------------------------------------------------------------
#define R_SMPLRT_DIV   0x19
#define R_CONFIG       0x1A
#define R_GYRO_CONFIG  0x1B
#define R_ACCEL_CONFIG 0x1C
#define R_INT_PIN_CFG  0x37
#define R_INT_ENABLE   0x38
#define R_INT_STATUS   0x3A
#define R_ACCEL_XOUT_H 0x3B
#define R_PWR_MGMT_1   0x6B
#define R_WHO_AM_I     0x75

// ----------------------------------------------------------------------------
// Protocol
// ----------------------------------------------------------------------------
#define SYNC0          0xA5
#define SYNC1          0x5A
#define T_IMU          0x01
#define T_TEXT         0x02

#define F_I2C_ERR      0x01
#define F_GYRO_CAL     0x02
#define F_ACC_SAT      0x04
#define F_GYR_SAT      0x08
#define F_WHOAMI_ODD   0x10
#define F_DROPPED      0x20
#define F_GATED        0x40   // accel correction was gated off for most of the window
#define F_STILL        0x80   // stillness detector: rig has been still for >= 2 s

// ----------------------------------------------------------------------------
// Persistent settings
// ----------------------------------------------------------------------------
struct Persist {
  uint16_t magic;
  float    gbias[3];      // rad/s
  uint8_t  dlpf;
  uint8_t  outDiv;
  uint8_t  calibrated;
  uint8_t  autoCal;       // learn gyro bias whenever the rig is still
  float    accOfs[3];     // accelerometer zero-g offset, g (ACCCAL, 6-position calibration)
  float    accScl[3];     // accelerometer scale factor
  float    gRef;          // learned |a| at rest, g (absorbs what calibration leaves)
  uint8_t  crc;
};
static const uint16_t PERSIST_MAGIC = 0xAD03;
static Persist P;

// ----------------------------------------------------------------------------
// State
// ----------------------------------------------------------------------------
static float accLsbPerG, gyrLsbPerDps;
static float q0 = 1, q1 = 0, q2 = 0, q3 = 0;
static float ix = 0, iy = 0, iz = 0;     // Mahony integral terms
static uint8_t whoAmI = 0;
static int16_t tempRaw = 0;                     // MPU die temperature (raw), for DIAG
static float   dRawA[3], dRawG[3]; static uint32_t dN = 0;   // DIAG: raw means since the last DIAG
static bool    i2cOk = false;
static bool    streaming = true;
static uint8_t seq = 0;
static uint32_t nextSampleUs = 0;
static uint32_t lastReinitMs = 0;
static uint32_t lastHostCheckMs = 0;
static bool    hostOpen = false;
static uint16_t droppedFrames = 0;

// window accumulators
static float   wAx, wAy, wAz, wGx, wGy, wGz;
static float   wPeakDev, wVibSq;
static uint8_t wN, wGatedN;
static uint8_t wFlags;
static float   hpPrev[3], hpOut[3];      // high-pass filter state (accel, g)
static float   hpAlpha;

// stillness detector state
static float   sAcc[3], sGyr[3], sGmin[3], sGmax[3], sAnMin, sAnMax, sAnSum;
static float   sPrevAcc[3];
static bool    sPrevValid = false;
static uint16_t sN = 0;
static uint8_t sStillWindows = 0;
static bool    isStill = false;
static bool    biasDirty = false;
static bool    biasSavedOnce = false;
static uint32_t lastBiasSaveMs = 0;
static uint16_t biasUpdates = 0;

// command line buffer
static char    cmdBuf[40];
static uint8_t cmdLen = 0;

// ----------------------------------------------------------------------------
// CRC-8 (poly 0x07, init 0x00) — same as the plugin's Crc8.Compute
// ----------------------------------------------------------------------------
static uint8_t crc8(const uint8_t *d, uint8_t n, uint8_t c = 0) {
  while (n--) {
    c ^= *d++;
    for (uint8_t i = 0; i < 8; i++) c = (c & 0x80) ? (uint8_t)((c << 1) ^ 0x07) : (uint8_t)(c << 1);
  }
  return c;
}

// ----------------------------------------------------------------------------
// EEPROM
// ----------------------------------------------------------------------------
static void persistDefaults() {
  P.magic = PERSIST_MAGIC;
  P.gbias[0] = P.gbias[1] = P.gbias[2] = 0;
  P.dlpf = DEFAULT_DLPF;
  P.outDiv = DEFAULT_OUT_DIV;
  P.calibrated = 0;
  P.autoCal = 1;
  for (uint8_t i = 0; i < 3; i++) { P.accOfs[i] = 0.0f; P.accScl[i] = 1.0f; }
  P.gRef = 1.0f;
}
#define PERSIST_CRC_LEN ((uint8_t)offsetof(Persist, crc))
static void persistLoad() {
  EEPROM.get(0, P);
  uint8_t c = crc8((const uint8_t *)&P, PERSIST_CRC_LEN);
  if (P.magic != PERSIST_MAGIC || c != P.crc || P.dlpf > 6 || P.outDiv == 0 || P.outDiv > 50 ||
      !(P.gRef > 0.7f && P.gRef < 1.3f))
    persistDefaults();
}
static void persistSave() {
  P.crc = crc8((const uint8_t *)&P, PERSIST_CRC_LEN);
  EEPROM.put(0, P);
  storeCommit();
}

// ----------------------------------------------------------------------------
// I2C helpers
// ----------------------------------------------------------------------------
static bool wr(uint8_t reg, uint8_t val) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(reg);
  Wire.write(val);
  return Wire.endTransmission() == 0;
}
static bool rd(uint8_t reg, uint8_t *buf, uint8_t n) {
  Wire.beginTransmission(MPU_ADDR);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom((uint8_t)MPU_ADDR, n) != n) return false;
  for (uint8_t i = 0; i < n; i++) buf[i] = Wire.read();
  return true;
}

#if HAS_SENSOR_POWER
static uint8_t vccOnLevel = HIGH;      // nice!nano v2: HIGH = VCC on; some clones invert
static bool    vccLevelKnown = false;
#endif

static void wireStart() {
  Wire.begin();
  Wire.setClock(400000);
#if defined(WIRE_HAS_TIMEOUT) || defined(NRF52_SERIES) || defined(ARDUINO_ARCH_NRF52)
  Wire.setWireTimeout(3000 /*us*/, true);   // never hang the loop on a glitched bus
#endif
}

static bool mpuProbe() {
#if HAS_SENSOR_POWER
  if (vccLevelKnown) { sensorPowerOn(vccOnLevel); return rd(R_WHO_AM_I, &whoAmI, 1); }
  for (uint8_t attempt = 0; attempt < 2; attempt++) {
    uint8_t lvl = attempt ? (uint8_t)!vccOnLevel : vccOnLevel;
    sensorPowerOn(lvl);
    delay(60);                          // GY-521 LDO + MPU power-up
    wdtFeed();
    if (rd(R_WHO_AM_I, &whoAmI, 1)) { vccOnLevel = lvl; vccLevelKnown = true; return true; }
  }
  sensorPowerOn(vccOnLevel);
  return false;
#else
  return rd(R_WHO_AM_I, &whoAmI, 1);
#endif
}

static bool mpuInit() {
  wireStart();
  if (!mpuProbe()) return false;
  if (!wr(R_PWR_MGMT_1, 0x80)) return false;  // device reset
  delay(100);
  wdtFeed();
  bool ok = wr(R_PWR_MGMT_1, 0x01)              // wake, PLL with X gyro ref
         && wr(R_CONFIG, P.dlpf & 0x07)         // DLPF -> 1 kHz internal rate
         && wr(R_SMPLRT_DIV, (1000 / SAMPLE_HZ) - 1)
         && wr(R_GYRO_CONFIG, GYRO_FS << 3)
         && wr(R_ACCEL_CONFIG, ACCEL_FS << 3)
         && wr(R_INT_PIN_CFG, 0x10)             // INT clears on any read
         && wr(R_INT_ENABLE, 0x01);             // data ready
  accLsbPerG   = 16384.0f / (float)(1 << ACCEL_FS);
  gyrLsbPerDps = 131.0f   / (float)(1 << GYRO_FS);
  return ok;
}

// ----------------------------------------------------------------------------
// Framing
// ----------------------------------------------------------------------------
static void sendFrame(uint8_t type, const uint8_t *payload, uint8_t len) {
  if (!hostOpen) return;
  uint8_t hdr[5] = { SYNC0, SYNC1, type, len, seq++ };
  uint8_t c = crc8(hdr + 2, 3);
  c = crc8(payload, len, c);
  // Never block the sensor loop on a slow/absent host: drop instead.
  if (Serial.availableForWrite() < (int)(len + 6)) { droppedFrames++; wFlags |= F_DROPPED; return; }
  Serial.write(hdr, 5);
  Serial.write(payload, len);
  Serial.write(c);
}

static void sendText(const char *s) {
  sendFrame(T_TEXT, (const uint8_t *)s, (uint8_t)strnlen(s, 60));
}

static inline void put16(uint8_t *&p, int16_t v) { *p++ = (uint8_t)v; *p++ = (uint8_t)(v >> 8); }
static inline void putU16(uint8_t *&p, uint16_t v) { *p++ = (uint8_t)v; *p++ = (uint8_t)(v >> 8); }
static inline int16_t clamp16(float v) { return v > 32767 ? 32767 : (v < -32768 ? -32768 : (int16_t)lroundf(v)); }

static void emitImuFrame() {
  uint8_t buf[30];
  uint8_t *p = buf;
  uint32_t t = micros();
  *p++ = (uint8_t)t; *p++ = (uint8_t)(t >> 8); *p++ = (uint8_t)(t >> 16); *p++ = (uint8_t)(t >> 24);
  float inv = 1.0f / (float)wN;
  put16(p, clamp16(wAx * inv * 1000.0f));          // mg
  put16(p, clamp16(wAy * inv * 1000.0f));
  put16(p, clamp16(wAz * inv * 1000.0f));
  put16(p, clamp16(wGx * inv * 57.29578f * 10.0f)); // 0.1 deg/s
  put16(p, clamp16(wGy * inv * 57.29578f * 10.0f));
  put16(p, clamp16(wGz * inv * 57.29578f * 10.0f));
  put16(p, clamp16(q0 * 16384.0f));                 // Q14 quaternion
  put16(p, clamp16(q1 * 16384.0f));
  put16(p, clamp16(q2 * 16384.0f));
  put16(p, clamp16(q3 * 16384.0f));
  putU16(p, (uint16_t)min(65535.0f, wPeakDev * 1000.0f));              // mg
  putU16(p, (uint16_t)min(65535.0f, sqrtf(wVibSq * inv) * 1000.0f));  // mg rms
  uint8_t f = wFlags;
  if (!i2cOk) f |= F_I2C_ERR;
  if (P.calibrated) f |= F_GYRO_CAL;
  if (whoAmI != 0x68) f |= F_WHOAMI_ODD;
  if (wGatedN > wN / 2) f |= F_GATED;
  if (isStill) f |= F_STILL;
  *p++ = f;
  *p++ = (uint8_t)min(droppedFrames, (uint16_t)255);
  sendFrame(T_IMU, buf, (uint8_t)(p - buf));

  wAx = wAy = wAz = wGx = wGy = wGz = 0;
  wPeakDev = 0; wVibSq = 0; wN = 0; wGatedN = 0; wFlags = 0;
}

// ----------------------------------------------------------------------------
// Mahony AHRS (6-axis)
// ----------------------------------------------------------------------------
static void mahony(float gx, float gy, float gz, float ax, float ay, float az, float dt, bool useAcc) {
  if (useAcc) {
    float n = ax * ax + ay * ay + az * az;
    if (n > 0.0f) {
      n = 1.0f / sqrtf(n);
      ax *= n; ay *= n; az *= n;
      // estimated gravity direction
      float vx = 2.0f * (q1 * q3 - q0 * q2);
      float vy = 2.0f * (q0 * q1 + q2 * q3);
      float vz = q0 * q0 - q1 * q1 - q2 * q2 + q3 * q3;
      float ex = (ay * vz - az * vy);
      float ey = (az * vx - ax * vz);
      float ez = (ax * vy - ay * vx);
      if (MAHONY_KI > 0.0f) {
        ix += MAHONY_KI * ex * dt; iy += MAHONY_KI * ey * dt; iz += MAHONY_KI * ez * dt;
        gx += ix; gy += iy; gz += iz;
      }
      gx += MAHONY_KP * ex; gy += MAHONY_KP * ey; gz += MAHONY_KP * ez;
    }
  }
  gx *= 0.5f * dt; gy *= 0.5f * dt; gz *= 0.5f * dt;
  float qa = q0, qb = q1, qc = q2;
  q0 += (-qb * gx - qc * gy - q3 * gz);
  q1 += (qa * gx + qc * gz - q3 * gy);
  q2 += (qa * gy - qb * gz + q3 * gx);
  q3 += (qa * gz + qb * gy - qc * gx);
  float n = 1.0f / sqrtf(q0 * q0 + q1 * q1 + q2 * q2 + q3 * q3);
  q0 *= n; q1 *= n; q2 *= n; q3 *= n;
}

// Seed attitude from gravity so we don't spend seconds converging at boot
static void seedFromAccel(float ax, float ay, float az) {
  float roll  = atan2f(ay, az);
  float pitch = atan2f(-ax, sqrtf(ay * ay + az * az));
  float cr = cosf(roll * 0.5f), sr = sinf(roll * 0.5f);
  float cp = cosf(pitch * 0.5f), sp = sinf(pitch * 0.5f);
  q0 = cr * cp; q1 = sr * cp; q2 = cr * sp; q3 = -sr * sp;
}

// ----------------------------------------------------------------------------
// Sample
// ----------------------------------------------------------------------------
static bool readRaw(float &ax, float &ay, float &az, float &gx, float &gy, float &gz, uint8_t &satFlags) {
  uint8_t b[14];
  if (!rd(R_ACCEL_XOUT_H, b, 14)) return false;
  int16_t r[7];
  for (uint8_t i = 0; i < 7; i++) r[i] = (int16_t)((b[2 * i] << 8) | b[2 * i + 1]);
  satFlags = 0;
  for (uint8_t i = 0; i < 3; i++) if (r[i] == 32767 || r[i] == -32768) satFlags |= F_ACC_SAT;
  for (uint8_t i = 4; i < 7; i++) if (r[i] == 32767 || r[i] == -32768) satFlags |= F_GYR_SAT;
  tempRaw = r[3];
  float rax = r[0] / accLsbPerG, ray = r[1] / accLsbPerG, raz = r[2] / accLsbPerG;
  const float d2r = 0.01745329f;
  if (dN < 100000UL) {
    dRawA[0] += rax; dRawA[1] += ray; dRawA[2] += raz;
    for (uint8_t i = 0; i < 3; i++) dRawG[i] += r[4 + i] / gyrLsbPerDps;
    dN++;
  }
  ax = (rax - P.accOfs[0]) * P.accScl[0];
  ay = (ray - P.accOfs[1]) * P.accScl[1];
  az = (raz - P.accOfs[2]) * P.accScl[2];
  gx = r[4] / gyrLsbPerDps * d2r - P.gbias[0];
  gy = r[5] / gyrLsbPerDps * d2r - P.gbias[1];
  gz = r[6] / gyrLsbPerDps * d2r - P.gbias[2];
  return true;
}

// ----------------------------------------------------------------------------
// Stillness detector + background gyro-bias learning.
// A window is "still" when the gyro barely moves (peak-to-peak), |a| stays ~1 g and the
// gravity direction did not turn since the previous window (so a slow tilt is never learned
// as bias). After STILL_WINDOWS still windows in a row the rig counts as still and every
// further still window nudges the bias towards that window's mean raw gyro reading.
// ----------------------------------------------------------------------------
static void stillReset() {
  for (uint8_t i = 0; i < 3; i++) { sAcc[i] = 0; sGyr[i] = 0; sGmin[i] = 1e9f; sGmax[i] = -1e9f; }
  sAnMin = 99.0f; sAnMax = 0.0f; sAnSum = 0.0f; sN = 0;
}

static void stillUpdate(float ax, float ay, float az, float gx, float gy, float gz, float an) {
  const float d2r = 0.01745329f;
  float raw[3] = { gx + P.gbias[0], gy + P.gbias[1], gz + P.gbias[2] };   // undo current bias
  float a[3] = { ax, ay, az };
  for (uint8_t i = 0; i < 3; i++) {
    sAcc[i] += a[i]; sGyr[i] += raw[i];
    if (raw[i] < sGmin[i]) sGmin[i] = raw[i];
    if (raw[i] > sGmax[i]) sGmax[i] = raw[i];
  }
  if (an < sAnMin) sAnMin = an;
  if (an > sAnMax) sAnMax = an;
  sAnSum += an;
  if (++sN < STILL_WIN) return;

  // ---- window complete ----
  float m[3], g[3];
  // Steadiness of |a| (not its absolute value): a cheap MPU-6050 often reads a few % off 1 g.
  // At rest |a| is gravity, whatever the sensor says it is, so a steady window also teaches gRef.
  float anMean = sAnSum / STILL_WIN;
  bool still = (sAnMax - sAnMin) < STILL_ACC_PP_G && fabsf(anMean - 1.0f) < STILL_ACC_ABS_G;
  for (uint8_t i = 0; i < 3; i++) {
    m[i] = sAcc[i] / STILL_WIN;
    g[i] = sGyr[i] / STILL_WIN;
    if (sGmax[i] - sGmin[i] > STILL_GYRO_PP_DPS * d2r) still = false;
    if (fabsf(g[i]) > BIAS_MAX_ABS_DPS * d2r) still = false;
  }
  if (sPrevValid) {
    // angle between this window's and the previous window's gravity vector
    float cx = m[1] * sPrevAcc[2] - m[2] * sPrevAcc[1];
    float cy = m[2] * sPrevAcc[0] - m[0] * sPrevAcc[2];
    float cz = m[0] * sPrevAcc[1] - m[1] * sPrevAcc[0];
    float n1 = sqrtf(m[0] * m[0] + m[1] * m[1] + m[2] * m[2]);
    float n2 = sqrtf(sPrevAcc[0] * sPrevAcc[0] + sPrevAcc[1] * sPrevAcc[1] + sPrevAcc[2] * sPrevAcc[2]);
    float s = sqrtf(cx * cx + cy * cy + cz * cz) / (n1 * n2 + 1e-9f);
    if (s > sinf(STILL_TILT_DEG * d2r)) still = false;
  } else still = false;
  for (uint8_t i = 0; i < 3; i++) sPrevAcc[i] = m[i];
  sPrevValid = true;

  if (still) { if (sStillWindows < 255) sStillWindows++; }
  else sStillWindows = 0;
  isStill = sStillWindows >= STILL_WINDOWS;
  if (isStill) {                       // learn the resting |a| (scale/offset error of this sensor)
    P.gRef += GREF_ALPHA * (anMean - P.gRef);
    if (P.gRef < 0.75f) P.gRef = 0.75f;
    if (P.gRef > 1.25f) P.gRef = 1.25f;
  }

  if (isStill && P.autoCal) {
    const float maxStep = BIAS_MAX_STEP_DPS * d2r;
    for (uint8_t i = 0; i < 3; i++) {
      float step = BIAS_ALPHA * (g[i] - P.gbias[i]);
      if (step > maxStep) step = maxStep;
      if (step < -maxStep) step = -maxStep;
      P.gbias[i] += step;
    }
    P.calibrated = 1;
    biasDirty = true;
    biasUpdates++;
    uint32_t now = millis();
    if (!biasSavedOnce || now - lastBiasSaveMs > BIAS_SAVE_MS) {
      persistSave();                       // ~1 write per boot + 1 per 10 min parked: fine for EEPROM
      lastBiasSaveMs = now; biasSavedOnce = true; biasDirty = false;
    }
  }
  stillReset();
}

static void sampleOnce(float dt) {
  float ax, ay, az, gx, gy, gz;
  uint8_t sat;
  if (!readRaw(ax, ay, az, gx, gy, gz, sat)) { i2cOk = false; return; }
  wFlags |= sat;

  float an = sqrtf(ax * ax + ay * ay + az * az);
  float anRel = an / P.gRef;            // |a| relative to the learned resting value
  bool useAcc = (anRel > ACC_GATE_LO && anRel < ACC_GATE_HI);
  if (!useAcc) wGatedN++;
  mahony(gx, gy, gz, ax, ay, az, dt, useAcc);
  stillUpdate(ax, ay, az, gx, gy, gz, an);

  // vibration: first-order high-pass per axis, magnitude squared
  float vib = 0;
  float a[3] = { ax, ay, az };
  for (uint8_t i = 0; i < 3; i++) {
    hpOut[i] = hpAlpha * (hpOut[i] + a[i] - hpPrev[i]);
    hpPrev[i] = a[i];
    vib += hpOut[i] * hpOut[i];
  }
  wVibSq += vib;
  float dev = fabsf(an - P.gRef);
  if (dev > wPeakDev) wPeakDev = dev;

  wAx += ax; wAy += ay; wAz += az;
  wGx += gx; wGy += gy; wGz += gz;
  wN++;
  if (wN >= P.outDiv) {
    if (streaming) emitImuFrame();
    else { wAx = wAy = wAz = wGx = wGy = wGz = 0; wPeakDev = wVibSq = 0; wN = wGatedN = 0; wFlags = 0; }
  }
}

// ----------------------------------------------------------------------------
// Gyro bias calibration (rig must be still: motion parked)
// ----------------------------------------------------------------------------
static void gyroCal() {
  const uint16_t N = 1000;
  float sx = 0, sy = 0, sz = 0, mx = 0;
  float old[3] = { P.gbias[0], P.gbias[1], P.gbias[2] };
  P.gbias[0] = P.gbias[1] = P.gbias[2] = 0;
  for (uint16_t i = 0; i < N; i++) {
    float ax, ay, az, gx, gy, gz; uint8_t s;
    if (!readRaw(ax, ay, az, gx, gy, gz, s)) { memcpy(P.gbias, old, sizeof(old)); sendText("ERR GYROCAL i2c"); return; }
    sx += gx; sy += gy; sz += gz;
    float m = fabsf(gx) + fabsf(gy) + fabsf(gz);
    if (m > mx) mx = m;
    delayMicroseconds(1800);
    if ((i & 63) == 0) wdtFeed();
  }
  if (mx > 0.35f) {   // > ~20 deg/s summed: something moved
    memcpy(P.gbias, old, sizeof(old));
    sendText("ERR GYROCAL motion");
    return;
  }
  P.gbias[0] = sx / N; P.gbias[1] = sy / N; P.gbias[2] = sz / N;
  stillReset(); sPrevValid = false; sStillWindows = 0;
  P.calibrated = 1;
  persistSave();
  sendText("OK GYROCAL");
}

// ----------------------------------------------------------------------------
// Commands (ASCII, newline terminated)
//   PING | INFO | GYROCAL | DLPF n (0..6) | ODR n (out divider 1..50)
//   STREAM 0|1 | DEFAULTS | REBOOT | AUTOCAL 0|1 | BIAS | DIAG | ACC | ACCCAL ox oy oz sx sy sz | ACCCAL 0
// ----------------------------------------------------------------------------
static void handleCmd(char *c) {
  char out[60];
  if (!strcmp(c, "PING")) { sendText("PONG"); }
  else if (!strcmp(c, "INFO")) {
    snprintf(out, sizeof(out), "INFO fw=%s who=0x%02X out=%d dlpf=%d cal=%d auto=%d",
             FW_VERSION, whoAmI, SAMPLE_HZ / P.outDiv, P.dlpf, P.calibrated, P.autoCal);
    sendText(out);
  }
  else if (!strcmp(c, "GYROCAL")) { gyroCal(); }
  else if (!strncmp(c, "DLPF ", 5)) {
    int v = atoi(c + 5);
    if (v >= 0 && v <= 6) { P.dlpf = (uint8_t)v; persistSave(); wr(R_CONFIG, P.dlpf); sendText("OK DLPF"); }
    else sendText("ERR DLPF range");
  }
  else if (!strncmp(c, "ODR ", 4)) {
    int v = atoi(c + 4);
    if (v >= 1 && v <= 50) { P.outDiv = (uint8_t)v; persistSave(); sendText("OK ODR"); }
    else sendText("ERR ODR range");
  }
  else if (!strncmp(c, "STREAM ", 7)) { streaming = c[7] == '1'; sendText("OK STREAM"); }
  else if (!strcmp(c, "DEFAULTS")) { persistDefaults(); persistSave(); i2cOk = mpuInit(); sendText("OK DEFAULTS"); }
  else if (!strcmp(c, "REBOOT")) { sendText("OK REBOOT"); Serial.flush(); hwReboot(); }
  else if (!strncmp(c, "AUTOCAL ", 8)) { P.autoCal = c[8] == '1'; persistSave(); sendText(P.autoCal ? "OK AUTOCAL 1" : "OK AUTOCAL 0"); }
  else if (!strcmp(c, "BIAS")) {
    // bias in 0.001 deg/s, plus how many still windows updated it since boot
    snprintf(out, sizeof(out), "BIAS %ld %ld %ld n=%u still=%d",
             lroundf(P.gbias[0] * 57295.78f), lroundf(P.gbias[1] * 57295.78f), lroundf(P.gbias[2] * 57295.78f),
             biasUpdates, isStill ? 1 : 0);
    sendText(out);
  }
  else if (!strcmp(c, "DIAG")) {
    // die temperature, raw accel (mg) and raw gyro (mdps) means since the last DIAG, learned |a| at rest
    float n = dN ? (float)dN : 1.0f;
    snprintf(out, sizeof(out), "DIAG T=%d a=%ld %ld %ld w=%ld %ld %ld r=%d",
             (int)lroundf(tempRaw / 34.0f + 365.3f),   // 0.1 degC
             lroundf(dRawA[0] / n * 1000), lroundf(dRawA[1] / n * 1000), lroundf(dRawA[2] / n * 1000),
             lroundf(dRawG[0] / n * 1000), lroundf(dRawG[1] / n * 1000), lroundf(dRawG[2] / n * 1000),
             (int)lroundf(P.gRef * 1000));
    for (uint8_t i = 0; i < 3; i++) dRawA[i] = dRawG[i] = 0;
    dN = 0;
    sendText(out);
  }
  else if (!strcmp(c, "ACC")) {
    snprintf(out, sizeof(out), "ACC o=%ld %ld %ld s=%ld %ld %ld",
             lroundf(P.accOfs[0] * 1000), lroundf(P.accOfs[1] * 1000), lroundf(P.accOfs[2] * 1000),
             lroundf(P.accScl[0] * 10000), lroundf(P.accScl[1] * 10000), lroundf(P.accScl[2] * 10000));
    sendText(out);
  }
  else if (!strncmp(c, "ACCCAL ", 7)) {
    // ACCCAL ox oy oz sx sy sz  (offset in mg, scale in 1/10000) | ACCCAL 0 = no correction
    long v[6]; int n = sscanf(c + 7, "%ld %ld %ld %ld %ld %ld", &v[0], &v[1], &v[2], &v[3], &v[4], &v[5]);
    if (n == 1 && v[0] == 0) {
      for (uint8_t i = 0; i < 3; i++) { P.accOfs[i] = 0.0f; P.accScl[i] = 1.0f; }
    } else if (n == 6 && labs(v[0]) < 500 && labs(v[1]) < 500 && labs(v[2]) < 500 &&
               v[3] > 7000 && v[3] < 13000 && v[4] > 7000 && v[4] < 13000 && v[5] > 7000 && v[5] < 13000) {
      for (uint8_t i = 0; i < 3; i++) { P.accOfs[i] = v[i] / 1000.0f; P.accScl[i] = v[3 + i] / 10000.0f; }
    } else { sendText("ERR ACCCAL args"); return; }
    P.gRef = 1.0f;                       // relearned from the corrected readings
    persistSave();
    sendText("OK ACCCAL");
  }
  else sendText("ERR unknown");
}

static void pollSerial() {
  while (Serial.available()) {
    char ch = (char)Serial.read();
    hostOpen = true;   // the host is talking to us, so the port is open (DTR is only polled 2x/s)
    if (ch == '\n' || ch == '\r') {
      if (cmdLen) { cmdBuf[cmdLen] = 0; handleCmd(cmdBuf); cmdLen = 0; }
    } else if (cmdLen < sizeof(cmdBuf) - 1) {
      cmdBuf[cmdLen++] = ch;
    }
  }
}

// ----------------------------------------------------------------------------
void setup() {
  wdtBoot();
  pinMode(LED_PIN, OUTPUT);
  Serial.begin(SERIAL_BAUD);
  storeBegin();
  persistLoad();

  float dt = 1.0f / SAMPLE_HZ;
  float rc = 1.0f / (2.0f * PI * VIB_HP_HZ);
  hpAlpha = rc / (rc + dt);

  stillReset();
  i2cOk = mpuInit();
  if (i2cOk) {
    float ax, ay, az, gx, gy, gz; uint8_t s;
    delay(50);
    if (readRaw(ax, ay, az, gx, gy, gz, s)) seedFromAccel(ax, ay, az);
  }
  wdtStart();
  nextSampleUs = micros();
}

void loop() {
  wdtFeed();
  uint32_t nowMs = millis();

  // Host presence (DTR) — checking Serial costs ~10 ms on some cores, so only 2x/s
  if (nowMs - lastHostCheckMs > 500) {
    lastHostCheckMs = nowMs;
    hostOpen = hostPresent();
    digitalWrite(LED_PIN, i2cOk ? (hostOpen ? HIGH : (nowMs / 250) & 1) : (nowMs / 60) & 1);
  }

  if (!i2cOk) {
    if (nowMs - lastReinitMs > 500) {
      lastReinitMs = nowMs;
      i2cOk = mpuInit();
      // still emit a frame so the host sees F_I2C_ERR rather than silence
      if (!i2cOk && streaming) { wN = 1; wAx = wAy = wAz = 0; emitImuFrame(); }
    }
    pollSerial();
    return;
  }

  // Fixed-rate sampling from micros(); catch up at most 3 samples, then resync
  const uint32_t periodUs = 1000000UL / SAMPLE_HZ;
  uint8_t guard = 0;
  while ((int32_t)(micros() - nextSampleUs) >= 0 && guard++ < 3) {
    sampleOnce(periodUs * 1e-6f);
    nextSampleUs += periodUs;
  }
  if (guard >= 3) nextSampleUs = micros() + periodUs;

  pollSerial();
}
