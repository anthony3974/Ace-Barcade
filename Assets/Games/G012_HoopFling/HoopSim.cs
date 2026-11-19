using System;
using UnityEngine;

namespace AceBarcade.Games.HoopFling
{
    /// <summary>What happened during one <see cref="HoopSim.Step"/> (or <see cref="HoopSim.Throw"/>).</summary>
    [Flags]
    public enum HoopEvent
    {
        None = 0,
        Thrown = 1,
        Rim = 2,
        Board = 4,
        Scored = 8,
        Swish = 16,
        Missed = 32,
        Over = 64,
        NewBall = 128,
    }

    public enum HoopState { Aiming, Flying, Pause }

    /// <summary>
    /// The rules of Hoop Fling, in plain C# so they can be tested. A hoop toss seen from the side:
    /// the ball waits low on the left; pull back (anywhere) and let go to fling it, the opposite way
    /// to the pull and as hard as the pull is long. It arcs under gravity, bounces off the rim and
    /// the backboard, and scores if it drops down through the hoop: 2 points, or 3 for a swish that
    /// touches nothing. A ball that falls away or flies off the screen is a miss; three misses end
    /// the run.
    ///
    /// The hoop moves to a new spot after every basket. From the 5th basket it bobs up and down,
    /// and from the 12th it slides from side to side as well, a little faster as it goes.
    /// <b>Fair by design:</b> every spot the hoop can reach, it can be swished from the ball's spot
    /// with less than a full-power fling (the tests check the corners of its range).
    /// </summary>
    public sealed class HoopSim
    {
        // The ball.
        public const float BallRadius = 0.3f;
        public const float Gravity = 12f;
        public const float MaxPull = 2.6f;
        public const float MinPull = 0.3f;
        public const float MaxSpeed = 18.5f;
        public const float Bounce = 0.55f;
        public const float BallY = -5.3f;
        public const float BallMinX = -3.4f, BallMaxX = -1.6f;

        // The hoop: rim ends either side of its centre, the backboard just past the far end.
        public const float RimHalf = 0.62f;
        public const float RimEnd = 0.08f;
        public const float BoardLeft = 0.74f, BoardRight = 0.92f, BoardBottom = -0.3f, BoardTop = 1.5f;
        /// <summary>The range the rim's centre stays inside, however it moves.</summary>
        public const float HoopMinX = 0.8f, HoopMaxX = 2.9f, HoopMinY = -1.4f, HoopMaxY = 4.2f;

        public const int Basket = 2, SwishBonus = 1;
        public const int StartLives = 3;
        public const float PauseTime = 0.6f;
        const float MaxFlight = 6f;
        public const float FixedStep = 1f / 240f;
        const float OffBottom = -9.5f, OffSide = 5.6f;

        readonly System.Random _random;
        float _carry, _flight;

        /// <summary>The hoop's clock: it never stops while the run is on, so moving hoops keep moving.</summary>
        public float Time { get; private set; }

        public Vector2 Ball { get; private set; }
        public Vector2 Velocity { get; private set; }
        public HoopState State { get; private set; } = HoopState.Aiming;
        public float PauseLeft { get; private set; }

        // Where the hoop is, and how it moves.
        public Vector2 HoopBase { get; private set; }
        public float BobAmp { get; private set; }
        public float BobSpeed { get; private set; }
        public float SlideAmp { get; private set; }
        public float SlideSpeed { get; private set; }
        float _phase;

        public int Baskets { get; private set; }
        public int Swishes { get; private set; }
        public int Throws { get; private set; }
        public int Score { get; private set; }
        public int Lives { get; private set; } = StartLives;
        public bool Over { get; private set; }
        /// <summary>The ball touched the rim or the board on this throw.</summary>
        public bool Touched { get; private set; }
        public Vector2 HitPoint { get; private set; }

        public HoopSim(int seed)
        {
            _random = new System.Random(seed);
            NewBall();
            PlaceHoop();
        }

        // --- The hoop ------------------------------------------------------------------------------

        public Vector2 RimAt(float t) => HoopBase + new Vector2(
            SlideAmp > 0f ? SlideAmp * Mathf.Sin(_phase + SlideSpeed / SlideAmp * t) : 0f,
            BobAmp > 0f ? BobAmp * Mathf.Sin(_phase * 1.7f + BobSpeed / BobAmp * t) : 0f);

        public Vector2 Rim => RimAt(Time);

        float Range(float a, float b) => Mathf.Lerp(a, b, (float)_random.NextDouble());

        /// <summary>How the hoop moves after a number of baskets: (bob, bob speed, slide, slide speed).</summary>
        public static (float bob, float bobSpeed, float slide, float slideSpeed) Motion(int baskets)
        {
            float bob = 0f, bobSpeed = 0f, slide = 0f, slideSpeed = 0f;
            if (baskets >= 5)
            {
                float k = Mathf.Clamp01((baskets - 5) / 15f);
                bob = Mathf.Lerp(0.6f, 1.3f, k);
                bobSpeed = Mathf.Lerp(1.0f, 2.0f, k);
            }
            if (baskets >= 12)
            {
                float k = Mathf.Clamp01((baskets - 12) / 15f);
                slide = Mathf.Lerp(0.5f, 1.0f, k);
                slideSpeed = Mathf.Lerp(0.9f, 1.8f, k);
            }
            return (bob, bobSpeed, slide, slideSpeed);
        }

        void PlaceHoop()
        {
            var (bob, bobSpeed, slide, slideSpeed) = Motion(Baskets);
            BobAmp = bob;
            BobSpeed = bobSpeed;
            SlideAmp = slide;
            SlideSpeed = slideSpeed;
            _phase = Range(0f, Mathf.PI * 2f);
            // Somewhere new each time, far enough from the last spot to need a fresh aim.
            var last = HoopBase;
            for (int i = 0; i < 10; i++)
            {
                HoopBase = new Vector2(Range(HoopMinX + slide, HoopMaxX - slide), Range(HoopMinY + bob, HoopMaxY - bob));
                if (Baskets == 0 || Vector2.Distance(HoopBase, last) > 1.5f) break;
            }
        }

        void NewBall()
        {
            Ball = new Vector2(Range(BallMinX, BallMaxX), BallY);
            Velocity = Vector2.zero;
            State = HoopState.Aiming;
            Touched = false;
            _moveFrom = Vector2.zero;
        }

        // --- The fling ----------------------------------------------------------------------------

        public static Vector2 VelocityFor(Vector2 pull)
        {
            float len = pull.magnitude;
            if (len < MinPull) return Vector2.zero;
            return pull / len * Mathf.Min(len / MaxPull, 1f) * MaxSpeed;
        }

        public static Vector2 PullFor(Vector2 velocity)
        {
            float len = Mathf.Min(velocity.magnitude, MaxSpeed);
            return len < 1e-5f ? Vector2.zero : velocity.normalized * (len / MaxSpeed * MaxPull);
        }

        /// <summary>Where a fling would carry the ball after <paramref name="seconds"/>, ignoring the hoop (for the aim dots).</summary>
        public Vector2 PreviewAt(Vector2 pull, float seconds)
        {
            var v = VelocityFor(pull);
            return Ball + v * seconds + 0.5f * Gravity * seconds * seconds * Vector2.down;
        }

        public bool Throw(Vector2 pull)
        {
            if (Over || State != HoopState.Aiming) return false;
            var v = VelocityFor(pull);
            if (v == Vector2.zero) return false;
            Velocity = v;
            State = HoopState.Flying;
            Touched = false;
            _flight = 0f;
            Throws++;
            return true;
        }

        public HoopEvent Step(float dt)
        {
            if (Over) return HoopEvent.None;
            var events = HoopEvent.None;
            _carry += dt;
            while (_carry >= FixedStep && !Over)
            {
                _carry -= FixedStep;
                events |= Tick(FixedStep);
            }
            return events;
        }

        HoopEvent Tick(float h)
        {
            Time += h;
            var events = HoopEvent.None;
            switch (State)
            {
                case HoopState.Aiming:
                    return events;

                case HoopState.Pause:
                    // The ball carries on falling (through the net, or away), touching nothing.
                    Velocity += Vector2.down * Gravity * h;
                    Ball += Velocity * h;
                    PauseLeft -= h;
                    if (PauseLeft <= 0f)
                    {
                        NewBall();
                        events |= HoopEvent.NewBall;
                    }
                    return events;
            }

            _flight += h;
            var b = new Flight { Pos = Ball, Vel = Velocity };
            var r = Move(ref b, RimAt(Time), h);
            Ball = b.Pos;
            Velocity = b.Vel;
            if (r.Rim) { events |= HoopEvent.Rim; Touched = true; HitPoint = r.HitPoint; }
            if (r.Board) { events |= HoopEvent.Board; Touched = true; HitPoint = r.HitPoint; }

            if (r.Through)
            {
                Baskets++;
                bool swish = !Touched;
                if (swish) Swishes++;
                Score += Basket + (swish ? SwishBonus : 0);
                events |= HoopEvent.Scored | (swish ? HoopEvent.Swish : HoopEvent.None);
                // The ball drops through the net; the hoop moves on.
                Velocity = new Vector2(Velocity.x * 0.2f, Mathf.Min(Velocity.y, -2f));
                State = HoopState.Pause;
                PauseLeft = PauseTime;
                var rim = Rim;
                PlaceHoop();
                // Keep the hoop where it was until the next ball (it jumps there with a pop).
                _moveFrom = rim;
                return events;
            }

            if (r.Off || _flight > MaxFlight)
            {
                Lives--;
                events |= HoopEvent.Missed;
                State = HoopState.Pause;
                PauseLeft = PauseTime;
                if (Lives <= 0)
                {
                    Over = true;
                    return events | HoopEvent.Over;
                }
            }
            return events;
        }

        Vector2 _moveFrom;

        /// <summary>Where to draw the hoop: during the pause after a basket it stays where the ball went in.</summary>
        public Vector2 ShownRim => State == HoopState.Pause && _moveFrom != Vector2.zero ? _moveFrom : Rim;

        /// <summary>For the view: the next ball has appeared, so the hoop's old spot no longer matters.</summary>
        public void ClearMoveFrom() => _moveFrom = Vector2.zero;

        // --- Physics, shared with the solver --------------------------------------------------------

        public struct Flight
        {
            public Vector2 Pos, Vel;
        }

        public struct MoveResult
        {
            public bool Rim, Board, Through, Off;
            public Vector2 HitPoint;
        }

        public static MoveResult Move(ref Flight b, Vector2 rim, float h)
        {
            var r = new MoveResult();
            var prev = b.Pos;
            b.Vel += Vector2.down * Gravity * h;
            b.Pos += b.Vel * h;

            // The rim's two ends.
            for (int side = -1; side <= 1; side += 2)
            {
                var end = rim + new Vector2(side * RimHalf, 0f);
                var d = b.Pos - end;
                float dist = d.magnitude;
                float min = BallRadius + RimEnd;
                if (dist >= min || dist < 1e-6f) continue;
                var n = d / dist;
                b.Pos = end + n * min;
                float vn = Vector2.Dot(b.Vel, n);
                if (vn < 0f) b.Vel -= (1f + Bounce) * vn * n;
                r.Rim = true;
                r.HitPoint = end;
            }

            // The backboard.
            var bmin = rim + new Vector2(BoardLeft, BoardBottom);
            var bmax = rim + new Vector2(BoardRight, BoardTop);
            var q = new Vector2(Mathf.Clamp(b.Pos.x, bmin.x, bmax.x), Mathf.Clamp(b.Pos.y, bmin.y, bmax.y));
            var dq = b.Pos - q;
            if (dq.magnitude < BallRadius)
            {
                Vector2 n;
                if (dq.sqrMagnitude > 1e-10f) n = dq.normalized;
                else n = b.Pos.x < (bmin.x + bmax.x) / 2f ? Vector2.left : Vector2.right;
                b.Pos = q + n * BallRadius;
                float vn = Vector2.Dot(b.Vel, n);
                if (vn < 0f) b.Vel -= (1f + Bounce) * vn * n;
                r.Board = true;
                r.HitPoint = q;
            }

            // Down through the hoop: the centre crosses the rim's line, between its ends, going down.
            if (b.Vel.y < 0f && prev.y >= rim.y && b.Pos.y < rim.y && Mathf.Abs(b.Pos.x - rim.x) < RimHalf - RimEnd)
                r.Through = true;

            if (b.Pos.y < OffBottom || Mathf.Abs(b.Pos.x) > OffSide) r.Off = true;
            return r;
        }

        /// <summary>
        /// Flies a fling to its end from a spot and a time, with the hoop moving as it would: 0 for a
        /// miss, 1 for a basket off the rim or board, 2 for a swish.
        /// </summary>
        public int Try(Vector2 from, Vector2 pull, float startTime, bool moving = true)
        {
            var b = new Flight { Pos = from, Vel = VelocityFor(pull) };
            if (b.Vel == Vector2.zero) return 0;
            bool touched = false;
            float t = startTime;
            for (float f = 0f; f < MaxFlight; f += FixedStep)
            {
                t += FixedStep;
                var r = Move(ref b, moving ? RimAt(t) : RimAt(startTime), FixedStep);
                if (r.Rim || r.Board) touched = true;
                if (r.Through) return touched ? 1 : 2;
                if (r.Off) return 0;
            }
            return 0;
        }

        /// <summary>
        /// A fling from the ball that swishes it, released now (or null if none is found). Tries
        /// angles and strengths on a grid; with <paramref name="cleanOnly"/> off, any basket will do.
        /// </summary>
        public Vector2? SolveNow(bool cleanOnly = true) => Solve(Ball, Time, cleanOnly, true);

        public Vector2? Solve(Vector2 from, float time, bool cleanOnly, bool moving, float maxPower = 1f)
        {
            Vector2? any = null;
            for (float a = 30f; a <= 87f; a += 1f)
            {
                var dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                for (float p = 0.4f; p <= maxPower + 1e-4f; p += 0.01f)
                {
                    var pull = dir * (p * MaxPull);
                    int r = Try(from, pull, time, moving);
                    if (r == 2) return pull;
                    if (r == 1 && any == null) any = pull;
                }
            }
            return cleanOnly ? null : any;
        }

        /// <summary>For tests: put the hoop's centre at a spot, standing still.</summary>
        public void PlaceHoopForTests(Vector2 at)
        {
            HoopBase = at;
            BobAmp = SlideAmp = 0f;
        }
    }
}
