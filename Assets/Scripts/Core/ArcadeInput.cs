using UnityEngine;
using UnityEngine.InputSystem;

namespace AceBarcade
{
    /// <summary>
    /// The only way games read input: one finger (the mouse in the editor) and the Android Back
    /// button (Escape in the editor). Every game is played with one thumb.
    /// Runs before games each frame.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ArcadeInput : MonoBehaviour
    {
        readonly GestureTracker _gestures = new GestureTracker();

        /// <summary>A finger is on the screen.</summary>
        public bool Pressed => _gestures.Pressed;

        /// <summary>A finger touched down this frame.</summary>
        public bool Down => _gestures.Down;

        /// <summary>The finger lifted this frame.</summary>
        public bool Up => _gestures.Up;

        /// <summary>A quick touch and release without moving, ending this frame.</summary>
        public bool Tapped => _gestures.Tapped;

        /// <summary>Up, down, left or right on the frame a swipe is recognised, else zero.</summary>
        public Vector2 Swipe => _gestures.Swipe;

        /// <summary>Where the finger is (or last was), in world units.</summary>
        public Vector2 Position => _gestures.Position;

        /// <summary>How far the finger moved this frame while held, in world units.</summary>
        public Vector2 DragDelta => _gestures.DragDelta;

        public float HeldFor => _gestures.HeldFor;

        /// <summary>The Android Back button (Escape in the editor) was pressed this frame.</summary>
        public bool Back { get; private set; }

        void Update()
        {
            var pointer = Pointer.current;
            bool pressed = pointer != null && pointer.press.isPressed;
            Vector2 world = _gestures.Position;
            if (pointer != null && Arcade.Camera != null)
                world = Arcade.Camera.ScreenToWorld(pointer.position.ReadValue());

            _gestures.Step(pressed, world, Time.unscaledTime);
            Back = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        }
    }
}
