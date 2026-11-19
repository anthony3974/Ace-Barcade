using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.WingIt
{
    /// <summary>
    /// Game 005. Tap to flap a bee through the gaps in thorny red columns, over a floor of spikes.
    /// Each column passed scores 1; a teal gem in every 4th gap scores 5. The columns come faster
    /// and their gaps get narrower, up to a cap.
    /// The rules live in <see cref="WingSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(5, "wing-it", "Wing It", Controls.Tap, Blurb = "Tap to flap through the gaps")]
    public sealed class WingIt : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        // The thorn cap is 168 x 80 px; its tips are 8 px from its top edge.
        const float CapTip = (40f - 8f) / Style.PixelsPerUnit;
        // The tips reach a little past the edge that counts as a hit, so grazes look fair.
        const float TipReach = 0.12f;
        const float ColumnReach = 14f;

        // The floor: the shared spikes prop (1080 x 120 px, tips 14 px from the top), scrolling.
        const float SpikesHeight = 120f / Style.PixelsPerUnit;
        const float SpikesTip = (60f - 14f) / Style.PixelsPerUnit;
        const float SpikePeriod = 90f / Style.PixelsPerUnit;

        // The bee: tilts with its speed, wings beat faster just after a flap.
        const float TiltPerSpeed = 3.5f;
        const float WingRest = 25f, WingSwing = 35f;
        const float DeadGravity = 30f;

        WingSim _sim;
        Sprite _thorns, _gem;
        Transform _bee, _body, _wingPivot;
        readonly List<Transform> _floor = new List<Transform>();
        readonly List<(Transform t, float x, float drift)> _hills = new List<(Transform, float, float)>();

        sealed class ColumnView
        {
            public Transform Root;
            public SpriteRenderer Gem;
            public bool Seen;
        }

        readonly Dictionary<WingColumn, ColumnView> _views = new Dictionary<WingColumn, ColumnView>();
        readonly List<WingColumn> _gone = new List<WingColumn>();

        float _wingPhase, _wingBoost, _readyTime, _deadY, _deadVelocity;

        public WingSim Sim => _sim;

        protected override void Build()
        {
            _sim = new WingSim(SeedForTests ?? System.Environment.TickCount);
            _thorns = GameSprite("thorns");
            _gem = PropSprite("gem");
            var t = transform;

            // Background: a Slate moon and rolling Slate hills that drift slower than the columns.
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-2.6f, 4.4f), new Vector2(2.2f, 2.2f), -20, "Moon");
            var random = new System.Random(8);
            for (int i = 0; i < 7; i++)
            {
                float size = Mathf.Lerp(3f, 5.5f, (float)random.NextDouble());
                float x = -10f + i * 3.4f;
                var hill = Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(x, WingSim.FloorY - 0.4f), new Vector2(size, size), -18, "Hill").transform;
                _hills.Add((hill, x, 0.3f));
            }

            float spikesY = WingSim.FloorY - SpikesTip;
            for (int i = -1; i <= 1; i++)
                _floor.Add(MakeSprite("Spikes", PropSprite("spikes"), new Vector2(i * Style.WorldWidth, spikesY), 8).transform);
            Shapes.Make(t, Shapes.Square, Palette.RedDark, new Vector2(0f, spikesY - SpikesHeight / 2f - 2.5f), new Vector2(30f, 5.2f), 8, "Pit");

            _bee = Make("Bee", new Vector2(WingSim.BeeX, WingSim.StartY)).transform;
            var body = new GameObject("Body").AddComponent<SpriteRenderer>();
            body.sprite = GameSprite("bee");
            body.sortingOrder = 20;
            _body = body.transform;
            _body.SetParent(_bee, false);

            _wingPivot = new GameObject("WingPivot").transform;
            _wingPivot.SetParent(_bee, false);
            _wingPivot.localPosition = new Vector2(-0.12f, 0.2f);
            var wing = new GameObject("Wing").AddComponent<SpriteRenderer>();
            wing.sprite = GameSprite("wing");
            wing.sortingOrder = 21;
            wing.transform.SetParent(_wingPivot, false);
            wing.transform.localPosition = new Vector2(-0.12f, 0.16f);

            SyncViews(0f);
        }

        protected override void Play(float dt)
        {
            var events = WingEvent.None;
            if (Input.Down)
            {
                _sim.Flap();
                events |= WingEvent.Flapped;
            }

            int before = _sim.Score;
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & WingEvent.Flapped) != 0)
            {
                _wingBoost = 1f;
                Juice.Squash(_body, 0.1f);
            }
            if ((events & WingEvent.Bumped) != 0) Juice.Squash(_body, 0.2f);
            if ((events & WingEvent.Gem) != 0)
            {
                Juice.Burst(transform, _sim.GemPoint, Palette.Teal, 12, 5f);
                Juice.ShakeSmall();
            }
            if ((events & WingEvent.Crashed) != 0)
            {
                Juice.Burst(transform, _sim.CrashPoint, Palette.Red, 16, 6f);
                Juice.ShakeBig();
                Juice.Squash(_body, 0.3f);
                _deadY = _sim.Y;
                _deadVelocity = Mathf.Max(_sim.Velocity, 2f);
                EndRun();
            }
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            float y = _sim.Y;
            float tilt = Mathf.Clamp(_sim.Velocity * TiltPerSpeed, -55f, 25f);
            if (State == Phase.Ready)
            {
                // Hovering, waiting for the first flap.
                _readyTime += dt;
                y = WingSim.StartY + Mathf.Sin(_readyTime * 3f) * 0.15f;
                tilt = Mathf.Cos(_readyTime * 3f) * 6f;
            }
            else if (_sim.Crashed)
            {
                // After a crash the bee tumbles down to the spikes.
                _deadVelocity -= DeadGravity * dt;
                _deadY = Mathf.Max(WingSim.FloorY + WingSim.Radius * 0.5f, _deadY + _deadVelocity * dt);
                y = _deadY;
                tilt = -80f;
            }
            _bee.localPosition = new Vector2(WingSim.BeeX, y);
            _bee.localRotation = Quaternion.Euler(0f, 0f, tilt);

            _wingBoost = Mathf.Max(0f, _wingBoost - dt * 3f);
            if (!_sim.Crashed) _wingPhase += dt * (18f + 22f * _wingBoost);
            _wingPivot.localRotation = Quaternion.Euler(0f, 0f, WingRest + Mathf.Sin(_wingPhase) * WingSwing);

            float d = _sim.Distance;
            for (int i = 0; i < _floor.Count; i++)
            {
                float x = (i - 1) * Style.WorldWidth - Mathf.Repeat(d, SpikePeriod);
                _floor[i].localPosition = new Vector2(x, _floor[i].localPosition.y);
            }
            const float hillSpan = 7 * 3.4f;
            foreach (var (hill, x, drift) in _hills)
                hill.localPosition = new Vector2(Mathf.Repeat(x - d * drift + hillSpan / 2f, hillSpan) - hillSpan / 2f, hill.localPosition.y);

            SyncColumns();
        }

        void SyncColumns()
        {
            foreach (var view in _views.Values) view.Seen = false;
            foreach (var column in _sim.Columns)
            {
                if (!_views.TryGetValue(column, out var view)) _views[column] = view = MakeColumn(column);
                view.Seen = true;
                view.Root.localPosition = new Vector2(column.X, 0f);
                if (view.Gem != null)
                {
                    if (!column.HasGem && view.Gem.enabled) view.Gem.enabled = false;
                    view.Gem.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f + column.Index) * 12f);
                }
            }

            _gone.Clear();
            foreach (var pair in _views) if (!pair.Value.Seen) _gone.Add(pair.Key);
            foreach (var column in _gone)
            {
                Destroy(_views[column].Root.gameObject);
                _views.Remove(column);
            }
        }

        ColumnView MakeColumn(WingColumn c)
        {
            var root = Make($"Column {c.Index}", new Vector2(c.X, 0f)).transform;

            // Red, because the columns end the run. Each half reaches well off the screen.
            float topBottom = c.GapTop - TipReach + CapTip;
            Shapes.Box(root, new Vector2(0f, topBottom + ColumnReach / 2f), new Vector2(WingSim.ColumnWidth, ColumnReach), Palette.Red, Palette.RedDark, 0, "Top");
            float bottomTop = c.GapBottom + TipReach - CapTip;
            Shapes.Box(root, new Vector2(0f, bottomTop - ColumnReach / 2f), new Vector2(WingSim.ColumnWidth, ColumnReach), Palette.Red, Palette.RedDark, 0, "Bottom");

            // Thorn caps point into the gap, their tips a little past the edge that counts as a hit.
            MakeChildSprite(root, "ThornsBottom", _thorns, new Vector2(0f, bottomTop), 4);
            var capDown = MakeChildSprite(root, "ThornsTop", _thorns, new Vector2(0f, topBottom), 4);
            capDown.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);

            var view = new ColumnView { Root = root };
            if (c.HasGem)
            {
                view.Gem = MakeChildSprite(root, "Gem", _gem, new Vector2(0f, c.GapY), 5);
                view.Gem.transform.localScale = Vector3.one * 0.8f;
            }
            return view;
        }

        static SpriteRenderer MakeChildSprite(Transform parent, string name, Sprite sprite, Vector2 position, int order)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            sr.transform.SetParent(parent, false);
            sr.transform.localPosition = position;
            return sr;
        }
    }
}
