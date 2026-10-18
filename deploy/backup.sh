#!/bin/bash
# Copies a save folder into a dated archive and keeps the newest few of that
# folder's archives. Safe while the server runs: it writes each file beside
# itself and renames it into place, so the copy holds whole files.
#
# Usage: backup.sh DATA_DIR BACKUP_DIR [KEEP]
#   DATA_DIR    the folder to copy, for example /var/lib/scape/scape
#   BACKUP_DIR  where archives go. They are named after DATA_DIR's last
#               part, so several worlds can share it
#   KEEP        how many archives of this folder to keep, 14 by default
set -euo pipefail
# Saves hold password hashes. Nobody but the caller reads an archive.
umask 077

data=${1:?usage: backup.sh DATA_DIR BACKUP_DIR [KEEP]}
dest=${2:?usage: backup.sh DATA_DIR BACKUP_DIR [KEEP]}
keep=${3:-14}

[ -d "$data" ] || { echo "backup: $data is not a folder" >&2; exit 1; }
case $keep in
  '' | *[!0-9]*) echo "backup: KEEP wants a whole number" >&2; exit 1 ;;
esac
# 10# keeps a value such as 08 from being read as octal.
keep=$((10#$keep))
[ "$keep" -ge 1 ] || { echo "backup: KEEP wants 1 or more" >&2; exit 1; }

name=$(basename "$data")
mkdir -p "$dest"
archive="$dest/$name-$(date -u +%Y%m%dT%H%M%SZ).tar.gz"
[ ! -e "$archive" ] || { echo "backup: $archive exists" >&2; exit 1; }
trap 'rm -f -- "$archive.part"' EXIT

status=0
tar -czf "$archive.part" -C "$(dirname "$data")" "$name" || status=$?
# GNU tar exits 1 when a file changed while it was read. The server renames
# files in and out while it runs, and the archive is still whole files.
[ "$status" -le 1 ] || exit "$status"
mv "$archive.part" "$archive"

# The fixed-width date keeps another world's archives out of the match. Names
# sort by date, so a reverse sort lists the newest first.
ls -1 "$dest/$name"-????????T??????Z.tar.gz | sort -r | tail -n +$((keep + 1)) |
  while IFS= read -r old; do rm -- "$old"; done
echo "$archive"
