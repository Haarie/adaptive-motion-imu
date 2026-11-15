#!/usr/bin/env bash
# Assemble a GitHub release for the plugin's Update tab: plugin DLL (built against the real SimHub
# references), firmware .uf2 and manifest.json with SHA-256 checksums.
#   NET48=... SIMHUB=... tools/make_release.sh <plugin-version> [notes-file]
# Then:  gh release create v<plugin-version> release/v<ver>/* --title "..." --notes-file <notes>
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
VER=${1:?plugin version}; NOTES=${2:-}
OUT="$ROOT/release/v$VER"; rm -rf "$OUT"; mkdir -p "$OUT"
"$ROOT/plugin/tools/build_release.sh" "$VER" "$OUT"
UF2=AdaptiveMotionIMU_promicro_nrf52840.uf2
cp "$ROOT/firmware/$UF2" "$OUT/"
FW=$(grep -m1 '#define FW_VERSION' "$ROOT/firmware/AdaptiveMotionIMU/AdaptiveMotionIMU.ino" | sed 's/.*"\(.*\)".*/\1/')
sha() { sha256sum "$1" | cut -d' ' -f1; }
python3 - "$OUT" "$VER" "$FW" "$(sha "$OUT/SimSpares.AdaptiveMotion.dll")" "$(sha "$OUT/$UF2")" "$UF2" "$NOTES" <<'PY'
import json, sys, datetime
out, ver, fw, sdll, suf2, uf2, notes = sys.argv[1:8]
m = {
  "schema": 1,
  "released": datetime.date.today().isoformat(),
  "plugin":   {"version": ver, "file": "SimSpares.AdaptiveMotion.dll", "sha256": sdll, "simhub_min": "9.0"},
  "firmware": {"version": fw, "file": uf2, "sha256": suf2, "board": "nRF52840", "softdevice": "S140 version 6.1.1"},
  "notes": open(notes).read() if notes else ""
}
json.dump(m, open(out + "/manifest.json", "w"), indent=2)
print(json.dumps(m, indent=2))
PY
echo "release files in $OUT"
