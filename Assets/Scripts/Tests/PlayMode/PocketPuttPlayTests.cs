using System.Collections;
using AceBarcade.Games.PocketPutt;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.PocketPutt.PuttSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Pocket Putt through the real arcade, pulling back and letting go like a thumb would.</summary>
    public class PocketPuttPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("pocket-putt");
            PocketPutt.SeedForTests = 11;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            PocketPutt.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("pocket-putt");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static PocketPutt Game => (PocketPutt)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(0.5f, -5f);

        /// <summary>Thumb down, pull back over a few frames, let go; the pull is worked out on the last frame.</summary>
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

        static IEnumerator RollOut()
        {
            for (int i = 0; i < 60 * 20 && Sim.State == PuttState.Rolling; i++) yield return null;
        }

        static IEnumerator NextHole()
        {
            for (int i = 0; i < 60 * 3 && Sim.State != PuttState.Aiming && !Sim.Over; i++) yield return null;
        }

        /// <summary>A putt that holes it, solved the moment the thumb lets go (gates and all).</summary>
        static Vector2 Holing() => Sim.SolveNow(3f) ?? Sim.Hole.Proof;

        [UnityTest]
        public IEnumerator PullsBackToPutt_SinksHoles_ThenRunsOutOfLives_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("pocket-putt"));
            yield return Frames(2);
            Assert.IsInstanceOf<PocketPutt>(Arcade.Current);
            Assert.AreEqual(Controls.Pull, Arcade.Current.Info.Meta.Controls);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(20);
            yield return PaddleRallyPlayTests.Capture("62-pocketputt-ready");

            // A tap starts the run but doesn't putt.
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Arcade.Input.Simulate(false, Thumb);
            yield return Frames(5);
            Assert.AreEqual(0, Sim.Putts);

            // Hole 1 in one.
            yield return PullAndLetGo(() => Sim.Hole.Proof, "63-pocketputt-aim");
            Assert.AreEqual(1, Sim.Putts);
            yield return RollOut();
            Assert.AreEqual(PuttState.Sunk, Sim.State);
            yield return Frames(8);
            yield return PaddleRallyPlayTests.Capture("64-pocketputt-sunk");
            Assert.AreEqual(3, Game.Score);
            yield return NextHole();
            Assert.AreEqual(2, Sim.HoleNumber);

            // On through the holes, to where water and gates come in.
            bool captured = false;
            while (Sim.HoleNumber < 9)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
                if (!captured && Sim.Hole.Water.Count > 0 && Sim.Hole.Bumpers.Count > 0)
                {
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("65-pocketputt-hole");
                }
                int hole = Sim.HoleNumber;
                yield return PullAndLetGo(Holing);
                yield return RollOut();
                Assert.AreEqual(PuttState.Sunk, Sim.State, $"hole {hole} drops");
                yield return NextHole();
            }
            Assert.AreEqual(8 * 3, Game.Score);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Three dribbles a hole lose a life each; the third ends the run.
            for (int life = 0; life < Rules.StartLives; life++)
                for (int p = 0; p < Rules.PuttsPerHole && Game.State == ArcadeGame.Phase.Playing; p++)
                {
                    yield return PullAndLetGo(() => (Sim.Hole.Cup - Sim.Ball).normalized * Rules.MinPull * 1.2f);
                    yield return RollOut();
                    if (Sim.State == PuttState.Missed && !Sim.Over) yield return NextHole();
                }
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.AreEqual(0, Sim.Lives);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("pocket-putt"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("66-pocketputt-over");

            var was = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(was, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(1, Sim.HoleNumber);
        }
    }
}
