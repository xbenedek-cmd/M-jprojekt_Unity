using UnityEditor;
using UnityEngine;

/// <summary>Aréna Lady Kuro (tmavá zrkadlová ulička) a dočasný model z primitív. Model sa vymení cez BossModelRig.</summary>
public static partial class ProgenyTools
{
    const string KuroArenaName = "Arena_Kuro";

    [MenuItem("Progeny/Boss/Kuro: postaviť arénu a model", false, 560)]
    public static void BuildKuroArena()
    {
        GameObject old = GameObject.Find(KuroArenaName);
        Vector3 origin = old != null ? old.transform.position : new Vector3(1500f, 60f, 1700f);
        if (old != null) Object.DestroyImmediate(old);

        Material floor = RenMat("Kuro_Floor", new Color(0.05f, 0.04f, 0.08f), 0.8f, 0.9f);
        Material pillar = RenMat("Kuro_Pillar", new Color(0.1f, 0.08f, 0.14f), 0.4f, 0.6f);
        Material lantern = RenMat("Kuro_Lantern", new Color(0.6f, 0.2f, 0.9f), 0f, 0.8f, true);
        Material robe = RenMat("Kuro_Robe", new Color(0.04f, 0.03f, 0.06f), 0.2f, 0.5f, true);
        Material skin = RenMat("Kuro_Skin", new Color(0.7f, 0.65f, 0.75f), 0f, 0.4f, true);
        Material eyes = RenMat("Kuro_Eyes", new Color(0.8f, 0.3f, 1f), 0f, 0.9f, true);
        Material blade = RenMat("Kuro_Blade", new Color(0.2f, 0.1f, 0.3f), 0.8f, 0.9f, true);
        Material lamp = RenMat("Kuro_Lamp", new Color(1f, 0.95f, 0.7f), 0f, 0.8f, true);
        Material stationMat = RenMat("Kuro_Station", new Color(0.25f, 0.25f, 0.3f), 0.6f, 0.5f);

        var root = new GameObject(KuroArenaName);
        root.transform.position = origin;

        RenPrim(PrimitiveType.Cube, "Podlaha", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(84f, 1f, 84f), floor, true);
        for (int i = 0; i < 4; i++)
        {
            bool x = i < 2;
            float s = (i % 2 == 0 ? 1f : -1f) * 42f;
            var wall = new GameObject("Stena_" + i);
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = x ? new Vector3(s + Mathf.Sign(s), 4f, 0f) : new Vector3(0f, 4f, s + Mathf.Sign(s));
            wall.AddComponent<BoxCollider>().size = x ? new Vector3(2f, 10f, 86f) : new Vector3(86f, 10f, 2f);
        }

        // Stĺpy s fialovými lampášmi (slabé svetlo: tiene sú tmavé)
        for (int i = 0; i < 10; i++)
        {
            float a = i * 36f * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 30f;
            GameObject pl = RenPrim(PrimitiveType.Cylinder, "Stlp_" + i, root.transform, p + Vector3.up * 5f, new Vector3(2f, 5f, 2f), pillar, true);
            RenPrim(PrimitiveType.Sphere, "Lampas", pl.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.9f, 0.5f, 0.9f), lantern);
            var lg = new GameObject("Svetlo");
            lg.transform.SetParent(pl.transform, false);
            lg.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 24f;
            l.intensity = 1.1f;
            l.color = new Color(0.6f, 0.25f, 0.95f);
            l.shadows = LightShadows.None;
        }

        // Svetlomety (silné svetlo) v strede arény
        for (int i = 0; i < 3; i++)
        {
            float a = (90f + i * 120f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 12f;
            GameObject st = RenPrim(PrimitiveType.Cylinder, "Svetlomet_" + i, root.transform, p + Vector3.up * 1.2f, new Vector3(1.6f, 1.2f, 1.6f), stationMat, true);
            RenPrim(PrimitiveType.Sphere, "Lampa", st.transform, new Vector3(0f, 1.1f, 0f), new Vector3(1.2f, 0.7f, 1.2f), lamp);
            st.AddComponent<LightStation>();
        }

        // Kuro: štíhla postava v čiernom kimone s fialovými zreničkami a tieňovými čepeľami
        var boss = new GameObject("Kuro");
        boss.transform.SetParent(root.transform, false);
        boss.transform.localPosition = new Vector3(0f, 0f, 20f);
        boss.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        var bc = boss.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 1.8f, 0f);
        bc.size = new Vector3(1.4f, 3.6f, 1.2f);
        boss.AddComponent<Damageable>();
        var kuro = boss.AddComponent<KuroBoss>();
        var model = new GameObject("Model");
        model.transform.SetParent(boss.transform, false);
        var rig = model.AddComponent<BossModelRig>();

        RenPrim(PrimitiveType.Cylinder, "Kimono", model.transform, new Vector3(0f, 1.2f, 0f), new Vector3(1.3f, 1.2f, 0.9f), robe);
        RenPrim(PrimitiveType.Cube, "Trup", model.transform, new Vector3(0f, 2.7f, 0f), new Vector3(0.9f, 1.2f, 0.5f), robe);
        RenPrim(PrimitiveType.Sphere, "Hlava", model.transform, new Vector3(0f, 3.6f, 0f), new Vector3(0.55f, 0.6f, 0.55f), skin);
        RenPrim(PrimitiveType.Sphere, "Oko_L", model.transform, new Vector3(-0.12f, 3.65f, 0.24f), new Vector3(0.12f, 0.08f, 0.06f), eyes);
        RenPrim(PrimitiveType.Sphere, "Oko_R", model.transform, new Vector3(0.12f, 3.65f, 0.24f), new Vector3(0.12f, 0.08f, 0.06f), eyes);
        RenPrim(PrimitiveType.Capsule, "Ruka_L", model.transform, new Vector3(-0.7f, 2.6f, 0.2f), new Vector3(0.25f, 0.6f, 0.25f), robe);
        RenPrim(PrimitiveType.Capsule, "Ruka_R", model.transform, new Vector3(0.7f, 2.6f, 0.2f), new Vector3(0.25f, 0.6f, 0.25f), robe);
        RenPrim(PrimitiveType.Cube, "Cepel_L", model.transform, new Vector3(-0.8f, 2.2f, 0.9f), new Vector3(0.08f, 0.08f, 1.6f), blade);
        RenPrim(PrimitiveType.Cube, "Cepel_R", model.transform, new Vector3(0.8f, 2.2f, 0.9f), new Vector3(0.08f, 0.08f, 1.6f), blade);

        rig.muzzleLeft = RenAnchor("Cepel_L_kotva", model.transform, new Vector3(-0.8f, 2.2f, 1.7f));
        rig.muzzleRight = RenAnchor("Cepel_R_kotva", model.transform, new Vector3(0.8f, 2.2f, 1.7f));
        rig.cockpit = RenAnchor("Srdce", model.transform, new Vector3(0f, 2.7f, 0.3f));
        var so = new SerializedObject(kuro);
        so.FindProperty("rig").objectReferenceValue = rig;
        so.ApplyModifiedPropertiesWithoutUndo();

        var spawn = RenAnchor("Spawn_hraca", root.transform, new Vector3(0f, 0.6f, -14f));
        var arena = root.AddComponent<EliteArena>();
        var aso = new SerializedObject(arena);
        aso.FindProperty("bossId").stringValue = "kuro";
        aso.FindProperty("speaker").stringValue = "Lady Kuro";
        SerializedProperty lines = aso.FindProperty("introLines");
        string[] text =
        {
            "Naučila som ťa vidieť tmu. Škoda, že si sa rozhodol svietiť.",
            "Tu sú len tiene. A tiene nemajú meno... ani smrť.",
            "Hľadaj ma. Nájdeš len svoju hrôzu."
        };
        lines.arraySize = text.Length;
        for (int i = 0; i < text.Length; i++) lines.GetArrayElementAtIndex(i).stringValue = text[i];
        aso.FindProperty("boss").objectReferenceValue = kuro;
        aso.FindProperty("playerSpawn").objectReferenceValue = spawn;
        aso.ApplyModifiedPropertiesWithoutUndo();

        Vector3 pad = FindEliteLiftSpot("Zem_Pustatina");
        Material padMat = RenMat("Ren_Pad", new Color(0.1f, 0.6f, 0.9f), 0.2f, 0.5f);
        var entrance = new GameObject("Vytah_Kuro_vstup");
        entrance.transform.SetParent(root.transform, false);
        entrance.transform.position = pad;
        RenPrim(PrimitiveType.Cylinder, "Plosina", entrance.transform, Vector3.zero, new Vector3(3f, 0.1f, 3f), padMat);
        var inPad = entrance.AddComponent<EliteLiftPad>();
        var ipso = new SerializedObject(inPad);
        ipso.FindProperty("arena").objectReferenceValue = arena;
        ipso.FindProperty("label").stringValue = "Lady Kuro";
        ipso.ApplyModifiedPropertiesWithoutUndo();

        var exit = new GameObject("Vytah_Kuro_vystup");
        exit.transform.SetParent(root.transform, false);
        exit.transform.localPosition = new Vector3(0f, 0.1f, -38f);
        RenPrim(PrimitiveType.Cylinder, "Plosina", exit.transform, Vector3.zero, new Vector3(3f, 0.1f, 3f), padMat);
        var outPad = exit.AddComponent<EliteLiftPad>();
        var opso = new SerializedObject(outPad);
        opso.FindProperty("arena").objectReferenceValue = arena;
        opso.FindProperty("isExit").boolValue = true;
        opso.FindProperty("destination").objectReferenceValue = RenAnchor("Navrat_dole", entrance.transform, new Vector3(2.5f, 0.3f, 0f));
        opso.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("Aréna Kuro postavená na " + origin + ", vstupný výťah na " + pad + ". Ulož scénu.");
    }

    // ---------- Testy v Play móde ----------

    static KuroBoss Kuro() { EliteArena a = EliteArena.Find("kuro"); return a != null ? a.Boss as KuroBoss : null; }

    [MenuItem("Progeny/Boss/Kuro: test - začať boj hneď", false, 561)]
    public static void KuroTestStart() { EliteArena a = EliteArena.Find("kuro"); if (a != null) a.DebugStartImmediately(); }

    [MenuItem("Progeny/Boss/Kuro: test - fáza 2 (70 %)", false, 562)]
    public static void KuroTestPhase2() { Kuro()?.DebugSetHealthFraction(0.69f); }

    [MenuItem("Progeny/Boss/Kuro: test - fáza 3 (40 %)", false, 563)]
    public static void KuroTestPhase3() { Kuro()?.DebugSetHealthFraction(0.39f); }

    [MenuItem("Progeny/Boss/Kuro: test - finišer (10 %)", false, 564)]
    public static void KuroTestFinisher() { Kuro()?.DebugSetHealthFraction(0.1f); }

    [MenuItem("Progeny/Boss/Kuro: test - začať boj hneď", true)]
    [MenuItem("Progeny/Boss/Kuro: test - fáza 2 (70 %)", true)]
    [MenuItem("Progeny/Boss/Kuro: test - fáza 3 (40 %)", true)]
    [MenuItem("Progeny/Boss/Kuro: test - finišer (10 %)", true)]
    static bool KuroPlayOnly() { return Application.isPlaying && EliteArena.Find("kuro") != null; }
}
