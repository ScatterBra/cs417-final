using UnityEditor;
using UnityEngine;

/// <summary>
/// Everything under Resources/Icons must import as a UI sprite, or
/// Resources.Load&lt;Sprite&gt; silently returns null and buttons render as
/// blank white squares. Runs on every (re)import of files in that folder.
/// </summary>
public class IconImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').Contains("Resources/Icons/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 100f;
        ti.mipmapEnabled = false;          // screen-space UI: mips only blur
        ti.alphaIsTransparency = true;     // no dark fringe on the edges
        ti.isReadable = false;
        ti.filterMode = FilterMode.Bilinear;
        ti.maxTextureSize = 128;

        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect; // Tight fights Image
        settings.spriteGenerateFallbackPhysicsShape = false;
        ti.SetTextureSettings(settings);
    }
}
