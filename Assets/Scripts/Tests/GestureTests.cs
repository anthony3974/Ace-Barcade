using NUnit.Framework;
using UnityEngine;

namespace AceBarcade.Tests
{
    public class GestureTests
    {
        GestureTracker _g;
        float _t;

        [SetUp]
        public void SetUp()
        {
            _g = new GestureTracker();
            _t = 0f;
        }

        void Frame(bool pressed, float x, float y, float dt = 1f / 60f)
        {
            _t += dt;
            _g.Step(pressed, new Vector2(x, y), _t);
        }

        [Test]
        public void QuickTouch_IsATap()
        {
            Frame(true, 0, 0);
            Assert.IsTrue(_g.Down);
            Frame(true, 0.05f, 0);
            Frame(false, 0.05f, 0);
            Assert.IsTrue(_g.Up);
            Assert.IsTrue(_g.Tapped);
        }

        [Test]
        public void LongHold_IsNotATap()
        {
            Frame(true, 0, 0);
            for (int i = 0; i < 30; i++) Frame(true, 0, 0);
            Assert.Greater(_g.HeldFor, 0.4f);
            Frame(false, 0, 0);
            Assert.IsFalse(_g.Tapped);
        }

        [Test]
        public void FastFlick_IsOneSwipeInItsDirection()
        {
            Frame(true, 0, 0);
            Frame(true, 0, 0.5f);
            Assert.AreEqual(Vector2.zero, _g.Swipe);
            Frame(true, 0.1f, 1.0f);
            Assert.AreEqual(Vector2.up, _g.Swipe);
            Frame(true, 0.1f, 2.0f);
            Assert.AreEqual(Vector2.zero, _g.Swipe, "only one swipe per touch");
            Frame(false, 0.1f, 2.0f);
            Assert.IsFalse(_g.Tapped);
        }

        [Test]
        public void SlowDrag_IsNotASwipe_ButReportsDelta()
        {
            Frame(true, 0, 0);
            Frame(true, 0.3f, 0, 0.3f);
            Assert.AreEqual(0.3f, _g.DragDelta.x, 1e-4f);
            Frame(true, 1.5f, 0, 0.3f);
            Assert.AreEqual(Vector2.zero, _g.Swipe);
            Assert.AreEqual(1.2f, _g.DragDelta.x, 1e-4f);
        }
    }
}
