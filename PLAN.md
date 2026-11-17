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

**Status (2026-11-17):** Game 001, **Paddle Rally**, is built and plays end to end with the shared HUD (score, best, pause, tap to start, pause screen, game-over card), the arcade font, the shared feel (squash, pop, shake, bursts) and the first Android build (`Builds/AceBarcade.apk`, made by **Tools > Ace Barcade > Build APK**). 21 edit mode and 2 play mode tests pass. It's waiting on the owner's phone playtest (tasks 3 and 6). The hub (main menu) is built too: the app opens on it. Sound is still to come. Game 002, **Drop Stack**, is merged (task 7). Game 003, **Brick Burst**, is merged (task 8). Game 004, **Rocket Lanes**, is merged (task 9). Next game: 005, **Paddle Duel** (task 10); every game and its number is in the [games list](#games-list).

### Setup
- [x] **1. Project and repo.** Universal 2D project, identity `Tony3974`, product `Ace Barcade`, id `com.Tony3974.AceBarcade`. Android, portrait, IL2CPP ARM64, min API 24, no HDR or MSAA. Assemblies: `AceBarcade` (shared), `AceBarcade.Games`, `AceBarcade.Editor`, `AceBarcade.Tests`, `AceBarcade.PlayTests`. The core: `ArcadeGame`, `GameRegistry`, `ArcadeInput` (tap, hold, drag, swipe, Back), `ArcadeCamera` (fixed 9 x 16 view), `Scores`, `Sprites`, `Palette`, `Style`. *Check: edit mode tests pass, and the repo is on GitHub.*
- [x] **2. Art pipeline and style check.** `Art/build-sprites.sh` and `Art/check-style.sh`, import settings for `Assets/Resources/Sprites/`. *Check: an SVG that breaks the rules is rejected with a reason for each, and a good one passes.*

### Prove the look, with the first game
The style sheet was folded into Game 001: its art, the shared UI art and the font are the sample.
- [ ] **3. The look on a phone.** Font: **Lilita One** (SIL Open Font License, `Assets/Fonts`), made into a TextMeshPro font with the Ink outline by `FontSetup.cs`. Shared UI art in `Art/Shared/UI` (primary and secondary buttons, the pause button, the game-over card). **Tools > Ace Barcade > Build APK**. Built; waiting on the owner. *Check: the owner likes how it looks on their phone. Adjust the palette or `docs/STYLE.md` now if anything looks wrong, before more games depend on it.*
- [ ] **6. Game 001: Paddle Rally.** Drag to slide a paddle along the bottom and keep the ball out of the red spikes. Each return scores 1 and speeds the ball up; where the ball lands on the paddle sets its angle. Every 4 returns a teal gem appears up the court, worth 5. Rules in `RallySim` (plain C#, 10 edit mode tests); a play mode test plays a rally, pauses, loses and retries. Built; waiting on the owner's phone playtest. *Check: each item below plays as described on the phone.*
  - **Install:** copy `Builds/AceBarcade.apk` to the phone and open it (allow installs from this source once). It opens on the hub, in portrait; tap Paddle Rally.
  - **Ready:** the title, "Keep the ball out of the spikes" and a pulsing TAP TO START. The ball rests on the paddle.
  - **Control:** touching starts the rally. The paddle follows your finger left and right (touch anywhere below it), quickly but not instantly, and stops at the walls.
  - **Rally:** the score goes up by 1 on each return with a little squash and spark. The ball gets faster each time. Hitting near the paddle's ends sends it off at a sharp angle.
  - **Gem:** after every 4th return a teal gem pops in up the court. Hitting it with the ball scores 5 with a teal burst.
  - **Losing:** a missed ball drops into the spikes with a red burst and a big shake. About half a second later the game-over card shows the score, the best, and NEW BEST! the first time.
  - **Retry and Arcade:** RETRY starts a fresh run at 0. ARCADE goes back to the hub.
  - **Pause:** the pause button (top right) and the phone's Back button both pause. Tapping the pause button doesn't move the paddle. RESUME carries on; Back on the pause screen leaves.
  - **Screen:** the score and pause button sit clear of the notch. Text and outlines look sharp. It runs smoothly.
  - **Feel:** the speed ramp is fun, not too slow at the start, not impossible by about 25 returns.

### Arcade shell
The shared HUD (score, best, pause, tap to start, pause screen, game-over card, safe area) and the shared feel (`Juice`: squash, pop, shake, bursts) were built with Game 001.
- [ ] **4. The hub.** The main menu: an ACE BARCADE header, then a scrolling grid of game cards, **2 columns x 6 rows** a screen on every phone (cards are sized from the screen). Each card shows the game's icon as a thumbnail on the left, and its number, controls, title and best score on the right. Until there are 12 games, "coming soon" cards fill the empty slots. The app opens on it; ARCADE and Back return to it; Back in the hub quits the app. Cards use a 9-slice tile (`Art/Shared/UI/tile_sliced.svg`). 4 play mode tests, including scrolling with 13 games. Built; waiting on the owner's phone check. *Check, on the phone:*
  - The app opens on the hub: Paddle Rally's card top left, eleven "coming soon" cards, two columns and six rows filling the screen below the header.
  - Tapping the Paddle Rally card starts it. Dragging on the grid doesn't start a game.
  - After a game over, ARCADE returns to the hub, and the card shows the new best.
  - Back on the pause or game-over screen returns to the hub; Back in the hub closes the app.
  - The header sits clear of the notch.
- [ ] **5. Sound and haptics.** The shared sound set (tap, return or score, hit, lose, new best, UI click) in one style, a short buzz on hits, and a mute toggle in the hub. Paddle Rally uses them. *Check: it sounds and feels good on the phone, and mute works.*

### Games
Take the next game from the [games list](#games-list) (or swap the order with the owner). Each game is one task: art, code, icon, tests for its rules, a play mode test, and the new game checklist in `docs/STYLE.md`. *Check for every game: it plays well on the phone and looks at home next to the others.*
- [ ] **7. Game 002: Drop Stack** (on the `tetris` branch). A falling-block puzzle: pieces fall into an 8 x 14 well, and a full row clears. Our own take on the genre: its own name and house-style look, a smaller well, our own set of six pieces mixing 3-block and 4-block shapes, and teal gem blocks worth 25 when their row clears. Clearing 1, 2, 3 or 4 rows at once scores 10, 30, 60 or 100 (times the level); every 8 rows is a level, and pieces fall faster. Rules in `DropSim` (12 edit mode tests); 2 play mode tests play it with simulated touches. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Drop Stack's card is in the hub next to Paddle Rally; tapping it shows the title and TAP TO START, with the next piece shown small above the well.
  - **Tap** turns the piece (the tap that starts the game doesn't). Next to a wall it slides out to turn.
  - **Drag** sideways slides it one column per block-width your finger moves, and it stops at the walls.
  - **Drag down** drops it faster; a quick **flick down** drops it all the way.
  - The grey shadow shows where it will land. A resting piece can still be slid for a moment before it locks.
  - A full row flashes and clears, with a small shake (a big one for 3 or more rows). A gem in a cleared row bursts teal and adds 25.
  - Stacking up to the red line ends the game with the usual game-over card.
  - It gets noticeably faster after 8 rows.
- [ ] **8. Game 003: Brick Burst** (on the `brick-breaker` branch). Drag the paddle to knock the ball into a wall of bricks. Our own take on the genre: the house style, Paddle Rally's court and paddle, tough grey bricks that crack on the first hit and break on the second, teal gem bricks worth 5, and an endless run of walls, each a row taller and a little faster, with tough bricks from the second wall and gaps from the third. Three lives. Bricks score 1, tough bricks 2, gems 5, and a cleared wall 10. The paddle, ball, gem and spikes are now shared props (`Art/Shared/Props`), used by both paddle games. Rules in `BrickSim` (11 edit mode tests); a play mode test breaks bricks, loses three balls and ends. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Brick Burst's card is in the hub; its start screen shows the wall of 5 rows, with the title below it.
  - The paddle and ball feel exactly like Paddle Rally's.
  - Bricks break with a burst in their colour. A grey brick shows a crack on the first hit (with a little shake) and breaks on the second.
  - A teal gem brick bursts teal and is worth 5.
  - Missing the ball costs one of the three small balls under the best score; the ball waits on the paddle for a second, then goes.
  - Clearing a wall brings a taller, faster one, popping in, with a big shake.
  - Losing the third ball ends the game with the usual card.
- [ ] **9. Game 004: Rocket Lanes** (on the `runner` branch). A top-down lane runner: a yellow rocket flies up three lanes while rows of red mines scroll down at it, and a swipe left or right moves it one lane. Every row leaves a lane open; each row passed scores 1, and teal crystals in open lanes score 5. The scroll speeds up a little with every row, up to a cap. **Fair by time:** if a row makes you move, the gap before it leaves at least reaction plus swipe time (0.5 s) after the last row is clear, and never asks for more than one lane; rows closer than that keep every open lane open. The gap shrinks and the speed rises only as far as that still holds at full speed. Rules and the fairness constants in `RocketLanesSim` (11 edit mode tests, including a bot with human reaction times that survives past top speed); 2 play mode tests swipe, dodge, pick up crystals, crash and retry. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Rocket Lanes' card is in the hub with the rocket icon; tapping it shows the title and TAP TO START, the rocket in the middle lane with its flame flickering.
  - The touch that starts the run doesn't change lanes.
  - **Swipe** left or right moves one lane, as soon as your finger has moved a little (not when you lift it). The rocket slides over and leans into the move, with a little squash.
  - One swipe is one lane, even if you keep dragging. Quick back-to-back swipes each count. The edges stop it.
  - Mines pop in at the top in rows of one or two; there's always a way through. Sometimes two rows come close together: staying in an open lane gets you through both.
  - Flying through a crystal scores 5 with a teal burst.
  - Hitting a mine gives a red burst and a big shake, then the game-over card.
  - It speeds up as you go, and late in a run (about 50 rows in, top speed) it's hard but still winnable.
  - Stars drift slower than the lane dashes, so space has depth. Nothing important hides under the score.
- [ ] **10. Game 005: Paddle Duel.** Two-paddle table tennis against the computer, in portrait: your yellow paddle along the bottom (drag), the computer's purple paddle along the top, side walls to bank shots off. Paddle Rally's ball and paddle feel (shared props in `Art/Shared/Props`), with where the ball hits your paddle setting its angle. A point you win (the ball gets past the computer) scores 5, and each return scores 1. You have three balls, like Brick Burst; missing costs one, and losing the third ends the run. Each point you win makes the ball a little faster and the computer a little sharper, up to a cap. **The computer plays fair:** it only sees the ball after a short reaction delay, moves at a capped speed, and aims with a little error, so sharp angles near your paddle's ends beat it. All of that is constants in `DuelSim`, with edit mode tests that a perfect shot gets past it at every skill level and that it returns easy balls. A play mode test plays points both ways, loses three balls and retries. *Check, on the phone:*
  - Paddle Duel's card is in the hub; the start screen shows both paddles and the ball resting on yours.
  - Your paddle feels exactly like Paddle Rally's.
  - The computer moves smoothly, never teleports, and misses sometimes; angled shots off your paddle's ends beat it more often.
  - Winning a point bursts and shakes, and scores 5; missing costs one of three balls.
  - It gets harder as you win points, but stays winnable.

## Games list
Every game, built or planned, with its number and id. **This list is where numbers come from.** Before starting a game, take the first planned row and mark it *In progress* here, so two branches never take the same number (Brick Burst and Rocket Lanes both reached for 003 once). Numbers are never reused, and an id never changes once the game is on the phone, because best scores are saved under it. A planned game's title and id are working names and can still change before it's built. Every game gets its own original name and look (see *Not to copy*).

| # | Id | Title | Controls | Genre | Status | Task |
|---|---|---|---|---|---|---|
| 001 | `paddle-rally` | Paddle Rally | Drag | Paddle and ball | Built, on main; waiting on phone check | 6 |
| 002 | `drop-stack` | Drop Stack | Drag | Falling blocks | Built, on main; waiting on phone check | 7 |
| 003 | `brick-burst` | Brick Burst | Drag | Brick breaker | Built, on main; waiting on phone check | 8 |
| 004 | `rocket-lanes` | Rocket Lanes | Swipe | Lane runner | Built, on main; waiting on phone check | 9 |
| 005 | `paddle-duel` | Paddle Duel | Drag | Two-paddle table tennis against the computer | **Next** | 10 |
| 006 | `slide-nine` | Slide Nine | Tap | Sliding puzzle: 3 x 3 tiles with one empty slot; tap a tile next to the gap to slide it. A shuffle button, and a move counter or timer. Best scores keep the highest number, so the score has to go up for playing well (for example points that drop with every move, or puzzles solved against the clock) | Planned | |
| 007 | `noodle` | Noodle | Swipe | Snake: swipe to steer; grow by eating, without hitting yourself | Planned | |
| 008 | `wing-it` | Wing It | Tap | Flappy-style flyer: one tap to flap through gaps | Planned | |
| 009 | `pop-up` | Pop Up | Tap | Whack-a-mole: reflex taps on things as they pop up, but not the red ones | Planned | |
| 010 | `bounce-up` | Bounce Up | Hold | Endless jumper: bounce up from platform to platform; hold the left or right half of the screen to steer (the halves are the "two buttons": games don't draw their own buttons) | Planned | |
| 011 | `star-swarm` | Star Swarm | Drag (TBD) | Galaga-style shooter: a ship along the bottom, waves of enemies above. Controls to decide: drag, or left and right halves | Planned | |
| 012 | `duck-runner` | Duck Runner | Tap, swipe down | Side-scrolling runner with a duck: jump over foxes, cattails and fallen logs; duck under low hawks and hanging branches; decoy ducks and snapping turtles for variety | Planned | |
| 013 | `stack-tower` | Stack Tower | Tap | Stacker: tap to drop each sliding block on the tower; overhangs get cut off | Planned | |
| 014 | `echo-pads` | Echo Pads | Tap | Memory pattern: Simon-style coloured pads light up in a growing sequence; tap it back | Planned | |
| 015 | `pop-shot` | Pop Shot | Drag | Bubble shooter: aim, shoot, and match three or more colours to pop them | Planned | |
| 016 | `cave-glide` | Cave Glide | Hold | Cave flyer: hold to rise, let go to fall, through a narrowing cave | Planned | |
| 017 | `rain-dodge` | Rain Dodge | Drag | Dodge field: drag around falling hazards, grab pickups | Planned | |
| 018 | `ring-stop` | Ring Stop | Tap | Timing ring: tap when the spinning marker crosses the target | Planned | |
| 019 | `slice-storm` | Slice Storm | Swipe | Slicer: swipe through flying things, not the red ones | Planned | |

Planned games are in the owner's order (006 to 015), then older ideas (016 on). The "-style" notes name the genre only: each game still gets its own original name, characters and look. New ideas go at the bottom as *Planned*, with the next free number.

## Maybe later
- A daily challenge game with its own leaderboard on the phone.
- Hub sorting and filters (by controls, most played) once there are more than about 30 games.
- Past about 100 games, only build the hub cards that are on screen (a recycled list) so the hub stays fast and light.
- Split the games assembly into batches if compiling gets slow; move game art to Addressables if the build gets big (see [docs/CONVENTIONS.md](docs/CONVENTIONS.md)).
