#!/usr/bin/env bash
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
: "${BA_GAME_MANAGED:?Set BA_GAME_MANAGED to a local game Managed DLL directory}"
DOTNET="${DOTNET:-dotnet}"
mkdir -p "$repo/build/deps" "$repo/build/refs"
# Local references only; never packaged. Optional exact Windows game assembly overrides a Mac reference set.
python3 - "$BA_GAME_MANAGED" "$repo/build/refs" "${BA_GAME_ASSEMBLY:-}" <<'PY'
import pathlib, shutil, sys
src, dst, override = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), sys.argv[3]
for old in dst.glob('*.dll'): old.unlink()
for p in src.glob('*.dll'): shutil.copy2(p, dst/p.name)
if override: shutil.copy2(override, dst/'BigAmbitions.dll')
if not (dst/'BigAmbitions.dll').is_file(): raise SystemExit('Missing BigAmbitions.dll')
PY
if [ ! -f "$repo/build/deps/0Harmony.dll" ]; then
  curl -fsSL https://api.nuget.org/v3-flatcontainer/lib.harmony/2.4.2/lib.harmony.2.4.2.nupkg -o "$repo/build/deps/harmony.nupkg"
  echo "d64592e53090464559fce48612c9ca7c8dc73113841376b7aa3455f46fc5d579  $repo/build/deps/harmony.nupkg" | shasum -a 256 -c -
  unzip -p "$repo/build/deps/harmony.nupkg" lib/net472/0Harmony.dll > "$repo/build/deps/0Harmony.dll"
  unzip -p "$repo/build/deps/harmony.nupkg" LICENSE > "$repo/build/deps/Harmony-LICENSE.txt"
fi
echo "7b9e756306fa3d7620e02a857c8927a6ab04973f9bd8a77d3866700a6deac55c  $repo/build/deps/0Harmony.dll" | shasum -a 256 -c -
"$DOTNET" run --project "$repo/tests/Tests.csproj" -c Release
"$DOTNET" build "$repo/CityPerformance.csproj" -c Release --nologo
python3 "$repo/tools/package.py"
