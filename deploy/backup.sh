#!/bin/bash
# Copies a save folder into a dated archive and keeps the newest few.
# Safe while the server runs: it writes each file beside itself and renames
# it into place, so the copy never holds half a file.
#
# Usage: backup.sh DATA_DIR BACKUP_DIR [KEEP]
#   DATA_DIR    the folder to copy, for example /var/lib/scape/scape
#   BACKUP_DIR  where archives go
#   KEEP        how many archives to keep, 14 by default
set -euo pipefail

data=${1:?usage: backup.sh DATA_DIR BACKUP_DIR [KEEP]}
dest=${2:?usage: backup.sh DATA_DIR BACKUP_DIR [KEEP]}
keep=${3:-14}

[ -d "$data" ] || { echo "backup: $data is not a folder" >&2; exit 1; }
case $keep in
  '' | *[!0-9]*) echo "backup: KEEP wants a whole number" >&2; exit 1 ;;
esac
[ "$keep" -ge 1 ] || { echo "backup: KEEP wants 1 or more" >&2; exit 1; }

mkdir -p "$dest"
archive="$dest/scape-$(date -u +%Y%m%dT%H%M%SZ).tar.gz"
tar -czf "$archive.part" -C "$(dirname "$data")" "$(basename "$data")"
mv "$archive.part" "$archive"

# Names sort by date, so the oldest come last in a reverse sort.
for old in $(ls -1 "$dest"/scape-*.tar.gz | sort -r | tail -n +$((keep + 1))); do
  rm -- "$old"
done
echo "$archive"
