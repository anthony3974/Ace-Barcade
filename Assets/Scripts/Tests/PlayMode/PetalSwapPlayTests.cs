using System.Collections;
using AceBarcade.Games.PetalSwap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Petal Swap through the real arcade with simulated swipes and taps.</summary>
    public class PetalSwapPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("petal-swap");
            PetalSwap.SeedForTests = 16;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            PetalSwap.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("petal-swap");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static PetalSwap Game => (PetalSwap)Arcade.Current;
        static PetalSim Sim => Game.Sim;

        static IEnumerator Tap(Vector2 at)
        {
            Arcade.Input.Simulate(true, at);
            yield return null;
            Arcade.Input.Simulate(false, at);
            yield return null;
        }

        /// <summary>Thumb down on a flower, slide it most of a cell toward its neighbour, let go.</summary>
        static IEnumerator Swipe(Vector2Int cell, Vector2Int dir, string capture = null)
        {
            var from = PetalSwap.CellCenter(cell);
            var to = from + (Vector2)dir * PetalSwap.Cell * 0.7f;
            Arcade.Input.Simulate(true, from);
            yield return null;
            for (int i = 1; i <= 6; i++)
            {
                Arcade.Input.Simulate(true, Vector2.Lerp(from, to, i / 6f));
                yield return null;
            }
            Arcade.Input.Simulate(false, to);
            yield return null;
            if (capture != null)
            {
                // Catch the flowers mid-pop.
                for (int i = 0; i < 30 && Sim.State != PetalSim.Phase.Popping; i++) yield return null;
                yield return Frames(4);
                yield return PaddleRallyPlayTests.Capture(capture);
            }
        }

        static IEnumerator UntilStill()
        {
            for (int i = 0; i < 600 && Sim.State != PetalSim.Phase.Still; i++) yield return null;
            Assert.AreEqual(PetalSim.Phase.Still, Sim.State, "the garden settled");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SwipesAndTapsSwapFlowers_MatchesScore_TimeUpEnds_AndRetry()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("petal-swap"));
            yield return Frames(2);
            Assert.IsInstanceOf<PetalSwap>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return PaddleRallyPlayTests.Capture("72-petalswap-ready");

            // The tap that starts the game doesn't select or swap anything.
            Assert.IsTrue(Sim.FindMove(out var cell, out var dir));
            var board = Sim.Board();
            yield return Tap(PetalSwap.CellCenter(cell));
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.IsNull(Game.Selected, "starting doesn't pick a flower");
            CollectionAssert.AreEqual(board, Sim.Board());
            Assert.AreEqual(0, Sim.Matches);

            // A swipe swaps a flower with its neighbour and makes a match.
            yield return Swipe(cell, dir);
            yield return UntilStill();
            Assert.AreEqual(1, Sim.Matches);
            Assert.Greater(Game.Score, 2);
            Assert.AreEqual(Sim.Score, Game.Score);

            // Tap a flower, then its neighbour.
            Assert.IsTrue(Sim.FindMove(out cell, out dir));
            yield return Tap(PetalSwap.CellCenter(cell));
            Assert.AreEqual(cell, Game.Selected);
            yield return Frames(10);
            yield return PaddleRallyPlayTests.Capture("73-petalswap-selected");
            yield return Tap(PetalSwap.CellCenter(cell + dir));
            Assert.IsNull(Game.Selected);
            yield return UntilStill();
            Assert.AreEqual(2, Sim.Matches);

            // Tapping a flower twice lets it go; a tap off the garden does too.
            yield return Tap(PetalSwap.CellCenter(3, 3));
            Assert.AreEqual(new Vector2Int(3, 3), Game.Selected);
            yield return Tap(PetalSwap.CellCenter(3, 3));
            Assert.IsNull(Game.Selected);
            yield return Tap(PetalSwap.CellCenter(3, 3));
            yield return Tap(new Vector2(0f, -7.2f));
            Assert.IsNull(Game.Selected);

            // A few more, swiping from the other flower of each pair, one caught mid-pop.
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(Sim.FindMove(out cell, out dir));
                yield return Swipe(cell + dir, -dir, i == 2 ? "74-petalswap-pop" : null);
                yield return UntilStill();
            }
            Assert.AreEqual(6, Sim.Matches);

            // A swipe off the edge of the garden does nothing.
            int score = Game.Score;
            yield return Swipe(new Vector2Int(0, 0), Vector2Int.left);
            yield return Frames(20);
            Assert.AreEqual(PetalSim.Phase.Still, Sim.State);
            Assert.AreEqual(score, Game.Score);

            // The hint wiggles after a while.
            yield return Frames(Mathf.CeilToInt(5.5f * 60));

            // Let the clock run out.
            Sim.SetTime(0.3f);
            yield return Frames(40);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("petal-swap"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("75-petalswap-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(PetalSim.StartSeconds, Sim.TimeLeft);
        }
    }
}
