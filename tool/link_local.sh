#!/bin/bash
# Points the packages at sibling checkouts instead of git dependencies, so a
# change to the engine or to networking can be tried here before it is pushed.
# The overrides are not committed, so nobody ships a build wired to a local
# path.
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

for dir in packages/scape_core packages/scape_content; do
  cat > "$dir/pubspec_overrides.yaml" <<YAML
# Written by tool/link_local.sh. Not committed.
dependency_overrides:
  orblit_dialogue:
    path: $ENGINE/packages/orblit_dialogue
  orblit_progress:
    path: $ENGINE/packages/orblit_progress
  orblit_quest:
    path: $ENGINE/packages/orblit_quest
YAML
done

cat > packages/scape_server/pubspec_overrides.yaml <<YAML
# Written by tool/link_local.sh. Not committed.
dependency_overrides:
  orblit_core:
    path: $ENGINE/packages/orblit_core
  orblit_dialogue:
    path: $ENGINE/packages/orblit_dialogue
  orblit_net:
    path: $NET
  orblit_progress:
    path: $ENGINE/packages/orblit_progress
  orblit_quest:
    path: $ENGINE/packages/orblit_quest
  orblit_save:
    path: $ENGINE/packages/orblit_save
YAML

# The parity exporters read scape_server, so they take its overrides.
cp packages/scape_server/pubspec_overrides.yaml tool/parity/pubspec_overrides.yaml

cat > packages/scape_client/pubspec_overrides.yaml <<YAML
# Written by tool/link_local.sh. Not committed.
dependency_overrides:
  orblit_core:
    path: $ENGINE/packages/orblit_core
  orblit_dialogue:
    path: $ENGINE/packages/orblit_dialogue
  orblit_net:
    path: $NET
  orblit_progress:
    path: $ENGINE/packages/orblit_progress
  orblit_quest:
    path: $ENGINE/packages/orblit_quest
  orblit_save:
    path: $ENGINE/packages/orblit_save
  orblit_terrain:
    path: $ENGINE/packages/orblit_terrain
YAML

# The panels take the client's overrides and the interface package.
{
  cat packages/scape_client/pubspec_overrides.yaml
  cat <<YAML
  orblit_ui:
    path: $ENGINE/packages/orblit_ui
YAML
} > packages/scape_panels/pubspec_overrides.yaml

# The bots use what the client and the server use.
cp packages/scape_client/pubspec_overrides.yaml packages/scape_bots/pubspec_overrides.yaml

(cd packages/scape_core && dart pub get > /dev/null)
(cd packages/scape_content && dart pub get > /dev/null)
(cd packages/scape_server && dart pub get > /dev/null)
(cd packages/scape_client && dart pub get > /dev/null)
(cd packages/scape_bots && dart pub get > /dev/null)
(cd packages/scape_panels && flutter pub get > /dev/null)
echo "scape packages -> $ENGINE, $NET"
