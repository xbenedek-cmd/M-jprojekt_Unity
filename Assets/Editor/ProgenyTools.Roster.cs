using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Zoznam démonov (Assets/Data/DemonRoster.asset) a jeho generátor: vytvorí 10 rôznych démonov
/// (všetky klany a všetky triedy) v kruhu okolo hráča.
/// </summary>
public static partial class ProgenyTools
{
    const string RosterRootName = "DemonRoster";

    static DemonRoster LoadOrCreateRoster(bool forceDefaults)
    {
        DemonRoster roster = AssetDatabase.LoadAssetAtPath<DemonRoster>(DemonRoster.AssetPath);
        if (roster != null && roster.entries.Count > 0 && !forceDefaults)
        {
            // Už uložený zoznam nemá bossa: doplní sa na koniec (ostatné záznamy ostanú, ako ich upravil hráč)
            if (FindBossEntry(roster) == null)
            {
                roster.entries.Add(DemonRoster.BuildLightningBoss());
                EditorUtility.SetDirty(roster);
                AssetDatabase.SaveAssets();
            }
            // Bossovia ďalších klanov (Kuro, Gora, Ignis, Carmilla): doplnia sa, ak chýbajú
            bool addedBosses = false;
            foreach (DemonEntry boss in DemonRoster.BuildClanBosses())
                if (!roster.entries.Exists(x => x.displayName == boss.displayName)) { roster.entries.Add(boss); addedBosses = true; }
            if (addedBosses) { EditorUtility.SetDirty(roster); AssetDatabase.SaveAssets(); }
            return roster;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Data")) AssetDatabase.CreateFolder("Assets", "Data");

        bool isNew = roster == null;
        if (isNew) roster = ScriptableObject.CreateInstance<DemonRoster>();
        roster.entries = DemonRoster.BuildDefaults();

        if (isNew) AssetDatabase.CreateAsset(roster, DemonRoster.AssetPath);
        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        return roster;
    }

    [MenuItem("Progeny/Démoni/Obnoviť zoznam démonov (predvolených 10)", false, 510)]
    static void ResetDemonRoster()
    {
        DemonRoster roster = LoadOrCreateRoster(true);
        Selection.activeObject = roster;
        Debug.Log($"Zoznam démonov obnovený: {roster.entries.Count} záznamov ({DemonRoster.AssetPath}).");
    }

    [MenuItem("Progeny/Démoni/Vytvoriť 10 rôznych démonov (zo zoznamu)", false, 511)]
    static void SpawnDemonRoster()
    {
        PrepareRaizenBoss(false);   // model + animácie bossa (ak ešte nie sú)
        DemonRoster roster = LoadOrCreateRoster(false);
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        Vector3 center = player != null ? player.transform.position : Vector3.zero;

        // Predtým vygenerovaný zoznam sa nahradí (nevznikajú duplikáty pri opakovanom spustení)
        GameObject old = GameObject.Find(RosterRootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject root = new GameObject(RosterRootName);
        Undo.RegisterCreatedObjectUndo(root, "Create Demon Roster");

        int count = roster.entries.Count;
        int lesser = 0, mid = 0, higher = 0;
        System.Collections.Generic.HashSet<FactionId> clans = new System.Collections.Generic.HashSet<FactionId>();

        for (int i = 0; i < count; i++)
        {
            DemonEntry entry = roster.entries[i];

            // Kruh okolo hráča, tri rôzne polomery, aby sa nezlievali
            float angle = i * Mathf.PI * 2f / Mathf.Max(1, count);
            float radius = 12f + (i % 3) * 3f;
            Vector3 position = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

            SpawnRosterDemon(root.transform, i, entry, position);

            clans.Add(entry.clan);
            if (entry.tier == DemonTier.Lesser) lesser++;
            else if (entry.tier == DemonTier.Mid) mid++;
            else higher++;
        }

        EnsurePlayerComponents(player);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;

        Debug.Log($"Vytvorených {count} démonov: nižší {lesser}, strední {mid}, vyšší {higher}; klany: {clans.Count}. Ulož scénu.");
    }

    static void SpawnRosterDemon(Transform parent, int index, DemonEntry entry, Vector3 position)
    {
        string name = $"Demon_{index + 1:00}_{entry.displayName}";

        // Základ: NPC s kapsulou na zemi, identitou (druh, klan) a pohybom (rovnako ako testovacie NPC)
        Spawn(parent, name, entry.Kind, entry.clan, position, false);
        Transform t = parent.Find(name);
        if (t == null) return;

        GameObject go = t.gameObject;
        SetupCombat(go);   // Damageable + DemonEnemy (+ znaky pre vyššieho démona)

        Damageable d = go.GetComponent<Damageable>();
        d.displayName = entry.displayName;
        d.team = CombatTeam.Demon;
        d.maxHealth = entry.maxHealth;
        d.moneyReward = entry.moneyReward;
        d.immuneToNormalWeapons = entry.tier >= DemonTier.Higher;
        // Nezraniteľný démon musí mať slabinu, inak by ho nešlo zabiť
        d.weakness = entry.weakness == DamageKind.Normal && d.immuneToNormalWeapons ? DamageKind.Fire : entry.weakness;

        DemonEnemy enemy = go.GetComponent<DemonEnemy>();
        enemy.tier = entry.tier;
        enemy.clan = entry.clan;
        enemy.attackDamage = entry.attackDamage;
        enemy.moveSpeed = entry.moveSpeed;

        NPCPerception npc = go.GetComponent<NPCPerception>();
        npc.faction = entry.clan;
        npc.monsterColor = entry.tint;
        // Vyšší démon sa cez deň ukrýva v ľudskej maske (kým ho hráč neskúma): farba masky podľa klanu
        if (entry.tier >= DemonTier.Higher) npc.humanColor = entry.disguiseColor;
        // Vlastný model (vlk, krkavec, boss ...) má prednosť pred spoločným modelom démona
        if (entry.model != null) npc.higherDemonBodyOverride = entry.model;
        if (entry.animationSet != null) npc.demonAnimationSet = entry.animationSet;
        if (entry.keepModelMaterials) { npc.keepModelMaterials = true; npc.demonMaterial = entry.material; }

        // Vyšší démon a boss: ľudská maska musí mať normálnu výšku (nesmie sa líšiť od ľudí), preto sa zväčšuje len démonická podoba
        float scale = Mathf.Max(0.5f, entry.scale);
        if (entry.tier >= DemonTier.Higher)
        {
            go.transform.localScale = Vector3.one;
            npc.demonVisualScale = scale;
        }
        else go.transform.localScale = Vector3.one * scale;

        EditorUtility.SetDirty(d);
        EditorUtility.SetDirty(enemy);
        EditorUtility.SetDirty(npc);
    }
}
