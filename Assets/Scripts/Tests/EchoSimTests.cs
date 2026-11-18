using System.Collections.Generic;
using System.Linq;
using AceBarcade.Games.EchoPads;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.EchoPads.EchoSim;

namespace AceBarcade.Tests
{
    public class EchoSimTests
    {
        const float Dt = 1f / 60f;
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };

        /// <summary>Steps until it's the player's turn; returns the pads lit, in order.</summary>
        static List<int> Watch(Sim sim)
        {
            var shown = new List<int>();
            for (int i = 0; i < 60 * 60 && sim.Phase != EchoPhase.Echo; i++)
            {
                var e = sim.Step(Dt);
                if ((e & EchoEvent.Shown) != 0) shown.Add(sim.Lit);
            }
            Assert.AreEqual(EchoPhase.Echo, sim.Phase);
            return shown;
        }

        /// <summary>Steps through the pause after a round or a mistake.</summary>
        static EchoEvent Pause(Sim sim)
        {
            var all = EchoEvent.None;
            for (int i = 0; i < 60 * 5 && sim.Phase == EchoPhase.Wait; i++) all |= sim.Step(Dt);
            return all;
        }

        /// <summary>Watches the tune and taps it all back.</summary>
        static EchoEvent PlayRound(Sim sim)
        {
            Watch(sim);
            var all = EchoEvent.None;
            foreach (int pad in sim.Tune.ToList()) all |= sim.Tap(pad);
            return all | Pause(sim);
        }

        static int Wrong(Sim sim) => (sim.Tune[sim.Echoed] + 1) % sim.Pads;

        [Test]
        public void Start_OneNoteOnFourPads_ThreeLives_AndTheTuneComesFirst()
        {
            var sim = new Sim(1);
            Assert.AreEqual(1, sim.Round);
            Assert.AreEqual(Sim.StartPads, sim.Pads);
            Assert.That(sim.Tune[0], Is.InRange(0, Sim.StartPads - 1));
            Assert.AreEqual(Sim.StartLives, sim.Lives);
            Assert.AreEqual(EchoPhase.Watch, sim.Phase);
            Assert.AreEqual(-1, sim.Lit);
            Assert.AreEqual(0, sim.Score);
        }

        [Test]
        public void TheTune_LightsEachPadInTurn_WithADarkGapBetween_ThenItsThePlayersTurn()
        {
            var sim = new Sim(3);
            for (int r = 0; r < 4; r++) PlayRound(sim);
            Assert.AreEqual(5, sim.Round);

            // Nothing for the lead-in.
            sim.Step(Sim.LeadIn - 0.05f);
            Assert.AreEqual(-1, sim.Lit);

            var lit = new List<int>();
            int last = -1;
            bool sawEcho = false;
            for (int i = 0; i < 60 * 20 && !sawEcho; i++)
            {
                var e = sim.Step(Dt);
                if (sim.Lit != -1 && last == -1) lit.Add(sim.Lit);
                last = sim.Lit;
                sawEcho = (e & EchoEvent.YourTurn) != 0;
            }
            Assert.IsTrue(sawEcho);
            CollectionAssert.AreEqual(sim.Tune, lit, "each note lights once, in order, with a gap even between repeats");
            Assert.AreEqual(-1, sim.Lit);
            Assert.AreEqual(EchoPhase.Echo, sim.Phase);
        }

        [Test]
        public void TapsWhileTheTuneIsShowing_OrDuringThePause_DoNothing()
        {
            var sim = new Sim(2);
            for (int pad = 0; pad < sim.Pads; pad++) Assert.AreEqual(EchoEvent.None, sim.Tap(pad));
            Watch(sim);
            sim.Tap(sim.Tune[0]);
            Assert.AreEqual(EchoPhase.Wait, sim.Phase);
            for (int pad = 0; pad < sim.Pads; pad++) Assert.AreEqual(EchoEvent.None, sim.Tap(pad));
            Assert.AreEqual(Sim.StartLives, sim.Lives);
            Assert.AreEqual(Sim.TapPoints, sim.Score);
            Assert.AreEqual(EchoEvent.None, sim.Tap(-1), "a tap off the pads");
        }

        [Test]
        public void EchoingTheTune_Scores1ATap_AndTheNextRoundIsTheSameTuneOneNoteLonger()
        {
            var sim = new Sim(5);
            var e = PlayRound(sim);
            Assert.IsTrue((e & EchoEvent.RoundDone) != 0);
            Assert.IsTrue((e & EchoEvent.NewRound) != 0);
            Assert.AreEqual(2, sim.Round);
            var tune = sim.Tune.ToList();
            PlayRound(sim);
            PlayRound(sim);
            Assert.AreEqual(4, sim.Round);
            CollectionAssert.AreEqual(tune, sim.Tune.Take(2), "the tune only grows at the end");
            Assert.AreEqual((1 + 2 + 3) * Sim.TapPoints, sim.Score);
            Assert.AreEqual(Sim.StartLives, sim.Lives);
        }

        [Test]
        public void AWrongTap_CostsALife_AndShowsTheSameRoundAgain_TheThirdEndsTheRun()
        {
            var sim = new Sim(4);
            PlayRound(sim);
            PlayRound(sim);
            var tune = sim.Tune.ToList();
            int score = sim.Score;

            Watch(sim);
            sim.Tap(sim.Tune[0]);
            int expected = sim.Tune[1];
            var e = sim.Tap(Wrong(sim));
            Assert.IsTrue((e & EchoEvent.Wrong) != 0);
            Assert.AreEqual(expected, sim.Expected);
            Assert.AreEqual(Sim.StartLives - 1, sim.Lives);
            Assert.AreEqual(score + Sim.TapPoints, sim.Score, "the right taps before it still count");
            e = Pause(sim);
            Assert.IsTrue((e & EchoEvent.Replay) != 0);
            Assert.IsFalse((e & EchoEvent.NewRound) != 0);
            CollectionAssert.AreEqual(tune, sim.Tune, "the same tune again");
            Assert.AreEqual(0, sim.Echoed);
            CollectionAssert.AreEqual(tune, Watch(sim), "and it's shown again");

            sim.Tap(Wrong(sim));
            Pause(sim);
            Watch(sim);
            e = sim.Tap(Wrong(sim));
            Assert.IsTrue((e & EchoEvent.Over) != 0);
            Assert.IsTrue(sim.Over);
            Assert.AreEqual(0, sim.Lives);
            Assert.AreEqual(EchoEvent.None, sim.Step(5f));
            Assert.AreEqual(EchoEvent.None, sim.Tap(sim.Tune[0]));
        }

        [Test]
        public void EveryFifthRound_BringsAGem_Worth5()
        {
            var sim = new Sim(6);
            int score = 0;
            for (int round = 1; round <= 10; round++)
            {
                var e = PlayRound(sim);
                score += round * Sim.TapPoints + (round % 5 == 0 ? Sim.GemPoints : 0);
                Assert.AreEqual(round % 5 == 0, (e & EchoEvent.Gem) != 0, $"round {round}");
                Assert.AreEqual(score, sim.Score);
            }
            Assert.AreEqual(2, sim.Gems);
        }

        [Test]
        public void AFifthPad_JoinsAtRound8_AndItsNoteIsTheNewOne()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                for (int round = 1; round < Sim.FifthPadRound; round++)
                {
                    Assert.AreEqual(Sim.StartPads, sim.Pads);
                    Assert.IsTrue(sim.Tune.All(p => p < Sim.StartPads));
                    var e = PlayRound(sim);
                    Assert.AreEqual(round + 1 == Sim.FifthPadRound, (e & EchoEvent.NewPad) != 0);
                }
                Assert.AreEqual(Sim.FifthPadRound, sim.Round);
                Assert.AreEqual(Sim.MaxPads, sim.Pads);
                Assert.AreEqual(Sim.MaxPads - 1, sim.Tune[sim.Round - 1], "the new pad's note comes straight away");
                for (int i = 0; i < 20; i++) PlayRound(sim);
                Assert.Greater(sim.Tune.Skip(Sim.FifthPadRound).Count(p => p == Sim.MaxPads - 1), 0, "and keeps coming up");
            }
        }

        [Test]
        public void TheTune_NeverRepeatsAPadThreeTimesRunning_AndUsesThemAll()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                for (int i = 0; i < 39; i++) PlayRound(sim);
                var tune = sim.Tune;
                for (int i = 2; i < tune.Count; i++)
                    Assert.IsFalse(tune[i] == tune[i - 1] && tune[i] == tune[i - 2], $"seed {seed}, note {i}");
                for (int pad = 0; pad < Sim.MaxPads; pad++) Assert.Contains(pad, tune.ToList());
            }
        }

        [Test]
        public void ItPlaysFasterEachRound_ToACap_AndEachNoteStaysLongEnoughToSee()
        {
            var sim = new Sim(1);
            float on = sim.OnTime, gap = sim.GapTime;
            Assert.AreEqual(Sim.StartOn, on);
            for (int i = 0; i < 30; i++)
            {
                PlayRound(sim);
                Assert.LessOrEqual(sim.OnTime, on);
                Assert.LessOrEqual(sim.GapTime, gap);
                on = sim.OnTime;
                gap = sim.GapTime;
            }
            Assert.AreEqual(Sim.MinOn, sim.OnTime);
            Assert.AreEqual(Sim.MinGap, sim.GapTime);
            Assert.GreaterOrEqual(Sim.MinOn, 0.25f, "a quarter of a second, at least");
            Assert.GreaterOrEqual(Sim.MinGap, 6 * Dt, "a gap you can see between repeats");
        }
    }
}
