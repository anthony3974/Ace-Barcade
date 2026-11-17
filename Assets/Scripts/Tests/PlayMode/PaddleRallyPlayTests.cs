using System.Collections;
using System.IO;
using AceBarcade.Games.PaddleRally;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AceBarcade.PlayTests
{
    /// <summary>
    /// Plays Paddle Rally through the real arcade: start, rally, lose, retry. With the
    /// ACE_CAPTURE_DIR environment variable set (and Unity run with graphics), it also saves
    /// phone-sized screenshots of each screen there.
    /// </summary>
    public class PaddleRallyPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("paddle-rally");
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("paddle-rally");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static PaddleRally Game => (PaddleRally)Arcade.Current;

        /// <summary>The arcade opens on the hub; start Paddle Rally from there.</summary>
        static IEnumerator StartGame()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("paddle-rally"));
            yield return Frames(2);
            Assert.IsInstanceOf<PaddleRally>(Arcade.Current);
        }

        [UnityTest]
        public IEnumerator Rally_ScoresWhileFollowed_EndsWhenMissed_AndRetries()
        {
            yield return StartGame();
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Capture("1-ready");

            // Touch to start, then keep the finger under the ball, a little off centre so it angles.
            Arcade.Input.Simulate(true, new Vector2(0f, -7f));
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);

            for (int i = 0; i < 60 * 12; i++)
            {
                var ball = Game.Sim.Ball;
                Arcade.Input.Simulate(true, new Vector2(ball.x - 0.35f * Mathf.Sign(ball.x + 0.01f), -7f));
                if (i == 60 * 6) yield return Capture("2-playing");
                yield return null;
            }
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State, "a paddle that follows the ball never misses");
            Assert.GreaterOrEqual(Game.Score, 4, "about one return every two seconds");
            Assert.AreEqual(Game.Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Pause and resume.
            Game.Pause();
            yield return Frames(5);
            Assert.AreEqual(0f, Time.timeScale);
            yield return Capture("3-paused");
            Game.Resume();
            Assert.AreEqual(1f, Time.timeScale);

            // Let go and move the finger away: the ball falls into the spikes.
            int scored = Game.Score;
            Arcade.Input.Simulate(false, new Vector2(0f, -7f));
            for (int i = 0; i < 60 * 8 && Game.State == ArcadeGame.Phase.Playing; i++)
            {
                var ball = Game.Sim.Ball;
                Arcade.Input.Simulate(true, new Vector2(ball.x > 0 ? -4f : 4f, -7f));
                yield return null;
            }
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("paddle-rally"));
            Assert.GreaterOrEqual(Game.Score, scored);
            Arcade.Input.Simulate(false, new Vector2(0f, -7f));
            yield return Frames(60);
            yield return Capture("4-game-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
        }

        [UnityTest]
        public IEnumerator BackButton_PausesWhilePlaying()
        {
            yield return StartGame();
            Arcade.Input.Simulate(true, new Vector2(0f, -7f));
            yield return Frames(2);
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Arcade.Input.Simulate(true, new Vector2(0f, -7f), back: true);
            yield return Frames(2);
            Assert.AreEqual(ArcadeGame.Phase.Paused, Game.State);
        }

        /// <summary>Saves a 1080 x 1920 screenshot when ACE_CAPTURE_DIR is set.</summary>
        internal static IEnumerator Capture(string name)
        {
            var dir = System.Environment.GetEnvironmentVariable("ACE_CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;

            var cam = Arcade.Camera.Cam;
            var rt = new RenderTexture(1080, 1920, 24);
            cam.targetTexture = rt;
            yield return null; // let the camera and the HUD lay out at phone size
            yield return null;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
