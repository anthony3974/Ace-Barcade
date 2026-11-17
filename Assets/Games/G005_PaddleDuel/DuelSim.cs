using System;
using UnityEngine;

namespace AceBarcade.Games.PaddleDuel
{
    /// <summary>What happened during one <see cref="DuelSim.Step"/>.</summary>
    [Flags]
    public enum DuelEvent
    {
        None = 0,
        Wall = 1,
        Paddle = 2,      // the player returned the ball
        CpuHit = 4,      // the computer returned it
        PointWon = 8,    // the ball got past the computer
        LifeLost = 16,   // the ball got past the player
        GameOver = 32,
    }

    /// <summary>
    /// The rules of Paddle Duel, in plain C# so edit mode tests can play it. Two paddles in a
    /// portrait court: the player's along the bottom, the computer's along the top, with walls
    /// at the sides. Where the ball lands on a paddle sets the angle it leaves at. A return
    /// scores 1, getting the ball past the computer scores 5, and missing it costs one of three
    /// balls. Each point won makes the serve faster and the computer sharper, up to a cap.
    ///
    /// The computer plays fair. It only looks at the ball every <see cref="CpuReaction"/>
    /// seconds and heads for where it saw it (no reading ahead), at no more than
    /// <see cref="CpuSpeed"/>. So a straight ball comes back, but a sharp angle whose sideways
    /// speed beats the computer's top speed gets past it, at every skill level.
    /// </summary>
    public sealed class DuelSim
    {
        // --- Court (inner edges of the walls), in world units ---------------------------------

        public const float Left = -4.05f;
        public const float Right = 4.05f;

        public const float PlayerY = -5.4f;
        public const float CpuY = 4.9f;
        public const float PaddleHalfWidth = 1.0f;
        public const float PaddleHalfHeight = 0.2f;
        public const float BallRadius = 0.25f;

        /// <summary>Once the ball's centre is this low, the player has missed it.</summary>
        public const float LoseY = -6.9f;

        /// <summary>Once the ball's centre is this high, the computer has missed it.</summary>
        public const float WinY = 6.4f;

        /// <summary>A ball hitting a paddle's very edge leaves at this angle from straight on.</summary>
        public const float MaxBounceDegrees = 60f;

        /// <summary>How far from a paddle's centre the ball still counts as caught.</summary>
        public const float Reach = PaddleHalfWidth + BallRadius * 0.8f;

        // --- Speed ----------------------------------------------------------------------------

        public const float StartSpeed = 8f;
        public const float SpeedPerPoint = 0.5f;
        public const float MaxServeSpeed = 12f;
        public const float SpeedPerHit = 0.25f;
        public const float MaxSpeed = 16f;
        public const float ServeMaxDegrees = 15f;

        // --- The computer ---------------------------------------------------------------------

        /// <summary>Skill goes from 0 to 1 over this many points won.</summary>
        public const int PointsToFullSkill = 10;

        public const float CpuSpeedStart = 4.5f;
        public const float CpuSpeedFull = 7f;
        public const float CpuReactionStart = 0.22f;
        public const float CpuReactionFull = 0.1f;

        /// <summary>How far off centre it tries to hit (as a share of <see cref="Reach"/>), to angle its returns.</summary>
        public const float CpuAimStart = 0.1f;
        public const float CpuAimFull = 0.5f;

        /// <summary>While the ball heads away, it drifts home this much slower than its top speed.</summary>
        public const float CpuHomeFactor = 0.5f;

        // --- Scoring --------------------------------------------------------------------------

        public const int StartLives = 3;
        public const int ReturnPoints = 1;
        public const int WinPoints = 5;

        // --- State ----------------------------------------------------------------------------

        public Vector2 Ball { get; private set; }
        public Vector2 Velocity { get; private set; }
        public float PlayerX { get; private set; }
        public float CpuX { get; private set; }
        public bool Served { get; private set; }
        public int Lives { get; private set; } = StartLives;
        public int Score { get; private set; }
        public int Returns { get; private set; }
        public int PointsWon { get; private set; }
        public bool Over { get; private set; }

        /// <summary>Where the last point ended (for effects).</summary>
        public Vector2 PointAt { get; private set; }

        public float Speed => Velocity.magnitude;

        /// <summary>0 at the start, 1 after <see cref="PointsToFullSkill"/> points won.</summary>
        public float Skill => Mathf.Clamp01((float)PointsWon / PointsToFullSkill);

        public float CpuSpeed => Mathf.Lerp(CpuSpeedStart, CpuSpeedFull, Skill);
        public float CpuReaction => Mathf.Lerp(CpuReactionStart, CpuReactionFull, Skill);
        public float CpuAim => Mathf.Lerp(CpuAimStart, CpuAimFull, Skill);

        /// <summary>The speed the next serve goes at.</summary>
        public float ServeSpeed => Mathf.Min(MaxServeSpeed, StartSpeed + PointsWon * SpeedPerPoint);

        readonly System.Random _random;
        float _lookTimer, _seenX, _aim;

        public DuelSim(int seed = 0)
        {
            _random = new System.Random(seed);
            Ball = RestingBall();
        }

        Vector2 RestingBall() => new Vector2(PlayerX, PlayerY + PaddleHalfHeight + BallRadius + 0.02f);

        /// <summary>Move the player's paddle to x (clamped inside the walls).</summary>
        public void SetPlayer(float x) => PlayerX = ClampPaddle(x);

        static float ClampPaddle(float x) => Mathf.Clamp(x, Left + PaddleHalfWidth, Right - PaddleHalfWidth);

        /// <summary>Launch the ball up from the player's paddle, a little to one side.</summary>
        public void Serve()
        {
            if (Served || Over) return;
            Served = true;
            float angle = Mathf.Lerp(-ServeMaxDegrees, ServeMaxDegrees, (float)_random.NextDouble());
            Velocity = FromAngle(angle, ServeSpeed, up: true);
            BallTurnedToCpu();
        }

        public DuelEvent Step(float dt)
        {
            if (Over || dt <= 0f) return DuelEvent.None;
            if (!Served)
            {
                Ball = RestingBall();
                MoveCpu(dt);
                return DuelEvent.None;
            }

            var events = DuelEvent.None;
            // Small steps, so a fast ball can't pass through a paddle or a wall in one frame.
            int steps = Mathf.Max(1, Mathf.CeilToInt(Speed * dt / (BallRadius * 0.5f)));
            float h = dt / steps;
            for (int i = 0; i < steps && Served && !Over; i++) events |= Substep(h);
            return events;
        }

        DuelEvent Substep(float dt)
        {
            MoveCpu(dt);

            var events = DuelEvent.None;
            var p = Ball + Velocity * dt;
            var v = Velocity;

            if (p.x - BallRadius < Left) { p.x = Left + BallRadius; v.x = Mathf.Abs(v.x); events |= DuelEvent.Wall; }
            if (p.x + BallRadius > Right) { p.x = Right - BallRadius; v.x = -Mathf.Abs(v.x); events |= DuelEvent.Wall; }

            // The player's paddle catches a falling ball whose bottom crosses its top face.
            float playerTop = PlayerY + PaddleHalfHeight;
            if (v.y < 0f && Ball.y - BallRadius >= playerTop - 0.05f && p.y - BallRadius <= playerTop
                && Mathf.Abs(p.x - PlayerX) <= Reach)
            {
                float offset = Mathf.Clamp((p.x - PlayerX) / Reach, -1f, 1f);
                v = FromAngle(offset * MaxBounceDegrees, Mathf.Min(MaxSpeed, v.magnitude + SpeedPerHit), up: true);
                p.y = playerTop + BallRadius;
                Returns++;
                Score += ReturnPoints;
                events |= DuelEvent.Paddle;
                Velocity = v;
                BallTurnedToCpu();
            }

            // The computer's paddle catches a rising ball whose top crosses its bottom face.
            float cpuBottom = CpuY - PaddleHalfHeight;
            if (v.y > 0f && Ball.y + BallRadius <= cpuBottom + 0.05f && p.y + BallRadius >= cpuBottom
                && Mathf.Abs(p.x - CpuX) <= Reach)
            {
                float offset = Mathf.Clamp((p.x - CpuX) / Reach, -1f, 1f);
                v = FromAngle(offset * MaxBounceDegrees, Mathf.Min(MaxSpeed, v.magnitude + SpeedPerHit), up: false);
                p.y = cpuBottom - BallRadius;
                events |= DuelEvent.CpuHit;
            }

            Ball = p;
            Velocity = v;

            if (p.y > WinY)
            {
                PointsWon++;
                Score += WinPoints;
                PointAt = new Vector2(p.x, WinY);
                events |= DuelEvent.PointWon;
                ResetForServe();
            }
            else if (p.y < LoseY)
            {
                Lives--;
                PointAt = new Vector2(p.x, LoseY);
                events |= DuelEvent.LifeLost;
                if (Lives <= 0) { Over = true; events |= DuelEvent.GameOver; }
                else ResetForServe();
            }
            return events;
        }

        void ResetForServe()
        {
            Served = false;
            Velocity = Vector2.zero;
            Ball = RestingBall();
        }

        /// <summary>The ball has just started toward the computer: pick how it will angle its return.</summary>
        void BallTurnedToCpu()
        {
            // Aim off centre, on the side that sends the ball away from the player's paddle.
            float away = PlayerX > 0f ? -1f : PlayerX < 0f ? 1f : (_random.NextDouble() < 0.5 ? -1f : 1f);
            float amount = CpuAim * Mathf.Lerp(0.5f, 1f, (float)_random.NextDouble());
            _aim = away * amount * Reach;
            // It takes a reaction time to notice the ball is coming: until then it holds still.
            _seenX = CpuX + _aim;
            _lookTimer = CpuReaction;
        }

        void MoveCpu(float dt)
        {
            bool coming = Served && Velocity.y > 0f;
            float target, speed;
            if (coming)
            {
                // It only looks at the ball now and then, and heads for where it last saw it.
                _lookTimer -= dt;
                if (_lookTimer <= 0f)
                {
                    _seenX = Ball.x;
                    _lookTimer = CpuReaction;
                }
                // Hitting the ball off centre on the far side sends it away from the player.
                target = _seenX - _aim;
                speed = CpuSpeed;
            }
            else
            {
                target = 0f;
                speed = CpuSpeed * CpuHomeFactor;
            }
            CpuX = ClampPaddle(Mathf.MoveTowards(CpuX, target, speed * dt));
        }

        /// <summary>Where the ball will cross the height <paramref name="y"/>, bouncing off the walls (NaN if it's heading away).</summary>
        public float PredictX(float y)
        {
            if (!Served || Mathf.Approximately(Velocity.y, 0f)) return float.NaN;
            float t = (y - Ball.y) / Velocity.y;
            if (t < 0f) return float.NaN;
            float lo = Left + BallRadius, hi = Right - BallRadius, span = hi - lo;
            float x = Mathf.Repeat(Ball.x + Velocity.x * t - lo, span * 2f);
            return lo + (x <= span ? x : span * 2f - x);
        }

        static Vector2 FromAngle(float degreesFromStraight, float speed, bool up)
        {
            float r = degreesFromStraight * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), (up ? 1f : -1f) * Mathf.Cos(r)) * speed;
        }

        // --- Test helpers -------------------------------------------------------------------

        /// <summary>For tests: as if this many points had been won (sets skill and serve speed).</summary>
        public void SetPointsWon(int points) => PointsWon = points;

        /// <summary>For tests: put the ball somewhere with a velocity, already served.</summary>
        public void Place(Vector2 ball, Vector2 velocity)
        {
            Served = true;
            Ball = ball;
            Velocity = velocity;
            if (velocity.y > 0f) BallTurnedToCpu();
        }

        /// <summary>For tests: put the computer's paddle somewhere.</summary>
        public void SetCpu(float x) => CpuX = ClampPaddle(x);
    }
}
