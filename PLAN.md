# Ace Barcade: Plan

**When asked "what's next?", the answer is the first open item under NEXT UP.**
**To continue in a new chat:** "Read PLAN.md and start the NEXT UP task."

Unity 6000.0.85f1, Universal 2D (URP), Input System. Android only, portrait. Public GitHub repo. A personal project: it won't be published to a store.

## The idea
An arcade on your phone: one app holding many small games, from 1 up to as many as 500, depending on how far the project goes. Open the app, scroll the cabinet of game cards, tap one, play a quick run, beat your best, try another.

## The one rule: nothing looks out of place
Every game shares one look and one feel, so a runner never looks high-end next to a crude paddle game. The full rules are in [docs/STYLE.md](docs/STYLE.md). In short:
- **Flat, chunky cartoon.** Big simple shapes, a thick Ink outline (8 px), a flat shadow side, light from the top left. No gradients, transparency, glow or lighting.
- **One palette** (`Art/palette.txt`, 27 colors), and colors mean the same thing everywhere: the player is Yellow, danger is Red, good things are Teal and Green.
- **Low-res on purpose.** Art is authored at 120 px per unit and rendered 1x. No high-res textures.
- **2D only, portrait, one thumb.** A 9 x 16 unit play area on every phone.
- **Shared everything else:** one font, one HUD, one game-over card, one sound set, one set of effects.
- **Enforced, not just written down.** `Art/check-style.sh` rejects art that breaks the rules and runs before every sprite build; a test keeps the palette in code and in art the same.

## How it's built
- One scene. Each game is a C# class extending `ArcadeGame`, tagged `[ArcadeGame(number, id, title, controls)]`, and found automatically: adding a game never touches a central list. See [docs/CONVENTIONS.md](docs/CONVENTIONS.md).
- Games build their world in code. Retry throws the game away and builds it fresh.
- The arcade owns the flow for every game: Ready → Playing → Paused → Over. A game only builds, plays, adds score and ends the run.
- Art is hand-built SVG in `Art/`, converted to PNG by `Art/build-sprites.sh` (needs `rsvg-convert` from librsvg, and `python3` for the style check).

## Not to copy
Games can be in any classic genre (paddle and ball, runner, snake, brick breaker...), but each one is our own: original names, art, characters and levels, drawn in the house style. No recreations of a specific existing game, its name or its look.

---

## NEXT UP (in order)

Each task is small enough for one session and ends with a check. Tick it off when the check passes.

Unity locks the project while the editor is open, so change Project Settings through `Assets/Editor/ProjectSetup.cs` (**Tools > Ace Barcade > Apply Project Settings**) rather than the settings window or the files on disk.

**Status (2026-11-17):** Game 001, **Paddle Rally**, is built and plays end to end with the shared HUD (score, best, pause, tap to start, pause screen, game-over card), the arcade font, the shared feel (squash, pop, shake, bursts) and the first Android build (`Builds/AceBarcade.apk`, made by **Tools > Ace Barcade > Build APK**). 21 edit mode and 2 play mode tests pass. It's waiting on the owner's phone playtest (tasks 3 and 6). The hub and sound are still to come; until the hub exists, the arcade opens the first game.

### Setup
- [x] **1. Project and repo.** Universal 2D project, identity `Tony3974`, product `Ace Barcade`, id `com.Tony3974.AceBarcade`. Android, portrait, IL2CPP ARM64, min API 24, no HDR or MSAA. Assemblies: `AceBarcade` (shared), `AceBarcade.Games`, `AceBarcade.Editor`, `AceBarcade.Tests`, `AceBarcade.PlayTests`. The core: `ArcadeGame`, `GameRegistry`, `ArcadeInput` (tap, hold, drag, swipe, Back), `ArcadeCamera` (fixed 9 x 16 view), `Scores`, `Sprites`, `Palette`, `Style`. *Check: edit mode tests pass, and the repo is on GitHub.*
- [x] **2. Art pipeline and style check.** `Art/build-sprites.sh` and `Art/check-style.sh`, import settings for `Assets/Resources/Sprites/`. *Check: an SVG that breaks the rules is rejected with a reason for each, and a good one passes.*

### Prove the look, with the first game
The style sheet was folded into Game 001: its art, the shared UI art and the font are the sample.
- [ ] **3. The look on a phone.** Font: **Lilita One** (SIL Open Font License, `Assets/Fonts`), made into a TextMeshPro font with the Ink outline by `FontSetup.cs`. Shared UI art in `Art/Shared/UI` (primary and secondary buttons, the pause button, the game-over card). **Tools > Ace Barcade > Build APK**. Built; waiting on the owner. *Check: the owner likes how it looks on their phone. Adjust the palette or `docs/STYLE.md` now if anything looks wrong, before more games depend on it.*
- [ ] **6. Game 001: Paddle Rally.** Drag to slide a paddle along the bottom and keep the ball out of the red spikes. Each return scores 1 and speeds the ball up; where the ball lands on the paddle sets its angle. Every 4 returns a teal gem appears up the court, worth 5. Rules in `RallySim` (plain C#, 10 edit mode tests); a play mode test plays a rally, pauses, loses and retries. Built; waiting on the owner's phone playtest. *Check: each item below plays as described on the phone.*
  - **Install:** copy `Builds/AceBarcade.apk` to the phone and open it (allow installs from this source once). The app opens straight into Paddle Rally, in portrait.
  - **Ready:** the title, "Keep the ball out of the spikes" and a pulsing TAP TO START. The ball rests on the paddle.
  - **Control:** touching starts the rally. The paddle follows your finger left and right (touch anywhere below it), quickly but not instantly, and stops at the walls.
  - **Rally:** the score goes up by 1 on each return with a little squash and spark. The ball gets faster each time. Hitting near the paddle's ends sends it off at a sharp angle.
  - **Gem:** after every 4th return a teal gem pops in up the court. Hitting it with the ball scores 5 with a teal burst.
  - **Losing:** a missed ball drops into the spikes with a red burst and a big shake. About half a second later the game-over card shows the score, the best, and NEW BEST! the first time.
  - **Retry and Arcade:** RETRY starts a fresh run at 0. ARCADE goes to the hub (for now, a fresh Paddle Rally).
  - **Pause:** the pause button (top right) and the phone's Back button both pause. Tapping the pause button doesn't move the paddle. RESUME carries on; Back on the pause screen leaves.
  - **Screen:** the score and pause button sit clear of the notch. Text and outlines look sharp. It runs smoothly.
  - **Feel:** the speed ramp is fun, not too slow at the start, not impossible by about 25 returns.

### Arcade shell
The shared HUD (score, best, pause, tap to start, pause screen, game-over card, safe area) and the shared feel (`Juice`: squash, pop, shake, bursts) were built with Game 001.
- [ ] **4. The hub.** A scrolling grid of game cards (icon, number, title, best score), built from each game's `icon.svg`. The app opens on it; ARCADE and Back return to it; Back in the hub quits. *Check: on the phone, hub → Paddle Rally → game over → ARCADE → hub, with the best score shown on the card.*
- [ ] **5. Sound and haptics.** The shared sound set (tap, return or score, hit, lose, new best, UI click) in one style, a short buzz on hits, and a mute toggle in the hub. Paddle Rally uses them. *Check: it sounds and feels good on the phone, and mute works.*

### Games
Pick each next game with the owner. Each game is one task: art, code, icon, tests for its rules, a play mode test, and the new game checklist in `docs/STYLE.md`. *Check for every game: it plays well on the phone and looks at home next to the others.*
- [ ] **7. Game 002.** Chosen with the owner from the ideas below.

## Game ideas
A pool to pick from, one thumb, portrait. Working titles only; every game gets its own original name and look.
- ~~**Paddle and ball:**~~ done as Paddle Rally (Game 001).
- **Lane runner:** a top-down run, swiping between three lanes to dodge and collect (swipe).
- **Brick breaker:** clear a wall of bricks with a bouncing ball (drag).
- **Snake:** grow a snake by eating, without hitting yourself (swipe).
- **Stacker:** drop sliding blocks on a tower and keep it straight (tap).
- **Cave flyer:** hold to rise, let go to fall, through a narrowing cave (hold).
- **Bouncer:** a ball that bounces on its own; steer it from platform to platform upward (drag).
- **Whack:** tap things as they pop up, but not the Red ones (tap).
- **Dodge field:** drag around to avoid falling hazards while grabbing pickups (drag).
- **Slicer:** swipe through flying objects, not the Red ones (swipe).
- **Timing ring:** tap when a spinning marker crosses the target (tap).
- **Gap jumper:** a side-on runner that jumps on tap, over gaps and Red spikes (tap).

## Maybe later
- A daily challenge game with its own leaderboard on the phone.
- Hub sorting and filters (by controls, most played) once there are more than about 30 games.
- Split the games assembly into batches if compiling gets slow; move game art to Addressables if the build gets big (see [docs/CONVENTIONS.md](docs/CONVENTIONS.md)).
