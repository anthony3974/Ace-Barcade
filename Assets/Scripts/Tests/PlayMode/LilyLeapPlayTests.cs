using System.Collections;
using AceBarcade.Games.LilyLeap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.LilyLeap.LeapSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Lily Leap through the real arcade, pulling back and letting go like a thumb would.</summary>
    public class LilyLeapPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("lily-leap");
            LilyLeap.SeedForTests = 10;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            LilyLeap.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("lily-leap");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static LilyLeap Game => (LilyLeap)Arcade.Current;
        static Rules Sim => Game.Sim;

        // Where the thumb goes down: low on the screen, where a thumb rests.
        static readonly Vector2 Thumb = new Vector2(0.5f, -5f);

        /// <summary>
        /// Put the thumb down, pull back over a few frames, then let go. The pull on the last frame
        /// comes from <paramref name="pullAtRelease"/>, worked out then (drifting pads move meanwhile).
        /// </summary>
        static IEnumerator PullAndLetGo(System.Func<Vector2> pullAtRelease, string capture = null)
        {
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            for (int i = 1; i <= 8; i++)
            {
                Arcade.Input.Simulate(true, Thumb - pullAtRelease() * (i / 8f));
                yield return null;
            }
            if (capture != null) yield return PaddleRallyPlayTests.Capture(capture);
            Arcade.Input.Simulate(false, Thumb - pullAtRelease());
            yield return null;
        }

        static IEnumerator Settle()
        {
            for (int i = 0; i < 120 && Sim.Flying; i++) yield return null;
            yield return Frames(6);
        }

        [UnityTest]
        public IEnumerator PullsBackToLeapUpThePads_ThenMissesIntoTheWater_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("lily-leap"));
            yield return Frames(2);
            Assert.IsInstanceOf<LilyLeap>(Arcade.Current);
            Assert.AreEqual(Controls.Pull, Arcade.Current.Info.Meta.Controls);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(20);
            yield return PaddleRallyPlayTests.Capture("58-lilyleap-ready");

            // A tap starts the run but doesn't leap.
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Arcade.Input.Simulate(false, Thumb);
            yield return Frames(5);
            Assert.AreEqual(0, Sim.Jumps, "a tap is too short a pull");

            // While pulling, the game shows the aim; letting go leaps that way.
            var first = Sim.NextPathPad();
            yield return PullAndLetGo(() => Sim.PullToHit(first), "59-lilyleap-aim");
            Assert.AreEqual(1, Sim.Jumps);
            yield return Settle();
            Assert.AreSame(first, Sim.OnPad, "it lands on the pad it was aimed at");
            Assert.AreEqual(1, Game.Score);

            // Leap on up, past drifting and sinking pads.
            bool captured = false;
            for (int hop = 0; hop < 80 && Sim.BestRow < 20; hop++)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State, $"fell in at row {Sim.BestRow}");
                var next = Sim.NextPathPad();
                bool shoot = !captured && Sim.BestRow >= 14;
                yield return PullAndLetGo(() => Sim.PullToHit(next));
                if (shoot)
                {
                    yield return Frames(10);
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("60-lilyleap-leaping");
                }
                yield return Settle();
            }
            Assert.GreaterOrEqual(Sim.BestRow, 20);
            Assert.Greater(Sim.CamY, 20f, "the view climbs with the frog");
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Leap straight out sideways, off the edge of the pond.
            var off = new Vector2(Sim.Frog.x > 0f ? 1f : -1f, 0f) * Rules.MaxPull;
            yield return PullAndLetGo(() => off);
            for (int i = 0; i < 120 && Game.State == ArcadeGame.Phase.Playing; i++) yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Sim.Over);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("lily-leap"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("61-lilyleap-over");

            var was = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(was, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(0f, Sim.CamY);
        }
    }
}
