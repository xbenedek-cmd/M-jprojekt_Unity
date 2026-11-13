using UnityEditor;
using UnityEngine;

/// <summary>
/// Postaví arénu Veliteľa Rena (strecha prekladiska) a dočasného mecha z primitív. Model sa dá vymeniť: stačí v komponente BossModelRig
/// priradiť kotvy (ústia, kokpit, plameňomet, štít, ventily) a presunúť BossHitZone na kotvu chrbta.
/// </summary>
public static partial class ProgenyTools
{
    const string RenArenaName = "Arena_Ren";

    static Material RenMat(string name, Color color, float metallic = 0.4f, float smooth = 0.4f, bool emission = false, bool transparent = false)
    {
        string dir = "Assets/Materials/Ren";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Materials", "Ren");
        string path = dir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, path); }
        m.color = color;
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smooth);
        if (emission) { m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; m.SetColor("_EmissionColor", Color.black); }
        if (transparent)
        {
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHABLEND_ON");
            m.renderQueue = 3000;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject RenPrim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool keepCollider = false)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    static Transform RenAnchor(string name, Transform parent, Vector3 pos)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        return g.transform;
    }

    [MenuItem("Progeny/Boss/Ren: postaviť arénu a mecha", false, 520)]
    public static void BuildRenArena()
    {
        GameObject old = GameObject.Find(RenArenaName);
        Vector3 origin = old != null ? old.transform.position : new Vector3(1500f, 60f, 1100f);
        if (old != null) Object.DestroyImmediate(old);

        Material floor = RenMat("Ren_Floor", new Color(0.16f, 0.17f, 0.19f), 0.1f, 0.3f);
        Material stripe = RenMat("Ren_Stripe", new Color(0.9f, 0.75f, 0.1f), 0f, 0.2f);
        Material tower = RenMat("Ren_Tower", new Color(0.42f, 0.44f, 0.47f), 0.6f, 0.35f);
        Material pipe = RenMat("Ren_Pipe", new Color(0.3f, 0.33f, 0.36f), 0.7f, 0.4f);
        Material mecha = RenMat("Ren_Mecha", new Color(0.32f, 0.35f, 0.4f), 0.7f, 0.5f, true);
        Material mechaDark = RenMat("Ren_MechaDark", new Color(0.12f, 0.13f, 0.15f), 0.6f, 0.4f, true);
        Material glass = RenMat("Ren_Cockpit", new Color(0.1f, 0.5f, 0.7f), 0.2f, 0.9f, true);
        Material vent = RenMat("Ren_Vent", new Color(0.25f, 0.06f, 0.05f), 0.3f, 0.3f, true);
        Material shieldMat = RenMat("Ren_Shield", new Color(0.2f, 0.7f, 1f, 0.22f), 0f, 0.8f, false, true);
        Material droneMat = RenMat("Ren_Drone", new Color(0.12f, 0.14f, 0.16f), 0.6f, 0.5f, true);

        var root = new GameObject(RenArenaName);
        root.transform.position = origin;

        // Podlaha a okraj
        RenPrim(PrimitiveType.Cube, "Strecha", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(84f, 1f, 84f), floor, true);
        for (int i = 0; i < 4; i++)
        {
            bool x = i < 2;
            float s = (i % 2 == 0 ? 1f : -1f) * 41.5f;
            RenPrim(PrimitiveType.Cube, "Pas_" + i, root.transform, x ? new Vector3(s, 0.02f, 0f) : new Vector3(0f, 0.02f, s), x ? new Vector3(0.6f, 0.05f, 84f) : new Vector3(84f, 0.05f, 0.6f), stripe);
            // Neviditeľná stena proti pádu
            var wall = new GameObject("Stena_" + i);
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = x ? new Vector3(s + Mathf.Sign(s) * 1f, 4f, 0f) : new Vector3(0f, 4f, s + Mathf.Sign(s) * 1f);
            wall.AddComponent<BoxCollider>().size = x ? new Vector3(2f, 10f, 86f) : new Vector3(86f, 10f, 2f);
        }
        RenPrim(PrimitiveType.Cylinder, "Znacka_stred", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(26f, 0.02f, 26f), RenMat("Ren_Mark", new Color(0.22f, 0.23f, 0.26f), 0f, 0.2f));

        // Kryty po obvode: chladiace veže + potrubia medzi nimi
        for (int i = 0; i < 8; i++)
        {
            float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 28f;
            GameObject t = RenPrim(PrimitiveType.Cylinder, "Chladiaca_vez_" + i, root.transform, p + Vector3.up * 4.5f, new Vector3(5f, 4.5f, 5f), tower, true);
            RenPrim(PrimitiveType.Cylinder, "Ventilator", t.transform, new Vector3(0f, 1.02f, 0f), new Vector3(0.8f, 0.03f, 0.8f), mechaDark);
            t.AddComponent<ArenaCover>();

            float b = (i * 45f) * Mathf.Deg2Rad;
            Vector3 q = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * 29f;
            GameObject pp = RenPrim(PrimitiveType.Cylinder, "Potrubie_" + i, root.transform, q + Vector3.up * 1.4f, new Vector3(2.6f, 5.2f, 2.6f), pipe, true);
            pp.transform.rotation = Quaternion.Euler(0f, -(i * 45f + 90f), 90f);   // leží tangenciálne
            pp.AddComponent<ArenaCover>();
        }

        // Osvetlenie strechy (nočné mesto, reflektory na vežiach)
        for (int i = 0; i < 4; i++)
        {
            var lg = new GameObject("Reflektor_" + i);
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = new Vector3(i % 2 == 0 ? -22f : 22f, 16f, i < 2 ? -22f : 22f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 55f;
            l.intensity = 3.5f;
            l.color = new Color(0.85f, 0.9f, 1f);
            l.shadows = LightShadows.None;
        }

        // Mecha
        var boss = new GameObject("Ren_Mecha");
        boss.transform.SetParent(root.transform, false);
        boss.transform.localPosition = new Vector3(0f, 0f, 9f);
        boss.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        var bc = boss.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 3.3f, 0f);
        bc.size = new Vector3(4.4f, 6.6f, 3f);
        boss.AddComponent<Damageable>();
        var ren = boss.AddComponent<RenBoss>();

        var model = new GameObject("Model");
        model.transform.SetParent(boss.transform, false);
        var rig = model.AddComponent<BossModelRig>();

        RenPrim(PrimitiveType.Cube, "Noha_L", model.transform, new Vector3(-1.1f, 1.5f, 0f), new Vector3(1.2f, 3f, 1.4f), mechaDark);
        RenPrim(PrimitiveType.Cube, "Noha_R", model.transform, new Vector3(1.1f, 1.5f, 0f), new Vector3(1.2f, 3f, 1.4f), mechaDark);
        RenPrim(PrimitiveType.Cube, "Trup", model.transform, new Vector3(0f, 4.3f, 0f), new Vector3(3.6f, 2.6f, 2.4f), mecha);
        RenPrim(PrimitiveType.Cube, "Panva", model.transform, new Vector3(0f, 3.0f, 0f), new Vector3(2.4f, 0.8f, 1.8f), mechaDark);
        GameObject head = RenPrim(PrimitiveType.Sphere, "Kokpit", model.transform, new Vector3(0f, 6.0f, 0.3f), new Vector3(1.7f, 1.4f, 1.7f), glass);
        RenPrim(PrimitiveType.Cube, "Rameno_L", model.transform, new Vector3(-2.4f, 3.4f, 0.2f), new Vector3(0.9f, 2.4f, 0.9f), mecha);
        GameObject armR = RenPrim(PrimitiveType.Cube, "Rameno_R", model.transform, new Vector3(2.4f, 3.4f, 0.2f), new Vector3(0.9f, 2.4f, 0.9f), mecha);
        GameObject canL = RenPrim(PrimitiveType.Cylinder, "Laser_L", model.transform, new Vector3(-2.3f, 5.2f, 0.9f), new Vector3(0.6f, 1.2f, 0.6f), mechaDark);
        GameObject canR = RenPrim(PrimitiveType.Cylinder, "Laser_R", model.transform, new Vector3(2.3f, 5.2f, 0.9f), new Vector3(0.6f, 1.2f, 0.6f), mechaDark);
        canL.transform.localRotation = canR.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        Transform mL = RenAnchor("Ustie_L", model.transform, new Vector3(-2.3f, 5.2f, 2.2f));
        Transform mR = RenAnchor("Ustie_R", model.transform, new Vector3(2.3f, 5.2f, 2.2f));
        Transform flame = RenAnchor("Plamenomet", model.transform, new Vector3(2.4f, 2.6f, 1.3f));
        Transform fist = RenAnchor("Pesť", model.transform, new Vector3(2.4f, 2.2f, 0.8f));

        // Chladiace ventily na chrbte + zásahová zóna
        Transform ventAnchor = RenAnchor("Ventily", model.transform, new Vector3(0f, 4.6f, -1.7f));
        RenPrim(PrimitiveType.Cube, "Batoh", model.transform, new Vector3(0f, 4.6f, -1.4f), new Vector3(2.6f, 2.2f, 0.6f), mechaDark);
        GameObject v1 = RenPrim(PrimitiveType.Cube, "Ventil_L", ventAnchor, new Vector3(-0.7f, 0f, -0.15f), new Vector3(0.8f, 1.5f, 0.2f), vent);
        GameObject v2 = RenPrim(PrimitiveType.Cube, "Ventil_R", ventAnchor, new Vector3(0.7f, 0f, -0.15f), new Vector3(0.8f, 1.5f, 0.2f), vent);
        var zone = ventAnchor.gameObject.AddComponent<BossHitZone>();
        zone.zoneId = "vent";
        zone.radius = 2.8f;
        zone.multiplier = 3f;

        GameObject shield = RenPrim(PrimitiveType.Sphere, "Stit", model.transform, new Vector3(0f, 3.5f, 0f), new Vector3(9f, 9f, 9f), shieldMat);
        shield.SetActive(false);

        rig.muzzleLeft = mL;
        rig.muzzleRight = mR;
        rig.cockpit = head.transform;
        rig.flame = flame;
        rig.fist = fist;
        rig.shieldVisual = shield;
        rig.ventRenderers = new[] { v1.GetComponent<Renderer>(), v2.GetComponent<Renderer>() };
        _ = armR;

        var so = new SerializedObject(ren);
        so.FindProperty("rig").objectReferenceValue = rig;
        so.FindProperty("droneMaterial").objectReferenceValue = droneMat;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Arena + výťahy
        var spawn = RenAnchor("Spawn_hraca", root.transform, new Vector3(0f, 0.6f, -15f));
        spawn.localRotation = Quaternion.identity;
        var arena = root.AddComponent<RenArena>();
        var aso = new SerializedObject(arena);
        aso.FindProperty("boss").objectReferenceValue = ren;
        aso.FindProperty("playerSpawn").objectReferenceValue = spawn;
        aso.ApplyModifiedPropertiesWithoutUndo();

        Vector3 pad = FindRenPadSpot();
        var entrance = new GameObject("Vytah_Ren_vstup");
        entrance.transform.SetParent(root.transform, false);
        entrance.transform.position = pad;
        RenPrim(PrimitiveType.Cylinder, "Plosina", entrance.transform, Vector3.zero, new Vector3(3f, 0.1f, 3f), RenMat("Ren_Pad", new Color(0.1f, 0.6f, 0.9f), 0.2f, 0.5f));
        entrance.AddComponent<RenLiftPad>();

        var exit = new GameObject("Vytah_Ren_vystup");
        exit.transform.SetParent(root.transform, false);
        exit.transform.localPosition = new Vector3(0f, 0.05f, -32f);
        RenPrim(PrimitiveType.Cylinder, "Plosina", exit.transform, Vector3.zero, new Vector3(3f, 0.1f, 3f), RenMat("Ren_Pad", new Color(0.1f, 0.6f, 0.9f), 0.2f, 0.5f));
        var exitPad = exit.AddComponent<RenLiftPad>();
        var padSo = new SerializedObject(exitPad);
        padSo.FindProperty("isExit").boolValue = true;
        var back = RenAnchor("Navrat_dole", entrance.transform, new Vector3(2.5f, 0.3f, 0f));
        padSo.FindProperty("destination").objectReferenceValue = back;
        padSo.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("Aréna Rena postavená na " + origin + ", vstupný výťah na " + pad + ". Ulož scénu.");
    }

    // Voľné miesto na zemi v zóne Megakorporácie pre vstupný výťah
    static Vector3 FindRenPadSpot()
    {
        GameObject zem = GameObject.Find("Zem_Megakorporacia");
        Vector3 c = zem != null ? zem.transform.position : new Vector3(1755f, 0f, 1371f);
        for (float r = 5f; r < 100f; r += 5f)
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f * Mathf.Deg2Rad;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit h, 120f)) continue;
                if (Physics.CheckSphere(h.point + Vector3.up * 2.5f, 3f)) continue;
                return h.point + Vector3.up * 0.1f;
            }
        return c + Vector3.up * 12f;
    }

    // ---------- Testy v Play móde ----------

    static RenBoss Ren() => RenArena.Instance != null ? RenArena.Instance.Boss : null;

    [MenuItem("Progeny/Boss/Ren: test - začať boj hneď", false, 521)]
    public static void RenTestStart() { if (RenArena.Instance != null) RenArena.Instance.DebugStartImmediately(); }

    [MenuItem("Progeny/Boss/Ren: test - fáza 2 (70 %)", false, 522)]
    public static void RenTestPhase2() { Ren()?.DebugSetHealthFraction(0.69f); }

    [MenuItem("Progeny/Boss/Ren: test - fáza 3 (40 %)", false, 523)]
    public static void RenTestPhase3() { Ren()?.DebugSetHealthFraction(0.39f); }

    [MenuItem("Progeny/Boss/Ren: test - finišer (10 %)", false, 524)]
    public static void RenTestFinisher() { Ren()?.DebugSetHealthFraction(0.1f); }

    [MenuItem("Progeny/Boss/Ren: test - začať boj hneď", true)]
    [MenuItem("Progeny/Boss/Ren: test - fáza 2 (70 %)", true)]
    [MenuItem("Progeny/Boss/Ren: test - fáza 3 (40 %)", true)]
    [MenuItem("Progeny/Boss/Ren: test - finišer (10 %)", true)]
    static bool RenPlayOnly() { return Application.isPlaying && RenArena.Instance != null; }
}
