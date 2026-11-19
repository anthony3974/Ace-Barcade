using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.SliceStorm
{
    /// <summary>What happened on a <see cref="SliceSim.Step"/>, <see cref="SliceSim.Slice"/> or <see cref="SliceSim.EndStroke"/>.</summary>
    [Flags]
    public enum SliceEvent
    {
        None = 0,
        Launched = 1,  // something was tossed up
        Sliced = 2,    // one or more veggies were cut (see LastCut)
        Combo = 4,     // a stroke that cut three or more ended (see LastCombo)
        Dropped = 8,   // a veggie fell away uncut and cost a life (see LastDropX)
        Thorned = 16,  // a thornball was cut: the run is over
        Over = 32,
    }

    /// <summary>One thing in the air.</summary>
    public sealed class Thing
    {
        public int Id;
        /// <summary>0 to <see cref="SliceSim.Kinds"/> - 1 for a veggie; ignored for a thornball.</summary>
        public int Kind;
        public bool Thorn;
        public Vector2 Pos, Vel;
        public float Angle, Spin;
        public float Radius => Thorn ? SliceSim.ThornRadius : SliceSim.VegRadius;
    }

    /// <summary>A veggie that was cut, and which way the stroke was going.</summary>
    public struct Cut
    {
        public Thing Thing;
        public Vector2 Dir;
    }

    /// <summary>
    /// The rules of Slice Storm, in plain C# so edit mode tests can play it. Veggies are tossed up
    /// from below the screen in waves; swipe through them to cut them. Each cut scores 1, and a
    /// single stroke that cuts three or more adds a bonus of as many again. A veggie that falls
    /// back out of sight uncut costs one of three lives. Red thornballs fly up with them from the
    /// fourth wave: cut one and the run is over. Waves get bigger, closer together and thornier.
    ///
    /// Only a moving finger cuts (at least <see cref="MinSpeed"/>), so resting a thumb on the
    /// screen never slices anything.
    /// </summary>
    public sealed class SliceSim
    {
        public const int Kinds = 4;
        public const int StartLives = 3;

        public const float Gravity = 13f;
        public const float LaunchY = -9.2f;
        /// <summary>Falling below this, a thing is gone.</summary>
        public const float GoneY = -9.6f;
        public const float MinApexY = -0.5f, MaxApexY = 4.6f;
        public const float MaxApexX = 3.1f;
        public const float StartX = 3.4f;

        public const float VegRadius = 0.52f;
        public const float ThornRadius = 0.48f;

        /// <summary>Finger speed needed to cut, in units per second.</summary>
        public const float MinSpeed = 5f;

        public const float FirstWave = 0.6f;
        public const int MaxWaveSize = 6;
        public const float Stagger = 0.22f;
        public const int FirstThornWave = 3;
        public const float MaxThornChance = 0.3f;
        /// <summary>A thornball is never launched this close (in x, at the bottom) to a veggie of the same wave.</summary>
        public const float ThornSpacing = 1.4f;

        public int Score { get; private set; }
        public int Lives { get; private set; } = StartLives;
        public bool Over { get; private set; }

        /// <summary>Waves launched so far.</summary>
        public int Waves { get; private set; }

        public int Cuts { get; private set; }

        public IReadOnlyList<Thing> Things => _things;
        public IReadOnlyList<Cut> LastCut => _lastCut;

        /// <summary>Veggies cut in the stroke so far.</summary>
        public int StrokeCuts { get; private set; }
        public int LastCombo { get; private set; }
        public float LastDropX { get; private set; }
        public Vector2 ThornAt { get; private set; }

        readonly List<Thing> _things = new List<Thing>();
        readonly List<Cut> _lastCut = new List<Cut>();
        readonly List<(float at, bool thorn, float x)> _queue = new List<(float, bool, float)>();
        readonly System.Random _random;
        float _clock, _nextWave = FirstWave;
        int _nextId;

        public SliceSim(int seed) => _random = new System.Random(seed);

        float Range(float a, float b) => a + (float)_random.NextDouble() * (b - a);

        public static int WaveSize(int wave) => Mathf.Min(MaxWaveSize, 1 + wave / 2);

        public static float WaveGap(int wave) => Mathf.Max(1.1f, 2.2f - wave * 0.05f);

        public static float ThornChance(int wave) =>
            wave < FirstThornWave ? 0f : Mathf.Min(MaxThornChance, 0.12f + (wave - FirstThornWave) * 0.012f);

        /// <summary>The speed to throw at to peak at (apexX, apexY) from (x, LaunchY).</summary>
        public static Vector2 LaunchVelocity(float x, float apexX, float apexY)
        {
            float vy = Mathf.Sqrt(2f * Gravity * (apexY - LaunchY));
            float up = vy / Gravity;
            return new Vector2((apexX - x) / up, vy);
        }

        // --- Time -----------------------------------------------------------------------------

        public SliceEvent Step(float dt)
        {
            if (Over) return SliceEvent.None;
            var events = SliceEvent.None;
            _clock += dt;

            if (_clock >= _nextWave) PlanWave();
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (_queue[i].at > _clock) continue;
                Launch(_queue[i].thorn, _queue[i].x);
                _queue.RemoveAt(i);
                events |= SliceEvent.Launched;
            }

            for (int i = _things.Count - 1; i >= 0; i--)
            {
                var t = _things[i];
                // Exact under constant gravity, so things peak just where they were thrown to.
                t.Pos += new Vector2(t.Vel.x, t.Vel.y - 0.5f * Gravity * dt) * dt;
                t.Vel.y -= Gravity * dt;
                t.Angle += t.Spin * dt;
                if (t.Pos.y >= GoneY || t.Vel.y > 0f) continue;
                _things.RemoveAt(i);
                if (t.Thorn) continue;
                Lives--;
                LastDropX = t.Pos.x;
                events |= SliceEvent.Dropped;
                if (Lives <= 0)
                {
                    Lives = 0;
                    Over = true;
                    events |= SliceEvent.Over;
                    break;
                }
            }
            return events;
        }

        void PlanWave()
        {
            int n = WaveSize(Waves);
            float chance = ThornChance(Waves);
            var thorns = new bool[n];
            var xs = new float[n];
            for (int i = 1; i < n; i++) thorns[i] = _random.NextDouble() < chance; // the first is always a veggie

            // Veggies anywhere; thornballs only where no veggie of the wave starts close by
            // (a thornball with no room stays home).
            for (int i = 0; i < n; i++)
                if (!thorns[i]) xs[i] = Range(-StartX, StartX);
            for (int i = 0; i < n; i++)
            {
                if (!thorns[i]) continue;
                xs[i] = float.NaN;
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    float x = Range(-StartX, StartX);
                    bool ok = true;
                    for (int j = 0; j < n; j++)
                        if (!thorns[j] && Mathf.Abs(xs[j] - x) < ThornSpacing) { ok = false; break; }
                    if (ok) { xs[i] = x; break; }
                }
            }

            // Small waves go up together; bigger ones come one after another.
            bool together = n <= 2 || _random.NextDouble() < 0.35;
            for (int i = 0; i < n; i++)
                if (!float.IsNaN(xs[i])) _queue.Add((_clock + (together ? 0f : i * Stagger), thorns[i], xs[i]));

            float span = together ? 0f : (n - 1) * Stagger;
            _nextWave = _clock + span + WaveGap(Waves);
            Waves++;
        }

        void Launch(bool thorn, float x)
        {
            var t = new Thing
            {
                Id = _nextId++,
                Thorn = thorn,
                Kind = thorn ? 0 : _random.Next(Kinds),
                Pos = new Vector2(x, LaunchY),
                Spin = Range(-220f, 220f),
                Angle = Range(0f, 360f),
            };
            float apexX = Mathf.Clamp(x * 0.4f + Range(-1.6f, 1.6f), -MaxApexX, MaxApexX);
            t.Vel = LaunchVelocity(x, apexX, Range(MinApexY, MaxApexY));
            _things.Add(t);
        }

        // --- The finger -----------------------------------------------------------------------

        /// <summary>
        /// The finger moved from a to b over dt seconds. Cuts every veggie the line passes
        /// through, if the finger was moving fast enough; a thornball on the line ends the run.
        /// </summary>
        public SliceEvent Slice(Vector2 a, Vector2 b, float dt)
        {
            _lastCut.Clear();
            if (Over || dt <= 0f) return SliceEvent.None;
            var move = b - a;
            if (move.magnitude / dt < MinSpeed) return SliceEvent.None;

            var events = SliceEvent.None;
            var dir = move.normalized;
            for (int i = _things.Count - 1; i >= 0; i--)
            {
                var t = _things[i];
                if (DistanceToSegment(t.Pos, a, b) > t.Radius) continue;
                if (t.Thorn)
                {
                    _things.RemoveAt(i);
                    ThornAt = t.Pos;
                    Over = true;
                    return events | SliceEvent.Thorned | SliceEvent.Over;
                }
                _things.RemoveAt(i);
                _lastCut.Add(new Cut { Thing = t, Dir = dir });
                Score++;
                Cuts++;
                StrokeCuts++;
                events |= SliceEvent.Sliced;
            }
            return events;
        }

        /// <summary>The finger lifted. Three or more cuts in the stroke score as many again.</summary>
        public SliceEvent EndStroke()
        {
            int n = StrokeCuts;
            StrokeCuts = 0;
            if (Over || n < 3) return SliceEvent.None;
            LastCombo = n;
            Score += n;
            return SliceEvent.Combo;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        // --- For tests ------------------------------------------------------------------------

        /// <summary>For tests: set the lives left.</summary>
        public void SetLives(int lives) => Lives = lives;

        /// <summary>For tests: put a thing in the air. Stops the waves unless told otherwise.</summary>
        public Thing AddForTests(bool thorn, Vector2 pos, Vector2 vel, bool keepWaves = false)
        {
            if (!keepWaves) _nextWave = float.MaxValue;
            var t = new Thing { Id = _nextId++, Thorn = thorn, Pos = pos, Vel = vel };
            _things.Add(t);
            return t;
        }
    }
}
