using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Divoká svorka: démoni Klanu Divočiny (vlk Okami, tank Jukai) zo zoznamu démonov v každej z piatich divočín.</summary>
public static partial class ProgenyTools
{
    // (stred lokality voči zóne, počet vlkov, počet tankov) podľa zóny; zóna s veľa stromami a kryštálmi je nebezpečnejšia
    static (Vector3 offset, int wolves, int tanks)[] PackLayout(string key)
    {
        int w = key == "Les" ? 2 : key == "Kanon" ? 0 : key == "Mociare" ? 1 : 0;   // doplnok vlkov
        int tk = key == "Kanon" ? 1 : key == "Pustatina" ? 1 : 0;                   // doplnok tankov
        WildAnchors(key, out Vector3 packA, out Vector3 packB, out Vector3 packC);
        return new[]
        {
            (packA, 3 + w, 1 + (key == "Les" ? 1 : 0)),
            (packB, 2 + (key == "Kanon" ? 2 : 0), 1 + tk),
            (packC, 2 + (key == "Pustatina" ? 2 : 0), tk),
            (new Vector3(-20f, 0f, -10f), 2 + (key == "Mociare" ? 1 : 0), 0)
        };
    }

    [MenuItem("Progeny/Divočiny/Divoká svorka (démoni vo všetkých divočinách)", false, 120)]
    static void CreateWildPack()
    {
        DemonRoster roster = LoadOrCreateRoster(false);
        DemonEntry wolf = null, tank = null;
        foreach (DemonEntry e in roster.entries)
        {
            if (e.clan != FactionId.WildClan || e.tier == DemonTier.Boss) continue;
            if (e.tier == DemonTier.Lesser && wolf == null) wolf = e;
            else if (e.tier == DemonTier.Mid && tank == null) tank = e;
        }
        if (wolf == null)
        {
            Debug.LogError("V zozname démonov nie je nižší démon Klanu Divočiny.");
            return;
        }

        Physics.SyncTransforms();
        int total = 0, zones = 0, index = 0;

        foreach (WildTheme theme in WildThemes)
        {
            GameObject wild = GameObject.Find(theme.RootName);
            if (wild == null) continue;   // zóna ešte nie je vytvorená

            string packName = theme.key == "Divocina" ? "DivokaSvorka" : "DivokaSvorka_" + theme.key;
            GameObject old = GameObject.Find(packName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            GameObject root = new GameObject(packName);
            Undo.RegisterCreatedObjectUndo(root, "Create Wild Pack");
            root.transform.SetParent(wild.transform);

            Vector3 c = wild.transform.position;
            System.Random rnd = new System.Random(21 + zones);

            foreach (var pack in PackLayout(theme.key))
            {
                for (int i = 0; i < pack.wolves; i++, total++)
                    SpawnRosterDemon(root.transform, index++, wolf, Around(rnd, c + pack.offset, 10f));
                if (tank == null) continue;
                for (int i = 0; i < pack.tanks; i++, total++)
                    SpawnRosterDemon(root.transform, index++, tank, Around(rnd, c + pack.offset, 6f));
            }
            zones++;
        }

        if (zones == 0)
        {
            Debug.LogError("Najprv vytvor divočiny: Progeny → Divočiny → Vytvoriť všetkých 5 divočín.");
            return;
        }

        ConformAllWilds();   // démoni na povrch terénu
        EnsurePlayerComponents(Object.FindAnyObjectByType<PlayerStats>());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Divoká svorka: {total} démonov v {zones} divočinách. Ulož scénu.");
    }
}
