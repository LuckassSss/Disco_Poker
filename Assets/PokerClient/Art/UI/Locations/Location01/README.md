# Location 01

The first desert location is prepared as a self-contained programmer handoff.

## Structure

- `Backgrounds/without_UI.png` — clean gameplay background for runtime UI composition.
- `Backgrounds/W UI.png` — complete visual reference with HUD, tutorial and table states baked in.
- `Objects` — three gameplay object groups, each supplied in active and inactive variants.
- `TableNames` — transparent Russian title artwork for the three tables.

Use `without_UI.png` as the base when building the real screen. Treat `W UI.png` as a visual
reference rather than a dynamic interface. Object placement coordinates, scene wiring and input
behaviour were not supplied, so no prefab or scene hierarchy is invented here.
