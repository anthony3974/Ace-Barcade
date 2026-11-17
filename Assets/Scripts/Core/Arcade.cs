using System;

namespace AceBarcade
{
    /// <summary>
    /// Where code finds the running pieces of the arcade, and how it moves between games.
    /// </summary>
    public static class Arcade
    {
        public static ArcadeInput Input { get; internal set; }
        public static ArcadeCamera Camera { get; internal set; }
        public static ArcadeRoot Root { get; internal set; }

        /// <summary>The game being played, or null in the hub.</summary>
        public static ArcadeGame Current { get; internal set; }

        /// <summary>Raised after a game is built (also on retry). The HUD listens to this.</summary>
        public static event Action<ArcadeGame> GameLaunched;

        /// <summary>Raised when the arcade returns to the hub.</summary>
        public static event Action HubShown;

        public static void Launch(GameInfo game) => Root.Launch(game);

        /// <summary>Throws the current game away and builds it again.</summary>
        public static void Retry()
        {
            if (Current != null) Launch(Current.Info);
        }

        public static void ShowHub() => Root.ShowHub();

        internal static void RaiseLaunched(ArcadeGame g) => GameLaunched?.Invoke(g);
        internal static void RaiseHubShown() => HubShown?.Invoke();
    }
}
