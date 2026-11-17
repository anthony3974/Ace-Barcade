using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AceBarcade.PlayTests
{
    /// <summary>The hub: what it shows, its 2 x 6 grid, and moving between it and a game.</summary>
    public class HubPlayTests
    {
        ArcadeRoot _root;

        [SetUp]
        public void SetUp()
        {
            Time.captureFramerate = 60;
            Scores.Clear("paddle-rally");
            _root = new GameObject("Arcade").AddComponent<ArcadeRoot>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            Object.Destroy(_root.gameObject);
            if (Arcade.Current != null) Object.Destroy(Arcade.Current.gameObject);
            Scores.Clear("paddle-rally");
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        Hub Hub => _root.Hub;

        [UnityTest]
        public IEnumerator OpensOnTheHub_WithACardPerGame_AndComingSoonToFillTheScreen()
        {
            yield return Frames(3);
            Assert.IsNull(Arcade.Current, "no game runs behind the hub");
            Assert.IsTrue(Hub.Showing);

            CollectionAssert.AreEqual(GameRegistry.All.Select(g => g.Id), Hub.Cards.Select(c => c.Info.Id));
            Assert.AreEqual(Hub.Columns * Hub.VisibleRows, Hub.Cards.Count + Hub.ComingSoonCount,
                "with fewer than 12 games, coming-soon cards fill the first screen");
            Assert.AreEqual("PADDLE RALLY", Hub.Cards[0].Title.text);
            Assert.AreEqual("BEST 0", Hub.Cards[0].Best.text);
            yield return PaddleRallyPlayTests.Capture("0-hub");
        }

        [UnityTest]
        public IEnumerator Grid_IsTwoColumns_AndSixRowsFillTheViewport()
        {
            yield return Frames(3);
            var grid = Hub.Grid;
            Assert.AreEqual(2, grid.constraintCount);

            float rows = grid.padding.top + grid.padding.bottom + Hub.VisibleRows * grid.cellSize.y + (Hub.VisibleRows - 1) * grid.spacing.y;
            Assert.AreEqual(Hub.Viewport.rect.height, rows, 2f, "six rows fit the viewport exactly");
            float cols = grid.padding.left + grid.padding.right + 2 * grid.cellSize.x + grid.spacing.x;
            Assert.AreEqual(Hub.Viewport.rect.width, cols, 2f, "two columns fill the width");
            Assert.Greater(grid.cellSize.x, grid.cellSize.y, "cards are wider than tall: thumbnail left, text right");
        }

        [UnityTest]
        public IEnumerator ThirteenGames_Scroll_AndStillShowSixRowsAtATime()
        {
            yield return Frames(3);
            var cell = Hub.Grid.cellSize;
            var real = GameRegistry.All[0];
            var many = Enumerable.Range(1, 13)
                .Select(n => new GameInfo(new ArcadeGameAttribute(n, $"test-{n}", $"Test Game {n}", Controls.Tap), real.Type))
                .ToList();
            LogAssert.ignoreFailingMessages = true; // the stand-in games have no icon art, which logs an error
            Hub.Populate(many);
            yield return Frames(3);

            Assert.AreEqual(13, Hub.Cards.Count);
            Assert.AreEqual(1, Hub.ComingSoonCount, "the last row is filled out to two");
            Assert.AreEqual(cell, Hub.Grid.cellSize, "cards stay the same size: still 6 rows a screen");
            Assert.Greater(Hub.Content.rect.height, Hub.Viewport.rect.height + cell.y * 0.5f, "a 7th row runs below the screen");

            Hub.Scroll.verticalNormalizedPosition = 0f; // scroll to the bottom
            yield return Frames(2);
            Assert.Greater(Hub.Content.anchoredPosition.y, cell.y * 0.5f, "the grid moved up");
            yield return PaddleRallyPlayTests.Capture("0-hub-scrolled");
        }

        [UnityTest]
        public IEnumerator TappingACard_StartsTheGame_AndArcadeComesBack_WithTheNewBest()
        {
            yield return Frames(3);
            Hub.Cards[0].Button.onClick.Invoke();
            yield return Frames(2);
            Assert.IsNotNull(Arcade.Current);
            Assert.AreEqual("paddle-rally", Arcade.Current.Info.Id);
            Assert.IsFalse(Hub.Showing, "the hub hides while a game runs");

            Scores.Submit("paddle-rally", 17);
            Arcade.ShowHub();
            yield return Frames(2);
            Assert.IsNull(Arcade.Current);
            Assert.IsTrue(Hub.Showing);
            Assert.AreEqual("BEST 17", Hub.Cards[0].Best.text);
        }

        [UnityTest]
        public IEnumerator BackInAGame_ThatIsPaused_ReturnsToTheHub()
        {
            yield return Frames(3);
            Hub.Cards[0].Button.onClick.Invoke();
            yield return Frames(2);
            Arcade.Input.Simulate(true, new Vector2(0f, -7f));
            yield return Frames(2);
            Arcade.Input.Simulate(true, new Vector2(0f, -7f), back: true);
            yield return Frames(2);
            Assert.AreEqual(ArcadeGame.Phase.Paused, Arcade.Current.State);
            Arcade.Input.Simulate(false, new Vector2(0f, -7f), back: true);
            yield return Frames(2);
            Assert.IsTrue(Hub.Showing);
            Assert.AreEqual(1f, Time.timeScale, "leaving a paused game unpauses time");
        }
    }
}
