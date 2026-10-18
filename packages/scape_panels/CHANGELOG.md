# Changelog

## 0.1.0

- `Panels` builds the game interface as an `orblit_ui` tree: the inventory,
  equipment, skills, quests, friends and settings tabs, the chat box, the
  minimap, the right-click menu, and the windows for a conversation, a shop,
  a bank and a player trade.
- `PanelController` turns a tap into a call on `GameSession`. A drag is two
  taps, and the panel changes only when the server sends the new slots.
- `ScapeShell` draws the tree in a `UiSurface`. It is one column up to 900
  wide and a world beside a 320 wide side panel above that.
