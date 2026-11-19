using AceBarcade.Games.PailDrop;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.PailDrop.PailSim;

namespace AceBarcade.Tests
{
    public class PailSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        /// <summary>Steps until a ball is held and taps would count (or a thornball is held).</summary>
        static PailEvent WaitReady(Sim sim, float limit = 5f)
        {
            var events = PailEvent.None;
            for (float t = 0f; t < limit && !sim.Over && !sim.CanTap; t += Dt) events |= sim.Step(Dt);
            return events;
        }

        /// <summary>Steps until the ball has landed (or was never dropped).</summary>
        static PailEvent WaitLand(Sim sim)
        {
            var events = PailEvent.None;
            for (int i = 0; i < 120 && sim.Hold == HoldState.Falling; i++) events |= sim.Step(Dt);
            return events;
        }

        /// <summary>
        /// The first moment, at least <paramref name="from"/> seconds ahead, when a drop would land
        /// dead in the pail's middle (to a millisecond).
        /// </summary>
        static float BestDrop(Sim sim, float from = 0f)
        {
            float prev = sim.OffsetIfDroppedIn(from);
            for (float t = from; t < from + 4f; t += 0.001f)
            {
                float o = sim.OffsetIfDroppedIn(t);
                if (Mathf.Abs(o) < 0.005f || Mathf.Sign(o) != Mathf.Sign(prev)) return t;
                prev = o;
            }
            Assert.Fail("the pail never came under the ball");
            return 0f;
        }

        /// <summary>Waits for a plain or gem ball that can be dropped, letting any thornball go.</summary>
        static PailEvent WaitForBall(Sim sim)
        {
            var events = PailEvent.None;
            for (int i = 0; i < 10 && !sim.Over; i++)
            {
                events |= WaitReady(sim);
                if (sim.Ball != BallKind.Thorn) break;
                for (int f = 0; f < 600 && sim.Ball == BallKind.Thorn && sim.Hold == HoldState.Holding; f++) events |= sim.Step(Dt);
            }
            return events;
        }

        /// <summary>Waits for a plain or gem ball and drops it into the middle of the pail.</summary>
        static PailEvent CatchMiddle(Sim sim)
        {
            var events = WaitForBall(sim);
            float wait = BestDrop(sim);
            if (wait > 0f) events |= sim.Step(wait);
            events |= sim.Tap();
            return events | WaitLand(sim);
        }

        /// <summary>
        /// A player who sees the pail coming and taps for the moment that lands the ball in its
        /// middle, but whose taps land up to <paramref name="jitter"/> seconds early or late (plus
        /// <paramref name="bias"/>), on whole 60 fps frames: the tap is read before the frame's step.
        /// They never tap a thornball.
        /// </summary>
        static (int hits, int lost, bool thorn, int perfectsFrom40) Play(Sim sim, int seed, float jitter, float bias, int hits, float limit = 600f)
        {
            var random = new System.Random(seed * 31 + 7);
            float time = 0f;
            float? tapAt = null;
            int ball = -1, perfectsFrom40 = 0;
            bool thorn = false;
            while (!sim.Over && sim.Hits < hits && time < limit)
            {
                if (sim.BallId != ball) { ball = sim.BallId; tapAt = null; }
                if (tapAt == null && sim.CanTap && sim.Ball != BallKind.Thorn)
                    tapAt = time + BestDrop(sim) + bias + jitter * (float)(random.NextDouble() * 2.0 - 1.0);

                var events = PailEvent.None;
                if (tapAt != null && time + Dt / 2f >= tapAt.Value && sim.CanTap)
                {
                    events |= sim.Tap();
                    tapAt = null;
                }
                int before = sim.Perfects;
                events |= sim.Step(Dt);
                if (sim.Hits > 40 && sim.Perfects > before) perfectsFrom40++;
                if ((events & PailEvent.Thorn) != 0) thorn = true;
                time += Dt;
            }
            return (sim.Hits, Sim.StartLives - sim.Lives, thorn, perfectsFrom40);
        }

        [Test]
        public void Drop_IntoTheMiddle_IsAPerfectCatch_AndANewBallComes()
        {
            var sim = new Sim(1);
            int id = sim.BallId;
            var events = CatchMiddle(sim);
            Assert.IsTrue((events & PailEvent.Dropped) != 0);
            Assert.IsTrue((events & PailEvent.Catch) != 0);
            Assert.IsTrue((events & PailEvent.Perfect) != 0);
            Assert.AreEqual(Sim.CatchPoints + Sim.PerfectPoints, sim.Score);
            Assert.AreEqual(1, sim.Hits);
            Assert.AreEqual(Sim.StartLives, sim.Lives);
            Assert.AreEqual(HoldState.Reloading, sim.Hold);

            events = sim.Step(Sim.ReloadTime + Dt);
            Assert.IsTrue((events & PailEvent.NewBall) != 0);
            Assert.AreNotEqual(id, sim.BallId);
            Assert.IsFalse(sim.CanTap, "a new ball ignores taps for a moment");
            Assert.AreEqual(PailEvent.None, sim.Tap());
            sim.Step(Sim.CalmTime + Dt);
            Assert.IsTrue(sim.CanTap);
        }

        [Test]
        public void Ball_FallsStraightDown_AndAlwaysTakesTheFallTime()
        {
            var sim = new Sim(2);
            for (int i = 0; i < 20; i++) CatchMiddle(sim);
            Assert.Greater(sim.SwayAmp, 0f, "the critter drifts by now");

            WaitForBall(sim);
            sim.Tap();
            float x = sim.BallX, t = 0f;
            while (sim.Hold == HoldState.Falling)
            {
                Assert.AreEqual(x, sim.BallX, 1e-5f, "the ball doesn't drift as it falls");
                Assert.AreEqual(PailEvent.None, sim.Tap(), "taps do nothing while it falls");
                sim.Step(0.001f);
                t += 0.001f;
            }
            Assert.AreEqual(Sim.FallTime, t, 0.002f);
        }

        [Test]
        public void Drop_WellAwayFromThePail_CostsALife_AndThreeEndTheRun()
        {
            var sim = new Sim(3);
            for (int life = Sim.StartLives - 1; life >= 0; life--)
            {
                WaitForBall(sim);
                for (int f = 0; f < 600 && Mathf.Abs(sim.OffsetIfDroppedIn(0f)) < sim.CatchHalf + 1f; f++) sim.Step(Dt);
                Assert.IsTrue((sim.Tap() & PailEvent.Dropped) != 0);
                var events = WaitLand(sim);
                Assert.IsTrue((events & PailEvent.Miss) != 0);
                Assert.IsTrue((events & PailEvent.LifeLost) != 0);
                Assert.AreEqual(life, sim.Lives);
                Assert.AreEqual(life == 0, sim.Over);
                Assert.AreEqual(life == 0, (events & PailEvent.Over) != 0);
            }
            Assert.AreEqual(0, sim.Score);
            Assert.AreEqual(PailEvent.None, sim.Step(1f), "nothing happens once it's over");
        }

        [Test]
        public void EverySixthBall_IsAGem_WorthFiveMore()
        {
            var sim = new Sim(4);
            for (int i = 1; i <= 12; i++)
            {
                WaitReady(sim);
                Assert.AreEqual(i % Sim.GemEvery == 0, sim.Ball == BallKind.Gem, $"ball {i}");
                int score = sim.Score;
                var events = CatchMiddle(sim);
                Assert.AreEqual(i % Sim.GemEvery == 0, (events & PailEvent.Gem) != 0);
                int expected = Sim.CatchPoints + Sim.PerfectPoints + (i % Sim.GemEvery == 0 ? Sim.GemPoints : 0);
                Assert.AreEqual(expected, sim.Score - score);
            }
            Assert.AreEqual(2, sim.Gems);
        }

        [Test]
        public void Thornballs_OnlyFromTenCatches_NeverTwiceRunning_AndWaitingTossesThemAway()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                int thorns = 0;
                bool lastThorn = false;
                while (sim.Hits < 60 && !sim.Over)
                {
                    WaitReady(sim);
                    bool isThorn = sim.Ball == BallKind.Thorn;
                    if (isThorn)
                    {
                        Assert.GreaterOrEqual(sim.Hits, Sim.ThornFrom, $"seed {seed}");
                        Assert.IsFalse(lastThorn, $"seed {seed}: two thornballs running");
                        thorns++;
                        int lives = sim.Lives, score = sim.Score;
                        var events = PailEvent.None;
                        for (int f = 0; f < 600 && sim.Hold == HoldState.Holding; f++) events |= sim.Step(Dt);
                        Assert.IsTrue((events & PailEvent.ThornGone) != 0);
                        Assert.AreEqual(lives, sim.Lives, "waiting out a thornball costs nothing");
                        Assert.AreEqual(score, sim.Score);
                    }
                    else CatchMiddle(sim);
                    lastThorn = isThorn;
                }
                Assert.Greater(thorns, 5, $"seed {seed}");
            }
        }

        [Test]
        public void TappingAThornball_EndsTheRun_LivesOrNot()
        {
            var sim = new Sim(5);
            while (!sim.Over)
            {
                WaitReady(sim);
                if (sim.Ball == BallKind.Thorn) break;
                CatchMiddle(sim);
            }
            Assert.AreEqual(BallKind.Thorn, sim.Ball);
            Assert.AreEqual(Sim.StartLives, sim.Lives);
            var events = sim.Tap();
            Assert.AreEqual(PailEvent.Thorn | PailEvent.Over, events);
            Assert.IsTrue(sim.Over);
        }

        [Test]
        public void ThePail_SpeedsUpAndNarrows_ToAFloor()
        {
            var sim = new Sim(6);
            Assert.AreEqual(Sim.StartSpeed, sim.Speed);
            Assert.AreEqual(Sim.StartWidth, sim.Width);
            float speed = sim.Speed, width = sim.Width;
            while (sim.Hits < 60)
            {
                CatchMiddle(sim);
                Assert.GreaterOrEqual(sim.Speed, speed);
                Assert.LessOrEqual(sim.Width, width);
                speed = sim.Speed;
                width = sim.Width;
            }
            Assert.AreEqual(Sim.MaxSpeed, sim.Speed, 1e-4f);
            Assert.AreEqual(Sim.MinWidth, sim.Width, 1e-4f);
            Assert.AreEqual(Sim.MaxSpeed, Sim.StartSpeed + Sim.SpeedUp * Sim.HardestAt, 0.01f, "top speed at the hardest");
            Assert.AreEqual(Sim.MinWidth, Sim.StartWidth - Sim.Narrow * Sim.HardestAt, 0.01f, "narrowest at the hardest");
        }

        [Test]
        public void ThePail_StaysInTheYard_AndAlwaysPassesUnderTheBall()
        {
            // At its narrowest the pail still reaches past the critter's widest drift.
            Assert.Greater(Sim.YardHalf - Sim.MinWidth / 2f, Sim.SwayMax + Sim.MinWidth / 2f);
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                for (int i = 0; i < 50 && !sim.Over; i++)
                {
                    CatchMiddle(sim);
                    for (int f = 0; f < 30; f++)
                    {
                        sim.Step(Dt);
                        Assert.LessOrEqual(Mathf.Abs(sim.BucketX) + sim.Width / 2f, Sim.YardHalf + 1e-4f);
                        Assert.LessOrEqual(Mathf.Abs(sim.HookX), Sim.SwayMax + 1e-4f);
                    }
                }
            }
        }

        [Test]
        public void ASteadyPlayer_NeverLosesALife_PastTheHardest()
        {
            // Taps land within 35 ms of the right moment, and never on a thornball.
            foreach (int seed in Seeds)
            {
                var (hits, lost, thorn, _) = Play(new Sim(seed), seed, 0.035f, 0f, 150);
                Assert.AreEqual(150, hits, $"seed {seed}");
                Assert.AreEqual(0, lost, $"seed {seed}");
                Assert.IsFalse(thorn, $"seed {seed}");
            }
        }

        [Test]
        public void ASharpPlayer_GetsMostlyPerfects_AtTopSpeed()
        {
            foreach (int seed in Seeds)
            {
                var (hits, lost, _, perfects) = Play(new Sim(seed), seed, 0.01f, 0f, 100);
                Assert.AreEqual(100, hits, $"seed {seed}");
                Assert.AreEqual(0, lost, $"seed {seed}");
                Assert.Greater(perfects, 40, $"seed {seed}: {perfects} of 60");
            }
        }

        [Test]
        public void ALatePlayer_DoesFineAtFirst_ButLosesAtSpeed()
        {
            int lostLate = 0;
            foreach (int seed in Seeds)
            {
                var early = Play(new Sim(seed), seed, 0f, 0.15f, 8);
                Assert.AreEqual(0, early.lost, $"seed {seed}: 150 ms late is fine at the start");

                var run = Play(new Sim(seed), seed, 0f, 0.15f, 150);
                lostLate += run.lost;
            }
            Assert.Greater(lostLate, Seeds.Length, "150 ms late misses at speed");
        }
    }
}
