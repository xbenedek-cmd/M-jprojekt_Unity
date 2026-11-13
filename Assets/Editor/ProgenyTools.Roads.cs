using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Blok 2 plánu ciest: ulice Centra a Hradby ako procedurálny mesh pozdĺž krivky (UV podľa dĺžky), povrchy podľa štvrte,
/// obrubníky, lampy (RoadLamps), prechody medzi štvrťami, štvrte (CityDistrict) a ordinácia doktorky Yumi.
/// Domy, parcely a prefaby sa nemenia; existujúce ulice sa len prekryjú tenkým povrchom (+3 cm) s kolíziou.
/// </summary>
public static partial class ProgenyTools
{
    const string RdMeshDir = "Assets/Models/Roads";
    const string RdMatDir = "Assets/Materials/Cesty";
    const float RdLift = 0.035f;     // povrch cesty nad podkladom
    const float RdCityY = 0.5f;      // povrch Centra
    const float RdWallY = 3.0f;      // povrch Hradby

    static readonly string[] RdCityRoads =
    {
        "U1|2.6|-447,-114 -462,-111 -476,-116 -488,-110 -500,-114 -514,-108",
        "U2|2.6|-483,-113 -481,-123 -485,-134 -482,-143 -482,-149",
        "U3|2.6|-483,-113 -486,-99 -481,-89 -483,-81",
        "U4|2.6|-447,-186 -436,-189 -422,-184 -409,-190 -398,-186",
        "U6|2.6|-412,-153 -414,-162 -409,-171 -412,-181",
        "R1|5|-447,-34 -465,-33 -483,-35 -503,-33 -516,-36",
        "R2|5|-483,-35 -482,-12 -483,-2",
        "R2b|5|-483,-35 -483,-50 -478,-58 -470,-64 -466,-67",
        "R3|5|-412,5 -411,20 -413,32 -412,45 -413,62 -412,70",
        "R4|5|-447,37 -430,38 -412,37 -396,38 -380,37",
        "O5|5|-365,37 -350,36 -332,37 -315,36 -301,37",
        "O6|5|-332,5 -331,20 -332,36 -331,55 -332,70",
        "O7|5|-261,1 -262,-15 -261,-34 -262,-52 -261,-66",
        "O8|5|-297,-34 -280,-34 -262,-33 -244,-34 -228,-33",
        "I1|8|-352,-186 -340,-185 -324,-186 -307,-185 -301,-186",
        "I3|8|-261,-145 -262,-130 -261,-114 -262,-100 -261,-85",
        "I4|7|-284,-115 -270,-115 -262,-114 -244,-115 -228,-114",
        "Z1a|4|-227,6 -227,26",
        "Z1b|4|-227,34 -227,60",
        "Z2a|4|-296,71 -270,70",
        "Z2b|4|-258,71 -228,70",
        "Y1|5|-323,-67 -323,-52",
    };

    static readonly string[] RdWallRoads =
    {
        "H1|6|136,-4 131,-15 122,-27 115,-39 112,-58 118,-80 128,-100 135,-118",
        "H2|3|135,-14 150,-12 164,-26 180,-27 193,-39 204,-58",
        "H3|3|115,15 111,28 116,37",
    };

    // Existujúce ulice Centra (xmin, xmax, zmin, zmax); cesty sa pri nich orežú, aby sa neprekrývali
    static readonly float[][] RdStreetRects =
    {
        new[] { -379f, -365f, -221f, 73f },   // AvenueS
        new[] { -519f, -225f, -81f, -67f },   // AvenueV
        new[] { -451f, -443f, -221f, 73f },   // UlicaS (západ)
        new[] { -301f, -293f, -221f, 73f },   // UlicaS (východ)
        new[] { -519f, -225f, -153f, -145f }, // UlicaV (juh)
        new[] { -519f, -225f, -3f, 5f },      // UlicaV (sever)
    };

    class RdRoad
    {
        public string id;
        public float width;
        public List<Vector2> pts = new List<Vector2>();
    }

    class RdMats
    {
        public Material hlavna, obytna, odboj, podsvetie, priemysel, ruiny, hradba, kerb;
        public Material lampOn, lampOff, pole, wood, stripeRed, stripeWhite, hazard, glassGreen, concrete, trash, manhole;
    }

    static RdMats rdm;
    static Transform rdRoot, rdPropsRoot;
    static RoadLamps rdLamps;
    static int rdSignCounter;
    static float rdLiftCur = RdLift;
    static readonly List<GameObject> rdBuilt = new List<GameObject>();

    // ------------------------------------------------------------------ Menu
    [MenuItem("Progeny/Mesto/Cesty: postaviť ulice Centra a Hradby (blok 2)", false, 330)]
    static void BuildRoadsBlock2()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Cesty: najprv ukonči Play."); return; }
        GameObject city = GameObject.Find("Mesto_Centrum");
        GameObject wall = GameObject.Find("Hradba_Rozsirenie");
        if (city == null || wall == null) { Debug.LogWarning("Cesty: chýba Mesto_Centrum alebo Hradba_Rozsirenie."); return; }

        RdClear();
        RdEnsureFolders();
        rdm = RdCreateMaterials();
        Physics.SyncTransforms();

        BuildRoadsFor(city.transform, "Cesty_Centrum", RdCityY, RdCityRoads, true);
        BuildRoadsFor(wall.transform, "Hradba_Cesty", RdWallY, RdWallRoads, false);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(city.scene);
        RoadsVerify();
        Debug.Log("Cesty (blok 2): hotovo. Scéna NIE je uložená; skontroluj snímky a ulož po schválení.");
    }

    [MenuItem("Progeny/Mesto/Cesty: vypnúť tiene plochým povrchom a drobnostiam (blok 5)", false, 333)]
    static void RdDisableFlatShadows()
    {
        int n = 0;
        foreach (string rootName in new[] { "Cesty_Centrum", "Hradba_Cesty" })
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null) continue;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r.name.StartsWith("Cesta_") || r.name.StartsWith("Obrubnik") || r.name == "Kanal" || r.name == "Odpad") { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; n++; }
        }
        foreach (string z in WrZoneRoots)
        {
            GameObject root = GameObject.Find(z);
            Transform g = root != null ? root.transform.Find(WrGroup) : null;
            if (g == null) continue;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
                if (r.name == "Stopy_zvierat" || r.name == "Svetluska" || r.name == "Latka_znacka" || r.name == "Popol") { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; n++; }
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Cesty: tiene vypnuté pre {n} plochých povrchov a drobností.");
    }

    [MenuItem("Progeny/Mesto/Cesty: zmazať (blok 2)", false, 331)]
    static void ClearRoadsBlock2()
    {
        RdClear();
        Debug.Log("Cesty (blok 2): zmazané (materiály a siete v Assets ostali).");
    }

    static void RdClear()
    {
        string[] names = { "Cesty_Centrum", "Hradba_Cesty", "Stvrte_Centrum", "Ordinacia_Yumi", "Healer_Yumi" };
        foreach (string n in names)
            for (int guard = 0; guard < 8; guard++)
            {
                GameObject go = GameObject.Find(n);
                if (go == null) break;
                Object.DestroyImmediate(go);
            }
    }

    static void RdEnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(RdMeshDir)) AssetDatabase.CreateFolder("Assets/Models", "Roads");
        if (!AssetDatabase.IsValidFolder(RdMatDir)) AssetDatabase.CreateFolder("Assets/Materials", "Cesty");
    }

    // ------------------------------------------------------------------ Materiály
    static Texture RdTex(string matPath, string prop)
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        return m != null && m.HasProperty(prop) ? m.GetTexture(prop) : null;
    }

    static Material RdMat(string name, Texture albedo, Texture normal, Color tint, float smooth = 0.12f, Color? emission = null)
    {
        string path = $"{RdMatDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = tint;
        m.mainTexture = albedo;
        m.SetFloat("_Glossiness", smooth);
        m.SetFloat("_Metallic", 0f);
        if (normal != null) { m.SetTexture("_BumpMap", normal); m.EnableKeyword("_NORMALMAP"); }
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static RdMats RdCreateMaterials()
    {
        const string P = "Assets/Materials/Povrchy/";
        Texture oldRoad = RdTex(P + "old_road.mat", "_MainTex"), oldRoadN = RdTex(P + "old_road.mat", "_BumpMap");
        Texture asphalt = RdTex(P + "damaged_asphalt.mat", "_MainTex"), asphaltN = RdTex(P + "damaged_asphalt.mat", "_BumpMap");
        Texture plaster = RdTex(P + "construction_plaster.mat", "_MainTex"), plasterN = RdTex(P + "construction_plaster.mat", "_BumpMap");
        Texture wallTex = RdTex(P + "castle_wall.mat", "_MainTex"), wallN = RdTex(P + "castle_wall.mat", "_BumpMap");
        const string NM = "Assets/NatureManufacture Assets/Forest Environment Dynamic Nature/Roads/Textures/";
        Texture dirt = AssetDatabase.LoadAssetAtPath<Texture2D>(NM + "T_ground_beech_forest_road_01_BC_M.tga");
        Texture dirtN = AssetDatabase.LoadAssetAtPath<Texture2D>(NM + "T_ground_beech_forest_road_01_N.png");
        Material manhole = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Stavby_do_hry/Metal_Manhole_Cover_wdlmdgebw_High.mat");

        var m = new RdMats
        {
            hlavna = RdMat("Cesta_Hlavna", oldRoad, oldRoadN, new Color(0.80f, 0.80f, 0.85f)),
            obytna = RdMat("Cesta_Obytna", oldRoad, oldRoadN, new Color(0.95f, 0.86f, 0.70f)),
            odboj = RdMat("Cesta_Odboj", oldRoad, oldRoadN, new Color(0.55f, 0.62f, 0.80f)),
            podsvetie = RdMat("Cesta_Podsvetie", asphalt, asphaltN, new Color(0.62f, 0.57f, 0.64f), 0.05f),
            priemysel = RdMat("Cesta_Priemysel", plaster, plasterN, new Color(0.62f, 0.62f, 0.64f), 0.08f),
            ruiny = RdMat("Cesta_Ruiny", dirt, dirtN, new Color(1f, 0.95f, 0.86f), 0.02f),
            hradba = RdMat("Cesta_Hradba", oldRoad, oldRoadN, new Color(1f, 0.93f, 0.80f)),
            kerb = RdMat("Cesta_Obrubnik", wallTex, wallN, new Color(0.75f, 0.75f, 0.78f), 0.05f),
            lampOff = RdMat("Lampa_Zhasnuta", null, null, new Color(0.35f, 0.33f, 0.30f), 0.3f),
            lampOn = RdMat("Lampa_Svieti", null, null, new Color(1f, 0.85f, 0.6f), 0.3f, new Color(4.2f, 3.1f, 1.6f)),
            pole = RdMat("Cesta_Stlp", null, null, new Color(0.14f, 0.14f, 0.16f), 0.4f),
            wood = RdMat("Cesta_Drevo", null, null, new Color(0.22f, 0.15f, 0.10f), 0.1f),
            stripeRed = RdMat("Cesta_PruhCerveny", null, null, new Color(0.75f, 0.1f, 0.08f), 0.2f),
            stripeWhite = RdMat("Cesta_PruhBiely", null, null, new Color(0.9f, 0.9f, 0.88f), 0.2f),
            hazard = RdMat("Cesta_Hazard", null, null, new Color(0.9f, 0.7f, 0.05f), 0.2f),
            glassGreen = RdMat("Cesta_ZeleneSvetlo", null, null, new Color(0.2f, 1f, 0.45f), 0.3f, new Color(0.4f, 2.2f, 0.9f)),
            concrete = RdMat("Cesta_Betonovy", plaster, plasterN, new Color(0.45f, 0.45f, 0.47f), 0.05f),
            trash = RdMat("Cesta_Odpad", null, null, new Color(0.12f, 0.12f, 0.11f), 0.1f),
            manhole = manhole != null ? manhole : RdMat("Cesta_Kanal", null, null, new Color(0.12f, 0.12f, 0.13f), 0.5f),
        };
        return m;
    }

    // ------------------------------------------------------------------ Geometria
    static List<RdRoad> RdParse(string[] lines)
    {
        var list = new List<RdRoad>();
        foreach (string line in lines)
        {
            string[] f = line.Split('|');
            var r = new RdRoad { id = f[0], width = float.Parse(f[1], CultureInfo.InvariantCulture) };
            foreach (string s in f[2].Split(' '))
            {
                string[] xy = s.Split(',');
                r.pts.Add(new Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture)));
            }
            list.Add(r);
        }
        return list;
    }

    static List<Vector3> RdSample(List<Vector2> pts, float step)
    {
        var res = new List<Vector3>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
            int n = Mathf.Max(2, Mathf.CeilToInt((p2 - p1).magnitude / step));
            for (int k = 0; k < n; k++)
            {
                float t = (float)k / n;
                Vector2 q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                res.Add(new Vector3(q.x, 0f, q.y));
            }
        }
        res.Add(new Vector3(pts[pts.Count - 1].x, 0f, pts[pts.Count - 1].y));
        return res;
    }

    static float RdGround(float x, float z, float baseY)
    {
        if (Physics.Raycast(new Vector3(x, baseY + 2f, z), Vector3.down, out RaycastHit h, 4f, ~0, QueryTriggerInteraction.Ignore)
            && h.point.y > baseY - 0.3f && h.point.y < baseY + 1f)
            return h.point.y;
        return baseY;
    }

    static bool RdInStreet(Vector3 p)
    {
        foreach (float[] r in RdStreetRects)
            if (p.x > r[0] + 0.25f && p.x < r[1] - 0.25f && p.z > r[2] + 0.25f && p.z < r[3] - 0.25f) return true;
        return false;
    }

    /// <summary>Postaví mesh cesty pozdĺž krivky. trim = orež úseky vo vnútri existujúcich ulíc. Výška sa meria lúčom pod každým bodom.</summary>
    static GameObject RdBuildRoad(string id, List<Vector2> pts, float width, Material mat, float baseY, float tile, float variance, float ragged, bool kerb, bool trim, Transform parent)
    {
        List<Vector3> s = RdSample(pts, 1f);
        if (trim)
        {
            int first = 0, last = s.Count - 1;
            while (first < last && RdInStreet(s[first])) first++;
            while (last > first && RdInStreet(s[last])) last--;
            var cut = new List<Vector3>();
            if (first > 0) cut.Add(Vector3.Lerp(s[first], s[first - 1], 0.9f));
            for (int i = first; i <= last; i++) cut.Add(s[i]);
            if (last < s.Count - 1) cut.Add(Vector3.Lerp(s[last], s[last + 1], 0.9f));
            s = cut;
        }
        if (s.Count < 2) return null;

        int n = s.Count;
        var y = new float[n];
        for (int i = 0; i < n; i++) y[i] = RdGround(s[i].x, s[i].z, baseY);
        var ys = new float[n];
        for (int i = 0; i < n; i++)   // 5-bodové vyhladenie výšky pozdĺž cesty
        {
            float sum = 0f; int c = 0;
            for (int k = -2; k <= 2; k++) { int j = Mathf.Clamp(i + k, 0, n - 1); sum += y[j]; c++; }
            ys[i] = Mathf.Max(y[i], sum / c) + rdLiftCur;   // nikdy pod podkladom (obrubníky, chodníky)
        }

        float seed = Mathf.Abs(id.GetHashCode() % 997);
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>(); var tans = new List<Vector4>();
        var kV = new List<Vector3>(); var kUv = new List<Vector2>(); var kT = new List<int>();
        var tangents = new Vector3[n]; var normals = new Vector3[n]; var half = new float[n];
        float dist = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = s[Mathf.Max(0, i - 1)], b = s[Mathf.Min(n - 1, i + 1)];
            Vector3 t = new Vector3(b.x - a.x, 0f, b.z - a.z).normalized;
            tangents[i] = t;
            normals[i] = new Vector3(-t.z, 0f, t.x);
            if (i > 0) dist += Vector3.Distance(s[i - 1], s[i]);
            half[i] = 0.5f * width * (1f + variance * (Mathf.PerlinNoise(seed + dist * 0.07f, 0.5f) - 0.5f) * 2f);
            float lo = ragged * (Mathf.PerlinNoise(seed + dist * 0.35f, 3.1f) - 0.5f) * 2f;
            float ro = ragged * (Mathf.PerlinNoise(seed + dist * 0.35f, 7.7f) - 0.5f) * 2f;
            Vector3 c = new Vector3(s[i].x, ys[i], s[i].z);
            Vector3 L = c + normals[i] * (half[i] + lo), R = c - normals[i] * (half[i] + ro);
            verts.Add(L); verts.Add(R);
            uvs.Add(new Vector2(0f, dist / tile)); uvs.Add(new Vector2(2f * half[i] / tile, dist / tile));
            tans.Add(new Vector4(-normals[i].x, 0f, -normals[i].z, 1f)); tans.Add(new Vector4(-normals[i].x, 0f, -normals[i].z, 1f));
        }
        for (int i = 0; i < n - 1; i++)
        {
            int l0 = i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
            tris.Add(l0); tris.Add(l1); tris.Add(r0);
            tris.Add(r0); tris.Add(l1); tris.Add(r1);
        }

        var mesh = new Mesh { name = "Cesta_" + id };
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        var norms = new List<Vector3>(); for (int i = 0; i < verts.Count; i++) norms.Add(Vector3.up);
        mesh.SetNormals(norms); mesh.SetTangents(tans);
        mesh.RecalculateBounds();
        string path = $"{RdMeshDir}/Cesta_{id}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);

        GameObject go = new GameObject("Cesta_" + id);
        go.transform.SetParent(parent, true);
        go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var surfaceRenderer = go.AddComponent<MeshRenderer>();
        surfaceRenderer.sharedMaterial = mat;
        surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // plochý povrch tieň nepotrebuje
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true;
        rdBuilt.Add(go);

        if (kerb)
        {
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 Pe(int k, float off, float dy) => new Vector3(s[k].x, ys[k], s[k].z) + normals[k] * side * (half[k] + off) + Vector3.up * dy;
                    const float W = 0.28f, H = 0.11f;
                    Vector3[] a = { Pe(i, 0f, 0f), Pe(i, 0f, H), Pe(i, W, H), Pe(i, W, -rdLiftCur) };
                    Vector3[] b = { Pe(i + 1, 0f, 0f), Pe(i + 1, 0f, H), Pe(i + 1, W, H), Pe(i + 1, W, -rdLiftCur) };
                    Vector3 toCenter = -normals[i] * side, outward = normals[i] * side;
                    RdQuad(kV, kUv, kT, a[0], a[1], b[1], b[0], toCenter, i);
                    RdQuad(kV, kUv, kT, a[1], a[2], b[2], b[1], Vector3.up, i);
                    RdQuad(kV, kUv, kT, a[2], a[3], b[3], b[2], outward, i);
                }
            var km = new Mesh { name = "Obrubnik_" + id };
            km.SetVertices(kV); km.SetUVs(0, kUv); km.SetTriangles(kT, 0);
            km.RecalculateNormals(); km.RecalculateBounds();
            string kp = $"{RdMeshDir}/Obrubnik_{id}.asset";
            AssetDatabase.DeleteAsset(kp);
            AssetDatabase.CreateAsset(km, kp);
            GameObject kg = new GameObject("Obrubnik_" + id);
            kg.transform.SetParent(go.transform, true);
            kg.transform.position = Vector3.zero;
            kg.AddComponent<MeshFilter>().sharedMesh = km;
            var kerbRenderer = kg.AddComponent<MeshRenderer>();
            kerbRenderer.sharedMaterial = rdm.kerb;
            kerbRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            kg.isStatic = true;
        }
        return go;
    }

    static void RdQuad(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 want, int step)
    {
        int o = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        float u = step * 0.5f;
        uv.Add(new Vector2(u, 0f)); uv.Add(new Vector2(u, 0.3f)); uv.Add(new Vector2(u + 0.5f, 0.3f)); uv.Add(new Vector2(u + 0.5f, 0f));
        Vector3 nrm = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(nrm, want) >= 0f) { t.Add(o); t.Add(o + 1); t.Add(o + 2); t.Add(o); t.Add(o + 2); t.Add(o + 3); }
        else { t.Add(o); t.Add(o + 2); t.Add(o + 1); t.Add(o); t.Add(o + 3); t.Add(o + 2); }
    }

    // ------------------------------------------------------------------ Pomocné objekty
    static GameObject RdBox(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rot, Material mat, bool collider, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(center, rot);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.isStatic = true;
        return go;
    }

    static void RdAddLamp(Vector3 groundPos, bool withLight, Color lightColor, float height = 5f)
    {
        GameObject pole = RdBox(rdPropsRoot, "StlpLampy", groundPos + Vector3.up * (height * 0.5f), new Vector3(0.14f, height * 0.5f, 0.14f), Quaternion.identity, rdm.pole, true, PrimitiveType.Cylinder);
        GameObject head = RdBox(rdPropsRoot, "Lampa", groundPos + Vector3.up * (height + 0.15f), new Vector3(0.55f, 0.3f, 0.55f), Quaternion.identity, rdm.lampOff, false);
        Light light = null;
        if (withLight)
        {
            var lg = new GameObject("Svetlo");
            lg.transform.SetParent(rdPropsRoot, true);
            lg.transform.position = groundPos + Vector3.up * (height - 0.2f);
            light = lg.AddComponent<Light>();
            light.type = LightType.Point; light.range = 14f; light.intensity = 2.1f; light.color = lightColor;
            light.shadows = LightShadows.None; light.enabled = false;
        }
        rdLamps.Add(head.GetComponent<Renderer>(), light);
    }

    static bool RdFree(Vector3 p, float radius, float baseY) =>
        !Physics.CheckSphere(new Vector3(p.x, baseY + 1.6f, p.z), radius, ~0, QueryTriggerInteraction.Ignore) && !RdNearHouse(p, baseY);

    static bool RdNearHouse(Vector3 p, float baseY)
    {
        foreach (Collider c in Physics.OverlapBox(new Vector3(p.x, baseY + 1.5f, p.z), new Vector3(1.2f, 0.9f, 1.2f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (!(c is TerrainCollider)) return true;
        return false;
    }

    /// <summary>Lampy pozdĺž priamky (striedavo vľavo/vpravo), vynechá obsadené miesta a okolie rondelu.</summary>
    static int RdLampLine(Vector3 a, Vector3 b, float spacing, float offset, bool lights, Color color, float baseY, bool skipRoundabout)
    {
        float len = Vector3.Distance(a, b); Vector3 dir = (b - a) / len; Vector3 side = new Vector3(-dir.z, 0f, dir.x);
        int count = 0, k = 0;
        for (float d = spacing * 0.5f; d < len; d += spacing, k++)
        {
            Vector3 p = a + dir * d + side * offset * (k % 2 == 0 ? 1f : -1f);
            if (skipRoundabout && Vector2.Distance(new Vector2(p.x, p.z), new Vector2(-372f, -74f)) < 42f) continue;
            p.y = RdGround(p.x, p.z, baseY);
            if (!RdFree(p, 0.5f, baseY)) continue;
            RdAddLamp(p, lights, color);
            count++;
        }
        return count;
    }

    static GameObject RdProp(string assetPath, Vector3 pos, float yaw, float targetSize, Transform parent)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) { Object.DestroyImmediate(go); return null; }
        Bounds b = rs[0].bounds; foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        float k = targetSize / Mathf.Max(b.size.x, b.size.z, 0.01f);
        go.transform.localScale = Vector3.one * k;
        b = rs[0].bounds; foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        var box = go.AddComponent<BoxCollider>();
        box.center = go.transform.InverseTransformPoint(b.center);
        box.size = b.size / k;
        go.transform.SetPositionAndRotation(new Vector3(pos.x, pos.y - b.min.y, pos.z), Quaternion.Euler(0f, yaw, 0f));
        foreach (Transform t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        return go;
    }

    static void RdLabel(Vector3 pos, string text, float size = 0.07f) =>
        PortalNetwork.MakeLabel(pos, rdPropsRoot, "Napis_Cesta_" + (++rdSignCounter) + "_" + text, text, size);

    // ------------------------------------------------------------------ Stavba
    static void BuildRoadsFor(Transform zone, string rootName, float baseY, string[] data, bool isCity)
    {
        var root = new GameObject(rootName);
        root.transform.SetParent(zone, true);
        root.transform.position = Vector3.zero;
        rdRoot = root.transform;
        var props = new GameObject("Detaily");
        props.transform.SetParent(rdRoot, true);
        rdPropsRoot = props.transform;
        rdLamps = root.AddComponent<RoadLamps>();
        rdLamps.Configure(rdm.lampOn, rdm.lampOff);
        rdBuilt.Clear();
        rdLiftCur = isCity ? RdLift : 0.10f;   // dlažba Hradby má vizuálnu rovinu na 3,05 bez kolízie

        if (isCity) BuildCityOverlays(baseY);
        Physics.SyncTransforms();

        foreach (RdRoad r in RdParse(data))
        {
            Material mat; float tile = 4f, variance = 0.03f, ragged = 0f; bool kerb = true;
            char k = r.id[0];
            if (k == 'U') { mat = rdm.podsvetie; tile = 3f; variance = 0.10f; ragged = 0.18f; kerb = false; }
            else if (k == 'R') mat = rdm.obytna;
            else if (k == 'O' || k == 'Y') mat = rdm.odboj;
            else if (k == 'I') mat = rdm.priemysel;
            else if (k == 'Z') { mat = rdm.ruiny; variance = 0.12f; ragged = 0.3f; kerb = false; }
            else if (r.id == "H1") mat = rdm.hradba;
            else { mat = rdm.ruiny; variance = 0.1f; ragged = 0.15f; kerb = false; }
            RdBuildRoad(r.id, r.pts, r.width, mat, baseY, tile, variance, ragged, kerb, isCity, rdRoot);
            Physics.SyncTransforms();
        }

        if (isCity) BuildCityDetails(zone, baseY);
        else BuildWallDetails(zone, baseY);
    }

    static void BuildCityOverlays(float baseY)
    {
        // (os, pevná súradnica, od, do, materiál)  os 'S' = ulica pozdĺž Z pri danom X, 'V' = ulica pozdĺž X pri danom Z
        var segs = new List<(char axis, float fixedC, float a, float b, float w, Material mat)>();
        void Add(char axis, float fx, float a, float b, float w, Material m) => segs.Add((axis, fx, a, b, w, m));
        // Hlavné ulice mimo rondelu (r = 34 okolo -372,-74)
        Add('S', -372f, -221f, -108f, 13.6f, rdm.hlavna); Add('S', -372f, -40f, 73f, 13.6f, rdm.hlavna);
        Add('V', -74f, -519f, -406f, 13.6f, rdm.hlavna); Add('V', -74f, -338f, -225f, 13.6f, rdm.hlavna);
        // UlicaS západ (x = -447)
        Add('S', -447f, -221f, -153f, 7.7f, rdm.podsvetie); Add('S', -447f, -145f, -81f, 7.7f, rdm.podsvetie);
        Add('S', -447f, -67f, -3f, 7.7f, rdm.obytna); Add('S', -447f, 5f, 73f, 7.7f, rdm.obytna);
        // UlicaS východ (x = -297)
        Add('S', -297f, -221f, -153f, 7.7f, rdm.priemysel); Add('S', -297f, -145f, -81f, 7.7f, rdm.priemysel);
        Add('S', -297f, -67f, -3f, 7.7f, rdm.odboj); Add('S', -297f, 5f, 73f, 7.7f, rdm.ruiny);
        // UlicaV juh (z = -149)
        Add('V', -149f, -519f, -451f, 7.7f, rdm.podsvetie); Add('V', -149f, -443f, -379f, 7.7f, rdm.podsvetie);
        Add('V', -149f, -365f, -301f, 7.7f, rdm.priemysel); Add('V', -149f, -293f, -225f, 7.7f, rdm.priemysel);
        // UlicaV sever (z = 1)
        Add('V', 1f, -519f, -451f, 7.7f, rdm.obytna); Add('V', 1f, -443f, -379f, 7.7f, rdm.obytna);
        Add('V', 1f, -365f, -301f, 7.7f, rdm.odboj); Add('V', 1f, -293f, -225f, 7.7f, rdm.ruiny);

        int n = 0;
        foreach (var s in segs)
        {
            var pts = s.axis == 'S'
                ? new List<Vector2> { new Vector2(s.fixedC, s.a), new Vector2(s.fixedC, s.b) }
                : new List<Vector2> { new Vector2(s.a, s.fixedC), new Vector2(s.b, s.fixedC) };
            RdBuildRoad("Povrch_" + (++n), pts, s.w, s.mat, baseY, 4f, 0f, 0f, false, false, rdRoot);
            Physics.SyncTransforms();
        }
    }

    static void BuildCityDetails(Transform zone, float baseY)
    {
        // Lampy: hlavné ulice so svetlom, bočné ulice len s žiarou
        int lamps = 0;
        Color warm = new Color(1f, 0.8f, 0.55f);
        lamps += RdLampLine(new Vector3(-372f, 0f, -216f), new Vector3(-372f, 0f, 68f), 34f, 9.5f, true, warm, baseY, true);   // pri osi sever–juh
        RdLampLine(new Vector3(-514f, 0f, -74f), new Vector3(-230f, 0f, -74f), 34f, 9.5f, true, warm, baseY, true);
        foreach (RdRoad r in RdParse(RdCityRoads))
        {
            if (r.id.StartsWith("Z") || r.id == "Y1" || r.id == "U6") continue;
            List<Vector3> s = RdSample(r.pts, 1f);
            Color c = r.id[0] == 'U' ? new Color(0.75f, 0.4f, 1f) : r.id[0] == 'I' ? new Color(1f, 0.75f, 0.4f) : warm;
            float spacing = r.id[0] == 'U' ? 22f : 34f;
            float acc = spacing * 0.5f, offset = r.width * 0.5f + 0.9f; int k = 0;
            for (int i = 1; i < s.Count; i++)
            {
                acc -= Vector3.Distance(s[i - 1], s[i]);
                if (acc > 0f) continue;
                acc += spacing; k++;
                Vector3 t = (s[i] - s[i - 1]).normalized; Vector3 nrm = new Vector3(-t.z, 0f, t.x);
                Vector3 p = s[i] + nrm * offset * (k % 2 == 0 ? 1f : -1f);
                p.y = RdGround(p.x, p.z, baseY);
                if (RdInStreet(p) || !RdFree(p, 0.5f, baseY)) continue;
                RdAddLamp(p, false, c, r.id[0] == 'U' ? 3.6f : 4.5f);
                lamps++;
            }
        }

        // Kanály (kryty) na hlavných uliciach
        for (float z = -200f; z < 60f; z += 46f)
            if (Mathf.Abs(z + 74f) > 40f) RdManhole(new Vector3(-368.5f, 0f, z), baseY);
        for (float x = -500f; x < -240f; x += 46f)
            if (Mathf.Abs(x + 372f) > 40f) RdManhole(new Vector3(x, 0f, -77f), baseY);

        // Slepé uličky: bedne a odpadky (úkryty)
        string crate = "Assets/Materials/Stavby_do_hry/Industrial_Junkyard_Crate_Metal_wdlifhrs_High.fbx";
        Vector3[] ends = { new Vector3(-513f, 0f, -108f), new Vector3(-399f, 0f, -186f), new Vector3(-515f, 0f, -36f), new Vector3(-229f, 0f, -114f) };
        float[] yaws = { 20f, 70f, 10f, 100f };
        for (int i = 0; i < ends.Length; i++)
        {
            Vector3 p = ends[i]; p.y = RdGround(p.x, p.z, baseY);
            RdProp(crate, p + new Vector3(0.6f, 0f, 1.1f), yaws[i], 1.0f, rdPropsRoot);
            RdBox(rdPropsRoot, "Odpad", p + new Vector3(-0.5f, 0.12f, -0.8f), new Vector3(0.5f, 0.24f, 0.4f), Quaternion.Euler(0f, yaws[i] * 2f, 0f), rdm.trash, false);
        }

        // Smerové tabule
        RdBox(rdPropsRoot, "StlpZnacky", new Vector3(-326f, RdGround(-326f, -65.5f, baseY) + 1.6f, -65.5f), new Vector3(0.15f, 3.2f, 0.15f), Quaternion.identity, rdm.pole, true);
        RdLabel(new Vector3(-326f, RdGround(-326f, -65.5f, baseY) + 3.9f, -65.5f), "Ordinácia Yumi");
        RdBox(rdPropsRoot, "StlpZnacky", new Vector3(-363.5f, RdGround(-363.5f, -186f, baseY) + 1.6f, -186f), new Vector3(0.15f, 3.2f, 0.15f), Quaternion.identity, rdm.pole, true);
        RdLabel(new Vector3(-363.5f, RdGround(-363.5f, -186f, baseY) + 3.9f, -186f), "Portály a priemysel");

        BuildCityGates(baseY);
        BuildDistricts(zone, baseY);
        BuildYumiClinic(zone, baseY);
    }

    static void RdManhole(Vector3 p, float baseY)
    {
        float y = RdGround(p.x, p.z, baseY) + 0.04f;
        if (Physics.CheckSphere(new Vector3(p.x, y + 0.5f, p.z), 0.6f, ~0, QueryTriggerInteraction.Ignore)) return;
        RdBox(rdPropsRoot, "Kanal", new Vector3(p.x, y, p.z), new Vector3(0.9f, 0.01f, 0.9f), Quaternion.identity, rdm.manhole, false, PrimitiveType.Cylinder);
    }

    static void RdGatePosts(string name, Vector3 center, float yaw, float span, float height, Material mat, bool beam)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        Vector3 right = q * Vector3.right;
        float y = RdGround(center.x, center.z, RdCityY);
        for (int s = -1; s <= 1; s += 2)
            RdBox(rdPropsRoot, name + "_stlp", new Vector3(center.x, y, center.z) + right * s * span * 0.5f + Vector3.up * height * 0.5f, new Vector3(0.5f, height, 0.5f), q, mat, true);
        if (beam)
            RdBox(rdPropsRoot, name + "_traverza", new Vector3(center.x, y + height + 0.15f, center.z), new Vector3(span + 1f, 0.45f, 0.6f), q, mat, false);
    }

    static void RdBoom(string name, Vector3 pivot, float yaw, float length, float raiseDeg)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        int segs = 6; float seg = length / segs;
        for (int i = 0; i < segs; i++)
        {
            Vector3 dir = q * (Quaternion.Euler(0f, 0f, raiseDeg) * Vector3.right);
            Vector3 c = pivot + dir * (seg * (i + 0.5f));
            RdBox(rdPropsRoot, name, c, new Vector3(seg, 0.18f, 0.18f), q * Quaternion.Euler(0f, 0f, raiseDeg), i % 2 == 0 ? rdm.stripeRed : rdm.stripeWhite, false);
        }
    }

    static void BuildCityGates(float baseY)
    {
        // G1: drevená oblúková brána civilná ↔ podsvetie (UlicaS x = -447)
        RdGatePosts("Brana_Podsvetie", new Vector3(-447f, 0f, -90f), 0f, 10f, 4.2f, rdm.wood, true);
        RdLabel(new Vector3(-447f, RdGround(-447f, -90f, baseY) + 5.4f, -90f), "Podsvetie");
        // G2: pruhovaná závora odboj ↔ priemysel (UlicaS x = -297)
        float y2 = RdGround(-297f, -90f, baseY);
        RdBox(rdPropsRoot, "Zavora_stlp", new Vector3(-302.4f, y2 + 0.7f, -90f), new Vector3(0.4f, 1.4f, 0.4f), Quaternion.identity, rdm.hazard, true);
        RdBoom("Zavora", new Vector3(-302.2f, y2 + 1.3f, -90f), 0f, 9f, 55f);
        RdLabel(new Vector3(-297f, y2 + 3.8f, -90f), "Priemyselná štvrť");
        // G3: kontrolný bod pri nádvorí (AvenueS, z = -149), dve búdky a vztýčené závory (priechod je voľný)
        float y3 = RdGround(-372f, -156f, baseY);
        foreach (float sx in new[] { -382.5f, -361.5f })
        {
            RdBox(rdPropsRoot, "Kontrola_budka", new Vector3(sx, y3 + 1.25f, -156f), new Vector3(2f, 2.5f, 2f), Quaternion.identity, rdm.concrete, true);
            RdBox(rdPropsRoot, "Kontrola_okno", new Vector3(sx + (sx < -372f ? 1.01f : -1.01f), y3 + 1.5f, -156f), new Vector3(0.05f, 0.7f, 1.2f), Quaternion.identity, rdm.lampOn, false);
        }
        RdBoom("Kontrola_zavora", new Vector3(-381.4f, y3 + 1.2f, -156f), 0f, 6f, 65f);
        RdBoom("Kontrola_zavora", new Vector3(-362.6f, y3 + 1.2f, -156f), 180f, 6f, 65f);
        RdProp("Assets/Materials/Stavby_do_hry/Military_Trenches_Barrier_Sandbag_Canvas_Worn_ydxlcck_High.fbx", new Vector3(-384.8f, y3, -153.2f), 90f, 2.2f, rdPropsRoot);
        RdLabel(new Vector3(-372f, y3 + 4.8f, -156f), "Kontrola portálov");
        // G4: lampióny na hranici civilná ↔ odboj (AvenueS, z = 1)
        foreach (float sx in new[] { -381.5f, -363.5f })
        {
            float yl = RdGround(sx, 7f, baseY);
            RdBox(rdPropsRoot, "Lampion_stlp", new Vector3(sx, yl + 1.8f, 7f), new Vector3(0.14f, 3.6f, 0.14f), Quaternion.identity, rdm.pole, true);
            GameObject lantern = RdBox(rdPropsRoot, "Lampion", new Vector3(sx, yl + 3.8f, 7f), new Vector3(0.7f, 0.9f, 0.7f), Quaternion.identity, rdm.lampOff, false, PrimitiveType.Sphere);
            rdLamps.Add(lantern.GetComponent<Renderer>(), null);
        }
        // G5: polozrútená brána do ruín (UlicaV z = 1, x = -289)
        float y5 = RdGround(-289f, 1f, baseY);
        RdBox(rdPropsRoot, "Ruiny_stlp", new Vector3(-289f, y5 + 2.1f, -4.6f), new Vector3(0.6f, 4.2f, 0.6f), Quaternion.identity, rdm.concrete, true);
        RdBox(rdPropsRoot, "Ruiny_stlp", new Vector3(-289f, y5 + 1.5f, 6.6f), new Vector3(0.6f, 3.0f, 0.6f), Quaternion.Euler(14f, 0f, 9f), rdm.concrete, true);
        RdBox(rdPropsRoot, "Ruiny_paska", new Vector3(-289f, y5 + 1.1f, 1f), new Vector3(0.04f, 0.18f, 10.2f), Quaternion.identity, rdm.stripeRed, false);
        RdLabel(new Vector3(-289f, y5 + 5.2f, 1f), "Zničená stará časť");
    }

    static void BuildDistricts(Transform zone, float baseY)
    {
        var root = new GameObject("Stvrte_Centrum");
        root.transform.SetParent(zone, true);
        root.transform.position = Vector3.zero;
        var defs = new (DistrictType type, string name, float x0, float x1, float z0, float z1)[]
        {
            (DistrictType.Residential, "Civilna_stvrt", -522f, -372f, -74f, 76f),
            (DistrictType.Underworld, "Podsvetie", -522f, -372f, -224f, -74f),
            (DistrictType.Residential, "Stvrt_odboja", -372f, -222f, -74f, 76f),
            (DistrictType.Industrial, "Priemyselna_stvrt", -372f, -222f, -224f, -74f),
            (DistrictType.Ruins, "Zniceny_stara_cast", -297f, -222f, 0f, 76f),
        };
        foreach (var d in defs)
        {
            var go = new GameObject("Stvrt_" + d.name);
            go.transform.SetParent(root.transform, true);
            go.transform.position = new Vector3((d.x0 + d.x1) * 0.5f, 0f, (d.z0 + d.z1) * 0.5f);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(d.x1 - d.x0, 8f, d.z1 - d.z0);
            box.center = new Vector3(0f, 4f, 0f);
            var cd = go.AddComponent<CityDistrict>();
            cd.type = d.type;
            cd.showOnGround = false;
        }
    }

    static void BuildYumiClinic(Transform zone, float baseY)
    {
        // Ordinácia na konci Y1 (-323, -52): stôl, strieška, zelený kríž a Yumi (Healer)
        float x = -323f, z = -49.5f, y = RdGround(x, z, baseY);
        var clinic = new GameObject("Ordinacia_Yumi");
        clinic.transform.SetParent(zone, true);
        clinic.transform.position = Vector3.zero;
        Transform p = clinic.transform;
        RdBox(p, "Stol", new Vector3(x, y + 0.5f, z + 1.1f), new Vector3(2.4f, 1f, 0.9f), Quaternion.identity, rdm.wood, true);
        foreach (float dx in new[] { -1.4f, 1.4f })
            foreach (float dz in new[] { -1.4f, 1.4f })
                RdBox(p, "Stlpik", new Vector3(x + dx, y + 1.35f, z + dz), new Vector3(0.12f, 2.7f, 0.12f), Quaternion.identity, rdm.pole, true);
        RdBox(p, "Strieska", new Vector3(x, y + 2.75f, z), new Vector3(3.4f, 0.12f, 3.4f), Quaternion.identity, rdm.concrete, false);
        RdBox(p, "Kriz_V", new Vector3(x, y + 3.4f, z + 1.45f), new Vector3(0.22f, 1.0f, 0.12f), Quaternion.identity, rdm.glassGreen, false);
        RdBox(p, "Kriz_H", new Vector3(x, y + 3.4f, z + 1.45f), new Vector3(1.0f, 0.22f, 0.12f), Quaternion.identity, rdm.glassGreen, false);
        PortalNetwork.MakeLabel(new Vector3(x, y + 4.5f, z), p, "Napis_Ordinacia_Yumi", "Doktorka Yumi", 0.08f);
        Spawn(zone, "Healer_Yumi", NPCKind.Civilian, FactionId.Civilians, new Vector3(x, y, z - 0.2f), true);
        GameObject yumi = GameObject.Find("Healer_Yumi");
        if (yumi != null && yumi.GetComponentInChildren<Renderer>() == null)
        {
            // Telo ako u ostatných príbehových postáv (kapsula); animovaný model doplní „Aktualizovať všetko“
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Telo";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(yumi.transform, false);
            body.transform.localPosition = new Vector3(0f, 1f, 0f);
            body.GetComponent<Renderer>().sharedMaterial = rdm.glassGreen;
        }
    }

    static void BuildWallDetails(Transform zone, float baseY)
    {
        Color warm = new Color(1f, 0.8f, 0.55f);
        List<Vector3> h1 = RdSample(RdParse(RdWallRoads)[0].pts, 1f);
        float acc = 14f; int k = 0;
        for (int i = 1; i < h1.Count; i++)
        {
            acc -= Vector3.Distance(h1[i - 1], h1[i]);
            if (acc > 0f) continue;
            acc += 30f; k++;
            Vector3 t = (h1[i] - h1[i - 1]).normalized; Vector3 nrm = new Vector3(-t.z, 0f, t.x);
            Vector3 pos = h1[i] + nrm * 3.9f * (k % 2 == 0 ? 1f : -1f);
            pos.y = RdGround(pos.x, pos.z, baseY);
            if (RdFree(pos, 0.5f, baseY)) RdAddLamp(pos, true, warm);
        }
        // Brána v lese (H2 pri X = 157) a južná brána na konci H1
        List<Vector3> h2 = RdSample(RdParse(RdWallRoads)[1].pts, 0.5f);
        Vector3 g = h2[0]; foreach (Vector3 v in h2) if (Mathf.Abs(v.x - 157f) < Mathf.Abs(g.x - 157f)) g = v;
        Vector3 tg = Vector3.right; int gi = h2.IndexOf(g);
        if (gi >= 0 && gi + 1 < h2.Count) tg = (h2[gi + 1] - h2[gi]).normalized;
        float yaw = Mathf.Atan2(tg.x, tg.z) * Mathf.Rad2Deg;
        float gy = RdGround(g.x, g.z, baseY);
        Quaternion q = Quaternion.Euler(0f, yaw + 0f, 0f);
        Vector3 rt = q * Vector3.right;
        for (int s = -1; s <= 1; s += 2)
            RdBox(rdPropsRoot, "Lesna_brana_stlp", new Vector3(g.x, gy, g.z) + rt * s * 2.4f + Vector3.up * 1.9f, new Vector3(0.9f, 3.8f, 0.9f), q, rdm.kerb, true);
        RdBox(rdPropsRoot, "Lesna_brana_oblúk", new Vector3(g.x, gy + 3.95f, g.z), new Vector3(5.8f, 0.7f, 0.9f), q, rdm.kerb, false);
        RdLabel(new Vector3(g.x, gy + 5.4f, g.z), "Les hradieb");
        Vector3 end = new Vector3(135f, RdGround(135f, -118f, baseY), -118f);
        RdLabel(end + Vector3.up * 4.2f, "Južná brána");
    }

    // ------------------------------------------------------------------ Kontrola
    [MenuItem("Progeny/Mesto/Cesty: skontrolovať (blok 2)", false, 332)]
    static void RoadsVerify()
    {
        Physics.SyncTransforms();
        int roads = 0, bad = 0; float maxStep = 0f;
        foreach (string rootName in new[] { "Cesty_Centrum", "Hradba_Cesty" })
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null) continue;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.name.StartsWith("Cesta_")) continue;
                roads++;
                Vector3[] v = mf.sharedMesh.vertices;
                float baseY = rootName == "Hradba_Cesty" ? RdWallY : RdCityY;
                for (int i = 0; i < v.Length; i += 2)
                {
                    Vector3 c = (v[i] + v[Mathf.Min(i + 1, v.Length - 1)]) * 0.5f;
                    float ground = RdGroundNoRoads(c.x, c.z, baseY);
                    float gap = c.y - ground;
                    maxStep = Mathf.Max(maxStep, Mathf.Abs(gap - (rootName == "Hradba_Cesty" ? 0.10f : RdLift)));
                    if (gap < 0.005f || gap > 0.2f) bad++;
                    Collider[] hit = Physics.OverlapSphere(new Vector3(c.x, c.y + 0.9f, c.z), 0.35f, ~0, QueryTriggerInteraction.Ignore);
                    foreach (Collider h in hit)
                        if (!(h is TerrainCollider) && !h.transform.IsChildOf(root.transform) && !h.name.StartsWith("Zem")) { bad++; break; }
                }
            }
        }
        Debug.Log($"Cesty: kontrola – trasy {roads}, body mimo tolerancie výšky alebo v kolízii {bad}, najväčšia odchýlka výšky od podkladu {maxStep * 100f:F1} cm.");
    }

    static float RdGroundNoRoads(float x, float z, float baseY)
    {
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, baseY + 2f, z), Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (RaycastHit h in hits)
        {
            if (h.collider.name.StartsWith("Cesta_")) continue;
            if (h.point.y > baseY - 0.3f && h.point.y < baseY + 1f && h.point.y > best) best = h.point.y;
        }
        return float.IsNegativeInfinity(best) ? baseY : best;
    }
}
