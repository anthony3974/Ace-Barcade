using System.Collections;
using AceBarcade.Games.HoopFling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.HoopFling.HoopSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Hoop Fling through the real arcade, pulling back and letting go like a thumb would.</summary>
    public class HoopFlingPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("hoop-fling");
            HoopFling.SeedForTests = 12;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            HoopFling.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("hoop-fling");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static HoopFling Game => (HoopFling)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(1f, -3f);

        /// <summary>Thumb down, pull back over a few frames, let go; the pull is worked out again on the last frame.</summary>
        static IEnumerator PullAndLetGo(System.Func<Vector2> pullAtRelease, string capture = null)
        {
            var preview = pullAtRelease();
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            for (int i = 1; i <= 8; i++)
            {
                Arcade.Input.Simulate(true, Thumb - preview * (i / 8f));
                yield return null;
            }
            if (capture != null) yield return PaddleRallyPlayTests.Capture(capture);
            Arcade.Input.Simulate(false, Thumb - pullAtRelease());
            yield return null;
        }

        /// <summary>A swish if there is one right now (a moving hoop is solved at the moment of release).</summary>
        static Vector2 Swish() => Sim.SolveNow() ?? Sim.SolveNow(false) ?? new Vector2(1f, 2f);

        static IEnumerator Fly()
        {
            for (int i = 0; i < 60 * 8 && Sim.State == HoopState.Flying; i++) yield return null;
        }

        static IEnumerator NextBall()
        {
            for (int i = 0; i < 60 * 3 && Sim.State != HoopState.Aiming && !Sim.Over; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator PullsBackToFling_SinksBaskets_ThenMissesThree_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("hoop-fling"));
            yield return Frames(2);
            Assert.IsInstanceOf<HoopFling>(Arcade.Current);
            Assert.AreEqual(Controls.Pull, Arcade.Current.Info.Meta.Controls);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(20);
            yield return PaddleRallyPlayTests.Capture("67-hoopfling-ready");

            // A tap starts the run but doesn't fling.
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Arcade.Input.Simulate(false, Thumb);
            yield return Frames(5);
            Assert.AreEqual(0, Sim.Throws);

            // The first: a swish.
            yield return PullAndLetGo(Swish, "68-hoopfling-aim");
            Assert.AreEqual(1, Sim.Throws);
            for (int i = 0; i < 26; i++) yield return null;
            yield return PaddleRallyPlayTests.Capture("69-hoopfling-flying");
            yield return Fly();
            Assert.AreEqual(1, Sim.Baskets);
            Assert.AreEqual(3, Game.Score);
            yield return Frames(4);
            yield return PaddleRallyPlayTests.Capture("70-hoopfling-swish");
            yield return NextBall();

            // On to where the hoop bobs.
            while (Sim.Baskets < 7)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
                int before = Sim.Baskets;
                yield return PullAndLetGo(Swish);
                yield return Fly();
                Assert.AreEqual(before + 1, Sim.Baskets, $"basket {before + 1} goes in");
                yield return NextBall();
            }
            Assert.Greater(Sim.BobAmp, 0f, "by now the hoop bobs");
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Three feeble lobs the wrong way end it.
            for (int i = 0; i < Rules.StartLives && Game.State == ArcadeGame.Phase.Playing; i++)
            {
                yield return PullAndLetGo(() => new Vector2(-0.5f, 0.6f));
                yield return Fly();
                yield return NextBall();
            }
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.AreEqual(0, Sim.Lives);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("hoop-fling"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("71-hoopfling-over");

            var was = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(was, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
        }
    }
}
