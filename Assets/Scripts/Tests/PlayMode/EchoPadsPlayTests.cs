using System.Collections;
using System.Linq;
using AceBarcade.Games.EchoPads;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Rules = AceBarcade.Games.EchoPads.EchoSim;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Echo Pads through the real arcade with simulated taps.</summary>
    public class EchoPadsPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("echo-pads");
            EchoPads.SeedForTests = 14;
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            EchoPads.SeedForTests = null;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("echo-pads");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static EchoPads Game => (EchoPads)Arcade.Current;
        static Rules Sim => Game.Sim;

        /// <summary>A tap on a pad: one frame down, one frame up.</summary>
        static IEnumerator TapPad(int pad)
        {
            var at = EchoPads.PadPosition(pad, Sim.Pads) + new Vector2(0.3f, -0.2f);
            Arcade.Input.Simulate(true, at);
            yield return null;
            Arcade.Input.Simulate(false, at);
            yield return null;
        }

        static IEnumerator UntilYourTurn()
        {
            for (int i = 0; i < 60 * 30 && Sim.Phase != EchoPhase.Echo && !Sim.Over; i++) yield return null;
            Assert.AreEqual(EchoPhase.Echo, Sim.Phase);
        }

        [UnityTest]
        public IEnumerator EchoesNineRounds_TheFifthPadJoins_ThenThreeWrongTapsEndIt_AndRetries()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("echo-pads"));
            yield return Frames(2);
            Assert.IsInstanceOf<EchoPads>(Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            yield return Frames(60);
            Assert.AreEqual(-1, Sim.Lit, "nothing lights until the first tap");
            yield return PaddleRallyPlayTests.Capture("41-echopads-ready");

            // The tap that starts the game, even on a pad, isn't a note.
            yield return TapPad(Sim.Tune[0]);
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.AreEqual(0, Sim.Echoed);
            Assert.AreEqual(Rules.StartLives, Sim.Lives);

            for (int round = 1; round <= 9; round++)
            {
                Assert.AreEqual(round, Sim.Round);
                if (round == 9)
                {
                    // Catch the tune mid-show, with all five pads out.
                    for (int i = 0; i < 60 * 10 && Sim.Lit < 0; i++) yield return null;
                    yield return Frames(3);
                    yield return PaddleRallyPlayTests.Capture("42-echopads-watch");
                }
                yield return UntilYourTurn();
                foreach (int pad in Sim.Tune.ToList()) yield return TapPad(pad);
                Assert.AreEqual(round, Sim.Echoed, $"round {round}");
                Assert.AreEqual(EchoPhase.Wait, Sim.Phase);
                for (int i = 0; i < 60 * 3 && Sim.Phase == EchoPhase.Wait; i++) yield return null;
            }
            Assert.AreEqual(10, Sim.Round);
            Assert.AreEqual(Rules.MaxPads, Sim.Pads);
            Assert.AreEqual(1, Sim.Gems, "the gem for round 5");
            Assert.AreEqual((1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9) * Rules.TapPoints + Rules.GemPoints, Game.Score);
            Assert.AreEqual(Sim.Score, Game.Score, "the arcade's score matches the rules'");

            // Three wrong taps: each costs a life and shows the round again; the third ends it.
            for (int life = Rules.StartLives; life > 0; life--)
            {
                yield return UntilYourTurn();
                yield return TapPad((Sim.Tune[0] + 1) % Sim.Pads);
                Assert.AreEqual(life - 1, Sim.Lives);
            }
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State);
            Assert.IsTrue(Game.NewBest);
            Assert.AreEqual(Game.Score, Scores.Best("echo-pads"));
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("43-echopads-over");

            var first = Arcade.Current;
            Arcade.Retry();
            yield return Frames(2);
            Assert.AreNotSame(first, Arcade.Current);
            Assert.AreEqual(ArcadeGame.Phase.Ready, Game.State);
            Assert.AreEqual(0, Game.Score);
            Assert.AreEqual(1, Sim.Round);
        }
    }
}
