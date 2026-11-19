using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.RocketLanes
{
    /// <summary>
    /// Game 007. A rocket flies up through three lanes while rows of red mines scroll down at
    /// it. Swipe left or right to change lanes; every row passed scores 1 and teal crystals in
    /// open lanes score 5. The scroll speeds up with every row, up to a cap.
    /// The rules live in <see cref="RocketLanesSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(7, "rocket-lanes", "Rocket Lanes", Controls.Swipe, Blurb = "Swipe between lanes to dodge the mines")]
    public sealed class RocketLanes : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run's rows (otherwise the clock).</summary>
        public static int? SeedForTests;

        // The flame tucks under the rocket's body (the rocket sprite is 160 px tall, body to 58 px below centre).
        const float FlameTop = -0.4f;
        const float FlameHalf = 0.3f;
        const float TiltDegrees = 14f;

        // Background: Slate stars drifting slower than the lanes, Mist dashes between the lanes.
        const int StarCount = 26;
        const float WrapHalf = 11f;
        const float DashLength = 0.9f;
        const float DashWidth = 0.14f;
        const float DashPeriod = 2.2f;

        RocketLanesSim _sim;
        Transform _rocket, _flame;
        Sprite _mineSprite, _crystalSprite;
        float _flamePhase;

        readonly List<(Transform t, float y, float drift)> _stars = new List<(Transform, float, float)>();
        readonly List<(Transform t, float y)> _dashes = new List<(Transform, float)>();

        sealed class RowView
        {
            public Transform Root;
            public readonly SpriteRenderer[] Mines = new SpriteRenderer[RocketLanesSim.Lanes];
            public SpriteRenderer Crystal;
            public bool Shown;
            public int Seen;
        }

        readonly Dictionary<LaneRow, RowView> _views = new Dictionary<LaneRow, RowView>();
        readonly Stack<RowView> _pool = new Stack<RowView>();
        readonly List<LaneRow> _gone = new List<LaneRow>();
        int _frame;

        public RocketLanesSim Sim => _sim;

        protected override void Build()
        {
            _sim = new RocketLanesSim(SeedForTests ?? System.Environment.TickCount);
            _mineSprite = GameSprite("mine");
            _crystalSprite = GameSprite("crystal");

            BuildSpace();

            _rocket = MakeSprite("Rocket", GameSprite("rocket"), new Vector2(_sim.PlayerX, RocketLanesSim.PlayerY), 20).transform;
            var flame = new GameObject("Flame").AddComponent<SpriteRenderer>();
            flame.sprite = GameSprite("flame");
            flame.sortingOrder = 19;
            _flame = flame.transform;
            _flame.SetParent(_rocket, false);
            _flame.localPosition = new Vector2(0f, FlameTop - FlameHalf);

            SyncViews();
        }

        void BuildSpace()
        {
            var t = transform;
            var random = new System.Random(4);
            for (int i = 0; i < StarCount; i++)
            {
                float size = Mathf.Lerp(0.14f, 0.3f, (float)random.NextDouble());
                float x = Mathf.Lerp(-5.6f, 5.6f, (float)random.NextDouble());
                float y = Mathf.Lerp(-WrapHalf, WrapHalf, (float)random.NextDouble());
                var star = Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(x, y), new Vector2(size, size), -20, "Star").transform;
                // Bigger stars are nearer, so they drift faster.
                _stars.Add((star, y, size > 0.22f ? 0.4f : 0.22f));
            }

            float lineX = RocketLanesSim.LaneWidth / 2f;
            int perLine = Mathf.CeilToInt(WrapHalf * 2f / DashPeriod);
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < perLine; i++)
            {
                float y = -WrapHalf + i * DashPeriod;
                var dash = Shapes.Make(t, Shapes.Square, Palette.Mist, new Vector2(side * lineX, y), new Vector2(DashWidth, DashLength), -15, "LaneDash").transform;
                _dashes.Add((dash, y));
            }
        }

        protected override void Play(float dt)
        {
            var swipe = Input.Swipe;
            if (swipe.x != 0f && _sim.Swipe(swipe.x > 0f ? 1 : -1)) Juice.Squash(_rocket);

            int before = _sim.Score;
            var events = _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & LaneEvent.Crystal) != 0)
            {
                Juice.Burst(transform, _sim.CrystalPoint, Palette.Teal, 12, 5f);
                Juice.ShakeSmall();
            }
            if ((events & LaneEvent.Crashed) != 0)
            {
                Juice.Burst(transform, _sim.CrashPoint, Palette.Red, 16, 6f);
                Juice.ShakeBig();
                _rocket.gameObject.SetActive(false);
                EndRun();
            }

            SyncViews();
        }

        void LateUpdate()
        {
            if (State == Phase.Ready) SyncViews();
            AnimateRocket();
        }

        /// <summary>The flame flickers faster as the scroll speeds up; the rocket leans into lane changes.</summary>
        void AnimateRocket()
        {
            if (_rocket == null || !_rocket.gameObject.activeSelf) return;
            _flamePhase += Time.deltaTime * (8f + _sim.Speed * 1.4f);
            float s = 1f + 0.16f * Mathf.Sin(_flamePhase) + 0.06f * Mathf.Sin(_flamePhase * 2.7f);
            _flame.localScale = new Vector3(0.9f + 0.1f * s, s, 1f);
            _flame.localPosition = new Vector2(0f, FlameTop - FlameHalf * s);

            float lean = (RocketLanesSim.LaneX(_sim.Lane) - _sim.PlayerX) / RocketLanesSim.LaneWidth;
            _rocket.localRotation = Quaternion.Euler(0f, 0f, -lean * TiltDegrees);
        }

        void SyncViews()
        {
            _frame++;
            _rocket.localPosition = new Vector2(_sim.PlayerX, RocketLanesSim.PlayerY);

            float d = _sim.Distance;
            foreach (var (star, y, drift) in _stars)
                star.localPosition = new Vector2(star.localPosition.x, Wrap(y - d * drift));
            foreach (var (dash, y) in _dashes)
                dash.localPosition = new Vector2(dash.localPosition.x, Wrap(y - d));

            // Rows pop in as they come into view, once the run has started (the ready screen stays clear).
            float viewTop = State == Phase.Ready ? float.NegativeInfinity
                : (Cam != null ? Cam.HalfSize.y : Style.WorldHeight / 2f) + 0.8f;
            foreach (var row in _sim.Rows)
            {
                if (!_views.TryGetValue(row, out var view)) _views[row] = view = TakeView(row);
                view.Seen = _frame;
                view.Root.localPosition = new Vector2(0f, row.Y);
                if (!view.Shown && row.Y < viewTop)
                {
                    view.Shown = true;
                    view.Root.gameObject.SetActive(true);
                    Juice.Pop(view.Root);
                }
                if (view.Crystal.enabled && !row.HasCrystal) view.Crystal.enabled = false;
                if (view.Crystal.enabled)
                    view.Crystal.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f + row.Index) * 10f);
            }

            _gone.Clear();
            foreach (var pair in _views) if (pair.Value.Seen != _frame) _gone.Add(pair.Key);
            foreach (var row in _gone)
            {
                var view = _views[row];
                view.Root.gameObject.SetActive(false);
                _pool.Push(view);
                _views.Remove(row);
            }
        }

        RowView TakeView(LaneRow row)
        {
            var view = _pool.Count > 0 ? _pool.Pop() : NewView();
            for (int lane = 0; lane < RocketLanesSim.Lanes; lane++) view.Mines[lane].enabled = row.Mine(lane);
            view.Crystal.enabled = row.HasCrystal;
            if (row.CrystalLane >= 0) view.Crystal.transform.localPosition = new Vector2(RocketLanesSim.LaneX(row.CrystalLane), 0f);
            view.Shown = false;
            view.Root.gameObject.SetActive(false);
            return view;
        }

        RowView NewView()
        {
            var view = new RowView { Root = Make("Row").transform };
            for (int lane = 0; lane < RocketLanesSim.Lanes; lane++)
            {
                var mine = new GameObject("Mine").AddComponent<SpriteRenderer>();
                mine.sprite = _mineSprite;
                mine.sortingOrder = 10;
                mine.transform.SetParent(view.Root, false);
                mine.transform.localPosition = new Vector2(RocketLanesSim.LaneX(lane), 0f);
                view.Mines[lane] = mine;
            }
            view.Crystal = new GameObject("Crystal").AddComponent<SpriteRenderer>();
            view.Crystal.sprite = _crystalSprite;
            view.Crystal.sortingOrder = 9;
            view.Crystal.transform.SetParent(view.Root, false);
            return view;
        }

        static float Wrap(float y)
        {
            float span = WrapHalf * 2f;
            return Mathf.Repeat(y + WrapHalf, span) - WrapHalf;
        }
    }
}
