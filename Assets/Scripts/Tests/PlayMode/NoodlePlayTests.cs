using System.Collections;
using AceBarcade.Games.Noodle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.Noodle.NoodleSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Noodle through the real arcade with simulated swipes.</summary>
    public class NoodlePlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("noodle");
            Noodle.SeedForTests = 21;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            Noodle.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("noodle");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static Noodle Game => (Noodle)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(0f, -4f);

        /// <summary>A quick swipe: touch, move past the threshold, lift.</summary>
        static IEnumerator Swipe(Vector2Int dir)
        {
            var end = Thumb + (Vector2)dir * 1f;
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(true, end);
            yield return null;
            Arcade.Input.Simulate(false, end);
            yield return null;
        }

        /// <summary>Waits until the noodle has taken every queued turn.</summary>
        static IEnumerator Settle()
        {
            for (int i = 0; i < 60 && Sim.QueuedTurns.Count > 0; i++) yield return null;
        }

        /// <summary>The short way round, on one axis: -1, 0 or 1.</summary>
        static int Toward(int from, int to, int size)
        {
            int d = ((to - from) % size + size) % size;
            return d == 0 ? 0 : d <= size / 2 ? 1 : -1;
        }

        [UnityTest]
        public IEnumerator EatsPeasBySwiping_ThenBitesItself_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("noodle"));
            yield return Frames(2);
            Assert.IsInstanceOf<Noodle>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(20);
            yield return PaddleRallyPlayTests.Capture("17-noodle-ready");

            // A tap starts the run without turning.
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(false, Thumb);
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.AreEqual(Rules.Up, Sim.Dir);

            // One stroke, right then up without lifting: two turns.
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(true, Thumb + new Vector2(0.8f, 0f));
            yield return null;
            Arcade.Input.Simulate(true, Thumb + new Vector2(0.8f, 0.8f));
            yield return null;
            Arcade.Input.Simulate(false, Thumb + new Vector2(0.8f, 0.8f));
            yield return Settle();
            Assert.AreEqual(Rules.Up, Sim.Dir);
            Assert.AreEqual(Rules.StartHead.x + 1, Sim.Head.x, "it went right one cell, then up");

            // Steer to three peas: line up across, then along.
            bool captured = false;
            int captureAt = -1;
            for (int frame = 0; frame < 60 * 40 && Sim.Peas < 3; frame++)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State, $"crashed after {Sim.Peas} peas");
                int dx = Toward(Sim.Head.x, Sim.Pea.x, Rules.Cols);
                int dy = Toward(Sim.Head.y, Sim.Pea.y, Rules.Rows);
                var want = Sim.Dir;
                if (Sim.Dir.x != 0 && dx == 0) want = new Vector2Int(0, dy == 0 ? 1 : dy);
                else if (Sim.Dir.y != 0 && dy == 0) want = new Vector2Int(dx == 0 ? 1 : dx, 0);
                else if (Sim.Dir.x != 0 && dx == -Sim.Dir.x) want = new Vector2Int(0, dy == 0 ? 1 : dy);
                else if (Sim.Dir.y != 0 && dy == -Sim.Dir.y) want = new Vector2Int(dx == 0 ? 1 : dx, 0);

                if (want != Sim.Dir && Sim.QueuedTurns.Count == 0)
                {
                    yield return Swipe(want);
                    yield return Settle();
                }
                else yield return null;
                if (Sim.Peas == 2 && captureAt < 0) captureAt = Sim.Moves + 3;
                if (!captured && Sim.Moves >= captureAt && captureAt >= 0)
                {
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("18-noodle-playing");
                }
            }
            Assert.AreEqual(3, Sim.Peas);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");
            Assert.AreEqual(3 * Rules.PeaPoints + Sim.Gems * Rules.GemPoints, Game.Score);
            Assert.Less(Sim.StepTime, Rules.StartStep, "faster with every pea");

            // Let it grow, then make the tight U-turn that bites its own body.
            yield return Frames(Mathf.CeilToInt(Rules.StartStep * 60f * 2f));
            Assert.GreaterOrEqual(Sim.Body.Count, 6);
            var d = Sim.Dir;
            var side = new Vector2Int(d.y, d.x);
            foreach (var turn in new[] { side, -d, -side })
            {
                yield return Swipe(turn);
                yield return Settle();
            }
            for (int i = 0; i < 60 && Game.State == ArcadeGame.Phase.Playing; i++) yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Sim.Crashed);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("noodle"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("19-noodle-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(Rules.StartLength, Sim.Body.Count);
        }
    }
}
