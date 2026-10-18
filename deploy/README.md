# Deploying the server

The server ships as a bundle: the `scape_server` executable and the native
library it loads. A machine with the bundle needs no Dart.

## Build

```sh
cd packages/scape_server
dart build cli -t bin/scape_server.dart -o build
```

The bundle is `build/bundle`. `dart compile exe` is refused, because the
`orblit_core` build hook has to ship beside the executable. Build on the
operating system you deploy to. CI builds the Linux bundle and keeps it as
the `scape-server-linux` artifact.

Check a build with `deploy/smoke.sh build/bundle`. It starts the server,
waits for the alive port, sends SIGTERM and expects exit code 0 within 10
seconds.

## Install on Linux

```sh
sudo useradd --system --home /opt/scape scape
sudo mkdir -p /opt/scape /etc/scape /var/lib/scape
sudo cp -r build/bundle /opt/scape/bundle
sudo cp deploy/server.env.example /etc/scape/server.env
sudo cp deploy/scape-server.service /etc/systemd/system/
sudo chown -R scape:scape /var/lib/scape
sudo systemctl enable --now scape-server
```

Edit `/etc/scape/server.env` first. Every line is optional. A value that
does not parse stops the server with exit code 78 and a line on stderr that
names the variable. The unit does not restart on 78.

| Variable | Default | Meaning |
| --- | --- | --- |
| `SCAPE_PORT` | 7600 | Where players connect |
| `SCAPE_ADDRESS` | 127.0.0.1 | The address to listen on. Put a reverse proxy in front to end TLS |
| `SCAPE_DATA` | scape-saves | The folder that holds saves |
| `SCAPE_WORLD` | scape | The saves go in `SCAPE_DATA/SCAPE_WORLD`. Lower case letters, digits, `-` and `_` |
| `SCAPE_HEALTH_PORT` | 7601 | Answers `alive` to any connection. 0 turns it off |
| `SCAPE_LOG_LEVEL` | info | `warn` logs faults. `info` adds a health line each minute |
| `SCAPE_ADMINS` | none | `ana=admin,bo=moderator`. Needs accounts |

The server saves players on SIGTERM, so `systemctl stop` and `restart` keep
what players did. The unit gives it 30 seconds.

## Watch it

```sh
journalctl -u scape-server -f
(exec 3<>/dev/tcp/127.0.0.1/7601; head -n 1 <&3)   # alive
```

The health line reads players, regions, and the tick average and maximum
over the last 100 ticks. A tick has 600 ms. The alive port runs on the
server's own isolate, so a stalled tick stalls the answer.

## Back up

```sh
deploy/backup.sh /var/lib/scape/scape /var/backups/scape 14
```

It writes a dated `.tar.gz` and keeps the newest 14. Run it from cron or a
systemd timer while the server runs. The server writes each file beside
itself and renames it into place, so an archive holds whole files. The
rename is not synced to disk, and files may differ in age from one another.
`history/` holds older versions of each save.

## What has not run

The Linux bundle, the unit file and the Linux smoke run have not run. The
macOS bundle ran and stopped on SIGTERM. Restarts that keep saves were
tested through the save library, not under systemd.
