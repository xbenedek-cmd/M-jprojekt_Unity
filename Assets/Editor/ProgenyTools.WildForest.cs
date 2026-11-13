using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Prepojenie balíka NatureManufacture "Forest Environment Dynamic Nature" s divočinami:
///  - buky (06–08, optimalizované pre les) sa miešajú so smrekmi z Mountain Tree Pack podľa zóny (Temný les najviac),
///  - vrstvy terénu z balíka (tráva s rastlinami, lístie, machy, pôda, mokrá pôda, kamene),
///  - lesný dekor: kroviny, lesné kamene, pne, padnuté kmene, huby (rozmiestnené na teréne v hraniciach mapy),
///  - kapradiny, rastliny a tráva z "Unity Terrain Prefabs" ako detaily terénu (pozri ProgenyTools.WildGrass.cs).
/// Zóny Bleskový kaňon a Zrezavená pustatina ostávajú bez lesa (len pár krovín a kameňov).
/// </summary>
public static partial class ProgenyTools
{
    // ======== NASTAVENIA ========
    const string ForestFolder = "Assets/NatureManufacture Assets/Forest Environment Dynamic Nature/";
    const float DecorMinSpacing = 1.8f;     // najmenšia vzdialenosť medzi dekormi (m)

    // Podiel stromov, ktoré sú buky (zvyšok smreky), a množstvo lesného dekoru (1 = plné) podľa zóny
    static float BeechShare(string key)
    {
        switch (key)
        {
            case "Les": return 0.65f;
            case "Divocina": return 0.5f;
            case "Mociare": return 0.3f;
            default: return 0f;
        }
    }

    static float ForestDecorAmount(string key)
    {
        switch (key)
        {
            case "Les": return 1f;
            case "Divocina": return 0.8f;
            case "Mociare": return 0.7f;
            case "Pustatina": return 0.15f;
            default: return 0.2f;
        }
    }

    static readonly string[] beechTrees = { "prefab_beech_tree_06", "prefab_beech_tree_07", "prefab_beech_tree_08" };

    static string ZoneKeyOf(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (p.name == "Divocina") return "Divocina";
            if (p.name.StartsWith("Divocina_")) return p.name.Substring("Divocina_".Length);
        }
        return "Divocina";
    }

    // Buk z balíka (len živý strom a len keď zóna má les); null = použije sa smrek
    static GameObject BeechFor(GameObject group, System.Random rnd)
    {
        float share = BeechShare(ZoneKeyOf(group.transform));
        if (share <= 0f || rnd.NextDouble() >= share) return null;

        string name = beechTrees[rnd.Next(beechTrees.Length)];
        return AssetDatabase.LoadAssetAtPath<GameObject>($"{ForestFolder}Beech Trees/Prefabs/{name}.prefab");
    }

    // ======== Vrstvy terénu z balíka ========
    static TerrainLayer PackLayer(string file) =>
        AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{ForestFolder}Terrain Layers/{file}.terrainlayer");

    // 0 tráva zóny, 1 cesta (pôda), 2 skala (kamene), 3 breh jazera (mokrá pôda)
    static TerrainLayer[] BuildTerrainLayers(string key, string grassSurface, string shoreSurface)
    {
        TerrainLayer grass = key == "Divocina" ? PackLayer("Terrain_Layer4_Grass_Plants")
                           : key == "Les" ? PackLayer("Terrain_Layer5_Leaves")
                           : key == "Mociare" ? PackLayer("Terrain_Layer6_Moss") : null;   // kaňon a pustatina ostávajú so sutinou

        return new[]
        {
            grass ?? MakeTerrainLayer(grassSurface, "wild_grass"),
            PackLayer("Terrain_Layer2_Soil") ?? MakeTerrainLayer("old_road", grassSurface),
            PackLayer("Terrain_Layer8_Stones") ?? MakeTerrainLayer("castle_wall", grassSurface),
            PackLayer("Terrain_Layer3_Soil_Wet") ?? MakeTerrainLayer(shoreSurface, grassSurface)
        };
    }

    // ======== Kapradiny, rastliny a tráva z balíka ako detaily terénu ========
    static string[] NatureDetailsFor(string key)
    {
        switch (key)
        {
            case "Les": return new[] { "prefab_Unity_Terrain_fern_01_1", "prefab_Unity_Terrain_fern_01_3", "prefab_Unity_Terrain_plant_02_1", "prefab_Unity_Terrain_grass_03_1" };
            case "Divocina": return new[] { "prefab_Unity_Terrain_grass_01_1", "prefab_Unity_Terrain_grass_02_2", "prefab_Unity_Terrain_fern_01_2", "prefab_Unity_Terrain_plant_01_1" };
            case "Mociare": return new[] { "prefab_Unity_Terrain_grass_01_3", "prefab_Unity_Terrain_plant_03_1", "prefab_Unity_Terrain_fern_01_4" };
            default: return new string[0];
        }
    }

    static GameObject NatureDetailPrefab(string name) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{ForestFolder}Foliage and Grass/Unity Terrain Prefabs/{name}.prefab");

    // ======== Lesný dekor ========
    static List<GameObject> ForestPrefabs(string subfolder, params string[] nameParts)
    {
        var list = new List<GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ForestFolder + subfolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            if (file.StartsWith("VS_") || file.Contains("Leaves") || file.EndsWith("_Moss") || file.EndsWith("_Sand") || file.Contains("Moss_") || file.Contains("Sand_")) continue;

            bool match = nameParts.Length == 0;
            foreach (string part in nameParts)
                if (file.ToLowerInvariant().Contains(part.ToLowerInvariant())) { match = true; break; }
            if (!match) continue;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) list.Add(prefab);
        }
        return list;
    }

    class DecorSpec
    {
        public string group;
        public List<GameObject> prefabs;
        public int count;
        public float minScale = 0.85f, maxScale = 1.25f;
        public bool collider;
        public bool trunk;                // pevný kmeň (kapsula) namiesto krabice
        public bool breakable = true;     // false = čistá dekorácia
    }

    static void DressForest(GameObject root, WildTheme theme, Terrain terrain)
    {
        Transform old = root.transform.Find("LesnyDekor");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        float amount = ForestDecorAmount(theme.key);
        if (amount <= 0f || terrain == null) return;

        var specs = new List<DecorSpec>
        {
            new DecorSpec { group = "Kroviny", prefabs = ForestPrefabs("Bushes", "forest_black_cherry"), count = Mathf.RoundToInt(36 * amount), minScale = 0.9f, maxScale = 1.35f },
            new DecorSpec { group = "Kamene", prefabs = ForestPrefabs("Rocks", "forest_stones"), count = Mathf.RoundToInt(14 * amount), minScale = 0.8f, maxScale = 1.5f, collider = true },
            new DecorSpec { group = "Pne", prefabs = ForestPrefabs("Stumps Roots and Branches", "forest_stump"), count = Mathf.RoundToInt(9 * amount), collider = true },
            new DecorSpec { group = "Kmene", prefabs = ForestPrefabs("Stumps Roots and Branches", "dead_log"), count = Mathf.RoundToInt(8 * amount), collider = true },
            new DecorSpec { group = "Huby", prefabs = ForestPrefabs("Mushrooms", "mushroom"), count = Mathf.RoundToInt(18 * amount), minScale = 0.8f, maxScale = 1.4f },
            // Mŕtve stromy: veľa, ale len ako dekorácia (nedajú sa zničiť)
            new DecorSpec { group = "MrtveStromy", prefabs = DeadFirPrefabs(), count = DeadTreeDecorCount(theme.key), minScale = 0.9f, maxScale = 1.3f, collider = true, trunk = true, breakable = false }
        };

        var holder = new GameObject("LesnyDekor").transform;
        holder.SetParent(root.transform, false);

        WildLake[] lakes = root.GetComponentsInChildren<WildLake>(true);
        var rnd = new System.Random(theme.key.GetHashCode() ^ 0x3d51);
        var placed = new List<Vector3>();
        Vector3 c = terrain.transform.position + new Vector3(TerrainSize * 0.5f, 0f, TerrainSize * 0.5f);   // stred podľa terénu
        float span = TerrainSize * 0.5f - BoundsMargin - 2f;
        int total = 0;

        foreach (DecorSpec spec in specs)
        {
            if (spec.prefabs.Count == 0 || spec.count <= 0) continue;
            Transform group = new GameObject(spec.group).transform;
            group.SetParent(holder, false);

            for (int i = 0; i < spec.count; i++)
            {
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    var p = new Vector3(c.x + ((float)rnd.NextDouble() * 2f - 1f) * span, 0f, c.z + ((float)rnd.NextDouble() * 2f - 1f) * span);
                    if (!PropPositionOk(terrain, p, root, theme, lakes, false)) continue;

                    bool tooClose = false;
                    foreach (Vector3 q in placed)
                        if ((q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z) < DecorMinSpacing * DecorMinSpacing) { tooClose = true; break; }
                    if (tooClose) continue;

                    p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                    GameObject prefab = spec.prefabs[rnd.Next(spec.prefabs.Count)];
                    GameObject item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                    item.transform.position = p;
                    item.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                    item.transform.localScale = Vector3.one * Mathf.Lerp(spec.minScale, spec.maxScale, (float)rnd.NextDouble());

                    if (spec.collider)
                    {
                        // Prefaby z balíka nemusia mať pevnú kolíziu: bez nej by sa cez ne dalo prechádzať
                        bool solid = false;
                        foreach (Collider col in item.GetComponentsInChildren<Collider>(true))
                            if (!col.isTrigger) { solid = true; break; }
                        if (!solid) { if (spec.trunk) AddTrunkCollider(item); else AddColliderFromBounds(item); }

                        // Kamene sa ťažia krompáčom, pne a kmene sekerou (rovnako ako ostatné zdroje v divočine)
                        if (!spec.breakable) { /* dekorácia */ }
                        else if (spec.group == "Kamene") MakeBreakableFromBounds(item, ToolKit.Kind.Pickaxe, ItemCatalog.Stone, "Skala", 50f, 45f);
                        else MakeBreakableFromBounds(item, ToolKit.Kind.Axe, ItemCatalog.Wood, spec.group == "Pne" ? "Pník" : "Kmeň", 40f, 30f);
                    }
                    placed.Add(p);
                    total++;
                    break;
                }
            }
        }
        Debug.Log($"{theme.displayName}: lesný dekor z balíka Dynamic Nature, {total} kusov (kroviny, kamene, pne, kmene, huby).");
    }
}

public static partial class ProgenyTools
{
    /// <summary>
    /// Každý kameň v divočine sa dá ťažiť alebo zničiť krompáčom a má pevnú kolíziu: skaly, monolit, lesné kamene z balíka
    /// a kamene z modelov. Kopy kameňa (Zdroj_kameň) sa zbierajú klávesom F ako doteraz, len sa doplní chýbajúca kolízia.
    /// </summary>
    static int EnsureRocksHarvestable(GameObject root)
    {
        int fixedCount = 0;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == root.transform) continue;

            bool isRock = t.name == "Skala" || t.name == "Monolit" || t.name.Contains("forest_stones") || t.name.Contains("Scatter_Rock") || t.name.Contains("Flint_Rock") || IsRockModelName(t.name);
            bool isPile = t.name == "Zdroj_kameň";
            if (!isRock && !isPile) continue;
            if (isRock && (t.GetComponentInParent<BreakableNode>() != null || t.GetComponentInParent<ResourceNode>() != null) && t.GetComponent<BreakableNode>() == null && t.name != "Skala" && t.name != "Monolit")
                continue;   // model v už ťažiteľnej skupine

            if (isRock && (t.name == "Skala" || t.name == "Monolit") && RockGuard.Realign(t.gameObject)) fixedCount++;   // kolízia k modelu, ak sa model posunul ručne

            bool solid = false;
            foreach (Collider c in t.GetComponentsInChildren<Collider>(true))
                if (!c.isTrigger) { solid = true; break; }
            if (!solid && t.GetComponentInChildren<Renderer>() != null) { AddColliderFromBounds(t.gameObject); fixedCount++; }

            if (isPile && t.GetComponent<BreakableNode>() == null)
            {
                // Kopa kameňov sa dá aj zbierať (F), aj ničiť krompáčom
                MakeBreakableFromBounds(t.gameObject, ToolKit.Kind.Pickaxe, ItemCatalog.Stone, "Kamene", 40f, 30f);
                fixedCount++;
            }
            if (isRock && t.GetComponent<BreakableNode>() == null && t.GetComponent<ResourceNode>() == null)
            {
                bool monolith = t.name == "Monolit";
                MakeBreakableFromBounds(t.gameObject, ToolKit.Kind.Pickaxe, ItemCatalog.Stone, monolith ? "Monolit" : "Skala", monolith ? 250f : 50f, monolith ? 20f : 45f);
                fixedCount++;
            }
        }

        // Skaly z modelov, ktoré vznikli bez textúry (materiál sa vtedy nepodarilo vytvoriť): textúra sa doplní z materiálu modelu
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || !t.name.EndsWith("_High") || !(IsRockModelName(t.name) || IsWreckModelName(t.name))) continue;

            Material mat = PropMaterial(t.name);
            if (mat == null) continue;

            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                bool bare = false;
                foreach (Material current in r.sharedMaterials)
                    if (current == null || current.mainTexture == null) { bare = true; break; }
                if (!bare) continue;

                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
                fixedCount++;
            }
        }
        return fixedCount;
    }
}

public static partial class ProgenyTools
{
    // ======== Mŕtve stromy: len dekorácia ========
    /// <summary>Mŕtvy strom sa nedá zničiť ani zbierať: zruší sa BreakableNode a ostane pevný kmeň (kolízia), aby sa cez neho nedalo prejsť.</summary>
    static void MakeDeadDecor(GameObject group)
    {
        BreakableNode node = group.GetComponent<BreakableNode>();
        if (node != null) Object.DestroyImmediate(node);
        Transform stump = group.transform.Find("Pen");
        if (stump != null) Object.DestroyImmediate(stump.gameObject);

        foreach (Collider c in group.GetComponentsInChildren<Collider>(true))
            if (c is CapsuleCollider) Object.DestroyImmediate(c);
        AddTrunkCollider(group);
    }

    // Kmeň ako kapsula (výška podľa stromu, najviac 6 m); koruna nekoliduje
    static void AddTrunkCollider(GameObject group)
    {
        Renderer[] renderers = group.GetComponentsInChildren<Renderer>();
        float height = 4f;
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            height = Mathf.Clamp(b.size.y, 1.5f, 6f);
        }

        float scale = Mathf.Max(0.0001f, group.transform.lossyScale.x);
        CapsuleCollider capsule = group.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = 0.4f / scale;
        capsule.height = height / scale;
        capsule.center = new Vector3(0f, height * 0.5f / scale, 0f);
    }

    // ======== Pne po zrúbaní stromu ========
    static readonly string[] stumpNames = { "prefab_beech_forest_stump_01_1", "prefab_beech_forest_stump_01_2", "prefab_beech_forest_stump_01_3" };

    /// <summary>Strom dostane neaktívny potomok "Pen" (peň z balíka); BreakableNode ho po páde stromu zapne a pri znovuvyrastení vypne.</summary>
    static void EnsureStump(GameObject group, System.Random rnd)
    {
        if (group.transform.Find("Pen") != null) return;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ForestFolder}Stumps Roots and Branches/Prefabs/{stumpNames[rnd.Next(stumpNames.Length)]}.prefab");
        if (prefab == null) return;

        GameObject stump = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
        stump.name = "Pen";
        stump.transform.localPosition = Vector3.zero;
        stump.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
        stump.transform.localScale = Vector3.one * RandomRange(rnd, 0.9f, 1.35f);

        // Kolízia sa počíta pri zapnutom objekte (neaktívne vykresľovače sa nezbierajú), potom sa peň vypne až do pádu stromu
        AddColliderFromBounds(stump);
        stump.SetActive(false);
        EditorUtility.SetDirty(group);
    }

    // ======== Krajšie byliny ========
    static readonly string[] herbPrefabs =
    {
        "prefab_lily_valley_01_1", "prefab_lily_valley_01_2", "prefab_shaggy_soldier_01_1", "prefab_shaggy_soldier_01_2", "prefab_plant_03_1"
    };

    /// <summary>Zbierané byliny: zhluk 3–4 rastlín z balíka Forest Environment (konvalinky, kvitnúce rastliny). False, ak balík chýba.</summary>
    static bool FillHerbModels(GameObject node, System.Random rnd)
    {
        int placed = 0;
        int count = 3 + rnd.Next(2);
        for (int i = 0; i < count; i++)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ForestFolder}Foliage and Grass/Prefabs/{herbPrefabs[rnd.Next(herbPrefabs.Length)]}.prefab");
            if (prefab == null) continue;

            GameObject plant = (GameObject)PrefabUtility.InstantiatePrefab(prefab, node.transform);
            float angle = (float)rnd.NextDouble() * Mathf.PI * 2f, radius = i == 0 ? 0f : 0.15f + (float)rnd.NextDouble() * 0.25f;
            plant.transform.localPosition = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            plant.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            plant.transform.localScale = Vector3.one * RandomRange(rnd, 0.9f, 1.4f);
            foreach (Collider c in plant.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            placed++;
        }
        return placed > 0;
    }
}

public static partial class ProgenyTools
{
    static List<GameObject> DeadFirPrefabs()
    {
        var list = new List<GameObject>();
        foreach (string name in deadFirs)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FirFolder + name + ".prefab");
            if (prefab != null) list.Add(prefab);
        }
        return list;
    }

    // Koľko mŕtvych stromov-dekorácií pribudne v zóne (močiare a les najviac)
    static int DeadTreeDecorCount(string key)
    {
        switch (key)
        {
            case "Mociare": return 34;
            case "Les": return 28;
            case "Divocina": return 22;
            case "Pustatina": return 24;
            default: return 14;
        }
    }
}
