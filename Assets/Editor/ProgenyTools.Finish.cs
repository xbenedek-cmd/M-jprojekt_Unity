using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Dotvorenie vzhľadu: nová kamenná brána (skutočný oblúk z klenákov, piliere, lampy), ulicové lampy, vchod do bane
/// (skalná hmota, koľajnice, vozík, debny, cedule). Všetko sú opakovateľné a nič nemažú okrem vlastných starších verzií.
/// </summary>
public static partial class ProgenyTools
{
    // ======== Materiály ========
    static Material hbStone;

    // Kameň z projektu (Povrchy/castle_wall), inak tmavosivý materiál
    static Material HbStoneMaterial()
    {
        if (hbStone != null) return hbStone;
        foreach (string guid in AssetDatabase.FindAssets("castle_wall t:Material", new[] { "Assets/Materials" }))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null) { hbStone = m; return hbStone; }
        }
        hbStone = HbMaterial(new Color(0.26f, 0.26f, 0.3f));
        return hbStone;
    }

    // ======== Lampa ========
    /// <summary>Kovová lampa s kužeľovým tienidlom, sklenenou lucernou so žiarou (bez svetelného zdroja, kvôli výkonu), kotvou a podstavcom.</summary>
    static void HbLamp(Transform parent, Vector3 localBase, float height)
    {
        var lamp = new GameObject("Lampa").transform;
        lamp.SetParent(parent, false);
        lamp.localPosition = localBase;

        Material iron = HbMaterial(new Color(0.08f, 0.08f, 0.1f));
        Material glass = HbMaterial(new Color(1f, 0.78f, 0.45f), 2.4f);

        HbPart(lamp, "Podstavec", PrimitiveType.Cylinder, new Vector3(0f, 0.18f, 0f), new Vector3(0.55f, 0.18f, 0.55f), Quaternion.identity, iron);
        HbPart(lamp, "PodstavecHore", PrimitiveType.Cylinder, new Vector3(0f, 0.42f, 0f), new Vector3(0.34f, 0.08f, 0.34f), Quaternion.identity, iron);
        HbPart(lamp, "Stlp", PrimitiveType.Cylinder, new Vector3(0f, height * 0.5f, 0f), new Vector3(0.14f, height * 0.5f, 0.14f), Quaternion.identity, iron);
        HbPart(lamp, "Prsten", PrimitiveType.Cylinder, new Vector3(0f, height * 0.62f, 0f), new Vector3(0.24f, 0.05f, 0.24f), Quaternion.identity, iron);
        HbPart(lamp, "Misa", PrimitiveType.Cylinder, new Vector3(0f, height + 0.05f, 0f), new Vector3(0.62f, 0.06f, 0.62f), Quaternion.identity, iron);
        HbPart(lamp, "Lucerna", PrimitiveType.Cylinder, new Vector3(0f, height + 0.38f, 0f), new Vector3(0.38f, 0.3f, 0.38f), Quaternion.identity, glass);
        HbPart(lamp, "Strieska", PrimitiveType.Cylinder, new Vector3(0f, height + 0.74f, 0f), new Vector3(0.52f, 0.06f, 0.52f), Quaternion.identity, iron);
        HbPart(lamp, "Hrot", PrimitiveType.Sphere, new Vector3(0f, height + 0.9f, 0f), new Vector3(0.18f, 0.2f, 0.18f), Quaternion.identity, iron);
    }

    // ======== Brána v. 2: skutočný oblúk ========
    /// <summary>
    /// Kamenná brána: dva piliere s hlavicami a pätami, polkruhový oblúk z klenákov, svietiaci stredový kameň, runy na vnútornej strane,
    /// stupňovitý podstavec s kruhom rún a dve lampy. Prvky sú v koreni "Obluk" (zrušená mierka brány, rozmery sú v metroch).
    /// </summary>
    static void DecoratePortalV2(PortalGate gate, Color glow)
    {
        if (gate == null) return;

        Transform existing = gate.transform.Find("Obluk");
        if (existing != null)
        {
            if (existing.Find("v2") != null) return;
            Object.DestroyImmediate(existing.gameObject);   // starší vzhľad z kociek sa nahradí
        }

        Vector3 s = gate.transform.localScale;
        if (s.x < 0.01f || s.y < 0.01f || s.z < 0.01f) return;

        var root = new GameObject("Obluk").transform;
        root.SetParent(gate.transform, false);
        root.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
        root.localPosition = new Vector3(0f, -1.5f / s.y, 0f);
        new GameObject("v2").transform.SetParent(root, false);

        Material stone = HbStoneMaterial();
        Material dark = HbMaterial(new Color(0.1f, 0.1f, 0.13f));
        Material rune = HbMaterial(glow, 2.16f);   // o 10 % slabšie než pôvodných 2,4 (neóny portálov boli príliš silné)

        // Podstavec: dva stupne a kruh rún
        HbPart(root, "Stupen1", PrimitiveType.Cylinder, new Vector3(0f, 0.07f, 0f), new Vector3(7.4f, 0.07f, 7.4f), Quaternion.identity, dark);
        HbPart(root, "Stupen2", PrimitiveType.Cylinder, new Vector3(0f, 0.17f, 0f), new Vector3(6.2f, 0.04f, 6.2f), Quaternion.identity, stone);
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16f;
            HbPart(root, "Runa", PrimitiveType.Cube, new Vector3(Mathf.Cos(a), 0.23f, Mathf.Sin(a)) * 3.3f, new Vector3(0.32f, 0.04f, 0.1f),
                Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f), rune);
        }

        // Piliere: päta, driek, hlavica a svietiaci pás na vnútornej strane
        const float pillarX = 1.9f, archCenterY = 3.6f, archRadius = 1.9f;
        foreach (float side in new[] { -1f, 1f })
        {
            HbPart(root, "PilierPata", PrimitiveType.Cube, new Vector3(side * pillarX, 0.35f, 0f), new Vector3(1.25f, 0.7f, 1.35f), Quaternion.identity, stone);
            HbPart(root, "PilierDriek", PrimitiveType.Cube, new Vector3(side * pillarX, 2.15f, 0f), new Vector3(0.85f, 3.0f, 1.05f), Quaternion.identity, stone);
            HbPart(root, "PilierHlavica", PrimitiveType.Cube, new Vector3(side * pillarX, archCenterY - 0.1f, 0f), new Vector3(1.1f, 0.3f, 1.3f), Quaternion.identity, dark);
            HbPart(root, "PilierRuna", PrimitiveType.Cube, new Vector3(side * (pillarX - 0.43f), 2.1f, 0f), new Vector3(0.06f, 2.5f, 0.16f), Quaternion.identity, rune);
        }

        // Polkruhový oblúk z klenákov (každý je kocka natočená po dotyčnici)
        const int blocks = 9;
        for (int i = 0; i < blocks; i++)
        {
            float deg = i * 180f / (blocks - 1);
            float rad = deg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            Quaternion rot = Quaternion.Euler(0f, 0f, deg);
            HbPart(root, "Klenak", PrimitiveType.Cube, new Vector3(0f, archCenterY, 0f) + dir * archRadius, new Vector3(0.8f, 0.78f, 1.1f), rot, i == blocks / 2 ? dark : stone);
            if (i != 0 && i != blocks - 1)
                HbPart(root, "KlenakRuna", PrimitiveType.Cube, new Vector3(0f, archCenterY, 0f) + dir * (archRadius - 0.42f), new Vector3(0.1f, 0.34f, 0.14f), rot, rune);
        }
        HbPart(root, "Slnecnik", PrimitiveType.Cube, new Vector3(0f, archCenterY + archRadius + 0.7f, 0f), new Vector3(0.55f, 0.55f, 0.55f), Quaternion.Euler(0f, 0f, 45f), rune);

        // Lampy po stranách brány
        HbLamp(root, new Vector3(-4.2f, 0f, -0.9f), 3.4f);
        HbLamp(root, new Vector3(4.2f, 0f, -0.9f), 3.4f);

        EditorUtility.SetDirty(gate.gameObject);
    }

    [MenuItem("Progeny/Mesto/Portály: obnoviť vzhľad brán (oblúk, lampy)", false, 296)]
    static void RefreshPortalLooks()
    {
        int count = 0;
        foreach (PortalGate g in Object.FindObjectsByType<PortalGate>(FindObjectsInactive.Include))
        {
            if (g == null) continue;
            Transform old = g.transform.Find("Obluk");
            bool current = old != null && old.Find("v2") != null;
            Renderer r = g.GetComponent<Renderer>();
            Color c = r != null && r.sharedMaterial != null ? r.sharedMaterial.color : new Color(0.3f, 0.7f, 1f);
            c.a = 1f;
            DecoratePortalV2(g, c);
            if (!current) count++;
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Portály: nový kamenný oblúk s lampami postavený na {count} bránach. Ulož scénu.");
    }

    // ======== Lampy v meste ========
    [MenuItem("Progeny/Mesto/Lampy: vylepšiť lampy pozdĺž ulíc", false, 313)]
    static void UpgradeAvenueLamps()
    {
        var poles = new List<Transform>();
        var heads = new List<Transform>();
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null) continue;
            if (t.name == "StlpAvenue") poles.Add(t);
            else if (t.name == "LampaAvenue") heads.Add(t);
        }

        int done = 0;
        foreach (Transform pole in poles)
        {
            Renderer pr = pole.GetComponent<Renderer>();
            if (pr == null) continue;
            Bounds b = pr.bounds;
            Vector3 world = new Vector3(b.center.x, b.min.y, b.center.z);
            Transform parent = pole.parent != null ? pole.parent : pole;

            // Stará hlava lampy je najbližší "LampaAvenue" pri vrchu stĺpa
            Transform head = null;
            float best = 9f;
            foreach (Transform h in heads)
            {
                if (h == null) continue;
                float d = (h.position - new Vector3(world.x, b.max.y, world.z)).sqrMagnitude;
                if (d < best) { best = d; head = h; }
            }

            HbLamp(parent, parent.InverseTransformPoint(world), 5.4f);
            Object.DestroyImmediate(pole.gameObject);
            if (head != null) Object.DestroyImmediate(head.gameObject);
            done++;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Lampy: vymenených {done} stĺpov za nové lampy (kovový stĺp, sklenená lucerna so žiarou). Ulož scénu.");
    }

    // ======== Vchod do bane: dokončenie ========
    [MenuItem("Progeny/Bane/Dokončiť vchod do bane (skala, koľajnice, vozík, cedule)", false, 202)]
    static void FinishMineEntranceMenu()
    {
        Transform entrance = HbFind("Vchod_do_bane");
        if (entrance == null) { Debug.LogError("Vchod do bane neexistuje. Najprv: Progeny → Bane → Pridať vchod Hlbinná Baňa."); return; }
        FinishMineEntrance(entrance, MakeWildMatsForMine());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    static WildMats MakeWildMatsForMine()
    {
        foreach (WildTheme t in WildThemes)
            if (t.key == "Bane") return MakeWildMats(t);
        return MakeWildMats(WildThemes[0]);
    }

    /// <summary>Skalná hmota okolo otvoru, drevené vzpery, koľajnice s pražcami, vozík s rudou, debny, sudy, cedula s nápisom, lampy a hlinená plocha.</summary>
    static void FinishMineEntrance(Transform entrance, WildMats m)
    {
        if (entrance.Find("Dokoncenie") != null) return;

        var root = new GameObject("Dokoncenie").transform;
        root.SetParent(entrance, false);

        Material rock = m.rock != null ? m.rock : HbStoneMaterial();
        Material dark = HbMaterial(new Color(0.12f, 0.1f, 0.09f));
        Material iron = HbMaterial(new Color(0.16f, 0.16f, 0.18f));
        Material wood = HbMaterial(new Color(0.3f, 0.19f, 0.1f));
        Material soil = HbMaterial(new Color(0.18f, 0.13f, 0.09f));
        var rnd = new System.Random(4242);

        // Skalná hmota hory za a nad otvorom (vnútro je v miestnom -z)
        HbPart(root, "HoraTelo", PrimitiveType.Cube, new Vector3(0f, 4.4f, -7.4f), new Vector3(21f, 8.8f, 9f), Quaternion.Euler(0f, 0f, 0f), rock, true);
        HbPart(root, "HoraVrch", PrimitiveType.Sphere, new Vector3(0f, 9.2f, -8.2f), new Vector3(15f, 6.4f, 9.5f), Quaternion.identity, rock);
        for (int i = 0; i < 9; i++)
        {
            float x = Mathf.Lerp(-10f, 10f, i / 8f) + ((float)rnd.NextDouble() - 0.5f) * 1.6f;
            if (Mathf.Abs(x) < 3.4f) continue;   // otvor ostáva voľný
            float size = 3f + (float)rnd.NextDouble() * 2.5f;
            HbPart(root, "Balvan", PrimitiveType.Sphere, new Vector3(x, size * 0.35f, -5.0f - (float)rnd.NextDouble() * 1.2f), new Vector3(size, size * 0.8f, size),
                Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), rock, true);
        }

        // Drevené vzpery po stranách otvoru
        foreach (float side in new[] { -1f, 1f })
        {
            HbPart(root, "Vzpera", PrimitiveType.Cube, new Vector3(side * 2.3f, 3.5f, 0.8f), new Vector3(0.3f, 1.7f, 0.3f), Quaternion.Euler(0f, 0f, side * 38f), wood);
            HbPart(root, "Stlp", PrimitiveType.Cube, new Vector3(side * 3.0f, 2.2f, 0.8f), new Vector3(0.4f, 4.4f, 0.4f), Quaternion.identity, wood);
        }
        HbPart(root, "Traverza", PrimitiveType.Cube, new Vector3(0f, 4.5f, 0.8f), new Vector3(6.6f, 0.4f, 0.45f), Quaternion.identity, wood);

        // Hlinená plocha pred vchodom a štrk
        HbPart(root, "Plocha", PrimitiveType.Cube, new Vector3(0f, 0.04f, 8f), new Vector3(15f, 0.1f, 17f), Quaternion.identity, soil);
        for (int i = 0; i < 14; i++)
        {
            float rs = 0.25f + (float)rnd.NextDouble() * 0.45f;
            HbPart(root, "Strk", PrimitiveType.Cube, new Vector3(((float)rnd.NextDouble() - 0.5f) * 13f, rs * 0.4f, 0.5f + (float)rnd.NextDouble() * 15f),
                new Vector3(rs, rs * 0.7f, rs * 1.2f), Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f), rock);
        }

        // Koľajnice s pražcami od otvoru dopredu
        const float railFrom = 2.6f, railTo = 17f;
        float railLen = railTo - railFrom;
        foreach (float x in new[] { -0.55f, 0.55f })
            HbPart(root, "Koľajnica", PrimitiveType.Cube, new Vector3(x, 0.17f, railFrom + railLen * 0.5f), new Vector3(0.09f, 0.09f, railLen), Quaternion.identity, iron);
        for (float z = railFrom; z <= railTo; z += 0.85f)
            HbPart(root, "Prazec", PrimitiveType.Cube, new Vector3(0f, 0.1f, z), new Vector3(1.6f, 0.08f, 0.22f), Quaternion.identity, wood);

        // Vozík s rudou
        var cart = new GameObject("Vozik").transform;
        cart.SetParent(root, false);
        cart.localPosition = new Vector3(0f, 0f, 9.5f);
        HbPart(cart, "Korba", PrimitiveType.Cube, new Vector3(0f, 0.72f, 0f), new Vector3(1.45f, 0.75f, 2.1f), Quaternion.identity, iron, true);
        HbPart(cart, "Okraj", PrimitiveType.Cube, new Vector3(0f, 1.12f, 0f), new Vector3(1.6f, 0.08f, 2.25f), Quaternion.identity, wood);
        foreach (float wx in new[] { -0.7f, 0.7f })
            foreach (float wz in new[] { -0.7f, 0.7f })
                HbPart(cart, "Koleso", PrimitiveType.Cylinder, new Vector3(wx, 0.3f, wz), new Vector3(0.5f, 0.06f, 0.5f), Quaternion.Euler(0f, 0f, 90f), iron);
        Material ore = HbMaterial(new Color(0.9f, 0.7f, 0.15f), 0.4f);
        for (int i = 0; i < 6; i++)
            HbPart(cart, "Ruda", PrimitiveType.Sphere, new Vector3(((float)rnd.NextDouble() - 0.5f) * 1.0f, 1.2f + (float)rnd.NextDouble() * 0.15f, ((float)rnd.NextDouble() - 0.5f) * 1.6f),
                Vector3.one * (0.3f + (float)rnd.NextDouble() * 0.2f), Quaternion.identity, i % 2 == 0 ? ore : dark);

        // Debny a sudy
        HbPart(root, "Debna1", PrimitiveType.Cube, new Vector3(4.2f, 0.45f, 3.2f), new Vector3(0.95f, 0.9f, 0.95f), Quaternion.Euler(0f, 18f, 0f), wood, true);
        HbPart(root, "Debna2", PrimitiveType.Cube, new Vector3(4.9f, 0.45f, 3.9f), new Vector3(0.9f, 0.9f, 0.9f), Quaternion.Euler(0f, -12f, 0f), wood, true);
        HbPart(root, "Debna3", PrimitiveType.Cube, new Vector3(4.5f, 1.3f, 3.5f), new Vector3(0.8f, 0.8f, 0.8f), Quaternion.Euler(0f, 31f, 0f), wood);
        HbPart(root, "Sud1", PrimitiveType.Cylinder, new Vector3(-4.4f, 0.5f, 3.0f), new Vector3(0.75f, 0.5f, 0.75f), Quaternion.identity, wood, true);
        HbPart(root, "Sud2", PrimitiveType.Cylinder, new Vector3(-5.2f, 0.5f, 3.7f), new Vector3(0.75f, 0.5f, 0.75f), Quaternion.identity, wood, true);

        // Cedula na dvoch stĺpoch s nápisom
        foreach (float side in new[] { -1f, 1f })
            HbPart(root, "CedulaStlp", PrimitiveType.Cube, new Vector3(side * 3.1f, 1.8f, 3.6f), new Vector3(0.22f, 3.6f, 0.22f), Quaternion.identity, wood);
        GameObject board = HbPart(root, "Cedula", PrimitiveType.Cube, new Vector3(0f, 3.3f, 3.6f), new Vector3(6.4f, 1.0f, 0.14f), Quaternion.identity, wood);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var textGo = new GameObject("Napis");
        textGo.transform.SetParent(board.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, 0.6f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // čitateľný od hráča (z +z)
        textGo.transform.localScale = new Vector3(1f / 6.4f, 1f, 1f / 0.14f);
        var tm = textGo.AddComponent<TextMesh>();
        tm.text = "HLBINNÁ BAŇA";
        tm.fontSize = 64;
        tm.characterSize = 0.1f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.95f, 0.85f, 0.6f);
        tm.font = font;
        textGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;

        // Lampy po stranách prístupu
        HbLamp(root, new Vector3(-3.6f, 0f, 6.5f), 3.6f);
        HbLamp(root, new Vector3(3.6f, 0f, 6.5f), 3.6f);

        Debug.Log("Vchod do bane dokončený: skalná hmota, vzpery, koľajnice, vozík s rudou, debny, sudy, cedula a lampy.");
    }
}

public static partial class ProgenyTools
{
    // Lampy v oblúku okolo nádvoria (medzi bránami a za nimi)
    static void HbLampRing(Transform yard, Vector3 plaza, float radius, float startDeg, float endDeg, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float deg = Mathf.Lerp(startDeg, endDeg, count > 1 ? i / (float)(count - 1) : 0.5f);
            float rad = deg * Mathf.Deg2Rad;
            Vector3 world = plaza + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
            if (Physics.Raycast(world + Vector3.up * 50f, Vector3.down, out RaycastHit h, 100f, ~0, QueryTriggerInteraction.Ignore)) world.y = h.point.y;
            HbLamp(yard, yard.InverseTransformPoint(world), 4.4f);
        }
    }
}
