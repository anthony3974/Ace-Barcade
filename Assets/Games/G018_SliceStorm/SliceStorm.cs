using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AceBarcade.Games.SliceStorm
{
    /// <summary>
    /// Game 018. Veggies fly up from behind the crates in waves; swipe through them to cut them
    /// in two. Each cut scores 1, and three or more in one stroke score as many again. A veggie
    /// that falls back uncut costs one of three lives; cutting a red thornball ends the run.
    /// The rules live in <see cref="SliceSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(18, "slice-storm", "Slice Storm", Controls.Swipe, Blurb = "Swipe to slice, but not the red ones")]
    public sealed class SliceStorm : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        static readonly string[] Names = { "carrot", "eggplant", "turnip", "cabbage" };
        static readonly Color[] BurstColors = { Palette.Orange, Palette.Purple, Palette.Pink, Palette.Lime };

        const float CrateTop = -6.9f;
        const float HalfGravity = SliceSim.Gravity;
        const float HalfSplit = 2.6f;
        const float TrailLife = 0.11f;
        const int TrailSegments = 24;
        const float TrailThin = 0.05f, TrailThick = 0.22f;

        sealed class Half
        {
            public Transform T;
            public Vector2 Vel;
            public float Spin;
        }

        SliceSim _sim;
        Sprite[] _whole, _left, _right;
        Sprite _thorn;
        readonly Dictionary<int, SpriteRenderer> _views = new Dictionary<int, SpriteRenderer>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _gone = new List<int>();
        readonly List<Half> _halves = new List<Half>();
        readonly List<(Vector2 p, float t)> _trail = new List<(Vector2, float)>();
        readonly List<SpriteRenderer> _segments = new List<SpriteRenderer>();
        SpriteRenderer[] _lives;
        TextMeshPro _message;
        float _messageFor;
        Vector2 _last;
        bool _startTouch, _stroke;
        float _now;

        public SliceSim Sim => _sim;

        protected override void Build()
        {
            _sim = new SliceSim(SeedForTests ?? System.Environment.TickCount);
            _whole = new Sprite[Names.Length];
            _left = new Sprite[Names.Length];
            _right = new Sprite[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                _whole[i] = GameSprite(Names[i]);
                _left[i] = GameSprite($"{Names[i]}_left");
                _right[i] = GameSprite($"{Names[i]}_right");
            }
            _thorn = GameSprite("thorn");
            var t = transform;

            // Storm clouds: clusters of big Slate circles, no outlines.
            BuildCloud(new Vector2(-2.6f, 4.6f), 1f);
            BuildCloud(new Vector2(2.8f, 2.2f), 0.8f);
            BuildCloud(new Vector2(-2.2f, -2.2f), 0.7f);

            // The crates along the bottom that everything is thrown from behind.
            const float crateH = 5f;
            for (int i = 0; i < 3; i++)
            {
                var crate = Shapes.Box(t, new Vector2(-3f + i * 3f, CrateTop - crateH / 2f), new Vector2(3.1f, crateH), Palette.Brown, Palette.BrownDark, 50 + i * 4, "Crate");
                Shapes.Make(crate, Shapes.Square, Palette.BrownDark, new Vector2(0f, crateH / 2f - 0.55f), new Vector2(2.9f, Style.Detail * 2f), 53 + i * 4, "Slat");
            }

            for (int i = 0; i < TrailSegments; i++)
            {
                var seg = Shapes.Make(t, Shapes.Square, Palette.Paper, Vector2.zero, Vector2.one, 40, "Trail");
                seg.enabled = false;
                _segments.Add(seg);
            }

            _lives = new SpriteRenderer[SliceSim.StartLives];
            for (int i = 0; i < _lives.Length; i++)
            {
                _lives[i] = MakeSprite("Life", _whole[0], new Vector2(-3.65f + i * 0.62f, 6.62f), 60);
                _lives[i].transform.localScale = Vector3.one * 0.55f;
                _lives[i].transform.localRotation = Quaternion.Euler(0f, 0f, -20f);
            }

            _message = new GameObject("Message").AddComponent<TextMeshPro>();
            _message.transform.SetParent(t, false);
            _message.font = UiKit.Font;
            _message.fontSize = 8f;
            _message.alignment = TextAlignmentOptions.Center;
            _message.textWrappingMode = TextWrappingModes.NoWrap;
            _message.rectTransform.sizeDelta = new Vector2(8f, 2f);
            _message.sortingOrder = 70;
            _message.gameObject.SetActive(false);
        }

        void BuildCloud(Vector2 at, float size)
        {
            var t = transform;
            Shapes.Make(t, Shapes.Circle, Palette.Slate, at, Vector2.one * 2.2f * size, -20, "Cloud");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, at + new Vector2(-1.3f, -0.4f) * size, Vector2.one * 1.6f * size, -20, "Cloud");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, at + new Vector2(1.4f, -0.3f) * size, Vector2.one * 1.7f * size, -20, "Cloud");
            Shapes.Make(t, Shapes.Square, Palette.Slate, at + new Vector2(0f, -0.65f) * size, new Vector2(3f, 0.9f) * size, -20, "Cloud");
        }

        protected override void OnBegin() => _startTouch = true;

        protected override void Play(float dt)
        {
            _now += dt;
            var events = _sim.Step(dt);
            int before = _sim.Score;

            // The touch that started the run isn't a stroke.
            if (_startTouch)
            {
                if (Input.Up || !Input.Pressed) _startTouch = false;
            }
            else
            {
                var p = Input.Position;
                if (Input.Down)
                {
                    _stroke = true;
                    _last = p;
                    _trail.Clear();
                    _trail.Add((p, _now));
                }
                else if (_stroke && (Input.Pressed || Input.Up))
                {
                    events |= _sim.Slice(_last, p, dt);
                    if ((events & SliceEvent.Sliced) != 0) Cuts();
                    _last = p;
                    _trail.Add((p, _now));
                }
                if (_stroke && Input.Up)
                {
                    _stroke = false;
                    events |= _sim.EndStroke();
                }
            }
            AddScore(_sim.Score - before);

            if ((events & SliceEvent.Combo) != 0)
            {
                Say($"COMBO x{_sim.LastCombo}  +{_sim.LastCombo}", Palette.Yellow);
                Juice.ShakeSmall();
            }
            if ((events & SliceEvent.Dropped) != 0)
            {
                int lost = _sim.Lives;
                if (lost >= 0 && lost < _lives.Length)
                {
                    Juice.Burst(transform, _lives[lost].transform.localPosition, Palette.Red, 6, 2f);
                    _lives[lost].enabled = false;
                }
                Juice.Burst(transform, new Vector2(Mathf.Clamp(_sim.LastDropX, -4f, 4f), CrateTop), Palette.Cloud, 6, 3f);
                Say("MISS", Palette.Cloud, new Vector2(Mathf.Clamp(_sim.LastDropX, -2.8f, 2.8f), CrateTop + 0.8f));
                Juice.ShakeSmall();
            }
            if ((events & SliceEvent.Thorned) != 0)
            {
                Juice.Burst(transform, _sim.ThornAt, Palette.Red, 20, 8f);
                Juice.Burst(transform, _sim.ThornAt, Palette.RedDark, 10, 5f);
            }
            if ((events & SliceEvent.Over) != 0)
            {
                Juice.ShakeBig();
                EndRun();
            }
        }

        void Cuts()
        {
            foreach (var cut in _sim.LastCut)
            {
                var th = cut.Thing;
                if (_views.TryGetValue(th.Id, out var view))
                {
                    Destroy(view.gameObject);
                    _views.Remove(th.Id);
                }
                // The halves' cut edge is upright in the art: turn it to run along the stroke.
                float angle = Mathf.Atan2(cut.Dir.y, cut.Dir.x) * Mathf.Rad2Deg - 90f;
                var rot = Quaternion.Euler(0f, 0f, angle);
                var leftSide = (Vector2)(rot * Vector3.left);
                AddHalf(_left[th.Kind], th, rot, th.Vel * 0.5f + leftSide * HalfSplit, 1f);
                AddHalf(_right[th.Kind], th, rot, th.Vel * 0.5f - leftSide * HalfSplit, -1f);
                Juice.Burst(transform, th.Pos, BurstColors[th.Kind], 8, 4.5f);
            }
            if (_sim.LastCut.Count >= 2) Juice.ShakeSmall();
        }

        void AddHalf(Sprite sprite, Thing th, Quaternion rot, Vector2 vel, float spinSign)
        {
            var sr = MakeSprite("Half", sprite, th.Pos, 12);
            sr.transform.localRotation = rot;
            _halves.Add(new Half { T = sr.transform, Vel = vel, Spin = spinSign * Random.Range(120f, 260f) });
        }

        void Say(string text, Color color, Vector2? at = null)
        {
            _message.text = text;
            _message.color = color;
            _message.transform.localPosition = at ?? new Vector2(0f, 1.2f);
            _message.gameObject.SetActive(true);
            Juice.Pop(_message.transform);
            _messageFor = 0.9f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;

            _seen.Clear();
            foreach (var th in _sim.Things)
            {
                _seen.Add(th.Id);
                if (!_views.TryGetValue(th.Id, out var view))
                {
                    view = MakeSprite(th.Thorn ? "Thorn" : "Veggie", th.Thorn ? _thorn : _whole[th.Kind], th.Pos, th.Thorn ? 11 : 10);
                    _views[th.Id] = view;
                }
                view.transform.localPosition = th.Pos;
                view.transform.localRotation = Quaternion.Euler(0f, 0f, th.Angle);
            }
            _gone.Clear();
            foreach (var id in _views.Keys) if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                Destroy(_views[id].gameObject);
                _views.Remove(id);
            }

            for (int i = _halves.Count - 1; i >= 0; i--)
            {
                var h = _halves[i];
                h.Vel.y -= HalfGravity * dt;
                h.T.localPosition += (Vector3)(h.Vel * dt);
                h.T.localRotation *= Quaternion.Euler(0f, 0f, h.Spin * dt);
                if (h.T.localPosition.y < -10f)
                {
                    Destroy(h.T.gameObject);
                    _halves.RemoveAt(i);
                }
            }

            DrawTrail();

            if (_messageFor > 0f && (_messageFor -= dt) <= 0f) _message.gameObject.SetActive(false);
        }

        /// <summary>The finger's trail: the last moment of its path, thick at the finger and thin behind.</summary>
        void DrawTrail()
        {
            while (_trail.Count > 0 && _now - _trail[0].t > TrailLife) _trail.RemoveAt(0);
            if (State != Phase.Playing) _trail.Clear();
            int used = 0;
            for (int i = 1; i < _trail.Count && used < _segments.Count; i++)
            {
                var a = _trail[i - 1].p;
                var b = _trail[i].p;
                var d = b - a;
                float len = d.magnitude;
                if (len < 0.01f) continue;
                float age = Mathf.Clamp01((_now - _trail[i].t) / TrailLife);
                float k = (float)i / (_trail.Count - 1);
                float thick = Mathf.Lerp(TrailThin, TrailThick, k * (1f - age));
                var seg = _segments[used++];
                seg.enabled = true;
                seg.transform.localPosition = (a + b) / 2f;
                seg.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                seg.transform.localScale = new Vector3(len + thick, thick, 1f);
            }
            for (int i = used; i < _segments.Count; i++) _segments[i].enabled = false;
        }
    }
}
