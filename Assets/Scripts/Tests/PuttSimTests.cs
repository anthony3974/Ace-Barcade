using System.Diagnostics;
using AceBarcade.Games.PocketPutt;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.PocketPutt.PuttSim;

namespace AceBarcade.Tests
{
    public class PuttSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        static PuttEvent StepFor(Sim sim, float seconds)
        {
            var all = PuttEvent.None;
            for (float t = 0f; t < seconds; t += Dt) all |= sim.Step(Dt);
            return all;
        }

        /// <summary>Step until the ball stops, drops, splashes or the next hole begins.</summary>
        static PuttEvent Roll(Sim sim)
        {
            var all = PuttEvent.None;
            for (int i = 0; i < 60 * 20 && sim.State == PuttState.Rolling; i++) all |= sim.Step(Dt);
            Assert.AreNotEqual(PuttState.Rolling, sim.State, "the ball comes to an end");
            return all;
        }

        static PuttHole Open(Vector2 tee, Vector2 cup) => new PuttHole { Number = 1, Tee = tee, Cup = cup };

        [Test]
        public void StartsOnTheFirstTee()
        {
            var sim = new Sim(1);
            Assert.AreEqual(1, sim.HoleNumber);
            Assert.AreEqual(sim.Hole.Tee, sim.Ball);
            Assert.AreEqual(PuttState.Aiming, sim.State);
            Assert.AreEqual(Sim.PuttsPerHole, sim.PuttsLeft);
            Assert.AreEqual(Sim.StartLives, sim.Lives);
            Assert.AreEqual(0, sim.Score);
        }

        [Test]
        public void PullGoesTheOtherWay_ScaledAndCapped()
        {
            Assert.AreEqual(Vector2.zero, Sim.VelocityFor(new Vector2(0f, Sim.MinPull * 0.9f)));
            Assert.AreEqual(Sim.MaxSpeed / 2f, Sim.VelocityFor(new Vector2(0f, Sim.MaxPull / 2f)).y, 1e-4f);
            Assert.AreEqual(Sim.MaxSpeed, Sim.VelocityFor(new Vector2(-9f, 0f)).magnitude, 1e-4f);
            var v = new Vector2(3f, -4f);
            Assert.That(Vector2.Distance(v, Sim.VelocityFor(Sim.PullFor(v))), Is.LessThan(1e-4f));

            var sim = new Sim(2);
            Assert.IsFalse(sim.Putt(new Vector2(0.1f, 0f)), "a tap doesn't putt");
            Assert.AreEqual(0, sim.Putts);
        }

        [Test]
        public void EveryHole_CanBeHoledInOne_AndKeepsTheTeeAndCupClear([ValueSource(nameof(Seeds))] int seed)
        {
            var random = new System.Random(seed);
            var watch = Stopwatch.StartNew();
            for (int n = 1; n <= 30; n++)
            {
                var hole = Sim.Build(n, random);
                Assert.IsTrue(Sim.WouldSink(hole, hole.Tee, hole.Proof, 0f, false), $"hole {n}'s proof putt sinks it");
                foreach (var b in hole.Blocks) { Assert.GreaterOrEqual(b.Distance(hole.Tee), 1.1f); Assert.GreaterOrEqual(b.Distance(hole.Cup), 0.9f); }
                foreach (var w in hole.Water) { Assert.GreaterOrEqual(w.Distance(hole.Tee), 1.1f); Assert.GreaterOrEqual(w.Distance(hole.Cup), 0.9f); }
                foreach (var s in hole.Sand) Assert.GreaterOrEqual(s.Distance(hole.Cup), 0.9f);
                foreach (var c in hole.Bumpers) Assert.GreaterOrEqual(Vector2.Distance(c, hole.Cup), 0.9f + Sim.BumperRadius - 0.01f);
                foreach (var m in hole.Movers)
                {
                    Assert.LessOrEqual(Mathf.Abs(m.Box.Center.x) + m.Box.Half.x + m.Amp, Sim.Right + 1e-3f, "gates stay on the green");
                    Assert.Greater(Mathf.Abs(m.Box.Center.y - hole.Tee.y), 1.5f);
                }
                Assert.IsTrue(hole.Cup.x > Sim.Left && hole.Cup.x < Sim.Right && hole.Cup.y > Sim.Bottom && hole.Cup.y < Sim.Top);
            }
            Assert.Less(watch.ElapsedMilliseconds / 30f, 400f, "building a hole is quick enough not to stall the game");
        }

        [Test]
        public void HolesGetBusier()
        {
            Assert.AreEqual((1, 0, 0, 0, 0), Sim.Counts(1));
            var (blocks, bumpers, sand, water, movers) = Sim.Counts(12);
            Assert.Greater(blocks, 1);
            Assert.Greater(bumpers, 0);
            Assert.Greater(sand, 0);
            Assert.Greater(water, 0);
            Assert.Greater(movers, 0);

            int withWater = 0, withGates = 0;
            foreach (var seed in Seeds)
            {
                var hole = Sim.Build(12, new System.Random(seed));
                if (hole.Water.Count > 0) withWater++;
                if (hole.Movers.Count > 0) withGates++;
            }
            Assert.GreaterOrEqual(withWater, Seeds.Length / 2);
            Assert.GreaterOrEqual(withGates, Seeds.Length / 2);
        }

        [Test]
        public void HoleInOne_ScoresThree_ThenTheNextHoleBegins()
        {
            var sim = new Sim(3);
            Assert.IsTrue(sim.Putt(sim.Hole.Proof));
            Assert.AreEqual(PuttState.Rolling, sim.State);
            Assert.IsFalse(sim.Putt(new Vector2(0f, 1f)), "no putting while it rolls");
            var events = Roll(sim);
            Assert.IsTrue((events & PuttEvent.Sank) != 0);
            Assert.AreEqual(PuttState.Sunk, sim.State);
            Assert.AreEqual(3, sim.Score);
            Assert.AreEqual(3, sim.LastPoints);
            Assert.AreEqual(sim.Hole.Cup, sim.Ball);

            events = StepFor(sim, Sim.PauseTime + 0.1f);
            Assert.IsTrue((events & PuttEvent.NewHole) != 0);
            Assert.AreEqual(2, sim.HoleNumber);
            Assert.AreEqual(sim.Hole.Tee, sim.Ball);
            Assert.AreEqual(Sim.PuttsPerHole, sim.PuttsLeft);
            Assert.AreEqual(PuttState.Aiming, sim.State);
        }

        [Test]
        public void SinkingOnTheSecondOrThirdPutt_ScoresLess()
        {
            foreach (int putts in new[] { 2, 3 })
            {
                var sim = new Sim(4);
                sim.UseHoleForTests(Open(new Vector2(0f, -5f), new Vector2(0f, 3f)));
                for (int i = 1; i < putts; i++)
                {
                    Assert.IsTrue(sim.Putt(new Vector2(0f, 0.5f)), "a soft putt that stops short");
                    Roll(sim);
                    Assert.AreEqual(PuttState.Aiming, sim.State);
                }
                var pull = sim.SolveNow();
                Assert.IsTrue(pull.HasValue);
                Assert.IsTrue(sim.Putt(pull.Value));
                Assert.IsTrue((Roll(sim) & PuttEvent.Sank) != 0);
                Assert.AreEqual(Sim.PuttsPerHole + 1 - putts, sim.Score);
            }
        }

        [Test]
        public void ThreePuttsShort_CostsALife_AndMovesOn()
        {
            var sim = new Sim(5);
            sim.UseHoleForTests(Open(new Vector2(0f, -5f), new Vector2(0f, 3f)));
            var all = PuttEvent.None;
            for (int i = 0; i < Sim.PuttsPerHole; i++)
            {
                Assert.IsTrue(sim.Putt(new Vector2(0f, 0.5f)));
                all |= Roll(sim);
            }
            Assert.IsTrue((all & PuttEvent.Missed) != 0);
            Assert.AreEqual(PuttState.Missed, sim.State);
            Assert.IsFalse(sim.LostToWater);
            Assert.AreEqual(Sim.StartLives - 1, sim.Lives);
            Assert.AreEqual(0, sim.Score);
            Assert.IsFalse(sim.Putt(new Vector2(0f, 2f)), "no fourth putt");
            Assert.IsTrue((StepFor(sim, Sim.PauseTime + 0.1f) & PuttEvent.NewHole) != 0);
            Assert.AreEqual(2, sim.HoleNumber);
        }

        [Test]
        public void Water_LosesTheHoleAtOnce()
        {
            var sim = new Sim(6);
            var hole = Open(new Vector2(0f, -5f), new Vector2(0f, 3f));
            hole.Water.Add(new PuttBox(new Vector2(0f, -2f), new Vector2(1.5f, 0.6f)));
            sim.UseHoleForTests(hole);
            Assert.IsTrue(sim.Putt(new Vector2(0f, 1.5f)));
            var events = Roll(sim);
            Assert.IsTrue((events & PuttEvent.Splashed) != 0 && (events & PuttEvent.Missed) != 0);
            Assert.IsTrue(sim.LostToWater);
            Assert.AreEqual(1, sim.Putts, "on the first putt");
            Assert.AreEqual(Sim.StartLives - 1, sim.Lives);
            Assert.IsTrue(hole.Water[0].Contains(sim.HitPoint));
        }

        [Test]
        public void ATooFastBall_RollsOverTheCup()
        {
            var hole = Open(new Vector2(0f, -5f), new Vector2(0f, 0f));
            var fast = new Sim.BallState { Pos = new Vector2(0f, -0.05f), Vel = new Vector2(0f, Sim.SinkSpeed + 2f), Rolling = true };
            Assert.IsFalse(Sim.Move(hole, ref fast, 0f, Sim.FixedStep, true).Sunk);
            var slow = new Sim.BallState { Pos = new Vector2(0f, -0.05f), Vel = new Vector2(0f, Sim.SinkSpeed - 1f), Rolling = true };
            Assert.IsTrue(Sim.Move(hole, ref slow, 0f, Sim.FixedStep, true).Sunk);
        }

        static float RollDistance(PuttHole hole, Vector2 pull)
        {
            var b = new Sim.BallState { Pos = hole.Tee, Vel = Sim.VelocityFor(pull), Rolling = true };
            for (int i = 0; i < 4000 && b.Rolling; i++) Sim.Move(hole, ref b, 0f, Sim.FixedStep, true);
            return b.Pos.y - hole.Tee.y;
        }

        [Test]
        public void Sand_SlowsTheBallDown()
        {
            var grass = Open(new Vector2(0f, -6f), new Vector2(3.5f, 5f));
            var sandy = Open(new Vector2(0f, -6f), new Vector2(3.5f, 5f));
            sandy.Sand.Add(new PuttBox(new Vector2(0f, -3f), new Vector2(1.5f, 1.5f)));
            var pull = new Vector2(0f, Sim.MaxPull * 0.6f);
            float onGrass = RollDistance(grass, pull), throughSand = RollDistance(sandy, pull);
            Assert.Greater(onGrass, 5f);
            Assert.Less(throughSand, onGrass - 2f, "sand takes the pace off");
        }

        [Test]
        public void Bumpers_KickHarderThanWalls()
        {
            var hole = Open(new Vector2(0f, -5f), new Vector2(3.5f, 5f));
            hole.Bumpers.Add(new Vector2(0f, 0f));
            var b = new Sim.BallState { Pos = new Vector2(0f, -1f), Vel = new Vector2(0f, 6f), Rolling = true };
            var hit = new Sim.MoveResult { Bumper = -1 };
            for (int i = 0; i < 200 && hit.Bumper < 0; i++) hit = Sim.Move(hole, ref b, 0f, Sim.FixedStep, true);
            Assert.AreEqual(0, hit.Bumper);
            Assert.Less(b.Vel.y, 0f, "bounced back");
            Assert.Greater(b.Vel.magnitude, 6f, "faster than it came in");

            var block = Open(new Vector2(0f, -5f), new Vector2(3.5f, 5f));
            block.Blocks.Add(new PuttBox(new Vector2(0f, 0f), new Vector2(1f, 0.2f)));
            var w = new Sim.BallState { Pos = new Vector2(0f, -1f), Vel = new Vector2(0f, 6f), Rolling = true };
            var wall = new Sim.MoveResult();
            for (int i = 0; i < 200 && !wall.Wall; i++) wall = Sim.Move(block, ref w, 0f, Sim.FixedStep, true);
            Assert.IsTrue(wall.Wall);
            Assert.Less(w.Vel.y, 0f);
            Assert.Less(w.Vel.magnitude, 6f, "a block takes some pace off");
        }

        [Test]
        public void AGate_Slides_AndNudgesAStillBallAside()
        {
            var hole = Open(new Vector2(0f, -5f), new Vector2(3.5f, 5f));
            var gate = new PuttMover { Box = new PuttBox(new Vector2(0f, 0f), new Vector2(0.8f, 0.18f)), Amp = 3f, Speed = 2f, Phase = 0f };
            hole.Movers.Add(gate);
            Assert.AreNotEqual(gate.At(0f).Center.x, gate.At(0.5f).Center.x, "it slides");
            Assert.LessOrEqual(Mathf.Abs(gate.At(1.234f).Center.x), gate.Amp + 1e-4f);

            var still = new Sim.BallState { Pos = new Vector2(1f, 0.1f), Rolling = false };
            for (float t = 0f; t < 2f; t += Sim.FixedStep)
            {
                Sim.Move(hole, ref still, t, Sim.FixedStep, true);
                Assert.GreaterOrEqual(gate.At(t).Distance(still.Pos), Sim.BallRadius - 1e-3f, "never inside the gate");
            }
            Assert.IsFalse(still.Rolling, "nudged, not set rolling");
        }

        [Test]
        public void LosingEveryLife_EndsTheRun()
        {
            var sim = new Sim(7);
            var all = PuttEvent.None;
            for (int life = 0; life < Sim.StartLives; life++)
            {
                sim.UseHoleForTests(Open(new Vector2(0f, -5f), new Vector2(0f, 3f)));
                for (int i = 0; i < Sim.PuttsPerHole; i++)
                {
                    Assert.IsTrue(sim.Putt(new Vector2(0f, 0.4f)));
                    all |= Roll(sim);
                }
            }
            Assert.IsTrue((all & PuttEvent.Over) != 0);
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(0, sim.Lives);
            Assert.AreEqual(PuttEvent.None, sim.Step(Dt));
            Assert.IsFalse(sim.Putt(new Vector2(0f, 2f)));
        }

        [Test]
        public void APerfectPlayer_HolesInOneEveryTime([ValueSource(nameof(Seeds))] int seed)
        {
            var sim = new Sim(seed);
            for (int n = 1; n <= 20; n++)
            {
                Assert.AreEqual(n, sim.HoleNumber);
                Vector2? pull = null;
                // With gates about, wait for a moment when the way is clear.
                for (int wait = 0; wait < 40 && pull == null; wait++)
                {
                    pull = sim.SolveNow(3f);
                    if (pull == null) StepFor(sim, 0.1f);
                }
                Assert.IsTrue(pull.HasValue, $"seed {seed} hole {n}: a hole in one is there to find");
                Assert.IsTrue(sim.Putt(pull.Value));
                var events = Roll(sim);
                Assert.IsTrue((events & PuttEvent.Sank) != 0, $"seed {seed} hole {n}: it drops");
                StepFor(sim, Sim.PauseTime + 0.1f);
            }
            Assert.AreEqual(20 * 3, sim.Score);
            Assert.AreEqual(Sim.StartLives, sim.Lives);
        }

        [Test]
        public void SameSeed_SameHoles()
        {
            var a = new Sim(1234);
            var b = new Sim(1234);
            Assert.AreEqual(a.Hole.Tee, b.Hole.Tee);
            Assert.AreEqual(a.Hole.Cup, b.Hole.Cup);
            Assert.AreEqual(a.Hole.Blocks.Count, b.Hole.Blocks.Count);
            Assert.AreEqual(a.Hole.Proof, b.Hole.Proof);

            // The next hole is built in the background; it's the same hole as building it directly.
            Assert.IsTrue(a.Putt(a.Hole.Proof));
            Roll(a);
            StepFor(a, Sim.PauseTime + 0.1f);
            var direct = Sim.Build(2, Sim.RandomFor(1234, 2));
            Assert.AreEqual(2, a.HoleNumber);
            Assert.AreEqual(direct.Tee, a.Hole.Tee);
            Assert.AreEqual(direct.Cup, a.Hole.Cup);
            Assert.AreEqual(direct.Proof, a.Hole.Proof);
        }
    }
}
