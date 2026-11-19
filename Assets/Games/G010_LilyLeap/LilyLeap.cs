using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.LilyLeap
{
    /// <summary>
    /// Game 010. A yellow frog hops up a night pond from lily pad to lily pad, seen from above.
    /// Pull back anywhere on the screen to aim (dots show where it will land), let go to leap.
    /// Miss a pad and it's in the water. Purple pads drift from side to side; tan pads sink soon
    /// after the frog lands. Each new pad up scores 1, teal gems 5.
    /// The rules live in <see cref="LeapSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(10, "lily-leap", "Lily Leap", Controls.Pull, Blurb = "Pull back to aim, let go to leap")]
    public sealed class LilyLeap : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        /// <summary>The pad art's radius in units (inside the outline): pads are scaled from it.</summary>
        const float ArtRadius = 0.95f;
        const float DotStep = 0.42f, DotSize = 0.16f, MarkSize = 0.34f;
        const int MaxDots = 18;
        const float HopScale = 0.5f;
        const float GemScale = 0.55f;

        LeapSim _sim;
        Sprite _pad, _padDrift, _padSink, _gem;
        Transform _world, _frog, _frogBody, _shadow;
        SpriteRenderer _frogSprite;
        SpriteRenderer _mark;
        readonly List<SpriteRenderer> _dots = new List<SpriteRenderer>();
        readonly List<(Transform t, Vector2 at, float drift, float span)> _decor = new List<(Transform, Vector2, float, float)>();

        sealed class PadView
        {
            public Transform Root, Body;
            public SpriteRenderer Gem;
            public bool Seen;
        }

        readonly Dictionary<LeapPad, PadView> _pads = new Dictionary<LeapPad, PadView>();
        readonly List<LeapPad> _gone = new List<LeapPad>();

        Vector2 _pullStart, _pull;
        bool _pulling;
        float _readyTime, _sinkT, _facing;

        public LeapSim Sim => _sim;

        /// <summary>For tests: the pull being aimed right now (zero when not pulling).</summary>
        public Vector2 Pull => _pulling ? _pull : Vector2.zero;

        protected override void Build()
        {
            _sim = new LeapSim(SeedForTests ?? System.Environment.TickCount);
            _pad = GameSprite("pad");
            _padDrift = GameSprite("pad_drift");
            _padSink = GameSprite("pad_sink");
            _gem = PropSprite("gem");
            var t = transform;

            // Background: Slate ripples and floating petals on the dark water, drifting slower than the climb.
            var random = new System.Random(10);
            for (int i = 0; i < 8; i++)
            {
                float size = Mathf.Lerp(0.9f, 1.6f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-4.2f, 4.2f, (float)random.NextDouble()), -12f + i * 3f);
                var ripple = Make("Ripple", at).transform;
                Shapes.Make(ripple, Shapes.Circle, Palette.Slate, Vector2.zero, new Vector2(size, size * 0.6f), -20, "Ring");
                Shapes.Make(ripple, Shapes.Circle, Palette.Night, Vector2.zero, new Vector2(size - 0.2f, size * 0.6f - 0.2f), -19, "Water");
                _decor.Add((ripple, at, 0.35f, 8 * 3f));
            }
            for (int i = 0; i < 16; i++)
            {
                float size = Mathf.Lerp(0.14f, 0.26f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-5f, 5f, (float)random.NextDouble()), -12f + i * 1.5f);
                var dot = Shapes.Make(t, Shapes.Circle, Palette.Slate, at, new Vector2(size, size), -21, "Speck").transform;
                _decor.Add((dot, at, 0.15f, 16 * 1.5f));
            }

            _world = Make("World").transform;

            _shadow = Shapes.Make(_world, Shapes.Circle, Palette.Ink, Vector2.zero, new Vector2(0.8f, 0.6f), 18, "Shadow").transform;
            // The frog: a root that moves, turns and hops, and the sprite under it, which squashes.
            _frog = new GameObject("Frog").transform;
            _frog.SetParent(_world, false);
            _frogSprite = new GameObject("Body").AddComponent<SpriteRenderer>();
            _frogSprite.sprite = GameSprite("frog");
            _frogSprite.sortingOrder = 20;
            _frogBody = _frogSprite.transform;
            _frogBody.SetParent(_frog, false);

            for (int i = 0; i < MaxDots; i++)
            {
                var dot = Shapes.Make(_world, Shapes.Circle, Palette.Cloud, Vector2.zero, new Vector2(DotSize, DotSize), 15, "Dot");
                dot.enabled = false;
                _dots.Add(dot);
            }
            _mark = Shapes.Make(_world, Shapes.Circle, Palette.Paper, Vector2.zero, new Vector2(MarkSize, MarkSize), 16, "Mark");
            _mark.enabled = false;

            SyncViews(0f);
        }

        protected override void Play(float dt)
        {
            var events = LeapEvent.None;

            // Pull back anywhere: the jump goes the other way, as far as the pull is long.
            if (Input.Down)
            {
                _pullStart = Input.Position;
                _pulling = true;
            }
            if (_pulling) _pull = _pullStart - Input.Position;
            if (_pulling && Input.Up)
            {
                _pulling = false;
                if (_sim.Jump(_pull))
                {
                    events |= LeapEvent.Jumped;
                    Juice.Squash(_frogBody, 0.25f);
                    Juice.Burst(_world, _sim.From, Palette.Lime, 6, 2.5f);
                }
            }
            else if (!Input.Pressed) _pulling = false;

            int before = _sim.Score;
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & LeapEvent.Landed) != 0)
            {
                Juice.Squash(_frogBody, 0.3f);
                if (_pads.TryGetValue(_sim.LastPad, out var view)) Juice.Squash(view.Body, 0.12f);
                Juice.Burst(_world, _sim.Frog, Palette.Lime, 8, 3f);
            }
            if ((events & LeapEvent.Gem) != 0)
            {
                Juice.Burst(_world, _sim.Frog, Palette.Teal, 12, 5f);
                Juice.ShakeSmall();
            }
            if ((events & LeapEvent.Sank) != 0)
                Juice.Burst(_world, _sim.LastPad.At(_sim.Time), Palette.Tan, 10, 3.5f);
            if ((events & LeapEvent.Splashed) != 0)
            {
                Juice.Burst(_world, _sim.SplashPoint, Palette.Sky, 16, 6f);
                Juice.Burst(_world, _sim.SplashPoint, Palette.Cloud, 8, 4f);
                Juice.ShakeBig();
                _sinkT = 0f;
                _pulling = false;
                EndRun();
            }
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            float cam = _sim.CamY;
            _world.localPosition = new Vector2(0f, -cam);
            foreach (var (t, at, drift, span) in _decor)
                t.localPosition = new Vector2(at.x, Mathf.Repeat(at.y - cam * drift + span / 2f, span) - span / 2f);

            SyncPads();
            SyncFrog(dt);
            SyncAim();
        }

        void SyncFrog(float dt)
        {
            var at = _sim.Frog;
            float hop = 0f;
            if (_sim.Flying)
            {
                // Seen from above, a leap is the frog growing toward the camera and back.
                hop = Mathf.Sin(_sim.FlightT * Mathf.PI);
                var dir = _sim.To - _sim.From;
                _facing = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            }
            else if (_pulling && _pull.magnitude >= LeapSim.MinPull)
            {
                var dir = LeapSim.JumpFor(_pull);
                _facing = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            }

            float scale = 1f + HopScale * hop;
            if (State == Phase.Ready)
            {
                _readyTime += dt;
                if (Mathf.Repeat(_readyTime, 1.2f) < dt) Juice.Squash(_frogBody, 0.12f);
            }
            if (_sim.Over)
            {
                // In the water: it shrinks away under the surface.
                _sinkT = Mathf.Min(1f, _sinkT + dt / 0.45f);
                scale = 1f - _sinkT;
            }
            else if (_pulling && !_sim.Flying)
            {
                // Crouch as the pull grows.
                float power = Mathf.Clamp01(_pull.magnitude / LeapSim.MaxPull);
                scale = 1f - 0.12f * power;
            }

            _frog.localPosition = at + new Vector2(0f, 0.1f * hop);
            _frog.localRotation = Quaternion.Euler(0f, 0f, _facing);
            _frog.localScale = Vector3.one * scale;

            _shadow.gameObject.SetActive(_sim.Flying);
            if (_sim.Flying)
            {
                _shadow.localPosition = at + new Vector2(0.15f, -0.2f) * (1f + hop);
                _shadow.localScale = new Vector3(0.8f, 0.6f, 1f) * (1f - 0.3f * hop);
            }
        }

        void SyncAim()
        {
            int used = 0;
            bool show = _pulling && !_sim.Flying && !_sim.Over && _pull.magnitude >= LeapSim.MinPull;
            _mark.enabled = show;
            if (show)
            {
                var from = _sim.Frog;
                var to = _sim.AimPoint(_pull);
                float len = (to - from).magnitude;
                for (float d = 0.7f; d < len - DotStep * 0.6f && used < MaxDots; d += DotStep)
                {
                    var dot = _dots[used++];
                    dot.transform.localPosition = Vector2.Lerp(from, to, d / len);
                    dot.enabled = true;
                }
                _mark.transform.localPosition = to;
            }
            for (int i = used; i < _dots.Count; i++) _dots[i].enabled = false;
        }

        void SyncPads()
        {
            foreach (var view in _pads.Values) view.Seen = false;
            foreach (var pad in _sim.Pads)
            {
                if (pad.Sunk) continue;
                if (!_pads.TryGetValue(pad, out var view)) _pads[pad] = view = MakePad(pad);
                view.Seen = true;
                view.Root.localPosition = pad.At(_sim.Time);
                if (view.Gem != null && pad.GemTaken) view.Gem.enabled = false;
                if (view.Gem != null && view.Gem.enabled)
                    view.Gem.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f + pad.Y) * 12f);

                // A sinking pad wobbles and shrinks as it goes under.
                float s = pad.Sinking(_sim.Time);
                if (s > 0f)
                {
                    float k = pad.Radius / ArtRadius * (1f - 0.4f * s * s);
                    view.Root.localScale = new Vector3(k, k, 1f);
                    view.Root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_sim.Time * 30f) * 6f * s);
                }
            }
            _gone.Clear();
            foreach (var pair in _pads) if (!pair.Value.Seen) _gone.Add(pair.Key);
            foreach (var pad in _gone)
            {
                Destroy(_pads[pad].Root.gameObject);
                _pads.Remove(pad);
            }
        }

        PadView MakePad(LeapPad pad)
        {
            var sprite = pad.Kind == PadKind.Drifting ? _padDrift : pad.Kind == PadKind.Sinking ? _padSink : _pad;
            var root = new GameObject($"Pad {pad.Index}").transform;
            root.SetParent(_world, false);
            root.localPosition = pad.At(_sim.Time);
            float k = pad.Radius / ArtRadius;
            root.localScale = new Vector3(k, k, 1f);
            var sr = new GameObject("Body").AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 5;
            sr.transform.SetParent(root, false);
            var view = new PadView { Root = root, Body = sr.transform };
            if (pad.HasGem && !pad.GemTaken)
            {
                view.Gem = new GameObject("Gem").AddComponent<SpriteRenderer>();
                view.Gem.sprite = _gem;
                view.Gem.sortingOrder = 7;
                view.Gem.transform.SetParent(view.Root, false);
                view.Gem.transform.localScale = Vector3.one * (GemScale / k);
            }
            return view;
        }
    }
}
