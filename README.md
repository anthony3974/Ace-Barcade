# Ace Barcade

An arcade of small games for Android phones, made with Unity. One app, many quick one-thumb games, all in the same flat, chunky cartoon style, so every game looks like it belongs to the same cabinet.

It's a personal project and won't be published to a store.

## Current state

The project is set up and has no games yet. The shared core is in place: the game base class, automatic game discovery, one-finger input, the fixed portrait camera, best scores, sprite loading, the palette and the style numbers. So is the art pipeline with its style check. See [PLAN.md](PLAN.md) for what's next, [docs/STYLE.md](docs/STYLE.md) for the house style every game follows, and [docs/CONVENTIONS.md](docs/CONVENTIONS.md) for how the code is organised and how to add a game.

## Playing

On the phone everything is one thumb: tap, hold, drag or swipe, depending on the game. Android's Back button pauses a game.

In the editor, the mouse stands in for the finger and Escape for Back. Use **Tools > Ace Barcade > Play Game...** to start in a game.

## Getting started

1. Install Unity 6000.0.85f1 through Unity Hub, with **Android Build Support** (OpenJDK and Android SDK & NDK).
2. Clone this repository.
3. In Unity Hub, choose **Add project from disk** and select the cloned folder. Switch the platform to Android if Unity asks.

On Linux with Intel graphics, launch the editor with `-force-vulkan`: OpenGL can draw red noise.

To run the tests, open **Window > General > Test Runner**, choose **EditMode** and click **Run All**.

To check that the code compiles without opening Unity (after Unity has opened the project once), run `Tools/compile-check.sh`.

## Art

All art is hand-built SVG in `Art/`, authored at 120 px per unit, and converted to PNG sprites in `Assets/Resources/Sprites/` by `Art/build-sprites.sh` (or **Tools > Ace Barcade > Build Sprites**). It needs `rsvg-convert` (from librsvg) and `python3`.

Before converting, `Art/check-style.sh` checks every SVG against the house style: palette colors only, no gradients or transparency, 8 px or 5 px outlines with round joins, and a 360 x 360 icon for each game. Art that breaks a rule doesn't get built.

## Adding a game

Each game is one class extending `ArcadeGame`, tagged with `[ArcadeGame(number, id, title, controls)]`, in its own folder under `Assets/Games/`, with its art in `Art/Games/`. The arcade finds it automatically. The steps are in [docs/CONVENTIONS.md](docs/CONVENTIONS.md#adding-a-game).
