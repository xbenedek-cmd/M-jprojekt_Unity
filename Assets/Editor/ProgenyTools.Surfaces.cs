using UnityEditor;
using UnityEngine;

/// <summary>
/// Povrchové materiály (PBR balíky z Assets/Materials/Povrchy/&lt;názov&gt;/: BaseColor + Normal): zo každého priečinka vznikne
/// Standard materiál Assets/Materials/Povrchy/&lt;názov&gt;.mat. Používajú ich démoni Blesku (corrugated_metal) a cesty parciel (old_road).
/// </summary>
public static partial class ProgenyTools
{
    const string SurfacesFolder = "Assets/Materials/Povrchy";

    [MenuItem("Progeny/Materiály a zvuk/Povrchy z balíkov", false, 801)]
    static void BuildSurfaceMaterialsMenu()
    {
        int n = EnsureSurfaceMaterials(true);
        Debug.Log($"Povrchové materiály: {n} (Assets/Materials/Povrchy).");
    }

    static int EnsureSurfaceMaterials(bool force)
    {
        if (!AssetDatabase.IsValidFolder(SurfacesFolder)) return 0;

        int count = 0;
        foreach (string folder in AssetDatabase.GetSubFolders(SurfacesFolder))
        {
            string key = System.IO.Path.GetFileName(folder);
            string matPath = $"{SurfacesFolder}/{key}.mat";
            if (!force && AssetDatabase.LoadAssetAtPath<Material>(matPath) != null) { count++; continue; }

            Texture2D albedo = null, normal = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (albedo == null && NameHas(file, "basecolor", "albedo", "diffuse")) albedo = ConfigureTexture(path, false, true);
                else if (normal == null && NameHas(file, "normal")) normal = ConfigureTexture(path, true, false);
            }
            if (albedo == null) continue;

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            bool isNew = mat == null;
            if (isNew) mat = new Material(Shader.Find("Standard"));

            mat.SetTexture("_MainTex", albedo);
            mat.color = Color.white;
            mat.SetFloat("_Metallic", key.Contains("metal") ? 0.55f : 0f);
            mat.SetFloat("_Glossiness", key.Contains("metal") ? 0.5f : 0.15f);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (isNew) AssetDatabase.CreateAsset(mat, matPath);
            EditorUtility.SetDirty(mat);
            count++;
        }

        AssetDatabase.SaveAssets();
        return count;
    }

    /// <summary>Povrchový materiál podľa názvu priečinka (napr. "old_road") alebo null; vytvorí ho, ak ešte nie je.</summary>
    static Material LoadSurface(string key)
    {
        EnsureSurfaceMaterials(false);
        return AssetDatabase.LoadAssetAtPath<Material>($"{SurfacesFolder}/{key}.mat");
    }
}
