using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AceBarcade.Games.PocketPutt
{
    /// <summary>What happened during one <see cref="PuttSim.Step"/> (or <see cref="PuttSim.Putt"/>).</summary>
    [Flags]
    public enum PuttEvent
    {
        None = 0,
        Putted = 1,
        Wall = 2,
        Bumper = 4,
        Sand = 8,
        Sank = 16,
        Splashed = 32,
        Missed = 64,
        NewHole = 128,
        Over = 256,
        Stopped = 512,
    }

    public enum PuttState { Aiming, Rolling, Sunk, Missed }

    /// <summary>An axis-aligned box: a wall block, a patch of sand or water, or a sliding gate.</summary>
    public struct PuttBox
    {
        public Vector2 Center;
        public Vector2 Half;

        public PuttBox(Vector2 center, Vector2 half) { Center = center; Half = half; }
        public Vector2 Min => Center - Half;
        public Vector2 Max => Center + Half;
        public bool Contains(Vector2 p) => Mathf.Abs(p.x - Center.x) <= Half.x && Mathf.Abs(p.y - Center.y) <= Half.y;

        /// <summary>The distance from a point to the box (0 inside).</summary>
        public float Distance(Vector2 p)
        {
            var q = new Vector2(Mathf.Clamp(p.x, Min.x, Max.x), Mathf.Clamp(p.y, Min.y, Max.y));
            return Vector2.Distance(p, q);
        }
    }

    /// <summary>A gate that slides from side to side across the course.</summary>
    public sealed class PuttMover
    {
        public PuttBox Box;
        public float Amp;
        public float Speed;
        public float Phase;

        public float OffsetAt(float t) => Amp * Mathf.Sin(Phase + Speed / Amp * t);
        public float VelocityAt(float t) => Speed * Mathf.Cos(Phase + Speed / Amp * t);
        public PuttBox At(float t) => new PuttBox(Box.Center + new Vector2(OffsetAt(t), 0f), Box.Half);
    }

    /// <summary>One hole: where the ball starts, the cup, and what's in the way.</summary>
    public sealed class PuttHole
    {
        public int Number;
        public Vector2 Tee;
        public Vector2 Cup;
        public readonly List<PuttBox> Blocks = new List<PuttBox>();
        public readonly List<Vector2> Bumpers = new List<Vector2>();
        public readonly List<PuttBox> Sand = new List<PuttBox>();
        public readonly List<PuttBox> Water = new List<PuttBox>();
        public readonly List<PuttMover> Movers = new List<PuttMover>();
        /// <summary>A putt (from the tee, gates left out) that sinks it first time: proof it's fair.</summary>
        public Vector2 Proof;
    }

    /// <summary>
    /// The rules of Pocket Putt, in plain C# so they can be tested. Mini golf seen from above, one
    /// hole per screen. Pull back (anywhere) to aim: the putt goes the opposite way to the pull, as
    /// hard as the pull is long. The ball rolls, slows, bounces off the walls, wooden blocks and
    /// (harder) the round bumpers, crawls through sand, and drops in the cup if it's slow enough
    /// when it gets there. Red water loses the hole at once.
    ///
    /// 3 putts a hole: sinking it scores 3, 2 or 1 by putts taken. Running out of putts, or the
    /// water, costs one of 3 lives. Holes go on forever, adding blocks, then bumpers (hole 2), sand
    /// (3), water (5) and sliding gates (7).
    /// <b>Fair by design:</b> every hole is built only once <see cref="Solve"/> finds a single putt
    /// from the tee that sinks it (with the gates left out, so any hole can be holed in one with the
    /// right timing), and nothing is ever placed over the tee or the cup.
    /// </summary>
    public sealed class PuttSim
    {
        // The course: inside the walls.
        public const float Left = -4.05f, Right = 4.05f, Bottom = -6.9f, Top = 5.7f;

        // The ball and the cup.
        public const float BallRadius = 0.27f;
        public const float CupRadius = 0.38f;
        /// <summary>A ball faster than this rolls over the cup instead of dropping in.</summary>
        public const float SinkSpeed = 5f;
        public const float BumperRadius = 0.55f;

        // The putt.
        public const float MaxPull = 2.6f;
        public const float MinPull = 0.3f;
        public const float MaxSpeed = 13f;
        public const float GrassDrag = 2.8f;
        public const float SandDrag = 13f;
        public const float WallBounce = 0.78f;
        public const float BumperBounce = 1.15f;
        public const float BumperMinSpeed = 4f;
        const float StopSpeed = 0.06f;
        const float MaxRoll = 14f;

        public const int PuttsPerHole = 3;
        public const int StartLives = 3;
        /// <summary>How long a sunk or missed hole stays on the screen before the next.</summary>
        public const float PauseTime = 1.1f;
        public const float FixedStep = 1f / 120f;

        readonly int _seed;
        /// <summary>The next hole, built in the background while this one is played (finding its proof takes a moment).</summary>
        Task<PuttHole> _next;
        int _nextNumber;
        float _carry;

        public PuttHole Hole { get; private set; }
        public int HoleNumber => Hole.Number;
        /// <summary>Seconds since this hole began: the gates' clock.</summary>
        public float Time { get; private set; }

        public Vector2 Ball { get; private set; }
        public Vector2 Velocity { get; private set; }
        public PuttState State { get; private set; } = PuttState.Aiming;
        public int Putts { get; private set; }
        public int PuttsLeft => PuttsPerHole - Putts;
        public int Lives { get; private set; } = StartLives;
        public int Score { get; private set; }
        public bool Over { get; private set; }
        public float PauseLeft { get; private set; }
        public bool InSand { get; private set; }
        /// <summary>The points the last sunk hole scored (3, 2 or 1).</summary>
        public int LastPoints { get; private set; }
        public int Sunk { get; private set; }
        /// <summary>Where the last splash or bumper hit was.</summary>
        public Vector2 HitPoint { get; private set; }
        public int LastBumper { get; private set; } = -1;
        /// <summary>The hole that was just lost was lost to the water.</summary>
        public bool LostToWater { get; private set; }

        public PuttSim(int seed)
        {
            _seed = seed;
            StartHole(1);
        }

        // --- The putt ------------------------------------------------------------------------------

        /// <summary>The ball's starting velocity for a pull (zero for one too short to putt).</summary>
        public static Vector2 VelocityFor(Vector2 pull)
        {
            float len = pull.magnitude;
            if (len < MinPull) return Vector2.zero;
            return pull / len * Mathf.Min(len / MaxPull, 1f) * MaxSpeed;
        }

        /// <summary>The pull that gives a velocity (the opposite of <see cref="VelocityFor"/>).</summary>
        public static Vector2 PullFor(Vector2 velocity)
        {
            float len = Mathf.Min(velocity.magnitude, MaxSpeed);
            return len < 1e-5f ? Vector2.zero : velocity.normalized * (len / MaxSpeed * MaxPull);
        }

        /// <summary>Putt with this pull. False when it can't (rolling, between holes, over, or too short a pull).</summary>
        public bool Putt(Vector2 pull)
        {
            if (Over || State != PuttState.Aiming || Putts >= PuttsPerHole) return false;
            var v = VelocityFor(pull);
            if (v == Vector2.zero) return false;
            Velocity = v;
            State = PuttState.Rolling;
            Putts++;
            _roll = 0f;
            return true;
        }

        float _roll;

        public PuttEvent Step(float dt)
        {
            if (Over) return PuttEvent.None;
            var events = PuttEvent.None;
            _carry += dt;
            while (_carry >= FixedStep)
            {
                _carry -= FixedStep;
                events |= Tick(FixedStep);
                if (Over) break;
            }
            return events;
        }

        PuttEvent Tick(float h)
        {
            var events = PuttEvent.None;
            Time += h;

            if (State == PuttState.Sunk || State == PuttState.Missed)
            {
                PauseLeft -= h;
                if (PauseLeft <= 0f)
                {
                    StartHole(Hole.Number + 1);
                    events |= PuttEvent.NewHole;
                }
                return events;
            }

            var ball = new BallState { Pos = Ball, Vel = Velocity, Rolling = State == PuttState.Rolling };
            bool wasSand = InSand;
            var r = Move(Hole, ref ball, Time, h, true);
            Ball = ball.Pos;
            Velocity = ball.Vel;
            InSand = ball.InSand;
            if (InSand && !wasSand && State == PuttState.Rolling) events |= PuttEvent.Sand;
            if (r.Wall) events |= PuttEvent.Wall;
            if (r.Bumper >= 0) { events |= PuttEvent.Bumper; LastBumper = r.Bumper; HitPoint = r.HitPoint; }
            if (State != PuttState.Rolling) return events;

            _roll += h;
            if (r.Water)
            {
                HitPoint = Ball;
                LostToWater = true;
                return events | PuttEvent.Splashed | Lose();
            }
            if (r.Sunk)
            {
                Ball = Hole.Cup;
                Velocity = Vector2.zero;
                State = PuttState.Sunk;
                PauseLeft = PauseTime;
                LastPoints = PuttsPerHole + 1 - Putts;
                Score += LastPoints;
                Sunk++;
                return events | PuttEvent.Sank;
            }
            if (!ball.Rolling || _roll > MaxRoll)
            {
                Velocity = Vector2.zero;
                State = PuttState.Aiming;
                events |= PuttEvent.Stopped;
                if (Putts >= PuttsPerHole)
                {
                    LostToWater = false;
                    events |= Lose();
                }
            }
            return events;
        }

        PuttEvent Lose()
        {
            State = PuttState.Missed;
            Velocity = Vector2.zero;
            PauseLeft = PauseTime;
            Lives--;
            var events = PuttEvent.Missed;
            if (Lives <= 0)
            {
                Over = true;
                events |= PuttEvent.Over;
            }
            return events;
        }

        /// <summary>For tests: play a hand-made hole from its tee, as if it had just begun.</summary>
        public void UseHoleForTests(PuttHole hole)
        {
            StartHole(hole.Number);
            Hole = hole;
            Ball = hole.Tee;
        }

        void StartHole(int number)
        {
            Hole = _next != null && _nextNumber == number ? _next.Result : Build(number, RandomFor(_seed, number));
            int next = number + 1, seed = _seed;
            _nextNumber = next;
            _next = Task.Run(() => Build(next, RandomFor(seed, next)));
            Time = 0f;
            Ball = Hole.Tee;
            Velocity = Vector2.zero;
            State = PuttState.Aiming;
            Putts = 0;
            InSand = false;
            _carry = 0f;
        }

        // --- Physics: shared by play and the solver, so a solution is exactly what play does --------

        public struct BallState
        {
            public Vector2 Pos, Vel;
            public bool Rolling, InSand;
        }

        public struct MoveResult
        {
            public bool Wall, Water, Sunk;
            public int Bumper;
            public Vector2 HitPoint;
        }

        /// <summary>One fixed step of the ball. A resting ball only gets nudged out of the way by gates.</summary>
        public static MoveResult Move(PuttHole hole, ref BallState b, float time, float h, bool withMovers)
        {
            var r = new MoveResult { Bumper = -1 };

            if (!b.Rolling)
            {
                if (withMovers)
                    foreach (var m in hole.Movers)
                    {
                        var box = m.At(time);
                        // Out of the gate's lane, to the nearer side, so it can't be pinned on a rail.
                        if (box.Distance(b.Pos) < BallRadius)
                            b.Pos.y = b.Pos.y >= box.Center.y ? box.Max.y + BallRadius : box.Min.y - BallRadius;
                    }
                b.Pos = new Vector2(Mathf.Clamp(b.Pos.x, Left + BallRadius, Right - BallRadius), Mathf.Clamp(b.Pos.y, Bottom + BallRadius, Top - BallRadius));
                return r;
            }

            b.InSand = false;
            foreach (var s in hole.Sand) if (s.Contains(b.Pos)) b.InSand = true;
            float speed = b.Vel.magnitude;
            float slowed = Mathf.Max(0f, speed - (b.InSand ? SandDrag : GrassDrag) * h);
            b.Vel = speed > 0f ? b.Vel * (slowed / speed) : Vector2.zero;
            b.Pos += b.Vel * h;

            // The course's own walls.
            if (b.Pos.x < Left + BallRadius) { b.Pos.x = Left + BallRadius; if (b.Vel.x < 0f) { b.Vel.x = -b.Vel.x * WallBounce; r.Wall = true; } }
            if (b.Pos.x > Right - BallRadius) { b.Pos.x = Right - BallRadius; if (b.Vel.x > 0f) { b.Vel.x = -b.Vel.x * WallBounce; r.Wall = true; } }
            if (b.Pos.y < Bottom + BallRadius) { b.Pos.y = Bottom + BallRadius; if (b.Vel.y < 0f) { b.Vel.y = -b.Vel.y * WallBounce; r.Wall = true; } }
            if (b.Pos.y > Top - BallRadius) { b.Pos.y = Top - BallRadius; if (b.Vel.y > 0f) { b.Vel.y = -b.Vel.y * WallBounce; r.Wall = true; } }

            foreach (var block in hole.Blocks)
                if (Bounce(block, ref b, Vector2.zero)) r.Wall = true;
            if (withMovers)
                foreach (var m in hole.Movers)
                    if (Bounce(m.At(time), ref b, new Vector2(m.VelocityAt(time), 0f))) r.Wall = true;

            for (int i = 0; i < hole.Bumpers.Count; i++)
            {
                var c = hole.Bumpers[i];
                var d = b.Pos - c;
                float dist = d.magnitude;
                if (dist >= BumperRadius + BallRadius || dist < 1e-6f) continue;
                var n = d / dist;
                b.Pos = c + n * (BumperRadius + BallRadius);
                float vn = Vector2.Dot(b.Vel, n);
                if (vn < 0f)
                {
                    b.Vel -= (1f + BumperBounce) * vn * n;
                    float s = b.Vel.magnitude;
                    if (s < BumperMinSpeed) b.Vel = b.Vel / Mathf.Max(s, 1e-4f) * BumperMinSpeed;
                    if (s > MaxSpeed) b.Vel = b.Vel / s * MaxSpeed;
                    r.Bumper = i;
                    r.HitPoint = c + n * BumperRadius;
                }
            }

            foreach (var w in hole.Water)
                if (w.Contains(b.Pos)) { r.Water = true; return r; }

            if (Vector2.Distance(b.Pos, hole.Cup) <= CupRadius && b.Vel.magnitude <= SinkSpeed)
            {
                r.Sunk = true;
                return r;
            }

            if (b.Vel.magnitude < StopSpeed)
            {
                b.Vel = Vector2.zero;
                b.Rolling = false;
            }
            return r;
        }

        static Vector2 PushOut(PuttBox box, Vector2 p)
        {
            var q = new Vector2(Mathf.Clamp(p.x, box.Min.x, box.Max.x), Mathf.Clamp(p.y, box.Min.y, box.Max.y));
            var d = p - q;
            if (d.sqrMagnitude > 1e-10f) return q + d.normalized * BallRadius;
            // The centre is inside: out the nearest side.
            float dl = p.x - box.Min.x, dr = box.Max.x - p.x, db = p.y - box.Min.y, dt = box.Max.y - p.y;
            float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
            if (m == dl) return new Vector2(box.Min.x - BallRadius, p.y);
            if (m == dr) return new Vector2(box.Max.x + BallRadius, p.y);
            if (m == db) return new Vector2(p.x, box.Min.y - BallRadius);
            return new Vector2(p.x, box.Max.y + BallRadius);
        }

        static bool Bounce(PuttBox box, ref BallState b, Vector2 boxVel)
        {
            if (box.Distance(b.Pos) >= BallRadius) return false;
            var q = new Vector2(Mathf.Clamp(b.Pos.x, box.Min.x, box.Max.x), Mathf.Clamp(b.Pos.y, box.Min.y, box.Max.y));
            var p = PushOut(box, b.Pos);
            var n = (p - q).normalized;
            if (n == Vector2.zero) n = (p - b.Pos).normalized;
            b.Pos = p;
            var rel = b.Vel - boxVel;
            float vn = Vector2.Dot(rel, n);
            if (vn >= 0f) return false;
            rel -= (1f + WallBounce) * vn * n;
            b.Vel = rel + boxVel;
            if (b.Vel.magnitude > MaxSpeed) b.Vel = b.Vel.normalized * MaxSpeed;
            return true;
        }

        /// <summary>
        /// Rolls a putt to its end without touching the game: true if it drops in the cup. Gates
        /// move on from <paramref name="startTime"/> when <paramref name="withMovers"/> is set.
        /// </summary>
        public static bool WouldSink(PuttHole hole, Vector2 from, Vector2 pull, float startTime, bool withMovers)
        {
            var b = new BallState { Pos = from, Vel = VelocityFor(pull), Rolling = true };
            if (b.Vel == Vector2.zero) return false;
            float t = startTime;
            for (float roll = 0f; roll < MaxRoll; roll += FixedStep)
            {
                t += FixedStep;
                var r = Move(hole, ref b, t, FixedStep, withMovers);
                if (r.Sunk) return true;
                if (r.Water || !b.Rolling) return false;
            }
            return false;
        }

        /// <summary>
        /// Looks for a putt from a spot that sinks it first time, trying angles nearest the line to the
        /// cup first. Null if there isn't one.
        /// </summary>
        public static Vector2? Solve(PuttHole hole, Vector2 from, float startTime, bool withMovers, float angleStep = 2f)
        {
            var toCup = hole.Cup - from;
            float a0 = Mathf.Atan2(toCup.y, toCup.x) * Mathf.Rad2Deg;
            int steps = Mathf.CeilToInt(180f / angleStep);
            for (int i = 0; i <= steps; i++)
                for (int sign = 1; sign >= -1; sign -= 2)
                {
                    if (i == 0 && sign < 0) continue;
                    float a = (a0 + sign * i * angleStep) * Mathf.Deg2Rad;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    for (float p = 0.12f; p <= 1.0001f; p += 0.03f)
                    {
                        var pull = dir * (p * MaxPull);
                        if (WouldSink(hole, from, pull, startTime, withMovers)) return pull;
                    }
                }
            return null;
        }

        /// <summary>A putt that sinks it from where the ball is now, gates and all (for tests and bots).</summary>
        public Vector2? SolveNow(float angleStep = 2f) => Solve(Hole, Ball, Time, true, angleStep);

        // --- Building holes ------------------------------------------------------------------------

        /// <summary>Each hole has its own random numbers, so a seed always gives the same holes, built in any order.</summary>
        public static System.Random RandomFor(int seed, int number) => new System.Random(unchecked(seed * 7919 + number * 104729));

        static float Range(System.Random r, float a, float b) => Mathf.Lerp(a, b, (float)r.NextDouble());

        /// <summary>How many of each thing a hole has.</summary>
        public static (int blocks, int bumpers, int sand, int water, int movers) Counts(int n) => (
            n <= 1 ? 1 : Mathf.Min(1 + n / 3, 4),
            n < 2 ? 0 : Mathf.Min(1 + (n - 2) / 3, 3),
            n < 3 ? 0 : Mathf.Min(1 + (n - 3) / 4, 2),
            n < 5 ? 0 : Mathf.Min(1 + (n - 5) / 4, 2),
            n < 7 ? 0 : Mathf.Min(1 + (n - 7) / 5, 2));

        /// <summary>Builds hole <paramref name="number"/>, trying again (with less in the way if need be) until it can be holed in one.</summary>
        public static PuttHole Build(int number, System.Random random)
        {
            var (blocks, bumpers, sand, water, movers) = Counts(number);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                // After a few tries, take things out until it works.
                int cut = attempt / 8;
                var hole = TryBuild(number, random, Mathf.Max(0, blocks - cut), Mathf.Max(0, bumpers - cut), sand, Mathf.Max(0, water - cut), movers);
                var proof = Solve(hole, hole.Tee, 0f, false, 3f);
                if (proof == null) continue;
                hole.Proof = proof.Value;
                return hole;
            }
            // Never happens in practice (the tests check), but an open green is always holeable.
            var open = TryBuild(number, random, 0, 0, 0, 0, 0);
            open.Proof = Solve(open, open.Tee, 0f, false, 1f) ?? Vector2.zero;
            return open;
        }

        static PuttHole TryBuild(int number, System.Random random, int blocks, int bumpers, int sand, int water, int movers)
        {
            var hole = new PuttHole { Number = number };
            hole.Tee = new Vector2(Range(random, -2.6f, 2.6f), Range(random, -6.0f, -5.3f));
            hole.Cup = new Vector2(Range(random, -3.2f, 3.2f), Range(random, 2.8f, 4.8f));
            var taken = new List<PuttBox>();

            bool Fits(PuttBox box, float gap)
            {
                if (box.Min.x < Left || box.Max.x > Right || box.Min.y < Bottom || box.Max.y > Top) return false;
                if (box.Distance(hole.Tee) < 1.1f || box.Distance(hole.Cup) < 0.9f) return false;
                foreach (var t in taken)
                {
                    var grown = new PuttBox(t.Center, t.Half + Vector2.one * gap);
                    if (Mathf.Abs(grown.Center.x - box.Center.x) < grown.Half.x + box.Half.x &&
                        Mathf.Abs(grown.Center.y - box.Center.y) < grown.Half.y + box.Half.y) return false;
                }
                return true;
            }

            PuttBox? Place(Func<PuttBox> make, float gap)
            {
                for (int i = 0; i < 25; i++)
                {
                    var box = make();
                    if (!Fits(box, gap)) continue;
                    taken.Add(box);
                    return box;
                }
                return null;
            }

            for (int i = 0; i < movers; i++)
            {
                var m = Place(() =>
                {
                    float y = Range(random, -3.4f, 1.6f);
                    return new PuttBox(new Vector2(0f, y), new Vector2(Right - 0.1f, 0.25f));
                }, 0.5f);
                if (m == null) continue;
                // The lane is taken whole; the gate itself is a short bar sliding along it.
                float half = Range(random, 0.7f, 1.0f);
                var lane = m.Value;
                hole.Movers.Add(new PuttMover
                {
                    Box = new PuttBox(new Vector2(0f, lane.Center.y), new Vector2(half, 0.22f)),
                    Amp = Right - half - 0.15f,
                    Speed = Range(random, 1.6f, 2.4f) + 0.04f * number,
                    Phase = Range(random, 0f, Mathf.PI * 2f),
                });
            }

            for (int i = 0; i < blocks; i++)
            {
                var b = Place(() =>
                {
                    bool across = random.NextDouble() < 0.6;
                    var half = across ? new Vector2(Range(random, 0.8f, 1.7f), 0.24f) : new Vector2(0.24f, Range(random, 0.6f, 1.3f));
                    var c = new Vector2(Range(random, Left + half.x, Right - half.x), Range(random, -4.4f, 2.6f));
                    // Some blocks lean on a side wall, making a channel.
                    if (across && random.NextDouble() < 0.4) c.x = random.NextDouble() < 0.5 ? Left + half.x : Right - half.x;
                    return new PuttBox(c, half);
                }, 0.55f);
                if (b != null) hole.Blocks.Add(b.Value);
            }

            for (int i = 0; i < water; i++)
            {
                var w = Place(() =>
                {
                    var half = new Vector2(Range(random, 0.6f, 1.3f), Range(random, 0.4f, 0.7f));
                    return new PuttBox(new Vector2(Range(random, Left + half.x, Right - half.x), Range(random, -4.0f, 3.6f)), half);
                }, 0.55f);
                if (w != null) hole.Water.Add(w.Value);
            }

            for (int i = 0; i < sand; i++)
            {
                var s = Place(() =>
                {
                    var half = new Vector2(Range(random, 0.6f, 1.2f), Range(random, 0.45f, 0.75f));
                    return new PuttBox(new Vector2(Range(random, Left + half.x, Right - half.x), Range(random, -4.2f, 4.0f)), half);
                }, 0.3f);
                if (s != null) hole.Sand.Add(s.Value);
            }

            for (int i = 0; i < bumpers; i++)
            {
                var b = Place(() =>
                {
                    var c = new Vector2(Range(random, Left + 0.6f, Right - 0.6f), Range(random, -4.2f, 4.2f));
                    return new PuttBox(c, Vector2.one * BumperRadius);
                }, 0.55f);
                if (b != null) hole.Bumpers.Add(b.Value.Center);
            }
            return hole;
        }
    }
}
