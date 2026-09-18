# Location 04

The fourth neon marina location is prepared as a self-contained programmer handoff.

## Structure

- `Backgrounds/NEW.png` — flattened active-state location composite without HUD.
- `Backgrounds/non_actrive.png` — flattened location composite with inactive hotel, limousine and yacht states.
- `Backgrounds/w_ui.png` — complete visual reference with the top HUD and Russian activity feed.
- `Objects` — hotel, limousine and yacht active/inactive artwork plus the active ice-cream kiosk.
- `TableNames` — transparent Russian title artwork for the three tables.

Use `NEW.png` and `non_actrive.png` as state references. No clean background without the
composited objects was supplied. The original `non_actrive.png` spelling is preserved to avoid
breaking source-file correspondence. Placement coordinates, scene wiring and input behaviour
were not supplied, so no prefab or scene hierarchy is invented here.
