using UnityEngine;

namespace AceBarcade.Games.PopUp
{
    /// <summary>
    /// Game 009. Critters pop out of nine hollow stumps. Tap the green sprouts (1) and the quick
    /// teal ones (5); a sprout that gets away costs one of three lives, and tapping a red spiky
    /// one ends the run. The rules live in <see cref="PopSim"/>; this class draws them and adds
    /// the feel.
    /// </summary>
    [ArcadeGame(9, "pop-up", "Pop Up", Controls.Tap, Blurb = "Tap the green ones, never the red")]
    public sealed class PopUp : ArcadeGame
    {
        /// <summary>For tests: the seed for the next run (otherwise the clock).</summary>
        public static int? SeedForTests;

        // Sprite geometry, from the SVGs: the stump front is 264 px tall with its lip 44 px down;
        // a critter is 180 px tall and shows down to 150 px when fully up.
        const float FrontOffset = (132f - 44f) / Style.PixelsPerUnit;
        const float CritterUp = (150f - 90f) / Style.PixelsPerUnit;
        const float CritterDown = -1f;

        PopSim _sim;
        Sprite _sprout, _gold, _spiky;
        readonly SpriteRenderer[] _critters = new SpriteRenderer[PopSim.Holes];
        readonly HoleState[] _shown = new HoleState[PopSim.Holes];
        SpriteRenderer[] _lives;

        public PopSim Sim => _sim;

        protected override void Build()
        {
            _sim = new PopSim(SeedForTests ?? System.Environment.TickCount);
            _sprout = GameSprite("sprout");
            _gold = GameSprite("gold");
            _spiky = GameSprite("spiky");
            var t = transform;

            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-3.4f, 5.2f), new Vector2(3f, 3f), -20, "Decor");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(4.2f, -0.8f), new Vector2(3.6f, 3.6f), -20, "Decor");
            Shapes.Make(t, Shapes.Circle, Palette.Slate, new Vector2(-4f, -6.4f), new Vector2(4.4f, 4.4f), -20, "Decor");

            var back = GameSprite("hole_back");
            var front = GameSprite("hole_front");
            for (int i = 0; i < PopSim.Holes; i++)
            {
                // Lower rows draw over higher ones; each critter sits between its hole and stump.
                int order = i / PopSim.Cols * 10;
                var lip = PopSim.HoleCenter(i);
                MakeSprite("Hole", back, lip, order);
                _critters[i] = MakeSprite("Critter", _sprout, lip, order + 1);
                _critters[i].gameObject.SetActive(false);
                MakeSprite("Stump", front, lip - new Vector2(0f, FrontOffset), order + 2);
            }

            // The lives left, as small sprouts under the best score.
            _lives = new SpriteRenderer[PopSim.StartLives];
            for (int i = 0; i < _lives.Length; i++)
            {
                _lives[i] = MakeSprite("Life", _sprout, new Vector2(-3.75f + i * 0.46f, 6.72f), 40);
                _lives[i].transform.localScale = Vector3.one * 0.26f;
            }

            SyncViews();
        }

        protected override void Play(float dt)
        {
            int before = _sim.Score;
            var events = PopEvent.None;
            if (Input.Down) events |= _sim.Tap(PopSim.HoleAt(Input.Position));
            events |= _sim.Step(dt);
            AddScore(_sim.Score - before);

            if ((events & (PopEvent.Hit | PopEvent.Gold)) != 0)
            {
                var critter = _critters[_sim.LastHole].transform;
                bool gold = (events & PopEvent.Gold) != 0;
                Juice.Burst(transform, critter.localPosition, Palette.Lime, gold ? 16 : 10, gold ? 6f : 4.5f);
                Juice.Squash(critter, 0.3f);
                if (gold) Juice.ShakeSmall();
            }
            if ((events & PopEvent.Escaped) != 0)
            {
                Juice.Burst(transform, PopSim.HoleCenter(_sim.LastHole), Palette.Mist, 6, 3f);
                int lost = _sim.Lives;
                if (lost >= 0 && lost < _lives.Length) Juice.Burst(transform, _lives[lost].transform.localPosition, Palette.Red, 6, 2f);
                Juice.ShakeSmall();
            }
            if ((events & PopEvent.Spiky) != 0)
            {
                var critter = _critters[_sim.LastHole].transform;
                Juice.Burst(transform, critter.localPosition, Palette.Orange, 18, 6f);
                Juice.Squash(critter, 0.35f);
                Juice.ShakeBig();
            }
            if ((events & (PopEvent.Spiky | PopEvent.OutOfLives)) != 0)
            {
                if ((events & PopEvent.OutOfLives) != 0) Juice.ShakeBig();
                EndRun();
            }
        }

        void LateUpdate() => SyncViews();

        void SyncViews()
        {
            var field = _sim.Field;
            for (int i = 0; i < PopSim.Holes; i++)
            {
                var hole = field[i];
                var critter = _critters[i];
                bool up = hole.State != HoleState.Empty;
                if (critter.gameObject.activeSelf != up) critter.gameObject.SetActive(up);
                if (!up) { _shown[i] = HoleState.Empty; continue; }

                if (_shown[i] == HoleState.Empty)
                    critter.sprite = hole.Kind == Critter.Gold ? _gold : hole.Kind == Critter.Spiky ? _spiky : _sprout;
                _shown[i] = hole.State;

                // Rising overshoots a little, like a pop; sinking just drops.
                float h = hole.State == HoleState.Rising ? Juice.BackOut(hole.Height) : hole.Height;
                var lip = PopSim.HoleCenter(i);
                critter.transform.localPosition = lip + new Vector2(0f, Mathf.LerpUnclamped(CritterDown, CritterUp, h));
            }

            for (int i = 0; i < _lives.Length; i++) _lives[i].enabled = i < _sim.Lives;
        }
    }
}
