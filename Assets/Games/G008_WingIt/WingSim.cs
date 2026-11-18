using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.WingIt
{
    /// <summary>What happened during one <see cref="WingSim.Step"/>.</summary>
    [Flags]
    public enum WingEvent
    {
        None = 0,
        Flapped = 1,
        Passed = 2,
        Gem = 4,
        Bumped = 8,
        Crashed = 16,
        Spawned = 32,
    }

    /// <summary>A pair of thorny columns with a gap between them, scrolling left.</summary>
    public sealed class WingColumn
    {
        public int Index { get; }
        public float X { get; internal set; }
        public float GapY { get; }
        public float Gap { get; }
        public bool HasGem { get; internal set; }
        public bool Passed { get; internal set; }

        public float GapTop => GapY + Gap / 2f;
        public float GapBottom => GapY - Gap / 2f;

        internal WingColumn(int index, float x, float gapY, float gap, bool gem)
        {
            Index = index;
            X = x;
            GapY = gapY;
            Gap = gap;
            HasGem = gem;
        }
    }

    /// <summary>
    /// The rules of Wing It, in plain C# so they can be tested. A bee hangs at a fixed x while
    /// columns scroll at it from the right. Gravity pulls it down; a tap flaps it up. Each column
    /// passed scores 1, and a teal gem in every 4th gap scores 5. The ceiling just stops the bee;
    /// the thorny columns and the spikes along the floor end the run.
    /// Columns get a little faster and their gaps a little narrower with each one, up to a cap,
    /// and a gap never moves further from the last one than a bee can follow.
    /// </summary>
    public sealed class WingSim
    {
        // The bee.
        public const float BeeX = -1.6f;
        public const float StartY = -2.6f;
        public const float Radius = 0.32f;
        public const float Gravity = 24f;
        public const float FlapSpeed = 7.6f;
        public const float MaxFall = 10f;

        // The space it flies in.
        public const float Ceiling = 6.3f;
        public const float FloorY = -6.9f;

        // Columns.
        public const float ColumnWidth = 1.3f;
        public const float SpawnX = 7.5f;
        public const float RemoveX = -7.5f;
        public const float FirstX = 8f;
        public const float Spacing = 4.6f;
        public const float StartSpeed = 3f;
        public const float SpeedUp = 0.04f;
        public const float MaxSpeed = 4.2f;
        public const float StartGap = 3.6f;
        public const float GapShrink = 0.04f;
        public const float MinGap = 2.6f;

        /// <summary>Gaps keep this clear of the floor and the ceiling.</summary>
        public const float FloorClear = 1.0f;
        public const float CeilingClear = 0.6f;

        /// <summary>
        /// The furthest one gap's centre is above or below the last one's. A flap just as the bee
        /// leaves a column brings it back to the same height by the next one, so a gap never
        /// drops further than the bee can be below its own gap's bottom edge: the gap, less the
        /// bee, less a margin (see <see cref="MaxDropFor"/>). Climbing takes a flurry of taps.
        /// </summary>
        public const float MaxRise = 2f;
        public const float MaxDrop = 2.4f;
        public const float DropMargin = 0.5f;

        /// <summary>The furthest a gap can drop below the last one, for a gap of this size.</summary>
        public static float MaxDropFor(float gap) => Mathf.Min(MaxDrop, gap - 2f * Radius - DropMargin);

        public const int ColumnPoints = 1;
        public const int GemPoints = 5;
        public const int GemEvery = 4;
        public const float GemRadius = 0.4f;

        readonly System.Random _random;
        readonly List<WingColumn> _columns = new List<WingColumn>();
        int _spawned;

        public IReadOnlyList<WingColumn> Columns => _columns;
        public float Y { get; private set; } = StartY;
        public float Velocity { get; private set; }
        public float Distance { get; private set; }
        public int Score { get; private set; }
        public int Passed { get; private set; }
        public int Gems { get; private set; }
        public bool Crashed { get; private set; }
        public Vector2 CrashPoint { get; private set; }
        public Vector2 GemPoint { get; private set; }

        /// <summary>Scroll speed now, in units a second.</summary>
        public float Speed => Mathf.Min(MaxSpeed, StartSpeed + SpeedUp * Passed);

        public WingSim(int seed)
        {
            _random = new System.Random(seed);
        }

        /// <summary>The gap a column gets, by its index.</summary>
        public static float GapFor(int index) => Mathf.Max(MinGap, StartGap - GapShrink * index);

        public static float LowestGapY(float gap) => FloorY + FloorClear + gap / 2f;
        public static float HighestGapY(float gap) => Ceiling - CeilingClear - gap / 2f;

        /// <summary>Flap: the bee shoots up at <see cref="FlapSpeed"/>, whatever it was doing.</summary>
        public void Flap()
        {
            if (Crashed) return;
            Velocity = FlapSpeed;
        }

        public WingEvent Step(float dt)
        {
            if (Crashed) return WingEvent.None;
            var events = WingEvent.None;

            Velocity = Mathf.Max(-MaxFall, Velocity - Gravity * dt);
            Y += Velocity * dt;
            if (Y > Ceiling - Radius)
            {
                Y = Ceiling - Radius;
                if (Velocity > 0f) events |= WingEvent.Bumped;
                Velocity = Mathf.Min(Velocity, 0f);
            }

            float move = Speed * dt;
            Distance += move;
            foreach (var c in _columns) c.X -= move;
            _columns.RemoveAll(c => c.X < RemoveX);
            while (_columns.Count == 0 || NextX() <= SpawnX)
            {
                Spawn(NextX());
                events |= WingEvent.Spawned;
            }

            var bee = new Vector2(BeeX, Y);
            foreach (var c in _columns)
            {
                if (c.HasGem && (bee - new Vector2(c.X, c.GapY)).magnitude < Radius + GemRadius)
                {
                    c.HasGem = false;
                    Gems++;
                    Score += GemPoints;
                    GemPoint = new Vector2(c.X, c.GapY);
                    events |= WingEvent.Gem;
                }
                if (Hits(c, bee, out var at)) return events | Crash(at);
                if (!c.Passed && c.X < BeeX)
                {
                    c.Passed = true;
                    Passed++;
                    Score += ColumnPoints;
                    events |= WingEvent.Passed;
                }
            }

            if (Y - Radius < FloorY) return events | Crash(new Vector2(BeeX, FloorY));
            return events;
        }

        float NextX() => _columns.Count == 0
            ? (_spawned == 0 ? FirstX : SpawnX)
            : _columns[_columns.Count - 1].X + Spacing;

        /// <summary>For tests: plan columns ahead of time (they spawn off to the right, in order).</summary>
        public void PlanAhead(int count)
        {
            while (_spawned < count) Spawn(NextX());
        }

        void Spawn(float x)
        {
            int index = _spawned++;
            float gap = GapFor(index);
            float lo = LowestGapY(gap), hi = HighestGapY(gap);
            float last = _columns.Count > 0 ? _columns[_columns.Count - 1].GapY : StartY;
            if (index == 0) last = StartY;
            lo = Mathf.Max(lo, last - MaxDropFor(gap));
            hi = Mathf.Min(hi, last + MaxRise);
            float y = Mathf.Lerp(lo, hi, (float)_random.NextDouble());
            if (index == 0) y = Mathf.Clamp(StartY, lo, hi);
            _columns.Add(new WingColumn(index, x, y, gap, index > 0 && index % GemEvery == GemEvery - 1));
        }

        /// <summary>Whether a bee at <paramref name="bee"/> touches the column's thorns, and where.</summary>
        public static bool Hits(WingColumn c, Vector2 bee, out Vector2 at)
        {
            float half = ColumnWidth / 2f;
            float nx = Mathf.Clamp(bee.x, c.X - half, c.X + half);
            // The nearest point of the top part, then of the bottom part.
            var top = new Vector2(nx, Mathf.Max(bee.y, c.GapTop));
            var bottom = new Vector2(nx, Mathf.Min(bee.y, c.GapBottom));
            if ((bee - top).sqrMagnitude < Radius * Radius) { at = top; return true; }
            if ((bee - bottom).sqrMagnitude < Radius * Radius) { at = bottom; return true; }
            at = default;
            return false;
        }

        WingEvent Crash(Vector2 at)
        {
            Crashed = true;
            CrashPoint = at;
            return WingEvent.Crashed;
        }
    }
}
