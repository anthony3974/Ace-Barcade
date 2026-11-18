using System.Collections;
using AceBarcade.Games.PopUp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.PopUp.PopSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Pop Up through the real arcade with simulated taps.</summary>
    public class PopUpPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("pop-up");
            PopUp.SeedForTests = 9;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            PopUp.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("pop-up");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static PopUp Game => (PopUp)Arcade.Current;
        static Rules Sim => Game.Sim;

        /// <summary>A tap on a critter's face: one frame down, one frame up.</summary>
        static IEnumerator Tap(int hole)
        {
            var at = Rules.HoleCenter(hole) + new Vector2(0.2f, 0.6f);
            Arcade.Input.Simulate(true, at);
            yield return null;
            Arcade.Input.Simulate(false, at);
            yield return null;
        }

        static int Showing(Critter kind)
        {
            foreach (var h in Sim.Field)
                if (h.Kind == kind && h.State == HoleState.Up && !h.Bonked) return h.Index;
            return -1;
        }

        [UnityTest]
        public IEnumerator TapsSproutsAndTealOnes_LetsOneGetAway_ThenTapsARedOne_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("pop-up"));
            yield return Frames(2);
            Assert.IsInstanceOf<PopUp>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            Assert.AreEqual(0, Sim.UpCount(), "nothing pops up before the first tap");
            yield return PaddleRallyPlayTests.Capture("23-popup-ready");

            // Start with a tap between the stumps.
            Arcade.Input.Simulate(true, new Vector2(Rules.ColSpacing / 2f, -6.5f));
            yield return null;
            Arcade.Input.Simulate(false, new Vector2(Rules.ColSpacing / 2f, -6.5f));
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);

            // Tap every sprout and teal one until 12 points, leaving the red ones alone.
            bool captured = false;
            for (int frame = 0; frame < 60 * 60 && Game.Score < 12; frame++)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
                if (!captured && Game.Score >= 6 && Sim.UpCount() >= 2)
                {
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("24-popup-playing");
                }
                int hole = Showing(Critter.Gold);
                if (hole < 0) hole = Showing(Critter.Sprout);
                if (hole >= 0) yield return Tap(hole);
                else yield return null;
            }
            Assert.GreaterOrEqual(Game.Score, 12);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");
            Assert.AreEqual(Rules.StartLives, Sim.Lives);

            // Let a sprout get away: one life gone.
            for (int frame = 0; frame < 60 * 10 && Sim.Lives == Rules.StartLives; frame++) yield return null;
            Assert.AreEqual(Rules.StartLives - 1, Sim.Lives);
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);

            // Now tap a red one, keeping the sprouts down while waiting for one.
            int red = -1;
            for (int frame = 0; frame < 60 * 30 && red < 0; frame++)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
                red = Showing(Critter.Spiky);
                if (red >= 0) break;
                int sprout = Showing(Critter.Sprout);
                if (sprout >= 0) yield return Tap(sprout);
                else yield return null;
            }
            Assert.GreaterOrEqual(red, 0, "a red one came up");
            int score = Game.Score;
            yield return Tap(red);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Sim.Over);
            Assert.AreEqual(score, Game.Score, "a red one scores nothing");
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("pop-up"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("25-popup-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(Rules.StartLives, Sim.Lives);
        }
    }
}
