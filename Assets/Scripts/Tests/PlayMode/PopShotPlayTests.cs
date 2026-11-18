using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AceBarcade.Games.PopShot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.PopShot.ShotSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Pop Shot through the real arcade with simulated drags.</summary>
    public class PopShotPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("pop-shot");
            PopShot.SeedForTests = 15;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            PopShot.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("pop-shot");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static PopShot Game => (PopShot)Arcade.Current;
        static Rules Sim => Game.Sim;

        /// <summary>The aim that pops the most, else sticks highest next to its own colour.</summary>
        static Vector2 BestAim()
        {
            Vector2 best = Vector2.up;
            float bestValue = float.MinValue;
            for (float angle = Rules.MinAngle; angle <= 180f - Rules.MinAngle; angle += 1f)
            {
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                Sim.Trace(dir, out int row, out int col);
                if (row < 0) continue;
                int clump = 1 + Clump(row, col, Sim.Current);
                float value = (clump >= Rules.PopAt ? 100f + clump : clump * 3f) - row * 0.5f;
                if (value > bestValue) { bestValue = value; best = dir; }
            }
            return best;
        }

        static int Clump(int row, int col, int color)
        {
            var seen = new HashSet<(int, int)> { (row, col) };
            var stack = new Stack<(int, int)>();
            stack.Push((row, col));
            int n = 0;
            while (stack.Count > 0)
            {
                var (r, c) = stack.Pop();
                foreach (var (nr, nc) in Sim.Neighbours(r, c))
                {
                    var b = Sim.At(nr, nc);
                    if (b == null || b.Color != color || !seen.Add((nr, nc))) continue;
                    n++;
                    stack.Push((nr, nc));
                }
            }
            return n;
        }

        /// <summary>Drags the finger to aim along <paramref name="dir"/>, holds, lets go, and waits for the shot to stick.</summary>
        static IEnumerator Shoot(Vector2 dir, string capture = null)
        {
            var finger = Rules.Shooter + dir * 4f;
            Arcade.Input.Simulate(true, finger + new Vector2(0.6f, -0.4f));
            yield return null;
            for (int i = 0; i < 6; i++)
            {
                Arcade.Input.Simulate(true, finger);
                yield return null;
            }
            if (capture != null) yield return PaddleRallyPlayTests.Capture(capture);
            int shots = Sim.Shots;
            Arcade.Input.Simulate(false, finger);
            yield return null;
            Assert.AreEqual(shots + 1, Sim.Shots, "letting go shoots");
            // A shallow shot bounces across many times on its way up: give it time.
            for (int i = 0; i < 600 && Sim.Flying; i++) yield return null;
            Assert.IsFalse(Sim.Flying);
            yield return Frames(4);
        }

        [UnityTest]
        public IEnumerator AimsAndShoots30Times_PoppingAndDropping_RowsPushIn_ThenOneBelowTheLineEndsIt_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("pop-shot"));
            yield return Frames(2);
            Assert.IsInstanceOf<PopShot>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            yield return PaddleRallyPlayTests.Capture("44-popshot-ready");

            // The touch that starts the game doesn't shoot, even after a drag.
            Arcade.Input.Simulate(true, new Vector2(-2f, 0f));
            yield return Frames(3);
            Arcade.Input.Simulate(true, new Vector2(-1f, 1f));
            yield return Frames(3);
            Arcade.Input.Simulate(false, new Vector2(-1f, 1f));
            yield return Frames(3);
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.AreEqual(0, Sim.Shots);

            int popped = 0;
            for (int shot = 0; shot < 30 && !Sim.Over; shot++)
            {
                var aim = BestAim();
                Sim.Trace(aim, out int row, out int col);
                int scoreBefore = Game.Score;
                yield return Shoot(aim, shot == 12 ? "45-popshot-aim" : null);
                Assert.AreEqual((row, col), (Sim.LandedRow, Sim.LandedCol), $"shot {shot} stuck where the aim line ended");
                if (Game.Score > scoreBefore) popped++;
            }
            Assert.IsFalse(Sim.Over);
            Assert.AreEqual(30, Sim.Shots);
            Assert.Greater(popped, 5, "plenty of pops");
            Assert.GreaterOrEqual(Sim.RowsPushed, 1, "and a row or more pushed in");
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");
            yield return PaddleRallyPlayTests.Capture("46-popshot-playing");

            // A board down to the red line: one more bubble under it ends the run.
            var rows = Enumerable.Range(0, Rules.MaxRows).Select(r => r % 2 == 0 ? "PO......" : "P......").ToArray();
            Sim.Load(4, 4, rows);
            yield return Frames(2);
            yield return Shoot(Rules.AimToward(Sim.CellPos(Rules.MaxRows - 1, 0)));
            Assert.IsTrue(Sim.Over);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("pop-shot"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("47-popshot-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(0, Sim.Shots);
        }
    }
}
