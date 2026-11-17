using UnityEngine;

namespace AceBarcade
{
    /// <summary>
    /// The one palette every game draws from. Mirrors Art/palette.txt (PaletteTests checks they match).
    /// Never write a raw color in game code: pick one of these.
    /// </summary>
    public static class Palette
    {
        public static readonly Color Ink = Hex("#2A2238");
        public static readonly Color Night = Hex("#3B3355");
        public static readonly Color Slate = Hex("#5C5A78");
        public static readonly Color Mist = Hex("#A7A9C4");
        public static readonly Color Cloud = Hex("#E4E6F0");
        public static readonly Color Paper = Hex("#FFF8EC");
        public static readonly Color Red = Hex("#E5484D");
        public static readonly Color RedDark = Hex("#B5303A");
        public static readonly Color Orange = Hex("#F28C38");
        public static readonly Color OrangeDark = Hex("#C4661F");
        public static readonly Color Yellow = Hex("#FFD23F");
        public static readonly Color YellowDark = Hex("#D9A21B");
        public static readonly Color Lime = Hex("#9BD84E");
        public static readonly Color Green = Hex("#3FAE5A");
        public static readonly Color GreenDark = Hex("#2A7F45");
        public static readonly Color Teal = Hex("#2EC4B6");
        public static readonly Color TealDark = Hex("#1E8F86");
        public static readonly Color Sky = Hex("#5BC0EB");
        public static readonly Color Blue = Hex("#3A7BD5");
        public static readonly Color BlueDark = Hex("#2A559C");
        public static readonly Color Purple = Hex("#8E5BD6");
        public static readonly Color PurpleDark = Hex("#6339A6");
        public static readonly Color Pink = Hex("#F37EB5");
        public static readonly Color PinkDark = Hex("#C9558D");
        public static readonly Color Tan = Hex("#F2C69B");
        public static readonly Color Brown = Hex("#A0663F");
        public static readonly Color BrownDark = Hex("#6E4426");

        /// <summary>Every palette color by name, in file order.</summary>
        public static readonly (string Name, Color Color)[] All =
        {
            ("Ink", Ink),
            ("Night", Night),
            ("Slate", Slate),
            ("Mist", Mist),
            ("Cloud", Cloud),
            ("Paper", Paper),
            ("Red", Red),
            ("RedDark", RedDark),
            ("Orange", Orange),
            ("OrangeDark", OrangeDark),
            ("Yellow", Yellow),
            ("YellowDark", YellowDark),
            ("Lime", Lime),
            ("Green", Green),
            ("GreenDark", GreenDark),
            ("Teal", Teal),
            ("TealDark", TealDark),
            ("Sky", Sky),
            ("Blue", Blue),
            ("BlueDark", BlueDark),
            ("Purple", Purple),
            ("PurpleDark", PurpleDark),
            ("Pink", Pink),
            ("PinkDark", PinkDark),
            ("Tan", Tan),
            ("Brown", Brown),
            ("BrownDark", BrownDark),
        };

        /// <summary>A palette color at a lower alpha (for shadows and fades only).</summary>
        public static Color WithAlpha(this Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
