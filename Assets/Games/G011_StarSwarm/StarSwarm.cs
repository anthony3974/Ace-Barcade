using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.StarSwarm
{
    /// <summary>
    /// Game 011. Drag to slide a yellow ship along the bottom; it fires on its own. Waves of red
    /// drones and brutes sway above, drop bullets and dive at the ship. Three lives.
    /// The rules live in <see cref="StarSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(11, "star-swarm", "Star Swarm", Controls.Drag, Blurb = "Slide to dodge, it shoots for you")]
    public sealed class StarSwarm : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        const float FlameY = -0.62f;
        const float TiltPerSpeed = 1.6f;

        StarSim _sim;
        Sprite _drone, _brute, _cracked, _shot, _bullet, _gem;
        Transform _ship, _flame;
        SpriteRenderer _shipSprite, _flameSprite, _gemView;
        SpriteRenderer[] _lives;
        readonly Dictionary<StarFoe, SpriteRenderer> _foes = new Dictionary<StarFoe, SpriteRenderer>();
        readonly List<StarFoe> _gone = new List<StarFoe>();
        readonly List<SpriteRenderer> _shotViews = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> _bulletViews = new List<SpriteRenderer>();
        readonly List<(Transform t, float speed)> _stars = new List<(Transform, float)>();
        float _lastShipX, _tilt;

        public StarSim Sim => _sim;

        protected override void Build()
        {
            _sim = new StarSim(SeedForTests ?? System.Environment.TickCount);
            _drone = GameSprite("drone");
            _brute = GameSprite("brute");
            _cracked = GameSprite("brute_cracked");
            _shot = GameSprite("shot");
            _bullet = GameSprite("bullet");
            _gem = PropSprite("gem");
            var t = transform;

            // Stars drifting down at two speeds, so space has depth.
            var random = new System.Random(11);
            for (int i = 0; i < 28; i++)
            {
                bool near = i % 3 == 0;
                float size = near ? 0.2f : 0.13f;
                var at = new Vector2(Mathf.Lerp(-5.5f, 5.5f, (float)random.NextDouble()), Mathf.Lerp(-10f, 10f, (float)random.NextDouble()));
                var star = Shapes.Make(t, Shapes.Circle, near ? Palette.Mist : Palette.Slate, at, new Vector2(size, size), -20, "Star").transform;
                _stars.Add((star, near ? 1.6f : 0.7f));
            }
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-3.4f, -2.2f), new Vector2(2.4f, 2.4f), -21, "Planet");

            _ship = Make("Ship", new Vector2(0f, StarSim.ShipY)).transform;
            _shipSprite = new GameObject("Body").AddComponent<SpriteRenderer>();
            _shipSprite.sprite = GameSprite("ship");
            _shipSprite.sortingOrder = 20;
            _shipSprite.transform.SetParent(_ship, false);
            _flameSprite = new GameObject("Flame").AddComponent<SpriteRenderer>();
            _flameSprite.sprite = GameSprite("flame");
            _flameSprite.sortingOrder = 19;
            _flame = _flameSprite.transform;
            _flame.SetParent(_ship, false);
            _flame.localPosition = new Vector2(0f, FlameY);

            _gemView = MakeSprite("Gem", _gem, Vector2.zero, 12);
            _gemView.transform.localScale = Vector3.one * 0.6f;
            _gemView.enabled = false;

            // Lives left, as small ships under the best score.
            _lives = new SpriteRenderer[StarSim.StartLives];
            for (int i = 0; i < _lives.Length; i++)
            {
                _lives[i] = MakeSprite("Life", _shipSprite.sprite, new Vector2(-3.75f + i * 0.46f, 6.72f), 40);
                _lives[i].transform.localScale = Vector3.one * 0.34f;
            }

            SyncViews(0f);
        }

        protected override void Play(float dt)
        {
            _sim.SetTarget(Input.Pressed ? Input.Position.x : (float?)null);
            int before = _sim.Score;
            int lives = _sim.Lives;
            var events = _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & StarEvent.Hit) != 0 && _foes.TryGetValue(_sim.LastFoe, out var hurt))
            {
                Juice.Squash(hurt.transform, 0.25f);
                Juice.Burst(transform, _sim.LastPoint, Palette.Orange, 6, 3f);
            }
            if ((events & StarEvent.Killed) != 0)
            {
                bool brute = _sim.LastFoe.Kind == FoeKind.Brute;
                Juice.Burst(transform, _sim.LastPoint, Palette.Orange, brute ? 16 : 10, brute ? 5.5f : 4.5f);
                if (brute) Juice.ShakeSmall();
            }
            if ((events & StarEvent.Gem) != 0)
            {
                Juice.Burst(transform, _sim.LastPoint, Palette.Teal, 12, 5f);
                Juice.ShakeSmall();
            }
            if ((events & StarEvent.WaveCleared) != 0) Juice.ShakeSmall();
            if ((events & StarEvent.ShipHit) != 0)
            {
                Juice.Burst(transform, new Vector2(_sim.ShipX, StarSim.ShipY), Palette.Orange, 16, 6f);
                Juice.Squash(_shipSprite.transform, 0.3f);
                Juice.ShakeBig();
                if (lives - 1 >= 0 && lives - 1 < _lives.Length) Juice.Burst(transform, _lives[lives - 1].transform.localPosition, Palette.Red, 6, 2f);
            }
            if ((events & StarEvent.GameOver) != 0)
            {
                Juice.Burst(transform, new Vector2(_sim.ShipX, StarSim.ShipY), Palette.Yellow, 20, 7f);
                EndRun();
            }
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            foreach (var (star, speed) in _stars)
            {
                var p = star.localPosition;
                p.y -= speed * dt;
                if (p.y < -10f) p.y += 20f;
                star.localPosition = p;
            }

            // The ship leans the way it's sliding and blinks while it's safe after a hit.
            float x = _sim.ShipX;
            float speedX = dt > 0f ? (x - _lastShipX) / dt : 0f;
            _lastShipX = x;
            _tilt = Mathf.Lerp(_tilt, Mathf.Clamp(-speedX * TiltPerSpeed, -18f, 18f), 1f - Mathf.Exp(-12f * dt));
            _ship.localPosition = new Vector2(x, StarSim.ShipY);
            _ship.localRotation = Quaternion.Euler(0f, 0f, _tilt);
            bool show = !_sim.Over && (_sim.SafeFor <= 0f || Mathf.Repeat(_sim.SafeFor, 0.2f) > 0.1f);
            _shipSprite.enabled = show;
            _flameSprite.enabled = show;
            _flame.localScale = new Vector3(1f, 0.85f + 0.2f * Mathf.Sin(Time.time * 40f), 1f);

            for (int i = 0; i < _lives.Length; i++) _lives[i].enabled = i < _sim.Lives;

            SyncFoes();
            Sync(_shotViews, _sim.Shots, _shot, 15);
            Sync(_bulletViews, _sim.Bullets, _bullet, 16);

            var gem = _sim.Gem;
            _gemView.enabled = gem.HasValue;
            if (gem.HasValue)
            {
                _gemView.transform.localPosition = gem.Value;
                _gemView.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 4f) * 15f);
            }
        }

        void SyncFoes()
        {
            foreach (var f in _sim.Foes)
            {
                if (!f.Alive) continue;
                if (!_foes.TryGetValue(f, out var sr))
                {
                    sr = MakeSprite($"Foe {f.Index}", f.Kind == FoeKind.Brute ? _brute : _drone, new Vector2(f.X, f.Y), 10);
                    if (f.HasGem)
                    {
                        var gem = new GameObject("Gem").AddComponent<SpriteRenderer>();
                        gem.sprite = _gem;
                        gem.sortingOrder = 11;
                        gem.transform.SetParent(sr.transform, false);
                        gem.transform.localPosition = new Vector2(0f, -0.38f);
                        gem.transform.localScale = Vector3.one * 0.4f;
                    }
                    _foes[f] = sr;
                    Juice.Pop(sr.transform);
                }
                if (f.Cracked && sr.sprite != _cracked) sr.sprite = _cracked;
                sr.transform.localPosition = new Vector2(f.X, f.Y);

                // Divers point the way they're flying; the rest sit level.
                float angle = 0f;
                if (f.State == FoeState.Diving)
                {
                    var ahead = _sim.DiverAhead(f, 0.05f) - new Vector2(f.X, f.Y);
                    if (ahead.sqrMagnitude > 1e-6f) angle = Mathf.Atan2(ahead.y, ahead.x) * Mathf.Rad2Deg + 90f;
                }
                else angle = Mathf.Sin(Time.time * 3f + f.Index) * 5f;
                sr.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            }

            _gone.Clear();
            foreach (var pair in _foes) if (!pair.Key.Alive || !Contains(pair.Key)) _gone.Add(pair.Key);
            foreach (var f in _gone)
            {
                Destroy(_foes[f].gameObject);
                _foes.Remove(f);
            }
        }

        bool Contains(StarFoe f)
        {
            foreach (var other in _sim.Foes) if (other == f) return true;
            return false;
        }

        void Sync(List<SpriteRenderer> views, IReadOnlyList<StarShot> shots, Sprite sprite, int order)
        {
            while (views.Count < shots.Count) views.Add(MakeSprite("Shot", sprite, Vector2.zero, order));
            for (int i = 0; i < views.Count; i++)
            {
                bool on = i < shots.Count;
                if (views[i].enabled != on) views[i].enabled = on;
                if (on) views[i].transform.localPosition = new Vector2(shots[i].X, shots[i].Y);
            }
        }
    }
}
