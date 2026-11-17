using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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

        // A touch that starts on a HUD button belongs to the HUD, not the game, until it lifts.
        bool _touchOnUi;
        readonly List<RaycastResult> _hits = new List<RaycastResult>();

        // Play mode tests drive the finger through Simulate instead of real input.
        bool _simulating, _simPressed, _simBack;
        Vector2 _simPosition;

        /// <summary>For tests: from now on the finger is wherever this says (world units).</summary>
        public void Simulate(bool pressed, Vector2 worldPosition, bool back = false)
        {
            _simulating = true;
            _simPressed = pressed;
            _simPosition = worldPosition;
            _simBack = back;
        }

        void Update()
        {
            bool pressed;
            Vector2 world = _gestures.Position;

            if (_simulating)
            {
                pressed = _simPressed;
                world = _simPosition;
                Back = _simBack;
                _simBack = false;
            }
            else
            {
                var pointer = Pointer.current;
                pressed = pointer != null && pointer.press.isPressed;
                if (pointer != null && Arcade.Camera != null)
                {
                    var screen = pointer.position.ReadValue();
                    world = Arcade.Camera.ScreenToWorld(screen);
                    if (pressed && !_gestures.Pressed && !_touchOnUi) _touchOnUi = OverUi(screen);
                }
                Back = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            }

            if (_touchOnUi)
            {
                if (!pressed) _touchOnUi = false;
                pressed = false;
            }

            _gestures.Step(pressed, world, Time.unscaledTime);
        }

        bool OverUi(Vector2 screen)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            _hits.Clear();
            es.RaycastAll(new PointerEventData(es) { position = screen }, _hits);
            return _hits.Count > 0;
        }
    }
}
