# orblit-scape

An online tile-based game on [Orblit](https://github.com/ChxisB/orblit). One
server, many players, a world of tiles with skills, items and combat.

The world, the names, the items and the art are original. Content is data,
so a new place or a new monster is a file and not a code change.

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

```sh
git clone https://github.com/ChxisB/orblit.git       # beside this one
git clone https://github.com/ChxisB/orblit-net.git   # beside this one
../orblit/tool/build_native.sh                         # the engine's native core
../orblit/tool/pack_local.sh                           # packs the engine into ~/.orblit/feed
dotnet pack ../orblit-net/dotnet/Orblit.Net -c Release -o ~/.orblit/feed
./tool/check_dotnet.sh
```

It needs the .NET 10 SDK and CMake.

Nothing starts `Scape.Server` as a program yet. See
[deploy/README.md](deploy/README.md).

## Licence

MPL-2.0, © 2026 Chris Beckett. See [LICENSE](LICENSE).
