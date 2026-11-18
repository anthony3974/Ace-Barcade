using System.Collections.Generic;
using System.Linq;
using AceBarcade.Games.WingIt;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.WingIt.WingSim;

namespace AceBarcade.Tests
{
    public class WingSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        static WingEvent Run(Sim sim, float seconds, System.Func<bool> flap = null)
        {
            var all = WingEvent.None;
            for (float t = 0; t < seconds && !sim.Crashed; t += Dt)
            {
                if (flap != null && flap()) sim.Flap();
                all |= sim.Step(Dt);
            }
            return all;
        }

        static WingColumn Next(Sim sim)
        {
            foreach (var c in sim.Columns)
                if (c.X + Sim.ColumnWidth / 2f + Sim.Radius > Sim.BeeX) return c;
            return null;
        }

        /// <summary>
        /// A player with human limits: it watches all the time, but its taps land 0.12 s after it decides,
        /// and it can't tap more than about 6 times a second. It aims a little below each gap's
        /// centre (see <see cref="Target"/>) and taps when it would sink below that.
        /// </summary>
        sealed class Bot
        {
            const float LookEvery = Dt, Lag = 0.12f, MinBetweenTaps = 0.16f;
            readonly List<float> _taps = new List<float>();
            public readonly List<string> Trace = new List<string>();
            float _time, _nextLook, _lastTap = -1f;

            public bool Tick(Sim sim)
            {
                _time += Dt;
                if (_time >= _nextLook)
                {
                    _nextLook = _time + LookEvery;
                    float target = Target(sim);
                    Trace.Add($"{_time:0.00} y {sim.Y:0.00} v {sim.Velocity:0.0} aim {target:0.00}");
                    if (Trace.Count > 40) Trace.RemoveAt(0);
                    // Where it will be by the time a tap lands.
                    const float ahead = Lag;
                    float v = sim.Velocity;
                    float y = sim.Y + v * ahead - Sim.Gravity * ahead * ahead / 2f;
                    if (y < target && v - Sim.Gravity * Lag < 1f && _time - _lastTap >= MinBetweenTaps)
                    {
                        Trace.Add("  decide tap");
                        _lastTap = _time;
                        _taps.Add(_time + Lag);
                    }
                }
                if (_taps.Count > 0 && _taps[0] <= _time)
                {
                    Trace.Add("  TAP");
                    _taps.RemoveAt(0);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Where the bot aims: a little below the middle of the gap it's in or heading for, but
        /// already leaning toward the next gap's side, as a player who can see both would.
        /// </summary>
        static float Target(Sim sim)
        {
            var c = Next(sim);
            if (c == null) return Sim.StartY;
            // A flap from the target rises about 1.2 units, so aim half that below the middle.
            float rise = Sim.FlapSpeed * Sim.FlapSpeed / (2f * Sim.Gravity);
            float target = c.GapY - 0.5f;
            int i = sim.Columns.ToList().IndexOf(c);
            if (i + 1 < sim.Columns.Count && c.X - Sim.ColumnWidth / 2f - Sim.Radius < Sim.BeeX)
            {
                var n = sim.Columns[i + 1];
                if (n.GapY < c.GapY) target = Mathf.Max(n.GapY - 0.5f, c.GapBottom + Sim.Radius + 0.35f);
                else target = Mathf.Min(n.GapY - 0.5f, c.GapTop - Sim.Radius - rise - 0.1f);
            }
            return target;
        }

        // --- The bee ---------------------------------------------------------------------------

        [Test]
        public void Start_BeeAtRest_FirstColumnOffScreenWithItsGapAtTheBee()
        {
            var sim = new Sim(1);
            Assert.AreEqual(Sim.StartY, sim.Y);
            Assert.AreEqual(0f, sim.Velocity);
            Assert.AreEqual(0, sim.Columns.Count);
            sim.Flap();
            sim.Step(Dt);
            Assert.AreEqual(1, sim.Columns.Count);
            var first = sim.Columns[0];
            Assert.Greater(first.X - Sim.ColumnWidth / 2f, Style.WorldWidth / 2f, "it starts off the screen");
            Assert.AreEqual(Sim.StartY, first.GapY, 1e-4f, "the first gap is right in front of the bee");
            Assert.AreEqual(Sim.StartGap, first.Gap);
            Assert.IsFalse(first.HasGem);
        }

        [Test]
        public void Gravity_PullsTheBeeDown_FasterAndFaster_UpToAMaximum()
        {
            var sim = new Sim(1);
            sim.Step(0.1f);
            float v1 = sim.Velocity;
            sim.Step(0.1f);
            Assert.Less(sim.Velocity, v1);
            Assert.Less(sim.Y, Sim.StartY);
            for (int i = 0; i < 20; i++) sim.Step(Dt);
            Assert.GreaterOrEqual(sim.Velocity, -Sim.MaxFall);
        }

        [Test]
        public void Flap_ShootsTheBeeUp_ToAFixedHeight_WhateverItWasDoing()
        {
            var sim = new Sim(1);
            Run(sim, 0.3f);
            Assert.Less(sim.Velocity, 0f);
            float from = sim.Y;
            sim.Flap();
            Assert.AreEqual(Sim.FlapSpeed, sim.Velocity);
            float top = from;
            for (int i = 0; i < 60 && sim.Velocity > -1f; i++) { sim.Step(Dt); top = Mathf.Max(top, sim.Y); }
            float expected = Sim.FlapSpeed * Sim.FlapSpeed / (2f * Sim.Gravity);
            Assert.AreEqual(expected, top - from, 0.1f);
        }

        [Test]
        public void TheCeiling_StopsTheBee_ButTheFloorEndsTheRun()
        {
            var sim = new Sim(1);
            var events = Run(sim, 2f, () => true); // flapping every frame
            Assert.IsFalse(sim.Crashed, "bumping the ceiling is safe");
            Assert.IsTrue((events & WingEvent.Bumped) != 0);
            Assert.AreEqual(Sim.Ceiling - Sim.Radius, sim.Y, 1e-4f);

            sim = new Sim(1);
            events = Run(sim, 5f);
            Assert.IsTrue(sim.Crashed);
            Assert.IsTrue((events & WingEvent.Crashed) != 0);
            Assert.AreEqual(Sim.FloorY, sim.CrashPoint.y, 1e-4f);
            Assert.AreEqual(0, sim.Score);

            float y = sim.Y, d = sim.Distance;
            Assert.AreEqual(WingEvent.None, sim.Step(1f), "nothing moves after a crash");
            sim.Flap();
            Assert.AreEqual(y, sim.Y);
            Assert.AreEqual(d, sim.Distance);
        }

        // --- Columns ---------------------------------------------------------------------------

        [Test]
        public void Hits_TheThornsAboveAndBelowTheGap_NotTheGapOrBesideIt()
        {
            var sim = new Sim(1);
            sim.Step(Dt);
            var c = sim.Columns[0];
            var mid = new Vector2(c.X, c.GapY);
            Assert.IsFalse(Sim.Hits(c, mid, out _), "the middle of the gap");
            Assert.IsFalse(Sim.Hits(c, new Vector2(c.X, c.GapTop - Sim.Radius - 0.01f), out _));
            Assert.IsTrue(Sim.Hits(c, new Vector2(c.X, c.GapTop - Sim.Radius + 0.01f), out var at));
            Assert.AreEqual(c.GapTop, at.y, 1e-4f);
            Assert.IsTrue(Sim.Hits(c, new Vector2(c.X, c.GapBottom + Sim.Radius - 0.01f), out at));
            Assert.AreEqual(c.GapBottom, at.y, 1e-4f);
            Assert.IsTrue(Sim.Hits(c, new Vector2(c.X, c.GapTop + 2f), out _), "inside the top half");
            float beside = c.X - Sim.ColumnWidth / 2f - Sim.Radius - 0.01f;
            Assert.IsFalse(Sim.Hits(c, new Vector2(beside, c.GapTop + 2f), out _), "just left of it");
            Assert.IsTrue(Sim.Hits(c, new Vector2(beside + 0.05f, c.GapTop + 2f), out _));
        }

        [Test]
        public void Columns_AreEvenlySpaced_GapsShrinkToAMinimum_AndNeverJumpFurtherThanABeeCanFollow()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                sim.PlanAhead(200);
                var columns = sim.Columns;
                for (int i = 0; i < columns.Count; i++)
                {
                    var c = columns[i];
                    Assert.AreEqual(i, c.Index);
                    Assert.AreEqual(Sim.GapFor(i), c.Gap);
                    Assert.GreaterOrEqual(c.GapBottom, Sim.FloorY + Sim.FloorClear - 1e-4f, $"seed {seed}, column {i}");
                    Assert.LessOrEqual(c.GapTop, Sim.Ceiling - Sim.CeilingClear + 1e-4f, $"seed {seed}, column {i}");
                    Assert.AreEqual(i > 0 && i % Sim.GemEvery == Sim.GemEvery - 1, c.HasGem);
                    if (i == 0) continue;
                    Assert.AreEqual(Sim.Spacing, c.X - columns[i - 1].X, 1e-3f);
                    float shift = c.GapY - columns[i - 1].GapY;
                    Assert.That(shift, Is.InRange(-Sim.MaxDropFor(c.Gap) - 1e-4f, Sim.MaxRise + 1e-4f), $"seed {seed}, column {i}");
                    // Even flapping from the very bottom of one gap, the bee fits under the next one's top.
                    Assert.LessOrEqual(columns[i - 1].GapBottom + Sim.Radius, c.GapTop - Sim.Radius, $"seed {seed}, column {i}");
                }
                Assert.AreEqual(Sim.MinGap, columns[columns.Count - 1].Gap);
            }
        }

        [Test]
        public void PassingColumns_Scores1Each_GemsScore5_AndItSpeedsUpToACap()
        {
            var sim = new Sim(5);
            var bot = new Bot();
            int gems = 0, passed = 0;
            float lastSpeed = sim.Speed;
            for (float t = 0; t < 120f && !sim.Crashed && sim.Passed < 12; t += Dt)
            {
                if (bot.Tick(sim)) sim.Flap();
                int score = sim.Score;
                var e = sim.Step(Dt);
                if ((e & WingEvent.Passed) != 0) passed++;
                if ((e & WingEvent.Gem) != 0) gems++;
                Assert.AreEqual(score + ((e & WingEvent.Passed) != 0 ? Sim.ColumnPoints : 0) + ((e & WingEvent.Gem) != 0 ? Sim.GemPoints : 0), sim.Score);
                Assert.GreaterOrEqual(sim.Speed, lastSpeed);
                lastSpeed = sim.Speed;
            }
            Assert.IsFalse(sim.Crashed);
            Assert.AreEqual(12, passed);
            Assert.AreEqual(3, gems, "a gem in columns 4, 8 and 12, collected by flying through the middle");
            Assert.AreEqual(12 * Sim.ColumnPoints + 3 * Sim.GemPoints, sim.Score);
            Assert.AreEqual(Sim.StartSpeed + 12 * Sim.SpeedUp, sim.Speed, 1e-4f);
        }

        [Test]
        public void ABotWithHumanReactions_GetsPastTopSpeedAndTheNarrowestGaps_OnEverySeed()
        {
            int hardest = Mathf.CeilToInt(Mathf.Max((Sim.MaxSpeed - Sim.StartSpeed) / Sim.SpeedUp, (Sim.StartGap - Sim.MinGap) / Sim.GapShrink));
            int goal = hardest + 40;
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var bot = new Bot();
                for (float t = 0; t < 600f && !sim.Crashed && sim.Passed < goal; t += Dt)
                {
                    if (bot.Tick(sim)) sim.Flap();
                    sim.Step(Dt);
                }
                if (sim.Crashed)
                {
                    var c = Next(sim);
                    int i = sim.Columns.ToList().IndexOf(c);
                    string before = i > 0 ? $"{sim.Columns[i - 1].GapBottom:0.00}..{sim.Columns[i - 1].GapTop:0.00}" : "gone";
                    Assert.Fail($"seed {seed}: crashed after {sim.Passed} columns at {sim.CrashPoint}, v {sim.Velocity:0.00}; " +
                        $"column {c.Index} x {c.X:0.00} gap {c.GapBottom:0.00}..{c.GapTop:0.00}; the one before {before}, speed {sim.Speed:0.00}\n" +
                        string.Join("\n", bot.Trace));
                }
                Assert.AreEqual(goal, sim.Passed);
                Assert.AreEqual(Sim.MaxSpeed, sim.Speed, 1e-4f);
            }
        }
    }
}
