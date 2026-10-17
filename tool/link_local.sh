#!/bin/bash
# Points the server at sibling checkouts instead of git dependencies, so a
# change to the engine or to networking can be tried here before it is pushed.
# The override is not committed, so nobody ships a build wired to a local path.
#
# Usage: link_local.sh [engine] [networking]
set -euo pipefail
cd "$(dirname "$0")/.."

resolve() { (cd "$1" 2>/dev/null && pwd) || echo ""; }

ENGINE=$(resolve "${1:-../orblit}")
NET=$(resolve "${2:-../orblit-net}")

if [ -z "$ENGINE" ] || [ -z "$NET" ]; then
  echo "Need checkouts of ChxisB/orblit and ChxisB/orblit-net beside this one,"
  echo "or pass their paths."
  exit 1
fi

cat > packages/scape_server/pubspec_overrides.yaml <<YAML
# Written by tool/link_local.sh. Not committed.
dependency_overrides:
  orblit_core:
    path: $ENGINE/packages/orblit_core
  orblit_net:
    path: $NET
YAML

(cd packages/scape_server && dart pub get > /dev/null)
echo "scape_server -> $ENGINE, $NET"
