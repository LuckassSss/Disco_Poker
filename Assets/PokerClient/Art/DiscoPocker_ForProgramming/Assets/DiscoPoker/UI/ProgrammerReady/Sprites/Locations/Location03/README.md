# Location 03

The third Miami beach location is prepared as a self-contained programmer handoff.

## Structure

- `Backgrounds/beach-casino-without-npcs.png` — visual reference with the top HUD but without NPCs.
- `Backgrounds/LOC_3_no_ui_without_npcs.png` — clean location base without HUD or NPCs.
- `Objects` — ferris wheel, lounge bar, neon stage and pool complex in active/color and inactive/grayscale variants.
- `TableNames` — transparent Russian title artwork for the four tables.

Use `LOC_3_no_ui_without_npcs.png` as the base when building the real screen. Files containing
`transparent` are active variants and files containing `inactive` are locked/inactive variants.
Placement coordinates, scene wiring and input behaviour were not supplied, so no prefab or scene
hierarchy is invented here.
