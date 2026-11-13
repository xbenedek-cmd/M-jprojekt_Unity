using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Popolová Hradba (štartovné mesto odboja), portály a portálové nádvorie v Centre:
///  - kamenný oblúk s runami okolo každého portálu (namiesto holej svietiacej dosky),
///  - Centrum je uzol: všetky portály (Hradba, Megakorporácia, Kaňon, Les, Pustatina, Močiare, Bane) stoja v polkruhu na jednom nádvorí,
///  - rozšírenie mapy Hradby: nová plocha s tromi domčekmi a lesom na polovici (pol mesto, pol divočina).
/// Nástroje nič nemažú; opakované spustenie neduplikuje oblúky ani nádvorie.
/// </summary>
public static partial class ProgenyTools
{
    // ======== Pomocné ========
    static Material HbMaterial(Color color, float emission = 0f, float alpha = 1f)
    {
        var m = new Material(Shader.Find("Standard")) { color = new Color(color.r, color.g, color.b, alpha) };
        m.SetFloat("_Glossiness", 0.2f);
        if (emission > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * emission);
        }
        return m;
    }

    static GameObject HbPart(Transform parent, string name, PrimitiveType type, Vector3 localPos, Vector3 scale, Quaternion rot, Material mat, bool collider = false)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = rot;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider)
        {
            Collider c = g.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }
        return g;
    }

    static Transform HbFind(string name)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t != null && t.name == name) return t;
        return null;
    }

    // ======== Portály: kamenný oblúk ========
    /// <summary>
    /// Okolo portálu postaví kamenný oblúk: dva piliere s runami, preklad so svietiacim stredovým kameňom, podstavec a kruh rún na zemi.
    /// Prvky sú v koreni "Obluk" (zrušená mierka brány, rozmery sú v metroch), takže sa s bránou posúvajú a otáčajú.
    /// </summary>
    static void DecoratePortal(PortalGate gate, Color glow) => DecoratePortalV2(gate, glow);

    [MenuItem("Progeny/Mesto/Portály: kamenné oblúky na všetky brány", false, 298)]
    static void DecorateAllPortals()
    {
        int count = 0;
        foreach (PortalGate g in Object.FindObjectsByType<PortalGate>(FindObjectsInactive.Include))
        {
            if (g == null || g.transform.Find("Obluk") != null) continue;
            Renderer r = g.GetComponent<Renderer>();
            Color c = r != null && r.sharedMaterial != null ? r.sharedMaterial.color : new Color(0.3f, 0.7f, 1f);
            c.a = 1f;
            DecoratePortal(g, c);
            count++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Portály: kamenný oblúk pridaný {count} bránam. Ulož scénu.");
    }

    // ======== Centrum: portálové nádvorie ========
    private static readonly string[] HubGates =
    {
        "Portal_zCentra",             // späť do Popolovej Hradby
        "Portal_DoMegakorporacie",
        "Portal_DoKanon",
        "Portal_DoLes",
        "Portal_DoPustatina",
        "Portal_DoMociare",
        "Portal_DoBane",
        "Portal_Centrum_DoDivociny",   // vstup do Divočiny (návrat ostáva do Hradby)
    };

    [MenuItem("Progeny/Mesto/Centrum: portálové nádvorie (všetky portály)", false, 312)]
    static void BuildPortalHub()
    {
        Transform centrum = HbFind("Mesto_Centrum");
        if (centrum == null) { Debug.LogError("Zóna Centrum neexistuje. Najprv: Progeny → Mesto → Zóna Centrum."); return; }

        Transform ground = centrum.Find("Zem_Centrum");
        Vector3 c = ground != null ? ground.position + Vector3.up * 0.5f : centrum.position;
        Vector3 plaza = c + new Vector3(0f, 0f, -126f);
        if (Physics.Raycast(plaza + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore)) plaza.y = hit.point.y;

        Transform old = centrum.Find("PortalNadvorie");
        if (old != null)
        {
            // Brány z predošlého nádvoria sa najprv vyberú, aby sa so starým nádvorím nezmazali
            foreach (PortalGate g in old.GetComponentsInChildren<PortalGate>(true)) g.transform.SetParent(centrum, true);
            Object.DestroyImmediate(old.gameObject);
        }
        var yard = new GameObject("PortalNadvorie").transform;
        Undo.RegisterCreatedObjectUndo(yard.gameObject, "Portálové nádvorie");
        yard.SetParent(centrum, true);
        yard.position = plaza;

        // Dlažba nádvoria, vnútorný kruh rún a stredový stĺp
        Material paving = HbMaterial(new Color(0.2f, 0.2f, 0.24f));
        Material rune = HbMaterial(new Color(0.35f, 0.75f, 1f), 1.8f);
        HbPart(yard, "Dlazba", PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0f), new Vector3(40f, 0.04f, 40f), Quaternion.identity, paving);
        for (int i = 0; i < 28; i++)
        {
            float a = i * Mathf.PI * 2f / 28f;
            HbPart(yard, "Runa", PrimitiveType.Cube, new Vector3(Mathf.Cos(a), 0.09f, Mathf.Sin(a)) * 8f, new Vector3(0.5f, 0.04f, 0.14f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f), rune);
        }
        HbPart(yard, "StredovyStlp", PrimitiveType.Cylinder, new Vector3(0f, 1.6f, 0f), new Vector3(0.7f, 1.6f, 0.7f), Quaternion.identity, HbMaterial(new Color(0.12f, 0.12f, 0.16f)), true);
        HbPart(yard, "StredovyKristal", PrimitiveType.Cube, new Vector3(0f, 3.7f, 0f), new Vector3(0.7f, 0.7f, 0.7f), Quaternion.Euler(45f, 45f, 0f), rune);
        HbLampRing(yard, plaza, 21f, 160f, 380f, 9);

        // Divočina: v Centre vstup do Divočiny (jej návrat ostáva do Hradby); ostatné brány sa presunú z mesta
        EnsureLinkedGate("Portal_Centrum_DoDivociny", "Portal_zDivociny", "Divočina", yard);

        // Brány v polkruhu otvorenom k avenue (sever); pri bráne vždy stojí jej oblúk a vstup smeruje do stredu nádvoria
        var gates = new List<PortalGate>();
        foreach (string gateName in HubGates)
        {
            PortalGate gate = FindGateObject(gateName);
            if (gate == null) { Debug.LogWarning($"Portálové nádvorie: brána {gateName} neexistuje, preskakujem."); continue; }
            gates.Add(gate);
        }
        ArrangeArc(gates, yard, plaza, 15f, 200f, 340f);

        BuildHradbaPortals();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Portály: Centrum má {gates.Count} brán na nádvorí, Popolová Hradba má vlastné nádvorie so všetkými bránami. V ostatných zónach ostáva len jedna brána späť. Ulož scénu.");
    }

    // Brány v oblúku okolo bodu, otočené do stredu; oblúk (kamenný) sa pridá každej
    static void ArrangeArc(List<PortalGate> gates, Transform parent, Vector3 plaza, float radius, float startDeg, float endDeg)
    {
        for (int i = 0; i < gates.Count; i++)
        {
            PortalGate gate = gates[i];
            float deg = Mathf.Lerp(startDeg, endDeg, gates.Count > 1 ? i / (float)(gates.Count - 1) : 0.5f);
            float rad = deg * Mathf.Deg2Rad;
            Vector3 pos = plaza + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
            if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit h, 100f, ~0, QueryTriggerInteraction.Ignore)) pos.y = h.point.y;
            Vector3 inward = plaza - pos;
            inward.y = 0f;

            Undo.RecordObject(gate.transform, "Presun portálu");
            gate.transform.SetParent(parent, true);
            gate.transform.SetPositionAndRotation(pos + Vector3.up * 1.5f, Quaternion.LookRotation(inward));

            Renderer r = gate.GetComponent<Renderer>();
            Color col = r != null && r.sharedMaterial != null ? r.sharedMaterial.color : new Color(0.3f, 0.7f, 1f);
            col.a = 1f;
            DecoratePortal(gate, col);
            EditorUtility.SetDirty(gate);
        }
    }

    private static int hbCreated;

    // Vytvorí (ak chýba) bránu, ktorá vedie do cieľovej brány v inej zóne
    static PortalGate EnsureLinkedGate(string name, string targetName, string zone, Transform parent)
    {
        PortalGate gate = FindGateObject(name);
        PortalGate target = FindGateObject(targetName);
        if (target == null) { Debug.LogWarning($"Brána {name}: cieľ {targetName} neexistuje (zóna ešte nie je vytvorená)."); return gate; }

        if (gate == null)
        {
            Color c = new Color(0.3f, 0.7f, 1f);
            Renderer tr = target.GetComponent<Renderer>();
            if (tr != null && tr.sharedMaterial != null) { c = tr.sharedMaterial.color; c.a = 1f; }
            gate = BuildPortal(parent, name, parent.position, c, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(gate.gameObject, "Brána " + name);
            hbCreated++;
        }
        gate.Link(target, zone);
        EditorUtility.SetDirty(gate);
        return gate;
    }

    // Popolová Hradba: vlastné nádvorie so všetkými bránami (Centrum, Divočina, Megakorporácia, Kaňon, Les, Pustatina, Močiare, Bane)
    static void BuildHradbaPortals()
    {
        PortalGate toCentrum = FindGateObject("Portal_DoCentra");
        if (toCentrum == null) { Debug.LogWarning("Hradba: chýba Portal_DoCentra, nádvorie Hradby sa nepostaví. Spusti Portály: doplniť všetky chýbajúce."); return; }

        Transform cityPortals = HbFind("CityPortals");
        Transform root = HbFind("PortalNadvorieHradba");
        bool freshYard = root == null;
        int createdBefore = hbCreated;
        Vector3 anchor = toCentrum.transform.position - Vector3.up * 1.5f;
        Vector3 plaza = anchor + toCentrum.transform.forward * 22f;
        if (Physics.Raycast(plaza + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore)) plaza.y = hit.point.y;

        if (root == null)
        {
            root = new GameObject("PortalNadvorieHradba").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Nádvorie Hradby");
            if (cityPortals != null) root.SetParent(cityPortals, true);
            root.position = plaza;
        }
        else plaza = root.position;   // ručne posunuté nádvorie sa nemení

        float baseLampDeg = Mathf.Atan2((plaza - anchor).z, (plaza - anchor).x) * Mathf.Rad2Deg;
        if (root.Find("Dlazba") == null)
        {
            HbPart(root, "Dlazba", PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0f), new Vector3(40f, 0.04f, 40f), Quaternion.identity, HbMaterial(new Color(0.2f, 0.2f, 0.24f)));
            Material rune = HbMaterial(new Color(0.35f, 0.75f, 1f), 1.8f);
            for (int i = 0; i < 28; i++)
            {
                float a = i * Mathf.PI * 2f / 28f;
                HbPart(root, "Runa", PrimitiveType.Cube, new Vector3(Mathf.Cos(a), 0.09f, Mathf.Sin(a)) * 8f, new Vector3(0.5f, 0.04f, 0.14f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f), rune);
            }
            HbLampRing(root, plaza, 21f, baseLampDeg - 100f, baseLampDeg + 100f, 7);
        }

        var gates = new List<PortalGate> { toCentrum, FindGateObject("Portal_DoDivociny") };
        gates.Add(EnsureLinkedGate("Portal_Hradba_DoMegakorporacie", "Portal_DoMesta", "Megakorporácia", root));
        gates.Add(EnsureLinkedGate("Portal_Hradba_DoKanon", "Portal_zKanon", "Bleskový kaňon", root));
        gates.Add(EnsureLinkedGate("Portal_Hradba_DoLes", "Portal_zLes", "Temný les", root));
        gates.Add(EnsureLinkedGate("Portal_Hradba_DoPustatina", "Portal_zPustatina", "Zrezavená pustatina", root));
        gates.Add(EnsureLinkedGate("Portal_Hradba_DoMociare", "Portal_zMociare", "Močiare", root));
        gates.Add(EnsureLinkedGate("Portal_Hradba_DoBane", "Portal_zBane", "Bane", root));
        gates.RemoveAll(g => g == null);

        // Oblúk otvorený smerom k pôvodnej bráne do Centra
        Vector3 away = plaza - anchor;
        float baseDeg = Mathf.Atan2(away.z, away.x) * Mathf.Rad2Deg;
        if (freshYard || hbCreated != createdBefore) ArrangeArc(gates, root, plaza, 16f, baseDeg - 75f, baseDeg + 75f);
        Debug.Log($"Hradba: nádvorie má {gates.Count} brán" + (hbCreated != createdBefore ? $" (doplnených {hbCreated - createdBefore})." : ", nič nechýbalo."));
    }

    // Ak je viac objektov rovnakého mena, použije sa ten, ktorý má cieľ
    static PortalGate FindGateObject(string name)
    {
        PortalGate best = null;
        foreach (PortalGate g in Object.FindObjectsByType<PortalGate>(FindObjectsInactive.Include))
        {
            if (g == null || g.gameObject.name != name) continue;
            if (best == null || (!best.HasDestination && g.HasDestination)) best = g;
        }
        return best;
    }

    // ======== Popolová Hradba: väčšia mapa ========
    [MenuItem("Progeny/Mesto/Popolová Hradba: rozšíriť mapu (pri označenom objekte)", false, 297)]
    static void ExpandStartCity()
    {
        // Kotva rozšírenia: označený objekt, inak miesto 110 m pred hráčom
        Vector3 anchor;
        Quaternion facing = Quaternion.identity;
        if (Selection.activeTransform != null) { anchor = Selection.activeTransform.position; facing = Quaternion.Euler(0f, Selection.activeTransform.eulerAngles.y, 0f); }
        else
        {
            PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
            if (player == null) { Debug.LogError("Označ objekt (stred nového územia) alebo vlož hráča do scény."); return; }
            anchor = player.transform.position + player.transform.forward * 110f;
            facing = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
        }
        if (Physics.Raycast(anchor + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)) anchor.y = hit.point.y;

        Transform old = HbFind("Hradba_Rozsirenie")?.transform;
        if (old != null) { Debug.LogWarning("Rozšírenie Hradby už existuje (Hradba_Rozsirenie). Zmaž ho, ak ho chceš postaviť nanovo."); return; }

        var root = new GameObject("Hradba_Rozsirenie").transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Rozšírenie Hradby");
        root.SetPositionAndRotation(anchor, facing);

        const float size = 180f;
        Material soil = HbMaterial(new Color(0.24f, 0.22f, 0.2f));
        GameObject slab = HbPart(root, "Zem_HradbaRozsirenie", PrimitiveType.Cube, new Vector3(0f, -0.25f, 0f), new Vector3(size, 0.5f, size), Quaternion.identity, soil, true);
        slab.layer = 0;

        // Dlažba mestskej polovice (západ) a lúka divočiny (východ): pol na pol
        HbPart(root, "DlazbaMesto", PrimitiveType.Cube, new Vector3(-size * 0.25f, 0.02f, 0f), new Vector3(size * 0.5f, 0.06f, size), Quaternion.identity, HbMaterial(new Color(0.3f, 0.3f, 0.33f)));
        HbPart(root, "TrávaDivocina", PrimitiveType.Cube, new Vector3(size * 0.25f, 0.02f, 0f), new Vector3(size * 0.5f, 0.06f, size), Quaternion.identity, HbMaterial(new Color(0.14f, 0.26f, 0.12f)));

        // Tri domčeky v mestskej polovici
        Vector3[] houses = { new Vector3(-62f, 0f, 48f), new Vector3(-30f, 0f, -10f), new Vector3(-66f, 0f, -52f) };
        for (int i = 0; i < houses.Length; i++)
        {
            Vector3 world = root.TransformPoint(houses[i]);
            Vector3 toCenter = root.TransformPoint(new Vector3(0f, 0f, 0f)) - world;
            toCenter.y = 0f;
            if (!PlaceHouseModel(root, true, i, world, toCenter, anchor.y, 12f))
            {
                // Bez modelu domu aspoň jednoduchý domček z kvádrov
                GameObject box = HbPart(root, "Domcek_" + (i + 1), PrimitiveType.Cube, houses[i] + Vector3.up * 2.5f, new Vector3(9f, 5f, 8f), Quaternion.identity, HbMaterial(new Color(0.55f, 0.48f, 0.4f)), true);
                HbPart(box.transform, "Strecha", PrimitiveType.Cube, new Vector3(0f, 0.62f, 0f), new Vector3(1.1f, 0.2f, 1.1f), Quaternion.identity, HbMaterial(new Color(0.3f, 0.15f, 0.12f)));
            }
        }

        // Cesta od domčekov k hranici s lesom a lampy pri domčekoch
        HbPart(root, "Cesta", PrimitiveType.Cube, new Vector3(-size * 0.25f, 0.06f, 0f), new Vector3(size * 0.5f, 0.05f, 4f), Quaternion.identity, HbMaterial(new Color(0.16f, 0.15f, 0.14f)));
        foreach (Vector3 hp in houses)
        {
            HbPart(root, "Chodnik", PrimitiveType.Cube, new Vector3(hp.x, 0.06f, hp.z * 0.5f), new Vector3(2.2f, 0.05f, Mathf.Abs(hp.z) + 0.1f), Quaternion.identity, HbMaterial(new Color(0.2f, 0.19f, 0.18f)));
            HbLamp(root, hp + new Vector3(7.5f, 0f, hp.z > 0 ? -4f : 4f), 4.0f);
        }
        for (int i = -3; i <= 3; i++) HbLamp(root, new Vector3(i * 24f - 20f, 0f, 3.4f), 4.2f);

        // Les na východnej polovici: skupiny "Smrek" (sekera ich za behu uzná za strom: RockGuard)
        GameObject firPrefab = FindTreeAsset("Fir_01_Plant") ?? FindTreeAsset("Fir_03_Medium");
        var rnd = new System.Random(2024);
        var forest = new GameObject("Les").transform;
        forest.SetParent(root, false);
        int trees = 0;
        for (int i = 0; i < 90 && firPrefab != null; i++)
        {
            Vector3 p = new Vector3((float)(8 + rnd.NextDouble() * (size * 0.5f - 16f)), 0f, (float)((rnd.NextDouble() * 2 - 1) * (size * 0.5f - 8f)));
            var group = new GameObject("Smrek").transform;
            group.SetParent(forest, false);
            group.localPosition = p;
            group.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(firPrefab, group);
            tree.transform.localPosition = Vector3.zero;
            tree.transform.localScale = Vector3.one * (0.85f + (float)rnd.NextDouble() * 0.45f);
            trees++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Popolová Hradba: pridaná plocha {size:0}×{size:0} m (mesto na západe, divočina na východe), 3 domčeky, {trees} stromov. Posuň objekt Hradba_Rozsirenie podľa potreby, aby nadväzoval na mesto. Ulož scénu.");
        Selection.activeTransform = root;
    }
}

public static partial class ProgenyTools
{
    /// <summary>
    /// Doplní chýbajúce brány v oboch mestách (Centrum aj Popolová Hradba): každá zóna (Divočina, Kaňon, Les, Pustatina, Močiare, Bane, Megakorporácia)
    /// musí mať bránu v Centre aj v Hradbe, v zónach je len návrat. Nič nemaže; oblúk sa prerovná len keď sa niečo pridá.
    /// </summary>
    [MenuItem("Progeny/Mesto/Portály: doplniť chýbajúce brány v mestách (Centrum aj Hradba)", false, 295)]
    static void EnsureCityGates()
    {
        int before = hbCreated;
        Transform centrum = HbFind("Mesto_Centrum");
        if (centrum != null)
        {
            Transform yard = centrum.Find("PortalNadvorie");
            if (yard == null) { BuildPortalHub(); return; }   // nádvorie ešte neexistuje: postaví sa celé aj s Hradbou

            // Brána Centra do Divočiny (chýbala v starších scénach)
            int hubBefore = hbCreated;
            EnsureLinkedGate("Portal_Centrum_DoDivociny", "Portal_zDivociny", "Divočina", yard);

            // Chýbajúce pôvodné brány Centra (zmazané alebo nikdy nevytvorené) sa doplnia ako nové brány s tým istým cieľom
            EnsureLinkedGate("Portal_DoMegakorporacie", "Portal_DoMesta", "Megakorporácia", yard);
            EnsureLinkedGate("Portal_DoKanon", "Portal_zKanon", "Bleskový kaňon", yard);
            EnsureLinkedGate("Portal_DoLes", "Portal_zLes", "Temný les", yard);
            EnsureLinkedGate("Portal_DoPustatina", "Portal_zPustatina", "Zrezavená pustatina", yard);
            EnsureLinkedGate("Portal_DoMociare", "Portal_zMociare", "Močiare", yard);
            EnsureLinkedGate("Portal_DoBane", "Portal_zBane", "Bane", yard);

            if (hbCreated != hubBefore)
            {
                var gates = new List<PortalGate>();
                foreach (string gateName in HubGates)
                {
                    PortalGate g = FindGateObject(gateName);
                    if (g != null) gates.Add(g);
                }
                Vector3 plaza = yard.position;
                ArrangeArc(gates, yard, plaza, 15f, 200f, 340f);
                Debug.Log($"Centrum: doplnených {hbCreated - hubBefore} brán na nádvorí.");
            }
        }

        BuildHradbaPortals();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log(hbCreated == before ? "Portály: v oboch mestách nič nechýbalo." : $"Portály: spolu doplnených {hbCreated - before} brán. Ulož scénu.");
    }
}
