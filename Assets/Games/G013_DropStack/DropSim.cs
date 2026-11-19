using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.DropStack
{
    /// <summary>The six piece shapes: our own mix of 3-block and 4-block pieces.</summary>
    public enum PieceKind { Bar, Corner, Square, Tee, Ell, Zig }

    [Flags]
    public enum DropEvent
    {
        None = 0,
        Moved = 1,
        Locked = 2,
        Cleared = 4,
        Gem = 8,
        Lost = 16,
    }

    /// <summary>
    /// The rules of Drop Stack, in plain C# so edit mode tests can play it. Pieces fall into an
    /// 8 x 14 well; a full row clears and everything above drops. Some pieces carry a gem block,
    /// worth a bonus when its row clears. The game ends when a new piece has no room to appear.
    /// Row 0 is the bottom; cells hold 0 (empty), a piece colour (1-6) or <see cref="GemCell"/>.
    /// </summary>
    public sealed class DropSim
    {
        public const int Cols = 8;
        public const int Rows = 14;
        public const int GemCell = 7;

        public const int LinesPerLevel = 8;
        public const int GemPoints = 25;
        public const float GemChance = 1f / 6f;
        public const float LockDelay = 0.4f;
        const int MaxLockResets = 15;

        /// <summary>Points for clearing 0, 1, 2, 3 or 4 rows at once (times the level + 1).</summary>
        public static readonly int[] LinePoints = { 0, 10, 30, 60, 100 };

        static readonly Vector2Int[][] Shapes =
        {
            new[] { V(-1, 0), V(0, 0), V(1, 0) },              // Bar
            new[] { V(0, 0), V(1, 0), V(0, 1) },               // Corner
            new[] { V(0, 0), V(1, 0), V(0, 1), V(1, 1) },      // Square
            new[] { V(-1, 0), V(0, 0), V(1, 0), V(0, 1) },     // Tee
            new[] { V(-1, 0), V(0, 0), V(1, 0), V(1, 1) },     // Ell
            new[] { V(-1, 0), V(0, 0), V(0, 1), V(1, 1) },     // Zig
        };

        static readonly int[] Kicks = { 0, -1, 1, -2, 2 };

        static Vector2Int V(int x, int y) => new Vector2Int(x, y);

        public readonly int[,] Cells = new int[Cols, Rows];

        public PieceKind Kind { get; private set; }
        public int Rotation { get; private set; }
        public Vector2Int Position { get; private set; }

        /// <summary>Which of the current piece's blocks is a gem, or -1.</summary>
        public int GemIndex { get; private set; } = -1;

        public PieceKind NextKind { get; private set; }
        public bool NextHasGem { get; private set; }

        public int Score { get; private set; }
        public int Lines { get; private set; }
        public int Level => Lines / LinesPerLevel;
        public bool Lost { get; private set; }

        /// <summary>The rows cleared by the last lock (bottom first), for effects.</summary>
        public IReadOnlyList<int> LastCleared => _lastCleared;

        /// <summary>Seconds between one-row falls; faster each level.</summary>
        public float FallInterval => Mathf.Max(0.1f, 0.75f * Mathf.Pow(0.85f, Level));

        readonly System.Random _random;
        readonly List<int> _lastCleared = new List<int>();
        float _fallTimer, _lockTimer;
        int _lockResets;
        bool _onFloor;

        public DropSim(int seed = 0)
        {
            _random = new System.Random(seed);
            NextKind = RandomKind(null);
            NextHasGem = RollGem();
            Spawn();
        }

        public static int ColorOf(PieceKind kind) => (int)kind + 1;

        /// <summary>The current piece's blocks, in well coordinates.</summary>
        public Vector2Int[] PieceCells() => CellsFor(Kind, Rotation, Position);

        /// <summary>Where the current piece would land if dropped now.</summary>
        public Vector2Int[] GhostCells()
        {
            var p = Position;
            while (Fits(Kind, Rotation, p + Vector2Int.down)) p += Vector2Int.down;
            return CellsFor(Kind, Rotation, p);
        }

        /// <summary>A shape's blocks at rotation 0 around (0, 0), for drawing the next piece.</summary>
        public static Vector2Int[] ShapeOf(PieceKind kind) => Shapes[(int)kind];

        public static Vector2Int[] CellsFor(PieceKind kind, int rotation, Vector2Int at)
        {
            var shape = Shapes[(int)kind];
            var cells = new Vector2Int[shape.Length];
            for (int i = 0; i < shape.Length; i++)
            {
                var c = shape[i];
                if (kind != PieceKind.Square)
                    for (int r = 0; r < rotation; r++) c = new Vector2Int(c.y, -c.x); // clockwise
                cells[i] = c + at;
            }
            return cells;
        }

        public bool Fits(PieceKind kind, int rotation, Vector2Int at)
        {
            foreach (var c in CellsFor(kind, rotation, at))
            {
                if (c.x < 0 || c.x >= Cols || c.y < 0) return false;
                if (c.y < Rows && Cells[c.x, c.y] != 0) return false;
            }
            return true;
        }

        // --- Player actions ------------------------------------------------------------------

        public bool Move(int dx)
        {
            if (Lost) return false;
            var to = Position + new Vector2Int(dx, 0);
            if (!Fits(Kind, Rotation, to)) return false;
            Position = to;
            Nudged();
            return true;
        }

        /// <summary>Turn clockwise, sliding sideways (or up one) if a wall or block is in the way.</summary>
        public bool Rotate()
        {
            if (Lost) return false;
            int r = (Rotation + 1) % 4;
            foreach (int dy in new[] { 0, 1 })
            foreach (int dx in Kicks)
            {
                var to = Position + new Vector2Int(dx, dy);
                if (!Fits(Kind, r, to)) continue;
                Rotation = r;
                Position = to;
                Nudged();
                return true;
            }
            return false;
        }

        /// <summary>Move down one row now. False if it's resting on something.</summary>
        public bool SoftDrop()
        {
            if (Lost) return false;
            var to = Position + Vector2Int.down;
            if (!Fits(Kind, Rotation, to)) return false;
            Position = to;
            _fallTimer = 0f;
            return true;
        }

        /// <summary>Drop straight to the bottom and lock.</summary>
        public DropEvent HardDrop()
        {
            if (Lost) return DropEvent.None;
            while (SoftDrop()) { }
            return Lock();
        }

        // --- Time ----------------------------------------------------------------------------

        public DropEvent Step(float dt)
        {
            if (Lost) return DropEvent.None;

            _onFloor = !Fits(Kind, Rotation, Position + Vector2Int.down);
            if (_onFloor)
            {
                _lockTimer += dt;
                if (_lockTimer >= LockDelay) return Lock();
                return DropEvent.None;
            }

            _lockTimer = 0f;
            _fallTimer += dt;
            var events = DropEvent.None;
            while (_fallTimer >= FallInterval)
            {
                _fallTimer -= FallInterval;
                if (!SoftDrop()) break;
                events |= DropEvent.Moved;
            }
            return events;
        }

        void Nudged()
        {
            // Moving or turning a piece that is resting gives a little more time, a few times.
            if (_onFloor && _lockResets < MaxLockResets) { _lockTimer = 0f; _lockResets++; }
        }

        // --- Locking, clearing, spawning -----------------------------------------------------

        DropEvent Lock()
        {
            var events = DropEvent.Locked;
            var cells = PieceCells();
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                if (c.y < Rows) Cells[c.x, c.y] = i == GemIndex ? GemCell : ColorOf(Kind);
            }

            _lastCleared.Clear();
            int gems = 0;
            for (int y = 0; y < Rows; y++)
            {
                bool full = true;
                for (int x = 0; x < Cols && full; x++) full = Cells[x, y] != 0;
                if (!full) continue;
                _lastCleared.Add(y);
                for (int x = 0; x < Cols; x++) if (Cells[x, y] == GemCell) gems++;
            }

            if (_lastCleared.Count > 0)
            {
                Score += LinePoints[Mathf.Min(_lastCleared.Count, 4)] * (Level + 1);
                Score += gems * GemPoints;
                Lines += _lastCleared.Count;
                // Remove the full rows from the top down, so the indexes below stay right.
                for (int i = _lastCleared.Count - 1; i >= 0; i--) RemoveRow(_lastCleared[i]);
                events |= DropEvent.Cleared;
                if (gems > 0) events |= DropEvent.Gem;
            }

            if (!Spawn()) { Lost = true; events |= DropEvent.Lost; }
            return events;
        }

        void RemoveRow(int row)
        {
            for (int y = row; y < Rows - 1; y++)
                for (int x = 0; x < Cols; x++)
                    Cells[x, y] = Cells[x, y + 1];
            for (int x = 0; x < Cols; x++) Cells[x, Rows - 1] = 0;
        }

        bool Spawn()
        {
            Kind = NextKind;
            Rotation = 0;
            Position = new Vector2Int(Cols / 2 - 1, Rows - 2);
            GemIndex = NextHasGem ? _random.Next(Shapes[(int)Kind].Length) : -1;
            NextKind = RandomKind(Kind);
            NextHasGem = RollGem();
            _fallTimer = _lockTimer = 0f;
            _lockResets = 0;
            _onFloor = false;
            return Fits(Kind, Rotation, Position);
        }

        PieceKind RandomKind(PieceKind? previous)
        {
            var k = (PieceKind)_random.Next(Shapes.Length);
            if (previous.HasValue && k == previous.Value) k = (PieceKind)_random.Next(Shapes.Length); // fewer repeats
            return k;
        }

        bool RollGem() => _random.NextDouble() < GemChance;

        // --- Test helpers --------------------------------------------------------------------

        /// <summary>For tests: make the current piece a given kind, at a given spot, without a gem.</summary>
        public void SetPiece(PieceKind kind, Vector2Int at, int rotation = 0, int gemIndex = -1)
        {
            Kind = kind;
            Position = at;
            Rotation = rotation;
            GemIndex = gemIndex;
        }
    }
}
