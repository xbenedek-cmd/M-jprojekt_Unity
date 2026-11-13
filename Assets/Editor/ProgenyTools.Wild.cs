using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Divočiny: päť zón mimo mesta (Divočina, Bleskový kaňon, Temný les, Zrezavená pustatina, Močiare). Každá má tému (vzhľad, stromy, skaly, vraky,
/// huby, rybníky, ruiny, monolit), tri lokality a vlastný pomer surovín: niekde je veľa kryštálov, inde dreva, šrotu alebo byliniek, iné suroviny chýbajú.
/// </summary>
public static partial class ProgenyTools
{
    class WildTheme
    {
        public string key, displayName;
        public Vector3 offset;          // stred zóny voči zemi mesta pod portálom do Megakorporácie
        public float cityGateOffset;    // posun portálu z mesta doprava (m)
        public Color ground, cliff, leaf, bark, gate;
        public int trees, conifers, deadTrees, rocks, wrecks, mushrooms, crystalDecor, ruins, ponds;
        public int herb, wood, stone, crystal, scrap;
        public string RootName => key == "Divocina" ? "Divocina" : "Divocina_" + key;
        public string GateKey => key == "Divocina" ? "Divociny" : key;
    }

    class WildMats
    {
        public Material bark, deadBark, leaf, leafDark, rock, metal, crystal, crystalGlow, glow, flower, water, dirt, cut, rust;
    }

    /// <summary>
    /// Tri lokality zóny (Les A, Skaly B, Vraky C) – offset od stredu zóny. Každá zóna má iné rozloženie, aby neboli všetky rovnaké;
    /// úvodná Divočina ostáva na pôvodných miestach (prológ, boss Raizen, Tieňový háj).
    /// </summary>
    static void WildAnchors(string key, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        switch (key)
        {
            case "Kanon": a = new Vector3(-115f, 0f, 20f); b = new Vector3(100f, 0f, 85f); c = new Vector3(-10f, 0f, 140f); break;
            case "Les": a = new Vector3(95f, 0f, 10f); b = new Vector3(-90f, 0f, 95f); c = new Vector3(20f, 0f, 150f); break;
            case "Pustatina": a = new Vector3(-100f, 0f, 105f); b = new Vector3(115f, 0f, 30f); c = new Vector3(10f, 0f, 20f); break;
            case "Mociare": a = new Vector3(60f, 0f, 120f); b = new Vector3(-120f, 0f, 60f); c = new Vector3(-60f, 0f, -20f); break;
            case "Bane": a = new Vector3(-30f, 0f, 130f); b = new Vector3(120f, 0f, 115f); c = new Vector3(80f, 0f, -30f); break;
            default: a = new Vector3(-55f, 0f, 45f); b = new Vector3(60f, 0f, 50f); c = new Vector3(5f, 0f, 88f); break;
        }
    }

    static readonly WildTheme[] WildThemes =
    {
        new WildTheme
        {
            key = "Divocina", displayName = "Divočina", offset = new Vector3(0f, 0f, 360f), cityGateOffset = 8f,
            ground = new Color(0.11f, 0.2f, 0.1f), cliff = new Color(0.14f, 0.17f, 0.14f), leaf = new Color(0.07f, 0.22f, 0.1f), bark = new Color(0.2f, 0.14f, 0.1f),
            gate = new Color(0.35f, 1f, 0.45f),
            trees = 45, conifers = 20, deadTrees = 6, rocks = 18, wrecks = 10, mushrooms = 0, crystalDecor = 0, ruins = 3, ponds = 1,
            herb = 12, wood = 10, stone = 14, crystal = 9, scrap = 10
        },
        new WildTheme
        {
            key = "Kanon", displayName = "Bleskový kaňon", offset = new Vector3(-540f, 0f, 360f), cityGateOffset = 16f,
            ground = new Color(0.22f, 0.2f, 0.19f), cliff = new Color(0.18f, 0.2f, 0.26f), leaf = new Color(0.1f, 0.18f, 0.15f), bark = new Color(0.16f, 0.14f, 0.14f),
            gate = new Color(0.4f, 0.7f, 1f),
            trees = 18, conifers = 8, deadTrees = 10, rocks = 46, wrecks = 4, mushrooms = 0, crystalDecor = 0, ruins = 2, ponds = 0,
            herb = 3, wood = 16, stone = 24, crystal = 30, scrap = 6
        },
        new WildTheme
        {
            key = "Les", displayName = "Temný les", offset = new Vector3(0f, 0f, 900f), cityGateOffset = 24f,
            ground = new Color(0.06f, 0.12f, 0.07f), cliff = new Color(0.08f, 0.12f, 0.09f), leaf = new Color(0.04f, 0.16f, 0.09f), bark = new Color(0.14f, 0.1f, 0.09f),
            gate = new Color(0.2f, 0.9f, 0.5f),
            trees = 80, conifers = 45, deadTrees = 12, rocks = 6, wrecks = 2, mushrooms = 0, crystalDecor = 0, ruins = 3, ponds = 2,
            herb = 24, wood = 30, stone = 4, crystal = 2, scrap = 3
        },
        new WildTheme
        {
            key = "Pustatina", displayName = "Zrezavená pustatina", offset = new Vector3(540f, 0f, 360f), cityGateOffset = 32f,
            ground = new Color(0.28f, 0.2f, 0.13f), cliff = new Color(0.3f, 0.22f, 0.17f), leaf = new Color(0.2f, 0.2f, 0.08f), bark = new Color(0.25f, 0.17f, 0.1f),
            gate = new Color(1f, 0.6f, 0.2f),
            trees = 2, conifers = 0, deadTrees = 14, rocks = 16, wrecks = 46, mushrooms = 0, crystalDecor = 0, ruins = 9, ponds = 0,
            herb = 0, wood = 2, stone = 10, crystal = 5, scrap = 32
        },
        new WildTheme
        {
            key = "Mociare", displayName = "Močiare", offset = new Vector3(-540f, 0f, 900f), cityGateOffset = 40f,
            ground = new Color(0.08f, 0.15f, 0.13f), cliff = new Color(0.1f, 0.16f, 0.15f), leaf = new Color(0.08f, 0.2f, 0.14f), bark = new Color(0.15f, 0.12f, 0.1f),
            gate = new Color(0.4f, 0.95f, 0.8f),
            trees = 25, conifers = 0, deadTrees = 40, rocks = 4, wrecks = 6, mushrooms = 0, crystalDecor = 0, ruins = 2, ponds = 7,
            herb = 30, wood = 12, stone = 3, crystal = 0, scrap = 6
        },
        new WildTheme
        {
            key = "Bane", displayName = "Bane", offset = new Vector3(540f, 0f, 900f), cityGateOffset = 48f,
            ground = new Color(0.17f, 0.16f, 0.16f), cliff = new Color(0.2f, 0.19f, 0.2f), leaf = new Color(0.1f, 0.12f, 0.1f), bark = new Color(0.16f, 0.13f, 0.11f),
            gate = new Color(1f, 0.75f, 0.3f),
            trees = 0, conifers = 0, deadTrees = 6, rocks = 44, wrecks = 16, mushrooms = 0, crystalDecor = 0, ruins = 5, ponds = 0,
            herb = 0, wood = 0, stone = 34, crystal = 16, scrap = 26
        }
    };

    const float CountScale = 2.4f;   // zóna je ~3× väčšia, objekty a suroviny sa pridávajú úmerne

    [MenuItem("Progeny/Bane/Vytvoriť zónu Bane", false, 200)]
    static void CreateMines()
    {
        foreach (WildTheme theme in WildThemes)
            if (theme.key == "Bane") BuildWildZone(theme);
    }

    [MenuItem("Progeny/Divočiny/Vytvoriť len prvú Divočinu", false, 101)]
    static void CreateWilderness() => BuildWildZone(WildThemes[0]);

    [MenuItem("Progeny/Divočiny/Vytvoriť 4 ďalšie (kaňon, les, pustatina, močiare)", false, 102)]
    static void CreateMoreWilderness()
    {
        for (int i = 1; i < WildThemes.Length; i++) BuildWildZone(WildThemes[i]);
    }

    [MenuItem("Progeny/Divočiny/Vytvoriť všetkých 5 divočín", false, 100)]
    static void CreateAllWilderness()
    {
        foreach (WildTheme theme in WildThemes) BuildWildZone(theme);
    }

    static void BuildWildZone(WildTheme t)
    {
        GameObject civilGate = GameObject.Find("Portal_DoMegakorporacie");
        if (civilGate == null)
        {
            CreateCityPortals();
            civilGate = GameObject.Find("Portal_DoMegakorporacie");
            if (civilGate == null) return;
        }

        GameObject old = GameObject.Find(t.RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        GameObject root = new GameObject(t.RootName);
        Undo.RegisterCreatedObjectUndo(root, "Create " + t.displayName);

        // Zem mesta pod portálom; zóna leží ďaleko od mesta, pod ňou je vlastná doska
        Vector3 civilGround = civilGate.transform.position + Vector3.down * 1.5f;
        Vector3 c = civilGround + t.offset;
        c.y = civilGround.y;
        root.transform.position = c;

        const float half = 210f;
        BuildZoneGround(root.transform, "Zem_" + t.key, c, new Vector2(2 * half, 2 * half), t.ground);
        Transform zoneGround = root.transform.Find("Zem_" + t.key);
        if (zoneGround != null) ApplyWildGround(zoneGround.gameObject, t);
        BuildHills(root, t, c, c.y);   // kopce (aj ich pôdorysy, ktorým sa vyhýba Pick)

        // Útesy po obvode (kamenný povrch, ak je balík castle_wall), aby hráč nespadol z okraja
        Block(root.transform, "Utes_S", c, new Vector3(0f, 0f, half), new Vector3(2 * half, 9f, 3f), t.cliff);
        Block(root.transform, "Utes_J", c, new Vector3(0f, 0f, -half), new Vector3(2 * half, 9f, 3f), t.cliff);
        Block(root.transform, "Utes_V", c, new Vector3(half, 0f, 0f), new Vector3(3f, 9f, 2 * half), t.cliff);
        Block(root.transform, "Utes_Z", c, new Vector3(-half, 0f, 0f), new Vector3(3f, 9f, 2 * half), t.cliff);
        Material cliffSurface = LoadSurface("castle_wall");
        if (cliffSurface != null)
            foreach (Transform child in root.transform)
                if (child.name.StartsWith("Utes_"))
                {
                    Material m = new Material(cliffSurface);
                    m.mainTextureScale = new Vector2(Mathf.Max(child.localScale.x, child.localScale.z) / 6f, 1.5f);
                    child.GetComponent<Renderer>().sharedMaterial = m;
                }

        // Portály: z mesta (v rade vedľa portálu do Megakorporácie) a späť pri južnom okraji zóny
        PortalGate toZone = BuildPortal(root.transform, "Portal_Do" + t.GateKey, civilGround + civilGate.transform.right * t.cityGateOffset, t.gate, civilGate.transform.rotation);
        Vector3 gatePos = c + new Vector3(0f, 0f, -(half - 25f));
        PortalGate fromZone = BuildPortal(root.transform, "Portal_z" + t.GateKey, gatePos, new Color(0.35f, 0.7f, 1f), Quaternion.LookRotation(Vector3.forward));
        toZone.Link(fromZone, t.displayName);
        fromZone.Link(toZone, WorldNames.StartCity);
        EditorUtility.SetDirty(toZone);
        EditorUtility.SetDirty(fromZone);

        Physics.SyncTransforms();
        System.Random rnd = new System.Random(t.key.GetHashCode() & 0xffff);
        WildMats m2 = MakeWildMats(t);

        // Tri lokality (miesto podľa zóny: WildAnchors): Les (A), Skaly (B), Vraky (C)
        WildAnchors(t.key, out Vector3 offA, out Vector3 offB, out Vector3 offC);
        Vector3 anchorA = c + offA;
        Vector3 anchorB = c + offB;
        Vector3 anchorC = c + offC;

        Transform forest = new GameObject("Lokacia_Les").transform;
        Transform rocks = new GameObject("Lokacia_Skaly").transform;
        Transform wrecks = new GameObject("Lokacia_Vraky").transform;
        Transform decor = new GameObject("Dekoracie").transform;
        foreach (Transform group in new[] { forest, rocks, wrecks, decor }) group.SetParent(root.transform);

        // Náhodné miesto: s pravdepodobnosťou anchorShare okolo lokality, inak kdekoľvek v zóne; mimo portálu a okraja
        Vector3 Pick(Vector3 anchor, float radius, float anchorShare)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector3 p = rnd.NextDouble() < anchorShare
                    ? Around(rnd, anchor, radius)
                    : c + new Vector3(((float)rnd.NextDouble() * 2f - 1f) * (half - 10f), 0f, ((float)rnd.NextDouble() * 2f - 1f) * (half - 10f));
                p.y = c.y;
                if (Mathf.Abs(p.x - c.x) > half - 8f || Mathf.Abs(p.z - c.z) > half - 8f) continue;
                if ((p - gatePos).sqrMagnitude < 14f * 14f) continue;
                if (InHill(p)) continue;
                return p;
            }
            return anchor;
        }

        // Cesty od portálu k lokalitám (bez kolízie, len stopa v zemi)
        foreach (Vector3 target in new[] { anchorA, anchorB, anchorC })
            BuildPath(decor, gatePos, target, m2.dirt);

        // Stromy, skaly, vraky a ďalšie dekorácie
        for (int i = 0; i < Mathf.RoundToInt(t.trees * 1.6f * CountScale); i++) WildTree(forest, Pick(anchorA, 40f, 0.6f), m2, rnd);
        for (int i = 0; i < Mathf.RoundToInt(t.conifers * 1.6f * CountScale); i++) WildConifer(forest, Pick(anchorA, 42f, 0.5f), m2, rnd);
        for (int i = 0; i < Mathf.RoundToInt(t.deadTrees * 1.6f * CountScale); i++) WildDeadTree(decor, Pick(anchorA, 48f, 0.3f), m2, rnd);
        for (int i = 0; i < Mathf.RoundToInt(t.rocks * CountScale); i++) WildRock(rocks, Pick(anchorB, 40f, 0.6f), m2, rnd);
        for (int i = 0; i < Mathf.RoundToInt(t.wrecks * CountScale); i++) WildWreck(wrecks, Pick(anchorC, 30f, 0.7f), m2, rnd);
        for (int i = 0; i < Mathf.RoundToInt(t.mushrooms * CountScale); i++) WildMushroom(decor, Pick(anchorA, 45f, 0.5f), m2, rnd);
        for (int i = 0; i < Mathf.RoundToInt(t.crystalDecor * CountScale); i++) WildCrystals(rocks, Pick(anchorB, 40f, 0.7f), m2, rnd, true, null);
        for (int i = 0; i < t.ponds; i++) WildPond(decor, Pick(c, 100f, 0.0f), m2, rnd);

        GameObject[] ruinModels = FindModels("brickruin");
        for (int i = 0; i < Mathf.RoundToInt(t.ruins * CountScale); i++) WildRuin(wrecks, Pick(anchorC, 35f, 0.5f), m2, rnd, ruinModels);

        if (t.key == "Bane") BuildMineEntrance(root.transform, gatePos, m2, rnd);
        WildMonolith(decor, c + new Vector3(25f, 0f, -10f), m2);
        for (int i = 0; i < Mathf.Clamp(t.wrecks / 10, 1, 4); i++) BuildCamp(wrecks, Pick(anchorC, 50f, 0.6f), rnd);

        // Zdroje surovín: každá surovina má svoju lokalitu, ale časť leží aj inde v zóne
        for (int i = 0; i < Mathf.RoundToInt(t.herb * CountScale); i++) WildHerb(forest, Pick(anchorA, 36f, 0.7f), m2, rnd);
        for (int i = 0; i < Mathf.RoundToInt(t.wood * CountScale); i++) WildLogs(forest, Pick(anchorA, 36f, 0.7f), m2, rnd);
        for (int i = 0; i < Mathf.RoundToInt(t.stone * CountScale); i++) WildStonePile(rocks, Pick(anchorB, 36f, 0.6f), m2, rnd);
        for (int i = 0; i < Mathf.RoundToInt(t.crystal * CountScale); i++) WildCrystals(rocks, Pick(anchorB, 36f, 0.6f), m2, rnd, false, ItemCatalog.Crystal);
        for (int i = 0; i < Mathf.RoundToInt(t.scrap * CountScale); i++) WildScrap(wrecks, Pick(anchorC, 28f, 0.7f), m2, rnd);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        SafeBuildWildTerrain(t);   // skutočný terén: kopce, cesty, jazerá, objekty v hraniciach
        Selection.activeGameObject = root;
        Debug.Log($"{t.displayName}: zdroje bylina {t.herb}, drevo {t.wood}, kameň {t.stone}, kryštál {t.crystal}, šrot {t.scrap}. Portál je v meste {t.cityGateOffset:0} m vpravo od portálu do Megakorporácie. Ulož scénu.");
    }

    // ---------- Materiály ----------
    static Material SurfaceOrColor(string key, Color fallback, float tile)
    {
        Material surface = LoadSurface(key);
        if (surface == null) return Mat(fallback);

        Material m = new Material(surface) { color = Color.Lerp(Color.white, fallback, 0.35f) };
        m.mainTextureScale = new Vector2(tile, tile);
        return m;
    }

    // Šesťboký fazetovaný kryštál (výška 1, vycentrovaný v 0) so zrazenými hranami dole aj v ramenách a špicom; plochy majú vlastné vrcholy (ostré fazety)
    static Mesh CrystalShardMesh()
    {
        const string path = "Assets/Models/BleskovyKrystal2.asset";   // v2: opravené otočenie plôch (v1 bol vnútrom von = neviditeľný)
        if (AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/BleskovyKrystal.asset") != null) AssetDatabase.DeleteAsset("Assets/Models/BleskovyKrystal.asset");
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        const int sides = 6;
        // (polomer, výška 0..1): zrazená päta, telo, zrazené rameno, hrot
        float[,] rings = { { 0.34f, 0f }, { 0.5f, 0.08f }, { 0.5f, 0.64f }, { 0.34f, 0.84f } };
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();

        Vector3 P(int ring, int side) => new Vector3(Mathf.Cos(side * Mathf.PI * 2f / sides) * rings[ring, 0], rings[ring, 1] - 0.5f,
                                                      Mathf.Sin(side * Mathf.PI * 2f / sides) * rings[ring, 0]);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        for (int r = 0; r < rings.GetLength(0) - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int n = (s + 1) % sides;
                Quad(P(r, s), P(r + 1, s), P(r + 1, n), P(r, n));
            }

        Vector3 tip = new Vector3(0f, 0.5f, 0f);
        for (int s = 0; s < sides; s++)
        {
            int n = (s + 1) % sides, i = verts.Count;
            verts.Add(P(3, s)); verts.Add(tip); verts.Add(P(3, n));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }
        // Spodná plocha
        for (int s = 0; s < sides; s++)
        {
            int n = (s + 1) % sides, i = verts.Count;
            verts.Add(new Vector3(0f, -0.5f, 0f)); verts.Add(P(0, n)); verts.Add(P(0, s));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        var uv = new Vector2[verts.Count];
        for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(verts[i].x + 0.5f, verts[i].y + 0.5f);

        Mesh mesh = new Mesh { name = "BleskovyKrystal" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.SetUVs(0, uv);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // Prepíše materiál už vytvoreným kryštálom v scéne (bez nového generovania Divočín)
    [MenuItem("Progeny/Divočiny/Kryštály: obnoviť materiál", false, 113)]
    static void RefreshCrystalMaterials()
    {
        AssetDatabase.Refresh();
        EnsureSurfaceMaterials(true);   // vytvorí crystal.mat z textúr

        Material crystal = MakeCrystalMaterial();
        const string path = "Assets/Materials/Povrchy/BleskovyKrystal.mat";
        Material saved = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (saved == null) AssetDatabase.CreateAsset(crystal, path);
        else { EditorUtility.CopySerialized(crystal, saved); crystal = saved; }

        Mesh shard = CrystalShardMesh();
        int groups = 0;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t.name != "Zdroj_kryštál" && t.name != "Kryštály") continue;
            bool resource = t.name == "Zdroj_kryštál";
            foreach (MeshRenderer r in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool isModel = false;   // model Crystal.fbx (Kristal) má vlastný vzhľad a sieť, nemení sa
                for (Transform up = r.transform; up != null && up != t; up = up.parent)
                    if (up.name == "Kristal") { isModel = true; break; }
                if (isModel) continue;

                Undo.RecordObject(r, "Kryštál materiál");
                r.sharedMaterial = crystal;

                // Nový tvar; zbierané kryštály sa naraz zmenšia (len raz: kým majú ešte kváder)
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == shard) continue;
                Mesh previousMesh = mf.sharedMesh;   // null = starý (zmazaný) tvar, ktorý už bol zmenšený
                mf.sharedMesh = shard;
                if (resource && r.transform != t && previousMesh != null && previousMesh.name == "Cube")
                {
                    r.transform.localScale *= 0.4f;
                    r.transform.localPosition *= 0.4f;
                }
            }
            groups++;
        }

        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"Kryštály: materiál obnovený v {groups} zhlukoch ({(LoadSurface("crystal") != null ? "s textúrou crystal" : "BEZ textúry – chýba Povrchy/crystal")}). Ulož scénu.");
    }

    // Bleskový kryštál: lesklý polopriehľadný ľad s modrým žiarením. Ak existuje balík Assets/Materials/Povrchy/crystal, použije sa jeho textúra.
    static Material MakeCrystalMaterial()
    {
        // Skleneno-ľadový kryštál: priehľadný, veľmi lesklý, s jasným svetlomodrým žiarením (bez textúry betónu, ktorá ho robila matným)
        Color tint = new Color(0.55f, 0.85f, 1f);
        Material m = new Material(Shader.Find("Standard"));
        m.SetFloat("_Mode", 2f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_ALPHABLEND_ON");
        m.renderQueue = 3000;
        m.color = new Color(tint.r, tint.g, tint.b, 0.78f);
        m.SetFloat("_Metallic", 0.1f);
        m.SetFloat("_Glossiness", 0.97f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(0.3f, 0.65f, 1f) * 1.3f);
        return m;
    }

    // Voda jazierok: balík swamp_water (Povrchy), lesklá; bez balíka tmavomodrá farba
    static Material MakeWaterMaterial()
    {
        Material surface = LoadSurface("swamp_water");
        Material m;
        if (surface != null)
        {
            m = new Material(surface) { color = new Color(0.6f, 0.78f, 0.72f) };
            m.mainTextureScale = new Vector2(3f, 3f);
        }
        else m = Mat(new Color(0.08f, 0.22f, 0.3f));

        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", 0.92f);
        return m;
    }

    static WildMats MakeWildMats(WildTheme t)
    {
        Material water = MakeWaterMaterial();
        bool swamp = t.key == "Mociare";

        return new WildMats
        {
            bark = Mat(t.bark),
            deadBark = Mat(t.bark * 0.7f),
            leaf = Mat(t.leaf),
            leafDark = Mat(t.leaf * 0.7f),
            rock = SurfaceOrColor("castle_wall", new Color(0.34f, 0.35f, 0.37f), 1.2f),
            metal = SurfaceOrColor("corrugated_metal", new Color(0.4f, 0.33f, 0.28f), 1f),
            crystal = MakeCrystalMaterial(),
            crystalGlow = Mat(new Color(0.18f, 0.4f, 0.6f), true),
            glow = Mat(swamp ? new Color(0.5f, 1f, 0.6f) : new Color(0.55f, 0.4f, 1f), true),
            flower = Mat(new Color(0.9f, 0.5f, 0.9f), true),
            water = water,
            dirt = Mat(new Color(0.26f, 0.2f, 0.14f)),
            cut = Mat(new Color(0.72f, 0.56f, 0.36f)),
            rust = Mat(new Color(0.42f, 0.2f, 0.1f))
        };
    }

    // ---------- Pomocné tvary ----------
    static float RandomRange(System.Random rnd, float min, float max) => min + (float)rnd.NextDouble() * (max - min);

    static GameObject NewGroup(string name, Transform parent, Vector3 position, System.Random rnd)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, RandomRange(rnd, 0f, 360f), 0f);
        return go;
    }

    static void Part(PrimitiveType type, Transform group, Vector3 localCenter, Vector3 scale, Material mat, bool collider, Quaternion? rotation = null)
    {
        GameObject g = Prim(type, "Cast", group, group.position, scale, mat, collider);
        g.transform.localPosition = localCenter;
        if (rotation.HasValue) g.transform.localRotation = rotation.Value;
    }

    // Kolízia z rozmerov v LOKÁLNOM priestore objektu (nie zo svetových bounds): inak sa pri natočenom objekte box otočí druhýkrát
    // a nepokrýva model (cez ruiny sa dalo prechádzať)
    static void AddColliderFromBounds(GameObject root)
    {
        foreach (BoxCollider old in root.GetComponents<BoxCollider>()) Object.DestroyImmediate(old);

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
        Bounds bounds = default;
        bool first = true;
        foreach (Renderer r in renderers)
        {
            Bounds lb = r.localBounds;
            Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
            }
        }

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;
    }

    static void BuildPath(Transform parent, Vector3 from, Vector3 to, Material mat)
    {
        Vector3 d = to - from;
        d.y = 0f;
        if (d.sqrMagnitude < 1f) return;

        GameObject path = Prim(PrimitiveType.Cube, "Cesta", parent, from + d * 0.5f + Vector3.up * 0.02f, new Vector3(3f, 0.04f, d.magnitude), mat, false);
        path.transform.rotation = Quaternion.LookRotation(d.normalized);
    }

    // ---------- Stromy a rastliny ----------
    static void WildTree(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        float h = RandomRange(rnd, 3.5f, 6f);
        GameObject tree = NewGroup("Strom", parent, g, rnd);
        if (PlaceFir(tree, false, rnd)) return;
        Part(PrimitiveType.Cylinder, tree.transform, Vector3.up * h * 0.5f, new Vector3(0.7f, h * 0.5f, 0.7f), m.bark, true);
        Part(PrimitiveType.Sphere, tree.transform, Vector3.up * (h + 1.1f), new Vector3(4f, 3.4f, 4f), m.leaf, false);
        Part(PrimitiveType.Sphere, tree.transform, new Vector3(0.9f, h + 0.3f, 0.4f), new Vector3(2.6f, 2.2f, 2.6f), m.leafDark, false);
        MakeBreakable(tree, ToolKit.Kind.Axe, ItemCatalog.Wood, "Strom", 90f + h * 12f, true);
    }

    // Strom v Divočine sa dá zničiť len sekerou (ToolKit): pevnosť podľa veľkosti, drevo za odmenu
    static void MakeBreakable(GameObject g, ToolKit.Kind tool, string item, string label, float hp, bool tree)
    {
        BreakableNode node = g.GetComponent<BreakableNode>();
        if (node == null) node = g.AddComponent<BreakableNode>();
        node.Configure(tool, item, label, hp, tree);
        EditorUtility.SetDirty(node);
    }

    static void WildConifer(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        float s = RandomRange(rnd, 0.8f, 1.4f);
        GameObject tree = NewGroup("Smrek", parent, g, rnd);
        if (PlaceFir(tree, false, rnd)) return;
        Part(PrimitiveType.Cylinder, tree.transform, Vector3.up * 1.6f * s, new Vector3(0.5f * s, 1.6f * s, 0.5f * s), m.bark, true);
        Part(PrimitiveType.Sphere, tree.transform, Vector3.up * 2.4f * s, new Vector3(3.4f * s, 1.5f * s, 3.4f * s), m.leafDark, false);
        Part(PrimitiveType.Sphere, tree.transform, Vector3.up * 3.6f * s, new Vector3(2.6f * s, 1.4f * s, 2.6f * s), m.leafDark, false);
        Part(PrimitiveType.Sphere, tree.transform, Vector3.up * 4.7f * s, new Vector3(1.7f * s, 1.3f * s, 1.7f * s), m.leafDark, false);
        MakeBreakable(tree, ToolKit.Kind.Axe, ItemCatalog.Wood, "Strom", 70f * s + 20f, true);
    }

    static void WildDeadTree(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        float h = RandomRange(rnd, 2.5f, 4.5f);
        GameObject tree = NewGroup("Mŕtvy strom", parent, g, rnd);
        if (PlaceFir(tree, true, rnd)) return;
        Part(PrimitiveType.Cylinder, tree.transform, Vector3.up * h * 0.5f, new Vector3(0.5f, h * 0.5f, 0.5f), m.deadBark, true);
        for (int i = 0; i < 3; i++)
        {
            float yaw = i * 120f + RandomRange(rnd, -20f, 20f);
            Part(PrimitiveType.Cylinder, tree.transform, new Vector3(0f, h * (0.6f + 0.12f * i), 0f) + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.5f, 0.25f, 0f),
                new Vector3(0.14f, 0.7f, 0.14f), m.deadBark, false, Quaternion.Euler(0f, yaw, 55f));
        }
        MakeBreakable(tree, ToolKit.Kind.Axe, ItemCatalog.Wood, "Mŕtvy strom", 45f, true);
    }

    static void WildMushroom(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        float s = RandomRange(rnd, 0.6f, 1.7f);
        GameObject mushroom = NewGroup("Huba", parent, g, rnd);
        Part(PrimitiveType.Cylinder, mushroom.transform, Vector3.up * 0.4f * s, new Vector3(0.15f * s, 0.4f * s, 0.15f * s), m.bark, false);
        Part(PrimitiveType.Sphere, mushroom.transform, Vector3.up * 0.85f * s, new Vector3(0.9f * s, 0.45f * s, 0.9f * s), m.glow, false);
    }

    static void WildPond(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        float r = RandomRange(rnd, 6f, 14f);
        Prim(PrimitiveType.Cylinder, "Rybník", parent, g + Vector3.up * 0.03f, new Vector3(r * 2f, 0.03f, r * 2f * RandomRange(rnd, 0.6f, 1f)), m.water, false);
    }

    // ---------- Skaly, vraky, ruiny, monolit ----------
    static void WildRock(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        float s = RandomRange(rnd, 1.5f, 4.5f);
        GameObject rock = NewGroup("Skala", parent, g, rnd);
        FillRockOrCube(rock, m, rnd, s);
        MakeBreakableFromBounds(rock, ToolKit.Kind.Pickaxe, ItemCatalog.Stone, "Skala", 50f, 45f);
    }

    // Skala: model z balíka (9 druhov), inak kváder
    static void FillRockOrCube(GameObject rock, WildMats m, System.Random rnd, float size)
    {
        if (FillRockModel(rock, rnd, size)) return;
        Part(PrimitiveType.Cube, rock.transform, Vector3.up * size * 0.4f, new Vector3(size, size * RandomRange(rnd, 0.7f, 1.6f), size * RandomRange(rnd, 0.7f, 1.2f)), m.rock, true,
            Quaternion.Euler(RandomRange(rnd, -8f, 8f), 0f, RandomRange(rnd, -8f, 8f)));
    }

    // Železný blok = malý kovový predmet z knižnice modelov (kovová debna, elektrická skriňa), ktorý sa láme kladivom a dá šrot
    static void WildWreck(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        GameObject wreck = NewGroup("Vrak", parent, g, rnd);
        if (!FillWreckModel(wreck, rnd)) FillWreckCube(wreck, m, rnd);
        MakeBreakableFromBounds(wreck, ToolKit.Kind.Hammer, ItemCatalog.Scrap, "Železný odpad", 35f, 20f);
    }

    static readonly string[] wreckModelKeys = { "Industrial_Junkyard_Crate_Metal", "Industrial_Junkyard_Crate_Metal", "Electrical_Box" };

    static bool IsWreckModelName(string name)
    {
        foreach (string key in wreckModelKeys) if (name.StartsWith(key)) return true;
        return false;
    }

    static bool HasWreckModel(Transform t)
    {
        foreach (Transform child in t)
            foreach (string key in wreckModelKeys)
                if (child.name.StartsWith(key)) return true;
        return false;
    }

    static bool FillWreckModel(GameObject wreck, System.Random rnd)
    {
        string key = wreckModelKeys[rnd.Next(wreckModelKeys.Length)];
        return PlaceProp(wreck.transform, key, wreck.transform.position, RandomRange(rnd, 0.9f, 1.5f), RandomRange(rnd, 0f, 360f), true) != null;
    }

    // Záloha, keď modely chýbajú: malý kovový kváder
    static void FillWreckCube(GameObject wreck, WildMats m, System.Random rnd)
    {
        Vector3 size = new Vector3(RandomRange(rnd, 0.9f, 1.5f), RandomRange(rnd, 0.6f, 1.1f), RandomRange(rnd, 0.9f, 1.5f));
        Part(PrimitiveType.Cube, wreck.transform, Vector3.up * size.y * 0.5f, size, m.metal, true, Quaternion.Euler(0f, 0f, RandomRange(rnd, -6f, 6f)));
    }


    static void WildRuin(Transform parent, Vector3 g, WildMats m, System.Random rnd, GameObject[] models)
    {
        GameObject ruin = NewGroup("Ruina", parent, g, rnd);
        if (models != null && models.Length > 0)
        {
            GameObject piece = Object.Instantiate(models[rnd.Next(models.Length)], ruin.transform);
            piece.transform.localPosition = Vector3.zero;
            piece.transform.localRotation = Quaternion.identity;
            ApplyRuinMaterial(piece, m);
            AddColliderFromBounds(piece);
        }
        else Part(PrimitiveType.Cube, ruin.transform, Vector3.up * 1.5f, new Vector3(6f, 3f, 0.7f), m.rock, true);
    }

    // Modely ruín sú bez textúry (biele): dostanú kamenný povrch castle_wall
    static void ApplyRuinMaterial(GameObject piece, WildMats m)
    {
        foreach (Renderer r in piece.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = m.rock;
            r.sharedMaterials = mats;
        }
    }

    static void WildMonolith(Transform parent, Vector3 g, WildMats m)
    {
        GameObject mono = new GameObject("Monolit");
        mono.transform.SetParent(parent);
        mono.transform.position = g;
        Part(PrimitiveType.Cube, mono.transform, Vector3.up * 3.5f, new Vector3(1.6f, 7f, 1.6f), m.rock, true);

        // Svietiace runy na stranách
        foreach (Vector3 side in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            Part(PrimitiveType.Cube, mono.transform, Vector3.up * 3.5f + side * 0.82f, new Vector3(side.x != 0f ? 0.05f : 0.12f, 4.5f, side.z != 0f ? 0.05f : 0.12f), m.crystal, false);
    }

    // ---------- Zdroje surovín ----------
    static void NodeOn(GameObject group, string itemId, int min, int max) => group.AddComponent<ResourceNode>().Configure(itemId, min, max);

    static void WildHerb(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        GameObject node = NewGroup("Zdroj_bylina", parent, g, rnd);
        FillHerb(node, m, rnd);
        NodeOn(node, ItemCatalog.Herb, 1, 3);
    }

    static void WildLogs(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        GameObject node = NewGroup("Zdroj_drevo", parent, g, rnd);
        FillLogs(node, m, rnd);
        NodeOn(node, ItemCatalog.Wood, 2, 4);
    }

    static void WildStonePile(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        GameObject node = NewGroup("Zdroj_kameň", parent, g, rnd);
        FillStone(node, m, rnd);
        NodeOn(node, ItemCatalog.Stone, 2, 4);
    }

    // Kryštály: ako zdroj (itemId) alebo len dekorácia (väčšie, bez zberu)
    static void WildCrystals(Transform parent, Vector3 g, WildMats m, System.Random rnd, bool decorOnly, string itemId)
    {
        GameObject cluster = NewGroup(decorOnly ? "Kryštály" : "Zdroj_kryštál", parent, g, rnd);
        FillCrystals(cluster, m, rnd, decorOnly);
        if (!decorOnly && itemId != null)
        {
            NodeOn(cluster, itemId, 1, 2);
            cluster.GetComponent<ResourceNode>().SetRange(3.4f);   // väčší dosah: kryštály sa dajú zbierať aj z odstupu
        }
    }

    // Zhluk fazetovaných kryštálov: jeden hlavný a štyri nižšie, zbierané ~0,8–1,3 m, dekorácie 2–3,5 m; pod nimi slabo žiariaci kruh na zemi
    static void FillCrystals(GameObject cluster, WildMats m, System.Random rnd, bool decorOnly)
    {
        if (!decorOnly && FillCrystalModel(cluster, m, rnd)) return;   // model Crystal.fbx + malé úlomky

        float size = decorOnly ? RandomRange(rnd, 2f, 3.5f) : RandomRange(rnd, 0.8f, 1.3f);
        float spread = decorOnly ? 0.8f : 0.3f;
        float width = decorOnly ? 0.55f : 0.24f;
        Mesh shard = CrystalShardMesh();

        for (int i = 0; i < 5; i++)
        {
            float angle = i * Mathf.PI * 2f / 5f + RandomRange(rnd, -0.3f, 0.3f);
            Vector3 offset = i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spread * RandomRange(rnd, 0.6f, 1f);
            float height = size * (i == 0 ? 1f : RandomRange(rnd, 0.45f, 0.8f));
            float w = width * (i == 0 ? 1.15f : RandomRange(rnd, 0.7f, 1f));
            Part(PrimitiveType.Cube, cluster.transform, offset + Vector3.up * height * 0.5f, new Vector3(w, height, w), m.crystal, true,
                Quaternion.Euler(RandomRange(rnd, -14f, 14f) * (i == 0 ? 0.3f : 1f), RandomRange(rnd, 0f, 90f), RandomRange(rnd, -14f, 14f) * (i == 0 ? 0.3f : 1f)));
            cluster.transform.GetChild(cluster.transform.childCount - 1).GetComponent<MeshFilter>().sharedMesh = shard;
        }

        // Žiara na zemi, aby bolo zbierateľné kryštály vidieť z diaľky
        Part(PrimitiveType.Cylinder, cluster.transform, new Vector3(0f, 0.02f, 0f), new Vector3(spread * 3.2f, 0.01f, spread * 3.2f), m.crystalGlow, false);
    }

    static void WildScrap(Transform parent, Vector3 g, WildMats m, System.Random rnd)
    {
        GameObject node = NewGroup("Zdroj_šrot", parent, g, rnd);
        FillScrap(node, m, rnd);
        NodeOn(node, ItemCatalog.Scrap, 1, 3);
    }
}
