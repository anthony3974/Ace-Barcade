using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AceBarcade.Games.PailDrop
{
    /// <summary>
    /// Game 024. A yellow critter hangs from a pulley on a beam, holding a ball over a green pail
    /// that slides back and forth along a track; tap to drop the ball into the pail. Misses cost
    /// one of three lives; tapping while it holds a red thornball ends the run.
    /// The rules live in <see cref="PailSim"/>; this class draws them and adds the feel.
    /// </summary>
    [ArcadeGame(24, "pail-drop", "Pail Drop", Controls.Tap, Blurb = "Tap to drop the ball in the pail")]
    public sealed class PailDrop : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        const float BeamY = 5.4f;
        const float CritterAbove = 0.75f;
        const float GroundY = -6.3f;
        const float BodyHeight = 1.2f;
        const float ThornScale = 0.85f;
        const float GemScale = 0.68f;

        // Sorting: the track and wheels, the ball, then the pail in front of it.
        const int BeamOrder = 0, RopeOrder = 2, WheelOrder = 4, CritterOrder = 6, BallOrder = 8,
            PailOrder = 10, LooseOrder = 20;

        sealed class Loose
        {
            public Transform T;
            public Vector2 Velocity;
            public float Spin, Life;
            public bool Bursts;
            /// <summary>Sinking into the pail: kept this far from its middle as it slides.</summary>
            public bool Sinking;
            public float OffsetX;
        }

        PailSim _sim;
        Transform _critter, _pulley, _rope, _pail, _ball;
        SpriteRenderer _ballRenderer;
        SpriteRenderer[] _lives;
        Sprite _ballSprite, _gemSprite, _thornSprite;
        TextMeshPro _flash;
        float _flashFor, _lastHookX, _tilt;
        int _shownBall = -1;
        float _shownWidth = -1f;
        bool _starting;
        readonly List<Loose> _loose = new List<Loose>();

        public PailSim Sim => _sim;

        protected override void Build()
        {
            _sim = new PailSim(SeedForTests ?? System.Environment.TickCount);
            _ballSprite = PropSprite("ball");
            _gemSprite = PropSprite("gem");
            _thornSprite = GameSprite("thorn");
            var t = transform;

            // A night sky with a few Slate stars, clear of the drop.
            var random = new System.Random(24);
            for (int i = 0; i < 20; i++)
            {
                float size = Mathf.Lerp(0.14f, 0.26f, (float)random.NextDouble());
                var at = new Vector2(Mathf.Lerp(-4.4f, 4.4f, (float)random.NextDouble()), Mathf.Lerp(-3.4f, 4.6f, (float)random.NextDouble()));
                if (Mathf.Abs(at.x) < 2.6f) continue;
                Shapes.Make(t, Shapes.Circle, Palette.Slate, at, new Vector2(size, size), -20, "Star");
            }

            // Slate hills behind the track.
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-3.2f, -6.7f), new Vector2(4.6f, 4.6f), -15, "Hill");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(0.9f, -7.4f), new Vector2(5.6f, 5.6f), -15, "Hill");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(4.5f, -6.5f), new Vector2(3.6f, 3.6f), -15, "Hill");

            // The beam along the top, the track and the ground along the bottom.
            Shapes.Box(t, new Vector2(0f, BeamY), new Vector2(10.4f, 0.5f), Palette.Brown, Palette.BrownDark, BeamOrder, "Beam");
            Shapes.Box(t, new Vector2(0f, GroundY - 1.3f), new Vector2(10.4f, 2.6f), Palette.Brown, Palette.BrownDark, -5, "Ground");
            Shapes.Box(t, new Vector2(0f, GroundY + 0.15f), new Vector2(10.4f, 0.3f), Palette.Mist, Palette.Slate, -4, "Track");

            // The pulley on the beam, the rope, and the critter hanging from it.
            _pulley = Make("Pulley", new Vector2(0f, BeamY)).transform;
            Shapes.Make(_pulley, Shapes.Circle, Palette.Ink, Vector2.zero, Vector2.one * 0.56f, WheelOrder, "Ink");
            Shapes.Make(_pulley, Shapes.Circle, Palette.Slate, Vector2.zero, Vector2.one * (0.56f - Style.Outline * 2f), WheelOrder + 1, "Wheel");
            Shapes.Make(_pulley, Shapes.Circle, Palette.Ink, Vector2.zero, Vector2.one * 0.14f, WheelOrder + 2, "Axle");

            _rope = Make("Rope", Vector2.zero).transform;
            Shapes.Make(_rope, Shapes.Square, Palette.Ink, Vector2.zero, new Vector2(0.18f, 1f), RopeOrder, "Ink");
            Shapes.Make(_rope, Shapes.Square, Palette.Tan, Vector2.zero, new Vector2(0.18f - Style.Detail * 2f, 1f), RopeOrder + 1, "Rope");

            _critter = MakeSprite("Critter", GameSprite("critter"), new Vector2(0f, PailSim.HoldY + CritterAbove), CritterOrder).transform;

            _ballRenderer = MakeSprite("Ball", _ballSprite, new Vector2(0f, PailSim.HoldY), BallOrder);
            _ball = _ballRenderer.transform;

            _lives = new SpriteRenderer[PailSim.StartLives];
            for (int i = 0; i < _lives.Length; i++)
            {
                _lives[i] = MakeSprite("Life", _ballSprite, new Vector2(-3.75f + i * 0.45f, 6.72f), 40);
                _lives[i].transform.localScale = Vector3.one * 0.5f;
            }

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

            _lastHookX = _sim.HookX;
            SyncViews(0f);
        }

        protected override void OnBegin() => _starting = true;

        protected override void Play(float dt)
        {
            // The touch that starts the run sets the pail going; it doesn't drop the ball.
            bool tap = Input.Down && !_starting;
            _starting = false;

            int before = _sim.Score;
            var kind = _sim.Ball;
            float width = _sim.Width;
            var events = tap ? _sim.Tap() : PailEvent.None;
            var ballAt = new Vector2(_sim.BallX, _sim.BallY);
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);
            // A catch narrows the pail: rebuild it now, so the squash below lands on the new one.
            if (!Mathf.Approximately(width, _sim.Width)) SyncPail();

            if ((events & PailEvent.Dropped) != 0)
                Juice.Squash(_critter, 0.15f);

            if ((events & PailEvent.Catch) != 0)
            {
                bool perfect = (events & PailEvent.Perfect) != 0;
                var rim = new Vector2(_sim.BucketX + _sim.LastOffset, PailSim.RimY);
                Juice.Burst(transform, rim, Palette.Lime, perfect ? 14 : 8, perfect ? 5f : 3.5f);
                Juice.Squash(_pail, perfect ? 0.22f : 0.15f);
                if (perfect) ShowFlash(_sim.Streak >= 2 ? $"PERFECT x{_sim.Streak}" : "PERFECT");
                // The ball sinks into the pail, behind its front.
                var sunk = MakeLoose(kind, rim, Vector2.down * 2f, 0f, false);
                sunk.T.GetComponent<SpriteRenderer>().sortingOrder = PailOrder - 1;
                sunk.Sinking = true;
                sunk.OffsetX = _sim.LastOffset;
                sunk.Life = 0.3f;
            }
            if ((events & PailEvent.Gem) != 0)
            {
                Juice.Burst(transform, new Vector2(_sim.BucketX, PailSim.RimY + 0.3f), Palette.Teal, 16, 6f);
                Juice.ShakeSmall();
            }
            if ((events & PailEvent.Miss) != 0)
            {
                // Off the rim it bounces away; clear of it, it falls on to the ground.
                float off = _sim.LastOffset;
                var at = new Vector2(_sim.BucketX + off, PailSim.RimY);
                bool clipped = Mathf.Abs(off) < _sim.Width / 2f + PailSim.BallRadius;
                var v = clipped ? new Vector2(Mathf.Sign(off) * 3f, 3.5f) : new Vector2(0f, -12f);
                MakeLoose(kind, at, v, Mathf.Sign(off) * -360f, true);
                if (clipped)
                {
                    Juice.Burst(transform, at, Palette.Paper, 5, 2.5f);
                    Juice.Squash(_pail, 0.12f);
                }
                int lost = _sim.Lives;
                if (lost >= 0 && lost < _lives.Length) Juice.Burst(transform, _lives[lost].transform.localPosition, Palette.Red, 6, 2f);
            }
            if ((events & PailEvent.ThornGone) != 0)
            {
                // The critter tosses the thornball away, off the nearer side.
                float side = _sim.HookX >= 0f ? 1f : -1f;
                MakeLoose(BallKind.Thorn, ballAt, new Vector2(side * 5f, 4f), side * -400f, false);
                Juice.Squash(_critter, 0.2f);
            }
            if ((events & PailEvent.Thorn) != 0)
            {
                Juice.Burst(transform, ballAt, Palette.Orange, 18, 6f);
                Juice.Burst(transform, ballAt, Palette.Red, 10, 4f);
                Juice.Squash(_critter, 0.3f);
            }
            if ((events & PailEvent.Over) != 0)
            {
                Juice.ShakeBig();
                EndRun();
            }
        }

        void LateUpdate() => SyncViews(Time.deltaTime);

        void SyncViews(float dt)
        {
            SyncPail();
            _pail.localPosition = new Vector2(_sim.BucketX, PailSim.RimY);

            // The critter hangs under the pulley and leans into the way it drifts; holding a
            // thornball, it trembles.
            float hook = _sim.HookX;
            if (dt > 0f)
            {
                float lean = Mathf.Clamp((hook - _lastHookX) / dt * -6f, -16f, 16f);
                _tilt = Mathf.Lerp(_tilt, lean, 1f - Mathf.Exp(-8f * dt));
            }
            _lastHookX = hook;
            bool holdingThorn = _sim.Hold == HoldState.Holding && _sim.Ball == BallKind.Thorn && !_sim.Over;
            float shiver = holdingThorn ? Mathf.Sin(Time.time * 40f) * 4f : 0f;
            _pulley.localPosition = new Vector2(hook, BeamY);
            var critterAt = new Vector2(hook, PailSim.HoldY + CritterAbove);
            _critter.localPosition = critterAt;
            _critter.localRotation = Quaternion.Euler(0f, 0f, _tilt + shiver);
            float ropeTop = BeamY, ropeBottom = critterAt.y + 0.4f;
            _rope.localPosition = new Vector2(hook, (ropeTop + ropeBottom) / 2f);
            _rope.localScale = new Vector3(1f, ropeTop - ropeBottom, 1f);

            // The ball: held under the critter, falling, or gone while the next one comes.
            if (_shownBall != _sim.BallId)
            {
                bool first = _shownBall < 0;
                _shownBall = _sim.BallId;
                SetBallLook(_ballRenderer, _sim.Ball);
                if (!first) Juice.Pop(_ball);
            }
            bool showBall = _sim.Hold != HoldState.Reloading && !(_sim.Over && _sim.Ball != BallKind.Thorn);
            _ballRenderer.enabled = showBall;
            _ball.localPosition = new Vector2(_sim.BallX, _sim.BallY);
            if (_sim.Ball == BallKind.Gem) _ball.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 3f) * 12f);
            else if (holdingThorn) _ball.localRotation = Quaternion.Euler(0f, 0f, shiver * 2f);
            else _ball.localRotation = Quaternion.identity;

            for (int i = 0; i < _lives.Length; i++) _lives[i].enabled = i < _sim.Lives;

            UpdateLoose(dt);

            if (_flashFor > 0f)
            {
                _flashFor -= dt;
                _flash.transform.localPosition += new Vector3(0f, dt * 0.8f, 0f);
                if (_flashFor <= 0f) _flash.gameObject.SetActive(false);
            }
        }

        void SetBallLook(SpriteRenderer sr, BallKind kind)
        {
            switch (kind)
            {
                case BallKind.Gem: sr.sprite = _gemSprite; sr.transform.localScale = Vector3.one * GemScale; break;
                case BallKind.Thorn: sr.sprite = _thornSprite; sr.transform.localScale = Vector3.one * ThornScale; break;
                default: sr.sprite = _ballSprite; sr.transform.localScale = Vector3.one; break;
            }
        }

        /// <summary>The pail, rebuilt when it narrows: a body, a rim with the Lime perfect mark, and wheels.</summary>
        void SyncPail()
        {
            if (Mathf.Approximately(_shownWidth, _sim.Width)) return;
            _shownWidth = _sim.Width;
            if (_pail != null) Destroy(_pail.gameObject);
            _pail = Make("Pail", new Vector2(_sim.BucketX, PailSim.RimY)).transform;
            float w = _sim.Width;

            var body = Shapes.Box(_pail, new Vector2(0f, -0.2f - BodyHeight / 2f), new Vector2(w - 0.24f, BodyHeight), Palette.Green, Palette.GreenDark, PailOrder, "Body");
            // A darker band round the middle, like a hoop on a real pail.
            Shapes.Make(body, Shapes.Square, Palette.GreenDark, new Vector2(0f, 0.05f), new Vector2(w - 0.24f - Style.Outline * 2f, 0.12f), PailOrder + 3, "Hoop");
            Shapes.Box(_pail, new Vector2(0f, -0.04f), new Vector2(w, 0.34f), Palette.Green, Palette.GreenDark, PailOrder + 4, "Rim");
            Shapes.Make(_pail, Shapes.Square, Palette.Lime, new Vector2(0f, 0f), new Vector2(PailSim.PerfectHalf * 2f, 0.12f), PailOrder + 8, "Perfect");

            float wheelX = w / 2f - 0.45f, wheelY = -0.2f - BodyHeight - 0.08f;
            for (int i = -1; i <= 1; i += 2)
            {
                var at = new Vector2(i * wheelX, wheelY);
                Shapes.Make(_pail, Shapes.Circle, Palette.Ink, at, Vector2.one * 0.44f, PailOrder - 3, "Wheel Ink");
                Shapes.Make(_pail, Shapes.Circle, Palette.Mist, at, Vector2.one * (0.44f - Style.Outline * 2f), PailOrder - 2, "Wheel");
            }
        }

        Loose MakeLoose(BallKind kind, Vector2 at, Vector2 velocity, float spin, bool bursts)
        {
            var sr = MakeSprite("Loose", _ballSprite, at, LooseOrder);
            SetBallLook(sr, kind);
            var loose = new Loose { T = sr.transform, Velocity = velocity, Spin = spin, Life = 3f, Bursts = bursts };
            _loose.Add(loose);
            return loose;
        }

        /// <summary>Balls falling away, thornballs tossed off, and caught balls sinking into the pail.</summary>
        void UpdateLoose(float dt)
        {
            for (int i = _loose.Count - 1; i >= 0; i--)
            {
                var l = _loose[i];
                if (l.T == null) { _loose.RemoveAt(i); continue; }
                l.Life -= dt;
                if (l.Sinking)
                    l.T.localPosition = new Vector2(_sim.BucketX + l.OffsetX, l.T.localPosition.y + l.Velocity.y * dt);
                else
                {
                    l.Velocity += Vector2.down * 30f * dt;
                    l.T.localPosition += (Vector3)(l.Velocity * dt);
                }
                l.T.localRotation *= Quaternion.Euler(0f, 0f, l.Spin * dt);

                var p = (Vector2)l.T.localPosition;
                bool landed = l.Bursts && p.y < GroundY + PailSim.BallRadius;
                if (landed)
                {
                    var at = new Vector2(p.x, GroundY + 0.1f);
                    Juice.Burst(transform, at, Palette.Red, 12, 4.5f);
                    if (!_sim.Over) Juice.ShakeSmall();
                }
                if (landed || l.Life <= 0f || p.y < -9f || Mathf.Abs(p.x) > 6.5f)
                {
                    Destroy(l.T.gameObject);
                    _loose.RemoveAt(i);
                }
            }
        }

        void ShowFlash(string text)
        {
            _flash.text = text;
            _flash.transform.localPosition = new Vector2(0f, 0.6f);
            _flash.gameObject.SetActive(true);
            Juice.Pop(_flash.transform);
            _flashFor = 0.7f;
        }
    }
}
