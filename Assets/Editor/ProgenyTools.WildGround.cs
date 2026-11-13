using UnityEditor;
using UnityEngine;

/// <summary>
/// Tráva v Divočinách: zem každej zóny (Zem_&lt;kľúč&gt;) dostane dlaždicový povrchový materiál z Assets/Materials/Povrchy
/// (wild_grass, mossy_grass, grass_and_rubble), zafarbený do tónu zóny. Spúšťa sa aj po vytvorení Divočín.
/// </summary>
public static partial class ProgenyTools
{
    const float GroundTileMeters = 5f;

    static string GroundSurfaceFor(string themeKey)
    {
        switch (themeKey)
        {
            case "Les": return "mossy_grass";
            case "Mociare": return "mossy_grass";
            case "Kanon": return "grass_and_rubble";
            case "Pustatina": return "grass_and_rubble";
            default: return "wild_grass";
        }
    }

    [MenuItem("Progeny/Divočiny/Tráva na zem", false, 111)]
    static void ApplyWildGrassMenu()
    {
        AssetDatabase.Refresh();
        EnsureSurfaceMaterials(false);

        int n = 0;
        foreach (WildTheme t in WildThemes)
        {
            GameObject ground = GameObject.Find("Zem_" + t.key);
            if (ground != null && ApplyWildGround(ground, t)) n++;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"Tráva v Divočinách: {n} zón. Ak je 0, spusti najprv Materiály: vytvoriť z balíkov (Povrchy). Ulož scénu.");
    }

    static bool ApplyWildGround(GameObject ground, WildTheme t)
    {
        Material surface = LoadSurface(GroundSurfaceFor(t.key));
        if (surface == null) return false;

        string path = $"{SurfacesFolder}/Zem_{t.key}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = m == null;
        if (isNew) m = new Material(surface);
        else EditorUtility.CopySerialized(surface, m);

        // Textúra tráva zostane viditeľná, tón zóny ju len stmaví / zafarbí
        m.color = Color.Lerp(Color.white, t.ground * 2.2f, 0.45f);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", 0.08f);
        Vector3 s = ground.transform.localScale;
        m.mainTextureScale = new Vector2(s.x / GroundTileMeters, s.z / GroundTileMeters);

        if (isNew) AssetDatabase.CreateAsset(m, path);
        EditorUtility.SetDirty(m);

        Renderer r = ground.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = m;
        return true;
    }
}
