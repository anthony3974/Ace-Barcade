using System.Collections.Generic;
using AceBarcade.Games.LilyLeap;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.LilyLeap.LeapSim;

namespace AceBarcade.Tests
{
    public class LeapSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        static void StepFor(Sim sim, float seconds, List<LeapEvent> log = null)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                var e = sim.Step(Dt);
                log?.Add(e);
            }
        }

        /// <summary>Leap at a pad and step until the frog comes down. Returns every event on the way.</summary>
        static LeapEvent LeapTo(Sim sim, LeapPad pad)
        {
            Assert.IsTrue(sim.Jump(sim.PullToHit(pad)), "the leap starts");
            var all = LeapEvent.Jumped;
            for (int i = 0; i < 120 && sim.Flying; i++) all |= sim.Step(Dt);
            Assert.IsFalse(sim.Flying, "and comes down within two seconds");
            return all;
        }

        /// <summary>
        /// A player that always leaps for the next pad up the path, a moment after landing, leading
        /// drifting pads. Plays until it reaches a row (or something stops it).
        /// </summary>
        static void PlayTo(Sim sim, int row, System.Action<Sim> onLand = null, float wait = 0.25f)
        {
            float rest = 0f;
            for (int i = 0; i < 60 * 600 && sim.BestRow < row && !sim.Over; i++)
            {
                var e = sim.Step(Dt);
                if ((e & LeapEvent.Landed) != 0) { rest = 0f; onLand?.Invoke(sim); }
                if (sim.Flying || sim.Over) continue;
                rest += Dt;
                if (rest < wait) continue;
                var next = sim.NextPathPad();
                if (next != null) sim.Jump(sim.PullToHit(next));
            }
        }

        [Test]
        public void StartsOnTheBigPad_WithNothingScored()
        {
            var sim = new Sim(1);
            Assert.AreEqual(PadKind.Start, sim.OnPad.Kind);
            Assert.AreEqual(new Vector2(0f, Sim.StartY), sim.Frog);
            Assert.IsFalse(sim.Flying);
            Assert.AreEqual(0, sim.Score);
            Assert.AreEqual(0f, sim.CamY);
            Assert.IsNotNull(sim.NextPathPad());
        }

        [Test]
        public void PullGoesTheOtherWay_ScaledAndCapped()
        {
            Assert.AreEqual(Vector2.zero, Sim.JumpFor(new Vector2(0f, Sim.MinPull * 0.9f)), "a short pull is let go");
            var half = Sim.JumpFor(new Vector2(0f, Sim.MaxPull / 2f));
            Assert.AreEqual(Sim.MaxJump / 2f, half.y, 1e-4f);
            Assert.AreEqual(0f, half.x, 1e-4f);
            Assert.AreEqual(Sim.MaxJump, Sim.JumpFor(new Vector2(Sim.MaxPull * 3f, 0f)).magnitude, 1e-4f, "capped");

            var jump = new Vector2(-2.3f, 3.1f);
            Assert.That(Vector2.Distance(jump, Sim.JumpFor(Sim.PullFor(jump))), Is.LessThan(1e-4f));

            var sim = new Sim(3);
            Assert.That(Vector2.Distance(sim.Frog + Sim.JumpFor(new Vector2(1f, 2f)), sim.AimPoint(new Vector2(1f, 2f))), Is.LessThan(1e-5f));
        }

        [Test]
        public void ShortPull_DoesNotJump()
        {
            var sim = new Sim(2);
            Assert.IsFalse(sim.Jump(new Vector2(0.1f, 0.1f)));
            Assert.IsFalse(sim.Flying);
            Assert.AreEqual(0, sim.Jumps);
        }

        [Test]
        public void LeapingOntoTheNextPad_LandsAndScores()
        {
            var sim = new Sim(4);
            var next = sim.NextPathPad();
            Assert.AreEqual(1, next.Row);
            var events = LeapTo(sim, next);
            Assert.IsTrue((events & LeapEvent.Landed) != 0);
            Assert.AreSame(next, sim.OnPad);
            Assert.AreEqual(1, sim.BestRow);
            Assert.GreaterOrEqual(sim.Score, 1);
            Assert.That(Vector2.Distance(sim.Frog, next.At(sim.Time)), Is.LessThanOrEqualTo(next.Radius));
        }

        [Test]
        public void CannotLeapAgain_WhileInTheAir()
        {
            var sim = new Sim(5);
            Assert.IsTrue(sim.Jump(sim.PullToHit(sim.NextPathPad())));
            sim.Step(Dt);
            Assert.IsTrue(sim.Flying);
            Assert.IsFalse(sim.Jump(new Vector2(0f, 2f)));
            Assert.AreEqual(1, sim.Jumps);
        }

        [Test]
        public void MissingEveryPad_SplashesAndEndsTheRun()
        {
            var sim = new Sim(6);
            // Find a spot within reach with no pad anywhere near it.
            Vector2 spot = Vector2.zero;
            bool found = false;
            for (int a = 0; a < 72 && !found; a++)
                for (float d = 2f; d <= Sim.MaxJump && !found; d += 0.5f)
                {
                    var p = sim.Frog + new Vector2(Mathf.Cos(a * 5f * Mathf.Deg2Rad), Mathf.Sin(a * 5f * Mathf.Deg2Rad)) * d;
                    bool clear = true;
                    foreach (var pad in sim.Pads)
                        if (Vector2.Distance(p, pad.At(0f)) < pad.Radius + pad.Amp + 0.6f) clear = false;
                    if (clear) { spot = p; found = true; }
                }
            Assert.IsTrue(found);

            Assert.IsTrue(sim.Jump(Sim.PullFor(spot - sim.Frog)));
            var all = LeapEvent.None;
            for (int i = 0; i < 120 && !sim.Over; i++) all |= sim.Step(Dt);
            Assert.IsTrue(sim.Over);
            Assert.IsTrue((all & LeapEvent.Splashed) != 0);
            Assert.AreEqual(0, sim.Score);
            Assert.That(Vector2.Distance(spot, sim.SplashPoint), Is.LessThan(1e-3f));
            Assert.AreEqual(LeapEvent.None, sim.Step(Dt), "nothing happens after");
            Assert.IsFalse(sim.Jump(new Vector2(0f, 2f)));
        }

        [Test]
        public void ThePath_IsAlwaysWithinReach_InBoundsAndNeverOverlapping([ValueSource(nameof(Seeds))] int seed)
        {
            var sim = new Sim(seed);
            var path = new SortedDictionary<int, LeapPad>();
            int overlaps = 0;
            void Check(Sim s)
            {
                var pads = s.Pads;
                for (int i = 0; i < pads.Count; i++)
                {
                    var a = pads[i];
                    if (a.IsPath) path[a.Row] = a;
                    Assert.LessOrEqual(Mathf.Abs(a.BaseX) + a.Amp + a.Radius, Sim.HalfWidth - Sim.Margin + 1e-4f, $"pad {a.Index} stays on the screen");
                    for (int j = i + 1; j < pads.Count; j++)
                    {
                        var b = pads[j];
                        if (a.Sunk || b.Sunk) continue;
                        if (Sim.Overlaps(a.BaseX, a.Y, a.Radius, a.Amp, b.BaseX, b.Y, b.Radius, b.Amp)) overlaps++;
                    }
                }
            }
            Check(sim);
            PlayTo(sim, 100, Check);
            Assert.IsFalse(sim.Over, $"seed {seed}: a perfect player never falls in (row {sim.BestRow})");
            Assert.GreaterOrEqual(sim.BestRow, 100);
            Assert.AreEqual(0, overlaps, "no two pads ever touch");

            LeapPad prev = null;
            foreach (var pad in path.Values)
            {
                if (prev != null && pad.Row == prev.Row + 1)
                {
                    float dx = Mathf.Abs(pad.BaseX - prev.BaseX) + pad.Amp + prev.Amp;
                    float dy = pad.Y - prev.Y;
                    Assert.Greater(dy, 0f);
                    Assert.LessOrEqual(Mathf.Sqrt(dx * dx + dy * dy), Sim.Reach + 1e-3f, $"row {pad.Row} is within reach of row {prev.Row}, drift and all");
                }
                prev = pad;
            }
        }

        [Test]
        public void ItGetsHarder_DriftingAndSinkingPadsComeIn()
        {
            int drifting = 0, sinking = 0, gems = 0;
            float firstGap = 0f, lateGap = 0f;
            foreach (var seed in Seeds)
            {
                var sim = new Sim(seed);
                var seen = new Dictionary<int, LeapPad>();
                PlayTo(sim, 80, s => { foreach (var p in s.Pads) seen[p.Index] = p; });
                foreach (var p in seen.Values)
                {
                    if (p.Kind == PadKind.Drifting) { drifting++; Assert.GreaterOrEqual(p.Row, 6, "no drifting pads at the start"); }
                    if (p.Kind == PadKind.Sinking) { sinking++; Assert.GreaterOrEqual(p.Row, 12); }
                    if (p.HasGem) gems++;
                }
                var byRow = new SortedDictionary<int, LeapPad>();
                foreach (var p in seen.Values) if (p.IsPath) byRow[p.Row] = p;
                firstGap += byRow[5].Y - byRow[1].Y;
                lateGap += byRow[79].Y - byRow[75].Y;
            }
            Assert.Greater(drifting, 20);
            Assert.Greater(sinking, 10);
            Assert.Greater(gems, 10);
            Assert.Greater(lateGap, firstGap * 1.2f, "the gaps grow");
        }

        [Test]
        public void ADriftingPad_CarriesTheFrog()
        {
            foreach (var seed in Seeds)
            {
                var sim = new Sim(seed);
                bool stop = false;
                for (int i = 0; i < 200 && !stop && !sim.Over; i++)
                {
                    var next = sim.NextPathPad();
                    LeapTo(sim, next);
                    if (sim.OnPad != null && sim.OnPad.Kind == PadKind.Drifting) stop = true;
                }
                if (!stop) continue;
                var pad = sim.OnPad;
                float x0 = sim.Frog.x;
                StepFor(sim, 0.6f);
                Assert.AreSame(pad, sim.OnPad);
                Assert.AreNotEqual(x0, sim.Frog.x, "it moves with the pad");
                Assert.AreEqual(pad.X + sim.Offset.x, sim.Frog.x, 1e-4f);
                return;
            }
            Assert.Fail("no seed reached a drifting pad");
        }

        [Test]
        public void ASinkingPad_GoesUnder_TakingTheFrogIfItStays()
        {
            foreach (var seed in Seeds)
            {
                var sim = new Sim(seed);
                var events = LeapEvent.None;
                for (int i = 0; i < 200 && !sim.Over; i++)
                {
                    events = LeapTo(sim, sim.NextPathPad());
                    if (sim.OnPad != null && sim.OnPad.Kind == PadKind.Sinking) break;
                }
                if (sim.OnPad == null || sim.OnPad.Kind != PadKind.Sinking) continue;
                Assert.IsTrue((events & LeapEvent.SinkStarted) != 0);
                var pad = sim.OnPad;
                int score = sim.Score;

                var log = new List<LeapEvent>();
                StepFor(sim, Sim.SinkDelay * 0.8f, log);
                Assert.IsFalse(sim.Over, "safe for a moment");
                Assert.Greater(pad.Sinking(sim.Time), 0.5f);
                StepFor(sim, Sim.SinkDelay * 0.4f, log);
                Assert.IsTrue(sim.Over, "then under it goes");
                Assert.IsTrue(pad.Sunk);
                var all = LeapEvent.None;
                foreach (var e in log) all |= e;
                Assert.IsTrue((all & LeapEvent.Sank) != 0 && (all & LeapEvent.Splashed) != 0);
                Assert.AreEqual(score, sim.Score);
                return;
            }
            Assert.Fail("no seed reached a sinking pad");
        }

        [Test]
        public void LeavingASinkingPadInTime_IsSafe()
        {
            var sim = new Sim(42);
            int sinkingLandings = 0;
            PlayTo(sim, 60, s => { if (s.OnPad.Kind == PadKind.Sinking) sinkingLandings++; }, wait: 0.5f);
            Assert.Greater(sinkingLandings, 0, "it landed on sinking pads");
            Assert.IsFalse(sim.Over, "and hopped off each before it went under");
            Assert.GreaterOrEqual(sim.BestRow, 60);
        }

        [Test]
        public void Gems_ScoreFive()
        {
            foreach (var seed in Seeds)
            {
                var sim = new Sim(seed);
                for (int i = 0; i < 300 && !sim.Over && sim.Gems == 0; i++)
                {
                    LeapPad target = null;
                    foreach (var p in sim.Pads)
                        if (p.HasGem && !p.GemTaken && !p.Sunk && p.Y > sim.Frog.y && Vector2.Distance(p.At(sim.Time), sim.Frog) + p.Amp < Sim.MaxJump * 0.9f && p.Kind != PadKind.Sinking)
                            target = p;
                    var events = LeapTo(sim, target ?? sim.NextPathPad());
                    if (target != null)
                    {
                        Assert.IsTrue((events & LeapEvent.Gem) != 0);
                        Assert.IsTrue(target.GemTaken);
                        Assert.AreEqual(sim.BestRow + Sim.GemPoints, sim.Score);
                        return;
                    }
                }
            }
            Assert.Fail("no gem was ever in reach");
        }

        [Test]
        public void TheViewClimbs_AndNeverComesBack()
        {
            var sim = new Sim(7);
            float lowest = 0f, last = 0f;
            PlayTo(sim, 30, s =>
            {
                Assert.GreaterOrEqual(s.CamTarget, last);
                last = s.CamTarget;
                Assert.LessOrEqual(s.OnPad.Y, s.CamTarget - Sim.FrogBelow + 1e-3f, "the frog's pad sits low on the screen");
            });
            StepFor(sim, 2f);
            Assert.AreEqual(sim.CamTarget, sim.CamY, 1e-3f, "the view catches up");
            Assert.Greater(sim.CamY, lowest + 30f);

            // Old pads far below are dropped.
            foreach (var p in sim.Pads) Assert.Greater(p.Y, sim.CamY - 12f);
        }

        [Test]
        public void SameSeed_SamePond()
        {
            var a = new Sim(1234);
            var b = new Sim(1234);
            Assert.AreEqual(a.Pads.Count, b.Pads.Count);
            for (int i = 0; i < a.Pads.Count; i++)
            {
                Assert.AreEqual(a.Pads[i].BaseX, b.Pads[i].BaseX);
                Assert.AreEqual(a.Pads[i].Y, b.Pads[i].Y);
                Assert.AreEqual(a.Pads[i].Kind, b.Pads[i].Kind);
            }
        }
    }
}
