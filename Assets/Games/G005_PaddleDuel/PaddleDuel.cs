using UnityEngine;

namespace AceBarcade.Games.PaddleDuel
{
    /// <summary>
    /// Game 005. Two-paddle table tennis against the computer. Drag your paddle along the
    /// bottom; the computer's purple paddle guards the green goal at the top. A return scores 1,
    /// getting the ball past the computer scores 5, and you have three balls (shown under the
    /// best score). Each point won makes the serve faster and the computer sharper.
    /// Same paddle, ball and spikes as Paddle Rally. The rules live in <see cref="DuelSim"/>.
    /// </summary>
    [ArcadeGame(5, "paddle-duel", "Paddle Duel", Controls.Drag, Blurb = "Get the ball past the computer")]
    public sealed class PaddleDuel : ArcadeGame
    {
        const float PaddleChase = 40f;
        const float ServeDelay = 1.0f;

        DuelSim _sim;
        Transform _player, _cpu, _ball;
        SpriteRenderer[] _lives;
        float _target, _restingFor;

        public DuelSim Sim => _sim;

        protected override void Build()
        {
            _sim = new DuelSim(System.Environment.TickCount);

            BuildCourt();

            _cpu = MakeSprite("Computer", GameSprite("paddle_cpu"), new Vector2(0f, DuelSim.CpuY), 10).transform;
            _player = MakeSprite("Paddle", PropSprite("paddle"), new Vector2(0f, DuelSim.PlayerY), 10).transform;
            _ball = MakeSprite("Ball", PropSprite("ball"), _sim.Ball, 20).transform;

            _lives = new SpriteRenderer[DuelSim.StartLives];
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
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(3.4f, 1.6f), new Vector2(3.8f, 3.8f), -20, "Decor");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-3.2f, -2.4f), new Vector2(2.6f, 2.6f), -20, "Decor");

            // Side walls: Blue, outlined, reaching past the screen edges so only their inner faces show.
            const float thick = 1.4f;
            const float goalLine = 6.15f;
            float wallBottom = -14f, wallTop = 14f;
            float height = wallTop - wallBottom, midY = (wallTop + wallBottom) / 2f;
            Shapes.Box(t, new Vector2(DuelSim.Left - thick / 2f, midY), new Vector2(thick, height), Palette.Blue, Palette.BlueDark, 0, "WallLeft");
            Shapes.Box(t, new Vector2(DuelSim.Right + thick / 2f, midY), new Vector2(thick, height), Palette.Blue, Palette.BlueDark, 0, "WallRight");

            // The computer's goal along the top: Green, a good place for the ball to go.
            Shapes.Box(t, new Vector2(0f, goalLine + 4f), new Vector2(DuelSim.Right - DuelSim.Left + thick * 2f, 8f), Palette.Green, Palette.GreenDark, 3, "Goal");

            // The spikes along the bottom (danger is Red), with RedDark below them down past the screen.
            const float spikesHeight = 120f / Style.PixelsPerUnit;
            float spikesY = DuelSim.LoseY - 0.25f - spikesHeight / 2f + 0.12f;
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
            if (Input.Pressed) _target = Input.Position.x;
            _sim.SetPlayer(Mathf.MoveTowards(_sim.PlayerX, _target, PaddleChase * dt));

            // After a point the ball waits on your paddle a moment, then goes.
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

        void React(DuelEvent events)
        {
            if ((events & DuelEvent.Paddle) != 0)
            {
                Juice.Squash(_player);
                Juice.Burst(transform, _sim.Ball + Vector2.down * DuelSim.BallRadius, Palette.Orange, 6, 3f);
                if (_sim.Speed > DuelSim.MaxSpeed * 0.8f) Juice.ShakeSmall();
            }
            if ((events & DuelEvent.CpuHit) != 0)
            {
                Juice.Squash(_cpu);
                Juice.Burst(transform, _sim.Ball + Vector2.up * DuelSim.BallRadius, Palette.Pink, 6, 3f);
            }
            if ((events & DuelEvent.Wall) != 0) Juice.Squash(_ball, 0.12f);

            if ((events & DuelEvent.PointWon) != 0)
            {
                _restingFor = 0f;
                Juice.Burst(transform, _sim.PointAt, Palette.Lime, 16, 6f);
                Juice.ShakeSmall();
                Juice.Squash(_cpu, 0.3f);
            }

            if ((events & DuelEvent.LifeLost) != 0)
            {
                _restingFor = 0f;
                Juice.Burst(transform, _sim.PointAt, Palette.Red, 16, 6f);
                Juice.ShakeBig();
                int lost = _sim.Lives; // the life icon that just went
                if (lost >= 0 && lost < _lives.Length) Juice.Burst(transform, _lives[lost].transform.localPosition, Palette.Red, 6, 2f);
            }

            if ((events & DuelEvent.GameOver) != 0)
            {
                _ball.gameObject.SetActive(false);
                EndRun();
            }
        }

        void SyncViews()
        {
            _player.localPosition = new Vector2(_sim.PlayerX, DuelSim.PlayerY);
            _cpu.localPosition = new Vector2(_sim.CpuX, DuelSim.CpuY);
            _ball.localPosition = _sim.Ball;
            for (int i = 0; i < _lives.Length; i++) _lives[i].enabled = i < _sim.Lives;
        }

        void LateUpdate()
        {
            if (State == Phase.Ready) SyncViews();
        }
    }
}
