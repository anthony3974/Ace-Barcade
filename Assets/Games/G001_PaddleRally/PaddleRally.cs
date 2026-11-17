using UnityEngine;

namespace AceBarcade.Games.PaddleRally
{
    /// <summary>
    /// Game 001. Drag to slide the paddle and keep the ball out of the spikes. Every return
    /// scores a point and speeds the ball up; where it hits the paddle sets its angle. Every
    /// few returns a gem appears up the court: hit it with the ball for bonus points.
    /// The rules live in <see cref="RallySim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(1, "paddle-rally", "Paddle Rally", Controls.Drag, Blurb = "Keep the ball out of the spikes")]
    public sealed class PaddleRally : ArcadeGame
    {
        /// <summary>How fast the paddle chases the finger (units per second).</summary>
        const float PaddleChase = 40f;

        RallySim _sim;
        Transform _paddle, _ball, _gem;
        float _paddleTarget;

        public RallySim Sim => _sim;

        protected override void Build()
        {
            _sim = new RallySim(System.Environment.TickCount);
            _paddleTarget = 0f;

            BuildCourt();

            _paddle = MakeSprite("Paddle", PropSprite("paddle"), new Vector2(0f, RallySim.PaddleY), 10).transform;
            _ball = MakeSprite("Ball", PropSprite("ball"), _sim.Ball, 20).transform;
            _gem = MakeSprite("Gem", PropSprite("gem"), Vector2.zero, 5).transform;
            _gem.gameObject.SetActive(false);
        }

        void BuildCourt()
        {
            var t = transform;

            // Background decoration: flat Slate shapes, no outlines, well behind the action.
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-3.6f, 3.4f), new Vector2(4.4f, 4.4f), -20, "Decor");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(4.1f, -1.4f), new Vector2(3.2f, 3.2f), -20, "Decor");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-1.2f, -3.2f), new Vector2(1.2f, 1.2f), -20, "Decor");

            // Walls: Blue, outlined, reaching past the screen edges so only their inner faces show.
            const float thick = 1.4f;
            float wallBottom = -14f; // well past the bottom of the tallest phone
            float wallTop = RallySim.Top + thick;
            float height = wallTop - wallBottom;
            float midY = (wallTop + wallBottom) / 2f;
            Shapes.Box(t, new Vector2(RallySim.Left - thick / 2f, midY), new Vector2(thick, height), Palette.Blue, Palette.BlueDark, 0, "WallLeft");
            Shapes.Box(t, new Vector2(RallySim.Right + thick / 2f, midY), new Vector2(thick, height), Palette.Blue, Palette.BlueDark, 0, "WallRight");
            Shapes.Box(t, new Vector2(0f, RallySim.Top + 4f), new Vector2(RallySim.Right - RallySim.Left + thick * 2f, 8f), Palette.Blue, Palette.BlueDark, 3, "WallTop");

            // The spikes along the bottom (danger is Red), with Ink below them down past the screen.
            const float spikesHeight = 120f / Style.PixelsPerUnit;
            float spikesY = RallySim.LoseY - 0.25f - spikesHeight / 2f + 0.12f;
            MakeSprite("Spikes", PropSprite("spikes"), new Vector2(0f, spikesY), 8);
            Shapes.Make(t, Shapes.Square, Palette.RedDark, new Vector2(0f, spikesY - spikesHeight / 2f - 2.5f), new Vector2(12f, 5.2f), 8, "Pit");
        }

        protected override void OnBegin() => _sim.Serve();

        protected override void Play(float dt)
        {
            if (Input.Pressed) _paddleTarget = Input.Position.x;
            float x = Mathf.MoveTowards(_sim.PaddleX, _paddleTarget, PaddleChase * dt);
            _sim.SetPaddle(x);

            int before = _sim.Score;
            var events = _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & RallyEvent.Paddle) != 0)
            {
                Juice.Squash(_paddle);
                Juice.Burst(transform, _sim.Ball + Vector2.down * RallySim.BallRadius, Palette.Orange, 6, 3f);
                if (_sim.Speed > RallySim.MaxSpeed * 0.75f) Juice.ShakeSmall();
            }
            if ((events & RallyEvent.Wall) != 0) Juice.Squash(_ball, 0.12f);
            if ((events & RallyEvent.Gem) != 0)
            {
                Juice.Burst(transform, _gem.localPosition, Palette.Teal, 14, 5f);
                Juice.ShakeSmall();
            }
            if ((events & RallyEvent.Lost) != 0)
            {
                Juice.Burst(transform, _sim.Ball, Palette.Red, 16, 6f);
                Juice.ShakeBig();
                _ball.gameObject.SetActive(false);
                EndRun();
            }

            SyncViews();
        }

        void LateUpdate()
        {
            // Before the first touch, keep the views where the sim says (the ball rests on the paddle).
            if (State == Phase.Ready) SyncViews();
        }

        void SyncViews()
        {
            _paddle.localPosition = new Vector2(_sim.PaddleX, RallySim.PaddleY);
            _ball.localPosition = _sim.Ball;

            bool gemOut = _sim.Gem.HasValue;
            if (gemOut && !_gem.gameObject.activeSelf)
            {
                _gem.localPosition = _sim.Gem.Value;
                _gem.gameObject.SetActive(true);
                Juice.Pop(_gem);
            }
            else if (!gemOut && _gem.gameObject.activeSelf) _gem.gameObject.SetActive(false);

            if (gemOut) _gem.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f) * 12f);
        }
    }
}
