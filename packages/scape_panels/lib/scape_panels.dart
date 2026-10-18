/// The panels of the orblit-scape client.
library;

export 'src/panel_controller.dart' show PanelController;
export 'src/panel_state.dart'
    show PanelState, SideTab, Slot, maxViewDistance, maxVolume, minViewDistance;
export 'src/panels.dart' show Panels;
export 'src/scape_shell.dart' show ScapeShell;

/// The version in `pubspec.yaml`. A test keeps the two equal.
const scapePanelsVersion = '0.1.0';
