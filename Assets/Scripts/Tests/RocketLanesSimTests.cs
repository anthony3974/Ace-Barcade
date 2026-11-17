using System;
using AceBarcade.Games.RocketLanes;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.RocketLanes.RocketLanesSim;

namespace AceBarcade.Tests
{
    public class RocketLanesSimTests
    {
        const float Dt = 1f / 60f;
        const int PlannedRows = 800;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        static LaneEvent Run(Sim sim, float seconds)
        {
            var all = LaneEvent.None;
            for (float t = 0; t < seconds && !sim.Crashed; t += Dt) all |= sim.Step(Dt);
            return all;
        }

        static bool[] Mines(LaneRow row)
        {
            var m = new bool[Sim.Lanes];
            for (int lane = 0; lane < Sim.Lanes; lane++) m[lane] = row.Mine(lane);
            return m;
        }

        /// <summary>Every consecutive pair of planned rows, for every seed.</summary>
        static void ForEachPair(Action<LaneRow, LaneRow> check)
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                sim.PlanAhead(PlannedRows);
                for (int i = 1; i < sim.Rows.Count; i++) check(sim.Rows[i - 1], sim.Rows[i]);
            }
        }

        // --- Rows and fairness ----------------------------------------------------------------

        [Test]
        public void EveryRow_HasOneOrTwoMines_SoALaneIsAlwaysOpen()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                sim.PlanAhead(PlannedRows);
                foreach (var row in sim.Rows)
                {
                    int mines = 0;
                    for (int lane = 0; lane < Sim.Lanes; lane++) if (row.Mine(lane)) mines++;
                    Assert.That(mines, Is.InRange(1, 2), $"seed {seed}, row {row.Index}");
                    if (row.Index < Sim.OneMineRows) Assert.AreEqual(1, mines, "the first rows are gentle");
                    if (row.CrystalLane >= 0) Assert.IsTrue(row.Open(row.CrystalLane), "crystals sit in open lanes");
                }
            }
        }

        [Test]
        public void OpenLane_NeverMovesMoreThanOneLane()
        {
            ForEachPair((before, after) =>
                Assert.IsTrue(Sim.Fair(Mines(before), Mines(after), 1),
                    $"rows {before.Index}->{after.Index}: from every open lane, an open lane is at most one step away"));
        }

        [Test]
        public void TimeGapRule_HoldsAcrossTheWholeRamp()
        {
            int moves = 0, tight = 0;
            ForEachPair((before, after) =>
            {
                float speed = Sim.SpeedAt(after.Index);
                float gap = (after.Y - before.Y) / speed;
                Assert.AreEqual(after.GapSeconds, gap, 1e-3f, "rows are spaced by their gap in seconds at the speed they arrive at");

                bool mustMove = !Sim.Fair(Mines(before), Mines(after), 0);
                if (mustMove)
                {
                    moves++;
                    Assert.GreaterOrEqual(Sim.Window(gap, speed), Sim.MoveTime,
                        $"row {after.Index} makes the player move, so it leaves at least reaction plus swipe time");
                }
                if (Sim.Window(gap, speed) < Sim.MoveTime)
                {
                    tight++;
                    Assert.IsTrue(after.Tight, $"only tight rows leave no time to move (row {after.Index})");
                }
            });
            Assert.Greater(moves, 1000, "plenty of rows make the player move");
            Assert.Greater(tight, 100, "tight rows happen too");

            // The ramp itself: a normal gap at any point leaves time for a move, even at full speed.
            for (int k = 0; k <= 5000; k++)
                Assert.GreaterOrEqual(Sim.Window(Sim.GapAt(k), Sim.SpeedAt(k)), Sim.MoveTime, $"row {k}");
            Assert.GreaterOrEqual(Sim.SwipeTime, Sim.LaneMoveTime, "a swipe's time covers the rocket's slide");
        }

        [Test]
        public void Speed_RisesALittleEachRow_UpToTheCap_AndTheGapShrinksToItsFloor()
        {
            Assert.AreEqual(Sim.StartSpeed, Sim.SpeedAt(0), 1e-5f);
            Assert.AreEqual(Sim.StartSpeed + 10 * Sim.SpeedPerRow, Sim.SpeedAt(10), 1e-4f);
            Assert.AreEqual(Sim.MaxSpeed, Sim.SpeedAt(1000), 1e-5f);
            for (int k = 1; k < 200; k++)
            {
                Assert.GreaterOrEqual(Sim.SpeedAt(k), Sim.SpeedAt(k - 1));
                Assert.LessOrEqual(Sim.GapAt(k), Sim.GapAt(k - 1));
            }
            Assert.AreEqual(Sim.MinGap, Sim.GapAt(1000), 1e-5f);

            var sim = new Sim();
            sim.ClearRows();
            sim.AddRow(Sim.PlayerY + 1.5f, -1, 0);
            float before = sim.Speed;
            Run(sim, 1f);
            Assert.AreEqual(1, sim.RowsPassed);
            Assert.AreEqual(before + Sim.SpeedPerRow, sim.Speed, 1e-4f, "passing a row speeds the scroll up");
        }

        // --- Scoring and collisions -----------------------------------------------------------

        [Test]
        public void PassingARow_Scores1()
        {
            var sim = new Sim();
            sim.ClearRows();
            sim.AddRow(Sim.PlayerY + 1.5f, -1, 0, 2);
            var e = Run(sim, 1f);
            Assert.IsTrue((e & LaneEvent.Passed) != 0);
            Assert.IsFalse(sim.Crashed, "the middle lane was open");
            Assert.AreEqual(Sim.RowPoints, sim.Score);
        }

        [Test]
        public void Crystal_InYourLane_Scores5()
        {
            var sim = new Sim();
            sim.ClearRows();
            var row = sim.AddRow(Sim.PlayerY + 1.5f, 1, 0);
            var e = Run(sim, 1f);
            Assert.IsTrue((e & LaneEvent.Crystal) != 0);
            Assert.IsTrue(row.CrystalTaken);
            Assert.AreEqual(1, sim.Crystals);
            Assert.AreEqual(Sim.RowPoints + Sim.CrystalPoints, sim.Score);

            // A crystal in another lane is missed.
            var sim2 = new Sim();
            sim2.ClearRows();
            sim2.AddRow(Sim.PlayerY + 1.5f, 2, 0);
            Run(sim2, 1f);
            Assert.AreEqual(0, sim2.Crystals);
            Assert.AreEqual(Sim.RowPoints, sim2.Score);
        }

        [Test]
        public void MineInYourLane_Crashes_AndStopsTheRun()
        {
            var sim = new Sim();
            sim.ClearRows();
            sim.AddRow(Sim.PlayerY + 1.5f, -1, 1);
            var e = Run(sim, 1f);
            Assert.IsTrue((e & LaneEvent.Crashed) != 0);
            Assert.IsTrue(sim.Crashed);
            Assert.AreEqual(0, sim.Score);
            Assert.AreEqual(0f, sim.CrashPoint.x, 0.01f);
            Assert.That(sim.CrashPoint.y, Is.InRange(Sim.PlayerY, Sim.PlayerY + Sim.HitDepth));
            Assert.AreEqual(LaneEvent.None, sim.Step(Dt), "nothing happens after a crash");
            Assert.IsFalse(sim.Swipe(1));
        }

        [Test]
        public void SwipingIntoAMineAlongside_Crashes()
        {
            var sim = new Sim();
            sim.ClearRows();
            sim.AddRow(Sim.PlayerY + 0.2f, -1, 2);
            Assert.AreEqual(LaneEvent.None, sim.Step(Dt), "the mine beside the rocket doesn't hit it");
            sim.Swipe(1);
            Run(sim, 0.3f);
            Assert.IsTrue(sim.Crashed);
        }

        // --- Swiping --------------------------------------------------------------------------

        [Test]
        public void Swipe_MovesOneLane_ClampsAtTheEdges_AndQuickSwipesAddUp()
        {
            var sim = new Sim();
            sim.ClearRows();
            Assert.AreEqual(1, sim.Lane);
            Assert.AreEqual(0f, sim.PlayerX);

            Assert.IsTrue(sim.Swipe(1));
            Assert.AreEqual(2, sim.Lane);
            sim.Step(Sim.LaneMoveTime / 2f);
            Assert.That(sim.PlayerX, Is.InRange(0.1f, Sim.LaneX(2) - 0.1f), "the rocket slides, not jumps");
            Run(sim, Sim.LaneMoveTime);
            Assert.AreEqual(Sim.LaneX(2), sim.PlayerX, 1e-4f, "and arrives within the lane move time");

            Assert.IsFalse(sim.Swipe(1), "already in the right lane");
            Assert.AreEqual(2, sim.Lane);

            // Two quick swipes before the rocket arrives: two lanes.
            Assert.IsTrue(sim.Swipe(-1));
            sim.Step(Dt);
            Assert.IsTrue(sim.Swipe(-1));
            Assert.AreEqual(0, sim.Lane);
            Run(sim, Sim.LaneMoveTime * 2f + Dt);
            Assert.AreEqual(Sim.LaneX(0), sim.PlayerX, 1e-4f);
            Assert.IsFalse(sim.Swipe(-1));
        }

        // --- Winnable -------------------------------------------------------------------------

        /// <summary>
        /// A player who only starts thinking about a row once the row before it is clear of the
        /// rocket, then takes the full reaction time, and swipes no faster than once per swipe time.
        /// </summary>
        static void PlayWithReaction(Sim sim, float seconds)
        {
            LaneRow watching = null;
            float watchingSince = 0f, lastSwipe = -1f;
            for (float t = 0f; t < seconds && !sim.Crashed; t += Dt)
            {
                LaneRow next = null;
                foreach (var row in sim.Rows)
                    if (row.Y > Sim.PlayerY - Sim.HitDepth) { next = row; break; }
                if (next != watching) { watching = next; watchingSince = t; }

                if (next != null && next.Y - Sim.PlayerY >= Sim.HitDepth && !next.Open(sim.Lane)
                    && t - watchingSince >= Sim.ReactionTime && t - lastSwipe >= Sim.SwipeTime)
                {
                    int best = -1;
                    for (int lane = 0; lane < Sim.Lanes; lane++)
                        if (next.Open(lane) && (best < 0 || Mathf.Abs(lane - sim.Lane) < Mathf.Abs(best - sim.Lane))) best = lane;
                    sim.Swipe(Math.Sign(best - sim.Lane));
                    lastSwipe = t;
                }
                sim.Step(Dt);
            }
        }

        [Test]
        public void APlayerWithHumanReactions_SurvivesPastTopSpeed()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                PlayWithReaction(sim, 150f);
                Assert.IsFalse(sim.Crashed, $"seed {seed}: crashed at row {sim.RowsPassed}");
                Assert.AreEqual(Sim.MaxSpeed, sim.Speed, 1e-4f, $"seed {seed}: reached top speed");
                Assert.Greater(sim.RowsPassed, 150);
                Assert.GreaterOrEqual(sim.Score, sim.RowsPassed);
            }
        }

        [Test]
        public void StandingStill_CrashesSoonerOrLater()
        {
            var sim = new Sim(5);
            Run(sim, 120f);
            Assert.IsTrue(sim.Crashed, "rows don't leave the middle lane open forever");
        }
    }
}
