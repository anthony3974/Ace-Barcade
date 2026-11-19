using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AceBarcade.Games.StackTower
{
    /// <summary>
    /// Game 014. A block slides across the top of a tower at night; tap to drop it. Whatever hangs
    /// over the floor below is cut off and falls, so the tower narrows; a perfect drop keeps it
    /// whole, and a run of them widens it again. Missing the tower ends the run.
    /// The rules live in <see cref="StackSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(14, "stack-tower", "Stack Tower", Controls.Tap, Blurb = "Tap to drop each floor in line")]
    public sealed class StackTower : ArcadeGame
    {
        const float H = StackSim.FloorHeight;

        /// <summary>Where the ground's top is on screen before the view starts to climb.</summary>
        const float GroundY = -5.6f;

        /// <summary>The view climbs to keep the moving block no higher than this on screen.</summary>
        const float BlockMaxY = 0.9f;

        // Windows sit on a grid fixed to the tower, so they line up from floor to floor.
        const float WindowStep = 0.6f;
        const float WindowSize = 0.22f;
        const float WindowY = 0.06f;
        const float WindowMargin = 0.12f;

        const float GemScale = 0.7f;
        const float GemSize = 112f / Style.PixelsPerUnit * GemScale;
        const float FallGravity = 28f;

        static readonly (Color Fill, Color Shade)[] FloorColors =
        {
            (Palette.Blue, Palette.BlueDark),
            (Palette.Purple, Palette.PurpleDark),
            (Palette.Tan, Palette.Brown),
        };

        StackSim _sim;
        Transform _world, _block, _blockGem;
        Sprite _gem;
        TextMeshPro _flash;
        float _flashFor, _scroll;
        int _shownLevel = -1;
        bool _starting;

        readonly List<Transform> _floors = new List<Transform>();
        readonly List<(Transform t, Vector2 at, float drift, float span)> _decor = new List<(Transform, Vector2, float, float)>();

        sealed class Faller
        {
            public Transform T;
            public Vector2 Velocity;
            public float Spin;
        }

        readonly List<Faller> _fallers = new List<Faller>();

        public StackSim Sim => _sim;

        protected override void Build()
        {
            _sim = new StackSim();
            _gem = PropSprite("gem");
            var t = transform;

            // The sky: stars and clouds that slide by slower than the tower as the view climbs.
            var random = new System.Random(13);
            for (int i = 0; i < 16; i++)
            {
                float size = Mathf.Lerp(0.14f, 0.24f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-5f, 5f, (float)random.NextDouble()), -10f + i * 1.4f);
                var star = Shapes.Make(t, Shapes.Circle, Palette.Slate, at, new Vector2(size, size), -22, "Star").transform;
                _decor.Add((star, at, 0.08f, 16 * 1.4f));
            }
            for (int i = 0; i < 5; i++)
            {
                float size = Mathf.Lerp(1.1f, 1.7f, (float)random.NextDouble());
                var at = new Vector2((i % 2 == 0 ? -1f : 1f) * Mathf.Lerp(2.4f, 4.6f, (float)random.NextDouble()), -6f + i * 4.4f);
                var cloud = Make("Cloud", at).transform;
                Shapes.Make(cloud, Shapes.Circle, Palette.Slate, Vector2.zero, new Vector2(size * 1.3f, size), -20, "Puff");
                Shapes.Make(cloud, Shapes.Circle, Palette.Slate, new Vector2(size * 0.55f, -size * 0.12f), new Vector2(size * 0.8f, size * 0.7f), -20, "Puff");
                Shapes.Make(cloud, Shapes.Circle, Palette.Slate, new Vector2(-size * 0.55f, -size * 0.16f), new Vector2(size * 0.7f, size * 0.6f), -20, "Puff");
                _decor.Add((cloud, at, 0.25f, 5 * 4.4f));
            }

            _world = Make("World", new Vector2(0f, GroundY)).transform;

            // A far-off town: Slate blocks of different heights behind the tower, on the ground.
            float x = -6f;
            while (x < 6f)
            {
                float w = Mathf.Lerp(0.8f, 1.6f, (float)random.NextDouble());
                float h = Mathf.Lerp(1.2f, 3.4f, (float)random.NextDouble());
                Shapes.Make(_world, Shapes.Square, Palette.Slate, new Vector2(x + w / 2f, h / 2f - 0.1f), new Vector2(w, h), -18, "House");
                x += w + 0.05f;
            }

            Shapes.Box(_world, new Vector2(0f, -0.25f), new Vector2(30f, 0.5f), Palette.Green, Palette.GreenDark, 2, "Grass");
            Shapes.Make(_world, Shapes.Square, Palette.Brown, new Vector2(0f, -0.5f - 7f), new Vector2(30f, 14f), 1, "Dirt");
            for (int i = 0; i < 8; i++)
            {
                float size = Mathf.Lerp(0.16f, 0.3f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-5f, 5f, (float)random.NextDouble()), Mathf.Lerp(-2.4f, -0.8f, (float)random.NextDouble()));
                Shapes.Make(_world, Shapes.Circle, Palette.BrownDark, at, new Vector2(size * 1.4f, size), 2, "Pebble");
            }

            // The foundation: a heavier Brown slab, as wide as the tower starts.
            var foundation = _sim.Floors[0];
            var slab = Shapes.Box(_world, new Vector2(foundation.X, H / 2f), new Vector2(foundation.Width, H), Palette.Brown, Palette.BrownDark, 5, "Foundation");
            _floors.Add(slab);

            _flash = new GameObject("Flash").AddComponent<TextMeshPro>();
            _flash.transform.SetParent(t, false);
            _flash.font = UiKit.Font;
            _flash.fontSize = 7f;
            _flash.color = Palette.Yellow;
            _flash.alignment = TextAlignmentOptions.Center;
            _flash.textWrappingMode = TextWrappingModes.NoWrap;
            _flash.rectTransform.sizeDelta = new Vector2(8f, 2f);
            _flash.sortingOrder = 40;
            _flash.gameObject.SetActive(false);

            SyncViews(0f);
        }

        protected override void OnBegin() => _starting = true;

        protected override void Play(float dt)
        {
            // The touch that starts the game sets the block sliding; it doesn't drop it.
            bool drop = Input.Down && !_starting;
            _starting = false;

            int before = _sim.Score;
            var top = _sim.Top;
            var events = drop ? _sim.Drop() : StackEvent.None;
            float droppedX = _sim.Over ? _sim.MissX : 0f;
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & StackEvent.Landed) != 0) Landed(events);
            if ((events & StackEvent.Missed) != 0) Missed(top, droppedX, (events & StackEvent.GemLost) != 0);
        }

        void Landed(StackEvent events)
        {
            var floor = _sim.Top;
            var view = MakeFloor(floor);
            _floors.Add(view);
            Juice.Squash(view, (events & StackEvent.Perfect) != 0 ? 0.14f : 0.1f);

            float y = FloorY(floor.Level);
            float bottom = y - H / 2f;
            Juice.Burst(_world, new Vector2(floor.Left, bottom), Palette.Cloud, 4, 2f);
            Juice.Burst(_world, new Vector2(floor.Right, bottom), Palette.Cloud, 4, 2f);

            if ((events & StackEvent.Perfect) != 0)
            {
                Juice.Burst(_world, new Vector2(floor.X, y + H / 2f), Palette.Lime, 12, 4.5f);
                ShowFlash(_sim.Streak >= 2 ? $"PERFECT x{_sim.Streak}" : "PERFECT");
            }
            if ((events & StackEvent.Grew) != 0)
            {
                Juice.Burst(_world, new Vector2(floor.Left, y), Palette.Green, 8, 4f);
                Juice.Burst(_world, new Vector2(floor.Right, y), Palette.Green, 8, 4f);
                Juice.ShakeSmall();
            }
            if ((events & StackEvent.Cut) != 0)
            {
                var (fill, shade) = ColorsFor(floor.Level);
                var piece = Shapes.Box(_world, new Vector2(_sim.CutX, y), new Vector2(_sim.CutWidth, H), fill, shade, 8, "Cut");
                AddWindows(piece, _sim.CutX, _sim.CutWidth, 11);
                if ((events & StackEvent.GemLost) != 0) AttachGem(piece, _sim.GemX - _sim.CutX);
                float side = Mathf.Sign(_sim.CutX - floor.X);
                _fallers.Add(new Faller { T = piece, Velocity = new Vector2(side * 1.2f, 0.5f), Spin = -side * 160f });
            }
            if ((events & StackEvent.Gem) != 0)
            {
                Juice.Burst(_world, new Vector2(_sim.GemX, y + H / 2f + GemSize / 2f), Palette.Teal, 14, 5f);
                Juice.ShakeSmall();
            }
        }

        void Missed(StackFloor top, float x, bool gem)
        {
            // The block drops from where it was and tumbles away past the tower.
            float y = FloorY(top.Level + 1);
            var block = MakeBlock(x, _sim.MissWidth, gem, "Missed");
            block.localPosition = new Vector2(x, y);
            float side = Mathf.Sign(x - top.X);
            _fallers.Add(new Faller { T = block, Velocity = new Vector2(side * 1.6f, 0f), Spin = -side * 120f });
            if (_block != null) { Destroy(_block.gameObject); _block = null; }

            Juice.Burst(_world, new Vector2(x, y), Palette.Orange, 16, 6f);
            Juice.ShakeBig();
            EndRun();
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            // The view climbs to keep the moving block in sight, and never comes back down.
            float target = Mathf.Max(0f, GroundY + FloorY(_sim.Level) - BlockMaxY);
            _scroll = Mathf.Max(_scroll, Mathf.Lerp(_scroll, target, 1f - Mathf.Exp(-8f * dt)));
            _world.localPosition = new Vector2(0f, GroundY - _scroll);

            foreach (var (t, at, drift, span) in _decor)
                t.localPosition = new Vector2(at.x, Mathf.Repeat(at.y - _scroll * drift + span / 2f, span) - span / 2f);

            // Floors well below the bottom of the screen are hidden.
            float bottom = -Cam.HalfSize.y - 1f - (GroundY - _scroll);
            foreach (var floor in _floors)
            {
                bool show = floor.localPosition.y + H > bottom;
                if (floor.gameObject.activeSelf != show) floor.gameObject.SetActive(show);
            }

            // The moving block.
            if (!_sim.Over)
            {
                if (_shownLevel != _sim.Level)
                {
                    _shownLevel = _sim.Level;
                    if (_block != null) Destroy(_block.gameObject);
                    _block = MakeBlock(_sim.X, _sim.Width, _sim.HasGem, "Block");
                    if (_sim.Level > 1) Juice.Pop(_block);
                }
                _block.localPosition = new Vector2(_sim.X, FloorY(_sim.Level));
                if (_blockGem != null)
                    _blockGem.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f) * 12f);
            }

            for (int i = _fallers.Count - 1; i >= 0; i--)
            {
                var f = _fallers[i];
                f.Velocity.y -= FallGravity * dt;
                f.T.localPosition += (Vector3)(f.Velocity * dt);
                f.T.localRotation = Quaternion.Euler(0f, 0f, f.T.localEulerAngles.z + f.Spin * dt);
                if (f.T.localPosition.y < bottom - 3f)
                {
                    Destroy(f.T.gameObject);
                    _fallers.RemoveAt(i);
                }
            }

            if (_flashFor > 0f)
            {
                _flashFor -= dt;
                _flash.transform.localPosition += new Vector3(0f, dt * 0.8f, 0f);
                if (_flashFor <= 0f) _flash.gameObject.SetActive(false);
            }
        }

        /// <summary>The height of the middle of a floor, above the ground.</summary>
        static float FloorY(int level) => level * H + H / 2f;

        static (Color Fill, Color Shade) ColorsFor(int level) => FloorColors[(level - 1) % FloorColors.Length];

        Transform MakeFloor(StackFloor floor)
        {
            var (fill, shade) = ColorsFor(floor.Level);
            var root = Shapes.Box(_world, new Vector2(floor.X, FloorY(floor.Level)), new Vector2(floor.Width, H), fill, shade, 5, $"Floor {floor.Level}");
            AddWindows(root, floor.X, floor.Width, 8);
            return root;
        }

        /// <summary>The block you drop: yellow, as the thing you play with is in every game.</summary>
        Transform MakeBlock(float x, float width, bool gem, string name)
        {
            var root = Shapes.Box(_world, new Vector2(x, FloorY(_sim.Level)), new Vector2(width, H), Palette.Yellow, Palette.YellowDark, 12, name);
            _blockGem = gem ? AttachGem(root, 0f) : null;
            return root;
        }

        Transform AttachGem(Transform parent, float x)
        {
            var sr = new GameObject("Gem").AddComponent<SpriteRenderer>();
            sr.sprite = _gem;
            sr.sortingOrder = 16;
            sr.transform.SetParent(parent, false);
            sr.transform.localPosition = new Vector2(x, H / 2f + GemSize / 2f - 0.06f);
            sr.transform.localScale = Vector3.one * GemScale;
            return sr.transform;
        }

        /// <summary>Night windows on the tower's grid, wherever a whole one fits.</summary>
        static void AddWindows(Transform parent, float x, float width, int order)
        {
            float left = x - width / 2f + WindowMargin + WindowSize / 2f;
            float right = x + width / 2f - WindowMargin - WindowSize / 2f;
            for (float wx = Mathf.Ceil(left / WindowStep) * WindowStep; wx <= right + 1e-4f; wx += WindowStep)
                Shapes.Make(parent, Shapes.Square, Palette.Night, new Vector2(wx - x, WindowY), new Vector2(WindowSize, WindowSize), order, "Window");
        }

        void ShowFlash(string text)
        {
            _flash.text = text;
            _flash.transform.localPosition = new Vector2(0f, GroundY - _scroll + FloorY(_sim.Top.Level) + 1.1f);
            _flash.gameObject.SetActive(true);
            Juice.Pop(_flash.transform);
            _flashFor = 0.7f;
        }
    }
}
