# Deploying the server

There is nothing to deploy yet. `Scape.Server` is a library and no program
starts it. The Dart server that this folder was written for is deleted. The
tag `dart-final` has it.

Two parts are missing. The host is a program that reads the settings below,
wires the world and the saves, and stops on SIGTERM. The Argon2id hasher is
the password hash that the host hands to the accounts. Until both are built,
the files here describe what the host has to do.

| File | Holds |
| --- | --- |
| `scape-server.service` | A systemd unit. It runs `/opt/scape/bundle/bin/scape_server` and does not restart on exit code 64 or 78 |
| `server.env.example` | The settings, to copy to `/etc/scape/server.env` |
| `smoke.sh` | Runs a built folder with a bare environment and checks how it starts and stops |
| `backup.sh` | Archives a save folder and keeps the newest few |

## Settings

`ServerConfig` reads these from the environment. Every line is optional. A
value that does not parse stops the host before it listens. The host stops
with exit code 78 and a line on stderr for a value that does not parse, a
port that cannot be bound, a save folder that cannot be written and game data
that disagrees with itself. It stops with 64 for an argument. The unit does
not restart on either, and gives up after five failed starts in two minutes.

| Variable | Default | Meaning |
| --- | --- | --- |
| `SCAPE_PORT` | 7600 | Where players connect |
| `SCAPE_ADDRESS` | 127.0.0.1 | The address to listen on. Put a reverse proxy in front to end TLS |
| `SCAPE_DATA` | scape-saves | The folder that holds saves. The unit sets `/var/lib/scape`, the one folder it may write |
| `SCAPE_WORLD` | scape | The saves go in `SCAPE_DATA/SCAPE_WORLD`. Lower case letters, digits, `-` and `_` |
| `SCAPE_HEALTH_PORT` | 7601 | Answers `alive` to any connection. 0 turns it off |
| `SCAPE_LOG_LEVEL` | info | `warn` logs faults. `info` adds a health line each minute |
| `SCAPE_ADMINS` | none | `ana=admin,bo=moderator`. Names are valid player names, and each appears once. Needs accounts |

The host has to save players on SIGTERM, so `systemctl stop` and `restart`
keep what players did. The unit gives it 30 seconds.

## Watch it

```sh
journalctl -u scape-server -f
(exec 3<>/dev/tcp/127.0.0.1/7601; read -r -t 2 line <&3; echo "$line")   # alive
```

The health line reads players, regions, and the tick average and maximum
over the last 100 ticks. A tick has 600 ms. `AlivePort` waits for the gate a
tick holds, so a stalled tick stalls the answer.

## Back up

```sh
deploy/backup.sh /var/lib/scape/scape /var/backups/scape 14
```

It writes a dated `.tar.gz` and keeps the newest 14. Run it from cron or a
systemd timer while the server runs. The server writes each file beside
itself and renames it into place, so an archive holds whole files. The
rename is not synced to disk, and files may differ in age from one another.
`history/` holds older versions of each save. Archives are named after the
last part of the folder, so several worlds can share one backup folder.
Saves hold password hashes, so the script and the unit both set a umask that
keeps other users out.

## What has not run

Nothing here has run against a C# server, because there is none. The smoke
script passed against the Dart bundle on macOS and never ran on Linux. The
unit file has not run. The backup script does not depend on the server.
