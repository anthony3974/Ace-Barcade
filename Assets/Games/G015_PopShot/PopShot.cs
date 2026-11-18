using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.PopShot
{
    /// <summary>
    /// Game 015. A bubble shooter: drag to aim (a dotted line shows where the shot will go,
    /// bounces and all), let go to shoot. Three or more of a colour pop, and whatever's left
    /// hanging drops. Shots that pop nothing fill the pips by the launcher; a full set pushes a new
    /// row in from the top. A bubble below the red line ends the run.
    /// The rules live in <see cref="ShotSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(15, "pop-shot", "Pop Shot", Controls.Drag, Blurb = "Drag to aim, let go to shoot")]
    public sealed class PopShot : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        static readonly string[] Names = { "blue", "green", "purple", "orange", "pink" };

        // Bursts take a paler colour than the bubble they come from.
        static readonly Color[] BurstColors = { Palette.Sky, Palette.Lime, Palette.Pink, Palette.Yellow, Palette.Paper };

        const float GemScale = 0.45f;
        const float NextScale = 0.6f;
        static readonly Vector2 NextPos = new Vector2(-1.75f, -6.95f);
        const float BaseY = -6.85f;
        const float PipY = -6.95f, PipX = 1.45f, PipStep = 0.42f, PipSize = 0.3f;
        const float DotStep = 0.5f, DotSize = 0.17f;
        const int MaxDots = 60;
        const float FallGravity = 26f;

        ShotSim _sim;
        Sprite[] _bubbles;
        Sprite _gem;
        Transform _barrel, _base;
        SpriteRenderer _loaded, _next, _shot;
        readonly List<SpriteRenderer> _pips = new List<SpriteRenderer>(), _pipRings = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> _dots = new List<SpriteRenderer>();
        readonly Dictionary<int, Transform> _views = new Dictionary<int, Transform>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _gone = new List<int>();

        sealed class Faller
        {
            public Transform T;
            public Vector2 Velocity;
        }

        readonly List<Faller> _fallers = new List<Faller>();

        Vector2 _aim = Vector2.up;
        bool _aiming, _starting;
        float _push, _pipFlash;
        int _shownCurrent = -1, _shownNext = -1;

        public ShotSim Sim => _sim;

        protected override void Build()
        {
            _sim = new ShotSim(SeedForTests ?? System.Environment.TickCount);
            _gem = PropSprite("gem");
            _bubbles = new Sprite[Names.Length];
            for (int i = 0; i < Names.Length; i++) _bubbles[i] = GameSprite($"bubble_{Names[i]}");
            var t = transform;

            // A wooden frame: walls down both sides and a beam across the top that the rows hang from.
            const float wall = 0.5f;
            float beamBottom = ShotSim.TopY + ShotSim.Radius;
            Shapes.Box(t, new Vector2(ShotSim.Left - wall / 2f, -1f), new Vector2(wall, 16f), Palette.Brown, Palette.BrownDark, 20, "WallLeft");
            Shapes.Box(t, new Vector2(ShotSim.Right + wall / 2f, -1f), new Vector2(wall, 16f), Palette.Brown, Palette.BrownDark, 20, "WallRight");
            // The beam reaches up off the screen, so new rows slide out from behind it.
            const float beamTop = 10f;
            Shapes.Box(t, new Vector2(0f, (beamBottom + beamTop) / 2f), new Vector2(ShotSim.Right - ShotSim.Left + wall * 2f, beamTop - beamBottom), Palette.Brown, Palette.BrownDark, 21, "Beam");

            // The red line: a bubble below it ends the run.
            for (float x = ShotSim.Left + 0.25f; x < ShotSim.Right; x += 0.7f)
                Shapes.Make(t, Shapes.Square, Palette.Red, new Vector2(x + 0.2f, ShotSim.DangerY), new Vector2(0.4f, 0.12f), -5, "Danger");

            // Background bubbles: a few big Slate circles drifting nowhere, no outlines.
            var random = new System.Random(15);
            for (int i = 0; i < 9; i++)
            {
                float size = Mathf.Lerp(0.3f, 1.1f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-3.6f, 3.6f, (float)random.NextDouble()), Mathf.Lerp(-7.5f, 4.5f, (float)random.NextDouble()));
                Shapes.Make(t, Shapes.Circle, Palette.Slate, at, new Vector2(size, size), -20, "Deco");
            }

            for (int i = 0; i < MaxDots; i++)
            {
                var dot = Shapes.Make(t, Shapes.Circle, Palette.Cloud, Vector2.zero, new Vector2(DotSize, DotSize), 3, "Dot");
                dot.enabled = false;
                _dots.Add(dot);
            }

            // The launcher: a barrel that turns to aim, behind the loaded bubble, in a yellow critter.
            _barrel = Make("Barrel", ShotSim.Shooter).transform;
            var barrel = new GameObject("Sprite").AddComponent<SpriteRenderer>();
            barrel.sprite = GameSprite("barrel");
            barrel.sortingOrder = 14;
            barrel.transform.SetParent(_barrel, false);
            barrel.transform.localPosition = new Vector2(0f, 0.45f);
            _loaded = MakeSprite("Loaded", null, ShotSim.Shooter, 16);
            _base = MakeSprite("Launcher", GameSprite("launcher"), new Vector2(0f, BaseY), 17).transform;
            _next = MakeSprite("Next", null, NextPos, 17);
            _next.transform.localScale = Vector3.one * NextScale;
            _shot = MakeSprite("Shot", null, ShotSim.Shooter, 12);
            _shot.enabled = false;

            for (int i = 0; i < ShotSim.StartPips; i++)
            {
                _pipRings.Add(Shapes.Make(t, Shapes.Circle, Palette.Ink, new Vector2(PipX + i * PipStep, PipY), Vector2.one * (PipSize + Style.Outline * 2f), 18, "PipRing"));
                _pips.Add(Shapes.Make(t, Shapes.Circle, Palette.Night, new Vector2(PipX + i * PipStep, PipY), Vector2.one * PipSize, 19, "Pip"));
            }

            SyncViews(0f);
        }

        protected override void OnBegin() => _starting = true;

        protected override void Play(float dt)
        {
            // The touch that starts the game doesn't aim or shoot.
            bool fire = false;
            if (_starting)
            {
                if (Input.Up || !Input.Pressed) _starting = false;
                _aiming = false;
            }
            else
            {
                if (Input.Pressed || Input.Up) _aim = ShotSim.AimToward(Input.Position);
                _aiming = Input.Pressed && !_sim.Flying;
                fire = Input.Up;
            }

            var events = ShotEvent.None;
            if (fire && _sim.Fire(_aim))
            {
                events |= ShotEvent.Fired;
                Juice.Squash(_base, 0.12f);
                Juice.Squash(_barrel, 0.2f);
            }

            int before = _sim.Score;
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & ShotEvent.Bounced) != 0)
                Juice.Burst(transform, new Vector2(_sim.ShotPos.x < 0f ? ShotSim.Left : ShotSim.Right, _sim.ShotPos.y), Palette.Cloud, 4, 2f);
            if ((events & ShotEvent.Landed) != 0) Landed(events);
            if ((events & ShotEvent.Over) != 0)
            {
                for (float x = ShotSim.Left + 0.5f; x < ShotSim.Right; x += 1.5f)
                    Juice.Burst(transform, new Vector2(x, ShotSim.DangerY), Palette.Red, 6, 4f);
                Juice.ShakeBig();
                EndRun();
            }
        }

        void Landed(ShotEvent events)
        {
            foreach (var gone in _sim.Popped)
            {
                Juice.Burst(transform, gone.At, BurstColors[gone.Bubble.Color], 7, 3.5f);
                if (gone.Bubble.Gem) Juice.Burst(transform, gone.At, Palette.Teal, 12, 5f);
                if (_views.TryGetValue(gone.Bubble.Id, out var v)) { Destroy(v.gameObject); _views.Remove(gone.Bubble.Id); }
            }
            var random = new System.Random(_sim.Shots);
            foreach (var gone in _sim.Dropped)
            {
                if (!_views.TryGetValue(gone.Bubble.Id, out var v)) continue;
                _views.Remove(gone.Bubble.Id);
                _fallers.Add(new Faller { T = v, Velocity = new Vector2(Mathf.Lerp(-1.5f, 1.5f, (float)random.NextDouble()), Mathf.Lerp(0f, 2f, (float)random.NextDouble())) });
                if (gone.Bubble.Gem) Juice.Burst(transform, gone.At, Palette.Teal, 12, 5f);
            }
            // Now the board's views, with the landed bubble's, which gets a little squash.
            SyncBoard();
            var landed = _sim.At(_sim.LandedRow, _sim.LandedCol);
            if (landed != null && _views.TryGetValue(landed.Id, out var view)) Juice.Squash(view, 0.15f);

            if (_sim.Popped.Count + _sim.Dropped.Count >= 6 || (events & ShotEvent.Gem) != 0) Juice.ShakeSmall();
            if ((events & ShotEvent.Pushed) != 0)
            {
                _push = ShotSim.RowHeight;
                _pipFlash = 0.3f;
                Juice.ShakeSmall();
            }
            if ((events & ShotEvent.Cleared) != 0)
            {
                Juice.Burst(transform, new Vector2(0f, 1f), Palette.Lime, 24, 8f);
                _push = ShotSim.RowHeight * ShotSim.ClearRefill;
            }
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            _push = Mathf.MoveTowards(_push, 0f, dt * ShotSim.RowHeight * 5f);
            SyncBoard();

            for (int i = _fallers.Count - 1; i >= 0; i--)
            {
                var f = _fallers[i];
                f.Velocity.y -= FallGravity * dt;
                f.T.localPosition += (Vector3)(f.Velocity * dt);
                if (f.T.localPosition.y < -9.5f)
                {
                    Destroy(f.T.gameObject);
                    _fallers.RemoveAt(i);
                }
            }

            // The launcher, the loaded bubble and the next one.
            float angle = Mathf.Atan2(_aim.y, _aim.x) * Mathf.Rad2Deg - 90f;
            _barrel.localRotation = Quaternion.Euler(0f, 0f, angle);
            if (_shownCurrent != _sim.Current)
            {
                _shownCurrent = _sim.Current;
                _loaded.sprite = _bubbles[_sim.Current];
                Juice.Pop(_loaded.transform);
            }
            _loaded.enabled = !_sim.Flying && !_sim.Over;
            if (_shownNext != _sim.Next)
            {
                _shownNext = _sim.Next;
                _next.sprite = _bubbles[_sim.Next];
            }
            _shot.enabled = _sim.Flying;
            if (_sim.Flying)
            {
                _shot.sprite = _bubbles[_sim.ShotColor];
                _shot.transform.localPosition = _sim.ShotPos;
            }

            // The pips: as many as it takes to push a row in, filled one per shot that pops nothing.
            _pipFlash = Mathf.Max(0f, _pipFlash - dt);
            for (int i = 0; i < _pips.Count; i++)
            {
                bool shown = i < _sim.PipsToPush;
                _pips[i].enabled = shown;
                _pipRings[i].enabled = shown;
                _pips[i].color = _pipFlash > 0f ? Palette.Yellow : i < _sim.Pips ? Palette.Cloud : Palette.Night;
            }

            // The aim line: dots along the path the shot would take, to where it would stop.
            int used = 0;
            if (_aiming && !_sim.Over)
            {
                var path = _sim.Trace(_aim, out _, out _);
                float carry = DotStep * 0.8f;
                for (int s = 0; s + 1 < path.Count && used < MaxDots; s++)
                {
                    var a = path[s];
                    var b = path[s + 1];
                    float len = (b - a).magnitude;
                    float d = carry;
                    while (d < len && used < MaxDots)
                    {
                        var dot = _dots[used++];
                        dot.transform.localPosition = Vector2.Lerp(a, b, d / len);
                        dot.enabled = true;
                        d += DotStep;
                    }
                    carry = d - len;
                }
            }
            for (int i = used; i < _dots.Count; i++) _dots[i].enabled = false;
        }

        void SyncBoard()
        {
            _seen.Clear();
            for (int r = 0; r < _sim.Rows; r++)
                for (int c = 0; c < _sim.ColsIn(r); c++)
                {
                    var b = _sim.At(r, c);
                    if (b == null) continue;
                    _seen.Add(b.Id);
                    var at = _sim.CellPos(r, c) + new Vector2(0f, _push);
                    if (!_views.TryGetValue(b.Id, out var view)) _views[b.Id] = view = MakeBubble(b, at);
                    view.localPosition = at;
                }

            // Anything still shown that's left the board without popping or dropping (it can't
            // happen in play, but keeps the view honest).
            _gone.Clear();
            foreach (var id in _views.Keys) if (!_seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                Destroy(_views[id].gameObject);
                _views.Remove(id);
            }
        }

        Transform MakeBubble(Bubble b, Vector2 at)
        {
            var sr = MakeSprite($"Bubble {b.Id}", _bubbles[b.Color], at, 5);
            if (b.Gem)
            {
                var gem = new GameObject("Gem").AddComponent<SpriteRenderer>();
                gem.sprite = _gem;
                gem.sortingOrder = 6;
                gem.transform.SetParent(sr.transform, false);
                gem.transform.localScale = Vector3.one * GemScale;
            }
            return sr.transform;
        }
    }
}
