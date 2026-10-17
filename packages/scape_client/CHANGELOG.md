# Changelog

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
