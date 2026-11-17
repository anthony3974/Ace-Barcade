using System.Collections;
using AceBarcade.Games.BrickBurst;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Brick Burst through the real arcade: break bricks, lose three balls, game over.</summary>
    public class BrickBurstPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("brick-burst");
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("brick-burst");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static BrickBurst Game => (BrickBurst)Arcade.Current;

        [UnityTest]
        public IEnumerator BreaksBricks_ThenLosesThreeBalls_AndEnds()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("brick-burst"));
            yield return Frames(2);
            Assert.IsInstanceOf<BrickBurst>(Arcade.Current);
            yield return PaddleRallyPlayTests.Capture("8-brickburst-ready");

            Arcade.Input.Simulate(true, new Vector2(0f, -7f));
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);

            int startBricks = Game.Sim.BricksLeft;
            for (int i = 0; i < 60 * 15; i++)
            {
                var ball = Game.Sim.Ball;
                Arcade.Input.Simulate(true, new Vector2(ball.x - 0.4f * Mathf.Sign(ball.x + 0.01f), -7f));
                if (i == 60 * 8) yield return PaddleRallyPlayTests.Capture("9-brickburst-playing");
                yield return null;
            }
            Assert.AreEqual(BrickSim.StartLives, Game.Sim.Lives, "a paddle that follows the ball never misses");
            Assert.Greater(Game.Score, 3, "bricks were broken");
            Assert.IsTrue(Game.Sim.BricksLeft < startBricks || Game.Sim.Level > 0);
            Assert.AreEqual(Game.Sim.Score, Game.Score);

            // Move away from the ball every time: three lost balls end the game.
            for (int i = 0; i < 60 * 30 && Game.State == ArcadeGame.Phase.Playing; i++)
            {
                Arcade.Input.Simulate(true, new Vector2(Game.Sim.Ball.x > 0 ? -4f : 4f, -7f));
                yield return null;
            }
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.AreEqual(0, Game.Sim.Lives);
            Assert.AreEqual(Game.Score, Scores.Best("brick-burst"));
            Arcade.Input.Simulate(false, new Vector2(0f, -7f));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("10-brickburst-over");
        }
    }
}
