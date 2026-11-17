# Conventions

How the arcade is put together, and how a game plugs into it. The look is in [STYLE.md](STYLE.md).

## How the arcade runs

- There is **one scene**, `Assets/Scenes/Arcade.unity`, holding one object with `ArcadeRoot`. It creates the camera (`ArcadeCamera`) and input (`ArcadeInput`), then shows the hub or starts a game. Nothing is placed by hand in the scene.
- A game is a C# class that extends `ArcadeGame` and carries an `[ArcadeGame(...)]` attribute. `GameRegistry` finds every such class by itself: there is no list of games to edit.
- A game builds its whole world **in code**, in `Build()`, as children of its own object. Leaving or retrying a game destroys that object and builds a fresh one, so a game never resets itself.
- `Arcade` (static) is where code finds the running pieces: `Arcade.Input`, `Arcade.Camera`, `Arcade.Current`. `Arcade.Launch`, `Arcade.Retry` and `Arcade.ShowHub` move between games.
- **The arcade owns the flow, the same for every game:** Ready ("tap to start") → Playing → Paused → Over (score card). A game only builds, plays, adds score and calls `EndRun()`.
- To play a game in the editor: **Tools > Ace Barcade > Play Game...**

## Writing a game

```csharp
using AceBarcade;
using UnityEngine;

namespace AceBarcade.Games.PaddleRally
{
    [ArcadeGame(1, "paddle-rally", "Paddle Rally", Controls.Drag, Blurb = "Keep the ball alive.")]
    public sealed class PaddleRally : ArcadeGame
    {
        SpriteRenderer _paddle;

        protected override void Build()
        {
            _paddle = MakeSprite("Paddle", GameSprite("paddle"), new Vector2(0, -6));
        }

        protected override void Play(float dt)
        {
            if (Input.Pressed)
                _paddle.transform.localPosition = new Vector2(Mathf.Clamp(Input.Position.x, -3.5f, 3.5f), -6);
            // ...AddScore(1) on a hit, EndRun() when the ball is lost.
        }
    }
}
```

- `Play(dt)` is only called while playing. Pausing also sets `Time.timeScale` to 0, so helper components that use `Time.deltaTime` stop too.
- Input is one finger: `Input.Pressed`, `Down`, `Up`, `Tapped`, `Swipe`, `Position`, `DragDelta`, `HeldFor`. Positions are in world units. Never read the Input System directly in a game.
- Colors come from `Palette`, sizes from `Style`.
- Put rules and maths that can be tested (scoring, spawning, collisions on paper) in plain C# classes, and test them in edit mode.

## Numbers, ids and names

- **Number:** 1 to 999, sets the hub order and the folder name. Never reused.
- **Id:** lower-case words with dashes (`paddle-rally`). Best scores are saved under it, so it never changes once a game ships to the phone.
- **Folder:** `G` + the number in 3 digits + the title without spaces: `G001_PaddleRally`. `GameInfo.Folder` works it out.
- **Namespace:** `AceBarcade` for shared code, `AceBarcade.Games.<TitleWithoutSpaces>` for a game, so 500 games never clash.

## File layout

```
Art/
  palette.txt                the palette (Palette.cs must match it; a test checks)
  build-sprites.sh           SVG -> PNG for every SVG under Art/
  check-style.sh             the house style check (check_style.py does the work)
  Shared/                    art every game can use: HUD icons, particles, props
  Games/G001_Name/           one game's art, including icon.svg (360 x 360)
  _anything/                 scratch: skipped by both scripts
Assets/
  Scenes/Arcade.unity        the only scene
  Resources/Sprites/         generated PNGs (a mirror of Art/); don't edit by hand
  Scripts/                   AceBarcade assembly: the shared kit
    Core/                    Arcade, ArcadeRoot, ArcadeGame, GameRegistry, ArcadeInput,
                             GestureTracker, ArcadeCamera, Palette, Style, Scores, Sprites
    Tests/                   edit mode tests (AceBarcade.Tests)
  Games/                     AceBarcade.Games assembly: every game
    G001_Name/               one game's code
  Editor/                    AceBarcade.Editor: Tools > Ace Barcade menu, import settings, setup
  link.xml                   keeps every game in Android builds (they're found by reflection)
Tools/compile-check.sh       compiles everything without opening Unity
docs/                        STYLE.md and this file
```

## Adding a game

1. Pick the next free number and an id. Add the game to the list in `PLAN.md`.
2. Draw its art in `Art/Games/G###_Name/`, starting with `icon.svg`. Run **Tools > Ace Barcade > Build Sprites** (or `Art/build-sprites.sh`), which checks the style first.
3. Write the game in `Assets/Games/G###_Name/<Name>.cs`, as above.
4. Run the tests and `Tools/compile-check.sh`, then play it on the phone.
5. Go through the new game checklist at the end of [STYLE.md](STYLE.md).

## Project settings

Unity locks its settings files while the editor is open, so settings are changed in code: `Assets/Editor/ProjectSetup.cs`, run from **Tools > Ace Barcade > Apply Project Settings** (or in batch mode, see the file). It sets the identity, Android, portrait, IL2CPP ARM64, the render settings and the scene. Change it there, not in the Project Settings window, so the setup can always be run again.

## When there are many games

- All games share one assembly for now. If compiling gets slow (likely past 100 or so games), split it into batches (`AceBarcade.Games.001-100`, ...), each referencing `AceBarcade`.
- Sprites load from `Resources` per game and unload when you leave it. If the build gets too big or slow to start, move game art to Addressables, one group per game.
- Best scores are keyed by id, so games can be renumbered or moved between assemblies without losing saves.
