using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.Noodle
{
    /// <summary>What happened during one <see cref="NoodleSim.Step"/>.</summary>
    [Flags]
    public enum NoodleEvent
    {
        None = 0,
        Moved = 1,
        Ate = 2,
        Gem = 4,
        GemShown = 8,
        GemGone = 16,
        Chili = 32,
        Crashed = 64,
    }

    /// <summary>
    /// The rules of Noodle, in plain C# so they can be tested. A noodle crawls one cell at a time
    /// over an 11 x 17 board whose edges wrap round. Swipes turn it (two can be queued, so quick
    /// swipes all count; turning straight back is ignored). Each pea scores 1, makes it one longer
    /// and a little faster. Every 5th pea brings a teal gem worth 5 that stays for a while, and a
    /// red chili that stays for good (up to 8). Crawling into a chili or into itself ends the run;
    /// crawling into the cell the tail is just leaving is fine.
    /// Cells are (column, row), with (0, 0) at the bottom left.
    /// </summary>
    public sealed class NoodleSim
    {
        public const int Cols = 11;
        public const int Rows = 17;
        public const int StartLength = 4;
        public static readonly Vector2Int StartHead = new Vector2Int(5, 4);
        public static readonly Vector2Int FirstPea = new Vector2Int(5, 13);

        /// <summary>Seconds per cell at the start, how much faster each pea makes it, and the fastest.</summary>
        public const float StartStep = 0.2f;
        public const float StepFaster = 0.003f;
        public const float MinStep = 0.12f;

        public const int PeaPoints = 1;
        public const int GemPoints = 5;

        /// <summary>Every this many peas, a gem and a chili appear.</summary>
        public const int BonusEvery = 5;

        /// <summary>How many cells the noodle crawls before an uneaten gem goes.</summary>
        public const int GemSteps = 40;

        /// <summary>Gems appear at least this far from the head (cells, counting the wrap).</summary>
        public const int GemMinDistance = 4;

        public const int MaxChilis = 8;

        /// <summary>Chilis appear at least this far from the head, so one never drops in front of it.</summary>
        public const int ChiliMinDistance = 5;

        /// <summary>Turns waiting for the next cell. Two lets a quick "up then left" both count.</summary>
        public const int MaxQueuedTurns = 2;

        public static readonly Vector2Int Up = new Vector2Int(0, 1);
        public static readonly Vector2Int Down = new Vector2Int(0, -1);
        public static readonly Vector2Int Left = new Vector2Int(-1, 0);
        public static readonly Vector2Int Right = new Vector2Int(1, 0);

        readonly System.Random _random;
        readonly List<Vector2Int> _body = new List<Vector2Int>();
        readonly List<Vector2Int> _before = new List<Vector2Int>();
        readonly List<Vector2Int> _chilis = new List<Vector2Int>();
        readonly List<Vector2Int> _turns = new List<Vector2Int>();
        readonly List<Vector2Int> _free = new List<Vector2Int>();
        int _grow;
        float _timer;

        /// <summary>The noodle, head first.</summary>
        public IReadOnlyList<Vector2Int> Body => _body;

        /// <summary>Where each part of <see cref="Body"/> was before the last step (for smooth drawing).</summary>
        public IReadOnlyList<Vector2Int> Before => _before;

        public IReadOnlyList<Vector2Int> Chilis => _chilis;
        public Vector2Int Head => _body[0];

        /// <summary>The way the noodle is heading (the last turn it took).</summary>
        public Vector2Int Dir { get; private set; } = Up;

        /// <summary>Turns swiped but not taken yet, oldest first.</summary>
        public IReadOnlyList<Vector2Int> QueuedTurns => _turns;

        public bool HasPea { get; private set; } = true;
        public Vector2Int Pea { get; private set; } = FirstPea;
        public bool HasGem { get; private set; }
        public Vector2Int Gem { get; private set; }
        public int GemStepsLeft { get; private set; }

        public int Score { get; private set; }
        public int Peas { get; private set; }
        public int Gems { get; private set; }
        public int Moves { get; private set; }
        public bool Crashed { get; private set; }
        public Vector2Int CrashCell { get; private set; }

        /// <summary>The cell where the last pea or gem was eaten.</summary>
        public Vector2Int EatenAt { get; private set; }

        /// <summary>A pea was eaten and the tail hasn't grown yet: it stays put on the next cell.</summary>
        public bool Growing => _grow > 0;

        /// <summary>Seconds per cell now.</summary>
        public float StepTime => Mathf.Max(MinStep, StartStep - StepFaster * Peas);

        /// <summary>How far through the current cell the noodle is, 0 to 1.</summary>
        public float Progress => Mathf.Clamp01(_timer / StepTime);

        public NoodleSim(int seed) : this(seed, StartLength) { }

        /// <summary>A noodle of any length, heading up with its tail straight down (tests use longer ones).</summary>
        public NoodleSim(int seed, int length)
        {
            _random = new System.Random(seed);
            for (int i = 0; i < length; i++) _body.Add(Wrap(StartHead - Up * i));
            _before.AddRange(_body);
        }

        /// <summary>Wraps a cell back onto the board.</summary>
        public static Vector2Int Wrap(Vector2Int c) =>
            new Vector2Int(((c.x % Cols) + Cols) % Cols, ((c.y % Rows) + Rows) % Rows);

        /// <summary>Cells apart, counting the shorter way round each edge.</summary>
        public static int Distance(Vector2Int a, Vector2Int b)
        {
            int dx = Math.Abs(a.x - b.x), dy = Math.Abs(a.y - b.y);
            return Math.Min(dx, Cols - dx) + Math.Min(dy, Rows - dy);
        }

        /// <summary>
        /// Queue a turn. Ignored if it's the way the noodle is already going (or will be, after the
        /// turns already queued), straight back, or the queue is full. Returns true if queued.
        /// </summary>
        public bool Turn(Vector2Int dir)
        {
            if (Crashed || Math.Abs(dir.x) + Math.Abs(dir.y) != 1) return false;
            var last = _turns.Count > 0 ? _turns[_turns.Count - 1] : Dir;
            if (dir == last || dir == -last || _turns.Count >= MaxQueuedTurns) return false;
            _turns.Add(dir);
            return true;
        }

        public bool Occupied(Vector2Int c) => _body.Contains(c) || _chilis.Contains(c) || (HasPea && Pea == c) || (HasGem && Gem == c);

        public NoodleEvent Step(float dt)
        {
            if (Crashed) return NoodleEvent.None;
            var events = NoodleEvent.None;
            _timer += dt;
            while (_timer >= StepTime && !Crashed)
            {
                _timer -= StepTime;
                events |= Move();
            }
            return events;
        }

        NoodleEvent Move()
        {
            if (_turns.Count > 0)
            {
                Dir = _turns[0];
                _turns.RemoveAt(0);
            }

            var next = Wrap(Head + Dir);
            bool tailLeaves = _grow == 0;
            int solid = tailLeaves ? _body.Count - 1 : _body.Count;
            for (int i = 0; i < solid; i++)
                if (_body[i] == next) return Crash(next);
            if (_chilis.Contains(next)) return Crash(next);

            _before.Clear();
            _before.AddRange(_body);
            _body.Insert(0, next);
            if (tailLeaves) _body.RemoveAt(_body.Count - 1);
            else
            {
                _grow--;
                _before.Add(_body[_body.Count - 1]); // the new tail end stays where it was
            }
            Moves++;
            var events = NoodleEvent.Moved;

            if (HasPea && next == Pea)
            {
                Score += PeaPoints;
                Peas++;
                _grow++;
                EatenAt = next;
                events |= NoodleEvent.Ate;
                HasPea = false;
                if (Peas % BonusEvery == 0)
                {
                    if (!HasGem && PlaceGem()) events |= NoodleEvent.GemShown;
                    if (_chilis.Count < MaxChilis && PlaceChili()) events |= NoodleEvent.Chili;
                }
                PlacePea();
            }
            else if (HasGem && next == Gem)
            {
                Score += GemPoints;
                Gems++;
                HasGem = false;
                EatenAt = next;
                events |= NoodleEvent.Gem;
            }

            if (HasGem && (events & NoodleEvent.GemShown) == 0 && --GemStepsLeft <= 0)
            {
                HasGem = false;
                events |= NoodleEvent.GemGone;
            }
            return events;
        }

        NoodleEvent Crash(Vector2Int at)
        {
            Crashed = true;
            CrashCell = at;
            _turns.Clear();
            return NoodleEvent.Crashed;
        }

        void PlacePea()
        {
            HasPea = Pick(c => true, out var cell);
            if (HasPea) Pea = cell;
        }

        bool PlaceGem()
        {
            if (!Pick(c => Distance(c, Head) >= GemMinDistance, out var cell)) return false;
            HasGem = true;
            Gem = cell;
            GemStepsLeft = GemSteps;
            return true;
        }

        /// <summary>
        /// Chilis keep clear of the head, and of each other (not even touching corners), so no free
        /// cell is ever walled in by chilis alone.
        /// </summary>
        bool PlaceChili()
        {
            if (!Pick(c => Distance(c, Head) >= ChiliMinDistance && !TouchesChili(c), out var cell)) return false;
            _chilis.Add(cell);
            return true;
        }

        public bool TouchesChili(Vector2Int c)
        {
            foreach (var chili in _chilis)
            {
                int dx = Math.Abs(c.x - chili.x), dy = Math.Abs(c.y - chili.y);
                if (Math.Min(dx, Cols - dx) <= 1 && Math.Min(dy, Rows - dy) <= 1) return true;
            }
            return false;
        }

        bool Pick(Func<Vector2Int, bool> allowed, out Vector2Int cell)
        {
            _free.Clear();
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                var c = new Vector2Int(x, y);
                if (!Occupied(c) && allowed(c)) _free.Add(c);
            }
            cell = _free.Count > 0 ? _free[_random.Next(_free.Count)] : default;
            return _free.Count > 0;
        }
    }

    /// <summary>
    /// Turns one finger into steering. Unlike a plain swipe, the finger can stay down: every time
    /// it has moved <see cref="TurnMove"/> units one way since the last turn, that's a turn. So a
    /// swipe up then left in one stroke is two turns, and a slow drag still steers.
    /// </summary>
    public sealed class NoodleSteer
    {
        public const float TurnMove = 0.6f;

        Vector2 _anchor;
        bool _held;

        /// <summary>Feed the finger each frame; returns a direction on the frame a turn is made, else zero.</summary>
        public Vector2Int Feed(bool pressed, Vector2 position)
        {
            if (!pressed) { _held = false; return Vector2Int.zero; }
            if (!_held) { _held = true; _anchor = position; return Vector2Int.zero; }

            var moved = position - _anchor;
            bool across = Mathf.Abs(moved.x) >= Mathf.Abs(moved.y);
            float along = across ? moved.x : moved.y;
            if (Mathf.Abs(along) < TurnMove) return Vector2Int.zero;

            _anchor = position;
            return across ? new Vector2Int(Math.Sign(along), 0) : new Vector2Int(0, Math.Sign(along));
        }
    }
}
