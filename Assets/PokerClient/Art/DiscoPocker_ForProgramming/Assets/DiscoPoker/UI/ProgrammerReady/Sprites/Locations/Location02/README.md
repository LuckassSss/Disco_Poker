# Location 02

The second Route 66 location is prepared as a self-contained programmer handoff.

## Structure

- `Backgrounds/image 29.png` — high-resolution location background without the gameplay HUD.
- `Backgrounds/NEW_VAR.png` — complete visual reference with HUD, tutorial and locked-table states.
- `Objects` — diner, drive-in, gas station and motel; files ending in `1` are active/color variants and files ending in `2` are inactive/grayscale variants.
- `TableNames` — transparent Russian title artwork for the four tables.

The source `image 29.png` is preserved at 10435 x 6946 pixels. The shared Unity importer caps
the imported texture at 4096 pixels, so platform compression and final background resolution
should be reviewed before release. Placement coordinates, scene wiring and input behaviour were
not supplied, so no prefab or scene hierarchy is invented here.
