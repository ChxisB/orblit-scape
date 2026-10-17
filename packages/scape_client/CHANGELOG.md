# Changelog

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
