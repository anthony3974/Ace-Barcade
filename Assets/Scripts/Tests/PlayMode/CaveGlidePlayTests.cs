using System.Collections;
using System.Collections.Generic;
using AceBarcade.Games.CaveGlide;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.CaveGlide.CaveSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Cave Glide through the real arcade with a simulated held finger.</summary>
    public class CaveGlidePlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("cave-glide");
            CaveGlide.SeedForTests = 16;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            CaveGlide.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("cave-glide");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static CaveGlide Game => (CaveGlide)Arcade.Current;
        static Rules Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(1f, -4f);

        /// <summary>A crystal a little way in front of the glider: a good moment for a screenshot.</summary>
        static bool CrystalJustAhead()
        {
            foreach (var c in Sim.Crystals)
                if (c.X - Sim.GliderWorldX > 2.5f && c.X - Sim.GliderWorldX < 3f) return true;
            return false;
        }

        [UnityTest]
        public IEnumerator Glides25Seconds_PicksUpGems_ThenSinksIntoACrystal_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("cave-glide"));
            yield return Frames(2);
            Assert.IsInstanceOf<CaveGlide>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(30);
            Assert.AreEqual(Rules.StartY, Sim.Y, "the glider waits for the first touch");
            Assert.AreEqual(0f, Sim.Distance);
            yield return PaddleRallyPlayTests.Capture("48-caveglide-ready");

            // Fly like a player: aim for the middle of the way ahead, allowing for the glider's
            // swing, with the finger 0.12 s behind the decision.
            const float lag = 0.12f;
            var decisions = new Queue<(int Frame, bool Hold)>();
            bool hold = true, captured = false;
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.IsTrue(Sim.Holding, "the touch that starts the run is a hold");
            for (int frame = 0; frame < 60 * 25; frame++)
            {
                Assert.IsFalse(Sim.Crashed, $"crashed at {Sim.Distance:0.0}");
                float acc = hold ? Rules.Lift : -Rules.Gravity;
                float v = Mathf.Clamp(Sim.Velocity + acc * lag, -Rules.MaxFall, Rules.MaxRise);
                float y = Sim.Y + (Sim.Velocity + v) / 2f * lag;
                float ahead = y + (v > 0f ? v * v / (2f * Rules.Gravity) : -v * v / (2f * Rules.Lift));
                decisions.Enqueue((frame + Mathf.RoundToInt(lag * 60f), ahead < Sim.PathAt(Sim.GliderWorldX + Sim.Speed * 0.2f)));
                while (decisions.Count > 0 && decisions.Peek().Frame <= frame) hold = decisions.Dequeue().Hold;
                Arcade.Input.Simulate(hold, Thumb);
                if (!captured && frame > 60 * 14 && CrystalJustAhead())
                {
                    captured = true;
                    yield return PaddleRallyPlayTests.Capture("49-caveglide-flying");
                }
                else yield return null;
            }
            Assert.IsFalse(Sim.Crashed);
            Assert.Greater(Sim.Distance, 120f);
            Assert.GreaterOrEqual(Sim.GemsTaken, 3);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Let go and stay down: the glider sinks to the floor and runs along it into a crystal.
            Arcade.Input.Simulate(false, Thumb);
            for (int i = 0; i < 60 * 30 && Game.State == ArcadeGame.Phase.Playing; i++) yield return null;
            Assert.IsTrue(Sim.Crashed);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("cave-glide"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("50-caveglide-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(0f, Sim.Distance);
        }
    }
}
