using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.StarSwarm
{
    /// <summary>What happened during one <see cref="StarSim.Step"/>.</summary>
    [Flags]
    public enum StarEvent
    {
        None = 0,
        Fired = 1,
        Hit = 2,
        Killed = 4,
        WaveCleared = 8,
        WaveStarted = 16,
        ShipHit = 32,
        GameOver = 64,
        Gem = 128,
        Dived = 256,
        EnemyFired = 512,
    }

    public enum FoeKind { Drone, Brute }

    public enum FoeState { Formation, Diving, Returning, Dead }

    /// <summary>One enemy: a red drone (one hit) or a red brute (two).</summary>
    public sealed class StarFoe
    {
        public int Index { get; }
        public FoeKind Kind { get; }
        public int Col { get; }
        public int Row { get; }
        public bool HasGem { get; internal set; }
        public int Hp { get; internal set; }
        public FoeState State { get; internal set; }
        public float X { get; internal set; }
        public float Y { get; internal set; }

        // The dive: where it started, where it's headed, its sway and when it started.
        internal float DiveX, DiveY, AimX, Sway, Side, DiveStart, DiveSpeed;
        internal bool DiveFired;

        public float Radius => Kind == FoeKind.Brute ? StarSim.BruteRadius : StarSim.DroneRadius;
        public bool Alive => State != FoeState.Dead;
        public bool Cracked => Kind == FoeKind.Brute && Hp == 1;

        internal StarFoe(int index, FoeKind kind, int col, int row, bool gem)
        {
            Index = index;
            Kind = kind;
            Col = col;
            Row = row;
            HasGem = gem;
            Hp = kind == FoeKind.Brute ? 2 : 1;
        }
    }

    /// <summary>A shot from the ship, or a bullet from an enemy.</summary>
    public sealed class StarShot
    {
        public float X { get; }
        public float Y { get; internal set; }
        public float Speed { get; }
        /// <summary>For enemy bullets: when it reaches the ship's row.</summary>
        public float Arrives { get; }
        internal StarShot(float x, float y, float speed, float arrives) { X = x; Y = y; Speed = speed; Arrives = arrives; }
    }

    /// <summary>
    /// The rules of Star Swarm, in plain C# so they can be tested. A ship slides along the bottom
    /// after the finger and fires on its own. Waves of red drones and brutes sway in a formation
    /// above, drop bullets, and now and then one dives at the ship, then loops back round from the
    /// top. Three lives; a hit costs one, clears the bullets and gives a moment of safety.
    /// Drones score 1 and brutes (two hits) 3, double while diving; a cleared wave scores 10, and
    /// the teal gem one enemy in each wave carries scores 5 if you catch it.
    /// Each wave is a little harder, up to <see cref="HardestWave"/>.
    /// <b>Fair by design:</b> bullets fall straight down, and no more than <see cref="MaxThreats"/>
    /// bullets or divers reach the ship's row in any <see cref="ThreatWindow"/> seconds, so there's
    /// always time and room to slip between them (see <see cref="CanFire"/>). A diver heads for
    /// where the ship was when it set off, swaying a little, and takes over a second to get there.
    /// </summary>
    public sealed class StarSim
    {
        // The ship.
        public const float ShipY = -6.2f;
        public const float ShipLimit = 3.9f;
        public const float ShipSpeed = 16f;
        /// <summary>The ship's hit circle: smaller than its art, so near misses look like misses.</summary>
        public const float ShipRadius = 0.3f;
        public const float FireEvery = 0.25f;
        public const float ShotSpeed = 14f;
        public const float ShotTop = 8.5f;
        public const float GunY = ShipY + 0.45f;
        public const int StartLives = 3;
        public const float SafeTime = 2f;

        // The formation.
        public const int Cols = 6;
        public const float SpacingX = 1.15f;
        public const float SpacingY = 0.95f;
        public const float TopY = 5.4f;
        public const float SwayAmp = 0.6f;
        public const float SwayPeriod = 4f;
        public const float DroneRadius = 0.36f;
        public const float BruteRadius = 0.46f;
        public const float WaveGap = 1.2f;

        // Diving.
        public const float PeelTime = 0.5f;
        public const float PeelOut = 0.7f;
        public const float BottomOut = -9.2f;
        public const float ReturnFrom = 9.2f;
        public const float ReturnSpeed = 6f;

        // Bullets.
        public const float BulletRadius = 0.15f;
        public const float BulletBottom = -8.5f;

        /// <summary>Fairness: at most this many bullets or divers reach the ship's row within any window this long.</summary>
        public const int MaxThreats = 2;
        public const float ThreatWindow = 0.6f;

        public const int HardestWave = 7;
        public const int DronePoints = 1, BrutePoints = 3, WavePoints = 10, GemPoints = 5;
        public const float GemFall = 2.5f, GemCatch = 0.6f;

        readonly System.Random _random;
        readonly List<StarFoe> _foes = new List<StarFoe>();
        readonly List<StarShot> _shots = new List<StarShot>();
        readonly List<StarShot> _bullets = new List<StarShot>();
        /// <summary>When bullets and divers reach the ship's row, and where.</summary>
        readonly List<(float time, float x)> _threats = new List<(float, float)>();
        float _fireTimer, _diveTimer, _bulletTimer, _gapTimer = -1f;
        float? _target;
        int _foeCount;

        public IReadOnlyList<StarFoe> Foes => _foes;
        public IReadOnlyList<StarShot> Shots => _shots;
        public IReadOnlyList<StarShot> Bullets => _bullets;
        public float ShipX { get; private set; }
        public float Time { get; private set; }
        public int Wave { get; private set; }
        public int Lives { get; private set; } = StartLives;
        public int Score { get; private set; }
        public int Kills { get; private set; }
        public int Gems { get; private set; }
        public float SafeFor { get; private set; }
        public bool Over => Lives <= 0;
        /// <summary>A falling gem, if one is falling.</summary>
        public Vector2? Gem { get; private set; }
        public Vector2 LastPoint { get; private set; }
        public StarFoe LastFoe { get; private set; }
        /// <summary>Whether the last enemy killed was diving (it scored double).</summary>
        public bool LastKillDived { get; private set; }
        /// <summary>The enemies killed in the last step, and whether each was diving.</summary>
        public IReadOnlyList<(StarFoe foe, bool dived)> KilledLastStep => _killed;
        readonly List<(StarFoe, bool)> _killed = new List<(StarFoe, bool)>();
        /// <summary>When the bullets and divers still to come reach the ship's row, and where.</summary>
        public IReadOnlyList<(float time, float x)> Threats => _threats;

        public StarSim(int seed)
        {
            _random = new System.Random(seed);
            SpawnWave();
        }

        // --- The waves, by number -------------------------------------------------------------

        public static float Difficulty(int wave) => Mathf.Clamp01(wave / (float)HardestWave);
        public static int RowsFor(int wave) => 3 + Mathf.Min(2, wave / 2);
        public static int BruteRowsFor(int wave) => wave >= 3 ? 2 : wave >= 1 ? 1 : 0;
        public static float DiveEvery(int wave) => Mathf.Lerp(3.2f, 1.3f, Difficulty(wave));
        public static int MaxDivers(int wave) => 1 + Mathf.RoundToInt(2f * Difficulty(wave));
        public static float BulletEvery(int wave) => Mathf.Lerp(1.8f, 0.7f, Difficulty(wave));
        public static float BulletSpeed(int wave) => Mathf.Lerp(4f, 6.5f, Difficulty(wave));
        public static float DiveSpeedFor(int wave) => Mathf.Lerp(4.5f, 7f, Difficulty(wave));

        public float FormationX => SwayAmp * Mathf.Sin(Time * Mathf.PI * 2f / SwayPeriod);

        public Vector2 SlotPosition(int col, int row) =>
            new Vector2(FormationX + (col - (Cols - 1) / 2f) * SpacingX, TopY - row * SpacingY);

        void SpawnWave()
        {
            _foes.Clear();
            int rows = RowsFor(Wave), brutes = BruteRowsFor(Wave);
            int gem = _random.Next(Cols * rows);
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                var foe = new StarFoe(_foeCount++, row < brutes ? FoeKind.Brute : FoeKind.Drone, col, row, row * Cols + col == gem);
                var at = SlotPosition(col, row);
                foe.X = at.x;
                foe.Y = at.y;
                _foes.Add(foe);
            }
            _diveTimer = DiveEvery(Wave) + 1f;
            _bulletTimer = BulletEvery(Wave);
        }

        /// <summary>For tests: start this wave now, in place of the one on screen.</summary>
        public void SkipToWaveForTests(int wave)
        {
            Wave = wave;
            _bullets.Clear();
            _threats.Clear();
            SpawnWave();
        }

        // --- Playing ----------------------------------------------------------------------------

        /// <summary>Where the finger is (the ship heads for it), or null when it's up.</summary>
        public void SetTarget(float? x) => _target = x;

        public StarEvent Step(float dt)
        {
            if (Over) return StarEvent.None;
            var events = StarEvent.None;
            _killed.Clear();
            Time += dt;
            SafeFor = Mathf.Max(0f, SafeFor - dt);
            _threats.RemoveAll(t => t.time < Time - ThreatWindow);

            if (_target.HasValue)
                ShipX = Mathf.MoveTowards(ShipX, Mathf.Clamp(_target.Value, -ShipLimit, ShipLimit), ShipSpeed * dt);

            // The ship fires on its own.
            _fireTimer -= dt;
            if (_fireTimer <= 0f)
            {
                _fireTimer += FireEvery;
                _shots.Add(new StarShot(ShipX, GunY, ShotSpeed, 0f));
                events |= StarEvent.Fired;
            }
            foreach (var s in _shots) s.Y += s.Speed * dt;
            _shots.RemoveAll(s => s.Y > ShotTop);

            events |= MoveFoes(dt);
            events |= HitFoes();

            foreach (var b in _bullets) b.Y -= b.Speed * dt;
            _bullets.RemoveAll(b => b.Y < BulletBottom);

            if (Gem.HasValue)
            {
                var g = Gem.Value + Vector2.down * GemFall * dt;
                if (Mathf.Abs(g.y - ShipY) < GemCatch && Mathf.Abs(g.x - ShipX) < GemCatch)
                {
                    Gems++;
                    Score += GemPoints;
                    LastPoint = g;
                    Gem = null;
                    events |= StarEvent.Gem;
                }
                else Gem = g.y < BulletBottom ? (Vector2?)null : g;
            }

            if (SafeFor <= 0f) events |= HitShip();
            if (Over) return events | StarEvent.GameOver;
            if ((events & StarEvent.ShipHit) != 0) return events;

            // The next wave.
            if (_gapTimer < 0f && !_foes.Exists(f => f.Alive))
            {
                Score += WavePoints;
                _gapTimer = WaveGap;
                events |= StarEvent.WaveCleared;
            }
            if (_gapTimer >= 0f)
            {
                _gapTimer -= dt;
                if (_gapTimer < 0f)
                {
                    Wave++;
                    SpawnWave();
                    events |= StarEvent.WaveStarted;
                }
                return events;
            }

            // Bullets and dives.
            _bulletTimer -= dt;
            if (_bulletTimer <= 0f)
            {
                _bulletTimer += BulletEvery(Wave) * Mathf.Lerp(0.7f, 1.3f, Next());
                var from = Pick(f => f.State == FoeState.Formation);
                if (from != null && FireFrom(from.X, from.Y - from.Radius)) events |= StarEvent.EnemyFired;
            }
            _diveTimer -= dt;
            if (_diveTimer <= 0f)
            {
                _diveTimer += DiveEvery(Wave) * Mathf.Lerp(0.7f, 1.3f, Next());
                if (_foes.FindAll(f => f.State == FoeState.Diving).Count < MaxDivers(Wave))
                {
                    var diver = Pick(f => f.State == FoeState.Formation);
                    if (diver != null && StartDive(diver)) events |= StarEvent.Dived;
                }
            }
            return events;
        }

        float Next() => (float)_random.NextDouble();

        StarFoe Pick(Predicate<StarFoe> which)
        {
            var all = _foes.FindAll(which);
            return all.Count == 0 ? null : all[_random.Next(all.Count)];
        }

        /// <summary>
        /// Whether a threat can reach the ship's row at this time: fewer than <see cref="MaxThreats"/>
        /// others within <see cref="ThreatWindow"/> of it.
        /// </summary>
        public bool CanFire(float arrives)
        {
            int near = 0;
            foreach (var t in _threats) if (Mathf.Abs(t.time - arrives) < ThreatWindow) near++;
            return near < MaxThreats;
        }

        bool FireFrom(float x, float y)
        {
            float speed = BulletSpeed(Wave);
            float arrives = Time + (y - ShipY) / speed;
            if (!CanFire(arrives)) return false;
            _threats.Add((arrives, x));
            _bullets.Add(new StarShot(x, y, speed, arrives));
            return true;
        }

        // --- Enemies ----------------------------------------------------------------------------

        bool StartDive(StarFoe f)
        {
            f.DiveX = f.X;
            f.DiveY = f.Y;
            f.DiveStart = Time;
            f.DiveSpeed = DiveSpeedFor(Wave);
            f.Side = f.X < ShipX ? -1f : 1f;
            f.AimX = Mathf.Clamp(ShipX, -ShipLimit, ShipLimit);
            f.Sway = Mathf.Lerp(0.5f, 1.3f, Next()) * (Next() < 0.5f ? -1f : 1f);
            f.DiveFired = false;
            float crosses = Time + PeelTime + (f.DiveY - ShipY) / f.DiveSpeed;
            if (!CanFire(crosses)) return false;
            _threats.Add((crosses, f.AimX));
            f.State = FoeState.Diving;
            return true;
        }

        /// <summary>Where a diving enemy is, this many seconds into its dive. Its path is fixed when it starts.</summary>
        public static Vector2 DivePosition(StarFoe f, float t)
        {
            if (t < PeelTime)
            {
                float k = t / PeelTime;
                return new Vector2(f.DiveX + f.Side * PeelOut * k, f.DiveY + 0.7f * Mathf.Sin(Mathf.PI * k));
            }
            float tau = t - PeelTime;
            float y = f.DiveY - f.DiveSpeed * tau;
            float p = Mathf.Clamp01((f.DiveY - y) / (f.DiveY - ShipY));
            float x = Mathf.Lerp(f.DiveX + f.Side * PeelOut, f.AimX, Mathf.SmoothStep(0f, 1f, p)) + f.Sway * Mathf.Sin(tau * Mathf.PI * 2f / 1.6f);
            return new Vector2(Mathf.Clamp(x, -Style.WorldWidth / 2f + 0.4f, Style.WorldWidth / 2f - 0.4f), y);
        }

        /// <summary>For a diving enemy: where it will be this many seconds from now.</summary>
        public Vector2 DiverAhead(StarFoe f, float seconds) => DivePosition(f, Time - f.DiveStart + seconds);

        StarEvent MoveFoes(float dt)
        {
            var events = StarEvent.None;
            foreach (var f in _foes)
            {
                var slot = SlotPosition(f.Col, f.Row);
                switch (f.State)
                {
                    case FoeState.Formation:
                        f.X = slot.x;
                        f.Y = slot.y;
                        break;
                    case FoeState.Diving:
                        float t = Time - f.DiveStart;
                        var at = DivePosition(f, t);
                        f.X = at.x;
                        f.Y = at.y;
                        // One bullet on the way down, straight down from where it is.
                        if (!f.DiveFired && t > PeelTime + 0.3f)
                        {
                            f.DiveFired = true;
                            if (f.Y > ShipY + 3f && FireFrom(f.X, f.Y - f.Radius)) events |= StarEvent.EnemyFired;
                        }
                        if (f.Y < BottomOut)
                        {
                            f.State = FoeState.Returning;
                            f.Y = ReturnFrom;
                        }
                        break;
                    case FoeState.Returning:
                        var to = Vector2.MoveTowards(new Vector2(f.X, f.Y), slot, ReturnSpeed * dt);
                        f.X = to.x;
                        f.Y = to.y;
                        if ((to - slot).sqrMagnitude < 1e-4f) f.State = FoeState.Formation;
                        break;
                }
            }
            return events;
        }

        StarEvent HitFoes()
        {
            var events = StarEvent.None;
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var s = _shots[i];
                foreach (var f in _foes)
                {
                    if (!f.Alive || Mathf.Abs(s.X - f.X) > f.Radius || Mathf.Abs(s.Y - f.Y) > f.Radius) continue;
                    if ((new Vector2(s.X, s.Y) - new Vector2(f.X, f.Y)).sqrMagnitude > f.Radius * f.Radius) continue;
                    _shots.RemoveAt(i);
                    LastFoe = f;
                    LastPoint = new Vector2(f.X, f.Y);
                    if (--f.Hp > 0)
                    {
                        events |= StarEvent.Hit;
                        break;
                    }
                    bool diving = f.State == FoeState.Diving;
                    LastKillDived = diving;
                    _killed.Add((f, diving));
                    f.State = FoeState.Dead;
                    Kills++;
                    Score += (f.Kind == FoeKind.Brute ? BrutePoints : DronePoints) * (diving ? 2 : 1);
                    if (f.HasGem) Gem = LastPoint;
                    events |= StarEvent.Killed;
                    break;
                }
            }
            return events;
        }

        StarEvent HitShip()
        {
            var ship = new Vector2(ShipX, ShipY);
            foreach (var b in _bullets)
                if ((new Vector2(b.X, b.Y) - ship).sqrMagnitude < (ShipRadius + BulletRadius) * (ShipRadius + BulletRadius))
                    return LoseLife(new Vector2(b.X, b.Y));
            foreach (var f in _foes)
                if (f.State == FoeState.Diving && (new Vector2(f.X, f.Y) - ship).magnitude < ShipRadius + f.Radius)
                    return LoseLife(new Vector2(f.X, f.Y));
            return StarEvent.None;
        }

        StarEvent LoseLife(Vector2 at)
        {
            Lives--;
            LastPoint = at;
            SafeFor = SafeTime;
            // The bullets go, and with them their places in the threat count; divers on the way keep theirs.
            foreach (var b in _bullets) _threats.Remove((b.Arrives, b.X));
            _bullets.Clear();
            return StarEvent.ShipHit;
        }
    }
}
