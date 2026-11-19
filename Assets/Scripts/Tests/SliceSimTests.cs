using System.Collections.Generic;
using AceBarcade.Games.SliceStorm;
using NUnit.Framework;
using UnityEngine;

namespace AceBarcade.Tests
{
    public class SliceSimTests
    {
        const float Dt = 1f / 60f;
        const float CrateTop = -6.9f; // things below this are behind the crates
        const float HudBottom = 6.5f;

        /// <summary>A quick stroke straight through a point, fast enough to cut.</summary>
        static SliceEvent CutThrough(SliceSim sim, Vector2 at) =>
            sim.Slice(at + new Vector2(-0.3f, 0f), at + new Vector2(0.3f, 0f), Dt);

        // --- Throws ---------------------------------------------------------------------------

        [Test]
        public void LaunchVelocity_PeaksWhereAsked()
        {
            foreach (var (x, ax, ay) in new[] { (0f, 0f, 2f), (-3.4f, 3.1f, 4.6f), (3.4f, -1f, -0.5f) })
            {
                var sim = new SliceSim(1);
                var t = sim.AddForTests(false, new Vector2(x, SliceSim.LaunchY), SliceSim.LaunchVelocity(x, ax, ay));
                var top = t.Pos;
                for (int i = 0; i < 600 && t.Vel.y > 0f; i++)
                {
                    sim.Step(Dt / 4f);
                    if (t.Pos.y > top.y) top = t.Pos;
                }
                Assert.AreEqual(ay, top.y, 0.05f);
                Assert.AreEqual(ax, top.x, 0.05f);
            }
        }

        [Test]
        public void EverythingThrown_PeaksOnScreenBelowTheHud_AndIsInSightLongEnoughToCut()
        {
            for (int seed = 0; seed < 6; seed++)
            {
                var sim = new SliceSim(seed);
                var top = new Dictionary<Thing, Vector2>();
                var inSight = new Dictionary<Thing, float>();
                var all = new HashSet<Thing>();
                for (int frame = 0; frame < 60 * 150; frame++)
                {
                    sim.SetLives(1000); // nothing is cut here, so keep the run going
                    sim.Step(Dt);
                    foreach (var t in sim.Things)
                    {
                        all.Add(t);
                        if (!top.TryGetValue(t, out var p) || t.Pos.y > p.y) top[t] = t.Pos;
                        if (t.Pos.y > CrateTop + t.Radius) inSight[t] = (inSight.TryGetValue(t, out var s) ? s : 0f) + Dt;
                    }
                }
                Assert.Greater(all.Count, 150, $"seed {seed}: plenty thrown");
                Assert.Greater(sim.Waves, 40);
                var stillUp = new HashSet<Thing>(sim.Things);
                foreach (var t in all)
                {
                    var p = top[t];
                    if (stillUp.Contains(t)) continue; // still in the air at the end
                    Assert.That(p.y, Is.InRange(SliceSim.MinApexY - 0.1f, SliceSim.MaxApexY + 0.1f), $"seed {seed}");
                    Assert.Less(p.y + t.Radius, HudBottom, "peaks below the HUD");
                    Assert.That(p.x, Is.InRange(-SliceSim.MaxApexX - 0.1f, SliceSim.MaxApexX + 0.1f), "peaks on screen");
                    Assert.Greater(inSight[t], 1.2f, $"seed {seed}: in sight long enough to cut");
                }
            }
        }

        [Test]
        public void Waves_StartSmall_WithNoThornballsAtFirst_AndGrow()
        {
            Assert.AreEqual(1, SliceSim.WaveSize(0));
            Assert.AreEqual(SliceSim.MaxWaveSize, SliceSim.WaveSize(100));
            Assert.Greater(SliceSim.WaveGap(0), SliceSim.WaveGap(20));
            for (int w = 0; w < SliceSim.FirstThornWave; w++) Assert.AreEqual(0f, SliceSim.ThornChance(w));
            Assert.Greater(SliceSim.ThornChance(SliceSim.FirstThornWave), 0f);
            Assert.AreEqual(SliceSim.MaxThornChance, SliceSim.ThornChance(200));

            var sim = new SliceSim(4);
            int thorns = 0, veg = 0;
            var seen = new HashSet<Thing>();
            for (int frame = 0; frame < 60 * 120; frame++)
            {
                int waves = sim.Waves;
                sim.SetLives(1000);
                sim.Step(Dt);
                foreach (var t in sim.Things)
                {
                    if (!seen.Add(t)) continue;
                    if (t.Thorn) { thorns++; Assert.Greater(waves, SliceSim.FirstThornWave - 1, "no thornballs in the first waves"); }
                    else { veg++; Assert.That(t.Kind, Is.InRange(0, SliceSim.Kinds - 1)); }
                }
            }
            Assert.Greater(thorns, 10);
            Assert.Greater(veg, thorns * 2, "mostly veggies");
        }

        // --- Cutting --------------------------------------------------------------------------

        [Test]
        public void AFastStrokeCuts_ASlowOneOrAMissDoesNot()
        {
            var sim = new SliceSim(1);
            var t = sim.AddForTests(false, new Vector2(0f, 2f), Vector2.zero);

            Assert.AreEqual(SliceEvent.None, sim.Slice(new Vector2(-0.03f, 2f), new Vector2(0.03f, 2f), Dt), "too slow");
            Assert.AreEqual(SliceEvent.None, sim.Slice(new Vector2(-1f, 3f), new Vector2(1f, 3f), Dt), "above it");
            Assert.AreEqual(SliceEvent.None, sim.Slice(new Vector2(-1f, 2f), new Vector2(1f, 2f), 0f));
            Assert.AreEqual(1, sim.Things.Count);

            var e = sim.Slice(new Vector2(-1f, 2.4f), new Vector2(1f, 2.4f), Dt);
            Assert.AreEqual(SliceEvent.Sliced, e, "the edge of it counts");
            Assert.AreEqual(0, sim.Things.Count);
            Assert.AreEqual(1, sim.Score);
            Assert.AreEqual(1, sim.LastCut.Count);
            Assert.AreSame(t, sim.LastCut[0].Thing);
            Assert.AreEqual(Vector2.right, sim.LastCut[0].Dir);
        }

        [Test]
        public void OneStroke_CutsEverythingOnItsLine_AndThreeOrMoreScoreAComboAtTheEnd()
        {
            var sim = new SliceSim(1);
            for (int i = 0; i < 4; i++) sim.AddForTests(false, new Vector2(-3f + i * 2f, 1f), Vector2.zero);
            sim.AddForTests(false, new Vector2(0f, -3f), Vector2.zero);

            // Two frames of one stroke cut two each.
            Assert.AreEqual(SliceEvent.Sliced, sim.Slice(new Vector2(-3.5f, 1f), new Vector2(-0.5f, 1f), Dt));
            Assert.AreEqual(2, sim.LastCut.Count);
            Assert.AreEqual(SliceEvent.Sliced, sim.Slice(new Vector2(-0.5f, 1f), new Vector2(3.5f, 1f), Dt));
            Assert.AreEqual(4, sim.StrokeCuts);
            Assert.AreEqual(4, sim.Score);

            Assert.AreEqual(SliceEvent.Combo, sim.EndStroke());
            Assert.AreEqual(4, sim.LastCombo);
            Assert.AreEqual(8, sim.Score, "4 cuts and a bonus of 4");
            Assert.AreEqual(0, sim.StrokeCuts);

            // One more on its own: no combo.
            CutThrough(sim, new Vector2(0f, -3f));
            Assert.AreEqual(SliceEvent.None, sim.EndStroke());
            Assert.AreEqual(9, sim.Score);
        }

        [Test]
        public void CuttingAThornball_EndsTheRun()
        {
            var sim = new SliceSim(1);
            sim.AddForTests(false, new Vector2(-1f, 0f), Vector2.zero);
            sim.AddForTests(true, new Vector2(1f, 0f), Vector2.zero);
            Assert.AreEqual(SliceEvent.Sliced, CutThrough(sim, new Vector2(-1f, 0f)));

            var e = CutThrough(sim, new Vector2(1f, 0f));
            Assert.IsTrue((e & SliceEvent.Thorned) != 0);
            Assert.IsTrue((e & SliceEvent.Over) != 0);
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(new Vector2(1f, 0f), sim.ThornAt);
            Assert.AreEqual(1, sim.Score);
            Assert.AreEqual(SliceEvent.None, sim.Step(Dt), "nothing happens after");
            Assert.AreEqual(SliceEvent.None, sim.EndStroke(), "no combo after");
        }

        // --- Lives ----------------------------------------------------------------------------

        [Test]
        public void AVeggieThatFallsAwayUncut_CostsALife_AThornballDoesNot_AndThreeEndTheRun()
        {
            var sim = new SliceSim(1);
            sim.AddForTests(true, new Vector2(0f, -9f), new Vector2(0f, -1f));
            var all = SliceEvent.None;
            for (int i = 0; i < 60; i++) all |= sim.Step(Dt);
            Assert.AreEqual(0, sim.Things.Count, "the thornball is gone");
            Assert.AreEqual(SliceSim.StartLives, sim.Lives);
            Assert.IsFalse((all & SliceEvent.Dropped) != 0);

            // Thrown up from below the bottom, it doesn't count until it comes back down.
            sim.AddForTests(false, new Vector2(2f, SliceSim.LaunchY), SliceSim.LaunchVelocity(2f, 2f, 0f));
            Assert.AreEqual(SliceEvent.None, sim.Step(Dt));
            all = SliceEvent.None;
            for (int i = 0; i < 400 && sim.Things.Count > 0; i++) all |= sim.Step(Dt);
            Assert.IsTrue((all & SliceEvent.Dropped) != 0);
            Assert.AreEqual(SliceSim.StartLives - 1, sim.Lives);
            Assert.AreEqual(2f, sim.LastDropX, 0.01f);

            for (int k = 0; k < 2; k++) sim.AddForTests(false, new Vector2(k, -9f), new Vector2(0f, -1f));
            all = SliceEvent.None;
            for (int i = 0; i < 60; i++) all |= sim.Step(Dt);
            Assert.AreEqual(0, sim.Lives);
            Assert.IsTrue(sim.Over);
            Assert.IsTrue((all & SliceEvent.Over) != 0);
        }

        // --- A whole run ----------------------------------------------------------------------

        [Test]
        public void ABotThatCutsEveryVeggie_AndStaysClearOfThornballs_KeepsGoing()
        {
            for (int seed = 0; seed < 8; seed++)
            {
                var sim = new SliceSim(seed);
                for (int frame = 0; frame < 60 * 180 && !sim.Over; frame++)
                {
                    sim.Step(Dt);
                    foreach (var t in new List<Thing>(sim.Things))
                    {
                        if (t.Thorn || t.Pos.y < CrateTop + 1f) continue;
                        var a = t.Pos + new Vector2(-0.3f, 0f);
                        var b = t.Pos + new Vector2(0.3f, 0f);
                        bool safe = true;
                        foreach (var o in sim.Things)
                            if (o.Thorn && SliceSim.DistanceToSegment(o.Pos, a, b) < o.Radius + 0.3f) safe = false;
                        if (safe) sim.Slice(a, b, Dt);
                    }
                    sim.EndStroke();
                }
                Assert.IsFalse(sim.Over, $"seed {seed}");
                Assert.AreEqual(SliceSim.StartLives, sim.Lives, $"seed {seed}: never had to let one drop");
                Assert.Greater(sim.Cuts, 300, $"seed {seed}");
                Assert.GreaterOrEqual(sim.Score, sim.Cuts);
            }
        }
    }
}
