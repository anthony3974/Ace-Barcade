using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.RocketLanes
{
    /// <summary>What happened during one <see cref="RocketLanesSim.Step"/>.</summary>
    [Flags]
    public enum LaneEvent
    {
        None = 0,
        Spawned = 1,
        Passed = 2,
        Crystal = 4,
        Crashed = 8,
    }

    /// <summary>One row of mines across the three lanes, maybe with a crystal in an open lane.</summary>
    public sealed class LaneRow
    {
        /// <summary>0 for the first row of the run, then 1, 2, 3...</summary>
        public int Index { get; }

        /// <summary>The row's height in world units. It scrolls down toward the player.</summary>
        public float Y { get; internal set; }

        /// <summary>Seconds between the previous row crossing the player's line and this one doing so.</summary>
        public float GapSeconds { get; }

        /// <summary>A short gap that leaves no time to swipe: every lane open in the row before stays open.</summary>
        public bool Tight { get; }

        /// <summary>The lane holding a crystal, or -1.</summary>
        public int CrystalLane { get; internal set; } = -1;

        public bool CrystalTaken { get; internal set; }

        /// <summary>True once the row has crossed the player's line (and scored).</summary>
        public bool Passed { get; internal set; }

        readonly bool[] _mines = new bool[RocketLanesSim.Lanes];

        internal LaneRow(int index, float y, float gapSeconds, bool tight, bool[] mines)
        {
            Index = index;
            Y = y;
            GapSeconds = gapSeconds;
            Tight = tight;
            Array.Copy(mines, _mines, RocketLanesSim.Lanes);
        }

        public bool Mine(int lane) => _mines[lane];
        public bool Open(int lane) => !_mines[lane];
        public bool HasCrystal => CrystalLane >= 0 && !CrystalTaken;

        internal bool[] CopyMines() => (bool[])_mines.Clone();
    }

    /// <summary>
    /// The rules of Rocket Lanes, in plain C# so edit mode tests can play it. The rocket sits low
    /// in one of three lanes; rows of mines scroll down toward it, and a swipe moves it one lane.
    /// Each row passed scores 1, and a crystal in an open lane scores 5. The scroll speeds up a
    /// little with every row, up to a cap.
    ///
    /// Fairness is measured in time, not distance. Between two rows the player has a
    /// <see cref="Window"/>: from the moment the first row is clear of the rocket until the next
    /// one reaches it. If the next row makes the player move, that window is at least
    /// <see cref="MoveTime"/> (reaction plus one swipe), and the move is never more than one
    /// lane. Rows closer than that (<see cref="LaneRow.Tight"/>) keep every open lane open, so
    /// staying put is always safe. The gap shrinks and the speed rises only until the window at
    /// full speed still fits a move, so every run stays winnable.
    /// </summary>
    public sealed class RocketLanesSim
    {
        // --- Track, in world units ------------------------------------------------------------

        public const int Lanes = 3;
        public const float LaneWidth = 2.6f;
        public const float PlayerY = -5f;

        /// <summary>Rows are made until the newest is this high (above the tallest phone's view).</summary>
        public const float SpawnAheadY = 13f;

        /// <summary>Rows below this are forgotten.</summary>
        public const float DespawnY = -12f;

        // --- Sizes for collisions -------------------------------------------------------------

        public const float PlayerHalfWidth = 0.38f;
        public const float PlayerHalfLength = 0.55f;
        public const float MineRadius = 0.45f;
        public const float CrystalRadius = 0.35f;

        /// <summary>A mine this close above or below the rocket's centre can hit it.</summary>
        public const float HitDepth = MineRadius + PlayerHalfLength;

        // --- Moving between lanes -------------------------------------------------------------

        /// <summary>How long the rocket takes to slide one lane over.</summary>
        public const float LaneMoveTime = 0.12f;

        /// <summary>Time to see that the next row needs a move and start swiping.</summary>
        public const float ReactionTime = 0.3f;

        /// <summary>Time for one swipe, including the rocket's slide (at least <see cref="LaneMoveTime"/>).</summary>
        public const float SwipeTime = 0.2f;

        /// <summary>The least window a row that makes the player move one lane may leave.</summary>
        public const float MoveTime = ReactionTime + SwipeTime;

        // --- The ramp -------------------------------------------------------------------------

        public const float StartSpeed = 6.5f;
        public const float SpeedPerRow = 0.12f;
        public const float MaxSpeed = 12f;

        /// <summary>Seconds from the start of the run to the first row reaching the rocket.</summary>
        public const float FirstGap = 2f;
        public const float StartGap = 1f;
        public const float GapPerRow = 0.006f;
        public const float MinGap = 0.72f;

        /// <summary>Gap for a tight row (no time to move: the open lanes stay open).</summary>
        public const float TightGap = 0.42f;
        public const int TightFrom = 8;
        public const float TightChance = 0.2f;

        /// <summary>The first rows have one mine; after that two mines grow more likely.</summary>
        public const int OneMineRows = 4;
        public const float TwoMineStart = 0.3f;
        public const float TwoMinePerRow = 0.01f;
        public const float TwoMineMax = 0.65f;

        public const int CrystalFrom = 3;
        public const float CrystalChance = 0.3f;

        public const int RowPoints = 1;
        public const int CrystalPoints = 5;

        // --- Fairness, as plain functions the tests check -------------------------------------

        /// <summary>The scroll speed while row <paramref name="rowIndex"/> comes in (that many rows passed).</summary>
        public static float SpeedAt(int rowIndex) => Mathf.Min(MaxSpeed, StartSpeed + rowIndex * SpeedPerRow);

        /// <summary>The normal (not tight) gap in seconds before row <paramref name="rowIndex"/>.</summary>
        public static float GapAt(int rowIndex) => Mathf.Max(MinGap, StartGap - rowIndex * GapPerRow);

        /// <summary>Seconds the player can move in between two rows <paramref name="gapSeconds"/> apart.</summary>
        public static float Window(float gapSeconds, float speed) => gapSeconds - 2f * HitDepth / speed;

        /// <summary>How many lanes the player can be asked to move in a window: 0 or 1.</summary>
        public static int Reach(float window) => window >= MoveTime ? 1 : 0;

        /// <summary>
        /// True if, from every lane open in <paramref name="before"/>, there is an open lane in
        /// <paramref name="after"/> at most <paramref name="reach"/> lanes away (and after has an open lane).
        /// </summary>
        public static bool Fair(bool[] before, bool[] after, int reach)
        {
            bool anyOpen = false;
            for (int b = 0; b < Lanes; b++) anyOpen |= !after[b];
            if (!anyOpen) return false;
            for (int a = 0; a < Lanes; a++)
            {
                if (before[a]) continue;
                bool reachable = false;
                for (int b = Mathf.Max(0, a - reach); b <= Mathf.Min(Lanes - 1, a + reach); b++)
                    if (!after[b]) reachable = true;
                if (!reachable) return false;
            }
            return true;
        }

        public static float LaneX(int lane) => (lane - 1) * LaneWidth;

        // --- State ----------------------------------------------------------------------------

        /// <summary>The lane the rocket is in or sliding to.</summary>
        public int Lane { get; private set; } = 1;

        /// <summary>The rocket's x: slides toward <see cref="LaneX"/>(<see cref="Lane"/>).</summary>
        public float PlayerX { get; private set; }

        public int Score { get; private set; }
        public int RowsPassed { get; private set; }
        public int Crystals { get; private set; }
        public bool Crashed { get; private set; }

        /// <summary>How far the world has scrolled, in units (for the background).</summary>
        public float Distance { get; private set; }

        /// <summary>Where the rocket hit a mine.</summary>
        public Vector2 CrashPoint { get; private set; }

        /// <summary>Where the last crystal was picked up.</summary>
        public Vector2 CrystalPoint { get; private set; }

        public float Speed => SpeedAt(RowsPassed);

        /// <summary>Rows from just below the rocket up to above the screen, lowest first.</summary>
        public IReadOnlyList<LaneRow> Rows => _rows;

        /// <summary>For tests: when false, no new rows are made.</summary>
        public bool AutoSpawn { get; set; } = true;

        readonly List<LaneRow> _rows = new List<LaneRow>();
        readonly System.Random _random;
        int _nextIndex;
        bool[] _lastMines;
        float _lastY;
        bool _lastTight;

        public RocketLanesSim(int seed = 0)
        {
            _random = new System.Random(seed);
            SpawnAhead();
        }

        /// <summary>Swipe one lane left (-1) or right (+1). False if already at that edge.</summary>
        public bool Swipe(int direction)
        {
            if (Crashed || direction == 0) return false;
            int target = Mathf.Clamp(Lane + Math.Sign(direction), 0, Lanes - 1);
            if (target == Lane) return false;
            Lane = target;
            return true;
        }

        public LaneEvent Step(float dt)
        {
            if (Crashed || dt <= 0f) return LaneEvent.None;
            var events = LaneEvent.None;
            // Small steps, so nothing jumps past the rocket in one long frame.
            int steps = Mathf.Max(1, Mathf.CeilToInt(Speed * dt / 0.2f));
            float h = dt / steps;
            for (int i = 0; i < steps && !Crashed; i++) events |= Substep(h);
            if (!Crashed) events |= SpawnAhead();
            return events;
        }

        LaneEvent Substep(float dt)
        {
            var events = LaneEvent.None;
            PlayerX = Mathf.MoveTowards(PlayerX, LaneX(Lane), LaneWidth / LaneMoveTime * dt);

            float dy = Speed * dt;
            Distance += dy;
            foreach (var row in _rows) row.Y -= dy;
            _lastY -= dy;

            foreach (var row in _rows)
            {
                float rel = row.Y - PlayerY;

                if (Mathf.Abs(rel) < HitDepth)
                    for (int lane = 0; lane < Lanes; lane++)
                        if (row.Mine(lane) && Mathf.Abs(LaneX(lane) - PlayerX) < MineRadius + PlayerHalfWidth)
                        {
                            Crashed = true;
                            CrashPoint = new Vector2((LaneX(lane) + PlayerX) / 2f, (row.Y + PlayerY) / 2f);
                            return events | LaneEvent.Crashed;
                        }

                if (row.HasCrystal && Mathf.Abs(rel) < CrystalRadius + PlayerHalfLength
                    && Mathf.Abs(LaneX(row.CrystalLane) - PlayerX) < CrystalRadius + PlayerHalfWidth)
                {
                    row.CrystalTaken = true;
                    Crystals++;
                    Score += CrystalPoints;
                    CrystalPoint = new Vector2(LaneX(row.CrystalLane), row.Y);
                    events |= LaneEvent.Crystal;
                }

                if (!row.Passed && row.Y < PlayerY)
                {
                    row.Passed = true;
                    RowsPassed++;
                    Score += RowPoints;
                    events |= LaneEvent.Passed;
                }
            }

            while (_rows.Count > 0 && _rows[0].Y < DespawnY) _rows.RemoveAt(0);
            return events;
        }

        LaneEvent SpawnAhead()
        {
            var events = LaneEvent.None;
            while (AutoSpawn && (_nextIndex == 0 || _lastY < SpawnAheadY))
            {
                _rows.Add(NextRow());
                events |= LaneEvent.Spawned;
            }
            return events;
        }

        /// <summary>Plans the next row: its gap from the last one, its mines and maybe a crystal.</summary>
        LaneRow NextRow()
        {
            int k = _nextIndex++;
            float speed = SpeedAt(k);
            bool tight = k >= TightFrom && !_lastTight && _random.NextDouble() < TightChance;
            float gap = k == 0 ? FirstGap : tight ? TightGap : GapAt(k);
            float y = k == 0 ? PlayerY + gap * speed : _lastY + gap * speed;

            bool[] mines = null;
            if (k == 0) mines = RandomMines(k);
            else
            {
                int reach = Reach(Window(gap, speed));
                for (int tries = 0; tries < 30 && mines == null; tries++)
                {
                    var m = RandomMines(k);
                    if (Fair(_lastMines, m, reach)) mines = m;
                }
                // Same mines as the last row: the same lanes open, always fair.
                if (mines == null) mines = (bool[])_lastMines.Clone();
            }

            var row = new LaneRow(k, y, gap, tight, mines);
            if (k >= CrystalFrom && _random.NextDouble() < CrystalChance)
            {
                int open = 0;
                for (int lane = 0; lane < Lanes; lane++) if (!mines[lane]) open++;
                int pick = _random.Next(open);
                for (int lane = 0; lane < Lanes; lane++)
                    if (!mines[lane] && pick-- == 0) row.CrystalLane = lane;
            }

            _lastMines = mines;
            _lastY = y;
            _lastTight = tight;
            return row;
        }

        bool[] RandomMines(int k)
        {
            float two = Mathf.Min(TwoMineMax, TwoMineStart + (k - OneMineRows) * TwoMinePerRow);
            int count = k >= OneMineRows && _random.NextDouble() < two ? 2 : 1;
            var mines = new bool[Lanes];
            while (count > 0)
            {
                int lane = _random.Next(Lanes);
                if (mines[lane]) continue;
                mines[lane] = true;
                count--;
            }
            return mines;
        }

        // --- Test helpers -------------------------------------------------------------------

        /// <summary>For tests: plan rows until at least <paramref name="count"/> exist (however high).</summary>
        public void PlanAhead(int count)
        {
            while (_rows.Count < count) _rows.Add(NextRow());
        }

        /// <summary>For tests: forget every row and stop making new ones.</summary>
        public void ClearRows()
        {
            _rows.Clear();
            AutoSpawn = false;
        }

        /// <summary>For tests: add a row at a height, with mines in the given lanes.</summary>
        public LaneRow AddRow(float y, int crystalLane = -1, params int[] mineLanes)
        {
            var mines = new bool[Lanes];
            foreach (int lane in mineLanes) mines[lane] = true;
            var row = new LaneRow(_nextIndex++, y, StartGap, false, mines) { CrystalLane = crystalLane };
            _rows.Add(row);
            _rows.Sort((a, b) => a.Y.CompareTo(b.Y));
            return row;
        }
    }
}
