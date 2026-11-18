using AceBarcade.Games.StackTower;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.StackTower.StackSim;

namespace AceBarcade.Tests
{
    public class StackSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        /// <summary>Runs until the block is sliding (its wait at the edge is over).</summary>
        static void Release(Sim sim)
        {
            while (sim.Wait > 0f) sim.Step(Dt);
        }

        /// <summary>Slides the block until its centre is exactly at <paramref name="x"/>, then drops it.</summary>
        static StackEvent DropAt(Sim sim, float x)
        {
            Release(sim);
            sim.Step(sim.TimeUntilOver(x));
            Assert.AreEqual(x, sim.X, 1e-3f, "the block slid to where it was asked");
            return sim.Drop();
        }

        /// <summary>
        /// A player who sees the block coming and taps for the moment it's over the tower, but whose
        /// taps land up to <paramref name="jitter"/> seconds early or late, on whole 60 fps frames.
        /// </summary>
        static int Play(Sim sim, int seed, float jitter, int floors)
        {
            var random = new System.Random(seed);
            float time = 0f, tapAt = float.NaN;
            for (int frame = 0; frame < 60 * 60 * 10 && !sim.Over && sim.Stacked < floors; frame++)
            {
                if (float.IsNaN(tapAt))
                    tapAt = time + sim.TimeUntilOver(sim.Top.X) + jitter * (float)(random.NextDouble() * 2.0 - 1.0);
                if (time >= tapAt - Dt / 2f && sim.Wait <= 0f)
                {
                    sim.Drop();
                    tapAt = float.NaN;
                }
                sim.Step(Dt);
                time += Dt;
            }
            return sim.Stacked;
        }

        // --- The start ---------------------------------------------------------------------------

        [Test]
        public void Start_AFoundation_AndABlockWaitingAtTheLeftEdge()
        {
            var sim = new Sim();
            Assert.AreEqual(1, sim.Floors.Count);
            Assert.AreEqual(0f, sim.Top.X);
            Assert.AreEqual(Sim.StartWidth, sim.Top.Width);
            Assert.AreEqual(0, sim.Stacked);
            Assert.AreEqual(1, sim.Level);
            Assert.AreEqual(-Sim.SlideRange, sim.X);
            Assert.AreEqual(1, sim.Direction);
            Assert.AreEqual(Sim.StartWidth, sim.Width);
            Assert.AreEqual(Sim.WaitTime, sim.Wait);
            Assert.IsFalse(sim.HasGem);
            Assert.AreEqual(0, sim.Score);
        }

        [Test]
        public void TheBlock_WaitsAtItsEdge_ThenSlidesAndBouncesBetweenTheEdges_AndTapsWhileItWaitsAreIgnored()
        {
            var sim = new Sim();
            Assert.AreEqual(StackEvent.None, sim.Drop(), "a tap while it waits does nothing");
            sim.Step(Sim.WaitTime / 2f);
            Assert.AreEqual(-Sim.SlideRange, sim.X, "still waiting");
            Assert.AreEqual(StackEvent.None, sim.Drop());
            sim.Step(Sim.WaitTime / 2f + 0.5f);
            Assert.AreEqual(-Sim.SlideRange + Sim.StartSpeed * 0.5f, sim.X, 1e-4f);

            var all = StackEvent.None;
            for (int i = 0; i < 600; i++)
            {
                all |= sim.Step(Dt);
                Assert.That(sim.X, Is.InRange(-Sim.SlideRange, Sim.SlideRange));
            }
            Assert.IsTrue((all & StackEvent.Bounced) != 0);
            Assert.AreEqual(1, sim.Floors.Count, "nothing lands without a tap");
        }

        // --- Dropping ------------------------------------------------------------------------------

        [Test]
        public void ADropRightOverTheTower_IsPerfect_SnapsIntoLine_AndScores2()
        {
            var sim = new Sim();
            var e = DropAt(sim, Sim.PerfectTolerance * 0.9f);
            Assert.IsTrue((e & StackEvent.Landed) != 0);
            Assert.IsTrue((e & StackEvent.Perfect) != 0);
            Assert.IsFalse((e & StackEvent.Cut) != 0);
            Assert.AreEqual(2, sim.Floors.Count);
            Assert.AreEqual(0f, sim.Top.X, "snapped into line with the floor below");
            Assert.AreEqual(Sim.StartWidth, sim.Top.Width);
            Assert.IsTrue(sim.Top.Perfect);
            Assert.AreEqual(Sim.FloorPoints + Sim.PerfectPoints, sim.Score);
            Assert.AreEqual(1, sim.Streak);

            // The next block comes from the right, as wide as the top, and waits.
            Assert.AreEqual(2, sim.Level);
            Assert.AreEqual(Sim.SlideRange, sim.X);
            Assert.AreEqual(-1, sim.Direction);
            Assert.AreEqual(Sim.StartWidth, sim.Width);
            Assert.AreEqual(Sim.WaitTime, sim.Wait);
        }

        [Test]
        public void AnOffsetDrop_CutsOffTheOverhang_OnTheSideItHangsOver()
        {
            var sim = new Sim();
            var e = DropAt(sim, 1f);
            Assert.IsTrue((e & StackEvent.Cut) != 0);
            Assert.IsFalse((e & StackEvent.Perfect) != 0);
            Assert.AreEqual(Sim.StartWidth - 1f, sim.Top.Width, 1e-4f);
            Assert.AreEqual(0.5f, sim.Top.X, 1e-4f);
            Assert.AreEqual(Sim.StartWidth / 2f, sim.Top.Right, 1e-4f, "lined up with the floor below on the right");
            Assert.AreEqual(1f, sim.CutWidth, 1e-4f);
            Assert.AreEqual(Sim.StartWidth / 2f + 0.5f, sim.CutX, 1e-4f, "the cut part hangs off the right");
            Assert.AreEqual(Sim.FloorPoints, sim.Score);
            Assert.AreEqual(0, sim.Streak);
            Assert.AreEqual(sim.Top.Width, sim.Width, 1e-4f, "the next block is as narrow as the top");

            // Now one to the left of the new top.
            e = DropAt(sim, 0.5f - 0.6f);
            Assert.IsTrue((e & StackEvent.Cut) != 0);
            Assert.AreEqual(Sim.StartWidth - 1.6f, sim.Top.Width, 1e-4f);
            Assert.AreEqual(sim.Floors[1].Left, sim.Top.Left, 1e-4f);
            Assert.AreEqual(0.6f, sim.CutWidth, 1e-4f);
            Assert.Less(sim.CutX, sim.Top.Left, "the cut part hangs off the left");
        }

        [Test]
        public void MissingTheTower_EndsTheRun_AndNothingMovesAfter()
        {
            var sim = new Sim();
            DropAt(sim, 1.5f);
            float top = sim.Top.X, width = sim.Top.Width;
            var e = DropAt(sim, top - width - 0.01f);
            Assert.IsTrue((e & StackEvent.Missed) != 0);
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(2, sim.Floors.Count, "the missed block isn't part of the tower");
            Assert.AreEqual(top - width - 0.01f, sim.MissX, 1e-3f);
            Assert.AreEqual(width, sim.MissWidth, 1e-4f);
            int score = sim.Score;
            float x = sim.X;
            Assert.AreEqual(StackEvent.None, sim.Step(1f));
            Assert.AreEqual(StackEvent.None, sim.Drop());
            Assert.AreEqual(x, sim.X);
            Assert.AreEqual(score, sim.Score);
        }

        [Test]
        public void ThreePerfectsInARow_WidenTheTower_UpToItsStartingWidth()
        {
            var sim = new Sim();
            DropAt(sim, 0.8f); // 2.8 wide
            float top = sim.Top.X;
            Assert.IsFalse((DropAt(sim, top) & StackEvent.Grew) != 0);
            Assert.IsFalse((DropAt(sim, top) & StackEvent.Grew) != 0);
            Assert.AreEqual(Sim.StartWidth - 0.8f, sim.Top.Width, 1e-4f);
            var e = DropAt(sim, top);
            Assert.IsTrue((e & StackEvent.Grew) != 0, "the third perfect in a row");
            Assert.AreEqual(Sim.StartWidth - 0.8f + Sim.GrowBy, sim.Top.Width, 1e-4f);
            Assert.AreEqual(top, sim.Top.X, 1e-4f, "it grows evenly on both sides");
            Assert.AreEqual(3, sim.Streak);
            DropAt(sim, top);
            Assert.AreEqual(Sim.StartWidth - 0.8f + 2f * Sim.GrowBy, sim.Top.Width, 1e-4f, "and the fourth");
            e = DropAt(sim, top);
            Assert.IsTrue((e & StackEvent.Grew) != 0);
            Assert.AreEqual(Sim.StartWidth, sim.Top.Width, 1e-4f, "never wider than it started");
            e = DropAt(sim, top);
            Assert.IsFalse((e & StackEvent.Grew) != 0);
            Assert.AreEqual(Sim.StartWidth, sim.Top.Width, 1e-4f);

            // A cut resets the streak.
            DropAt(sim, top + 0.5f);
            Assert.AreEqual(0, sim.Streak);
            Assert.AreEqual(1 + 6 + 1, sim.Stacked);
            Assert.AreEqual(6, sim.Perfects);
            Assert.AreEqual(1, sim.Gems, "the 6th block's gem, dropped perfectly");
            Assert.AreEqual(2 * Sim.FloorPoints + 6 * (Sim.FloorPoints + Sim.PerfectPoints) + Sim.GemPoints, sim.Score);
        }

        [Test]
        public void EverySixthBlock_CarriesAGem_ThatScores5IfItLandsOnTheTower_ButFallsWithTheCutPart()
        {
            var sim = new Sim();
            for (int i = 1; i < Sim.GemEvery; i++)
            {
                Assert.IsFalse(sim.HasGem, $"block {i}");
                DropAt(sim, 0f);
            }
            Assert.IsTrue(sim.HasGem);
            int score = sim.Score;
            var e = DropAt(sim, 0.9f); // its centre lands on the tower
            Assert.IsTrue((e & StackEvent.Gem) != 0);
            Assert.AreEqual(score + Sim.FloorPoints + Sim.GemPoints, sim.Score);
            Assert.AreEqual(0.9f, sim.GemX, 1e-3f);
            Assert.AreEqual(1, sim.Gems);

            for (int i = 1; i < Sim.GemEvery; i++)
            {
                Assert.IsFalse(sim.HasGem);
                DropAt(sim, sim.Top.X);
            }
            Assert.IsTrue(sim.HasGem);
            score = sim.Score;
            e = DropAt(sim, sim.Top.X - sim.Top.Width * 0.7f); // its centre hangs off the left
            Assert.IsTrue((e & StackEvent.GemLost) != 0);
            Assert.IsFalse((e & StackEvent.Gem) != 0);
            Assert.AreEqual(score + Sim.FloorPoints, sim.Score);
            Assert.AreEqual(1, sim.Gems);
        }

        [Test]
        public void ItSpeedsUpWithEveryFloor_ToACap()
        {
            var sim = new Sim();
            Assert.AreEqual(Sim.StartSpeed, sim.Speed);
            float last = sim.Speed;
            for (int i = 0; i < 60; i++)
            {
                DropAt(sim, sim.Top.X);
                Assert.GreaterOrEqual(sim.Speed, last);
                last = sim.Speed;
            }
            Assert.AreEqual(Sim.MaxSpeed, sim.Speed);
            int floorsToTop = Mathf.CeilToInt((Sim.MaxSpeed - Sim.StartSpeed) / Sim.SpeedUp);
            Assert.That(floorsToTop, Is.InRange(30, 50));
        }

        // --- Fair play -------------------------------------------------------------------------------

        [Test]
        public void TheTowerNeverLeavesTheFoundation_SoEverySweepCrossesItsMiddle()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim();
                var random = new System.Random(seed);
                for (int i = 0; i < 200 && !sim.Over; i++)
                {
                    var top = sim.Top;
                    Assert.That(top.X, Is.InRange(-Sim.StartWidth / 2f, Sim.StartWidth / 2f));
                    Assert.Less(Mathf.Abs(top.X) + Sim.PerfectTolerance, Sim.SlideRange);
                    // Somewhere over the tower, mostly not perfectly.
                    float x = top.X + (float)(random.NextDouble() * 2.0 - 1.0) * top.Width * 0.3f;
                    var e = DropAt(sim, x);
                    if ((e & StackEvent.Landed) == 0) break;
                    if (sim.Top.Width < 0.4f) sim = new Sim();
                }
            }
        }

        [Test]
        public void ANewBlock_ReachesTheTowerSoon_EvenAtTopSpeed()
        {
            var sim = new Sim();
            for (int i = 0; i < 60; i++) DropAt(sim, sim.Top.X);
            Assert.AreEqual(Sim.MaxSpeed, sim.Speed);
            // The furthest the tower can be from a block's starting edge.
            float worst = (Sim.SlideRange + Sim.StartWidth / 2f) / Sim.MaxSpeed + Sim.WaitTime;
            Assert.Less(worst, 1.2f);
            float best = (Sim.SlideRange - Sim.StartWidth / 2f) / Sim.MaxSpeed + Sim.WaitTime;
            Assert.Greater(best, 0.45f, "time to see it coming");
        }

        [Test]
        public void APlayerWhoseTapsLandWithin25ms_Stacks150Floors_PastTopSpeed_OnEverySeed()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim();
                int floors = Play(sim, seed, 0.025f, 150);
                Assert.IsFalse(sim.Over, $"seed {seed}: missed at floor {floors}, width {sim.Width:0.00}");
                Assert.AreEqual(150, floors);
                Assert.Greater(sim.Perfects, 20, $"seed {seed}");
                Assert.Greater(sim.Gems, 15, $"seed {seed}");
            }
        }

        [Test]
        public void APlayerWhoAlwaysTaps60msLate_NarrowsTheTower_UntilTheyMiss()
        {
            var sim = new Sim();
            var random = new System.Random(1);
            float time = 0f, tapAt = float.NaN;
            float lastWidth = sim.Width;
            for (int frame = 0; frame < 60 * 60 * 5 && !sim.Over; frame++)
            {
                if (float.IsNaN(tapAt)) tapAt = time + sim.TimeUntilOver(sim.Top.X) + 0.06f;
                if (time >= tapAt - Dt / 2f && sim.Wait <= 0f)
                {
                    var e = sim.Drop();
                    if ((e & StackEvent.Landed) != 0)
                    {
                        Assert.Less(sim.Top.Width, lastWidth, "always late, never perfect");
                        lastWidth = sim.Top.Width;
                    }
                    tapAt = float.NaN;
                }
                sim.Step(Dt);
                time += Dt;
            }
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(0, sim.Perfects);
            Assert.That(sim.Stacked, Is.InRange(5, 40));
        }
    }
}
