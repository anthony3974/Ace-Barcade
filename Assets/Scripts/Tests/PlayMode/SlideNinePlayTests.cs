using System.Collections;
using AceBarcade.Games.SlideNine;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Slide Nine through the real arcade with simulated taps.</summary>
    public class SlideNinePlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("slide-nine");
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("slide-nine");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static SlideNine Game => (SlideNine)Arcade.Current;
        static SlideSim Sim => Game.Sim;

        static IEnumerator Tap(Vector2 at)
        {
            Arcade.Input.Simulate(true, at);
            yield return null;
            Arcade.Input.Simulate(false, at);
            yield return null;
        }

        /// <summary>The tile next to the gap that brings the board one step nearer solved.</summary>
        static int BestTap()
        {
            int g = Sim.Gap, gc = g % SlideSim.Size, best = -1, bestD = int.MaxValue;
            var board = new int[SlideSim.Cells];
            foreach (int cell in new[] { g - SlideSim.Size, g + SlideSim.Size, gc > 0 ? g - 1 : -1, gc < SlideSim.Size - 1 ? g + 1 : -1 })
            {
                if (cell < 0 || cell >= SlideSim.Cells) continue;
                for (int i = 0; i < SlideSim.Cells; i++) board[i] = Sim.Board[i];
                board[g] = board[cell];
                board[cell] = 0;
                int d = SlideSim.Distance(board);
                if (d < bestD) { bestD = d; best = cell; }
            }
            return best;
        }

        [UnityTest]
        public IEnumerator TapsSlideTiles_SolvesScoreAndAddTime_TimeUpEnds_AndRetry()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("slide-nine"));
            yield return Frames(2);
            Assert.IsInstanceOf<SlideNine>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(SlideSim.StartPar, Sim.Par);
            yield return PaddleRallyPlayTests.Capture("14-slidenine-ready");

            // The tap that starts the game doesn't move a tile, even on a tile next to the gap.
            var board = new int[SlideSim.Cells];
            for (int i = 0; i < SlideSim.Cells; i++) board[i] = Sim.Board[i];
            yield return Tap(SlideNine.CellCenter(BestTap()));
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.AreEqual(0, Sim.Moves, "starting doesn't move a tile");
            CollectionAssert.AreEqual(board, Sim.Board);

            // A tap off the board does nothing.
            yield return Tap(new Vector2(0f, -7.5f));
            Assert.AreEqual(0, Sim.Moves);

            // Solve two puzzles the shortest way.
            bool captured = false;
            for (int i = 0; i < 200 && Sim.Solved < 2; i++)
            {
                if (Sim.Celebrating) { yield return null; continue; }
                int moves = Sim.Moves;
                yield return Tap(SlideNine.CellCenter(BestTap()));
                Assert.AreEqual(moves + 1, Sim.Moves, "each tap next to the gap is one move");
                if (!captured && Sim.Moves == 3) { captured = true; yield return PaddleRallyPlayTests.Capture("15-slidenine-playing"); }
            }
            Assert.AreEqual(2, Sim.Solved);
            int perfect = SlideSim.SolvePoints + SlideSim.MoveBonusMax;
            Assert.AreEqual(2 * perfect, Game.Score, "solved in par twice: full points both times");
            Assert.AreEqual(Sim.Score, Game.Score);
            Assert.Greater(Sim.TimeLeft, SlideSim.StartSeconds, "solves add time");

            // Let the clock run out.
            yield return Frames(60);
            Sim.SetTime(0.5f);
            yield return Frames(60);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("slide-nine"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("16-slidenine-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(SlideSim.StartSeconds, Sim.TimeLeft);
        }
    }
}
