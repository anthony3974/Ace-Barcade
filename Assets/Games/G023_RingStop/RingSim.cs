using System;
using UnityEngine;

namespace AceBarcade.Games.RingStop
{
    /// <summary>What happened during one <see cref="RingSim.Tap"/> or <see cref="RingSim.Step"/>.</summary>
    [Flags]
    public enum RingEvent
    {
        None = 0,
        Hit = 1,
        Perfect = 2,
        Gem = 4,
        /// <summary>A tap off the target (and off any red arc).</summary>
        Miss = 8,
        /// <summary>The spark ran past the target without a tap.</summary>
        Late = 16,
        /// <summary>A tap while the spark was on a red arc: the run ends at once.</summary>
        Red = 32,
        LifeLost = 64,
        Over = 128,
        NewTarget = 256,
    }

    /// <summary>
    /// An arc of the ring: where its middle is (degrees, anticlockwise from the right, as Unity
    /// draws angles), how wide it is, and how far the spark still has to go along its way to reach
    /// that middle (negative once it's past).
    /// </summary>
    public sealed class RingArc
    {
        public float Center { get; }
        public float Width { get; }
        public bool Gem { get; }
        public float Remaining { get; internal set; }

        public float Half => Width / 2f;

        internal RingArc(float center, float width, bool gem, float remaining)
        {
            Center = center;
            Width = width;
            Gem = gem;
            Remaining = remaining;
        }
    }

    /// <summary>
    /// The rules of Ring Stop, in plain C# so they can be tested. A spark runs round a ring; a
    /// tap while it's on the green target arc scores and sends it back the other way, a little
    /// faster, with a new, slightly narrower target ahead. A tap off the target, or letting the
    /// spark run past it, costs one of three lives (and moves the target). A tap in the middle of
    /// the target is perfect, and every 6th target holds a gem. From 10 hits a red arc can sit
    /// between the spark and the target: a tap on it ends the run straight away.
    ///
    /// Fair by time: a new target's near edge is always at least <see cref="ReactTime"/> away at
    /// the spark's speed; taps are ignored for <see cref="CalmTime"/> after a hit or a mistake (so
    /// a double tap can't cost a life), which is shorter than that; a red arc never comes within
    /// <see cref="RedLeadTime"/> of the spark nor within <see cref="RedClearTime"/> before the
    /// target, so a tap that's that early for the target is a miss, never a red one; and both
    /// arcs are forgiving: the target counts <see cref="Grace"/> past its drawn ends, a red arc
    /// only <see cref="RedForgive"/> inside them.
    /// </summary>
    public sealed class RingSim
    {
        /// <summary>The spark starts at the top of the ring, going clockwise.</summary>
        public const float StartAngle = 90f;

        // Speed in degrees a second, by hits.
        public const float StartSpeed = 120f;
        public const float SpeedUp = 3.5f;
        public const float MaxSpeed = 260f;

        // The target's drawn width in degrees, by hits.
        public const float StartWidth = 44f;
        public const float Narrow = 0.4f;
        public const float MinWidth = 28f;
        public const float GemWidthScale = 0.8f;

        /// <summary>A tap this far past either drawn end of the target still counts.</summary>
        public const float Grace = 3f;

        /// <summary>A tap this close to the target's middle is perfect.</summary>
        public const float PerfectHalf = 6f;

        /// <summary>A new target's near edge is at least this long away at the spark's speed...</summary>
        public const float ReactTime = 0.4f;

        /// <summary>...and at least this many degrees, and at most <see cref="MaxGap"/>.</summary>
        public const float MinGap = 50f;
        public const float MaxGap = 250f;

        /// <summary>Taps are ignored this long after a hit or a mistake.</summary>
        public const float CalmTime = 0.25f;

        public const int StartLives = 3;
        public const int HitPoints = 1;
        public const int PerfectPoints = 1;
        public const int GemPoints = 5;
        public const int GemEvery = 6;

        // Red arcs.
        public const int RedFrom = 10;
        public const int RedFullAt = 40;
        public const float RedChanceStart = 0.35f;
        public const float RedChanceMax = 0.7f;
        public const float RedWidth = 22f;
        public const float RedForgive = 3f;
        public const float RedLeadTime = 0.25f;
        public const float RedClearTime = 0.2f;

        /// <summary>Hits from which the speed and width stop changing.</summary>
        public const int HardestAt = 40;

        /// <summary>The spark never moves more than this in one go, so it can't skip an arc.</summary>
        const float SubStep = 2f;

        readonly System.Random _random;

        public float Angle { get; private set; } = StartAngle;
        /// <summary>+1 anticlockwise, -1 clockwise.</summary>
        public int Dir { get; private set; } = -1;

        public RingArc Target { get; private set; }
        /// <summary>The red arc on the way to the target, or null.</summary>
        public RingArc RedArc { get; private set; }

        /// <summary>Counts every target placed, so a view can tell when it changed.</summary>
        public int TargetId { get; private set; }

        public int Hits { get; private set; }
        public int Perfects { get; private set; }
        public int Streak { get; private set; }
        public int Gems { get; private set; }
        public int Lives { get; private set; } = StartLives;
        public int Score { get; private set; }
        public bool Over { get; private set; }
        public float Calm { get; private set; }

        /// <summary>After a hit: how far the spark was from the target's middle, in degrees.</summary>
        public float LastOffset { get; private set; }

        public float Speed => Mathf.Min(MaxSpeed, StartSpeed + SpeedUp * Hits);
        public float Width => Mathf.Max(MinWidth, StartWidth - Narrow * Hits);
        public bool NextIsGem => (Hits + 1) % GemEvery == 0;

        public float RedChance => Hits < RedFrom ? 0f
            : Mathf.Lerp(RedChanceStart, RedChanceMax, (float)(Hits - RedFrom) / (RedFullAt - RedFrom));

        /// <summary>The target counts from this far either side of its middle.</summary>
        public float Window => Target.Half + Grace;

        /// <summary>The spark is where a tap would hit the target.</summary>
        public bool OnTarget => Mathf.Abs(Target.Remaining) <= Window;

        /// <summary>The spark is where a tap would end the run.</summary>
        public bool OnRed => RedArc != null && Mathf.Abs(RedArc.Remaining) <= RedArc.Half - RedForgive;

        /// <summary>For tests: how far the last placement put the target's near edge ahead.</summary>
        public float LastGap { get; private set; }

        public RingSim(int seed)
        {
            _random = new System.Random(seed);
            PlaceTarget();
        }

        public RingEvent Step(float dt)
        {
            if (Over) return RingEvent.None;
            Calm = Mathf.Max(0f, Calm - dt);

            float move = Speed * dt;
            while (move > 0f)
            {
                float d = Mathf.Min(move, SubStep);
                move -= d;
                Angle = Mathf.Repeat(Angle + Dir * d, 360f);
                Target.Remaining -= d;
                if (RedArc != null) RedArc.Remaining -= d;

                if (Target.Remaining < -Window)
                    return RingEvent.Late | LoseLife();
            }
            return RingEvent.None;
        }

        /// <summary>The player tapped. Ignored for a moment after a hit or a mistake.</summary>
        public RingEvent Tap()
        {
            if (Over || Calm > 0f) return RingEvent.None;

            if (OnTarget)
            {
                var events = RingEvent.Hit;
                LastOffset = Target.Remaining;
                Score += HitPoints;
                if (Mathf.Abs(Target.Remaining) <= PerfectHalf)
                {
                    events |= RingEvent.Perfect;
                    Score += PerfectPoints;
                    Perfects++;
                    Streak++;
                }
                else Streak = 0;
                if (Target.Gem)
                {
                    events |= RingEvent.Gem;
                    Score += GemPoints;
                    Gems++;
                }
                Hits++;
                Dir = -Dir;
                Calm = CalmTime;
                PlaceTarget();
                return events | RingEvent.NewTarget;
            }

            if (OnRed)
            {
                Over = true;
                return RingEvent.Red | RingEvent.Over;
            }

            return RingEvent.Miss | LoseLife();
        }

        RingEvent LoseLife()
        {
            Lives--;
            Streak = 0;
            if (Lives <= 0)
            {
                Over = true;
                return RingEvent.LifeLost | RingEvent.Over;
            }
            Calm = CalmTime;
            PlaceTarget();
            return RingEvent.LifeLost | RingEvent.NewTarget;
        }

        /// <summary>A new target ahead of the spark (and maybe a red arc before it).</summary>
        void PlaceTarget()
        {
            bool gem = NextIsGem;
            float width = gem ? Width * GemWidthScale : Width;
            float window = width / 2f + Grace;
            float minGap = Mathf.Max(MinGap, Speed * ReactTime);

            float lead = Speed * RedLeadTime, clear = Speed * RedClearTime;
            float redNeeds = lead + RedWidth + clear;
            bool red = RedChance > 0f && _random.NextDouble() < RedChance && redNeeds <= MaxGap;

            float low = red ? Mathf.Max(minGap, redNeeds) : minGap;
            float gap = Mathf.Lerp(low, MaxGap, (float)_random.NextDouble());
            LastGap = gap;

            float remaining = gap + window;
            Target = new RingArc(Mathf.Repeat(Angle + Dir * remaining, 360f), width, gem, remaining);

            RedArc = null;
            if (red)
            {
                float from = lead + RedWidth / 2f, to = gap - clear - RedWidth / 2f;
                float at = Mathf.Lerp(from, to, (float)_random.NextDouble());
                RedArc = new RingArc(Mathf.Repeat(Angle + Dir * at, 360f), RedWidth, false, at);
            }
            TargetId++;
        }

        /// <summary>For tests: seconds until the spark reaches the target's middle.</summary>
        public float TimeToTarget => Target.Remaining / Speed;
    }
}
