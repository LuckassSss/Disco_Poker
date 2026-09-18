# Disco Poker UI — Programmer Ready

This folder contains the current standalone UI artwork prepared for Unity uGUI.
The source PNG files are preserved without renaming or destructive edits.

## Folder layout

- `Sprites/HUD` — top navigation, currency, avatar, level, shop and status buttons.
- `Sprites/Map` — table panels, mission markers and location title artwork.
- `Sprites/Tutorial` — tutorial callout artwork.
- `Sprites/Loading` — loading/status panel artwork.
- `Sprites/Locations/Location01` — first desert location backgrounds, active/inactive objects and table names.
- `Sprites/Locations/Location02` — second Route 66 location background, reference screen, active/inactive objects and table names.
- `Sprites/Locations/Location03` — third Miami beach location backgrounds, active/inactive objects and table names.
- `Sprites/Locations/Location04` — fourth neon marina location composites, active/inactive objects and table names.
- `Animations/Balls/Frames` — ten ordered transparent PNG frames for the disco-ball UI animation.
- `Animations/Balls/animation-frames.json` — frame order and source hashes for programmer handoff.
- `Documentation/ui-assets.json` — stable IDs, dimensions, intended use and baked-text warnings.
- `Documentation/validation-report.json` — source PNG transparency and size checks.
- `Editor/DiscoPokerUiTexturePostprocessor.cs` — scoped importer settings for this pack only.

## Unity import settings

Every PNG under `Sprites` and `Animations/Balls/Frames` is imported as a single uGUI Sprite with a centered pivot,
Full Rect mesh, 100 pixels per unit, alpha transparency, bilinear filtering, clamp wrapping,
no mipmaps, and no forced texture compression. The largest accepted texture size is 4096.
Platform-specific compression and a Sprite Atlas should be selected later for the actual target build.

## Programmer usage

1. Use the sprites on `UnityEngine.UI.Image` components.
2. Add `Button` or project-specific view/controller behaviour only where interaction is required.
3. Use anchors and a `CanvasScaler`; do not build a fixed-resolution layout from native PNG size alone.
4. Treat the file names as artwork names and use the stable `id` values in `ui-assets.json` for code-facing keys.
5. Build runtime values as separate text/images whenever an empty artwork variant exists.

## Important baked-content warnings

- `02_currency_29900.png` includes the value `29 900` in the artwork.
- `04_level_1.png` includes level `1`; prefer `04_level_empty-transparent.png` for dynamic level text.
- `active-table-panel-americana-80s.png` includes the table name and stake values.
- `tutorial-callout-americana-80s-transparent-v2.png` includes Russian tutorial text.
- `zakusochnaya-starliner-title-transparent.png` includes the location title.
- `loaddiing screen.png` is the complete loading screen with the baked `DISCO POKER` logo and Russian loading indicator.
- `loading screen_ no loading ui.png` keeps the baked `DISCO POKER` logo but removes the loading indicator for runtime UI layering.
- `Locations/Location01/Backgrounds/W UI.png` is a complete visual reference with baked HUD, tutorial and Russian labels; use `without_UI.png` as the programmable base.
- Every PNG in `Locations/Location01/TableNames` contains a baked Russian table name and requires separate localized artwork or runtime text for other languages.
- `Locations/Location02/Backgrounds/NEW_VAR.png` is a complete visual reference with baked HUD and Russian labels; use `image 29.png` as the high-resolution location base.
- Location 02 objects contain baked English environmental signage, while its `TableNames` files contain baked Russian names.
- `Locations/Location03/Backgrounds/beach-casino-without-npcs.png` includes the top HUD; use `LOC_3_no_ui_without_npcs.png` as the programmable base without HUD or NPCs.
- Location 03 object signage is baked in English, while its `TableNames` files contain baked Russian names.
- `Locations/Location04/Backgrounds/w_ui.png` is the complete visual reference with the baked HUD and Russian activity feed.
- `Locations/Location04/Backgrounds/NEW.png` and `non_actrive.png` are flattened active/inactive composites rather than empty location backgrounds.
- Location 04 hotel and ice-cream artwork contains baked English signage, while its `TableNames` files contain baked Russian names.

These files are useful as visual-complete variants, but dynamic/localized UI should use empty
variants or be rebuilt from layered source artwork. No scene, prefab or runtime playback logic
was supplied, so none was invented here. The disco-ball art sequence is prepared for animation,
while playback speed and looping remain deliberate runtime choices.
