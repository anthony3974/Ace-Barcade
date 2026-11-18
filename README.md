# Ace Barcade

An arcade of small games for Android phones, made with Unity. One app, many quick one-thumb games, all in the same flat, chunky cartoon style, so every game looks like it belongs to the same cabinet.

It's a personal project and won't be published to a store.

## Current state

The app opens on the hub: a scrolling grid of game cards, two across and six down, each with the game's thumbnail, title and best score. Six games so far, all waiting on their first playtest on a phone: **Paddle Rally** (drag to keep a ball out of the spikes), **Drop Stack** (a falling-block puzzle), **Brick Burst** (a brick breaker), **Rocket Lanes** (swipe between lanes to dodge mines), **Paddle Duel** (table tennis against the computer) and **Slide Nine** (a sliding puzzle against the clock). Around it is the shared kit every game uses: the HUD (score, best, pause, game-over card), the font, one-finger input, the portrait camera, best scores, game feel (squash, pop, shake, bursts) and the art pipeline with its style check. Sound is next. See [PLAN.md](PLAN.md) for what's next, [docs/STYLE.md](docs/STYLE.md) for the house style every game follows, and [docs/CONVENTIONS.md](docs/CONVENTIONS.md) for how the code is organised and how to add a game.

## Playing

On the phone everything is one thumb: tap, hold, drag or swipe, depending on the game. Tap a card in the hub to play. Android's Back button pauses a game, goes back to the hub from the pause or game-over screen, and closes the app from the hub.

| # | Game | Controls | How to play |
|---|---|---|---|
| 001 | Paddle Rally | Drag | Slide the paddle to keep the ball out of the spikes. Hit the gem for bonus points. |
| 002 | Drop Stack | Tap, drag, flick | Fill rows of falling blocks to clear them. Tap to turn, drag to slide, flick down to drop. Clear gem blocks for bonus points. |
| 003 | Brick Burst | Drag | Knock the ball into the wall of bricks. Grey bricks take two hits, gem bricks are worth more. Three lives. |
| 004 | Rocket Lanes | Swipe | Swipe left or right to change lanes and dodge the rows of mines. Fly through crystals for bonus points. |
| 005 | Paddle Duel | Drag | Get the ball past the computer's paddle at the top. Hit near your paddle's ends for sharp angles it can't reach. Three balls. |
| 006 | Slide Nine | Tap | Tap tiles to slide them into the gap and put 1 to 8 in order. Each solve adds time; fewer moves score more. |

In the editor, the mouse stands in for the finger and Escape for Back. Use **Tools > Ace Barcade > Play Game...** to start in a game.

To put it on a phone, use **Tools > Ace Barcade > Build APK**, then copy `Builds/AceBarcade.apk` to the phone and open it.

## Getting started

1. Install Unity 6000.0.85f1 through Unity Hub, with **Android Build Support** (OpenJDK and Android SDK & NDK).
2. Clone this repository.
3. In Unity Hub, choose **Add project from disk** and select the cloned folder. Switch the platform to Android if Unity asks.

On Linux with Intel graphics, launch the editor with `-force-vulkan`: OpenGL can draw red noise.

To run the tests, open **Window > General > Test Runner**, choose **EditMode** or **PlayMode** and click **Run All**. The play mode tests play each game through the real arcade, and can save phone-sized screenshots (see [docs/CONVENTIONS.md](docs/CONVENTIONS.md#tests-screenshots-and-builds)).

To check that the code compiles without opening Unity (after Unity has opened the project once), run `Tools/compile-check.sh`.

## Art

All art is hand-built SVG in `Art/`, authored at 120 px per unit, and converted to PNG sprites in `Assets/Resources/Sprites/` by `Art/build-sprites.sh` (or **Tools > Ace Barcade > Build Sprites**). It needs `rsvg-convert` (from librsvg) and `python3`.

The app's launcher icon (a gold arcade coin) is drawn the same way, in `Art/AppIcon/`, and rendered to `Assets/AppIcon/` by the same script.

Before converting, `Art/check-style.sh` checks every SVG against the house style: palette colors only, no gradients or transparency, 8 px or 5 px outlines with round joins, and a 360 x 360 icon for each game. Art that breaks a rule doesn't get built.

## Credits

Font: [Lilita One](https://fonts.google.com/specimen/Lilita+One) by Juan Montoreano, under the SIL Open Font License (`Assets/Fonts/LilitaOne-OFL.txt`).

## Adding a game

Each game is one class extending `ArcadeGame`, tagged with `[ArcadeGame(number, id, title, controls)]`, in its own folder under `Assets/Games/`, with its art in `Art/Games/`. The arcade finds it automatically. The steps are in [docs/CONVENTIONS.md](docs/CONVENTIONS.md#adding-a-game).
