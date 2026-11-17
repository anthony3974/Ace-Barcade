using System.Collections;
using AceBarcade.Games.PaddleDuel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Paddle Duel through the real arcade with a simulated finger.</summary>
    public class PaddleDuelPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("paddle-duel");
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("paddle-duel");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static PaddleDuel Game => (PaddleDuel)Arcade.Current;
        static DuelSim Sim => Game.Sim;
        const float FingerY = -7f;

        /// <summary>Where to put the finger so the paddle meets the ball with this offset (-1 to 1).</summary>
        static float Aim(float offset)
        {
            if (!Sim.Served || Sim.Velocity.y >= 0f) return Sim.PlayerX;
            float x = Sim.PredictX(DuelSim.PlayerY + DuelSim.PaddleHalfHeight + DuelSim.BallRadius);
            return float.IsNaN(x) ? Sim.PlayerX : x - offset * DuelSim.Reach;
        }

        [UnityTest]
        public IEnumerator SharpShots_WinPoints_MissesCostBalls_AndRetry()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("paddle-duel"));
            yield return Frames(2);
            Assert.IsInstanceOf<PaddleDuel>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return PaddleRallyPlayTests.Capture("11-paddleduel-ready");

            // Touch to start: the ball is served.
            Arcade.Input.Simulate(true, new Vector2(0f, FingerY));
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.IsTrue(Sim.Served);

            // Meet every ball, hitting sharp toward the side the computer isn't on.
            bool captured = false;
            for (int i = 0; i < 60 * 120 && !(Sim.PointsWon >= 2 && Sim.Returns >= 4); i++)
            {
                float offset = Sim.CpuX > 0f ? -0.9f : 0.9f;
                Arcade.Input.Simulate(true, new Vector2(Aim(offset), FingerY));
                if (!captured && Sim.Returns >= 2) { captured = true; yield return PaddleRallyPlayTests.Capture("12-paddleduel-playing"); }
                yield return null;
            }
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.AreEqual(DuelSim.StartLives, Sim.Lives, "a paddle that meets the ball never misses");
            Assert.GreaterOrEqual(Sim.PointsWon, 2, "sharp shots get past the computer");
            Assert.AreEqual(Sim.Returns * DuelSim.ReturnPoints + Sim.PointsWon * DuelSim.WinPoints, Game.Score);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Now stay away from the ball: three misses end the run (the ball serves itself between them).
            int scored = Game.Score;
            for (int i = 0; i < 60 * 60 && Game.State == ArcadeGame.Phase.Playing; i++)
            {
                float landing = Aim(0f);
                Arcade.Input.Simulate(true, new Vector2(landing > 0f ? -4f : 4f, FingerY));
                yield return null;
            }
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.AreEqual(0, Sim.Lives);
            Assert.IsTrue(Game.NewBest);
            Assert.GreaterOrEqual(Game.Score, scored);
            Assert.AreEqual(Game.Score, Scores.Best("paddle-duel"));
            Arcade.Input.Simulate(false, new Vector2(0f, FingerY));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("13-paddleduel-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(DuelSim.StartLives, Sim.Lives);
        }
    }
}
