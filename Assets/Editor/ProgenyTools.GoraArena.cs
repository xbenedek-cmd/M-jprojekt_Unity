using UnityEditor;
using UnityEngine;

/// <summary>Aréna Generála Gora (lesná čistina) a dočasný model z primitív. Model sa vymení cez BossModelRig.</summary>
public static partial class ProgenyTools
{
    const string GoraArenaName = "Arena_Gora";

    [MenuItem("Progeny/Boss/Gora: postaviť arénu a model", false, 540)]
    public static void BuildGoraArena()
    {
        GameObject old = GameObject.Find(GoraArenaName);
        Vector3 origin = old != null ? old.transform.position : new Vector3(1500f, 60f, 1400f);
        if (old != null) Object.DestroyImmediate(old);

        Material floor = RenMat("Gora_Floor", new Color(0.18f, 0.2f, 0.1f), 0f, 0.1f);
        Material trunk = RenMat("Gora_Trunk", new Color(0.3f, 0.2f, 0.12f), 0f, 0.1f);
        Material leaves = RenMat("Gora_Leaves", new Color(0.12f, 0.3f, 0.1f), 0f, 0.1f);
        Material rock = RenMat("Gora_Rock", new Color(0.32f, 0.3f, 0.27f), 0f, 0.1f);
        Material bark = RenMat("Gora_Bark", new Color(0.25f, 0.18f, 0.1f), 0f, 0.15f, true);
        Material moss = RenMat("Gora_Moss", new Color(0.2f, 0.38f, 0.12f), 0f, 0.1f, true);
        Material vein = RenMat("Gora_Vein", new Color(0.3f, 0.9f, 0.2f), 0f, 0.6f, true);
        Material backMat = RenMat("Gora_Back", new Color(0.35f, 0.28f, 0.1f), 0f, 0.2f, true);

        var root = new GameObject(GoraArenaName);
        root.transform.position = origin;

        RenPrim(PrimitiveType.Cube, "Zem", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(84f, 1f, 84f), floor, true);
        for (int i = 0; i < 4; i++)
        {
            bool x = i < 2;
            float s = (i % 2 == 0 ? 1f : -1f) * 42f;
            var wall = new GameObject("Stena_" + i);
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = x ? new Vector3(s + Mathf.Sign(s), 4f, 0f) : new Vector3(0f, 4f, s + Mathf.Sign(s));
            wall.AddComponent<BoxCollider>().size = x ? new Vector3(2f, 10f, 86f) : new Vector3(86f, 10f, 2f);
        }

        // Kmene po obvode (kryt pred balvanmi; balvany ich drvia)
        for (int i = 0; i < 12; i++)
        {
            float a = (i * 30f + 15f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 27f;
            GameObject t = RenPrim(PrimitiveType.Cylinder, "Strom_" + i, root.transform, p + Vector3.up * 5f, new Vector3(2.6f, 5f, 2.6f), trunk, true);
            RenPrim(PrimitiveType.Sphere, "Koruna", t.transform, new Vector3(0f, 1.3f, 0f), new Vector3(3.4f, 1.8f, 3.4f), leaves);
            t.AddComponent<ArenaCover>();
        }
        for (int i = 0; i < 5; i++)
        {
            float a = (i * 72f + 40f) * Mathf.Deg2Rad;
            RenPrim(PrimitiveType.Sphere, "Skala_" + i, root.transform, new Vector3(Mathf.Cos(a), 0.7f, Mathf.Sin(a)) * 14f, new Vector3(3f, 2f, 3f), rock);
        }
        var lg = new GameObject("Slnko");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0f, 30f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 90f;
        l.intensity = 2.5f;
        l.color = new Color(1f, 0.95f, 0.75f);
        l.shadows = LightShadows.None;

        // Gora: kolos obrastený kôrou, skalami a machom
        var boss = new GameObject("Gora");
        boss.transform.SetParent(root.transform, false);
        boss.transform.localPosition = new Vector3(0f, 0f, 10f);
        boss.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        var bc = boss.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 5f, 0f);
        bc.size = new Vector3(5.6f, 10f, 3.6f);
        boss.AddComponent<Damageable>();
        var gora = boss.AddComponent<GoraBoss>();
        var model = new GameObject("Model");
        model.transform.SetParent(boss.transform, false);
        var rig = model.AddComponent<BossModelRig>();

        RenPrim(PrimitiveType.Capsule, "Noha_L", model.transform, new Vector3(-1.5f, 2.2f, 0f), new Vector3(1.9f, 2.2f, 1.9f), bark);
        RenPrim(PrimitiveType.Capsule, "Noha_R", model.transform, new Vector3(1.5f, 2.2f, 0f), new Vector3(1.9f, 2.2f, 1.9f), bark);
        RenPrim(PrimitiveType.Cube, "Trup", model.transform, new Vector3(0f, 6f, 0f), new Vector3(4.6f, 4.2f, 2.8f), bark);
        RenPrim(PrimitiveType.Sphere, "Ramenny_kamen_L", model.transform, new Vector3(-3f, 7.6f, 0f), new Vector3(2.6f, 2.2f, 2.6f), rock);
        RenPrim(PrimitiveType.Sphere, "Ramenny_kamen_R", model.transform, new Vector3(3f, 7.6f, 0f), new Vector3(2.6f, 2.2f, 2.6f), rock);
        RenPrim(PrimitiveType.Capsule, "Ruka_L", model.transform, new Vector3(-3.2f, 5.2f, 0.4f), new Vector3(1.3f, 2.4f, 1.3f), bark);
        RenPrim(PrimitiveType.Capsule, "Ruka_R", model.transform, new Vector3(3.2f, 5.2f, 0.4f), new Vector3(1.3f, 2.4f, 1.3f), bark);
        RenPrim(PrimitiveType.Sphere, "Hlava", model.transform, new Vector3(0f, 8.8f, 0.3f), new Vector3(1.8f, 1.6f, 1.8f), rock);
        GameObject hl = RenPrim(PrimitiveType.Cylinder, "Roh_L", model.transform, new Vector3(-0.9f, 9.9f, 0.3f), new Vector3(0.35f, 0.8f, 0.35f), rock);
        GameObject hr = RenPrim(PrimitiveType.Cylinder, "Roh_R", model.transform, new Vector3(0.9f, 9.9f, 0.3f), new Vector3(0.35f, 0.8f, 0.35f), rock);
        hl.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
        hr.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);
        RenPrim(PrimitiveType.Sphere, "Mach", model.transform, new Vector3(0f, 7.9f, -0.6f), new Vector3(4f, 1.2f, 2.4f), moss);
        RenPrim(PrimitiveType.Cube, "Zila_1", model.transform, new Vector3(-0.8f, 6f, 1.42f), new Vector3(0.18f, 3f, 0.05f), vein);
        RenPrim(PrimitiveType.Cube, "Zila_2", model.transform, new Vector3(0.9f, 6f, 1.42f), new Vector3(0.18f, 2.4f, 0.05f), vein);

        // Slabé miesto na chrbte (zásah zozadu ignoruje brnenie)
        Transform backAnchor = RenAnchor("Chrbat", model.transform, new Vector3(0f, 5.5f, -1.7f));
        RenPrim(PrimitiveType.Cube, "Chrbat_kora", backAnchor, new Vector3(0f, 0f, -0.1f), new Vector3(3.2f, 3f, 0.5f), backMat);
        var zone = backAnchor.gameObject.AddComponent<BossHitZone>();
        zone.zoneId = "back";
        zone.radius = 3.6f;
        zone.multiplier = 1.5f;

        rig.fist = RenAnchor("Pest", model.transform, new Vector3(3.2f, 4.2f, 1.2f));
        rig.cockpit = RenAnchor("Srdce", model.transform, new Vector3(0f, 6f, 1.5f));
        var so = new SerializedObject(gora);
        so.FindProperty("rig").objectReferenceValue = rig;
        so.ApplyModifiedPropertiesWithoutUndo();

        var spawn = RenAnchor("Spawn_hraca", root.transform, new Vector3(0f, 0.6f, -26f));
        var arena = root.AddComponent<EliteArena>();
        var aso = new SerializedObject(arena);
        aso.FindProperty("bossId").stringValue = "gora";
        aso.FindProperty("speaker").stringValue = "General Gora";
        SerializedProperty lines = aso.FindProperty("introLines");
        string[] text =
        {
            "Cítim v tebe stroje. Cítim bane, ktoré vŕtajú do mojej zeme.",
            "Les pamätá každý strom, ktorý si dal zhodiť. Dnes mu to vrátim.",
            "Skús ma zraniť. Skala sa nebojí ocele."
        };
        lines.arraySize = text.Length;
        for (int i = 0; i < text.Length; i++) lines.GetArrayElementAtIndex(i).stringValue = text[i];
        aso.FindProperty("boss").objectReferenceValue = gora;
        aso.FindProperty("playerSpawn").objectReferenceValue = spawn;
        aso.ApplyModifiedPropertiesWithoutUndo();

        Vector3 pad = FindEliteLiftSpot("Zem_Divocina");
        Material padMat = RenMat("Ren_Pad", new Color(0.1f, 0.6f, 0.9f), 0.2f, 0.5f);
        var entrance = new GameObject("Vytah_Gora_vstup");
        entrance.transform.SetParent(root.transform, false);
        entrance.transform.position = pad;
        RenPrim(PrimitiveType.Cylinder, "Plosina", entrance.transform, Vector3.zero, new Vector3(3f, 0.1f, 3f), padMat);
        var inPad = entrance.AddComponent<EliteLiftPad>();
        var ipso = new SerializedObject(inPad);
        ipso.FindProperty("arena").objectReferenceValue = arena;
        ipso.FindProperty("label").stringValue = "General Gora";
        ipso.ApplyModifiedPropertiesWithoutUndo();

        var exit = new GameObject("Vytah_Gora_vystup");
        exit.transform.SetParent(root.transform, false);
        exit.transform.localPosition = new Vector3(0f, 0.05f, -38f);
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
        Debug.Log("Aréna Gora postavená na " + origin + ", vstupný výťah na " + pad + ". Ulož scénu.");
    }

    // ---------- Testy v Play móde ----------

    static GoraBoss Gora() { EliteArena a = EliteArena.Find("gora"); return a != null ? a.Boss as GoraBoss : null; }

    [MenuItem("Progeny/Boss/Gora: test - začať boj hneď", false, 541)]
    public static void GoraTestStart() { EliteArena a = EliteArena.Find("gora"); if (a != null) a.DebugStartImmediately(); }

    [MenuItem("Progeny/Boss/Gora: test - fáza 2 (70 %)", false, 542)]
    public static void GoraTestPhase2() { Gora()?.DebugSetHealthFraction(0.69f); }

    [MenuItem("Progeny/Boss/Gora: test - fáza 3 (40 %)", false, 543)]
    public static void GoraTestPhase3() { Gora()?.DebugSetHealthFraction(0.39f); }

    [MenuItem("Progeny/Boss/Gora: test - finišer (10 %)", false, 544)]
    public static void GoraTestFinisher() { Gora()?.DebugSetHealthFraction(0.1f); }

    [MenuItem("Progeny/Boss/Gora: test - začať boj hneď", true)]
    [MenuItem("Progeny/Boss/Gora: test - fáza 2 (70 %)", true)]
    [MenuItem("Progeny/Boss/Gora: test - fáza 3 (40 %)", true)]
    [MenuItem("Progeny/Boss/Gora: test - finišer (10 %)", true)]
    static bool GoraPlayOnly() { return Application.isPlaying && EliteArena.Find("gora") != null; }
}
