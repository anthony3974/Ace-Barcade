# House style

Ace Barcade may hold anywhere from 1 to 500 games. They should look like they came out of one arcade, not like a slick 3D runner sitting next to a crude Pong. These rules are how. Every game follows all of them, and no game gets its own exceptions.

Rules marked **(checked)** are enforced by `Art/check-style.sh`, which also runs before every sprite build. The numbers live in code in `Assets/Scripts/Core/Style.cs` and `Palette.cs`.

## The look in one line

Flat, chunky cartoon: big simple shapes, one thick dark outline, colors from one shared palette, a flat shadow side, no gradients and no lighting.

## Screen

- **Portrait, Android phones.** Every game is played with one thumb.
- **The play area is 9 x 16 units** (9:16), centred on (0, 0). The camera always shows all of it. Taller phones see extra background above and below; wider screens see extra at the sides. Nothing that matters goes outside the 9 x 16 area.
- **2D only.** Orthographic camera, no 3D models, no perspective. An idea that is usually 3D gets a 2D version: a runner becomes a top-down lane runner or a side-on one.
- The top 1.5 units are kept clear for the shared HUD (score, best, pause).

## Scale

- **SVGs are authored at 120 px = 1 unit.** A full-screen mock is 1080 x 1920 px. **(checked: nothing bigger)**
- Sprites render at 1x and import at 120 pixels per unit. That is sharp on a 1080p phone and keeps textures small. We don't push high-res textures.
- **Nothing smaller than 16 px** (about 0.13 units). If a detail would be smaller, leave it out.

## Shapes

- Big, simple shapes: circles, rounded rectangles, simple polygons. Characters are a few blobs, not a drawing.
- Corners are rounded (radius 8 px or more). Sharp points only where they mean danger (spikes, teeth).
- Eyes, when a thing has them, are the same in every game: a white (Paper) oval with an Ink pupil.

## Outlines

- **Every object has an Ink outline, 8 px**, on the outside of its silhouette.
- **Inner detail lines are 5 px.** **(checked: strokes are 8 or 5 px)**
- **Round joins** everywhere. **(checked)**
- Backgrounds have no outlines, so they sit behind the action.

## Color

- **Only colors from `Art/palette.txt`.** **(checked)** In code, only `Palette.X`. Never a raw color.
- **Light comes from the top left in every game.** Each object is its base color, with a flat shadow shape in the matching `Dark` color on its bottom-right side. One optional flat highlight in Paper on the top left.
- **No gradients, no transparency, no filters, no blur, no glow.** **(checked)** Fades and flashes are done in code.
- **Colors mean the same thing in every game:**

| Role | Colors |
|---|---|
| Background | Night, Slate, Mist, Cloud (low contrast, no outlines) |
| The player | Yellow (Orange as an accent) |
| Danger, enemies, things that end your run | Red (and RedDark) |
| Good things to collect | Teal, Green, Lime |
| Neutral solid things (walls, paddles, platforms) | Blue, Purple, Brown, Tan |
| Text and outlines | Ink on light, Paper on dark |

  A game can bend the player's color (two players, a themed level), but **Red always means danger** and never appears in a background.

## Text and UI

- **One font: Lilita One**, a chunky rounded display face, always with the Ink outline (it's baked into the font's material, so there's no way to forget it). Games never pick a font or draw their own buttons.
- Text is Paper on dark backgrounds and Ink or Slate on light cards. Yellow and Pink are for the moments that matter (TAP TO START, the final score, NEW BEST!).
- Two button kinds only: **primary** (Green: the thing you most likely want, like RETRY or RESUME) and **secondary** (Cloud: everything else). Labels are short and in capitals.
- The shared HUD (`Hud.cs`) draws the score, best score, pause button, "Tap to start", the pause screen and the game-over card the same way in every game. A game only reports its score.
- No text inside art. **(checked)**

## Motion and effects

- 60 fps. Movement is snappy with eased starts and stops; nothing floaty.
- Shared feel, from `Juice`: `Squash` on landings and hits, `Pop` when things appear, `ShakeSmall` and `ShakeBig` (the only two shake strengths), and `Burst` for flat palette-colored dots. A burst takes the color of what made it, but never the same color as the thing it bursts from (Paddle Rally's yellow paddle throws orange sparks).
- Plain shapes that need no outline (background decoration, particles, dimming) come from `Shapes`, in palette colors. Walls and platforms drawn in code use `Shapes.Box`, which adds the Ink outline and the shade side.
- No motion blur, trails made of transparency, or post-processing.

## Sound

One shared sound set (PLAN task 5), in one style. A game that needs a new sound adds it to the shared set, made the same way, so other games can use it too.

## Cards and panels

UI panels that stretch (the hub's cards) are 9-slice art: an SVG whose name ends in `_sliced`, with its rounded corners, outline and shade inside the outer 64 px, so they stay the same size however big the panel is. Game cards are Paper with a Cloud shade; empty "coming soon" slots are Slate with a Night shade.

## Game icons

Every game has `Art/Games/G###_Name/icon.svg`, **exactly 360 x 360 px** **(checked)**: a rounded square (radius 56 px, Ink outline) showing the game's own background, with its main objects large in the middle, outlined like everything else. `Art/Games/G001_PaddleRally/icon.svg` is the template. No text on icons; the hub prints the title. The icon is the thumbnail on the game's hub card, shown at about 200 px, so keep it bold and simple.

## App icon

The launcher icon is a gold arcade coin with a star, on the Night background. It lives in `Art/AppIcon/` as Android's two adaptive layers, `background.svg` (full bleed, no outlines) and `foreground.svg`, each 216 x 216 px (the 108 dp canvas at 2 px per dp, so outlines read at launcher size). Phones crop it to the middle 144 px in their own shape (circle, squircle, rounded square), and only the middle circle of radius 66 px is safe from every shape, so the coin stays inside it. `Art/build-sprites.sh` renders it to `Assets/AppIcon/` (the two layers, plus flattened round and square versions for Android 7) and **Apply Project Settings** sets it as the app icon.

## New game checklist

Before a game is ticked off:

1. `Art/check-style.sh` passes.
2. It uses only the shared HUD, input, font, sounds and effects.
3. It's played in portrait with one thumb, inside the 9 x 16 area, with the top 1.5 units clear.
4. Red only means danger; the player reads at a glance against the background.
5. Side by side in the hub with the other games, nothing about it looks out of place. If it does, fix the game, not this guide. If the guide is wrong, change it for every game at once.
