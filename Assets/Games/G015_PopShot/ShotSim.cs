using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.PopShot
{
    /// <summary>What happened during one <see cref="ShotSim.Step"/>.</summary>
    [Flags]
    public enum ShotEvent
    {
        None = 0,
        Fired = 1,
        Bounced = 2,
        Landed = 4,
        Popped = 8,
        Dropped = 16,
        Gem = 32,
        Pip = 64,
        Pushed = 128,
        Cleared = 256,
        Over = 512,
    }

    /// <summary>One bubble on the board. Ids never repeat, so the view can follow each one.</summary>
    public sealed class Bubble
    {
        public int Id { get; }
        public int Color { get; }
        public bool Gem { get; }

        internal Bubble(int id, int color, bool gem)
        {
            Id = id;
            Color = color;
            Gem = gem;
        }
    }

    /// <summary>A bubble that left the board this step, and where it was.</summary>
    public readonly struct Gone
    {
        public readonly Bubble Bubble;
        public readonly Vector2 At;

        public Gone(Bubble bubble, Vector2 at)
        {
            Bubble = bubble;
            At = at;
        }
    }

    /// <summary>
    /// The rules of Pop Shot, in plain C# so they can be tested. Bubbles hang from the top in
    /// rows packed like a honeycomb: 8 to a row, then 7 set half a bubble in, and so on. The
    /// player aims from the bottom and shoots the loaded bubble; it bounces off the side walls
    /// and sticks where it touches the board or the top. Three or more of one colour touching
    /// pop (1 each), and anything left hanging by nothing drops (2 each). A gem in a bubble adds
    /// 5 however it goes. Each shot that pops nothing fills one of six pips; when they're all
    /// full a new row pushes in from the top. A bubble below the red line ends the run.
    /// Every 8 rows pushed in there's one pip fewer (down to 4), and a fifth colour joins after 10.
    /// </summary>
    public sealed class ShotSim
    {
        // The board, in units.
        public const int Cols = 8;
        public const float Radius = 0.5f;
        public const float RowHeight = 0.8660254f;
        public const float TopY = 5.3f;
        public const float Left = -4f, Right = 4f;
        public const float DangerY = -4.1f;

        /// <summary>The most rows that fit above the red line.</summary>
        public static int MaxRows => Mathf.FloorToInt((TopY - Radius - DangerY) / RowHeight) + 1;

        // The shot.
        public static readonly Vector2 Shooter = new Vector2(0f, -6f);
        public const float Speed = 18f;
        public const float MinAngle = 10f;

        /// <summary>A shot sticks once its centre is this close to a bubble's (a touch less than touching, so it can slip into gaps).</summary>
        public const float StickDistance = 0.84f;
        const float SubStep = 0.05f;

        // Rules.
        public const int StartRows = 5;
        public const int StartColors = 4;
        public const int MaxColors = 5;
        public const int ColorsJoinAfter = 10;
        public const int StartPips = 6, MinPips = 4, PipsDropEvery = 8;
        public const int PopAt = 3;
        public const int PopPoints = 1, DropPoints = 2, GemPoints = 5, ClearPoints = 20;
        public const int ClearRefill = 4;

        /// <summary>One bubble in this many comes with a gem.</summary>
        public const int GemOneIn = 14;

        /// <summary>How likely a new bubble copies the colour of the one beside it: bubbles come in clumps.</summary>
        public const double Clumping = 0.45;

        readonly System.Random _random;
        readonly List<Bubble[]> _rows = new List<Bubble[]>();
        readonly List<Gone> _popped = new List<Gone>(), _dropped = new List<Gone>();
        int _shift, _nextId = 1;

        public int Current { get; private set; }
        public int Next { get; private set; }
        public int Score { get; private set; }
        public int Shots { get; private set; }
        public int Pips { get; private set; }
        public int RowsPushed { get; private set; }
        public int Gems { get; private set; }
        public bool Over { get; private set; }

        /// <summary>The shot in flight, if there is one.</summary>
        public bool Flying { get; private set; }
        public Vector2 ShotPos { get; private set; }
        public Vector2 ShotDir { get; private set; }
        public int ShotColor { get; private set; }

        /// <summary>Where the last shot stuck.</summary>
        public int LandedRow { get; private set; } = -1;
        public int LandedCol { get; private set; } = -1;

        public IReadOnlyList<Gone> Popped => _popped;
        public IReadOnlyList<Gone> Dropped => _dropped;
        public int Rows => _rows.Count;

        public int PipsToPush => Mathf.Max(MinPips, StartPips - RowsPushed / PipsDropEvery);
        public int Colors => RowsPushed >= ColorsJoinAfter ? MaxColors : StartColors;

        public ShotSim(int seed)
        {
            _random = new System.Random(seed);
            for (int i = 0; i < StartRows; i++) AddRowAtBottom();
            Current = PickColor();
            Next = PickColor();
        }

        // --- The honeycomb ----------------------------------------------------------------------

        /// <summary>Whether row <paramref name="row"/> is set half a bubble in (and holds 7, not 8).</summary>
        public bool Shifted(int row) => ((row + _shift) & 1) == 1;

        public int ColsIn(int row) => Shifted(row) ? Cols - 1 : Cols;

        public Vector2 CellPos(int row, int col) =>
            new Vector2(Left + Radius + col + (Shifted(row) ? Radius : 0f), TopY - row * RowHeight);

        public Bubble At(int row, int col) =>
            row >= 0 && row < _rows.Count && col >= 0 && col < ColsIn(row) ? _rows[row][col] : null;

        /// <summary>The cells touching a cell (up to six).</summary>
        public IEnumerable<(int Row, int Col)> Neighbours(int row, int col)
        {
            yield return (row, col - 1);
            yield return (row, col + 1);
            // A shifted row sits between columns c and c+1 of the rows next to it.
            int a = Shifted(row) ? col : col - 1;
            foreach (int r in new[] { row - 1, row + 1 })
            {
                yield return (r, a);
                yield return (r, a + 1);
            }
        }

        bool Valid(int row, int col) => row >= 0 && col >= 0 && col < ColsIn(row);

        public int Count
        {
            get
            {
                int n = 0;
                foreach (var row in _rows) foreach (var b in row) if (b != null) n++;
                return n;
            }
        }

        /// <summary>The lowest row with a bubble in it, or -1 for an empty board.</summary>
        public int LowestRow
        {
            get
            {
                for (int r = _rows.Count - 1; r >= 0; r--)
                    foreach (var b in _rows[r]) if (b != null) return r;
                return -1;
            }
        }

        Bubble NewBubble(int color) => new Bubble(_nextId++, color, _random.Next(GemOneIn) == 0);

        Bubble[] RandomRow(int row)
        {
            var cells = new Bubble[Cols];
            int last = -1;
            for (int c = 0; c < ColsIn(row); c++)
            {
                int color = last >= 0 && _random.NextDouble() < Clumping ? last : _random.Next(Colors);
                cells[c] = NewBubble(color);
                last = color;
            }
            return cells;
        }

        void AddRowAtBottom() => _rows.Add(RandomRow(_rows.Count));

        /// <summary>A new row pushes in from the top; every row below moves down one, keeping its place across.</summary>
        void PushRow()
        {
            _shift ^= 1;
            _rows.Insert(0, null);
            _rows[0] = RandomRow(0);
            RowsPushed++;
            Trim();
        }

        void Trim()
        {
            while (_rows.Count > 0 && Array.TrueForAll(_rows[_rows.Count - 1], b => b == null)) _rows.RemoveAt(_rows.Count - 1);
        }

        void Set(int row, int col, Bubble b)
        {
            while (_rows.Count <= row) _rows.Add(new Bubble[Cols]);
            _rows[row][col] = b;
        }

        /// <summary>A colour that's on the board (any colour if it's empty), so every shot can match something.</summary>
        int PickColor()
        {
            var present = new List<int>();
            foreach (var row in _rows)
                foreach (var b in row)
                    if (b != null && !present.Contains(b.Color)) present.Add(b.Color);
            return present.Count == 0 ? _random.Next(Colors) : present[_random.Next(present.Count)];
        }

        bool OnBoard(int color)
        {
            foreach (var row in _rows) foreach (var b in row) if (b != null && b.Color == color) return true;
            return false;
        }

        // --- Shooting ---------------------------------------------------------------------------

        /// <summary>The direction a shot would take toward a point: up, and never flatter than <see cref="MinAngle"/>.</summary>
        public static Vector2 AimToward(Vector2 point)
        {
            var d = point - Shooter;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            if (d.y < 0f) angle = d.x < 0f ? 180f : 0f;
            angle = Mathf.Clamp(angle, MinAngle, 180f - MinAngle);
            return new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
        }

        /// <summary>Fire the loaded bubble. Ignored while a shot is flying or the run is over.</summary>
        public bool Fire(Vector2 dir)
        {
            if (Over || Flying) return false;
            Flying = true;
            ShotPos = Shooter;
            ShotDir = dir.normalized;
            ShotColor = Current;
            Current = Next;
            Next = PickColor();
            Shots++;
            return true;
        }

        /// <summary>
        /// Moves a shot from <paramref name="pos"/> along <paramref name="dir"/> by up to
        /// <paramref name="distance"/>, bouncing off the walls. True if it touched the board.
        /// </summary>
        bool Move(ref Vector2 pos, ref Vector2 dir, float distance, ref bool bounced)
        {
            while (distance > 0f)
            {
                float step = Mathf.Min(SubStep, distance);
                distance -= step;
                pos += dir * step;
                if (pos.x < Left + Radius) { pos.x = 2f * (Left + Radius) - pos.x; dir.x = Mathf.Abs(dir.x); bounced = true; }
                if (pos.x > Right - Radius) { pos.x = 2f * (Right - Radius) - pos.x; dir.x = -Mathf.Abs(dir.x); bounced = true; }
                if (Touches(pos)) return true;
            }
            return false;
        }

        bool Touches(Vector2 pos)
        {
            if (pos.y >= TopY) return true;
            int near = Mathf.RoundToInt((TopY - pos.y) / RowHeight);
            for (int r = Mathf.Max(0, near - 1); r <= near + 1 && r < _rows.Count; r++)
                for (int c = 0; c < ColsIn(r); c++)
                    if (_rows[r][c] != null && (CellPos(r, c) - pos).sqrMagnitude < StickDistance * StickDistance) return true;
            return false;
        }

        /// <summary>
        /// The path a shot would take (the shooter, each bounce, where it stops) and the cell it
        /// would stick in. The aim line draws this, so what you see is where it goes.
        /// </summary>
        public List<Vector2> Trace(Vector2 dir, out int row, out int col)
        {
            var path = new List<Vector2> { Shooter };
            var pos = Shooter;
            dir = dir.normalized;
            for (int i = 0; i < 4000; i++)
            {
                bool bounced = false;
                var was = dir;
                bool hit = Move(ref pos, ref dir, SubStep, ref bounced);
                // Where it met the wall (to within a sub-step).
                if (bounced) path.Add(new Vector2(was.x < 0f ? Left + Radius : Right - Radius, pos.y));
                if (hit) break;
            }
            path.Add(pos);
            (row, col) = Snap(pos);
            return path;
        }

        /// <summary>The empty cell a shot that stopped at <paramref name="pos"/> sticks in: the nearest one that hangs from something.</summary>
        public (int Row, int Col) Snap(Vector2 pos)
        {
            int near = Mathf.Max(0, Mathf.RoundToInt((TopY - pos.y) / RowHeight));
            (int, int) best = (-1, -1);
            float bestD = float.MaxValue;
            for (int r = Mathf.Max(0, near - 1); r <= near + 1; r++)
                for (int c = 0; c < ColsIn(r); c++)
                {
                    if (At(r, c) != null || !Anchored(r, c)) continue;
                    float d = (CellPos(r, c) - pos).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = (r, c); }
                }
            if (best.Item1 < 0)
            {
                // Nothing nearby to hang from (it can't happen in play): the nearest empty cell.
                for (int r = Mathf.Max(0, near - 1); r <= near + 1; r++)
                    for (int c = 0; c < ColsIn(r); c++)
                    {
                        if (At(r, c) != null) continue;
                        float d = (CellPos(r, c) - pos).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = (r, c); }
                    }
            }
            return best;
        }

        bool Anchored(int row, int col)
        {
            if (row == 0) return true;
            foreach (var (r, c) in Neighbours(row, col))
                if (Valid(r, c) && At(r, c) != null) return true;
            return false;
        }

        public ShotEvent Step(float dt)
        {
            _popped.Clear();
            _dropped.Clear();
            if (Over || !Flying) return ShotEvent.None;

            var pos = ShotPos;
            var dir = ShotDir;
            bool bounced = false;
            bool hit = Move(ref pos, ref dir, Speed * dt, ref bounced);
            ShotPos = pos;
            ShotDir = dir;
            var events = bounced ? ShotEvent.Bounced : ShotEvent.None;
            if (!hit) return events;

            Flying = false;
            var (row, col) = Snap(pos);
            return events | Land(row, col, ShotColor);
        }

        ShotEvent Land(int row, int col, int color)
        {
            var events = ShotEvent.Landed;
            LandedRow = row;
            LandedCol = col;
            Set(row, col, new Bubble(_nextId++, color, false));

            // Pop the clump it joined, if it's big enough.
            var clump = Flood(row, col, b => b.Color == color);
            if (clump.Count >= PopAt)
            {
                foreach (var (r, c) in clump)
                {
                    var b = _rows[r][c];
                    _popped.Add(new Gone(b, CellPos(r, c)));
                    Score += PopPoints;
                    if (b.Gem) { Score += GemPoints; Gems++; events |= ShotEvent.Gem; }
                    _rows[r][c] = null;
                }
                events |= ShotEvent.Popped;

                // Whatever no longer hangs from the top drops.
                var held = new HashSet<(int, int)>();
                for (int c = 0; c < ColsIn(0) && _rows.Count > 0; c++)
                    if (_rows[0][c] != null) foreach (var cell in Flood(0, c, _ => true)) held.Add(cell);
                for (int r = 0; r < _rows.Count; r++)
                    for (int c = 0; c < ColsIn(r); c++)
                    {
                        var b = _rows[r][c];
                        if (b == null || held.Contains((r, c))) continue;
                        _dropped.Add(new Gone(b, CellPos(r, c)));
                        Score += DropPoints;
                        if (b.Gem) { Score += GemPoints; Gems++; events |= ShotEvent.Gem; }
                        _rows[r][c] = null;
                    }
                if (_dropped.Count > 0) events |= ShotEvent.Dropped;
                Trim();

                if (Count == 0)
                {
                    Score += ClearPoints;
                    Pips = 0;
                    events |= ShotEvent.Cleared;
                    for (int i = 0; i < ClearRefill; i++) PushRow();
                }
            }
            else
            {
                Pips++;
                events |= ShotEvent.Pip;
                if (Pips >= PipsToPush)
                {
                    Pips = 0;
                    PushRow();
                    events |= ShotEvent.Pushed;
                }
            }

            // The next shots are always colours still on the board.
            if (!OnBoard(Current)) Current = PickColor();
            if (!OnBoard(Next)) Next = PickColor();

            if (LowestRow >= MaxRows)
            {
                Over = true;
                events |= ShotEvent.Over;
            }
            return events;
        }

        List<(int, int)> Flood(int row, int col, Func<Bubble, bool> same)
        {
            var found = new List<(int, int)>();
            var seen = new HashSet<(int, int)> { (row, col) };
            var queue = new Queue<(int, int)>();
            queue.Enqueue((row, col));
            while (queue.Count > 0)
            {
                var (r, c) = queue.Dequeue();
                found.Add((r, c));
                foreach (var (nr, nc) in Neighbours(r, c))
                {
                    if (!Valid(nr, nc) || seen.Contains((nr, nc))) continue;
                    var b = At(nr, nc);
                    if (b == null || !same(b)) continue;
                    seen.Add((nr, nc));
                    queue.Enqueue((nr, nc));
                }
            }
            return found;
        }

        // --- For tests ----------------------------------------------------------------------------

        /// <summary>
        /// For tests: replace the board. One string per row from the top, one character per cell:
        /// '.' empty, 'B' blue, 'G' green, 'P' purple, 'O' orange, 'K' pink; lower case has a gem.
        /// Row 0 is never shifted.
        /// </summary>
        public void Load(int current, int next, params string[] rows)
        {
            _rows.Clear();
            _shift = 0;
            for (int r = 0; r < rows.Length; r++)
            {
                var cells = new Bubble[Cols];
                for (int c = 0; c < rows[r].Length && c < ColsIn(r); c++)
                {
                    char ch = rows[r][c];
                    int color = "BGPOK".IndexOf(char.ToUpperInvariant(ch));
                    if (color >= 0) cells[c] = new Bubble(_nextId++, color, char.IsLower(ch));
                }
                _rows.Add(cells);
            }
            Trim();
            Current = current;
            Next = next;
            Pips = 0;
        }

        /// <summary>For tests: load the next two shots.</summary>
        public void SetShots(int current, int next)
        {
            Current = current;
            Next = next;
        }

        /// <summary>For tests: the board as <see cref="Load"/> reads it.</summary>
        public string[] Dump()
        {
            var rows = new string[_rows.Count];
            for (int r = 0; r < _rows.Count; r++)
            {
                var chars = new char[ColsIn(r)];
                for (int c = 0; c < chars.Length; c++)
                {
                    var b = _rows[r][c];
                    chars[c] = b == null ? '.' : b.Gem ? char.ToLowerInvariant("BGPOK"[b.Color]) : "BGPOK"[b.Color];
                }
                rows[r] = new string(chars);
            }
            return rows;
        }
    }
}
