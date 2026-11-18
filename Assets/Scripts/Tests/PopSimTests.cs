using System.Collections.Generic;
using AceBarcade.Games.PopUp;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.PopUp.PopSim;

namespace AceBarcade.Tests
{
    public class PopSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        /// <summary>Steps until a critter of this kind is fully up; returns its hole, or -1.</summary>
        static int WaitFor(Sim sim, Critter kind, float seconds = 120f)
        {
            for (float t = 0; t < seconds && !sim.Over; t += Dt)
            {
                sim.Step(Dt);
                foreach (var h in sim.Field)
                    if (h.State == HoleState.Up && h.Kind == kind) return h.Index;
                // Keep the run alive: tap every sprout as it comes up, unless we're looking for one.
                if (kind != Critter.Sprout)
                    foreach (var h in sim.Field)
                        if (h.Kind == Critter.Sprout && h.State == HoleState.Up) sim.Tap(h.Index);
            }
            return -1;
        }

        /// <summary>
        /// A player with human limits: it taps a sprout or a teal one 0.35 s after it starts to
        /// pop up, no more than 5 taps a second, and never aims at a red one. It taps the hole
        /// where it saw the critter, whatever is there by then.
        /// </summary>
        sealed class Bot
        {
            const float Reaction = 0.35f, MinBetweenTaps = 0.2f;
            readonly List<(float at, int hole)> _plan = new List<(float, int)>();
            readonly List<(float at, int hole)> _stray = new List<(float, int)>();
            readonly HoleState[] _seen = new HoleState[Sim.Holes];
            readonly System.Random _random;
            float _lastTap = -1f;

            /// <param name="strayTaps">Also tap again, late, where each sprout was: up to 0.75 s after it's gone.</param>
            public Bot(int seed = 0, bool strayTaps = false)
            {
                _random = strayTaps ? new System.Random(seed) : null;
            }

            public PopEvent Tick(Sim sim)
            {
                var events = PopEvent.None;
                foreach (var h in sim.Field)
                {
                    if (_seen[h.Index] == HoleState.Empty && h.State == HoleState.Rising && h.Kind != Critter.Spiky)
                    {
                        float at = Mathf.Max(sim.Time + Reaction, _lastTap + MinBetweenTaps);
                        _plan.Add((at, h.Index));
                        _lastTap = at;
                    }
                    if (_random != null && _seen[h.Index] != HoleState.Empty && h.State == HoleState.Empty && h.LastKind != Critter.Spiky)
                        _stray.Add((sim.Time + (float)_random.NextDouble() * 0.75f, h.Index));
                    _seen[h.Index] = h.State;
                }
                for (int i = _stray.Count - 1; i >= 0; i--)
                {
                    if (_stray[i].at > sim.Time) continue;
                    events |= sim.Tap(_stray[i].hole);
                    _stray.RemoveAt(i);
                }
                while (_plan.Count > 0 && _plan[0].at <= sim.Time)
                {
                    events |= sim.Tap(_plan[0].hole);
                    _plan.RemoveAt(0);
                }
                return events | sim.Step(Dt);
            }
        }

        // --- The field -------------------------------------------------------------------------

        [Test]
        public void Holes_AreAThreeByThreeGrid_AndATapNearACritterCountsForItsHole()
        {
            for (int i = 0; i < Sim.Holes; i++)
            {
                var c = Sim.HoleCenter(i);
                Assert.AreEqual(i, Sim.HoleAt(c));
                Assert.AreEqual(i, Sim.HoleAt(c + new Vector2(0.8f, 1.4f)), "on the critter's head");
                Assert.AreEqual(i, Sim.HoleAt(c + new Vector2(-0.8f, -0.4f)), "on the stump's lip");
                Assert.That(Mathf.Abs(c.x), Is.LessThanOrEqualTo(Style.WorldWidth / 2f - 1f));
                Assert.Less(c.y + Sim.TapAbove, Style.WorldHeight / 2f - 1.5f, "clear of the HUD");
            }
            Assert.AreEqual(-1, Sim.HoleAt(new Vector2(Sim.ColSpacing / 2f, Sim.TopLipY)), "between two stumps");
            Assert.AreEqual(-1, Sim.HoleAt(new Vector2(0f, 7f)));
        }

        // --- Popping up and down ---------------------------------------------------------------

        [Test]
        public void ACritter_PopsUp_StaysUp_AndGoesBackDown_TappableOnlyWhileShowing()
        {
            var sim = new Sim(1);
            var events = PopEvent.None;
            for (int i = 0; i < 60 && (events & PopEvent.Popped) == 0; i++) events |= sim.Step(Dt);
            Assert.IsTrue((events & PopEvent.Popped) != 0, "the first one comes within a second");
            PopHole hole = null;
            foreach (var h in sim.Field) if (h.State != HoleState.Empty) hole = h;
            Assert.AreEqual(Critter.Sprout, hole.Kind, "the first ones are sprouts");
            Assert.AreEqual(HoleState.Rising, hole.State);
            Assert.IsFalse(hole.Tappable, "not until it's a third of the way up");

            while (hole.State == HoleState.Rising) sim.Step(Dt);
            Assert.AreEqual(1f, hole.Height);
            Assert.IsTrue(hole.Tappable);
            float upFor = 0f;
            while (hole.State == HoleState.Up) { sim.Step(Dt); upFor += Dt; }
            Assert.AreEqual(Sim.StartUp, upFor, 2 * Dt);
            Assert.IsTrue(hole.Tappable, "still, just after it starts going down");
            while (hole.Height >= 0.5f) sim.Step(Dt);
            Assert.IsFalse(hole.Tappable, "too late");
        }

        [Test]
        public void TappingASprout_Scores1_AndKnocksItDown_Once()
        {
            var sim = new Sim(2);
            int hole = WaitFor(sim, Critter.Sprout);
            Assert.AreEqual(PopEvent.None, sim.Tap(-1));
            Assert.AreEqual(PopEvent.Hit, sim.Tap(hole));
            Assert.AreEqual(Sim.SproutPoints, sim.Score);
            Assert.AreEqual(1, sim.Hits);
            Assert.AreEqual(hole, sim.LastHole);
            Assert.AreEqual(PopEvent.None, sim.Tap(hole), "a knocked critter can't be tapped again");
            Assert.AreEqual(Sim.SproutPoints, sim.Score);

            var h = sim.Field[hole];
            for (int i = 0; i < 60 && h.State != HoleState.Empty; i++) Assert.IsTrue((sim.Step(Dt) & PopEvent.Escaped) == 0);
            Assert.AreEqual(HoleState.Empty, h.State);
            Assert.AreEqual(Sim.StartLives, sim.Lives, "a tapped sprout didn't get away");
        }

        [Test]
        public void TappingATealOne_Scores5_AndItStaysUpForLessTime()
        {
            var sim = new Sim(3);
            int hole = WaitFor(sim, Critter.Gold);
            Assert.GreaterOrEqual(hole, 0, "a teal one turns up");
            Assert.AreEqual(sim.UpTime * Sim.GoldUpShare, sim.Field[hole].UpFor, 1e-4f);
            int score = sim.Score;
            Assert.AreEqual(PopEvent.Gold, sim.Tap(hole));
            Assert.AreEqual(score + Sim.GoldPoints, sim.Score);
            Assert.AreEqual(1, sim.Golds);
        }

        [Test]
        public void TappingARedOne_EndsTheRun()
        {
            var sim = new Sim(4);
            int hole = WaitFor(sim, Critter.Spiky);
            Assert.GreaterOrEqual(hole, 0, "a red one turns up");
            int score = sim.Score;
            Assert.AreEqual(PopEvent.Spiky, sim.Tap(hole));
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(score, sim.Score);
            Assert.AreEqual(PopEvent.None, sim.Step(1f), "nothing happens after the run ends");
        }

        [Test]
        public void SproutsThatGetAway_CostALifeEach_AndThreeEndTheRun_ButRedAndTealOnesCostNothing()
        {
            var sim = new Sim(5);
            var events = PopEvent.None;
            int escapes = 0, lives = Sim.StartLives;
            for (float t = 0; t < 60f && !sim.Over; t += Dt)
            {
                var e = sim.Step(Dt);
                events |= e;
                if ((e & PopEvent.Escaped) != 0)
                {
                    escapes++;
                    Assert.AreEqual(Critter.Sprout, sim.Field[sim.LastHole].LastKind);
                    Assert.AreEqual(--lives, sim.Lives);
                }
            }
            Assert.IsTrue(sim.Over);
            Assert.IsTrue((events & PopEvent.OutOfLives) != 0);
            Assert.AreEqual(Sim.StartLives, escapes);
            Assert.AreEqual(0, sim.Lives);
            Assert.AreEqual(0, sim.Score);
        }

        // --- Difficulty and fairness -----------------------------------------------------------

        [Test]
        public void ItGetsHarderWithEveryHit_UpToTheHardest()
        {
            var sim = new Sim(6);
            Assert.AreEqual(Sim.StartUp, sim.UpTime);
            Assert.AreEqual(Sim.StartEvery, sim.SpawnEvery);
            Assert.AreEqual(Sim.StartSpiky, sim.SpikyChance);
            Assert.AreEqual(Sim.StartMaxUp, sim.MaxUp);

            var bot = new Bot();
            float up = sim.UpTime;
            for (float t = 0; t < 300f && !sim.Over && sim.Hits < Sim.RampHits + 10; t += Dt)
            {
                bot.Tick(sim);
                Assert.LessOrEqual(sim.UpTime, up + 1e-5f);
                up = sim.UpTime;
            }
            Assert.IsFalse(sim.Over);
            Assert.AreEqual(1f, sim.Difficulty);
            Assert.AreEqual(Sim.HardestUp, sim.UpTime, 1e-5f);
            Assert.AreEqual(Sim.HardestEvery, sim.SpawnEvery, 1e-5f);
            Assert.AreEqual(Sim.HardestSpiky, sim.SpikyChance, 1e-5f);
            Assert.AreEqual(Sim.HardestMaxUp, sim.MaxUp);
        }

        [Test]
        public void HolesRest_RedOnesNeverReplaceASproutQuickly_AndOnlySoManyAreUpAtOnce()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var bot = new Bot();
                var was = new HoleState[Sim.Holes];
                int reds = 0;
                for (float t = 0; t < 240f && !sim.Over; t += Dt)
                {
                    bot.Tick(sim);
                    Assert.LessOrEqual(sim.UpCount(), sim.MaxUp);
                    foreach (var h in sim.Field)
                    {
                        if (was[h.Index] == HoleState.Empty && h.State != HoleState.Empty)
                        {
                            float rested = sim.Time - h.EmptySince;
                            Assert.GreaterOrEqual(rested, Sim.Rest - Dt, $"seed {seed}");
                            if (h.Kind == Critter.Spiky)
                            {
                                reds++;
                                if (h.LastKind != Critter.Spiky) Assert.GreaterOrEqual(rested, Sim.SpikyAfterSprout - Dt, $"seed {seed}");
                            }
                        }
                        was[h.Index] = h.State;
                    }
                }
                Assert.Greater(reds, 20, $"seed {seed}: red ones do turn up");
            }
        }

        [Test]
        public void ABotWithHumanReactions_NeverTapsARedOne_OrMissesASprout_OnEverySeed()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var bot = new Bot();
                PlayToTheHardestAndBeyond(sim, bot, seed);
            }
        }

        [Test]
        public void ALateSecondTap_WhereASproutJustWas_NeverLandsOnARedOne()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                var bot = new Bot(seed, strayTaps: true);
                PlayToTheHardestAndBeyond(sim, bot, seed);
            }
        }

        static void PlayToTheHardestAndBeyond(Sim sim, Bot bot, int seed)
        {
            float hardestFrom = -1f;
            for (float t = 0; t < 600f && !sim.Over; t += Dt)
            {
                var e = bot.Tick(sim);
                Assert.IsTrue((e & PopEvent.Spiky) == 0, $"seed {seed}: a late tap landed on a red one at {sim.Time:0.0}s");
                Assert.IsTrue((e & PopEvent.Escaped) == 0, $"seed {seed}: a sprout got away at {sim.Time:0.0}s");
                if (hardestFrom < 0f && sim.Difficulty >= 1f) hardestFrom = sim.Time;
                if (hardestFrom >= 0f && sim.Time - hardestFrom > 120f) break;
            }
            Assert.IsFalse(sim.Over, $"seed {seed}");
            Assert.GreaterOrEqual(hardestFrom, 0f, $"seed {seed}: it reached the hardest");
            Assert.AreEqual(sim.Hits - sim.Golds, (sim.Score - sim.Golds * Sim.GoldPoints) / Sim.SproutPoints);
        }
    }
}
