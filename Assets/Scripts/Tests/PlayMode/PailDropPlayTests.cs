using System.Collections;
using AceBarcade.Games.PailDrop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.PailDrop.PailSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Pail Drop through the real arcade with simulated taps.</summary>
    public class PailDropPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("pail-drop");
            PailDrop.SeedForTests = 24;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            PailDrop.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("pail-drop");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static PailDrop Game => (PailDrop)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(0.5f, -2f);

        /// <summary>A tap: one frame down, one frame up. The game reads it before the next frame's step.</summary>
        static IEnumerator Tap()
        {
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(false, Thumb);
            yield return null;
        }

        /// <summary>Waits until a ball (not a thornball) can be dropped.</summary>
        static IEnumerator WaitForBall()
        {
            for (int i = 0; i < 60 * 8; i++)
            {
                if (Sim.CanTap && Sim.Ball != BallKind.Thorn) yield break;
                yield return null;
            }
            Assert.Fail("no ball to drop");
        }

        /// <summary>Drops the next ball so it lands within <paramref name="within"/> of the pail's middle.</summary>
        static IEnumerator DropInto(float within = 0.1f)
        {
            yield return WaitForBall();
            for (int i = 0; i < 60 * 6 && Mathf.Abs(Sim.OffsetIfDroppedIn(0f)) > within; i++) yield return null;
            yield return Tap();
            for (int i = 0; i < 120 && Sim.Hold == HoldState.Falling; i++) yield return null;
        }

        static IEnumerator CatchMiddle()
        {
            int hits = Sim.Hits;
            yield return DropInto();
            Assert.AreEqual(hits + 1, Sim.Hits, "the ball landed in the pail");
        }

        [UnityTest]
        public IEnumerator CatchesBalls_MissesOne_CollectsGems_ThenTapsAThornball_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("pail-drop"));
            yield return Frames(2);
            Assert.IsInstanceOf<PailDrop>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            float bucket = Sim.BucketX;
            yield return Frames(30);
            Assert.AreEqual(bucket, Sim.BucketX, "the pail waits for the first tap");
            yield return PaddleRallyPlayTests.Capture("80-paildrop-ready");

            // The tap that starts the run sets the pail going; it doesn't drop the ball.
            yield return Tap();
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            yield return Frames(10);
            Assert.AreEqual(HoldState.Holding, Sim.Hold);
            Assert.AreNotEqual(bucket, Sim.BucketX);

            // A ball mid-fall, for a look.
            yield return WaitForBall();
            for (int i = 0; i < 60 * 6 && Mathf.Abs(Sim.OffsetIfDroppedIn(0f)) > 0.1f; i++) yield return null;
            yield return Tap();
            yield return Frames(Mathf.RoundToInt(Rules.FallTime * 60f * 0.5f));
            Assert.AreEqual(HoldState.Falling, Sim.Hold);
            yield return PaddleRallyPlayTests.Capture("81-paildrop-falling");
            for (int i = 0; i < 120 && Sim.Hold == HoldState.Falling; i++) yield return null;
            Assert.AreEqual(1, Sim.Hits);

            // Four more, the fifth catch then the gem.
            for (int i = 0; i < 4; i++) yield return CatchMiddle();
            Assert.AreEqual(5 * (Rules.CatchPoints + Rules.PerfectPoints), Game.Score);
            yield return WaitForBall();
            Assert.AreEqual(BallKind.Gem, Sim.Ball, "the 6th ball is a gem");
            yield return PaddleRallyPlayTests.Capture("82-paildrop-gem");
            yield return CatchMiddle();
            Assert.AreEqual(1, Sim.Gems);

            // A drop well away from the pail costs a life.
            yield return WaitForBall();
            for (int i = 0; i < 60 * 6 && Mathf.Abs(Sim.OffsetIfDroppedIn(0f)) < Sim.CatchHalf + 1f; i++) yield return null;
            yield return Tap();
            for (int i = 0; i < 120 && Sim.Hold == HoldState.Falling; i++) yield return null;
            Assert.AreEqual(Rules.StartLives - 1, Sim.Lives);
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            yield return Frames(8);
            yield return PaddleRallyPlayTests.Capture("83-paildrop-miss");

            // On past 10 catches, until a thornball shows up.
            for (int i = 0; i < 80; i++)
            {
                for (int f = 0; f < 60 * 3 && !Sim.CanTap; f++) yield return null;
                if (Sim.Ball == BallKind.Thorn && Sim.Hits >= Rules.ThornFrom) break;
                yield return CatchMiddle();
            }
            Assert.AreEqual(BallKind.Thorn, Sim.Ball);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");
            yield return PaddleRallyPlayTests.Capture("84-paildrop-thorn");

            // Tapping it ends the run, lives or not.
            yield return Tap();
            Assert.IsTrue(Sim.Over);
            Assert.AreEqual(2, Sim.Lives);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("pail-drop"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("85-paildrop-over");

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
