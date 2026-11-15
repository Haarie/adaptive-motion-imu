#!/usr/bin/env bash
# Off-Windows checks (Linux/macOS with Mono). None of this ships in the plugin.
#   1. core unit tests
#   2. whole plugin compiled against compile-only stubs of the SimHub SDK + WPF surface it uses
#   3. live-view preview rendered to SVG/PNG through an SVG-backed DrawingContext stand-in
# The stubs mirror the real signatures but are NOT the real APIs: the authoritative build is
# `dotnet build` on the SimHub PC.
set -euo pipefail
cd "$(dirname "$0")/../SimSpares.AdaptiveMotion"
T=$(mktemp -d)
mcs -langversion:latest -out:$T/tests.exe Tests/CoreTests.cs Core/*.cs && mono $T/tests.exe ../../firmware/host_test/frames.bin
mcs -langversion:latest -target:library -r:System.dll -out:$T/plugin.dll ../tools/SimHubStubs.cs ../tools/WpfStubs.cs ../tools/FormsStubs.cs *.cs Core/*.cs IO/*.cs UI/*.cs && echo "plugin compiles against stubs"
mcs -langversion:latest -r:System.dll -out:$T/preview.exe ../tools/SimHubStubs.cs ../tools/WpfRenderSvg.cs ../tools/FormsStubs.cs ../tools/Preview.cs *.cs Core/*.cs IO/*.cs UI/*.cs
mono $T/preview.exe ../../docs/visualizer_preview.svg
command -v rsvg-convert >/dev/null && rsvg-convert -w 1350 ../../docs/visualizer_preview.svg -o ../../docs/visualizer_preview.png && echo "preview -> docs/visualizer_preview.png"
