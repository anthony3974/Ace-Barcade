using AceBarcade.Games.HoopFling;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.HoopFling.HoopSim;

namespace AceBarcade.Tests
{
    public class HoopSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        static HoopEvent StepFor(Sim sim, float seconds)
        {
            var all = HoopEvent.None;
            for (float t = 0f; t < seconds; t += Dt) all |= sim.Step(Dt);
            return all;
        }

        /// <summary>Step until the throw is decided (a basket or a miss).</summary>
        static HoopEvent Fly(Sim sim)
        {
            var all = HoopEvent.None;
            for (int i = 0; i < 60 * 8 && sim.State == HoopState.Flying && !sim.Over; i++) all |= sim.Step(Dt);
            Assert.AreNotEqual(HoopState.Flying, sim.State, "the throw ends");
            return all;
        }

        [Test]
        public void StartsWithTheBallWaiting()
        {
            var sim = new Sim(1);
            Assert.AreEqual(HoopState.Aiming, sim.State);
            Assert.AreEqual(Sim.BallY, sim.Ball.y);
            Assert.That(sim.Ball.x, Is.InRange(Sim.BallMinX, Sim.BallMaxX));
            Assert.That(sim.Rim.x, Is.InRange(Sim.HoopMinX, Sim.HoopMaxX));
            Assert.That(sim.Rim.y, Is.InRange(Sim.HoopMinY, Sim.HoopMaxY));
            Assert.AreEqual(Sim.StartLives, sim.Lives);
            Assert.AreEqual(0, sim.Score);
            Assert.AreEqual(HoopEvent.None, StepFor(sim, 1f), "nothing happens until it's flung");
            Assert.AreEqual(HoopState.Aiming, sim.State);
        }

        [Test]
        public void PullGoesTheOtherWay_ScaledAndCapped()
        {
            Assert.AreEqual(Vector2.zero, Sim.VelocityFor(new Vector2(0f, Sim.MinPull * 0.9f)));
            Assert.AreEqual(Sim.MaxSpeed / 2f, Sim.VelocityFor(new Vector2(0f, Sim.MaxPull / 2f)).y, 1e-4f);
            Assert.AreEqual(Sim.MaxSpeed, Sim.VelocityFor(new Vector2(5f, 5f)).magnitude, 1e-4f);
            var v = new Vector2(4f, 9f);
            Assert.That(Vector2.Distance(v, Sim.VelocityFor(Sim.PullFor(v))), Is.LessThan(1e-4f));

            var sim = new Sim(2);
            Assert.IsFalse(sim.Throw(new Vector2(0.1f, 0.1f)), "a tap doesn't fling");
            Assert.AreEqual(0, sim.Throws);

            // The aim dots follow the real arc.
            var pull = new Vector2(1f, 2f);
            var at = sim.PreviewAt(pull, 0.3f);
            Assert.IsTrue(sim.Throw(pull));
            StepFor(sim, 0.3f);
            Assert.That(Vector2.Distance(at, sim.Ball), Is.LessThan(0.15f));
        }

        [Test]
        public void EverySpotTheHoopCanReach_CanBeSwished()
        {
            var sim = new Sim(3);
            foreach (float bx in new[] { Sim.BallMinX, Sim.BallMaxX })
                foreach (float hx in new[] { Sim.HoopMinX, Sim.HoopMaxX })
                    foreach (float hy in new[] { Sim.HoopMinY, Sim.HoopMaxY })
                    {
                        sim.PlaceHoopForTests(new Vector2(hx, hy));
                        var pull = sim.Solve(new Vector2(bx, Sim.BallY), sim.Time, true, false, 0.95f);
                        Assert.IsTrue(pull.HasValue, $"ball at {bx}, hoop at ({hx}, {hy}): a swish with power to spare");
                    }
        }

        [Test]
        public void ASwish_ScoresThree_ThenTheHoopMovesAndANewBallComes()
        {
            var sim = new Sim(4);
            var rim = sim.Rim;
            var pull = sim.SolveNow();
            Assert.IsTrue(pull.HasValue);
            Assert.IsTrue(sim.Throw(pull.Value));
            Assert.IsFalse(sim.Throw(pull.Value), "one ball at a time");
            var events = Fly(sim);
            Assert.IsTrue((events & HoopEvent.Scored) != 0 && (events & HoopEvent.Swish) != 0);
            Assert.AreEqual(Sim.Basket + Sim.SwishBonus, sim.Score);
            Assert.AreEqual(1, sim.Baskets);
            Assert.AreEqual(HoopState.Pause, sim.State);
            Assert.AreEqual(rim, sim.ShownRim, "the hoop is drawn where the ball went in until the next ball");
            Assert.Greater(Vector2.Distance(rim, sim.Rim), 1.4f, "it has somewhere new to go");

            events = StepFor(sim, Sim.PauseTime + 0.05f);
            Assert.IsTrue((events & HoopEvent.NewBall) != 0);
            Assert.AreEqual(HoopState.Aiming, sim.State);
            Assert.AreEqual(sim.Rim, sim.ShownRim);
            Assert.AreEqual(Sim.BallY, sim.Ball.y);
        }

        [Test]
        public void OffTheRimOrBoard_ScoresTwo()
        {
            var sim = new Sim(5);
            Vector2? touched = null;
            for (float a = 30f; a <= 85f && touched == null; a += 0.5f)
                for (float p = 0.4f; p <= 1f && touched == null; p += 0.005f)
                {
                    var pull = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad)) * (p * Sim.MaxPull);
                    if (sim.Try(sim.Ball, pull, sim.Time) == 1) touched = pull;
                }
            Assert.IsTrue(touched.HasValue, "some throw goes in off the rim or the board");
            Assert.IsTrue(sim.Throw(touched.Value));
            var events = Fly(sim);
            Assert.IsTrue((events & HoopEvent.Scored) != 0);
            Assert.IsTrue((events & (HoopEvent.Rim | HoopEvent.Board)) != 0);
            Assert.IsTrue((events & HoopEvent.Swish) == 0);
            Assert.AreEqual(Sim.Basket, sim.Score);
        }

        [Test]
        public void TheBoard_KnocksTheBallBack()
        {
            var rim = new Vector2(1f, 1f);
            var b = new Sim.Flight { Pos = rim + new Vector2(0.2f, 0.8f), Vel = new Vector2(8f, 0f) };
            var hit = new Sim.MoveResult();
            for (int i = 0; i < 200 && !hit.Board; i++) hit = Sim.Move(ref b, rim, Sim.FixedStep);
            Assert.IsTrue(hit.Board);
            Assert.Less(b.Vel.x, 0f);
        }

        [Test]
        public void ThreeMisses_EndTheRun()
        {
            var sim = new Sim(6);
            var all = HoopEvent.None;
            for (int i = 0; i < Sim.StartLives; i++)
            {
                Assert.IsTrue(sim.Throw(new Vector2(-0.5f, 0.6f)), "a feeble lob backwards");
                all |= Fly(sim);
                Assert.AreEqual(Sim.StartLives - 1 - i, sim.Lives);
                if (!sim.Over) Assert.IsTrue((StepFor(sim, Sim.PauseTime + 0.05f) & HoopEvent.NewBall) != 0);
            }
            Assert.IsTrue((all & HoopEvent.Missed) != 0 && (all & HoopEvent.Over) != 0);
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(0, sim.Score);
            Assert.AreEqual(HoopEvent.None, sim.Step(Dt));
            Assert.IsFalse(sim.Throw(new Vector2(1f, 2f)));
        }

        [Test]
        public void TheHoop_StaysStill_ThenBobs_ThenSlides_AndStaysInRange()
        {
            Assert.AreEqual((0f, 0f, 0f, 0f), Sim.Motion(4));
            var (bob, _, slide, _) = Sim.Motion(5);
            Assert.Greater(bob, 0f);
            Assert.AreEqual(0f, slide);
            (bob, _, slide, _) = Sim.Motion(12);
            Assert.Greater(bob, 0f);
            Assert.Greater(slide, 0f);

            foreach (var seed in new[] { 0, 7, 42, 1234 })
            {
                var sim = new Sim(seed);
                for (int n = 0; n < 16; n++)
                {
                    for (float t = 0f; t < 8f; t += 0.1f)
                    {
                        var rim = sim.RimAt(t);
                        Assert.That(rim.x, Is.InRange(Sim.HoopMinX - 1e-3f, Sim.HoopMaxX + 1e-3f));
                        Assert.That(rim.y, Is.InRange(Sim.HoopMinY - 1e-3f, Sim.HoopMaxY + 1e-3f));
                    }
                    var pull = sim.SolveNow(false);
                    for (int wait = 0; wait < 40 && pull == null; wait++) { StepFor(sim, 0.05f); pull = sim.SolveNow(false); }
                    Assert.IsTrue(pull.HasValue);
                    sim.Throw(pull.Value);
                    Fly(sim);
                    StepFor(sim, Sim.PauseTime + 0.05f);
                }
                Assert.GreaterOrEqual(sim.Baskets, 14, "past where it starts to slide");
            }
        }

        [Test]
        public void APerfectPlayer_SwishesEveryTime([ValueSource(nameof(Seeds))] int seed)
        {
            var sim = new Sim(seed);
            for (int n = 0; n < 25; n++)
            {
                Vector2? pull = sim.SolveNow();
                // A moving hoop: wait for a moment a swish is on.
                for (int wait = 0; wait < 60 && pull == null; wait++) { StepFor(sim, 0.05f); pull = sim.SolveNow(); }
                Assert.IsTrue(pull.HasValue, $"seed {seed} basket {n}: a swish is there to find");
                Assert.IsTrue(sim.Throw(pull.Value));
                var events = Fly(sim);
                Assert.IsTrue((events & HoopEvent.Swish) != 0, $"seed {seed} basket {n}: swish");
                StepFor(sim, Sim.PauseTime + 0.05f);
            }
            Assert.AreEqual(25 * (Sim.Basket + Sim.SwishBonus), sim.Score);
            Assert.AreEqual(Sim.StartLives, sim.Lives);
        }

        [Test]
        public void SameSeed_SameHoops()
        {
            var a = new Sim(1234);
            var b = new Sim(1234);
            Assert.AreEqual(a.Ball, b.Ball);
            Assert.AreEqual(a.Rim, b.Rim);
        }
    }
}
