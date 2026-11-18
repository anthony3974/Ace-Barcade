using System.Collections;
using AceBarcade.Games.RingStop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.RingStop.RingSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Ring Stop through the real arcade with simulated taps.</summary>
    public class RingStopPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("ring-stop");
            RingStop.SeedForTests = 18;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            RingStop.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("ring-stop");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static RingStop Game => (RingStop)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(0.5f, -6f);

        /// <summary>A tap: one frame down, one frame up. The game reads it before the next frame's step.</summary>
        static IEnumerator Tap()
        {
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(false, Thumb);
            yield return null;
        }

        /// <summary>Waits for the spark to come within <paramref name="before"/> degrees of the target's middle, then taps.</summary>
        static IEnumerator TapAt(float before)
        {
            for (int i = 0; i < 60 * 6; i++)
            {
                if (Sim.Calm <= 0f && Sim.Target.Remaining <= before + Sim.Speed / 120f) break;
                yield return null;
            }
            yield return Tap();
        }

        static IEnumerator HitMiddle()
        {
            int hits = Sim.Hits;
            yield return TapAt(0f);
            Assert.AreEqual(hits + 1, Sim.Hits, "the tap hit the target");
        }

        [UnityTest]
        public IEnumerator HitsTargets_MissesOne_CollectsGems_ThenTapsRed_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("ring-stop"));
            yield return Frames(2);
            Assert.IsInstanceOf<RingStop>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            Assert.AreEqual(Rules.StartAngle, Sim.Angle, "the spark waits for the first tap");
            yield return PaddleRallyPlayTests.Capture("54-ringstop-ready");

            // The tap that starts the run sets the spark going; it isn't a try.
            yield return Tap();
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            yield return Frames(10);
            Assert.AreEqual(Rules.StartLives, Sim.Lives);
            Assert.AreNotEqual(Rules.StartAngle, Sim.Angle);

            // Five perfect hits, each turning the spark round.
            for (int i = 0; i < 5; i++)
            {
                int dir = Sim.Dir;
                yield return HitMiddle();
                Assert.AreEqual(-dir, Sim.Dir);
            }
            Assert.AreEqual(5 * (Rules.HitPoints + Rules.PerfectPoints), Game.Score);
            Assert.IsTrue(Sim.Target.Gem, "the 6th target holds a gem");
            yield return Frames(6);
            yield return PaddleRallyPlayTests.Capture("55-ringstop-gem");
            yield return HitMiddle();
            Assert.AreEqual(1, Sim.Gems);

            // A tap well before the target costs a life and moves the target.
            int id = Sim.TargetId;
            yield return TapAt(Sim.Window + 20f);
            Assert.AreEqual(Rules.StartLives - 1, Sim.Lives);
            Assert.AreNotEqual(id, Sim.TargetId);
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);

            // On past 10 hits, until a red arc shows up.
            for (int i = 0; i < 60 && (Sim.RedArc == null || Sim.Hits < Rules.RedFrom); i++)
                yield return HitMiddle();
            Assert.IsNotNull(Sim.RedArc);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");
            yield return Frames(4);
            yield return PaddleRallyPlayTests.Capture("56-ringstop-red");

            // A tap on the red arc ends the run, lives or not.
            for (int i = 0; i < 60 * 6 && !Sim.OnRed; i++) yield return null;
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(false, Thumb);
            yield return null;
            Assert.IsTrue(Sim.Over);
            Assert.AreEqual(2, Sim.Lives);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("ring-stop"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("57-ringstop-over");

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
