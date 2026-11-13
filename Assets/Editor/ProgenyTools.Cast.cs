using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Postavy z Postavy.txt: príbehové NPC (Ren, Kazumi, Vance, Kage, Mei – StoryNpc) a bossovia klanov (Kuro, Gora, Ignis, Carmilla)
/// na pevných miestach svojich zón. Raizen je už pripravený, Yumi je lekárka, Arakani prehovára v noci. Opakované spustenie postavy nahradí.
/// </summary>
public static partial class ProgenyTools
{

    // Výška povrchu: terén (divočiny) alebo fyzika (mesto), inak daná výška
    static float SurfaceHeight(Vector3 position, float fallback)
    {
        foreach (Terrain t in Terrain.activeTerrains)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (position.x >= o.x && position.x <= o.x + s.x && position.z >= o.z && position.z <= o.z + s.z)
                return t.SampleHeight(position) + o.y;
        }
        position.y = fallback;
        return TryGetGroundY(position, out float y) ? y : fallback;
    }

    static bool GroundBoundsOf(string groundName, out Bounds bounds)
    {
        bounds = new Bounds();
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            if (r.gameObject.name == groundName) { bounds = ZoneLoader.GroundBounds(r); return true; }
        return false;
    }

    static void RemoveByName(string objectName)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t != null && t.name == objectName && !EditorUtility.IsPersistent(t)) Undo.DestroyObjectImmediate(t.gameObject);
    }

    [MenuItem("Progeny/Postavy/Pridať postavy z Postavy.txt (príbehové NPC + bossovia klanov)", false, 600)]
    static void AddCastToWorld()
    {
        int npcs = AddStoryNpcs();
        int bosses = AddClanBosses();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Postavy: príbehových NPC {npcs}, bossov klanov {bosses}. Ulož scénu. (Raizen: menu Démoni → Bossovia: pevné miesta; Yumi je lekárka; Arakani hovorí v noci.)");
    }

    static int AddStoryNpcs()
    {
        // Mesto Centrum: Kazumi a Mei na civilnej strane (západ), Vance a Kage na strane odboja (východ); Ren v Megakorporácii
        var placements = new List<(CharId id, string ground, string rootName, Vector3 offset)>
        {
            (CharId.Kazumi, "Zem_Centrum", "Mesto_Centrum", new Vector3(-40f, 0f, 17f)),
            (CharId.Mei, "Zem_Centrum", "Mesto_Centrum", new Vector3(-40f, 0f, -17f)),
            (CharId.Vance, "Zem_Centrum", "Mesto_Centrum", new Vector3(40f, 0f, 17f)),
            (CharId.Kage, "Zem_Centrum", "Mesto_Centrum", new Vector3(26f, 0f, -17f)),
            (CharId.Ren, "Zem_Megakorporacia", "CorpCity", new Vector3(12f, 0f, -40f))
        };

        int added = 0;
        foreach (var p in placements)
        {
            RemoveByName("Postava_" + p.id);
            GameObject root = GameObject.Find(p.rootName);
            if (root == null || !GroundBoundsOf(p.ground, out Bounds b))
            {
                Debug.LogWarning($"Postavy: {p.id} sa nepridal, chýba {p.rootName} / {p.ground} (najprv vytvor zónu).");
                continue;
            }

            Vector3 pos = b.center + p.offset;
            pos.y = SurfaceHeight(pos, b.max.y);

            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Undo.RegisterCreatedObjectUndo(go, "Príbehová postava");
            go.name = "Postava_" + p.id;
            go.transform.SetParent(root.transform);
            go.transform.position = pos + Vector3.up * 1f;
            var npc = go.AddComponent<StoryNpc>();
            npc.Character = p.id;
            EditorUtility.SetDirty(npc);
            added++;
        }
        return added;
    }

    static int AddClanBosses()
    {
        PrepareRaizenBoss(false);   // spoločný model a animácie démonov
        DemonRoster roster = LoadOrCreateRoster(false);   // doplní chýbajúcich bossov klanov

        var plan = new List<(string name, string rootName, string ground, Vector3 offset, string post)>
        {
            (DemonRoster.ShadowBossName, "Divocina", "Zem_Divocina", new Vector3(-55f, 0f, 45f), "Tieňový háj"),
            (DemonRoster.WildBossName, "Divocina_Les", "Zem_Les", new Vector3(0f, 0f, 20f), "Srdce lesa"),
            (DemonRoster.AshBossName, "Divocina_Pustatina", "Zem_Pustatina", new Vector3(0f, 0f, 20f), "Popolavé ruiny"),
            (DemonRoster.BloodBossName, "Divocina_Mociare", "Zem_Mociare", new Vector3(0f, 0f, 20f), "Krvavé močiare")
        };

        int placed = 0;
        for (int i = 0; i < plan.Count; i++)
        {
            var step = plan[i];
            DemonEntry entry = roster.entries.Find(e => e.displayName == step.name);
            GameObject root = GameObject.Find(step.rootName);
            if (entry == null || root == null || !GroundBoundsOf(step.ground, out Bounds b))
            {
                Debug.LogWarning($"Boss '{step.name}' sa nepridal: chýba záznam v zozname alebo zóna {step.rootName}.");
                continue;
            }

            // Starý boss s rovnakým menom sa nahradí
            foreach (Damageable old in Object.FindObjectsByType<Damageable>(FindObjectsInactive.Include))
                if (old != null && old.displayName == step.name) Undo.DestroyObjectImmediate(old.gameObject);

            Vector3 pos = b.center + step.offset;
            pos.y = SurfaceHeight(pos, b.max.y);
            SpawnRosterDemon(root.transform, 100 + i, entry, pos);
            if (PlaceBoss(step.name, pos, step.post)) placed++;
        }

        ApplyRosterToSceneDemons();   // model, sada a materiál démonov podľa záznamov
        return placed;
    }
}
