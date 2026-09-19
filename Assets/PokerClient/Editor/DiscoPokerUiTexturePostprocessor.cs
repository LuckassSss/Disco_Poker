using System;
using UnityEditor;
using UnityEngine;

namespace DiscoPoker.UI.Editor
{
    /// <summary>
    /// Keeps the prepared Disco Poker PNG files ready for use by uGUI Images and UI animation.
    /// The rule is deliberately limited to this asset pack.
    /// </summary>
    internal sealed class DiscoPokerUiTexturePostprocessor : AssetPostprocessor
    {
        private static readonly string[] PreparedTextureRoots =
        {
            "Assets/PokerClient/Art/UI/"
        };

        private void OnPreprocessTexture()
        {
            if (!IsPreparedTexture(assetPath))
            {
                return;
            }

            if (assetImporter is not TextureImporter importer)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.streamingMipmaps = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            // кадры шара 1254² ×10 — на экране они 130 px, полный размер только жрёт память
            importer.maxTextureSize = assetPath.Contains("/Animations/") ? 512 : 4096;

            var spriteSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(spriteSettings);
            spriteSettings.spriteAlignment = (int)SpriteAlignment.Center;
            spriteSettings.spritePivot = new Vector2(0.5f, 0.5f);
            spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
            spriteSettings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(spriteSettings);
        }

        private static bool IsPreparedTexture(string path)
        {
            foreach (var root in PreparedTextureRoots)
            {
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        [MenuItem("Tools/Disco Poker/Reimport Programmer Ready UI")]
        private static void ReimportPreparedUi()
        {
            var textureGuids = AssetDatabase.FindAssets("t:Texture2D", Array.ConvertAll(PreparedTextureRoots, r => r.TrimEnd('/')));

            foreach (var guid in textureGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }

            Debug.Log($"Disco Poker UI: reimported {textureGuids.Length} prepared texture(s).");
        }
    }
}
