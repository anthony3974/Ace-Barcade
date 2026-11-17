using UnityEngine;

namespace AceBarcade
{
    /// <summary>
    /// Plain shapes made in code, white so they take a palette color exactly through
    /// SpriteRenderer.color. For backgrounds, particles and UI dimming: things the style draws
    /// without an outline. Anything the player plays with is outlined SVG art instead.
    /// </summary>
    public static class Shapes
    {
        static Sprite _circle, _square;

        /// <summary>A circle 1 unit across.</summary>
        public static Sprite Circle => _circle != null ? _circle : _circle = MakeCircle(128);

        /// <summary>A square 1 unit across.</summary>
        public static Sprite Square => _square != null ? _square : _square = MakeSquare();

        /// <summary>A shape as a new child of <paramref name="parent"/>, sized in units.</summary>
        public static SpriteRenderer Make(Transform parent, Sprite shape, Color color, Vector2 position, Vector2 size, int order = -10, string name = "Shape")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = shape;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>
        /// A solid box in the house style, for walls and platforms: an Ink outline
        /// (<see cref="Style.Outline"/>), the fill color, and a flat shade strip on the side away
        /// from the top-left light (bottom for wide boxes, right for tall ones).
        /// </summary>
        public static Transform Box(Transform parent, Vector2 center, Vector2 size, Color fill, Color shade, int order = 0, string name = "Box")
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = center;

            float o = Style.Outline;
            Make(root, Square, Palette.Ink, Vector2.zero, size, order, "Outline");
            var inner = size - new Vector2(o * 2f, o * 2f);
            Make(root, Square, fill, Vector2.zero, inner, order + 1, "Fill");

            if (size.x >= size.y)
            {
                float h = Mathf.Min(inner.y * 0.35f, 0.18f);
                Make(root, Square, shade, new Vector2(0f, -inner.y / 2f + h / 2f), new Vector2(inner.x, h), order + 2, "Shade");
            }
            else
            {
                float w = Mathf.Min(inner.x * 0.35f, 0.18f);
                Make(root, Square, shade, new Vector2(inner.x / 2f - w / 2f, 0f), new Vector2(w, inner.y), order + 2, "Shade");
            }
            return root;
        }

        static Sprite MakeCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Circle" };
            var px = new Color32[size * size];
            float r = size / 2f, c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                byte a = (byte)(Mathf.Clamp01(r - d - 0.5f) * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        }

        static Sprite MakeSquare()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "Square" };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4, 0, SpriteMeshType.FullRect);
        }
    }
}
