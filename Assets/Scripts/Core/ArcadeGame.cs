using UnityEngine;

namespace AceBarcade
{
    /// <summary>
    /// Base class for every game. A game builds its whole world in <see cref="Build"/>, under its own
    /// transform, and runs it in <see cref="Play"/>. The arcade owns everything else the same way for
    /// all games: the Ready / Playing / Paused / Over flow, pausing, best scores, and retry
    /// (which throws the game away and builds it fresh, so a game never resets itself).
    /// </summary>
    public abstract class ArcadeGame : MonoBehaviour
    {
        public enum Phase { Ready, Playing, Paused, Over }

        public GameInfo Info { get; private set; }
        public Phase State { get; private set; } = Phase.Ready;
        public int Score { get; private set; }

        /// <summary>True once the run is over, if it beat the saved best.</summary>
        public bool NewBest { get; private set; }

        /// <summary>Seconds of play in this run (stops while paused).</summary>
        public float RunTime { get; private set; }

        protected ArcadeInput Input => Arcade.Input;
        protected ArcadeCamera Cam => Arcade.Camera;

        /// <summary>Create the world: every object goes under this transform. Called once.</summary>
        protected abstract void Build();

        /// <summary>One frame of play. Only called while <see cref="State"/> is Playing.</summary>
        protected abstract void Play(float dt);

        /// <summary>Called on the first touch, as play begins. That touch also reaches Play.</summary>
        protected virtual void OnBegin() { }

        /// <summary>Called once when the run ends, after the best score is saved.</summary>
        protected virtual void OnEnd() { }

        internal void Init(GameInfo info)
        {
            Info = info;
            Build();
        }

        // --- Called by games ------------------------------------------------------------------

        protected void AddScore(int points = 1)
        {
            if (State != Phase.Playing) return;
            Score += points;
        }

        /// <summary>The run is over (the player lost, or finished). Saves the best score.</summary>
        protected void EndRun()
        {
            if (State != Phase.Playing) return;
            State = Phase.Over;
            NewBest = Scores.Submit(Info.Id, Score);
            OnEnd();
        }

        /// <summary>A sprite from this game's art folder (Art/Games/G001_Name/paddle.svg is "paddle").</summary>
        protected Sprite GameSprite(string name) => Sprites.Get($"Games/{Info.Folder}/{name}");

        /// <summary>A new child object of the game, so it is cleaned up with the game.</summary>
        protected GameObject Make(string name, Vector2 position = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            return go;
        }

        /// <summary>A new child object showing a sprite.</summary>
        protected SpriteRenderer MakeSprite(string name, Sprite sprite, Vector2 position = default, int order = 0)
        {
            var sr = Make(name, position).AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        // --- Flow -----------------------------------------------------------------------------

        public void Pause()
        {
            if (State != Phase.Playing) return;
            State = Phase.Paused;
            Time.timeScale = 0f;
        }

        public void Resume()
        {
            if (State != Phase.Paused) return;
            State = Phase.Playing;
            Time.timeScale = 1f;
        }

        void Update()
        {
            var input = Input;
            if (input == null) return;

            switch (State)
            {
                case Phase.Ready:
                    if (input.Back) { Arcade.ShowHub(); return; }
                    if (input.Down)
                    {
                        State = Phase.Playing;
                        OnBegin();
                        Play(Time.deltaTime);
                    }
                    break;

                case Phase.Playing:
                    if (input.Back) { Pause(); return; }
                    RunTime += Time.deltaTime;
                    Play(Time.deltaTime);
                    break;

                // The HUD's buttons handle Resume and Retry; Back leaves for the arcade.
                case Phase.Paused:
                case Phase.Over:
                    if (input.Back) Arcade.ShowHub();
                    break;
            }
        }

        void OnDestroy()
        {
            if (State == Phase.Paused) Time.timeScale = 1f;
        }
    }
}
