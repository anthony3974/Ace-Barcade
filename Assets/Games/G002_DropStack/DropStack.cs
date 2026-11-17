using UnityEngine;

namespace AceBarcade.Games.DropStack
{
    /// <summary>
    /// Game 002. Blocks fall into a well; fill a row to clear it. Tap to turn the piece, drag to
    /// slide it (one column per block-width of finger travel), drag down to drop it faster, and
    /// flick down to drop it all the way. Gem blocks pay a bonus when their row clears.
    /// The rules live in <see cref="DropSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(2, "drop-stack", "Drop Stack", Controls.Drag, Blurb = "Fill a row to clear it")]
    public sealed class DropStack : ArcadeGame
    {
        /// <summary>One cell, in units (a block sprite is 108 px at 120 px per unit).</summary>
        public const float Cell = 0.9f;
        public const float Left = -DropSim.Cols * Cell / 2f;
        public const float Bottom = -7.4f;
        public const float Top = Bottom + DropSim.Rows * Cell;

        const float PreviewScale = 0.45f;
        static readonly Vector2 PreviewCenter = new Vector2(0f, Top + 0.68f);

        static readonly string[] BlockNames = { null, "block_blue", "block_pink", "block_yellow", "block_purple", "block_orange", "block_green", "block_gem" };

        DropSim _sim;
        Sprite[] _blocks;
        SpriteRenderer[,] _cells;
        SpriteRenderer[] _piece, _ghost, _preview;
        Transform _pieceRoot;
        Vector2 _drag;
        bool _skipTap;
        bool _boardDirty = true;

        public DropSim Sim => _sim;

        public static Vector2 CellCenter(Vector2Int c) => new Vector2(Left + (c.x + 0.5f) * Cell, Bottom + (c.y + 0.5f) * Cell);

        protected override void Build()
        {
            _sim = new DropSim(System.Environment.TickCount);

            _blocks = new Sprite[BlockNames.Length];
            for (int i = 1; i < BlockNames.Length; i++) _blocks[i] = GameSprite(BlockNames[i]);

            BuildWell();

            _cells = new SpriteRenderer[DropSim.Cols, DropSim.Rows];
            for (int x = 0; x < DropSim.Cols; x++)
            for (int y = 0; y < DropSim.Rows; y++)
            {
                _cells[x, y] = MakeSprite($"Cell {x},{y}", null, CellCenter(new Vector2Int(x, y)), 5);
                _cells[x, y].enabled = false;
            }

            _ghost = new SpriteRenderer[4];
            for (int i = 0; i < 4; i++)
                _ghost[i] = Shapes.Make(transform, Shapes.Square, Palette.Slate, Vector2.zero, new Vector2(Cell * 0.7f, Cell * 0.7f), 2, "Ghost");

            _pieceRoot = Make("Piece").transform;
            _piece = new SpriteRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                _piece[i] = new GameObject("Block").AddComponent<SpriteRenderer>();
                _piece[i].transform.SetParent(_pieceRoot, false);
                _piece[i].sortingOrder = 10;
            }

            var previewRoot = Make("Next", PreviewCenter).transform;
            previewRoot.localScale = Vector3.one * PreviewScale;
            _preview = new SpriteRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                _preview[i] = new GameObject("Block").AddComponent<SpriteRenderer>();
                _preview[i].transform.SetParent(previewRoot, false);
                _preview[i].sortingOrder = 10;
            }

            SyncAll();
        }

        void BuildWell()
        {
            var t = transform;
            float w = DropSim.Cols * Cell, h = DropSim.Rows * Cell;
            var center = new Vector2(0f, Bottom + h / 2f);

            // The well's floor colour, with a dot in each cell so columns are easy to line up.
            Shapes.Make(t, Shapes.Square, Palette.Night, center, new Vector2(w, h), -5, "Well");
            for (int x = 0; x < DropSim.Cols; x++)
            for (int y = 0; y < DropSim.Rows; y++)
                Shapes.Make(t, Shapes.Circle, Palette.Slate, CellCenter(new Vector2Int(x, y)), new Vector2(0.12f, 0.12f), -4, "Dot");

            // Blue walls and floor, like Paddle Rally's, reaching past the screen edges.
            const float thick = 1.4f;
            float wallTop = Top + 0.3f, wallBottom = -14f;
            float midY = (wallTop + wallBottom) / 2f, height = wallTop - wallBottom;
            Shapes.Box(t, new Vector2(Left - thick / 2f, midY), new Vector2(thick, height), Palette.Blue, Palette.BlueDark, 0, "WallLeft");
            Shapes.Box(t, new Vector2(-Left + thick / 2f, midY), new Vector2(thick, height), Palette.Blue, Palette.BlueDark, 0, "WallRight");
            Shapes.Box(t, new Vector2(0f, Bottom - 3f), new Vector2(w + thick * 2f, 6f), Palette.Blue, Palette.BlueDark, 3, "Floor");

            // The top of the well is the danger line: a piece that can't appear there ends the run.
            Shapes.Box(t, new Vector2(0f, Top + 0.12f), new Vector2(w + 0.2f, 0.24f), Palette.Red, Palette.RedDark, 4, "DangerLine");
        }

        protected override void OnBegin()
        {
            _skipTap = true; // the touch that started the game isn't a turn
            _drag = Vector2.zero;
        }

        protected override void Play(float dt)
        {
            int before = _sim.Score;
            var events = DropEvent.None;

            if (Input.Down) _drag = Vector2.zero;
            if (Input.Up && _skipTap) _skipTap = false;
            else if (Input.Tapped) _sim.Rotate();

            if (Input.Pressed)
            {
                _drag += Input.DragDelta;
                while (Mathf.Abs(_drag.x) >= Cell)
                {
                    int dir = _drag.x > 0f ? 1 : -1;
                    if (!_sim.Move(dir)) { _drag.x = 0f; break; }
                    _drag.x -= dir * Cell;
                }
                while (_drag.y <= -Cell)
                {
                    if (!_sim.SoftDrop()) { _drag.y = 0f; break; }
                    _drag.y += Cell;
                }
                if (_drag.y > 0f) _drag.y = 0f; // dragging up does nothing
            }

            if (Input.Swipe == Vector2.down) events |= _sim.HardDrop();
            if (!_sim.Lost) events |= _sim.Step(dt);

            AddScore(_sim.Score - before);
            React(events);
            SyncAll();
        }

        void React(DropEvent events)
        {
            if ((events & DropEvent.Locked) != 0)
            {
                _boardDirty = true;
                _drag = Vector2.zero;
            }

            if ((events & DropEvent.Cleared) != 0)
            {
                foreach (int row in _sim.LastCleared)
                {
                    var y = CellCenter(new Vector2Int(0, row)).y;
                    var flash = Shapes.Make(transform, Shapes.Square, Palette.Paper, new Vector2(0f, y), new Vector2(DropSim.Cols * Cell, Cell), 20, "Flash");
                    flash.gameObject.AddComponent<RowFlash>();
                    for (int x = 0; x < DropSim.Cols; x += 2)
                        Juice.Burst(transform, CellCenter(new Vector2Int(x, row)), Palette.Yellow, 3, 4f);
                }
                if (_sim.LastCleared.Count >= 3) Juice.ShakeBig(); else Juice.ShakeSmall();
            }

            if ((events & DropEvent.Gem) != 0)
                foreach (int row in _sim.LastCleared)
                    Juice.Burst(transform, new Vector2(0f, CellCenter(new Vector2Int(0, row)).y), Palette.Teal, 14, 6f);

            if ((events & DropEvent.Lost) != 0)
            {
                Juice.Burst(transform, new Vector2(0f, Top), Palette.Red, 18, 6f);
                Juice.ShakeBig();
                EndRun();
            }
        }

        void SyncAll()
        {
            if (_boardDirty)
            {
                _boardDirty = false;
                for (int x = 0; x < DropSim.Cols; x++)
                for (int y = 0; y < DropSim.Rows; y++)
                {
                    int v = _sim.Cells[x, y];
                    _cells[x, y].enabled = v != 0;
                    _cells[x, y].sprite = v != 0 ? _blocks[v] : null;
                }
            }

            bool showPiece = !_sim.Lost && State != Phase.Ready; // hidden behind the title until play starts
            var cells = _sim.PieceCells();
            var ghost = _sim.GhostCells();
            int color = DropSim.ColorOf(_sim.Kind);
            for (int i = 0; i < 4; i++)
            {
                bool used = showPiece && i < cells.Length;
                _piece[i].enabled = used;
                _ghost[i].enabled = used && ghost[0] != cells[0];
                if (!used) continue;
                _piece[i].sprite = _blocks[i == _sim.GemIndex ? DropSim.GemCell : color];
                _piece[i].transform.localPosition = CellCenter(cells[i]);
                _ghost[i].transform.localPosition = CellCenter(ghost[i]);
            }

            var next = DropSim.ShapeOf(_sim.NextKind);
            var offset = PreviewOffset(next);
            for (int i = 0; i < 4; i++)
            {
                bool used = i < next.Length;
                _preview[i].enabled = used;
                if (!used) continue;
                _preview[i].sprite = _blocks[DropSim.ColorOf(_sim.NextKind)];
                _preview[i].transform.localPosition = (Vector2)next[i] * Cell - offset;
            }
        }

        /// <summary>Centres a shape's blocks on the preview spot.</summary>
        static Vector2 PreviewOffset(Vector2Int[] shape)
        {
            Vector2 min = shape[0], max = shape[0];
            foreach (var c in shape) { min = Vector2.Min(min, c); max = Vector2.Max(max, c); }
            return (min + max) / 2f * Cell;
        }

        void LateUpdate()
        {
            if (State == Phase.Ready) SyncAll();
        }
    }

    /// <summary>A cleared row's Paper flash: it squeezes flat and disappears.</summary>
    sealed class RowFlash : MonoBehaviour
    {
        const float Life = 0.22f;
        float _age;
        Vector3 _size;

        void Start() => _size = transform.localScale;

        void Update()
        {
            _age += Time.deltaTime;
            if (_age >= Life) { Destroy(gameObject); return; }
            transform.localScale = new Vector3(_size.x, _size.y * (1f - _age / Life), 1f);
        }
    }
}
