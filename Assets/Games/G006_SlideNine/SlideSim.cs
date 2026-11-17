using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.SlideNine
{
    /// <summary>What happened on a tap or a <see cref="SlideSim.Step"/>.</summary>
    [Flags]
    public enum SlideEvent
    {
        None = 0,
        Moved = 1,      // one or more tiles slid
        Solved = 2,     // ... and that solved the puzzle
        NewPuzzle = 4,  // the next puzzle is laid out
        TimeUp = 8,
    }

    /// <summary>
    /// The rules of Slide Nine, in plain C# so edit mode tests can play it. A 3 x 3 sliding
    /// puzzle against the clock: eight numbered tiles and a gap. Tap a tile in the gap's row or
    /// column and it slides toward the gap (pushing any tiles between). Put them in order, 1 to
    /// 8 with the gap last, to solve it. Each solve scores <see cref="SolvePoints"/> plus a bonus
    /// for using few moves, and adds time to the clock; then a harder puzzle comes.
    ///
    /// Every puzzle is exactly <see cref="Par"/> moves from solved (the fewest possible), found
    /// in a table of all 181,440 positions a 3 x 3 puzzle can be in, so the difficulty is exact.
    /// </summary>
    public sealed class SlideSim
    {
        public const int Size = 3;
        public const int Cells = Size * Size;

        public const float StartSeconds = 90f;
        public const float SolveBonusSeconds = 15f;
        public const float MaxSeconds = 99f;

        /// <summary>After a solve the clock stops this long while the board celebrates.</summary>
        public const float SolvePause = 0.8f;

        public const int StartPar = 6;
        public const int ParStep = 2;
        public const int MaxPar = 20;

        public const int SolvePoints = 10;
        public const int MoveBonusMax = 10;

        /// <summary>Tile numbers by cell (0 is the gap). Cell 0 is the top left, reading across.</summary>
        public IReadOnlyList<int> Board => _board;

        public int Gap { get; private set; }

        /// <summary>Puzzles solved this run (also the index of the current puzzle).</summary>
        public int Solved { get; private set; }

        /// <summary>The fewest moves that solve the current puzzle (how it was set up).</summary>
        public int Par { get; private set; }

        /// <summary>Tiles slid on the current puzzle.</summary>
        public int Moves { get; private set; }

        public int Score { get; private set; }
        public float TimeLeft { get; private set; } = StartSeconds;
        public bool Over { get; private set; }

        /// <summary>True during the short pause after a solve (taps are ignored, the clock is stopped).</summary>
        public bool Celebrating => _pause > 0f;

        /// <summary>Points the last solve scored.</summary>
        public int LastSolvePoints { get; private set; }

        public bool IsSolved
        {
            get
            {
                for (int i = 0; i < Cells - 1; i++) if (_board[i] != i + 1) return false;
                return _board[Cells - 1] == 0;
            }
        }

        /// <summary>True if a tile sits in its own cell (tile n belongs in cell n - 1).</summary>
        public bool Home(int cell) => _board[cell] != 0 && _board[cell] == cell + 1;

        public static int ParFor(int puzzle) => Math.Min(MaxPar, StartPar + puzzle * ParStep);

        /// <summary>The bonus for solving in <paramref name="moves"/> when <paramref name="par"/> was possible.</summary>
        public static int MoveBonus(int moves, int par) => Math.Max(0, MoveBonusMax - Math.Max(0, moves - par));

        readonly int[] _board = new int[Cells];
        readonly System.Random _random;
        float _pause;

        public SlideSim(int seed = 0)
        {
            _random = new System.Random(seed);
            LayOut(ParFor(0));
        }

        /// <summary>
        /// Tap a cell. A tile in the gap's row or column slides toward the gap, pushing any tiles
        /// between. Anything else (the gap, a tile off the gap's lines) does nothing.
        /// </summary>
        public SlideEvent Tap(int cell)
        {
            if (Over || Celebrating || cell < 0 || cell >= Cells || cell == Gap) return SlideEvent.None;
            int r = cell / Size, c = cell % Size, gr = Gap / Size, gc = Gap % Size;
            if (r != gr && c != gc) return SlideEvent.None;

            int step = r == gr ? Math.Sign(c - gc) : Math.Sign(r - gr) * Size;
            while (Gap != cell)
            {
                int next = Gap + step;
                _board[Gap] = _board[next];
                _board[next] = 0;
                Gap = next;
                Moves++;
            }

            var events = SlideEvent.Moved;
            if (IsSolved)
            {
                LastSolvePoints = SolvePoints + MoveBonus(Moves, Par);
                Score += LastSolvePoints;
                Solved++;
                TimeLeft = Mathf.Min(MaxSeconds, TimeLeft + SolveBonusSeconds);
                _pause = SolvePause;
                events |= SlideEvent.Solved;
            }
            return events;
        }

        public SlideEvent Step(float dt)
        {
            if (Over || dt <= 0f) return SlideEvent.None;
            if (_pause > 0f)
            {
                _pause -= dt;
                if (_pause > 0f) return SlideEvent.None;
                _pause = 0f;
                LayOut(ParFor(Solved));
                return SlideEvent.NewPuzzle;
            }
            TimeLeft -= dt;
            if (TimeLeft > 0f) return SlideEvent.None;
            TimeLeft = 0f;
            Over = true;
            return SlideEvent.TimeUp;
        }

        void LayOut(int par)
        {
            var choices = Table.WithDistance(par);
            Decode(choices[_random.Next(choices.Count)], _board);
            Gap = Array.IndexOf(_board, 0);
            Par = par;
            Moves = 0;
        }

        // --- The table of every position ------------------------------------------------------

        /// <summary>Fewest moves from this board to solved (single-tile moves).</summary>
        public static int Distance(IReadOnlyList<int> board) => Table.Distance(Encode(board));

        /// <summary>The board as one number: cell i's tile is digit i in base 9.</summary>
        public static int Encode(IReadOnlyList<int> board)
        {
            int code = 0;
            for (int i = Cells - 1; i >= 0; i--) code = code * Cells + board[i];
            return code;
        }

        public static void Decode(int code, int[] board)
        {
            for (int i = 0; i < Cells; i++) { board[i] = code % Cells; code /= Cells; }
        }

        /// <summary>
        /// Every position reachable from solved, with its distance, found once by a breadth-first
        /// search out from the solved board (about 181 thousand positions, quick to build).
        /// </summary>
        public static class Table
        {
            static Dictionary<int, byte> _distance;
            static List<int>[] _byDistance;

            public static int Count { get { Build(); return _distance.Count; } }
            public static int MaxDistance { get { Build(); return _byDistance.Length - 1; } }

            public static int Distance(int code)
            {
                Build();
                return _distance.TryGetValue(code, out var d) ? d : -1;
            }

            public static IReadOnlyList<int> WithDistance(int d)
            {
                Build();
                return _byDistance[Mathf.Clamp(d, 0, _byDistance.Length - 1)];
            }

            static void Build()
            {
                if (_distance != null) return;
                var solved = new int[Cells];
                for (int i = 0; i < Cells - 1; i++) solved[i] = i + 1;
                int start = Encode(solved);

                var distance = new Dictionary<int, byte>(190000) { [start] = 0 };
                var layers = new List<List<int>> { new List<int> { start } };
                var pow = new int[Cells];
                pow[0] = 1;
                for (int i = 1; i < Cells; i++) pow[i] = pow[i - 1] * Cells;

                while (true)
                {
                    var frontier = layers[layers.Count - 1];
                    var next = new List<int>();
                    byte d = (byte)layers.Count;
                    foreach (int code in frontier)
                    {
                        int gap = 0;
                        for (int i = 0, c = code; i < Cells; i++, c /= Cells) if (c % Cells == 0) { gap = i; break; }
                        int gr = gap / Size, gc = gap % Size;
                        for (int k = 0; k < 4; k++)
                        {
                            int nr = gr + (k == 0 ? -1 : k == 1 ? 1 : 0);
                            int nc = gc + (k == 2 ? -1 : k == 3 ? 1 : 0);
                            if (nr < 0 || nr >= Size || nc < 0 || nc >= Size) continue;
                            int cell = nr * Size + nc;
                            int tile = code / pow[cell] % Cells;
                            // Slide the tile into the gap: it leaves its cell, the gap moves there.
                            int moved = code - tile * pow[cell] + tile * pow[gap];
                            if (distance.ContainsKey(moved)) continue;
                            distance[moved] = d;
                            next.Add(moved);
                        }
                    }
                    if (next.Count == 0) break;
                    layers.Add(next);
                }
                _byDistance = layers.ToArray();
                _distance = distance;
            }
        }

        // --- Test helpers -------------------------------------------------------------------

        /// <summary>For tests: set the board (tile numbers by cell, 0 the gap); par becomes its distance.</summary>
        public void SetBoard(params int[] board)
        {
            Array.Copy(board, _board, Cells);
            Gap = Array.IndexOf(_board, 0);
            Par = Distance(_board);
            Moves = 0;
        }

        /// <summary>For tests: set the clock.</summary>
        public void SetTime(float seconds) => TimeLeft = seconds;
    }
}
