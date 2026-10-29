#!/bin/bash
# Runs every Dart exporter in tool/parity and shows what changed in
# dotnet/fixtures. A fixture changes only on purpose, and the diff is what a
# reviewer reads.
set -euo pipefail
cd "$(dirname "$0")/.."
root=$PWD

export PATH="$HOME/development/flutter/bin:$PATH"

cd tool/parity
dart pub get > /dev/null
dart analyze --fatal-infos
for exporter in bin/*.dart; do
  dart run "$exporter" "$root"
done

cd "$root"
git diff --stat -- dotnet/fixtures
