using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AceBarcade.Games.HoopFling
{
    /// <summary>
    /// Game 012. A hoop toss seen from the side, on a rooftop at night. The ball waits low on the
    /// left; pull back anywhere to aim (dots show the start of the arc), let go to fling it. Down
    /// through the hoop scores 2, or 3 for a swish that touches nothing. The hoop moves after every
    /// basket, then bobs, then slides too. Three misses end the run.
    /// The rules live in <see cref="HoopSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(12, "hoop-fling", "Hoop Fling", Controls.Pull, Blurb = "Pull back to aim, let go to fling")]
    public sealed class HoopFling : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        // The hoop art is 240 x 330 px with the rim's centre at (90, 210): its middle is this far from the rim.
        static readonly Vector2 HoopArt = new Vector2(30f, 45f) / Style.PixelsPerUnit;
        const int PreviewDots = 8;
        const float PreviewStep = 0.055f, DotSize = 0.17f;
        const float Spin = 40f;

        HoopSim _sim;
        Sprite _ball;
        Transform _ballT, _ballBody, _hoopBack, _hoopFront, _stand;
        readonly List<SpriteRenderer> _dots = new List<SpriteRenderer>();
        SpriteRenderer[] _lives;
        TextMeshPro _message;

        Vector2 _pullStart, _pull;
        bool _pulling;
        float _spin, _messageTime;

        public HoopSim Sim => _sim;

        protected override void Build()
        {
            _sim = new HoopSim(SeedForTests ?? System.Environment.TickCount);
            _ball = GameSprite("ball");
            var t = transform;

            // A rooftop at night: a big Slate moon, a few stars, the roof's edge along the bottom.
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-2.6f, 4.4f), Vector2.one * 2.2f, -30, "Moon");
            var random = new System.Random(12);
            for (int i = 0; i < 18; i++)
            {
                float size = Mathf.Lerp(0.12f, 0.24f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-4.4f, 4.4f, (float)random.NextDouble()), Mathf.Lerp(-3f, 6.5f, (float)random.NextDouble()));
                Shapes.Make(t, Shapes.Circle, Palette.Slate, at, Vector2.one * size, -29, "Star");
            }
            for (int i = 0; i < 5; i++)
            {
                float w = Mathf.Lerp(1.2f, 2.2f, (float)random.NextDouble()), h = Mathf.Lerp(1.2f, 3.2f, (float)random.NextDouble());
                Shapes.Make(t, Shapes.Square, Palette.Slate, new Vector2(-4.5f + i * 2.2f, -6.2f + h / 2f), new Vector2(w, h), -28, "Building");
            }
            Shapes.Box(t, new Vector2(0f, -7.4f), new Vector2(11f, 2.4f), Palette.Brown, Palette.BrownDark, -5, "Roof");

            _stand = Shapes.Box(t, new Vector2(_sim.Ball.x, HoopSim.BallY - HoopSim.BallRadius - 0.6f), new Vector2(0.5f, 1.2f), Palette.Purple, Palette.PurpleDark, 2, "Stand");

            _hoopBack = MakeSprite("Hoop", GameSprite("hoop"), Vector2.zero, 5).transform;
            _hoopFront = MakeSprite("HoopFront", GameSprite("hoop_front"), Vector2.zero, 15).transform;

            _ballT = Make("Ball").transform;
            var body = new GameObject("Body").AddComponent<SpriteRenderer>();
            body.sprite = _ball;
            body.sortingOrder = 10;
            _ballBody = body.transform;
            _ballBody.SetParent(_ballT, false);

            for (int i = 0; i < PreviewDots; i++)
            {
                var dot = Shapes.Make(t, Shapes.Circle, Palette.Cloud, Vector2.zero, Vector2.one * DotSize, 9, "Dot");
                dot.enabled = false;
                _dots.Add(dot);
            }

            _lives = new SpriteRenderer[HoopSim.StartLives];
            for (int i = 0; i < _lives.Length; i++)
            {
                _lives[i] = MakeSprite("Life", _ball, new Vector2(-3.65f + i * 0.62f, 6.62f), 40);
                _lives[i].transform.localScale = Vector3.one * 0.62f;
            }

            _message = new GameObject("Message").AddComponent<TextMeshPro>();
            _message.transform.SetParent(t, false);
            _message.transform.localPosition = new Vector2(0f, 0f);
            _message.font = UiKit.Font;
            _message.fontSize = 8f;
            _message.alignment = TextAlignmentOptions.Center;
            _message.textWrappingMode = TextWrappingModes.NoWrap;
            _message.rectTransform.sizeDelta = new Vector2(8f, 2f);
            _message.sortingOrder = 30;
            _message.text = "";

            SyncViews(0f);
        }

        protected override void Play(float dt)
        {
            var events = HoopEvent.None;

            // Pull back anywhere: the fling goes the other way, as hard as the pull is long.
            if (Input.Down)
            {
                _pullStart = Input.Position;
                _pulling = true;
            }
            if (_pulling) _pull = _pullStart - Input.Position;
            if (_pulling && Input.Up)
            {
                _pulling = false;
                if (_sim.Throw(_pull))
                {
                    events |= HoopEvent.Thrown;
                    Juice.Squash(_ballBody, 0.3f);
                    Juice.Squash(_stand, 0.15f);
                    Juice.Burst(transform, _sim.Ball + Vector2.down * HoopSim.BallRadius, Palette.Cloud, 6, 2.5f);
                }
            }
            else if (!Input.Pressed) _pulling = false;

            int before = _sim.Score;
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & HoopEvent.Rim) != 0) Juice.Burst(transform, _sim.HitPoint, Palette.Teal, 5, 2.5f);
            if ((events & HoopEvent.Board) != 0) Juice.Burst(transform, _sim.HitPoint, Palette.Cloud, 5, 2.5f);
            if ((events & HoopEvent.Scored) != 0)
            {
                var rim = _sim.ShownRim;
                bool swish = (events & HoopEvent.Swish) != 0;
                Juice.Squash(_hoopFront, 0.2f);
                Juice.Burst(transform, rim, Palette.Lime, swish ? 16 : 10, swish ? 6f : 4f);
                if (swish) { Juice.Burst(transform, rim, Palette.Yellow, 8, 4f); Juice.ShakeSmall(); }
                Say(swish ? "SWISH! +3" : "+2", Palette.Yellow);
            }
            if ((events & HoopEvent.Missed) != 0)
            {
                int lost = _sim.Lives;
                if (lost >= 0 && lost < _lives.Length) Juice.Burst(transform, _lives[lost].transform.localPosition, Palette.Red, 6, 2f);
                Say("MISS", Palette.Cloud);
                if ((events & HoopEvent.Over) != 0) Juice.ShakeBig(); else Juice.ShakeSmall();
            }
            if ((events & HoopEvent.NewBall) != 0)
            {
                Juice.Pop(_ballBody);
                Juice.Pop(_hoopBack);
                Juice.Pop(_hoopFront);
                _spin = 0f;
            }
            if ((events & HoopEvent.Over) != 0)
            {
                _pulling = false;
                EndRun();
            }
        }

        void Say(string text, Color color)
        {
            _message.text = text;
            _message.color = color;
            _messageTime = 0.9f;
            Juice.Pop(_message.transform);
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            var rim = _sim.ShownRim;
            _hoopBack.localPosition = rim + HoopArt;
            _hoopFront.localPosition = rim + HoopArt;
            _message.transform.localPosition = rim + new Vector2(-0.4f, 1.9f);
            if (_messageTime > 0f && (_messageTime -= dt) <= 0f) _message.text = "";

            _ballT.localPosition = _sim.Ball;
            if (_sim.State != HoopState.Aiming) _spin -= _sim.Velocity.x * Spin * dt;
            _ballT.localRotation = Quaternion.Euler(0f, 0f, _spin);
            _stand.localPosition = new Vector2(_sim.State == HoopState.Aiming ? _sim.Ball.x : _stand.localPosition.x, _stand.localPosition.y);

            for (int i = 0; i < _lives.Length; i++) _lives[i].enabled = i < _sim.Lives;

            // The aim: the start of the arc only, so judging the rest is up to you.
            bool aim = _pulling && _sim.State == HoopState.Aiming && !_sim.Over && _pull.magnitude >= HoopSim.MinPull;
            for (int i = 0; i < _dots.Count; i++)
            {
                _dots[i].enabled = aim;
                if (aim) _dots[i].transform.localPosition = _sim.PreviewAt(_pull, (i + 1.5f) * PreviewStep);
            }
        }
    }
}
