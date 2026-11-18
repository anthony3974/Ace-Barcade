using System.Collections;
using AceBarcade.Games.WingIt;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.WingIt.WingSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Wing It through the real arcade with simulated taps.</summary>
    public class WingItPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("wing-it");
            WingIt.SeedForTests = 8;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            WingIt.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("wing-it");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static WingIt Game => (WingIt)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(1f, -3f);

        /// <summary>A tap: one frame down, one frame up.</summary>
        static IEnumerator Tap()
        {
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(false, Thumb);
            yield return null;
        }

        /// <summary>A little below the middle of the gap ahead (a flap rises about 1.2).</summary>
        static float Aim()
        {
            foreach (var c in Sim.Columns)
                if (c.X + Rules.ColumnWidth / 2f + Rules.Radius > Rules.BeeX) return c.GapY - 0.5f;
            return Rules.StartY;
        }

        [UnityTest]
        public IEnumerator FlapsThroughGaps_PicksUpAGem_ThenFallsOntoTheSpikes_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("wing-it"));
            yield return Frames(2);
            Assert.IsInstanceOf<WingIt>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            Assert.AreEqual(Rules.StartY, Sim.Y, "the bee hovers until the first tap");
            yield return PaddleRallyPlayTests.Capture("20-wingit-ready");

            // The tap that starts the run is also the first flap.
            yield return Tap();
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.Greater(Sim.Velocity, 0f);
            Assert.Greater(Sim.Y, Rules.StartY);

            // Fly through 9 columns: the gems are in the 4th and 8th gaps.
            bool captured = false;
            for (int frame = 0; frame < 60 * 60 && Sim.Passed < 9; frame++)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State, $"crashed after {Sim.Passed} columns");
                if (!captured && Sim.Passed == 4 && Sim.Columns[0].X < -2.6f)
                {
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("21-wingit-playing");
                }
                if (Sim.Y < Aim() && Sim.Velocity < 0f) yield return Tap();
                else yield return null;
            }
            Assert.AreEqual(9, Sim.Passed);
            Assert.AreEqual(2, Sim.Gems);
            Assert.AreEqual(9 * Rules.ColumnPoints + 2 * Rules.GemPoints, Game.Score);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Stop flapping: down onto the thorns or the spikes.
            for (int i = 0; i < 60 * 5 && Game.State == ArcadeGame.Phase.Playing; i++) yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Sim.Crashed);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("wing-it"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("22-wingit-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(0, Sim.Columns.Count);
        }
    }
}
