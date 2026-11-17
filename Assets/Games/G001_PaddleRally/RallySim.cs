using System;
using UnityEngine;

namespace AceBarcade.Games.PaddleRally
{
    /// <summary>What happened during one <see cref="RallySim.Step"/>.</summary>
    [Flags]
    public enum RallyEvent
    {
        None = 0,
        Wall = 1,
        Paddle = 2,
        Gem = 4,
        Lost = 8,
    }

    /// <summary>
    /// The rules of Paddle Rally, in plain C# so edit mode tests can play it. The court is the
    /// 9 x 16 play area minus the walls and the HUD strip. The ball speeds up with every return,
    /// and where it lands on the paddle sets the angle it flies off at. Every few returns a gem
    /// appears up the court for bonus points.
    /// </summary>
    public sealed class RallySim
    {
        // Court (inner edges of the walls), in world units.
        public const float Left = -4.05f;
        public const float Right = 4.05f;
        public const float Top = 6.15f;

        public const float PaddleY = -5.4f;
        public const float PaddleHalfWidth = 1.0f;
        public const float PaddleHalfHeight = 0.2f;

        public const float BallRadius = 0.25f;

        /// <summary>Once the ball's centre is this low it's gone.</summary>
        public const float LoseY = -6.9f;

        public const float StartSpeed = 10f;
        public const float SpeedPerReturn = 0.3f;
        public const float MaxSpeed = 18f;

        /// <summary>A ball hitting the paddle's very edge leaves at this angle from straight up.</summary>
        public const float MaxBounceDegrees = 62f;

        public const float GemRadius = 0.38f;
        public const int GemEvery = 4;
        public const int ReturnPoints = 1;
        public const int GemPoints = 5;

        public Vector2 Ball { get; private set; }
        public Vector2 Velocity { get; private set; }
        public float PaddleX { get; private set; }
        public int Returns { get; private set; }
        public int Score { get; private set; }
        public bool Served { get; private set; }
        public bool Lost { get; private set; }

        /// <summary>The gem's position, if one is out.</summary>
        public Vector2? Gem { get; private set; }

        public float Speed => Velocity.magnitude;

        readonly System.Random _random;

        public RallySim(int seed = 0)
        {
            _random = new System.Random(seed);
            PaddleX = 0f;
            Ball = RestingBall();
        }

        Vector2 RestingBall() => new Vector2(PaddleX, PaddleY + PaddleHalfHeight + BallRadius + 0.02f);

        /// <summary>Move the paddle toward x (clamped inside the walls).</summary>
        public void SetPaddle(float x) => PaddleX = Mathf.Clamp(x, Left + PaddleHalfWidth, Right - PaddleHalfWidth);

        /// <summary>Launch the ball up from the paddle, a little to one side.</summary>
        public void Serve()
        {
            if (Served) return;
            Served = true;
            float side = _random.NextDouble() < 0.5 ? -1f : 1f;
            Velocity = FromAngle(side * 18f, StartSpeed);
        }

        public RallyEvent Step(float dt)
        {
            if (Lost) return RallyEvent.None;
            if (!Served) { Ball = RestingBall(); return RallyEvent.None; }

            var events = RallyEvent.None;
            // Small steps, so a fast ball can't pass through the paddle or a wall in one frame.
            int steps = Mathf.Max(1, Mathf.CeilToInt(Speed * dt / (BallRadius * 0.5f)));
            float h = dt / steps;
            for (int i = 0; i < steps && !Lost; i++) events |= Substep(h);
            return events;
        }

        RallyEvent Substep(float dt)
        {
            var events = RallyEvent.None;
            var p = Ball + Velocity * dt;
            var v = Velocity;

            if (p.x - BallRadius < Left) { p.x = Left + BallRadius; v.x = Mathf.Abs(v.x); events |= RallyEvent.Wall; }
            if (p.x + BallRadius > Right) { p.x = Right - BallRadius; v.x = -Mathf.Abs(v.x); events |= RallyEvent.Wall; }
            if (p.y + BallRadius > Top) { p.y = Top - BallRadius; v.y = -Mathf.Abs(v.y); events |= RallyEvent.Wall; }

            // The paddle only catches a falling ball whose bottom crosses its top face.
            float paddleTop = PaddleY + PaddleHalfHeight;
            bool wasAbove = Ball.y - BallRadius >= paddleTop - 0.05f;
            bool nowAtOrBelow = p.y - BallRadius <= paddleTop;
            float reach = PaddleHalfWidth + BallRadius * 0.8f;
            if (v.y < 0f && wasAbove && nowAtOrBelow && Mathf.Abs(p.x - PaddleX) <= reach)
            {
                float offset = Mathf.Clamp((p.x - PaddleX) / reach, -1f, 1f);
                Returns++;
                Score += ReturnPoints;
                v = FromAngle(offset * MaxBounceDegrees, Mathf.Min(MaxSpeed, StartSpeed + Returns * SpeedPerReturn));
                p.y = paddleTop + BallRadius;
                events |= RallyEvent.Paddle;
                if (Gem == null && Returns % GemEvery == 0) Gem = NewGemSpot();
            }

            if (Gem is Vector2 gem && (p - gem).magnitude <= GemRadius + BallRadius)
            {
                Gem = null;
                Score += GemPoints;
                events |= RallyEvent.Gem;
            }

            if (p.y < LoseY)
            {
                Lost = true;
                events |= RallyEvent.Lost;
            }

            Ball = p;
            Velocity = v;
            return events;
        }

        Vector2 NewGemSpot()
        {
            float x = Mathf.Lerp(Left + 0.8f, Right - 0.8f, (float)_random.NextDouble());
            float y = Mathf.Lerp(0.5f, Top - 1.0f, (float)_random.NextDouble());
            return new Vector2(x, y);
        }

        static Vector2 FromAngle(float degreesFromUp, float speed)
        {
            float r = degreesFromUp * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r)) * speed;
        }

        // --- Test helpers -------------------------------------------------------------------

        /// <summary>For tests: put the ball somewhere with a velocity, already served.</summary>
        public void Place(Vector2 ball, Vector2 velocity)
        {
            Served = true;
            Ball = ball;
            Velocity = velocity;
        }

        /// <summary>For tests: put a gem somewhere.</summary>
        public void PlaceGem(Vector2 at) => Gem = at;
    }
}
