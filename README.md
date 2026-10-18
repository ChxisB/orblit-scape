# orblit-scape

An online tile-based game on [Orblit](https://github.com/ChxisB/orblit). One
server, many players, a world of tiles with skills, items and combat.

The world, the names, the items and the art are original. Content is data,
so a new place or a new monster is a file and not a code change.

## Packages

| Package | Holds |
| --- | --- |
| `scape_core` | Tile coordinates, flags, paths and the rules both sides share. Pure Dart |
| `scape_server` | The headless tick, sessions, actions and saves. Built on `orblit_core` and `orblit_net` |
| `scape_client` | What a player sees and sends: the session, menus and minimap data. Pure Dart |
| `scape_panels` | The interface as `orblit_ui` trees, and the widget that draws them. Flutter |
| `scape_content` | Data files and the art built in code |
| `scape_bots` | Bots that play over loopback, a probe that reads the server, and the `bots` load command |

```sh
git clone https://github.com/ChxisB/orblit.git       # beside this one
git clone https://github.com/ChxisB/orblit-net.git   # beside this one
./tool/link_local.sh
./tool/check.sh
```

## Licence

MPL-2.0, © 2026 Chris Beckett. See [LICENSE](LICENSE).
