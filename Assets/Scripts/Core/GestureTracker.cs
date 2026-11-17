using UnityEngine;

namespace AceBarcade
{
    /// <summary>
    /// Turns one finger's position over time into taps, holds, drags and swipes. Plain C#, fed by
    /// <see cref="ArcadeInput"/> each frame, so edit mode tests can drive it directly.
    /// Positions are in world units, so the thresholds feel the same on every phone.
    /// </summary>
    public sealed class GestureTracker
    {
        public const float TapMaxTime = 0.25f;
        public const float TapMaxMove = 0.35f;
        public const float SwipeMinMove = 0.8f;
        public const float SwipeMaxTime = 0.45f;

        public bool Pressed { get; private set; }
        public bool Down { get; private set; }
        public bool Up { get; private set; }
        public bool Tapped { get; private set; }

        /// <summary>Up, down, left or right on the frame a swipe is recognised (once per touch), else zero.</summary>
        public Vector2 Swipe { get; private set; }

        public Vector2 Position { get; private set; }
        public Vector2 DragDelta { get; private set; }

        /// <summary>How long the finger has been down, in seconds.</summary>
        public float HeldFor { get; private set; }

        Vector2 _start;
        float _startTime;
        bool _swiped;
        bool _movedTooFar;

        public void Step(bool pressed, Vector2 position, float time)
        {
            Down = pressed && !Pressed;
            Up = !pressed && Pressed;
            Tapped = false;
            Swipe = Vector2.zero;
            DragDelta = pressed && !Down ? position - Position : Vector2.zero;

            if (Down)
            {
                _start = position;
                _startTime = time;
                _swiped = false;
                _movedTooFar = false;
            }

            if (pressed || Up)
            {
                var moved = position - _start;
                if (moved.magnitude > TapMaxMove) _movedTooFar = true;

                if (!_swiped && moved.magnitude >= SwipeMinMove && time - _startTime <= SwipeMaxTime)
                {
                    _swiped = true;
                    Swipe = Mathf.Abs(moved.x) > Mathf.Abs(moved.y)
                        ? new Vector2(Mathf.Sign(moved.x), 0f)
                        : new Vector2(0f, Mathf.Sign(moved.y));
                }
            }

            if (Up && !_swiped && !_movedTooFar && time - _startTime <= TapMaxTime) Tapped = true;

            HeldFor = pressed ? time - _startTime : 0f;
            Pressed = pressed;
            if (pressed || Up) Position = position;
        }
    }
}
