using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AceBarcade.Games.PetalSwap
{
    /// <summary>
    /// Game 016. Match three in a 7 x 7 garden: swipe a flower to swap it with its neighbour (or
    /// tap one, then a neighbour). Three or more of a colour in a line pop, the rest fall and new
    /// flowers drop in from behind the hedge; cascades are chains, worth more each wave. Every
    /// popped flower tops up the clock, which only runs while the garden is still. A swap that
    /// makes no match slides back at no cost; a garden with no moves shuffles itself.
    /// The rules live in <see cref="PetalSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(16, "petal-swap", "Petal Swap", Controls.Swipe, Blurb = "Swap flowers to line up three")]
    public sealed class PetalSwap : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        public const float Cell = 1.16f;
        public const float BottomRowY = -3.9f;
        const float LeftColX = -3f * Cell;
        const float BoardTop = BottomRowY + (PetalSim.Size - 0.5f) * Cell;
        const float HedgeHeight = 1f;

        // A flower is hidden once it's far enough up to be behind the hedge.
        const float HiddenAboveRow = PetalSim.Size - 0.5f + 0.43f;

        const float BarY = BoardTop + HedgeHeight + 0.95f;
        const float BarHeight = 0.5f;
        const float WarnSeconds = 5f;

        const float DragToSwap = 0.45f;
        const float HintAfter = 5f;

        static readonly string[] Names = { "pink", "orange", "purple", "blue", "white" };
        static readonly Color[] BurstColors = { Palette.Pink, Palette.Orange, Palette.Purple, Palette.Sky, Palette.Paper };

        sealed class View
        {
            public SpriteRenderer Sprite;
            public int Color = -1;
            public float Drop;
        }

        sealed class Floater
        {
            public TextMeshPro Text;
            public float Age, Life;
            public Vector2 From;
        }

        PetalSim _sim;
        Sprite[] _flowers;
        readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _gone = new List<int>();
        readonly HashSet<int> _popping = new HashSet<int>();
        readonly List<Floater> _floaters = new List<Floater>();
        SpriteRenderer _select, _barFill;
        float _barWidth;

        bool _startTouch, _dragging;
        Vector2Int _downCell;
        Vector2 _downPos;
        Vector2Int? _selected;

        public PetalSim Sim => _sim;
        public Vector2Int? Selected => _selected;

        public static Vector2 CellCenter(int col, int row) => new Vector2(LeftColX + col * Cell, BottomRowY + row * Cell);
        public static Vector2 CellCenter(Vector2Int cell) => CellCenter(cell.x, cell.y);

        /// <summary>The cell under a world position, or (-1, -1).</summary>
        public static Vector2Int CellAt(Vector2 p)
        {
            int col = Mathf.RoundToInt((p.x - LeftColX) / Cell);
            int row = Mathf.RoundToInt((p.y - BottomRowY) / Cell);
            return PetalSim.In(col, row) ? new Vector2Int(col, row) : new Vector2Int(-1, -1);
        }

        protected override void Build()
        {
            _sim = new PetalSim(SeedForTests ?? System.Environment.TickCount);
            _flowers = new Sprite[Names.Length];
            for (int i = 0; i < Names.Length; i++) _flowers[i] = GameSprite($"flower_{Names[i]}");
            var t = transform;

            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-3.6f, 6.4f), new Vector2(3f, 3f), -20, "Decor");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(3.9f, -6.6f), new Vector2(2.6f, 2.6f), -20, "Decor");

            // The garden bed: a wooden frame, and grass in a checker under the flowers.
            float side = Cell * PetalSim.Size;
            var mid = new Vector2(0f, BottomRowY + (PetalSim.Size - 1) * Cell / 2f);
            Shapes.Box(t, mid + new Vector2(0f, -0.04f), new Vector2(side + 0.44f, side + 0.52f), Palette.Brown, Palette.BrownDark, 0, "Frame");
            for (int c = 0; c < PetalSim.Size; c++)
                for (int r = 0; r < PetalSim.Size; r++)
                    Shapes.Make(t, Shapes.Square, (c + r) % 2 == 0 ? Palette.Green : Palette.GreenDark, CellCenter(c, r), new Vector2(Cell, Cell), 4, "Grass");

            // The hedge along the top: new flowers drop in from behind it.
            float hedgeY = BoardTop + HedgeHeight / 2f;
            Shapes.Box(t, new Vector2(0f, hedgeY), new Vector2(side + 0.44f, HedgeHeight), Palette.Green, Palette.GreenDark, 20, "Hedge");
            for (int i = 0; i <= 8; i++)
            {
                float x = -side / 2f + i * side / 8f;
                Shapes.Make(t, Shapes.Circle, Palette.Ink, new Vector2(x, BoardTop + HedgeHeight - 0.05f), Vector2.one * (0.9f + Style.Outline * 2f), 18, "Bush");
                Shapes.Make(t, Shapes.Circle, Palette.Green, new Vector2(x, BoardTop + HedgeHeight - 0.05f), Vector2.one * 0.9f, 19, "Bush");
            }

            BuildClock(side + 0.44f);

            _select = MakeSprite("Select", GameSprite("select"), Vector2.zero, 12);
            _select.transform.localScale = Vector3.one * 0.95f;
            _select.enabled = false;

            SyncViews(0f);
        }

        void BuildClock(float width)
        {
            var t = transform;
            float o = Style.Outline * 2f;
            Shapes.Make(t, Shapes.Square, Palette.Ink, new Vector2(0f, BarY), new Vector2(width, BarHeight), 0, "ClockOutline");
            Shapes.Make(t, Shapes.Square, Palette.Night, new Vector2(0f, BarY), new Vector2(width - o, BarHeight - o), 1, "ClockBack");
            _barWidth = width - o;
            _barFill = Shapes.Make(t, Shapes.Square, Palette.Teal, new Vector2(0f, BarY), new Vector2(_barWidth, BarHeight - o), 2, "ClockFill");
        }

        protected override void OnBegin() => _startTouch = true;

        protected override void Play(float dt)
        {
            HandleInput();

            int before = _sim.Score;
            var events = _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & PetalEvent.SwappedBack) != 0) Juice.ShakeSmall();
            if ((events & PetalEvent.Popped) != 0) Popped();
            if ((events & PetalEvent.Shuffled) != 0)
            {
                foreach (var v in _views.Values) Juice.Pop(v.Sprite.transform);
                Float("SHUFFLE!", new Vector2(0f, CellCenter(3, 3).y), 9f, Palette.Paper, 1.2f);
            }
            if ((events & PetalEvent.TimeUp) != 0)
            {
                _select.enabled = false;
                Juice.Burst(transform, new Vector2(-_barWidth / 2f, BarY), Palette.Red, 14, 5f);
                Juice.ShakeBig();
                EndRun();
            }
        }

        void HandleInput()
        {
            // The touch that started the game doesn't swap or select anything.
            if (_startTouch)
            {
                if (Input.Up || !Input.Pressed) _startTouch = false;
                return;
            }

            if (Input.Down)
            {
                _downCell = CellAt(Input.Position);
                _downPos = Input.Position;
                _dragging = _downCell.x >= 0;
            }

            // Swipe: drag a flower far enough toward a neighbour and they swap (it waits for the
            // garden to settle if it's still moving).
            if (_dragging && Input.Pressed)
            {
                var d = Input.Position - _downPos;
                if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)) >= DragToSwap)
                {
                    var dir = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? new Vector2Int((int)Mathf.Sign(d.x), 0) : new Vector2Int(0, (int)Mathf.Sign(d.y));
                    var to = _downCell + dir;
                    if (!PetalSim.In(to.x, to.y)) _dragging = false;
                    else if (TrySwap(_downCell, dir)) _dragging = false;
                }
            }
            if (Input.Up) _dragging = false;

            // Tap: pick a flower, then tap a neighbour to swap with it.
            if (Input.Tapped)
            {
                var cell = CellAt(Input.Position);
                if (cell.x < 0 || _selected == cell) _selected = null;
                else if (_selected is Vector2Int s && Mathf.Abs(s.x - cell.x) + Mathf.Abs(s.y - cell.y) == 1)
                {
                    if (!TrySwap(s, cell - s)) _selected = cell;
                }
                else
                {
                    _selected = cell;
                    if (_views.TryGetValue(_sim.At(cell.x, cell.y).Id, out var v)) Juice.Squash(v.Sprite.transform, 0.12f);
                }
            }
        }

        bool TrySwap(Vector2Int cell, Vector2Int dir)
        {
            if (!_sim.Swap(cell, dir)) return false;
            _selected = null;
            return true;
        }

        void Popped()
        {
            _popping.Clear();
            var sum = Vector2.zero;
            foreach (var f in _sim.Popped)
            {
                _popping.Add(f.Id);
                var at = CellCenter(f.Col, f.Row);
                sum += at;
                Juice.Burst(transform, at, BurstColors[f.Color], 6, 3.5f);
            }
            var centre = sum / Mathf.Max(1, _sim.Popped.Count);
            Float($"+{_sim.LastPoints}", centre, 8f, Palette.Yellow, 0.8f);
            if (_sim.Chain >= 2)
            {
                Float($"CHAIN x{_sim.Chain}", centre + new Vector2(0f, 0.9f), 6f, Palette.Lime, 0.9f);
                Juice.ShakeSmall();
            }
            else if (_sim.Popped.Count >= 5) Juice.ShakeSmall();
        }

        void Float(string text, Vector2 at, float size, Color color, float life)
        {
            var go = new GameObject("Float");
            go.transform.SetParent(transform, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = UiKit.Font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
            tmp.sortingOrder = 40;
            at.x = Mathf.Clamp(at.x, -2.6f, 2.6f);
            go.transform.localPosition = at;
            Juice.Pop(go.transform);
            _floaters.Add(new Floater { Text = tmp, Life = life, From = at });
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            _seen.Clear();
            bool popping = _sim.State == PetalSim.Phase.Popping;
            float p = _sim.Progress;

            // A still garden shows a hint after a while: one flower of a move wiggles.
            int hintId = -1;
            if (_sim.State == PetalSim.Phase.Still && _sim.StillFor > HintAfter && _sim.FindMove(out var hc, out _))
                hintId = _sim.At(hc.x, hc.y).Id;

            for (int c = 0; c < PetalSim.Size; c++)
                for (int r = 0; r < PetalSim.Size; r++)
                {
                    var f = _sim.At(c, r);
                    _seen.Add(f.Id);
                    if (!_views.TryGetValue(f.Id, out var v))
                    {
                        v = new View { Sprite = MakeSprite($"Flower {f.Id}", null, Vector2.zero, 10), Drop = f.Drop };
                        _views[f.Id] = v;
                    }
                    if (v.Color != f.Color)
                    {
                        v.Color = f.Color;
                        v.Sprite.sprite = _flowers[f.Color];
                    }

                    var at = _sim.DrawPos(f);
                    var tr = v.Sprite.transform;
                    tr.localPosition = new Vector2(LeftColX + at.x * Cell, BottomRowY + at.y * Cell);
                    v.Sprite.enabled = at.y <= HiddenAboveRow;
                    v.Sprite.sortingOrder = 10;

                    // Landing from a fall squashes a little.
                    if (v.Drop > 0f && f.Drop <= 0f) Juice.Squash(tr, 0.14f);
                    v.Drop = f.Drop;

                    if (popping && _popping.Contains(f.Id))
                    {
                        // Swell, then shrink away.
                        float s = p < 0.3f ? 1f + p : 1.3f * (1f - (p - 0.3f) / 0.7f);
                        tr.localScale = Vector3.one * Mathf.Max(0f, s);
                        v.Sprite.sortingOrder = 11;
                    }

                    tr.localRotation = f.Id == hintId
                        ? Quaternion.Euler(0f, 0f, Mathf.Sin((_sim.StillFor - HintAfter) * 14f) * 12f)
                        : Quaternion.identity;
                }

            _gone.Clear();
            foreach (var id in _views.Keys) if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                Destroy(_views[id].Sprite.gameObject);
                _views.Remove(id);
            }

            if (_selected is Vector2Int sel && !_sim.Over)
            {
                _select.enabled = true;
                _select.transform.localPosition = CellCenter(sel);
            }
            else _select.enabled = false;

            float share = Mathf.Clamp01(_sim.TimeLeft / PetalSim.MaxSeconds);
            float w = _barWidth * share;
            _barFill.transform.localScale = new Vector3(w, _barFill.transform.localScale.y, 1f);
            _barFill.transform.localPosition = new Vector2(-_barWidth / 2f + w / 2f, BarY);
            _barFill.color = _sim.TimeLeft <= WarnSeconds ? Palette.Red : Palette.Teal;

            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var fl = _floaters[i];
                fl.Age += dt;
                if (fl.Age >= fl.Life)
                {
                    Destroy(fl.Text.gameObject);
                    _floaters.RemoveAt(i);
                    continue;
                }
                fl.Text.transform.localPosition = fl.From + new Vector2(0f, fl.Age * 1.2f);
            }
        }
    }
}
