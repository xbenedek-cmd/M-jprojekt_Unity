using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Vzhľad zbieraných zdrojov v Divočine (menšie a detailnejšie než pôvodné kvádre) a jednorazová úprava už vytvorených Divočín:
/// stromy dostanú ChoppableTree (ničia sa len sekerou), zdroje sa prestavajú, ruiny dostanú kameň (castle_wall) a jazierka vodu (swamp_water).
/// </summary>
public static partial class ProgenyTools
{
    // ---------- Tvary zdrojov (v lokálnom priestore skupiny) ----------
    static void FillHerb(GameObject node, WildMats m, System.Random rnd)
    {
        if (FillHerbModels(node, rnd)) return;   // krajšie byliny z balíka Forest Environment

        Transform t = node.transform;
        Part(PrimitiveType.Sphere, t, new Vector3(0f, 0.02f, 0f), new Vector3(0.5f, 0.12f, 0.5f), m.dirt, false);

        for (int i = 0; i < 9; i++)
        {
            float angle = i * 40f * Mathf.Deg2Rad + RandomRange(rnd, -0.15f, 0.15f);
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float tilt = RandomRange(rnd, 14f, 34f);
            float len = RandomRange(rnd, 0.14f, 0.24f);
            Quaternion lean = Quaternion.AngleAxis(tilt, Vector3.Cross(Vector3.up, outward));
            Vector3 pos = outward * RandomRange(rnd, 0.03f, 0.1f) + lean * (Vector3.up * len) + Vector3.up * 0.04f;
            Part(PrimitiveType.Capsule, t, pos, new Vector3(0.045f, len, 0.045f), i % 3 == 0 ? m.leafDark : m.leaf, false, lean);
        }
        for (int i = 0; i < 3; i++)
            Part(PrimitiveType.Sphere, t, new Vector3(RandomRange(rnd, -0.1f, 0.1f), RandomRange(rnd, 0.28f, 0.36f), RandomRange(rnd, -0.1f, 0.1f)), Vector3.one * 0.075f, m.flower, false);
    }

    static void FillLogs(GameObject node, WildMats m, System.Random rnd)
    {
        Transform t = node.transform;
        Quaternion lying = Quaternion.Euler(0f, 0f, 90f);
        Vector3[] spots = { new Vector3(0f, 0.09f, -0.1f), new Vector3(0f, 0.09f, 0.1f), new Vector3(0f, 0.25f, 0f) };

        foreach (Vector3 p in spots)
        {
            float shift = RandomRange(rnd, -0.05f, 0.05f);
            Vector3 c = p + Vector3.right * shift;
            Part(PrimitiveType.Cylinder, t, c, new Vector3(0.17f, 0.36f, 0.17f), m.bark, true, lying);
            // Svetlé rezy na koncoch polená
            Part(PrimitiveType.Cylinder, t, c + Vector3.right * 0.362f, new Vector3(0.15f, 0.006f, 0.15f), m.cut, false, lying);
            Part(PrimitiveType.Cylinder, t, c - Vector3.right * 0.362f, new Vector3(0.15f, 0.006f, 0.15f), m.cut, false, lying);
        }
    }

    static void FillStone(GameObject node, WildMats m, System.Random rnd)
    {
        // Kôpka z 2–3 malých kameňov z balíka (kolízia ostáva na zbieranie klávesom F); bez modelov kvádre
        int pieces = 2 + rnd.Next(2);
        bool any = false;
        for (int i = 0; i < pieces; i++)
        {
            string key = rockKeys[rnd.Next(rockKeys.Length)];
            Vector3 at = node.transform.position + node.transform.rotation * new Vector3(RandomRange(rnd, -0.25f, 0.25f), 0f, RandomRange(rnd, -0.25f, 0.25f));
            if (PlaceProp(node.transform, key, at, RandomRange(rnd, 0.35f, 0.65f), RandomRange(rnd, 0f, 360f), true) != null) any = true;
        }
        if (any) return;

        Transform t = node.transform;
        Part(PrimitiveType.Cube, t, new Vector3(-0.12f, 0.16f, 0f), new Vector3(0.5f, 0.32f, 0.42f), m.rock, true, Quaternion.Euler(0f, 20f, 6f));
        Part(PrimitiveType.Cube, t, new Vector3(0.26f, 0.12f, 0.1f), new Vector3(0.38f, 0.26f, 0.36f), m.rock, true, Quaternion.Euler(0f, -30f, -5f));
    }

    static void FillScrap(GameObject node, WildMats m, System.Random rnd)
    {
        if (!FillScrapProp(node, rnd)) FillScrapPrimitive(node, m, rnd);   // kľúče a poklop z balíka, inak kvádre
    }

    static void FillScrapPrimitive(GameObject node, WildMats m, System.Random rnd)
    {
        Transform t = node.transform;
        Part(PrimitiveType.Cube, t, new Vector3(0f, 0.03f, 0f), new Vector3(0.55f, 0.05f, 0.42f), m.metal, true, Quaternion.Euler(0f, RandomRange(rnd, -20f, 20f), 3f));
        Part(PrimitiveType.Cube, t, new Vector3(-0.1f, 0.17f, 0f), new Vector3(0.3f, 0.22f, 0.3f), m.rust, true, Quaternion.Euler(0f, 20f, 0f));
        Part(PrimitiveType.Cylinder, t, new Vector3(0.14f, 0.1f, 0.18f), new Vector3(0.1f, 0.26f, 0.1f), m.metal, true, Quaternion.Euler(0f, 0f, 90f));
        Part(PrimitiveType.Cylinder, t, new Vector3(0.16f, 0.24f, -0.12f), new Vector3(0.24f, 0.02f, 0.24f), m.rust, false, Quaternion.Euler(70f, 0f, 0f));
        Part(PrimitiveType.Cube, t, new Vector3(0.2f, 0.16f, -0.05f), new Vector3(0.06f, 0.28f, 0.06f), m.metal, false, Quaternion.Euler(0f, 0f, 12f));
    }

    // ---------- Jednorazová úprava existujúcich Divočín ----------
    [MenuItem("Progeny/Divočiny/Vylepšiť zdroje, stromy, ruiny a vodu", false, 112)]
    static void UpgradeExistingWilds()
    {
        AssetDatabase.Refresh();
        EnsureSurfaceMaterials(false);
        EnsureToolModels(false);

        int trees = 0, removed = 0, added = 0, nodes = 0, rocksN = 0, wrecksN = 0, ruins = 0, ponds = 0, camps = 0, hillsLifted = 0;
        foreach (WildTheme theme in WildThemes)
        {
            GameObject root = GameObject.Find(theme.RootName);
            if (root == null) continue;

            WildMats m = MakeWildMats(theme);
            Transform groundT = root.transform.Find("Zem_" + theme.key);
            Bounds zone = groundT != null && groundT.GetComponent<Renderer>() != null
                ? groundT.GetComponent<Renderer>().bounds
                : new Bounds(root.transform.position, new Vector3(420f, 10f, 420f));

            // 0) Kopce (ak ešte nie sú) a vyzdvihnutie objektov, ktoré by v nich uviazli
            BuildHills(root, theme, zone.center, zone.max.y);
            if (FindWildTerrain(root) == null) hillsLifted += LiftOntoHills(root, zone.max.y);   // s terénom objekty usádza ConformAllWilds

            // 1) Stromy mimo mapy sa zmažú (zóna má okraj s útesmi)
            const float inset = 12f;
            var all = new List<Transform>(root.GetComponentsInChildren<Transform>(true));
            foreach (Transform t in all)
            {
                if (t == null || t == root.transform) continue;
                if (t.name != "Strom" && t.name != "Smrek" && t.name != "Mŕtvy strom") continue;

                Vector3 p = t.position;
                if (Mathf.Abs(p.x - zone.center.x) > zone.extents.x - inset || Mathf.Abs(p.z - zone.center.z) > zone.extents.z - inset)
                {
                    Object.DestroyImmediate(t.gameObject);
                    removed++;
                }
            }

            // 2) Všetky ostatné objekty
            int treesHere = 0, campsHere = 0, logsHere = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == root.transform) continue;
                var rnd = new System.Random(Mathf.RoundToInt(t.position.x * 31f + t.position.z * 17f));
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);   // zvyšky zrušeného ChoppableTree

                switch (t.name)
                {
                    case "Strom": ReplaceWithFir(t, false, rnd, 140f); treesHere++; break;
                    case "Smrek": ReplaceWithFir(t, false, rnd, 100f); treesHere++; break;
                    case "Mŕtvy strom": ReplaceWithFir(t, true, rnd, 45f); treesHere++; break;

                    case "Skala":
                        if (!HasRockModel(t)) Rebuild(t, () => FillRockOrCube(t.gameObject, m, rnd, RandomRange(rnd, 1.5f, 4.5f)));
                        MakeBreakableFromBounds(t.gameObject, ToolKit.Kind.Pickaxe, ItemCatalog.Stone, "Skala", 50f, 45f);
                        rocksN++;
                        break;
                    case "Vrak":   // železný blok = malý kovový predmet z knižnice (debna, skriňa)
                        if (!HasWreckModel(t)) Rebuild(t, () => { if (!FillWreckModel(t.gameObject, rnd)) FillWreckCube(t.gameObject, m, rnd); });
                        MakeBreakableFromBounds(t.gameObject, ToolKit.Kind.Hammer, ItemCatalog.Scrap, "Železný odpad", 35f, 20f);
                        wrecksN++;
                        break;

                    case "Zdroj_bylina": Rebuild(t, () => FillHerb(t.gameObject, m, rnd)); nodes++; break;
                    case "Zdroj_drevo": Rebuild(t, () => FillLogs(t.gameObject, m, rnd)); nodes++; logsHere++; break;
                    case "Zdroj_kameň": if (!HasRockModel(t)) Rebuild(t, () => FillStone(t.gameObject, m, rnd)); nodes++; break;
                    case "Zdroj_šrot": Rebuild(t, () => FillScrap(t.gameObject, m, rnd)); nodes++; break;
                    case "Zdroj_kryštál":
                        Rebuild(t, () => FillCrystals(t.gameObject, m, rnd, false));
                        ResourceNode crystalNode = t.GetComponent<ResourceNode>();
                        if (crystalNode != null) { crystalNode.SetRange(3.4f); EditorUtility.SetDirty(crystalNode); }
                        nodes++;
                        break;
                    case "Kryštály": Object.DestroyImmediate(t.gameObject); break;   // veľké dekoratívne kryštály sa zrušili

                    case "Ruina":
                        ApplyRuinMaterial(t.gameObject, m);
                        foreach (Transform piece in t) AddColliderFromBounds(piece.gameObject);   // oprava kolízie (predtým sa cez ruiny dalo prejsť)
                        ruins++;
                        break;
                    case "Rybník":
                        Renderer r = t.GetComponent<Renderer>();
                        if (r != null && t.GetComponent<WildLake>() == null) { r.sharedMaterial = m.water; ponds++; }   // jazerá s terénom majú vlastný vodný materiál
                        break;
                    case "Tabor": campsHere++; break;
                }
            }
            trees += treesHere;

            // 3) Viac stromov (cieľ 1,6× pôvodný zámer zóny), len vo vnútri mapy a mimo portálov a predmetov; vojenský tábor, ak ešte nie je
            added += TopUpTrees(root, theme, zone, inset, treesHere);
            nodes += TopUpLogs(root, theme, m, zone, inset, logsHere);
            camps += campsHere > 0 ? campsHere : AddCamps(root, theme, zone, inset);
        }

        PlotManager plotManager = Object.FindAnyObjectByType<PlotManager>();
        if (plotManager != null)
        {
            plotManager.SetRuinMaterial(LoadSurface("castle_wall"));
            EditorUtility.SetDirty(plotManager);
        }

        ConformAllWilds();
        RefreshCrystalMaterials();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"Divočiny upravené: stromy {trees} (zmazané mimo mapy {removed}, pridané {added}), skaly {rocksN}, železné bloky {wrecksN}, zdroje {nodes}, ruiny {ruins}, jazierka {ponds}, vojenské tábory {camps}, objekty vyzdvihnuté na kopce {hillsLifted}. Ulož scénu.");
    }

    // Pevnosť podľa veľkosti objektu: hp = základ + diagonála × koeficient (obmedzené 40–300, aby suroviny vyšli 3–10)
    static void MakeBreakableFromBounds(GameObject g, ToolKit.Kind tool, string item, string label, float baseHp, float perMeter)
    {
        Renderer[] rs = g.GetComponentsInChildren<Renderer>(true);
        float diagonal = 2f;
        if (rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            diagonal = b.size.magnitude;
        }
        MakeBreakable(g, tool, item, label, Mathf.Clamp(baseHp + diagonal * perMeter, 40f, 300f), false);
    }

    static int TopUpTrees(GameObject root, WildTheme theme, Bounds zone, float inset, int existing)
    {
        // Bleskový kaňon: quest chce 50 dreva, preto sa počet dopĺňa na plnú hustotu ako pri vytvorení zóny
        int target = Mathf.RoundToInt((theme.trees + theme.conifers + theme.deadTrees) * 1.6f * (theme.key == "Kanon" ? CountScale : 1f));
        int toAdd = target - existing;
        if (toAdd <= 0) return 0;

        Transform forest = root.transform.Find("Lokacia_Les");
        if (forest == null) return 0;

        var rnd = new System.Random(theme.key.GetHashCode() ^ 0x5bd1);
        Vector3 center = zone.center;
        WildAnchors(theme.key, out Vector3 refA, out _, out _);
        Vector3 anchorA = center + refA;
        Physics.SyncTransforms();

        int added = 0;
        for (int attempt = 0; attempt < toAdd * 8 && added < toAdd; attempt++)
        {
            Vector3 p = rnd.NextDouble() < 0.6
                ? Around(rnd, anchorA, 45f)
                : center + new Vector3(((float)rnd.NextDouble() * 2f - 1f) * (zone.extents.x - inset - 2f), 0f, ((float)rnd.NextDouble() * 2f - 1f) * (zone.extents.z - inset - 2f));
            p.y = zone.max.y;   // povrch zeme
            if (Mathf.Abs(p.x - center.x) > zone.extents.x - inset || Mathf.Abs(p.z - center.z) > zone.extents.z - inset) continue;
            if (InHill(p) || Physics.CheckSphere(p + Vector3.up * 1.5f, 2.2f)) continue;   // kopec alebo niečo tu už stojí (portál, skala, zdroj...)

            bool dead = rnd.NextDouble() < 0.12;
            GameObject tree = NewGroup(dead ? "Mŕtvy strom" : "Smrek", forest, p, rnd);
            if (!PlaceFir(tree, dead, rnd)) { Object.DestroyImmediate(tree); return added; }
            Physics.SyncTransforms();
            added++;
        }
        return added;
    }

    // Hromady polien na zbieranie (F): doplní sa do cieľa zóny (wood × CountScale), len v Bleskovom kaňone kvôli questu
    static int TopUpLogs(GameObject root, WildTheme theme, WildMats m, Bounds zone, float inset, int existing)
    {
        if (theme.key != "Kanon") return 0;
        int toAdd = Mathf.RoundToInt(theme.wood * CountScale) - existing;
        Transform forest = root.transform.Find("Lokacia_Les");
        if (toAdd <= 0 || forest == null) return 0;

        var rnd = new System.Random(theme.key.GetHashCode() ^ 0x7a31);
        Vector3 center = zone.center;
        WildAnchors(theme.key, out Vector3 refA, out _, out _);
        Vector3 anchorA = center + refA;
        Physics.SyncTransforms();

        int added = 0;
        for (int attempt = 0; attempt < toAdd * 8 && added < toAdd; attempt++)
        {
            Vector3 p = rnd.NextDouble() < 0.6
                ? Around(rnd, anchorA, 45f)
                : center + new Vector3(((float)rnd.NextDouble() * 2f - 1f) * (zone.extents.x - inset - 2f), 0f, ((float)rnd.NextDouble() * 2f - 1f) * (zone.extents.z - inset - 2f));
            p.y = zone.max.y;
            if (Mathf.Abs(p.x - center.x) > zone.extents.x - inset || Mathf.Abs(p.z - center.z) > zone.extents.z - inset) continue;
            if (InHill(p) || Physics.CheckSphere(p + Vector3.up * 1.2f, 1.8f)) continue;

            WildLogs(forest, p, m, rnd);
            Physics.SyncTransforms();
            added++;
        }
        return added;
    }

    static int AddCamps(GameObject root, WildTheme theme, Bounds zone, float inset)
    {
        Transform parent = root.transform.Find("Lokacia_Vraky");
        if (parent == null) return 0;

        int count = Mathf.Clamp(theme.wrecks / 10, 1, 4);
        var rnd = new System.Random(theme.key.GetHashCode() ^ 0x3c6e);
        WildAnchors(theme.key, out _, out _, out Vector3 refC);
        Vector3 anchorC = zone.center + refC;
        Physics.SyncTransforms();

        int built = 0;
        for (int attempt = 0; attempt < count * 10 && built < count; attempt++)
        {
            Vector3 p = Around(rnd, anchorC, 60f);
            p.y = zone.max.y;
            if (Mathf.Abs(p.x - zone.center.x) > zone.extents.x - inset - 4f || Mathf.Abs(p.z - zone.center.z) > zone.extents.z - inset - 4f) continue;
            if (InHill(p) || Physics.CheckSphere(p + Vector3.up * 1.2f, 4.5f)) continue;

            BuildCamp(parent, p, rnd);
            Physics.SyncTransforms();
            built++;
        }
        return built;
    }

    // ---------- Smreky z balíka Dynamic Nature – Mountain Tree Pack (built-in shadery, LOD, kolízia kmeňa) ----------
    const string FirFolder = "Assets/NatureManufacture Assets/Dynamic Nature - Mountain Tree Pack/Prefabs/Standard/";
    static readonly string[] liveFirs =
    {
        "Fir_02_Small", "Fir_03_Medium", "Fir_04_Standalone", "Fir_04_Standalone_cheap", "Fir_05_Standalone", "Fir_05_Standalone_cheap",
        "Fir_06_Forest", "Fir_06_Forest_cheap", "Fir_07_Forest", "Fir_07_Forest_cheap"
    };
    static readonly string[] deadFirs = { "Fir_08_Dead_Forest", "Fir_08_Dead_Forest_cheap" };

    /// <summary>Vloží do skupiny náhodný smrek z balíka a spraví ju zničiteľnou sekerou. False, ak balík chýba (použije sa kvádrový strom).</summary>
    static bool PlaceFir(GameObject group, bool dead, System.Random rnd)
    {
        string[] pool = dead ? deadFirs : liveFirs;
        GameObject prefab = dead ? null : BeechFor(group, rnd);   // les zóny: časť stromov sú buky z Forest Environment
        if (prefab == null) prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FirFolder + pool[rnd.Next(pool.Length)] + ".prefab");
        if (prefab == null) return false;

        GameObject tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
        tree.transform.localPosition = Vector3.zero;
        tree.transform.localRotation = Quaternion.identity;
        tree.transform.localScale = Vector3.one * RandomRange(rnd, 0.8f, 1.2f);

        // Pevnosť podľa výšky stromu
        float height = 6f;
        Renderer[] renderers = tree.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            height = Mathf.Max(2f, b.size.y);
        }
        if (dead) MakeDeadDecor(group);   // mŕtve stromy sú len dekorácia: nedajú sa zničiť
        else
        {
            MakeBreakable(group, ToolKit.Kind.Axe, ItemCatalog.Wood, "Strom", Mathf.Clamp(50f + height * 12f, 40f, 300f), true);
            EnsureStump(group, rnd);
        }
        return true;
    }

    static void ReplaceWithFir(Transform group, bool dead, System.Random rnd, float fallbackHp)
    {
        // Už vložený smrek z balíka sa nemení (spustenie menu viackrát nemení stromy); len sa dohodí zničiteľnosť
        foreach (Transform child in group)
            if (child.name.StartsWith("Fir_") || child.name.Contains("beech_tree"))
            {
                if (dead) MakeDeadDecor(group.gameObject);
                else
                {
                    if (group.GetComponent<BreakableNode>() == null)
                        MakeBreakable(group.gameObject, ToolKit.Kind.Axe, ItemCatalog.Wood, "Strom", fallbackHp, true);
                    EnsureStump(group.gameObject, rnd);
                }
                return;
            }

        var kids = new System.Collections.Generic.List<GameObject>();
        foreach (Transform child in group) kids.Add(child.gameObject);

        // Najprv sa vloží smrek, až potom sa zmažú kvádrové časti (ak balík chýba, strom ostane)
        if (PlaceFir(group.gameObject, dead, rnd))
            foreach (GameObject kid in kids) Object.DestroyImmediate(kid);
        else if (dead) MakeDeadDecor(group.gameObject);
        else MakeBreakable(group.gameObject, ToolKit.Kind.Axe, ItemCatalog.Wood, "Strom", fallbackHp, true);
    }

    // Zmaže tvary zdroja (komponent ResourceNode ostáva) a vytvorí ich nanovo
    static void Rebuild(Transform node, System.Action fill)
    {
        for (int i = node.childCount - 1; i >= 0; i--) Object.DestroyImmediate(node.GetChild(i).gameObject);
        fill();
    }
}

public static partial class ProgenyTools
{
    static bool HasRockModel(Transform t)
    {
        foreach (Transform child in t)
            if (IsRockModelName(child.name)) return true;
        return false;
    }
}
