using System.Collections;
using AceBarcade.Games.StarSwarm;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.StarSwarm.StarSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Star Swarm through the real arcade, dragging the ship with a simulated finger.</summary>
    public class StarSwarmPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("star-swarm");
            StarSwarm.SeedForTests = 11;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            StarSwarm.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("star-swarm");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static StarSwarm Game => (StarSwarm)Arcade.Current;
        static Rules Sim => Game.Sim;

        static void Finger(float x) => Arcade.Input.Simulate(true, new Vector2(x, -4.5f));

        /// <summary>The closest gap to a bullet or diver if the ship were at x when each comes through its row.</summary>
        static float Margin(float x)
        {
            float worst = float.MaxValue;
            foreach (var b in Sim.Bullets)
            {
                float t = (b.Y - Rules.ShipY) / b.Speed;
                if (t < -0.15f || t > 1f) continue;
                worst = Mathf.Min(worst, Mathf.Abs(b.X - x) - Rules.ShipRadius - Rules.BulletRadius - 0.3f);
            }
            foreach (var f in Sim.Foes)
            {
                if (f.State != FoeState.Diving) continue;
                for (float s = 0f; s < 1.2f; s += 0.05f)
                {
                    var p = Sim.DiverAhead(f, s);
                    if (Mathf.Abs(p.y - Rules.ShipY) < f.Radius + Rules.ShipRadius + 0.2f)
                        worst = Mathf.Min(worst, Mathf.Abs(p.x - x) - f.Radius - Rules.ShipRadius - 0.3f);
                }
            }
            return worst;
        }

        /// <summary>Under the nearest enemy (or a falling gem), wherever that's clear of what's coming down.</summary>
        static float Steer()
        {
            float want = 0f, nearest = float.MaxValue;
            if (Sim.Gem.HasValue) want = Sim.Gem.Value.x;
            else
                foreach (var f in Sim.Foes)
                    if (f.State == FoeState.Formation && Mathf.Abs(f.X - Sim.ShipX) < nearest) { nearest = Mathf.Abs(f.X - Sim.ShipX); want = f.X; }
            float best = Sim.ShipX, bestCost = float.MaxValue, safest = Sim.ShipX, safestMargin = float.MinValue;
            for (float x = -Rules.ShipLimit; x <= Rules.ShipLimit + 1e-3f; x += 0.15f)
            {
                float m = Margin(x);
                // Getting there mustn't cross anything coming through soon.
                float travel = Mathf.Abs(x - Sim.ShipX) / Rules.ShipSpeed + 0.05f;
                float lo = Mathf.Min(x, Sim.ShipX), hi = Mathf.Max(x, Sim.ShipX);
                foreach (var b in Sim.Bullets)
                {
                    float t = (b.Y - Rules.ShipY) / b.Speed;
                    if (t > -0.15f && t < travel + 0.1f && b.X + 0.5f > lo && b.X - 0.5f < hi) m = Mathf.Min(m, -1f);
                }
                if (m > safestMargin) { safestMargin = m; safest = x; }
                if (m < 0f) continue;
                float cost = Mathf.Abs(x - want) + 0.3f * Mathf.Abs(x - Sim.ShipX);
                if (cost < bestCost) { bestCost = cost; best = x; }
            }
            return bestCost < float.MaxValue ? best : safest;
        }

        [UnityTest]
        public IEnumerator DragsToDodgeAndShoot_ClearsTwoWaves_ThenSitsStillUntilItsHit_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("star-swarm"));
            yield return Frames(2);
            Assert.IsInstanceOf<StarSwarm>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            Assert.AreEqual(0, Sim.Shots.Count, "nothing fires until the first touch");
            Assert.AreEqual(3 * Rules.Cols, Sim.Foes.Count);
            yield return PaddleRallyPlayTests.Capture("29-starswarm-ready");

            // The touch that starts the run also steers.
            Finger(2f);
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            yield return Frames(20);
            Assert.AreEqual(2f, Sim.ShipX, 1e-3f, "the ship slides to the finger");
            Assert.Greater(Sim.Shots.Count, 0, "and fires on its own");

            // Shoot, dodge and catch gems through two waves.
            bool captured = false;
            for (int frame = 0; frame < 60 * 150 && Sim.Wave < 2; frame++)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
                if (!captured && Sim.Wave == 1 && Sim.Bullets.Count > 0 && System.Linq.Enumerable.Any(Sim.Foes, f => f.State == FoeState.Diving && f.Y < 0f))
                {
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("30-starswarm-playing");
                }
                Finger(Steer());
                yield return null;
            }
            Assert.AreEqual(2, Sim.Wave, "two waves cleared");
            Assert.AreEqual(Rules.StartLives, Sim.Lives, "without being hit");
            Assert.GreaterOrEqual(Sim.Gems, 1);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Lift the finger: the ship stays put, and the divers and bullets get it.
            Arcade.Input.Simulate(false, new Vector2(Sim.ShipX, -4.5f));
            float parked = Sim.ShipX;
            for (int i = 0; i < 60 * 180 && Game.State == ArcadeGame.Phase.Playing; i++) yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.AreEqual(parked, Sim.ShipX, 1e-4f);
            Assert.AreEqual(0, Sim.Lives);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("star-swarm"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("31-starswarm-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(0, Sim.Wave);
        }
    }
}
