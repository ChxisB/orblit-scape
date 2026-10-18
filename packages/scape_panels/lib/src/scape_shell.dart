import 'package:flutter/widgets.dart';
import 'package:orblit_ui/orblit_ui.dart';
import 'package:scape_core/scape_core.dart';

import 'panel_controller.dart';
import 'panels.dart';

/// The panels, drawn from [controller] and redrawn when it says so.
class ScapeShell extends StatelessWidget {
  const ScapeShell({
    super.key,
    required this.controller,
    required this.skills,
    this.width,
  });

  final PanelController controller;
  final SkillSet skills;

  /// The width to lay out at, when the caller knows it.
  final double? width;

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: controller,
    builder: (context, _) => UiSurface(
      description: Panels(
        controller.session,
        controller.menus.items,
        skills,
      ).build(controller.state),
      width: width,
      onEvent: (handler, _) => controller.handle(handler),
    ),
  );
}
