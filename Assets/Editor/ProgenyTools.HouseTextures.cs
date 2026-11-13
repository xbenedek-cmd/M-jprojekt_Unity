using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Modely domov majú textúry vložené priamo vo FBX (často 4K+), preto sa nedajú zmenšiť v importe. Táto úloha ich vytiahne do
/// Assets/Models/House/Textures (FBX sa na ne automaticky prepojí) a nastaví maximálnu veľkosť a kompresiu.
/// </summary>
public static partial class ProgenyTools
{
    const string HouseFolder = "Assets/Models/House";
    const int HouseTextureMax = 1024;   // 2048, ak by boli domy zblízka rozmazané

    [MenuItem("Progeny/Materiály a zvuk/Domy: zmenšiť textúry (1024)", false, 9100)]
    static void ShrinkHouseTextures()
    {
        string texDir = HouseFolder + "/Textures";
        if (!AssetDatabase.IsValidFolder(texDir)) AssetDatabase.CreateFolder(HouseFolder, "Textures");

        int extracted = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { HouseFolder }))
        {
            string modelPath = AssetDatabase.GUIDToAssetPath(guid);
            string modelName = Path.GetFileNameWithoutExtension(modelPath);

            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(modelPath))
            {
                if (!(sub is Texture2D tex) || !AssetDatabase.IsSubAsset(tex)) continue;

                string safe = (modelName + "_" + tex.name).Replace(' ', '_');
                foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                string error = AssetDatabase.ExtractAsset(tex, $"{texDir}/{safe}.png");
                if (string.IsNullOrEmpty(error)) extracted++;
                else Debug.LogWarning($"Domy: textúru {tex.name} z {modelName} sa nepodarilo vytiahnuť: {error}");
            }
            AssetDatabase.WriteImportSettingsIfDirty(modelPath);
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
        }

        int capped = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;

            importer.maxTextureSize = HouseTextureMax;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = true;
            importer.compressionQuality = 50;
            importer.streamingMipmaps = true;
            importer.SaveAndReimport();
            capped++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Domy: vytiahnutých textúr {extracted}, zmenšených na max {HouseTextureMax} px: {capped}.");
    }
}
