#!/usr/bin/env bash
# Build the firmware for the "ProMicro NRF52840" (nice!nano v2 clone) without the IDE.
# Needs: arduino-cli, the ArduinoNRF core (github.com/dunknowcoding/ArduinoNRF) in
# $SKETCHBOOK/hardware/arduinonrf, and arm-none-eabi-gcc (apt: gcc-arm-none-eabi).
# Usage: SKETCHBOOK=~/sketchbook ./build_nrf.sh [arduino-cli]
set -euo pipefail
HERE=$(cd "$(dirname "$0")" && pwd)
CLI=${1:-arduino-cli}
export ARDUINO_DIRECTORIES_USER=${SKETCHBOOK:?set SKETCHBOOK}
GCC=$(dirname "$(dirname "$(command -v arm-none-eabi-gcc)")")
CTAGS=$(dirname "$(command -v ctags || echo /usr/bin/ctags)")
W=$(mktemp -d); mkdir -p "$W/fw"
# Build as a plain .cpp so no IDE prototype generation (ctags) is needed.
echo "// wrapper" > "$W/fw/fw.ino"
{ echo '#include <Arduino.h>'; cat "$HERE/AdaptiveMotionIMU/AdaptiveMotionIMU.ino"; } > "$W/fw/sketch.cpp"
"$CLI" compile -b arduinonrf:nrf52:promicro_nrf52840 --warnings all \
  --build-property "runtime.tools.arm-none-eabi-gcc.path=$GCC" \
  --build-property "runtime.tools.ctags.path=$CTAGS" \
  --output-dir "$W/out" "$W/fw" 2>&1 | grep -E "warning|error|Sketch uses|Global variables" || true
test -f "$W/out/fw.ino.hex"
cp "$W/out/fw.ino.hex" "$HERE/AdaptiveMotionIMU_promicro_nrf52840.hex"
python3 "$HERE/tools/hex2uf2.py" "$W/out/fw.ino.hex" "$HERE/AdaptiveMotionIMU_promicro_nrf52840.uf2"
