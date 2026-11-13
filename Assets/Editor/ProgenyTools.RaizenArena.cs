using UnityEditor;
using UnityEngine;

/// <summary>Aréna Lorda Raizena (neónová strecha v búrke) a dočasný model z primitív. Model sa vymení cez BossModelRig.</summary>
public static partial class ProgenyTools
{
    const string RaizenArenaName = "Arena_Raizen";

    [MenuItem("Progeny/Boss/Raizen: postaviť arénu a model", false, 530)]
    public static void BuildRaizenArena()
    {
        GameObject old = GameObject.Find(RaizenArenaName);
        Vector3 origin = old != null ? old.transform.position : new Vector3(1500f, 60f, 1250f);
        if (old != null) Object.DestroyImmediate(old);

        Material floor = RenMat("Raizen_Floor", new Color(0.07f, 0.08f, 0.12f), 0.3f, 0.6f);
        Material grid = RenMat("Raizen_Grid", new Color(0.1f, 0.3f, 0.45f), 0f, 0.3f, true);
        Material pylon = RenMat("Raizen_Pylon", new Color(0.2f, 0.22f, 0.3f), 0.7f, 0.5f, true);
        Material stone = RenMat("Raizen_Stone", new Color(0.4f, 0.36f, 0.3f), 0f, 0.1f);
        Material body = RenMat("Raizen_Body", new Color(0.12f, 0.13f, 0.2f), 0.5f, 0.6f, true);
        Material neon = RenMat("Raizen_Neon", new Color(0.3f, 0.8f, 1f), 0f, 0.9f, true);
        Material horn = RenMat("Raizen_Horn", new Color(0.8f, 0.95f, 1f), 0f, 0.9f, true);

        var root = new GameObject(RaizenArenaName);
        root.transform.position = origin;

        // Strecha + neónová mriežka + steny proti pádu
        RenPrim(PrimitiveType.Cube, "Strecha", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(84f, 1f, 84f), floor, true);
        for (int i = -4; i <= 4; i++)
        {
            RenPrim(PrimitiveType.Cube, "Mriezka_x" + i, root.transform, new Vector3(i * 8f, 0.02f, 0f), new Vector3(0.12f, 0.03f, 84f), grid);
            RenPrim(PrimitiveType.Cube, "Mriezka_z" + i, root.transform, new Vector3(0f, 0.02f, i * 8f), new Vector3(84f, 0.03f, 0.12f), grid);
        }
        for (int i = 0; i < 4; i++)
        {
            bool x = i < 2;
            float s = (i % 2 == 0 ? 1f : -1f) * 42f;
            var wall = new GameObject("Stena_" + i);
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = x ? new Vector3(s + Mathf.Sign(s), 4f, 0f) : new Vector3(0f, 4f, s + Mathf.Sign(s));
            wall.AddComponent<BoxCollider>().size = x ? new Vector3(2f, 10f, 86f) : new Vector3(86f, 10f, 2f);
        }

        // Neónové pylóny po obvode (vedenie, po ktorom sa Raizen sťahuje)
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 38f;
            GameObject py = RenPrim(PrimitiveType.Cylinder, "Pylon_" + i, root.transform, p + Vector3.up * 7f, new Vector3(1.4f, 7f, 1.4f), pylon);
            RenPrim(PrimitiveType.Sphere, "Cievka", py.transform, new Vector3(0f, 1.05f, 0f), new Vector3(1.6f, 0.25f, 1.6f), neon);
            var lg = new GameObject("Svetlo");
            lg.transform.SetParent(py.transform, false);
            lg.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 38f;
            l.intensity = 2.2f;
            l.color = new Color(0.4f, 0.8f, 1f);
            l.shadows = LightShadows.None;
        }

        // Kamenné zábrany: kryt pred bleskami; keď do nich Raizen vrazí, uzemní sa
        for (int i = 0; i < 6; i++)
        {
            float a = (30f + i * 60f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 14f;
            GameObject st = RenPrim(PrimitiveType.Cube, "Kamenna_zabrana_" + i, root.transform, p + Vector3.up * 2f, new Vector3(3.4f, 4f, 3.4f), stone, true);
            st.transform.rotation = Quaternion.Euler(0f, 17f + i * 31f, 0f);
            st.AddComponent<ArenaCover>();
        }

        // Raizen: štíhly kyber-démon s rohom z blesku a neónovou armatúrou
        var boss = new GameObject("Raizen");
        boss.transform.SetParent(root.transform, false);
        boss.transform.localPosition = new Vector3(0f, 0f, 24f);   // mimo kamenných zábran (polomer 14)
        boss.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        var bc = boss.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 1.9f, 0f);
        bc.size = new Vector3(1.6f, 3.8f, 1.2f);
        boss.AddComponent<Damageable>();
        var raizen = boss.AddComponent<RaizenBoss>();
        var model = new GameObject("Model");
        model.transform.SetParent(boss.transform, false);
        var rig = model.AddComponent<BossModelRig>();

        RenPrim(PrimitiveType.Capsule, "Noha_L", model.transform, new Vector3(-0.35f, 0.95f, 0f), new Vector3(0.45f, 0.95f, 0.45f), body);
        RenPrim(PrimitiveType.Capsule, "Noha_R", model.transform, new Vector3(0.35f, 0.95f, 0f), new Vector3(0.45f, 0.95f, 0.45f), body);
        RenPrim(PrimitiveType.Cube, "Trup", model.transform, new Vector3(0f, 2.5f, 0f), new Vector3(1.3f, 1.4f, 0.7f), body);
        RenPrim(PrimitiveType.Cube, "Pas_neon", model.transform, new Vector3(0f, 2.5f, 0.36f), new Vector3(0.18f, 1.2f, 0.05f), neon);
        RenPrim(PrimitiveType.Sphere, "Hlava", model.transform, new Vector3(0f, 3.5f, 0f), new Vector3(0.7f, 0.75f, 0.7f), body);
        GameObject hornObj = RenPrim(PrimitiveType.Cylinder, "Roh", model.transform, new Vector3(0f, 4.2f, 0.05f), new Vector3(0.14f, 0.45f, 0.14f), horn);
        hornObj.transform.localRotation = Quaternion.Euler(-12f, 0f, 0f);
        RenPrim(PrimitiveType.Capsule, "Rameno_L", model.transform, new Vector3(-0.95f, 2.5f, 0f), new Vector3(0.32f, 0.8f, 0.32f), body);
        RenPrim(PrimitiveType.Capsule, "Rameno_R", model.transform, new Vector3(0.95f, 2.5f, 0f), new Vector3(0.32f, 0.8f, 0.32f), body);
        RenPrim(PrimitiveType.Cube, "Armatura_L", model.transform, new Vector3(-0.95f, 2.4f, 0.2f), new Vector3(0.1f, 0.8f, 0.1f), neon);
        RenPrim(PrimitiveType.Cube, "Armatura_R", model.transform, new Vector3(0.95f, 2.4f, 0.2f), new Vector3(0.1f, 0.8f, 0.1f), neon);

        rig.muzzleRight = RenAnchor("Ustie_R", model.transform, new Vector3(0.95f, 2.9f, 0.7f));
        rig.muzzleLeft = RenAnchor("Ustie_L", model.transform, new Vector3(-0.95f, 2.9f, 0.7f));
        rig.cockpit = hornObj.transform;
        var so = new SerializedObject(raizen);
        so.FindProperty("rig").objectReferenceValue = rig;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Aréna + výťahy
        var spawn = RenAnchor("Spawn_hraca", root.transform, new Vector3(0f, 0.6f, -20f));
        var arena = root.AddComponent<EliteArena>();
        var aso = new SerializedObject(arena);
        aso.FindProperty("bossId").stringValue = "raizen";
        aso.FindProperty("speaker").stringValue = "Lord Raizen";
        SerializedProperty lines = aso.FindProperty("introLines");
        string[] text =
        {
            "Polokrvník. Arakani ti dal moc, ktorú si nezaslúžiš. Ja som si ju vybojoval.",
            "Stojím po jeho pravici, kým ty sa ešte učíš stáť. Dnes si zmeriame rebríček.",
            "Skús ma zasiahnuť. Nikto sa ešte nedotkol blesku, ktorý nechce byť dotknutý."
        };
        lines.arraySize = text.Length;
        for (int i = 0; i < text.Length; i++) lines.GetArrayElementAtIndex(i).stringValue = text[i];
        aso.FindProperty("boss").objectReferenceValue = raizen;
        aso.FindProperty("playerSpawn").objectReferenceValue = spawn;
        aso.ApplyModifiedPropertiesWithoutUndo();

        Vector3 pad = FindEliteLiftSpot("Zem_Centrum");
        Material padMat = RenMat("Ren_Pad", new Color(0.1f, 0.6f, 0.9f), 0.2f, 0.5f);
        var entrance = new GameObject("Vytah_Raizen_vstup");
        entrance.transform.SetParent(root.transform, false);
        entrance.transform.position = pad;
        RenPrim(PrimitiveType.Cylinder, "Plosina", entrance.transform, Vector3.zero, new Vector3(3f, 0.1f, 3f), padMat);
        var inPad = entrance.AddComponent<EliteLiftPad>();
        var ipso = new SerializedObject(inPad);
        ipso.FindProperty("arena").objectReferenceValue = arena;
        ipso.FindProperty("label").stringValue = "Lord Raizen";
        ipso.ApplyModifiedPropertiesWithoutUndo();

        var exit = new GameObject("Vytah_Raizen_vystup");
        exit.transform.SetParent(root.transform, false);
        exit.transform.localPosition = new Vector3(0f, 0.05f, -36f);
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
        Debug.Log("Aréna Raizena postavená na " + origin + ", vstupný výťah na " + pad + ". Ulož scénu.");
    }

    // Voľné miesto na zemi pri strede danej zóny (vstupné výťahy elít)
    static Vector3 FindEliteLiftSpot(string zoneObject)
    {
        GameObject zem = GameObject.Find(zoneObject);
        Vector3 c = zem != null ? zem.transform.position : new Vector3(1500f, 0f, 1000f);
        for (float r = 8f; r < 120f; r += 6f)
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit h, 120f)) continue;
                if (Physics.CheckSphere(h.point + Vector3.up * 2.5f, 3f)) continue;
                return h.point + Vector3.up * 0.1f;
            }
        // Nič voľné: aspoň na zem v strede zóny (nie vo vzduchu)
        if (Physics.Raycast(c + Vector3.up * 200f, Vector3.down, out RaycastHit g, 400f)) return g.point + Vector3.up * 0.1f;
        return c;
    }

    // ---------- Testy v Play móde ----------

    static RaizenBoss Raizen() { EliteArena a = EliteArena.Find("raizen"); return a != null ? a.Boss as RaizenBoss : null; }

    [MenuItem("Progeny/Boss/Raizen: test - začať boj hneď", false, 531)]
    public static void RaizenTestStart() { EliteArena a = EliteArena.Find("raizen"); if (a != null) a.DebugStartImmediately(); }

    [MenuItem("Progeny/Boss/Raizen: test - fáza 2 (70 %)", false, 532)]
    public static void RaizenTestPhase2() { Raizen()?.DebugSetHealthFraction(0.69f); }

    [MenuItem("Progeny/Boss/Raizen: test - fáza 3 (40 %)", false, 533)]
    public static void RaizenTestPhase3() { Raizen()?.DebugSetHealthFraction(0.39f); }

    [MenuItem("Progeny/Boss/Raizen: test - finišer (10 %)", false, 534)]
    public static void RaizenTestFinisher() { Raizen()?.DebugSetHealthFraction(0.1f); }

    [MenuItem("Progeny/Boss/Raizen: test - začať boj hneď", true)]
    [MenuItem("Progeny/Boss/Raizen: test - fáza 2 (70 %)", true)]
    [MenuItem("Progeny/Boss/Raizen: test - fáza 3 (40 %)", true)]
    [MenuItem("Progeny/Boss/Raizen: test - finišer (10 %)", true)]
    static bool RaizenPlayOnly() { return Application.isPlaying && EliteArena.Find("raizen") != null; }
}
