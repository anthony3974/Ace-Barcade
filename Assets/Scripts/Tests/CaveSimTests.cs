using System.Collections.Generic;
using System.Linq;
using AceBarcade.Games.CaveGlide;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.CaveGlide.CaveSim;

namespace AceBarcade.Tests
{
    public class CaveSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        /// <summary>
        /// A player who steers for the middle of the way through, a little ahead, but whose finger
        /// goes down or up 0.12 s after they decide. Like a player, they allow for the glider's
        /// swing: they let go before it reaches the height they want, and hold before it drops to it.
        /// </summary>
        sealed class Bot
        {
            const float Lag = 0.12f;
            readonly Queue<(float At, bool Hold)> _decisions = new Queue<(float, bool)>();
            float _time;
            bool _hold;

            public bool Tick(Sim sim)
            {
                _time += Dt;
                float target = sim.PathAt(sim.GliderWorldX + sim.Speed * 0.2f);
                float acc = _hold ? Sim.Lift : -Sim.Gravity;
                float v = Mathf.Clamp(sim.Velocity + acc * Lag, -Sim.MaxFall, Sim.MaxRise);
                float y = sim.Y + (sim.Velocity + v) / 2f * Lag;
                float ahead = y + (v > 0f ? v * v / (2f * Sim.Gravity) : -v * v / (2f * Sim.Lift));
                _decisions.Enqueue((_time + Lag, ahead < target));
                while (_decisions.Count > 0 && _decisions.Peek().At <= _time) _hold = _decisions.Dequeue().Hold;
                return _hold;
            }
        }

        static void Fly(Sim sim, float seconds, System.Func<bool> hold, System.Action each = null)
        {
            for (float t = 0; t < seconds && !sim.Crashed; t += Dt)
            {
                sim.Hold(hold());
                sim.Step(Dt);
                each?.Invoke();
            }
        }

        [Test]
        public void Start_TheGliderInAWideStraightCave_NoCrystalsYet()
        {
            var sim = new Sim(1);
            Assert.AreEqual(Sim.StartY, sim.Y);
            Assert.AreEqual(0f, sim.Velocity);
            Assert.AreEqual(0, sim.Score);
            Assert.LessOrEqual(sim.Samples[0].X, sim.GliderWorldX - Style.WorldWidth / 2f);
            Assert.GreaterOrEqual(sim.Samples.Last().X, sim.GliderWorldX + Style.WorldWidth);
            foreach (var s in sim.Samples.Where(s => s.X <= Sim.CalmStart))
            {
                Assert.AreEqual(Sim.StartY, s.Path);
                Assert.AreEqual(Sim.StartHeight, s.Top - s.Bottom, 2f * Sim.Lumps + 1e-4f);
            }
            Assert.AreEqual(0, sim.Crystals.Count);
        }

        [Test]
        public void Holding_Lifts_LettingGo_Sinks_BothUpToACap()
        {
            var sim = new Sim(1);
            Fly(sim, 0.15f, () => true);
            Assert.Greater(sim.Velocity, 0f);
            Assert.Greater(sim.Y, Sim.StartY);
            Fly(sim, 0.6f, () => false);
            Assert.AreEqual(-Sim.MaxFall, sim.Velocity, 1e-3f, "still above the floor, falling flat out");
            sim = new Sim(1);
            Fly(sim, 0.4f, () => true);
            Assert.LessOrEqual(sim.Velocity, Sim.MaxRise);
            Assert.IsFalse(sim.Crashed);
        }

        [Test]
        public void TheRock_JustStopsTheGlider_ItDoesntEndTheRun()
        {
            var sim = new Sim(1);
            var all = CaveEvent.None;
            for (float t = 0; t < 2f; t += Dt) { sim.Hold(true); all |= sim.Step(Dt); }
            Assert.IsFalse(sim.Crashed);
            Assert.IsTrue((all & CaveEvent.Bumped) != 0);
            Assert.AreEqual(sim.TopAt(sim.GliderWorldX) - Sim.Radius, sim.Y, 1e-3f);
            all = CaveEvent.None;
            for (float t = 0; t < 2f; t += Dt) { sim.Hold(false); all |= sim.Step(Dt); }
            Assert.IsFalse(sim.Crashed);
            Assert.IsTrue((all & CaveEvent.Bumped) != 0);
            Assert.AreEqual(sim.BottomAt(sim.GliderWorldX) + Sim.Radius, sim.Y, 1e-3f);
        }

        [Test]
        public void Crystals_HitWhereTheyAre_NotBesideTheirBaseOrPastTheirPoint()
        {
            var c = new Crystal(0, 10f, false, 1.5f, -3f);
            Assert.IsTrue(Sim.Hits(c, new Vector2(10f, -2.5f), out _), "inside it");
            Assert.IsTrue(Sim.Hits(c, new Vector2(10f, c.TipY - Sim.HitShrink + Sim.Radius - 0.02f), out _), "just over its point");
            Assert.IsFalse(Sim.Hits(c, new Vector2(10f, c.TipY - Sim.HitShrink + Sim.Radius + 0.02f), out _), "clear of its point");
            Assert.IsFalse(Sim.Hits(c, new Vector2(10f + Sim.CrystalBase / 2f + Sim.Radius + 0.05f, -2.9f), out _), "beside its base");
            var up = new Crystal(1, 10f, true, 2f, 3f);
            Assert.AreEqual(1f, up.TipY);
            Assert.IsTrue(Sim.Hits(up, new Vector2(10f, 1.4f), out var at));
            Assert.Less(at.y, 3f);
            Assert.IsFalse(Sim.Hits(up, new Vector2(10f, 1f - Sim.Radius - 0.02f), out _));
        }

        [Test]
        public void TheCave_FollowsAPathNoSteeperThanTheGliderCanFly_AndCrystalsNeverReachIt()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var bot = new Bot();
                var samples = new Dictionary<int, CaveSample>();
                var crystals = new Dictionary<int, Crystal>();
                Fly(sim, 120f, () => bot.Tick(sim), () =>
                {
                    foreach (var s in sim.Samples) samples[s.Index] = s;
                    foreach (var c in sim.Crystals) crystals[c.Index] = c;
                });
                var list = samples.Values.OrderBy(s => s.Index).ToList();
                Assert.Greater(list.Last().X, Sim.HardestAt + 100f, $"seed {seed} got far enough");
                for (int i = 1; i < list.Count; i++)
                {
                    var s = list[i];
                    Assert.AreEqual(Sim.SampleStep, s.X - list[i - 1].X, 1e-3f);
                    Assert.LessOrEqual(Mathf.Abs(s.Path - list[i - 1].Path), Sim.MaxSlope * Sim.SampleStep + 0.01f, $"seed {seed}, x {s.X}");
                    if (i > 1)
                    {
                        float bend = (s.Path - list[i - 1].Path) - (list[i - 1].Path - list[i - 2].Path);
                        Assert.LessOrEqual(Mathf.Abs(bend), Sim.SlopeChange * Sim.SampleStep + 0.01f, $"seed {seed}, x {s.X}: no sharp bends");
                    }
                    Assert.Greater(s.Top - Sim.Radius, s.Path, $"seed {seed}, x {s.X}");
                    Assert.Less(s.Bottom + Sim.Radius, s.Path, $"seed {seed}, x {s.X}");
                    Assert.LessOrEqual(s.Top, Sim.Roof);
                    Assert.GreaterOrEqual(s.Bottom, Sim.Ground);
                }
                Assert.AreEqual(Sim.MinHeight, Sim.HeightAt(list.Last().X));

                var cs = crystals.Values.OrderBy(c => c.X).ToList();
                Assert.Greater(cs.Count, 30, $"seed {seed}");
                Assert.GreaterOrEqual(cs[0].X, Sim.CrystalsFrom);
                Assert.IsTrue(cs.Any(c => c.Ceiling) && cs.Any(c => !c.Ceiling), "from both sides");
                Assert.IsTrue(cs.Any(c => c.Depth >= Sim.Depths[1]), "longer ones, later on");
                Assert.IsTrue(cs.Where(c => c.X < Sim.CrystalsFrom + 0.3f * (Sim.HardestAt - Sim.CrystalsFrom)).All(c => c.Depth == Sim.Depths[0]), "short ones first");
                for (int i = 0; i < cs.Count; i++)
                {
                    var c = cs[i];
                    var s = list.First(x => Mathf.Abs(x.X - c.X) < 1e-3f);
                    Assert.GreaterOrEqual(Mathf.Abs(c.TipY - s.Path), Sim.PathClear - 1e-3f, $"seed {seed}, crystal at {c.X}");
                    Assert.AreEqual(c.Ceiling ? s.Top : s.Bottom, c.BaseY, 1e-4f);
                    if (i > 0) Assert.GreaterOrEqual(c.X - cs[i - 1].X, Sim.MinSpacing - 1e-3f);
                }
            }
        }

        [Test]
        public void ItGetsFasterAndNarrower_ToACap_AndForgetsTheCaveBehind()
        {
            Assert.AreEqual(Sim.StartSpeed, new Sim(1).Speed);
            Assert.AreEqual(Sim.StartHeight, Sim.HeightAt(Sim.CalmStart));
            Assert.AreEqual(Sim.MinHeight, Sim.HeightAt(1000f));
            float atMax = (Sim.MaxSpeed - Sim.StartSpeed) / Sim.SpeedUp;
            Assert.That(atMax, Is.InRange(100f, 160f), "top speed after a while");
            var sim = new Sim(3);
            var bot = new Bot();
            Fly(sim, 60f, () => bot.Tick(sim));
            Assert.AreEqual(Sim.MaxSpeed, sim.Speed);
            Assert.Less(sim.Samples.Count, 60, "old cave is dropped");
            Assert.IsTrue(sim.Crystals.All(c => c.X > sim.GliderWorldX - Sim.Behind - Style.WorldWidth));
        }

        [Test]
        public void Scoring_1Every2Units_AndGemsAre5()
        {
            var sim = new Sim(2);
            var bot = new Bot();
            int gems = 0;
            for (float t = 0; t < 30f && !sim.Crashed; t += Dt)
            {
                sim.Hold(bot.Tick(sim));
                if ((sim.Step(Dt) & CaveEvent.Gem) != 0) gems++;
                Assert.AreEqual(Mathf.FloorToInt(sim.Distance / Sim.UnitsPerPoint) + gems * Sim.GemPoints, sim.Score);
            }
            Assert.IsFalse(sim.Crashed);
            Assert.Greater(gems, 5, "a bot on the path flies through the gems on it");
            Assert.AreEqual(gems, sim.GemsTaken);
        }

        [Test]
        public void ACrashEndsTheRun_AndNothingMovesAfter()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                // Hug the floor: sooner or later a crystal grows from it.
                Fly(sim, 60f, () => false);
                Assert.IsTrue(sim.Crashed, $"seed {seed}");
                Assert.GreaterOrEqual(sim.Distance, Sim.CrystalsFrom + Sim.GliderX);
                float d = sim.Distance, y = sim.Y;
                Assert.AreEqual(CaveEvent.None, sim.Step(1f));
                Assert.AreEqual(d, sim.Distance);
                Assert.AreEqual(y, sim.Y);
            }
        }

        [Test]
        public void APlayerWithAHumanLag_FliesPastTopSpeedAndTheNarrowestCave_OnEverySeed()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var bot = new Bot();
                float goal = Sim.HardestAt + 250f;
                for (float t = 0; t < 300f && !sim.Crashed && sim.Distance < goal; t += Dt)
                {
                    sim.Hold(bot.Tick(sim));
                    sim.Step(Dt);
                }
                Assert.IsFalse(sim.Crashed, $"seed {seed}: crashed at {sim.Distance:0.0} ({sim.CrashPoint}), path {sim.PathAt(sim.GliderWorldX):0.00}, y {sim.Y:0.00}");
                Assert.GreaterOrEqual(sim.Distance, goal);
            }
        }
    }
}
