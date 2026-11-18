using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.RainDodge
{
    /// <summary>
    /// Game 017. Drag anywhere to fly a yellow bee around a stormy field while red drops fall
    /// from the clouds. Every drop that reaches the ground scores 1, teal gems score 5, and a
    /// green leaf takes one hit for the bee. Big drops, swaying drops and lightning join in.
    /// The rules live in <see cref="RainSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(17, "rain-dodge", "Rain Dodge", Controls.Drag, Blurb = "Drag the bee between the drops")]
    public sealed class RainDodge : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        const float Px = 1f / Style.PixelsPerUnit;
        const float BoltHeight = 1560 * Px, BoltTop = RainSim.GroundY + BoltHeight - 0.04f;
        static readonly Vector2 WingAt = new Vector2(-0.12f, 0.42f), ShieldAt = new Vector2(0f, 0.66f);
        const float StreakSpeed = 13f, DashGap = 0.9f;
        const float DeadGravity = 22f;

        RainSim _sim;
        Sprite _drop, _big, _gemSprite, _leafSprite, _boltSprite;
        Transform _bee, _wing, _shield;
        SpriteRenderer _body, _wingSprite, _shieldSprite, _gem, _leaf;
        readonly Dictionary<int, SpriteRenderer> _drops = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, BoltView> _bolts = new Dictionary<int, BoltView>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _gone = new List<int>();
        readonly List<Transform> _streaks = new List<Transform>();
        readonly List<(Transform t, float x0, float drift)> _clouds = new List<(Transform, float, float)>();
        RainPickup _shownGem, _shownLeaf;
        Vector2 _lastBee;
        float _tilt, _clock, _deadY, _deadVy, _deadSpin;
        bool _faceLeft;

        sealed class BoltView
        {
            public Transform Root;
            public SpriteRenderer Bolt, Flash;
            public SpriteRenderer[] Dashes;
            public bool Struck;
        }

        public RainSim Sim => _sim;

        protected override void Build()
        {
            _sim = new RainSim(SeedForTests ?? System.Environment.TickCount);
            _drop = GameSprite("drop");
            _big = GameSprite("drop_big");
            _boltSprite = GameSprite("bolt");
            _gemSprite = PropSprite("gem");
            _leafSprite = GameSprite("leaf");
            var t = transform;

            // Rain that doesn't hurt: Slate streaks falling fast, behind everything.
            var random = new System.Random(17);
            for (int i = 0; i < 34; i++)
            {
                var at = new Vector2(Mathf.Lerp(-5f, 5f, (float)random.NextDouble()), Mathf.Lerp(-9f, 9f, (float)random.NextDouble()));
                _streaks.Add(Shapes.Make(t, Shapes.Square, Palette.Slate, at, new Vector2(0.14f, 0.7f), -20, "Streak").transform);
            }

            // Far hills, then the grass.
            for (int i = 0; i < 5; i++)
                Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-5f + i * 2.6f, RainSim.GroundY - 0.4f + (i % 2) * 0.5f), new Vector2(4f, 2.6f), -18, "Hill");
            Shapes.Box(t, new Vector2(0f, RainSim.GroundY - 2f), new Vector2(11f, 4f), Palette.Green, Palette.GreenDark, 5, "Grass");

            // Storm clouds along the top: the drops come out of them.
            for (int i = 0; i < 11; i++)
            {
                float size = 2f + (i % 3) * 0.3f;
                var at = new Vector2(-6f + i * 1.2f, 6.25f + (i % 2) * 0.25f);
                var puff = Shapes.Make(t, Shapes.Circle, Palette.Slate, at, new Vector2(size, size * 0.85f), 26, "Cloud").transform;
                _clouds.Add((puff, at.x, 0.15f + (i % 3) * 0.05f));
            }
            Shapes.Make(t, Shapes.Square, Palette.Slate, new Vector2(0f, 7.8f), new Vector2(12f, 2.4f), 26, "CloudTop");

            _bee = Make("Bee", RainSim.StartAt).transform;
            _wingSprite = Child(_bee, "Wing", GameSprite("wing"), WingAt, 19);
            _wing = _wingSprite.transform;
            _body = Child(_bee, "Body", GameSprite("bee"), Vector2.zero, 20);
            _shieldSprite = Child(_bee, "Leaf", _leafSprite, ShieldAt, 21);
            _shield = _shieldSprite.transform;
            _shieldSprite.enabled = false;

            _gem = MakeSprite("Gem", _gemSprite, Vector2.zero, 12);
            _gem.transform.localScale = Vector3.one * 0.7f;
            _gem.enabled = false;
            _leaf = MakeSprite("LeafPickup", _leafSprite, Vector2.zero, 12);
            _leaf.enabled = false;

            _lastBee = RainSim.StartAt;
            SyncViews(0f);
        }

        static SpriteRenderer Child(Transform parent, string name, Sprite sprite, Vector2 at, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.transform.SetParent(parent, false);
            sr.transform.localPosition = at;
            return sr;
        }

        protected override void Play(float dt)
        {
            if (Input.Down) _sim.Grab();
            else if (Input.Pressed) _sim.Drag(Input.DragDelta);

            int before = _sim.Score;
            var events = _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & RainEvent.Splash) != 0)
                foreach (var at in _sim.Splashes) Juice.Burst(transform, at, Palette.Orange, 4, 2.2f);
            if ((events & RainEvent.Gem) != 0)
            {
                Juice.Burst(transform, _sim.LastPoint, Palette.Teal, 14, 5f);
                Juice.ShakeSmall();
            }
            if ((events & RainEvent.Leaf) != 0)
            {
                Juice.Burst(transform, _sim.LastPoint, Palette.Lime, 12, 4f);
                _shieldSprite.enabled = true;
                Juice.Pop(_shield);
            }
            if ((events & RainEvent.ShieldLost) != 0)
            {
                Juice.Burst(transform, (Vector2)_shield.position - (Vector2)transform.position, Palette.Lime, 16, 5.5f);
                Juice.Squash(_body.transform, 0.25f);
                Juice.ShakeSmall();
            }
            if ((events & RainEvent.Strike) != 0) Juice.ShakeSmall();
            if ((events & RainEvent.Hit) != 0)
            {
                Juice.Burst(transform, _sim.LastPoint, Palette.Orange, 16, 6f);
                Juice.Burst(transform, _sim.Bee, Palette.Yellow, 10, 4.5f);
                Juice.Squash(_body.transform, 0.3f);
                Juice.ShakeBig();
                _deadY = _sim.Bee.y;
                _deadVy = 3f;
                EndRun();
            }
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            _clock += dt;
            float time = _sim.Time;

            foreach (var s in _streaks)
            {
                var p = s.localPosition;
                p.y -= StreakSpeed * dt;
                if (p.y < -9f) p.y += 18f;
                s.localPosition = p;
            }
            foreach (var (c, x0, drift) in _clouds)
            {
                var p = c.localPosition;
                p.x = Mathf.Repeat(x0 + _clock * drift + 6f, 13.2f) - 6f;
                c.localPosition = p;
            }

            SyncBee(dt);
            SyncDrops(time);
            SyncBolts(time);
            _shownGem = SyncPickup(_gem, _sim.Gem, _shownGem, time, 0.7f);
            _shownLeaf = SyncPickup(_leaf, _sim.Leaf, _shownLeaf, time, 0.85f);
        }

        void SyncBee(float dt)
        {
            var at = _sim.Bee;
            float speedX = dt > 0f ? (at.x - _lastBee.x) / dt : 0f;
            _lastBee = at;
            if (speedX > 0.5f) _faceLeft = false;
            else if (speedX < -0.5f) _faceLeft = true;
            _body.flipX = _faceLeft;
            _wingSprite.flipX = _faceLeft;
            _wing.localPosition = new Vector2(_faceLeft ? -WingAt.x : WingAt.x, WingAt.y);

            float flap = Mathf.Abs(Mathf.Sin(_clock * 28f));
            _wing.localScale = new Vector3(1f, 0.55f + 0.45f * flap, 1f);
            _tilt = Mathf.Lerp(_tilt, Mathf.Clamp(-speedX * 1.4f, -20f, 20f), 1f - Mathf.Exp(-12f * dt));

            if (State == Phase.Ready) at.y += Mathf.Sin(_clock * 3f) * 0.12f;
            else if (_sim.Over)
            {
                // Knocked out of the air: it tumbles to the grass.
                _deadVy -= DeadGravity * dt;
                _deadY = Mathf.Max(RainSim.GroundY + 0.3f, _deadY + _deadVy * dt);
                _deadSpin += 360f * dt;
                at.y = _deadY;
                _tilt = _deadY > RainSim.GroundY + 0.3f ? _deadSpin : 180f;
                _wing.localScale = new Vector3(1f, 0.6f, 1f);
            }
            _bee.localPosition = at;
            _bee.localRotation = Quaternion.Euler(0f, 0f, _tilt);

            bool show = _sim.SafeFor <= 0f || Mathf.Repeat(_sim.SafeFor, 0.2f) > 0.1f;
            _body.enabled = show;
            _wingSprite.enabled = show;
            _shieldSprite.enabled = _sim.Shield && !_sim.Over;
            _shield.localRotation = Quaternion.Euler(0f, 0f, -_tilt * 0.5f + Mathf.Sin(_clock * 4f) * 4f);
        }

        void SyncDrops(float time)
        {
            _seen.Clear();
            foreach (var d in _sim.Drops)
            {
                _seen.Add(d.Index);
                if (!_drops.TryGetValue(d.Index, out var sr))
                    _drops[d.Index] = sr = MakeSprite($"Drop {d.Index}", d.Kind == DropKind.Big ? _big : _drop, d.At(time), d.Kind == DropKind.Big ? 15 : 16);
                sr.transform.localPosition = d.At(time);
                // A swaying drop leans the way it's drifting.
                float lean = d.Kind == DropKind.Sway ? Mathf.Atan2(d.DriftAt(time), d.Speed) * Mathf.Rad2Deg : 0f;
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, lean);
            }
            _gone.Clear();
            foreach (var id in _drops.Keys) if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                Destroy(_drops[id].gameObject);
                _drops.Remove(id);
            }
        }

        void SyncBolts(float time)
        {
            _seen.Clear();
            foreach (var b in _sim.Bolts)
            {
                _seen.Add(b.Index);
                if (!_bolts.TryGetValue(b.Index, out var view)) _bolts[b.Index] = view = MakeBolt(b);

                // The warning: red dashes down the column, blinking faster as the strike nears,
                // and the cloud above it lights up. Then the bolt.
                bool striking = time >= b.StrikeAt;
                float left = b.StrikeAt - time;
                float rate = left > 0.4f ? 6f : 14f;
                bool dashesOn = !striking && Mathf.Repeat(time * rate, 1f) < 0.6f;
                foreach (var dash in view.Dashes) dash.enabled = dashesOn;
                view.Flash.enabled = striking || Mathf.Repeat(time * rate, 1f) < 0.3f;
                view.Bolt.enabled = striking;
                if (striking && !view.Struck)
                {
                    view.Struck = true;
                    Juice.Burst(transform, new Vector2(b.X, RainSim.GroundY), Palette.Orange, 12, 4.5f);
                }
            }
            _gone.Clear();
            foreach (var id in _bolts.Keys) if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                Destroy(_bolts[id].Root.gameObject);
                _bolts.Remove(id);
            }
        }

        BoltView MakeBolt(Bolt b)
        {
            var root = Make($"Bolt {b.Index}", new Vector2(b.X, 0f)).transform;
            var view = new BoltView { Root = root };
            var dashes = new List<SpriteRenderer>();
            for (float y = RainSim.CloudBottom - 0.5f; y > RainSim.GroundY + 0.2f; y -= DashGap)
                dashes.Add(Shapes.Make(root, Shapes.Square, Palette.Red, new Vector2(0f, y), new Vector2(0.16f, 0.42f), 14, "Dash"));
            dashes.Add(Shapes.Make(root, Shapes.Square, Palette.Red, new Vector2(0f, RainSim.GroundY + 0.05f), new Vector2(1f, 0.16f), 14, "Mark"));
            view.Dashes = dashes.ToArray();
            view.Flash = Shapes.Make(root, Shapes.Circle, Palette.Mist, new Vector2(0f, 6.2f), new Vector2(1.8f, 1.4f), 27, "Flash");
            view.Bolt = MakeSprite("Strike", _boltSprite, Vector2.zero, 17);
            view.Bolt.transform.SetParent(root, false);
            view.Bolt.transform.localPosition = new Vector2(0f, BoltTop - BoltHeight / 2f);
            view.Bolt.enabled = false;
            return view;
        }

        RainPickup SyncPickup(SpriteRenderer sr, RainPickup p, RainPickup shown, float time, float scale)
        {
            if (p == null)
            {
                sr.enabled = false;
                return null;
            }
            if (p != shown)
            {
                sr.transform.localScale = Vector3.one * scale;
                Juice.Pop(sr.transform);
            }
            // Bobbing, and blinking for its last 1.5 s.
            float left = p.Until - time;
            sr.enabled = left > 1.5f || Mathf.Repeat(left, 0.24f) > 0.1f;
            sr.transform.localPosition = p.At + new Vector2(0f, Mathf.Sin(_clock * 3f + p.At.x) * 0.08f);
            sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_clock * 2.5f + p.At.y) * 10f);
            return p;
        }
    }
}
