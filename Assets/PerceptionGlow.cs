using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Kláves E (obrátené vnímanie) nemení vzhľad postáv, len ich zvýrazní žiarou (aj cez steny)
/// a zatemní okraje obrazovky (tunelové videnie).
/// Démon vidí žiariť ľudí a zvieratá (korisť), človek vidí žiariť všetkých ľudí (modro) a preskúmaných démonov (fialovo).
/// Skript sa vytvorí sám, netreba ho nikam pridávať.
/// </summary>
public class PerceptionGlow : MonoBehaviour
{
    /// <summary>0 = vypnuté, 1 = plné zvýraznenie (používa HUD na tunelové videnie).</summary>
    public static float Blend { get; private set; }
    public static bool PlayerIsDemon { get; private set; }

    [Header("Zvýraznenie")]
    public float range = 45f;
    public float fadeSpeed = 5f;
    [Tooltip("Ako človek zvýrazniť aj zjavné príšery (nižší, strední), nielen preskúmaných")]
    public bool humanHighlightsOvertDemons = false;
    public Color preyColor = new Color(1f, 0.15f, 0.2f);       // korisť pre démona
    public Color demonColor = new Color(0.75f, 0.3f, 1f);      // preskúmaní démoni pre človeka
    public Color humanColor = new Color(0.35f, 0.85f, 1f);     // ľudia pre človeka

    private class Halo
    {
        public GameObject go;
        public Material mat;
        public Color color;
        public float phase;
    }

    private readonly Dictionary<Transform, Halo> halos = new Dictionary<Transform, Halo>();
    private readonly HashSet<Transform> wanted = new HashSet<Transform>();
    private readonly List<Transform> stale = new List<Transform>();

    private PlayerStats player;
    private float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<PerceptionGlow>() == null)
            new GameObject("PerceptionGlow").AddComponent<PerceptionGlow>();
    }

    void OnDestroy() => ClearAll();

    void Update()
    {
        if (player == null)
        {
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.5f;
                player = FindAnyObjectByType<PlayerStats>();
            }
            return;
        }

        bool on = PerceptionToggle.IsDemonVision && !player.IsDead;
        Blend = Mathf.MoveTowards(Blend, on ? 1f : 0f, fadeSpeed * Time.deltaTime);
        PlayerIsDemon = player.IsDemon;

        if (!on && Blend <= 0.001f)
        {
            if (halos.Count > 0) ClearAll();
            return;
        }

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.3f;
            Scan();
        }

        // Jemné pulzovanie žiary
        foreach (KeyValuePair<Transform, Halo> kv in halos)
        {
            Halo h = kv.Value;
            float a = Blend * (0.26f + 0.10f * Mathf.Sin(Time.time * 4f + h.phase));
            h.mat.color = new Color(h.color.r, h.color.g, h.color.b, a);
        }
    }

    // ---------- Kto má žiariť ----------
    void Scan()
    {
        wanted.Clear();
        Vector3 p = player.transform.position;
        bool demon = player.IsDemon;

        foreach (NPCPerception n in NPCPerception.All)
        {
            if (n == null) continue;

            Damageable d = n.GetComponent<Damageable>();
            if (d != null && d.IsDead) continue;
            BloodSource bs = n.GetComponent<BloodSource>();
            if (bs != null && bs.IsDead) continue;

            if (Vector3.Distance(p, n.transform.position) > range) continue;

            Color c;
            if (WantsGlow(n, demon, out c)) Register(n.transform, c);
        }

        // Zvieratá žiaria len démonovi (korisť)
        if (demon)
        {
            foreach (BloodSource b in BloodSource.All)
            {
                if (b == null || b.IsDead || b.GetComponent<NPCPerception>() != null) continue;
                if (Vector3.Distance(p, b.transform.position) > range) continue;
                Register(b.transform, preyColor);
            }
        }

        // Zruš žiaru tým, čo už žiariť nemajú
        stale.Clear();
        foreach (KeyValuePair<Transform, Halo> kv in halos)
            if (kv.Key == null || !wanted.Contains(kv.Key)) stale.Add(kv.Key);

        foreach (Transform t in stale) RemoveHalo(t);
    }

    bool WantsGlow(NPCPerception n, bool playerIsDemon, out Color color)
    {
        if (playerIsDemon)
        {
            // Démon vidí ľudí (aj maskovaného vyššieho démona, ktorý vyzerá ako človek)
            color = preyColor;
            return n.kind == NPCKind.Civilian || (n.kind == NPCKind.HigherDemon && !n.Revealed);
        }

        // Človek vidí žiariť všetkých ľudí (rovnako ako démon korisť) a démonov, ktorých už preskúmal cez T
        if (n.kind == NPCKind.Civilian) { color = humanColor; return true; }

        if (n.kind == NPCKind.HigherDemon)
        {
            DemonMarks m = n.GetComponent<DemonMarks>();
            bool known = n.Revealed || (m != null && (m.Discovered != DemonSign.None || m.Resolved));
            color = known ? demonColor : humanColor;   // maskovaný vyšší démon vyzerá ako človek
            return true;
        }
        color = demonColor;
        return humanHighlightsOvertDemons;      // nižší a strední
    }

    // ---------- Žiara okolo postavy ----------
    void Register(Transform t, Color color)
    {
        wanted.Add(t);

        Halo h;
        if (halos.TryGetValue(t, out h))
        {
            h.color = color;
            return;
        }
        halos[t] = CreateHalo(t, color);
    }

    Halo CreateHalo(Transform t, Color color)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(g.GetComponent<Collider>());
        g.name = "PerceptionHalo";
        g.transform.SetParent(t, false);

        // Veľkosť podľa kolidéra postavy
        Vector3 size = new Vector3(1.2f, 2.2f, 1.2f);
        Vector3 center = t.position;
        Collider col = t.GetComponent<Collider>();
        if (col != null)
        {
            size = col.bounds.size + new Vector3(0.3f, 0.25f, 0.3f);
            center = col.bounds.center;
        }

        Vector3 ls = t.lossyScale;
        float sx = Mathf.Approximately(ls.x, 0f) ? 1f : ls.x;
        float sy = Mathf.Approximately(ls.y, 0f) ? 1f : ls.y;
        float sz = Mathf.Approximately(ls.z, 0f) ? 1f : ls.z;

        g.transform.position = center;
        g.transform.localScale = new Vector3(size.x / sx, size.y / (2f * sy), size.z / sz);

        // Priesvitná žiara, ktorá je vidieť aj cez steny (UI/Default s ZTest Always)
        Shader sh = Shader.Find("UI/Default");
        bool seeThrough = sh != null;
        if (sh == null) sh = Shader.Find("Sprites/Default");

        Material m = new Material(sh);
        if (seeThrough) m.SetInt("unity_GUIZTestMode", 8);     // 8 = CompareFunction.Always
        m.renderQueue = 3500;
        m.color = new Color(color.r, color.g, color.b, 0f);
        g.GetComponent<Renderer>().material = m;

        return new Halo { go = g, mat = m, color = color, phase = Random.value * 6.28f };
    }

    void RemoveHalo(Transform key)
    {
        Halo h;
        if (!halos.TryGetValue(key, out h)) return;

        if (h.go != null) Destroy(h.go);
        if (h.mat != null) Destroy(h.mat);
        halos.Remove(key);
    }

    void ClearAll()
    {
        foreach (KeyValuePair<Transform, Halo> kv in halos)
        {
            if (kv.Value.go != null) Destroy(kv.Value.go);
            if (kv.Value.mat != null) Destroy(kv.Value.mat);
        }
        halos.Clear();
    }
}