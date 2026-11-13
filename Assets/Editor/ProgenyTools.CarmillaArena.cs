using UnityEditor;
using UnityEngine;

/// <summary>Aréna Kňažky Carmilly (prapôvodná krvavá svätyňa) a dočasný model z primitív. Model sa vymení cez BossModelRig.</summary>
public static partial class ProgenyTools
{
    const string CarmillaArenaName = "Arena_Carmilla";

    [MenuItem("Progeny/Boss/Carmilla: postaviť arénu a model", false, 570)]
    public static void BuildCarmillaArena()
    {
        GameObject old = GameObject.Find(CarmillaArenaName);
        Vector3 origin = old != null ? old.transform.position : new Vector3(1500f, 60f, 1850f);
        if (old != null) Object.DestroyImmediate(old);

        Material floor = RenMat("Carmilla_Floor", new Color(0.14f, 0.03f, 0.04f), 0.4f, 0.7f);
        Material rune = RenMat("Carmilla_Rune", new Color(0.8f, 0.05f, 0.12f), 0f, 0.5f, true);
        Material pillar = RenMat("Carmilla_Pillar", new Color(0.22f, 0.12f, 0.12f), 0.3f, 0.4f);
        Material robe = RenMat("Carmilla_Robe", new Color(0.55f, 0.02f, 0.08f), 0.1f, 0.5f, true);
        Material skin = RenMat("Carmilla_Skin", new Color(0.85f, 0.75f, 0.75f), 0f, 0.4f, true);
        Material orb = RenMat("Carmilla_Orb", new Color(0.9f, 0.05f, 0.15f), 0f, 0.9f, true);
        Material table = RenMat("Carmilla_Table", new Color(0.75f, 0.85f, 0.8f), 0.4f, 0.6f);
        Material serum = RenMat("Carmilla_Serum", new Color(0.3f, 0.95f, 0.5f), 0f, 0.9f, true);

        var root = new GameObject(CarmillaArenaName);
        root.transform.position = origin;

        RenPrim(PrimitiveType.Cube, "Podlaha", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(84f, 1f, 84f), floor, true);
        RenPrim(PrimitiveType.Cylinder, "Rituálny_kruh", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(46f, 0.02f, 46f), rune);
        RenPrim(PrimitiveType.Cylinder, "Rituálny_kruh_vnutro", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(43f, 0.02f, 43f), floor);
        for (int i = 0; i < 4; i++)
        {
            bool x = i < 2;
            float s = (i % 2 == 0 ? 1f : -1f) * 42f;
            var wall = new GameObject("Stena_" + i);
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = x ? new Vector3(s + Mathf.Sign(s), 4f, 0f) : new Vector3(0f, 4f, s + Mathf.Sign(s));
            wall.AddComponent<BoxCollider>().size = x ? new Vector3(2f, 10f, 86f) : new Vector3(86f, 10f, 2f);
        }

        // Stĺpy svätyne: kryt pred vysávaním a orbmi (lúč ich postupne drví)
        for (int i = 0; i < 6; i++)
        {
            float a = (30f + i * 60f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 15f;
            GameObject pl = RenPrim(PrimitiveType.Cylinder, "Stlp_" + i, root.transform, p + Vector3.up * 4f, new Vector3(2.8f, 4f, 2.8f), pillar, true);
            pl.AddComponent<ArenaCover>();
        }
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 38f;
            var lg = new GameObject("Svetlo_" + i);
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = p + Vector3.up * 8f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 40f;
            l.intensity = 2.2f;
            l.color = new Color(1f, 0.15f, 0.2f);
            l.shadows = LightShadows.None;
            RenPrim(PrimitiveType.Sphere, "Krvava_lampa_" + i, root.transform, p + Vector3.up * 8f, Vector3.one * 0.8f, orb);
        }

        // Očistné stoly doktorky Yumi (sérum)
        for (int i = 0; i < 2; i++)
        {
            Vector3 p = new Vector3(i == 0 ? -8f : 8f, 0f, -8f);
            GameObject st = RenPrim(PrimitiveType.Cube, "Yumin_stol_" + i, root.transform, p + Vector3.up * 0.6f, new Vector3(2.2f, 1.2f, 1.2f), table, true);
            RenPrim(PrimitiveType.Cylinder, "Sérum", st.transform, new Vector3(0f, 0.75f, 0f), new Vector3(0.3f, 0.4f, 0.5f), serum);
            st.AddComponent<SerumStation>();
        }

        // Carmilla: krvavo-červené kňazské rúcho a vznášajúce sa orby
        var boss = new GameObject("Carmilla");
        boss.transform.SetParent(root.transform, false);
        boss.transform.localPosition = new Vector3(0f, 0f, 22f);
        boss.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        var bc = boss.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 1.9f, 0f);
        bc.size = new Vector3(1.6f, 3.8f, 1.2f);
        boss.AddComponent<Damageable>();
        var carm = boss.AddComponent<CarmillaBoss>();
        var model = new GameObject("Model");
        model.transform.SetParent(boss.transform, false);
        var rig = model.AddComponent<BossModelRig>();

        RenPrim(PrimitiveType.Cylinder, "Rucho", model.transform, new Vector3(0f, 1.3f, 0f), new Vector3(1.5f, 1.3f, 1.1f), robe);
        RenPrim(PrimitiveType.Cube, "Trup", model.transform, new Vector3(0f, 2.8f, 0f), new Vector3(1f, 1.3f, 0.55f), robe);
        RenPrim(PrimitiveType.Sphere, "Hlava", model.transform, new Vector3(0f, 3.75f, 0f), new Vector3(0.6f, 0.65f, 0.6f), skin);
        RenPrim(PrimitiveType.Capsule, "Ruka_L", model.transform, new Vector3(-0.8f, 2.8f, 0.2f), new Vector3(0.26f, 0.65f, 0.26f), robe);
        RenPrim(PrimitiveType.Capsule, "Ruka_R", model.transform, new Vector3(0.8f, 2.8f, 0.2f), new Vector3(0.26f, 0.65f, 0.26f), robe);
        for (int i = 0; i < 3; i++)
        {
            float a = (i * 120f + 20f) * Mathf.Deg2Rad;
            RenPrim(PrimitiveType.Sphere, "Orb_" + i, model.transform, new Vector3(Mathf.Cos(a) * 2.2f, 3.3f, Mathf.Sin(a) * 2.2f), Vector3.one * 0.55f, orb);
        }
        rig.muzzleLeft = RenAnchor("Ustie_L", model.transform, new Vector3(-0.8f, 3f, 1f));
        rig.muzzleRight = RenAnchor("Ustie_R", model.transform, new Vector3(0.8f, 3f, 1f));
        rig.cockpit = RenAnchor("Srdce", model.transform, new Vector3(0f, 2.8f, 0.3f));
        var so = new SerializedObject(carm);
        so.FindProperty("rig").objectReferenceValue = rig;
        so.ApplyModifiedPropertiesWithoutUndo();

        var spawn = RenAnchor("Spawn_hraca", root.transform, new Vector3(0f, 0.6f, -16f));
        var arena = root.AddComponent<EliteArena>();
        var aso = new SerializedObject(arena);
        aso.FindProperty("bossId").stringValue = "carmilla";
        aso.FindProperty("speaker").stringValue = "Kňažka Carmilla";
        SerializedProperty lines = aso.FindProperty("introLines");
        string[] text =
        {
            "Hybrid. Taká vzácna krv v takej nečistej nádobe.",
            "Arakani ťa chce celého. Ja mu stačím vyžmýkať z teba len to najlepšie.",
            "Pokľakni. Alebo ti ju vypijem zaživa."
        };
        lines.arraySize = text.Length;
        for (int i = 0; i < text.Length; i++) lines.GetArrayElementAtIndex(i).stringValue = text[i];
        aso.FindProperty("boss").objectReferenceValue = carm;
        aso.FindProperty("playerSpawn").objectReferenceValue = spawn;
        aso.ApplyModifiedPropertiesWithoutUndo();

        Vector3 pad = FindEliteLiftSpot("Zem_Mociare");
        Material padMat = RenMat("Ren_Pad", new Color(0.1f, 0.6f, 0.9f), 0.2f, 0.5f);
        var entrance = new GameObject("Vytah_Carmilla_vstup");
        entrance.transform.SetParent(root.transform, false);
        entrance.transform.position = pad;
        RenPrim(PrimitiveType.Cylinder, "Plosina", entrance.transform, Vector3.zero, new Vector3(3f, 0.1f, 3f), padMat);
        var inPad = entrance.AddComponent<EliteLiftPad>();
        var ipso = new SerializedObject(inPad);
        ipso.FindProperty("arena").objectReferenceValue = arena;
        ipso.FindProperty("label").stringValue = "Kňažka Carmilla";
        ipso.ApplyModifiedPropertiesWithoutUndo();

        var exit = new GameObject("Vytah_Carmilla_vystup");
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
        Debug.Log("Aréna Carmilly postavená na " + origin + ", vstupný výťah na " + pad + ". Ulož scénu.");
    }

    // ---------- Testy v Play móde ----------

    static CarmillaBoss Carmilla() { EliteArena a = EliteArena.Find("carmilla"); return a != null ? a.Boss as CarmillaBoss : null; }

    [MenuItem("Progeny/Boss/Carmilla: test - začať boj hneď", false, 571)]
    public static void CarmillaTestStart() { EliteArena a = EliteArena.Find("carmilla"); if (a != null) a.DebugStartImmediately(); }

    [MenuItem("Progeny/Boss/Carmilla: test - fáza 2 (70 %)", false, 572)]
    public static void CarmillaTestPhase2() { Carmilla()?.DebugSetHealthFraction(0.69f); }

    [MenuItem("Progeny/Boss/Carmilla: test - fáza 3 (40 %)", false, 573)]
    public static void CarmillaTestPhase3() { Carmilla()?.DebugSetHealthFraction(0.39f); }

    [MenuItem("Progeny/Boss/Carmilla: test - finišer (10 %)", false, 574)]
    public static void CarmillaTestFinisher() { Carmilla()?.DebugSetHealthFraction(0.1f); }

    [MenuItem("Progeny/Boss/Carmilla: test - začať boj hneď", true)]
    [MenuItem("Progeny/Boss/Carmilla: test - fáza 2 (70 %)", true)]
    [MenuItem("Progeny/Boss/Carmilla: test - fáza 3 (40 %)", true)]
    [MenuItem("Progeny/Boss/Carmilla: test - finišer (10 %)", true)]
    static bool CarmillaPlayOnly() { return Application.isPlaying && EliteArena.Find("carmilla") != null; }
}
