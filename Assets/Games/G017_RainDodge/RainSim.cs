using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.RainDodge
{
    /// <summary>What happened during one <see cref="RainSim.Step"/>.</summary>
    [Flags]
    public enum RainEvent
    {
        None = 0,
        Splash = 1,
        Gem = 2,
        Leaf = 4,
        ShieldLost = 8,
        Warn = 16,
        Strike = 32,
        Hit = 64,
    }

    public enum DropKind { Small, Big, Sway }

    /// <summary>
    /// A red drop falling from the clouds. It falls at a steady speed (a swaying one also drifts
    /// from side to side), so where it will be is known: see <see cref="At"/>.
    /// </summary>
    public sealed class RainDrop
    {
        public int Index { get; }
        public DropKind Kind { get; }
        public float X0 { get; }
        public float Born { get; }
        public float Speed { get; }

        /// <summary>The drawn size; it hits a little inside it (<see cref="HitRadius"/>).</summary>
        public float Radius { get; }

        public float Sway { get; }
        public float Phase { get; }
        public float HitRadius => Radius * RainSim.HitShrink;

        public RainDrop(int index, DropKind kind, float x0, float born, float speed, float radius, float sway = 0f, float phase = 0f)
        {
            Index = index;
            Kind = kind;
            X0 = x0;
            Born = born;
            Speed = speed;
            Radius = radius;
            Sway = sway;
            Phase = phase;
        }

        /// <summary>Where it is (or will be) at this run time.</summary>
        public Vector2 At(float time)
        {
            float age = time - Born;
            return new Vector2(X0 + Sway * Mathf.Sin(age * RainSim.SwayRate + Phase), RainSim.SpawnY - Speed * age);
        }

        /// <summary>How fast it drifts sideways at this run time.</summary>
        public float DriftAt(float time) => Sway * RainSim.SwayRate * Mathf.Cos((time - Born) * RainSim.SwayRate + Phase);
    }

    /// <summary>A lightning strike down one column: a warning first, then the bolt.</summary>
    public sealed class Bolt
    {
        public int Index { get; }
        public float X { get; }
        public float WarnAt { get; }
        public float StrikeAt => WarnAt + RainSim.Warning;
        public float EndAt => StrikeAt + RainSim.StrikeTime;

        public Bolt(int index, float x, float warnAt)
        {
            Index = index;
            X = x;
            WarnAt = warnAt;
        }

        public bool StrikingAt(float time) => time >= StrikeAt && time < EndAt;
    }

    /// <summary>Something to grab: a teal gem, or a green leaf that shields the bee from one hit.</summary>
    public sealed class RainPickup
    {
        public Vector2 At { get; }
        public bool Leaf { get; }
        public float Born { get; }
        public float Until { get; }

        public RainPickup(Vector2 at, bool leaf, float born, float until)
        {
            At = at;
            Leaf = leaf;
            Born = born;
            Until = until;
        }
    }

    /// <summary>
    /// The rules of Rain Dodge, in plain C# so they can be tested. A bee is dragged around the
    /// lower part of a stormy field while red drops fall from the clouds. Every drop that reaches
    /// the ground scores 1, and teal gems score 5; a drop or a lightning bolt touching the bee ends
    /// the run, unless it's holding a leaf, which takes the hit instead. It gets busier and faster
    /// up to <see cref="HardestAt"/> seconds: big slow drops, swaying ones, then lightning.
    /// <para>
    /// <b>Fair by design:</b> the bee's field stops well below the clouds, so every drop is seen
    /// falling before it gets there; drops never come down right next to the last two; lightning
    /// warns for a full second before it strikes, and no new drop starts down its column meanwhile;
    /// everything hits a little inside its drawn edge.
    /// </para>
    /// Every 4th drop falls over the bee, so standing still isn't safe.
    /// </summary>
    public sealed class RainSim
    {
        // The field.
        public const float SpawnY = 6.4f, CloudBottom = 5.5f, GroundY = -6.9f;

        // The bee.
        public const float BeeRadius = 0.32f;
        public const float MinX = -4f, MaxX = 4f, MinY = -6.3f, MaxY = 1.5f;
        public static readonly Vector2 StartAt = new Vector2(0f, -4f);
        public const float MaxSpeed = 20f;

        // Drops.
        public const float SmallRadius = 0.28f, BigRadius = 0.5f, HitShrink = 0.8f;
        public const float StartInterval = 0.7f, MinInterval = 0.17f;
        public const float StartSpeed = 4.5f, TopSpeed = 9f, SpeedJitter = 0.1f;
        public const float HardestAt = 80f;
        public const float BigFrom = 12f, BigChance = 0.2f, BigSlow = 0.7f;
        public const float SwayFrom = 35f, SwayChance = 0.2f, SwayWidth = 0.9f, SwayRate = 3.5f;
        public const int AimEvery = 4;
        public const float AimJitter = 0.4f;

        /// <summary>A new drop never comes down this close to either of the last two.</summary>
        public const float Spread = 1f;

        // Lightning.
        public const float BoltsFrom = 25f, BoltStartGap = 7f, BoltMinGap = 3.2f;
        public const float Warning = 1f, StrikeTime = 0.3f, BoltHalfWidth = 0.45f;

        /// <summary>No drop starts falling this close to a bolt's column while it warns or strikes.</summary>
        public const float BoltClear = 1.1f;

        // Pickups.
        public const float GemsFrom = 3f, GemGapMin = 2.5f, GemGapMax = 4.5f, GemLife = 6f, PickupRadius = 0.4f;
        public const int GemPoints = 5;
        public const float LeafFrom = 30f, LeafGap = 30f, LeafRetry = 12f, LeafLife = 8f;
        public const float SafeAfterLeaf = 1.2f;

        readonly System.Random _random;
        readonly List<RainDrop> _drops = new List<RainDrop>();
        readonly List<Bolt> _bolts = new List<Bolt>();
        readonly List<Vector2> _splashes = new List<Vector2>();
        readonly List<float> _spots = new List<float>();
        int _dropCount, _boltCount;
        float _nextDrop, _nextBolt = BoltsFrom, _nextGem = GemsFrom, _nextLeaf = LeafFrom;
        float _lastX = float.NaN, _lastX2 = float.NaN;

        public RainSim(int seed)
        {
            _random = new System.Random(seed);
            Bee = StartAt;
            Target = StartAt;
            _nextDrop = 0.6f;
        }

        public float Time { get; private set; }
        public Vector2 Bee { get; private set; }
        public Vector2 Target { get; private set; }
        public int Score { get; private set; }
        public int GemsTaken { get; private set; }
        public int Dodged { get; private set; }
        public bool Over { get; private set; }

        /// <summary>Holding a leaf: the next hit takes it instead of ending the run.</summary>
        public bool Shield { get; private set; }

        /// <summary>Seconds left of the short safe spell after the leaf takes a hit.</summary>
        public float SafeFor { get; private set; }

        public IReadOnlyList<RainDrop> Drops => _drops;
        public IReadOnlyList<Bolt> Bolts => _bolts;
        public RainPickup Gem { get; private set; }
        public RainPickup Leaf { get; private set; }

        /// <summary>Where drops landed on the ground this step.</summary>
        public IReadOnlyList<Vector2> Splashes => _splashes;

        /// <summary>Where the last gem, leaf or hit happened.</summary>
        public Vector2 LastPoint { get; private set; }

        /// <summary>The drop that hit (the bee or its leaf) last, if it was a drop.</summary>
        public RainDrop LastDrop { get; private set; }

        /// <summary>0 at the start, 1 from <see cref="HardestAt"/> on.</summary>
        public float Hardness => Mathf.Clamp01(Time / HardestAt);

        public float Interval => Mathf.Lerp(StartInterval, MinInterval, Hardness);
        public float DropSpeed => Mathf.Lerp(StartSpeed, TopSpeed, Hardness);

        /// <summary>Steer the bee toward this point; it flies there at up to <see cref="MaxSpeed"/>.</summary>
        public void SteerTo(Vector2 point) =>
            Target = new Vector2(Mathf.Clamp(point.x, MinX, MaxX), Mathf.Clamp(point.y, MinY, MaxY));

        /// <summary>The finger moved: the bee's target moves the same way.</summary>
        public void Drag(Vector2 delta) => SteerTo(Target + delta);

        /// <summary>The finger touched down: the target starts where the bee is, so nothing jumps.</summary>
        public void Grab() => Target = Bee;

        /// <summary>For tests: a drop, placed by hand.</summary>
        public RainDrop AddDrop(DropKind kind, float x, float y, float speed)
        {
            float radius = kind == DropKind.Big ? BigRadius : SmallRadius;
            var drop = new RainDrop(_dropCount++, kind, x, Time - (SpawnY - y) / speed, speed, radius);
            _drops.Add(drop);
            return drop;
        }

        /// <summary>For tests: a bolt that starts warning now.</summary>
        public Bolt AddBolt(float x)
        {
            var bolt = new Bolt(_boltCount++, x, Time);
            _bolts.Add(bolt);
            return bolt;
        }

        /// <summary>For tests: put a pickup here, now.</summary>
        public void PlacePickup(Vector2 at, bool leaf)
        {
            var p = new RainPickup(at, leaf, Time, Time + (leaf ? LeafLife : GemLife));
            if (leaf) Leaf = p;
            else Gem = p;
        }

        /// <summary>For tests: stop the rain coming on its own.</summary>
        public bool Calm { get; set; }

        public RainEvent Step(float dt)
        {
            _splashes.Clear();
            if (Over) return RainEvent.None;
            var events = RainEvent.None;
            Time += dt;
            Bee = Vector2.MoveTowards(Bee, Target, MaxSpeed * dt);
            SafeFor = Mathf.Max(0f, SafeFor - dt);

            if (!Calm)
            {
                _nextDrop -= dt;
                while (_nextDrop <= 0f)
                {
                    SpawnDrop();
                    _nextDrop += Interval;
                }
                if (Time >= _nextBolt && _bolts.Count == 0)
                {
                    SpawnBolt();
                    events |= RainEvent.Warn;
                }
            }

            // Bolts: strike, then fade and count as dodged.
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                if (b.StrikeAt > Time - dt && b.StrikeAt <= Time) events |= RainEvent.Strike;
                if (Time >= b.EndAt)
                {
                    _bolts.RemoveAt(i);
                    Score++;
                    Dodged++;
                }
            }

            // Drops that reach the ground splash and score.
            for (int i = _drops.Count - 1; i >= 0; i--)
            {
                var at = _drops[i].At(Time);
                if (at.y - _drops[i].Radius > GroundY) continue;
                _splashes.Add(new Vector2(at.x, GroundY));
                _drops.RemoveAt(i);
                Score++;
                Dodged++;
                events |= RainEvent.Splash;
            }

            events |= Pickups();
            events |= Hits();
            return events;
        }

        RainEvent Hits()
        {
            if (SafeFor > 0f) return RainEvent.None;
            for (int i = 0; i < _drops.Count; i++)
            {
                var d = _drops[i];
                var at = d.At(Time);
                float reach = BeeRadius + d.HitRadius;
                if ((at - Bee).sqrMagnitude >= reach * reach) continue;
                LastDrop = d;
                LastPoint = Vector2.Lerp(Bee, at, BeeRadius / reach);
                _drops.RemoveAt(i);
                return Hurt();
            }
            foreach (var b in _bolts)
            {
                if (!b.StrikingAt(Time) || Mathf.Abs(Bee.x - b.X) >= BoltHalfWidth + BeeRadius) continue;
                LastDrop = null;
                LastPoint = Bee;
                return Hurt();
            }
            return RainEvent.None;
        }

        RainEvent Hurt()
        {
            if (Shield)
            {
                Shield = false;
                SafeFor = SafeAfterLeaf;
                _nextLeaf = Mathf.Max(_nextLeaf, Time + LeafRetry);
                return RainEvent.ShieldLost;
            }
            Over = true;
            return RainEvent.Hit;
        }

        RainEvent Pickups()
        {
            var events = RainEvent.None;
            float reach = BeeRadius + PickupRadius;

            if (Gem != null)
            {
                if ((Gem.At - Bee).sqrMagnitude < reach * reach)
                {
                    LastPoint = Gem.At;
                    Gem = null;
                    Score += GemPoints;
                    GemsTaken++;
                    _nextGem = Time + Range(GemGapMin, GemGapMax);
                    events |= RainEvent.Gem;
                }
                else if (Time >= Gem.Until)
                {
                    Gem = null;
                    _nextGem = Time + Range(GemGapMin, GemGapMax);
                }
            }
            else if (!Calm && Time >= _nextGem) Gem = new RainPickup(PickupSpot(), false, Time, Time + GemLife);

            if (Leaf != null)
            {
                if ((Leaf.At - Bee).sqrMagnitude < reach * reach)
                {
                    LastPoint = Leaf.At;
                    Leaf = null;
                    Shield = true;
                    _nextLeaf = Time + LeafGap;
                    events |= RainEvent.Leaf;
                }
                else if (Time >= Leaf.Until)
                {
                    Leaf = null;
                    _nextLeaf = Time + LeafRetry;
                }
            }
            else if (!Calm && !Shield && Time >= _nextLeaf) Leaf = new RainPickup(PickupSpot(), true, Time, Time + LeafLife);
            return events;
        }

        /// <summary>Somewhere in the bee's field, a fair way from the bee and the other pickup.</summary>
        Vector2 PickupSpot()
        {
            var best = Vector2.zero;
            float bestScore = -1f;
            for (int i = 0; i < 12; i++)
            {
                var at = new Vector2(Range(MinX + 0.3f, MaxX - 0.3f), Range(MinY + 0.4f, MaxY - 0.5f));
                var other = Gem ?? Leaf;
                float score = Mathf.Min((at - Bee).magnitude / 2.5f, other != null ? (at - other.At).magnitude / 1.5f : 9f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = at;
                }
                if (score >= 1f) break;
            }
            return best;
        }

        void SpawnDrop()
        {
            float k = Hardness;
            var kind = DropKind.Small;
            double roll = _random.NextDouble();
            if (Time >= SwayFrom && roll < SwayChance) kind = DropKind.Sway;
            else if (Time >= BigFrom && roll > 1.0 - BigChance) kind = DropKind.Big;

            float radius = kind == DropKind.Big ? BigRadius : SmallRadius;
            float speed = DropSpeed * (1f + Range(-SpeedJitter, SpeedJitter)) * (kind == DropKind.Big ? BigSlow : 1f);
            float sway = kind == DropKind.Sway ? SwayWidth : 0f;
            float edge = 4.5f - radius - sway;
            bool aimed = _dropCount % AimEvery == AimEvery - 1;

            float x = float.NaN;
            for (int tries = 0; tries < 8 && float.IsNaN(x); tries++)
            {
                float at = Mathf.Clamp(aimed && tries < 4 ? Bee.x + Range(-AimJitter, AimJitter) : Range(-edge, edge), -edge, edge);
                if (Allowed(at)) x = at;
            }
            if (float.IsNaN(x))
            {
                // Unlucky: pick at random from every allowed spot. The two drops and a bolt can
                // never cover the whole width, so there always is one.
                _spots.Clear();
                for (float at = -edge; at <= edge; at += 0.1f) if (Allowed(at)) _spots.Add(at);
                x = _spots.Count > 0 ? _spots[_random.Next(_spots.Count)] : Range(-edge, edge);
            }
            float phase = kind == DropKind.Sway ? Range(0f, Mathf.PI * 2f) : 0f;
            _drops.Add(new RainDrop(_dropCount++, kind, x, Time, speed, radius, sway, phase));
            _lastX2 = _lastX;
            _lastX = x;
        }

        void SpawnBolt()
        {
            float x = _boltCount % 2 == 0 ? Bee.x + Range(-0.5f, 0.5f) : Range(MinX + 0.2f, MaxX - 0.2f);
            x = Mathf.Clamp(x, MinX + 0.2f, MaxX - 0.2f);
            _bolts.Add(new Bolt(_boltCount++, x, Time));
            float k = Mathf.Clamp01((Time - BoltsFrom) / (HardestAt - BoltsFrom));
            _nextBolt = Time + Warning + StrikeTime + Mathf.Lerp(BoltStartGap, BoltMinGap, k);
        }

        bool Allowed(float x)
        {
            if (Near(x, _lastX, Spread) || Near(x, _lastX2, Spread)) return false;
            foreach (var b in _bolts) if (Mathf.Abs(x - b.X) < BoltClear) return false;
            return true;
        }

        static bool Near(float a, float b, float d) => !float.IsNaN(b) && Mathf.Abs(a - b) < d;

        float Range(float a, float b) => a + (float)_random.NextDouble() * (b - a);
    }
}
