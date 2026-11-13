using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Načítavanie zón podľa polohy: mapa (koreň) zóny je zapnutá len keď v nej stojí hráč alebo práve prešiel portálom / koreňmi / z bane.
/// Domovská oblasť (štart) ostáva zapnutá vždy. Ostatné zóny sa po opustení vypnú po krátkej pauze. Parcely a brány z mesta sa pri štarte
/// presunú mimo zón, aby stavby, príjem a prechody fungovali aj keď je zóna vypnutá.
/// </summary>
public class ZoneLoader : MonoBehaviour
{
    private class Zone
    {
        public string name;
        public GameObject root;
        public Bounds area;       // vodorovný rozsah (y sa ignoruje okrem rozsahu)
        public float lastInside = -999f;
    }

    private const float Linger = 5f;        // zóna po opustení ostane zapnutá ešte toľko sekúnd
    private const float Margin = 14f;
    private const string MineZone = "Divocina_Bane";

    private static readonly List<Zone> zones = new List<Zone>();
    private static readonly Dictionary<string, Bounds> grounds = new Dictionary<string, Bounds>();
    private static bool running;

    private PlayerStats player;
    private float startAt = -1f;

    public static bool HasUnloadedZones
    {
        get
        {
            foreach (Zone z in zones) if (z.root != null && !z.root.activeSelf) return true;
            return false;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { zones.Clear(); grounds.Clear(); running = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<ZoneLoader>() != null) return;
        new GameObject("ZoneLoader").AddComponent<ZoneLoader>();
    }

    void Awake() => Discover();

    // ---------- Zistenie zón (kým je všetko zapnuté) ----------
    void Discover()
    {
        zones.Clear();
        grounds.Clear();

        foreach (string name in new[] { "Divocina", "Divocina_Kanon", "Divocina_Les", "Divocina_Pustatina", "Divocina_Mociare", "Divocina_Bane" })
            AddWild(name);

        AddFlat("Mesto_Centrum", "Zem_Centrum");
        AddFlat("CorpCity", "Zem_Megakorporacia");
        AddFlat("CorpComplex", "Zem_Megakorporacia");

        // Zem zón pre mapu (aj keď sú neskôr vypnuté)
        foreach (Renderer r in FindObjectsByType<Renderer>())
            if (r.gameObject.name.StartsWith("Zem_")) grounds[r.gameObject.name] = GroundBounds(r);

        MakeNonZonePersistent();
    }

    // Hranice zeme z tvaru siete (nezávisle od toho, či je Renderer zapnutý); pri zóne s terénom z terénu
    public static Bounds GroundBounds(Renderer r)
    {
        Terrain terrain = r.transform.parent != null ? r.transform.parent.GetComponentInChildren<Terrain>() : null;
        if (terrain != null && terrain.terrainData != null)
            return new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size);

        // Static batching: sharedMesh je spoločná "Combined Mesh" celej scény (obrovské bounds), preto sa berie bounds samotného renderera
        if (r.isPartOfStaticBatch) return r.bounds;

        MeshFilter mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return r.bounds;

        Bounds local = mf.sharedMesh.bounds;
        var result = new Bounds(r.transform.TransformPoint(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            result.Encapsulate(r.transform.TransformPoint(corner));
        }
        return result;
    }

    void AddWild(string rootName)
    {
        GameObject root = GameObject.Find(rootName);
        if (root == null) return;

        Bounds area;
        Terrain terrain = root.GetComponentInChildren<Terrain>();
        if (terrain != null)
            area = new Bounds(terrain.transform.position + terrain.terrainData.size * 0.5f, terrain.terrainData.size);
        else
        {
            Transform ground = root.transform.Find("Zem_" + rootName.Replace("Divocina_", ""));
            Renderer r = ground != null ? ground.GetComponent<Renderer>() : null;
            if (r == null) return;
            area = GroundBounds(r);
        }
        zones.Add(new Zone { name = rootName, root = root, area = area });
    }

    void AddFlat(string rootName, string groundName)
    {
        GameObject root = GameObject.Find(rootName);
        GameObject ground = GameObject.Find(groundName);
        Renderer r = ground != null ? ground.GetComponent<Renderer>() : null;
        if (root == null || r == null) return;
        foreach (Zone z in zones) if (z.root == root) return;
        zones.Add(new Zone { name = rootName, root = root, area = GroundBounds(r) });
    }

    // Brány (z mesta do zón) a parcely musia ostať aktívne, aj keď je zóna vypnutá
    void MakeNonZonePersistent()
    {
        var gates = new GameObject("PortalyAParcely").transform;

        foreach (PortalGate gate in FindObjectsByType<PortalGate>(FindObjectsInactive.Include))
        {
            string n = gate.gameObject.name;
            if (!n.StartsWith("Portal_Do") || n == "Portal_DoMesta" || n == "Portal_DoMegakorporacie") continue;
            if (InAnyZone(gate.transform)) gate.transform.SetParent(gates, true);
        }
        foreach (BuildPlot plot in FindObjectsByType<BuildPlot>(FindObjectsInactive.Include))
            if (InAnyZone(plot.transform)) plot.transform.SetParent(gates, true);
    }

    static bool InAnyZone(Transform t)
    {
        foreach (Zone z in zones)
            if (z.root != null && t.IsChildOf(z.root.transform)) return true;
        return false;
    }

    // ---------- Verejné rozhranie ----------
    /// <summary>Zapne zónu, v ktorej leží bod (volá sa pri každom teleporte hráča).</summary>
    public static void EnsureLoadedAt(Vector3 p)
    {
        if (!running) return;
        if (p.y < -300f) { Enable(MineZone); return; }
        foreach (Zone z in zones)
            if (Contains(z, p)) { z.lastInside = Time.time; Activate(z); }
    }

    /// <summary>Vypnuté koreňe zón, ktoré pokrývajú bod (napr. Megakorporácia má dva koreňe); pridajú sa do zoznamu.</summary>
    public static bool GetUnloadedRoots(Vector3 p, List<GameObject> into)
    {
        into.Clear();
        foreach (Zone z in zones)
            if (z.root != null && !z.root.activeSelf && Contains(z, p) && !into.Contains(z.root)) into.Add(z.root);
        return into.Count > 0;
    }

    /// <summary>Koreň zóny podľa mena (aj keď je vypnutá); zapne ju.</summary>
    public static GameObject GetRoot(string name)
    {
        foreach (Zone z in zones)
            if (z.name == name && z.root != null) { z.lastInside = Time.time + 30f; Activate(z); return z.root; }
        return GameObject.Find(name);
    }

    /// <summary>Koreň zóny, ktorá leží okolo bodu a je práve vypnutá (mapa si z nej urobí jednorazový snímok bez trvalého načítania).</summary>
    public static bool TryGetUnloadedZone(Vector3 p, out GameObject root)
    {
        foreach (Zone z in zones)
            if (z.root != null && !z.root.activeSelf && Contains(z, p)) { root = z.root; return true; }
        root = null;
        return false;
    }

    public static bool TryGetGround(string objectName, out Bounds bounds) => grounds.TryGetValue(objectName, out bounds);

    private static readonly (string ground, string label)[] ZoneLabels =
    {
        ("Zem_Megakorporacia", "Megakorporácia"), ("Zem_Centrum", "Centrum mesta"), ("Zem_Divocina", "Divočina"),
        ("Zem_Kanon", "Bleskový kaňon"), ("Zem_Les", "Temný les"), ("Zem_Pustatina", "Zrezavená pustatina"),
        ("Zem_Mociare", "Močiare"), ("Zem_Bane", "Bane (povrch)")
    };

    /// <summary>Názov zóny, v ktorej leží bod (vodorovne); mimo známych zón "Mesto odboja".</summary>
    public static string ZoneNameAt(Vector3 p)
    {
        foreach (var z in ZoneLabels)
        {
            if (!grounds.TryGetValue(z.ground, out Bounds b)) continue;
            if (Mathf.Abs(p.x - b.center.x) <= b.extents.x && Mathf.Abs(p.z - b.center.z) <= b.extents.z) return z.label;
        }
        return WorldNames.StartCity;
    }

    static void Enable(string name)
    {
        foreach (Zone z in zones)
            if (z.name == name && z.root != null) { z.lastInside = Time.time; Activate(z); }
    }

    static void Activate(Zone z)
    {
        if (z.root == null || z.root.activeSelf) return;
        z.root.SetActive(true);
        Physics.SyncTransforms();
    }

    static bool Contains(Zone z, Vector3 p)
    {
        Bounds b = z.area;
        return p.x >= b.min.x - Margin && p.x <= b.max.x + Margin && p.z >= b.min.z - Margin && p.z <= b.max.z + Margin
               && p.y >= b.min.y - 80f && p.y <= b.max.y + 150f;
    }

    // ---------- Beh ----------
    void Update()
    {
        if (PauseMenu.AtStartup) return;
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null && startAt < 0f) startAt = Time.time + 6f;   // nech sa najprv dokončí načítanie uloženej hry
            return;
        }
        if (Time.time < startAt) return;
        running = true;

        TickMemoryCleanup();

        Vector3 p = player.transform.position;
        if (p.y < -300f) { Enable(MineZone); return; }   // v bani sa nič nemení

        foreach (Zone z in zones)
        {
            if (z.root == null) continue;
            bool inside = Contains(z, p);
            if (inside) { z.lastInside = Time.time; Activate(z); }
            else if (z.root.activeSelf && Time.time - z.lastInside > Linger)
            {
                RenderDebug.Hidden(z.root, $"ZoneLoader vypína zónu {z.name} (hráč je mimo oblasti + margin {Margin} m, poloha {p})");
                z.root.SetActive(false);
                cleanupPending = true;
                cleanupAt = Time.time + CleanupDelay;
            }
        }
    }

    // ---------- Uvoľnenie pamäte po prechode medzi zónami ----------
    // SetActive(false) pamäť sám neuvoľní: assety vypnutej zóny ostávajú v scéne odkazované, takže UnloadUnusedAssets uvoľní len osirotené
    // (inštancie materiálov, procedurálne siete, zrušené klony). Skutočné uvoľnenie textúr/sietí zón by vyžadovalo additive scény alebo Addressables.
    /// <summary>Vypnúť, ak čistenie spôsobuje záseky na slabšom stroji.</summary>
    public static bool ReleaseMemoryOnZoneChange = true;

    private const float CleanupDelay = 3f;      // po vypnutí zóny počkať, kým sa dokončí prechod (portál, kamera, zápis uloženia)
    private const float CleanupMinGap = 120f;   // plné čistenie je drahé (GC.Collect je blokujúci): nie častejšie než raz za 2 minúty
    private bool cleanupPending;
    private float cleanupAt, lastCleanup = -999f;
    private AsyncOperation unloadOp;

    void TickMemoryCleanup()
    {
        if (!ReleaseMemoryOnZoneChange) { cleanupPending = false; return; }

        // 2. fáza: po dokončení uvoľnenia assetov zbehne GC (už v bezpečnom okamihu, nie počas boja)
        if (unloadOp != null)
        {
            if (!unloadOp.isDone) return;
            unloadOp = null;
            System.GC.Collect();
            return;
        }

        if (!cleanupPending || Time.time < cleanupAt || Time.time - lastCleanup < CleanupMinGap) return;

        if (!IsSafeMomentForCleanup()) { cleanupAt = Time.time + 5f; return; }

        cleanupPending = false;
        lastCleanup = Time.time;
        unloadOp = Resources.UnloadUnusedAssets();
    }

    // Nie počas väzenia, QTE, menu, naháňania hráča ani pred dokončením načítania uloženej hry (zákaz zásekov v zlej chvíli)
    static bool IsSafeMomentForCleanup()
    {
        if (!running || PauseMenu.AtStartup) return false;
        if (JailManager.IsInJail || JailManager.MenuOpen || CombatQte.Active) return false;

        foreach (DemonEnemy e in DemonEnemy.All)
            if (e != null && e.IsChasing) return false;
        return true;
    }
}
