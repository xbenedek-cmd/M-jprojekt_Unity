using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sieť ciest zóny (mesto, Hradba, divočina) pre NPC: uzly pozdĺž postavených ciest a ulíc, spojené hrany a pevné hliadkové trasy.
/// Projekt nemá NavMesh, preto sa hľadá cesta po uzloch (A*) a postavy idú po uliciach a chodníkoch, nie cez domy a steny.
/// Sieť sa zostavuje v editore (Progeny → Mesto → Navigácia …), za behu sa len číta.
/// </summary>
public class RoadGraph : MonoBehaviour
{
    [Serializable]
    public class Node
    {
        public Vector3 pos;
        public Vector3 tangent;
        public float width;
        public int[] links = new int[0];
    }

    [Serializable]
    public class Patrol
    {
        public string id;
        public bool pingPong;
        public Vector3[] points = new Vector3[0];
        public Vector2[] dwell = new Vector2[0];
    }

    [SerializeField] private Vector3 areaMin;
    [SerializeField] private Vector3 areaMax;
    [SerializeField] private Node[] nodes = new Node[0];
    [SerializeField] private Patrol[] patrols = new Patrol[0];

    private static readonly List<RoadGraph> all = new List<RoadGraph>();

    public int NodeCount => nodes.Length;
    public Patrol[] Patrols => patrols;
    public Node GetNode(int i) => nodes[i];

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() => all.Clear();

    public void Set(Vector3 min, Vector3 max, Node[] n, Patrol[] p)
    {
        areaMin = min;
        areaMax = max;
        nodes = n;
        patrols = p;
    }

    public bool Contains(Vector3 p) => p.x >= areaMin.x && p.x <= areaMax.x && p.z >= areaMin.z && p.z <= areaMax.z;

    /// <summary>Sieť, do ktorej oblasti patrí bod (aktívna zóna); null, ak žiadna.</summary>
    public static RoadGraph At(Vector3 p)
    {
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].nodes.Length > 0 && all[i].Contains(p)) return all[i];
        return null;
    }

    public static bool HasPatrols(Vector3 p)
    {
        RoadGraph g = At(p);
        return g != null && g.patrols.Length > 0;
    }

    /// <summary>Hliadka podľa mena postavy ("…_P1_…"), inak podľa stabilného čísla.</summary>
    public Patrol FindPatrol(string npcName, int hash)
    {
        if (patrols.Length == 0) return null;
        foreach (Patrol p in patrols)
            if (!string.IsNullOrEmpty(p.id) && npcName.Contains("_" + p.id + "_")) return p;
        return patrols[(hash & 0x7fffffff) % patrols.Length];
    }

    /// <summary>Najbližší uzol (vodorovne, výškový rozdiel do 3 m); -1, ak je žiadny do maxDist.</summary>
    public int Nearest(Vector3 p, float maxDist)
    {
        int best = -1;
        float bestSqr = maxDist * maxDist;
        for (int i = 0; i < nodes.Length; i++)
        {
            Vector3 d = nodes[i].pos - p;
            if (Mathf.Abs(d.y) > 3f) continue;
            d.y = 0f;
            float s = d.sqrMagnitude;
            if (s < bestSqr) { bestSqr = s; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Cesta po uzloch od bodu from k bodu to. lane (-1..1) posunie body bokom v rámci šírky cesty, aby postavy nechodili po jednej čiare.
    /// Do result sa pridajú len medziľahlé body (od najbližšieho uzla pri from po najbližší uzol pri to).
    /// </summary>
    public bool TryPath(Vector3 from, Vector3 to, List<Vector3> result, float lane = 0f, float maxSnap = 30f)
    {
        int s = Nearest(from, maxSnap), g = Nearest(to, maxSnap);
        if (s < 0 || g < 0) return false;

        var path = new List<int>();
        if (s == g) path.Add(s);
        else
        {
            int n = nodes.Length;
            var cost = new float[n];
            var prev = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { cost[i] = float.MaxValue; prev[i] = -1; }
            var open = new SortedSet<(float f, int id)>();
            cost[s] = 0f;
            open.Add((0f, s));
            bool found = false;
            while (open.Count > 0)
            {
                (float f, int id) top = open.Min;
                open.Remove(top);
                int cur = top.id;
                if (closed[cur]) continue;
                closed[cur] = true;
                if (cur == g) { found = true; break; }
                int[] links = nodes[cur].links;
                for (int k = 0; k < links.Length; k++)
                {
                    int nb = links[k];
                    if (closed[nb]) continue;
                    float ng = cost[cur] + Vector3.Distance(nodes[cur].pos, nodes[nb].pos);
                    if (ng >= cost[nb]) continue;
                    cost[nb] = ng;
                    prev[nb] = cur;
                    open.Add((ng + Vector3.Distance(nodes[nb].pos, nodes[g].pos), nb));
                }
            }
            if (!found) return false;
            for (int k = g; k >= 0; k = prev[k]) path.Add(k);
            path.Reverse();
        }

        for (int i = 0; i < path.Count; i++)
        {
            Node nd = nodes[path[i]];
            Vector3 perp = new Vector3(-nd.tangent.z, 0f, nd.tangent.x);
            float off = Mathf.Clamp(lane, -1f, 1f) * Mathf.Min(nd.width * 0.3f, 2.2f);
            result.Add(nd.pos + perp * off);
        }
        return true;
    }
}
