#!/usr/bin/env bash
# Compile-check the sketch with plain avr-gcc against ArduinoCore-avr (no IDE needed).
# Usage: ./build_check.sh <path-to-ArduinoCore-avr> [micro|nano]
set -euo pipefail
CORE=${1:?path to ArduinoCore-avr}
BOARD=${2:-micro}
HERE=$(cd "$(dirname "$0")" && pwd)
OUT=$(mktemp -d)
if [ "$BOARD" = micro ]; then
  MCU=atmega32u4; VAR=micro
  DEFS="-DARDUINO_AVR_MICRO -DUSB_VID=0x2341 -DUSB_PID=0x8037 -DUSB_MANUFACTURER=\\\"Arduino\\\" -DUSB_PRODUCT=\\\"AdaptiveMotionIMU\\\""
  FLASH=28672; RAM=2560
else
  MCU=atmega328p; VAR=eightanaloginputs
  DEFS="-DARDUINO_AVR_NANO"
  FLASH=30720; RAM=2048
fi
CF="-c -g -Os -w -std=gnu++11 -fpermissive -fno-exceptions -ffunction-sections -fdata-sections -fno-threadsafe-statics -flto -mmcu=$MCU -DF_CPU=16000000L -DARDUINO=10819 -DARDUINO_ARCH_AVR $DEFS"
CCF="-c -g -Os -w -std=gnu11 -ffunction-sections -fdata-sections -flto -fno-fat-lto-objects -mmcu=$MCU -DF_CPU=16000000L -DARDUINO=10819 -DARDUINO_ARCH_AVR $DEFS"
INC="-I$CORE/cores/arduino -I$CORE/variants/$VAR -I$CORE/libraries/Wire/src -I$CORE/libraries/Wire/src/utility -I$CORE/libraries/EEPROM/src"
objs=()
for f in "$CORE"/cores/arduino/*.c "$CORE"/libraries/Wire/src/utility/*.c; do
  o="$OUT/$(basename "$f").o"; eval avr-gcc $CCF $INC "$f" -o "$o"; objs+=("$o"); done
for f in "$CORE"/cores/arduino/*.cpp "$CORE"/libraries/Wire/src/*.cpp; do
  o="$OUT/$(basename "$f").o"; eval avr-g++ $CF $INC "$f" -o "$o"; objs+=("$o"); done
cp "$HERE/AdaptiveMotionIMU/AdaptiveMotionIMU.ino" "$OUT/sketch.cpp"
sed -i '1i #include <Arduino.h>' "$OUT/sketch.cpp"
eval avr-g++ $CF -Wall -Wextra $INC "$OUT/sketch.cpp" -o "$OUT/sketch.o"
avr-gcc -w -Os -g -flto -fuse-linker-plugin -Wl,--gc-sections -mmcu=$MCU -o "$OUT/fw.elf" "$OUT/sketch.o" "${objs[@]}" -lm
avr-size -A "$OUT/fw.elf" | awk -v F=$FLASH -v R=$RAM '/^.text|^.data|^.bss/{s[$1]=$2} END{t=s[".text"]+s[".data"]; r=s[".data"]+s[".bss"]; printf "flash %d/%d (%.0f%%)  ram %d/%d\n", t, F, 100*t/F, r, R}'
avr-objcopy -O ihex -R .eeprom "$OUT/fw.elf" "$HERE/AdaptiveMotionIMU_$BOARD.hex"
echo "hex: $HERE/AdaptiveMotionIMU_$BOARD.hex"
