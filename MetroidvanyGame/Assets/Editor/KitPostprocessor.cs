using UnityEditor;

namespace Fortress.EditorTools
{
    /// <summary>Настройки импорта для китов Quaternius (Assets/Kits): без камер/света/анимации, нормали как NormalMap.</summary>
    public class KitPostprocessor : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/Kits/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importAnimation = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importBlendShapes = false;
            mi.isReadable = false;
            mi.globalScale = 1f;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Kits/")) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.Contains("_Normal")) ti.textureType = TextureImporterType.NormalMap;
            if (assetPath.Contains("_ORM") || assetPath.Contains("_Normal")) ti.sRGBTexture = false;
            ti.maxTextureSize = 2048;
        }
    }
}
