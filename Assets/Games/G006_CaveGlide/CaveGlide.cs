using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.CaveGlide
{
    /// <summary>
    /// Game 006. Hold to lift a yellow paper glider through a winding cave; let go to sink.
    /// The rock is safe to bump along; the red crystals growing from it are not. Every 2 units
    /// scores 1, and teal gems along the way score 5. It gets faster and narrower, to a cap.
    /// The rules live in <see cref="CaveSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(6, "cave-glide", "Cave Glide", Controls.Hold, Blurb = "Hold to rise, let go to sink")]
    public sealed class CaveGlide : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        // The rock's edge is a row of overlapping lumps, one per sample, outlined in Ink.
        const float Lump = 0.36f;
        const float RockReach = 12f;

        // Crystal art: 140 px wide, the point 6 px from the top, the base 36 px into the rock.
        const float Px = 1f / Style.PixelsPerUnit;
        static readonly float[] CrystalCanvas = { (120 + 36 + 4) * Px, (180 + 36 + 4) * Px, (240 + 36 + 4) * Px };

        const float TrailEvery = 0.035f, TrailLife = 0.45f, TrailSize = 0.2f;
        const float DeadGravity = 24f;

        CaveSim _sim;
        Transform _glider, _body;
        Sprite[] _crystalSprites;
        Sprite _gem;

        readonly Dictionary<int, Transform> _samples = new Dictionary<int, Transform>();
        readonly Dictionary<int, Transform> _crystals = new Dictionary<int, Transform>();
        readonly Dictionary<CaveGem, SpriteRenderer> _gems = new Dictionary<CaveGem, SpriteRenderer>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _gone = new List<int>();
        readonly List<CaveGem> _goneGems = new List<CaveGem>();
        readonly List<(Transform t, float worldX, float y, float age)> _trail = new List<(Transform, float, float, float)>();
        readonly Stack<Transform> _trailPool = new Stack<Transform>();
        readonly List<(Transform t, Vector2 at, float drift, float span)> _decor = new List<(Transform, Vector2, float, float)>();

        float _readyTime, _trailTimer, _deadY, _deadVy, _deadSpin;

        public CaveSim Sim => _sim;

        protected override void Build()
        {
            _sim = new CaveSim(SeedForTests ?? System.Environment.TickCount);
            _gem = PropSprite("gem");
            _crystalSprites = new[] { GameSprite("crystal_1"), GameSprite("crystal_2"), GameSprite("crystal_3") };
            var t = transform;

            // Far-off rock: Slate blobs drifting past slower than the cave, no outlines.
            var random = new System.Random(16);
            for (int i = 0; i < 12; i++)
            {
                float size = Mathf.Lerp(1.6f, 3.2f, (float)random.NextDouble());
                bool top = i % 2 == 0;
                var at = new Vector2(-8f + i * 1.6f, top ? Mathf.Lerp(4.8f, 6.8f, (float)random.NextDouble()) : Mathf.Lerp(-7.2f, -5.6f, (float)random.NextDouble()));
                var blob = Shapes.Make(t, Shapes.Circle, Palette.Slate, at, new Vector2(size, size * 0.8f), -30, "FarRock").transform;
                _decor.Add((blob, at, 0.35f, 12 * 1.6f));
            }

            _glider = Make("Glider", new Vector2(CaveSim.GliderX, CaveSim.StartY)).transform;
            var body = new GameObject("Body").AddComponent<SpriteRenderer>();
            body.sprite = GameSprite("glider");
            body.sortingOrder = 30;
            _body = body.transform;
            _body.SetParent(_glider, false);

            SyncViews(0f);
        }

        protected override void Play(float dt)
        {
            // The touch that starts the run is already a hold.
            _sim.Hold(Input.Pressed);
            int before = _sim.Score;
            var events = _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & CaveEvent.Bumped) != 0)
            {
                Juice.Squash(_body, 0.14f);
                float x = _sim.GliderWorldX;
                bool up = _sim.Y > _sim.PathAt(x);
                float edge = up ? _sim.TopAt(x) : _sim.BottomAt(x);
                Juice.Burst(transform, new Vector2(CaveSim.GliderX, edge), Palette.Tan, 5, 2.5f);
            }
            if ((events & CaveEvent.Gem) != 0)
            {
                Juice.Burst(transform, ToScreen(_sim.GemPoint), Palette.Teal, 14, 5f);
                Juice.ShakeSmall();
            }
            if ((events & CaveEvent.Crashed) != 0)
            {
                var at = ToScreen(_sim.CrashPoint);
                Juice.Burst(transform, at, Palette.Red, 14, 6f);
                Juice.Burst(transform, new Vector2(CaveSim.GliderX, _sim.Y), Palette.Orange, 10, 5f);
                Juice.ShakeBig();
                Juice.Squash(_body, 0.3f);
                _deadY = _sim.Y;
                _deadVy = 3f;
                _deadSpin = 0f;
                EndRun();
            }
        }

        Vector2 ToScreen(Vector2 world) => new Vector2(world.x - _sim.Distance, world.y);

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            float d = _sim.Distance;

            foreach (var (t, at, drift, span) in _decor)
                t.localPosition = new Vector2(Mathf.Repeat(at.x - d * drift + span / 2f, span) - span / 2f, at.y);

            SyncRock(d);
            SyncCrystals(d);
            SyncGems(d);

            // The glider: points the way it's going. Before the first touch it bobs in place;
            // after a crash it tumbles down.
            float y = _sim.Y, tilt = Mathf.Atan2(_sim.Velocity, _sim.Speed) * Mathf.Rad2Deg;
            if (State == Phase.Ready)
            {
                _readyTime += dt;
                y = CaveSim.StartY + Mathf.Sin(_readyTime * 3f) * 0.12f;
                tilt = Mathf.Cos(_readyTime * 3f) * 5f;
            }
            else if (_sim.Crashed)
            {
                _deadVy -= DeadGravity * dt;
                _deadY = Mathf.Max(_sim.BottomAt(_sim.GliderWorldX) + CaveSim.Radius * 0.5f, _deadY + _deadVy * dt);
                _deadSpin -= 400f * dt;
                y = _deadY;
                tilt = _deadSpin;
            }
            _glider.localPosition = new Vector2(CaveSim.GliderX, y);
            _glider.localRotation = Quaternion.Euler(0f, 0f, tilt);

            // A trail of puffs behind it, left hanging in the cave as it flies on.
            if (State == Phase.Playing && !_sim.Crashed)
            {
                _trailTimer -= dt;
                if (_trailTimer <= 0f)
                {
                    _trailTimer = TrailEvery;
                    var tail = _glider.TransformPoint(new Vector3(-0.45f, 0f, 0f)) - transform.position;
                    var puff = _trailPool.Count > 0 ? _trailPool.Pop() : Shapes.Make(transform, Shapes.Circle, Palette.Cloud, Vector2.zero, Vector2.one, 25, "Puff").transform;
                    puff.gameObject.SetActive(true);
                    _trail.Add((puff, tail.x + d, tail.y, 0f));
                }
            }
            for (int i = _trail.Count - 1; i >= 0; i--)
            {
                var (p, wx, py, age) = _trail[i];
                age += dt;
                if (age >= TrailLife)
                {
                    p.gameObject.SetActive(false);
                    _trailPool.Push(p);
                    _trail.RemoveAt(i);
                    continue;
                }
                _trail[i] = (p, wx, py, age);
                p.localPosition = new Vector2(wx - d, py);
                p.localScale = Vector3.one * TrailSize * (1f - age / TrailLife);
            }
        }

        void SyncRock(float d)
        {
            _seen.Clear();
            foreach (var s in _sim.Samples)
            {
                _seen.Add(s.Index);
                if (!_samples.TryGetValue(s.Index, out var view)) _samples[s.Index] = view = MakeSample(s);
                view.localPosition = new Vector2(s.X - d, 0f);
            }
            _gone.Clear();
            foreach (var id in _samples.Keys) if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                Destroy(_samples[id].gameObject);
                _samples.Remove(id);
            }
        }

        Transform MakeSample(CaveSample s)
        {
            var root = Make($"Rock {s.Index}").transform;
            float w = CaveSim.SampleStep + 0.02f;
            float o = Style.Outline;
            // Each lump: an Ink rim, the shade colour, and the rock colour a little smaller and
            // up-left, so the shade shows down-right. Lumps sit back in the rock so their edge is
            // where the glider bumps. The rock column is drawn over the half of the lump inside
            // the rock, so only the bulge into the cave shows. A stone or two for texture.
            var random = new System.Random(s.Index);
            foreach (var (edge, sign) in new[] { (s.Top, 1f), (s.Bottom, -1f) })
            {
                float y = edge + sign * (Lump - 0.04f);
                Shapes.Make(root, Shapes.Circle, Palette.Ink, new Vector2(0f, y), Vector2.one * (Lump + o) * 2f, 0, "Outline");
                Shapes.Make(root, Shapes.Circle, Palette.BrownDark, new Vector2(0f, y), Vector2.one * Lump * 2f, 2, "Shade");
                Shapes.Make(root, Shapes.Circle, Palette.Brown, new Vector2(-0.035f, y + 0.035f), Vector2.one * (Lump - 0.06f) * 2f, 3, "Lump");
                Shapes.Make(root, Shapes.Square, Palette.Brown, new Vector2(0f, y + sign * RockReach / 2f), new Vector2(w, RockReach), 4, "Rock");
                if (random.NextDouble() < 0.45)
                {
                    float size = Mathf.Lerp(0.18f, 0.34f, (float)random.NextDouble());
                    float into = Mathf.Lerp(0.9f, 5f, (float)random.NextDouble());
                    Shapes.Make(root, Shapes.Circle, Palette.BrownDark, new Vector2((float)random.NextDouble() * 0.3f - 0.15f, edge + sign * into), new Vector2(size * 1.4f, size), 5, "Stone");
                }
            }
            return root;
        }

        void SyncCrystals(float d)
        {
            _seen.Clear();
            foreach (var c in _sim.Crystals)
            {
                _seen.Add(c.Index);
                if (!_crystals.TryGetValue(c.Index, out var view)) _crystals[c.Index] = view = MakeCrystal(c);
                view.localPosition = new Vector2(c.X - d, view.localPosition.y);
            }
            _gone.Clear();
            foreach (var id in _crystals.Keys) if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                Destroy(_crystals[id].gameObject);
                _crystals.Remove(id);
            }
        }

        Transform MakeCrystal(Crystal c)
        {
            int k = System.Array.IndexOf(CaveSim.Depths, c.Depth);
            if (k < 0) k = 0;
            float dir = c.Ceiling ? -1f : 1f;
            // The drawn point sits 6 px short of the crystal's full depth; the hit shape is a little inside it.
            float tip = c.BaseY + dir * (c.Depth - 6f * Px);
            float centre = tip - dir * (CrystalCanvas[k] / 2f - 6f * Px);
            var sr = MakeSprite($"Crystal {c.Index}", _crystalSprites[k], new Vector2(c.X - _sim.Distance, centre), -1);
            if (c.Ceiling) sr.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            return sr.transform;
        }

        void SyncGems(float d)
        {
            foreach (var g in _sim.Gems)
            {
                if (!_gems.TryGetValue(g, out var sr))
                {
                    _gems[g] = sr = MakeSprite("Gem", _gem, new Vector2(g.X - d, g.Y), 10);
                    sr.transform.localScale = Vector3.one * 0.75f;
                }
                sr.enabled = !g.Taken;
                sr.transform.localPosition = new Vector2(g.X - d, g.Y + Mathf.Sin(Time.time * 3f + g.X) * 0.08f);
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2.5f + g.X) * 12f);
            }
            _goneGems.Clear();
            foreach (var g in _gems.Keys) if (!Contains(_sim.Gems, g)) _goneGems.Add(g);
            foreach (var g in _goneGems)
            {
                Destroy(_gems[g].gameObject);
                _gems.Remove(g);
            }
        }

        static bool Contains(IReadOnlyList<CaveGem> list, CaveGem g)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == g) return true;
            return false;
        }
    }
}
