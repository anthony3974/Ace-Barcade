using System.Linq;
using NUnit.Framework;

namespace AceBarcade.Tests
{
    public class RegistryTests
    {
        [Test]
        public void AllGames_HaveValidUniqueNumbersAndIds()
        {
            var problems = GameRegistry.Validate(GameRegistry.All);
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void AllGames_AreInTheGamesAssembly()
        {
            foreach (var g in GameRegistry.All)
                Assert.AreEqual("AceBarcade.Games", g.Type.Assembly.GetName().Name, $"{g.Type.Name} must live under Assets/Games");
        }

        [Test]
        public void Validate_CatchesRepeatsAndBadIds()
        {
            var a = new GameInfo(new ArcadeGameAttribute(1, "same-id", "A", Controls.Tap), typeof(FakeGame));
            var b = new GameInfo(new ArcadeGameAttribute(1, "same-id", "B", Controls.Tap), typeof(FakeGame));
            var c = new GameInfo(new ArcadeGameAttribute(2, "Bad Id", "C", Controls.Tap), typeof(FakeGame));
            var problems = GameRegistry.Validate(new[] { a, b, c });
            Assert.IsTrue(problems.Any(p => p.Contains("number 1")));
            Assert.IsTrue(problems.Any(p => p.Contains("id 'same-id'")));
            Assert.IsTrue(problems.Any(p => p.Contains("'Bad Id'")));
        }

        [Test]
        public void Folder_IsNumberAndTitleWithoutSpaces()
        {
            var g = new GameInfo(new ArcadeGameAttribute(7, "paddle-rally", "Paddle Rally!", Controls.Drag), typeof(FakeGame));
            Assert.AreEqual("G007_PaddleRally", g.Folder);
        }

        class FakeGame : ArcadeGame
        {
            protected override void Build() { }
            protected override void Play(float dt) { }
        }
    }
}
