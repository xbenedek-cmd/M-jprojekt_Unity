using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Blok 4 plánu ciest: navigačná sieť (RoadGraph) z postavených ciest, pevné hliadky vojakov (P1–P4), vojaci v Centre a Hradbe
/// a tábor drevorubačov v Divočine. Projekt nemá NavMesh, preto sa používajú uzly a hrany pozdĺž ulíc.
/// </summary>
public static partial class ProgenyTools
{
    class RgLine
    {
        public string id;
        public float width;
        public List<Vector3> pts = new List<Vector3>();
    }

    class RgNode
    {
        public Vector3 pos;
        public float width;
        public int line;
        public bool endpoint;
        public HashSet<int> links = new HashSet<int>();
    }

    static readonly string[] RgSoldierNames = { "Hliadka_P1_a", "Hliadka_P1_b", "Hliadka_P2_a", "Hliadka_P3_a", "Hliadka_P4_a", "Hliadka_P4_b" };

    [MenuItem("Progeny/Mesto/Navigácia: zostaviť sieť ciest, hliadky a drevorubačov (blok 4)", false, 335)]
    static void BuildNavigationBlock4()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Navigácia: najprv ukonči Play."); return; }
        GameObject city = GameObject.Find("Mesto_Centrum"), wall = GameObject.Find("Hradba_Rozsirenie");
        GameObject cityRoads = GameObject.Find("Cesty_Centrum"), wallRoads = GameObject.Find("Hradba_Cesty");
        if (city == null || wall == null || cityRoads == null || wallRoads == null) { Debug.LogWarning("Navigácia: najprv postav cesty (blok 2)."); return; }

        // staré výtvory sa zmažú
        foreach (RoadGraph g in Object.FindObjectsByType<RoadGraph>(FindObjectsInactive.Include)) Object.DestroyImmediate(g);
        foreach (string n in RgSoldierNames)
            for (int guard = 0; guard < 4; guard++) { GameObject s = GameObject.Find(n); if (s == null) break; Object.DestroyImmediate(s); }
        foreach (LumberCamp lc in Object.FindObjectsByType<LumberCamp>(FindObjectsInactive.Include)) Object.DestroyImmediate(lc.gameObject);
        Physics.SyncTransforms();

        // ----- Centrum -----
        var cityLines = new List<RgLine>();
        RgStraight(cityLines, "AvenueS_j", -372f, -176f, -372f, -102f, 14f);
        RgStraight(cityLines, "AvenueS_s", -372f, -46f, -372f, 70f, 14f);
        RgStraight(cityLines, "AvenueV_z", -516f, -74f, -400f, -74f, 14f);
        RgStraight(cityLines, "AvenueV_v", -344f, -74f, -228f, -74f, 14f);
        RgStraight(cityLines, "UlicaS_z", -447f, -218f, -447f, 70f, 8f);
        RgStraight(cityLines, "UlicaS_v", -297f, -218f, -297f, 70f, 8f);
        RgStraight(cityLines, "UlicaV_j", -516f, -149f, -228f, -149f, 8f);
        RgStraight(cityLines, "UlicaV_s", -516f, 1f, -228f, 1f, 8f);
        var ring = new RgLine { id = "Rondel", width = 8f };
        for (int i = 0; i <= 32; i++)
        {
            float a = i * Mathf.PI * 2f / 32f;
            ring.pts.Add(new Vector3(-372f + Mathf.Cos(a) * 26f, 0f, -74f + Mathf.Sin(a) * 26f));
        }
        cityLines.Add(ring);
        foreach (RdRoad r in RdParse(RdCityRoads)) cityLines.Add(new RgLine { id = r.id, width = r.width, pts = RdSample(r.pts, 1f) });
        RoadGraph cityGraph = RgBuildGraph(cityRoads, cityLines, RdCityY, new Vector3(-522f, -10f, -224f), new Vector3(-222f, 40f, 76f), "Centrum");

        // hliadky Centra: body sa po uliciach prepoja cez sieť
        var cityPatrols = new List<RoadGraph.Patrol>
        {
            RgPatrol(cityGraph, "P1", true, new[] { new Vector3(-372f, 0f, -176f), new Vector3(-372f, 0f, -104f), new Vector3(-390f, 0f, -92f), new Vector3(-398f, 0f, -74f), new Vector3(-390f, 0f, -56f), new Vector3(-372f, 0f, -48f), new Vector3(-372f, 0f, 56f) },
                new[] { new Vector2(8f, 12f), new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(3f, 6f), new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(3f, 5f) }),
            RgPatrol(cityGraph, "P2", true, new[] { new Vector3(-514f, 0f, -72f), new Vector3(-420f, 0f, -72f), new Vector3(-398f, 0f, -74f), new Vector3(-346f, 0f, -74f), new Vector3(-230f, 0f, -72f) },
                new[] { new Vector2(4f, 8f), new Vector2(0f, 0.1f), new Vector2(3f, 6f), new Vector2(0f, 0.1f), new Vector2(4f, 8f) }),
            RgPatrol(cityGraph, "P3", false, new[] { new Vector3(-360f, 0f, -186f), new Vector3(-301f, 0f, -186f), new Vector3(-297f, 0f, -150f), new Vector3(-262f, 0f, -149f), new Vector3(-262f, 0f, -100f), new Vector3(-262f, 0f, -85f), new Vector3(-300f, 0f, -74f), new Vector3(-372f, 0f, -100f) },
                new[] { new Vector2(5f, 8f), new Vector2(0f, 0.1f), new Vector2(3f, 5f), new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(4f, 7f), new Vector2(0f, 0.1f), new Vector2(3f, 5f) }, true),
        };
        RgSetPatrols(cityGraph, cityPatrols);

        // ----- Hradba -----
        var wallLines = new List<RgLine>();
        RgStraight(wallLines, "H0a", 136f, 18f, 136f, -4f, 6f);
        RgStraight(wallLines, "H0b", 136f, 18f, 115f, 15f, 4f);
        foreach (RdRoad r in RdParse(RdWallRoads)) wallLines.Add(new RgLine { id = r.id, width = r.width, pts = RdSample(r.pts, 1f) });
        RoadGraph wallGraph = RgBuildGraph(wallRoads, wallLines, RdWallY, new Vector3(67f, 0f, -124f), new Vector3(247f, 40f, 60f), "Hradba");
        RgSetPatrols(wallGraph, new List<RoadGraph.Patrol>
        {
            RgPatrol(wallGraph, "P4", true, new[] { new Vector3(136f, 3.1f, -4f), new Vector3(122f, 3.1f, -27f), new Vector3(115f, 3.1f, -39f), new Vector3(112f, 3.1f, -58f), new Vector3(128f, 3.1f, -100f), new Vector3(135f, 3.1f, -118f) },
                new[] { new Vector2(8f, 12f), new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), new Vector2(8f, 12f) }),
        });

        // ----- Divočiny -----
        rgPruneDecor = true;
        foreach (string rootName in WrZoneRoots)
        {
            GameObject root = GameObject.Find(rootName);
            Transform group = root != null ? root.transform.Find(WrGroup) : null;
            if (group == null) continue;
            var lines = new List<RgLine>();
            foreach (WildRoadPath p in group.GetComponentsInChildren<WildRoadPath>())
            {
                if (p.Kind == 'A') continue;
                lines.Add(new RgLine { id = p.RoadId, width = p.Width, pts = new List<Vector3>(p.Points) });
            }
            Terrain t = root.GetComponentInChildren<Terrain>(true);
            Vector3 o = t != null ? t.transform.position : root.transform.position;
            float size = t != null ? t.terrainData.size.x : 420f;
            Vector2? camp = null;
            Transform campT = group.Find("Tabor_drevorubacov");
            if (campT != null) { Transform fire = campT.Find("Plamen"); if (fire != null) camp = new Vector2(fire.position.x, fire.position.z); }
            if (camp.HasValue) lines.Add(new RgLine { id = "Tabor", width = 6f, pts = new List<Vector3> { new Vector3(camp.Value.x, 0f, camp.Value.y), new Vector3(camp.Value.x + 0.5f, 0f, camp.Value.y - 0.5f) } });
            RoadGraph g = RgBuildGraph(group.gameObject, lines, 0f, new Vector3(o.x, -50f, o.z), new Vector3(o.x + size, 200f, o.z + size), rootName, true);

            if (camp.HasValue && rootName == "Divocina" && campT != null)
            {
                var boss = new GameObject("Velitel_tabora");
                boss.transform.SetParent(campT, true);
                boss.transform.position = new Vector3(camp.Value.x, RgGroundAny(camp.Value.x, camp.Value.y), camp.Value.y);
                var lc = boss.AddComponent<LumberCamp>();
                Transform pileT = campT.Find("Hromada_polien");
                lc.Configure(pileT != null ? pileT.position : boss.transform.position + new Vector3(0f, 0f, -7.9f));
            }
        }

        rgPruneDecor = false;

        // ----- Vojaci na hliadke -----
        RgSpawnPatrolSoldiers(city.transform, cityGraph, new[] { ("Hliadka_P1_a", "P1"), ("Hliadka_P1_b", "P1"), ("Hliadka_P2_a", "P2"), ("Hliadka_P3_a", "P3") });
        RgSpawnPatrolSoldiers(wall.transform, wallGraph, new[] { ("Hliadka_P4_a", "P4"), ("Hliadka_P4_b", "P4") });

        EditorSceneManager.MarkSceneDirty(city.scene);
        RgReport(cityGraph, wallGraph);
    }

    // ------------------------------------------------------------------ Pomocné
    static void RgStraight(List<RgLine> list, string id, float x0, float z0, float x1, float z1, float width)
    {
        var l = new RgLine { id = id, width = width };
        int n = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(new Vector2(x0, z0), new Vector2(x1, z1)) / 1f));
        for (int i = 0; i <= n; i++) l.pts.Add(new Vector3(Mathf.Lerp(x0, x1, (float)i / n), 0f, Mathf.Lerp(z0, z1, (float)i / n)));
        list.Add(l);
    }

    static float RgGroundAny(float x, float z)
    {
        if (Physics.Raycast(new Vector3(x, 400f, z), Vector3.down, out RaycastHit h, 800f, ~0, QueryTriggerInteraction.Ignore)) return h.point.y;
        return 0f;
    }

    static bool RgSolidAbove(Collider c, float y)
    {
        if (c == null || c.isTrigger || c is TerrainCollider || c.attachedRigidbody != null) return false;
        if (c.GetComponentInParent<Damageable>() != null || c.GetComponentInParent<PlayerStats>() != null || c.GetComponentInParent<QuestWorker>() != null) return false;
        return c.bounds.max.y > y + 0.5f;
    }

    static bool rgPruneDecor;   // v divočine: dekor (kmene, kamene) stojaci na hrane siete sa odstráni

    static bool InDecor(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent) if (p.name == "Dekor" && p.parent != null && p.parent.name == WrGroup) return true;
        return false;
    }

    static bool RgEdgeClear(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist < 0.3f) return true;
        List<GameObject> prune = null;
        foreach (RaycastHit h in Physics.SphereCastAll(a + Vector3.up * 0.8f, 0.35f, d / dist, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.distance <= 0.01f || !RgSolidAbove(h.collider, Mathf.Min(a.y, b.y))) continue;
            if (rgPruneDecor && InDecor(h.collider.transform))
            {
                if (prune == null) prune = new List<GameObject>();
                Transform top = h.collider.transform;
                while (top.parent != null && top.parent.name != "Dekor") top = top.parent;
                if (!prune.Contains(top.gameObject)) prune.Add(top.gameObject);
                continue;
            }
            return false;
        }
        if (prune != null) { foreach (GameObject g in prune) if (g != null) Object.DestroyImmediate(g); Physics.SyncTransforms(); }
        return true;
    }

    static RoadGraph RgBuildGraph(GameObject host, List<RgLine> lines, float baseY, Vector3 min, Vector3 max, string label, bool pointsHaveHeight = false)
    {
        var nodes = new List<RgNode>();
        for (int li = 0; li < lines.Count; li++)
        {
            RgLine line = lines[li];
            float acc = 0f; int prev = -1;
            for (int i = 0; i < line.pts.Count; i++)
            {
                bool last = i == line.pts.Count - 1;
                if (i > 0) acc += Vector3.Distance(line.pts[i - 1], line.pts[i]);
                if (i != 0 && !last && acc < 4f) continue;
                acc = 0f;
                Vector3 p = line.pts[i];
                p.y = pointsHaveHeight ? p.y : RdGround(p.x, p.z, baseY);
                var n = new RgNode { pos = p, width = line.width, line = li, endpoint = i == 0 || last };
                nodes.Add(n);
                int id = nodes.Count - 1;
                if (prev >= 0 && RgEdgeClear(nodes[prev].pos, p)) { nodes[prev].links.Add(id); n.links.Add(prev); }
                prev = id;
            }
        }
        // križovatky: uzly rôznych ulíc blízko seba
        for (int i = 0; i < nodes.Count; i++)
            for (int j = i + 1; j < nodes.Count; j++)
            {
                if (nodes[i].line == nodes[j].line) continue;
                Vector3 d = nodes[i].pos - nodes[j].pos;
                if (Mathf.Abs(d.y) > 1.5f) continue;
                d.y = 0f;
                if (d.sqrMagnitude < 4.8f * 4.8f && RgEdgeClear(nodes[i].pos, nodes[j].pos)) { nodes[i].links.Add(j); nodes[j].links.Add(i); }
            }
        // koncové body sa napoja na najbližšiu inú ulicu (napojenie na hranu ulice)
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!nodes[i].endpoint) continue;
            int best = -1; float bestSqr = 14f * 14f;
            for (int j = 0; j < nodes.Count; j++)
            {
                if (nodes[j].line == nodes[i].line) continue;
                Vector3 d = nodes[i].pos - nodes[j].pos;
                if (Mathf.Abs(d.y) > 2f) continue;
                d.y = 0f;
                float s = d.sqrMagnitude;
                if (s < bestSqr && !nodes[i].links.Contains(j) && RgEdgeClear(nodes[i].pos, nodes[j].pos)) { bestSqr = s; best = j; }
            }
            if (best >= 0 && !RgLinkedToOtherLine(nodes, i)) { nodes[i].links.Add(best); nodes[best].links.Add(i); }
        }

        var arr = new RoadGraph.Node[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            Vector3 t = Vector3.forward;
            int first = -1, lastN = -1;
            foreach (int l in nodes[i].links) if (nodes[l].line == nodes[i].line) { if (first < 0) first = l; else lastN = l; }
            if (first >= 0) t = lastN >= 0 ? nodes[lastN].pos - nodes[first].pos : nodes[first].pos - nodes[i].pos;
            t.y = 0f;
            arr[i] = new RoadGraph.Node { pos = nodes[i].pos, tangent = t.sqrMagnitude > 0.001f ? t.normalized : Vector3.forward, width = nodes[i].width, links = new List<int>(nodes[i].links).ToArray() };
        }
        RoadGraph graph = host.GetComponent<RoadGraph>();
        if (graph == null) graph = host.AddComponent<RoadGraph>();
        graph.Set(min, max, arr, new RoadGraph.Patrol[0]);
        EditorUtility.SetDirty(graph);
        Debug.Log($"Navigácia ({label}): uzly {arr.Length}.");
        return graph;
    }

    static bool RgLinkedToOtherLine(List<RgNode> nodes, int i)
    {
        foreach (int l in nodes[i].links) if (nodes[l].line != nodes[i].line) return true;
        return false;
    }

    static RoadGraph.Patrol RgPatrol(RoadGraph g, string id, bool pingPong, Vector3[] design, Vector2[] dwell, bool closeLoop = false)
    {
        var pts = new List<Vector3>(); var dw = new List<Vector2>();
        var buffer = new List<Vector3>();
        for (int i = 0; i < design.Length; i++)
        {
            buffer.Clear();
            if (i == 0) { int n0 = g.Nearest(design[0], 25f); if (n0 >= 0) { pts.Add(g.GetNode(n0).pos); dw.Add(dwell[0]); } continue; }
            if (!g.TryPath(design[i - 1], design[i], buffer, 0f, 25f)) { Debug.LogWarning($"Hliadka {id}: úsek {i} sa po uliciach nenašiel."); continue; }
            for (int k = 0; k < buffer.Count; k++)
            {
                bool lastOfLeg = k == buffer.Count - 1;
                if (pts.Count > 0 && (pts[pts.Count - 1] - buffer[k]).sqrMagnitude < 0.25f) { if (lastOfLeg) dw[dw.Count - 1] = dwell[i]; continue; }
                pts.Add(buffer[k]);
                dw.Add(lastOfLeg ? dwell[i] : new Vector2(0f, 0.1f));
            }
        }
        if (closeLoop && pts.Count > 2)
        {
            buffer.Clear();
            if (g.TryPath(pts[pts.Count - 1], pts[0], buffer, 0f, 25f))
                foreach (Vector3 b in buffer) if ((pts[pts.Count - 1] - b).sqrMagnitude > 0.25f && (pts[0] - b).sqrMagnitude > 0.25f) { pts.Add(b); dw.Add(new Vector2(0f, 0.1f)); }
        }
        return new RoadGraph.Patrol { id = id, pingPong = pingPong, points = pts.ToArray(), dwell = dw.ToArray() };
    }

    static void RgSetPatrols(RoadGraph g, List<RoadGraph.Patrol> patrols)
    {
        var so = new SerializedObject(g);
        SerializedProperty arr = so.FindProperty("patrols");
        arr.arraySize = patrols.Count;
        for (int i = 0; i < patrols.Count; i++)
        {
            SerializedProperty e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("id").stringValue = patrols[i].id;
            e.FindPropertyRelative("pingPong").boolValue = patrols[i].pingPong;
            SerializedProperty pts = e.FindPropertyRelative("points");
            pts.arraySize = patrols[i].points.Length;
            for (int k = 0; k < pts.arraySize; k++) pts.GetArrayElementAtIndex(k).vector3Value = patrols[i].points[k];
            SerializedProperty dw = e.FindPropertyRelative("dwell");
            dw.arraySize = patrols[i].dwell.Length;
            for (int k = 0; k < dw.arraySize; k++) dw.GetArrayElementAtIndex(k).vector2Value = patrols[i].dwell[k];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(g);
    }

    static void RgSpawnPatrolSoldiers(Transform parent, RoadGraph g, (string name, string patrol)[] list)
    {
        foreach (var s in list)
        {
            RoadGraph.Patrol patrol = null;
            foreach (RoadGraph.Patrol p in g.Patrols) if (p.id == s.patrol) patrol = p;
            if (patrol == null || patrol.points.Length == 0) continue;
            int idx = (s.name.EndsWith("_b") ? patrol.points.Length / 2 : 0);
            Vector3 pos = patrol.points[idx];
            SpawnSoldier(parent, s.name, pos);
        }
    }

    static void RgReport(RoadGraph city, RoadGraph wall)
    {
        foreach (RoadGraph g in Object.FindObjectsByType<RoadGraph>(FindObjectsInactive.Include))
        {
            int n = g.NodeCount;
            if (n == 0) continue;
            var seen = new bool[n];
            var stack = new Stack<int>(); stack.Push(0); seen[0] = true; int reach = 1;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                foreach (int l in g.GetNode(c).links) if (!seen[l]) { seen[l] = true; reach++; stack.Push(l); }
            }
            int edges = 0, bad = 0;
            for (int i = 0; i < n; i++)
            {
                edges += g.GetNode(i).links.Length;
                Vector3 p = g.GetNode(i).pos;
                foreach (Collider c in Physics.OverlapSphere(p + Vector3.up * 0.9f, 0.4f, ~0, QueryTriggerInteraction.Ignore))
                    if (RgSolidAbove(c, p.y)) { bad++; break; }
            }
            string pat = "";
            foreach (RoadGraph.Patrol p in g.Patrols) pat += $"{p.id}={p.points.Length} bodov{(p.pingPong ? " tam a späť" : " okruh")}; ";
            Debug.Log($"Navigácia ({g.name}): uzly {n}, hrany {edges / 2}, dosiahnuteľné z prvého uzla {reach}/{n}, uzly v prekážke {bad}. {pat}");
        }

        // skúšobné cesty po uliciach: lekárka, kupec, príbehové postavy
        var buf = new List<Vector3>();
        string[] names = { "Kazumi→Yumi", "Yumi→Kupec", "Kupec→Mei", "Nádvorie→Vance" };
        Vector3[,] pairs =
        {
            { new Vector3(-412f, 0.6f, -57f), new Vector3(-323f, 0.6f, -50f) },
            { new Vector3(-323f, 0.6f, -50f), new Vector3(-356f, 0.6f, -35f) },
            { new Vector3(-356f, 0.6f, -35f), new Vector3(-412f, 0.6f, -91f) },
            { new Vector3(-372f, 0.6f, -176f), new Vector3(-332f, 0.6f, -57f) },
        };
        for (int i = 0; i < names.Length; i++)
        {
            buf.Clear();
            bool ok = city.TryPath(pairs[i, 0], pairs[i, 1], buf, 0f, 25f);
            float len = 0f;
            for (int k = 1; k < buf.Count; k++) len += Vector3.Distance(buf[k - 1], buf[k]);
            Debug.Log($"Navigácia skúška {names[i]}: {(ok ? $"cesta po uliciach {len:F0} m, {buf.Count} bodov" : "NENÁJDENÁ")}.");
        }
    }
}
