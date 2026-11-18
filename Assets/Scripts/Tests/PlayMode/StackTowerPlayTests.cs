using System.Collections;
using AceBarcade.Games.StackTower;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.StackTower.StackSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Stack Tower through the real arcade with simulated taps.</summary>
    public class StackTowerPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("stack-tower");
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("stack-tower");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static StackTower Game => (StackTower)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(1f, -4f);

        /// <summary>A tap: one frame down, one frame up. The drop lands where the block is now.</summary>
        static IEnumerator Tap()
        {
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(false, Thumb);
            yield return null;
        }

        /// <summary>Waits for the block to slide to within a hair of <paramref name="x"/>, then taps.</summary>
        static IEnumerator DropAt(float x)
        {
            int level = Sim.Level;
            for (int i = 0; i < 60 * 5; i++)
            {
                if (Sim.Wait <= 0f && Mathf.Abs(Sim.X - x) <= 0.06f) break;
                yield return null;
            }
            yield return Tap();
            Assert.AreNotEqual(level, Sim.Level, "the block landed");
        }

        [UnityTest]
        public IEnumerator StacksPerfectFloors_CutsOne_PicksUpGems_ThenMisses_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("stack-tower"));
            yield return Frames(2);
            Assert.IsInstanceOf<StackTower>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            Assert.AreEqual(-Rules.SlideRange, Sim.X, "the block waits until the first tap");
            yield return PaddleRallyPlayTests.Capture("37-stacktower-ready");

            // The tap that starts the run sets the block going; it doesn't drop it.
            yield return Tap();
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            yield return Frames(30);
            Assert.AreEqual(0, Sim.Stacked);
            Assert.Greater(Sim.X, -Rules.SlideRange);

            // 14 floors, all perfect but the 9th, which hangs half a unit off the tower.
            for (int floor = 1; floor <= 14; floor++)
            {
                float top = Sim.Top.X;
                if (floor == 9)
                {
                    yield return DropAt(top + 0.5f);
                    Assert.AreEqual(Rules.StartWidth - 0.5f, Sim.Top.Width, 0.08f, "the overhang was cut off");
                    yield return Frames(8);
                    yield return PaddleRallyPlayTests.Capture("38-stacktower-playing");
                }
                else
                {
                    yield return DropAt(top);
                    Assert.IsTrue(Sim.Top.Perfect, $"floor {floor}");
                    if (floor == 12)
                    {
                        Assert.AreEqual(3, Sim.Streak);
                        yield return Frames(4);
                        yield return PaddleRallyPlayTests.Capture("39-stacktower-perfect");
                    }
                }
            }
            Assert.AreEqual(14, Sim.Stacked);
            Assert.AreEqual(2, Sim.Gems, "the gems on the 6th and 12th blocks");
            Assert.AreEqual(Rules.StartWidth, Sim.Top.Width, 1e-4f, "the run of perfects after the cut widened it back");
            int expected = 13 * (Rules.FloorPoints + Rules.PerfectPoints) + Rules.FloorPoints + 2 * Rules.GemPoints;
            Assert.AreEqual(expected, Game.Score);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // A late tap near the edge leaves a sliver; then one that misses it altogether.
            yield return DropAt(Sim.Top.X + 2.7f);
            Assert.Less(Sim.Top.Width, 1f);
            float missAt = Sim.Top.X - Sim.Top.Width - 0.3f;
            for (int i = 0; i < 60 * 5 && !(Sim.Wait <= 0f && Sim.X < missAt); i++) yield return null;
            yield return Tap();
            Assert.IsTrue(Sim.Over);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("stack-tower"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("40-stacktower-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(0, Sim.Stacked);
        }
    }
}
