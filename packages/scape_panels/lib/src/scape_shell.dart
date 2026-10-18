import 'package:flutter/widgets.dart';
import 'package:orblit_ui/orblit_ui.dart';
import 'package:scape_core/scape_core.dart';

import 'panel_controller.dart';
import 'panels.dart';

/// The panels, drawn from [controller] and redrawn when it says so.
final class ScapeShell extends StatelessWidget {
  const ScapeShell({super.key, required this.controller, required this.skills});

  final PanelController controller;
  final SkillSet skills;

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: controller,
    builder: (context, _) => UiSurface(
      description: Panels(
        controller.session,
        controller.menus.items,
        skills,
      ).build(controller.state),
      onEvent: (handler, _) => controller.handle(handler),
    ),
  );
}
