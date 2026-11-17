using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace AceBarcade
{
    /// <summary>
    /// The one object in Assets/Scenes/Arcade.unity. Creates the camera and input, then shows the
    /// hub or launches a game. Everything else is built in code.
    /// </summary>
    public sealed class ArcadeRoot : MonoBehaviour
    {
        public Hud Hud { get; private set; }
        public Hub Hub { get; private set; }

#if UNITY_EDITOR
        /// <summary>Set by the editor's Play Game tool: the id of the game to start in.</summary>
        public const string StartGameKey = "AceBarcade.StartGame";
#endif

        void Awake()
        {
            Arcade.Root = this;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Time.timeScale = 1f;

            var cam = new GameObject("Camera").AddComponent<ArcadeCamera>();
            cam.transform.SetParent(transform, false);
            Arcade.Camera = cam;

            var input = new GameObject("Input").AddComponent<ArcadeInput>();
            input.transform.SetParent(transform, false);
            Arcade.Input = input;

            var events = new GameObject("EventSystem", typeof(EventSystem));
            events.transform.SetParent(transform, false);
            events.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            // A finger wobbles more than a mouse: about 2 mm of movement before a press becomes a scroll.
            events.GetComponent<EventSystem>().pixelDragThreshold = Mathf.Max(10, Mathf.RoundToInt(Screen.dpi * 0.08f));

            Hub = Hub.Create(transform, cam.Cam);
            Hud = Hud.Create(transform, cam.Cam);

            foreach (var problem in GameRegistry.Validate(GameRegistry.All))
                Debug.LogError($"[Arcade] {problem}");
        }

        void Start()
        {
            GameInfo start = null;
#if UNITY_EDITOR
            var id = UnityEditor.SessionState.GetString(StartGameKey, "");
            UnityEditor.SessionState.EraseString(StartGameKey);
            if (id != "") start = GameRegistry.Find(id);
#endif
            if (start != null) Launch(start);
            else ShowHub();
        }

        public void Launch(GameInfo game)
        {
            bool sameGame = Arcade.Current != null && Arcade.Current.Info == game;
            ClearGame(unloadSprites: !sameGame);
            var go = new GameObject($"{game.Folder}");
            var instance = (ArcadeGame)go.AddComponent(game.Type);
            Arcade.Current = instance;
            instance.Init(game);
            Arcade.RaiseLaunched(instance);
        }

        public void ShowHub()
        {
            ClearGame(unloadSprites: true);
            Arcade.RaiseHubShown();
        }

        void ClearGame(bool unloadSprites)
        {
            Time.timeScale = 1f;
            Arcade.Camera.ResetView();
            if (Arcade.Current == null) return;
            var old = Arcade.Current.gameObject;
            Arcade.Current = null;
            Destroy(old);
            if (unloadSprites) Sprites.Unload();
        }
    }
}
