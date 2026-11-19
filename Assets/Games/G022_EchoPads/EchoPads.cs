using TMPro;
using UnityEngine;

namespace AceBarcade.Games.EchoPads
{
    /// <summary>
    /// Game 022. Lily pads on a night pond light up in a tune; tap them back in the same order.
    /// Each round the tune comes back one note longer and a little faster, and a fifth pad joins
    /// at round 8. A yellow frog in the middle watches with you. Three lives.
    /// The rules live in <see cref="EchoSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(22, "echo-pads", "Echo Pads", Controls.Tap, Blurb = "Watch the pads, then tap them back")]
    public sealed class EchoPads : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        public static readonly Vector2 PondCenter = new Vector2(0f, -0.6f);
        const float PondRadius = 3.95f;

        /// <summary>A tap this close to a pad's middle counts as that pad (the pads are 2 units across).</summary>
        public const float HitRadius = 1.15f;

        const float LitScale = 1.08f;
        const float MoveTime = 0.45f;

        // Where the pads sit around the frog: four in a square, then five in a ring with the new
        // (brown) one at the bottom. Angles in degrees, by pad: blue, green, orange, purple, brown.
        static readonly float[] Angles4 = { 135f, 45f, -45f, -135f, -90f };
        static readonly float[] Angles5 = { 126f, 54f, -18f, 198f, -90f };
        const float Ring4 = 2.05f, Ring5 = 2.4f;

        static readonly string[] Names = { "blue", "green", "orange", "purple", "brown" };
        static readonly Color[] LitColors = { Palette.Sky, Palette.Lime, Palette.Yellow, Palette.Pink, Palette.Tan };

        EchoSim _sim;
        Transform[] _padRoots;
        SpriteRenderer[] _pads, _lives;
        Sprite[] _normal, _lit;
        float[] _tapLight;
        Transform _frog;
        TextMeshPro _status, _round;
        float _move = 1f;
        int _layoutPads;

        public EchoSim Sim => _sim;

        /// <summary>Where pad <paramref name="i"/> sits, with <paramref name="pads"/> on the pond.</summary>
        public static Vector2 PadPosition(int i, int pads)
        {
            float a = (pads > EchoSim.StartPads ? Angles5[i] : Angles4[i]) * Mathf.Deg2Rad;
            float r = pads > EchoSim.StartPads ? Ring5 : Ring4;
            return PondCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        /// <summary>The pad under a point, or -1.</summary>
        public static int PadAt(Vector2 p, int pads)
        {
            int best = -1;
            float bestD = HitRadius;
            for (int i = 0; i < pads; i++)
            {
                float d = (p - PadPosition(i, pads)).magnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        protected override void Build()
        {
            _sim = new EchoSim(SeedForTests ?? System.Environment.TickCount);
            var t = transform;

            // The night pond: a Slate pool with a few reeds' worth of ripples, no outlines.
            Shapes.Make(t, Shapes.Circle, Palette.Slate, PondCenter, Vector2.one * PondRadius * 2f, -20, "Pond");
            var random = new System.Random(14);
            for (int i = 0; i < 18; i++)
            {
                float size = Mathf.Lerp(0.14f, 0.24f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-4.6f, 4.6f, (float)random.NextDouble()), Mathf.Lerp(-8f, 6f, (float)random.NextDouble()));
                if ((at - PondCenter).magnitude < PondRadius + 0.3f) continue;
                Shapes.Make(t, Shapes.Circle, Palette.Slate, at, new Vector2(size, size), -21, "Star");
            }

            _normal = new Sprite[EchoSim.MaxPads];
            _lit = new Sprite[EchoSim.MaxPads];
            _pads = new SpriteRenderer[EchoSim.MaxPads];
            _padRoots = new Transform[EchoSim.MaxPads];
            _tapLight = new float[EchoSim.MaxPads];
            for (int i = 0; i < EchoSim.MaxPads; i++)
            {
                _normal[i] = GameSprite($"pad_{Names[i]}");
                _lit[i] = GameSprite($"pad_{Names[i]}_lit");
                // The root holds the place and the lit size; the sprite under it squashes and pops.
                _padRoots[i] = Make($"Pad {Names[i]}", PadPosition(i, EchoSim.StartPads)).transform;
                _pads[i] = new GameObject("Sprite").AddComponent<SpriteRenderer>();
                _pads[i].sprite = _normal[i];
                _pads[i].sortingOrder = 5;
                _pads[i].transform.SetParent(_padRoots[i], false);
                _padRoots[i].gameObject.SetActive(i < _sim.Pads);
            }
            _layoutPads = _sim.Pads;

            var frog = GameSprite("frog");
            _frog = MakeSprite("Frog", frog, PondCenter, 10).transform;

            _lives = new SpriteRenderer[EchoSim.StartLives];
            for (int i = 0; i < _lives.Length; i++)
            {
                _lives[i] = MakeSprite("Life", frog, new Vector2(-3.75f + i * 0.5f, 6.72f), 40);
                _lives[i].transform.localScale = Vector3.one * 0.3f;
            }

            _round = MakeText("", 6f, Palette.Cloud, new Vector2(0f, 4.3f));
            _status = MakeText("", 7f, Palette.Cloud, new Vector2(0f, -5.4f));

            SyncViews(0f);
        }

        TextMeshPro MakeText(string text, float size, Color color, Vector2 at)
        {
            var tmp = new GameObject("Text").AddComponent<TextMeshPro>();
            tmp.transform.SetParent(transform, false);
            tmp.transform.localPosition = at;
            tmp.font = UiKit.Font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
            tmp.sortingOrder = 30;
            return tmp;
        }

        protected override void Play(float dt)
        {
            int before = _sim.Score;
            var events = EchoEvent.None;
            // The touch that starts the game lands while the tune is showing, so it's never a note.
            if (Input.Down) events |= _sim.Tap(PadAt(Input.Position, _sim.Pads));
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & EchoEvent.Shown) != 0 && _sim.Lit >= 0) Juice.Squash(_pads[_sim.Lit].transform, 0.1f);
            if ((events & EchoEvent.YourTurn) != 0) Juice.Pop(_status.transform);

            if ((events & EchoEvent.Correct) != 0)
            {
                int pad = _sim.LastTap;
                _tapLight[pad] = EchoSim.TapLight;
                Juice.Squash(_pads[pad].transform, 0.12f);
                Juice.Burst(transform, PadPosition(pad, _sim.Pads), LitColors[pad], 6, 3f);
                Juice.Squash(_frog, 0.08f);
            }
            if ((events & EchoEvent.RoundDone) != 0)
            {
                Juice.Burst(transform, PondCenter, Palette.Lime, 14, 5f);
                Juice.Squash(_frog, 0.2f);
            }
            if ((events & EchoEvent.Gem) != 0)
            {
                Juice.Burst(transform, PondCenter + new Vector2(0f, 0.9f), Palette.Teal, 16, 6f);
                Juice.ShakeSmall();
            }
            if ((events & EchoEvent.Wrong) != 0)
            {
                int pad = _sim.LastTap;
                Juice.Burst(transform, PadPosition(pad, _sim.Pads), Palette.Red, 14, 5f);
                Juice.Squash(_pads[pad].transform, 0.2f);
                Juice.Squash(_frog, 0.25f);
                int lost = _sim.Lives;
                if (lost < _lives.Length) Juice.Burst(transform, _lives[lost].transform.localPosition, Palette.Red, 6, 2f);
                if ((events & EchoEvent.Over) != 0)
                {
                    Juice.ShakeBig();
                    EndRun();
                }
                else Juice.ShakeSmall();
            }
            if ((events & EchoEvent.NewPad) != 0)
            {
                _move = 0f;
                _padRoots[EchoSim.MaxPads - 1].gameObject.SetActive(true);
                Juice.Pop(_pads[EchoSim.MaxPads - 1].transform);
            }
            if ((events & EchoEvent.NewRound) != 0) Juice.Pop(_round.transform);
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            // Slide the pads into the ring of five when the fifth one joins.
            if (_move < 1f)
            {
                _move = Mathf.Min(1f, _move + dt / MoveTime);
                float k = 1f - (1f - _move) * (1f - _move);
                for (int i = 0; i < EchoSim.StartPads; i++)
                    _padRoots[i].localPosition = Vector2.Lerp(PadPosition(i, _layoutPads), PadPosition(i, _sim.Pads), k);
                if (_move >= 1f) _layoutPads = _sim.Pads;
            }
            _padRoots[EchoSim.MaxPads - 1].localPosition = PadPosition(EchoSim.MaxPads - 1, EchoSim.MaxPads);

            for (int i = 0; i < _pads.Length; i++)
            {
                _tapLight[i] = Mathf.Max(0f, _tapLight[i] - dt);
                bool lit = _sim.Lit == i || _tapLight[i] > 0f;
                var sprite = lit ? _lit[i] : _normal[i];
                if (_pads[i].sprite != sprite) _pads[i].sprite = sprite;
                _pads[i].sortingOrder = lit ? 6 : 5;
                _padRoots[i].localScale = Vector3.one * (lit ? LitScale : 1f);
            }

            // The frog leans toward the pad that's lit.
            int look = _sim.Lit >= 0 ? _sim.Lit : (_tapLight[Mathf.Max(0, _sim.LastTap)] > 0f ? _sim.LastTap : -1);
            Vector2 lean = look >= 0 ? (PadPosition(look, _sim.Pads) - PondCenter).normalized * 0.18f : Vector2.zero;
            var target = PondCenter + lean;
            _frog.localPosition = Vector2.Lerp(_frog.localPosition, target, 1f - Mathf.Exp(-14f * dt));
            _frog.localRotation = Quaternion.Euler(0f, 0f, -lean.x * 40f);

            for (int i = 0; i < _lives.Length; i++) _lives[i].enabled = i < _sim.Lives;

            _round.text = $"ROUND {_sim.Round}";
            bool echo = _sim.Phase == EchoPhase.Echo && !_sim.Over;
            _status.text = State == Phase.Ready ? "" : echo ? "YOUR TURN" : _sim.Over ? "" : _sim.Phase == EchoPhase.Wait ? "" : "WATCH";
            _status.color = echo ? Palette.Yellow : Palette.Cloud;
        }
    }
}
