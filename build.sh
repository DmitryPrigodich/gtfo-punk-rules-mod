#!/bin/bash
# Builds and packages GtfoPunk packages for Thunderstore / r2modman.
#   ./build.sh            — build the default project and package it if it has modules
#   ./build.sh QoL        — QoL only
# Output: dist/<name>-<version>.zip  (layout: manifest.json, README.md, CHANGELOG.md, icon.png, *.dll at the root)
set -e
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
mkdir -p dist
targets=${1:-"Rules"}
for t in $targets; do
  proj="$t/GtfoPunk.$t.csproj"
  dotnet build "$proj" -c Release -nologo -v q | grep -E "error|warn|Build succeeded" || true
  dll="$t/bin/Release/net6.0/GtfoPunk.$t.dll"
  # Don't package a project without modules.
  if ! ls "$t"/Modules/*.cs >/dev/null 2>&1; then echo "[$t] no modules — not packaging"; continue; fi
  name=$(python3 -c "import json;print(json.load(open('packaging/$t/manifest.json'))['name'])")
  ver=$(python3 -c "import json;print(json.load(open('packaging/$t/manifest.json'))['version_number'])")
  stage=$(mktemp -d)
  cp packaging/$t/manifest.json packaging/$t/README.md packaging/$t/icon.png "$stage"/
  [ -f packaging/$t/CHANGELOG.md ] && cp packaging/$t/CHANGELOG.md "$stage"/
  cp "$dll" "$stage"/
  if [ -d packaging/$t/icons ]; then cp -r packaging/$t/icons "$stage"/; fi
  rm -f "dist/$name-$ver.zip"
  (cd "$stage" && zip -q -r "$OLDPWD/dist/$name-$ver.zip" .)
  rm -rf "$stage"
  echo "[$t] -> dist/$name-$ver.zip"
done
