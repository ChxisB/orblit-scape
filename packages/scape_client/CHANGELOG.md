# Changelog

## 0.16.0

- `GameSession.lookOf` and `animOf` give how an actor looks and what it is
  doing. The session lists `Look` and `Anim` in its replication set, which
  a server from `scape_server` 0.19.0 sends.
- `ActorAnimator` turns an actor's `Anim` into a pose for each frame. An act
  plays once from the frame its count changes. An actor seen for the first
  time shows a death but does not replay an act that is over.
- `scape_content` is a dependency, for `Pose`.

## 0.15.0

- `GameSession.moveItem` sends a drag. The slots change when the server
  sends them, so a refused drag leaves the panel as it was.
- `chat` gives the lines of the game, public and private tabs. `journal`
  lists the quests begun and the text for each stage. It needs the `story`
  the game was made with.
- `Menus` gives the right-click entries for a creature, object, ground item
  or player. `minimapDots` gives the players and creatures near the player.

## 0.14.0

- `GameSession.cast` and `togglePrayer` send the intents. `shots` holds the
  flights the server announced and `prayer` holds the points and the prayers
  that are on.

## 0.13.0

- `GameSession.requestTrade` asks another player to trade. `playerTrade` holds
  the open trade as the server last sent it. `offer`, `acceptTrade` and
  `declineTrade` act on it.

## 0.12.0

- `GameSession.talk` starts a conversation. `line` holds what a creature
  says and `choices` what the player may pick. `continueTalk` goes on
  from a line and `choose` picks an option. The quest table is not read
  yet, so there is no journal.

## 0.11.0

- `GameSession.login` takes a `password` and `create`, for a server that
  has accounts.

## 0.10.0

- `GameSession.trade` opens a creature's shop. `buy`, `sell`, `deposit`
  and `withdraw` send a request. `shop` holds the stock the server last
  sent and `bankOpen` says whether the bank is open. `attack` sends the
  attack option by name.

## 0.9.0

- `GameSession.attack` sends a click on a creature. `splats` lists the
  swings that landed or missed, and `healthOf` reads the hit points of
  any fighter in view.

## 0.8.0

- `GameSession.npcs` lists the creatures in view with their kind. They
  are not `actors`.

## 0.7.0

- `GameSession.objects` lists the objects in view with their state,
  apart from the `actors`.
- `useObject`, `takeItem`, `useItemOnObject` and `useItemOnItem` send a
  click to the server.

## 0.6.0

- `GameSession.itemAt` reads the slots the server has sent. A session
  takes the `items` of the game.
- `groundItems` lists the items lying in view, apart from the `actors`.

## 0.5.0

- `GameSession.xpOf` and `levelOf` read the experience the server has sent.
  A session takes the `skills` of the game.

## 0.4.0

- `GameSession.say`, `whisper` and `editList` send chat and list changes.
- `bubbles` lists the chat lines to draw over heads. A line stays for
  `bubbleSeconds`, 5, by the session's clock.
- `whispers`, `friends` and `ignored` hold what the server has sent. Notices
  and whispers keep the newest 100.

## 0.3.0

- `GameSession.connect` opens a WebSocket to a server. The session closes the
  connection on `dispose`.

## 0.2.0

- `OrbitView` is an orbit camera that turns, zooms and follows a point. It
  turns a pixel into a ray.
- `pickTile` finds the tile under a ray on the terrain. `standingPoint` gives
  the height an actor stands at.
- `GameSession` logs in over a transport, sends walks and lists the actors it
  can draw. Other players' movement is blended over 700 ms.
- The package is still pure Dart. The window that draws it is not written.

## 0.1.0

- The package exists. It holds a version constant and a test that keeps it
  equal to the version in `pubspec.yaml`.
