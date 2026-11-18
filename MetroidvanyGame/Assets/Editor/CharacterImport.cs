using UnityEditor;

namespace Fortress.EditorTools
{
    /// <summary>Импорт людей Quaternius (Assets/Characters): гуманоидный риг (Humanoid, свой аватар),
    /// анимации из библиотек UAL и Mixamo (Assets/Characters/Mixamo/Anim, без кожи, FBX), текстуры нормалей/ORM/шероховатости — линейные.</summary>
    public class CharacterImport : AssetPostprocessor
    {
        const string Root = "Assets/Characters/";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var mi = (ModelImporter)assetImporter;
            bool anims = assetPath.Contains("/Anim/");
            mi.importCameras = false;
            mi.importLights = false;
            mi.globalScale = 1f;
            // причёски и отдельные части нарядов — неполный скелет: без аватара (кости привяжем к телу по именам)
            // герой (Characters/Hero) — калека, поза процедурная (HeroBody): гуманоидный аватар ему не нужен и не собирается
            bool partial = assetPath.Contains("/Hair/") || assetPath.Contains("/Parts/") || assetPath.Contains("/Hero/");
            mi.animationType = partial ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human;
            if (!partial) mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = anims;
            mi.isReadable = assetPath.Contains("/Base/");   // из базового тела вырезаем голову для нарядов
            mi.importBlendShapes = !anims;
            mi.materialImportMode = anims ? ModelImporterMaterialImportMode.None : ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
            mi.materialName = ModelImporterMaterialName.BasedOnTextureName;
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Root) || !assetPath.Contains("/Anim/")) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            if (assetPath.Contains("/Mixamo/"))
            {
                // Mixamo: в каждом файле один клип «mixamo.com» — имя берём из файла; ходьба идёт корнем (root motion),
                // повороты отдают и поворот корня; высота корня — в позу (вверх-вниз ведёт капсула)
                string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                string f = file.ToLowerInvariant();
                bool turn = f.Contains("turn");
                bool once = f.Contains("start") || f.Contains("stop") || turn || f.Contains("react") || f.Contains("hit")
                            || f.Contains("stumble") || f.Contains("stagger") || f.Contains("fall") || f.Contains("getting up") || f.Contains("stand up")
                            || f == "sit_down" || f == "sit_up";
                foreach (var c in clips)
                {
                    c.name = file;
                    c.loopTime = !once;
                    c.lockRootRotation = !turn;
                    c.lockRootHeightY = true;
                    c.lockRootPositionXZ = false;
                    // ориентация — по телу: в части клипов Mixamo тело развёрнуто к пути (удары ~56°, шаг назад −43°, вбок ±54°) —
                    // так в первом кадре тело смотрит вперёд (без рывка), а путь клипа идёт косо, как в исходнике.
                    // Смесь ходьбы берёт точки по реальной скорости клипа, путь удара NPC доворачивает по толчку (HumanNPC)
                    c.keepOriginalOrientation = false;
                    c.rotationOffset = 0f;
                    c.keepOriginalPositionY = true;
                    c.keepOriginalPositionXZ = false;
                }
                mi.clipAnimations = clips;
                return;
            }
            foreach (var c in clips)
            {
                string n = c.name.ToLowerInvariant();
                c.loopTime = n.Contains("loop") || n.EndsWith("sword_idle");   // в UAL зацикленные помечены _Loop (LayToIdle — нет)
                c.lockRootRotation = true;
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = false;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
            }
            mi.clipAnimations = clips;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.Contains("_Normal")) ti.textureType = TextureImporterType.NormalMap;
            if (assetPath.Contains("_ORM") || assetPath.Contains("_Normal") || assetPath.Contains("_Roughness")) ti.sRGBTexture = false;
            ti.maxTextureSize = assetPath.Contains("/Hero/") ? 4096 : 2048;   // главный герой — крупно в кадре
        }
    }
}
