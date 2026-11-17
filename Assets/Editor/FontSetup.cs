using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace AceBarcade.EditorTools
{
    /// <summary>
    /// Makes the arcade's one font: Lilita One (Assets/Fonts, SIL Open Font License) as a static
    /// TextMeshPro font asset in Resources/Fonts, with the house Ink outline on its material,
    /// so every piece of text in every game is outlined the same way.
    /// Run by ProjectSetup.Apply; delete the asset and run it again to rebuild.
    /// </summary>
    public static class FontSetup
    {
        public const string TtfPath = "Assets/Fonts/LilitaOne-Regular.ttf";
        public const string AssetPath = "Assets/Resources/Fonts/LilitaOne SDF.asset";

        /// <summary>Outline thickness in TMP's units (0-1 of the SDF range).</summary>
        const float OutlineWidth = 0.24f;

        public static void Ensure()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath) != null) return;

            var font = AssetDatabase.LoadAssetAtPath<Font>(TtfPath);
            if (font == null) { Debug.LogError($"[Ace Barcade] No font at {TtfPath}"); return; }

            var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            asset.name = "LilitaOne SDF";

            // Printable ASCII plus a few symbols the arcade uses.
            var chars = new string(Enumerable.Range(32, 95).Select(i => (char)i).ToArray()) + "×•…’“”";
            asset.TryAddCharacters(chars, out string missing);
            if (!string.IsNullOrEmpty(missing)) Debug.Log($"[Ace Barcade] Font has no glyphs for: {missing}");
            asset.atlasPopulationMode = AtlasPopulationMode.Static;

            var mat = asset.material;
            mat.name = "LilitaOne SDF Material";
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, Palette.Ink);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, OutlineWidth);
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, OutlineWidth * 0.5f);

            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Fonts")) AssetDatabase.CreateFolder("Assets/Resources", "Fonts");

            AssetDatabase.CreateAsset(asset, AssetPath);
            var atlas = asset.atlasTextures[0];
            atlas.name = "LilitaOne SDF Atlas";
            AssetDatabase.AddObjectToAsset(atlas, asset);
            AssetDatabase.AddObjectToAsset(mat, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath);
            Debug.Log("[Ace Barcade] Made the font asset.");
        }
    }
}
