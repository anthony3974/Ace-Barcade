using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.DuckRunner
{
    /// <summary>What happened during one <see cref="DuckSim.Step"/> (or a call that acts on the duck).</summary>
    [Flags]
    public enum DuckEvent
    {
        None = 0,
        Jumped = 1,
        Landed = 2,
        Ducked = 4,
        Dived = 8,
        Passed = 16,
        Decoy = 32,
        Crashed = 64,
        Spawned = 128,
    }

    /// <summary>Everything the duck runs into. Ground ones are jumped; high ones are ducked under.</summary>
    public enum ObstacleKind
    {
        Fox,
        Cattails,
        Log,
        Turtle,
        Hawk,
        Branch,
    }

    /// <summary>One obstacle's box (above the ground) and how fast it comes at the duck on its own.</summary>
    public readonly struct ObstacleSpec
    {
        /// <summary>Width of the box that counts as a hit.</summary>
        public readonly float Length;

        /// <summary>Height of the box's bottom and top above the ground.</summary>
        public readonly float Bottom, Top;

        /// <summary>Extra speed toward the duck on top of the ground's scroll (the fox runs, the hawk flies).</summary>
        public readonly float OwnSpeed;

        /// <summary>True for the high ones: the duck has to duck under them.</summary>
        public bool High => Bottom > 0f;

        public ObstacleSpec(float length, float bottom, float top, float ownSpeed)
        {
            Length = length;
            Bottom = bottom;
            Top = top;
            OwnSpeed = ownSpeed;
        }
    }

    public sealed class DuckObstacle
    {
        public int Index { get; }
        public ObstacleKind Kind { get; }
        public ObstacleSpec Spec => DuckSim.SpecOf(Kind);

        /// <summary>The left edge of its box, in world x.</summary>
        public float X { get; internal set; }

        /// <summary>Planned seconds between the last obstacle reaching the duck and this one doing so.</summary>
        public float Gap { get; }

        public bool Passed { get; internal set; }

        internal DuckObstacle(int index, ObstacleKind kind, float x, float gap)
        {
            Index = index;
            Kind = kind;
            X = x;
            Gap = gap;
        }
    }

    /// <summary>A teal decoy duck floating over a gap: worth a jump.</summary>
    public sealed class DuckDecoy
    {
        public float X { get; internal set; }
        public bool Taken { get; internal set; }

        internal DuckDecoy(float x) => X = x;
    }

    /// <summary>
    /// The rules of Duck Runner, in plain C# so edit mode tests can play it. A duck runs along
    /// the ground at a fixed x while obstacles scroll at it from the right. A touch makes it
    /// jump; a swipe down makes it duck (and dive back down first if it's in the air). Ground
    /// obstacles (foxes, cattails, logs, snapping turtles) are jumped; high ones (hawks,
    /// hanging branches) are ducked under. Each obstacle passed scores 1; a teal decoy duck
    /// floating over a gap scores 5. The scroll speeds up a little with every obstacle, to a cap.
    ///
    /// Heights are above the ground (the duck's feet are at <see cref="Y"/>, 0 when it stands);
    /// x is world x. Fairness is planned in time, like Rocket Lanes: each obstacle leaves a
    /// window to act in that is at least <see cref="MinSlack"/> long, and after one obstacle is
    /// dealt with (the latest jump has landed, or the duck is out from under), there is at least
    /// <see cref="Recover"/> before the next one needs acting on.
    /// </summary>
    public sealed class DuckSim
    {
        // --- The duck -------------------------------------------------------------------------

        public const float PlayerX = -2.4f;
        public const float HalfWidth = 0.36f;
        public const float StandHeight = 0.95f;
        public const float DuckHeight = 0.5f;

        public const float JumpSpeed = 13f;
        public const float Gravity = 36f;
        public const float DiveSpeed = 30f;

        /// <summary>A touch this soon before landing jumps on landing.</summary>
        public const float JumpBuffer = 0.12f;

        /// <summary>A quick swipe down ducks for at least this long; holding the finger keeps ducking.</summary>
        public const float DuckMin = 0.5f;

        /// <summary>Seconds from jumping to landing.</summary>
        public const float AirTime = 2f * JumpSpeed / Gravity;

        public const float Apex = JumpSpeed * JumpSpeed / (2f * Gravity);

        // --- Obstacles ------------------------------------------------------------------------

        public static ObstacleSpec SpecOf(ObstacleKind kind)
        {
            switch (kind)
            {
                case ObstacleKind.Fox: return new ObstacleSpec(1.05f, 0f, 0.72f, 1.5f);
                case ObstacleKind.Cattails: return new ObstacleSpec(0.6f, 0f, 1.32f, 0f);
                case ObstacleKind.Log: return new ObstacleSpec(1.55f, 0f, 0.6f, 0f);
                case ObstacleKind.Turtle: return new ObstacleSpec(1.0f, 0f, 0.6f, 0f);
                case ObstacleKind.Hawk: return new ObstacleSpec(1.1f, 0.62f, 1.3f, 2.5f);
                default: return new ObstacleSpec(1.0f, 0.62f, 100f, 0f);
            }
        }

        public static readonly ObstacleKind[] Kinds = (ObstacleKind[])Enum.GetValues(typeof(ObstacleKind));

        /// <summary>An obstacle's left edge meets the duck's right edge here.</summary>
        public const float ContactX = PlayerX + HalfWidth;

        /// <summary>Obstacles are planned until the newest is this far right (off the widest screen).</summary>
        public const float SpawnAheadX = 12f;

        /// <summary>Obstacles and decoys left of this are forgotten.</summary>
        public const float DespawnX = -9f;

        public const float DecoyHeight = 2.4f;
        public const float DecoyRadius = 0.35f;

        // --- Timing, in seconds ---------------------------------------------------------------

        /// <summary>A swipe down starts with a touch, which jumps: the touch has to come this long before a high obstacle.</summary>
        public const float DuckLead = 0.25f;

        /// <summary>At least this long between being free of one obstacle and having to act on the next.</summary>
        public const float Recover = 0.25f;

        /// <summary>Every jump has a window at least this long that clears its obstacle, at every speed.</summary>
        public const float MinSlack = 0.2f;

        public const float StartSpeed = 6f;
        public const float SpeedPerObstacle = 0.1f;
        public const float MaxSpeed = 10.5f;

        public const float FirstGap = 2.2f;
        public const float StartGap = 1.6f;
        public const float GapPerObstacle = 0.015f;
        public const float MinGap = 1.0f;

        /// <summary>The first obstacles are low and easy; cattails and then high ones join in later.</summary>
        public const int CattailsFrom = 2;
        public const int HighFrom = 4;

        public const int DecoyFrom = 3;
        public const float DecoyChance = 0.3f;

        public const int PassPoints = 1;
        public const int DecoyPoints = 5;

        // --- Fairness, as plain functions the tests check -------------------------------------

        public static float SpeedAt(int index) => Mathf.Min(MaxSpeed, StartSpeed + index * SpeedPerObstacle);

        public static float BaseGapAt(int index) => Mathf.Max(MinGap, StartGap - index * GapPerObstacle);

        /// <summary>Seconds an obstacle overlaps the duck's x, at a ground speed.</summary>
        public static float Overlap(ObstacleKind kind, float speed)
        {
            var s = SpecOf(kind);
            return (s.Length + 2f * HalfWidth) / (speed + s.OwnSpeed);
        }

        /// <summary>Seconds a jump spends with the duck's feet above <paramref name="height"/>.</summary>
        public static float TimeAbove(float height)
        {
            float d = JumpSpeed * JumpSpeed - 2f * Gravity * height;
            return d <= 0f ? 0f : 2f * Mathf.Sqrt(d) / Gravity;
        }

        /// <summary>
        /// When a jump can start and still clear a ground obstacle, in seconds relative to the
        /// moment the obstacle reaches the duck (negative is before).
        /// </summary>
        public static (float Early, float Late) JumpWindow(ObstacleKind kind, float speed)
        {
            float above = TimeAbove(SpecOf(kind).Top);
            float rise = JumpSpeed / Gravity;
            return (Overlap(kind, speed) - rise - above / 2f, above / 2f - rise);
        }

        /// <summary>The latest the player can start acting on an obstacle, relative to it reaching the duck.</summary>
        public static float ReadyBy(ObstacleKind kind, float speed) =>
            SpecOf(kind).High ? -DuckLead : JumpWindow(kind, speed).Late;

        /// <summary>When the player is free again after the latest safe action, relative to the obstacle reaching the duck.</summary>
        public static float FreeAt(ObstacleKind kind, float speed) =>
            SpecOf(kind).High ? Overlap(kind, speed) : JumpWindow(kind, speed).Late + AirTime;

        /// <summary>The shortest fair gap between two obstacles reaching the duck.</summary>
        public static float FairGap(ObstacleKind before, float speedBefore, ObstacleKind after, float speedAfter) =>
            FreeAt(before, speedBefore) + Recover - ReadyBy(after, speedAfter);

        /// <summary>The shortest gap that fits a decoy jump between two obstacles, with time to recover on both sides.</summary>
        public static float DecoyGap(ObstacleKind before, float speedBefore, ObstacleKind after, float speedAfter) =>
            FreeAt(before, speedBefore) + 2f * Recover + AirTime - ReadyBy(after, speedAfter);

        // --- State ----------------------------------------------------------------------------

        /// <summary>Height of the duck's feet above the ground.</summary>
        public float Y { get; private set; }

        public float VelocityY { get; private set; }
        public bool OnGround { get; private set; } = true;
        public bool Ducking => OnGround && (_duckHeld || _duckTimer > 0f);
        public float Height => Ducking ? DuckHeight : StandHeight;

        public int Score { get; private set; }
        public int Passed { get; private set; }
        public int Decoys { get; private set; }
        public bool Crashed { get; private set; }

        /// <summary>Seconds of play.</summary>
        public float Time { get; private set; }

        /// <summary>How far the ground has scrolled, in units (for the background).</summary>
        public float Distance { get; private set; }

        /// <summary>Where the duck hit something (x is world x, y is above the ground).</summary>
        public Vector2 CrashPoint { get; private set; }

        /// <summary>Where the last decoy was picked up.</summary>
        public Vector2 DecoyPoint { get; private set; }

        public float Speed => SpeedAt(Passed);

        public IReadOnlyList<DuckObstacle> Obstacles => _obstacles;
        public IReadOnlyList<DuckDecoy> DecoyList => _decoys;

        /// <summary>For tests: when false, no new obstacles are made.</summary>
        public bool AutoSpawn { get; set; } = true;

        readonly List<DuckObstacle> _obstacles = new List<DuckObstacle>();
        readonly List<DuckDecoy> _decoys = new List<DuckDecoy>();
        readonly System.Random _random;
        int _nextIndex;
        float _pressedAt = -1f;
        bool _duckHeld;
        float _duckTimer;

        public DuckSim(int seed = 0)
        {
            _random = new System.Random(seed);
            SpawnAhead();
        }

        // --- Input ----------------------------------------------------------------------------

        /// <summary>A finger touched down: jump (from standing or ducking), or jump on landing if it's in the air.</summary>
        public DuckEvent Press()
        {
            if (Crashed) return DuckEvent.None;
            _duckHeld = false;
            _duckTimer = 0f;
            if (OnGround) return Jump();
            _pressedAt = Time;
            return DuckEvent.None;
        }

        /// <summary>A swipe down: duck while the finger stays down (at least <see cref="DuckMin"/>), diving first if airborne.</summary>
        public DuckEvent SwipeDown()
        {
            if (Crashed) return DuckEvent.None;
            _pressedAt = -1f;
            _duckHeld = true;
            _duckTimer = DuckMin;
            if (!OnGround)
            {
                VelocityY = Mathf.Min(VelocityY, -DiveSpeed);
                return DuckEvent.Dived;
            }
            return DuckEvent.Ducked;
        }

        /// <summary>The finger lifted.</summary>
        public void Release() => _duckHeld = false;

        DuckEvent Jump()
        {
            OnGround = false;
            VelocityY = JumpSpeed;
            _pressedAt = -1f;
            return DuckEvent.Jumped;
        }

        // --- Stepping -------------------------------------------------------------------------

        public DuckEvent Step(float dt)
        {
            if (Crashed || dt <= 0f) return DuckEvent.None;
            var events = DuckEvent.None;
            // Small steps, so nothing passes through the duck in a long frame.
            float fastest = Mathf.Max(Speed + 2.5f, Mathf.Abs(VelocityY));
            int steps = Mathf.Max(1, Mathf.CeilToInt(fastest * dt / 0.05f));
            float h = dt / steps;
            for (int i = 0; i < steps && !Crashed; i++) events |= Substep(h);
            if (!Crashed) events |= SpawnAhead();
            return events;
        }

        DuckEvent Substep(float dt)
        {
            var events = DuckEvent.None;
            Time += dt;

            if (OnGround) _duckTimer = Mathf.Max(0f, _duckTimer - dt);
            else
            {
                Y += VelocityY * dt - Gravity * dt * dt / 2f;
                VelocityY -= Gravity * dt;
                if (Y <= 0f)
                {
                    Y = 0f;
                    VelocityY = 0f;
                    OnGround = true;
                    events |= DuckEvent.Landed;
                    if (_duckHeld || _duckTimer > 0f) events |= DuckEvent.Ducked;
                    else if (_pressedAt >= 0f && Time - _pressedAt <= JumpBuffer) events |= Jump();
                }
            }

            float speed = Speed;
            float move = speed * dt;
            Distance += move;
            foreach (var o in _obstacles) o.X -= (speed + o.Spec.OwnSpeed) * dt;
            foreach (var d in _decoys) d.X -= move;

            float left = PlayerX - HalfWidth, right = PlayerX + HalfWidth, top = Y + Height;
            foreach (var o in _obstacles)
            {
                var s = o.Spec;
                if (o.X < right && o.X + s.Length > left && Y < s.Top && top > s.Bottom)
                {
                    Crashed = true;
                    float x = (Mathf.Max(left, o.X) + Mathf.Min(right, o.X + s.Length)) / 2f;
                    float y = (Mathf.Max(Y, s.Bottom) + Mathf.Min(top, s.Top)) / 2f;
                    CrashPoint = new Vector2(x, y);
                    return events | DuckEvent.Crashed;
                }
                if (!o.Passed && o.X + s.Length < left)
                {
                    o.Passed = true;
                    Passed++;
                    Score += PassPoints;
                    events |= DuckEvent.Passed;
                }
            }

            foreach (var d in _decoys)
            {
                if (d.Taken) continue;
                float nx = Mathf.Clamp(d.X, left, right), ny = Mathf.Clamp(DecoyHeight, Y, top);
                float dx = d.X - nx, dy = DecoyHeight - ny;
                if (dx * dx + dy * dy < DecoyRadius * DecoyRadius)
                {
                    d.Taken = true;
                    Decoys++;
                    Score += DecoyPoints;
                    DecoyPoint = new Vector2(d.X, DecoyHeight);
                    events |= DuckEvent.Decoy;
                }
            }

            _obstacles.RemoveAll(o => o.X + o.Spec.Length < DespawnX);
            _decoys.RemoveAll(d => d.X < DespawnX);
            return events;
        }

        // --- Planning -------------------------------------------------------------------------

        DuckEvent SpawnAhead()
        {
            var events = DuckEvent.None;
            while (AutoSpawn && (_obstacles.Count == 0 || _obstacles[_obstacles.Count - 1].X < SpawnAheadX))
            {
                SpawnNext();
                events |= DuckEvent.Spawned;
            }
            return events;
        }

        /// <summary>Seconds until an obstacle reaches the duck, if the ground ran at <paramref name="speed"/> until then.</summary>
        static float TimeToContact(DuckObstacle o, float speed) => (o.X - ContactX) / (speed + o.Spec.OwnSpeed);

        void SpawnNext()
        {
            int k = _nextIndex++;
            var kind = PickKind(k);
            float speed = SpeedAt(k);
            var spec = SpecOf(kind);

            float gap, when;
            DuckObstacle last = _obstacles.Count > 0 ? _obstacles[_obstacles.Count - 1] : null;
            if (last == null)
            {
                // The first obstacle (or, in tests, one planned after every other has gone).
                gap = k == 0 ? FirstGap : StartGap;
                when = gap;
            }
            else
            {
                float lastSpeed = SpeedAt(last.Index);
                gap = Mathf.Max(BaseGapAt(k), FairGap(last.Kind, lastSpeed, kind, speed));
                bool decoy = k >= DecoyFrom && _random.NextDouble() < DecoyChance;
                if (decoy) gap = Mathf.Max(gap, DecoyGap(last.Kind, lastSpeed, kind, speed));
                float lastWhen = TimeToContact(last, lastSpeed);
                when = lastWhen + gap;
                if (decoy)
                {
                    // Halfway between being free of the last one and acting on this one, so a
                    // jump for it lands with at least Recover to spare on both sides.
                    float free = lastWhen + FreeAt(last.Kind, lastSpeed);
                    float ready = when + ReadyBy(kind, speed);
                    float at = (free + ready) / 2f;
                    _decoys.Add(new DuckDecoy(PlayerX + at * speed));
                }
            }
            _obstacles.Add(new DuckObstacle(k, kind, ContactX + when * (speed + spec.OwnSpeed), gap));
        }

        ObstacleKind PickKind(int k)
        {
            if (k < CattailsFrom) return k % 2 == 0 ? ObstacleKind.Log : ObstacleKind.Turtle;
            var pool = k < HighFrom
                ? new[] { ObstacleKind.Log, ObstacleKind.Turtle, ObstacleKind.Fox, ObstacleKind.Cattails }
                : Kinds;
            var last = _obstacles.Count > 0 ? _obstacles[_obstacles.Count - 1].Kind : (ObstacleKind)(-1);
            // Not the same thing twice in a row, so the run keeps changing.
            ObstacleKind pick;
            do pick = pool[_random.Next(pool.Length)];
            while (pick == last);
            return pick;
        }

        // --- Test helpers ---------------------------------------------------------------------

        /// <summary>For tests: plan obstacles until at least <paramref name="count"/> exist (however far).</summary>
        public void PlanAhead(int count)
        {
            while (_obstacles.Count < count) SpawnNext();
        }

        /// <summary>For tests: forget every obstacle and decoy and stop making new ones.</summary>
        public void Clear()
        {
            _obstacles.Clear();
            _decoys.Clear();
            AutoSpawn = false;
        }

        /// <summary>For tests: add an obstacle with its left edge at <paramref name="x"/>.</summary>
        public DuckObstacle Add(ObstacleKind kind, float x)
        {
            var o = new DuckObstacle(_nextIndex++, kind, x, 0f);
            _obstacles.Add(o);
            _obstacles.Sort((a, b) => a.X.CompareTo(b.X));
            return o;
        }

        /// <summary>For tests: add a decoy at <paramref name="x"/>.</summary>
        public DuckDecoy AddDecoy(float x)
        {
            var d = new DuckDecoy(x);
            _decoys.Add(d);
            return d;
        }
    }
}
