using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.CaveGlide
{
    /// <summary>What happened during one <see cref="CaveSim.Step"/>.</summary>
    [Flags]
    public enum CaveEvent
    {
        None = 0,
        Bumped = 1,
        Gem = 2,
        Crashed = 4,
    }

    /// <summary>One slice of the cave, every <see cref="CaveSim.SampleStep"/> units along it.</summary>
    public sealed class CaveSample
    {
        public int Index { get; }
        public float X { get; }
        public float Top { get; }
        public float Bottom { get; }

        /// <summary>The middle of the safe way through, which the cave and its crystals are built around.</summary>
        public float Path { get; }

        internal CaveSample(int index, float x, float top, float bottom, float path)
        {
            Index = index;
            X = x;
            Top = top;
            Bottom = bottom;
            Path = path;
        }
    }

    /// <summary>A red crystal growing from the ceiling or the floor. Its point is <see cref="Depth"/> from the rock.</summary>
    public sealed class Crystal
    {
        public int Index { get; }
        public float X { get; }
        public bool Ceiling { get; }
        public float Depth { get; }

        /// <summary>Where the rock is under (or over) it.</summary>
        public float BaseY { get; }

        public float TipY => Ceiling ? BaseY - Depth : BaseY + Depth;

        public Crystal(int index, float x, bool ceiling, float depth, float baseY)
        {
            Index = index;
            X = x;
            Ceiling = ceiling;
            Depth = depth;
            BaseY = baseY;
        }
    }

    public sealed class CaveGem
    {
        public float X { get; }
        public float Y { get; }
        public bool Taken { get; internal set; }

        internal CaveGem(float x, float y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// The rules of Cave Glide, in plain C# so they can be tested. A glider flies through a cave
    /// that scrolls past from the right. Holding lifts it; letting go lets it sink. The rock walls
    /// are safe to bump along, but the red crystals growing from them end the run. Every 2 units
    /// flown scores 1 and teal gems along the way score 5. The cave gets faster and narrower as
    /// it goes, up to a cap, and more crystals grow, longer ones too.
    /// <para>
    /// <b>Fair by design:</b> the cave is built around a path that never climbs or drops more
    /// steeply than the glider can follow at top speed, and no crystal's point comes within
    /// <see cref="PathClear"/> of that path.
    /// </para>
    /// Positions along the cave are in world units from the start; the glider is always at
    /// <see cref="Distance"/> + <see cref="GliderX"/>.
    /// </summary>
    public sealed class CaveSim
    {
        // The glider.
        public const float GliderX = -2f;
        public const float StartY = 0f;
        public const float Radius = 0.28f;
        public const float Lift = 20f;
        public const float Gravity = 18f;
        public const float MaxRise = 6.5f, MaxFall = 6.5f;

        // The cave.
        public const float SampleStep = 0.5f;
        public const float Ahead = 7f, Behind = 7f;
        public const float Roof = 6.3f, Ground = -7.4f;
        public const float StartHeight = 7.5f, MinHeight = 5.6f, Narrowing = 0.016f;
        public const float Lumps = 0.22f;

        /// <summary>The start is straight and wide.</summary>
        public const float CalmStart = 14f;

        /// <summary>The path never climbs or drops more steeply than this (units up per unit along).</summary>
        public const float MaxSlope = 0.45f;

        /// <summary>How much the slope can change from one sample to the next: how sharply the path bends.</summary>
        public const float SlopeChange = 0.12f;

        // Speed.
        public const float StartSpeed = 4.5f, SpeedUp = 0.02f, MaxSpeed = 7f;

        // Crystals.
        public const float CrystalsFrom = 20f;
        public const float StartSpacing = 8f, MinSpacing = 3f, SpacingJitter = 1.5f;
        public const float HardestAt = 140f;
        public static readonly float[] Depths = { 1f, 1.4f, 1.8f };
        public const float CrystalBase = 0.9f;

        /// <summary>A crystal's point stays at least this far from the path.</summary>
        public const float PathClear = 1.2f;

        /// <summary>Crystals hit a little inside their drawn edge, so a graze looks like a miss.</summary>
        public const float HitShrink = 0.08f;

        // Gems.
        public const float GemsFrom = 12f, GemSpacing = 10f, GemRadius = 0.4f;
        public const int GemPoints = 5;
        public const float UnitsPerPoint = 2f;

        readonly System.Random _random;
        readonly List<CaveSample> _samples = new List<CaveSample>();
        readonly List<Crystal> _crystals = new List<Crystal>();
        readonly List<CaveGem> _gems = new List<CaveGem>();
        int _nextSample, _crystalCount;
        float _path, _slope, _nextCrystal = CrystalsFrom, _nextGem = GemsFrom;
        bool _hold;

        public IReadOnlyList<CaveSample> Samples => _samples;
        public IReadOnlyList<Crystal> Crystals => _crystals;
        public IReadOnlyList<CaveGem> Gems => _gems;

        public float Y { get; private set; } = StartY;
        public float Velocity { get; private set; }
        public float Distance { get; private set; }
        public int GemsTaken { get; private set; }
        public int Score => Mathf.FloorToInt(Distance / UnitsPerPoint) + GemsTaken * GemPoints;
        public bool Crashed { get; private set; }
        public Vector2 CrashPoint { get; private set; }
        public Vector2 GemPoint { get; private set; }
        public bool Holding => _hold;

        /// <summary>Where the glider is along the cave.</summary>
        public float GliderWorldX => Distance + GliderX;

        public float Speed => Mathf.Min(MaxSpeed, StartSpeed + SpeedUp * Distance);

        public static float HeightAt(float x) => Mathf.Max(MinHeight, StartHeight - Narrowing * Mathf.Max(0f, x - CalmStart));

        /// <summary>How hard the cave is at a point along it, 0 to 1.</summary>
        public static float Difficulty(float x) => Mathf.Clamp01((x - CrystalsFrom) / (HardestAt - CrystalsFrom));

        public CaveSim(int seed)
        {
            _random = new System.Random(seed);
            _path = StartY;
            Generate();
        }

        public void Hold(bool hold) => _hold = hold;

        public CaveEvent Step(float dt)
        {
            if (Crashed) return CaveEvent.None;
            var events = CaveEvent.None;

            Velocity = Mathf.Clamp(Velocity + (_hold ? Lift : -Gravity) * dt, -MaxFall, MaxRise);
            Y += Velocity * dt;
            Distance += Speed * dt;
            Generate();

            // The rock just stops it.
            float x = GliderWorldX;
            float top = TopAt(x) - Radius, bottom = BottomAt(x) + Radius;
            if (Y > top)
            {
                Y = top;
                if (Velocity > 0.5f) events |= CaveEvent.Bumped;
                Velocity = Mathf.Min(Velocity, 0f);
            }
            if (Y < bottom)
            {
                Y = bottom;
                if (Velocity < -0.5f) events |= CaveEvent.Bumped;
                Velocity = Mathf.Max(Velocity, 0f);
            }

            var glider = new Vector2(x, Y);
            foreach (var g in _gems)
            {
                if (g.Taken || (new Vector2(g.X, g.Y) - glider).sqrMagnitude > (Radius + GemRadius) * (Radius + GemRadius)) continue;
                g.Taken = true;
                GemsTaken++;
                GemPoint = new Vector2(g.X, g.Y);
                events |= CaveEvent.Gem;
            }
            foreach (var c in _crystals)
            {
                if (Mathf.Abs(c.X - x) > CrystalBase) continue;
                if (Hits(c, glider, out var at))
                {
                    Crashed = true;
                    CrashPoint = at;
                    return events | CaveEvent.Crashed;
                }
            }
            return events;
        }

        // --- The cave ahead -----------------------------------------------------------------------

        float SampleX(int i) => (i - Behind / SampleStep) * SampleStep;

        void Generate()
        {
            float until = GliderWorldX + Ahead + Style.WorldWidth;
            while (_samples.Count == 0 || _samples[_samples.Count - 1].X < until) AddSample();
            float from = GliderWorldX - Behind - Style.WorldWidth / 2f;
            _samples.RemoveAll(s => s.X < from);
            _crystals.RemoveAll(c => c.X < from);
            _gems.RemoveAll(g => g.X < from);
        }

        void AddSample()
        {
            int i = _nextSample++;
            float x = SampleX(i);
            float h = HeightAt(x);
            if (x > CalmStart)
            {
                // The path wanders, never steeper than the glider can follow and never bending
                // sharper than it can turn. Near the top or bottom it starts levelling off in time.
                float lo = Ground + h / 2f + Lumps, hi = Mathf.Max(lo, Roof - h / 2f - Lumps);
                float slope = Mathf.Clamp(_slope + (float)(_random.NextDouble() * 2.0 - 1.0) * SlopeChange, -MaxSlope, MaxSlope);
                float samplesToLevel = Mathf.Abs(slope) / SlopeChange;
                float levelling = slope * SampleStep * (samplesToLevel + 1f) / 2f;
                float ahead = _path + slope * SampleStep + levelling;
                if (ahead > hi) slope = Mathf.Max(_slope - SlopeChange, -MaxSlope);
                else if (ahead < lo) slope = Mathf.Min(_slope + SlopeChange, MaxSlope);
                _slope = slope;
                _path = Mathf.Clamp(_path + _slope * SampleStep, lo, hi);
            }
            float top = _path + h / 2f + (float)(_random.NextDouble() * 2.0 - 1.0) * Lumps;
            float bottom = _path - h / 2f + (float)(_random.NextDouble() * 2.0 - 1.0) * Lumps;
            var sample = new CaveSample(i, x, Mathf.Min(top, Roof), Mathf.Max(bottom, Ground), _path);
            _samples.Add(sample);

            if (x >= _nextCrystal) PlaceCrystal(sample);
            if (x >= _nextGem)
            {
                bool clear = true;
                foreach (var c in _crystals) if (Mathf.Abs(c.X - x) < 1.5f) clear = false;
                if (clear) _gems.Add(new CaveGem(x, _path));
                _nextGem = x + GemSpacing;
            }
        }

        void PlaceCrystal(CaveSample s)
        {
            float t = Difficulty(s.X);
            _nextCrystal = s.X + Mathf.Lerp(StartSpacing, MinSpacing, t) + (float)_random.NextDouble() * SpacingJitter;

            // Mostly the other side from the last one, so the way through weaves.
            bool lastCeiling = _crystals.Count > 0 && _crystals[_crystals.Count - 1].Ceiling;
            bool ceiling = _crystals.Count == 0 ? _random.Next(2) == 0 : (_random.NextDouble() < 0.65 ? !lastCeiling : lastCeiling);

            // As long as fits without its point coming near the path; longer ones later on.
            float baseY = ceiling ? s.Top : s.Bottom;
            float room = Mathf.Abs(baseY - s.Path) - PathClear;
            int longest = t < 0.3f ? 1 : t < 0.7f ? 2 : 3;
            float depth = 0f;
            for (int k = longest - 1; k >= 0; k--)
                if (Depths[k] <= room) { depth = Depths[_random.Next(k + 1)]; break; }
            if (depth <= 0f && !ceiling)
            {
                // Try the other side before giving up on this one.
                ceiling = true;
                baseY = s.Top;
                room = Mathf.Abs(baseY - s.Path) - PathClear;
                for (int k = longest - 1; k >= 0; k--)
                    if (Depths[k] <= room) { depth = Depths[_random.Next(k + 1)]; break; }
            }
            if (depth <= 0f) return;
            _crystals.Add(new Crystal(_crystalCount++, s.X, ceiling, depth, baseY));
        }

        float Interpolate(float x, Func<CaveSample, float> value)
        {
            if (_samples.Count == 0) return 0f;
            float f = (x - _samples[0].X) / SampleStep;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, _samples.Count - 2);
            return Mathf.Lerp(value(_samples[i]), value(_samples[i + 1]), Mathf.Clamp01(f - i));
        }

        public float TopAt(float x) => Interpolate(x, s => s.Top);
        public float BottomAt(float x) => Interpolate(x, s => s.Bottom);
        public float PathAt(float x) => Interpolate(x, s => s.Path);

        /// <summary>Whether a glider at <paramref name="p"/> touches the crystal (a triangle from its base to its point), and where.</summary>
        public static bool Hits(Crystal c, Vector2 p, out Vector2 at)
        {
            float dir = c.Ceiling ? -1f : 1f;
            // The base sits a little into the rock; the hit shape is a touch inside the drawn one.
            var tip = new Vector2(c.X, c.TipY - dir * HitShrink);
            float half = CrystalBase / 2f - HitShrink;
            var left = new Vector2(c.X - half, c.BaseY - dir * 0.3f);
            var right = new Vector2(c.X + half, c.BaseY - dir * 0.3f);

            if (Inside(p, left, right, tip)) { at = p; return true; }
            at = Closest(p, left, tip);
            var b = Closest(p, right, tip);
            if ((b - p).sqrMagnitude < (at - p).sqrMagnitude) at = b;
            return (at - p).sqrMagnitude < Radius * Radius;
        }

        static Vector2 Closest(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return a + ab * t;
        }

        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
