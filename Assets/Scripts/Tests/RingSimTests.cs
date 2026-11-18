using AceBarcade.Games.RingStop;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.RingStop.RingSim;

namespace AceBarcade.Tests
{
    public class RingSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        /// <summary>Runs the spark on until it's <paramref name="before"/> degrees short of the target's middle.</summary>
        static RingEvent RunTo(Sim sim, float before = 0f)
        {
            float d = sim.Target.Remaining - before;
            return d > 0f ? sim.Step(d / sim.Speed) : RingEvent.None;
        }

        /// <summary>Lets the calm after a hit or a mistake run out without moving far.</summary>
        static void WaitCalm(Sim sim)
        {
            if (sim.Calm > 0f) sim.Step(sim.Calm);
        }

        /// <summary>Hits the next target dead in the middle.</summary>
        static RingEvent HitMiddle(Sim sim)
        {
            WaitCalm(sim);
            RunTo(sim);
            return sim.Tap();
        }

        /// <summary>
        /// A player who sees the spark coming and taps for the moment it's on the target's middle,
        /// but whose taps land up to <paramref name="jitter"/> seconds early or late (or always
        /// <paramref name="bias"/> late), on whole 60 fps frames: the tap is read before the frame's step.
        /// </summary>
        static (int hits, int lost, bool red) Play(Sim sim, int seed, float jitter, float bias, int hits)
        {
            var random = new System.Random(seed);
            float time = 0f, tapAt = float.NaN;
            int lost = 0, id = -1;
            bool red = false;
            for (int frame = 0; frame < 60 * 60 * 20 && !sim.Over && sim.Hits < hits; frame++)
            {
                if (id != sim.TargetId)
                {
                    id = sim.TargetId;
                    tapAt = time + sim.TimeToTarget + bias + jitter * (float)(random.NextDouble() * 2.0 - 1.0);
                }
                var events = RingEvent.None;
                if (time >= tapAt - Dt / 2f && !float.IsNaN(tapAt))
                {
                    events |= sim.Tap();
                    tapAt = float.NaN;
                }
                events |= sim.Step(Dt);
                time += Dt;
                if ((events & RingEvent.LifeLost) != 0) lost++;
                if ((events & RingEvent.Red) != 0) red = true;
            }
            return (sim.Hits, lost, red);
        }

        // --- The start ---------------------------------------------------------------------------

        [Test]
        public void Start_TheSparkAtTheTopGoingClockwise_AndATargetAheadOfIt()
        {
            var sim = new Sim(1);
            Assert.AreEqual(Sim.StartAngle, sim.Angle);
            Assert.AreEqual(-1, sim.Dir);
            Assert.AreEqual(Sim.StartSpeed, sim.Speed);
            Assert.AreEqual(Sim.StartLives, sim.Lives);
            Assert.AreEqual(0, sim.Score);
            Assert.AreEqual(Sim.StartWidth, sim.Target.Width);
            Assert.IsFalse(sim.Target.Gem);
            Assert.IsNull(sim.RedArc, "no red arcs before 10 hits");
            Assert.IsFalse(sim.OnTarget);
            Assert.GreaterOrEqual(sim.LastGap, Sim.MinGap);
            // Its middle is where the spark will be after running Remaining degrees clockwise.
            Assert.AreEqual(0f, Mathf.DeltaAngle(sim.Target.Center, sim.Angle - sim.Target.Remaining), 1e-3f);
        }

        [Test]
        public void TheSpark_RunsRoundAtItsSpeed()
        {
            var sim = new Sim(1);
            sim.Step(0.25f);
            Assert.AreEqual(Sim.StartAngle - Sim.StartSpeed * 0.25f, sim.Angle, 1e-3f);
        }

        // --- Hits --------------------------------------------------------------------------------

        [Test]
        public void AHit_Scores_TurnsTheSparkRound_SpeedsItUp_AndPutsANarrowerTargetAhead()
        {
            var sim = new Sim(2);
            RunTo(sim, Sim.PerfectHalf + 4f);
            Assert.IsTrue(sim.OnTarget);
            int id = sim.TargetId;
            var events = sim.Tap();
            Assert.AreEqual(RingEvent.Hit | RingEvent.NewTarget, events, "on the target, not the middle");
            Assert.AreEqual(Sim.HitPoints, sim.Score);
            Assert.AreEqual(1, sim.Hits);
            Assert.AreEqual(1, sim.Dir, "it goes back the other way");
            Assert.AreEqual(Sim.StartSpeed + Sim.SpeedUp, sim.Speed);
            Assert.AreEqual(Sim.StartWidth - Sim.Narrow, sim.Target.Width, 1e-4f);
            Assert.AreNotEqual(id, sim.TargetId);
            Assert.Greater(sim.Target.Remaining, sim.Window, "the new target is ahead, not under it");
        }

        [Test]
        public void AHitInTheMiddle_IsPerfect_AndScoresOneMore()
        {
            var sim = new Sim(3);
            var events = HitMiddle(sim);
            Assert.IsTrue((events & RingEvent.Perfect) != 0);
            Assert.AreEqual(Sim.HitPoints + Sim.PerfectPoints, sim.Score);
            Assert.AreEqual(1, sim.Streak);
            HitMiddle(sim);
            Assert.AreEqual(2, sim.Streak);
            WaitCalm(sim);
            RunTo(sim, sim.Target.Half);
            sim.Tap();
            Assert.AreEqual(0, sim.Streak, "a hit off the middle ends the run of perfects");
        }

        [Test]
        public void TheTarget_CountsALittlePastItsDrawnEnds_ButNoFurther()
        {
            var sim = new Sim(4);
            RunTo(sim, sim.Target.Half + Sim.Grace + 0.5f);
            Assert.IsFalse(sim.OnTarget);
            RunTo(sim, sim.Target.Half + Sim.Grace - 0.5f);
            Assert.IsTrue(sim.OnTarget, "just before its near end");
            RunTo(sim, -(sim.Target.Half + Sim.Grace - 0.5f));
            Assert.IsTrue(sim.OnTarget, "just past its far end");
            Assert.IsTrue((sim.Tap() & RingEvent.Hit) != 0);
        }

        [Test]
        public void EverySixthTarget_HoldsAGem_WorthFive()
        {
            var sim = new Sim(5);
            for (int i = 1; i < Sim.GemEvery; i++)
            {
                Assert.IsFalse(sim.Target.Gem, $"target {i}");
                HitMiddle(sim);
            }
            Assert.IsTrue(sim.Target.Gem);
            Assert.AreEqual(sim.Width * Sim.GemWidthScale, sim.Target.Width, 1e-4f, "gem targets are narrower");
            int before = sim.Score;
            var events = HitMiddle(sim);
            Assert.IsTrue((events & RingEvent.Gem) != 0);
            Assert.AreEqual(Sim.HitPoints + Sim.PerfectPoints + Sim.GemPoints, sim.Score - before);
            Assert.AreEqual(1, sim.Gems);
            Assert.IsFalse(sim.Target.Gem);
        }

        [Test]
        public void SpeedAndWidth_ReachTheirLimitsAtTheHardest_AndStayThere()
        {
            var sim = new Sim(6);
            Assert.AreEqual(Sim.MaxSpeed, Sim.StartSpeed + Sim.SpeedUp * Sim.HardestAt, 1e-3f);
            Assert.AreEqual(Sim.MinWidth, Sim.StartWidth - Sim.Narrow * Sim.HardestAt, 1e-3f);
            while (sim.Hits < Sim.HardestAt + 10)
            {
                var events = HitMiddle(sim);
                Assert.IsTrue((events & RingEvent.Hit) != 0);
            }
            Assert.AreEqual(Sim.MaxSpeed, sim.Speed);
            Assert.AreEqual(Sim.MinWidth, sim.Width);
        }

        // --- Mistakes ----------------------------------------------------------------------------

        [Test]
        public void ATapOffTheTarget_CostsALife_AndMovesTheTarget_AndTheThirdEndsTheRun()
        {
            var sim = new Sim(7);
            for (int life = Sim.StartLives - 1; life >= 0; life--)
            {
                WaitCalm(sim);
                RunTo(sim, sim.Window + 10f);
                int id = sim.TargetId;
                var events = sim.Tap();
                Assert.IsTrue((events & RingEvent.Miss) != 0);
                Assert.IsTrue((events & RingEvent.LifeLost) != 0);
                Assert.AreEqual(life, sim.Lives);
                Assert.AreEqual(0, sim.Score);
                if (life > 0)
                {
                    Assert.AreNotEqual(id, sim.TargetId, "a fresh target");
                    Assert.IsFalse(sim.Over);
                    Assert.AreEqual(-1, sim.Dir, "a miss doesn't turn the spark round");
                }
            }
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(RingEvent.None, sim.Tap());
            Assert.AreEqual(RingEvent.None, sim.Step(1f));
        }

        [Test]
        public void RunningPastTheTarget_CostsALife_AndPutsANewOneAhead()
        {
            var sim = new Sim(8);
            var events = RunTo(sim, -(sim.Window + 1f));
            Assert.IsTrue((events & RingEvent.Late) != 0);
            Assert.IsTrue((events & RingEvent.LifeLost) != 0);
            Assert.AreEqual(Sim.StartLives - 1, sim.Lives);
            Assert.Greater(sim.Target.Remaining, sim.Window);
        }

        [Test]
        public void ADoubleTap_CantCostALife_AndTheCalmIsShorterThanTheTimeToTheNextTarget()
        {
            Assert.Less(Sim.CalmTime, Sim.ReactTime);
            var sim = new Sim(9);
            HitMiddle(sim);
            sim.Step(Dt * 5);
            Assert.AreEqual(RingEvent.None, sim.Tap(), "the second tap of a double tap is ignored");
            Assert.AreEqual(Sim.StartLives, sim.Lives);
        }

        // --- Red arcs ----------------------------------------------------------------------------

        /// <summary>Hits targets until one comes with a red arc.</summary>
        static void UntilRed(Sim sim)
        {
            for (int i = 0; i < 200 && sim.RedArc == null; i++) HitMiddle(sim);
            Assert.IsNotNull(sim.RedArc);
            WaitCalm(sim);
        }

        [Test]
        public void ATapOnARedArc_EndsTheRunAtOnce_WhateverTheLives()
        {
            var sim = new Sim(10);
            UntilRed(sim);
            Assert.GreaterOrEqual(sim.Hits, Sim.RedFrom);
            sim.Step(sim.RedArc.Remaining / sim.Speed);
            Assert.IsTrue(sim.OnRed);
            var events = sim.Tap();
            Assert.AreEqual(RingEvent.Red | RingEvent.Over, events);
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(Sim.StartLives, sim.Lives);
        }

        [Test]
        public void ARedArc_IsForgivingAtItsEnds()
        {
            var sim = new Sim(11);
            UntilRed(sim);
            float edge = sim.RedArc.Half - Sim.RedForgive;
            sim.Step((sim.RedArc.Remaining - edge - 0.5f) / sim.Speed);
            Assert.IsFalse(sim.OnRed, "just inside its drawn end");
            var events = sim.Tap();
            Assert.IsTrue((events & RingEvent.Miss) != 0, "that's only a miss");
            Assert.IsFalse(sim.Over);
        }

        [Test]
        public void EveryPlacement_IsFairByTime_AtEverySpeed_OnEverySeed()
        {
            int reds = 0, placements = 0;
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var random = new System.Random(seed);
                while (sim.Hits < Sim.HardestAt + 40 && !sim.Over)
                {
                    placements++;
                    float speed = sim.Speed;
                    float nearEdge = sim.Target.Remaining - sim.Window;
                    Assert.GreaterOrEqual(nearEdge, speed * Sim.ReactTime - 1e-3f, "time to react");
                    Assert.GreaterOrEqual(nearEdge, Sim.MinGap - 1e-3f);
                    Assert.LessOrEqual(sim.Target.Remaining + sim.Window, 360f - 30f, "it never wraps round to the spark");
                    if (sim.RedArc != null)
                    {
                        reds++;
                        var red = sim.RedArc;
                        Assert.GreaterOrEqual(red.Remaining - red.Half, speed * Sim.RedLeadTime - 1e-3f, "red is clear of the spark");
                        Assert.GreaterOrEqual(nearEdge - (red.Remaining + red.Half), speed * Sim.RedClearTime - 1e-3f,
                            "a tap that much early for the target is never on red");
                    }
                    // Hit most, miss some, to see placements after mistakes too.
                    if (random.NextDouble() < 0.1 && sim.Lives > 1)
                    {
                        WaitCalm(sim);
                        RunTo(sim, -(sim.Window + 1f));
                    }
                    else HitMiddle(sim);
                }
            }
            Assert.Greater(reds, placements / 4, "red arcs show up often once they've started");
        }

        // --- Players -----------------------------------------------------------------------------

        [Test]
        public void APlayerWithin35ms_NeverLosesALife_For150Hits_OnEverySeed()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var (hits, lost, red) = Play(sim, seed, 0.035f, 0f, 150);
                Assert.AreEqual(150, hits, $"seed {seed}");
                Assert.AreEqual(0, lost, $"seed {seed}");
                Assert.IsFalse(red);
                Assert.GreaterOrEqual(sim.Gems, 150 / Sim.GemEvery);
            }
        }

        [Test]
        public void APlayerWithin10ms_GetsMostlyPerfects_EvenAtTopSpeed()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                Play(sim, seed, 0.01f, 0f, Sim.HardestAt);
                int before = sim.Perfects;
                Play(sim, seed + 1, 0.01f, 0f, Sim.HardestAt + 60);
                Assert.Greater(sim.Perfects - before, 40, $"seed {seed}: perfects at top speed");
            }
        }

        [Test]
        public void APlayerAlways80msLate_IsFineAtFirst_ButLosesAtSpeed()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var (hits, lost, _) = Play(sim, seed, 0f, 0.08f, 500);
                Assert.IsTrue(sim.Over, $"seed {seed}");
                Assert.AreEqual(Sim.StartLives, lost);
                Assert.Greater(hits, 15, "slow enough at the start");
                Assert.Less(hits, 200);
            }
        }

        [Test]
        public void ATapUpTo180msEarlyForTheTarget_IsNeverOnRed()
        {
            int checkedReds = 0;
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                while (sim.Hits < Sim.HardestAt + 40)
                {
                    WaitCalm(sim);
                    if (sim.RedArc != null)
                    {
                        checkedReds++;
                        // Everywhere from the target's near edge back to 180 ms before its middle.
                        float early = sim.Speed * 0.18f;
                        Assert.Greater(early, sim.Window, "180 ms early is off the target");
                        Assert.Greater(sim.Target.Remaining, early);
                        sim.Step((sim.Target.Remaining - early) / sim.Speed);
                        while (sim.Target.Remaining > sim.Window)
                        {
                            Assert.IsFalse(sim.OnRed, $"seed {seed}, {sim.Target.Remaining:0.0} degrees short");
                            sim.Step(0.5f / sim.Speed);
                        }
                    }
                    Assert.IsTrue((HitMiddle(sim) & RingEvent.Hit) != 0);
                }
            }
            Assert.Greater(checkedReds, 100);
        }
    }
}
