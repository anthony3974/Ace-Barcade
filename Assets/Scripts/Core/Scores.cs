using UnityEngine;

namespace AceBarcade
{
    /// <summary>Best scores, saved on the phone, keyed by game id.</summary>
    public static class Scores
    {
        public static string Key(string gameId) => $"best.{gameId}";

        public static int Best(string gameId) => PlayerPrefs.GetInt(Key(gameId), 0);

        /// <summary>Saves the score if it beats the best. Returns true when it did.</summary>
        public static bool Submit(string gameId, int score)
        {
            if (score <= Best(gameId)) return false;
            PlayerPrefs.SetInt(Key(gameId), score);
            PlayerPrefs.Save();
            return true;
        }

        public static void Clear(string gameId) => PlayerPrefs.DeleteKey(Key(gameId));
    }
}
