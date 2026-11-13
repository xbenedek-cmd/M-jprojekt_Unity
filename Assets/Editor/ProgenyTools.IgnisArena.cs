using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Aréna Lorda Ignisa (horiaca priemyselná zóna) a dočasný model z primitív. Model sa vymení cez BossModelRig.</summary>
public static partial class ProgenyTools
{
    const string IgnisArenaName = "Arena_Ignis";

    [MenuItem("Progeny/Boss/Ignis: postaviť arénu a model", false, 550)]
    public static void BuildIgnisArena()
    {
        GameObject old = GameObject.Find(IgnisArenaName);
        Vector3 origin = old != null ? old.transform.position : new Vector3(1500f, 60f, 1550f);
        if (old != null) Object.DestroyImmediate(old);

        Material floor = RenMat("Ignis_Floor", new Color(0.12f, 0.11f, 0.11f), 0.2f, 0.2f);
        Material lava = RenMat("Ignis_Lava", new Color(0.9f, 0.25f, 0.05f), 0f, 0.4f, true);
        Material safe = RenMat("Ignis_Safe", new Color(0.25f, 0.24f, 0.24f), 0.2f, 0.2f);
        Material pipe = RenMat("Ignis_Pipe", new Color(0.2f, 0.18f, 0.17f), 0.6f, 0.3f);
        Material body = RenMat("Ignis_Body", new Color(0.22f, 0.14f, 0.1f), 0.1f, 0.2f);
        Material vein = RenMat("Ignis_Vein", new Color(1f, 0.4f, 0.08f), 0f, 0.6f, true);
        Material water = RenMat("Ignis_Water", new Color(0.2f, 0.55f, 0.9f), 0.3f, 0.7f, true);
        Material stationMat = RenMat("Ignis_Station", new Color(0.25f, 0.3f, 0.36f), 0.6f, 0.5f);

        var root = new GameObject(IgnisArenaName);
        root.transform.position = origin;

        RenPrim(PrimitiveType.Cube, "Podlaha", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(84f, 1f, 84f), floor, true);
        RenPrim(PrimitiveType.Cube, "Lava", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(84f, 0.04f, 84f), lava);
        GameObject safeDisc = RenPrim(PrimitiveType.Cylinder, "Bezpecny_kruh", root.transform, new Vector3(0f, 0.06f, 0f), new Vector3(80f, 0.03f, 80f), safe);
        for (int i = 0; i < 4; i++)
        {
            bool x = i < 2;
            float s = (i % 2 == 0 ? 1f : -1f) * 42f;
            var wall = new GameObject("Stena_" + i);
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = x ? new Vector3(s + Mathf.Sign(s), 4f, 0f) : new Vector3(0f, 4f, s + Mathf.Sign(s));
            wall.AddComponent<BoxCollider>().size = x ? new Vector3(2f, 10f, 86f) : new Vector3(86f, 10f, 2f);
        }

        // Komíny a potrubia po obvode, žiara plameňov
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 39f;
            RenPrim(PrimitiveType.Cylinder, "Komin_" + i, root.transform, p + Vector3.up * 9f, new Vector3(3f, 9f, 3f), pipe, true);
            var lg = new GameObject("Plamen_" + i);
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = p + Vector3.up * 19f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 45f;
            l.intensity = 2.4f;
            l.color = new Color(1f, 0.45f, 0.15f);
            l.shadows = LightShadows.None;
        }

        // Chladiace stanice (vodné kanóny) v bezpečnom jadre
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + i * 90f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 10f;
            GameObject st = RenPrim(PrimitiveType.Cylinder, "Chladiaca_stanica_" + i, root.transform, p + Vector3.up * 1.2f, new Vector3(2f, 1.2f, 2f), stationMat, true);
            RenPrim(PrimitiveType.Sphere, "Nadrz", st.transform, new Vector3(0f, 1.1f, 0f), new Vector3(1.1f, 0.8f, 1.1f), water);
            st.AddComponent<CoolantStation>();
        }

        // Ignis: popolavé masívne telo s roztavenými žilami a plamennými pazúrmi
        var boss = new GameObject("Ignis");
        boss.transform.SetParent(root.transform, false);
        boss.transform.localPosition = new Vector3(0f, 0f, 22f);
        boss.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        var bc = boss.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 4.5f, 0f);
        bc.size = new Vector3(5.2f, 9f, 3.4f);
        boss.AddComponent<Damageable>();
        var ignis = boss.AddComponent<IgnisBoss>();
        var model = new GameObject("Model");
        model.transform.SetParent(boss.transform, false);
        var rig = model.AddComponent<BossModelRig>();

        var bodyR = new List<Renderer>();
        var veinR = new List<Renderer>();
        void Body(GameObject g) { bodyR.Add(g.GetComponent<Renderer>()); }
        void Vein(GameObject g) { veinR.Add(g.GetComponent<Renderer>()); }

        Body(RenPrim(PrimitiveType.Capsule, "Noha_L", model.transform, new Vector3(-1.4f, 2f, 0f), new Vector3(1.7f, 2f, 1.7f), body));
        Body(RenPrim(PrimitiveType.Capsule, "Noha_R", model.transform, new Vector3(1.4f, 2f, 0f), new Vector3(1.7f, 2f, 1.7f), body));
        Body(RenPrim(PrimitiveType.Cube, "Trup", model.transform, new Vector3(0f, 5.4f, 0f), new Vector3(4.2f, 3.8f, 2.6f), body));
        Body(RenPrim(PrimitiveType.Sphere, "Hlava", model.transform, new Vector3(0f, 8f, 0.2f), new Vector3(1.8f, 1.7f, 1.8f), body));
        Body(RenPrim(PrimitiveType.Capsule, "Ruka_L", model.transform, new Vector3(-3f, 5f, 0.3f), new Vector3(1.2f, 2.2f, 1.2f), body));
        Body(RenPrim(PrimitiveType.Capsule, "Ruka_R", model.transform, new Vector3(3f, 5f, 0.3f), new Vector3(1.2f, 2.2f, 1.2f), body));
        Vein(RenPrim(PrimitiveType.Cube, "Zila_hrud", model.transform, new Vector3(0f, 5.4f, 1.32f), new Vector3(1.2f, 3f, 0.08f), vein));
        Vein(RenPrim(PrimitiveType.Cube, "Zila_L", model.transform, new Vector3(-1.5f, 5.2f, 1.32f), new Vector3(0.18f, 2.4f, 0.06f), vein));
        Vein(RenPrim(PrimitiveType.Cube, "Zila_R", model.transform, new Vector3(1.5f, 5.2f, 1.32f), new Vector3(0.18f, 2.4f, 0.06f), vein));
        Vein(RenPrim(PrimitiveType.Sphere, "Jadro", model.transform, new Vector3(0f, 4.4f, 1.2f), new Vector3(0.9f, 0.9f, 0.5f), vein));
        Vein(RenPrim(PrimitiveType.Cube, "Pazur_L", model.transform, new Vector3(-3f, 3.4f, 0.8f), new Vector3(0.3f, 1f, 0.3f), vein));
        Vein(RenPrim(PrimitiveType.Cube, "Pazur_R", model.transform, new Vector3(3f, 3.4f, 0.8f), new Vector3(0.3f, 1f, 0.3f), vein));
        Vein(RenPrim(PrimitiveType.Cylinder, "Koruna_plamen", model.transform, new Vector3(0f, 9.2f, 0.2f), new Vector3(0.9f, 0.6f, 0.9f), vein));

        rig.fist = RenAnchor("Pazur", model.transform, new Vector3(3f, 3.4f, 1f));
        rig.cockpit = RenAnchor("Jadro_kotva", model.transform, new Vector3(0f, 4.4f, 1.3f));
        rig.ventRenderers = veinR.ToArray();   // žily svietia samostatne (blesk telegrafu ich neprepisuje)

        var so = new SerializedObject(ignis);
        so.FindProperty("rig").objectReferenceValue = rig;
        so.FindProperty("safeDisc").objectReferenceValue = safeDisc.transform;
        SerializedProperty br = so.FindProperty("bodyRenderers");
        br.arraySize = bodyR.Count;
        for (int i = 0; i < bodyR.Count; i++) br.GetArrayElementAtIndex(i).objectReferenceValue = bodyR[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        var spawn = RenAnchor("Spawn_hraca", root.transform, new Vector3(0f, 0.6f, -14f));
        var arena = root.AddComponent<EliteArena>();
        var aso = new SerializedObject(arena);
        aso.FindProperty("bossId").stringValue = "ignis";
        aso.FindProperty("speaker").stringValue = "Lord Ignis";
        SerializedProperty lines = aso.FindProperty("introLines");
        string[] text =
        {
            "Chladný odpad. Držíš sa ľudskej slabosti ako vši kožušiny.",
            "Neprišiel som za Arakanim. Prišiel som spáliť to mesto do posledného komína.",
            "Postav sa mi, ak chceš. Podlaha pod tebou už horí."
        };
        lines.arraySize = text.Length;
        for (int i = 0; i < text.Length; i++) lines.GetArrayElementAtIndex(i).stringValue = text[i];
        aso.FindProperty("boss").objectReferenceValue = ignis;
        aso.FindProperty("playerSpawn").objectReferenceValue = spawn;
        aso.ApplyModifiedPropertiesWithoutUndo();

        Vector3 pad = FindEliteLiftSpot("Zem_Bane");
        Material padMat = RenMat("Ren_Pad", new Color(0.1f, 0.6f, 0.9f), 0.2f, 0.5f);
        var entrance = new GameObject("Vytah_Ignis_vstup");
        entrance.transform.SetParent(root.transform, false);
        entrance.transform.position = pad;
        RenPrim(PrimitiveType.Cylinder, "Plosina", entrance.transform, Vector3.zero, new Vector3(3f, 0.1f, 3f), padMat);
        var inPad = entrance.AddComponent<EliteLiftPad>();
        var ipso = new SerializedObject(inPad);
        ipso.FindProperty("arena").objectReferenceValue = arena;
        ipso.FindProperty("label").stringValue = "Lord Ignis";
        ipso.ApplyModifiedPropertiesWithoutUndo();

        var exit = new GameObject("Vytah_Ignis_vystup");
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
        Debug.Log("Aréna Ignisa postavená na " + origin + ", vstupný výťah na " + pad + ". Ulož scénu.");
    }

    // ---------- Testy v Play móde ----------

    static IgnisBoss Ignis() { EliteArena a = EliteArena.Find("ignis"); return a != null ? a.Boss as IgnisBoss : null; }

    [MenuItem("Progeny/Boss/Ignis: test - začať boj hneď", false, 551)]
    public static void IgnisTestStart() { EliteArena a = EliteArena.Find("ignis"); if (a != null) a.DebugStartImmediately(); }

    [MenuItem("Progeny/Boss/Ignis: test - fáza 2 (70 %)", false, 552)]
    public static void IgnisTestPhase2() { Ignis()?.DebugSetHealthFraction(0.69f); }

    [MenuItem("Progeny/Boss/Ignis: test - fáza 3 (40 %)", false, 553)]
    public static void IgnisTestPhase3() { Ignis()?.DebugSetHealthFraction(0.39f); }

    [MenuItem("Progeny/Boss/Ignis: test - finišer (10 %)", false, 554)]
    public static void IgnisTestFinisher() { Ignis()?.DebugSetHealthFraction(0.1f); }

    [MenuItem("Progeny/Boss/Ignis: test - ochladiť", false, 555)]
    public static void IgnisTestCool() { Ignis()?.Cool(); }

    [MenuItem("Progeny/Boss/Ignis: test - začať boj hneď", true)]
    [MenuItem("Progeny/Boss/Ignis: test - fáza 2 (70 %)", true)]
    [MenuItem("Progeny/Boss/Ignis: test - fáza 3 (40 %)", true)]
    [MenuItem("Progeny/Boss/Ignis: test - finišer (10 %)", true)]
    [MenuItem("Progeny/Boss/Ignis: test - ochladiť", true)]
    static bool IgnisPlayOnly() { return Application.isPlaying && EliteArena.Find("ignis") != null; }
}
