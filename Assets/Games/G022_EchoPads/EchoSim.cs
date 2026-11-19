using System;
using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade.Games.EchoPads
{
    /// <summary>What happened during one <see cref="EchoSim.Step"/> or <see cref="EchoSim.Tap"/>.</summary>
    [Flags]
    public enum EchoEvent
    {
        None = 0,
        Shown = 1,
        YourTurn = 2,
        Correct = 4,
        RoundDone = 8,
        Gem = 16,
        Wrong = 32,
        Over = 64,
        NewRound = 128,
        NewPad = 256,
        Replay = 512,
    }

    public enum EchoPhase { Watch, Echo, Wait }

    /// <summary>
    /// The rules of Echo Pads, in plain C# so they can be tested. The pads light up one after
    /// another; then the player taps them back in the same order. Each round the same tune comes
    /// back one note longer. Each right tap scores 1, and every 5th round finished brings a gem
    /// worth 5. A wrong tap costs one of three lives and the round is shown again from the start;
    /// losing the third ends the run. The tune plays a little faster each round, and from round
    /// <see cref="FifthPadRound"/> a fifth pad joins in.
    /// </summary>
    public sealed class EchoSim
    {
        public const int StartPads = 4;
        public const int MaxPads = 5;

        /// <summary>The round the fifth pad joins; that round's new note is on it.</summary>
        public const int FifthPadRound = 8;

        public const int StartLives = 3;
        public const int TapPoints = 1;
        public const int GemPoints = 5;
        public const int GemEvery = 5;

        // Timing, in seconds.
        public const float LeadIn = 0.6f;
        public const float StartOn = 0.5f, MinOn = 0.26f, OnDrop = 0.02f;
        public const float StartGap = 0.22f, MinGap = 0.12f, GapDrop = 0.008f;
        public const float RoundPause = 0.7f;
        public const float MistakePause = 1.1f;

        /// <summary>How long a pad stays lit after the player taps it (for the view only).</summary>
        public const float TapLight = 0.22f;

        readonly System.Random _random;
        readonly List<int> _tune = new List<int>();
        float _time, _wait;
        bool _nextIsNewRound;
        int _shownStep = -1;

        public IReadOnlyList<int> Tune => _tune;
        public int Round => _tune.Count;
        public int Pads { get; private set; } = StartPads;
        public EchoPhase Phase { get; private set; }

        /// <summary>How many notes of this round the player has tapped back.</summary>
        public int Echoed { get; private set; }

        public int Lives { get; private set; } = StartLives;
        public int Score { get; private set; }
        public int Gems { get; private set; }
        public int Mistakes { get; private set; }
        public bool Over { get; private set; }

        /// <summary>The pad lit by the tune right now, or -1.</summary>
        public int Lit { get; private set; } = -1;

        /// <summary>The pad the last tap was on, right or wrong.</summary>
        public int LastTap { get; private set; } = -1;

        /// <summary>After a wrong tap: the pad that should have been tapped.</summary>
        public int Expected { get; private set; } = -1;

        public float OnTime => Mathf.Max(MinOn, StartOn - OnDrop * (Round - 1));
        public float GapTime => Mathf.Max(MinGap, StartGap - GapDrop * (Round - 1));

        public EchoSim(int seed)
        {
            _random = new System.Random(seed);
            AddNote();
            StartWatching();
        }

        public static int PadsFor(int round) => round >= FifthPadRound ? MaxPads : StartPads;

        /// <summary>Lengthens the tune by one note; true if that brought in a new pad.</summary>
        bool AddNote()
        {
            int round = _tune.Count + 1;
            int pads = PadsFor(round);
            bool newPad = pads > Pads;
            Pads = pads;

            int note;
            if (newPad) note = pads - 1; // show off the new pad straight away
            else
            {
                // Never the same pad three times running.
                int n = _tune.Count;
                bool twice = n >= 2 && _tune[n - 1] == _tune[n - 2];
                do note = _random.Next(pads);
                while (twice && note == _tune[n - 1]);
            }
            _tune.Add(note);
            return newPad;
        }

        void StartWatching()
        {
            Phase = EchoPhase.Watch;
            _time = -LeadIn;
            _shownStep = -1;
            Echoed = 0;
            Lit = -1;
        }

        public EchoEvent Step(float dt)
        {
            if (Over) return EchoEvent.None;
            var events = EchoEvent.None;

            if (Phase == EchoPhase.Wait)
            {
                _wait -= dt;
                if (_wait > 0f) return events;
                if (_nextIsNewRound)
                {
                    if (AddNote()) events |= EchoEvent.NewPad;
                    events |= EchoEvent.NewRound;
                }
                else events |= EchoEvent.Replay;
                StartWatching();
                return events;
            }

            if (Phase == EchoPhase.Watch)
            {
                _time += dt;
                float beat = OnTime + GapTime;
                if (_time < 0f) return events;
                int step = Mathf.FloorToInt(_time / beat);
                if (step >= _tune.Count)
                {
                    Lit = -1;
                    Phase = EchoPhase.Echo;
                    return events | EchoEvent.YourTurn;
                }
                bool on = _time - step * beat < OnTime;
                Lit = on ? _tune[step] : -1;
                if (on && step != _shownStep)
                {
                    _shownStep = step;
                    events |= EchoEvent.Shown;
                }
            }
            return events;
        }

        /// <summary>The player taps a pad. Only counts on the player's turn.</summary>
        public EchoEvent Tap(int pad)
        {
            if (Over || Phase != EchoPhase.Echo || pad < 0 || pad >= Pads) return EchoEvent.None;
            LastTap = pad;

            if (pad == _tune[Echoed])
            {
                Echoed++;
                Score += TapPoints;
                var events = EchoEvent.Correct;
                if (Echoed == _tune.Count)
                {
                    events |= EchoEvent.RoundDone;
                    if (Round % GemEvery == 0)
                    {
                        Gems++;
                        Score += GemPoints;
                        events |= EchoEvent.Gem;
                    }
                    Wait(RoundPause, true);
                }
                return events;
            }

            Expected = _tune[Echoed];
            Mistakes++;
            Lives--;
            if (Lives <= 0)
            {
                Over = true;
                return EchoEvent.Wrong | EchoEvent.Over;
            }
            Wait(MistakePause, false);
            return EchoEvent.Wrong;
        }

        void Wait(float seconds, bool newRound)
        {
            Phase = EchoPhase.Wait;
            _wait = seconds;
            _nextIsNewRound = newRound;
        }

        /// <summary>How long the whole tune takes to show this round, lead-in included.</summary>
        public float TuneTime => LeadIn + _tune.Count * (OnTime + GapTime);
    }
}
