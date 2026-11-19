using System.Collections;
using AceBarcade.Games.SliceStorm;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Slice Storm through the real arcade, swiping like a thumb would.</summary>
    public class SliceStormPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("slice-storm");
            SliceStorm.SeedForTests = 18;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            SliceStorm.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("slice-storm");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static SliceStorm Game => (SliceStorm)Arcade.Current;
        static SliceSim Sim => Game.Sim;

        static Thing FirstVeggieInSight()
        {
            foreach (var t in Sim.Things)
            {
                if (t.Thorn || t.Pos.y < -4f) continue;
                // Only if the swipe's path is well clear of every thornball.
                bool safe = true;
                foreach (var o in Sim.Things)
                    if (o.Thorn && SliceSim.DistanceToSegment(o.Pos, t.Pos + new Vector2(-2f, 0f), t.Pos + new Vector2(2f, 0f)) < 1.5f) safe = false;
                if (safe) return t;
            }
            return null;
        }

        /// <summary>A quick stroke across a thing, aimed at where it is each frame.</summary>
        static IEnumerator SwipeAcross(Thing t, string capture = null)
        {
            Arcade.Input.Simulate(true, t.Pos + new Vector2(-1.6f, 0.3f));
            yield return null;
            Arcade.Input.Simulate(true, t.Pos + new Vector2(-0.7f, 0.1f));
            yield return null;
            Arcade.Input.Simulate(true, t.Pos + new Vector2(0.7f, -0.1f));
            yield return null;
            Arcade.Input.Simulate(true, t.Pos + new Vector2(1.5f, -0.3f));
            yield return null;
            if (capture != null) yield return PaddleRallyPlayTests.Capture(capture);
            Arcade.Input.Simulate(false, t.Pos + new Vector2(1.5f, -0.3f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SwipesCutVeggies_CombosScore_DropsCostLives_AThornballEnds_AndRetry()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("slice-storm"));
            yield return Frames(2);
            Assert.IsInstanceOf<SliceStorm>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return PaddleRallyPlayTests.Capture("76-slicestorm-ready");

            // A tap starts the run.
            Arcade.Input.Simulate(true, new Vector2(0f, 0f));
            yield return null;
            Arcade.Input.Simulate(false, new Vector2(0f, 0f));
            yield return null;
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);

            // Resting a thumb on a veggie doesn't cut it.
            Thing veg = null;
            for (int i = 0; i < 400 && (veg = FirstVeggieInSight()) == null; i++) yield return null;
            Assert.IsNotNull(veg, "the first veggie flew up");
            var rest = veg.Pos;
            Arcade.Input.Simulate(true, rest);
            yield return null;
            Arcade.Input.Simulate(true, rest);
            yield return null;
            Arcade.Input.Simulate(false, rest);
            yield return null;
            Assert.IsTrue(ContainsThing(veg), "a still thumb cuts nothing");
            Assert.AreEqual(0, Game.Score);

            // Swipe through five as they come.
            int cuts = 0;
            for (int i = 0; i < 60 * 30 && cuts < 5; i++)
            {
                veg = FirstVeggieInSight();
                if (veg == null) { yield return null; continue; }
                yield return SwipeAcross(veg, cuts == 2 ? "77-slicestorm-cut" : null);
                Assert.IsFalse(ContainsThing(veg), "the swipe cut it");
                cuts++;
            }
            Assert.AreEqual(5, cuts);
            Assert.GreaterOrEqual(Game.Score, 5);
            Assert.AreEqual(Sim.Score, Game.Score);

            // Let the air clear, then three in a row with one stroke: a combo.
            for (int i = 0; i < 600 && Sim.Things.Count > 0; i++)
            {
                if ((veg = FirstVeggieInSight()) != null) yield return SwipeAcross(veg);
                else yield return null;
            }
            int score = Game.Score;
            for (int k = 0; k < 3; k++) Sim.AddForTests(false, new Vector2(-2f + k * 2f, 1f), new Vector2(0f, 3f));
            yield return null;
            Arcade.Input.Simulate(true, new Vector2(-3.6f, 1.2f));
            yield return null;
            for (int s = 1; s <= 4; s++)
            {
                Arcade.Input.Simulate(true, new Vector2(-3.6f + s * 1.8f, 1.2f));
                yield return null;
            }
            Arcade.Input.Simulate(false, new Vector2(3.6f, 1.2f));
            yield return null;
            Assert.AreEqual(score + 6, Game.Score, "3 cuts and a bonus of 3");
            yield return Frames(10);

            // One left to fall costs a life.
            int lives = Sim.Lives;
            Assert.Greater(lives, 1);
            Sim.AddForTests(false, new Vector2(1f, -6f), new Vector2(0f, -2f));
            yield return Frames(60);
            Assert.AreEqual(lives - 1, Sim.Lives);
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);

            // Cutting a thornball ends it.
            var thorn = Sim.AddForTests(true, new Vector2(0f, 0f), new Vector2(0f, 4f));
            yield return SwipeAcross(thorn);
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("slice-storm"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("78-slicestorm-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(SliceSim.StartLives, Sim.Lives);
        }

        static bool ContainsThing(Thing t)
        {
            foreach (var o in Sim.Things) if (o == t) return true;
            return false;
        }
    }
}
