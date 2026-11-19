using System;
using UnityEngine;

namespace AceBarcade.Games.PailDrop
{
    /// <summary>What happened during one <see cref="PailSim.Tap"/> or <see cref="PailSim.Step"/>.</summary>
    [Flags]
    public enum PailEvent
    {
        None = 0,
        Dropped = 1,
        Catch = 2,
        Perfect = 4,
        Gem = 8,
        /// <summary>The ball reached the pail's rim height outside the pail.</summary>
        Miss = 16,
        /// <summary>A tap while holding a thornball: the run ends at once.</summary>
        Thorn = 32,
        /// <summary>A thornball was held long enough and tossed away, harmlessly.</summary>
        ThornGone = 64,
        LifeLost = 128,
        Over = 256,
        NewBall = 512,
    }

    public enum BallKind { Plain, Gem, Thorn }

    public enum HoldState { Holding, Falling, Reloading }

    /// <summary>
    /// The rules of Pail Drop, in plain C# so they can be tested. A critter hangs from a beam
    /// holding a ball; a pail slides back and forth along a track at the bottom. A tap drops the
    /// ball, which falls straight down: landing in the pail scores (more in its middle), and
    /// missing costs one of three lives. Each catch makes the pail a little faster and narrower.
    /// From 12 catches the critter sways side to side as it hangs, and from 10 it sometimes holds
    /// a red thornball instead: a tap then ends the run, and waiting lets it toss the thornball
    /// away. Every 6th ball is a gem.
    ///
    /// Fair by time: the ball falls straight down and always takes <see cref="FallTime"/>, so where
    /// it lands is known the moment it's dropped; taps are ignored for <see cref="CalmTime"/> after a
    /// new ball appears (so a late or double tap meant for the last ball can't drop the new one, or
    /// a thornball); a thornball never follows a thornball; and the pail counts the ball in a little
    /// past its inner walls.
    /// </summary>
    public sealed class PailSim
    {
        // The playfield, in world units.
        public const float HoldY = 3.4f;
        public const float RimY = -4.3f;
        public const float Gravity = 36f;
        /// <summary>The pail's outer edges never go past this either side of the middle.</summary>
        public const float YardHalf = 4f;
        public const float BallRadius = 0.3f;

        // The pail: speed and width by catches.
        public const float StartSpeed = 2.4f;
        public const float SpeedUp = 0.065f;
        public const float MaxSpeed = 5f;
        public const float StartWidth = 2.6f;
        public const float Narrow = 0.02f;
        public const float MinWidth = 1.8f;

        /// <summary>The ball's middle counts as in while it's within the pail's half width less this.</summary>
        public const float WallInset = 0.12f;

        /// <summary>A catch this close to the pail's middle is perfect.</summary>
        public const float PerfectHalf = 0.22f;

        /// <summary>Taps are ignored this long after a new ball appears.</summary>
        public const float CalmTime = 0.25f;
        /// <summary>After a ball lands, the next appears this much later.</summary>
        public const float ReloadTime = 0.4f;
        /// <summary>A thornball is held this long (after the calm), then tossed away.</summary>
        public const float ThornHold = 1.1f;

        public const int StartLives = 3;
        public const int CatchPoints = 1;
        public const int PerfectPoints = 1;
        public const int GemPoints = 5;
        public const int GemEvery = 6;

        // Thornballs.
        public const int ThornFrom = 10;
        public const int ThornFullAt = 40;
        public const float ThornChanceStart = 0.25f;
        public const float ThornChanceMax = 0.4f;

        // The sway.
        public const int SwayFrom = 12;
        public const int SwayFullAt = 40;
        public const float SwayMax = 2f;
        public const float SwayPeriod = 3.4f;
        /// <summary>How fast the sway's reach grows toward its target, in units a second.</summary>
        public const float SwayGrow = 0.5f;

        /// <summary>Catches from which the pail stops changing.</summary>
        public const int HardestAt = 40;

        /// <summary>Seconds from a drop to the ball reaching the rim.</summary>
        public static readonly float FallTime = Mathf.Sqrt(2f * (HoldY - RimY) / Gravity);

        readonly System.Random _random;
        float _phase, _wait, _fallFor;
        bool _lastThorn;

        public float BucketX { get; private set; }
        /// <summary>+1 right, -1 left.</summary>
        public int BucketDir { get; private set; } = 1;

        public float SwayAmp { get; private set; }
        public float HookX => SwayAmp * Mathf.Sin(_phase);

        public HoldState Hold { get; private set; } = HoldState.Holding;
        public BallKind Ball { get; private set; } = BallKind.Plain;
        /// <summary>Counts every ball handed out, so a view can tell when it changed.</summary>
        public int BallId { get; private set; } = 1;

        public float BallX { get; private set; }
        public float BallY { get; private set; } = HoldY;

        public int Hits { get; private set; }
        public int Perfects { get; private set; }
        public int Streak { get; private set; }
        public int Gems { get; private set; }
        public int Lives { get; private set; } = StartLives;
        public int Score { get; private set; }
        public bool Over { get; private set; }
        public float Calm { get; private set; }

        /// <summary>After a catch or a miss: the ball's middle less the pail's, at the rim.</summary>
        public float LastOffset { get; private set; }

        public float Speed => Mathf.Min(MaxSpeed, StartSpeed + SpeedUp * Hits);
        public float Width => Mathf.Max(MinWidth, StartWidth - Narrow * Hits);
        public float CatchHalf => Width / 2f - WallInset;
        public float BucketMax => YardHalf - Width / 2f;
        public bool NextIsGem => (Hits + 1) % GemEvery == 0;

        public float ThornChance => Hits < ThornFrom ? 0f
            : Mathf.Lerp(ThornChanceStart, ThornChanceMax, (float)(Hits - ThornFrom) / (ThornFullAt - ThornFrom));

        public float SwayTarget => Hits < SwayFrom ? 0f
            : SwayMax * Mathf.Clamp01((float)(Hits - SwayFrom) / (SwayFullAt - SwayFrom));

        /// <summary>A tap now would drop the ball (or set off the thornball).</summary>
        public bool CanTap => !Over && Hold == HoldState.Holding && Calm <= 0f;

        public PailSim(int seed)
        {
            _random = new System.Random(seed);
            // The pail starts off to one side, heading across.
            BucketDir = _random.NextDouble() < 0.5 ? 1 : -1;
            BucketX = -BucketDir * BucketMax * 0.8f;
            BallX = HookX;
        }

        public PailEvent Step(float dt)
        {
            if (Over) return PailEvent.None;
            var events = PailEvent.None;
            Calm = Mathf.Max(0f, Calm - dt);

            BucketX = Slide(BucketX, BucketDir, dt, out int dir);
            BucketDir = dir;

            SwayAmp = Mathf.MoveTowards(SwayAmp, SwayTarget, SwayGrow * dt);
            _phase = Mathf.Repeat(_phase + dt * Mathf.PI * 2f / SwayPeriod, Mathf.PI * 2f);

            switch (Hold)
            {
                case HoldState.Holding:
                    BallX = HookX;
                    BallY = HoldY;
                    if (Ball == BallKind.Thorn && Calm <= 0f)
                    {
                        _wait -= dt;
                        if (_wait <= 0f)
                        {
                            events |= PailEvent.ThornGone;
                            Reload();
                        }
                    }
                    break;

                case HoldState.Falling:
                    _fallFor += dt;
                    BallY = HoldY - 0.5f * Gravity * _fallFor * _fallFor;
                    if (_fallFor >= FallTime)
                    {
                        BallY = RimY;
                        events |= Land();
                    }
                    break;

                case HoldState.Reloading:
                    _wait -= dt;
                    if (_wait <= 0f) events |= NewBall();
                    break;
            }
            return events;
        }

        /// <summary>The player tapped. Ignored while a ball falls, and for a moment after a new one appears.</summary>
        public PailEvent Tap()
        {
            if (!CanTap) return PailEvent.None;

            if (Ball == BallKind.Thorn)
            {
                Over = true;
                return PailEvent.Thorn | PailEvent.Over;
            }

            Hold = HoldState.Falling;
            BallX = HookX;
            _fallFor = 0f;
            return PailEvent.Dropped;
        }

        PailEvent Land()
        {
            LastOffset = BallX - BucketX;
            if (Mathf.Abs(LastOffset) > CatchHalf)
            {
                Lives--;
                Streak = 0;
                if (Lives <= 0)
                {
                    Over = true;
                    return PailEvent.Miss | PailEvent.LifeLost | PailEvent.Over;
                }
                Reload();
                return PailEvent.Miss | PailEvent.LifeLost;
            }

            var events = PailEvent.Catch;
            Score += CatchPoints;
            if (Mathf.Abs(LastOffset) <= PerfectHalf)
            {
                events |= PailEvent.Perfect;
                Score += PerfectPoints;
                Perfects++;
                Streak++;
            }
            else Streak = 0;
            if (Ball == BallKind.Gem)
            {
                events |= PailEvent.Gem;
                Score += GemPoints;
                Gems++;
            }
            Hits++;
            Reload();
            // The pail narrows; keep it inside the yard.
            BucketX = Mathf.Clamp(BucketX, -BucketMax, BucketMax);
            return events;
        }

        void Reload()
        {
            Hold = HoldState.Reloading;
            _wait = ReloadTime;
        }

        PailEvent NewBall()
        {
            Hold = HoldState.Holding;
            Calm = CalmTime;
            BallX = HookX;
            BallY = HoldY;

            if (NextIsGem) Ball = BallKind.Gem;
            else if (!_lastThorn && ThornChance > 0f && _random.NextDouble() < ThornChance) Ball = BallKind.Thorn;
            else Ball = BallKind.Plain;
            _lastThorn = Ball == BallKind.Thorn;
            if (Ball == BallKind.Thorn) _wait = ThornHold;

            BallId++;
            return PailEvent.NewBall;
        }

        /// <summary>The pail slid <paramref name="dt"/> on from <paramref name="x"/>, bouncing off the ends.</summary>
        float Slide(float x, int dir, float dt, out int newDir)
        {
            float max = BucketMax;
            x += dir * Speed * dt;
            newDir = dir;
            // Fold back off the ends (more than once if dt is long).
            for (int i = 0; i < 8 && (x > max || x < -max); i++)
            {
                if (x > max) { x = 2f * max - x; newDir = -1; }
                else { x = -2f * max - x; newDir = 1; }
            }
            return x;
        }

        // --- Looking ahead (for the bot in the tests, and the play test) -------------------------

        /// <summary>Where the pail will be in <paramref name="t"/> seconds, at today's speed and width.</summary>
        public float BucketXIn(float t) => Slide(BucketX, BucketDir, t, out _);

        /// <summary>Where the critter's hands will be in <paramref name="t"/> seconds.</summary>
        public float HookXIn(float t)
        {
            float amp = Mathf.MoveTowards(SwayAmp, SwayTarget, SwayGrow * t);
            return amp * Mathf.Sin(_phase + t * Mathf.PI * 2f / SwayPeriod);
        }

        /// <summary>
        /// Where a ball dropped in <paramref name="t"/> seconds would land, measured from the pail's
        /// middle (positive is right of it).
        /// </summary>
        public float OffsetIfDroppedIn(float t) => HookXIn(t) - BucketXIn(t + FallTime);
    }
}
