using System.Collections;
using AceBarcade.Games.DuckRunner;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.DuckRunner.DuckSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Duck Runner through the real arcade with simulated taps and swipes.</summary>
    public class DuckRunnerPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("duck-runner");
            DuckRunner.SeedForTests = 12;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            DuckRunner.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("duck-runner");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static DuckRunner Game => (DuckRunner)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(1f, -5f);

        static IEnumerator Tap()
        {
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(false, Thumb);
            yield return null;
        }

        static DuckObstacle Next()
        {
            foreach (var o in Sim.Obstacles) if (!o.Passed) return o;
            return null;
        }

        static float ToContact(DuckObstacle o) => (o.X - Rules.ContactX) / (Sim.Speed + o.Spec.OwnSpeed);

        [UnityTest]
        public IEnumerator JumpsAndDucksPastObstacles_ThenRunsIntoOne_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("duck-runner"));
            yield return Frames(2);
            Assert.IsInstanceOf<DuckRunner>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            Assert.AreEqual(0f, Sim.Distance, "nothing moves until the first tap");
            yield return PaddleRallyPlayTests.Capture("32-duckrunner-ready");

            // The tap that starts the run is also a jump.
            yield return Tap();
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.IsFalse(Sim.OnGround);

            // Jump the low ones in the middle of their windows; swipe down and hold under the high ones.
            const int Target = 14;
            int ducked = 0;
            bool captured = false;
            for (int frame = 0; frame < 60 * 60 && Sim.Passed < Target; frame++)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State, $"crashed after {Sim.Passed} obstacles");
                var next = Next();
                float t = ToContact(next);
                if (next.Spec.High)
                {
                    if (t <= Rules.DuckLead + 0.15f && Sim.OnGround)
                    {
                        // Touch, then drag down: the swipe is seen a couple of frames in.
                        Arcade.Input.Simulate(true, Thumb);
                        yield return null;
                        for (int i = 1; i <= 4; i++)
                        {
                            Arcade.Input.Simulate(true, Thumb + Vector2.down * 0.4f * i);
                            yield return null;
                        }
                        Assert.IsTrue(Sim.Ducking, "the swipe ducks");
                        ducked++;
                        while (!next.Passed && Game.State == ArcadeGame.Phase.Playing)
                        {
                            if (!captured && next.X < Rules.PlayerX)
                            {
                                captured = true;
                                yield return PaddleRallyPlayTests.Capture("33-duckrunner-playing");
                            }
                            yield return null;
                        }
                        Arcade.Input.Simulate(false, Thumb + Vector2.down * 1.6f);
                        yield return null;
                        continue;
                    }
                }
                else
                {
                    var (early, late) = Rules.JumpWindow(next.Kind, Sim.Speed);
                    if (Sim.OnGround && t <= -(early + late) / 2f)
                    {
                        yield return Tap();
                        continue;
                    }
                }
                yield return null;
            }
            Assert.GreaterOrEqual(Sim.Passed, Target);
            Assert.Greater(ducked, 0, "some obstacles were ducked under");
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");
            Assert.AreEqual(Sim.Passed * Rules.PassPoints + Sim.Decoys * Rules.DecoyPoints, Game.Score);

            // Stop playing: the next obstacle ends the run.
            Arcade.Input.Simulate(false, Thumb);
            for (int i = 0; i < 60 * 5 && Game.State == ArcadeGame.Phase.Playing; i++) yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Sim.Crashed);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("duck-runner"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("34-duckrunner-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(0f, Sim.Distance);
        }

        /// <summary>Every obstacle and a decoy on screen at once, to check how they sit on the ground.</summary>
        [UnityTest]
        public IEnumerator EveryObstacle_DrawsOverItsBox()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("duck-runner"));
            yield return Frames(2);

            Sim.Clear();
            Sim.Add(ObstacleKind.Fox, -1.1f);
            Sim.Add(ObstacleKind.Cattails, 0.7f);
            Sim.Add(ObstacleKind.Log, 2.2f);
            Sim.AddDecoy(0.9f);
            yield return Frames(10);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return PaddleRallyPlayTests.Capture("35-duckrunner-ground");

            Sim.Clear();
            Sim.Add(ObstacleKind.Turtle, -1.2f);
            Sim.Add(ObstacleKind.Hawk, 0.6f);
            Sim.Add(ObstacleKind.Branch, 2.6f);
            yield return Frames(10);
            yield return PaddleRallyPlayTests.Capture("36-duckrunner-high");
        }
    }
}
