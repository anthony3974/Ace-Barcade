using System.Collections.Generic;
using AceBarcade.Games.PetalSwap;
using NUnit.Framework;
using UnityEngine;

namespace AceBarcade.Tests
{
    public class PetalSimTests
    {
        const float Dt = 1f / 60f;

        // Colour (col + 2 row) mod 5: no line made anywhere, and no swap makes one.
        static readonly string[] NoMoves =
        {
            "2340123",
            "0123401",
            "3401234",
            "1234012",
            "4012340",
            "2340123",
            "0123401",
        };

        // Swapping (2, 0) up lines up three 0s along the bottom; when they pop, columns 0 to 2
        // drop a row, which lines up 1s in row 1 and 3s in row 0: a second wave.
        static readonly string[] Cascade =
        {
            "2340123",
            "0123401",
            "3401234",
            "1234012",
            "4112340",
            "2301123",
            "0033401",
        };

        static PetalSim Board(string[] rows, int seed = 1)
        {
            var sim = new PetalSim(seed);
            sim.SetBoard(rows);
            return sim;
        }

        /// <summary>Steps until the garden is still again; returns everything that happened.</summary>
        static PetalEvent Settle(PetalSim sim, List<(int points, int chain, int count)> waves = null)
        {
            var all = PetalEvent.None;
            for (int i = 0; i < 2000; i++)
            {
                var e = sim.Step(Dt);
                all |= e;
                if ((e & PetalEvent.Popped) != 0) waves?.Add((sim.LastPoints, sim.Chain, sim.Popped.Count));
                if (sim.State == PetalSim.Phase.Still && i > 0) return all;
                if (sim.Over) return all;
            }
            Assert.Fail("the garden never settled");
            return all;
        }

        static void AssertSameColours(string[] before, string[] after)
        {
            var a = new int[PetalSim.Colors];
            foreach (var row in before) foreach (var ch in row) a[ch - '0']++;
            foreach (var row in after) foreach (var ch in row) a[ch - '0']--;
            for (int k = 0; k < PetalSim.Colors; k++) Assert.AreEqual(0, a[k], $"colour {k} count changed");
        }

        // --- Dealing --------------------------------------------------------------------------

        [Test]
        public void NewGarden_HasNoLinesMade_AndAMove_OnEverySeed()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                var sim = new PetalSim(seed);
                Assert.IsFalse(sim.AnyLineMade, $"seed {seed}: a line was already made");
                Assert.IsTrue(sim.HasMove, $"seed {seed}: no move");
                Assert.AreEqual(PetalSim.Phase.Still, sim.State);
                Assert.AreEqual(PetalSim.StartSeconds, sim.TimeLeft);
                foreach (var row in sim.Board())
                    foreach (var ch in row) Assert.That(ch - '0', Is.InRange(0, PetalSim.Colors - 1));
            }
        }

        // --- Swaps ----------------------------------------------------------------------------

        [Test]
        public void Swaps_MustBeOneStepToANeighbourOnTheBoard_WhileStill()
        {
            var sim = Board(Cascade);
            Assert.IsFalse(sim.Swap(new Vector2Int(0, 0), new Vector2Int(1, 1)), "diagonal");
            Assert.IsFalse(sim.Swap(new Vector2Int(0, 0), new Vector2Int(2, 0)), "two steps");
            Assert.IsFalse(sim.Swap(new Vector2Int(0, 0), Vector2Int.zero), "nowhere");
            Assert.IsFalse(sim.Swap(new Vector2Int(0, 0), Vector2Int.left), "off the left edge");
            Assert.IsFalse(sim.Swap(new Vector2Int(6, 6), Vector2Int.up), "off the top");
            Assert.IsFalse(sim.Swap(new Vector2Int(7, 0), Vector2Int.left), "from off the board");
            Assert.AreEqual(PetalSim.Phase.Still, sim.State);

            Assert.IsTrue(sim.Swap(new Vector2Int(2, 0), Vector2Int.up));
            Assert.AreEqual(PetalSim.Phase.Swapping, sim.State);
            Assert.IsFalse(sim.Swap(new Vector2Int(5, 5), Vector2Int.down), "one swap at a time");
        }

        [Test]
        public void ASwapThatMakesNoLine_SlidesBack_AtNoCost()
        {
            var sim = Board(Cascade);
            var before = sim.Board();
            sim.Step(Dt); // the first still frame checks for moves
            sim.SetTime(20f);

            // (5, 5) and (6, 5) are 2 and 3: swapping them lines up nothing.
            Assert.IsTrue(sim.Swap(new Vector2Int(5, 5), Vector2Int.right));
            var mid = sim.At(5, 5);
            var events = Settle(sim);

            Assert.IsTrue((events & PetalEvent.SwappedBack) != 0);
            Assert.IsFalse((events & PetalEvent.Popped) != 0);
            CollectionAssert.AreEqual(before, sim.Board(), "everything is back where it was");
            Assert.AreSame(mid, sim.At(5, 5), "the same flower, back in its cell");
            Assert.AreEqual(0, sim.Score);
            Assert.AreEqual(20f, sim.TimeLeft, 1e-4f, "the clock doesn't run while flowers move");
            Assert.AreEqual(0, sim.Matches);
        }

        [Test]
        public void ADrawnPosition_SlidesBetweenTheCells_DuringASwap()
        {
            var sim = Board(Cascade);
            var a = sim.At(2, 0);
            var b = sim.At(2, 1);
            Assert.AreEqual(new Vector2(2, 0), sim.DrawPos(a));
            sim.Swap(new Vector2Int(2, 0), Vector2Int.up);
            sim.Step(PetalSim.SwapTime / 2f);
            Assert.AreEqual(0.5f, sim.DrawPos(a).y, 0.01f, "half way up");
            Assert.AreEqual(0.5f, sim.DrawPos(b).y, 0.01f, "half way down");
        }

        // --- Matching, falling, chains --------------------------------------------------------

        [Test]
        public void AMatch_Pops_Scores_AddsTime_AndCascadesAreChainsWorthMore()
        {
            var sim = Board(Cascade);
            sim.Step(Dt);
            sim.SetTime(10f);
            var popped = sim.At(0, 0);

            Assert.IsTrue(sim.Swap(new Vector2Int(2, 0), Vector2Int.up));
            var waves = new List<(int points, int chain, int count)>();
            var events = Settle(sim, waves);

            Assert.IsTrue((events & PetalEvent.Popped) != 0);
            Assert.IsTrue((events & PetalEvent.Landed) != 0);
            Assert.GreaterOrEqual(waves.Count, 2, "the drop set off a second wave");
            Assert.AreEqual((3, 1, 3), waves[0], "three 0s, chain 1: 3 points");
            Assert.AreEqual(2, waves[1].chain);
            Assert.GreaterOrEqual(waves[1].count, 6, "1s in row 1 and 3s in row 0");
            Assert.AreEqual(waves[1].count * 2, waves[1].points, "the second wave scores double");

            int total = 0, flowers = 0;
            foreach (var w in waves) { total += w.points; flowers += w.count; }
            Assert.AreEqual(total, sim.Score);
            Assert.AreEqual(Mathf.Min(PetalSim.MaxSeconds, 10f + flowers * PetalSim.SecondsPerFlower), sim.TimeLeft, 1e-3f);
            Assert.AreEqual(1, sim.Matches, "one swap, one match, however many waves");

            // Afterwards: a full garden, nothing left lined up, nothing still falling, and a move.
            Assert.IsFalse(sim.AnyLineMade);
            Assert.IsTrue(sim.HasMove);
            for (int c = 0; c < PetalSim.Size; c++)
                for (int r = 0; r < PetalSim.Size; r++)
                {
                    var f = sim.At(c, r);
                    Assert.IsNotNull(f);
                    Assert.AreEqual(0f, f.Drop);
                    Assert.AreEqual((c, r), (f.Col, f.Row), "every flower knows its cell");
                    Assert.AreNotSame(popped, f, "popped flowers are gone");
                }
        }

        [Test]
        public void Flowers_FallFromWhereTheyWere_AndNewOnesStartAboveTheBoard()
        {
            var sim = Board(Cascade);
            sim.Step(Dt);
            var above = sim.At(0, 1); // falls one row into (0, 0)
            sim.Swap(new Vector2Int(2, 0), Vector2Int.up);
            for (int i = 0; i < 200 && sim.State != PetalSim.Phase.Falling; i++) sim.Step(Dt);
            Assert.AreEqual(PetalSim.Phase.Falling, sim.State);

            Assert.AreSame(above, sim.At(0, 0));
            Assert.AreEqual(1f, sim.DrawPos(above).y, 0.2f, "it starts from where it was");
            var top = sim.At(0, PetalSim.Size - 1);
            Assert.Greater(sim.DrawPos(top).y, PetalSim.Size - 0.5f, "new flowers start above the board");
        }

        static int FirstWaveCount(string[] rows, Vector2Int cell, Vector2Int dir)
        {
            var sim = Board(rows);
            Assert.IsFalse(sim.AnyLineMade);
            sim.Step(Dt);
            Assert.IsTrue(sim.Swap(cell, dir));
            var waves = new List<(int points, int chain, int count)>();
            Settle(sim, waves);
            Assert.AreEqual(1, waves[0].chain);
            Assert.AreEqual(waves[0].count, waves[0].points);
            return waves[0].count;
        }

        [Test]
        public void FiveInARow_PopsAllFive()
        {
            // Swapping (3, 1) down drops a 0 between two pairs of 0s along the bottom.
            Assert.AreEqual(5, FirstWaveCount(new[]
            {
                "2340123",
                "0123401",
                "3401234",
                "1234012",
                "4012340",
                "2340123",
                "3001004",
            }, new Vector2Int(3, 1), Vector2Int.down));
        }

        [Test]
        public void AnL_PopsEveryFlowerOnce()
        {
            // Swapping (3, 0) left makes three 0s across and three up column 2, sharing a corner.
            Assert.AreEqual(5, FirstWaveCount(new[]
            {
                "2340123",
                "0123401",
                "3401234",
                "1234012",
                "4002340",
                "2300123",
                "0040401",
            }, new Vector2Int(3, 0), Vector2Int.left), "the corner counts once");
        }

        // --- Shuffling ------------------------------------------------------------------------

        [Test]
        public void AGardenWithNoMoves_ShufflesItself_KeepingItsFlowers()
        {
            var sim = Board(NoMoves);
            Assert.IsFalse(sim.HasMove);
            Assert.IsFalse(sim.AnyLineMade);
            sim.SetTime(12f);

            var e = sim.Step(Dt);
            Assert.IsTrue((e & PetalEvent.Shuffled) != 0);
            Assert.AreEqual(PetalSim.Phase.Shuffling, sim.State);
            Assert.IsTrue(sim.HasMove);
            Assert.IsFalse(sim.AnyLineMade);
            AssertSameColours(NoMoves, sim.Board());

            Settle(sim);
            Assert.AreEqual(PetalSim.Phase.Still, sim.State);
            Assert.AreEqual(12f, sim.TimeLeft, 1e-3f, "shuffling costs no time");
        }

        // --- The clock ------------------------------------------------------------------------

        [Test]
        public void TheClock_DrainsWhileStill_FasterAsTheScoreClimbs_AndEndsTheRun()
        {
            var sim = new PetalSim(3);
            Assert.AreEqual(1f, sim.DrawPos(sim.At(0, 1)).y);
            Assert.AreEqual(1f, sim.DrainRate);
            for (int i = 0; i < 60; i++) sim.Step(Dt);
            Assert.AreEqual(PetalSim.StartSeconds - 1f, sim.TimeLeft, 0.05f, "a second a second at first");

            sim.SetTime(0.05f);
            var all = PetalEvent.None;
            for (int i = 0; i < 10; i++) all |= sim.Step(Dt);
            Assert.IsTrue((all & PetalEvent.TimeUp) != 0);
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(0f, sim.TimeLeft);
            Assert.IsFalse(sim.Swap(new Vector2Int(0, 0), Vector2Int.right), "no swaps once it's over");

            Assert.LessOrEqual(PetalSim.MaxDrain, 1f + 1000 * PetalSim.DrainPerPoint, "the drain reaches its cap");
        }

        // --- A whole run ----------------------------------------------------------------------

        [Test]
        public void ABotTakingTheFirstMoveItFinds_NeverGetsStuck_AndScores()
        {
            for (int seed = 0; seed < 8; seed++)
            {
                var sim = new PetalSim(seed);
                int swaps = 0;
                for (int frame = 0; frame < 60 * 600 && !sim.Over; frame++)
                {
                    if (sim.State == PetalSim.Phase.Still && sim.StillFor > 1.5f)
                    {
                        Assert.IsTrue(sim.FindMove(out var cell, out var dir), $"seed {seed}: still with no move");
                        Assert.IsTrue(sim.Swap(cell, dir));
                        swaps++;
                    }
                    sim.Step(Dt);
                    Assert.LessOrEqual(sim.TimeLeft, PetalSim.MaxSeconds);
                }
                Assert.Greater(swaps, 20, $"seed {seed}");
                Assert.AreEqual(swaps, sim.Matches, $"seed {seed}: every found move made a match");
                Assert.Greater(sim.Score, swaps * 3 - 1, $"seed {seed}");
                Assert.IsTrue(sim.Over, $"seed {seed}: the run ends in the end, as the drain speeds up");
            }
        }
    }
}
