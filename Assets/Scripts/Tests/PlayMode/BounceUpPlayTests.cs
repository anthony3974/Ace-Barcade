using System.Collections;
using AceBarcade.Games.BounceUp;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.BounceUp.BounceSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Bounce Up through the real arcade, holding the left and right halves of the screen.</summary>
    public class BounceUpPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("bounce-up");
            BounceUp.SeedForTests = 10;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            BounceUp.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("bounce-up");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static BounceUp Game => (BounceUp)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Left = new Vector2(-2.5f, -4f), Right = new Vector2(2.5f, -4f);

        /// <summary>Hold a half of the screen this frame (0 lets go).</summary>
        static void Hold(int dir)
        {
            if (dir == 0) Arcade.Input.Simulate(false, Right);
            else Arcade.Input.Simulate(true, dir < 0 ? Left : Right);
        }

        /// <summary>
        /// Which way to hold to land on the next pad up the path from the last one bounced on
        /// (or back on that one, if the next is out of reach this bounce).
        /// </summary>
        static int Toward()
        {
            var from = Sim.LastPad;
            BouncePad up = null;
            foreach (var p in Sim.Pads)
                if (p.IsPath && !p.Broken && p.Y > from.Y + 0.01f && (up == null || p.Y < up.Y)) up = p;
            if (up == null) return 0;
            float t = TimeToFall(up.Top);
            var target = up;
            bool reachable = t >= 0f && Mathf.Abs(Rules.WrapDelta(up.XAt(Sim.Time + t) - Sim.X)) <= 0.8f + Rules.MaxSide * Mathf.Max(0f, t - 0.15f);
            if (!reachable && from.Kind != PadKind.Ground && !from.Broken) { target = from; t = TimeToFall(from.Top); }
            if (t < 0f) return 0;
            float delta = Rules.WrapDelta(target.XAt(Sim.Time + t) - Sim.X);
            float v = Sim.Vx;
            float rest = delta - Mathf.Sign(v) * v * v / (2f * Rules.SideDecel);
            return Mathf.Abs(rest) < 0.12f ? 0 : (int)Mathf.Sign(rest);
        }

        static float TimeToFall(float top)
        {
            float disc = Sim.Vy * Sim.Vy + 2f * Rules.Gravity * (Sim.Y - top - Rules.FootY);
            if (disc < 0f) return -1f;
            float t = (Sim.Vy + Mathf.Sqrt(disc)) / Rules.Gravity;
            return t > 0f ? t : -1f;
        }

        static bool BurrInView()
        {
            foreach (var b in Sim.Burrs)
                if (Mathf.Abs(b.Y - Sim.CamY) < 5f) return true;
            return false;
        }

        /// <summary>Which way to hold to get clear of every pad below.</summary>
        static int AwayFromPads()
        {
            float best = 0f, bestGap = -1f;
            for (int i = 0; i < 36; i++)
            {
                float x = -Rules.HalfWidth + i * 0.25f, gap = 99f;
                foreach (var p in Sim.Pads)
                    if (p.Top < Sim.Y) gap = Mathf.Min(gap, Rules.WrapDistance(x, p.X) - p.Amp);
                if (gap > bestGap) { bestGap = gap; best = x; }
            }
            float delta = Rules.WrapDelta(best - Sim.X);
            return Mathf.Abs(delta) < 0.2f ? 0 : (int)Mathf.Sign(delta);
        }

        [UnityTest]
        public IEnumerator HoldsToSteerUpThePads_PicksUpAGem_ThenFallsOff_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("bounce-up"));
            yield return Frames(2);
            Assert.IsInstanceOf<BounceUp>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            Assert.AreEqual(Rules.StartY, Sim.Y, "it waits on the ground until the first touch");
            yield return PaddleRallyPlayTests.Capture("26-bounceup-ready");

            // The touch that starts the run is the first bounce, and holding the right half steers right.
            Hold(1);
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.Greater(Sim.Vy, 0f);
            yield return Frames(10);
            Assert.Greater(Sim.Vx, 0f);
            Hold(-1);
            yield return Frames(20);
            Assert.Less(Sim.Vx, 0f, "and the left half steers left");

            // Climb past where moving pads, crumbly pads and burrs come in, picking up a gem or two.
            bool captured = false;
            for (int frame = 0; frame < 60 * 180 && (Sim.Gems < 2 || Sim.HeightPoints < 110); frame++)
            {
                Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State, $"lost at height {Sim.HeightPoints}");
                if (!captured && Sim.HeightPoints >= 80 && Sim.Vy < 0f && Sim.Vy > -3f && BurrInView())
                {
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("27-bounceup-playing");
                }
                Hold(Toward());
                yield return null;
            }
            Assert.GreaterOrEqual(Sim.Gems, 2);
            Assert.GreaterOrEqual(Sim.HeightPoints, 110);
            Assert.Greater(Sim.CamY, 100f, "the view climbs with it");
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");
            Assert.AreEqual(Sim.HeightPoints + Sim.Gems * Rules.GemPoints, Game.Score);

            // Steer away from the pads: it falls off the bottom.
            for (int i = 0; i < 60 * 20 && Game.State == ArcadeGame.Phase.Playing; i++)
            {
                Hold(AwayFromPads());
                yield return null;
            }
            Hold(0);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Sim.Over, "off the bottom, or into a burr on the way down");
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("bounce-up"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("28-bounceup-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(0f, Sim.CamY);
        }
    }
}
