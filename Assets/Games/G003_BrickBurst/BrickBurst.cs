using UnityEngine;

namespace AceBarcade.Games.BrickBurst
{
    /// <summary>
    /// Game 003. Drag to slide the paddle and knock the ball into a wall of bricks. Tough grey
    /// bricks take two hits, teal gem bricks are worth more, and clearing a wall brings a new,
    /// harder one. Three lives: the lives left show as small balls under the best score.
    /// Same court, paddle and ball as Paddle Rally. The rules live in <see cref="BrickSim"/>.
    /// </summary>
    [ArcadeGame(3, "brick-burst", "Brick Burst", Controls.Drag, Blurb = "Break every brick")]
    public sealed class BrickBurst : ArcadeGame
    {
        const float PaddleChase = 40f;
        const float ServeDelay = 1.0f;

        static readonly string[] BrickSprites = { null, "brick_pink", "brick_orange", "brick_blue", "brick_purple", "brick_tough", "brick_cracked", "brick_gem" };

        static readonly Color[] BurstColors = { default, Palette.Pink, Palette.Orange, Palette.Blue, Palette.Purple, Palette.Mist, Palette.Mist, Palette.Teal };

        BrickSim _sim;
        Sprite[] _brickSprites;
        SpriteRenderer[,] _bricks;
        SpriteRenderer[] _lives;
        Transform _paddle, _ball;
        float _paddleTarget, _restingFor;
        bool _wallDirty = true;

        public BrickSim Sim => _sim;

        protected override void Build()
        {
            _sim = new BrickSim(System.Environment.TickCount);

            BuildCourt();

            _brickSprites = new Sprite[BrickSprites.Length];
            for (int i = 1; i < BrickSprites.Length; i++) _brickSprites[i] = GameSprite(BrickSprites[i]);
            _bricks = new SpriteRenderer[BrickSim.Columns, BrickSim.MaxRows];
            for (int c = 0; c < BrickSim.Columns; c++)
                for (int r = 0; r < BrickSim.MaxRows; r++)
                    _bricks[c, r] = MakeSprite($"Brick {c},{r}", null, BrickSim.BrickCenter(c, r), 6);

            _paddle = MakeSprite("Paddle", PropSprite("paddle"), new Vector2(0f, BrickSim.PaddleY), 10).transform;
            _ball = MakeSprite("Ball", PropSprite("ball"), _sim.Ball, 20).transform;

            _lives = new SpriteRenderer[BrickSim.StartLives];
            for (int i = 0; i < _lives.Length; i++)
            {
                _lives[i] = MakeSprite("Life", PropSprite("ball"), new Vector2(-3.75f + i * 0.42f, 6.72f), 12);
                _lives[i].transform.localScale = Vector3.one * 0.55f;
            }

            SyncViews();
        }

        void BuildCourt()
        {
            var t = transform;
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-3.6f, -1.6f), new Vector2(3.6f, 3.6f), -20, "Decor");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(4.0f, -4.2f), new Vector2(2.4f, 2.4f), -20, "Decor");

            const float thick = 1.4f;
            float wallBottom = -14f, wallTop = BrickSim.Top + thick;
            float height = wallTop - wallBottom, midY = (wallTop + wallBottom) / 2f;
            Shapes.Box(t, new Vector2(BrickSim.Left - thick / 2f, midY), new Vector2(thick, height), Palette.Blue, Palette.BlueDark, 0, "WallLeft");
            Shapes.Box(t, new Vector2(BrickSim.Right + thick / 2f, midY), new Vector2(thick, height), Palette.Blue, Palette.BlueDark, 0, "WallRight");
            Shapes.Box(t, new Vector2(0f, BrickSim.Top + 4f), new Vector2(BrickSim.Right - BrickSim.Left + thick * 2f, 8f), Palette.Blue, Palette.BlueDark, 3, "WallTop");

            const float spikesHeight = 120f / Style.PixelsPerUnit;
            float spikesY = BrickSim.LoseY - 0.25f - spikesHeight / 2f + 0.12f;
            MakeSprite("Spikes", PropSprite("spikes"), new Vector2(0f, spikesY), 8);
            Shapes.Make(t, Shapes.Square, Palette.RedDark, new Vector2(0f, spikesY - spikesHeight / 2f - 2.5f), new Vector2(12f, 5.2f), 8, "Pit");
        }

        protected override void OnBegin()
        {
            _sim.Serve();
            _restingFor = 0f;
        }

        protected override void Play(float dt)
        {
            if (Input.Pressed) _paddleTarget = Input.Position.x;
            _sim.SetPaddle(Mathf.MoveTowards(_sim.PaddleX, _paddleTarget, PaddleChase * dt));

            // After a lost ball or a cleared wall, the ball waits on the paddle a moment, then goes.
            if (!_sim.Served)
            {
                _restingFor += dt;
                if (_restingFor >= ServeDelay) { _sim.Serve(); _restingFor = 0f; }
            }

            int before = _sim.Score;
            var events = _sim.Step(dt);
            AddScore(_sim.Score - before);
            React(events);
            SyncViews();
        }

        void React(BrickEvent events)
        {
            if ((events & BrickEvent.Hit) != 0)
            {
                _wallDirty = true;
                var at = BrickSim.BrickCenter(_sim.LastHit.x, _sim.LastHit.y);
                var color = BurstColors[(int)_sim.LastHitKind];
                if ((events & BrickEvent.Broke) != 0) Juice.Burst(transform, at, color, 8, 4f);
                else
                {
                    Juice.Squash(_bricks[_sim.LastHit.x, _sim.LastHit.y].transform, 0.2f);
                    Juice.ShakeSmall();
                }
            }
            if ((events & BrickEvent.Gem) != 0)
            {
                Juice.Burst(transform, BrickSim.BrickCenter(_sim.LastHit.x, _sim.LastHit.y), Palette.Teal, 14, 6f);
                Juice.ShakeSmall();
            }
            if ((events & BrickEvent.Paddle) != 0)
            {
                Juice.Squash(_paddle);
                Juice.Burst(transform, _sim.Ball + Vector2.down * BrickSim.BallRadius, Palette.Orange, 6, 3f);
            }
            if ((events & BrickEvent.Wall) != 0) Juice.Squash(_ball, 0.12f);

            if ((events & BrickEvent.Cleared) != 0)
            {
                _wallDirty = true;
                _restingFor = 0f;
                Juice.ShakeBig();
                SyncWall();
                foreach (var b in _bricks) if (b.enabled) Juice.Pop(b.transform);
            }

            if ((events & BrickEvent.LifeLost) != 0)
            {
                _restingFor = 0f;
                Juice.Burst(transform, new Vector2(_sim.Ball.x, BrickSim.LoseY), Palette.Red, 16, 6f);
                Juice.ShakeBig();
                int lost = _sim.Lives; // the life icon that just went
                if (lost >= 0 && lost < _lives.Length) Juice.Burst(transform, _lives[lost].transform.localPosition, Palette.Red, 6, 2f);
            }

            if ((events & BrickEvent.GameOver) != 0)
            {
                _ball.gameObject.SetActive(false);
                EndRun();
            }
        }

        void SyncWall()
        {
            if (!_wallDirty) return;
            _wallDirty = false;
            for (int c = 0; c < BrickSim.Columns; c++)
            for (int r = 0; r < BrickSim.MaxRows; r++)
            {
                var kind = _sim.Bricks[c, r];
                _bricks[c, r].enabled = kind != BrickKind.None;
                _bricks[c, r].sprite = kind != BrickKind.None ? _brickSprites[(int)kind] : null;
            }
        }

        void SyncViews()
        {
            SyncWall();
            _paddle.localPosition = new Vector2(_sim.PaddleX, BrickSim.PaddleY);
            _ball.localPosition = _sim.Ball;
            for (int i = 0; i < _lives.Length; i++) _lives[i].enabled = i < _sim.Lives;
        }

        void LateUpdate()
        {
            if (State == Phase.Ready) SyncViews();
        }
    }
}
