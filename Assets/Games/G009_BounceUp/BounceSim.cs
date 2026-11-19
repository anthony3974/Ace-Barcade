using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.BounceUp
{
    /// <summary>What happened during one <see cref="BounceSim.Step"/>.</summary>
    [Flags]
    public enum BounceEvent
    {
        None = 0,
        Bounced = 1,
        Sprang = 2,
        Crumbled = 4,
        Gem = 8,
        Crashed = 16,
        Fell = 32,
        Spawned = 64,
    }

    public enum PadKind { Ground, Plain, Moving, Crumbly }

    /// <summary>A platform. Every platform is one-way: you rise through it and land on its top.</summary>
    public sealed class BouncePad
    {
        public int Index { get; }
        public PadKind Kind { get; }
        public float BaseX { get; }
        public float Y { get; }
        /// <summary>How far a moving pad swings either side of <see cref="BaseX"/> (0 for the others).</summary>
        public float Amp { get; }
        public float Speed { get; }
        public bool HasSpring { get; }
        /// <summary>One of the chain of pads that always leads up (extras are on the side).</summary>
        public bool IsPath { get; }
        public bool Broken { get; internal set; }
        public float X { get; internal set; }

        readonly float _phase;

        public float Top => Y + BounceSim.PadHalfHeight;

        internal BouncePad(int index, PadKind kind, float x, float y, float amp, float speed, float phase, bool spring, bool path)
        {
            Index = index;
            Kind = kind;
            BaseX = x;
            Y = y;
            Amp = amp;
            Speed = speed;
            _phase = phase;
            HasSpring = spring;
            IsPath = path;
            X = XAt(0f);
        }

        /// <summary>Where the pad's centre is at a time in the run (moving pads swing smoothly).</summary>
        public float XAt(float time) => Kind == PadKind.Moving ? BaseX + Amp * Mathf.Sin(_phase + Speed / Amp * time) : BaseX;
    }

    /// <summary>A red spiky burr hanging in the air: touching it ends the run.</summary>
    public sealed class BounceBurr
    {
        public float X { get; }
        public float Y { get; }
        internal BounceBurr(float x, float y) { X = x; Y = y; }
    }

    public sealed class BounceGem
    {
        public float X { get; }
        public float Y { get; }
        public bool Taken { get; internal set; }
        internal BounceGem(float x, float y) { X = x; Y = y; }
    }

    /// <summary>
    /// The rules of Bounce Up, in plain C# so they can be tested. A bouncer springs off every pad
    /// it lands on, always to the same height; holding the left or right half of the screen steers
    /// it, and the sides wrap round. The view climbs with it and never comes back down: falling off
    /// the bottom, or touching a red burr, ends the run. Height climbed scores 1 a unit, teal gems 5.
    ///
    /// Pads come in a chain that always leads up (the path), with extras beside it. Higher up, the
    /// gaps grow, path pads start to move, extras start to crumble after one bounce, and burrs appear.
    /// <b>Fair by design:</b> each path pad is at most <see cref="MaxShift"/> sideways from the last,
    /// counting the whole swing of moving pads, and at most <see cref="MaxGap"/> above it, well under
    /// a bounce. A burr never hangs over a pad within a bounce of it, nor across the way between two
    /// pads that lead up (see <see cref="BurrFits"/>), so bouncing straight up, or heading for the
    /// next pad up, never meets one.
    /// </summary>
    public sealed class BounceSim
    {
        // The bouncer.
        public const float BodyRadius = 0.36f;
        /// <summary>From the bouncer's centre down to its feet, which land on pads.</summary>
        public const float FootY = 0.46f;
        public const float Gravity = 22f;
        public const float BounceSpeed = 11.5f;
        public const float SpringSpeed = 18f;
        public const float MaxFall = 16f;
        public const float MaxSide = 7f;
        public const float SideAccel = 34f;
        public const float SideDecel = 26f;

        // The space: 9 units wide, wrapping round.
        public const float HalfWidth = Style.WorldWidth / 2f;
        public const float GroundTop = -6.4f;
        public const float StartY = GroundTop + FootY;
        /// <summary>The view climbs to keep the bouncer at or below this height on the screen.</summary>
        public const float FollowY = 0f;
        /// <summary>Below this far under the view's centre, the bouncer has fallen off the screen.</summary>
        public const float FallBelow = 8.6f;
        const float GenerateAhead = 12f;
        const float RemoveBelow = 10f;

        // Pads: 168 x 48 px inside the outline.
        public const float PadHalfWidth = 0.7f;
        public const float PadHalfHeight = 0.2f;
        /// <summary>How far past a pad's end the feet can still land on it.</summary>
        public const float FootReach = 0.3f;
        public const float XLimit = HalfWidth - PadHalfWidth;
        public const float SpringReach = 0.45f;

        // The climb gets harder up to this height.
        public const float Hardest = 300f;
        public const float MaxShift = 3f;
        public const float MaxGap = 2.4f;
        public const float MovingFrom = 40f, CrumblyFrom = 40f, BurrFrom = 60f;
        public const int SpringAfter = 6;
        public const float SpringChance = 0.07f;
        public const int GemEvery = 8;
        public const float GemHover = 1.0f;
        /// <summary>The gap above a pad with a gem over it is at least this, so the next pad clears the gem.</summary>
        public const float GemGapMin = PadHalfHeight + GemHover + GemRadius + 0.1f + PadHalfHeight;
        public const float GemRadius = 0.3f;

        // Burrs.
        public const float BurrRadius = 0.32f;
        /// <summary>The gap a burr keeps from anywhere the bouncer is sure to go (its size, the bouncer's, and a margin).</summary>
        public const float BurrClear = BurrRadius + BodyRadius + 0.4f;
        /// <summary>The most burrs tried in one gap before giving up on it.</summary>
        const int BurrTries = 12;

        public const int GemPoints = 5;

        /// <summary>How high a bounce goes.</summary>
        public static float Apex(float speed) => speed * speed / (2f * Gravity);

        /// <summary>Somewhere the bouncer is sure to go: from one pad up to the next, across this span of x.</summary>
        public readonly struct Route
        {
            public readonly float Bottom, Top, Center, Half;
            public Route(float bottom, float top, float center, float half) { Bottom = bottom; Top = top; Center = center; Half = half; }
        }

        readonly System.Random _random;
        readonly List<BouncePad> _pads = new List<BouncePad>();
        readonly List<BounceBurr> _burrs = new List<BounceBurr>();
        readonly List<BounceGem> _gems = new List<BounceGem>();
        readonly List<Route> _routes = new List<Route>();
        BouncePad _lastPath;
        int _padCount, _pathCount;
        bool _gemOnLast;
        int _steer;

        public IReadOnlyList<BouncePad> Pads => _pads;
        public IReadOnlyList<BounceBurr> Burrs => _burrs;
        public IReadOnlyList<BounceGem> GemList => _gems;
        public IReadOnlyList<Route> Routes => _routes;

        public float X { get; private set; }
        public float Y { get; private set; } = StartY;
        public float Vx { get; private set; }
        public float Vy { get; private set; }
        public float Time { get; private set; }
        /// <summary>The height the view is centred on. It only ever goes up.</summary>
        public float CamY { get; private set; }
        public float MaxY { get; private set; } = StartY;
        public BouncePad LastPad { get; private set; }
        public int Gems { get; private set; }
        public int Score { get; private set; }
        public bool Crashed { get; private set; }
        public bool Fell { get; private set; }
        public bool Over => Crashed || Fell;
        public Vector2 CrashPoint { get; private set; }
        public Vector2 GemPoint { get; private set; }
        /// <summary>How many path pads have been made so far.</summary>
        public int PathCount => _pathCount;

        public BounceSim(int seed)
        {
            _random = new System.Random(seed);
            var ground = new BouncePad(_padCount++, PadKind.Ground, 0f, GroundTop - PadHalfHeight, 0f, 0f, 0f, false, true);
            _pads.Add(ground);
            _lastPath = ground;
            LastPad = ground;
            Generate(CamY + GenerateAhead);
        }

        /// <summary>For tests: make the climb up to this height ahead of time.</summary>
        public void PlanAhead(float upTo) => Generate(upTo);

        /// <summary>For tests: put the bouncer somewhere, moving like this. The view follows it up.</summary>
        public void PlaceForTests(float x, float y, float vx, float vy)
        {
            X = Wrap(x);
            Y = y;
            Vx = vx;
            Vy = vy;
            CamY = Mathf.Max(CamY, Y - FollowY);
            Generate(CamY + GenerateAhead);
        }

        /// <summary>-1 to steer left, 1 right, 0 to let go.</summary>
        public void Steer(int direction) => _steer = Math.Sign(direction);

        /// <summary>Points for the height climbed: 1 a unit.</summary>
        public int HeightPoints => Mathf.FloorToInt(MaxY - StartY);

        /// <summary>0 at the start, rising to 1 at <see cref="Hardest"/>.</summary>
        public static float Difficulty(float height) => Mathf.Clamp01(height / Hardest);

        public static float GapMin(float d) => Mathf.Lerp(1.4f, 1.7f, d);
        public static float GapMax(float d) => Mathf.Lerp(1.9f, MaxGap, d);

        /// <summary>A position wrapped into -4.5..4.5.</summary>
        public static float Wrap(float x) => Mathf.Repeat(x + HalfWidth, Style.WorldWidth) - HalfWidth;

        /// <summary>The shortest way from one x to another, round the wrap.</summary>
        public static float WrapDelta(float d) => Mathf.Repeat(d + HalfWidth, Style.WorldWidth) - HalfWidth;

        public static float WrapDistance(float a, float b) => Mathf.Abs(WrapDelta(b - a));

        public BounceEvent Step(float dt)
        {
            if (Over) return BounceEvent.None;
            var events = BounceEvent.None;
            Time += dt;
            foreach (var p in _pads) p.X = p.XAt(Time);

            float target = _steer * MaxSide;
            float rate = _steer != 0 && (Vx == 0f || Mathf.Sign(Vx) == _steer) ? SideAccel : SideDecel + (_steer != 0 ? SideAccel : 0f);
            Vx = Mathf.MoveTowards(Vx, target, rate * dt);
            X = Wrap(X + Vx * dt);

            float before = Y - FootY;
            Vy = Mathf.Max(-MaxFall, Vy - Gravity * dt);
            Y += Vy * dt;
            if (Vy < 0f)
            {
                float feet = Y - FootY;
                BouncePad landed = null;
                foreach (var p in _pads)
                {
                    if (p.Broken || before < p.Top - 1e-4f || feet > p.Top) continue;
                    if (p.Kind != PadKind.Ground && WrapDistance(X, p.X) > PadHalfWidth + FootReach) continue;
                    if (landed == null || p.Top > landed.Top) landed = p;
                }
                if (landed != null) events |= Land(landed);
            }

            MaxY = Mathf.Max(MaxY, Y);
            CamY = Mathf.Max(CamY, Y - FollowY);
            if (Generate(CamY + GenerateAhead)) events |= BounceEvent.Spawned;
            float floor = CamY - RemoveBelow;
            _pads.RemoveAll(p => p.Top < floor && p != LastPad);
            _burrs.RemoveAll(b => b.Y < floor);
            _gems.RemoveAll(g => g.Y < floor);
            _routes.RemoveAll(r => r.Top < floor - 10f);

            var me = new Vector2(X, Y);
            foreach (var g in _gems)
            {
                if (g.Taken || Distance(me, g.X, g.Y) >= BodyRadius + GemRadius) continue;
                g.Taken = true;
                Gems++;
                GemPoint = new Vector2(g.X, g.Y);
                events |= BounceEvent.Gem;
            }
            Score = HeightPoints + Gems * GemPoints;

            foreach (var b in _burrs)
            {
                if (Distance(me, b.X, b.Y) >= BodyRadius + BurrRadius) continue;
                Crashed = true;
                var toward = new Vector2(WrapDelta(b.X - X), b.Y - Y).normalized;
                CrashPoint = me + toward * BodyRadius;
                return events | BounceEvent.Crashed;
            }

            if (Y < CamY - FallBelow)
            {
                Fell = true;
                CrashPoint = new Vector2(X, CamY - Style.WorldHeight / 2f);
                events |= BounceEvent.Fell;
            }
            return events;
        }

        static float Distance(Vector2 me, float x, float y) => new Vector2(WrapDelta(x - me.x), y - me.y).magnitude;

        BounceEvent Land(BouncePad p)
        {
            Y = p.Top + FootY;
            LastPad = p;
            if (p.HasSpring && WrapDistance(X, p.X) <= SpringReach)
            {
                Vy = SpringSpeed;
                return BounceEvent.Sprang;
            }
            Vy = BounceSpeed;
            if (p.Kind == PadKind.Crumbly)
            {
                p.Broken = true;
                return BounceEvent.Bounced | BounceEvent.Crumbled;
            }
            return BounceEvent.Bounced;
        }

        // --- Making the climb ------------------------------------------------------------------

        bool Generate(float upTo)
        {
            bool any = false;
            while (_lastPath.Y < upTo)
            {
                AddGap();
                any = true;
            }
            return any;
        }

        float Next() => (float)_random.NextDouble();

        /// <summary>The next path pad above the last one, with what goes in the gap between them.</summary>
        void AddGap()
        {
            var prev = _lastPath;
            float height = prev.Top - GroundTop;
            float d = Difficulty(height);
            int index = _pathCount++;
            float dy = Mathf.Lerp(_gemOnLast ? Mathf.Max(GapMin(d), GemGapMin) : GapMin(d), GapMax(d), Next());
            float y = prev.Y + dy;

            bool moving = height >= MovingFrom && Next() < Mathf.Lerp(0.15f, 0.4f, d);
            float amp = moving ? Mathf.Lerp(0.6f, 1.2f, Next()) : 0f;
            float speed = moving ? Mathf.Lerp(0.8f, 1.4f, d) * Mathf.Lerp(0.9f, 1.2f, Next()) : 0f;
            float phase = moving ? Next() * Mathf.PI * 2f : 0f;

            // Sideways, within reach of the last path pad, counting both pads' swing.
            float x = prev.BaseX;
            float limit = XLimit - amp;
            for (int i = 0; i < 40; i++)
            {
                float c = Mathf.Lerp(-limit, limit, Next());
                if (WrapDistance(c, prev.BaseX) + amp + prev.Amp <= MaxShift) { x = c; break; }
            }
            x = Mathf.Clamp(x, -limit, limit);

            bool spring = !moving && index >= SpringAfter && Next() < SpringChance;
            var next = new BouncePad(_padCount++, moving ? PadKind.Moving : PadKind.Plain, x, y, amp, speed, phase, spring, true);
            _pads.Add(next);
            _lastPath = next;
            _routes.Add(RouteBetween(prev, next));

            _gemOnLast = index % GemEvery == GemEvery - 1;
            if (_gemOnLast)
                _gems.Add(new BounceGem(next.BaseX, next.Top + GemHover));

            // An extra pad beside the path, now and then: more of them low down, and crumbly higher up.
            if (dy >= 1.2f && Next() < Mathf.Lerp(0.45f, 0.2f, d))
            {
                bool crumbly = height >= CrumblyFrom && Next() < Mathf.Lerp(0.3f, 0.7f, d);
                float ey = prev.Y + dy * Mathf.Lerp(0.35f, 0.65f, Next());
                for (int i = 0; i < 20; i++)
                {
                    float ex = Mathf.Lerp(-XLimit, XLimit, Next());
                    if (WrapDistance(ex, prev.BaseX) < prev.Amp + 1.6f || WrapDistance(ex, next.BaseX) < next.Amp + 1.6f) continue;
                    if (WrapDistance(ex, next.BaseX) + next.Amp > MaxShift + 0.6f) continue;
                    var extra = new BouncePad(_padCount++, crumbly ? PadKind.Crumbly : PadKind.Plain, ex, ey, 0f, 0f, 0f, false, false);
                    _pads.Add(extra);
                    _routes.Add(RouteBetween(extra, next));
                    break;
                }
            }

            // A burr in the gap, now and then, higher up.
            if (height >= BurrFrom && dy >= 1.5f && Next() < Mathf.Lerp(0.2f, 0.45f, d))
            {
                for (int i = 0; i < BurrTries; i++)
                {
                    var spot = new Vector2(Mathf.Lerp(-HalfWidth, HalfWidth, Next()), Mathf.Lerp(prev.Y + 0.5f, y - 0.6f, Next()));
                    if (!BurrFits(spot)) continue;
                    _burrs.Add(new BounceBurr(spot.x, spot.y));
                    break;
                }
            }
        }

        /// <summary>The way up from one pad to another: the span of x between them, the short way round.</summary>
        static Route RouteBetween(BouncePad a, BouncePad b)
        {
            float delta = WrapDelta(b.BaseX - a.BaseX);
            float reach = PadHalfWidth + FootReach;
            float lo = Mathf.Min(a.BaseX - a.Amp, a.BaseX + delta - b.Amp) - reach;
            float hi = Mathf.Max(a.BaseX + a.Amp, a.BaseX + delta + b.Amp) + reach;
            return new Route(a.Top, a.Top + Apex(a.HasSpring ? SpringSpeed : BounceSpeed) + FootY + BodyRadius, (lo + hi) / 2f, (hi - lo) / 2f);
        }

        /// <summary>
        /// Whether a burr can hang here: clear of the column above every pad it's within a bounce of,
        /// clear of every route up that passes its height, and not crowding any pad.
        /// </summary>
        public bool BurrFits(Vector2 spot)
        {
            const float reach = PadHalfWidth + FootReach;
            foreach (var p in _pads)
            {
                float apex = p.Top + Apex(p.HasSpring ? SpringSpeed : BounceSpeed) + FootY + BodyRadius;
                if (spot.y + BurrClear >= p.Top && spot.y - BurrClear <= apex)
                {
                    if (p.Kind == PadKind.Ground) return false;
                    if (WrapDistance(spot.x, p.BaseX) < p.Amp + reach + BurrClear) return false;
                }
                if (Mathf.Abs(spot.y - p.Y) < 0.8f && WrapDistance(spot.x, p.BaseX) < p.Amp + PadHalfWidth + 0.5f) return false;
            }
            foreach (var r in _routes)
            {
                if (spot.y + BurrClear < r.Bottom || spot.y - BurrClear > r.Top) continue;
                if (WrapDistance(spot.x, r.Center) < r.Half + BurrClear) return false;
            }
            return true;
        }
    }
}
