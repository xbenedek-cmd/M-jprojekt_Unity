using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Po zabití človeka (civilistu, vojaka) zostane pri mŕtvole kaluž krvi a hráč za sebou ďalších ~24 m zanecháva krvavé odtlačky topánok
/// (striedavo ľavá a pravá, čoraz slabšie). Vojaci stopy vidia (dosah, zorný kužeľ, výhľad) a idú po nich k hráčovi.
/// Odtlačky aj kaluže sú v poole (bez vytvárania za behu), po čase blednú a miznú.
/// </summary>
public class PlayerBloodTrail : MonoBehaviour
{
    private const int MaxDrops = 40;
    private const int MaxPools = 6;
    private const float TrailLength = 24f;     // koľko metrov po zabití sa odtlačky dropujú
    private const float StepDistance = 0.8f;   // krok (vzdialenosť medzi odtlačkami)
    private const float VisibleSeconds = 90f;  // ako dlho odtlačok leží na zemi
    private const float PoolSeconds = 140f;
    private const float FadeSeconds = 25f;     // koľko sekúnd pred zmiznutím bledne
    private const float ClueSeconds = 30f;     // ako dlho ju vojaci berú ako stopu

    private struct Drop { public Vector3 position, forward, normal; public float time, strength; public int order; public bool left; }
    private struct Pool { public Vector3 position, normal; public float time, size, rotation; }

    private static readonly Drop[] drops = new Drop[MaxDrops];
    private static int next;
    private static int orderCounter;

    private readonly Pool[] pools = new Pool[MaxPools];
    private int nextPool;
    private Transform[] printObjects, poolObjects;
    private Renderer[] printRenderers, poolRenderers;
    private MaterialPropertyBlock block;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private PlayerStats player;
    private float remaining;
    private Vector3 lastPos;
    private bool hasLast, leftFoot;
    private float nextCorpseScan;
    private readonly HashSet<Damageable> seen = new HashSet<Damageable>();
    private bool primed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        for (int i = 0; i < MaxDrops; i++) drops[i].time = -9999f;
        next = 0;
        orderCounter = 0;
        if (FindAnyObjectByType<PlayerBloodTrail>() != null) return;
        new GameObject("PlayerBloodTrail").AddComponent<PlayerBloodTrail>();
    }

    // ---------- Vzhľad: textúry sa kreslia kódom (bez súborov) ----------
    static Texture2D MakeFootprint()
    {
        const int w = 32, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f - w * 0.5f) / (w * 0.5f), v = (y + 0.5f) / h;   // v = 0 päta, 1 špička
                float sole = Ellipse(u, v, 0f, 0.68f, 0.62f, 0.31f);   // chodidlo
                float heel = Ellipse(u, v, 0f, 0.17f, 0.45f, 0.15f);   // päta
                float a = Mathf.Max(sole, heel);
                a *= 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.4f, y * 0.4f);   // nerovný okraj
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static Texture2D MakePool()
    {
        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s * 2f - 1f, v = (y + 0.5f) / s * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u);
                float edge = 0.68f + 0.22f * Mathf.PerlinNoise(Mathf.Cos(ang) * 1.6f + 3f, Mathf.Sin(ang) * 1.6f + 3f);   // nepravidelný okraj
                float a = Mathf.Clamp01((edge - r) / 0.12f);
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    // Elipsa s mäkkým okrajom: au = polšírka (v jednotkách u), av = polovičná dĺžka (v jednotkách v)
    static float Ellipse(float u, float v, float cu, float cv, float au, float av)
    {
        float du = (u - cu) / au, dv = (v - cv) / av;
        float d = Mathf.Sqrt(du * du + dv * dv);
        return Mathf.Clamp01((1f - d) / 0.22f);
    }

    Transform MakeDecal(string name, Texture2D tex, Vector3 scale, out Renderer r)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(g.GetComponent<Collider>());
        g.name = name;
        g.transform.SetParent(transform, false);
        g.transform.localScale = scale;
        r = g.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        var m = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        r.sharedMaterial = m;
        g.SetActive(false);
        return g.transform;
    }

    void Start()
    {
        block = new MaterialPropertyBlock();
        Texture2D foot = MakeFootprint(), pool = MakePool();
        printObjects = new Transform[MaxDrops]; printRenderers = new Renderer[MaxDrops];
        poolObjects = new Transform[MaxPools]; poolRenderers = new Renderer[MaxPools];
        for (int i = 0; i < MaxDrops; i++) printObjects[i] = MakeDecal("KrvavyOdtlacok", foot, new Vector3(0.3f, 0.38f, 1f), out printRenderers[i]);
        for (int i = 0; i < MaxPools; i++) poolObjects[i] = MakeDecal("KalouzKrvi", pool, Vector3.one, out poolRenderers[i]);
        for (int i = 0; i < MaxPools; i++) pools[i].time = -9999f;
    }

    void Update()
    {
        if (printObjects == null) return;
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            return;
        }

        if (Time.time >= nextCorpseScan)
        {
            nextCorpseScan = Time.time + 0.25f;
            ScanKills();
            RefreshVisuals();
        }

        if (remaining <= 0f || player.IsDead) return;

        Vector3 p = player.transform.position;
        if (!hasLast) { lastPos = p; hasLast = true; }
        Vector3 delta = p - lastPos;
        delta.y = 0f;
        if (delta.sqrMagnitude < StepDistance * StepDistance) return;

        Vector3 forward = delta.normalized;
        remaining -= StepDistance;
        lastPos = p;
        leftFoot = !leftFoot;

        Vector3 ground = p, normal = Vector3.up;
        GroundAt(p, ref ground, ref normal);
        Vector3 side = Vector3.Cross(Vector3.up, forward);
        ground += side * (leftFoot ? -0.11f : 0.11f);   // chodidlá vedľa osi chôdze

        drops[next] = new Drop
        {
            position = ground, forward = forward, normal = normal, time = Time.time, order = ++orderCounter, left = leftFoot,
            strength = 0.3f + 0.65f * Mathf.Clamp01(remaining / TrailLength)   // čím ďalej od mŕtvoly, tým slabší odtlačok
        };
        next = (next + 1) % MaxDrops;
    }

    // Nová mŕtvola človeka, ktorú zabil hráč: kaluž krvi a začiatok stopy
    void ScanKills()
    {
        foreach (Damageable corpse in Damageable.AllCorpses)
        {
            if (corpse == null || !seen.Add(corpse)) continue;
            if (!primed) continue;   // mŕtvoly z minulosti (načítaná hra) stopu nespúšťajú
            if (!corpse.KilledByPlayer || corpse.GetComponent<DemonEnemy>() != null) continue;

            remaining = TrailLength;
            hasLast = false;

            Vector3 pos = corpse.transform.position, normal = Vector3.up;
            GroundAt(pos, ref pos, ref normal);
            pools[nextPool] = new Pool { position = pos, normal = normal, time = Time.time, size = Random.Range(1.3f, 1.9f), rotation = Random.Range(0f, 360f) };
            nextPool = (nextPool + 1) % MaxPools;
        }
        primed = true;
        seen.RemoveWhere(IsGone);
    }

    static bool IsGone(Damageable d) => d == null;

    private static readonly RaycastHit[] groundHits = new RaycastHit[12];

    /// <summary>Zem pod bodom: preskočí postavy (hráč, mŕtvola, NPC), aby odtlačok neostal vo výške hlavy.</summary>
    static void GroundAt(Vector3 p, ref Vector3 ground, ref Vector3 normal)
    {
        int n = Physics.RaycastNonAlloc(p + Vector3.up * 2f, Vector3.down, groundHits, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Collider c = groundHits[i].collider;
            if (c.attachedRigidbody != null || c.GetComponentInParent<Damageable>() != null || c.GetComponentInParent<PlayerStats>() != null) continue;
            if (groundHits[i].distance < best) { best = groundHits[i].distance; ground = groundHits[i].point; normal = groundHits[i].normal; }
        }
    }

    static float Alpha(float age, float lifetime) => Mathf.Clamp01((lifetime - age) / FadeSeconds);

    void RefreshVisuals()
    {
        for (int i = 0; i < MaxDrops; i++)
        {
            float age = Time.time - drops[i].time;
            bool on = age < VisibleSeconds;
            if (printObjects[i].gameObject.activeSelf != on) printObjects[i].gameObject.SetActive(on);
            if (!on) continue;

            Transform t = printObjects[i];
            t.position = drops[i].position + drops[i].normal * 0.03f;
            Vector3 up = Vector3.ProjectOnPlane(drops[i].forward, drops[i].normal);
            if (up.sqrMagnitude < 0.0001f) up = drops[i].forward;
            t.rotation = Quaternion.LookRotation(-drops[i].normal, up);
            // ľavé chodidlo je zrkadlovo otočené, aby odtlačky netvorili dvakrát tú istú nohu
            Vector3 ls = t.localScale; ls.x = drops[i].left ? -Mathf.Abs(ls.x) : Mathf.Abs(ls.x); t.localScale = ls;   // (zrkadlenie ľavého chodidla)

            block.SetColor(ColorId, new Color(0.5f, 0.02f, 0.03f, drops[i].strength * Alpha(age, VisibleSeconds)));
            printRenderers[i].SetPropertyBlock(block);
        }

        for (int i = 0; i < MaxPools; i++)
        {
            float age = Time.time - pools[i].time;
            bool on = age < PoolSeconds;
            if (poolObjects[i].gameObject.activeSelf != on) poolObjects[i].gameObject.SetActive(on);
            if (!on) continue;

            Transform t = poolObjects[i];
            float grow = Mathf.SmoothStep(0.25f, 1f, Mathf.Clamp01(age / 4f));   // kaluž sa rozlieva
            t.position = pools[i].position + pools[i].normal * 0.025f;
            t.rotation = Quaternion.LookRotation(-pools[i].normal, Quaternion.AngleAxis(pools[i].rotation, pools[i].normal) * Vector3.ProjectOnPlane(Vector3.forward, pools[i].normal).normalized);
            t.localScale = new Vector3(pools[i].size * grow, pools[i].size * grow, 1f);

            block.SetColor(ColorId, new Color(0.38f, 0.01f, 0.02f, 0.9f * Alpha(age, PoolSeconds)));
            poolRenderers[i].SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// Najnovšia viditeľná stopa pre pozorovateľa (vojaka). Novšia stopa = bližšie k hráčovi, takže ju vojak sleduje k cieľu.
    /// </summary>
    public static bool TryFindClue(Transform watcher, float range, float fov, out Vector3 clue)
    {
        clue = default;
        int bestOrder = -1;

        for (int i = 0; i < MaxDrops; i++)
        {
            if (Time.time - drops[i].time > ClueSeconds || drops[i].order <= bestOrder) continue;
            if (!StealthWorld.CanSeePoint(watcher, drops[i].position, range, fov)) continue;

            bestOrder = drops[i].order;
            clue = drops[i].position;
        }
        return bestOrder >= 0;
    }
}
