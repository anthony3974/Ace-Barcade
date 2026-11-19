using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.LilyLeap
{
    /// <summary>What happened during one <see cref="LeapSim.Step"/> (or <see cref="LeapSim.Jump"/>).</summary>
    [Flags]
    public enum LeapEvent
    {
        None = 0,
        Jumped = 1,
        Landed = 2,
        Gem = 4,
        SinkStarted = 8,
        Sank = 16,
        Splashed = 32,
    }

    public enum PadKind { Start, Plain, Drifting, Sinking }

    /// <summary>A lily pad, seen from above. Drifting pads slide from side to side; sinking pads go under soon after you land.</summary>
    public sealed class LeapPad
    {
        public int Index { get; }
        public PadKind Kind { get; }
        public float BaseX { get; }
        public float Y { get; }
        public float Radius { get; }
        /// <summary>How far a drifting pad slides either side of <see cref="BaseX"/> (0 for the others).</summary>
        public float Amp { get; }
        public float Speed { get; }
        /// <summary>One of the chain of pads that always leads up (extras sit beside it).</summary>
        public bool IsPath { get; }
        /// <summary>How many path pads up the chain this one is; the score counts the highest reached.</summary>
        public int Row { get; }
        public bool HasGem { get; }
        public bool GemTaken { get; internal set; }
        /// <summary>When the frog landed on a sinking pad (negative until then).</summary>
        public float SinkStart { get; internal set; } = -1f;
        public bool Sunk { get; internal set; }
        public float X { get; internal set; }

        readonly float _phase;

        internal LeapPad(int index, PadKind kind, float x, float y, float radius, float amp, float speed, float phase, bool path, int row, bool gem)
        {
            Index = index;
            Kind = kind;
            BaseX = x;
            Y = y;
            Radius = radius;
            Amp = amp;
            Speed = speed;
            _phase = phase;
            IsPath = path;
            Row = row;
            HasGem = gem;
            X = XAt(0f);
        }

        /// <summary>Where the pad's centre is at a time in the run (drifting pads slide smoothly).</summary>
        public float XAt(float time) => Kind == PadKind.Drifting ? BaseX + Amp * Mathf.Sin(_phase + Speed / Amp * time) : BaseX;

        public Vector2 At(float time) => new Vector2(XAt(time), Y);

        /// <summary>0 while a sinking pad is safe, rising to 1 as it goes under.</summary>
        public float Sinking(float time) => SinkStart < 0f ? 0f : Mathf.Clamp01((time - SinkStart) / LeapSim.SinkDelay);
    }

    /// <summary>
    /// The rules of Lily Leap, in plain C# so they can be tested. A frog sits on a lily pad on a
    /// pond seen from above. Pull back (drag away from where you want to go) to aim: the jump goes
    /// the opposite way to the pull, as far as the pull is long, up to <see cref="MaxJump"/>. Let go
    /// to leap. Landing on a pad is safe; landing in the water ends the run. The view climbs with
    /// the frog and never comes back down.
    ///
    /// Each new path pad reached scores 1 (the highest <see cref="LeapPad.Row"/>), teal gems 5.
    /// Higher up, the gaps grow and the pads shrink, purple pads drift from side to side, and tan
    /// pads sink <see cref="SinkDelay"/> seconds after the frog lands on them.
    /// <b>Fair by design:</b> every path pad is within <see cref="Reach"/> of the one before, wherever
    /// both are in their drift, and no two pads ever overlap.
    /// </summary>
    public sealed class LeapSim
    {
        // The pull and the jump.
        /// <summary>A pull this long (world units of finger travel) is a full-power jump.</summary>
        public const float MaxPull = 2.6f;
        /// <summary>Pulls shorter than this are let go without jumping (a tap, or a change of mind).</summary>
        public const float MinPull = 0.3f;
        public const float MaxJump = 6.4f;
        /// <summary>The longest gap the path ever asks for, leaving room for an imperfect pull.</summary>
        public const float Reach = MaxJump * 0.82f;
        /// <summary>How far past a pad's edge the frog's middle can land and still hold on.</summary>
        public const float LandSlack = 0.12f;
        public const float SinkDelay = 1.3f;
        public const int GemPoints = 5;

        // The pond: 9 units wide.
        public const float HalfWidth = Style.WorldWidth / 2f;
        /// <summary>Pads keep this far in from the screen's sides.</summary>
        public const float Margin = 0.25f;
        public const float StartY = -5.2f;
        public const float StartRadius = 1.05f;
        /// <summary>The view climbs to keep the frog's pad at least this far below the middle of the screen.</summary>
        public const float FrogBelow = 3.6f;
        public const float CamSpeed = 6f;
        const float GenerateAhead = 13f;
        const float RemoveBelow = 11f;

        readonly System.Random _random;
        readonly List<LeapPad> _pads = new List<LeapPad>();
        LeapPad _lastPath;
        int _nextIndex, _pathRow, _sinkRun;
        float _generatedTo;

        public IReadOnlyList<LeapPad> Pads => _pads;
        public float Time { get; private set; }

        // The frog.
        public Vector2 Frog { get; private set; }
        public LeapPad OnPad { get; private set; }
        /// <summary>Where on its pad the frog sits, from the pad's centre (it rides drifting pads).</summary>
        public Vector2 Offset { get; private set; }
        public bool Flying { get; private set; }
        public Vector2 From { get; private set; }
        public Vector2 To { get; private set; }
        public float FlightTime { get; private set; }
        public float FlightAge { get; private set; }
        /// <summary>0 to 1 through the current leap.</summary>
        public float FlightT => Flying ? Mathf.Clamp01(FlightAge / FlightTime) : 0f;
        public int Jumps { get; private set; }

        public float CamY { get; private set; }
        public float CamTarget { get; private set; }

        public int BestRow { get; private set; }
        public int Gems { get; private set; }
        public int Score => BestRow + Gems * GemPoints;
        public bool Over { get; private set; }
        public Vector2 SplashPoint { get; private set; }
        /// <summary>The pad the last event happened on (landed, gem, sink).</summary>
        public LeapPad LastPad { get; private set; }

        public LeapSim(int seed)
        {
            _random = new System.Random(seed);
            var start = Add(PadKind.Start, 0f, StartY, StartRadius, 0f, 0f, 0f, true, 0, false);
            _lastPath = start;
            OnPad = start;
            Frog = start.At(0f);
            Generate();
        }

        /// <summary>How hard the pond is at a row, 0 at the start to 1 from row 45 up.</summary>
        public static float Difficulty(int row) => Mathf.Clamp01(row / 45f);

        /// <summary>The jump a pull would make, from where the frog is (zero for a pull too short to jump).</summary>
        public static Vector2 JumpFor(Vector2 pull)
        {
            float len = pull.magnitude;
            if (len < MinPull) return Vector2.zero;
            return pull / len * Mathf.Min(len / MaxPull, 1f) * MaxJump;
        }

        /// <summary>The pull that makes a jump (the opposite of <see cref="JumpFor"/>, for tests and bots).</summary>
        public static Vector2 PullFor(Vector2 jump)
        {
            float len = Mathf.Min(jump.magnitude, MaxJump);
            return len < 1e-5f ? Vector2.zero : jump.normalized * (len / MaxJump * MaxPull);
        }

        /// <summary>How long a leap of this length is in the air.</summary>
        public static float FlightFor(float distance) => 0.34f + 0.26f * Mathf.Clamp01(distance / MaxJump);

        /// <summary>Where a pull would land the frog, in the world.</summary>
        public Vector2 AimPoint(Vector2 pull) => Frog + JumpFor(pull);

        /// <summary>Leap with this pull. False when it can't (in the air, the run's over, or too short a pull).</summary>
        public bool Jump(Vector2 pull)
        {
            if (Over || Flying) return false;
            var jump = JumpFor(pull);
            if (jump == Vector2.zero) return false;
            Flying = true;
            From = Frog;
            To = Frog + jump;
            FlightTime = FlightFor(jump.magnitude);
            FlightAge = 0f;
            OnPad = null;
            Jumps++;
            return true;
        }

        public LeapEvent Step(float dt)
        {
            if (Over) return LeapEvent.None;
            var events = LeapEvent.None;
            Time += dt;
            foreach (var pad in _pads) pad.X = pad.XAt(Time);

            // Sinking pads go under; the frog goes with one if it's still aboard.
            foreach (var pad in _pads)
            {
                if (pad.Sunk || pad.SinkStart < 0f || Time - pad.SinkStart < SinkDelay) continue;
                pad.Sunk = true;
                LastPad = pad;
                events |= LeapEvent.Sank;
                if (OnPad == pad)
                {
                    OnPad = null;
                    Splash(Frog);
                    events |= LeapEvent.Splashed;
                    return events;
                }
            }

            if (Flying)
            {
                FlightAge += dt;
                Frog = Vector2.Lerp(From, To, FlightT);
                if (FlightAge >= FlightTime) events |= Land();
            }
            else if (OnPad != null)
            {
                Frog = OnPad.At(Time) + Offset;
            }

            CamY = Mathf.MoveTowards(CamY, CamTarget, Mathf.Max(CamSpeed, (CamTarget - CamY) * 4f) * dt);
            Generate();
            Prune();
            return events;
        }

        LeapEvent Land()
        {
            Flying = false;
            Frog = To;
            LeapPad best = null;
            float bestDistance = float.MaxValue;
            foreach (var pad in _pads)
            {
                if (pad.Sunk) continue;
                float d = Vector2.Distance(To, pad.At(Time));
                if (d <= pad.Radius + LandSlack && d < bestDistance) { best = pad; bestDistance = d; }
            }
            if (best == null)
            {
                Splash(To);
                return LeapEvent.Splashed;
            }

            var events = LeapEvent.Landed;
            OnPad = best;
            LastPad = best;
            // Sit where it landed, pulled in a little so the frog is wholly on the pad.
            var offset = To - best.At(Time);
            float inside = Mathf.Max(0f, best.Radius - 0.3f);
            Offset = offset.magnitude > inside ? offset.normalized * inside : offset;
            Frog = best.At(Time) + Offset;

            if (best.Row > BestRow) BestRow = best.Row;
            if (best.HasGem && !best.GemTaken)
            {
                best.GemTaken = true;
                Gems++;
                events |= LeapEvent.Gem;
            }
            if (best.Kind == PadKind.Sinking && best.SinkStart < 0f)
            {
                best.SinkStart = Time;
                events |= LeapEvent.SinkStarted;
            }
            CamTarget = Mathf.Max(CamTarget, best.Y + FrogBelow);
            return events;
        }

        void Splash(Vector2 at)
        {
            Over = true;
            Flying = false;
            SplashPoint = at;
            Frog = at;
        }

        // --- The pond ---------------------------------------------------------------------------

        LeapPad Add(PadKind kind, float x, float y, float r, float amp, float speed, float phase, bool path, int row, bool gem)
        {
            var pad = new LeapPad(_nextIndex++, kind, x, y, r, amp, speed, phase, path, row, gem);
            _pads.Add(pad);
            return pad;
        }

        float Range(float a, float b) => Mathf.Lerp(a, b, (float)_random.NextDouble());
        bool Chance(float p) => _random.NextDouble() < p;

        /// <summary>The widest a pad's centre can sit from the middle, keeping clear of the sides.</summary>
        public static float MaxX(float radius, float amp) => HalfWidth - Margin - radius - amp;

        void Generate()
        {
            while (_generatedTo < CamTarget + GenerateAhead) AddRow();
        }

        void AddRow()
        {
            var prev = _lastPath;
            int row = _pathRow + 1;
            float d = Difficulty(row);

            float r = Range(0.85f, 0.95f) - 0.25f * d;
            float dy = Mathf.Max(Range(1.9f, 2.4f) + Range(0f, 1.6f) * d, prev.Radius + r + 0.45f);
            var kind = PadKind.Plain;
            if (row >= 6 && Chance(Mathf.Lerp(0.25f, 0.5f, d))) kind = PadKind.Drifting;
            else if (row >= 12 && _sinkRun < 2 && Chance(Mathf.Lerp(0.2f, 0.4f, d))) kind = PadKind.Sinking;
            _sinkRun = kind == PadKind.Sinking ? _sinkRun + 1 : 0;

            float amp = 0f, speed = 0f, phase = 0f;
            if (kind == PadKind.Drifting)
            {
                amp = Range(0.7f, 1.0f) + 0.7f * d;
                speed = Range(0.9f, 1.2f) + 1.1f * d;
                phase = Range(0f, Mathf.PI * 2f);
            }

            // Sideways room: wherever both pads are in their drift, the gap is within reach. If a
            // drift would leave too little room, the pad stays put instead.
            float reachSide = Mathf.Sqrt(Mathf.Max(0f, Reach * Reach - dy * dy));
            float xmax = MaxX(r, amp);
            float side = reachSide - prev.Amp - amp;
            float lo = Mathf.Max(-xmax, prev.BaseX - side), hi = Mathf.Min(xmax, prev.BaseX + side);
            if (kind == PadKind.Drifting && (side < 1f || lo > hi))
            {
                kind = PadKind.Plain;
                amp = speed = 0f;
                xmax = MaxX(r, 0f);
                side = reachSide - prev.Amp;
                lo = Mathf.Max(-xmax, prev.BaseX - side);
                hi = Mathf.Min(xmax, prev.BaseX + side);
            }
            // Prefer a real sideways hop over sitting straight above.
            float x = Range(lo, hi);
            if (Mathf.Abs(x - prev.BaseX) < 0.8f && hi - lo > 2f) x = Range(lo, hi);

            bool gem = Chance(0.08f);
            // The path comes first: an extra from the row below that would touch it goes.
            for (int i = _pads.Count - 1; i >= 0; i--)
            {
                var p = _pads[i];
                if (!p.IsPath && p != OnPad && Overlaps(p.BaseX, p.Y, p.Radius, p.Amp, x, prev.Y + dy, r, amp)) _pads.RemoveAt(i);
            }
            var pad = Add(kind, x, prev.Y + dy, r, amp, speed, phase, true, row, gem);
            _lastPath = pad;
            _pathRow = row;
            _generatedTo = pad.Y;

            // Sometimes an extra pad beside the path: a place to rest, often with a gem.
            if (Chance(Mathf.Lerp(0.6f, 0.35f, d)))
            {
                float er = Range(0.7f, 0.85f) - 0.15f * d;
                var ekind = prev.Row >= 12 && Chance(0.35f) ? PadKind.Sinking : PadKind.Plain;
                for (int tries = 0; tries < 6; tries++)
                {
                    float ex = Range(-MaxX(er, 0f), MaxX(er, 0f));
                    float ey = prev.Y + dy * Range(0.2f, 0.8f);
                    if (!Clear(ex, ey, er, 0f)) continue;
                    Add(ekind, ex, ey, er, 0f, 0f, 0f, false, prev.Row, Chance(0.4f));
                    break;
                }
            }
        }

        /// <summary>True if a pad here would stay clear of every other pad, drift and all.</summary>
        public bool Clear(float x, float y, float r, float amp)
        {
            foreach (var p in _pads)
                if (!p.Sunk && Overlaps(p.BaseX, p.Y, p.Radius, p.Amp, x, y, r, amp)) return false;
            return true;
        }

        /// <summary>The gap kept between any two pads, wherever they are in their drift.</summary>
        public const float PadGap = 0.3f;

        /// <summary>True if two pads (centre, radius, drift) could ever come closer than <see cref="PadGap"/>.</summary>
        public static bool Overlaps(float x1, float y1, float r1, float amp1, float x2, float y2, float r2, float amp2)
        {
            float nx = Mathf.Max(0f, Mathf.Abs(x1 - x2) - amp1 - amp2);
            float dy = y1 - y2;
            return Mathf.Sqrt(nx * nx + dy * dy) < r1 + r2 + PadGap;
        }

        void Prune()
        {
            for (int i = _pads.Count - 1; i >= 0; i--)
            {
                var p = _pads[i];
                if (p == OnPad || p == _lastPath) continue;
                if (p.Y < CamY - RemoveBelow) _pads.RemoveAt(i);
            }
        }

        // --- For tests -----------------------------------------------------------------------------

        /// <summary>The next path pad up from the frog's pad (null if none is made yet).</summary>
        public LeapPad NextPathPad()
        {
            var from = OnPad ?? LastPad;
            int row = from != null ? from.Row : BestRow;
            LeapPad best = null;
            foreach (var p in _pads)
                if (p.IsPath && !p.Sunk && p.Row > row && (best == null || p.Row < best.Row)) best = p;
            return best;
        }

        /// <summary>
        /// The pull that lands the frog on the middle of a pad, leading a drifting one to where it
        /// will be when the frog comes down.
        /// </summary>
        public Vector2 PullToHit(LeapPad pad)
        {
            var target = pad.At(Time);
            for (int i = 0; i < 6; i++)
            {
                float t = FlightFor(Mathf.Min((target - Frog).magnitude, MaxJump));
                target = pad.At(Time + t);
            }
            return PullFor(target - Frog);
        }
    }
}
