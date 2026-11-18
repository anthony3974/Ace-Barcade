using System.Collections;
using System.Collections.Generic;
using AceBarcade.Games.RainDodge;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.RainDodge.RainSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Rain Dodge through the real arcade with a simulated dragging finger.</summary>
    public class RainDodgePlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("rain-dodge");
            RainDodge.SeedForTests = 17;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            RainDodge.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("rain-dodge");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static RainDodge Game => (RainDodge)Arcade.Current;
        static Rules Sim => Game.Sim;

        /// <summary>
        /// Where to steer next: a spot on a grid over the lower field (or where the bee is, or a
        /// low gem or leaf) whose way there stays clear of the rain for the next second or so.
        /// </summary>
        static Vector2 Pick(Rules sim)
        {
            const float dt = 1f / 60f;
            var candidates = new List<Vector2>();
            for (float x = Rules.MinX; x <= Rules.MaxX + 0.01f; x += 0.5f)
                for (float y = Rules.MinY; y <= -2f; y += 1f)
                    candidates.Add(new Vector2(x, y));
            candidates.Add(sim.Bee);
            var want = sim.Leaf != null && !sim.Shield ? sim.Leaf : sim.Gem;
            if (want != null && want.At.y > -1.5f) want = null;
            if (want != null) candidates.Add(want.At);

            Vector2 best = sim.Bee;
            float bestCost = float.MaxValue;
            foreach (var c in candidates)
            {
                var p = sim.Bee;
                float clear = 2f;
                int firstHit = -1;
                for (int j = 1; j <= 70; j++)
                {
                    p = Vector2.MoveTowards(p, c, Rules.MaxSpeed * dt);
                    float time = sim.Time + (j + 1) * dt;
                    float here = 2f;
                    foreach (var d in sim.Drops) here = Mathf.Min(here, (d.At(time) - p).magnitude - Rules.BeeRadius - d.HitRadius);
                    foreach (var b in sim.Bolts)
                        if (time >= b.StrikeAt - 2f * dt && time < b.EndAt + 2f * dt)
                            here = Mathf.Min(here, Mathf.Abs(p.x - b.X) - Rules.BeeRadius - Rules.BoltHalfWidth);
                    clear = Mathf.Min(clear, here);
                    if (here < 0f)
                    {
                        firstHit = j;
                        break;
                    }
                }
                float cost = 0.12f * (c - sim.Bee).magnitude + 0.08f * Mathf.Abs(c.y + 5f);
                if (firstHit >= 0) cost += 100f + (70 - firstHit) * 2f;
                else if (clear < 0.25f) cost += 80f * (0.25f - clear);
                else cost -= Mathf.Min(clear, 1.5f);
                if (want != null && c == want.At && clear >= 0.25f) cost -= 2f;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = c;
                }
            }
            return best;
        }

        static bool Striking()
        {
            foreach (var b in Sim.Bolts) if (b.StrikingAt(Sim.Time + 0.05f) && Sim.Time > b.StrikeAt) return true;
            return false;
        }

        [UnityTest]
        public IEnumerator Dodges30Seconds_GrabsGems_PastLightning_ThenStandsStillAndGetsHit_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("rain-dodge"));
            yield return Frames(2);
            Assert.IsInstanceOf<RainDodge>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            Assert.AreEqual(0f, Sim.Time, "nothing falls before the first touch");
            Assert.IsEmpty(Sim.Drops);
            yield return PaddleRallyPlayTests.Capture("51-raindodge-ready");

            // The thumb goes down well below the bee and drags it around from there.
            var finger = new Vector2(0f, -6.5f);
            Arcade.Input.Simulate(true, finger);
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.AreEqual(Rules.StartAt, Sim.Bee, "touching down doesn't move the bee");

            bool captured = false;
            int strikes = 0;
            bool wasStriking = false;
            for (int frame = 0; frame < 60 * 30; frame++)
            {
                Assert.IsFalse(Sim.Over, $"hit at {Sim.Time:0.0} s");
                finger += Pick(Sim) - Sim.Target;
                Arcade.Input.Simulate(true, finger);
                bool striking = Striking();
                if (striking && !wasStriking) strikes++;
                wasStriking = striking;
                if (!captured && striking && Sim.Drops.Count >= 5)
                {
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("52-raindodge-dodging");
                }
                else yield return null;
            }
            Assert.IsFalse(Sim.Over);
            Assert.GreaterOrEqual(strikes, 1, "lightning struck");
            Assert.GreaterOrEqual(Sim.GemsTaken, 2);
            Assert.Greater(Sim.Dodged, 40);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Stop moving: drops fall on the bee before long.
            for (int i = 0; i < 60 * 20 && Game.State == ArcadeGame.Phase.Playing; i++)
            {
                Arcade.Input.Simulate(true, finger);
                yield return null;
            }
            Assert.IsTrue(Sim.Over);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("rain-dodge"));
            Arcade.Input.Simulate(false, finger);
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("53-raindodge-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(0f, Sim.Time);
        }
    }
}
