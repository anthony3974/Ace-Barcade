using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.PetalSwap
{
    /// <summary>What happened on a <see cref="PetalSim.Step"/>.</summary>
    [Flags]
    public enum PetalEvent
    {
        None = 0,
        SwappedBack = 1, // a swap made no match and is sliding back
        Popped = 2,      // a wave of flowers popped (see Popped, LastPoints, Chain)
        Landed = 4,      // everything has fallen into place
        Shuffled = 8,    // no moves were left, so the garden shuffled itself
        TimeUp = 16,
    }

    /// <summary>One flower in the garden. The view keys its sprite on <see cref="Id"/>.</summary>
    public sealed class Flower
    {
        public int Id;
        public int Color;
        public int Col, Row;

        /// <summary>How many rows above its cell the flower still is while it falls.</summary>
        public float Drop;
    }

    /// <summary>
    /// The rules of Petal Swap, in plain C# so edit mode tests can play it. A 7 x 7 garden of
    /// flowers in five colours. Swap a flower with a neighbour: if that lines up three or more of
    /// a colour across or down, they pop, the flowers above fall into the gaps and new ones drop
    /// in from the top, which can set off more matches (a chain, worth more each wave). A swap that
    /// makes no match slides back at no cost. A garden with no moves left shuffles itself.
    ///
    /// Each popped flower scores 1 times the chain and adds <see cref="SecondsPerFlower"/> to the
    /// clock, up to <see cref="MaxSeconds"/>. The clock only runs while the garden is still and
    /// waiting on the player, and drains faster as the score climbs. The run ends when it empties.
    ///
    /// Cells are (column, row), row 0 at the bottom.
    /// </summary>
    public sealed class PetalSim
    {
        public const int Size = 7;
        public const int Colors = 5;

        public const float StartSeconds = 30f;
        public const float MaxSeconds = 30f;
        public const float SecondsPerFlower = 0.5f;

        /// <summary>The clock drains this much faster for every point scored, up to <see cref="MaxDrain"/>.</summary>
        public const float DrainPerPoint = 1f / 250f;
        public const float MaxDrain = 2.5f;

        public const float SwapTime = 0.14f;
        public const float PopTime = 0.22f;
        public const float ShuffleTime = 0.45f;
        public const float FallStartSpeed = 5f;   // rows per second
        public const float FallGravity = 45f;     // rows per second per second

        public enum Phase { Still, Swapping, SwappingBack, Popping, Falling, Shuffling, Over }

        public Phase State { get; private set; } = Phase.Still;
        public bool Over => State == Phase.Over;
        public int Score { get; private set; }
        public float TimeLeft { get; private set; } = StartSeconds;

        /// <summary>The wave of the current chain: 1 for the swap's own match, 2 for the first cascade...</summary>
        public int Chain { get; private set; }

        /// <summary>Points the last wave scored.</summary>
        public int LastPoints { get; private set; }

        /// <summary>The flowers popping in the current wave (still on the board until the pop ends).</summary>
        public IReadOnlyList<Flower> Popped => _popped;

        /// <summary>Swaps that made a match.</summary>
        public int Matches { get; private set; }

        /// <summary>Seconds the garden has been still and waiting (for the hint).</summary>
        public float StillFor { get; private set; }

        /// <summary>0 to 1 through the current swap, pop or shuffle.</summary>
        public float Progress => _timer / PhaseTime;

        public float DrainRate => Mathf.Min(MaxDrain, 1f + Score * DrainPerPoint);

        /// <summary>For tests: colours for new flowers, used before the random ones.</summary>
        public readonly Queue<int> SpawnQueue = new Queue<int>();

        readonly Flower[,] _grid = new Flower[Size, Size];
        readonly List<Flower> _popped = new List<Flower>();
        readonly bool[,] _marked = new bool[Size, Size];
        readonly System.Random _random;
        Flower _a, _b;
        float _timer, _fallTime;
        bool _checked;
        int _nextId;

        public PetalSim(int seed)
        {
            _random = new System.Random(seed);
            for (int c = 0; c < Size; c++)
                for (int r = 0; r < Size; r++)
                    _grid[c, r] = new Flower { Id = _nextId++, Col = c, Row = r };
            Deal();
        }

        public Flower At(int col, int row) => In(col, row) ? _grid[col, row] : null;

        public static bool In(int col, int row) => col >= 0 && col < Size && row >= 0 && row < Size;

        float PhaseTime => State == Phase.Popping ? PopTime : State == Phase.Shuffling ? ShuffleTime : SwapTime;

        /// <summary>
        /// Where a flower is drawn, in cells (fractions while it swaps or falls).
        /// </summary>
        public Vector2 DrawPos(Flower f)
        {
            var at = new Vector2(f.Col, f.Row + f.Drop);
            if ((State == Phase.Swapping || State == Phase.SwappingBack) && (f == _a || f == _b))
            {
                var other = f == _a ? _b : _a;
                float t = Progress;
                float e = t * t * (3f - 2f * t);
                at = Vector2.Lerp(at, new Vector2(other.Col, other.Row), e);
            }
            return at;
        }

        // --- Moves ----------------------------------------------------------------------------

        /// <summary>
        /// Swap the flower at a cell with its neighbour one step along <paramref name="dir"/>.
        /// Only while the garden is still. Returns false if the swap can't start.
        /// </summary>
        public bool Swap(Vector2Int cell, Vector2Int dir)
        {
            if (State != Phase.Still) return false;
            if (Mathf.Abs(dir.x) + Mathf.Abs(dir.y) != 1) return false;
            var to = cell + dir;
            if (!In(cell.x, cell.y) || !In(to.x, to.y)) return false;
            _a = _grid[cell.x, cell.y];
            _b = _grid[to.x, to.y];
            State = Phase.Swapping;
            _timer = 0f;
            return true;
        }

        /// <summary>A swap that would make a match, or false if there is none.</summary>
        public bool FindMove(out Vector2Int cell, out Vector2Int dir)
        {
            for (int c = 0; c < Size; c++)
                for (int r = 0; r < Size; r++)
                {
                    if (c + 1 < Size && SwapMatches(c, r, c + 1, r)) { cell = new Vector2Int(c, r); dir = Vector2Int.right; return true; }
                    if (r + 1 < Size && SwapMatches(c, r, c, r + 1)) { cell = new Vector2Int(c, r); dir = Vector2Int.up; return true; }
                }
            cell = dir = default;
            return false;
        }

        public bool HasMove => FindMove(out _, out _);

        bool SwapMatches(int c1, int r1, int c2, int r2)
        {
            int k1 = _grid[c1, r1].Color, k2 = _grid[c2, r2].Color;
            if (k1 == k2) return false;
            _grid[c1, r1].Color = k2;
            _grid[c2, r2].Color = k1;
            bool match = MatchAt(c1, r1) || MatchAt(c2, r2);
            _grid[c1, r1].Color = k1;
            _grid[c2, r2].Color = k2;
            return match;
        }

        /// <summary>True if the flower at a cell is part of three or more in a line.</summary>
        bool MatchAt(int c, int r)
        {
            int k = _grid[c, r].Color;
            int run = 1;
            for (int x = c - 1; x >= 0 && _grid[x, r].Color == k; x--) run++;
            for (int x = c + 1; x < Size && _grid[x, r].Color == k; x++) run++;
            if (run >= 3) return true;
            run = 1;
            for (int y = r - 1; y >= 0 && _grid[c, y].Color == k; y--) run++;
            for (int y = r + 1; y < Size && _grid[c, y].Color == k; y++) run++;
            return run >= 3;
        }

        /// <summary>Marks every flower in a line of three or more; returns how many.</summary>
        int MarkMatches()
        {
            Array.Clear(_marked, 0, _marked.Length);
            int count = 0;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size;)
                {
                    int end = c + 1;
                    while (end < Size && _grid[end, r].Color == _grid[c, r].Color) end++;
                    if (end - c >= 3)
                        for (int x = c; x < end; x++) if (!_marked[x, r]) { _marked[x, r] = true; count++; }
                    c = end;
                }
            for (int c = 0; c < Size; c++)
                for (int r = 0; r < Size;)
                {
                    int end = r + 1;
                    while (end < Size && _grid[c, end].Color == _grid[c, r].Color) end++;
                    if (end - r >= 3)
                        for (int y = r; y < end; y++) if (!_marked[c, y]) { _marked[c, y] = true; count++; }
                    r = end;
                }
            return count;
        }

        // --- Time -----------------------------------------------------------------------------

        public PetalEvent Step(float dt)
        {
            var events = PetalEvent.None;
            switch (State)
            {
                case Phase.Still:
                    // Each time the garden settles, make sure there's a move to make.
                    if (!_checked)
                    {
                        _checked = true;
                        if (!HasMove)
                        {
                            Shuffle();
                            State = Phase.Shuffling;
                            _timer = 0f;
                            events |= PetalEvent.Shuffled;
                            break;
                        }
                    }
                    StillFor += dt;
                    TimeLeft -= dt * DrainRate;
                    if (TimeLeft <= 0f)
                    {
                        TimeLeft = 0f;
                        State = Phase.Over;
                        events |= PetalEvent.TimeUp;
                    }
                    break;

                case Phase.Swapping:
                    if ((_timer += dt) < SwapTime) break;
                    Exchange(_a, _b);
                    if (MarkMatches() > 0)
                    {
                        Matches++;
                        Chain = 0;
                        events |= StartPop();
                    }
                    else
                    {
                        State = Phase.SwappingBack;
                        _timer = 0f;
                        events |= PetalEvent.SwappedBack;
                    }
                    break;

                case Phase.SwappingBack:
                    if ((_timer += dt) < SwapTime) break;
                    Exchange(_a, _b);
                    _a = _b = null;
                    BeStill();
                    break;

                case Phase.Popping:
                    if ((_timer += dt) < PopTime) break;
                    Collapse();
                    State = Phase.Falling;
                    _fallTime = 0f;
                    break;

                case Phase.Falling:
                    _fallTime += dt;
                    float fall = (FallStartSpeed + FallGravity * _fallTime) * dt;
                    bool moving = false;
                    foreach (var f in _grid)
                    {
                        f.Drop = Mathf.Max(0f, f.Drop - fall);
                        if (f.Drop > 0f) moving = true;
                    }
                    if (moving) break;
                    events |= PetalEvent.Landed;
                    if (MarkMatches() > 0) events |= StartPop();
                    else BeStill();
                    break;

                case Phase.Shuffling:
                    if ((_timer += dt) < ShuffleTime) break;
                    State = Phase.Still;
                    StillFor = 0f;
                    break;
            }
            return events;
        }

        void BeStill()
        {
            State = Phase.Still;
            StillFor = 0f;
            _checked = false;
        }

        void Exchange(Flower a, Flower b)
        {
            (a.Col, b.Col) = (b.Col, a.Col);
            (a.Row, b.Row) = (b.Row, a.Row);
            _grid[a.Col, a.Row] = a;
            _grid[b.Col, b.Row] = b;
        }

        PetalEvent StartPop()
        {
            Chain++;
            _popped.Clear();
            for (int c = 0; c < Size; c++)
                for (int r = 0; r < Size; r++)
                    if (_marked[c, r]) _popped.Add(_grid[c, r]);
            LastPoints = _popped.Count * Chain;
            Score += LastPoints;
            TimeLeft = Mathf.Min(MaxSeconds, TimeLeft + _popped.Count * SecondsPerFlower);
            State = Phase.Popping;
            _timer = 0f;
            _a = _b = null;
            return PetalEvent.Popped;
        }

        /// <summary>Takes the popped flowers out, drops the rest down and new ones in from the top.</summary>
        void Collapse()
        {
            for (int c = 0; c < Size; c++)
            {
                int to = 0;
                for (int r = 0; r < Size; r++)
                {
                    if (_marked[c, r]) continue;
                    var f = _grid[c, r];
                    f.Drop = r - to;
                    f.Row = to;
                    _grid[c, to++] = f;
                }
                int missing = Size - to;
                for (int r = to; r < Size; r++)
                    _grid[c, r] = new Flower { Id = _nextId++, Color = NewColor(), Col = c, Row = r, Drop = missing };
            }
            Array.Clear(_marked, 0, _marked.Length);
        }

        int NewColor() => SpawnQueue.Count > 0 ? SpawnQueue.Dequeue() : _random.Next(Colors);

        // --- Dealing and shuffling ------------------------------------------------------------

        /// <summary>Fresh colours everywhere: no lines already made, and at least one move.</summary>
        void Deal()
        {
            do
            {
                for (int c = 0; c < Size; c++)
                    for (int r = 0; r < Size; r++)
                    {
                        int k;
                        do k = _random.Next(Colors);
                        while ((c >= 2 && _grid[c - 1, r].Color == k && _grid[c - 2, r].Color == k)
                            || (r >= 2 && _grid[c, r - 1].Color == k && _grid[c, r - 2].Color == k));
                        _grid[c, r].Color = k;
                    }
            } while (!HasMove);
        }

        /// <summary>Moves the colours around until there's a move and no line is already made.</summary>
        void Shuffle()
        {
            var colors = new List<int>(Size * Size);
            foreach (var f in _grid) colors.Add(f.Color);
            for (int attempt = 0; attempt < 500; attempt++)
            {
                for (int i = colors.Count - 1; i > 0; i--)
                {
                    int j = _random.Next(i + 1);
                    (colors[i], colors[j]) = (colors[j], colors[i]);
                }
                int n = 0;
                for (int c = 0; c < Size; c++)
                    for (int r = 0; r < Size; r++)
                        _grid[c, r].Color = colors[n++];
                if (MarkMatches() == 0 && HasMove) return;
            }
            Deal();
        }

        // --- For tests ------------------------------------------------------------------------

        /// <summary>For tests: lay out the colours, rows given top first, one digit per flower.</summary>
        public void SetBoard(params string[] rowsTopFirst)
        {
            for (int i = 0; i < Size; i++)
                for (int c = 0; c < Size; c++)
                {
                    var f = _grid[c, Size - 1 - i];
                    f.Color = rowsTopFirst[i][c] - '0';
                    f.Drop = 0f;
                }
            BeStill();
        }

        /// <summary>For tests: set the clock.</summary>
        public void SetTime(float seconds) => TimeLeft = seconds;

        /// <summary>For tests: the board's colours, rows top first.</summary>
        public string[] Board()
        {
            var rows = new string[Size];
            for (int i = 0; i < Size; i++)
            {
                var chars = new char[Size];
                for (int c = 0; c < Size; c++) chars[c] = (char)('0' + _grid[c, Size - 1 - i].Color);
                rows[i] = new string(chars);
            }
            return rows;
        }

        /// <summary>For tests, while the garden is still: is a line of three already made?</summary>
        public bool AnyLineMade
        {
            get
            {
                int n = MarkMatches();
                Array.Clear(_marked, 0, _marked.Length);
                return n > 0;
            }
        }
    }
}
