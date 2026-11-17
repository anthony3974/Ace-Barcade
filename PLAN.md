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

**Status (2026-11-17):** the project, repo, art pipeline and style check are set up, with the shared core (game base class, registry, input, camera, scores, sprite loading) and 11 passing edit mode tests. There are no games yet. Until the hub exists (task 4), the arcade opens the first game.

### Setup
- [x] **1. Project and repo.** Universal 2D project, identity `Tony3974`, product `Ace Barcade`, id `com.Tony3974.AceBarcade`. Android, portrait, IL2CPP ARM64, min API 24, no HDR or MSAA. Assemblies: `AceBarcade` (shared), `AceBarcade.Games`, `AceBarcade.Editor`, `AceBarcade.Tests`. The core: `ArcadeGame`, `GameRegistry`, `ArcadeInput` (tap, hold, drag, swipe, Back), `ArcadeCamera` (fixed 9 x 16 view), `Scores`, `Sprites`, `Palette`, `Style`. *Check: edit mode tests pass, and the repo is on GitHub.*
- [x] **2. Art pipeline and style check.** `Art/build-sprites.sh` and `Art/check-style.sh`, import settings for `Assets/Resources/Sprites/`. *Check: an SVG that breaks the rules is rejected with a reason for each, and a good one passes.*

### Prove the look (it comes first)
- [ ] **3. Style sheet and first phone build.** Pick the font (candidates: Fredoka or Lilita One, both free under the OFL) and bring it in with TextMeshPro. Draw a sample sheet in the house style: a player character, a ball, a paddle, a pickup, a hazard, a wall, a button and a game icon, on a full-screen 1080 x 1920 background mock. Add **Tools > Ace Barcade > Build APK** and show the sheet on the phone. Adjust the palette or the rules in `docs/STYLE.md` if something looks wrong. *Check: the owner likes it on their phone.*

### Arcade shell
- [ ] **4. Hub and shared HUD.** The hub: a scrolling grid of game cards (icon, number, title, best score), using the icon template. The HUD every game uses: score, best, pause button, "Tap to start", the pause screen and the game-over card (score, best, new best, Retry, Arcade). Respect the phone's safe area (notches). Android Back: pause in a game, leave from pause or game over, quit from the hub. *Check: on the phone, hub → game → pause → game over → retry → back to the hub, with a stand-in test game.*
- [ ] **5. Shared feel and sound.** Shared helpers every game uses: tweens (pop, squash and stretch), screen shake, flat particle bursts, haptics (a short buzz on a hit), and the shared sound set (tap, score, hit, lose, new best, UI click) with a mute toggle in the hub. *Check: the test game uses each one and it feels good on the phone.*

### Games
Pick the next game with the owner. Each game is one task: art, code, icon, tests for its rules, and the new game checklist in `docs/STYLE.md`. *Check for every game: it plays well on the phone and looks at home in the hub next to the others.*

- [ ] **6. Game 001.** The first game, chosen with the owner. A paddle and ball game is a good first pick: it's the simplest, and it tests the whole kit.

## Game ideas
A pool to pick from, one thumb, portrait. Working titles only; every game gets its own original name and look.
- **Paddle and ball:** keep a ball alive with a paddle along the bottom (drag).
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
