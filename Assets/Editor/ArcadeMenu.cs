using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AceBarcade.EditorTools
{
    /// <summary>Tools > Ace Barcade: build sprites, check the art style, and play a game.</summary>
    static class ArcadeMenu
    {
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [MenuItem("Tools/Ace Barcade/Build Sprites", priority = 20)]
        static void BuildSprites()
        {
            if (Run("Art/check-style.sh") && Run("Art/build-sprites.sh")) AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Ace Barcade/Check Art Style", priority = 21)]
        static void CheckStyle() => Run("Art/check-style.sh");

        [MenuItem("Tools/Ace Barcade/Play Game...", priority = 0)]
        static void PlayGame() => EditorWindow.GetWindow<PlayGameWindow>(true, "Play Game");

        /// <summary>Opens the arcade scene and starts it in the given game.</summary>
        public static void Play(GameInfo game)
        {
            if (EditorApplication.isPlaying) { Arcade.Launch(game); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ProjectSetup.ScenePath);
            SessionState.SetString(ArcadeRoot.StartGameKey, game.Id);
            EditorApplication.EnterPlaymode();
        }

        static bool Run(string script)
        {
            var psi = new ProcessStartInfo("bash", script)
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0) Debug.Log($"[Ace Barcade] {script}\n{output}");
            else Debug.LogError($"[Ace Barcade] {script} failed\n{output}");
            return p.ExitCode == 0;
        }
    }

    sealed class PlayGameWindow : EditorWindow
    {
        Vector2 _scroll;

        void OnGUI()
        {
            var games = GameRegistry.All;
            if (games.Count == 0)
            {
                EditorGUILayout.HelpBox("No games yet. Add a class under Assets/Games with [ArcadeGame(...)].", MessageType.Info);
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var g in games)
                if (GUILayout.Button($"{g.Number:000}  {g.Title}", GUILayout.Height(24)))
                {
                    ArcadeMenu.Play(g);
                    Close();
                }
            EditorGUILayout.EndScrollView();
        }
    }
}
