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

**Status (2026-11-17):** Game 001, **Paddle Rally**, is built and plays end to end with the shared HUD (score, best, pause, tap to start, pause screen, game-over card), the arcade font, the shared feel (squash, pop, shake, bursts) and the first Android build (`Builds/AceBarcade.apk`, made by **Tools > Ace Barcade > Build APK**). 21 edit mode and 2 play mode tests pass. It's waiting on the owner's phone playtest (tasks 3 and 6). The hub (main menu) is built too: the app opens on it. Sound is still to come. Game 002, **Drop Stack**, is merged (task 7). Game 003, **Brick Burst**, is merged (task 8). Game 004, **Rocket Lanes**, is merged (task 9). Game 005, **Paddle Duel**, is merged (task 10). Game 006, **Slide Nine**, is merged (task 11). Game 007, **Noodle**, is merged (task 12). Game 008, **Wing It**, is merged (task 13). Game 009, **Pop Up**, is merged (task 14). Game 010, **Bounce Up**, is merged (task 15). Game 011, **Star Swarm**, is merged (task 16). Game 012, **Duck Runner**, is merged (task 17). Next game: 013, **Stack Tower**; every game and its number is in the [games list](#games-list).

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
- [ ] **10. Game 005: Paddle Duel** (on the `paddle-duel` branch). Two-paddle table tennis against the computer, in portrait: your yellow paddle along the bottom (drag), the computer's purple paddle along the top, side walls to bank shots off. Paddle Rally's ball and paddle feel (shared props in `Art/Shared/Props`), with where the ball hits your paddle setting its angle. A point you win (the ball gets past the computer) scores 5, and each return scores 1. You have three balls, like Brick Burst; missing costs one, and losing the third ends the run. Each point you win makes the ball a little faster and the computer a little sharper, up to a cap. **The computer plays fair:** it only looks at the ball every so often (every 0.22 s, down to 0.1 s as it gets sharper) and heads for where it last saw it, never reading ahead, at a capped speed (4.5 units a second, up to 7). It aims its returns off centre, away from your paddle. A sharp angle whose sideways speed beats its top speed gets past it. Rules and constants in `DuelSim` (10 edit mode tests: sharp shots get past it about a third of the time at every skill level, while straight ones come back). A play mode test wins points with sharp shots, loses three balls and retries. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Paddle Duel's card is in the hub; the start screen shows both paddles and the ball resting on yours.
  - Your paddle feels exactly like Paddle Rally's.
  - The computer moves smoothly, never teleports, and misses sometimes; angled shots off your paddle's ends beat it more often.
  - Winning a point bursts and shakes, and scores 5; missing costs one of three balls.
  - It gets harder as you win points, but stays winnable.
- [ ] **11. Game 006: Slide Nine** (on the `slide-nine` branch). A 3 x 3 sliding puzzle against the clock: eight numbered tiles and a gap. Tap a tile in the gap's row or column and it slides toward the gap, pushing any tiles between. Put 1 to 8 in order to solve it. A solve scores 10, plus a bonus of up to 10 that drops by 1 for each move over par, and adds 15 seconds (the clock starts at 90 and holds at most 99); then a harder puzzle comes. Every puzzle is exactly *par* moves from solved, the fewest possible: 6 for the first, 2 more each puzzle, up to 20. Par comes from a table of all 181,440 positions a 3 x 3 puzzle can be in, so the difficulty is exact. Tiles in their own cell turn green. The clock is a bar above the board that turns red in the last 10 seconds. No shuffle button: each puzzle and each retry is freshly shuffled, and games don't draw their own buttons. Rules in `SlideSim` (9 edit mode tests); a play mode test solves two puzzles in par, runs the clock out and retries. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Slide Nine's card is in the hub; tapping it shows the shuffled board, the full clock bar, and MOVES 0 PAR 6 under the board.
  - The tap that starts the game doesn't move a tile.
  - Tapping a tile next to the gap slides it over smoothly; tapping one two away in the same row or column pushes both. Taps elsewhere do nothing.
  - Tiles turn green in their own cell.
  - Solving bursts, shows the points and +15s, then a new, harder puzzle pops in.
  - The bar empties as time runs, turns red near the end, and the game-over card shows when it runs out.
  - The numbers are easy to read and taps land on the tile you meant.
- [ ] **12. Game 007: Noodle** (on the `noodle` branch). A snake game: a yellow noodle crawls over an 11 x 17 board, one cell at a time. Our own take on the genre: the edges wrap round (out one side, in the other), so the only walls are red chilies and the noodle itself. Swipe to steer; the finger can stay down, and every 0.6 units it moves one way is a turn, so "right then up" in one stroke is two turns. Two turns can wait for the next cell; turning straight back is ignored. Each green pea scores 1 and makes the noodle one longer and a little faster (0.2 s a cell, down to 0.12 s after 27 peas). Every 5th pea brings a teal gem worth 5 that stays for 40 cells (blinking near the end), and a red chili that stays for good, up to 8. Chilies appear at least 5 cells from the head and never touch each other, even at a corner, so they can't wall anything in. Crawling into a chili or into its own body ends the run; crawling into the cell the tail is leaving is fine. The noodle is drawn in code as a tube of outlined circles, so it bends smoothly; a swallowed pea bulges down it. Rules in `NoodleSim` (11 edit mode tests, including a bot that gets past top speed and all 8 chilies on every seed); a play mode test steers with swipes, eats three peas, bites itself and retries. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Noodle's card is in the hub; tapping it shows the noodle at the bottom of the board, looking up at a pea.
  - The tap that starts the run doesn't turn it. A swipe on the starting touch does.
  - **Swipe** turns it as soon as your finger has moved a little. A swipe the way it's already going, or straight back, does nothing.
  - **One stroke, two turns:** swiping right then up without lifting turns right, then up on the next cell. Quick separate swipes all count too.
  - It slides smoothly from cell to cell, and its eyes turn to look where it's going.
  - Leaving one edge brings it in at the opposite one; the part crossing is clipped at the board's edge (the eyes blink out for a moment as the head crosses).
  - A pea scores 1 with a lime burst; a bulge travels down the noodle and it gets one longer. It gets faster with each pea.
  - After every 5th pea a teal gem pops in somewhere (5 points, blinks before it goes) and a red chili pops in, never right in front of you.
  - Running into a chili or your own body gives a red burst and a big shake, then the game-over card. Chasing your own tail round a tight loop is safe.
  - Steering feels responsive at top speed, and the swipe threshold isn't so small that a tap or a wobble turns it.
- [ ] **13. Game 008: Wing It** (on the `wing-it` branch). A one-tap flyer: tap to flap a yellow bee through the gaps in red columns, over a floor of the shared spikes. Our own take on the genre: a striped bee with a beating wing, red columns capped with thorns (danger is red), Slate hills and a moon drifting behind. Gravity pulls the bee down and every tap sends it up at the same speed, whatever it was doing; the ceiling just stops it. Each column passed scores 1, and a teal gem in the middle of every 4th gap scores 5. The columns come a little faster with each one (3 to 4.2 units a second, at 30 columns) and the gaps narrow (3.6 to 2.6 units, at 25). **Fair by design:** a gap never rises more than 2 units above the last, and never drops further than the bee can fall even if you flapped just as you left the last column (the gap, less the bee, less half a unit). Rules in `WingSim` (8 edit mode tests, including a bot with a 0.12 s reaction lag and no more than about 6 taps a second that gets through 65 columns on every seed); a play mode test flaps through 9 columns, picks up 2 gems, falls and retries. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Wing It's card is in the hub; tapping it shows the bee hovering below TAP TO START, its wing beating.
  - The tap that starts the run is also the first flap.
  - **Tap** anywhere flaps: the bee jumps up the same height every time, tilts up, and its wing beats faster for a moment. It tilts nose down as it falls.
  - Flying into the top of the screen just stops it.
  - The first column's gap is right in front of the bee. Each column passed scores 1.
  - A teal gem sits in every 4th gap; flying through it scores 5 with a teal burst and a small shake.
  - Touching a column's thorns or the floor spikes gives a red burst and a big shake; the bee tumbles onto the spikes and the game-over card shows.
  - Grazing the thorn tips without being caught feels fair, and no hit looks like a miss.
  - It speeds up and the gaps narrow, and around 30 columns in it's hard but every gap still feels reachable, including big drops.
- [ ] **14. Game 009: Pop Up** (on the `pop-up` branch). Whack-a-mole: critters pop out of nine hollow tree stumps in a 3 x 3 grid. Our own take on the genre: round critters with big eyes, drawn in the house style, rising from behind the stump's lip. Tap a green sprout for 1, or a rarer teal one (a lime gem on its head, up for less time) for 5. Tapping a red spiky one ends the run, and every sprout that gets away untapped costs one of three lives (shown as small sprouts under the best score). A tap counts once a critter is a third of the way up, until it's half way back down. As you score, critters come faster (every 0.9 s down to 0.45 s), stay up for less time (1.5 s down to 0.8 s), more of them are red (15% up to 30%), and up to 4 can be up at once, all reaching the hardest at 60 hits. The first three are always sprouts. **Fair by design:** a hole rests for 0.35 s once its critter has gone, and a red one never pops up where a sprout left less than 0.8 s ago, so a late or second tap on a sprout can't land on a red one. Rules in `PopSim` (10 edit mode tests, including a bot with a 0.35 s reaction and at most 5 taps a second that never misses a sprout for 2 minutes at the hardest, and one that also taps again, late, wherever a sprout was, which catches a red tap if the two fairness rules are switched off). A play mode test taps sprouts and teal ones, lets one get away, taps a red one and retries. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Pop Up's card is in the hub; tapping it shows the nine stumps and three small sprouts under the best score. Nothing pops up until the first tap.
  - Critters pop up with a little bounce from inside the stump, and go back down behind its lip.
  - **Tap** a green sprout: a lime burst, a squash, 1 point, and it drops back down. Tapping it again does nothing.
  - A teal one with a gem on its head scores 5 with a bigger burst and a small shake; it's up for less time.
  - A sprout you miss costs a small sprout from the top, with a red puff and a small shake. The third one ends the run.
  - Tapping a red spiky one gives an orange burst and a big shake, and ends the run straight away.
  - Taps land on the critter you aimed at, including on its head when it's fully up, and never on the stump next to it.
  - Tapping late, just as a sprout goes down, never hits a red one.
  - It gets faster and busier, with more red ones, but stays fair after about 60 hits.
- [ ] **15. Game 010: Bounce Up** (on the `bounce-up` branch). An endless jumper: a round yellow bouncer springs off every pad it lands on, always to the same height (3 units), and you hold the left or right half of the screen to steer it (letting go slows it to a stop; holding the other half brakes hard and turns). Our own take on the genre: a chubby yellow critter with an orange tuft and feet, blue pads, a night sky of Slate clouds, and the sides wrap round (out one side, in the other). Pads are one-way: you rise through them and land on top. The view climbs with you and never comes back down; falling off the bottom ends the run, and so does touching a red spiky burr. Height scores 1 a unit, and a teal gem over every 8th pad on the way up scores 5. Higher up, the gaps grow, purple pads swing side to side (from 40), tan cracked pads crumble after one bounce (from 40), red burrs hang in some gaps (from 60), and now and then a lime spring on a pad launches you about 7 units; it all reaches its hardest at 300. **Fair by design:** the pads come as a chain that always leads up, each at most 3 units sideways from the last (counting the whole swing of moving pads) and at most 2.4 above it, well under a bounce; the chain never crumbles. A burr never hangs over a pad within a bounce (or a spring) of it, nor across the way from one pad up to the next, so bouncing straight up, or heading for the next pad, never meets one. Rules in `BounceSim` (10 edit mode tests, including a bot with a 0.15 s reaction lag that climbs past 450 on every seed, and checks that no burr is in the way). A play mode test steers with both halves, climbs past 110 with two gems, falls off and retries. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Bounce Up's card is in the hub; tapping it shows the bouncer standing on the brown ground, pads above it, and "Hold left or right to steer".
  - The touch that starts the run is the first bounce; it bounces the same height every time, with a squash and a puff of dust.
  - **Hold** the left half: it moves left, speeding up quickly but not instantly. The right half moves it right. Letting go slows it to a stop; it faces the way it's going and leans into it.
  - Going off one side brings it in at the other, drawn on both sides while it crosses.
  - It rises through pads from below and lands on them from above. The ground catches it until it scrolls away.
  - The view follows it up, never down; dropping off the bottom ends the run with the game-over card.
  - A gem over a pad scores 5 with a teal burst and a small shake.
  - Higher up: purple pads slide side to side, tan cracked ones burst and vanish after one bounce, a lime spring sends it flying, and red burrs hang in some gaps. Touching a burr gives a red burst and a big shake, and it tumbles away.
  - The next pad up is always reachable, and no burr ever sits right above a pad or in the way to the next one. Around 300 it's hard but fair.
  - Holding near the middle of the screen doesn't flicker between left and right, and the pause button doesn't steer.
- [ ] **16. Game 011: Star Swarm** (on the `star-swarm` branch). A wave shooter: drag to slide a yellow ship along the bottom (it follows the finger, quickly but not instantly, and stays put when you lift it); it fires on its own, four shots a second. Our own take on the genre: round red bug drones with wings and antennae, and horned red brutes that crack on the first hit and break on the second, swaying in a 6-wide formation under the HUD. They drop red bullets straight down, and now and then one peels out of the formation and dives at where the ship was, swaying as it goes, then loops back in from the top. Drones score 1 and brutes 3, double if shot while diving; a cleared wave scores 10, and one enemy in each wave carries a teal gem that falls when it's shot and scores 5 if you catch it. Three lives (small ships under the best score); a hit costs one, wipes the bullets, and the ship blinks, safe, for 2 seconds. Each wave is a little harder up to wave 8: 3 rows growing to 5, brutes from wave 2 (two rows from wave 4), and more dives (up to 3 at once), more and faster bullets, and faster divers. **Fair by design:** bullets fall straight down, and no more than 2 bullets or divers reach the ship's row within any 0.6 seconds, so there's always time and room to slip between them; a hit doesn't free up a diver's place. Rules in `StarSim` (8 edit mode tests, including a bot whose finger lands 0.2 s after it decides that clears 12 waves on every seed without losing a life). A play mode test drags to dodge and shoot through two waves without a hit, catches a gem, then sits still until it's hit three times, and retries. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Star Swarm's card is in the hub; tapping it shows the ship at the bottom, three rows of red drones (one holding a gem), and three small ships under the best score. Nothing fires until the first touch.
  - **Drag** anywhere: the ship slides after your finger and stops at the edges; it leans as it slides, and stays put when you lift. Touching the pause button doesn't move it.
  - Shots go up on their own. A drone bursts orange; a brute cracks first (with a squash), then bursts with a small shake.
  - Divers peel out, point the way they fly, swoop down past the ship and come back in from the top.
  - Shooting the gem carrier drops the gem; catching it scores 5 with a teal burst.
  - A bullet or diver hitting the ship: orange burst, big shake, a small ship goes from the top, and the ship blinks for a moment. The third hit ends the run.
  - Clearing a wave brings a bigger one, popping in. By wave 8 it's busy but there's always a gap to slip through.
  - Bullets are easy to see, and near misses look like misses.
- [ ] **17. Game 012: Duck Runner** (on the `duck-runner` branch). A side-on runner along a riverbank at night: a yellow duck runs at the left while obstacles scroll in from the right. **Tap** to jump (anywhere; a touch just before landing jumps on landing). **Swipe down** to duck under the high ones, and keep the finger down to stay ducked (a quick flick ducks for half a second); a swipe in the air dives straight down first. Our own take on the genre: red foxes that run at you, red-headed cattails, fallen red logs and snapping turtles (their heads snap out now and then) to jump; red hawks swooping in fast and red leaves hanging on vines to duck under. Each obstacle passed scores 1, and a teal decoy duck floating over some gaps scores 5 if you jump for it. The run speeds up with every obstacle, to a cap. **Fair by time:** every jump has a window at least 0.2 s long that clears its obstacle, at every speed; after one obstacle is dealt with (the latest safe jump has landed, or you're out from under), there's at least 0.25 s before the next needs acting on. A swipe starts with a touch, which jumps, so high obstacles are planned for a touch 0.25 s ahead; the hop dives back down well in time. Gaps with a decoy are long enough to jump for it with time to spare on both sides. The first obstacles are low and easy; cattails join at the third and high ones at the fifth. Rules and constants in `DuckSim` (18 edit mode tests, including bots that act at random, always as early as they can, always as late as they can, or jump for every decoy, each surviving 120 obstacles on every seed). Two play mode tests: one jumps and ducks past 14 obstacles, crashes and retries; one lines up every obstacle for a screenshot. Built; waiting on the owner's phone playtest. *Check, on the phone:*
  - Duck Runner's card is in the hub with the duck-and-log icon; tapping it shows the duck on the grass, with the moon, hills and reeds behind. Nothing moves until the first touch, which is also a jump.
  - **Tap** jumps at once; tapping again in the air doesn't double jump, but a tap just before landing jumps again on landing.
  - **Swipe down** on the ground: a tiny hop, then the duck flattens. It stays down while your finger does, and stands when you lift (after at least half a second). Swiping down in the air drops it straight to the ground, ducked.
  - A tap while ducked jumps straight away.
  - Foxes run in faster than the ground and hawks faster still; logs need a well-timed jump, cattails a full one.
  - Hawks and hanging leaves pass just over a ducking duck, and hit a standing one.
  - Jumping for a teal decoy scores 5 with a teal burst. There's always time to land and jump the next thing.
  - Hitting anything: red burst, big shake, the duck tumbles over, then the game-over card.
  - It speeds up as you go (top speed by about 45 obstacles) and stays winnable.

## Games list
Every game, built or planned, with its number and id. **This list is where numbers come from.** Before starting a game, take the first planned row and mark it *In progress* here, so two branches never take the same number (Brick Burst and Rocket Lanes both reached for 003 once). Numbers are never reused, and an id never changes once the game is on the phone, because best scores are saved under it. A planned game's title and id are working names and can still change before it's built. Every game gets its own original name and look (see *Not to copy*).

| # | Id | Title | Controls | Genre | Status | Task |
|---|---|---|---|---|---|---|
| 001 | `paddle-rally` | Paddle Rally | Drag | Paddle and ball | Built, on main; waiting on phone check | 6 |
| 002 | `drop-stack` | Drop Stack | Drag | Falling blocks | Built, on main; waiting on phone check | 7 |
| 003 | `brick-burst` | Brick Burst | Drag | Brick breaker | Built, on main; waiting on phone check | 8 |
| 004 | `rocket-lanes` | Rocket Lanes | Swipe | Lane runner | Built, on main; waiting on phone check | 9 |
| 005 | `paddle-duel` | Paddle Duel | Drag | Two-paddle table tennis against the computer | Built, on main; waiting on phone check | 10 |
| 006 | `slide-nine` | Slide Nine | Tap | Sliding puzzle: 3 x 3 tiles with one empty slot; tap a tile next to the gap to slide it. A shuffle button, and a move counter or timer. Best scores keep the highest number, so the score has to go up for playing well (for example points that drop with every move, or puzzles solved against the clock) | Built, on main; waiting on phone check | 11 |
| 007 | `noodle` | Noodle | Swipe | Snake: swipe to steer; grow by eating, without hitting yourself | Built, on main; waiting on phone check | 12 |
| 008 | `wing-it` | Wing It | Tap | Flappy-style flyer: one tap to flap through gaps | Built, on main; waiting on phone check | 13 |
| 009 | `pop-up` | Pop Up | Tap | Whack-a-mole: reflex taps on things as they pop up, but not the red ones | Built, on main; waiting on phone check | 14 |
| 010 | `bounce-up` | Bounce Up | Hold | Endless jumper: bounce up from platform to platform; hold the left or right half of the screen to steer (the halves are the "two buttons": games don't draw their own buttons) | Built, on main; waiting on phone check | 15 |
| 011 | `star-swarm` | Star Swarm | Drag | Galaga-style shooter: a ship along the bottom, waves of enemies above; drag to move, it fires on its own | Built, on main; waiting on phone check | 16 |
| 012 | `duck-runner` | Duck Runner | Tap, swipe down | Side-scrolling runner with a duck: jump over foxes, cattails and fallen logs; duck under low hawks and hanging branches; decoy ducks and snapping turtles for variety | Built, on main; waiting on phone check | 17 |
| 013 | `stack-tower` | Stack Tower | Tap | Stacker: tap to drop each sliding block on the tower; overhangs get cut off | **Next** | |
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
