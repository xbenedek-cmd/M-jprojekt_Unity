using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Aréna Arakaniho (Prapôvodné Jadro: kruhová plošina nad priepasťou, stúpa dym) a dočasný veľký model z primitív. Model sa vymení cez BossModelRig.</summary>
public static partial class ProgenyTools
{
    const string ArakaniArenaName = "Arena_Arakani";

    [MenuItem("Progeny/Boss/Arakani: postaviť arénu a model", false, 580)]
    public static void BuildArakaniArena()
    {
        GameObject old = GameObject.Find(ArakaniArenaName);
        Vector3 origin = old != null ? old.transform.position : new Vector3(1500f, 60f, 2000f);
        if (old != null) Object.DestroyImmediate(old);

        Material floor = RenMat("Arakani_Floor", new Color(0.1f, 0.08f, 0.1f), 0.3f, 0.5f);
        Material ring = RenMat("Arakani_Ring", new Color(0.6f, 0.1f, 0.25f), 0f, 0.5f, true);
        Material abyss = RenMat("Arakani_Abyss", new Color(0.02f, 0.01f, 0.02f), 0f, 0f);
        Material pillar = RenMat("Arakani_Pillar", new Color(0.18f, 0.16f, 0.22f), 0.5f, 0.5f, true);
        Material spark = RenMat("Arakani_Spark", new Color(0.4f, 0.85f, 1f), 0f, 0.9f, true);
        Material rock = RenMat("Arakani_Rock", new Color(0.3f, 0.28f, 0.28f), 0f, 0.1f);
        Material body = RenMat("Arakani_Body", new Color(0.07f, 0.05f, 0.08f), 0.4f, 0.4f, true);
        Material core = RenMat("Arakani_Core", new Color(0.45f, 0.2f, 0.8f), 0f, 0.8f, true);

        Material smoke = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Ren/Arakani_Smoke.mat");
        if (smoke == null)
        {
            Shader sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            smoke = new Material(sh);
            AssetDatabase.CreateAsset(smoke, "Assets/Materials/Ren/Arakani_Smoke.mat");
        }
        smoke.color = new Color(0.25f, 0.22f, 0.28f, 0.35f);
        EditorUtility.SetDirty(smoke);

        var root = new GameObject(ArakaniArenaName);
        root.transform.position = origin;

        // Plošina nad priepasťou (polomer 40), svietiaci okraj a tmavá hĺbka
        RenPrim(PrimitiveType.Cylinder, "Plosina", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(80f, 0.5f, 80f), floor, true);
        RenPrim(PrimitiveType.Cylinder, "Okraj", root.transform, new Vector3(0f, 0.02f, 0f), new Vector3(79f, 0.02f, 79f), ring);
        RenPrim(PrimitiveType.Cylinder, "Plosina_vrch", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(76f, 0.02f, 76f), floor);
        RenPrim(PrimitiveType.Cube, "Priepast", root.transform, new Vector3(0f, -90f, 0f), new Vector3(400f, 1f, 400f), abyss);
        for (int i = 0; i < 16; i++)
        {
            float a = i * 22.5f * Mathf.Deg2Rad;
            var wall = new GameObject("Stena_" + i);
            wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 41.5f + Vector3.up * 5f;
            wall.transform.localRotation = Quaternion.Euler(0f, -(i * 22.5f + 90f), 0f);
            wall.AddComponent<BoxCollider>().size = new Vector3(17f, 12f, 2f);
        }

        // Dym stúpajúci z priepasti okolo plošiny
        for (int i = 0; i < 10; i++)
        {
            float a = i * 36f * Mathf.Deg2Rad;
            var sg = new GameObject("Dym_" + i);
            sg.transform.SetParent(root.transform, false);
            sg.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 46f + Vector3.down * 4f;
            sg.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = sg.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 9f;
            main.startSpeed = 3.5f;
            main.startSize = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startColor = new Color(0.3f, 0.26f, 0.32f, 0.4f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40;
            var em = ps.emission;
            em.rateOverTime = 4f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 6f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.4f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            sg.GetComponent<ParticleSystemRenderer>().sharedMaterial = smoke;
        }

        // Stĺpy po obvode (blesky preskakujú medzi nimi) a kamenné kryty bližšie k stredu
        var pillarList = new List<Transform>();
        for (int i = 0; i < 6; i++)
        {
            float a = (i * 60f + 10f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 30f;
            GameObject pl = RenPrim(PrimitiveType.Cylinder, "Stlp_" + i, root.transform, p + Vector3.up * 8f, new Vector3(2.4f, 8f, 2.4f), pillar, true);
            RenPrim(PrimitiveType.Sphere, "Cievka", pl.transform, new Vector3(0f, 1.02f, 0f), new Vector3(1.5f, 0.12f, 1.5f), spark);
            pillarList.Add(pl.transform);
        }
        for (int i = 0; i < 6; i++)
        {
            float a = (i * 60f + 40f) * Mathf.Deg2Rad;
            Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 16f;
            GameObject st = RenPrim(PrimitiveType.Cube, "Kamen_" + i, root.transform, p + Vector3.up * 2f, new Vector3(3.6f, 4f, 3.6f), rock, true);
            st.transform.rotation = Quaternion.Euler(0f, 23f + i * 37f, 0f);
            st.AddComponent<ArenaCover>();
        }

        // Svetlá arény (Arakani ich v tieňovej fáze zhasína)
        var lights = new List<Light>();
        for (int i = 0; i < 4; i++)
        {
            float a = (i * 90f + 45f) * Mathf.Deg2Rad;
            var lg = new GameObject("Svetlo_" + i);
            lg.transform.SetParent(root.transform, false);
            lg.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 26f + Vector3.up * 14f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 60f;
            l.intensity = 2.6f;
            l.color = new Color(0.75f, 0.5f, 0.9f);
            l.shadows = LightShadows.None;
            lights.Add(l);
        }

        // Arakani: obrovská tmavá postava s rohmi a žiarivým jadrom (mierka 3x)
        var boss = new GameObject("Arakani");
        boss.transform.SetParent(root.transform, false);
        boss.transform.localPosition = new Vector3(0f, 0f, 14f);
        boss.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var rb = boss.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        var bc = boss.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 12f, 0f);
        bc.size = new Vector3(9f, 24f, 6f);
        boss.AddComponent<Damageable>();
        var arakani = boss.AddComponent<ArakaniBoss>();
        var model = new GameObject("Model");
        model.transform.SetParent(boss.transform, false);
        model.transform.localScale = Vector3.one * 3f;
        var rig = model.AddComponent<BossModelRig>();

        RenPrim(PrimitiveType.Capsule, "Noha_L", model.transform, new Vector3(-1f, 1.6f, 0f), new Vector3(1.3f, 1.7f, 1.3f), body);
        RenPrim(PrimitiveType.Capsule, "Noha_R", model.transform, new Vector3(1f, 1.6f, 0f), new Vector3(1.3f, 1.7f, 1.3f), body);
        RenPrim(PrimitiveType.Cube, "Trup", model.transform, new Vector3(0f, 4.4f, 0f), new Vector3(3.2f, 3.2f, 1.8f), body);
        RenPrim(PrimitiveType.Sphere, "Plece_L", model.transform, new Vector3(-2.2f, 5.7f, 0f), new Vector3(1.8f, 1.6f, 1.8f), body);
        RenPrim(PrimitiveType.Sphere, "Plece_R", model.transform, new Vector3(2.2f, 5.7f, 0f), new Vector3(1.8f, 1.6f, 1.8f), body);
        RenPrim(PrimitiveType.Capsule, "Ruka_L", model.transform, new Vector3(-2.5f, 3.9f, 0.3f), new Vector3(0.9f, 1.8f, 0.9f), body);
        RenPrim(PrimitiveType.Capsule, "Ruka_R", model.transform, new Vector3(2.5f, 3.9f, 0.3f), new Vector3(0.9f, 1.8f, 0.9f), body);
        RenPrim(PrimitiveType.Sphere, "Hlava", model.transform, new Vector3(0f, 6.6f, 0.1f), new Vector3(1.5f, 1.5f, 1.5f), body);
        GameObject hl = RenPrim(PrimitiveType.Cylinder, "Roh_L", model.transform, new Vector3(-0.8f, 7.8f, 0.1f), new Vector3(0.28f, 0.8f, 0.28f), body);
        GameObject hr = RenPrim(PrimitiveType.Cylinder, "Roh_R", model.transform, new Vector3(0.8f, 7.8f, 0.1f), new Vector3(0.28f, 0.8f, 0.28f), body);
        hl.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
        hr.transform.localRotation = Quaternion.Euler(0f, 0f, -25f);

        var veins = new List<Renderer>();
        veins.Add(RenPrim(PrimitiveType.Sphere, "Jadro", model.transform, new Vector3(0f, 4.4f, 0.95f), new Vector3(1.1f, 1.1f, 0.5f), core).GetComponent<Renderer>());
        veins.Add(RenPrim(PrimitiveType.Cube, "Zila_L", model.transform, new Vector3(-1f, 4.4f, 0.93f), new Vector3(0.12f, 2.4f, 0.05f), core).GetComponent<Renderer>());
        veins.Add(RenPrim(PrimitiveType.Cube, "Zila_R", model.transform, new Vector3(1f, 4.4f, 0.93f), new Vector3(0.12f, 2.4f, 0.05f), core).GetComponent<Renderer>());
        veins.Add(RenPrim(PrimitiveType.Sphere, "Oko_L", model.transform, new Vector3(-0.35f, 6.7f, 0.75f), new Vector3(0.25f, 0.12f, 0.1f), core).GetComponent<Renderer>());
        veins.Add(RenPrim(PrimitiveType.Sphere, "Oko_R", model.transform, new Vector3(0.35f, 6.7f, 0.75f), new Vector3(0.25f, 0.12f, 0.1f), core).GetComponent<Renderer>());

        rig.cockpit = RenAnchor("Jadro_kotva", model.transform, new Vector3(0f, 4.4f, 1f));
        rig.fist = RenAnchor("Pest", model.transform, new Vector3(2.5f, 2.8f, 0.8f));
        rig.ventRenderers = veins.ToArray();   // žily svietia farbou živlu

        var so = new SerializedObject(arakani);
        so.FindProperty("rig").objectReferenceValue = rig;
        SerializedProperty pp = so.FindProperty("pillars");
        pp.arraySize = pillarList.Count;
        for (int i = 0; i < pillarList.Count; i++) pp.GetArrayElementAtIndex(i).objectReferenceValue = pillarList[i];
        SerializedProperty lp = so.FindProperty("arenaLights");
        lp.arraySize = lights.Count;
        for (int i = 0; i < lights.Count; i++) lp.GetArrayElementAtIndex(i).objectReferenceValue = lights[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        // Aréna + výťahy
        var spawn = RenAnchor("Spawn_hraca", root.transform, new Vector3(0f, 0.6f, -30f));
        var arena = root.AddComponent<EliteArena>();
        var aso = new SerializedObject(arena);
        aso.FindProperty("bossId").stringValue = "arakani";
        aso.FindProperty("speaker").stringValue = "Arakani";
        SerializedProperty lines = aso.FindProperty("introLines");
        string[] text =
        {
            "Konečne stojíš predo mnou. Poznám ťa dlhšie, než poznáš seba.",
            "Všetky klany sú moje: blesk, tieň, oheň, krv. Postavím ti ich do cesty, jedného po druhom.",
            "Hlas, ktorý počuješ, bol vždy môj. Dnes mu uvidíš do tváre."
        };
        lines.arraySize = text.Length;
        for (int i = 0; i < text.Length; i++) lines.GetArrayElementAtIndex(i).stringValue = text[i];
        aso.FindProperty("boss").objectReferenceValue = arakani;
        aso.FindProperty("playerSpawn").objectReferenceValue = spawn;
        aso.ApplyModifiedPropertiesWithoutUndo();

        Vector3 pad = FindEliteLiftSpot("Zem_HradbaRozsirenie");
        Material padMat = RenMat("Ren_Pad", new Color(0.1f, 0.6f, 0.9f), 0.2f, 0.5f);
        var entrance = new GameObject("Vytah_Arakani_vstup");
        entrance.transform.SetParent(root.transform, false);
        entrance.transform.position = pad;
        RenPrim(PrimitiveType.Cylinder, "Plosina", entrance.transform, Vector3.zero, new Vector3(3f, 0.1f, 3f), padMat);
        var inPad = entrance.AddComponent<EliteLiftPad>();
        var ipso = new SerializedObject(inPad);
        ipso.FindProperty("arena").objectReferenceValue = arena;
        ipso.FindProperty("label").stringValue = "Arakani, Prapôvodné Jadro";
        ipso.ApplyModifiedPropertiesWithoutUndo();

        var exit = new GameObject("Vytah_Arakani_vystup");
        exit.transform.SetParent(root.transform, false);
        exit.transform.localPosition = new Vector3(0f, 0.1f, -37f);
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
        Debug.Log("Aréna Arakaniho postavená na " + origin + ", vstupný výťah na " + pad + ". Ulož scénu.");
    }

    // ---------- Testy v Play móde ----------

    static ArakaniBoss Arakani() { EliteArena a = EliteArena.Find("arakani"); return a != null ? a.Boss as ArakaniBoss : null; }

    [MenuItem("Progeny/Boss/Arakani: test - začať boj hneď", false, 581)]
    public static void ArakaniTestStart() { EliteArena a = EliteArena.Find("arakani"); if (a != null) a.DebugStartImmediately(); }

    [MenuItem("Progeny/Boss/Arakani: test - fáza 2 Oheň (80 %)", false, 582)]
    public static void ArakaniTestPhase2() { Arakani()?.DebugSetHealthFraction(0.79f); }

    [MenuItem("Progeny/Boss/Arakani: test - fáza 3 Blesk (60 %)", false, 583)]
    public static void ArakaniTestPhase3() { Arakani()?.DebugSetHealthFraction(0.59f); }

    [MenuItem("Progeny/Boss/Arakani: test - fáza 4 Krv (40 %)", false, 584)]
    public static void ArakaniTestPhase4() { Arakani()?.DebugSetHealthFraction(0.39f); }

    [MenuItem("Progeny/Boss/Arakani: test - záverečná fáza (20 %)", false, 585)]
    public static void ArakaniTestPhase5() { Arakani()?.DebugSetHealthFraction(0.19f); }

    [MenuItem("Progeny/Boss/Arakani: test - finišer (8 %)", false, 586)]
    public static void ArakaniTestFinisher() { Arakani()?.DebugSetHealthFraction(0.08f); }

    [MenuItem("Progeny/Boss/Arakani: test - začať boj hneď", true)]
    [MenuItem("Progeny/Boss/Arakani: test - fáza 2 Oheň (80 %)", true)]
    [MenuItem("Progeny/Boss/Arakani: test - fáza 3 Blesk (60 %)", true)]
    [MenuItem("Progeny/Boss/Arakani: test - fáza 4 Krv (40 %)", true)]
    [MenuItem("Progeny/Boss/Arakani: test - záverečná fáza (20 %)", true)]
    [MenuItem("Progeny/Boss/Arakani: test - finišer (8 %)", true)]
    static bool ArakaniPlayOnly() { return Application.isPlaying && EliteArena.Find("arakani") != null; }
}
