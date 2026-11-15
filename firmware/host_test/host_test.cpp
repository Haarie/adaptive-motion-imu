// Host (PC) build of the real sketch with a fake Arduino layer and a fake MPU-6050.
// Simulates the rig rolled 45° and writes the first 3 IMU frames to frames.bin so the
// C# parser test can decode exactly what the firmware encodes.
//   g++ -std=gnu++11 -I. host_test.cpp -o host_test && ./host_test frames.bin
#include <cstdio>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <vector>
#include <cstdlib>
#include <algorithm>

// ---------------- fake Arduino --------------------------------------------------------------
#define PI 3.14159265358979323846
#define HIGH 1
#define LOW 0
#define OUTPUT 1
#define LED_BUILTIN 13
#define WDTO_15MS 0
#define WDTO_250MS 5
#define USBCON 1
#define ARDUINO_ARCH_AVR 1
typedef uint8_t byte;
static uint32_t g_us = 0;
uint32_t micros() { return g_us; }
uint32_t millis() { return g_us / 1000; }
void delay(unsigned long ms) { g_us += ms * 1000; }
void delayMicroseconds(unsigned int us) { g_us += us; }
void pinMode(int, int) {}
void digitalWrite(int, int) {}
static void wdt_reset() {}
static void wdt_enable(int) {}
static void wdt_disable() {}
static uint8_t MCUSR;
using std::min;

static std::vector<uint8_t> g_out;
struct FakeSerial {
  void begin(unsigned long) {}
  int available() { return 0; }
  int read() { return -1; }
  int availableForWrite() { return 64; }
  size_t write(const uint8_t *b, size_t n) { g_out.insert(g_out.end(), b, b + n); return n; }
  size_t write(uint8_t b) { g_out.push_back(b); return 1; }
  void flush() {}
  explicit operator bool() { return true; }
} Serial;

// fake MPU: rolled 45° about X (optionally rotating), with an optional gyro bias on X
static uint8_t g_reg = 0;
static double g_biasDps = 0.0;      // gyro zero-offset the sensor reports at rest
static double g_rateDps = 0.0;      // true rotation rate about X (rig slowly moving)
static double g_noiseDps = 0.0;     // +/- uniform gyro noise
static double rollNowDeg() { return 45.0 + g_rateDps * (g_us / 1e6); }
static std::vector<uint8_t> g_rx;
static size_t g_rxi = 0;
static bool g_fixedAcc = false; static double g_fa[3];   // attitude scenario: fixed accel vector (g)
static double g_accScale = 1.0;     // accelerometer scale error (real MPU-6050s: 0.95..1.05)
static int16_t accRaw(double g) { return (int16_t)lround(g * g_accScale * 8192.0); }   // ±4 g
struct FakeWire {
  void begin() {}
  void setClock(uint32_t) {}
  void beginTransmission(uint8_t) { g_reg = 0xFF; }
  size_t write(uint8_t b) { if (g_reg == 0xFF) g_reg = b; return 1; }
  uint8_t endTransmission(bool = true) { return 0; }
  uint8_t requestFrom(uint8_t, uint8_t n) {
    g_rx.clear(); g_rxi = 0;
    if (g_reg == 0x75) { g_rx.push_back(0x68); }
    else if (g_reg == 0x3B) {
      double r = rollNowDeg() * PI / 180, s = sin(r), c = cos(r);
      double gx = g_biasDps + g_rateDps + g_noiseDps * ((rand() / (double)RAND_MAX) * 2 - 1);
      int16_t v[7] = { 0, accRaw(s), accRaw(c), 0, (int16_t)lround(gx * 65.5), 0, 0 };   // ±500 dps
      if (g_fixedAcc) { v[0] = accRaw(g_fa[0]); v[1] = accRaw(g_fa[1]); v[2] = accRaw(g_fa[2]); v[4] = 0; }
      for (int i = 0; i < 7; i++) { g_rx.push_back((uint8_t)(v[i] >> 8)); g_rx.push_back((uint8_t)v[i]); }
    }
    while (g_rx.size() < n) g_rx.push_back(0);
    return n;
  }
  int read() { return g_rxi < g_rx.size() ? g_rx[g_rxi++] : 0; }
} Wire;

struct FakeEEPROM {
  uint8_t mem[64];
  FakeEEPROM() { memset(mem, 0xFF, sizeof(mem)); }
  template <typename T> T &get(int a, T &t) { memcpy(&t, mem + a, sizeof(T)); return t; }
  template <typename T> const T &put(int a, const T &t) { memcpy(mem + a, &t, sizeof(T)); return t; }
} EEPROM;


#include "../AdaptiveMotionIMU/AdaptiveMotionIMU.ino"

// Scenario mode: ./host_test bias <bias_dps> <rate_dps> <seconds> [acc_scale] -> prints learned bias (deg/s) and still flag
static int scenario(double bias, double rate, double secs) {
  g_biasDps = bias; g_rateDps = rate; g_noiseDps = 0.3;
  setup();
  uint32_t end = g_us + (uint32_t)(secs * 1e6);
  while (g_us < end) { g_us += 100; loop(); }
  printf("learned=%.3f still=%d updates=%u\n", P.gbias[0] * 57.29578, isStill ? 1 : 0, biasUpdates);
  return 0;
}

// ./host_test att ax ay az -> quaternion + script's tilt A/B after 20 s at a fixed accel vector
static int attitude(double ax, double ay, double az) {
  g_fixedAcc = true; g_fa[0] = ax; g_fa[1] = ay; g_fa[2] = az;
  setup();
  uint32_t end = g_us + 20000000UL;
  while (g_us < end) { g_us += 100; loop(); }
  double sp = 2 * (q0 * q2 - q3 * q1); if (sp > 1) sp = 1; if (sp < -1) sp = -1;
  printf("gRef=%.3f still=%d q=(%.4f %.4f %.4f %.4f) A=%.1f B=%.1f\n", P.gRef, isStill ? 1 : 0, q0, q1, q2, q3, asin(sp) * 57.2958,
         atan2(2 * (q0 * q1 + q2 * q3), 1 - 2 * (q1 * q1 + q2 * q2)) * 57.2958);
  return 0;
}

int main(int argc, char **argv) {
  if (argc > 4 && !strcmp(argv[1], "att")) return attitude(atof(argv[2]), atof(argv[3]), atof(argv[4]));
  if (argc > 4 && !strcmp(argv[1], "bias")) { if (argc > 5) g_accScale = atof(argv[5]); return scenario(atof(argv[2]), atof(argv[3]), atof(argv[4])); }
  setup();
  // run until 3 IMU frames (type 0x01) were emitted
  int frames = 0;
  for (int step = 0; step < 200000 && frames < 3; step++) {
    g_us += 100;
    size_t before = g_out.size();
    loop();
    if (g_out.size() > before && g_out[before + 2] == 0x01) frames++;
  }
  // keep only IMU frames
  std::vector<uint8_t> imu;
  for (size_t i = 0; i + 5 < g_out.size();) {
    size_t len = 6 + g_out[i + 3];
    if (g_out[i + 2] == 0x01) imu.insert(imu.end(), g_out.begin() + i, g_out.begin() + i + len);
    i += len;
  }
  FILE *f = fopen(argc > 1 ? argv[1] : "frames.bin", "wb");
  fwrite(imu.data(), 1, imu.size(), f);
  fclose(f);
  printf("frames=%d bytes=%zu q=(%.4f %.4f %.4f %.4f)\n", frames, imu.size(), q0, q1, q2, q3);
  return frames == 3 ? 0 : 1;
}
