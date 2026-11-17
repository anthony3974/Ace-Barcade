using System;
using AceBarcade.Games.PaddleDuel;
using NUnit.Framework;
using UnityEngine;

namespace AceBarcade.Tests
{
    public class DuelSimTests
    {
        const float Dt = 1f / 60f;
        const float Sharp = 0.9f;
        static readonly int[] SkillLevels = { 0, DuelSim.PointsToFullSkill / 2, DuelSim.PointsToFullSkill };

        static DuelEvent Run(DuelSim sim, float seconds)
        {
            var all = DuelEvent.None;
            for (float t = 0; t < seconds && !sim.Over; t += Dt) all |= sim.Step(Dt);
            return all;
        }

        /// <summary>A player who always gets to the ball and hits it with the given offset (-1 to 1).</summary>
        static void Follow(DuelSim sim, Func<DuelSim, float> offset)
        {
            if (!sim.Served || sim.Velocity.y >= 0f) return;
            float x = sim.PredictX(DuelSim.PlayerY + DuelSim.PaddleHalfHeight + DuelSim.BallRadius);
            if (!float.IsNaN(x)) sim.SetPlayer(x - offset(sim) * DuelSim.Reach);
        }

        /// <summary>Hit sharp, toward the side the computer isn't on.</summary>
        static float SharpAway(DuelSim s) => s.CpuX > 0f ? -Sharp : Sharp;

        /// <summary>
        /// Plays points at a fixed skill level with a perfect player. Returns the share of balls
        /// sent the computer's way (serves and returns) that it failed to return.
        /// </summary>
        static float CpuMissRate(int pointsWon, Func<DuelSim, float> offset, int points = 40, int seed = 1)
        {
            var sim = new DuelSim(seed);
            int missed = 0, returned = 0;
            for (int p = 0; p < points; p++)
            {
                sim.SetPointsWon(pointsWon);
                sim.Serve();
                for (float t = 0f; t < 30f; t += Dt)
                {
                    Follow(sim, offset);
                    var e = sim.Step(Dt);
                    if ((e & DuelEvent.CpuHit) != 0) returned++;
                    if ((e & DuelEvent.PointWon) != 0) { missed++; break; }
                    Assert.IsTrue((e & DuelEvent.LifeLost) == 0, "the perfect player never misses");
                }
            }
            return (float)missed / (missed + returned);
        }

        // --- The computer plays fair ----------------------------------------------------------

        [Test]
        public void SharpShots_BeatTheComputer_AtEverySkillLevel()
        {
            var rates = new float[SkillLevels.Length];
            for (int i = 0; i < SkillLevels.Length; i++)
            {
                rates[i] = CpuMissRate(SkillLevels[i], SharpAway);
                TestContext.WriteLine($"skill {SkillLevels[i]}: sharp shots get past it {rates[i]:P0}");
            }
            for (int i = 0; i < SkillLevels.Length; i++)
                Assert.GreaterOrEqual(rates[i], 0.3f, $"at {SkillLevels[i]} points won, sharp angles still win points");
        }

        [Test]
        public void StraightShots_AlwaysComeBack()
        {
            // "Straight" means hit with the middle of the paddle. A ball hugging a wall can't be
            // met square on, so a few still leave at an angle.
            var rates = new float[SkillLevels.Length];
            for (int i = 0; i < SkillLevels.Length; i++)
            {
                rates[i] = CpuMissRate(SkillLevels[i], _ => 0f, points: 10);
                TestContext.WriteLine($"skill {SkillLevels[i]}: straight shots get past it {rates[i]:P0}");
            }
            for (int i = 0; i < SkillLevels.Length; i++)
                Assert.LessOrEqual(rates[i], 0.1f, $"at {SkillLevels[i]} points won, it returns easy balls");
        }

        [Test]
        public void TheComputer_GetsSharper_AsPointsAreWon_UpToACap()
        {
            var sim = new DuelSim();
            Assert.AreEqual(0f, sim.Skill);
            Assert.AreEqual(DuelSim.CpuSpeedStart, sim.CpuSpeed, 1e-5f);
            Assert.AreEqual(DuelSim.CpuReactionStart, sim.CpuReaction, 1e-5f);
            sim.SetPointsWon(DuelSim.PointsToFullSkill / 2);
            Assert.That(sim.CpuSpeed, Is.InRange(DuelSim.CpuSpeedStart + 0.1f, DuelSim.CpuSpeedFull - 0.1f));
            sim.SetPointsWon(DuelSim.PointsToFullSkill * 3);
            Assert.AreEqual(1f, sim.Skill);
            Assert.AreEqual(DuelSim.CpuSpeedFull, sim.CpuSpeed, 1e-5f);
            Assert.AreEqual(DuelSim.CpuReactionFull, sim.CpuReaction, 1e-5f);

            // Even at full skill, a sharp ball's sideways speed beats the computer's top speed.
            float sideways = DuelSim.StartSpeed * Mathf.Sin(Sharp * DuelSim.MaxBounceDegrees * Mathf.Deg2Rad);
            Assert.Greater(sideways, DuelSim.CpuSpeedStart);
            float sidewaysFull = DuelSim.MaxServeSpeed * Mathf.Sin(Sharp * DuelSim.MaxBounceDegrees * Mathf.Deg2Rad);
            Assert.Greater(sidewaysFull, DuelSim.CpuSpeedFull);
        }

        [Test]
        public void TheComputer_NeverMovesFasterThanItsTopSpeed_AndWaitsToReact()
        {
            var sim = new DuelSim(3);
            sim.SetPlayer(-3f);
            sim.Serve();
            float last = sim.CpuX;
            for (float t = 0f; t < 20f && !sim.Over; t += Dt)
            {
                Follow(sim, SharpAway);
                sim.Step(Dt);
                Assert.LessOrEqual(Mathf.Abs(sim.CpuX - last), sim.CpuSpeed * Dt + 1e-4f, "no teleporting");
                last = sim.CpuX;
            }

            // A ball fired its way: it holds still for its reaction time first.
            var fresh = new DuelSim(4);
            fresh.SetCpu(0f);
            fresh.Place(new Vector2(3f, -2f), new Vector2(0f, 8f));
            float start = fresh.CpuX;
            for (float t = 0f; t < DuelSim.CpuReactionStart - 2f * Dt; t += Dt) fresh.Step(Dt);
            Assert.AreEqual(start, fresh.CpuX, 1e-4f, "it hasn't noticed yet");
            Run(fresh, 0.3f);
            Assert.Greater(fresh.CpuX, start + 0.5f, "then it goes for the ball");
        }

        // --- Ball and paddles -----------------------------------------------------------------

        [Test]
        public void BeforeServe_BallRestsOnThePlayersPaddle_AndFollowsIt()
        {
            var sim = new DuelSim();
            sim.SetPlayer(2f);
            sim.Step(Dt);
            Assert.AreEqual(2f, sim.Ball.x, 1e-4f);
            Assert.Greater(sim.Ball.y, DuelSim.PlayerY);
            sim.Serve();
            Assert.Greater(sim.Velocity.y, 0f);
            Assert.AreEqual(DuelSim.StartSpeed, sim.Speed, 1e-3f);
        }

        [Test]
        public void Paddles_ClampInsideTheWalls()
        {
            var sim = new DuelSim();
            sim.SetPlayer(100f);
            Assert.AreEqual(DuelSim.Right - DuelSim.PaddleHalfWidth, sim.PlayerX, 1e-4f);
            sim.SetCpu(-100f);
            Assert.AreEqual(DuelSim.Left + DuelSim.PaddleHalfWidth, sim.CpuX, 1e-4f);
        }

        [Test]
        public void WhereTheBallLands_SetsItsAngle_AndReturnsSpeedItUp()
        {
            var sim = new DuelSim();
            sim.Place(new Vector2(0.8f, DuelSim.PlayerY + 1f), new Vector2(0f, -8f));
            var e = Run(sim, 0.3f);
            Assert.IsTrue((e & DuelEvent.Paddle) != 0);
            Assert.Greater(sim.Velocity.x, 3f, "a ball off the right of the paddle goes right");
            Assert.AreEqual(8f + DuelSim.SpeedPerHit, sim.Speed, 1e-3f);
            Assert.AreEqual(DuelSim.ReturnPoints, sim.Score);
            Assert.AreEqual(1, sim.Returns);
        }

        [Test]
        public void PredictX_FollowsTheBallOffTheWalls()
        {
            var sim = new DuelSim();
            sim.Place(new Vector2(0f, 0f), new Vector2(6f, 2f));
            float predicted = sim.PredictX(DuelSim.CpuY - 1f);
            for (float t = 0f; t < 5f && sim.Ball.y < DuelSim.CpuY - 1f; t += 1f / 600f)
            {
                sim.SetCpu(DuelSim.Left); // keep the computer out of the way
                sim.Step(1f / 600f);
            }
            Assert.AreEqual(predicted, sim.Ball.x, 0.05f);
        }

        // --- Points, lives, the end -----------------------------------------------------------

        [Test]
        public void BallPastTheComputer_Scores5_AndRaisesTheNextServeSpeed()
        {
            var sim = new DuelSim();
            sim.SetCpu(DuelSim.Left);
            sim.Place(new Vector2(3f, 4f), new Vector2(0f, 10f));
            var e = Run(sim, 0.5f);
            Assert.IsTrue((e & DuelEvent.PointWon) != 0);
            Assert.AreEqual(DuelSim.WinPoints, sim.Score);
            Assert.AreEqual(1, sim.PointsWon);
            Assert.IsFalse(sim.Served, "the ball waits on the player's paddle for the next serve");
            Assert.AreEqual(DuelSim.StartSpeed + DuelSim.SpeedPerPoint, sim.ServeSpeed, 1e-4f);

            sim.SetPointsWon(100);
            Assert.AreEqual(DuelSim.MaxServeSpeed, sim.ServeSpeed, 1e-4f, "up to a cap");
        }

        [Test]
        public void MissingCostsABall_AndTheThirdEndsTheRun()
        {
            var sim = new DuelSim();
            for (int life = DuelSim.StartLives; life > 0; life--)
            {
                sim.SetPlayer(DuelSim.Right);
                sim.Place(new Vector2(-3f, -4f), new Vector2(0f, -10f));
                var e = Run(sim, 0.5f);
                Assert.IsTrue((e & DuelEvent.LifeLost) != 0);
                Assert.AreEqual(life - 1, sim.Lives);
                Assert.AreEqual(life == 1, sim.Over);
                Assert.AreEqual(life == 1, (e & DuelEvent.GameOver) != 0);
            }
            Assert.AreEqual(DuelEvent.None, sim.Step(Dt), "nothing happens after the end");
            Assert.AreEqual(0, sim.Score);
        }
    }
}
