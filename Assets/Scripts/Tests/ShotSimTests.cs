using System.Collections.Generic;
using System.Linq;
using AceBarcade.Games.PopShot;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.PopShot.ShotSim;

namespace AceBarcade.Tests
{
    public class ShotSimTests
    {
        const float Dt = 1f / 60f;
        const int B = 0, G = 1, P = 2, O = 3, K = 4;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        static ShotEvent Fly(Sim sim)
        {
            var all = ShotEvent.None;
            for (int i = 0; i < 600 && sim.Flying; i++) all |= sim.Step(Dt);
            Assert.IsFalse(sim.Flying, "the shot stuck somewhere");
            return all;
        }

        static ShotEvent Shoot(Sim sim, Vector2 at)
        {
            Assert.IsTrue(sim.Fire(Sim.AimToward(at)));
            return Fly(sim);
        }

        static Vector2? Find(Sim sim, int id)
        {
            for (int r = 0; r < sim.Rows; r++)
                for (int c = 0; c < sim.ColsIn(r); c++)
                    if (sim.At(r, c)?.Id == id) return sim.CellPos(r, c);
            return null;
        }

        static HashSet<int> ColorsOnBoard(Sim sim)
        {
            var set = new HashSet<int>();
            for (int r = 0; r < sim.Rows; r++)
                for (int c = 0; c < sim.ColsIn(r); c++)
                    if (sim.At(r, c) != null) set.Add(sim.At(r, c).Color);
            return set;
        }

        // --- The board ------------------------------------------------------------------------------

        [Test]
        public void Start_FiveRowsOfFourColours_PackedEightThenSeven_AndTheShotsMatchTheBoard()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                Assert.AreEqual(Sim.StartRows, sim.Rows);
                for (int r = 0; r < sim.Rows; r++)
                {
                    Assert.AreEqual(r % 2 == 0 ? 8 : 7, sim.ColsIn(r));
                    for (int c = 0; c < sim.ColsIn(r); c++)
                    {
                        Assert.IsNotNull(sim.At(r, c));
                        Assert.Less(sim.At(r, c).Color, Sim.StartColors);
                        var p = sim.CellPos(r, c);
                        Assert.That(p.x, Is.InRange(Sim.Left + Sim.Radius - 1e-4f, Sim.Right - Sim.Radius + 1e-4f));
                    }
                }
                var colors = ColorsOnBoard(sim);
                Assert.IsTrue(colors.Contains(sim.Current) && colors.Contains(sim.Next));
                Assert.AreEqual(0, sim.Pips);
                Assert.AreEqual(Sim.StartPips, sim.PipsToPush);
                Assert.AreEqual(0, sim.Score);
            }
            Assert.AreEqual(11, Sim.MaxRows, "eleven rows fit above the red line");
            Assert.Greater(Sim.TopY - (Sim.MaxRows - 1) * Sim.RowHeight - Sim.Radius, Sim.DangerY);
            Assert.Less(Sim.TopY - Sim.MaxRows * Sim.RowHeight - Sim.Radius, Sim.DangerY);
        }

        [Test]
        public void Neighbours_AreExactlyTheCellsOneBubbleAway()
        {
            var sim = new Sim(1);
            sim.Load(B, B, "BBBBBBBB", "BBBBBBB", "BBBBBBBB", "BBBBBBB", "BBBBBBBB");
            for (int r = 0; r < sim.Rows; r++)
                for (int c = 0; c < sim.ColsIn(r); c++)
                {
                    var here = sim.CellPos(r, c);
                    var listed = sim.Neighbours(r, c).Where(n => sim.At(n.Row, n.Col) != null).ToList();
                    foreach (var n in listed) Assert.AreEqual(1f, (sim.CellPos(n.Row, n.Col) - here).magnitude, 1e-3f);
                    int near = 0;
                    for (int r2 = 0; r2 < sim.Rows; r2++)
                        for (int c2 = 0; c2 < sim.ColsIn(r2); c2++)
                            if ((r2, c2) != (r, c) && (sim.CellPos(r2, c2) - here).magnitude < 1.01f) near++;
                    Assert.AreEqual(near, listed.Count, $"cell {r},{c}");
                }
        }

        [Test]
        public void Aiming_IsAlwaysUpward_NeverFlatterThan10Degrees()
        {
            Assert.AreEqual(Vector2.up.x, Sim.AimToward(Sim.Shooter + Vector2.up).x, 1e-4f);
            var low = Sim.AimToward(Sim.Shooter + new Vector2(5f, -1f));
            Assert.AreEqual(Sim.MinAngle, Mathf.Atan2(low.y, low.x) * Mathf.Rad2Deg, 1e-3f);
            var lowLeft = Sim.AimToward(Sim.Shooter + new Vector2(-5f, 0.1f));
            Assert.AreEqual(180f - Sim.MinAngle, Mathf.Atan2(lowLeft.y, lowLeft.x) * Mathf.Rad2Deg, 1e-3f);
        }

        // --- Shooting ------------------------------------------------------------------------------

        [Test]
        public void AShot_BouncesOffTheWalls_AndSticksAtTheTop_WhereTheAimLineSaidItWould()
        {
            var sim = new Sim(1);
            sim.Load(G, G, "B.......");
            var dir = Sim.AimToward(Sim.Shooter + new Vector2(1f, 1.2f));
            var path = sim.Trace(dir, out int row, out int col);
            Assert.AreEqual(3, path.Count, "the shooter, one bounce, the stop");
            Assert.AreEqual(Sim.Right - Sim.Radius, path[1].x, 1e-4f);
            Assert.IsTrue(sim.Fire(dir));
            var e = Fly(sim);
            Assert.IsTrue((e & ShotEvent.Bounced) != 0);
            Assert.IsTrue((e & ShotEvent.Landed) != 0);
            Assert.AreEqual(0, sim.LandedRow);
            Assert.AreEqual((row, col), (sim.LandedRow, sim.LandedCol));
            Assert.AreEqual(G, sim.At(row, col).Color);
            Assert.AreEqual(1, sim.Pips, "no pop, so a pip fills");
        }

        [Test]
        public void AShot_SticksBesideTheBubbleItTouches_InTheNearestFreeCell()
        {
            var sim = new Sim(1);
            sim.Load(G, G, "....B...");
            var e = Shoot(sim, Sim.Shooter + Vector2.up);
            Assert.IsTrue((e & ShotEvent.Landed) != 0);
            Assert.AreEqual((1, 3), (sim.LandedRow, sim.LandedCol), "under it, half a bubble left");
            CollectionAssert.AreEqual(new[] { "....B...", "...G..." }, sim.Dump());
        }

        [Test]
        public void ThreeOfAColourTouching_Pop_1Each()
        {
            var sim = new Sim(1);
            sim.Load(G, G, "B..GG...");
            var e = Shoot(sim, Sim.Shooter + Vector2.up);
            Assert.IsTrue((e & ShotEvent.Popped) != 0);
            Assert.AreEqual(3, sim.Popped.Count);
            Assert.AreEqual(3 * Sim.PopPoints, sim.Score);
            Assert.AreEqual(0, sim.Pips, "a pop fills no pip");
            CollectionAssert.AreEqual(new[] { "B......." }, sim.Dump());
        }

        [Test]
        public void BubblesLeftHangingByNothing_Drop_2Each_AndGemsAdd5()
        {
            var sim = new Sim(1);
            sim.Load(G, G, "gG.....B", "P.....O", "K.......");
            var e = Shoot(sim, sim.CellPos(0, 2));
            Assert.AreEqual((0, 2), (sim.LandedRow, sim.LandedCol));
            Assert.IsTrue((e & ShotEvent.Popped) != 0);
            Assert.IsTrue((e & ShotEvent.Dropped) != 0);
            Assert.IsTrue((e & ShotEvent.Gem) != 0);
            Assert.AreEqual(3, sim.Popped.Count);
            Assert.AreEqual(2, sim.Dropped.Count, "the purple and the pink under it");
            Assert.AreEqual(3 * Sim.PopPoints + 2 * Sim.DropPoints + Sim.GemPoints, sim.Score);
            Assert.AreEqual(1, sim.Gems);
            CollectionAssert.AreEqual(new[] { ".......B", "......O" }, sim.Dump());
        }

        [Test]
        public void ClearingTheBoard_Scores20_AndFourRowsPushIn()
        {
            var sim = new Sim(1);
            sim.Load(G, G, "GG......");
            var e = Shoot(sim, sim.CellPos(0, 2));
            Assert.IsTrue((e & ShotEvent.Cleared) != 0);
            Assert.AreEqual(3 * Sim.PopPoints + Sim.ClearPoints, sim.Score);
            Assert.AreEqual(Sim.ClearRefill, sim.Rows);
            Assert.Greater(sim.Count, 25);
            Assert.IsTrue(ColorsOnBoard(sim).Contains(sim.Current));
        }

        [Test]
        public void ShotsThatPopNothing_FillThePips_AndAFullSetPushesARowIn_KeepingEveryBubbleInItsPlaceAcross()
        {
            var sim = new Sim(1);
            sim.Load(K, K, "POPOPOPO", "OPOPOPO");
            int id = sim.At(1, 2).Id;
            var was = Find(sim, id).Value;
            // Spread out, and alternating pink and blue, so nothing pops.
            float[] targets = { -3.5f, -2f, -0.5f, 1f, 2.5f };
            for (int i = 0; i < targets.Length; i++)
            {
                int color = i % 2 == 0 ? K : B;
                sim.SetShots(color, color);
                var e = Shoot(sim, new Vector2(targets[i], 3f));
                Assert.IsTrue((e & ShotEvent.Pip) != 0);
                Assert.IsFalse((e & ShotEvent.Pushed) != 0);
                Assert.AreEqual(i + 1, sim.Pips);
            }
            sim.SetShots(B, B);
            var last = Shoot(sim, new Vector2(4f, 3f));
            Assert.IsTrue((last & ShotEvent.Pushed) != 0, "the sixth pip");
            Assert.AreEqual(0, sim.Pips);
            Assert.AreEqual(1, sim.RowsPushed);
            var now = Find(sim, id).Value;
            Assert.AreEqual(was.x, now.x, 1e-4f, "same place across");
            Assert.AreEqual(was.y - Sim.RowHeight, now.y, 1e-4f, "one row down");
        }

        [Test]
        public void ABubbleBelowTheRedLine_EndsTheRun()
        {
            var sim = new Sim(1);
            var rows = Enumerable.Range(0, Sim.MaxRows).Select(r => r % 2 == 0 ? "P......." : "P......").ToArray();
            sim.Load(K, K, rows);
            Assert.AreEqual(Sim.MaxRows - 1, sim.LowestRow);
            Assert.IsFalse(sim.Over, "the bottom row still clears the line");
            var e = Shoot(sim, sim.CellPos(Sim.MaxRows - 1, 0));
            Assert.IsTrue((e & ShotEvent.Over) != 0);
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(Sim.MaxRows, sim.LandedRow);
            Assert.IsFalse(sim.Fire(Vector2.up));
            Assert.AreEqual(ShotEvent.None, sim.Step(1f));
        }

        [Test]
        public void TheAimLine_AlwaysShowsTheCellTheShotSticksIn()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var random = new System.Random(seed);
                for (int i = 0; i < 40 && !sim.Over; i++)
                {
                    float angle = Mathf.Lerp(Sim.MinAngle, 180f - Sim.MinAngle, (float)random.NextDouble());
                    var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                    sim.Trace(dir, out int row, out int col);
                    sim.Fire(dir);
                    Fly(sim);
                    Assert.AreEqual((row, col), (sim.LandedRow, sim.LandedCol), $"seed {seed}, shot {i}, angle {angle:0.0}");
                }
            }
        }

        // --- A whole game -----------------------------------------------------------------------------

        /// <summary>
        /// A player who tries every aim a degree apart, follows the aim line, and takes the shot
        /// that pops the most (with what drops), else the one that sticks highest beside its own colour.
        /// </summary>
        static Vector2 BestShot(Sim sim)
        {
            Vector2 best = Vector2.up;
            float bestValue = float.MinValue;
            for (float angle = Sim.MinAngle; angle <= 180f - Sim.MinAngle; angle += 1f)
            {
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                sim.Trace(dir, out int row, out int col);
                if (row < 0) continue;
                int clump = 1 + Clump(sim, row, col, sim.Current);
                float value = clump >= Sim.PopAt ? 100f + clump : clump * 3f;
                value -= row * 0.5f;
                if (value > bestValue) { bestValue = value; best = dir; }
            }
            return best;
        }

        static int Clump(Sim sim, int row, int col, int color)
        {
            var seen = new HashSet<(int, int)> { (row, col) };
            var stack = new Stack<(int, int)>();
            stack.Push((row, col));
            int n = 0;
            while (stack.Count > 0)
            {
                var (r, c) = stack.Pop();
                foreach (var (nr, nc) in sim.Neighbours(r, c))
                {
                    var b = sim.At(nr, nc);
                    if (b == null || b.Color != color || !seen.Add((nr, nc))) continue;
                    n++;
                    stack.Push((nr, nc));
                }
            }
            return n;
        }

        [Test]
        public void AnAimingPlayer_Plays250Shots_RowsKeepComing_TheFifthColourJoins_AndTheNextShotsAlwaysMatchTheBoard()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                for (int shot = 0; shot < 250 && !sim.Over; shot++)
                {
                    sim.Fire(BestShot(sim));
                    Fly(sim);
                    var colors = ColorsOnBoard(sim);
                    Assert.IsTrue(colors.Contains(sim.Current), $"seed {seed}, shot {shot}");
                    Assert.IsTrue(colors.Contains(sim.Next), $"seed {seed}, shot {shot}");
                    Assert.Less(sim.Pips, sim.PipsToPush);
                }
                Assert.IsFalse(sim.Over, $"seed {seed}: lost after {sim.Shots} shots, {sim.RowsPushed} rows pushed, score {sim.Score}");
                Assert.GreaterOrEqual(sim.RowsPushed, Sim.ColorsJoinAfter, $"seed {seed}");
                Assert.AreEqual(Sim.MaxColors, sim.Colors);
                Assert.Less(sim.PipsToPush, Sim.StartPips);
                Assert.Greater(sim.Score, 300, $"seed {seed}");
            }
        }
    }
}
