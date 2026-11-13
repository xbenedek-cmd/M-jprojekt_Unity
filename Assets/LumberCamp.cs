using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tábor drevorubačov: stála trojica drevorubačov chodí cez deň z tábora po lesnej ceste (RoadGraph) k stromom, vyrúbe ich,
/// poleno donesie späť na hromadu a v noci sedí pri ohni. Zabití drevorubači sa po čase vrátia.
/// </summary>
public class LumberCamp : MonoBehaviour
{
    [SerializeField] private int crewSize = 3;
    [SerializeField] private float searchRadius = 90f;
    [SerializeField] private float walkSpeed = 2.1f;
    [SerializeField] private float respawnSeconds = 90f;
    [SerializeField] private Vector3 pilePosition;      // kde sa ukladajú polená (vedľa hromady polien)

    private enum State { Rest, ToTree, Chop, Return }

    private class Worker
    {
        public QuestWorker body;
        public State state = State.Rest;
        public BreakableNode tree;
        public readonly List<Vector3> path = new List<Vector3>();
        public int index;
        public float until, respawnAt, lane;
        public bool spawned;
        public Transform carried;
    }

    private readonly List<Worker> crew = new List<Worker>();
    private readonly List<BreakableNode> claimed = new List<BreakableNode>();
    private RoadGraph graph;
    private DayNightCycle clock;
    private int delivered;
    private Material logMaterial;
    private static readonly RaycastHit[] hits = new RaycastHit[16];

    public int WoodDelivered => delivered;

    public void Configure(Vector3 pile) => pilePosition = pile;

    void OnEnable()
    {
        if (crew.Count == 0)
            for (int i = 0; i < crewSize; i++) crew.Add(new Worker { lane = (i - (crewSize - 1) * 0.5f) * 0.8f });
    }

    void Start()
    {
        clock = FindAnyObjectByType<DayNightCycle>();
        logMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.32f, 0.22f, 0.14f) };
    }

    bool IsNight => clock != null && (clock.CurrentPhase == DayNightCycle.DayPhase.Night || clock.CurrentPhase == DayNightCycle.DayPhase.Dusk);

    void Update()
    {
        if (PauseMenu.AtStartup) return;
        if (graph == null) graph = RoadGraph.At(transform.position);
        for (int i = 0; i < crew.Count; i++) Tick(crew[i], i);
    }

    void Tick(Worker w, int index)
    {
        if (w.body == null)
        {
            if (w.spawned)   // zabili ho: vráti sa po čase
            {
                w.spawned = false;
                w.respawnAt = Time.time + respawnSeconds;
                w.state = State.Rest;
                w.carried = null;
                if (w.tree != null) claimed.Remove(w.tree);
                w.tree = null;
                return;
            }
            if (Time.time < w.respawnAt) return;
            Spawn(w, index);
            return;
        }

        switch (w.state)
        {
            case State.Rest:
                RestAtCamp(w, index);
                if (!IsNight && Time.time >= w.until) FindJob(w);
                break;
            case State.ToTree:
                if (IsNight || w.tree == null || w.tree.IsDown || !w.tree.gameObject.activeInHierarchy) { GoHome(w); break; }
                if (Follow(w, 1.6f))
                {
                    w.state = State.Chop;
                    w.until = Time.time + Random.Range(6f, 9f);
                    w.body.SetChopping(true);
                }
                break;
            case State.Chop:
                if (w.tree != null)
                {
                    Vector3 look = w.tree.transform.position - w.body.transform.position;
                    look.y = 0f;
                    if (look.sqrMagnitude > 0.01f) w.body.transform.rotation = Quaternion.Slerp(w.body.transform.rotation, Quaternion.LookRotation(look), 6f * Time.deltaTime);
                }
                if (Time.time < w.until) break;
                w.body.SetChopping(false);
                if (w.tree != null && !w.tree.IsDown) w.tree.AutoHarvest();
                claimed.Remove(w.tree);
                w.tree = null;
                Carry(w, true);
                GoHome(w);
                break;
            case State.Return:
                if (Follow(w, 1.2f))
                {
                    if (w.carried != null) { Deliver(); Carry(w, false); }
                    w.state = State.Rest;
                    w.until = Time.time + Random.Range(5f, 12f);
                }
                break;
        }
    }

    void Spawn(Worker w, int index)
    {
        Vector3 p = transform.position + new Vector3(Mathf.Cos(index * 2.1f), 0f, Mathf.Sin(index * 2.1f)) * 2.6f;
        w.body = QuestWorker.Create(p, 11 + index, "Drevorubač", new Color(0.55f, 0.35f, 0.2f));
        w.body.follow = false;
        w.body.transform.SetParent(transform, true);
        w.state = State.Rest;
        w.until = Time.time + Random.Range(2f, 8f);
        w.respawnAt = 0f;
        w.spawned = true;
        w.carried = null;
        w.tree = null;
    }

    void RestAtCamp(Worker w, int index)
    {
        // pri ohni: pomaly sa priblíži k svojmu miestu a otočí sa k ohňu
        Vector3 spot = transform.position + new Vector3(Mathf.Cos(index * 2.1f + 0.5f), 0f, Mathf.Sin(index * 2.1f + 0.5f)) * (IsNight ? 2.0f : 3.2f);
        Vector3 to = spot - w.body.transform.position;
        to.y = 0f;
        if (to.magnitude > 0.5f) Step(w.body.transform, to.normalized, walkSpeed * 0.7f);
        else
        {
            Vector3 fire = transform.position - w.body.transform.position;
            fire.y = 0f;
            if (fire.sqrMagnitude > 0.01f) w.body.transform.rotation = Quaternion.Slerp(w.body.transform.rotation, Quaternion.LookRotation(fire), 3f * Time.deltaTime);
        }
    }

    // ---------- Práca ----------
    void FindJob(Worker w)
    {
        if (graph == null) { w.until = Time.time + 5f; return; }
        BreakableNode best = null;
        float bestSqr = searchRadius * searchRadius;
        Vector3 from = transform.position;
        foreach (BreakableNode n in BreakableNode.All)
        {
            if (n == null || !n.IsTree || n.IsDown || !n.gameObject.activeInHierarchy || claimed.Contains(n)) continue;
            float sqr = (n.transform.position - from).sqrMagnitude;
            if (sqr >= bestSqr) continue;
            int node = graph.Nearest(n.transform.position, 22f);
            if (node < 0) continue;
            if (!LegClear(graph.GetNode(node).pos, n.transform.position)) continue;
            bestSqr = sqr;
            best = n;
        }
        if (best == null) { w.until = Time.time + 8f; return; }

        Vector3 treePos = best.transform.position;
        int nearNode = graph.Nearest(treePos, 22f);
        w.path.Clear();
        if (!graph.TryPath(w.body.transform.position, graph.GetNode(nearNode).pos, w.path, w.lane, 60f)) { w.until = Time.time + 8f; return; }
        Vector3 end = w.path[w.path.Count - 1];
        Vector3 dir = treePos - end;
        dir.y = 0f;
        w.path.Add(treePos - dir.normalized * 1.5f);
        w.tree = best;
        claimed.Add(best);
        w.index = 0;
        w.state = State.ToTree;
    }

    bool LegClear(Vector3 a, Vector3 treePos)
    {
        Vector3 d = treePos - a;
        d.y = 0f;
        float dist = d.magnitude - 1.2f;
        if (dist < 0.5f) return true;
        int n = Physics.SphereCastNonAlloc(a + Vector3.up * 0.8f, 0.35f, d.normalized, hits, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i].collider;
            if (c is TerrainCollider || c.isTrigger || c.attachedRigidbody != null) continue;
            if (c.GetComponentInParent<Damageable>() != null || c.GetComponentInParent<QuestWorker>() != null) continue;
            if (hits[i].distance > 0.01f) return false;
        }
        return true;
    }

    void GoHome(Worker w)
    {
        w.body.SetChopping(false);
        if (w.tree != null) { claimed.Remove(w.tree); w.tree = null; }
        w.path.Clear();
        if (graph != null) graph.TryPath(w.body.transform.position, transform.position, w.path, w.lane, 60f);
        w.path.Add(pilePosition != Vector3.zero ? pilePosition : transform.position);
        w.index = 0;
        w.state = State.Return;
    }

    /// <summary>Ide po bodoch trasy; true, keď došiel na jej koniec.</summary>
    bool Follow(Worker w, float stop)
    {
        if (w.index >= w.path.Count) return true;
        Vector3 target = w.path[w.index];
        Vector3 to = target - w.body.transform.position;
        to.y = 0f;
        bool last = w.index == w.path.Count - 1;
        if (to.magnitude <= (last ? stop : 0.8f))
        {
            w.index++;
            return w.index >= w.path.Count;
        }
        Step(w.body.transform, to.normalized, walkSpeed);
        return false;
    }

    static void Step(Transform t, Vector3 dir, float speed)
    {
        t.position += dir * speed * GameSpeed.Npc * Time.deltaTime;
        t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }

    // ---------- Poleno ----------
    void Carry(Worker w, bool on)
    {
        if (on && w.carried == null)
        {
            GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(log.GetComponent<Collider>());
            log.name = "Poleno";
            log.transform.SetParent(w.body.transform, false);
            log.transform.localPosition = new Vector3(0.25f, 1.55f, 0f);
            log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            log.transform.localScale = new Vector3(0.2f, 0.55f, 0.2f);
            log.GetComponent<Renderer>().sharedMaterial = logMaterial;
            w.carried = log.transform;
        }
        else if (!on && w.carried != null)
        {
            Destroy(w.carried.gameObject);
            w.carried = null;
        }
    }

    void Deliver()
    {
        delivered++;
        if (delivered > 12) return;   // na hromade je viditeľných najviac 12 polien
        Vector3 basePos = pilePosition != Vector3.zero ? pilePosition : transform.position;
        GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(log.GetComponent<Collider>());
        log.name = "Poleno_hromada";
        log.transform.SetParent(transform, true);
        int i = delivered - 1;
        log.transform.position = basePos + new Vector3(((i % 4) - 1.5f) * 0.34f, 0.2f + (i / 4) * 0.28f, 0.9f);
        log.transform.rotation = Quaternion.Euler(0f, 90f, 90f);
        log.transform.localScale = new Vector3(0.28f, 0.55f, 0.28f);
        log.GetComponent<Renderer>().sharedMaterial = logMaterial;
    }
}
