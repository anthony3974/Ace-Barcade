using System.Linq;
using AceBarcade.Games.BrickBurst;
using NUnit.Framework;
using UnityEngine;

namespace AceBarcade.Tests
{
    public class BrickSimTests
    {
        const float Dt = 1f / 60f;

        static BrickEvent Run(BrickSim sim, float seconds)
        {
            var all = BrickEvent.None;
            for (float t = 0; t < seconds && sim.Served && !sim.Over; t += Dt) all |= sim.Step(Dt);
            return all;
        }

        [Test]
        public void Wall_FitsBetweenTheWalls_BelowTheTop_WithoutOverlaps()
        {
            var first = BrickSim.BrickCenter(0, 0);
            var last = BrickSim.BrickCenter(BrickSim.Columns - 1, BrickSim.MaxRows - 1);
            Assert.GreaterOrEqual(first.x - BrickSim.BrickWidth / 2f, BrickSim.Left);
            Assert.LessOrEqual(last.x + BrickSim.BrickWidth / 2f, BrickSim.Right);
            Assert.LessOrEqual(first.y + BrickSim.BrickHeight / 2f, BrickSim.Top - 0.5f, "room for the ball behind the wall");
            Assert.Greater(last.y - BrickSim.BrickHeight / 2f, 0f, "the wall stays in the top half");
            Assert.Greater(BrickSim.BrickCenter(1, 0).x - first.x, BrickSim.BrickWidth);
            Assert.Greater(first.y - BrickSim.BrickCenter(0, 1).y, BrickSim.BrickHeight);
        }

        [Test]
        public void FirstWall_HasFiveRows_AndAGem()
        {
            var sim = new BrickSim(5);
            var bricks = sim.AllBricks().ToList();
            Assert.AreEqual(BrickSim.Columns * 5, bricks.Count);
            Assert.AreEqual(bricks.Count, sim.BricksLeft);
            Assert.IsTrue(bricks.Any(b => sim.Bricks[b.x, b.y] == BrickKind.Gem));
            Assert.IsFalse(bricks.Any(b => sim.Bricks[b.x, b.y] == BrickKind.Tough), "no tough bricks on the first wall");
        }

        [Test]
        public void HitFromBelow_BreaksTheBrick_AndBouncesBack()
        {
            var sim = new BrickSim();
            sim.SetWall((3, 4, BrickKind.Pink), (0, 0, BrickKind.Blue));
            var c = BrickSim.BrickCenter(3, 4);
            sim.Place(c + Vector2.down * 1.5f, new Vector2(0f, 8f));
            var e = Run(sim, 0.4f);
            Assert.IsTrue((e & BrickEvent.Broke) != 0);
            Assert.AreEqual(BrickKind.None, sim.Bricks[3, 4]);
            Assert.AreEqual(BrickSim.BrickPoints, sim.Score);
            Assert.Less(sim.Velocity.y, 0f, "the ball comes back down");
            Assert.AreEqual(1, sim.BricksLeft);
        }

        [Test]
        public void HitOnTheSide_BouncesSideways()
        {
            var sim = new BrickSim();
            sim.SetWall((3, 4, BrickKind.Pink), (0, 0, BrickKind.Blue));
            var c = BrickSim.BrickCenter(3, 4);
            sim.Place(c + Vector2.left * 1.5f, new Vector2(8f, 0.5f));
            Run(sim, 0.3f);
            Assert.AreEqual(BrickKind.None, sim.Bricks[3, 4]);
            Assert.Less(sim.Velocity.x, 0f);
        }

        [Test]
        public void ToughBrick_TakesTwoHits()
        {
            var sim = new BrickSim();
            sim.SetWall((3, 4, BrickKind.Tough), (0, 0, BrickKind.Blue));
            var c = BrickSim.BrickCenter(3, 4);
            sim.Place(c + Vector2.down * 1.5f, new Vector2(0f, 8f));
            var e = Run(sim, 0.4f);
            Assert.IsTrue((e & BrickEvent.Hit) != 0);
            Assert.IsFalse((e & BrickEvent.Broke) != 0);
            Assert.AreEqual(BrickKind.Cracked, sim.Bricks[3, 4]);
            Assert.AreEqual(0, sim.Score);

            sim.Place(c + Vector2.down * 1.5f, new Vector2(0f, 8f));
            e = Run(sim, 0.4f);
            Assert.IsTrue((e & BrickEvent.Broke) != 0);
            Assert.AreEqual(BrickSim.ToughPoints, sim.Score);
        }

        [Test]
        public void GemBrick_PaysTheBonus()
        {
            var sim = new BrickSim();
            sim.SetWall((3, 4, BrickKind.Gem), (0, 0, BrickKind.Blue));
            sim.Place(BrickSim.BrickCenter(3, 4) + Vector2.down * 1.5f, new Vector2(0f, 8f));
            var e = Run(sim, 0.4f);
            Assert.IsTrue((e & BrickEvent.Gem) != 0);
            Assert.AreEqual(BrickSim.GemPoints, sim.Score);
        }

        [Test]
        public void FastBall_InOneLongFrame_DoesNotPassThroughABrick()
        {
            var sim = new BrickSim();
            sim.SetWall((3, 4, BrickKind.Pink), (0, 0, BrickKind.Blue));
            sim.Place(BrickSim.BrickCenter(3, 4) + Vector2.down * 3f, new Vector2(0f, BrickSim.MaxSpeed));
            var e = sim.Step(0.25f);
            Assert.IsTrue((e & BrickEvent.Broke) != 0);
        }

        [Test]
        public void Missing_CostsALife_AndTheBallWaitsOnThePaddle_ThreeEndTheGame()
        {
            var sim = new BrickSim();
            sim.SetPaddle(-3f);
            for (int i = 1; i <= BrickSim.StartLives; i++)
            {
                sim.Place(new Vector2(2.5f, -3f), new Vector2(0f, -8f));
                var e = Run(sim, 2f);
                Assert.IsTrue((e & BrickEvent.LifeLost) != 0);
                Assert.AreEqual(BrickSim.StartLives - i, sim.Lives);
                Assert.IsFalse(sim.Served, "the ball waits to be served");
                Assert.AreEqual(sim.PaddleX, sim.Ball.x, 1e-4f);
                Assert.AreEqual(i == BrickSim.StartLives, sim.Over);
                if (i == BrickSim.StartLives) Assert.IsTrue((e & BrickEvent.GameOver) != 0);
            }
            sim.Serve();
            Assert.IsFalse(sim.Served, "no serving after the game is over");
        }

        [Test]
        public void ClearingTheWall_BuildsAHarderOne()
        {
            var sim = new BrickSim(9);
            sim.SetWall((3, 4, BrickKind.Pink));
            sim.Place(BrickSim.BrickCenter(3, 4) + Vector2.down * 1.5f, new Vector2(0f, 8f));
            var e = Run(sim, 0.4f);
            Assert.IsTrue((e & BrickEvent.Cleared) != 0);
            Assert.AreEqual(1, sim.Level);
            Assert.AreEqual(BrickSim.BrickPoints + BrickSim.ClearBonus, sim.Score);
            Assert.IsFalse(sim.Served, "the ball waits on the paddle for the new wall");

            var bricks = sim.AllBricks().ToList();
            Assert.IsTrue(bricks.Any(b => b.y == 5), "one more row than the first wall");
            Assert.IsTrue(bricks.Any(b => sim.Bricks[b.x, b.y] == BrickKind.Tough), "tough bricks from the second wall");

            sim.Serve();
            Assert.Greater(sim.Speed, BrickSim.StartSpeed, "each wall starts a little faster");
        }

        [Test]
        public void PaddleEdge_SendsTheBallOutward()
        {
            var sim = new BrickSim();
            sim.Place(new Vector2(0.9f, -3f), new Vector2(0f, -8f));
            var e = Run(sim, 0.6f);
            Assert.IsTrue((e & BrickEvent.Paddle) != 0);
            Assert.Greater(sim.Velocity.x, 0f);
        }

        [Test]
        public void Speed_NeverPassesTheMax()
        {
            var sim = new BrickSim();
            for (int i = 0; i < 300; i++)
            {
                sim.SetWall((3, 4, BrickKind.Pink), (0, 0, BrickKind.Blue));
                sim.Place(BrickSim.BrickCenter(3, 4) + Vector2.down * 1.5f, new Vector2(0f, BrickSim.MaxSpeed));
                Run(sim, 0.15f);
            }
            Assert.LessOrEqual(sim.Speed, BrickSim.MaxSpeed + 1e-3f);
        }
    }
}
