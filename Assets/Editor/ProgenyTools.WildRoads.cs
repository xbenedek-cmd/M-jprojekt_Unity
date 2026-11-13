using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Blok 3 plánu ciest: lesné cesty, chodníky, zvieracie stopy a skrytá odbočka v divočinách. Tvar je namaľovaný do vrstiev terénu
/// (udupaná zem s mäkkým okrajom, blato v nížinách, kamene na svahoch, stopy kolies), terén sa pod cestou vyrovná len minimálne.
/// Trasy: hlavná Divočina podľa schváleného návrhu, ostatné zóny hľadá A* po mriežke (svah, voda, stromy, skaly, existujúca cesta).
/// </summary>
public static partial class ProgenyTools
{
    const string WrGroup = "Cesty_Divociny";
    const float WrCell = 4f;
    const float WrBlocked = 1e6f;
    static readonly string[] WrZoneRoots = { "Divocina", "Divocina_Kanon", "Divocina_Les", "Divocina_Pustatina", "Divocina_Mociare", "Divocina_Bane" };

    // Schválený návrh pre hlavnú Divočinu (id|šírka|uzly), overený fyzikou
    static readonly string[] WrDesigned =
    {
        "W1|5|8,183 10,215 9,250 11,285 13,305",
        "W1a|4|13,305 5,330 -6,352 -6,372 -2,390 4,396",
        "W1b|4|13,305 30,325 36,351 43,379 52,396 66,414",
        "W1c|4|44,375 34,395 27,418 13,437 16,454",
        "T1|2|-6,352 -18,360 -22,370 -21,380",
        "T2|2|11,320 16,334",
        "T3|2|40,360 51,367",
        "T4|2|4,396 -8,408 -25,413 -33,421",
        "T5|2|66,414 69,402",
        "HB1|1.8|9,258 -18,262 -55,275 -80,300 -98,318 -108,322",
        "A1|0.9|16,334 6,342 -8,350 -17,353",
        "A2|0.9|4,396 -17,402 -35,404 -52,408",
        "A3|0.9|52,400 62,405 73,415",
        "A4|0.9|18,440 12,447 9,452",
        "A5|0.9|59,415 100,440 130,452 137,456",
        "A6|0.9|-52,420 -80,470 -100,495 -110,507",
    };

    class WrRoad
    {
        public string id;
        public char kind;            // W hlavná, T chodník, H skrytá odbočka, A zvieracia stopa
        public float width;
        public float edgeW;          // šírka mäkkého okraja (m)
        public List<Vector2> pts = new List<Vector2>();
        public List<Vector3> dense = new List<Vector3>();   // vyhladená stredová čiara (y = výška terénu)
        public List<float> half = new List<float>();
        public List<float> hs = new List<float>();          // vyhladená výška stredu pre vyrovnanie
        public bool disc;                                    // plocha (tábor): jediný bod s polomerom
    }

    class WrCtx
    {
        public Transform root, group;
        public Terrain ter;
        public TerrainData data;
        public Vector3 o;
        public float size;
        public string key;
        public WildLake[] lakes;
        public Vector2 center;
        public int n;
        public float[] cost;
        public HashSet<int> network = new HashSet<int>();
        public List<WrRoad> roads = new List<WrRoad>();
        public System.Random rnd;
        public Vector2 portal;
    }

    class WrRaster
    {
        public int res;
        public float[,] edge, ew, h, kf;
    }

    // ------------------------------------------------------------------ Menu
    [MenuItem("Progeny/Divočiny/Cesty: postaviť hlavnú Divočinu (blok 3)", false, 340)]
    static void BuildWildRoadsMain() => BuildWildRoads(new[] { "Divocina" });

    [MenuItem("Progeny/Divočiny/Cesty: postaviť všetky divočiny (blok 3)", false, 341)]
    static void BuildWildRoadsAll() => BuildWildRoads(WrZoneRoots);

    [MenuItem("Progeny/Divočiny/Cesty: zmazať dekor a značky (blok 3)", false, 342)]
    static void ClearWildRoads()
    {
        foreach (string r in WrZoneRoots)
        {
            GameObject root = GameObject.Find(r);
            if (root == null) continue;
            Transform g = root.transform.Find(WrGroup);
            if (g != null) UnityEngine.Object.DestroyImmediate(g.gameObject);
        }
        Debug.Log("Cesty divočín: dekor, značky a stopy zmazané (namaľovaná cesta v teréne ostala; záloha terénov v _backup_terrain).");
    }

    static void BuildWildRoads(string[] roots)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Cesty divočín: najprv ukonči Play."); return; }
        RdEnsureFolders();
        foreach (string rootName in roots)
        {
            GameObject root = GameObject.Find(rootName);
            if (root == null) { Debug.LogWarning("Cesty divočín: chýba " + rootName); continue; }
            try { BuildWildRoadsFor(root); }
            catch (Exception e) { Debug.LogError($"Cesty divočín ({rootName}): {e}"); }
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Cesty divočín (blok 3): hotovo. Scéna NIE je uložená.");
    }

    static void BuildWildRoadsFor(GameObject root)
    {
        Terrain ter = root.GetComponentInChildren<Terrain>(true);
        if (ter == null) { Debug.LogWarning("Cesty divočín: " + root.name + " nemá terén."); return; }

        Transform old = root.transform.Find(WrGroup);
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        Physics.SyncTransforms();

        var c = new WrCtx
        {
            root = root.transform, ter = ter, data = ter.terrainData, o = ter.transform.position, size = ter.terrainData.size.x,
            key = root.name == "Divocina" ? "Divocina" : root.name.Substring(9),
            lakes = root.GetComponentsInChildren<WildLake>(true),
        };
        c.center = new Vector2(c.o.x + c.size * 0.5f, c.o.z + c.size * 0.5f);
        c.rnd = new System.Random(c.key.GetHashCode() ^ 0x7a11);
        c.n = Mathf.RoundToInt(c.size / WrCell);
        c.group = new GameObject(WrGroup).transform;
        c.group.SetParent(root.transform, false);
        c.group.position = Vector3.zero;

        PortalGate gate = null;
        foreach (PortalGate g in root.GetComponentsInChildren<PortalGate>(true))
            if (g.name.StartsWith("Portal_z")) { gate = g; break; }
        c.portal = gate != null ? new Vector2(gate.transform.position.x, gate.transform.position.z) : c.center + new Vector2(0f, -185f);

        WrEnsureRoadLayer(c);
        WrBuildCost(c);

        if (c.key == "Divocina") WrAddDesigned(c);
        else WrGenerate(c);
        WrExtraTrails(c);

        foreach (WrRoad r in c.roads) WrDensify(c, r);

        // Hidden/camp discs musia byť v zozname pred maľovaním
        Vector2? camp = null;
        if (c.key == "Divocina") camp = WrPlanCamp(c);

        WrFlatten(c);
        WrPaint(c);
        Physics.SyncTransforms();

        foreach (WrRoad r in c.roads)
        {
            if (r.disc) continue;
            var marker = new GameObject("Cesta_" + r.id);
            marker.transform.SetParent(c.group, false);
            var path = marker.AddComponent<WildRoadPath>();
            var pts = new Vector3[r.dense.Count];
            for (int i = 0; i < pts.Length; i += 1) pts[i] = r.dense[i];
            path.Set(r.id, r.kind, r.width, pts);
        }

        var rnd = c.rnd;
        WrScatterProps(c, rnd);
        WrTracksMesh(c);
        WrSpecials(c, camp);
        WrVerify(c);
    }

    /// <summary>Vrstva 1 terénu = udupaná zem so stopami kolies (textúra lesnej cesty z balíka).</summary>
    static void WrEnsureRoadLayer(WrCtx c)
    {
        const string path = "Assets/Materials/Cesty/TL_Cesta_Zem.terrainlayer";
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            const string NM = "Assets/NatureManufacture Assets/Forest Environment Dynamic Nature/Roads/Textures/";
            layer = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(NM + "T_ground_beech_forest_road_01_BC_M.tga"),
                normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(NM + "T_ground_beech_forest_road_01_N.png"),
                tileSize = new Vector2(6f, 6f),
                smoothness = 0f,
                metallic = 0f,
            };
            AssetDatabase.CreateAsset(layer, path);
        }
        TerrainLayer[] layers = c.data.terrainLayers;
        if (layers.Length > 1 && layers[1] != layer)
        {
            layers[1] = layer;
            c.data.terrainLayers = layers;
            EditorUtility.SetDirty(c.data);
        }
    }

    // ------------------------------------------------------------------ Mriežka nákladov a A*
    static float WrHeight(WrCtx c, float x, float z) => c.ter.SampleHeight(new Vector3(x, 0f, z)) + c.o.y;

    static bool WrNearLake(WrCtx c, float x, float z, float extra)
    {
        foreach (WildLake l in c.lakes) if (l.Covers(new Vector3(x, 0f, z), extra)) return true;
        return false;
    }

    static void WrBuildCost(WrCtx c)
    {
        int n = c.n;
        c.cost = new float[n * n];
        int res = c.data.alphamapWidth;
        float[,,] maps = c.data.GetAlphamaps(0, 0, res, res);
        for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                float x = c.o.x + (ix + 0.5f) * WrCell, z = c.o.z + (iz + 0.5f) * WrCell;
                float u = (x - c.o.x) / c.size, v = (z - c.o.z) / c.size;
                float cost = 1f;
                float steep = c.data.GetSteepness(u, v);
                if (ix < 2 || iz < 2 || ix >= n - 2 || iz >= n - 2 || steep > 24f || WrNearLake(c, x, z, 8f)) { c.cost[iz * n + ix] = WrBlocked; continue; }
                cost += Mathf.Max(0f, steep - 8f) * 0.35f;

                float h = WrHeight(c, x, z);
                foreach (Collider col in Physics.OverlapBox(new Vector3(x, h + 1.2f, z), new Vector3(2f, 1f, 2f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (col is TerrainCollider || col.GetComponentInParent<DemonEnemy>() != null || col.transform.root.name == "Player") continue;
                    cost += 40f;
                    break;
                }
                int ax = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 0, res - 1), az = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 0, res - 1);
                if (maps[az, ax, 1] > 0.35f) cost *= 0.55f;   // existujúca cesta: radšej po nej
                c.cost[iz * n + ix] = cost;
            }
    }

    static int WrCellOf(WrCtx c, Vector2 p)
    {
        int ix = Mathf.Clamp(Mathf.FloorToInt((p.x - c.o.x) / WrCell), 0, c.n - 1);
        int iz = Mathf.Clamp(Mathf.FloorToInt((p.y - c.o.z) / WrCell), 0, c.n - 1);
        return iz * c.n + ix;
    }

    static Vector2 WrCellPos(WrCtx c, int id) => new Vector2(c.o.x + (id % c.n + 0.5f) * WrCell, c.o.z + (id / c.n + 0.5f) * WrCell);

    /// <summary>A* od štartu k cieľovému bodu (goal) alebo k najbližšej bunke zo siete ciest (goalCells). Okolie štartu a cieľa je uvoľnené.</summary>
    static List<Vector2> WrAStar(WrCtx c, Vector2 start, Vector2? goal, HashSet<int> goalCells, int relaxCells)
    {
        int n = c.n, total = n * n;
        int s = WrCellOf(c, start);
        int g = goal.HasValue ? WrCellOf(c, goal.Value) : -1;
        var gScore = new float[total];
        var prev = new int[total];
        var closed = new bool[total];
        for (int i = 0; i < total; i++) { gScore[i] = float.MaxValue; prev[i] = -1; }
        var open = new SortedSet<(float f, int id)>();
        gScore[s] = 0f;
        open.Add((0f, s));
        int gx = g >= 0 ? g % n : 0, gz = g >= 0 ? g / n : 0, sx = s % n, sz = s / n;
        int found = -1;

        while (open.Count > 0)
        {
            (float f, int id) top = open.Min;
            open.Remove(top);
            int cur = top.id;
            if (closed[cur]) continue;
            closed[cur] = true;
            if ((g >= 0 && cur == g) || (goalCells != null && goalCells.Contains(cur) && cur != s)) { found = cur; break; }

            int cx = cur % n, cz = cur / n;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int nid = nz * n + nx;
                    if (closed[nid]) continue;
                    float cn = c.cost[nid];
                    bool relaxed = (Mathf.Abs(nx - sx) <= relaxCells && Mathf.Abs(nz - sz) <= relaxCells) || (g >= 0 && Mathf.Abs(nx - gx) <= relaxCells && Mathf.Abs(nz - gz) <= relaxCells);
                    if (cn >= WrBlocked) { if (!relaxed) continue; cn = 6f; }
                    else if (relaxed) cn = Mathf.Min(cn, 6f);
                    float step = (dx != 0 && dz != 0) ? 1.414f : 1f;
                    float ng = gScore[cur] + step * 0.5f * (Mathf.Min(c.cost[cur], 60f) + cn);
                    if (ng >= gScore[nid]) continue;
                    gScore[nid] = ng;
                    prev[nid] = cur;
                    float hh = 0f;
                    if (g >= 0)
                    {
                        int adx = Mathf.Abs(nx - gx), adz = Mathf.Abs(nz - gz);
                        hh = 0.5f * (Mathf.Max(adx, adz) + 0.414f * Mathf.Min(adx, adz));
                    }
                    open.Add((ng + hh, nid));
                }
        }
        if (found < 0) return null;

        var cells = new List<int>();
        for (int k = found; k >= 0; k = prev[k]) cells.Add(k);
        cells.Reverse();                       // štart → cieľ
        var pts = new List<Vector2> { start };
        for (int i = 1; i < cells.Count - 1; i++) pts.Add(WrCellPos(c, cells[i]));
        pts.Add(goal.HasValue ? goal.Value : WrCellPos(c, cells[cells.Count - 1]));
        return pts;
    }

    static void WrSimplify(List<Vector2> pts, int a, int b, float tol, List<bool> keep)
    {
        if (b <= a + 1) return;
        Vector2 p = pts[a], q = pts[b];
        float best = 0f; int idx = -1;
        for (int i = a + 1; i < b; i++)
        {
            Vector2 d = q - p;
            float len = d.magnitude;
            float dist = len < 0.01f ? Vector2.Distance(pts[i], p) : Mathf.Abs(d.x * (pts[i].y - p.y) - d.y * (pts[i].x - p.x)) / len;
            if (dist > best) { best = dist; idx = i; }
        }
        if (best > tol) { keep[idx] = true; WrSimplify(pts, a, idx, tol, keep); WrSimplify(pts, idx, b, tol, keep); }
    }

    static List<Vector2> WrSimplified(List<Vector2> pts, float tol)
    {
        var keep = new List<bool>();
        for (int i = 0; i < pts.Count; i++) keep.Add(i == 0 || i == pts.Count - 1);
        WrSimplify(pts, 0, pts.Count - 1, tol, keep);
        var res = new List<Vector2>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) res.Add(pts[i]);
        return res;
    }

    // ------------------------------------------------------------------ Kontrola prekážok
    static string WrBlockReason(WrCtx c, Vector2 p, float r, bool ignoreSteep)
    {
        if (p.x < c.o.x + 6f || p.y < c.o.z + 6f || p.x > c.o.x + c.size - 6f || p.y > c.o.z + c.size - 6f) return "okraj";
        float u = (p.x - c.o.x) / c.size, v = (p.y - c.o.z) / c.size;
        if (!ignoreSteep && c.data.GetSteepness(u, v) > 24f) return "svah";
        if (WrNearLake(c, p.x, p.y, r + 3f)) return "jazero";
        float h = WrHeight(c, p.x, p.y);
        foreach (Collider col in Physics.OverlapSphere(new Vector3(p.x, h + 1.2f, p.y), r, ~0, QueryTriggerInteraction.Ignore))
        {
            if (col is TerrainCollider || col.GetComponentInParent<DemonEnemy>() != null || col.transform.root.name == "Player") continue;
            if (col.transform.IsChildOf(c.group)) continue;
            return col.name;
        }
        return null;
    }

    static int WrConflicts(WrCtx c, List<Vector2> pts, float width, int from, int to)
    {
        int bad = 0;
        for (int i = Mathf.Max(0, from); i <= Mathf.Min(pts.Count - 2, to); i++)
        {
            Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
            int steps = Mathf.Max(2, (int)((p2 - p1).magnitude / 1.5f));
            for (int k = 0; k < steps; k++)
            {
                float t = (float)k / steps;
                Vector2 q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                if (WrBlockReason(c, q, width * 0.5f + 0.4f, false) != null) bad++;
            }
        }
        return bad;
    }

    static void WrNudge(WrCtx c, List<Vector2> pts, float width)
    {
        for (int pass = 0; pass < 3; pass++)
            for (int i = 1; i < pts.Count - 1; i++)
            {
                int cur = WrConflicts(c, pts, width, i - 1, i);
                if (cur == 0) continue;
                Vector2 orig = pts[i], best = orig; int bc = cur; float bd = 0f;
                for (float rad = 2f; rad <= 10f && bc > 0; rad += 2f)
                    for (int a = 0; a < 12 && bc > 0; a++)
                    {
                        float ang = a * 30f * Mathf.Deg2Rad;
                        pts[i] = orig + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                        int c2 = WrConflicts(c, pts, width, i - 1, i);
                        if (c2 < bc || (c2 == bc && c2 < cur && rad < bd)) { bc = c2; best = pts[i]; bd = rad; }
                    }
                pts[i] = best;
            }
    }

    // ------------------------------------------------------------------ Zoznam ciest
    static Vector2 WrRotate(Vector2 v, float deg)
    {
        float r = deg * Mathf.Deg2Rad, cs = Mathf.Cos(r), sn = Mathf.Sin(r);
        return new Vector2(v.x * cs - v.y * sn, v.x * sn + v.y * cs);
    }

    static float WrEdge(char kind) => kind == 'W' ? 1.6f : kind == 'T' ? 1.0f : kind == 'H' ? 0.8f : 0.3f;

    static WrRoad WrNewRoad(string id, float width, List<Vector2> pts)
    {
        char k = id.StartsWith("HB") || id.StartsWith("H_") ? 'H' : id[0] == 'W' ? 'W' : id[0] == 'T' ? 'T' : 'A';
        var r = new WrRoad { id = id, kind = k, width = width, edgeW = WrEdge(k) };
        r.pts.AddRange(pts);
        return r;
    }

    static void WrRegister(WrCtx c, WrRoad r)
    {
        c.roads.Add(r);
        if (r.kind == 'A' || r.disc) return;
        for (int i = 0; i < r.pts.Count - 1; i++)
        {
            float len = Vector2.Distance(r.pts[i], r.pts[i + 1]);
            int steps = Mathf.Max(1, (int)(len / (WrCell * 0.5f)));
            for (int k = 0; k <= steps; k++) c.network.Add(WrCellOf(c, Vector2.Lerp(r.pts[i], r.pts[i + 1], (float)k / steps)));
        }
    }

    static void WrAddDesigned(WrCtx c)
    {
        foreach (string line in WrDesigned)
        {
            string[] f = line.Split('|');
            var pts = new List<Vector2>();
            foreach (string s in f[2].Split(' '))
            {
                string[] xy = s.Split(',');
                pts.Add(new Vector2(float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture)));
            }
            WrRegister(c, WrNewRoad(f[0], float.Parse(f[1], CultureInfo.InvariantCulture), pts));
        }
    }

    static void WrAddAStar(WrCtx c, string id, float width, Vector2 from, Vector2 to, int relax)
    {
        List<Vector2> p = WrAStar(c, from, to, null, relax);
        if (p == null || p.Count < 2) { Debug.LogWarning($"Cesty divočín ({c.key}): trasa {id} sa nenašla."); return; }
        p = WrSimplified(p, 2.6f);
        WrNudge(c, p, width);
        WrRegister(c, WrNewRoad(id, width, p));
    }

    /// <summary>Chodník/stopa od cieľa k najbližšej ceste; posledných stopMeters pred cieľom sa odreže.</summary>
    static bool WrAddTrail(WrCtx c, string id, float width, Vector2 target, float stopMeters, bool animal)
    {
        if (c.network.Count == 0) return false;
        List<Vector2> p = WrAStar(c, target, null, c.network, 3);
        if (p == null || p.Count < 2) { Debug.LogWarning($"Cesty divočín ({c.key}): chodník {id} sa nenašiel."); return false; }
        p.Reverse();   // od cesty k cieľu
        // odrež posledných stopMeters
        float cut = stopMeters;
        while (p.Count > 2 && Vector2.Distance(p[p.Count - 1], target) < cut) p.RemoveAt(p.Count - 1);
        if (p.Count >= 2 && Vector2.Distance(p[p.Count - 1], target) < cut)
            p[p.Count - 1] = target + (p[p.Count - 2] - target).normalized * cut;
        float len = 0f;
        for (int i = 0; i < p.Count - 1; i++) len += Vector2.Distance(p[i], p[i + 1]);
        if (len < 7f) return false;
        p = WrSimplified(p, 2.6f);
        WrNudge(c, p, width);
        WrRegister(c, WrNewRoad(id, width, p));
        return true;
    }

    static void WrGenerate(WrCtx c)
    {
        WildAnchors(c.key, out Vector3 a, out Vector3 b, out Vector3 cc);
        Vector2 fork = c.center + new Vector2(5f, -61f);
        Vector2 A = c.center + new Vector2(a.x, a.z), B = c.center + new Vector2(b.x, b.z), C = c.center + new Vector2(cc.x, cc.z);
        WrAddAStar(c, "W1", 5f, c.portal, fork, 2);
        WrAddAStar(c, "W1a", 4f, fork, A, 3);
        WrAddAStar(c, "W1b", 4f, fork, B, 3);
        WrAddAStar(c, "W1c", 4f, fork, C, 3);
        if (c.key == "Bane")
        {
            MineEntrance mine = c.root.GetComponentInChildren<MineEntrance>(true);
            if (mine != null) WrAddTrail(c, "W2_baňa", 4f, new Vector2(mine.transform.position.x, mine.transform.position.z), 5f, false);
        }
    }

    /// <summary>Doplnkové chodníky (tábory, zdroje, svorky, jazerá, skrytá odbočka) pre zóny bez návrhu a doplnky Divočiny.</summary>
    static void WrExtraTrails(WrCtx c)
    {
        // Tábory
        int camp = 0;
        foreach (Transform t in c.root.GetComponentsInChildren<Transform>(true))
            if (t.name == "Tabor") WrAddTrail(c, "T_tabor" + (++camp), 3f, new Vector2(t.position.x, t.position.z), 7f, false);

        if (c.key != "Divocina")
        {
            // Zhluky zdrojov
            var groups = new Dictionary<string, List<ResourceNode>>();
            foreach (ResourceNode rn in c.root.GetComponentsInChildren<ResourceNode>(true))
            {
                if (!groups.ContainsKey(rn.ItemId)) groups[rn.ItemId] = new List<ResourceNode>();
                groups[rn.ItemId].Add(rn);
            }
            foreach (KeyValuePair<string, List<ResourceNode>> kv in groups)
            {
                if (kv.Value.Count < 4) continue;
                Vector3 centroid = Vector3.zero;
                foreach (ResourceNode rn in kv.Value) centroid += rn.transform.position;
                centroid /= kv.Value.Count;
                ResourceNode near = kv.Value[0];
                foreach (ResourceNode rn in kv.Value)
                    if ((rn.transform.position - centroid).sqrMagnitude < (near.transform.position - centroid).sqrMagnitude) near = rn;
                WrAddTrail(c, "T_" + kv.Key, 2f, new Vector2(near.transform.position.x, near.transform.position.z), 3.5f, false);
            }
            // Svorky
            var packs = new List<Vector2>();
            foreach (DemonEnemy d in c.root.GetComponentsInChildren<DemonEnemy>(true))
            {
                Vector2 p = new Vector2(d.transform.position.x, d.transform.position.z);
                bool merged = false;
                foreach (Vector2 q in packs) if (Vector2.Distance(p, q) < 30f) { merged = true; break; }
                if (!merged) packs.Add(p);
            }
            for (int i = 0; i < packs.Count; i++) WrAddTrail(c, "A_svorka" + (i + 1), 0.9f, packs[i], 9f, true);
            // Jazerá
            for (int i = 0; i < c.lakes.Length; i++)
            {
                Vector2 lake = new Vector2(c.lakes[i].transform.position.x, c.lakes[i].transform.position.z);
                Vector2 nearest = lake; float nd = float.MaxValue;
                foreach (int cell in c.network)
                {
                    Vector2 q = WrCellPos(c, cell);
                    float d = (q - lake).sqrMagnitude;
                    if (d < nd) { nd = d; nearest = q; }
                }
                Vector2 shore = lake + (nearest - lake).normalized * (c.lakes[i].waterRadius + 15f);   // zviera sa napíja pri brehu
                WrAddTrail(c, "A_jazero" + (i + 1), 0.9f, shore, 1f, true);
            }
            WrAddHidden(c);
        }
    }

    static void WrAddHidden(WrCtx c)
    {
        WrRoad w1 = null;
        foreach (WrRoad r in c.roads) if (r.id == "W1") w1 = r;
        if (w1 == null || w1.pts.Count < 2) return;
        // bod na ceste ~40 % dĺžky
        Vector2 j = Vector2.Lerp(w1.pts[0], w1.pts[w1.pts.Count - 1], 0.4f);
        Vector2 dir = (w1.pts[w1.pts.Count - 1] - w1.pts[0]).normalized, nrm = new Vector2(-dir.y, dir.x);
        float bestScore = float.MaxValue; Vector2 bestEnd = j + nrm * 90f;
        foreach (float side in new[] { -1f, 1f })
            foreach (float dist in new[] { 85f, 100f, 115f })
                foreach (float jitter in new[] { -25f, 0f, 25f })
                {
                    Vector2 d = WrRotate(nrm * side, jitter);
                    Vector2 e = j + d * dist;
                    if (WrBlockReason(c, e, 3f, false) != null) continue;
                    float score = 0f;
                    for (int s = 1; s <= 10; s++)
                    {
                        Vector2 q = Vector2.Lerp(j, e, s / 10f);
                        score += Mathf.Min(c.cost[WrCellOf(c, q)], 80f);
                    }
                    if (score < bestScore) { bestScore = score; bestEnd = e; }
                }
        var p = WrAStar(c, j, bestEnd, null, 2);
        if (p == null || p.Count < 2) return;
        p = WrSimplified(p, 2.6f);
        WrNudge(c, p, 1.8f);
        WrRegister(c, WrNewRoad("HB1", 1.8f, p));
    }

    // ------------------------------------------------------------------ Hustá čiara a rastre
    static void WrDensify(WrCtx c, WrRoad r)
    {
        r.dense.Clear(); r.half.Clear(); r.hs.Clear();
        if (r.disc)
        {
            Vector2 p0 = r.pts[0];
            r.dense.Add(new Vector3(p0.x, WrHeight(c, p0.x, p0.y), p0.y)); r.half.Add(r.width * 0.5f); r.hs.Add(r.dense[0].y);
            return;
        }
        float seed = Mathf.Abs(r.id.GetHashCode() % 991);
        float amp = r.kind == 'W' ? 0.5f : r.kind == 'T' ? 0.3f : r.kind == 'H' ? 0.25f : 0.2f;
        var pts = r.pts;
        float dist = 0f;
        Vector2 last = pts[0];
        var raw = new List<Vector2>();
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Count - 1, i + 2)];
            int steps = Mathf.Max(2, Mathf.CeilToInt((p2 - p1).magnitude / 0.5f));
            for (int k = 0; k < steps; k++)
            {
                float t = (float)k / steps;
                raw.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
            }
        }
        raw.Add(pts[pts.Count - 1]);
        float total = 0f;
        for (int i = 1; i < raw.Count; i++) total += Vector2.Distance(raw[i - 1], raw[i]);
        for (int i = 0; i < raw.Count; i++)
        {
            Vector2 a = raw[Mathf.Max(0, i - 1)], b = raw[Mathf.Min(raw.Count - 1, i + 1)];
            Vector2 tan = (b - a).normalized, nrm = new Vector2(-tan.y, tan.x);
            if (i > 0) dist += Vector2.Distance(raw[i - 1], raw[i]);
            float fade = Mathf.Clamp01(Mathf.Min(dist, total - dist) / 4f);
            float lateral = (Mathf.PerlinNoise(seed + dist * 0.09f, 0.7f) - 0.5f) * 2f * amp * fade;
            Vector2 p = raw[i] + nrm * lateral;
            if (Mathf.Abs(lateral) > 0.05f && i % 2 == 0 && WrBlockReason(c, p, r.width * 0.5f + 0.3f, true) != null) p = raw[i];   // šum nesmie viesť do prekážky
            float w = r.width * (1f + (r.kind == 'A' ? 0.2f : 0.14f) * (Mathf.PerlinNoise(seed + dist * 0.05f, 2.3f) - 0.5f) * 2f);
            r.dense.Add(new Vector3(p.x, WrHeight(c, p.x, p.y), p.y));
            r.half.Add(w * 0.5f);
        }
        for (int i = 0; i < r.dense.Count; i++)   // vyhladená výška stredu (okno ~4,5 m)
        {
            float sum = 0f; int cnt = 0;
            for (int k = -4; k <= 4; k++) { sum += r.dense[Mathf.Clamp(i + k, 0, r.dense.Count - 1)].y; cnt++; }
            r.hs.Add(sum / cnt);
        }
    }

    static WrRaster WrRasterize(WrCtx c, int res, string kinds)
    {
        var r = new WrRaster { res = res, edge = new float[res, res], ew = new float[res, res], h = new float[res, res], kf = new float[res, res] };
        for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) r.edge[z, x] = 999f;
        float px = c.size / (res - 1);
        foreach (WrRoad road in c.roads)
        {
            if (kinds.IndexOf(road.kind) < 0) continue;
            float kfac = road.kind == 'W' ? 0.55f : road.kind == 'T' ? 0.35f : 0.3f;
            for (int i = 0; i < road.dense.Count; i++)
            {
                Vector3 p = road.dense[i];
                float half = road.half[i], rad = half + road.edgeW;
                int ix0 = Mathf.Max(0, Mathf.FloorToInt((p.x - rad - c.o.x) / px)), ix1 = Mathf.Min(res - 1, Mathf.CeilToInt((p.x + rad - c.o.x) / px));
                int iz0 = Mathf.Max(0, Mathf.FloorToInt((p.z - rad - c.o.z) / px)), iz1 = Mathf.Min(res - 1, Mathf.CeilToInt((p.z + rad - c.o.z) / px));
                for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        float dx = c.o.x + ix * px - p.x, dz = c.o.z + iz * px - p.z;
                        float e = Mathf.Sqrt(dx * dx + dz * dz) - half;
                        if (e < r.edge[iz, ix]) { r.edge[iz, ix] = e; r.ew[iz, ix] = road.edgeW; r.h[iz, ix] = road.hs[i]; r.kf[iz, ix] = kfac; }
                    }
            }
        }
        return r;
    }

    static float WrMask(float edge, float ew) => edge >= ew ? 0f : edge <= 0f ? 1f : 1f - Smooth01(edge / ew);

    // ------------------------------------------------------------------ Vyrovnanie terénu (minimálne)
    static void WrFlatten(WrCtx c)
    {
        int hr = c.data.heightmapResolution;
        float[,] hts = c.data.GetHeights(0, 0, hr, hr);
        WrRaster r = WrRasterize(c, hr, "WT");
        float ys = c.data.size.y, maxDelta = 0f;
        for (int z = 0; z < hr; z++)
            for (int x = 0; x < hr; x++)
            {
                float e = r.edge[z, x];
                if (e >= 0.8f) continue;
                float s = WrMask(e, 0.8f);
                float wy = c.o.y + hts[z, x] * ys;
                float delta = Mathf.Clamp((r.h[z, x] - wy) * s * r.kf[z, x], -0.2f, 0.2f);
                hts[z, x] += delta / ys;
                maxDelta = Mathf.Max(maxDelta, Mathf.Abs(delta));
            }
        c.data.SetHeights(0, 0, hts);
        EditorUtility.SetDirty(c.data);
        Debug.Log($"Cesty divočín ({c.key}): terén vyrovnaný len pod cestami, najväčšia zmena výšky {maxDelta * 100f:F0} cm.");
    }

    // ------------------------------------------------------------------ Maľovanie terénu
    static void WrPaint(WrCtx c)
    {
        int res = c.data.alphamapWidth;
        float[,,] maps = c.data.GetAlphamaps(0, 0, res, res);
        float px = c.size / (res - 1);
        WrRaster main = WrRasterize(c, res, "WTH");
        WrRaster tracks = WrRasterize(c, res, "A");
        float baseY = WrHeight(c, c.portal.x, c.portal.y);

        // Stopy kolies na hlavných cestách
        var ruts = new float[res, res];
        foreach (WrRoad road in c.roads)
        {
            if (road.kind != 'W' || road.disc) continue;
            for (int i = 3; i < road.dense.Count - 3; i++)
            {
                Vector3 a = road.dense[i - 1], b = road.dense[i + 1];
                Vector2 tan = new Vector2(b.x - a.x, b.z - a.z).normalized, nrm = new Vector2(-tan.y, tan.x);
                foreach (float side in new[] { -0.9f, 0.9f })
                {
                    Vector2 p = new Vector2(road.dense[i].x, road.dense[i].z) + nrm * side;
                    int ix0 = Mathf.Max(0, Mathf.FloorToInt((p.x - 0.4f - c.o.x) / px)), ix1 = Mathf.Min(res - 1, Mathf.CeilToInt((p.x + 0.4f - c.o.x) / px));
                    int iz0 = Mathf.Max(0, Mathf.FloorToInt((p.y - 0.4f - c.o.z) / px)), iz1 = Mathf.Min(res - 1, Mathf.CeilToInt((p.y + 0.4f - c.o.z) / px));
                    for (int iz = iz0; iz <= iz1; iz++)
                        for (int ix = ix0; ix <= ix1; ix++)
                        {
                            float d = Vector2.Distance(new Vector2(c.o.x + ix * px, c.o.z + iz * px), p);
                            if (d < 0.38f) ruts[iz, ix] = Mathf.Max(ruts[iz, ix], 1f - Smooth01(d / 0.38f));
                        }
                }
            }
        }

        for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                // staré cesty sa zrušia (nahradí ich nová sieť)
                maps[z, x, 0] += maps[z, x, 1];
                maps[z, x, 1] = 0f;

                float wx = c.o.x + x * px, wz = c.o.z + z * px;
                float m = WrMask(main.edge[z, x], main.ew[z, x]);
                if (m > 0f && m < 1f)   // rozstrapkaný okraj, tráva zasahuje do cesty
                    m = Mathf.Clamp01(m + (Mathf.PerlinNoise(wx * 0.8f, wz * 0.8f) - 0.5f) * 0.7f * Mathf.Sin(m * Mathf.PI));
                float tr = WrMask(tracks.edge[z, x], tracks.ew[z, x]);
                if (tr > 0f) tr *= 0.55f * (Mathf.PerlinNoise(wx * 1.3f, wz * 1.3f) > 0.42f ? 1f : 0.25f);
                float rut = ruts[z, x];
                if (m <= 0.002f && tr <= 0.002f && rut <= 0.002f) continue;

                float u = x / (float)(res - 1), v = z / (float)(res - 1);
                float steep = c.data.GetSteepness(u, v);
                float rockF = Smooth01((steep - 14f) / 10f) * 0.65f;
                float hRel = c.o.y + c.ter.SampleHeight(new Vector3(wx, 0f, wz)) - baseY;
                float mudF = Smooth01((-0.1f - hRel) / 0.5f) * 0.7f;
                float roadW = Mathf.Max(0f, 1f - rockF - mudF);
                float[] target = { 0f, roadW, rockF, mudF };

                float k = Mathf.Max(m, tr);
                float sum = 0f;
                float[] w4 = new float[4];
                for (int l = 0; l < 4; l++) { w4[l] = Mathf.Lerp(maps[z, x, l], target[l], k); }
                if (rut > 0f)
                {
                    float[] rt = { 0f, 0.45f, 0f, 0.55f };
                    for (int l = 0; l < 4; l++) w4[l] = Mathf.Lerp(w4[l], rt[l], rut * 0.7f);
                }
                for (int l = 0; l < 4; l++) sum += w4[l];
                if (sum < 0.0001f) { w4[0] = 1f; sum = 1f; }
                for (int l = 0; l < 4; l++) maps[z, x, l] = w4[l] / sum;
            }
        c.data.SetAlphamaps(0, 0, maps);

        // Tráva a kroviny sa z cesty vytratia (okraj rieďa)
        int dw = c.data.detailWidth, dh = c.data.detailHeight;
        for (int layer = 0; layer < c.data.detailPrototypes.Length; layer++)
        {
            int[,] d = c.data.GetDetailLayer(0, 0, dw, dh, layer);
            for (int z = 0; z < dh; z++)
                for (int x = 0; x < dw; x++)
                {
                    if (d[z, x] == 0) continue;
                    float u = (x + 0.5f) / dw, v = (z + 0.5f) / dh;
                    int ix = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 0, res - 1), iz = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 0, res - 1);
                    float m = Mathf.Max(WrMask(main.edge[iz, ix], main.ew[iz, ix]), WrMask(tracks.edge[iz, ix], tracks.ew[iz, ix]) * 0.5f);
                    if (m > 0.3f) d[z, x] = 0;
                    else if (m > 0.04f && (((x * 73856093) ^ (z * 19349663)) & 1023) / 1023f < m * 2.4f) d[z, x] = 0;
                }
            c.data.SetDetailLayer(0, 0, layer, d);
        }
        EditorUtility.SetDirty(c.data);
    }

    // ------------------------------------------------------------------ Dekor pozdĺž ciest
    static bool WrFarFromRoads(WrCtx c, Vector2 p, float minGap)
    {
        foreach (WrRoad r in c.roads)
        {
            if (r.kind == 'A') continue;
            for (int i = 0; i < r.dense.Count; i += 2)
            {
                float dx = r.dense[i].x - p.x, dz = r.dense[i].z - p.y;
                float lim = r.half[i] + minGap;
                if (dx * dx + dz * dz < lim * lim) return false;
            }
        }
        return true;
    }

    static void WrScatterProps(WrCtx c, System.Random rnd)
    {
        var holder = new GameObject("Dekor").transform;
        holder.SetParent(c.group, false);
        List<GameObject> logs = ForestPrefabs("Stumps Roots and Branches", "dead_log", "dry_bough");
        List<GameObject> stumps = ForestPrefabs("Stumps Roots and Branches", "forest_stump");
        List<GameObject> stones = ForestPrefabs("Rocks", "forest_stones");
        List<GameObject> branches = ForestPrefabs("Stumps Roots and Branches", "forest_branch", "old_roots");
        int placed = 0;
        foreach (WrRoad r in c.roads)
        {
            if (r.kind == 'A' || r.disc) continue;
            float spacing = r.kind == 'W' ? 17f : r.kind == 'T' ? 30f : 40f;
            float acc = spacing * (0.3f + (float)rnd.NextDouble());
            for (int i = 1; i < r.dense.Count; i++)
            {
                acc -= Vector3.Distance(r.dense[i - 1], r.dense[i]);
                if (acc > 0f) continue;
                acc = spacing * (0.4f + (float)rnd.NextDouble() * 1.3f);   // nepravidelné rozostupy
                Vector3 a = r.dense[Mathf.Max(0, i - 1)], b = r.dense[Mathf.Min(r.dense.Count - 1, i + 1)];
                Vector2 tan = new Vector2(b.x - a.x, b.z - a.z).normalized, nrm = new Vector2(-tan.y, tan.x);
                float side = rnd.NextDouble() < 0.5 ? -1f : 1f;
                Vector2 p = new Vector2(r.dense[i].x, r.dense[i].z) + nrm * side * (r.half[i] + 1.6f + (float)rnd.NextDouble() * 2.6f);
                if (!WrFarFromRoads(c, p, 1.2f)) continue;
                if (WrBlockReason(c, p, 1.1f, false) != null) continue;

                double roll = rnd.NextDouble();
                List<GameObject> pool = roll < 0.30 ? logs : roll < 0.50 ? stumps : roll < 0.85 ? stones : branches;
                string kind = pool == logs ? "Kmeň" : pool == stumps ? "Pník" : pool == stones ? "Skala" : "Konáre";
                if (pool.Count == 0) continue;
                GameObject prefab = pool[rnd.Next(pool.Count)];
                var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
                item.transform.position = new Vector3(p.x, WrHeight(c, p.x, p.y), p.y);
                item.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                item.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.3f, (float)rnd.NextDouble());
                bool solid = false;
                foreach (Collider col in item.GetComponentsInChildren<Collider>(true)) if (!col.isTrigger) { solid = true; break; }
                if (!solid && kind != "Konáre") AddColliderFromBounds(item);
                if (kind == "Skala") MakeBreakableFromBounds(item, ToolKit.Kind.Pickaxe, ItemCatalog.Stone, "Skala", 50f, 45f);
                else if (kind == "Kmeň" || kind == "Pník") MakeBreakableFromBounds(item, ToolKit.Kind.Axe, ItemCatalog.Wood, kind, 40f, 30f);
                foreach (Transform t in item.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
                placed++;
            }
        }
        Debug.Log($"Cesty divočín ({c.key}): dekor pozdĺž ciest {placed} kusov.");
    }

    // ------------------------------------------------------------------ Zvieracie stopy (jedna sieť na zónu)
    static void WrTracksMesh(WrCtx c)
    {
        var verts = new List<Vector3>(); var tris = new List<int>();
        var rnd = new System.Random(c.key.GetHashCode() ^ 0x51a1);
        foreach (WrRoad r in c.roads)
        {
            if (r.kind != 'A' || r.dense.Count < 6) continue;
            bool deer = r.id.StartsWith("A_jazero") || r.id == "A5" || r.id == "A6";
            float step = deer ? 0.8f : 0.65f, acc = 0f; int k = 0;
            for (int i = 1; i < r.dense.Count - 1; i++)
            {
                acc += Vector3.Distance(r.dense[i - 1], r.dense[i]);
                if (acc < step * (0.85f + (float)rnd.NextDouble() * 0.4f)) continue;
                acc = 0f; k++;
                Vector3 a = r.dense[i - 1], b = r.dense[i + 1];
                Vector2 dir = new Vector2(b.x - a.x, b.z - a.z).normalized, nrm = new Vector2(-dir.y, dir.x);
                Vector2 p = new Vector2(r.dense[i].x, r.dense[i].z) + nrm * (k % 2 == 0 ? 0.16f : -0.16f) + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.1f;
                if (deer) { WrPrintHoof(c, verts, tris, p, dir); }
                else WrPrintPaw(c, verts, tris, p, dir);
            }
        }
        if (verts.Count == 0) return;
        var mesh = new Mesh { name = "Stopy_" + c.key, indexFormat = verts.Count > 60000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string path = $"{RdMeshDir}/Stopy_{c.key}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject("Stopy_zvierat");
        go.transform.SetParent(c.group, true);
        go.transform.position = Vector3.zero;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var printRenderer = go.AddComponent<MeshRenderer>();
        printRenderer.sharedMaterial = RdMat("Stopy_zvierat", null, null, new Color(0.10f, 0.075f, 0.05f), 0.05f);
        printRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.isStatic = true;
    }

    static void WrAddDisc(WrCtx c, List<Vector3> v, List<int> t, Vector2 p, float rx, float rz, Vector2 dir, int seg)
    {
        Vector2 side = new Vector2(-dir.y, dir.x);
        int o = v.Count;
        v.Add(new Vector3(p.x, WrHeight(c, p.x, p.y) + 0.035f, p.y));
        for (int i = 0; i < seg; i++)
        {
            float a = i * Mathf.PI * 2f / seg;
            Vector2 q = p + dir * (Mathf.Cos(a) * rz) + side * (Mathf.Sin(a) * rx);
            v.Add(new Vector3(q.x, WrHeight(c, q.x, q.y) + 0.035f, q.y));
        }
        for (int i = 0; i < seg; i++)
        {
            t.Add(o); t.Add(o + 1 + (i + 1) % seg); t.Add(o + 1 + i);
            t.Add(o); t.Add(o + 1 + i); t.Add(o + 1 + (i + 1) % seg);   // oboje strany (nevadí smer vrcholov)
        }
    }

    static void WrPrintPaw(WrCtx c, List<Vector3> v, List<int> t, Vector2 p, Vector2 dir)
    {
        Vector2 side = new Vector2(-dir.y, dir.x);
        WrAddDisc(c, v, t, p, 0.055f, 0.07f, dir, 7);
        for (int i = -1; i <= 2; i++) WrAddDisc(c, v, t, p + dir * 0.11f + side * ((i - 0.5f) * 0.05f), 0.021f, 0.026f, dir, 5);
    }

    static void WrPrintHoof(WrCtx c, List<Vector3> v, List<int> t, Vector2 p, Vector2 dir)
    {
        Vector2 side = new Vector2(-dir.y, dir.x);
        foreach (float s in new[] { -0.03f, 0.03f }) WrAddDisc(c, v, t, p + side * s, 0.017f, 0.05f, dir, 6);
    }

    // ------------------------------------------------------------------ Tábor drevorubačov (Divočina)
    static Vector2? WrPlanCamp(WrCtx c)
    {
        Vector2 want = new Vector2(6f, 396f);
        Vector2 best = want; bool ok = false;
        for (float rad = 0f; rad <= 24f && !ok; rad += 3f)
            for (int a = 0; a < (rad == 0f ? 1 : 12) && !ok; a++)
            {
                float ang = a * 30f * Mathf.Deg2Rad;
                Vector2 p = want + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
                if (WrBlockReason(c, p, 6.5f, false) == null) { best = p; ok = true; }
            }
        if (!ok) { Debug.LogWarning("Cesty divočín: pre tábor drevorubačov sa nenašla čistinka."); return null; }
        var disc = new WrRoad { id = "Plocha_tabora", kind = 'W', width = 13f, edgeW = 2.5f, disc = true };
        disc.pts.Add(best);
        c.roads.Add(disc);
        WrDensify(c, disc);
        // spoj s cestou W1a
        WrRoad w1a = c.roads.Find(r => r.id == "W1a");
        if (w1a != null)
        {
            Vector2 end = w1a.pts[w1a.pts.Count - 1];
            if (Vector2.Distance(end, best) > 5f)
            {
                var pts = new List<Vector2> { end, Vector2.Lerp(end, best, 0.5f), best };
                var link = WrNewRoad("W1d", 4f, pts);
                c.roads.Add(link);
                WrDensify(c, link);
            }
        }
        return best;
    }

    /// <summary>Nájde miesto okolo stredu tábora mimo ciest a prekážok; preferuje daný smer.</summary>
    static Vector3 WrCampSpot(WrCtx c, Vector3 center, float minR, float maxR, float clear, List<Vector3> taken, float minGap, int seed)
    {
        for (float r = minR; r <= maxR; r += 0.8f)
            for (int a = 0; a < 24; a++)
            {
                float ang = (seed * 37 + a * 15) * Mathf.Deg2Rad;
                Vector2 p = new Vector2(center.x + Mathf.Cos(ang) * r, center.z + Mathf.Sin(ang) * r);
                if (!WrFarFromRoads(c, p, clear + 0.6f) || WrBlockReason(c, p, clear, true) != null) continue;
                bool near = false;
                foreach (Vector3 t in taken) if (Vector2.Distance(new Vector2(t.x, t.z), p) < minGap) { near = true; break; }
                if (near) continue;
                var v = new Vector3(p.x, WrHeight(c, p.x, p.y), p.y);
                taken.Add(v);
                return v;
            }
        return center + new Vector3(minR, 0f, 0f);
    }

    static void WrBuildCamp(WrCtx c, Vector2 pos)
    {
        var camp = new GameObject("Tabor_drevorubacov").transform;
        camp.SetParent(c.group, false);
        camp.position = Vector3.zero;
        float y = WrHeight(c, pos.x, pos.y);
        Vector3 C = new Vector3(pos.x, y, pos.y);
        Material wood = RdMat("Cesta_Drevo", null, null, new Color(0.22f, 0.15f, 0.10f), 0.1f);
        Material cloth1 = RdMat("Tabor_stan1", null, null, new Color(0.42f, 0.38f, 0.26f), 0.05f);
        Material cloth2 = RdMat("Tabor_stan2", null, null, new Color(0.30f, 0.34f, 0.30f), 0.05f);
        Material flame = RdMat("Tabor_plamen", null, null, new Color(1f, 0.55f, 0.15f), 0.2f, new Color(3f, 1.4f, 0.3f));
        Material ash = RdMat("Tabor_popol", null, null, new Color(0.07f, 0.06f, 0.05f), 0.05f);

        // ohnisko: kruh kameňov, polená, plameň a kmitajúce svetlo
        string flint = "Assets/Materials/Stavby_do_hry/Military_Trenches_Scatter_Flint_Rock_S_01_yfbmejis_High.fbx";
        for (int i = 0; i < 9; i++)
        {
            float a = i * 40f * Mathf.Deg2Rad;
            Vector3 p = C + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.95f;
            p.y = WrHeight(c, p.x, p.z);
            RdProp(flint, p, i * 47f, 0.45f, camp);
        }
        RdBox(camp, "Popol", C + Vector3.up * 0.03f, new Vector3(1.5f, 0.02f, 1.5f), Quaternion.identity, ash, false, PrimitiveType.Cylinder);
        for (int i = 0; i < 3; i++)
            RdBox(camp, "Poleno", C + Quaternion.Euler(0f, i * 120f + 20f, 0f) * new Vector3(0.35f, 0.15f, 0f), new Vector3(0.14f, 0.55f, 0.14f), Quaternion.Euler(0f, i * 120f + 20f, 70f), wood, false, PrimitiveType.Cylinder);
        GameObject fl = RdBox(camp, "Plamen", C + Vector3.up * 0.4f, new Vector3(0.42f, 0.55f, 0.42f), Quaternion.identity, flame, false, PrimitiveType.Sphere);
        var lightGo = new GameObject("SvetloOhna");
        lightGo.transform.SetParent(fl.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point; light.color = new Color(1f, 0.62f, 0.28f); light.range = 8f; light.intensity = 1.6f; light.shadows = LightShadows.None;
        fl.AddComponent<CampFire>().Configure(light);
        var solid = new GameObject("OhenKolizia");
        solid.transform.SetParent(camp, false);
        solid.transform.position = C + Vector3.up * 0.3f;
        var cc = solid.AddComponent<SphereCollider>(); cc.radius = 0.6f;

        // stany (A-rám z dvoch plátien)
        var taken = new List<Vector3> { C };
        Vector3[] tents = { WrCampSpot(c, C, 4.6f, 9f, 2.3f, taken, 5.5f, 0), WrCampSpot(c, C, 4.6f, 9f, 2.3f, taken, 5.5f, 5) };
        float[] yaws = { 200f, 150f };
        for (int i = 0; i < tents.Length; i++)
        {
            Vector3 t = tents[i]; t.y = WrHeight(c, t.x, t.z);
            Quaternion q = Quaternion.Euler(0f, yaws[i], 0f);
            Material m = i == 0 ? cloth1 : cloth2;
            RdBox(camp, "Stan_lave", t + q * new Vector3(-0.85f, 0.85f, 0f), new Vector3(0.06f, 2.3f, 3.1f), q * Quaternion.Euler(0f, 0f, 38f), m, true);
            RdBox(camp, "Stan_prave", t + q * new Vector3(0.85f, 0.85f, 0f), new Vector3(0.06f, 2.3f, 3.1f), q * Quaternion.Euler(0f, 0f, -38f), m, true);
            RdBox(camp, "Stan_chrbat", t + q * new Vector3(0f, 0.7f, -1.55f), new Vector3(1.9f, 1.3f, 0.06f), q, m, false);
        }

        // hromada polien, sekací peň, debny
        Vector3 pile = WrCampSpot(c, C, 5f, 10f, 2.0f, taken, 4f, 9);
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 4 - row; i++)
            {
                Vector3 p = pile + new Vector3(-1.0f + i * 0.34f + row * 0.17f, 0f, 0f);
                p.y = WrHeight(c, p.x, p.z) + 0.16f + row * 0.28f;
                RdBox(camp, "Polienko", p, new Vector3(0.28f, 1.1f, 0.28f), Quaternion.Euler(0f, 90f, 90f), wood, row == 0, PrimitiveType.Cylinder);
            }
        Vector3 blockPos = WrCampSpot(c, C, 2.6f, 5f, 0.8f, taken, 2.2f, 3);
        blockPos.y = WrHeight(c, blockPos.x, blockPos.z);
        RdBox(camp, "Sekaci_pen", blockPos + Vector3.up * 0.25f, new Vector3(0.55f, 0.25f, 0.55f), Quaternion.identity, wood, true, PrimitiveType.Cylinder);
        string crate = "Assets/Materials/Stavby_do_hry/Industrial_Junkyard_Crate_Metal_wdlifhrs_High.fbx";
        Vector3 cp = WrCampSpot(c, C, 3f, 6f, 0.9f, taken, 2f, 7); cp.y = WrHeight(c, cp.x, cp.z);
        RdProp(crate, cp, 25f, 1.0f, camp);
        Vector3 cp2 = WrCampSpot(c, C, 3f, 6f, 0.9f, taken, 2f, 11); cp2.y = WrHeight(c, cp2.x, cp2.z);
        RdProp(crate, cp2, 70f, 0.8f, camp);
        var pileMarker = new GameObject("Hromada_polien");
        pileMarker.transform.SetParent(camp, true);
        pileMarker.transform.position = pile + new Vector3(0f, 0f, -0.9f);
        PortalNetwork.MakeLabel(C + Vector3.up * 3.6f, camp, "Napis_Tabor_drevorubacov", "Tábor drevorubačov", 0.08f);
        Debug.Log($"Cesty divočín: tábor drevorubačov na ({C.x:F0}, {C.z:F0}).");
    }

    // ------------------------------------------------------------------ Značky, odmena skrytej odbočky
    static void WrSpecials(WrCtx c, Vector2? camp)
    {
        Material glow = RdMat("Svetluska", null, null, new Color(0.3f, 1f, 0.8f), 0.3f, new Color(0.6f, 2.4f, 1.8f));
        Material cloth = RdMat("Latka_znacka", null, null, new Color(0.9f, 0.22f, 0.12f), 0.1f, new Color(0.5f, 0.08f, 0.03f));
        var marks = new GameObject("Znacky").transform;
        marks.SetParent(c.group, false);
        marks.position = Vector3.zero;

        if (camp.HasValue) WrBuildCamp(c, camp.Value);

        // Skrytá odbočka: kôpka kameňov a pás látky pri začiatku, odmena na konci
        WrRoad hb = c.roads.Find(r => r.id == "HB1");
        if (hb != null && hb.dense.Count > 10)
        {
            Vector3 junction = hb.dense[0];
            Vector3 away = -(hb.dense[Mathf.Min(8, hb.dense.Count - 1)] - hb.dense[0]); away.y = 0f; away.Normalize();   // opačná strana cesty než odbočka
            Vector3 side = new Vector3(-away.z, 0f, away.x);
            Vector3 s = junction + away * 5.2f;   // za okrajom hlavnej cesty
            // kôpka kameňov na opačnej strane od odbočky
            List<GameObject> stones = ForestPrefabs("Rocks", "forest_stones");
            Vector3 cairn = s + side * 2.4f;
            for (int i = 0; i < 4 && stones.Count > 0; i++)
            {
                Vector3 p = cairn + new Vector3((float)(c.rnd.NextDouble() - 0.5) * 0.7f, 0f, (float)(c.rnd.NextDouble() - 0.5) * 0.7f);
                p.y = WrHeight(c, p.x, p.z) + i * 0.32f;
                var item = (GameObject)PrefabUtility.InstantiatePrefab(stones[c.rnd.Next(stones.Count)], marks);
                item.transform.position = p;
                item.transform.rotation = Quaternion.Euler(0f, i * 70f, 0f);
                item.transform.localScale = Vector3.one * (1.1f - i * 0.15f);
                if (i == 0) AddColliderFromBounds(item);
                foreach (Transform t in item.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
            }
            // pás látky na najbližšom strome
            Transform tree = null; float bestD = 12f;
            foreach (BreakableNode bn in c.root.GetComponentsInChildren<BreakableNode>(true))
            {
                if (bn.Requires != ToolKit.Kind.Axe) continue;
                float d = Vector3.Distance(bn.transform.position, s);
                if (d < bestD && d > 2.5f) { bestD = d; tree = bn.transform; }
            }
            Vector3 clothPos = tree != null ? tree.position + (junction - tree.position).normalized * 0.45f + Vector3.up * 2.1f : s + Vector3.up * 2.1f;
            if (tree == null)
            {
                Vector3 pole = s; pole.y = WrHeight(c, pole.x, pole.z);
                RdBox(marks, "Koliec", pole + Vector3.up * 1.2f, new Vector3(0.1f, 1.2f, 0.1f), Quaternion.identity, RdMat("Cesta_Drevo", null, null, new Color(0.22f, 0.15f, 0.10f), 0.1f), true, PrimitiveType.Cylinder);
                clothPos = pole + Vector3.up * 2.0f;
            }
            WrCloth(marks, clothPos, cloth);

            // odmena na konci
            Vector3 e = hb.dense[hb.dense.Count - 1];
            WrReward(c, marks, new Vector2(e.x, e.z));
        }

        // Žiariace byliny pri prvom chodníku
        WrRoad trail = c.roads.Find(r => r.kind == 'T' && r.dense.Count > 30);
        if (trail != null)
            for (int i = 0; i < 4; i++)
            {
                int idx = Mathf.Clamp((int)(trail.dense.Count * (0.35f + 0.1f * i)), 1, trail.dense.Count - 2);
                Vector3 a = trail.dense[idx - 1], b = trail.dense[idx + 1];
                Vector2 tan = new Vector2(b.x - a.x, b.z - a.z).normalized, nrm = new Vector2(-tan.y, tan.x);
                Vector2 p = new Vector2(trail.dense[idx].x, trail.dense[idx].z) + nrm * ((i % 2 == 0 ? 1f : -1f) * (trail.half[idx] + 0.9f + (float)c.rnd.NextDouble()));
                for (int k = 0; k < 4; k++)
                {
                    Vector2 q = p + new Vector2((float)c.rnd.NextDouble() - 0.5f, (float)c.rnd.NextDouble() - 0.5f) * 0.7f;
                    RdBox(marks, "Svetluska", new Vector3(q.x, WrHeight(c, q.x, q.y) + 0.25f + (float)c.rnd.NextDouble() * 0.5f, q.y), Vector3.one * 0.13f, Quaternion.identity, glow, false, PrimitiveType.Sphere);
                }
            }
    }

    static void WrCloth(Transform parent, Vector3 pos, Material mat)
    {
        var mesh = new Mesh { name = "LatkaZnacka" };
        mesh.vertices = new[]
        {
            new Vector3(-0.12f, 0.45f, 0f), new Vector3(0.12f, 0.45f, 0f), new Vector3(-0.10f, 0.1f, 0.06f), new Vector3(0.14f, 0.1f, 0.06f),
            new Vector3(-0.07f, -0.3f, 0.02f), new Vector3(0.17f, -0.3f, 0.02f),
        };
        mesh.triangles = new[] { 0, 1, 2, 2, 1, 3, 2, 3, 4, 4, 3, 5, 0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5 };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        string path = $"{RdMeshDir}/Latka_znacka.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject("Latka_znacka");
        go.transform.SetParent(parent, true);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, (float)(pos.x * 37.0 % 360.0), 6f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
    }

    static void WrReward(WrCtx c, Transform parent, Vector2 end)
    {
        var glade = new GameObject("Skryta_cistinka").transform;
        glade.SetParent(parent, false);
        glade.position = Vector3.zero;
        // vzácny zdroj: kópie existujúceho kryštálu zo zóny alebo z hlavnej Divočiny
        ResourceNode source = null;
        foreach (ResourceNode rn in c.root.GetComponentsInChildren<ResourceNode>(true)) if (rn.ItemId == ItemCatalog.Crystal) { source = rn; break; }
        if (source == null)
            foreach (ResourceNode rn in UnityEngine.Object.FindObjectsByType<ResourceNode>(FindObjectsInactive.Include))
                if (rn.ItemId == ItemCatalog.Crystal) { source = rn; break; }
        int crystals = 0;
        if (source != null)
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad + 0.4f;
                Vector2 p = end + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.2f;
                GameObject copy = UnityEngine.Object.Instantiate(source.gameObject, glade);
                copy.name = "Zdroj_krystal_skryty_" + (i + 1);
                copy.transform.position = new Vector3(p.x, WrHeight(c, p.x, p.y), p.y);
                copy.SetActive(true);
                crystals++;
            }
        if (c.key == "Divocina")
        {
            Vector3 altar = new Vector3(end.x, WrHeight(c, end.x, end.y), end.y);
            RdBox(glade, "Oltar", altar + Vector3.up * 0.3f, new Vector3(0.9f, 0.6f, 0.9f), Quaternion.Euler(0f, 20f, 0f), RdMat("Cesta_Betonovy", null, null, new Color(0.35f, 0.35f, 0.36f), 0.05f), true);
            var pick = new GameObject("Yumin_zapisnik");
            pick.transform.SetParent(glade, false);
            pick.transform.position = altar + Vector3.up * 0.62f;
            pick.AddComponent<ItemPickup>().itemId = ItemCatalog.YumiNotebook;
        }
        Debug.Log($"Cesty divočín ({c.key}): skrytá čistinka na ({end.x:F0}, {end.y:F0}), kryštály {crystals}{(c.key == "Divocina" ? ", Yumin zápisník" : "")}.");
    }

    // ------------------------------------------------------------------ Kontrola
    static void WrVerify(WrCtx c)
    {
        Physics.SyncTransforms();
        int roads = 0, samples = 0, blocked = 0;
        float maxSlope = 0f;
        var reasons = new Dictionary<string, int>();
        foreach (WrRoad r in c.roads)
        {
            if (r.disc) continue;
            roads++;
            for (int i = 0; i < r.dense.Count; i += 3)
            {
                samples++;
                Vector3 p = r.dense[i];
                float u = (p.x - c.o.x) / c.size, v = (p.z - c.o.z) / c.size;
                maxSlope = Mathf.Max(maxSlope, c.data.GetSteepness(u, v));
                string why = WrBlockReason(c, new Vector2(p.x, p.z), r.half[i] + 0.1f, true);
                if (why != null) { blocked++; reasons[r.id + " " + why] = reasons.TryGetValue(r.id + " " + why, out int n) ? n + 1 : 1; }
            }
        }
        string detail = "";
        foreach (KeyValuePair<string, int> kv in reasons) detail += $"{kv.Key} x{kv.Value}; ";
        Debug.Log($"Cesty divočín ({c.key}): trasy {roads}, vzorky {samples}, v kolízii {blocked}, najväčší sklon pod cestou {maxSlope:F0}°. {detail}");
    }
}
