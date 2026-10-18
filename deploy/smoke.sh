#!/bin/bash
# Starts a built bundle, waits for its alive port, stops it with SIGTERM and
# checks that it exits 0 within 10 seconds.
#
# Usage: smoke.sh BUNDLE_DIR [PORT]
set -euo pipefail

bundle=${1:?usage: smoke.sh BUNDLE_DIR [PORT]}
port=${2:-7691}
work=$(mktemp -d)
trap 'kill "$pid" 2>/dev/null || true; rm -rf "$work"' EXIT

SCAPE_PORT=$port SCAPE_HEALTH_PORT=$((port + 1)) SCAPE_DATA="$work/data" \
  "$bundle/bin/scape_server" > "$work/out.log" 2> "$work/err.log" &
pid=$!

reply=""
for _ in $(seq 1 50); do
  if reply=$({ exec 3<>"/dev/tcp/127.0.0.1/$((port + 1))" \
      && head -n 1 <&3; } 2>/dev/null); then
    break
  fi
  sleep 0.2
done
if [ "$reply" != "alive" ]; then
  echo "smoke: no alive reply" >&2
  cat "$work/err.log" >&2
  exit 1
fi

kill -TERM "$pid"
# A server that ignores SIGTERM must fail the run, not hang it.
for _ in $(seq 1 50); do
  kill -0 "$pid" 2>/dev/null || break
  sleep 0.2
done
if kill -0 "$pid" 2>/dev/null; then
  echo "smoke: the server did not stop within 10 seconds" >&2
  exit 1
fi
status=0
wait "$pid" || status=$?
if [ "$status" -ne 0 ]; then
  echo "smoke: the server exited with $status" >&2
  cat "$work/err.log" >&2
  exit 1
fi
echo "smoke: alive, stopped cleanly"
