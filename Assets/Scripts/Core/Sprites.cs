using System.Collections.Generic;
using UnityEngine;

namespace AceBarcade
{
    /// <summary>
    /// Loads sprites made by Art/build-sprites.sh. Art/Games/G001_Name/ball.svg becomes
    /// Assets/Resources/Sprites/Games/G001_Name/ball.png and is loaded as "Games/G001_Name/ball".
    /// Only the game being played has its sprites in memory.
    /// </summary>
    public static class Sprites
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string path)
        {
            if (Cache.TryGetValue(path, out var s) && s != null) return s;
            s = Resources.Load<Sprite>("Sprites/" + path);
            if (s == null) Debug.LogError($"[Sprites] No sprite at Art/{path}.svg (did Art/build-sprites.sh run?)");
            Cache[path] = s;
            return s;
        }

        /// <summary>Forget loaded sprites and let Unity free the unused ones (on leaving a game).</summary>
        public static void Unload()
        {
            Cache.Clear();
            Resources.UnloadUnusedAssets();
        }
    }
}
