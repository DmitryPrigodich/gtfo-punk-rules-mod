#!/bin/bash
# Сборка и упаковка пакетов GtfoPunk для Thunderstore / r2modman.
#   ./build.sh            — собрать оба проекта и упаковать те, у которых есть модули
#   ./build.sh QoL        — только QoL
# Результат: dist/<name>-<version>.zip  (структура: manifest.json, README.md, CHANGELOG.md, icon.png, *.dll в корне)
set -e
cd "$(dirname "$0")"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
mkdir -p dist
targets=${1:-"Rules"}
for t in $targets; do
  proj="$t/GtfoPunk.$t.csproj"
  dotnet build "$proj" -c Release -nologo -v q | grep -E "error|warn|Build succeeded" || true
  dll="$t/bin/Release/net6.0/GtfoPunk.$t.dll"
  # Пакет без модулей не публикуем.
  if ! ls "$t"/Modules/*.cs >/dev/null 2>&1; then echo "[$t] модулей нет — пакет не собираю"; continue; fi
  name=$(python3 -c "import json;print(json.load(open('packaging/$t/manifest.json'))['name'])")
  ver=$(python3 -c "import json;print(json.load(open('packaging/$t/manifest.json'))['version_number'])")
  stage=$(mktemp -d)
  cp packaging/$t/manifest.json packaging/$t/README.md packaging/$t/icon.png "$stage"/
  [ -f packaging/$t/CHANGELOG.md ] && cp packaging/$t/CHANGELOG.md "$stage"/
  cp "$dll" "$stage"/
  rm -f "dist/$name-$ver.zip"
  (cd "$stage" && zip -q -r "$OLDPWD/dist/$name-$ver.zip" .)
  rm -rf "$stage"
  echo "[$t] -> dist/$name-$ver.zip"
done
