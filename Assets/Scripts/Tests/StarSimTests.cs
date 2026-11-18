using System.Collections.Generic;
using System.Linq;
using AceBarcade.Games.StarSwarm;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.StarSwarm.StarSim;

namespace AceBarcade.Tests
{
    public class StarSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        /// <summary>
        /// A player with human limits: it sees everything, but its finger lands where it decided
        /// 0.2 s ago. It sits under the nearest enemy to shoot it (or under a falling gem), unless a
        /// bullet or diver will come down there, or across its way there, soon.
        /// </summary>
        sealed class Bot
        {
            public const float Lag = 0.2f;
            readonly Queue<(float at, float x)> _plan = new Queue<(float, float)>();
            public static readonly List<string> Trace = new List<string>();
            float _time, _target;

            public float Tick(Sim sim)
            {
                _time += Dt;
                _plan.Enqueue((_time + Lag, Decide(sim)));
                while (_plan.Count > 0 && _plan.Peek().at <= _time + 1e-4f) _target = _plan.Dequeue().x;
                return _target;
            }

            float Decide(Sim sim)
            {
                float want = Want(sim);
                var dangers = Dangers(sim);
                Pending(sim);
                // Keep a wide berth if it can, a narrow one if it must.
                _standClear = 0.35f;
                bool any = false;
                for (float x = -Sim.ShipLimit; x <= Sim.ShipLimit + 1e-3f && !any; x += 0.15f) any = Margin(dangers, x) >= 0f;
                if (!any) _standClear = 0.12f;
                float best = sim.ShipX, bestCost = float.MaxValue, fallback = sim.ShipX, fallbackMargin = float.MinValue;
                for (float x = -Sim.ShipLimit; x <= Sim.ShipLimit + 1e-3f; x += 0.15f)
                {
                    float margin = Margin(dangers, x);
                    // With nothing safe, the least bad place, and the nearest of those.
                    if (margin > fallbackMargin + 0.01f || (margin > fallbackMargin - 0.01f && Mathf.Abs(x - _pending[Steps]) < Mathf.Abs(fallback - _pending[Steps])))
                    {
                        fallbackMargin = Mathf.Max(margin, fallbackMargin);
                        fallback = x;
                    }
                    if (margin < 0f) continue;
                    float cost = Mathf.Abs(x - want) + 0.3f * Mathf.Abs(x - sim.ShipX);
                    if (cost < bestCost) { bestCost = cost; best = x; }
                }
                bool none = bestCost == float.MaxValue;
                if (Trace.Count > 70) Trace.RemoveAt(0);
                Trace.Add($"{sim.Time:0.00} ship {sim.ShipX:0.00} want {want:0.00} -> {(none ? fallback : best):0.00}{(none ? " (NOTHING SAFE)" : "")} dangers " +
                    string.Join(" ", dangers.Select(d => $"[{d.x:0.00}±{d.hit:0.00} in {d.t:0.00}]")) +
                    " divers " + string.Join(" ", sim.Foes.Where(f => f.State == FoeState.Diving).Select(f => $"({f.X:0.00},{f.Y:0.00})")));
                return none ? fallback : best;
            }

            static float Want(Sim sim)
            {
                if (sim.Gem.HasValue) return sim.Gem.Value.x;
                StarFoe near = null;
                foreach (var f in sim.Foes)
                    if (f.State == FoeState.Formation && (near == null || Mathf.Abs(f.X - sim.ShipX) < Mathf.Abs(near.X - sim.ShipX))) near = f;
                return near != null ? near.X : 0f;
            }

            /// <summary>Where bullets and divers will come through the ship's row soon: x, how close is a hit, and in how long.</summary>
            static List<(float x, float hit, float t)> Dangers(Sim sim)
            {
                var list = new List<(float, float, float)>();
                foreach (var b in sim.Bullets)
                {
                    // A bullet is in the ship's row for a moment, not an instant.
                    const float hit = Sim.ShipRadius + Sim.BulletRadius;
                    float t = (b.Y - Sim.ShipY) / b.Speed, w = (hit + 0.2f) / b.Speed;
                    if (t + w < 0f || t - w > 1.5f) continue;
                    for (float s = Mathf.Max(0f, t - w); s <= t + w; s += 0.03f) list.Add((b.X, hit, s));
                }
                foreach (var f in sim.Foes)
                {
                    if (f.State != FoeState.Diving) continue;
                    float hit = f.Radius + Sim.ShipRadius;
                    for (float s = 0f; s < 1.8f; s += 0.05f)
                    {
                        var p = sim.DiverAhead(f, s);
                        if (Mathf.Abs(p.y - Sim.ShipY) <= hit + 0.2f) list.Add((p.x, hit, s));
                    }
                }
                return list;
            }

            /// <summary>Where the ship goes over the next <see cref="Lag"/>, following what the bot already decided.</summary>
            readonly float[] _pending = new float[Steps + 1];
            const int Steps = 12;
            float _standClear = 0.35f;

            void Pending(Sim sim)
            {
                float pos = sim.ShipX, target = _target;
                _pending[0] = pos;
                for (int k = 0; k < Steps; k++)
                {
                    float at = _time + k * Dt;
                    foreach (var p in _plan) if (p.at <= at + 1e-4f) target = p.x;
                    pos = Mathf.MoveTowards(pos, Mathf.Clamp(target, -Sim.ShipLimit, Sim.ShipLimit), Sim.ShipSpeed * Dt);
                    _pending[k + 1] = pos;
                }
            }

            /// <summary>
            /// How safely it can head for x and stay there: the smallest gap to a danger, less the margin
            /// wanted (wide for where it ends up, narrow on the way). Below zero is not safe.
            /// </summary>
            float Margin(List<(float x, float hit, float t)> dangers, float x)
            {
                float worst = float.MaxValue;
                float start = _pending[Steps];
                float dir = Mathf.Sign(x - start), far = Mathf.Abs(x - start);
                foreach (var d in dangers)
                {
                    // Where the ship is when this danger comes through.
                    float at;
                    bool there = false;
                    if (d.t < Lag) at = _pending[Mathf.Clamp(Mathf.RoundToInt(d.t / Dt), 0, Steps)];
                    else
                    {
                        float moved = Mathf.Min(far, Sim.ShipSpeed * (d.t - Lag));
                        at = start + dir * moved;
                        there = moved >= far - 1e-3f;
                    }
                    // Something still a second away can be stepped aside from later.
                    if (there && d.t > 1f) continue;
                    worst = Mathf.Min(worst, Mathf.Abs(d.x - at) - d.hit - (there ? _standClear : 0.1f));
                }
                return worst;
            }
        }

        static void Play(Sim sim, Bot bot, float seconds, System.Func<bool> until, System.Action<StarEvent> each = null)
        {
            for (float t = 0; t < seconds && !sim.Over && !until(); t += Dt)
            {
                sim.SetTarget(bot.Tick(sim));
                var e = sim.Step(Dt);
                each?.Invoke(e);
            }
        }

        // --- The start ------------------------------------------------------------------------

        [Test]
        public void Start_ThreeRowsOfDrones_OneCarryingAGem_ClearOfTheHudAndTheShip()
        {
            var sim = new Sim(1);
            Assert.AreEqual(0, sim.Wave);
            Assert.AreEqual(Sim.StartLives, sim.Lives);
            Assert.AreEqual(3 * Sim.Cols, sim.Foes.Count);
            Assert.IsTrue(sim.Foes.All(f => f.Kind == FoeKind.Drone && f.State == FoeState.Formation));
            Assert.AreEqual(1, sim.Foes.Count(f => f.HasGem));
            for (int wave = 0; wave <= Sim.HardestWave; wave++)
            {
                for (int row = 0; row < Sim.RowsFor(wave); row++)
                for (int col = 0; col < Sim.Cols; col++)
                {
                    foreach (float sway in new[] { -Sim.SwayAmp, Sim.SwayAmp })
                    {
                        float x = sway + (col - (Sim.Cols - 1) / 2f) * Sim.SpacingX;
                        Assert.LessOrEqual(Mathf.Abs(x) + Sim.BruteRadius, Style.WorldWidth / 2f, "on the screen");
                    }
                    float y = Sim.TopY - row * Sim.SpacingY;
                    Assert.LessOrEqual(y + Sim.BruteRadius, Style.WorldHeight / 2f - 1.5f, "below the HUD");
                    Assert.Greater(y - Sim.BruteRadius, Sim.ShipY + 6f, "well above the ship");
                }
            }
        }

        [Test]
        public void TheShip_FollowsTheFinger_AtACappedSpeed_AndStaysWhenItLifts()
        {
            var sim = new Sim(1);
            sim.SetTarget(3f);
            sim.Step(0.1f);
            Assert.AreEqual(Sim.ShipSpeed * 0.1f, sim.ShipX, 1e-4f, "quick, not instant");
            for (int i = 0; i < 30; i++) sim.Step(Dt);
            Assert.AreEqual(3f, sim.ShipX, 1e-4f);
            sim.SetTarget(9f);
            for (int i = 0; i < 30; i++) sim.Step(Dt);
            Assert.AreEqual(Sim.ShipLimit, sim.ShipX, 1e-4f, "stops at the edge");
            sim.SetTarget(null);
            for (int i = 0; i < 30; i++) sim.Step(Dt);
            Assert.AreEqual(Sim.ShipLimit, sim.ShipX, 1e-4f, "stays put when the finger lifts");
        }

        [Test]
        public void TheShip_FiresOnItsOwn_ADroneTakesOneShot_ABruteTwo()
        {
            var sim = new Sim(1);
            int fired = 0;
            for (int i = 0; i < 60; i++) if ((sim.Step(Dt) & StarEvent.Fired) != 0) fired++;
            Assert.AreEqual(Mathf.RoundToInt(1f / Sim.FireEvery), fired, 1);

            sim = new Sim(1);
            var events = StarEvent.None;
            for (int i = 0; i < 120 && (events & StarEvent.Killed) == 0; i++)
            {
                var lowest = sim.Foes.Where(f => f.Alive).OrderBy(f => f.Y).ThenBy(f => Mathf.Abs(f.X)).First();
                sim.SetTarget(lowest.X);
                events |= sim.Step(Dt);
            }
            Assert.IsTrue((events & StarEvent.Killed) != 0);
            Assert.AreEqual(FoeKind.Drone, sim.LastFoe.Kind);
            Assert.AreEqual(Sim.DronePoints, sim.Score);

            sim = new Sim(1);
            sim.SkipToWaveForTests(1);
            var brute = sim.Foes.First(f => f.Kind == FoeKind.Brute && f.Col == 0);
            // Clear the drones under it first, then two shots for the brute.
            var hits = new List<StarEvent>();
            for (int i = 0; i < 60 * 10 && brute.Alive; i++)
            {
                sim.SetTarget(brute.X);
                var e = sim.Step(Dt);
                if ((e & (StarEvent.Hit | StarEvent.Killed)) != 0 && sim.LastFoe == brute) hits.Add(e);
            }
            Assert.IsFalse(brute.Alive);
            Assert.AreEqual(2, hits.Count);
            Assert.IsTrue((hits[0] & StarEvent.Hit) != 0, "the first shot cracks it");
            Assert.IsTrue((hits[1] & StarEvent.Killed) != 0, "the second breaks it");
        }

        [Test]
        public void Waves_GetHarderToACap()
        {
            for (int w = 1; w <= Sim.HardestWave + 3; w++)
            {
                Assert.GreaterOrEqual(Sim.RowsFor(w), Sim.RowsFor(w - 1));
                Assert.GreaterOrEqual(Sim.BruteRowsFor(w), Sim.BruteRowsFor(w - 1));
                Assert.LessOrEqual(Sim.DiveEvery(w), Sim.DiveEvery(w - 1));
                Assert.LessOrEqual(Sim.BulletEvery(w), Sim.BulletEvery(w - 1));
                Assert.GreaterOrEqual(Sim.BulletSpeed(w), Sim.BulletSpeed(w - 1));
                Assert.GreaterOrEqual(Sim.MaxDivers(w), Sim.MaxDivers(w - 1));
            }
            int hard = Sim.HardestWave, later = Sim.HardestWave + 10;
            Assert.AreEqual(Sim.RowsFor(hard), Sim.RowsFor(later));
            Assert.AreEqual(Sim.DiveEvery(hard), Sim.DiveEvery(later));
            Assert.AreEqual(Sim.BulletEvery(hard), Sim.BulletEvery(later));
            Assert.AreEqual(Sim.BulletSpeed(hard), Sim.BulletSpeed(later));
            Assert.AreEqual(5, Sim.RowsFor(hard));
            Assert.AreEqual(3, Sim.MaxDivers(hard));
        }

        // --- Scoring --------------------------------------------------------------------------

        [Test]
        public void Scoring_Drones1_Brutes3_DoubleWhileDiving_Waves10_Gems5()
        {
            var sim = new Sim(4);
            var bot = new Bot();
            int expected = 0, gems = 0, divers = 0, cleared = 0;
            Play(sim, bot, 400f, () => sim.Wave >= 4, e =>
            {
                foreach (var (foe, dived) in sim.KilledLastStep)
                {
                    int points = foe.Kind == FoeKind.Brute ? Sim.BrutePoints : Sim.DronePoints;
                    if (dived) { points *= 2; divers++; }
                    expected += points;
                }
                if ((e & StarEvent.Gem) != 0) { expected += Sim.GemPoints; gems++; }
                if ((e & StarEvent.WaveCleared) != 0) { expected += Sim.WavePoints; cleared++; }
                Assert.AreEqual(expected, sim.Score);
            });
            Assert.IsFalse(sim.Over);
            Assert.AreEqual(4, cleared);
            Assert.GreaterOrEqual(gems, 2, "it catches the gems it goes for");
            Assert.Greater(divers, 0, "some divers get shot");
            Assert.AreEqual(4, sim.Wave);
            Assert.AreEqual(Sim.RowsFor(4) * Sim.Cols, sim.Foes.Count, "the next wave is bigger");
        }

        // --- Getting hit ----------------------------------------------------------------------

        [Test]
        public void Hits_CostALife_ClearTheBullets_AndGiveAMomentOfSafety_ThreeEndTheRun()
        {
            var sim = new Sim(2);
            sim.SkipToWaveForTests(Sim.HardestWave);
            sim.SetTarget(0f);
            int hits = 0;
            var events = StarEvent.None;
            for (float t = 0; t < 120f && !sim.Over; t += Dt)
            {
                var e = sim.Step(Dt);
                events |= e;
                if ((e & StarEvent.ShipHit) == 0) continue;
                hits++;
                Assert.AreEqual(Sim.StartLives - hits, sim.Lives);
                Assert.AreEqual(0, sim.Bullets.Count, "bullets are cleared");
                if (sim.Over) break;
                Assert.AreEqual(Sim.SafeTime, sim.SafeFor, 1e-4f);
                // Nothing hurts it while it's safe.
                for (float s = 0; s < Sim.SafeTime - 0.05f; s += Dt)
                    Assert.AreEqual(StarEvent.None, sim.Step(Dt) & StarEvent.ShipHit);
            }
            Assert.AreEqual(Sim.StartLives, hits, "sitting still in the middle gets it hit");
            Assert.IsTrue(sim.Over);
            Assert.IsTrue((events & StarEvent.GameOver) != 0);
            int score = sim.Score;
            Assert.AreEqual(StarEvent.None, sim.Step(1f), "nothing happens after it ends");
            Assert.AreEqual(score, sim.Score);
        }

        // --- Fairness -------------------------------------------------------------------------

        [Test]
        public void BulletsFallStraightDown_AndNoMoreThanTwoThreatsComeAtOnce()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                sim.SkipToWaveForTests(Sim.HardestWave);
                var bot = new Bot();
                var seen = new HashSet<(float, float)>();
                var all = new List<float>();
                var xs = new Dictionary<StarShot, float>();
                Play(sim, bot, 90f, () => false, e =>
                {
                    foreach (var b in sim.Bullets)
                    {
                        if (!xs.ContainsKey(b)) xs[b] = b.X;
                        Assert.AreEqual(xs[b], b.X, "straight down");
                    }
                    // A hit wipes the bullets, so the ones that never came don't count.
                    if ((e & StarEvent.ShipHit) != 0)
                        all.RemoveAll(time => time > sim.Time && !sim.Threats.Any(t => t.time == time));
                    foreach (var t in sim.Threats)
                        if (seen.Add(t)) all.Add(t.time);
                });
                all.Sort();
                Assert.Greater(all.Count, 40, $"seed {seed}: plenty of bullets and divers");
                for (int i = Sim.MaxThreats; i < all.Count; i++)
                    Assert.GreaterOrEqual(all[i] - all[i - Sim.MaxThreats], Sim.ThreatWindow - 1e-4f, $"seed {seed}: threats {i - Sim.MaxThreats}..{i}");
            }
        }

        [Test]
        public void ABotWithHumanReactions_ClearsPastTheHardestWave_WithoutLosingALife_OnEverySeed()
        {
            int goal = Sim.HardestWave + 5;
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var bot = new Bot();
                string lost = null;
                Play(sim, bot, 1500f, () => sim.Wave >= goal || lost != null, e =>
                {
                    if ((e & StarEvent.ShipHit) != 0)
                        lost = $"seed {seed}: hit in wave {sim.Wave} at {sim.Time:0.00}s, ship {sim.ShipX:0.00}, by ({sim.LastPoint.x:0.00}, {sim.LastPoint.y:0.00})\n" + string.Join("\n", Bot.Trace.Skip(Bot.Trace.Count - 28).Select(l => l.Length > 330 ? l.Substring(0, 330) : l));
                });
                Assert.IsNull(lost, lost);
                Assert.AreEqual(goal, sim.Wave, $"seed {seed}: too slow");
            }
        }
    }
}
