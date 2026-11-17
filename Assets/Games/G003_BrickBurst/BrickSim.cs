using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.BrickBurst
{
    public enum BrickKind
    {
        None = 0,
        Pink, Orange, Blue, Purple,   // one hit
        Tough,                        // two hits
        Cracked,                      // a tough brick that's been hit once
        Gem,                          // one hit, bonus points
    }

    [Flags]
    public enum BrickEvent
    {
        None = 0,
        Wall = 1,
        Paddle = 2,
        Hit = 4,        // a brick was hit (see LastHit)
        Broke = 8,      // ... and broke
        Gem = 16,       // ... and it was a gem
        LifeLost = 32,
        Cleared = 64,   // the last brick went; a new wall is up
        GameOver = 128,
    }

    /// <summary>
    /// The rules of Brick Burst, in plain C# so edit mode tests can play it. Break the wall with
    /// the ball; a paddle return sends it back up at an angle set by where it lands. Tough bricks
    /// take two hits; gem bricks pay a bonus. Clear the wall and a new, harder one appears.
    /// Missing the ball costs one of three lives; the ball then waits on the paddle to be served.
    /// The court is the same as Paddle Rally's.
    /// </summary>
    public sealed class BrickSim
    {
        // Court (inner edges of the walls), in world units.
        public const float Left = -4.05f;
        public const float Right = 4.05f;
        public const float Top = 6.15f;

        public const float PaddleY = -5.4f;
        public const float PaddleHalfWidth = 1.0f;
        public const float PaddleHalfHeight = 0.2f;
        public const float BallRadius = 0.25f;
        public const float LoseY = -6.9f;
        public const float MaxBounceDegrees = 62f;

        // The wall of bricks.
        public const int Columns = 7;
        public const int MaxRows = 8;
        public const float BrickWidth = 1.1f;
        public const float BrickHeight = 0.5f;
        public const float BrickGap = 0.06f;
        /// <summary>The top edge of the first row, leaving room for the ball to get behind the wall.</summary>
        public const float WallTop = Top - 0.9f;

        public const int StartLives = 3;
        public const float StartSpeed = 8.5f;
        public const float SpeedPerLevel = 0.6f;
        public const float SpeedPerBrick = 0.04f;
        public const float MaxSpeed = 15f;

        public const int BrickPoints = 1;
        public const int ToughPoints = 2;
        public const int GemPoints = 5;
        public const int ClearBonus = 10;

        public readonly BrickKind[,] Bricks = new BrickKind[Columns, MaxRows];

        public Vector2 Ball { get; private set; }
        public Vector2 Velocity { get; private set; }
        public float PaddleX { get; private set; }
        public bool Served { get; private set; }
        public int Lives { get; private set; } = StartLives;
        public int Level { get; private set; }
        public int Score { get; private set; }
        public bool Over { get; private set; }

        /// <summary>The brick (column, row) hit most recently, for effects.</summary>
        public Vector2Int LastHit { get; private set; }

        /// <summary>The kind of the brick hit most recently, before the hit.</summary>
        public BrickKind LastHitKind { get; private set; }

        public float Speed => Velocity.magnitude;
        public int BricksLeft { get; private set; }

        readonly System.Random _random;
        float _levelSpeed;

        public BrickSim(int seed = 0)
        {
            _random = new System.Random(seed);
            BuildWall();
            Ball = RestingBall();
        }

        /// <summary>The centre of a brick, in world units. Row 0 is the top row.</summary>
        public static Vector2 BrickCenter(int col, int row)
        {
            float x = Left + BrickGap / 2f + BrickWidth / 2f + col * (BrickWidth + BrickGap) + (Right - Left - Columns * (BrickWidth + BrickGap)) / 2f;
            float y = WallTop - BrickHeight / 2f - row * (BrickHeight + BrickGap);
            return new Vector2(x, y);
        }

        Vector2 RestingBall() => new Vector2(PaddleX, PaddleY + PaddleHalfHeight + BallRadius + 0.02f);

        public void SetPaddle(float x) => PaddleX = Mathf.Clamp(x, Left + PaddleHalfWidth, Right - PaddleHalfWidth);

        public void Serve()
        {
            if (Served || Over) return;
            Served = true;
            float side = _random.NextDouble() < 0.5 ? -1f : 1f;
            Velocity = FromAngle(side * 20f, _levelSpeed);
        }

        // --- The wall -----------------------------------------------------------------------

        /// <summary>
        /// Lays out the wall for the current level: more rows as levels go up (5 to 8), tough
        /// bricks from level 2, a gem or two each wall, and gaps from level 3.
        /// </summary>
        void BuildWall()
        {
            Array.Clear(Bricks, 0, Bricks.Length);
            int rows = Mathf.Min(MaxRows, 5 + Level);
            var colors = new[] { BrickKind.Pink, BrickKind.Orange, BrickKind.Blue, BrickKind.Purple };
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < Columns; c++)
                {
                    var kind = colors[r % colors.Length];
                    if (Level >= 1 && r < Level && (c + r) % 2 == 0) kind = BrickKind.Tough;
                    if (Level >= 2 && _random.NextDouble() < 0.12) kind = BrickKind.None;
                    Bricks[c, r] = kind;
                }
            }
            int gems = 1 + Level / 2;
            for (int i = 0; i < gems; i++)
                Bricks[_random.Next(Columns), _random.Next(rows)] = BrickKind.Gem;

            BricksLeft = 0;
            foreach (var b in Bricks) if (b != BrickKind.None) BricksLeft++;
            _levelSpeed = Mathf.Min(MaxSpeed, StartSpeed + Level * SpeedPerLevel);
        }

        // --- Time ---------------------------------------------------------------------------

        public BrickEvent Step(float dt)
        {
            if (Over) return BrickEvent.None;
            if (!Served) { Ball = RestingBall(); return BrickEvent.None; }

            var events = BrickEvent.None;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Speed * dt / (BallRadius * 0.5f)));
            float h = dt / steps;
            for (int i = 0; i < steps && Served && !Over; i++)
            {
                events |= Substep(h);
                if ((events & BrickEvent.Cleared) != 0) break;
            }
            return events;
        }

        BrickEvent Substep(float dt)
        {
            var events = BrickEvent.None;
            var p = Ball + Velocity * dt;
            var v = Velocity;

            if (p.x - BallRadius < Left) { p.x = Left + BallRadius; v.x = Mathf.Abs(v.x); events |= BrickEvent.Wall; }
            if (p.x + BallRadius > Right) { p.x = Right - BallRadius; v.x = -Mathf.Abs(v.x); events |= BrickEvent.Wall; }
            if (p.y + BallRadius > Top) { p.y = Top - BallRadius; v.y = -Mathf.Abs(v.y); events |= BrickEvent.Wall; }

            events |= HitBrick(ref p, ref v);

            float paddleTop = PaddleY + PaddleHalfHeight;
            bool wasAbove = Ball.y - BallRadius >= paddleTop - 0.05f;
            float reach = PaddleHalfWidth + BallRadius * 0.8f;
            if (v.y < 0f && wasAbove && p.y - BallRadius <= paddleTop && Mathf.Abs(p.x - PaddleX) <= reach)
            {
                float offset = Mathf.Clamp((p.x - PaddleX) / reach, -1f, 1f);
                v = FromAngle(offset * MaxBounceDegrees, v.magnitude);
                p.y = paddleTop + BallRadius;
                events |= BrickEvent.Paddle;
            }

            Ball = p;
            Velocity = v;

            if (p.y < LoseY)
            {
                Lives--;
                Served = false;
                Velocity = Vector2.zero;
                Ball = RestingBall();
                events |= BrickEvent.LifeLost;
                if (Lives <= 0) { Over = true; events |= BrickEvent.GameOver; }
            }
            return events;
        }

        /// <summary>At most one brick per small step: bounce off the side the ball came in from.</summary>
        BrickEvent HitBrick(ref Vector2 p, ref Vector2 v)
        {
            for (int c = 0; c < Columns; c++)
            for (int r = 0; r < MaxRows; r++)
            {
                var kind = Bricks[c, r];
                if (kind == BrickKind.None) continue;
                var center = BrickCenter(c, r);
                var half = new Vector2(BrickWidth / 2f, BrickHeight / 2f);
                var closest = new Vector2(Mathf.Clamp(p.x, center.x - half.x, center.x + half.x), Mathf.Clamp(p.y, center.y - half.y, center.y + half.y));
                if ((p - closest).sqrMagnitude > BallRadius * BallRadius) continue;

                // Which face: compare how deep the ball is in from each side, scaled by the brick's shape.
                var d = p - center;
                float overlapX = half.x + BallRadius - Mathf.Abs(d.x);
                float overlapY = half.y + BallRadius - Mathf.Abs(d.y);
                if (overlapX < overlapY) { v.x = Mathf.Sign(d.x) * Mathf.Abs(v.x); p.x = center.x + Mathf.Sign(d.x) * (half.x + BallRadius); }
                else { v.y = Mathf.Sign(d.y) * Mathf.Abs(v.y); p.y = center.y + Mathf.Sign(d.y) * (half.y + BallRadius); }

                return Damage(c, r, ref v);
            }
            return BrickEvent.None;
        }

        BrickEvent Damage(int c, int r, ref Vector2 v)
        {
            var kind = Bricks[c, r];
            LastHit = new Vector2Int(c, r);
            LastHitKind = kind;
            var events = BrickEvent.Hit;

            if (kind == BrickKind.Tough)
            {
                Bricks[c, r] = BrickKind.Cracked;
                return events;
            }

            Bricks[c, r] = BrickKind.None;
            BricksLeft--;
            events |= BrickEvent.Broke;
            Score += kind == BrickKind.Gem ? GemPoints : kind == BrickKind.Cracked ? ToughPoints : BrickPoints;
            if (kind == BrickKind.Gem) events |= BrickEvent.Gem;
            v = v.normalized * Mathf.Min(MaxSpeed, v.magnitude + SpeedPerBrick);

            if (BricksLeft <= 0)
            {
                Score += ClearBonus;
                Level++;
                BuildWall();
                Served = false;
                Velocity = Vector2.zero;
                Ball = RestingBall();
                events |= BrickEvent.Cleared;
            }
            return events;
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

        /// <summary>For tests: empty the wall, then set the given bricks.</summary>
        public void SetWall(params (int col, int row, BrickKind kind)[] bricks)
        {
            Array.Clear(Bricks, 0, Bricks.Length);
            foreach (var b in bricks) Bricks[b.col, b.row] = b.kind;
            BricksLeft = bricks.Length;
        }

        /// <summary>For tests: every brick on the wall, as (col, row).</summary>
        public IEnumerable<Vector2Int> AllBricks()
        {
            for (int c = 0; c < Columns; c++)
                for (int r = 0; r < MaxRows; r++)
                    if (Bricks[c, r] != BrickKind.None) yield return new Vector2Int(c, r);
        }
    }
}
