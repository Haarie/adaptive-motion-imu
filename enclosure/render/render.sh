#!/usr/bin/env bash
# Photo renders of the mounted box (needs OpenSCAD + Blender 4.x; optional python3-opencv for denoising).
# Usage: ./render.sh [samples]     -> ../../docs/enclosure_render.png, ../../docs/enclosure_render_bolts.png
set -euo pipefail
cd "$(dirname "$0")"; mkdir -p build
S=${1:-160}
for r in profile base lid inlay_green inlay_orange inlay_white bolts washers tnuts lidscrews plug cable; do
  openscad -D 'part="none"' -D "rpart=\"$r\"" -o build/$r.stl render_scene.scad >/dev/null 2>&1
done
EXPOSURE=-0.4 blender -b -P render_blender.py -- build build/front.png "$S" front >/dev/null
EXPOSURE=-0.4 blender -b -P render_blender.py -- build build/close.png "$S" close >/dev/null
python3 denoise.py build/front.png ../../docs/enclosure_render.png
python3 denoise.py build/close.png ../../docs/enclosure_render_bolts.png
