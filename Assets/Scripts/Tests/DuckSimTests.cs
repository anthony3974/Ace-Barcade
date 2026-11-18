using AceBarcade.Games.DuckRunner;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.DuckRunner.DuckSim;

namespace AceBarcade.Tests
{
    public class DuckSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 12, 42, 1234, 99999 };

        static DuckEvent Run(Sim sim, float seconds)
        {
            var all = DuckEvent.None;
            for (float t = 0; t < seconds && !sim.Crashed; t += Dt) all |= sim.Step(Dt);
            return all;
        }

        /// <summary>An empty track with one obstacle that reaches the duck in <paramref name="seconds"/>.</summary>
        static Sim WithOne(ObstacleKind kind, float seconds)
        {
            var sim = new Sim(0);
            sim.Clear();
            sim.Add(kind, Sim.ContactX + seconds * (sim.Speed + Sim.SpecOf(kind).OwnSpeed));
            return sim;
        }

        static readonly float[] Speeds = { Sim.StartSpeed, 7f, 8f, 9f, 10f, Sim.MaxSpeed };

        // --- Shapes and timing -------------------------------------------------------------

        [Test]
        public void EveryJump_HasAWindowAtLeastMinSlackLong_AtEverySpeed()
        {
            foreach (var kind in Sim.Kinds)
            {
                if (Sim.SpecOf(kind).High) continue;
                foreach (float speed in Speeds)
                {
                    var (early, late) = Sim.JumpWindow(kind, speed);
                    Assert.GreaterOrEqual(late - early, Sim.MinSlack, $"{kind} at {speed}");
                }
            }
        }

        [Test]
        public void HighObstacles_HitAStandingDuck_ButNotADuckingOne_AndBranchesCantBeJumped()
        {
            foreach (var kind in Sim.Kinds)
            {
                var spec = Sim.SpecOf(kind);
                if (!spec.High)
                {
                    Assert.Less(spec.Top, Sim.Apex - 0.5f, $"{kind} can be jumped with room to spare");
                    continue;
                }
                Assert.Greater(spec.Bottom, Sim.DuckHeight, $"{kind} clears a ducking duck");
                Assert.Less(spec.Bottom, Sim.StandHeight, $"{kind} hits a standing duck");
            }
            Assert.AreEqual(0f, Sim.TimeAbove(Sim.SpecOf(ObstacleKind.Branch).Top), "a branch hangs too low to jump over");
        }

        [Test]
        public void StandingStill_RunsIntoAGroundObstacle()
        {
            var sim = WithOne(ObstacleKind.Log, 1f);
            Assert.IsTrue((Run(sim, 2f) & DuckEvent.Crashed) != 0);
            Assert.AreEqual(0, sim.Score);
        }

        [Test]
        public void JumpingInTheWindow_ClearsEachGroundObstacle_AndScores()
        {
            foreach (var kind in Sim.Kinds)
            {
                if (Sim.SpecOf(kind).High) continue;
                foreach (float t in new[] { 0.03f, 0.5f, 0.97f })
                {
                    var sim = WithOne(kind, 1.5f);
                    var (early, late) = Sim.JumpWindow(kind, sim.Speed);
                    float jumpAt = 1.5f + Mathf.Lerp(early + 0.02f, late - 0.02f, t);
                    Run(sim, jumpAt);
                    Assert.AreEqual(DuckEvent.Jumped, sim.Press());
                    Run(sim, 2f);
                    Assert.IsFalse(sim.Crashed, $"{kind}, jumping at {t:P0} of its window");
                    Assert.AreEqual(Sim.PassPoints, sim.Score);
                }
            }
        }

        [Test]
        public void JumpingFarTooEarly_LandsOnTheLog()
        {
            var sim = WithOne(ObstacleKind.Log, 1.5f);
            var (early, _) = Sim.JumpWindow(ObstacleKind.Log, sim.Speed);
            Run(sim, 1.5f + early - 0.15f);
            sim.Press();
            Run(sim, 2f);
            Assert.IsTrue(sim.Crashed);
        }

        [Test]
        public void DuckingUnderHighObstacles_Passes_StandingHitsThem()
        {
            foreach (var kind in new[] { ObstacleKind.Hawk, ObstacleKind.Branch })
            {
                var standing = WithOne(kind, 1f);
                Run(standing, 2f);
                Assert.IsTrue(standing.Crashed, $"{kind} hits a standing duck");

                var ducking = WithOne(kind, 1f);
                Run(ducking, 0.5f);
                Assert.AreEqual(DuckEvent.Ducked, ducking.SwipeDown());
                Assert.IsTrue(ducking.Ducking);
                Run(ducking, 1.5f);
                Assert.IsFalse(ducking.Crashed, $"{kind} passes over a ducking duck");
                Assert.AreEqual(1, ducking.Passed);
            }
        }

        [Test]
        public void ASwipeDown_StartsWithAHop_ThatDivesBackDown_InTimeForAHawk()
        {
            // The touch that starts a swipe jumps; the swipe is seen a few frames later.
            var sim = WithOne(ObstacleKind.Hawk, 1f);
            Run(sim, 1f - Sim.DuckLead);
            Assert.AreEqual(DuckEvent.Jumped, sim.Press());
            Run(sim, 0.1f);
            Assert.IsFalse(sim.OnGround);
            Assert.AreEqual(DuckEvent.Dived, sim.SwipeDown());
            var events = Run(sim, 0.06f);
            Assert.IsTrue((events & DuckEvent.Landed) != 0, "a dive lands fast");
            Assert.IsTrue(sim.Ducking);
            Run(sim, 1f);
            Assert.IsFalse(sim.Crashed);
        }

        [Test]
        public void AQuickSwipe_DucksForAMoment_HoldingKeepsDucking()
        {
            var sim = new Sim(0);
            sim.Clear();
            sim.SwipeDown();
            sim.Release();
            Run(sim, Sim.DuckMin - 0.05f);
            Assert.IsTrue(sim.Ducking);
            Run(sim, 0.1f);
            Assert.IsFalse(sim.Ducking);

            sim.SwipeDown();
            Run(sim, 2f);
            Assert.IsTrue(sim.Ducking, "still holding");
            sim.Release();
            Run(sim, Dt);
            Assert.IsFalse(sim.Ducking);
        }

        [Test]
        public void TouchingWhileDucking_Jumps()
        {
            var sim = new Sim(0);
            sim.Clear();
            sim.SwipeDown();
            sim.Release();
            Assert.AreEqual(DuckEvent.Jumped, sim.Press());
            Assert.IsFalse(sim.Ducking);
            Run(sim, 0.1f);
            Assert.Greater(sim.Y, 0.5f);
        }

        [Test]
        public void AJump_LandsAfterAirTime_AndATouchJustBeforeLandingJumpsAgain()
        {
            var sim = new Sim(0);
            sim.Clear();
            sim.Press();
            Assert.AreEqual(DuckEvent.None, sim.Press(), "no double jump");
            Run(sim, Sim.AirTime / 2f);
            Assert.AreEqual(Sim.Apex, sim.Y, 0.02f);
            Run(sim, Sim.AirTime / 2f - 0.08f);
            Assert.IsFalse(sim.OnGround);
            sim.Press();
            var events = Run(sim, 0.12f);
            Assert.IsTrue((events & DuckEvent.Landed) != 0);
            Assert.IsTrue((events & DuckEvent.Jumped) != 0, "the early touch jumps on landing");
            Assert.IsFalse(sim.OnGround);
        }

        [Test]
        public void ADecoy_TakesAJumpToReach_AndScoresFive()
        {
            var standing = new Sim(0);
            standing.Clear();
            standing.AddDecoy(Sim.PlayerX + 1f * standing.Speed);
            Run(standing, 2f);
            Assert.AreEqual(0, standing.Decoys, "a standing duck runs under it");

            var jumping = new Sim(0);
            jumping.Clear();
            jumping.AddDecoy(Sim.PlayerX + 1f * jumping.Speed);
            Run(jumping, 1f - Sim.JumpSpeed / Sim.Gravity);
            jumping.Press();
            Assert.IsTrue((Run(jumping, 1f) & DuckEvent.Decoy) != 0);
            Assert.AreEqual(1, jumping.Decoys);
            Assert.AreEqual(Sim.DecoyPoints, jumping.Score);
        }

        // --- Planning ----------------------------------------------------------------------

        [Test]
        public void PlannedGaps_AreFair_AndNeverShorterThanTheBaseGap()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                sim.PlanAhead(300);
                var list = sim.Obstacles;
                for (int i = 1; i < list.Count; i++)
                {
                    var a = list[i - 1];
                    var b = list[i];
                    Assert.AreNotEqual(a.Kind, b.Kind, "never the same thing twice in a row");
                    float fair = Sim.FairGap(a.Kind, Sim.SpeedAt(a.Index), b.Kind, Sim.SpeedAt(b.Index));
                    Assert.GreaterOrEqual(b.Gap, fair - 1e-4f, $"seed {seed}, obstacle {b.Index}");
                    Assert.GreaterOrEqual(b.Gap, Sim.BaseGapAt(b.Index) - 1e-4f);
                }
            }
        }

        [Test]
        public void TheRunStartsEasy_AndGetsFaster_UpToACap()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                sim.PlanAhead(80);
                foreach (var o in sim.Obstacles)
                {
                    if (o.Index < Sim.HighFrom) Assert.IsFalse(o.Spec.High, "nothing to duck under at first");
                    if (o.Index < Sim.CattailsFrom) Assert.AreNotEqual(ObstacleKind.Cattails, o.Kind);
                }
            }
            Assert.AreEqual(Sim.StartSpeed, Sim.SpeedAt(0));
            Assert.Greater(Sim.SpeedAt(20), Sim.SpeedAt(10));
            Assert.AreEqual(Sim.MaxSpeed, Sim.SpeedAt(1000));
            Assert.AreEqual(Sim.MinGap, Sim.BaseGapAt(1000));
        }

        [Test]
        public void TheSameSeed_PlansTheSameRun()
        {
            var a = new Sim(5);
            var b = new Sim(5);
            a.PlanAhead(50);
            b.PlanAhead(50);
            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual(a.Obstacles[i].Kind, b.Obstacles[i].Kind);
                Assert.AreEqual(a.Obstacles[i].X, b.Obstacles[i].X);
            }
        }

        // --- Bots with human timing ----------------------------------------------------------

        enum Timing { Random, Early, Late }

        /// <summary>
        /// Plays like a person who sees what's coming: jumps somewhere in each window (not on its
        /// very edge), touches down for a swipe before a high obstacle and only gets the swipe
        /// seen 0.1 s later, holds the duck until it's past, and maybe jumps for decoys (then waits
        /// to land before the next jump, as a person would).
        /// </summary>
        sealed class Bot
        {
            const float Margin = 0.03f;
            const int SwipeFrames = 6;

            readonly Sim _sim;
            readonly System.Random _random;
            readonly Timing _timing;
            readonly bool _decoys;

            int _planned = -1;
            float _actAt;
            bool _acted;
            int _swipeIn = -1;
            bool _holding, _tapUp;
            DuckObstacle _holdFor;
            float _releaseAfter;
            DuckDecoy _decoyPlanned;

            public Bot(Sim sim, int seed, Timing timing, bool decoys)
            {
                _sim = sim;
                _random = new System.Random(seed);
                _timing = timing;
                _decoys = decoys;
            }

            float Pick(float lo, float hi) => _timing == Timing.Early ? lo
                : _timing == Timing.Late ? hi
                : Mathf.Lerp(lo, hi, (float)_random.NextDouble());

            DuckObstacle Next()
            {
                foreach (var o in _sim.Obstacles) if (!o.Passed) return o;
                return null;
            }

            public void Frame()
            {
                if (_tapUp) { _sim.Release(); _tapUp = false; }
                if (_swipeIn >= 0 && _swipeIn-- == 0) _sim.SwipeDown();

                var next = Next();
                if (next == null) return;
                var spec = next.Spec;
                float speed = _sim.Speed;
                float toContact = (next.X - Sim.ContactX) / (speed + spec.OwnSpeed);

                if (_holding && _holdFor.Passed && Time() >= _releaseAfter)
                {
                    _sim.Release();
                    _holding = false;
                }

                if (next.Index != _planned)
                {
                    _planned = next.Index;
                    _acted = false;
                    if (spec.High) _actAt = Pick(Sim.DuckLead + 0.3f, Sim.DuckLead);
                    else
                    {
                        var (early, late) = Sim.JumpWindow(next.Kind, speed);
                        _actAt = -Pick(early + Margin, late - Margin);
                    }
                }

                if (_decoys && !_holding && _sim.OnGround && _decoyPlanned == null)
                    foreach (var d in _sim.DecoyList)
                    {
                        if (d.Taken || d.X < Sim.PlayerX) continue;
                        float toDecoy = (d.X - Sim.PlayerX) / speed;
                        if (toDecoy < toContact && toDecoy <= Sim.JumpSpeed / Sim.Gravity) { Tap(); _decoyPlanned = d; }
                        break;
                    }
                if (_decoyPlanned != null && _sim.OnGround && !_sim.Ducking && _decoyPlanned.X < Sim.PlayerX) _decoyPlanned = null;

                if (_acted || _holding || toContact > _actAt) return;
                // Still coming down from a decoy jump: wait to land (there's Recover to spare).
                if (!spec.High && !_sim.OnGround) return;
                _acted = true;
                if (spec.High)
                {
                    _sim.Press();
                    _swipeIn = SwipeFrames;
                    _holding = true;
                    _holdFor = next;
                    _releaseAfter = Time() + toContact + Sim.Overlap(next.Kind, speed) + (float)_random.NextDouble() * 0.1f;
                }
                else Tap();
            }

            void Tap()
            {
                _sim.Press();
                _tapUp = true;
            }

            float Time() => _sim.Time;
        }

        static void Survives(Timing timing, bool decoys, int seeds, int obstacles)
        {
            int totalDecoys = 0;
            for (int seed = 0; seed < seeds; seed++)
            {
                var sim = new Sim(seed);
                var bot = new Bot(sim, seed * 31 + 1, timing, decoys);
                for (int frame = 0; frame < 60 * 60 * 10 && sim.Passed < obstacles; frame++)
                {
                    bot.Frame();
                    sim.Step(Dt);
                    if (sim.Crashed)
                    {
                        var o = sim.Obstacles;
                        string near = "";
                        foreach (var x in o) if (!x.Passed) { near = $"{x.Kind} #{x.Index} (gap {x.Gap:F2})"; break; }
                        Assert.Fail($"{timing} bot crashed on seed {seed} after {sim.Passed} obstacles, into {near} at {sim.CrashPoint}, speed {sim.Speed:F1}");
                    }
                }
                Assert.GreaterOrEqual(sim.Passed, obstacles);
                totalDecoys += sim.Decoys;
            }
            if (decoys) Assert.Greater(totalDecoys, seeds * 3, "a bot that goes for decoys gets plenty");
        }

        [Test]
        public void ABotWithHumanTiming_SurvivesPastTopSpeed() => Survives(Timing.Random, false, 30, 120);

        [Test]
        public void ABotThatAlwaysActsAsLateAsItCan_Survives() => Survives(Timing.Late, false, 12, 120);

        [Test]
        public void ABotThatAlwaysActsAsEarlyAsItCan_Survives() => Survives(Timing.Early, false, 12, 120);

        [Test]
        public void ABotThatJumpsForEveryDecoy_Survives_AndCollectsThem() => Survives(Timing.Random, true, 20, 120);
    }
}
