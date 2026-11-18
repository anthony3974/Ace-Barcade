using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.PopUp
{
    /// <summary>What happened during one <see cref="PopSim.Step"/> or <see cref="PopSim.Tap"/>.</summary>
    [Flags]
    public enum PopEvent
    {
        None = 0,
        Popped = 1,
        Hit = 2,
        Gold = 4,
        Escaped = 8,
        Spiky = 16,
        OutOfLives = 32,
    }

    public enum Critter { Sprout, Gold, Spiky }

    public enum HoleState { Empty, Rising, Up, Sinking }

    /// <summary>One hole in the 3 x 3 field, and whatever is popping out of it.</summary>
    public sealed class PopHole
    {
        public int Index { get; }
        public HoleState State { get; internal set; }
        public Critter Kind { get; internal set; }

        /// <summary>Seconds since the hole changed state.</summary>
        public float Timer { get; internal set; }

        /// <summary>How long this critter stays fully up.</summary>
        public float UpFor { get; internal set; }

        /// <summary>It was tapped, and is on its way back down.</summary>
        public bool Bonked { get; internal set; }

        /// <summary>When the last critter finished going down (run time), and what it was.</summary>
        public float EmptySince { get; internal set; } = -99f;
        public Critter LastKind { get; internal set; }

        /// <summary>How far out of the hole the critter is, 0 (gone) to 1 (all the way up).</summary>
        public float Height => State switch
        {
            HoleState.Rising => Mathf.Clamp01(Timer / PopSim.RiseTime),
            HoleState.Up => 1f,
            HoleState.Sinking => 1f - Mathf.Clamp01(Timer / SinkTime),
            _ => 0f,
        };

        internal float SinkTime => Bonked ? PopSim.BonkSinkTime : PopSim.SinkTime;

        /// <summary>A tap now would count: it's far enough up and hasn't been tapped already.</summary>
        public bool Tappable => !Bonked && (State == HoleState.Up
            || (State == HoleState.Rising && Height >= PopSim.TapFromHeight)
            || (State == HoleState.Sinking && Height >= 0.5f));

        internal PopHole(int index) => Index = index;
    }

    /// <summary>
    /// The rules of Pop Up, in plain C# so they can be tested. Critters pop up out of a 3 x 3
    /// field of holes and go back down after a moment. Tap green sprouts for 1 and the rarer,
    /// quicker teal ones for 5. Tapping a red spiky one ends the run, and every sprout that gets
    /// away untapped costs one of three lives. As you score, critters come faster, stay up for
    /// less time, and more of them are red, up to a cap.
    /// Fair by design: a hole rests for a moment once its critter has gone, and a red one never
    /// pops up where a sprout has just been, so a late tap can't land on a red one.
    /// </summary>
    public sealed class PopSim
    {
        public const int Cols = 3;
        public const int Rows = 3;
        public const int Holes = Cols * Rows;

        // Where the holes are: the x of each column and the y of each row's lip.
        public const float ColSpacing = 2.9f;
        public const float TopLipY = 1.6f;
        public const float RowSpacing = 3f;

        // The area around a hole that counts as tapping its critter.
        public const float TapHalfWidth = 0.95f;
        public const float TapBelow = 0.5f;
        public const float TapAbove = 1.6f;

        public const float RiseTime = 0.12f;
        public const float SinkTime = 0.16f;
        public const float BonkSinkTime = 0.22f;
        public const float TapFromHeight = 0.3f;

        // Difficulty ramps from the start to the hardest over this many hits.
        public const int RampHits = 60;
        public const float StartUp = 1.5f, HardestUp = 0.8f;
        public const float StartEvery = 0.9f, HardestEvery = 0.45f;
        public const float StartSpiky = 0.15f, HardestSpiky = 0.3f;
        public const float GoldChance = 0.08f;
        public const float GoldUpShare = 0.6f;
        public const int StartMaxUp = 3, HardestMaxUp = 4;

        /// <summary>The first few critters are always sprouts.</summary>
        public const int GentleStart = 3;

        /// <summary>A hole stays empty this long after its critter has gone.</summary>
        public const float Rest = 0.35f;

        /// <summary>A spiky one never pops up in a hole a sprout left less than this long ago.</summary>
        public const float SpikyAfterSprout = 0.8f;

        public const int StartLives = 3;
        public const int SproutPoints = 1;
        public const int GoldPoints = 5;

        readonly System.Random _random;
        readonly PopHole[] _holes = new PopHole[Holes];
        readonly List<PopHole> _pick = new List<PopHole>();
        float _spawnTimer;
        int _popped;

        public IReadOnlyList<PopHole> Field => _holes;
        public float Time { get; private set; }
        public int Score { get; private set; }
        public int Hits { get; private set; }
        public int Golds { get; private set; }
        public int Lives { get; private set; } = StartLives;
        public int Escaped { get; private set; }
        public bool Over { get; private set; }

        /// <summary>The hole of the last critter tapped, or that got away.</summary>
        public int LastHole { get; private set; } = -1;

        /// <summary>0 at the start, 1 at the hardest.</summary>
        public float Difficulty => Mathf.Clamp01((float)Hits / RampHits);
        public float UpTime => Mathf.Lerp(StartUp, HardestUp, Difficulty);
        public float SpawnEvery => Mathf.Lerp(StartEvery, HardestEvery, Difficulty);
        public float SpikyChance => Mathf.Lerp(StartSpiky, HardestSpiky, Difficulty);
        public int MaxUp => Difficulty >= 0.5f ? HardestMaxUp : StartMaxUp;

        public PopSim(int seed)
        {
            _random = new System.Random(seed);
            for (int i = 0; i < Holes; i++) _holes[i] = new PopHole(i);
            _spawnTimer = StartEvery * 0.6f; // the first one comes quickly
        }

        public static Vector2 HoleCenter(int index) =>
            new Vector2((index % Cols - 1) * ColSpacing, TopLipY - index / Cols * RowSpacing);

        /// <summary>The hole whose critter a tap at this point would hit, or -1.</summary>
        public static int HoleAt(Vector2 p)
        {
            for (int i = 0; i < Holes; i++)
            {
                var c = HoleCenter(i);
                if (Mathf.Abs(p.x - c.x) <= TapHalfWidth && p.y >= c.y - TapBelow && p.y <= c.y + TapAbove) return i;
            }
            return -1;
        }

        public int UpCount()
        {
            int n = 0;
            foreach (var h in _holes) if (h.State != HoleState.Empty) n++;
            return n;
        }

        /// <summary>A tap on a hole (-1 for none). Only a critter that's <see cref="PopHole.Tappable"/> counts.</summary>
        public PopEvent Tap(int index)
        {
            if (Over || index < 0 || index >= Holes) return PopEvent.None;
            var h = _holes[index];
            if (!h.Tappable) return PopEvent.None;

            LastHole = index;
            if (h.Kind == Critter.Spiky)
            {
                Over = true;
                return PopEvent.Spiky;
            }

            // Knocked back down, from wherever it had got to.
            float height = h.Height;
            h.Bonked = true;
            h.State = HoleState.Sinking;
            h.Timer = (1f - height) * BonkSinkTime;
            Hits++;
            if (h.Kind == Critter.Gold)
            {
                Golds++;
                Score += GoldPoints;
                return PopEvent.Gold;
            }
            Score += SproutPoints;
            return PopEvent.Hit;
        }

        public PopEvent Step(float dt)
        {
            if (Over) return PopEvent.None;
            Time += dt;
            var events = PopEvent.None;

            foreach (var h in _holes)
            {
                if (h.State == HoleState.Empty) continue;
                h.Timer += dt;
                if (h.State == HoleState.Rising && h.Timer >= RiseTime) Next(h, HoleState.Up, h.Timer - RiseTime);
                if (h.State == HoleState.Up && h.Timer >= h.UpFor) Next(h, HoleState.Sinking, h.Timer - h.UpFor);
                if (h.State == HoleState.Sinking && h.Timer >= h.SinkTime)
                {
                    bool escaped = h.Kind == Critter.Sprout && !h.Bonked;
                    h.State = HoleState.Empty;
                    h.EmptySince = Time;
                    h.LastKind = h.Kind;
                    h.Bonked = false;
                    if (escaped)
                    {
                        Escaped++;
                        Lives--;
                        LastHole = h.Index;
                        events |= PopEvent.Escaped;
                        if (Lives <= 0)
                        {
                            Over = true;
                            return events | PopEvent.OutOfLives;
                        }
                    }
                }
            }

            _spawnTimer += dt;
            if (_spawnTimer >= SpawnEvery && UpCount() < MaxUp && Spawn())
            {
                _spawnTimer = 0f;
                events |= PopEvent.Popped;
            }
            return events;
        }

        static void Next(PopHole h, HoleState state, float carry)
        {
            h.State = state;
            h.Timer = carry;
        }

        bool Spawn()
        {
            var kind = Critter.Sprout;
            if (_popped >= GentleStart)
            {
                double roll = _random.NextDouble();
                if (roll < GoldChance) kind = Critter.Gold;
                else if (roll < GoldChance + SpikyChance) kind = Critter.Spiky;
            }

            if (!Pick(kind, out var hole))
            {
                if (kind != Critter.Spiky || !Pick(Critter.Sprout, out hole)) return false;
                kind = Critter.Sprout;
            }

            hole.Kind = kind;
            hole.State = HoleState.Rising;
            hole.Timer = 0f;
            hole.Bonked = false;
            hole.UpFor = kind == Critter.Gold ? UpTime * GoldUpShare : UpTime;
            _popped++;
            return true;
        }

        bool Pick(Critter kind, out PopHole hole)
        {
            _pick.Clear();
            foreach (var h in _holes)
            {
                if (h.State != HoleState.Empty || Time - h.EmptySince < Rest) continue;
                if (kind == Critter.Spiky && h.LastKind != Critter.Spiky && Time - h.EmptySince < SpikyAfterSprout) continue;
                _pick.Add(h);
            }
            hole = _pick.Count > 0 ? _pick[_random.Next(_pick.Count)] : null;
            return hole != null;
        }
    }
}
