# Ace Barcade

An arcade of small games for Android phones, made with Unity. One app, many quick one-thumb games, all in the same flat, chunky cartoon style, so every game looks like it belongs to the same cabinet.

It's a personal project and won't be published to a store.

## Current state

The app opens on the hub: a scrolling grid of game cards, two across and six down, each with the game's thumbnail, title and best score. Nineteen games so far, listed below; all but Cave Glide, Rain Dodge, Ring Stop and Lily Leap have been played on a phone. The numbers skip the games still to come (011, 012, 016 and 018), so similar games sit side by side. Around it is the shared kit every game uses: the HUD (score, best, pause, game-over card), the font, one-finger input, the portrait camera, best scores, game feel (squash, pop, shake, bursts) and the art pipeline with its style check. Sound is next. See [PLAN.md](PLAN.md) for what's next, [docs/STYLE.md](docs/STYLE.md) for the house style every game follows, and [docs/CONVENTIONS.md](docs/CONVENTIONS.md) for how the code is organised and how to add a game.

## Playing

On the phone everything is one thumb: tap, hold, drag, swipe or pull, depending on the game. Tap a card in the hub to play. Android's Back button pauses a game, goes back to the hub from the pause or game-over screen, and closes the app from the hub.

| # | Game | Controls | How to play |
|---|---|---|---|
| 001 | Paddle Rally | Drag | Slide the paddle to keep the ball out of the spikes. Hit the gem for bonus points. |
| 002 | Paddle Duel | Drag | Get the ball past the computer's paddle at the top. Hit near your paddle's ends for sharp angles it can't reach. Three balls. |
| 003 | Brick Burst | Drag | Knock the ball into the wall of bricks. Grey bricks take two hits, gem bricks are worth more. Three lives. |
| 004 | Star Swarm | Drag | Slide your ship to dodge bullets and divers while it shoots the waves of bugs for you. Catch falling gems. Three lives. |
| 005 | Wing It | Tap | Tap to flap a bee through the gaps in thorny red columns. Fly through gems for bonus points. |
| 006 | Cave Glide | Hold | Hold to lift the glider through the cave, let go to sink. The rock is safe; the red crystals aren't. Grab gems on the way. |
| 007 | Rocket Lanes | Swipe | Swipe left or right to change lanes and dodge the rows of mines. Fly through crystals for bonus points. |
| 008 | Duck Runner | Tap, swipe down | Tap to jump over foxes, logs and turtles; swipe down to duck under hawks and hanging leaves. Jump for decoy ducks. |
| 009 | Bounce Up | Hold | Bounce up from pad to pad; hold the left or right half of the screen to steer. Watch out for crumbling pads and red burrs. |
| 010 | Lily Leap | Pull | Pull back to aim (the dots show where the frog lands), let go to leap from lily pad to lily pad. Purple pads drift, tan pads sink soon after you land; miss and you're in the water. Gems are worth more. |
| 013 | Drop Stack | Tap, drag, flick | Fill rows of falling blocks to clear them. Tap to turn, drag to slide, flick down to drop. Clear gem blocks for bonus points. |
| 014 | Stack Tower | Tap | Tap to drop each sliding floor onto the tower. Whatever hangs over is cut off; perfect drops keep it whole. |
| 015 | Pop Shot | Drag | Drag to aim (the dots show where it goes), let go to shoot. Match three or more colours to pop them; don't let the bubbles reach the red line. |
| 017 | Pop Up | Tap | Tap the green sprouts as they pop out of the stumps, but never the red spiky ones. Teal ones are worth more. Three lives. |
| 019 | Rain Dodge | Drag | Drag the bee between the red drops falling from the storm clouds; lightning warns before it strikes. Grab gems, and a leaf takes one hit for you. |
| 020 | Noodle | Swipe | Steer a noodle round a board whose edges wrap. Eat peas to grow; don't bite yourself or the red chilies. Gems are worth more. |
| 021 | Slide Nine | Tap | Tap tiles to slide them into the gap and put 1 to 8 in order. Each solve adds time; fewer moves score more. |
| 022 | Echo Pads | Tap | Watch the lily pads light up in a tune, then tap them back in order. Each round adds a note. Three lives. |
| 023 | Ring Stop | Tap | Tap when the spark running round the ring is on the green arc; it turns round and speeds up. Misses cost one of three lives, and a tap on red ends the run. |

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
