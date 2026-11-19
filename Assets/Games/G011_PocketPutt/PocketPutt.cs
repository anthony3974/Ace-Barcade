using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AceBarcade.Games.PocketPutt
{
    /// <summary>
    /// Game 011. Mini golf seen from above, one hole per screen. Pull back anywhere to aim (a dotted
    /// arrow from the ball shows the way and how hard), let go to putt. 3 putts a hole: sinking it
    /// scores 3, 2 or 1. Running out of putts, or rolling into the red water, costs one of three
    /// lives. Holes go on forever, adding blocks, bumpers, sand, water and sliding gates.
    /// The rules live in <see cref="PuttSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(11, "pocket-putt", "Pocket Putt", Controls.Pull, Blurb = "Pull back to aim, let go to putt")]
    public sealed class PocketPutt : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        // The ball prop is 30 px (plus outline) across its radius; the bumper art 50 px.
        const float BallArt = 30f / Style.PixelsPerUnit;
        const float BumperArt = 50f / Style.PixelsPerUnit;
        static readonly Vector2 FlagFoot = new Vector2(20f, -62f) / Style.PixelsPerUnit;
        const float Rail = 0.35f;
        const float DotStep = 0.3f, DotSize = 0.15f, TipSize = 0.28f;
        const int MaxDots = 14;
        const float BarY = -7.6f;

        PuttSim _sim;
        Sprite _ball, _bumper, _flag;
        Transform _course, _ballT, _ballBody;
        readonly List<SpriteRenderer> _dots = new List<SpriteRenderer>();
        readonly List<Transform> _bumpers = new List<Transform>();
        readonly List<Transform> _movers = new List<Transform>();
        SpriteRenderer[] _lives, _putts;
        TextMeshPro _holeText, _message;

        Vector2 _pullStart, _pull;
        bool _pulling;
        float _sinkT;
        int _shownHole;

        public PuttSim Sim => _sim;

        protected override void Build()
        {
            _sim = new PuttSim(SeedForTests ?? System.Environment.TickCount);
            _ball = PropSprite("ball");
            _bumper = GameSprite("bumper");
            _flag = GameSprite("flag");
            var t = transform;

            // The green, inside a wooden rail.
            float w = PuttSim.Right - PuttSim.Left, h = PuttSim.Top - PuttSim.Bottom;
            var mid = new Vector2((PuttSim.Left + PuttSim.Right) / 2f, (PuttSim.Bottom + PuttSim.Top) / 2f);
            Shapes.Make(t, Shapes.Square, Palette.Slate, mid, new Vector2(w, h), -30, "Green");
            Shapes.Box(t, new Vector2(mid.x, PuttSim.Top + Rail / 2f), new Vector2(w + Rail * 2f, Rail), Palette.Brown, Palette.BrownDark, 8, "RailTop");
            Shapes.Box(t, new Vector2(mid.x, PuttSim.Bottom - Rail / 2f), new Vector2(w + Rail * 2f, Rail), Palette.Brown, Palette.BrownDark, 8, "RailBottom");
            Shapes.Box(t, new Vector2(PuttSim.Left - Rail / 2f, mid.y), new Vector2(Rail, h), Palette.Brown, Palette.BrownDark, 8, "RailLeft");
            Shapes.Box(t, new Vector2(PuttSim.Right + Rail / 2f, mid.y), new Vector2(Rail, h), Palette.Brown, Palette.BrownDark, 8, "RailRight");

            _ballT = Make("Ball").transform;
            var body = new GameObject("Body").AddComponent<SpriteRenderer>();
            body.sprite = _ball;
            body.sortingOrder = 10;
            _ballBody = body.transform;
            _ballBody.SetParent(_ballT, false);
            _ballBody.localScale = Vector3.one * (PuttSim.BallRadius / BallArt);

            for (int i = 0; i < MaxDots; i++)
            {
                var dot = Shapes.Make(t, Shapes.Circle, Palette.Cloud, Vector2.zero, Vector2.one * DotSize, 9, "Dot");
                dot.enabled = false;
                _dots.Add(dot);
            }

            _lives = new SpriteRenderer[PuttSim.StartLives];
            for (int i = 0; i < _lives.Length; i++)
            {
                _lives[i] = MakeSprite("Life", _flag, new Vector2(-3.65f + i * 0.55f, 6.6f), 40);
                _lives[i].transform.localScale = Vector3.one * 0.42f;
            }
            _putts = new SpriteRenderer[PuttSim.PuttsPerHole];
            for (int i = 0; i < _putts.Length; i++)
            {
                _putts[i] = MakeSprite("PuttLeft", _ball, new Vector2(2.4f + i * 0.6f, BarY), 40);
                _putts[i].transform.localScale = Vector3.one * 0.7f;
            }
            _holeText = MakeText("", 5f, Palette.Cloud, new Vector2(-2.2f, BarY));
            _message = MakeText("", 8f, Palette.Yellow, new Vector2(0f, 0.6f));

            BuildHole();
            SyncViews(0f);
        }

        TextMeshPro MakeText(string text, float size, Color color, Vector2 at)
        {
            var tmp = new GameObject("Text").AddComponent<TextMeshPro>();
            tmp.transform.SetParent(transform, false);
            tmp.transform.localPosition = at;
            tmp.font = UiKit.Font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
            tmp.sortingOrder = 30;
            return tmp;
        }

        /// <summary>Draws the current hole under a fresh object (the old one goes).</summary>
        void BuildHole()
        {
            if (_course != null) Destroy(_course.gameObject);
            _course = Make("Hole").transform;
            _bumpers.Clear();
            _movers.Clear();
            var hole = _sim.Hole;
            var random = new System.Random(hole.Number);

            foreach (var s in hole.Sand)
            {
                Shapes.Make(_course, Shapes.Square, Palette.Tan, s.Center, s.Half * 2f, -20, "Sand");
                int specks = Mathf.RoundToInt(s.Half.x * s.Half.y * 10f);
                for (int i = 0; i < specks; i++)
                {
                    var at = s.Center + new Vector2(Mathf.Lerp(-s.Half.x + 0.12f, s.Half.x - 0.12f, (float)random.NextDouble()), Mathf.Lerp(-s.Half.y + 0.12f, s.Half.y - 0.12f, (float)random.NextDouble()));
                    Shapes.Make(_course, Shapes.Circle, Palette.Brown, at, Vector2.one * 0.14f, -19, "Speck");
                }
            }
            foreach (var wtr in hole.Water)
            {
                Shapes.Box(_course, wtr.Center, wtr.Half * 2f, Palette.Red, Palette.RedDark, -18, "Water");
                for (float y = wtr.Center.y + wtr.Half.y - 0.3f; y > wtr.Center.y - wtr.Half.y + 0.25f; y -= 0.4f)
                    for (float x = wtr.Center.x - wtr.Half.x + 0.35f; x < wtr.Center.x + wtr.Half.x - 0.3f; x += 0.7f)
                        Shapes.Make(_course, Shapes.Square, Palette.RedDark, new Vector2(x + (Mathf.Repeat(y * 2f, 1f) * 0.3f), y), new Vector2(0.3f, 0.07f), -15, "Wave");
            }

            // The cup and its flag.
            Shapes.Make(_course, Shapes.Circle, Palette.Ink, hole.Cup, Vector2.one * (PuttSim.CupRadius + Style.Outline) * 2f, -10, "Cup");
            Shapes.Make(_course, Shapes.Circle, Palette.Night, hole.Cup + new Vector2(0.03f, -0.03f), Vector2.one * PuttSim.CupRadius * 1.5f, -9, "CupInside");
            var flag = MakeSprite("Flag", _flag, hole.Cup + FlagFoot, 12);
            flag.transform.SetParent(_course, true);

            foreach (var b in hole.Blocks)
                Juice.Pop(Shapes.Box(_course, b.Center, b.Half * 2f, Palette.Brown, Palette.BrownDark, 3, "Block"));
            foreach (var c in hole.Bumpers)
            {
                var root = new GameObject("Bumper").transform;
                root.SetParent(_course, false);
                root.localPosition = c;
                var sr = new GameObject("Body").AddComponent<SpriteRenderer>();
                sr.sprite = _bumper;
                sr.sortingOrder = 4;
                sr.transform.SetParent(root, false);
                sr.transform.localScale = Vector3.one * (PuttSim.BumperRadius / BumperArt);
                _bumpers.Add(sr.transform);
                Juice.Pop(root);
            }
            foreach (var m in hole.Movers)
            {
                Shapes.Make(_course, Shapes.Square, Palette.Night, new Vector2(0f, m.Box.Center.y), new Vector2(PuttSim.Right - PuttSim.Left, 0.12f), -12, "Track");
                var gate = Shapes.Box(_course, m.At(0f).Center, m.Box.Half * 2f, Palette.Purple, Palette.PurpleDark, 6, "Gate");
                _movers.Add(gate);
            }

            _holeText.text = $"HOLE {hole.Number}";
            Juice.Pop(_holeText.transform);
            Juice.Pop(flag.transform);
            _shownHole = hole.Number;
            _sinkT = 0f;
        }

        protected override void Play(float dt)
        {
            var events = PuttEvent.None;

            // Pull back anywhere: the putt goes the other way, as hard as the pull is long.
            if (Input.Down)
            {
                _pullStart = Input.Position;
                _pulling = true;
            }
            if (_pulling) _pull = _pullStart - Input.Position;
            if (_pulling && Input.Up)
            {
                _pulling = false;
                if (_sim.Putt(_pull))
                {
                    events |= PuttEvent.Putted;
                    Juice.Squash(_ballBody, 0.3f);
                    Juice.Burst(transform, _sim.Ball, Palette.Cloud, 5, 2f);
                }
            }
            else if (!Input.Pressed) _pulling = false;

            int before = _sim.Score;
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & PuttEvent.Wall) != 0)
            {
                Juice.Burst(transform, _sim.Ball, Palette.Tan, 4, 2f);
                Juice.Squash(_ballBody, 0.15f);
            }
            if ((events & PuttEvent.Bumper) != 0 && _sim.LastBumper >= 0 && _sim.LastBumper < _bumpers.Count)
            {
                Juice.Squash(_bumpers[_sim.LastBumper], 0.25f);
                Juice.Burst(transform, _sim.HitPoint, Palette.Sky, 8, 4f);
                Juice.ShakeSmall();
            }
            if ((events & PuttEvent.Sand) != 0) Juice.Burst(transform, _sim.Ball, Palette.Tan, 8, 2.5f);
            if ((events & PuttEvent.Sank) != 0)
            {
                Juice.Burst(transform, _sim.Hole.Cup, Palette.Lime, 14, 5f);
                Juice.Burst(transform, _sim.Hole.Cup, Palette.Yellow, 8, 3f);
                Juice.ShakeSmall();
                Say(_sim.LastPoints == PuttSim.PuttsPerHole ? "HOLE IN ONE!" : $"+{_sim.LastPoints}", Palette.Yellow);
            }
            if ((events & PuttEvent.Splashed) != 0)
            {
                Juice.Burst(transform, _sim.HitPoint, Palette.Orange, 14, 5f);
                Juice.Burst(transform, _sim.HitPoint, Palette.Paper, 6, 3f);
            }
            if ((events & PuttEvent.Missed) != 0)
            {
                int lost = _sim.Lives;
                if (lost >= 0 && lost < _lives.Length) Juice.Burst(transform, _lives[lost].transform.localPosition, Palette.Red, 6, 2f);
                Say(_sim.LostToWater ? "SPLASH!" : "OUT OF PUTTS", Palette.Cloud);
                if ((events & PuttEvent.Over) != 0) Juice.ShakeBig(); else Juice.ShakeSmall();
            }
            if ((events & PuttEvent.NewHole) != 0)
            {
                _message.text = "";
                BuildHole();
            }
            if ((events & PuttEvent.Over) != 0)
            {
                _pulling = false;
                EndRun();
            }
        }

        void Say(string text, Color color)
        {
            _message.text = text;
            _message.color = color;
            Juice.Pop(_message.transform);
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            if (_shownHole != _sim.HoleNumber) BuildHole();
            var hole = _sim.Hole;
            for (int i = 0; i < _movers.Count && i < hole.Movers.Count; i++)
                _movers[i].localPosition = hole.Movers[i].At(_sim.Time).Center;

            // The ball: drops into the cup when sunk, gone in the water.
            _ballT.localPosition = _sim.Ball;
            float scale = 1f;
            if (_sim.State == PuttState.Sunk)
            {
                _sinkT = Mathf.Min(1f, _sinkT + dt / 0.25f);
                scale = 1f - _sinkT;
            }
            bool shown = !(_sim.State == PuttState.Missed && _sim.LostToWater);
            _ballT.gameObject.SetActive(shown);
            _ballT.localScale = Vector3.one * scale;

            for (int i = 0; i < _lives.Length; i++) _lives[i].enabled = i < _sim.Lives;
            for (int i = 0; i < _putts.Length; i++) _putts[i].enabled = i < _sim.PuttsLeft;

            // The aim: a dotted arrow from the ball, as long as the putt is hard.
            int used = 0;
            bool aim = _pulling && _sim.State == PuttState.Aiming && !_sim.Over && _pull.magnitude >= PuttSim.MinPull;
            if (aim)
            {
                var v = PuttSim.VelocityFor(_pull);
                float power = v.magnitude / PuttSim.MaxSpeed;
                var dir = v.normalized;
                float len = 0.45f + 3.4f * power;
                for (float d = PuttSim.BallRadius + 0.25f; d <= len && used < MaxDots; d += DotStep)
                {
                    var dot = _dots[used++];
                    dot.transform.localPosition = _sim.Ball + dir * d;
                    dot.transform.localScale = Vector3.one * DotSize;
                    dot.enabled = true;
                }
                if (used > 0) _dots[used - 1].transform.localScale = Vector3.one * TipSize;
            }
            for (int i = used; i < _dots.Count; i++) _dots[i].enabled = false;
        }
    }
}
