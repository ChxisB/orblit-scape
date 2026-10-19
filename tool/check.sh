#!/bin/bash
# Analyzes and tests every package.
set -uo pipefail
cd "$(dirname "$0")/.."

# The Dart packages are checked with dart. A Flutter package needs flutter,
# and flutter analyze would rewrite the analysis options of a Dart one.
PACKAGES=(
  packages/scape_core
  packages/scape_server
  packages/scape_client
  packages/scape_content
  packages/scape_panels
  packages/scape_bots
)
FLUTTER_PACKAGES=(packages/scape_panels)

failures=0

# A package that is on disk and in no list is checked by nothing, and that
# looks exactly like a passing build.
for found in packages/*/; do
  name=${found%/}
  [ -f "$name/pubspec.yaml" ] || continue
  case " ${PACKAGES[*]} " in *" $name "*) continue ;; esac
  echo "  note  $name is checked by nothing"
done

for package in "${PACKAGES[@]}"; do
  echo "== $package =="
  tool=dart
  case " ${FLUTTER_PACKAGES[*]} " in *" $package "*) tool=flutter ;; esac
  (cd "$package" && "$tool" pub get > /dev/null 2>&1)

  if (cd "$package" && "$tool" analyze --fatal-infos > /tmp/orblit_scape_analyze.log 2>&1); then
    echo "  ok    analyze"
  else
    echo "  FAIL  analyze"; tail -20 /tmp/orblit_scape_analyze.log; failures=$((failures+1))
  fi

  if (cd "$package" && "$tool" test > /tmp/orblit_scape_test.log 2>&1); then
    summary=$(tr '\r' '\n' < /tmp/orblit_scape_test.log | tail -1 \
      | sed -e 's/\x1b\[[0-9;]*m//g' -e 's/^[0-9:]* //')
    echo "  ok    $summary"
  else
    echo "  FAIL  tests"; grep -iE "pixel|golden|differ|%|Expected|Actual" /tmp/orblit_scape_test.log | head -30; tail -25 /tmp/orblit_scape_test.log; failures=$((failures+1))
  fi

  if (cd "$package" && dart format --output=none --set-exit-if-changed lib test > /dev/null 2>&1); then
    echo "  ok    format"
  else
    echo "  FAIL  format"; failures=$((failures+1))
  fi
done

echo
[ "$failures" -eq 0 ] && echo "everything green" || echo "$failures failing step(s)"
exit "$failures"
