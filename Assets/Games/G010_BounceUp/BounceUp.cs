using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.BounceUp
{
    /// <summary>
    /// Game 010. A yellow bouncer springs off every pad it lands on; hold the left or right half of
    /// the screen to steer it up from pad to pad. The sides wrap round. Falling off the bottom or
    /// touching a red burr ends the run. Height scores 1 a unit, teal gems 5.
    /// The rules live in <see cref="BounceSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(10, "bounce-up", "Bounce Up", Controls.Hold, Blurb = "Hold left or right to steer")]
    public sealed class BounceUp : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        // The spring is 80 x 72 px, its coil's foot 34 px below its centre.
        const float SpringFoot = 32f / Style.PixelsPerUnit;
        const float TiltPerSpeed = 2.2f;
        const float DeadGravity = 30f;
        const float GroundDepth = 6f;

        BounceSim _sim;
        Sprite _pad, _padMoving, _padCrumbly, _spring, _burr, _gem;
        Transform _world, _bouncer, _body, _ghost;
        SpriteRenderer _bodySprite, _ghostSprite;
        readonly List<(Transform t, Vector2 at, float drift, float span)> _decor = new List<(Transform, Vector2, float, float)>();

        sealed class PadView
        {
            public Transform Root;
            public Transform Spring;
            public bool Broken, Seen;
        }

        readonly Dictionary<BouncePad, PadView> _pads = new Dictionary<BouncePad, PadView>();
        readonly Dictionary<BounceBurr, Transform> _burrs = new Dictionary<BounceBurr, Transform>();
        readonly Dictionary<BounceGem, SpriteRenderer> _gems = new Dictionary<BounceGem, SpriteRenderer>();
        readonly List<object> _gone = new List<object>();
        readonly HashSet<object> _seen = new HashSet<object>();

        float _readyTime, _deadY, _deadVy, _facing = 1f;

        public BounceSim Sim => _sim;

        protected override void Build()
        {
            _sim = new BounceSim(SeedForTests ?? System.Environment.TickCount);
            _pad = GameSprite("pad");
            _padMoving = GameSprite("pad_moving");
            _padCrumbly = GameSprite("pad_crumbly");
            _spring = GameSprite("spring");
            _burr = GameSprite("burr");
            _gem = PropSprite("gem");
            var t = transform;

            // Background: Slate clouds and small Slate stars, drifting down slower than the climb.
            var random = new System.Random(10);
            for (int i = 0; i < 7; i++)
            {
                float size = Mathf.Lerp(1f, 1.8f, (float)random.NextDouble());
                var at = new Vector2((i % 2 == 0 ? -1f : 1f) * Mathf.Lerp(1.5f, 4.5f, (float)random.NextDouble()), -12f + i * 3.6f);
                var cloud = Make("Cloud", at).transform;
                Shapes.Make(cloud, Shapes.Circle, Palette.Slate, Vector2.zero, new Vector2(size, size), -20, "Puff");
                Shapes.Make(cloud, Shapes.Circle, Palette.Slate, new Vector2(size * 0.45f, -size * 0.12f), new Vector2(size * 0.7f, size * 0.7f), -20, "Puff");
                Shapes.Make(cloud, Shapes.Circle, Palette.Slate, new Vector2(-size * 0.45f, -size * 0.18f), new Vector2(size * 0.6f, size * 0.6f), -20, "Puff");
                _decor.Add((cloud, at, 0.3f, 7 * 3.6f));
            }
            for (int i = 0; i < 14; i++)
            {
                float size = Mathf.Lerp(0.14f, 0.24f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-5f, 5f, (float)random.NextDouble()), -12f + i * 1.8f);
                var star = Shapes.Make(t, Shapes.Circle, Palette.Slate, at, new Vector2(size, size), -21, "Star").transform;
                _decor.Add((star, at, 0.1f, 14 * 1.8f));
            }

            _world = Make("World").transform;
            Shapes.Box(_world, new Vector2(0f, BounceSim.GroundTop - GroundDepth / 2f), new Vector2(30f, GroundDepth), Palette.Brown, Palette.BrownDark, 2, "Ground");

            _bouncer = new GameObject("Bouncer").transform;
            _bouncer.SetParent(_world, false);
            _bodySprite = new GameObject("Body").AddComponent<SpriteRenderer>();
            _bodySprite.sprite = GameSprite("bouncer");
            _bodySprite.sortingOrder = 20;
            _body = _bodySprite.transform;
            _body.SetParent(_bouncer, false);

            // A copy drawn on the other side while the bouncer crosses an edge, so the wrap reads.
            _ghostSprite = new GameObject("Ghost").AddComponent<SpriteRenderer>();
            _ghostSprite.sprite = _bodySprite.sprite;
            _ghostSprite.sortingOrder = 20;
            _ghost = _ghostSprite.transform;
            _ghost.SetParent(_world, false);

            SyncViews(0f);
        }

        protected override void Play(float dt)
        {
            _sim.Steer(Input.Pressed ? (Input.Position.x < 0f ? -1 : 1) : 0);
            int before = _sim.Score;
            var events = _sim.Step(dt);
            AddScore(_sim.Score - before);

            var feet = new Vector2(_sim.X, _sim.Y - BounceSim.FootY);
            if ((events & BounceEvent.Sprang) != 0)
            {
                Juice.Squash(_body, 0.3f);
                if (_pads.TryGetValue(_sim.LastPad, out var view) && view.Spring != null) Juice.Squash(view.Spring, 0.4f);
                Juice.Burst(_world, feet, Palette.Lime, 10, 5f);
                Juice.ShakeSmall();
            }
            else if ((events & BounceEvent.Bounced) != 0)
            {
                Juice.Squash(_body, 0.22f);
                Juice.Burst(_world, feet, Palette.Cloud, 5, 2.5f);
            }
            if ((events & BounceEvent.Crumbled) != 0)
            {
                var pad = _sim.LastPad;
                Juice.Burst(_world, new Vector2(pad.X, pad.Y), Palette.Brown, 12, 4f);
                Juice.Burst(_world, new Vector2(pad.X, pad.Y), Palette.Tan, 8, 3f);
            }
            if ((events & BounceEvent.Gem) != 0)
            {
                Juice.Burst(_world, _sim.GemPoint, Palette.Teal, 12, 5f);
                Juice.ShakeSmall();
            }
            if ((events & BounceEvent.Crashed) != 0)
            {
                Juice.Burst(_world, _sim.CrashPoint, Palette.Red, 16, 6f);
                Juice.Squash(_body, 0.3f);
                Juice.ShakeBig();
                _deadY = _sim.Y;
                _deadVy = 4f;
                EndRun();
            }
            if ((events & BounceEvent.Fell) != 0)
            {
                Juice.Burst(_world, _sim.CrashPoint, Palette.Orange, 12, 5f);
                Juice.ShakeBig();
                _deadY = _sim.Y;
                _deadVy = _sim.Vy;
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

            // The bouncer: faces the way it's going and leans into it.
            float y = _sim.Y, tilt = -_sim.Vx * TiltPerSpeed;
            if (State == Phase.Ready)
            {
                _readyTime += dt;
                y = BounceSim.StartY;
                if (Mathf.Repeat(_readyTime, 1.2f) < dt) Juice.Squash(_body, 0.12f);
            }
            else if (_sim.Over)
            {
                // After a crash it tumbles down off the screen.
                _deadVy -= DeadGravity * dt;
                _deadY += _deadVy * dt;
                y = _deadY;
                tilt = _sim.Crashed ? 180f : tilt;
            }
            if (Mathf.Abs(_sim.Vx) > 0.5f) _facing = Mathf.Sign(_sim.Vx);
            _bodySprite.flipX = _facing < 0f;
            _bouncer.localPosition = new Vector2(_sim.X, y);
            _bouncer.localRotation = Quaternion.Euler(0f, 0f, tilt);

            float edge = BounceSim.HalfWidth - 0.6f;
            bool ghost = Mathf.Abs(_sim.X) > edge;
            if (_ghostSprite.enabled != ghost) _ghostSprite.enabled = ghost;
            if (ghost)
            {
                _ghost.localPosition = new Vector2(_sim.X - Mathf.Sign(_sim.X) * Style.WorldWidth, y);
                _ghost.localRotation = _bouncer.localRotation;
                _ghost.localScale = _body.localScale;
                _ghostSprite.flipX = _bodySprite.flipX;
            }

            SyncPads();
            SyncBurrs();
            SyncGems();
        }

        void SyncPads()
        {
            foreach (var view in _pads.Values) view.Seen = false;
            foreach (var pad in _sim.Pads)
            {
                if (pad.Kind == PadKind.Ground) continue;
                if (!_pads.TryGetValue(pad, out var view)) _pads[pad] = view = MakePad(pad);
                view.Seen = true;
                view.Root.localPosition = new Vector2(pad.X, pad.Y);
                if (pad.Broken && !view.Broken)
                {
                    view.Broken = true;
                    view.Root.gameObject.SetActive(false);
                }
            }
            _gone.Clear();
            foreach (var pair in _pads) if (!pair.Value.Seen) _gone.Add(pair.Key);
            foreach (BouncePad pad in _gone)
            {
                Destroy(_pads[pad].Root.gameObject);
                _pads.Remove(pad);
            }
        }

        PadView MakePad(BouncePad pad)
        {
            var sprite = pad.Kind == PadKind.Moving ? _padMoving : pad.Kind == PadKind.Crumbly ? _padCrumbly : _pad;
            var sr = new GameObject($"Pad {pad.Index}").AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 5;
            sr.transform.SetParent(_world, false);
            sr.transform.localPosition = new Vector2(pad.X, pad.Y);
            var view = new PadView { Root = sr.transform };
            if (pad.HasSpring)
            {
                var spring = new GameObject("Spring").AddComponent<SpriteRenderer>();
                spring.sprite = _spring;
                spring.sortingOrder = 6;
                view.Spring = spring.transform;
                view.Spring.SetParent(view.Root, false);
                view.Spring.localPosition = new Vector2(0f, BounceSim.PadHalfHeight + SpringFoot);
            }
            return view;
        }

        void SyncBurrs()
        {
            _seen.Clear();
            foreach (var burr in _sim.Burrs)
            {
                _seen.Add(burr);
                if (!_burrs.TryGetValue(burr, out var t))
                {
                    var sr = new GameObject("Burr").AddComponent<SpriteRenderer>();
                    sr.sprite = _burr;
                    sr.sortingOrder = 8;
                    t = sr.transform;
                    t.SetParent(_world, false);
                    t.localPosition = new Vector2(burr.X, burr.Y);
                    _burrs[burr] = t;
                }
                t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2f + burr.X) * 14f);
            }
            _gone.Clear();
            foreach (var key in _burrs.Keys) if (!_seen.Contains(key)) _gone.Add(key);
            foreach (BounceBurr burr in _gone)
            {
                Destroy(_burrs[burr].gameObject);
                _burrs.Remove(burr);
            }
        }

        void SyncGems()
        {
            _seen.Clear();
            foreach (var gem in _sim.GemList)
            {
                _seen.Add(gem);
                if (!_gems.TryGetValue(gem, out var sr))
                {
                    sr = new GameObject("Gem").AddComponent<SpriteRenderer>();
                    sr.sprite = _gem;
                    sr.sortingOrder = 7;
                    sr.transform.SetParent(_world, false);
                    sr.transform.localPosition = new Vector2(gem.X, gem.Y);
                    sr.transform.localScale = Vector3.one * 0.6f;
                    _gems[gem] = sr;
                }
                if (gem.Taken && sr.enabled) sr.enabled = false;
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f + gem.Y) * 12f);
            }
            _gone.Clear();
            foreach (var key in _gems.Keys) if (!_seen.Contains(key)) _gone.Add(key);
            foreach (BounceGem gem in _gone)
            {
                Destroy(_gems[gem].gameObject);
                _gems.Remove(gem);
            }
        }
    }
}
