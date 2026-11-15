#!/usr/bin/env bash
# Publish a release to the update channel read by the plugin's Update tab:
#   updates/<channel>/manifest.json            <- the plugin reads this (raw.githubusercontent.com, main branch)
#   updates/<channel>/<ver>/SimSpares.AdaptiveMotion.dll   (built against the real SimHub references)
#   updates/<channel>/<ver>/AdaptiveMotionIMU_promicro_nrf52840.uf2
# Usage:  NET48=... SIMHUB=... tools/make_release.sh <plugin-version> <notes.md> [stable|beta]
# Then commit + push updates/ (the repository must be public).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
VER=${1:?plugin version}; NOTES=${2:?notes file}; CH=${3:-stable}
DIR="$ROOT/updates/$CH"; OUT="$DIR/$VER"
rm -rf "$OUT"; mkdir -p "$OUT"
"$ROOT/plugin/tools/build_release.sh" "$VER" "$OUT"
UF2=AdaptiveMotionIMU_promicro_nrf52840.uf2
cp "$ROOT/firmware/$UF2" "$OUT/"
FW=$(grep -m1 '#define FW_VERSION' "$ROOT/firmware/AdaptiveMotionIMU/AdaptiveMotionIMU.ino" | sed 's/.*"\(.*\)".*/\1/')
python3 - "$DIR" "$VER" "$FW" "$UF2" "$NOTES" <<'PY'
import json, sys, datetime, hashlib, os
d, ver, fw, uf2, notes = sys.argv[1:6]
def f(name):
    p = os.path.join(d, ver, name)
    return {"file": name, "url": ver + "/" + name, "size": os.path.getsize(p),
            "sha256": hashlib.sha256(open(p, "rb").read()).hexdigest()}
m = {
  "schema": 2, "tag": "v" + ver, "name": "Adaptive Motion (IMU) " + ver,
  "released": datetime.date.today().isoformat(),
  "plugin":   dict(version=ver, simhub_min="9.0", **f("SimSpares.AdaptiveMotion.dll")),
  "firmware": dict(version=fw, board="nRF52840", softdevice="S140 version 6.1.1", **f(uf2)),
  "notes": open(notes).read(),
}
json.dump(m, open(os.path.join(d, "manifest.json"), "w"), indent=2)
print(json.dumps({k: v for k, v in m.items() if k != "notes"}, indent=2))
PY
echo "channel '$CH' now offers plugin $VER / firmware $FW  ->  commit and push updates/$CH"
