using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Modely z Assets/Materials/Stavby_do_hry v Divočine: kľúče na šrot (Rusty Wrench ×2, Metal Manhole Cover), vojenský tábor
/// (Military Trenches: debna s korisťou, vrecia s pieskom, plechová stena) a modely nástrojov (Splitting Maul = sekera, Old Pickaxe = krompáč)
/// pre HeldToolVisual (Resources/Tools). Materiály vznikajú z BaseColor + Normal textúr pri modeloch.
/// </summary>
public static partial class ProgenyTools
{
    const string ToolPrefabFolder = "Assets/Resources/Tools";

    // ---------- Materiál a model ----------
    static Material PropMaterial(string fileBase)
    {
        string matPath = $"{BuildingModelsFolder}/{fileBase}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null && existing.GetTexture("_MainTex") != null) return existing;

        Texture2D albedo = PropTexture(fileBase, "BaseColor", false);
        Texture2D normal = PropTexture(fileBase, "Normal", true);
        if (albedo == null) return existing;

        Material mat = existing != null ? existing : new Material(Shader.Find("Standard"));
        mat.SetTexture("_MainTex", albedo);
        mat.color = Color.white;
        mat.SetFloat("_Metallic", 0.15f);
        mat.SetFloat("_Glossiness", 0.3f);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }

        if (existing == null) AssetDatabase.CreateAsset(mat, matPath);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D PropTexture(string fileBase, string kind, bool normalMap)
    {
        foreach (string guid in AssetDatabase.FindAssets(fileBase + "_4K_" + kind, new[] { BuildingModelsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                bool changed = false;
                TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType != type) { importer.textureType = type; changed = true; }
                if (importer.maxTextureSize != 1024) { importer.maxTextureSize = 1024; changed = true; }
                if (changed) importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        return null;
    }

    /// <summary>Model podľa časti názvu súboru (napr. "Rusty_Wrench_wlklcjw"), spolu s názvom súboru bez prípony pre materiál.</summary>
    static GameObject PropModel(string key, out string fileBase)
    {
        fileBase = null;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { BuildingModelsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (file.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            fileBase = file;
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return null;
    }

    /// <summary>
    /// Postaví model do scény: textúrovaný materiál, mierka na najdlhší rozmer targetSize (m), spodok na zemi (y), kolízia z rozmerov.
    /// Vráti null, ak model chýba.
    /// </summary>
    static GameObject PlaceProp(Transform parent, string key, Vector3 groundPos, float targetSize, float yaw, bool collider)
    {
        GameObject model = PropModel(key, out string fileBase);
        if (model == null) return null;

        Material mat = PropMaterial(fileBase);
        GameObject root = new GameObject(key);
        root.transform.SetParent(parent);
        root.transform.position = groundPos;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) { Object.DestroyImmediate(root); return null; }

        foreach (Renderer r in renderers)
        {
            if (mat == null) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        float scale = targetSize / Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
        instance.transform.localScale = Vector3.one * scale;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        instance.transform.position += new Vector3(groundPos.x - b.center.x, groundPos.y - b.min.y, groundPos.z - b.center.z);

        if (collider) AddColliderFromBounds(root);
        return root;
    }

    // ---------- Šrot: kľúče a poklop ----------
    static readonly string[] scrapProps = { "Rusty_Wrench_wlklcjw", "Rusty_Wrench_wlklda1", "Metal_Manhole_Cover" };

    static bool FillScrapProp(GameObject node, System.Random rnd)
    {
        string key = scrapProps[rnd.Next(scrapProps.Length)];
        float size = key.StartsWith("Metal_Manhole") ? RandomRange(rnd, 0.6f, 0.8f) : RandomRange(rnd, 0.42f, 0.6f);

        // Skupina má pivot na zemi; model sa vloží doň (zdedí otočenie skupiny)
        GameObject prop = PlaceProp(node.transform, key, node.transform.position, size, RandomRange(rnd, 0f, 360f), true);
        return prop != null;
    }

    // ---------- Vojenský tábor ----------
    static void BuildCamp(Transform parent, Vector3 g, System.Random rnd)
    {
        GameObject camp = new GameObject("Tabor");
        camp.transform.SetParent(parent);
        camp.transform.position = g;
        float baseYaw = RandomRange(rnd, 0f, 360f);

        GameObject crate = PlaceProp(camp.transform, "Military_Trenches_Storage_Crate", g, 1.0f, baseYaw, true);
        if (crate != null) crate.AddComponent<LootCrate>();
        else
        {
            // Bez modelu aspoň drevená debna z kvádra
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Debna";
            box.transform.SetParent(camp.transform);
            box.transform.position = g + Vector3.up * 0.4f;
            box.transform.localScale = new Vector3(1f, 0.8f, 0.7f);
            box.AddComponent<LootCrate>();
        }

        Quaternion rot = Quaternion.Euler(0f, baseYaw, 0f);
        PlaceProp(camp.transform, "Military_Trenches_Barrier_Sandbag", g + rot * new Vector3(-2.2f, 0f, 1.6f), 2.4f, baseYaw + 20f, true);
        PlaceProp(camp.transform, "Military_Trenches_Barrier_Sandbag", g + rot * new Vector3(2.2f, 0f, 1.6f), 2.4f, baseYaw - 20f, true);
        PlaceProp(camp.transform, "Military_Trenches_Equipment_Sandbag", g + rot * new Vector3(1.6f, 0f, -1.4f), 1.1f, baseYaw + 40f, true);
        PlaceProp(camp.transform, "Military_Trenches_Wall_Extrusion", g + rot * new Vector3(0f, 0f, -2.4f), 3.4f, baseYaw, true);
    }

    // ---------- Modely nástrojov pre HeldToolVisual ----------
    [MenuItem("Progeny/Hráč/Pripraviť modely sekery a krompáča", false, 603)]
    static void PrepareToolModelsMenu()
    {
        AssetDatabase.Refresh();
        int n = EnsureToolModels(true);
        Debug.Log($"Modely nástrojov: {n}/2 (Assets/Resources/Tools).");
    }

    static int EnsureToolModels(bool force)
    {
        int count = 0;
        if (MakeToolPrefab("Splitting_Maul", "Tool_Axe", 0.95f, force)) count++;
        if (MakeToolPrefab("Old_Pickaxe", "Tool_Pickaxe", 0.95f, force)) count++;
        return count;
    }

    static bool MakeToolPrefab(string key, string prefabName, float length, bool force)
    {
        string prefabPath = $"{ToolPrefabFolder}/{prefabName}.prefab";
        if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) return true;

        GameObject model = PropModel(key, out string fileBase);
        if (model == null) return false;
        Material mat = PropMaterial(fileBase);

        GameObject wrapper = new GameObject(prefabName);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, wrapper.transform);
        foreach (Collider c in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

        MeshFilter[] filters = instance.GetComponentsInChildren<MeshFilter>();
        if (filters.Length == 0) { Object.DestroyImmediate(wrapper); return false; }

        if (mat != null)
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }

        // Najdlhšia os = tvar nástroja; hlava je na strane, kde je viac vrcholov (ťažisko)
        var points = new List<Vector3>();
        foreach (MeshFilter mf in filters)
            foreach (Vector3 v in mf.sharedMesh.vertices) points.Add(mf.transform.TransformPoint(v));

        Bounds b = new Bounds(points[0], Vector3.zero);
        Vector3 centroid = Vector3.zero;
        foreach (Vector3 p in points) { b.Encapsulate(p); centroid += p; }
        centroid /= points.Count;

        Vector3 size = b.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        Vector3 axisVector = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        float sign = Mathf.Sign(Vector3.Dot(centroid - b.center, axisVector) + 1e-6f);   // smer k hlave
        instance.transform.localRotation = Quaternion.FromToRotation(axisVector * sign, Vector3.up);
        instance.transform.localScale = Vector3.one * (length / Mathf.Max(0.001f, size[axis]));

        // Úchop v štvrtine dĺžky od konca rukoväte
        Bounds rb = default;
        bool first = true;
        foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
        {
            if (first) { rb = r.bounds; first = false; } else rb.Encapsulate(r.bounds);
        }
        instance.transform.position += new Vector3(-rb.center.x, -(rb.min.y + rb.size.y * 0.25f), -rb.center.z);

        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(ToolPrefabFolder)) AssetDatabase.CreateFolder("Assets/Resources", "Tools");
        PrefabUtility.SaveAsPrefabAsset(wrapper, prefabPath);
        Object.DestroyImmediate(wrapper);
        return true;
    }
}

public static partial class ProgenyTools
{
    // ---------- Kamene z balíka (Military Trenches Scatter Rock, Flint Rock, Mossy Stone) ----------
    static readonly string[] rockKeys =
    {
        "Military_Trenches_Scatter_Rock_S_01", "Military_Trenches_Scatter_Rock_S_02", "Military_Trenches_Scatter_Rock_S_03",
        "Military_Trenches_Scatter_Rock_S_07", "Military_Trenches_Scatter_Rock_S_08", "Military_Trenches_Scatter_Rock_S_09",
        "Military_Trenches_Scatter_Flint_Rock_S_01", "Military_Trenches_Scatter_Flint_Rock_S_02", "Mossy_Stone_sdAfk"
    };

    static bool IsRockModelName(string name)
    {
        foreach (string key in rockKeys) if (name.StartsWith(key)) return true;
        return false;
    }

    /// <summary>Vloží do skupiny náhodný kameň z balíka s najdlhším rozmerom size (m). False, ak modely chýbajú.</summary>
    static bool FillRockModel(GameObject group, System.Random rnd, float size)
    {
        for (int attempt = 0; attempt < rockKeys.Length; attempt++)
        {
            string key = rockKeys[rnd.Next(rockKeys.Length)];
            GameObject rock = PlaceProp(group.transform, key, group.transform.position, size, RandomRange(rnd, 0f, 360f), true);
            if (rock != null) return true;
        }
        return false;
    }

    // ---------- Prefaby s materiálom pre PlotManager (rumovisko, trámy, krb) ----------
    const string PlotPrefabFolder = "Assets/Materials/Stavby_do_hry/Prefabs";

    /// <summary>Prefab modelu s textúrovaným materiálom (za behu sa nedá priradiť materiál z textúr, preto sa uloží ako asset).</summary>
    static GameObject TexturedPrefab(string key)
    {
        GameObject model = PropModel(key, out string fileBase);
        if (model == null) return null;

        string path = $"{PlotPrefabFolder}/{fileBase}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Material mat = PropMaterial(fileBase);
        if (existing != null && mat != null)
        {
            Renderer er = existing.GetComponentInChildren<Renderer>();
            if (er != null && er.sharedMaterial == mat) return existing;
        }

        if (!AssetDatabase.IsValidFolder(PlotPrefabFolder)) AssetDatabase.CreateFolder(BuildingModelsFolder, "Prefabs");

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        if (mat != null)
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
        return saved;
    }
}
