using AceBarcade.Games.PaddleRally;
using NUnit.Framework;
using UnityEngine;

namespace AceBarcade.Tests
{
    public class RallySimTests
    {
        const float Dt = 1f / 60f;

        static RallyEvent Run(RallySim sim, float seconds)
        {
            var all = RallyEvent.None;
            for (float t = 0; t < seconds && !sim.Lost; t += Dt) all |= sim.Step(Dt);
            return all;
        }

        [Test]
        public void BeforeServe_BallRestsOnPaddle_AndFollowsIt()
        {
            var sim = new RallySim();
            sim.SetPaddle(2f);
            sim.Step(Dt);
            Assert.AreEqual(2f, sim.Ball.x, 1e-4f);
            Assert.Greater(sim.Ball.y, RallySim.PaddleY);
            Assert.IsFalse(sim.Lost);
        }

        [Test]
        public void Serve_LaunchesUpwardAtStartSpeed()
        {
            var sim = new RallySim();
            sim.Serve();
            Assert.Greater(sim.Velocity.y, 0f);
            Assert.AreEqual(RallySim.StartSpeed, sim.Speed, 1e-3f);
        }

        [Test]
        public void Paddle_ClampsInsideTheWalls()
        {
            var sim = new RallySim();
            sim.SetPaddle(100f);
            Assert.AreEqual(RallySim.Right - RallySim.PaddleHalfWidth, sim.PaddleX, 1e-4f);
            sim.SetPaddle(-100f);
            Assert.AreEqual(RallySim.Left + RallySim.PaddleHalfWidth, sim.PaddleX, 1e-4f);
        }

        [Test]
        public void Walls_BounceTheBall()
        {
            var sim = new RallySim();
            sim.Place(new Vector2(RallySim.Right - 0.5f, 0f), new Vector2(8f, 0f));
            var e = Run(sim, 0.2f);
            Assert.IsTrue((e & RallyEvent.Wall) != 0);
            Assert.Less(sim.Velocity.x, 0f);

            sim.Place(new Vector2(0f, RallySim.Top - 0.5f), new Vector2(0f, 8f));
            Run(sim, 0.2f);
            Assert.Less(sim.Velocity.y, 0f);
        }

        [Test]
        public void CenterHit_ReturnsStraightUp_ScoresAndSpeedsUp()
        {
            var sim = new RallySim();
            sim.Place(new Vector2(0f, -3f), new Vector2(0f, -RallySim.StartSpeed));
            var e = Run(sim, 0.6f);
            Assert.IsTrue((e & RallyEvent.Paddle) != 0);
            Assert.AreEqual(1, sim.Returns);
            Assert.AreEqual(RallySim.ReturnPoints, sim.Score);
            Assert.Greater(sim.Velocity.y, 0f);
            Assert.AreEqual(0f, sim.Velocity.x, 0.05f);
            Assert.Greater(sim.Speed, RallySim.StartSpeed);
        }

        [Test]
        public void EdgeHit_SendsTheBallOutward()
        {
            var sim = new RallySim();
            sim.Place(new Vector2(0.9f, -3f), new Vector2(0f, -8f));
            Run(sim, 0.6f);
            Assert.AreEqual(1, sim.Returns);
            Assert.Greater(sim.Velocity.x, 0f, "a hit on the right edge goes right");

            sim = new RallySim();
            sim.Place(new Vector2(-0.9f, -3f), new Vector2(0f, -8f));
            Run(sim, 0.6f);
            Assert.Less(sim.Velocity.x, 0f, "a hit on the left edge goes left");
        }

        [Test]
        public void Miss_LosesTheBall()
        {
            var sim = new RallySim();
            sim.SetPaddle(-3f);
            sim.Place(new Vector2(2.5f, -3f), new Vector2(0f, -8f));
            var e = Run(sim, 2f);
            Assert.IsTrue((e & RallyEvent.Lost) != 0);
            Assert.IsTrue(sim.Lost);
            Assert.AreEqual(0, sim.Returns);
        }

        [Test]
        public void FastBall_InOneLongFrame_StillHitsThePaddle()
        {
            var sim = new RallySim();
            sim.Place(new Vector2(0f, -3.5f), new Vector2(0f, -RallySim.MaxSpeed));
            var e = sim.Step(0.2f); // a 200 ms hitch: the ball moves 3 units
            Assert.IsTrue((e & RallyEvent.Paddle) != 0);
            Assert.IsFalse(sim.Lost);
        }

        [Test]
        public void Speed_NeverPassesTheMax()
        {
            var sim = new RallySim();
            for (int i = 0; i < 200; i++)
            {
                sim.Place(new Vector2(0f, -4f), new Vector2(0f, -RallySim.StartSpeed));
                Run(sim, 0.3f);
            }
            Assert.AreEqual(200, sim.Returns);
            Assert.LessOrEqual(sim.Speed, RallySim.MaxSpeed + 1e-3f);
        }

        [Test]
        public void Gem_AppearsEveryFewReturns_AndPaysBonus()
        {
            var sim = new RallySim(seed: 3);
            for (int i = 0; i < RallySim.GemEvery; i++)
            {
                Assert.IsFalse(sim.Gem.HasValue, $"no gem before return {RallySim.GemEvery}");
                sim.Place(new Vector2(0f, -4f), new Vector2(0f, -8f));
                Run(sim, 0.3f);
            }
            Assert.IsTrue(sim.Gem.HasValue);
            var gem = sim.Gem.Value;
            Assert.That(gem.x, Is.InRange(RallySim.Left, RallySim.Right));
            Assert.That(gem.y, Is.InRange(0f, RallySim.Top));

            int before = sim.Score;
            sim.Place(gem + Vector2.down * 1f, new Vector2(0f, 8f));
            var e = Run(sim, 0.3f);
            Assert.IsTrue((e & RallyEvent.Gem) != 0);
            Assert.IsFalse(sim.Gem.HasValue);
            Assert.AreEqual(before + RallySim.GemPoints, sim.Score);
        }
    }
}
