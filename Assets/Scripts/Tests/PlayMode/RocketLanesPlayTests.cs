using System.Collections;
using AceBarcade.Games.RocketLanes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.RocketLanes.RocketLanesSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Rocket Lanes through the real arcade with simulated swipes.</summary>
    public class RocketLanesPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("rocket-lanes");
            RocketLanes.SeedForTests = 11;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            RocketLanes.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("rocket-lanes");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static RocketLanes Game => (RocketLanes)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(0f, -3f);

        IEnumerator Begin()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("rocket-lanes"));
            yield return Frames(2);
            Assert.IsInstanceOf<RocketLanes>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return PaddleRallyPlayTests.Capture("8-rocketlanes-ready");

            // The touch that starts the game doesn't move the rocket.
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(false, Thumb);
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.AreEqual(1, Sim.Lane);
        }

        /// <summary>A quick sideways swipe: touch, move past the threshold, lift.</summary>
        static IEnumerator Swipe(int direction)
        {
            var end = Thumb + new Vector2(direction * 1.2f, 0f);
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(true, end);
            yield return null;
            Arcade.Input.Simulate(false, end);
            yield return null;
        }

        /// <summary>The first row not yet clear of the rocket.</summary>
        static LaneRow NextRow()
        {
            foreach (var row in Sim.Rows)
                if (row.Y > Rules.PlayerY - Rules.HitDepth) return row;
            return null;
        }

        static int Nearest(LaneRow row, bool wantMine)
        {
            int best = -1;
            for (int lane = 0; lane < Rules.Lanes; lane++)
                if (row.Mine(lane) == wantMine && (best < 0 || Mathf.Abs(lane - Sim.Lane) < Mathf.Abs(best - Sim.Lane))) best = lane;
            return best;
        }

        [UnityTest]
        public IEnumerator Swipe_ChangesLaneOncePerSwipe_AsTheFingerMoves_AndQuickSwipesAllCount()
        {
            yield return Begin();

            // The lane changes while the finger is still down, as soon as it has moved far enough.
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(true, Thumb + new Vector2(1.2f, 0f));
            yield return null;
            Assert.AreEqual(2, Sim.Lane, "the swipe counts before the finger lifts");

            // Dragging on, even back the other way, doesn't change lanes again.
            Arcade.Input.Simulate(true, Thumb + new Vector2(3f, 0f));
            yield return null;
            Arcade.Input.Simulate(true, Thumb + new Vector2(-2f, 0f));
            yield return Frames(3);
            Arcade.Input.Simulate(false, Thumb + new Vector2(-2f, 0f));
            yield return null;
            Assert.AreEqual(2, Sim.Lane, "one swipe, one lane");

            // Two quick swipes back to back: two lanes.
            yield return Swipe(-1);
            yield return Swipe(-1);
            Assert.AreEqual(0, Sim.Lane);
            yield return Swipe(-1);
            Assert.AreEqual(0, Sim.Lane, "the left edge stops it");
            yield return Frames(10);
            Assert.AreEqual(Rules.LaneX(0), Sim.PlayerX, 1e-3f);
        }

        [UnityTest]
        public IEnumerator Dodges_PicksUpCrystals_ThenHitsAMine_AndRetries()
        {
            yield return Begin();

            // Dodge, and go for crystals when there's time.
            bool captured = false;
            for (int i = 0; i < 60 * 60 && !(Sim.Crystals >= 2 && Sim.RowsPassed >= 15); i++)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State, $"crashed at row {Sim.RowsPassed}");
                if (!captured && Sim.RowsPassed >= 5)
                {
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("9-rocketlanes-playing");
                }

                var next = NextRow();
                if (next == null || next.Y - Rules.PlayerY < Rules.HitDepth) { yield return null; continue; }

                float timeLeft = (next.Y - Rules.HitDepth - Rules.PlayerY) / Sim.Speed;
                int goal = next.Open(Sim.Lane) ? Sim.Lane : Nearest(next, false);
                if (next.HasCrystal && Mathf.Abs(next.CrystalLane - Sim.Lane) * 0.2f + 0.05f < timeLeft) goal = next.CrystalLane;

                if (goal != Sim.Lane) yield return Swipe(goal > Sim.Lane ? 1 : -1);
                else yield return null;
            }
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.GreaterOrEqual(Sim.RowsPassed, 15);
            Assert.GreaterOrEqual(Sim.Crystals, 2);
            Assert.AreEqual(Sim.RowsPassed * Rules.RowPoints + Sim.Crystals * Rules.CrystalPoints, Game.Score);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Now fly into a mine.
            int scored = Game.Score;
            for (int i = 0; i < 60 * 20 && Game.State == ArcadeGame.Phase.Playing; i++)
            {
                var next = NextRow();
                int goal = next == null ? Sim.Lane : Nearest(next, true);
                if (goal != Sim.Lane) yield return Swipe(goal > Sim.Lane ? 1 : -1);
                else yield return null;
            }
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Sim.Crashed);
            Assert.IsTrue(Game.NewBest);
            Assert.GreaterOrEqual(Game.Score, scored);
            Assert.AreEqual(Game.Score, Scores.Best("rocket-lanes"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("10-rocketlanes-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(1, Sim.Lane);
        }
    }
}
