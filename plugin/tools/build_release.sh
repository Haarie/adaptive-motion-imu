#!/usr/bin/env bash
# Build the plugin DLL against the REAL SimHub + .NET Framework 4.8 reference assemblies with Mono's mcs
# (no Windows needed), stamp the version, and optionally assemble a release folder.
#   NET48=<dir with mscorlib.dll ... (Microsoft.NETFramework.ReferenceAssemblies.net48 build/.NETFramework/v4.8)>
#   SIMHUB=<dir with SimHub.Plugins.dll, GameReaderCommon.dll, SimHub.Logging.dll, log4net.dll>
#   ./build_release.sh <version> [outdir]
set -euo pipefail
VER=${1:?version, e.g. 1.2.0}
OUT=${2:-$(mktemp -d)}
: "${NET48:?set NET48}" "${SIMHUB:?set SIMHUB}"
cd "$(dirname "$0")/../SimSpares.AdaptiveMotion"
mkdir -p "$OUT"
AI=$(mktemp --suffix=.cs)
cat > "$AI" <<CS
using System.Reflection;
[assembly: AssemblyTitle("Adaptive Motion (IMU) SimHub plugin")]
[assembly: AssemblyCompany("Sim-Spares")]
[assembly: AssemblyProduct("SimSpares.AdaptiveMotion")]
[assembly: AssemblyVersion("$VER.0")]
[assembly: AssemblyFileVersion("$VER.0")]
[assembly: AssemblyInformationalVersion("$VER")]
CS
mcs -langversion:latest -nostdlib -noconfig -optimize+ -debug- -target:library -out:"$OUT/SimSpares.AdaptiveMotion.dll" \
  -lib:"$NET48" -lib:"$NET48/Facades" \
  -r:mscorlib.dll -r:System.dll -r:System.Core.dll -r:System.Xml.dll -r:System.Xaml.dll -r:WindowsBase.dll \
  -r:PresentationCore.dll -r:PresentationFramework.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll -r:netstandard.dll \
  -r:"$SIMHUB/SimHub.Plugins.dll" -r:"$SIMHUB/GameReaderCommon.dll" -r:"$SIMHUB/SimHub.Logging.dll" -r:"$SIMHUB/log4net.dll" \
  "$AI" *.cs Core/*.cs IO/*.cs UI/*.cs
rm -f "$AI"
echo "built $OUT/SimSpares.AdaptiveMotion.dll ($VER) against the real SimHub references"
