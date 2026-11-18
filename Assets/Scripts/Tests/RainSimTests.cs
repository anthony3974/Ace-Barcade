using System.Collections.Generic;
using System.Linq;
using AceBarcade.Games.RainDodge;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.RainDodge.RainSim;

namespace AceBarcade.Tests
{
    public class RainSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        /// <summary>
        /// A careful player whose finger moves 0.12 s after they decide. Every other frame they
        /// pick a spot to fly to (a grid over the lower field, where they are, where they were
        /// going, or a gem or leaf in the lower half) by playing out the next 1.4 s of rain: a
        /// spot whose way there gets hit soonest is worst, near misses are next, and the more room
        /// the better. They allow for their own lag: the decisions already made play out first.
        /// </summary>
        sealed class Bot
        {
            const int Horizon = 84, Every = 2;
            const float Margin = 0.25f;
            readonly int _lag;
            readonly Queue<(int Frame, Vector2 Target)> _decisions = new Queue<(int, Vector2)>();
            Vector2 _last;
            int _frame;

            public Bot(Sim sim, float lag = 0.12f)
            {
                _lag = Mathf.RoundToInt(lag / Dt);
                _last = sim.Bee;
            }

            public void Tick(Sim sim)
            {
                if (_frame % Every == 0)
                {
                    _last = Decide(sim);
                    _decisions.Enqueue((_frame + _lag, _last));
                }
                while (_decisions.Count > 0 && _decisions.Peek().Frame <= _frame) sim.SteerTo(_decisions.Dequeue().Target);
                _frame++;
            }

            static float Clearance(Sim sim, Vector2 p, float time)
            {
                float c = 2f;
                foreach (var d in sim.Drops)
                {
                    var off = d.At(time) - p;
                    if (off.x > 2.5f || off.x < -2.5f || off.y > 2.5f || off.y < -2.5f) continue;
                    c = Mathf.Min(c, off.magnitude - Sim.BeeRadius - d.HitRadius);
                }
                foreach (var b in sim.Bolts)
                    if (time >= b.StrikeAt - 2f * Dt && time < b.EndAt + 2f * Dt)
                        c = Mathf.Min(c, Mathf.Abs(p.x - b.X) - Sim.BeeRadius - Sim.BoltHalfWidth);
                return c;
            }

            Vector2 Decide(Sim sim)
            {
                var start = sim.Bee;
                var steer = sim.Target;
                foreach (var (frame, target) in _decisions)
                    if (frame < _frame) steer = target;
                for (int k = 0; k < _lag; k++)
                {
                    foreach (var (frame, target) in _decisions)
                        if (frame == _frame + k) steer = target;
                    start = Vector2.MoveTowards(start, steer, Sim.MaxSpeed * Dt);
                }

                var candidates = new List<Vector2>();
                for (float x = Sim.MinX; x <= Sim.MaxX + 0.01f; x += 0.5f)
                    for (float y = Sim.MinY; y <= -2f; y += 1f)
                        candidates.Add(new Vector2(x, y));
                candidates.Add(start);
                candidates.Add(_last);
                var want = sim.Leaf != null && !sim.Shield ? sim.Leaf : sim.Gem;
                if (want != null && want.At.y > -1.5f) want = null;
                if (want != null) candidates.Add(want.At);

                Vector2 best = start;
                float bestCost = float.MaxValue;
                foreach (var c in candidates)
                {
                    var p = start;
                    float clear = 2f;
                    int firstHit = -1;
                    for (int j = 1; j <= Horizon; j++)
                    {
                        p = Vector2.MoveTowards(p, c, Sim.MaxSpeed * Dt);
                        if (p != c || j % 3 == 0)
                        {
                            float here = Clearance(sim, p, sim.Time + (_lag + j) * Dt);
                            clear = Mathf.Min(clear, here);
                            if (here < 0f)
                            {
                                firstHit = j;
                                break;
                            }
                        }
                    }
                    float cost = 0.12f * (c - start).magnitude + 0.08f * Mathf.Abs(c.y + 5f);
                    if (firstHit >= 0) cost += 100f + (Horizon - firstHit) * 2f;
                    else if (clear < Margin) cost += 80f * (Margin - clear);
                    else cost -= Mathf.Min(clear, 1.5f);
                    if (want != null && c == want.At && clear >= Margin) cost -= 2f;
                    if (c == _last) cost -= 1f;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = c;
                    }
                }
                return best;
            }
        }

        static RainEvent Run(Sim sim, float seconds)
        {
            var all = RainEvent.None;
            for (float t = 0f; t < seconds && !sim.Over; t += Dt) all |= sim.Step(Dt);
            return all;
        }

        [Test]
        public void Start_TheBeeWaitsLowInTheMiddle_NothingFallingYet()
        {
            var sim = new Sim(1);
            Assert.AreEqual(Sim.StartAt, sim.Bee);
            Assert.AreEqual(Sim.StartAt, sim.Target);
            Assert.AreEqual(0, sim.Score);
            Assert.IsEmpty(sim.Drops);
            Assert.IsEmpty(sim.Bolts);
            Assert.IsNull(sim.Gem);
            Assert.IsNull(sim.Leaf);
            Assert.IsFalse(sim.Shield);
            Assert.IsFalse(sim.Over);
        }

        [Test]
        public void Dragging_MovesTheBeeTheSameWay_Quickly_AndNeverOutOfItsField()
        {
            var sim = new Sim(1) { Calm = true };
            sim.Grab();
            sim.Drag(new Vector2(1.5f, 2f));
            Assert.AreEqual(Sim.StartAt + new Vector2(1.5f, 2f), sim.Target);
            sim.Step(Dt);
            Assert.AreEqual(Sim.MaxSpeed * Dt, (sim.Bee - Sim.StartAt).magnitude, 1e-4f, "flies at its top speed");
            Run(sim, 0.2f);
            Assert.AreEqual(sim.Target, sim.Bee, "2.5 units takes well under 0.2 s");

            sim.Drag(new Vector2(-20f, 20f));
            Assert.AreEqual(new Vector2(Sim.MinX, Sim.MaxY), sim.Target, "the field stops short of the edges and the clouds");
            Run(sim, 1f);
            Assert.AreEqual(sim.Target, sim.Bee);

            // A new touch starts from where the bee is, so lifting and putting the finger down
            // somewhere else doesn't move it.
            sim.SteerTo(new Vector2(3f, -6f));
            sim.Step(Dt);
            var at = sim.Bee;
            sim.Grab();
            Run(sim, 0.5f);
            Assert.AreEqual(at, sim.Bee);
        }

        [Test]
        public void TheField_StopsWellBelowTheClouds_SoEveryDropIsSeenFallingFirst()
        {
            float fastest = Sim.TopSpeed * (1f + Sim.SpeedJitter);
            float seen = (Sim.CloudBottom - Sim.SmallRadius - (Sim.MaxY + Sim.BeeRadius)) / fastest;
            Assert.GreaterOrEqual(seen, 0.3f, "even at the very top of the field, the fastest drop shows for 0.3 s first");
            Assert.Greater(Sim.SpawnY - Sim.BigRadius, Sim.CloudBottom - 0.2f, "drops start inside the cloud");
            Assert.Less(Sim.MinY - Sim.BeeRadius, Sim.GroundY + 0.8f);
            Assert.Greater(Sim.MinY - Sim.BeeRadius, Sim.GroundY, "the bee flies above the ground");
        }

        [Test]
        public void ADropReachingTheGround_Splashes_AndScores1()
        {
            var sim = new Sim(1) { Calm = true };
            var drop = sim.AddDrop(DropKind.Small, 3f, 0f, 6f);
            Assert.AreEqual(0f, drop.At(sim.Time).y, 1e-4f);
            var all = RainEvent.None;
            for (int i = 0; i < 200 && sim.Drops.Count > 0; i++)
            {
                var e = sim.Step(Dt);
                all |= e;
                if ((e & RainEvent.Splash) != 0)
                {
                    Assert.AreEqual(1, sim.Splashes.Count);
                    Assert.AreEqual(new Vector2(3f, Sim.GroundY), sim.Splashes[0]);
                }
            }
            Assert.IsTrue((all & RainEvent.Splash) != 0);
            Assert.AreEqual(1, sim.Score);
            Assert.AreEqual(1, sim.Dodged);
            Assert.IsFalse(sim.Over);
        }

        [Test]
        public void ADropTouchingTheBee_EndsTheRun_ButAGrazeDoesnt()
        {
            // Passing 0.58 from the bee's middle: the drawn drop and bee overlap, but it hits
            // inside its drawn edge, so this is a graze.
            var sim = new Sim(1) { Calm = true };
            float graze = Sim.BeeRadius + Sim.SmallRadius * Sim.HitShrink + 0.02f;
            Assert.Less(graze, 0.45f + Sim.SmallRadius, "the drawn shapes overlap");
            sim.AddDrop(DropKind.Small, Sim.StartAt.x + graze, Sim.StartAt.y + 2f, 6f);
            Run(sim, 2f);
            Assert.IsFalse(sim.Over);
            Assert.AreEqual(1, sim.Score);

            sim.AddDrop(DropKind.Big, Sim.StartAt.x + 0.5f, Sim.StartAt.y + 2f, 4f);
            var all = Run(sim, 2f);
            Assert.IsTrue((all & RainEvent.Hit) != 0);
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(DropKind.Big, sim.LastDrop.Kind);
            Assert.AreEqual(1, sim.Score, "the drop that hit doesn't score");
            Assert.AreEqual(RainEvent.None, sim.Step(Dt), "nothing happens after the run ends");
        }

        [Test]
        public void TheLeaf_TakesOneHit_ThenABriefSafeSpell_ThenTheNextHitEndsTheRun()
        {
            var sim = new Sim(1) { Calm = true };
            sim.PlacePickup(Sim.StartAt + new Vector2(0.3f, 0f), leaf: true);
            Assert.IsTrue((sim.Step(Dt) & RainEvent.Leaf) != 0);
            Assert.IsTrue(sim.Shield);
            Assert.IsNull(sim.Leaf);

            sim.AddDrop(DropKind.Small, Sim.StartAt.x, Sim.StartAt.y + 1f, 6f);
            var all = Run(sim, 0.5f);
            Assert.IsTrue((all & RainEvent.ShieldLost) != 0);
            Assert.IsFalse(sim.Over);
            Assert.IsFalse(sim.Shield);
            Assert.Greater(sim.SafeFor, 0f);
            Assert.IsEmpty(sim.Drops, "the leaf knocks the drop away");

            // Right after, a drop passes straight through harmlessly...
            sim.AddDrop(DropKind.Small, Sim.StartAt.x, Sim.StartAt.y + 0.8f, 8f);
            Run(sim, 0.4f);
            Assert.IsFalse(sim.Over);
            Run(sim, Sim.SafeAfterLeaf);
            Assert.AreEqual(0f, sim.SafeFor);

            // ...but once the safe spell is over, the next one ends the run.
            sim.AddDrop(DropKind.Small, Sim.StartAt.x, Sim.StartAt.y + 1f, 6f);
            all = Run(sim, 0.5f);
            Assert.IsTrue((all & RainEvent.Hit) != 0);
            Assert.IsTrue(sim.Over);
        }

        [Test]
        public void Gems_Score5_AndGoAwayIfLeft()
        {
            var sim = new Sim(1) { Calm = true };
            sim.PlacePickup(Sim.StartAt + new Vector2(0.5f, 0.2f), leaf: false);
            Assert.IsTrue((sim.Step(Dt) & RainEvent.Gem) != 0);
            Assert.AreEqual(Sim.GemPoints, sim.Score);
            Assert.AreEqual(1, sim.GemsTaken);
            Assert.IsNull(sim.Gem);

            sim.PlacePickup(new Vector2(3f, 0f), leaf: false);
            Run(sim, Sim.GemLife - 0.1f);
            Assert.IsNotNull(sim.Gem);
            Run(sim, 0.2f);
            Assert.IsNull(sim.Gem, "a gem left alone goes");
            Assert.AreEqual(1, sim.GemsTaken);
        }

        [Test]
        public void Lightning_WarnsForASecond_ThenStrikesItsOwnColumnOnly()
        {
            var sim = new Sim(1) { Calm = true };
            float beside = Sim.StartAt.x + Sim.BoltHalfWidth + Sim.BeeRadius + 0.02f;
            var bolt = sim.AddBolt(beside);
            var all = Run(sim, Sim.Warning + Sim.StrikeTime + 0.1f);
            Assert.IsTrue((all & RainEvent.Strike) != 0);
            Assert.IsFalse(sim.Over, "a bolt just beside the bee misses");
            Assert.IsEmpty(sim.Bolts);
            Assert.AreEqual(1, sim.Score, "a bolt that misses counts as dodged");

            bolt = sim.AddBolt(Sim.StartAt.x);
            Run(sim, Sim.Warning - 0.05f);
            Assert.IsFalse(sim.Over, "the warning doesn't hurt");
            Assert.IsFalse(bolt.StrikingAt(sim.Time));
            all = Run(sim, 0.1f);
            Assert.IsTrue((all & RainEvent.Hit) != 0);
            Assert.IsNull(sim.LastDrop, "the bolt hit, not a drop");
        }

        [Test]
        public void StandingStill_IsNotSafe_EveryFourthDropFallsOnTheBee()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                Run(sim, 15f);
                Assert.IsTrue(sim.Over, $"seed {seed}: a bee that never moves gets hit within 15 s");
            }
        }

        [Test]
        public void TheRain_GetsBusierAndFaster_ToACap()
        {
            var sim = new Sim(1);
            Assert.AreEqual(Sim.StartInterval, sim.Interval);
            Assert.AreEqual(Sim.StartSpeed, sim.DropSpeed);
            Assert.Less(Sim.MinInterval, Sim.StartInterval);
            Assert.Greater(Sim.TopSpeed, Sim.StartSpeed);
            Assert.Less(Sim.BigFrom, Sim.BoltsFrom);
            Assert.Less(Sim.BoltsFrom, Sim.SwayFrom);
            Assert.Less(Sim.SwayFrom, Sim.HardestAt);
            Assert.Greater(Sim.Warning, 0.9f, "lightning always warns for about a second");
        }

        [Test]
        public void ACarefulPlayerWithAHumanLag_IsNeverHit_ForLongPastTheHardest_OnEverySeed()
        {
            const float seconds = 100f;
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var bot = new Bot(sim);
                var seen = new HashSet<int>();
                var seenBolts = new HashSet<int>();
                var kinds = new HashSet<DropKind>();
                var recent = new List<float>();
                float firstLeaf = -1f;
                while (sim.Time < seconds && !sim.Over)
                {
                    bot.Tick(sim);
                    var e = sim.Step(Dt);
                    Assert.IsTrue((e & RainEvent.ShieldLost) == 0, $"seed {seed}: hit (and saved by the leaf) at {sim.Time:0.0} s, bee {sim.Bee}");
                    if (sim.Leaf != null && firstLeaf < 0f) firstLeaf = sim.Time;
                    if (sim.Shield) Assert.IsNull(sim.Leaf, "no leaf shows while the bee holds one");

                    foreach (var b in sim.Bolts)
                    {
                        if (!seenBolts.Add(b.Index)) continue;
                        Assert.GreaterOrEqual(b.WarnAt, Sim.BoltsFrom - Dt);
                        Assert.That(b.X, Is.InRange(Sim.MinX, Sim.MaxX));
                    }
                    foreach (var d in sim.Drops.OrderBy(d => d.Index))
                    {
                        if (!seen.Add(d.Index)) continue;
                        kinds.Add(d.Kind);
                        if (d.Kind == DropKind.Big) Assert.GreaterOrEqual(d.Born, Sim.BigFrom - Dt);
                        if (d.Kind == DropKind.Sway) Assert.GreaterOrEqual(d.Born, Sim.SwayFrom - Dt);
                        Assert.LessOrEqual(Mathf.Abs(d.X0) + d.Sway + d.Radius, Style.WorldWidth / 2f + 1e-4f, "drops stay on screen, swaying ones too");
                        foreach (float x in recent.Skip(Mathf.Max(0, recent.Count - 2)))
                            Assert.GreaterOrEqual(Mathf.Abs(d.X0 - x), Sim.Spread - 1e-4f, $"seed {seed}: drop {d.Index} came down next to one of the last two");
                        foreach (var b in sim.Bolts)
                            if (d.Born > b.WarnAt && d.Born < b.EndAt)
                                Assert.GreaterOrEqual(Mathf.Abs(d.X0 - b.X), Sim.BoltClear - 1e-4f, "no new drop down a bolt's column");
                        recent.Add(d.X0);
                    }
                }
                Assert.IsFalse(sim.Over, $"seed {seed}: hit at {sim.Time:0.0} s, bee {sim.Bee}");
                Assert.GreaterOrEqual(sim.GemsTaken, 3, $"seed {seed}");
                Assert.GreaterOrEqual(seenBolts.Count, 10, $"seed {seed}: lightning");
                CollectionAssert.AreEquivalent(new[] { DropKind.Small, DropKind.Big, DropKind.Sway }, kinds);
                Assert.GreaterOrEqual(firstLeaf, Sim.LeafFrom, "the first leaf comes after a while");
                Assert.AreEqual(sim.Dodged + sim.GemsTaken * Sim.GemPoints, sim.Score, "1 a dodge, 5 a gem");
            }
        }
    }
}
