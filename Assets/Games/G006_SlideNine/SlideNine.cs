using TMPro;
using UnityEngine;

namespace AceBarcade.Games.SlideNine
{
    /// <summary>
    /// Game 006. A 3 x 3 sliding puzzle against the clock. Tap a tile in the gap's row or column
    /// to slide it; put 1 to 8 in order to solve. Each solve scores 10 plus up to 10 for few
    /// moves and adds 15 seconds, then a harder puzzle comes. The run ends when time runs out.
    /// Tiles in their own cell turn green. The rules live in <see cref="SlideSim"/>.
    /// </summary>
    [ArcadeGame(6, "slide-nine", "Slide Nine", Controls.Tap, Blurb = "Solve as many as you can before time runs out")]
    public sealed class SlideNine : ArcadeGame
    {
        public const float BoardY = -0.4f;
        public const float Step = 2.52f;
        const float TileSlide = 24f;
        const float TrayPad = 0.32f;

        // The clock: a bar above the board that empties as time runs out.
        const float BarY = 4.3f;
        const float BarHeight = 0.52f;
        const float WarnSeconds = 10f;

        SlideSim _sim;
        Sprite _tile, _tileHome;
        readonly SpriteRenderer[] _tiles = new SpriteRenderer[SlideSim.Cells]; // by tile number (0 unused)
        readonly int[] _cellOf = new int[SlideSim.Cells];                       // where each tile was last frame
        SpriteRenderer _barFill;
        float _barWidth;
        TextMeshPro _movesText, _pointsText, _timeText;
        float _pointsFor, _timeFor;
        int _shownMoves = -1;
        bool _startTouch;

        public SlideSim Sim => _sim;

        public static Vector2 CellCenter(int cell) =>
            new Vector2((cell % SlideSim.Size - 1) * Step, BoardY - (cell / SlideSim.Size - 1) * Step);

        /// <summary>The cell under a world position, or -1.</summary>
        public static int CellAt(Vector2 p)
        {
            int col = Mathf.RoundToInt(p.x / Step) + 1;
            int row = Mathf.RoundToInt((BoardY - p.y) / Step) + 1;
            if (col < 0 || col >= SlideSim.Size || row < 0 || row >= SlideSim.Size) return -1;
            return row * SlideSim.Size + col;
        }

        protected override void Build()
        {
            _sim = new SlideSim(System.Environment.TickCount);
            _tile = GameSprite("tile");
            _tileHome = GameSprite("tile_home");

            var t = transform;
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-3.4f, 5.6f), new Vector2(3.4f, 3.4f), -20, "Decor");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(3.8f, -5.8f), new Vector2(2.8f, 2.8f), -20, "Decor");

            // The tray the tiles slide in: Blue, a neutral solid thing.
            float tray = Step * SlideSim.Size + TrayPad;
            Shapes.Box(t, new Vector2(0f, BoardY), new Vector2(tray, tray), Palette.Blue, Palette.BlueDark, 0, "Tray");

            for (int n = 1; n < SlideSim.Cells; n++)
            {
                _tiles[n] = MakeSprite($"Tile {n}", _tile, Vector2.zero, 10);
                var label = MakeText(_tiles[n].transform, n.ToString(), 15f, Palette.Ink, 11);
                label.transform.localPosition = new Vector2(0f, 0.08f);
            }

            BuildClock();

            _movesText = MakeText(t, "", 5f, Palette.Paper, 12);
            _movesText.transform.localPosition = new Vector2(0f, BoardY - tray / 2f - 0.55f);
            _pointsText = MakeText(t, "", 20f, Palette.Yellow, 40);
            _pointsText.transform.localPosition = new Vector2(0f, BoardY);
            _pointsText.gameObject.SetActive(false);
            _timeText = MakeText(t, "", 7f, Palette.Lime, 40);
            _timeText.transform.localPosition = new Vector2(0f, BarY - 0.9f);
            _timeText.gameObject.SetActive(false);

            SnapTiles();
            SyncViews(0f);
        }

        void BuildClock()
        {
            var t = transform;
            float width = Step * SlideSim.Size + TrayPad;
            float o = Style.Outline * 2f;
            Shapes.Make(t, Shapes.Square, Palette.Ink, new Vector2(0f, BarY), new Vector2(width, BarHeight), 0, "ClockOutline");
            Shapes.Make(t, Shapes.Square, Palette.Night, new Vector2(0f, BarY), new Vector2(width - o, BarHeight - o), 1, "ClockBack");
            _barWidth = width - o;
            _barFill = Shapes.Make(t, Shapes.Square, Palette.Teal, new Vector2(0f, BarY), new Vector2(_barWidth, BarHeight - o), 2, "ClockFill");
        }

        TextMeshPro MakeText(Transform parent, string text, float size, Color color, int order)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = UiKit.Font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
            tmp.sortingOrder = order;
            return tmp;
        }

        protected override void OnBegin() => _startTouch = true;

        protected override void Play(float dt)
        {
            // The touch that started the game isn't a move, even if it was a quick tap.
            bool tapped = Input.Tapped;
            if (_startTouch && (Input.Up || !Input.Pressed)) { _startTouch = false; tapped = false; }

            int before = _sim.Score;
            var events = SlideEvent.None;
            if (tapped && !_startTouch) events |= _sim.Tap(CellAt(Input.Position));
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & SlideEvent.Moved) != 0) KickMovedTiles();
            if ((events & SlideEvent.Solved) != 0)
            {
                Juice.Burst(transform, new Vector2(0f, BoardY), Palette.Lime, 20, 7f);
                Juice.ShakeSmall();
                for (int n = 1; n < SlideSim.Cells; n++) Juice.Squash(_tiles[n].transform, 0.12f);
                Flash(_pointsText, $"+{_sim.LastSolvePoints}", ref _pointsFor, SlideSim.SolvePause);
                Flash(_timeText, $"+{SlideSim.SolveBonusSeconds:0}s", ref _timeFor, 1.2f);
            }
            if ((events & SlideEvent.NewPuzzle) != 0)
            {
                SnapTiles();
                for (int n = 1; n < SlideSim.Cells; n++) Juice.Pop(_tiles[n].transform);
            }
            if ((events & SlideEvent.TimeUp) != 0)
            {
                Juice.Burst(transform, new Vector2(-_barWidth / 2f, BarY), Palette.Red, 14, 5f);
                Juice.ShakeBig();
                EndRun();
            }
        }

        static void Flash(TextMeshPro text, string value, ref float timer, float seconds)
        {
            text.text = value;
            text.gameObject.SetActive(true);
            Juice.Pop(text.transform);
            timer = seconds;
        }

        void KickMovedTiles()
        {
            for (int cell = 0; cell < SlideSim.Cells; cell++)
            {
                int n = _sim.Board[cell];
                if (n == 0 || _cellOf[n] == cell) continue;
                _cellOf[n] = cell;
                Juice.Squash(_tiles[n].transform, 0.1f);
            }
        }

        void SnapTiles()
        {
            for (int cell = 0; cell < SlideSim.Cells; cell++)
            {
                int n = _sim.Board[cell];
                if (n == 0) continue;
                _cellOf[n] = cell;
                _tiles[n].transform.localPosition = CellCenter(cell);
            }
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            for (int cell = 0; cell < SlideSim.Cells; cell++)
            {
                int n = _sim.Board[cell];
                if (n == 0) continue;
                var tile = _tiles[n].transform;
                tile.localPosition = Vector2.MoveTowards(tile.localPosition, CellCenter(cell), TileSlide * dt);
                _tiles[n].sprite = _sim.Home(cell) ? _tileHome : _tile;
            }

            float share = Mathf.Clamp01(_sim.TimeLeft / SlideSim.MaxSeconds);
            float w = _barWidth * share;
            _barFill.transform.localScale = new Vector3(w, _barFill.transform.localScale.y, 1f);
            _barFill.transform.localPosition = new Vector2(-_barWidth / 2f + w / 2f, BarY);
            _barFill.color = _sim.TimeLeft <= WarnSeconds ? Palette.Red : Palette.Teal;

            int shown = _sim.Moves * 100 + _sim.Par;
            if (shown != _shownMoves)
            {
                _shownMoves = shown;
                _movesText.text = $"MOVES {_sim.Moves}   PAR {_sim.Par}";
            }

            if (_pointsFor > 0f && (_pointsFor -= dt) <= 0f) _pointsText.gameObject.SetActive(false);
            if (_timeFor > 0f && (_timeFor -= dt) <= 0f) _timeText.gameObject.SetActive(false);
        }
    }
}
