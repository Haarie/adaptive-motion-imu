#!/usr/bin/env bash
# Flat-lay product shot of the enclosure kit -> ../../docs/enclosure_kit.png
set -euo pipefail
cd "$(dirname "$0")"; mkdir -p build_kit
S=${1:-160}
for r in base lid inlay_green inlay_orange inlay_white bolts washers tnuts lidscrews; do
  openscad -D 'part="none"' -D "kpart=\"$r\"" -o build_kit/$r.stl kit_scene.scad >/dev/null 2>&1
done
BACKDROP="#161917" EXPOSURE=-0.5 blender -b -P render_blender.py -- build_kit build_kit/kit.png "$S" kit >/dev/null
python3 denoise.py build_kit/kit.png ../../docs/enclosure_kit.png
