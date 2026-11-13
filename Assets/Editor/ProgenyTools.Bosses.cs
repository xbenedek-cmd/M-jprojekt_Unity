using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Bossovia na pevných miestach: každý boss dostane svoju lokáciu (BossPost) a stojí tam po celú hru.</summary>
public static partial class ProgenyTools
{
    [MenuItem("Progeny/Démoni/Bossovia: pevné miesta", false, 501)]
    static void PlaceBosses()
    {
        // Lord Raizen (Klan Blesku): Skalná oblasť Divočiny (bleskové kryštály). Ďalších bossov doplniť sem s ich lokalitou.
        GameObject wilderness = GameObject.Find("Divocina");
        if (wilderness == null)
        {
            Debug.LogError("Najprv vytvor Divočinu: Progeny → Divočiny → Vytvoriť všetkých 5 divočín.");
            return;
        }

        int placed = 0;
        Vector3 rocks = wilderness.transform.position + new Vector3(60f, 0f, 50f);   // stred Skalnej oblasti (pozri CreateWilderness)
        placed += PlaceBoss(DemonRoster.LightningBossName, rocks + new Vector3(0f, 0f, -6f), "Skalná oblasť") ? 1 : 0;

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Bossovia na pevných miestach: {placed}. Ulož scénu.");
    }

    static bool PlaceBoss(string displayName, Vector3 position, string postName)
    {
        foreach (Damageable d in Object.FindObjectsByType<Damageable>(FindObjectsInactive.Include))
        {
            if (d.displayName != displayName || d.GetComponent<DemonEnemy>() == null) continue;

            if (TryGetGroundY(position, out float groundY)) position.y = groundY;

            Undo.RecordObject(d.transform, "Presun bossa");
            d.transform.position = position;

            BossPost post = d.GetComponent<BossPost>();
            if (post == null) post = Undo.AddComponent<BossPost>(d.gameObject);
            post.SetHome(position, postName);
            EditorUtility.SetDirty(post);
            return true;
        }

        Debug.LogWarning($"Boss '{displayName}' sa v scéne nenašiel (vytvor ho cez Vytvoriť 10 rôznych démonov).");
        return false;
    }
}
