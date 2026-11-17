using UnityEditor;

namespace AceBarcade.EditorTools
{
    /// <summary>
    /// Every PNG made by Art/build-sprites.sh lands in Assets/Resources/Sprites and is imported the
    /// same way: a single sprite at <see cref="Style.PixelsPerUnit"/>, no mipmaps, capped at 2048 px (a full-screen background is 1080 x 1920).
    /// Games never pick their own import settings.
    /// </summary>
    sealed class SpriteImportSettings : AssetPostprocessor
    {
        const string Folder = "Assets/Resources/Sprites/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;

            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = Style.PixelsPerUnit;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.filterMode = UnityEngine.FilterMode.Bilinear;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
