using System;

namespace AceBarcade
{
    /// <summary>How a game is played with one thumb. Shown on its hub card.</summary>
    public enum Controls
    {
        Tap,
        Hold,
        Drag,
        Swipe,
    }

    /// <summary>
    /// Marks an <see cref="ArcadeGame"/> so the arcade finds it. Adding a game is just adding a
    /// class with this attribute: there is no central list to edit.
    /// <code>[ArcadeGame(1, "paddle-rally", "Paddle Rally", Controls.Drag)]</code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ArcadeGameAttribute : Attribute
    {
        /// <summary>The game's number, 1-999. Sets its order in the hub and its folder name (G001_...).</summary>
        public int Number { get; }

        /// <summary>Stable id, lower-case words with dashes. Saves are keyed on it, so never change it.</summary>
        public string Id { get; }

        public string Title { get; }
        public Controls Controls { get; }

        /// <summary>One short line for the hub card.</summary>
        public string Blurb { get; set; } = "";

        public ArcadeGameAttribute(int number, string id, string title, Controls controls)
        {
            Number = number;
            Id = id;
            Title = title;
            Controls = controls;
        }
    }
}
