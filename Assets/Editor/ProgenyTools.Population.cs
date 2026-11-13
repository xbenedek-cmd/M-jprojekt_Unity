using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Obyvatelia zón (ZonePopulation): pool postáv a rozpočet pre mesto odboja a Megakorporáciu.</summary>
public static partial class ProgenyTools
{
    [MenuItem("Progeny/Obyvatelia/Mesto odboja", false, 400)]
    static void BuildCityPopulation()
    {
        GameObject gate = GameObject.Find("Portal_DoMegakorporacie");
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        Vector3 center = gate != null ? gate.transform.position : (player != null ? player.transform.position : Vector3.zero);
        Vector3 size = new Vector3(170f, 40f, 170f);

        BuildCityPopulationAt("Obyvatelia_MestoOdboja", WorldNames.StartCity, center, size, 18);
    }

    static void BuildCityPopulationAt(string objectName, string zoneName, Vector3 center, Vector3 size, int budget)
    {
        RemovePopulationZone(objectName);
        GameObject zoneGo = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(zoneGo, "Create City Population");
        zoneGo.transform.position = center;
        BoxCollider box = zoneGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        Bounds bounds = new Bounds(center, size);

        // Existujúci civilisti a vojaci v zóne sa stanú súčasťou poolu
        var civilians = new List<GameObject>();
        var soldiers = new List<GameObject>();
        foreach (NPCPerception npc in Object.FindObjectsByType<NPCPerception>(FindObjectsInactive.Include))
        {
            if (!bounds.Contains(npc.transform.position)) continue;
            if (npc.GetComponent<Healer>() != null || npc.GetComponent<MerchantNPC>() != null) continue;

            if (npc.GetComponent<SoldierAI>() != null) soldiers.Add(npc.gameObject);
            else if (npc.kind == NPCKind.Civilian && npc.GetComponent<DemonEnemy>() == null) civilians.Add(npc.gameObject);
        }

        // Doplnenie poolu na 36 civilistov (~30 % odboj), rozmiestnených okolo stredu mesta; vypnutí, zapne ich rozpočet
        System.Random rnd = new System.Random(11);
        Transform pool = new GameObject("PoolCivilistov").transform;
        pool.SetParent(zoneGo.transform);
        int existing = civilians.Count;
        for (int i = existing; i < 36; i++)
        {
            Vector3 pos = center + new Vector3((float)(rnd.NextDouble() * 2 - 1) * size.x * 0.4f, 0f, (float)(rnd.NextDouble() * 2 - 1) * size.z * 0.4f);
            FactionId faction = rnd.NextDouble() < 0.3 ? FactionId.Rebels : FactionId.None;
            string name = $"Civilista_{i + 1}";
            Spawn(pool, name, NPCKind.Civilian, faction, pos, false);
            Transform t = pool.Find(name);
            if (t == null) continue;

            SetupCombat(t.gameObject);
            civilians.Add(t.gameObject);
        }

        // Vypnuté šablóny, z ktorých sa za behu klonujú noví obyvatelia (rast populácie)
        Spawn(pool, "Sablona_Civilista", NPCKind.Civilian, FactionId.None, center, false);
        GameObject civilianTemplate = pool.Find("Sablona_Civilista")?.gameObject;
        if (civilianTemplate != null) SetupCombat(civilianTemplate);
        SpawnSoldier(pool, "Sablona_Vojak", center);
        GameObject soldierTemplate = pool.Find("Sablona_Vojak")?.gameObject;

        FinishPopulation(zoneGo, zoneName, budget, soldiers.Count, civilianTemplate, soldierTemplate, civilians, soldiers, existing, soldiers.Count);
    }

    [MenuItem("Progeny/Obyvatelia/Megakorporácia", false, 401)]
    static void BuildCorpPopulation()
    {
        GameObject ground = GameObject.Find("Zem_Megakorporacia");
        GameObject complex = GameObject.Find("CorpCity") ?? GameObject.Find("CorpComplex");
        if (complex == null)
        {
            BuildCorpCity();   // komplex ešte nestojí: postaví sa nový
            complex = GameObject.Find("CorpCity");
            ground = GameObject.Find("Zem_Megakorporacia");
        }
        if (ground == null || complex == null)
        {
            Debug.LogError("Megakorporácia sa nepodarila vytvoriť: spusti Progeny → Megakorporácia → Postaviť mesto.");
            return;
        }

        Vector3 center = ground.transform.position + Vector3.up * 0.5f;
        Vector3 size = new Vector3(ground.transform.localScale.x, 40f, ground.transform.localScale.z);

        RemovePopulationZone("Obyvatelia_Megakorporacia");
        GameObject zoneGo = new GameObject("Obyvatelia_Megakorporacia");
        Undo.RegisterCreatedObjectUndo(zoneGo, "Create Corp Population");
        zoneGo.transform.position = center;
        BoxCollider box = zoneGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        Bounds bounds = new Bounds(center, size);

        var soldiers = new List<GameObject>();
        foreach (SoldierAI s in Object.FindObjectsByType<SoldierAI>(FindObjectsInactive.Include))
            if (bounds.Contains(s.transform.position)) soldiers.Add(s.gameObject);

        int baseCount = soldiers.Count;
        Physics.SyncTransforms();
        System.Random rnd = new System.Random(13);
        for (int i = baseCount; i < baseCount + 8; i++)
        {
            Vector3 pos = center + new Vector3((float)(rnd.NextDouble() * 2 - 1) * 90f, 0f, (float)(rnd.NextDouble() * 2 - 1) * 6f);
            string name = $"Soldier_{i + 1}";
            SpawnSoldier(complex.transform, name, pos);
            Transform t = complex.transform.Find(name);
            if (t != null) soldiers.Add(t.gameObject);
        }

        SpawnSoldier(complex.transform, "Sablona_Vojak", center);
        GameObject soldierTemplate = complex.transform.Find("Sablona_Vojak")?.gameObject;

        // Rozpočet 6 vojakov naraz z poolu 14
        FinishPopulation(zoneGo, "Megakorporácia", 0, 6, null, soldierTemplate, new List<GameObject>(), soldiers, 0, baseCount);
    }

    static void RemovePopulationZone(string name)
    {
        GameObject old = GameObject.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old);
    }

    // Pridá ZonePopulation a vypne doplnených členov poolu (adoptovaní ostávajú; zapne ich rozpočet za behu)
    static void FinishPopulation(GameObject zoneGo, string zoneName, int civilianBudget, int soldierBudget, GameObject civilianTemplate, GameObject soldierTemplate,
        List<GameObject> civilians, List<GameObject> soldiers, int adoptedCivilians, int adoptedSoldiers)
    {
        for (int i = adoptedCivilians; i < civilians.Count; i++) civilians[i].SetActive(false);
        for (int i = adoptedSoldiers; i < soldiers.Count; i++) soldiers[i].SetActive(false);

        ZonePopulation population = zoneGo.AddComponent<ZonePopulation>();
        population.Configure(zoneName, civilianBudget, soldierBudget, civilians, soldiers, civilianTemplate, soldierTemplate);
        if (civilianTemplate != null) civilianTemplate.SetActive(false);
        if (soldierTemplate != null) soldierTemplate.SetActive(false);
        EditorUtility.SetDirty(population);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = zoneGo;
        Debug.Log($"Obyvatelia zóny '{zoneName}': pool {civilians.Count} civilistov (naraz {civilianBudget}) a {soldiers.Count} vojakov (naraz {soldierBudget}). Ulož scénu.");
    }
}
