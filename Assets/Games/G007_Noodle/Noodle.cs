using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.Noodle
{
    /// <summary>
    /// Game 007. A yellow noodle crawls over a board whose edges wrap round. Swipe (or drag) to
    /// steer it; each green pea scores 1 and makes it longer and faster, teal gems score 5, and
    /// crawling into a red chili or into itself ends the run.
    /// The rules live in <see cref="NoodleSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(7, "noodle", "Noodle", Controls.Swipe, Blurb = "Eat the peas, not the chilies or your own tail")]
    public sealed class Noodle : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        public const float CellSize = 0.75f;
        public const float BoardY = -0.6f;
        public const float BoardWidth = NoodleSim.Cols * CellSize;
        public const float BoardHeight = NoodleSim.Rows * CellSize;
        const float BoardLeft = -BoardWidth / 2f;
        const float BoardBottom = BoardY - BoardHeight / 2f;

        // The noodle is a tube of overlapping circles: an Ink ring, a dark shade, then the lit
        // top-left of each circle on top, so neighbours melt into one outlined shape.
        const float BodySize = 0.6f;
        const float HeadSize = 0.82f;
        const float LitShrink = 0.8f;
        const int InkOrder = 10, ShadeOrder = 11, LitOrder = 12, EyesOrder = 14;

        const float BulgeSpeed = 2f;   // segments per step a swallowed pea travels down the noodle
        const float BulgeSize = 0.32f;
        const float EyesTurn = 900f;   // degrees per second
        const int GemBlinkSteps = 8;

        NoodleSim _sim;
        readonly NoodleSteer _steer = new NoodleSteer();

        Transform _head, _eyes;
        readonly List<Transform> _parts = new List<Transform>();  // segment, gap, segment, gap... (head excluded)
        readonly List<float> _bulges = new List<float>();
        SpriteRenderer _pea, _gem;
        readonly List<SpriteRenderer> _chilis = new List<SpriteRenderer>();
        Sprite _chiliSprite;
        Vector2Int _peaShown = new Vector2Int(-1, -1);
        float _eyesAngle;

        public NoodleSim Sim => _sim;

        /// <summary>The world position of a cell's centre.</summary>
        public static Vector2 CellCenter(Vector2Int c) => CellToWorld(c);

        static Vector2 CellToWorld(Vector2 c) =>
            new Vector2(BoardLeft + (c.x + 0.5f) * CellSize, BoardBottom + (c.y + 0.5f) * CellSize);

        protected override void Build()
        {
            _sim = new NoodleSim(SeedForTests ?? System.Environment.TickCount);
            _chiliSprite = GameSprite("chili");
            var t = transform;

            // The board: Slate with a Night dot in every cell (background, so no outline).
            var boardCentre = new Vector2(0f, BoardY);
            Shapes.Make(t, Shapes.Square, Palette.Slate, boardCentre, new Vector2(BoardWidth, BoardHeight), -20, "Board");
            for (int y = 0; y < NoodleSim.Rows; y++)
            for (int x = 0; x < NoodleSim.Cols; x++)
                Shapes.Make(t, Shapes.Circle, Palette.Night, CellCenter(new Vector2Int(x, y)), new Vector2(0.16f, 0.16f), -19, "Dot");

            // Parts of the noodle crossing an edge are clipped to the board, so it slides out of
            // one side and back in at the other.
            var mask = Make("BoardMask", boardCentre).AddComponent<SpriteMask>();
            mask.sprite = Shapes.Square;
            mask.transform.localScale = new Vector3(BoardWidth, BoardHeight, 1f);

            _pea = MakeSprite("Pea", GameSprite("pea"), Vector2.zero, 8);
            _pea.transform.localScale = Vector3.one * 1.15f;
            _gem = MakeSprite("Gem", PropSprite("gem"), Vector2.zero, 8);
            _gem.transform.localScale = Vector3.one * 0.78f;
            _gem.gameObject.SetActive(false);

            _head = MakePart("Head", HeadSize);
            var highlight = Shapes.Make(_head, Shapes.Circle, Palette.Paper, new Vector2(-0.2f, 0.2f), new Vector2(0.15f, 0.15f), LitOrder + 1, "Shine");
            highlight.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            var eyes = new GameObject("Eyes").AddComponent<SpriteRenderer>();
            eyes.sprite = GameSprite("eyes");
            eyes.sortingOrder = EyesOrder;
            _eyes = eyes.transform;
            _eyes.SetParent(_head, false);
            _eyesAngle = Angle(_sim.Dir);

            SyncViews(0f);
        }

        Transform MakePart(string name, float size)
        {
            var root = Make(name).transform;
            float ring = size + Style.Outline * 2f;
            float lit = size * LitShrink;
            float shift = (size - lit) / 2f * 0.7f;
            Masked(Shapes.Make(root, Shapes.Circle, Palette.Ink, Vector2.zero, new Vector2(ring, ring), InkOrder, "Ink"));
            Masked(Shapes.Make(root, Shapes.Circle, Palette.YellowDark, Vector2.zero, new Vector2(size, size), ShadeOrder, "Shade"));
            Masked(Shapes.Make(root, Shapes.Circle, Palette.Yellow, new Vector2(-shift, shift), new Vector2(lit, lit), LitOrder, "Lit"));
            return root;
        }

        static void Masked(SpriteRenderer sr) => sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        protected override void Play(float dt)
        {
            var turn = _steer.Feed(Input.Pressed, Input.Position);
            if (turn != Vector2Int.zero) _sim.Turn(turn);

            int before = _sim.Score;
            var events = _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & NoodleEvent.Ate) != 0)
            {
                Juice.Burst(transform, CellCenter(_sim.EatenAt), Palette.Lime, 10, 4f);
                Juice.Squash(_head);
                _bulges.Add(0.5f);
            }
            if ((events & NoodleEvent.Gem) != 0)
            {
                Juice.Burst(transform, CellCenter(_sim.EatenAt), Palette.Teal, 14, 5f);
                Juice.ShakeSmall();
                Juice.Squash(_head);
                _bulges.Add(0.5f);
            }
            if ((events & NoodleEvent.GemGone) != 0)
                Juice.Burst(transform, _gem.transform.localPosition, Palette.Mist, 6, 3f);
            if ((events & NoodleEvent.Crashed) != 0)
            {
                // Where the head meets what it hit (wrapped, in case that's across an edge).
                var at = CellToWorld(WrapCells((Vector2)_sim.Head + (Vector2)_sim.Dir * 0.5f));
                Juice.Burst(transform, at, Palette.Red, 16, 6f);
                Juice.ShakeBig();
                Juice.Squash(_head, 0.3f);
                EndRun();
            }

            for (int i = _bulges.Count - 1; i >= 0; i--)
            {
                _bulges[i] += dt / _sim.StepTime * BulgeSpeed;
                if (_bulges[i] > _sim.Body.Count + 1) _bulges.RemoveAt(i);
            }
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            float t = _sim.Crashed ? 1f : _sim.Progress;
            var body = _sim.Body;
            var before = _sim.Before;
            int n = body.Count;

            // Each segment slides from where it was to its new cell; positions are in cells.
            Vector2 Seg(int i)
            {
                var from = (Vector2)before[i];
                return WrapCells(from + Unwrap(body[i] - before[i]) * t);
            }

            int needed = 2 * (n - 1);
            while (_parts.Count < needed) _parts.Add(MakePart(_parts.Count % 2 == 0 ? "Gap" : "Segment", BodySize));

            var prev = Seg(0);
            _head.localPosition = CellToWorld(prev);

            // The eyes are art, and art can't be clipped by the mask (it draws as a white
            // silhouette), so they blink out while the head slides through an edge.
            bool crossing = prev.x < -0.2f || prev.x > NoodleSim.Cols - 0.8f || prev.y < -0.2f || prev.y > NoodleSim.Rows - 0.8f;
            _eyes.gameObject.SetActive(!crossing);
            for (int i = 1; i < n; i++)
            {
                var p = Seg(i);
                var gap = _parts[2 * (i - 1)];
                var seg = _parts[2 * (i - 1) + 1];
                gap.gameObject.SetActive(true);
                seg.gameObject.SetActive(true);
                gap.localPosition = CellToWorld(WrapCells(prev + Unwrap(p - prev) / 2f));
                seg.localPosition = CellToWorld(p);
                gap.localScale = Vector3.one * PartScale(i - 0.5f, n);
                seg.localScale = Vector3.one * PartScale(i, n);
                prev = p;
            }
            for (int i = needed; i < _parts.Count; i++) _parts[i].gameObject.SetActive(false);

            // The eyes turn smoothly to look the way the noodle is heading.
            _eyesAngle = Mathf.MoveTowardsAngle(_eyesAngle, Angle(_sim.Dir), EyesTurn * dt);
            if (dt == 0f) _eyesAngle = Angle(_sim.Dir);
            _eyes.localRotation = Quaternion.Euler(0f, 0f, _eyesAngle);
            _eyes.localPosition = Quaternion.Euler(0f, 0f, _eyesAngle) * new Vector2(0f, 0.1f);

            SyncPickups();
        }

        void SyncPickups()
        {
            _pea.gameObject.SetActive(_sim.HasPea);
            if (_sim.HasPea)
            {
                if (_sim.Pea != _peaShown)
                {
                    _peaShown = _sim.Pea;
                    _pea.transform.localPosition = CellCenter(_sim.Pea);
                    Juice.Pop(_pea.transform);
                }
                _pea.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 4f) * 8f);
            }

            if (_sim.HasGem && !_gem.gameObject.activeSelf)
            {
                _gem.gameObject.SetActive(true);
                _gem.transform.localPosition = CellCenter(_sim.Gem);
                Juice.Pop(_gem.transform);
            }
            else if (!_sim.HasGem && _gem.gameObject.activeSelf) _gem.gameObject.SetActive(false);
            if (_sim.HasGem)
            {
                _gem.enabled = _sim.GemStepsLeft > GemBlinkSteps || Mathf.Repeat(Time.time * 8f, 1f) < 0.6f;
                _gem.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f) * 10f);
            }

            var chilis = _sim.Chilis;
            while (_chilis.Count < chilis.Count)
            {
                var chili = MakeSprite("Chili", _chiliSprite, CellCenter(chilis[_chilis.Count]), 7);
                chili.transform.localScale = Vector3.one * 0.9f;
                Juice.Pop(chili.transform);
                _chilis.Add(chili);
            }
        }

        /// <summary>Thinner at the tail, fatter where a swallowed pea is passing.</summary>
        float PartScale(float index, int length)
        {
            float fromTail = length - 1 - index;
            float s = Mathf.Min(1f, 0.62f + 0.19f * fromTail);
            foreach (float b in _bulges) s += BulgeSize * Mathf.Max(0f, 1f - Mathf.Abs(index - b) / 1.3f);
            return s;
        }

        /// <summary>A cell step that crossed an edge, as the short way round (-1, 0 or 1 on each axis).</summary>
        static Vector2 Unwrap(Vector2 d)
        {
            if (d.x > NoodleSim.Cols / 2f) d.x -= NoodleSim.Cols;
            if (d.x < -NoodleSim.Cols / 2f) d.x += NoodleSim.Cols;
            if (d.y > NoodleSim.Rows / 2f) d.y -= NoodleSim.Rows;
            if (d.y < -NoodleSim.Rows / 2f) d.y += NoodleSim.Rows;
            return d;
        }

        static Vector2 WrapCells(Vector2 c) =>
            new Vector2(Mathf.Repeat(c.x + 0.5f, NoodleSim.Cols) - 0.5f, Mathf.Repeat(c.y + 0.5f, NoodleSim.Rows) - 0.5f);

        /// <summary>The eyes sprite looks up; this turns it to look along a direction.</summary>
        static float Angle(Vector2Int dir) => Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
    }
}
