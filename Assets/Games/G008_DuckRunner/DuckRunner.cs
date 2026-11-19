using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.DuckRunner
{
    /// <summary>
    /// Game 008. A duck runs along a riverbank at night. Tap to jump over foxes, cattails, logs
    /// and snapping turtles; swipe down to duck under hawks and hanging branches (holding the
    /// finger down keeps ducking). Each obstacle passed scores 1, and a teal decoy duck floating
    /// over a gap scores 5. The run speeds up as it goes, to a cap.
    /// The rules live in <see cref="DuckSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(8, "duck-runner", "Duck Runner", Controls.Tap, Blurb = "Tap to jump, swipe down to duck")]
    public sealed class DuckRunner : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        /// <summary>Where the ground is, in world y. The sim's heights are above it.</summary>
        public const float GroundY = -3f;

        const float Px = 1f / Style.PixelsPerUnit;

        // The duck's sprites sit so their bodies are over the sim's box (see the SVGs' sizes).
        static readonly Vector2 StandOffset = new Vector2(12f * Px, 0.16f + 56f * Px);
        static readonly Vector2 LowOffset = new Vector2(16f * Px, 0.06f + 40f * Px);
        static readonly Vector2 FootBack = new Vector2(-0.14f, 0.1f), FootFront = new Vector2(0.14f, 0.1f);
        const float StrideLength = 0.75f;
        const float DeadGravity = 30f;

        /// <summary>The duck tilts and tumbles about the middle of its body, not its feet.</summary>
        static readonly Vector2 BodyCentre = new Vector2(0.1f, 0.55f);

        // Background: hills and reeds in Slate, drifting slower than the ground; pebbles in the dirt.
        const float HillSpan = 24f, ReedSpan = 18f, PebbleSpan = 18f;

        /// <summary>One kind's art: its sprite, and where in the canvas the ground and the box's middle are (px).</summary>
        readonly struct Look
        {
            public readonly string Sprite;
            public readonly Vector2 Canvas;
            public readonly float CentreX, BaseY;

            public Look(string sprite, float w, float h, float centreX, float baseY)
            {
                Sprite = sprite;
                Canvas = new Vector2(w, h);
                CentreX = centreX;
                BaseY = baseY;
            }

            /// <summary>The sprite's offset from the box's bottom middle, so that canvas point (CentreX, BaseY) lands there.</summary>
            public Vector2 Offset => new Vector2((Canvas.x / 2f - CentreX) * Px, (BaseY - Canvas.y / 2f) * Px);
        }

        static Look LookOf(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.Fox: return new Look("fox", 184, 124, 88, 108);
                case ObstacleKind.Cattails: return new Look("cattails", 120, 208, 60, 196);
                case ObstacleKind.Log: return new Look("log", 248, 112, 120, 100);
                case ObstacleKind.Turtle: return new Look("turtle", 168, 112, 86, 104);
                // High ones: BaseY is the art's lowest point, which sits just above the box's bottom.
                case ObstacleKind.Hawk: return new Look("hawk", 176, 104, 88, 92);
                default: return new Look("branch", 176, 128, 88, 128);
            }
        }

        DuckSim _sim;
        Transform _duck, _feetBack, _feetFront;
        SpriteRenderer _stand, _low;
        Sprite _decoySprite;
        readonly Dictionary<ObstacleKind, Sprite> _sprites = new Dictionary<ObstacleKind, Sprite>();
        Sprite _turtleHead, _hawkWing;

        readonly List<(Transform t, float x, float drift, float span)> _scenery = new List<(Transform, float, float, float)>();

        sealed class ObstacleView
        {
            public Transform Root, Body, Part;
            public bool Seen;
        }

        readonly Dictionary<DuckObstacle, ObstacleView> _views = new Dictionary<DuckObstacle, ObstacleView>();
        readonly Dictionary<DuckDecoy, SpriteRenderer> _decoyViews = new Dictionary<DuckDecoy, SpriteRenderer>();
        readonly List<DuckObstacle> _gone = new List<DuckObstacle>();
        readonly List<DuckDecoy> _goneDecoys = new List<DuckDecoy>();

        float _stride, _readyTime, _deadY, _deadVelocity, _deadSpin, _clock;
        bool _wasDucking;

        public DuckSim Sim => _sim;

        protected override void Build()
        {
            _sim = new DuckSim(SeedForTests ?? System.Environment.TickCount);
            foreach (var kind in DuckSim.Kinds) _sprites[kind] = GameSprite(LookOf(kind).Sprite);
            _turtleHead = GameSprite("turtle_head");
            _hawkWing = GameSprite("hawk_wing");
            _decoySprite = GameSprite("decoy");

            BuildScenery();

            _duck = Make("Duck", new Vector2(DuckSim.PlayerX, GroundY)).transform;
            _feetBack = MakeChild(_duck, "FootBack", GameSprite("foot"), FootBack, 19).transform;
            _feetFront = MakeChild(_duck, "FootFront", GameSprite("foot"), FootFront, 21).transform;
            _stand = MakeChild(_duck, "Stand", GameSprite("duck"), StandOffset, 20);
            _low = MakeChild(_duck, "Low", GameSprite("duck_low"), LowOffset, 20);
            _low.enabled = false;

            SyncViews(0f);
        }

        void BuildScenery()
        {
            var t = transform;
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(2.4f, 4.6f), new Vector2(2f, 2f), -20, "Moon");

            var random = new System.Random(12);
            for (int i = 0; i < 6; i++)
            {
                float size = Mathf.Lerp(4f, 6.5f, (float)random.NextDouble());
                float x = -HillSpan / 2f + i * HillSpan / 6f;
                var hill = Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(x, GroundY - 0.6f), new Vector2(size, size * 0.8f), -19, "Hill");
                _scenery.Add((hill.transform, x, 0.15f, HillSpan));
            }

            // Reeds: tall Night stems with Night heads, in front of the hills.
            for (int i = 0; i < 12; i++)
            {
                float x = -ReedSpan / 2f + i * ReedSpan / 12f + (float)random.NextDouble() * 0.6f;
                float h = Mathf.Lerp(0.9f, 1.8f, (float)random.NextDouble());
                var reed = new GameObject("Reed").transform;
                reed.SetParent(t, false);
                Shapes.Make(reed, Shapes.Square, Palette.Night, new Vector2(0f, h / 2f), new Vector2(0.14f, h), -17, "Stem");
                Shapes.Make(reed, Shapes.Circle, Palette.Night, new Vector2(0f, h), new Vector2(0.26f, 0.6f), -17, "Head");
                reed.localPosition = new Vector2(x, GroundY);
                _scenery.Add((reed, x, 0.45f, ReedSpan));
            }

            // The ground: an outlined grass strip over plain dirt, with pebbles that scroll at running speed.
            Shapes.Box(t, new Vector2(0f, GroundY - 0.2f), new Vector2(30f, 0.5f), Palette.Green, Palette.GreenDark, 2, "Grass");
            Shapes.Make(t, Shapes.Square, Palette.Brown, new Vector2(0f, GroundY - 0.4f - 7f), new Vector2(30f, 14f), 1, "Dirt");
            for (int i = 0; i < 14; i++)
            {
                float x = -PebbleSpan / 2f + i * PebbleSpan / 14f;
                float y = GroundY - Mathf.Lerp(0.8f, 3.2f, (float)random.NextDouble());
                float size = Mathf.Lerp(0.16f, 0.32f, (float)random.NextDouble());
                var pebble = Shapes.Make(t, Shapes.Circle, Palette.BrownDark, new Vector2(x, y), new Vector2(size * 1.4f, size), 2, "Pebble");
                _scenery.Add((pebble.transform, x, 1f, PebbleSpan));
            }
        }

        protected override void Play(float dt)
        {
            var events = DuckEvent.None;
            if (Input.Down) events |= _sim.Press();
            if (Input.Swipe.y < 0f) events |= _sim.SwipeDown();
            if (Input.Up || !Input.Pressed) _sim.Release();

            int before = _sim.Score;
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & DuckEvent.Jumped) != 0) Juice.Squash(_stand.transform, -0.15f);
            if ((events & DuckEvent.Landed) != 0) Juice.Squash(_sim.Ducking ? _low.transform : _stand.transform, 0.2f);
            if ((events & DuckEvent.Decoy) != 0)
            {
                Juice.Burst(transform, World(_sim.DecoyPoint), Palette.Teal, 12, 5f);
                Juice.ShakeSmall();
            }
            if ((events & DuckEvent.Crashed) != 0)
            {
                Juice.Burst(transform, World(_sim.CrashPoint), Palette.Red, 16, 6f);
                Juice.ShakeBig();
                _deadY = _sim.Y;
                _deadVelocity = 7f;
                EndRun();
            }
        }

        static Vector2 World(Vector2 simPoint) => new Vector2(simPoint.x, GroundY + simPoint.y);

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            _clock += dt;
            SyncDuck(dt);

            float d = _sim.Distance;
            foreach (var (t, x, drift, span) in _scenery)
                t.localPosition = new Vector2(Mathf.Repeat(x - d * drift + span / 2f, span) - span / 2f, t.localPosition.y);

            SyncObstacles();
            SyncDecoys();
        }

        void SyncDuck(float dt)
        {
            float y = _sim.Y, tilt = 0f;
            bool low = _sim.Ducking;

            if (State == Phase.Ready)
            {
                _readyTime += dt;
                tilt = Mathf.Sin(_readyTime * 3f) * 4f;
            }
            else if (_sim.Crashed)
            {
                // Knocked over: a little hop, then onto its back.
                _deadVelocity -= DeadGravity * dt;
                _deadY = Mathf.Max(0f, _deadY + _deadVelocity * dt);
                _deadSpin = Mathf.MoveTowards(_deadSpin, 160f, 600f * dt);
                y = _deadY;
                tilt = _deadSpin;
                low = false;
            }
            else if (!_sim.OnGround) tilt = Mathf.Clamp(_sim.VelocityY * 1.2f, -20f, 15f);

            var turn = Quaternion.Euler(0f, 0f, tilt);
            _duck.localPosition = new Vector2(DuckSim.PlayerX, GroundY + y) + BodyCentre - (Vector2)(turn * BodyCentre);
            _duck.localRotation = turn;

            if (low != _wasDucking)
            {
                _wasDucking = low;
                _stand.enabled = !low;
                _low.enabled = low;
                if (low) Juice.Squash(_low.transform, 0.15f);
            }

            // Feet: running on the ground, tucked back in the air, hidden while ducking.
            _feetBack.gameObject.SetActive(!low);
            _feetFront.gameObject.SetActive(!low);
            if (low) return;
            if (State == Phase.Playing && _sim.OnGround && !_sim.Crashed)
            {
                _stride += _sim.Speed * dt / StrideLength * Mathf.PI;
                PlaceFoot(_feetBack, FootBack, _stride);
                PlaceFoot(_feetFront, FootFront, _stride + Mathf.PI);
            }
            else if (!_sim.OnGround && !_sim.Crashed)
            {
                _feetBack.localPosition = FootBack + new Vector2(-0.08f, 0.04f);
                _feetFront.localPosition = FootFront + new Vector2(-0.08f, 0.04f);
                _feetBack.localRotation = _feetFront.localRotation = Quaternion.Euler(0f, 0f, 30f);
            }
            else
            {
                _feetBack.localPosition = FootBack;
                _feetFront.localPosition = FootFront;
                _feetBack.localRotation = _feetFront.localRotation = Quaternion.identity;
            }
        }

        static void PlaceFoot(Transform foot, Vector2 home, float phase)
        {
            foot.localPosition = home + new Vector2(Mathf.Sin(phase) * 0.14f, Mathf.Max(0f, Mathf.Cos(phase)) * 0.1f);
            foot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(phase) * 15f);
        }

        void SyncObstacles()
        {
            foreach (var view in _views.Values) view.Seen = false;
            foreach (var o in _sim.Obstacles)
            {
                if (!_views.TryGetValue(o, out var view)) _views[o] = view = MakeObstacle(o);
                view.Seen = true;
                var spec = o.Spec;
                view.Root.localPosition = new Vector2(o.X + spec.Length / 2f, GroundY + spec.Bottom);
                Animate(o, view);
            }

            _gone.Clear();
            foreach (var pair in _views) if (!pair.Value.Seen) _gone.Add(pair.Key);
            foreach (var o in _gone)
            {
                Destroy(_views[o].Root.gameObject);
                _views.Remove(o);
            }
        }

        void Animate(DuckObstacle o, ObstacleView view)
        {
            if (_sim.Crashed) return;
            float t = _clock + o.Index * 0.37f;
            var look = LookOf(o.Kind);
            switch (o.Kind)
            {
                case ObstacleKind.Fox:
                    // Bounding along.
                    view.Body.localPosition = look.Offset + new Vector2(0f, Mathf.Abs(Mathf.Sin(t * 9f)) * 0.08f);
                    break;
                case ObstacleKind.Cattails:
                    view.Root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 2f) * 3f);
                    break;
                case ObstacleKind.Turtle:
                    // Snaps forward now and then.
                    float snap = Mathf.Repeat(t, 1.3f);
                    float reach = snap < 0.12f ? snap / 0.12f : Mathf.Max(0f, 1f - (snap - 0.12f) / 0.25f);
                    view.Part.localPosition = new Vector2(-0.55f - 0.2f * reach, 0.38f);
                    break;
                case ObstacleKind.Hawk:
                    view.Part.localRotation = Quaternion.Euler(0f, 0f, -15f + Mathf.Sin(t * 12f) * 30f);
                    view.Body.localPosition = look.Offset + new Vector2(0f, Mathf.Sin(t * 12f) * 0.03f);
                    break;
            }
        }

        ObstacleView MakeObstacle(DuckObstacle o)
        {
            var look = LookOf(o.Kind);
            var root = Make($"{o.Kind} {o.Index}").transform;
            var body = MakeChild(root, "Body", _sprites[o.Kind], look.Offset, 10).transform;
            var view = new ObstacleView { Root = root, Body = body };

            switch (o.Kind)
            {
                case ObstacleKind.Turtle:
                    view.Part = MakeChild(root, "Head", _turtleHead, new Vector2(-0.55f, 0.38f), 9).transform;
                    break;
                case ObstacleKind.Hawk:
                    // The wing beats from a pivot on the hawk's back.
                    var pivot = new GameObject("WingPivot").transform;
                    pivot.SetParent(body, false);
                    pivot.localPosition = new Vector2(14f * Px, 4f * Px);
                    MakeChild(pivot, "Wing", _hawkWing, new Vector2(0.06f, 0.2f), 11);
                    view.Part = pivot;
                    break;
                case ObstacleKind.Branch:
                    // The vine runs from behind the leaves up off the top of the screen.
                    Shapes.Box(root, new Vector2(0f, look.Offset.y + 10f), new Vector2(40f * Px, 20f), Palette.Brown, Palette.BrownDark, 6, "Vine");
                    break;
            }
            return view;
        }

        void SyncDecoys()
        {
            foreach (var d in _sim.DecoyList)
            {
                if (!_decoyViews.TryGetValue(d, out var sr))
                {
                    sr = MakeChild(transform, "Decoy", _decoySprite, Vector2.zero, 15);
                    _decoyViews[d] = sr;
                }
                if (d.Taken && sr.enabled) sr.enabled = false;
                float bob = Mathf.Sin(_clock * 3f + d.X) * 0.08f;
                sr.transform.localPosition = new Vector2(d.X - 4f * Px, GroundY + DuckSim.DecoyHeight + 4f * Px + bob);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_clock * 2f) * 6f);
            }

            _goneDecoys.Clear();
            foreach (var pair in _decoyViews)
            {
                bool still = false;
                foreach (var d in _sim.DecoyList) if (d == pair.Key) { still = true; break; }
                if (!still) _goneDecoys.Add(pair.Key);
            }
            foreach (var d in _goneDecoys)
            {
                Destroy(_decoyViews[d].gameObject);
                _decoyViews.Remove(d);
            }
        }

        static SpriteRenderer MakeChild(Transform parent, string name, Sprite sprite, Vector2 position, int order)
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
