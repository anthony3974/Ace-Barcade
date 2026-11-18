using TMPro;
using UnityEngine;

namespace AceBarcade.Games.RingStop
{
    /// <summary>
    /// Game 018. A yellow spark runs round a ring about a sleepy blue planet; tap while it's on the
    /// green arc to send it back the other way, faster, with a new arc ahead. Taps off the arc, or
    /// letting it run past, cost one of three lives; a tap on a red thorn arc ends the run.
    /// The rules live in <see cref="RingSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(18, "ring-stop", "Ring Stop", Controls.Tap, Blurb = "Tap when the spark is on the green")]
    public sealed class RingStop : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        public static readonly Vector2 Center = new Vector2(0f, -0.6f);
        public const float Radius = 3f;
        const float TrackThick = 0.5f;
        const float ArcThick = 0.64f;
        const float SparkScale = 0.9f;
        const float GemScale = 0.6f;
        const int TrailDots = 4;

        // Sorting: the track, then arcs on it, then the spark on top.
        const int TrackOrder = 0, ThornOrder = 3, ArcOrder = 4, GemOrder = 9, TrailOrder = 10, SparkOrder = 12;

        RingSim _sim;
        Transform _spark, _planet, _target, _red, _gem;
        Transform[] _pupils;
        Vector2[] _eyeAt;
        SpriteRenderer[] _lives, _trail;
        Sprite _sparkSprite, _thorn, _gemSprite;
        SpriteRenderer _sparkRenderer;
        TextMeshPro _flash;
        float _flashFor;
        int _shownTarget = -1;
        bool _starting;

        public RingSim Sim => _sim;

        /// <summary>A point on the ring at <paramref name="angle"/> degrees, <paramref name="offset"/> out from its middle line.</summary>
        public static Vector2 OnRing(float angle, float offset = 0f)
        {
            float a = angle * Mathf.Deg2Rad;
            return Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (Radius + offset);
        }

        protected override void Build()
        {
            _sim = new RingSim(SeedForTests ?? System.Environment.TickCount);
            _sparkSprite = GameSprite("spark");
            _thorn = GameSprite("thorn");
            _gemSprite = PropSprite("gem");
            var t = transform;

            // A night sky with a few Slate stars, none on the ring.
            var random = new System.Random(18);
            for (int i = 0; i < 22; i++)
            {
                float size = Mathf.Lerp(0.14f, 0.26f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-4.6f, 4.6f, (float)random.NextDouble()), Mathf.Lerp(-9f, 6.2f, (float)random.NextDouble()));
                float d = (at - Center).magnitude;
                if (d > Radius - 0.6f && d < Radius + 0.9f) continue;
                if (d < 1.6f) continue;
                Shapes.Make(t, Shapes.Circle, Palette.Slate, at, new Vector2(size, size), -20, "Star");
            }

            // The track: a Purple tube all the way round.
            var track = Make("Track", Center).transform;
            AddTube(track, Vector2.zero, 0f, 360f, TrackThick, Palette.Purple, Palette.PurpleDark, TrackOrder, 2.5f);

            // The planet in the middle, whose eyes follow the spark.
            _planet = MakeSprite("Planet", GameSprite("planet"), Center, 2).transform;
            _eyeAt = new[] { new Vector2(-0.34f, 0.1f), new Vector2(0.34f, 0.1f) };
            _pupils = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                var eye = new Vector2(0.4f, 0.52f);
                Shapes.Make(_planet, Shapes.Circle, Palette.Ink, _eyeAt[i], eye + Vector2.one * Style.Detail * 2f, 3, "Eye Ink");
                Shapes.Make(_planet, Shapes.Circle, Palette.Paper, _eyeAt[i], eye, 4, "Eye");
                _pupils[i] = Shapes.Make(_planet, Shapes.Circle, Palette.Ink, _eyeAt[i], new Vector2(0.22f, 0.22f), 5, "Pupil").transform;
            }

            _trail = new SpriteRenderer[TrailDots];
            for (int i = 0; i < TrailDots; i++)
            {
                float size = Mathf.Lerp(0.32f, 0.14f, (float)i / (TrailDots - 1));
                _trail[i] = Shapes.Make(t, Shapes.Circle, Palette.Orange, OnRing(RingSim.StartAngle), new Vector2(size, size), TrailOrder, "Trail");
                _trail[i].enabled = false;
            }

            _sparkRenderer = MakeSprite("Spark", _sparkSprite, OnRing(RingSim.StartAngle), SparkOrder);
            _spark = _sparkRenderer.transform;
            _spark.localScale = Vector3.one * SparkScale;

            _lives = new SpriteRenderer[RingSim.StartLives];
            for (int i = 0; i < _lives.Length; i++)
            {
                _lives[i] = MakeSprite("Life", _sparkSprite, new Vector2(-3.75f + i * 0.5f, 6.72f), 40);
                _lives[i].transform.localScale = Vector3.one * 0.36f;
            }

            _flash = new GameObject("Flash").AddComponent<TextMeshPro>();
            _flash.transform.SetParent(t, false);
            _flash.font = UiKit.Font;
            _flash.fontSize = 7f;
            _flash.color = Palette.Yellow;
            _flash.alignment = TextAlignmentOptions.Center;
            _flash.textWrappingMode = TextWrappingModes.NoWrap;
            _flash.rectTransform.sizeDelta = new Vector2(8f, 2f);
            _flash.sortingOrder = 40;
            _flash.gameObject.SetActive(false);

            SyncViews(0f);
        }

        protected override void OnBegin() => _starting = true;

        protected override void Play(float dt)
        {
            // The touch that starts the run sets the spark going; it isn't a try.
            bool tap = Input.Down && !_starting;
            _starting = false;

            int before = _sim.Score;
            var sparkAt = OnRing(_sim.Angle);
            var gemAt = _gem != null ? (Vector2)_gem.localPosition : sparkAt;
            var events = tap ? _sim.Tap() : RingEvent.None;
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & RingEvent.Hit) != 0)
            {
                bool perfect = (events & RingEvent.Perfect) != 0;
                Juice.Burst(transform, sparkAt, Palette.Lime, perfect ? 14 : 8, perfect ? 5f : 3.5f);
                Juice.Squash(_spark, 0.2f);
                Juice.Squash(_planet, perfect ? 0.08f : 0.05f);
                if (perfect) ShowFlash(_sim.Streak >= 2 ? $"PERFECT x{_sim.Streak}" : "PERFECT");
            }
            if ((events & RingEvent.Gem) != 0)
            {
                Juice.Burst(transform, gemAt, Palette.Teal, 16, 6f);
                Juice.ShakeSmall();
            }
            if ((events & RingEvent.LifeLost) != 0)
            {
                Juice.Burst(transform, OnRing(_sim.Angle), Palette.Red, 12, 4.5f);
                Juice.Squash(_spark, 0.25f);
                Juice.Squash(_planet, 0.12f);
                int lost = _sim.Lives;
                if (lost >= 0 && lost < _lives.Length) Juice.Burst(transform, _lives[lost].transform.localPosition, Palette.Red, 6, 2f);
                if ((events & RingEvent.Over) == 0) Juice.ShakeSmall();
            }
            if ((events & RingEvent.Red) != 0)
            {
                Juice.Burst(transform, sparkAt, Palette.Orange, 18, 6f);
                Juice.Squash(_spark, 0.3f);
                Juice.Squash(_planet, 0.15f);
            }
            if ((events & RingEvent.Over) != 0)
            {
                Juice.ShakeBig();
                EndRun();
            }
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            if (_shownTarget != _sim.TargetId)
            {
                bool first = _shownTarget < 0;
                _shownTarget = _sim.TargetId;
                RebuildArcs(!first);
            }

            // The spark faces the way it runs, kept upright.
            float tangent = _sim.Angle + _sim.Dir * 90f;
            _spark.localPosition = OnRing(_sim.Angle);
            _spark.localRotation = Quaternion.Euler(0f, 0f, tangent);
            _sparkRenderer.flipY = Mathf.Cos(tangent * Mathf.Deg2Rad) < 0f;

            // A trail of dots behind it, longer the faster it goes.
            bool moving = State == Phase.Playing;
            float spacing = _sim.Speed * 0.028f;
            for (int i = 0; i < _trail.Length; i++)
            {
                _trail[i].enabled = moving;
                _trail[i].transform.localPosition = OnRing(_sim.Angle - _sim.Dir * spacing * (i + 1.2f));
            }

            // The planet watches it.
            var look = (OnRing(_sim.Angle) - Center).normalized * 0.09f;
            for (int i = 0; i < _pupils.Length; i++)
                _pupils[i].localPosition = Vector2.Lerp(_pupils[i].localPosition, _eyeAt[i] + look, 1f - Mathf.Exp(-20f * dt));

            if (_gem != null) _gem.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f) * 12f);

            for (int i = 0; i < _lives.Length; i++) _lives[i].enabled = i < _sim.Lives;

            if (_flashFor > 0f)
            {
                _flashFor -= dt;
                _flash.transform.localPosition += new Vector3(0f, dt * 0.8f, 0f);
                if (_flashFor <= 0f) _flash.gameObject.SetActive(false);
            }
        }

        void RebuildArcs(bool pop)
        {
            if (_target != null) Destroy(_target.gameObject);
            if (_red != null) Destroy(_red.gameObject);
            if (_gem != null) Destroy(_gem.gameObject);
            _target = _red = _gem = null;

            var arc = _sim.Target;
            _target = MakeArc("Target", arc, arc.Gem ? Palette.Teal : Palette.Green, arc.Gem ? Palette.TealDark : Palette.GreenDark);
            // The perfect middle: a Lime strip, no outline.
            AddStrip(_target, OnRing(arc.Center), arc.Center, RingSim.PerfectHalf * 2f, ArcThick * 0.36f, Palette.Lime, ArcOrder + 3);
            if (pop) Juice.Pop(_target);

            if (arc.Gem)
            {
                var sr = MakeSprite("Gem", _gemSprite, OnRing(arc.Center, ArcThick / 2f + 0.42f), GemOrder);
                sr.transform.localScale = Vector3.one * GemScale;
                _gem = sr.transform;
                if (pop) Juice.Pop(_gem);
            }

            var red = _sim.RedArc;
            if (red != null)
            {
                _red = MakeArc("Red", red, Palette.Red, Palette.RedDark);
                // Thorns along its outside edge, pointing out.
                for (int i = 0; i < 3; i++)
                {
                    float a = red.Center + (i - 1) * red.Width * 0.3f;
                    var thorn = new GameObject("Thorn").AddComponent<SpriteRenderer>();
                    thorn.sprite = _thorn;
                    thorn.sortingOrder = ThornOrder;
                    thorn.transform.SetParent(_red, false);
                    thorn.transform.localPosition = OnRing(a, ArcThick / 2f + 0.2f) - OnRing(red.Center);
                    thorn.transform.localScale = Vector3.one * 1.15f;
                    thorn.transform.localRotation = Quaternion.Euler(0f, 0f, a - 90f);
                }
                if (pop) Juice.Pop(_red);
            }
        }

        /// <summary>An outlined arc on the ring, rooted at its middle so it pops in place.</summary>
        Transform MakeArc(string name, RingArc arc, Color fill, Color shade)
        {
            var mid = OnRing(arc.Center);
            var root = Make(name, mid).transform;
            AddTube(root, Center - mid, arc.Center - arc.Half, arc.Center + arc.Half, ArcThick, fill, shade, ArcOrder, 2f);
            return root;
        }

        /// <summary>
        /// A tube along the ring from <paramref name="from"/> to <paramref name="to"/> degrees, made
        /// of overlapping circles: an Ink outline, the shade colour, and the fill shifted toward the
        /// top-left light so the shade shows on the bottom right. <paramref name="center"/> is the
        /// ring's middle in <paramref name="parent"/>'s space.
        /// </summary>
        static void AddTube(Transform parent, Vector2 center, float from, float to, float thick, Color fill, Color shade, int order, float step)
        {
            float lit = thick * 0.76f;
            float shift = (thick - lit) / 2f * 0.7f;
            int n = Mathf.Max(1, Mathf.CeilToInt((to - from) / step));
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.Lerp(from, to, (float)i / n) * Mathf.Deg2Rad;
                var at = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Radius;
                Shapes.Make(parent, Shapes.Circle, Palette.Ink, at, Vector2.one * (thick + Style.Outline * 2f), order, "Ink");
                Shapes.Make(parent, Shapes.Circle, shade, at, Vector2.one * thick, order + 1, "Shade");
                Shapes.Make(parent, Shapes.Circle, fill, at + new Vector2(-shift, shift), Vector2.one * lit, order + 2, "Fill");
            }
        }

        /// <summary>A plain strip along the ring, centred on <paramref name="angle"/>, in a root placed at <paramref name="rootAt"/>.</summary>
        static void AddStrip(Transform parent, Vector2 rootAt, float angle, float width, float thick, Color color, int order)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(width / 1.5f));
            for (int i = 0; i <= n; i++)
            {
                float a = angle - width / 2f + width * i / n;
                Shapes.Make(parent, Shapes.Circle, color, OnRing(a) - rootAt, Vector2.one * thick, order, "Perfect");
            }
        }

        void ShowFlash(string text)
        {
            _flash.text = text;
            _flash.transform.localPosition = new Vector2(0f, Center.y + Radius + 1.05f);
            _flash.gameObject.SetActive(true);
            Juice.Pop(_flash.transform);
            _flashFor = 0.7f;
        }
    }
}
