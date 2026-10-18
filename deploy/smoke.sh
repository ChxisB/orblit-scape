#!/bin/bash
# Copies a built bundle to a fresh folder and runs it with a bare
# environment, so the Dart SDK and the build folder are out of reach. Checks
# how it refuses bad starts, waits for its alive port, stops it with SIGTERM
# and checks that it exits 0 within 10 seconds.
#
# Usage: smoke.sh BUNDLE_DIR [PORT]
set -euo pipefail

bundle=${1:?usage: smoke.sh BUNDLE_DIR [PORT]}
port=${2:-7691}
work=$(mktemp -d)
pid=""
trap '[ -z "$pid" ] || kill "$pid" 2>/dev/null || true; rm -rf "$work"' EXIT

cp -R "$bundle" "$work/bundle"
server="$work/bundle/bin/scape_server"

fail() { echo "smoke: $1" >&2; exit 1; }

# A later assignment beats an earlier one, so a caller can override these.
# env replaces itself with the server, so $! below is the server's own pid.
bare=(env -i PATH=/usr/bin:/bin "SCAPE_DATA=$work/data")

# refuses CODE WHAT COMMAND...: the command must exit with CODE.
refuses() {
  local want=$1 what=$2 status=0
  shift 2
  "${bare[@]}" "$@" > /dev/null 2>&1 || status=$?
  [ "$status" -eq "$want" ] || fail "$what: wanted exit $want, got $status"
}

refuses 64 "an argument" "$server" extra
refuses 78 "a bad port" SCAPE_PORT=abc "$server"
refuses 78 "a save folder it cannot write" SCAPE_DATA=/dev/null/x "$server"

"${bare[@]}" SCAPE_PORT=$port SCAPE_HEALTH_PORT=$((port + 1)) "$server" \
  > "$work/out.log" 2> "$work/err.log" &
pid=$!

reply=""
for _ in $(seq 1 50); do
  reply=$({ exec 3<>"/dev/tcp/127.0.0.1/$((port + 1))" \
      && IFS= read -r -t 2 line <&3 && echo "$line"; } 2>/dev/null) || true
  [ "$reply" = alive ] && break
  sleep 0.2
done
if [ "$reply" != alive ]; then
  cat "$work/err.log" >&2
  fail "no alive reply"
fi

refuses 78 "a port already in use" \
  SCAPE_PORT=$port SCAPE_HEALTH_PORT=$((port + 2)) "$server"

kill -TERM "$pid"
# A server that ignores SIGTERM must fail the run, not hang it.
for _ in $(seq 1 50); do
  kill -0 "$pid" 2>/dev/null || break
  sleep 0.2
done
if kill -0 "$pid" 2>/dev/null; then
  fail "the server did not stop within 10 seconds"
fi
status=0
wait "$pid" || status=$?
pid=""
if [ "$status" -ne 0 ]; then
  cat "$work/err.log" >&2
  fail "the server exited with $status"
fi
echo "smoke: alive, refused bad starts, stopped cleanly"
