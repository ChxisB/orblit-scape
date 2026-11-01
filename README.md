# orblit-scape

An unofficial, fan-made replica of Old School RuneScape. It runs on
[Orblit](https://github.com/ChxisB/orblit). One server, many players, a world
of tiles with skills, items and combat.

Scape copies how the game plays: a tile world, a 600 ms tick, and skills,
items and combat that run on that tick. It does not copy the game's code,
data or art. The world, the names, the items and the art here are original.
Content is data, so a new place or a new monster is a file and not a code
change.

## Not affiliated with Jagex

Scape is not made, endorsed or supported by Jagex Ltd. Old School RuneScape
and RuneScape are trademarks of Jagex Ltd. They appear here only to say what
this project imitates.

- Scape has its own server, its own wire and its own accounts. It does not
  connect to Jagex's servers and it cannot sign in to a Jagex account.
- `Scape.Bots` logs in to a Scape server and nothing else. It does not speak
  the real game's protocol.
- This repository holds no Jagex code, no Jagex art and no game cache. No
  cache reader is part of it either.
- Do not commit a file from the real game here. To test a client against
  one, use your own copy that you are allowed to use, and keep it outside
  the repository.
- If you hold rights in something here and want it changed, open an issue.

## Projects

| Project | Holds |
| --- | --- |
| `Scape.Core` | Tile coordinates, flags, paths, items, combat and the messages the server and client share |
| `Scape.Content` | The items, creatures, skills, quests and models of the first game, the starter island, and the check that they fit together |
| `Scape.Server` | The tick loop, the client wire, sessions, actions and saves. A library |
| `Scape.Client` | What a player sees and sends: the camera, the pick under the pointer, actor poses and the session with its menus |
| `Scape.Panels` | The side panels, minimap and right-click menu as Avalonia controls |
| `Scape.Bots` | Bots that log in over the wire, and the tools an agent uses to look at a server |
| `Scape.Demo` | The starter island in 3D in a window, with the rules run in the app and no server |

## Run it

It needs the .NET 10 SDK and CMake. Run every command below from the root of
this repository.

### Build the engine once

Scape builds against the Orblit engine and its networking library. Clone
both beside this repository, then pack them into a local feed.

```sh
git clone https://github.com/ChxisB/orblit.git       # beside this one
git clone https://github.com/ChxisB/orblit-net.git   # beside this one
../orblit/tool/build_native.sh                         # the engine's native core
../orblit/tool/pack_local.sh                           # packs the engine into ~/.orblit/feed
dotnet pack ../orblit-net/dotnet/Orblit.Net -c Release -o ~/.orblit/feed
```

Run these again after you pull a change in `orblit` or `orblit-net`.

### Check it

```sh
./tool/check_dotnet.sh
```

This builds `dotnet/`, runs the tests and checks the format. The analyzer
rules are build errors, so the build is the lint.

### Walk the starter island

```sh
dotnet run --project dotnet/Scape.Demo
dotnet run --project dotnet/Scape.Demo -- --walk X Y
```

`--walk X Y` sends the player to tile X Y as the window opens. A click walks
and a shift-click runs. A right drag or the arrow keys turn the camera, and
the wheel zooms. The text in the top left names what is under the pointer.

### Run a server with bots

```sh
dotnet run --project dotnet/Scape.Bots -- --bots 50 --ticks 100
```

The bots log in to a server that runs inside the same process, over loopback.
This is the only way to run the server today. The run prints the tick times
and writes the full result to a JSON file.

| Option | Default | Meaning |
| --- | --- | --- |
| `--bots N` | 50 | Bots to log in, 1 to 9999 |
| `--ticks N` | 100 | Ticks to run |
| `--seed N` | 1 | Seed for what the bots do |
| `--reach N` | 12 | Tiles from the spawn a bot may walk |
| `--out PATH` | `bots-result.json` | Where to write the result |

### Host the server

Nothing starts `Scape.Server` as a program yet. See
[deploy/README.md](deploy/README.md) for what the host has to do.

## Licence

MPL-2.0, © 2026 Chris Beckett. See [LICENSE](LICENSE). The licence covers the
code and data in this repository. It gives no rights in Jagex's marks.
