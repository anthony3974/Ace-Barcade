using AceBarcade.Games.SlideNine;
using NUnit.Framework;
using UnityEngine;

namespace AceBarcade.Tests
{
    public class SlideSimTests
    {
        const float Dt = 1f / 60f;

        /// <summary>The single tile next to the gap whose move brings the board one step nearer solved.</summary>
        public static int BestTap(SlideSim sim)
        {
            int best = -1, bestD = int.MaxValue;
            var board = new int[SlideSim.Cells];
            int g = sim.Gap, gr = g / SlideSim.Size, gc = g % SlideSim.Size;
            foreach (int cell in new[] { g - SlideSim.Size, g + SlideSim.Size, gc > 0 ? g - 1 : -1, gc < SlideSim.Size - 1 ? g + 1 : -1 })
            {
                if (cell < 0 || cell >= SlideSim.Cells) continue;
                for (int i = 0; i < SlideSim.Cells; i++) board[i] = sim.Board[i];
                board[g] = board[cell];
                board[cell] = 0;
                int d = SlideSim.Distance(board);
                if (d < bestD) { bestD = d; best = cell; }
            }
            return best;
        }

        static void Solve(SlideSim sim)
        {
            for (int i = 0; i < 40 && !sim.IsSolved; i++) sim.Tap(BestTap(sim));
        }

        static void RunUntilNewPuzzle(SlideSim sim)
        {
            for (int i = 0; i < 120; i++)
                if ((sim.Step(Dt) & SlideEvent.NewPuzzle) != 0) return;
            Assert.Fail("no new puzzle after the pause");
        }

        // --- The table and the puzzles --------------------------------------------------------

        [Test]
        public void Table_HasEveryReachablePosition()
        {
            Assert.AreEqual(181440, SlideSim.Table.Count, "half of 9! positions can be reached");
            Assert.AreEqual(31, SlideSim.Table.MaxDistance, "the hardest 3 x 3 position takes 31 moves");
            Assert.AreEqual(0, SlideSim.Distance(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 0 }));
            Assert.AreEqual(1, SlideSim.Distance(new[] { 1, 2, 3, 4, 5, 6, 7, 0, 8 }));
            Assert.AreEqual(-1, SlideSim.Distance(new[] { 2, 1, 3, 4, 5, 6, 7, 8, 0 }), "two tiles swapped can't be solved");
        }

        [Test]
        public void EveryPuzzle_IsExactlyParMovesFromSolved_AndParGrowsToACap()
        {
            Assert.AreEqual(SlideSim.StartPar, SlideSim.ParFor(0));
            Assert.AreEqual(SlideSim.StartPar + SlideSim.ParStep, SlideSim.ParFor(1));
            Assert.AreEqual(SlideSim.MaxPar, SlideSim.ParFor(100));

            for (int seed = 0; seed < 5; seed++)
            {
                var sim = new SlideSim(seed);
                for (int puzzle = 0; puzzle < 10; puzzle++)
                {
                    Assert.AreEqual(SlideSim.ParFor(puzzle), sim.Par);
                    Assert.AreEqual(sim.Par, SlideSim.Distance(sim.Board), $"seed {seed}, puzzle {puzzle}");
                    Assert.IsFalse(sim.IsSolved);
                    Assert.AreEqual(0, sim.Moves);
                    Solve(sim);
                    Assert.AreEqual(sim.Par, sim.Moves, "the best taps solve it in exactly par");
                    RunUntilNewPuzzle(sim);
                }
            }
        }

        // --- Tapping --------------------------------------------------------------------------

        [Test]
        public void TappingNextToTheGap_SlidesThatTile()
        {
            var sim = new SlideSim();
            sim.SetBoard(1, 2, 3, 4, 0, 5, 6, 7, 8);
            Assert.AreEqual(SlideEvent.Moved, sim.Tap(5));
            Assert.AreEqual(5, sim.Board[4]);
            Assert.AreEqual(0, sim.Board[5]);
            Assert.AreEqual(5, sim.Gap);
            Assert.AreEqual(1, sim.Moves);
        }

        [Test]
        public void TappingFurtherAlongARowOrColumn_PushesTheTilesBetween()
        {
            var sim = new SlideSim();
            sim.SetBoard(0, 1, 2, 3, 4, 5, 6, 7, 8);
            sim.Tap(2);
            CollectionAssert.AreEqual(new[] { 1, 2, 0, 3, 4, 5, 6, 7, 8 }, sim.Board);
            Assert.AreEqual(2, sim.Moves, "each tile slid is a move");
            sim.Tap(8);
            CollectionAssert.AreEqual(new[] { 1, 2, 5, 3, 4, 8, 6, 7, 0 }, sim.Board);
            Assert.AreEqual(4, sim.Moves);
        }

        [Test]
        public void TappingTheGap_OrATileOffItsLines_DoesNothing()
        {
            var sim = new SlideSim();
            sim.SetBoard(1, 2, 3, 4, 0, 5, 6, 7, 8);
            Assert.AreEqual(SlideEvent.None, sim.Tap(4), "the gap");
            Assert.AreEqual(SlideEvent.None, sim.Tap(0), "a corner, not in the gap's row or column");
            Assert.AreEqual(SlideEvent.None, sim.Tap(-1));
            Assert.AreEqual(SlideEvent.None, sim.Tap(9));
            Assert.AreEqual(0, sim.Moves);
        }

        [Test]
        public void HomeTiles_AreTheOnesInTheirOwnCell()
        {
            var sim = new SlideSim();
            sim.SetBoard(1, 2, 3, 4, 5, 6, 7, 0, 8);
            for (int cell = 0; cell < 7; cell++) Assert.IsTrue(sim.Home(cell));
            Assert.IsFalse(sim.Home(7), "the gap");
            Assert.IsFalse(sim.Home(8), "8 is in cell 9, not its own");
        }

        // --- Solving, scoring and the clock ---------------------------------------------------

        [Test]
        public void Solving_ScoresPointsAndAMoveBonus_AndAddsTime()
        {
            var sim = new SlideSim();
            sim.SetBoard(1, 2, 3, 4, 5, 6, 7, 0, 8);
            sim.SetTime(50f);
            var e = sim.Tap(8);
            Assert.IsTrue((e & SlideEvent.Solved) != 0);
            Assert.AreEqual(1, sim.Solved);
            Assert.AreEqual(SlideSim.SolvePoints + SlideSim.MoveBonusMax, sim.Score, "solved in par: the full bonus");
            Assert.AreEqual(50f + SlideSim.SolveBonusSeconds, sim.TimeLeft, 1e-4f);
            Assert.IsTrue(sim.Celebrating);
            Assert.AreEqual(SlideEvent.None, sim.Tap(5), "taps wait for the next puzzle");

            float before = sim.TimeLeft;
            RunUntilNewPuzzle(sim);
            Assert.AreEqual(before, sim.TimeLeft, 1e-4f, "the clock stops while it celebrates");
            Assert.AreEqual(SlideSim.ParFor(1), sim.Par, "the next puzzle is harder");
            Assert.IsFalse(sim.IsSolved);
        }

        [Test]
        public void MoveBonus_ShrinksWithEveryExtraMove_ButNeverGoesNegative()
        {
            Assert.AreEqual(SlideSim.MoveBonusMax, SlideSim.MoveBonus(6, 6));
            Assert.AreEqual(SlideSim.MoveBonusMax - 4, SlideSim.MoveBonus(10, 6));
            Assert.AreEqual(0, SlideSim.MoveBonus(60, 6));

            var sim = new SlideSim();
            sim.SetBoard(1, 2, 3, 4, 5, 6, 7, 0, 8);
            sim.Tap(6); // wrong way: 7 slides right
            sim.Tap(7); // back
            sim.Tap(8); // solved, in 3 moves for a par of 1
            Assert.AreEqual(SlideSim.SolvePoints + SlideSim.MoveBonusMax - 2, sim.Score);
        }

        [Test]
        public void TheClock_AddsTimeUpToACap_AndRunningOutEndsTheRun()
        {
            var sim = new SlideSim();
            sim.SetBoard(1, 2, 3, 4, 5, 6, 7, 0, 8);
            sim.SetTime(95f);
            sim.Tap(8);
            Assert.AreEqual(SlideSim.MaxSeconds, sim.TimeLeft, 1e-4f);

            var sim2 = new SlideSim();
            Assert.AreEqual(SlideSim.StartSeconds, sim2.TimeLeft);
            sim2.SetTime(0.5f);
            var all = SlideEvent.None;
            for (int i = 0; i < 60 && !sim2.Over; i++) all |= sim2.Step(Dt);
            Assert.IsTrue((all & SlideEvent.TimeUp) != 0);
            Assert.IsTrue(sim2.Over);
            Assert.AreEqual(0f, sim2.TimeLeft);
            Assert.AreEqual(SlideEvent.None, sim2.Tap(sim2.Gap > 0 ? sim2.Gap - 1 : 1), "no moves after time is up");
        }
    }
}
