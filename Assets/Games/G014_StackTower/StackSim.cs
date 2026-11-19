using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.StackTower
{
    /// <summary>What happened during one <see cref="StackSim.Drop"/> or <see cref="StackSim.Step"/>.</summary>
    [Flags]
    public enum StackEvent
    {
        None = 0,
        Landed = 1,
        Perfect = 2,
        Cut = 4,
        Grew = 8,
        Gem = 16,
        GemLost = 32,
        Missed = 64,
        Bounced = 128,
    }

    /// <summary>One floor of the tower. Level 0 is the foundation.</summary>
    public sealed class StackFloor
    {
        public int Level { get; }
        public float X { get; }
        public float Width { get; }
        public bool Perfect { get; }

        public float Left => X - Width / 2f;
        public float Right => X + Width / 2f;

        internal StackFloor(int level, float x, float width, bool perfect)
        {
            Level = level;
            X = x;
            Width = width;
            Perfect = perfect;
        }
    }

    /// <summary>
    /// The rules of Stack Tower, in plain C# so they can be tested. A block slides back and forth
    /// across the top of the tower; a tap drops it. The part that hangs over the floor below is
    /// cut off and falls away, so the tower narrows. A drop within <see cref="PerfectTolerance"/>
    /// of the floor below is perfect: it snaps into line and loses nothing, and from the third
    /// perfect in a row each one widens the tower again. Missing the tower altogether ends the run.
    /// Each floor scores 1, a perfect 1 more, and every 6th block carries a teal gem worth 5 if it
    /// lands on the tower rather than with the part that's cut off.
    /// Blocks come in from alternate sides and slide a little faster each floor, up to a cap.
    /// </summary>
    public sealed class StackSim
    {
        // Sizes, in units. Floor n sits from n * FloorHeight to (n + 1) * FloorHeight.
        public const float FloorHeight = 0.6f;
        public const float StartWidth = 3.6f;

        /// <summary>
        /// The moving block's centre slides between -SlideRange and +SlideRange. The tower's centre
        /// can never leave the foundation (each floor sits on the one below), so it's always well
        /// inside this: every sweep passes right over it.
        /// </summary>
        public const float SlideRange = 3.0f;

        public const float StartSpeed = 3.0f;
        public const float SpeedUp = 0.08f;
        public const float MaxSpeed = 6.2f;

        /// <summary>A new block waits this long at its edge before sliding; taps meanwhile are ignored.</summary>
        public const float WaitTime = 0.3f;

        /// <summary>Landing this close to the floor below counts as perfect, and snaps into line.</summary>
        public const float PerfectTolerance = 0.12f;

        /// <summary>From this many perfects in a row, each perfect widens the tower by <see cref="GrowBy"/>.</summary>
        public const int GrowStreak = 3;
        public const float GrowBy = 0.3f;

        public const int FloorPoints = 1;
        public const int PerfectPoints = 1;
        public const int GemPoints = 5;
        public const int GemEvery = 6;

        readonly List<StackFloor> _floors = new List<StackFloor>();

        public IReadOnlyList<StackFloor> Floors => _floors;
        public StackFloor Top => _floors[_floors.Count - 1];

        /// <summary>Floors stacked on the foundation.</summary>
        public int Stacked => _floors.Count - 1;

        // The moving block.
        public int Level => _floors.Count;
        public float X { get; private set; }
        public float Width { get; private set; }
        public int Direction { get; private set; }
        public float Wait { get; private set; }
        public bool HasGem => Level % GemEvery == 0;
        public bool FromLeft => Level % 2 == 1;

        public int Score { get; private set; }
        public int Streak { get; private set; }
        public int Perfects { get; private set; }
        public int Gems { get; private set; }
        public bool Over { get; private set; }

        /// <summary>After a cut: the part that fell off (centre and width), at the level just placed.</summary>
        public float CutX { get; private set; }
        public float CutWidth { get; private set; }

        /// <summary>After a miss: where the block was when it was dropped.</summary>
        public float MissX { get; private set; }
        public float MissWidth { get; private set; }

        /// <summary>Where the last gem was, when it was collected or lost.</summary>
        public float GemX { get; private set; }

        /// <summary>Sliding speed now, in units a second.</summary>
        public float Speed => Mathf.Min(MaxSpeed, StartSpeed + SpeedUp * Stacked);

        public StackSim()
        {
            _floors.Add(new StackFloor(0, 0f, StartWidth, false));
            NextBlock(StartWidth);
        }

        void NextBlock(float width)
        {
            Width = width;
            Direction = FromLeft ? 1 : -1;
            X = -Direction * SlideRange;
            Wait = WaitTime;
        }

        public StackEvent Step(float dt)
        {
            if (Over) return StackEvent.None;
            if (Wait > 0f)
            {
                float waited = Mathf.Min(Wait, dt);
                Wait -= waited;
                dt -= waited;
                if (dt <= 0f) return StackEvent.None;
            }

            var events = StackEvent.None;
            X += Direction * Speed * dt;
            if (X > SlideRange) { X = 2f * SlideRange - X; Direction = -1; events |= StackEvent.Bounced; }
            if (X < -SlideRange) { X = -2f * SlideRange - X; Direction = 1; events |= StackEvent.Bounced; }
            return events;
        }

        /// <summary>Drop the moving block onto the tower. Ignored while it waits at its edge.</summary>
        public StackEvent Drop()
        {
            if (Over || Wait > 0f) return StackEvent.None;

            var top = Top;
            float offset = X - top.X;
            bool gem = HasGem;
            GemX = X;

            if (Mathf.Abs(offset) <= PerfectTolerance)
            {
                var events = StackEvent.Landed | StackEvent.Perfect;
                Streak++;
                Perfects++;
                float width = Width;
                if (Streak >= GrowStreak && width < StartWidth)
                {
                    width = Mathf.Min(StartWidth, width + GrowBy);
                    events |= StackEvent.Grew;
                }
                Score += FloorPoints + PerfectPoints;
                if (gem) events |= CollectGem(top.X);
                return events | Place(top.X, width, true);
            }

            float left = Mathf.Max(X - Width / 2f, top.Left);
            float right = Mathf.Min(X + Width / 2f, top.Right);
            if (right - left <= 0f)
            {
                Over = true;
                MissX = X;
                MissWidth = Width;
                return StackEvent.Missed | (gem ? StackEvent.GemLost : StackEvent.None);
            }

            var cutEvents = StackEvent.Landed | StackEvent.Cut;
            Streak = 0;
            // The part hanging past the floor below falls away.
            if (offset > 0f) { CutX = (right + X + Width / 2f) / 2f; CutWidth = X + Width / 2f - right; }
            else { CutX = (X - Width / 2f + left) / 2f; CutWidth = left - (X - Width / 2f); }
            Score += FloorPoints;
            if (gem) cutEvents |= GemX >= left && GemX <= right ? CollectGem(GemX) : StackEvent.GemLost;
            return cutEvents | Place((left + right) / 2f, right - left, false);
        }

        StackEvent CollectGem(float x)
        {
            GemX = x;
            Gems++;
            Score += GemPoints;
            return StackEvent.Gem;
        }

        StackEvent Place(float x, float width, bool perfect)
        {
            _floors.Add(new StackFloor(Level, x, width, perfect));
            NextBlock(width);
            return StackEvent.None;
        }

        /// <summary>For tests: how long until the moving block's centre is next over <paramref name="x"/>.</summary>
        public float TimeUntilOver(float x)
        {
            float t = Wait, pos = X, dir = Direction, speed = Speed;
            for (int leg = 0; leg < 3; leg++)
            {
                float edge = dir * SlideRange;
                if ((x - pos) * dir >= 0f) return t + Mathf.Abs(x - pos) / speed;
                t += Mathf.Abs(edge - pos) / speed;
                pos = edge;
                dir = -dir;
            }
            return t;
        }
    }
}
