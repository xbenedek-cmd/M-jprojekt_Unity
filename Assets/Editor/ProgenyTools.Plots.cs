using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Parcely a stavanie: mriežka parciel s cestami, PlotManager s modelmi ruín, nástroje na testovanie a uložený stav sveta.</summary>
public static partial class ProgenyTools
{
    const string BuildingModelsFolder = "Assets/Materials/Stavby_do_hry";
    const float PlotSide = 18f;
    const float PlotRoad = 6f;

    [MenuItem("Progeny/Mesto/Parcely pred hráčom", false, 301)]
    static void CreatePlotGrid()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        if (player == null) { Debug.LogError("Nenašiel sa hráč!"); return; }

        GameObject old = GameObject.Find("Parcely");
        if (old != null) Undo.DestroyObjectImmediate(old);
        GameObject root = new GameObject("Parcely");
        Undo.RegisterCreatedObjectUndo(root, "Create Plots");

        const int columns = 3, rows = 2;
        float pitch = PlotSide + PlotRoad;
        Vector3 center = player.transform.position + player.transform.forward * 40f;
        if (TryGetGroundY(center, out float groundY)) center.y = groundY; else center.y = player.transform.position.y - 0.9f;
        root.transform.position = center;

        // Cesty: jedna prašná plocha pod celou mriežkou; parcely sú na nej vyvýšené štvorce
        float width = columns * pitch + PlotRoad, depth = rows * pitch + PlotRoad;
        Slab(root.transform, "Cesta", center + Vector3.up * 0.03f, new Vector2(width, depth), new Color(0.33f, 0.27f, 0.2f));

        // Textúra starej cesty (balík old_road), ak je; dlaždice po ~4 m
        Material road = LoadSurface("old_road");
        Transform roadObject = root.transform.Find("Cesta");
        if (road != null && roadObject != null)
        {
            Material instance = new Material(road);
            instance.mainTextureScale = new Vector2(width / 4f, depth / 4f);
            roadObject.GetComponent<Renderer>().sharedMaterial = instance;
        }

        // Prvá parcela je lacná a bez nárokov, aby sa dalo začať; ďalšie sú drahšie a chcú dôveru
        FactionId[] owners = { FactionId.Civilians, FactionId.Rebels, FactionId.Civilians, FactionId.Rebels, FactionId.Civilians, FactionId.Rebels };
        int[] prices = { 50, 100, 150, 120, 180, 220 };
        float[] trust = { 0f, 5f, 10f, 10f, 15f, 20f };

        int n = 0;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                Vector3 pos = center + new Vector3((c - (columns - 1) * 0.5f) * pitch, 0f, (r - (rows - 1) * 0.5f) * pitch);

                GameObject plotGo = new GameObject($"Plot_P_{n + 1:00}");
                plotGo.transform.SetParent(root.transform);
                plotGo.transform.position = pos;
                Slab(plotGo.transform, "Podklad", pos + Vector3.up * 0.07f, new Vector2(PlotSide, PlotSide), new Color(0.22f, 0.21f, 0.2f));

                BuildPlot plot = plotGo.AddComponent<BuildPlot>();
                plot.Configure($"P_{n + 1:00}", owners[n], prices[n], trust[n]);
                EditorUtility.SetDirty(plot);
                n++;
            }

        EnsurePlotManager();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;
        Debug.Log($"Vytvorených {n} parciel (kláves P pri parcele). Prvá je lacná (50). Suroviny na stavbu sa zbierajú v Divočine. Ulož scénu.");
    }

    // Pevná doska (1 m hrubá, s kolíziou): vrch je na úrovni topCenter, takže parcely a cesty držia hráča aj tam, kde pod nimi nie je zem
    static void Slab(Transform parent, string name, Vector3 topCenter, Vector2 size, Color color)
    {
        const float thickness = 1f;
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent);
        g.transform.position = topCenter + Vector3.down * (thickness * 0.5f);
        g.transform.localScale = new Vector3(size.x, thickness, size.y);
        g.isStatic = true;

        Material m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Glossiness", 0f);
        g.GetComponent<Renderer>().sharedMaterial = m;
    }

    // Plocha bez kolízie (chodí sa po zemi pod ňou)
    static void Flat(Transform parent, string name, Vector3 center, Vector3 size, Color color)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent);
        g.transform.position = center;
        g.transform.localScale = size;
        Object.DestroyImmediate(g.GetComponent<Collider>());

        Material m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Glossiness", 0f);
        g.GetComponent<Renderer>().sharedMaterial = m;
    }

    // PlotManager + modely ruín z priečinka so stavbami (tehlové múry, oblúk, okno, komín)
    static void EnsurePlotManager()
    {
        PlotManager manager = Object.FindAnyObjectByType<PlotManager>();
        if (manager == null)
        {
            GameObject go = new GameObject("PlotManager");
            Undo.RegisterCreatedObjectUndo(go, "Create PlotManager");
            manager = go.AddComponent<PlotManager>();
        }

        var ruins = new List<GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { BuildingModelsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string file = Path.GetFileNameWithoutExtension(path);
            if (NameHas(file, "brickruin"))
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model != null) ruins.Add(model);
            }
        }

        manager.SetRuinPrefabs(ruins.ToArray());
        manager.SetRuinMaterial(LoadSurface("castle_wall"));
        EditorUtility.SetDirty(manager);
        GameObject[] chairs = FindModels("wooden_chair");
        manager.SetChair(chairs.Length > 0 ? chairs[0] : null);
        manager.SetConstruction(
            new[] { TexturedPrefab("Bricks_Rubble"), TexturedPrefab("Construction_Rubble") },
            TexturedPrefab("Wooden_Beam"), TexturedPrefab("Fireplace"));
        EditorUtility.SetDirty(manager);

        // Dekorácie okolo stavieb podľa frakcie (len predmety rozumnej veľkosti: bez šálok, koláčov a kľúčov)
        manager.SetDecor(
            FindModels("workbench", "crate_metal", "bulkhead", "corrugated", "electrical_box"),
            FindModels("sandbag", "storage_crate", "electrical_boxes", "trenches_equipment"),
            FindModels("storage_crate"),
            FindModels("storage_crate"));
        EditorUtility.SetDirty(manager);
        Debug.Log($"PlotManager: {ruins.Count} modelov ruín z {BuildingModelsFolder}.");
    }

    // Modely zo Stavby_do_hry, ktorých názov obsahuje niektoré z kľúčových slov
    static GameObject[] FindModels(params string[] keys)
    {
        var found = new List<GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { BuildingModelsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!NameHas(Path.GetFileNameWithoutExtension(path), keys)) continue;

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model != null) found.Add(model);
        }
        return found.ToArray();
    }

    [MenuItem("Progeny/Testy a pomocné/Zničiť najbližšiu stavbu (za behu)", false, 940)]
    static void DestroyNearestBuilding()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Funguje len za behu hry."); return; }

        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        if (player == null) return;

        BuildPlot best = null;
        float bestDistance = float.MaxValue;
        foreach (BuildPlot plot in BuildPlot.All)
        {
            if (plot.State != PlotState.Built) continue;
            float d = (plot.transform.position - player.transform.position).sqrMagnitude;
            if (d < bestDistance) { best = plot; bestDistance = d; }
        }

        if (best != null) best.DestroyBuilding();
        else Debug.LogWarning("Nie je žiadna postavená stavba.");
    }

    [MenuItem("Progeny/Testy a pomocné/Zmazať uložený stav sveta", false, 941)]
    static void DeleteWorldSave()
    {
        int deleted = 0;
        for (int slot = 1; slot <= WorldSave.SlotCount; slot++)
            if (File.Exists(WorldSave.PathFor(slot))) { File.Delete(WorldSave.PathFor(slot)); deleted++; }
        if (deleted > 0)
        {
            Debug.Log($"Uložený stav sveta zmazaný ({WorldSave.FilePath}).");
        }
        else Debug.Log("Nie je čo mazať, uložený stav sveta neexistuje.");
    }
}
