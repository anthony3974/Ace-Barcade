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

- One font for the whole arcade, chosen in PLAN task 3. Games never pick a font or draw their own buttons.
- The shared HUD draws the score, best score, pause button, "Tap to start", the pause screen and the game-over card the same way in every game. A game only reports its score.
- No text inside art. **(checked)**

## Motion and effects

- 60 fps. Movement is snappy with eased starts and stops; nothing floaty.
- Shared feel, from the shared kit: squash and stretch on landings and hits (`Style.Squash`), pops when things appear (`Style.PopTime`), screen shake in two strengths only (`Style.ShakeSmall`, `Style.ShakeBig`), and flat particle bursts in palette colors.
- No motion blur, trails made of transparency, or post-processing.

## Sound

One shared sound set (task 5), in one style. A game that needs a new sound adds it to the shared set, made the same way, so other games can use it too.

## Game icons

Every game has `Art/Games/G###_Name/icon.svg`, **exactly 360 x 360 px** **(checked)**, built from the icon template (task 4): a rounded square in one palette color with the game's main object large in the middle, outlined like everything else. No text on icons; the hub prints the title.

## New game checklist

Before a game is ticked off:

1. `Art/check-style.sh` passes.
2. It uses only the shared HUD, input, font, sounds and effects.
3. It's played in portrait with one thumb, inside the 9 x 16 area, with the top 1.5 units clear.
4. Red only means danger; the player reads at a glance against the background.
5. Side by side in the hub with the other games, nothing about it looks out of place. If it does, fix the game, not this guide. If the guide is wrong, change it for every game at once.
