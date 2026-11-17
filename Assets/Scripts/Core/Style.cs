namespace AceBarcade
{
    /// <summary>
    /// The house style in numbers. docs/STYLE.md explains each rule; Art/check-style.sh enforces
    /// the art side. Games read these instead of choosing their own sizes.
    /// </summary>
    public static class Style
    {
        // --- Screen ---------------------------------------------------------------------------

        /// <summary>The play area is always this many units wide, on every phone (portrait).</summary>
        public const float WorldWidth = 9f;

        /// <summary>The play area height games design for (9:16). Taller phones show extra background.</summary>
        public const float WorldHeight = 16f;

        // --- Art scale ------------------------------------------------------------------------

        /// <summary>SVGs are authored at this many px per unit; a full-screen mock is 1080 x 1920.</summary>
        public const int PixelsPerUnit = 120;

        /// <summary>Main outline, in SVG px (Ink, round joins). 8 px = 0.067 units.</summary>
        public const float OutlinePx = 8f;

        /// <summary>Inner detail lines, in SVG px.</summary>
        public const float DetailPx = 5f;

        public const float Outline = OutlinePx / PixelsPerUnit;
        public const float Detail = DetailPx / PixelsPerUnit;

        /// <summary>Nothing smaller than this (in SVG px) is drawn: keeps the look chunky.</summary>
        public const float SmallestFeaturePx = 16f;

        // --- Motion ---------------------------------------------------------------------------

        /// <summary>Squash and stretch on landings, hits and pops.</summary>
        public const float Squash = 0.18f;

        /// <summary>Screen shake strength in units, for small and big hits.</summary>
        public const float ShakeSmall = 0.08f;
        public const float ShakeBig = 0.22f;

        /// <summary>How long UI pops and object spawns take, in seconds.</summary>
        public const float PopTime = 0.18f;
    }
}
