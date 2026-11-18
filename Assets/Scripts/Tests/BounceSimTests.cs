using System.Collections.Generic;
using System.Linq;
using AceBarcade.Games.BounceUp;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.BounceUp.BounceSim;

namespace AceBarcade.Tests
{
    public class BounceSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        /// <summary>Seconds until the bouncer, falling, has its feet at this height (or -1 if it won't get that high).</summary>
        static float TimeToFall(Sim sim, float top)
        {
            float to = top + Sim.FootY;
            float disc = sim.Vy * sim.Vy + 2f * Sim.Gravity * (sim.Y - to);
            if (disc < 0f) return -1f;
            float t = (sim.Vy + Mathf.Sqrt(disc)) / Sim.Gravity;
            return t > 0f ? t : -1f;
        }

        /// <summary>
        /// A player with human limits: it sees everything, but what it does lands 0.15 s after it decides.
        /// It heads for the next pad up the path from the one it last bounced on, and if it can't get
        /// there this bounce, it comes back down on the pad it left and tries again.
        /// </summary>
        sealed class Bot
        {
            const float Lag = 0.15f;
            readonly Queue<(float at, int dir)> _plan = new Queue<(float, int)>();
            public readonly List<string> Trace = new List<string>();
            float _time;
            int _dir;

            public int Tick(Sim sim)
            {
                _time += Dt;
                _plan.Enqueue((_time + Lag, Decide(sim)));
                while (_plan.Count > 0 && _plan.Peek().at <= _time + 1e-4f) _dir = _plan.Dequeue().dir;
                return _dir;
            }

            int Decide(Sim sim)
            {
                var from = sim.LastPad;
                BouncePad up = null;
                foreach (var p in sim.Pads)
                    if (p.IsPath && !p.Broken && p.Y > from.Y + 0.01f && (up == null || p.Y < up.Y)) up = p;

                var target = up;
                float t = up != null ? TimeToFall(sim, up.Top) : -1f;
                bool canStay = from.Kind != PadKind.Ground && !from.Broken;
                if ((up == null || t < 0f || !Reachable(sim, up, t)) && canStay)
                {
                    target = from;
                    t = TimeToFall(sim, from.Top);
                }
                if (target == null || t < 0f) return 0;

                float aim = target.XAt(sim.Time + t);
                if (Trace.Count > 60) Trace.RemoveAt(0);
                Trace.Add($"{_time:0.00} at ({sim.X:0.00}, {sim.Y:0.00}) v ({sim.Vx:0.0}, {sim.Vy:0.0}) target pad {target.Index} {(target == from ? "(stay)" : "")} aim {aim:0.00} in {t:0.00}s");
                return SteerTo(sim, aim);
            }

            static bool Reachable(Sim sim, BouncePad pad, float t)
            {
                float delta = Mathf.Abs(Sim.WrapDelta(pad.XAt(sim.Time + t) - sim.X));
                float run = Mathf.Max(0f, t - Lag - Sim.MaxSide / Sim.SideAccel / 2f);
                return delta <= Sim.PadHalfWidth + Sim.FootReach - 0.2f + Sim.MaxSide * run;
            }

            static int SteerTo(Sim sim, float aim)
            {
                float delta = Sim.WrapDelta(aim - sim.X);
                float v = sim.Vx;
                // Where it would end up if it let go now, after its lag.
                float drift = v * Lag + Mathf.Sign(v) * v * v / (2f * Sim.SideDecel);
                float rest = delta - drift;
                if (Mathf.Abs(rest) < 0.12f) return 0;
                return rest > 0f ? 1 : -1;
            }
        }

        /// <summary>Runs the bot until <paramref name="until"/> or the run ends.</summary>
        static Bot Climb(Sim sim, float seconds, System.Func<bool> until)
        {
            var bot = new Bot();
            for (float t = 0; t < seconds && !sim.Over && !until(); t += Dt)
            {
                sim.Steer(bot.Tick(sim));
                sim.Step(Dt);
            }
            return bot;
        }

        static string Describe(Sim sim, Bot bot) =>
            $"{(sim.Crashed ? "crashed" : "fell")} at height {sim.HeightPoints}, ({sim.X:0.00}, {sim.Y:0.00}), last pad {sim.LastPad.Index} " +
            $"({sim.LastPad.Kind} at {sim.LastPad.X:0.00}, {sim.LastPad.Y:0.00})\n" +
            string.Join("\n", sim.Pads.Where(p => p.Y > sim.LastPad.Y - 3f && p.Y < sim.LastPad.Y + 4f)
                .Select(p => $"  pad {p.Index} {p.Kind}{(p.IsPath ? " path" : "")}{(p.Broken ? " broken" : "")} x {p.BaseX:0.00}±{p.Amp:0.00} y {p.Y:0.00}")) + "\n" +
            string.Join("\n", sim.Burrs.Select(b => $"  burr ({b.X:0.00}, {b.Y:0.00})")) + "\n" +
            string.Join("\n", bot.Trace);

        /// <summary>Every pad made up to a height, path pads in order.</summary>
        static List<BouncePad> Plan(int seed, float upTo, out Sim sim)
        {
            sim = new Sim(seed);
            sim.PlanAhead(upTo);
            return sim.Pads.ToList();
        }

        // --- The bouncer -----------------------------------------------------------------------

        [Test]
        public void Start_StandsOnTheGround_AndBouncesOnTheFirstStep()
        {
            var sim = new Sim(1);
            Assert.AreEqual(Sim.StartY, sim.Y);
            Assert.AreEqual(0f, sim.X);
            Assert.AreEqual(PadKind.Ground, sim.LastPad.Kind);
            Assert.AreEqual(0, sim.Score);
            var events = sim.Step(Dt);
            Assert.IsTrue((events & BounceEvent.Bounced) != 0);
            Assert.AreEqual(Sim.BounceSpeed, sim.Vy);
            Assert.AreEqual(Sim.StartY, sim.Y, 1e-4f);
        }

        [Test]
        public void EveryBounce_RisesTheSameHeight_AndTheGroundCatchesItWhileItsInView()
        {
            var sim = new Sim(1);
            sim.Step(Dt);
            float top = sim.Y;
            while (sim.Vy > 0f) { sim.Step(Dt); top = Mathf.Max(top, sim.Y); }
            Assert.AreEqual(Sim.Apex(Sim.BounceSpeed), top - Sim.StartY, 0.1f);
            Assert.AreEqual(3f, Sim.Apex(Sim.BounceSpeed), 0.05f, "a bounce is about 3 units");

            // Left alone, it bounces in place for ever, on the ground or the first pad it meets.
            foreach (int seed in Seeds)
            {
                sim = new Sim(seed);
                int bounces = 0;
                for (int i = 0; i < 60 * 10; i++)
                    if ((sim.Step(Dt) & BounceEvent.Bounced) != 0) bounces++;
                Assert.IsFalse(sim.Over, $"seed {seed}");
                Assert.GreaterOrEqual(bounces, 9, $"seed {seed}");
            }
        }

        [Test]
        public void Holding_SteersUpToTopSpeed_LettingGoSlowsIt_AndTheSidesWrap()
        {
            var sim = new Sim(1);
            sim.Steer(1);
            for (int i = 0; i < 30; i++) sim.Step(Dt);
            Assert.AreEqual(Sim.MaxSide, sim.Vx, 1e-3f, "full speed in half a second");
            sim.Steer(0);
            for (int i = 0; i < 30; i++) sim.Step(Dt);
            Assert.AreEqual(0f, sim.Vx, 1e-3f, "it stops when you let go");

            sim = new Sim(1);
            sim.PlaceForTests(Sim.HalfWidth - 0.05f, Sim.StartY, Sim.MaxSide, 0f);
            sim.Steer(1);
            sim.Step(Dt);
            Assert.Less(sim.X, -Sim.HalfWidth + 0.2f, "out the right side, in at the left");
            sim.Steer(-1);
            for (int i = 0; i < 6; i++) sim.Step(Dt);
            Assert.Less(sim.Vx, Sim.MaxSide, "turning round brakes hard");
        }

        [Test]
        public void Pads_AreOneWay_RiseThroughThem_LandOnTop()
        {
            var sim = new Sim(1);
            var pad = sim.Pads.Where(p => p.IsPath && p.Kind == PadKind.Plain).OrderBy(p => p.Y).First();
            sim.PlaceForTests(pad.X, pad.Top - 0.8f, 0f, 9f);
            var events = BounceEvent.None;
            for (int i = 0; i < 30 && sim.Vy > 0f; i++) events |= sim.Step(Dt);
            Assert.AreEqual(BounceEvent.None, events & BounceEvent.Bounced, "rises through from below");
            Assert.Greater(sim.Y - Sim.FootY, pad.Top);
            for (int i = 0; i < 60 && sim.LastPad != pad; i++) events |= sim.Step(Dt);
            Assert.AreSame(pad, sim.LastPad, "lands on it coming down");
            Assert.AreEqual(Sim.BounceSpeed, sim.Vy);

            // Its feet land within reach of its ends, not beyond.
            foreach (float past in new[] { 0.05f, -0.05f })
            {
                sim = new Sim(1);
                sim.PlaceForTests(pad.X + Sim.PadHalfWidth + Sim.FootReach + past, pad.Top + Sim.FootY + 0.05f, 0f, 0f);
                for (int i = 0; i < 10; i++) sim.Step(Dt);
                Assert.AreEqual(past < 0f, sim.LastPad.Index == pad.Index, past < 0f ? "just inside the reach" : "just past it");
            }
        }

        [Test]
        public void Springs_LaunchItHigher_CrumblyPadsTakeOneBounce()
        {
            var pads = Plan(5, Sim.Hardest, out var sim);
            var spring = pads.First(p => p.HasSpring);
            var crumbly = pads.First(p => p.Kind == PadKind.Crumbly);

            sim.PlaceForTests(spring.X, spring.Top + Sim.FootY + 0.05f, 0f, 0f);
            var events = BounceEvent.None;
            for (int i = 0; i < 10; i++) events |= sim.Step(Dt);
            Assert.IsTrue((events & BounceEvent.Sprang) != 0);
            Assert.Greater(Sim.Apex(Sim.SpringSpeed), 2f * Sim.Apex(Sim.BounceSpeed));

            sim = new Sim(5);
            sim.PlanAhead(Sim.Hardest);
            crumbly = sim.Pads.First(p => p.Index == crumbly.Index);
            sim.PlaceForTests(crumbly.X, crumbly.Top + Sim.FootY + 0.05f, 0f, 0f);
            events = BounceEvent.None;
            for (int i = 0; i < 10; i++) events |= sim.Step(Dt);
            Assert.IsTrue((events & BounceEvent.Crumbled) != 0);
            Assert.IsTrue((events & BounceEvent.Bounced) != 0, "it still gives a bounce");
            Assert.IsTrue(crumbly.Broken);
            sim.PlaceForTests(crumbly.X, crumbly.Top + Sim.FootY + 0.05f, 0f, 0f);
            for (int i = 0; i < 10; i++)
                if ((sim.Step(Dt) & BounceEvent.Bounced) != 0) Assert.AreNotSame(crumbly, sim.LastPad, "then it's gone");
            Assert.Less(sim.Y - Sim.FootY, crumbly.Top, "it falls through where it was");
        }

        // --- The climb -------------------------------------------------------------------------

        [Test]
        public void ThePath_StaysWithinReach_AndGetsHarderToACap()
        {
            foreach (int seed in Seeds)
            {
                var pads = Plan(seed, Sim.Hardest + 200f, out var sim);
                var path = pads.Where(p => p.IsPath).OrderBy(p => p.Y).ToList();
                Assert.AreEqual(PadKind.Ground, path[0].Kind);
                for (int i = 1; i < path.Count; i++)
                {
                    var a = path[i - 1];
                    var b = path[i];
                    float d = Sim.Difficulty(a.Top - Sim.GroundTop);
                    float dy = b.Y - a.Y;
                    string where = $"seed {seed}, path pad {i}";
                    Assert.That(dy, Is.InRange(Sim.GapMin(d) - 1e-3f, Sim.GapMax(d) + 1e-3f), where);
                    Assert.LessOrEqual(dy, Sim.MaxGap + 1e-3f, where);
                    Assert.LessOrEqual(Sim.WrapDistance(a.BaseX, b.BaseX) + a.Amp + b.Amp, Sim.MaxShift + 1e-3f, where);
                    Assert.LessOrEqual(Mathf.Abs(b.BaseX) + b.Amp, Sim.XLimit + 1e-3f, "pads stay on the screen");
                    Assert.AreNotEqual(PadKind.Crumbly, b.Kind, "the path never crumbles");
                    if (b.Kind == PadKind.Moving) Assert.GreaterOrEqual(a.Top - Sim.GroundTop, Sim.MovingFrom, where);
                    if (b.HasSpring) Assert.AreEqual(PadKind.Plain, b.Kind);
                }
                Assert.LessOrEqual(Sim.MaxGap, Sim.Apex(Sim.BounceSpeed) - 0.5f, "every gap is well under a bounce");

                foreach (var p in pads.Where(p => !p.IsPath))
                {
                    Assert.AreNotEqual(PadKind.Moving, p.Kind);
                    if (p.Kind == PadKind.Crumbly) Assert.GreaterOrEqual(p.Top - Sim.GroundTop, Sim.CrumblyFrom - Sim.MaxGap);
                }
                foreach (var g in sim.GemList)
                    Assert.IsTrue(path.Any(p => Mathf.Abs(p.BaseX - g.X) < 1e-4f && Mathf.Abs(p.Top + Sim.GemHover - g.Y) < 1e-4f), "a gem hovers over a path pad");
                Assert.AreEqual((path.Count - 1) / Sim.GemEvery, sim.GemList.Count, 1);
                foreach (var g in sim.GemList)
                foreach (var p in pads)
                {
                    bool overlaps = Mathf.Abs(g.Y - p.Y) < Sim.PadHalfHeight + Sim.GemRadius
                        && Sim.WrapDistance(g.X, p.BaseX) < p.Amp + Sim.PadHalfWidth + Sim.GemRadius;
                    Assert.IsFalse(overlaps, $"seed {seed}: pad {p.Index} overlaps the gem at ({g.X:0.00}, {g.Y:0.00})");
                }

                // It all turns up by the hardest height.
                var high = pads.Where(p => p.Top - Sim.GroundTop > Sim.Hardest).ToList();
                Assert.IsTrue(high.Any(p => p.Kind == PadKind.Moving), $"seed {seed}: moving pads");
                Assert.IsTrue(pads.Any(p => p.Kind == PadKind.Crumbly), $"seed {seed}: crumbly pads");
                Assert.IsTrue(pads.Any(p => p.HasSpring), $"seed {seed}: springs");
                Assert.GreaterOrEqual(sim.Burrs.Count, 10, $"seed {seed}: burrs");
                Assert.IsTrue(sim.Burrs.All(b => b.Y - Sim.GroundTop >= Sim.BurrFrom), "no burrs low down");
            }
        }

        [Test]
        public void Burrs_NeverHangOverAPad_OrAcrossTheWayUp()
        {
            const float touch = Sim.BodyRadius + Sim.BurrRadius;
            const float reach = Sim.PadHalfWidth + Sim.FootReach;
            int checkedBurrs = 0;
            foreach (int seed in Seeds)
            {
                var pads = Plan(seed, Sim.Hardest + 200f, out var sim);
                var path = pads.Where(p => p.IsPath).OrderBy(p => p.Y).ToList();
                foreach (var b in sim.Burrs)
                {
                    checkedBurrs++;
                    string where = $"seed {seed}, burr ({b.X:0.00}, {b.Y:0.00})";
                    // Bouncing straight up off any pad, wherever its feet are on it, never touches it.
                    foreach (var p in pads)
                    {
                        float apex = p.Top + Sim.Apex(p.HasSpring ? Sim.SpringSpeed : Sim.BounceSpeed) + Sim.FootY;
                        if (b.Y + touch < p.Top || b.Y - touch > apex) continue;
                        Assert.GreaterOrEqual(Sim.WrapDistance(b.X, p.BaseX), p.Amp + reach + touch, $"{where} over pad {p.Index}");
                    }
                    // Nor does heading the short way from one path pad to the next.
                    for (int i = 1; i < path.Count; i++)
                    {
                        var a = path[i - 1];
                        var c = path[i];
                        if (b.Y + touch < a.Top || b.Y - touch > a.Top + Sim.Apex(Sim.BounceSpeed) + Sim.FootY) continue;
                        float delta = Sim.WrapDelta(c.BaseX - a.BaseX);
                        float lo = Mathf.Min(a.BaseX - a.Amp, a.BaseX + delta - c.Amp) - reach;
                        float hi = Mathf.Max(a.BaseX + a.Amp, a.BaseX + delta + c.Amp) + reach;
                        float off = Sim.WrapDelta(b.X - (lo + hi) / 2f);
                        Assert.GreaterOrEqual(Mathf.Abs(off), (hi - lo) / 2f + touch, $"{where} between path pads {a.Index} and {c.Index}");
                    }
                }
            }
            Assert.Greater(checkedBurrs, 80);
        }

        [Test]
        public void Climbing_ScoresTheHeight_GemsScore5_AndTheViewNeverComesDown()
        {
            var sim = new Sim(3);
            var bot = new Bot();
            float cam = sim.CamY;
            int gems = 0;
            for (float t = 0; t < 120f && !sim.Over && sim.HeightPoints < 80; t += Dt)
            {
                sim.Steer(bot.Tick(sim));
                var e = sim.Step(Dt);
                if ((e & BounceEvent.Gem) != 0) gems++;
                Assert.GreaterOrEqual(sim.CamY, cam);
                cam = sim.CamY;
                Assert.AreEqual(sim.HeightPoints + sim.Gems * Sim.GemPoints, sim.Score);
            }
            Assert.IsFalse(sim.Over, Describe(sim, bot));
            Assert.AreEqual(80, sim.HeightPoints);
            Assert.AreEqual(Mathf.FloorToInt(sim.MaxY - Sim.StartY), sim.HeightPoints);
            Assert.GreaterOrEqual(gems, 2, "it bounces through a gem over every 8th path pad now and then");
            Assert.AreEqual(gems, sim.Gems);
        }

        [Test]
        public void FallingOffTheBottom_OrTouchingABurr_EndsTheRun()
        {
            // Climb a little, then steer away from every pad below.
            var sim = new Sim(2);
            Climb(sim, 60f, () => sim.HeightPoints >= 20);
            Assert.IsFalse(sim.Over);
            var events = BounceEvent.None;
            for (float t = 0; t < 20f && !sim.Over; t += Dt)
            {
                float best = 0f, bestGap = -1f;
                for (int i = 0; i < 36; i++)
                {
                    float x = -Sim.HalfWidth + i * 0.25f, gap = 99f;
                    foreach (var p in sim.Pads)
                        if (p.Top < sim.Y) gap = Mathf.Min(gap, Sim.WrapDistance(x, p.X) - p.Amp);
                    if (gap > bestGap) { bestGap = gap; best = x; }
                }
                float delta = Sim.WrapDelta(best - sim.X);
                sim.Steer(Mathf.Abs(delta) < 0.2f ? 0 : (int)Mathf.Sign(delta));
                events |= sim.Step(Dt);
            }
            Assert.IsTrue(sim.Fell);
            Assert.IsFalse(sim.Crashed);
            Assert.IsTrue((events & BounceEvent.Fell) != 0);
            Assert.Less(sim.Y, sim.CamY - Sim.FallBelow);
            float y = sim.Y;
            Assert.AreEqual(BounceEvent.None, sim.Step(1f), "nothing moves after it ends");
            Assert.AreEqual(y, sim.Y);

            sim = new Sim(2);
            sim.PlanAhead(Sim.Hardest);
            var burr = sim.Burrs.First();
            sim.PlaceForTests(burr.X + 1.5f, burr.Y, -Sim.MaxSide, 0f);
            sim.Steer(-1);
            events = BounceEvent.None;
            for (int i = 0; i < 30 && !sim.Over; i++) events |= sim.Step(Dt);
            Assert.IsTrue(sim.Crashed);
            Assert.IsTrue((events & BounceEvent.Crashed) != 0);
            var off = new Vector2(Sim.WrapDelta(sim.CrashPoint.x - burr.X), sim.CrashPoint.y - burr.Y);
            Assert.Less(off.magnitude, Sim.BurrRadius + 0.05f, "it bursts where they touch");
        }

        [Test]
        public void ABotWithHumanReactions_ClimbsPastTheHardestHeight_OnEverySeed()
        {
            int goal = Mathf.CeilToInt(Sim.Hardest) + 150;
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var bot = Climb(sim, 900f, () => sim.HeightPoints >= goal);
                Assert.IsFalse(sim.Over, $"seed {seed}: " + Describe(sim, bot));
                Assert.GreaterOrEqual(sim.HeightPoints, goal, $"seed {seed}: too slow");
            }
        }
    }
}
