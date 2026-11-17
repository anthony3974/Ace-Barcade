using NUnit.Framework;

namespace AceBarcade.Tests
{
    public class ScoresTests
    {
        const string Id = "test-only-scores";

        [SetUp, TearDown]
        public void Clean() => Scores.Clear(Id);

        [Test]
        public void Submit_OnlySavesABetterScore()
        {
            Assert.IsTrue(Scores.Submit(Id, 5));
            Assert.IsFalse(Scores.Submit(Id, 3));
            Assert.IsFalse(Scores.Submit(Id, 5));
            Assert.AreEqual(5, Scores.Best(Id));
        }
    }
}
