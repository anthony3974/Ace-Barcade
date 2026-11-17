using System.Collections;
using AceBarcade.Games.DropStack;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AceBarcade.PlayTests
{
    /// <summary>Plays Drop Stack through the real arcade with simulated touches.</summary>
    public class DropStackPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("drop-stack");
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("drop-stack");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static DropStack Game => (DropStack)Arcade.Current;
        static DropSim Sim => Game.Sim;
        static readonly Vector2 Thumb = new Vector2(0f, -3f);

        IEnumerator Begin()
        {
            yield return Frames(3);
            Arcade.Launch(GameRegistry.Find("drop-stack"));
            yield return Frames(2);
            Assert.IsInstanceOf<DropStack>(Arcade.Current);
            yield return PaddleRallyPlayTests.Capture("5-dropstack-ready");
            // The touch that starts the game; it isn't a turn.
            int rotation = Sim.Rotation;
            Arcade.Input.Simulate(true, Thumb);
            yield return Frames(2);
            Arcade.Input.Simulate(false, Thumb);
            yield return Frames(2);
            Assert.AreEqual(ArcadeGame.Phase.Playing, Game.State);
            Assert.AreEqual(rotation, Sim.Rotation, "starting the game doesn't turn the piece");
        }

        static IEnumerator Tap()
        {
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(false, Thumb);
            yield return null;
        }

        /// <summary>A quick flick down: drops the piece all the way.</summary>
        static IEnumerator Flick()
        {
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            Arcade.Input.Simulate(true, Thumb + new Vector2(0f, -1.2f));
            yield return null;
            Arcade.Input.Simulate(false, Thumb + new Vector2(0f, -1.2f));
            yield return null;
        }

        /// <summary>A slow sideways drag of the given distance (not a flick).</summary>
        static IEnumerator Drag(float dx)
        {
            Arcade.Input.Simulate(true, Thumb);
            yield return null;
            const int steps = 30;
            for (int i = 1; i <= steps; i++)
            {
                Arcade.Input.Simulate(true, Thumb + new Vector2(dx * i / steps, 0f));
                yield return null;
            }
            Arcade.Input.Simulate(false, Thumb + new Vector2(dx, 0f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Tap_Turns_Drag_Slides_Flick_Drops()
        {
            yield return Begin();

            int r = Sim.Rotation;
            var kind = Sim.Kind;
            yield return Tap();
            if (kind != PieceKind.Square) Assert.AreEqual((r + 1) % 4, Sim.Rotation, "a tap turns the piece");

            int x = Sim.Position.x;
            yield return Drag(-DropStack.Cell * 2.1f);
            Assert.AreEqual(x - 2, Sim.Position.x, "dragging two blocks' width slides two columns");

            int filledBefore = CountFilled();
            yield return Flick();
            Assert.Greater(CountFilled(), filledBefore, "a flick down drops and locks the piece");
        }

        [UnityTest]
        public IEnumerator DroppingWithoutAim_StacksUp_AndEndsTheGame()
        {
            yield return Begin();
            for (int i = 0; i < 80 && Game.State == ArcadeGame.Phase.Playing; i++)
            {
                if (i == 6) yield return PaddleRallyPlayTests.Capture("6-dropstack-playing");
                // Alternate sides a little so it isn't one tower.
                if (i % 3 == 1) yield return Drag(DropStack.Cell * 2.1f);
                if (i % 3 == 2) yield return Drag(-DropStack.Cell * 2.1f);
                yield return Flick();
            }
            Assert.AreEqual(ArcadeGame.Phase.Over, Game.State, "the stack reaches the top");
            Assert.AreEqual(Sim.Score, Game.Score);
            yield return Frames(60);
            yield return PaddleRallyPlayTests.Capture("7-dropstack-over");
        }

        static int CountFilled()
        {
            int n = 0;
            for (int x = 0; x < DropSim.Cols; x++)
                for (int y = 0; y < DropSim.Rows; y++)
                    if (Sim.Cells[x, y] != 0) n++;
            return n;
        }
    }
}
