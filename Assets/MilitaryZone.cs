using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Územie ovládané vojskom. Vojaci strieľajú na všetkých démonov v zóne okrem bossov
/// (nižší a strední dostávajú poškodenie, vyššieho zničia aj napriek nezraniteľnosti).
/// Veľkosť určuje BoxCollider (trigger).
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(ZoneBoundary))]
public class MilitaryZone : MonoBehaviour
{
    [Tooltip("Ako dlho vojaci páli na vyššieho démona, kým ho zničia")]
    public float executionDelay = 1.5f;
    [Tooltip("Poškodenie za sekundu pre nižších a stredných démonov v zóne")]
    public float soldierDamagePerSecond = 30f;
    public float tickInterval = 0.25f;

    static readonly List<MilitaryZone> zones = new List<MilitaryZone>();

    private BoxCollider box;
    private float nextTick;
    private readonly Dictionary<DemonEnemy, float> executeAt = new Dictionary<DemonEnemy, float>();
    private readonly List<DemonEnemy> scratch = new List<DemonEnemy>();

    [Header("Zobrazenie")]
    public Color fillColor = new Color(0.15f, 0.9f, 0.25f, 0.28f);
    public Color borderColor = new Color(0.3f, 1f, 0.4f, 0.85f);
    public float borderWidth = 0.4f;

    private readonly List<GameObject> visuals = new List<GameObject>();

    void Reset()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        b.isTrigger = true;
        b.size = new Vector3(20f, 6f, 20f);
        b.center = new Vector3(0f, 3f, 0f);      // objekt stojí na zemi, box sa tiahne nahor
    }

    public Bounds Area
    {
        get
        {
            if (box == null) box = GetComponent<BoxCollider>();
            return box.bounds;
        }
    }

    public Vector3 ClampInside(Vector3 p, float margin = 0.5f)
    {
        if (box == null) return p;
        Bounds b = box.bounds;
        p.x = Mathf.Clamp(p.x, b.min.x + margin, b.max.x - margin);
        p.z = Mathf.Clamp(p.z, b.min.z + margin, b.max.z - margin);
        return p;
    }

    void Start() => BuildVisual();

    void OnDestroy()
    {
        foreach (GameObject g in visuals)
            if (g != null) Destroy(g);
    }

    // Zelený obdĺžnik na zemi s jasným okrajom
    void BuildVisual()
    {
        if (box == null) box = GetComponent<BoxCollider>();

        Bounds b = box.bounds;
        float y = b.min.y + 0.05f;
        if (Physics.Raycast(b.center, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
            y = hit.point.y + 0.05f;

        Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
        Vector3 c = transform.TransformPoint(box.center);
        float yaw = transform.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(90f, yaw, 0f);
        Quaternion flat = Quaternion.Euler(0f, yaw, 0f);

        MakeQuad(new Vector3(c.x, y, c.z), rot, new Vector3(size.x, size.z, 1f), fillColor);

        // Okraj: štyri tenké pásy
        float hx = size.x * 0.5f, hz = size.z * 0.5f, w = borderWidth;
        MakeQuad(new Vector3(c.x, y + 0.01f, c.z) + flat * new Vector3(0f, 0f, hz), rot, new Vector3(size.x, w, 1f), borderColor);
        MakeQuad(new Vector3(c.x, y + 0.01f, c.z) + flat * new Vector3(0f, 0f, -hz), rot, new Vector3(size.x, w, 1f), borderColor);
        MakeQuad(new Vector3(c.x, y + 0.01f, c.z) + flat * new Vector3(hx, 0f, 0f), rot, new Vector3(w, size.z, 1f), borderColor);
        MakeQuad(new Vector3(c.x, y + 0.01f, c.z) + flat * new Vector3(-hx, 0f, 0f), rot, new Vector3(w, size.z, 1f), borderColor);
    }

    void MakeQuad(Vector3 pos, Quaternion rot, Vector3 scale, Color color)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "MilitaryZoneVisual";
        q.transform.SetPositionAndRotation(pos, rot);
        q.transform.localScale = scale;

        Renderer r = q.GetComponent<Renderer>();
        Shader sh = Shader.Find("Sprites/Default");     // nesvieti, podporuje priehľadnosť
        if (sh != null) r.material = new Material(sh);
        r.material.color = color;

        visuals.Add(q);
    }

    void OnEnable()
    {
        box = GetComponent<BoxCollider>();
        zones.Add(this);
    }

    void OnDisable() => zones.Remove(this);

    /// <summary>Vojenská zóna na danom mieste (len podľa X a Z), alebo null.</summary>
    public static MilitaryZone At(Vector3 p)
    {
        foreach (MilitaryZone z in zones)
        {
            if (z == null || z.box == null) continue;
            Bounds b = z.box.bounds;
            if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z)
                return z;
        }
        return null;
    }

    /// <summary>Je bod vnútri zóny? (X, Z podľa boxu, výška s rezervou)</summary>
    public bool Contains(Vector3 p)
    {
        if (box == null) return false;
        Bounds b = box.bounds;
        return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z
            && p.y >= b.min.y - 2f && p.y <= b.max.y + 3f;
    }

    // Vojaci strieľajú na všetkých démonov v zóne okrem bossov
    void Update()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + tickInterval;

        scratch.Clear();
        scratch.AddRange(DemonEnemy.All);

        foreach (DemonEnemy e in scratch)
        {
            if (e == null || e.Damage == null || e.Damage.IsDead)
            {
                if (e != null) executeAt.Remove(e);
                continue;
            }

            // Bossovia, vodcovia a Arakani sú mimo dosahu vojakov; maskovaných démonov nikto nepozná
            if (e.IsBossClass || !e.IsVisibleThreat || !Contains(e.transform.position))
            {
                executeAt.Remove(e);
                continue;
            }

            FireAt(e.transform.position);

            if (e.Tier >= DemonTier.Higher)
            {
                // Vyšší démon je nezraniteľný - vojaci ho zničia po chvíli sústredenej paľby
                float t;
                if (!executeAt.TryGetValue(e, out t))
                {
                    t = Time.time + executionDelay;
                    executeAt[e] = t;
                    GameHUD.Message("Vojaci páli na vyššieho démona!");
                }

                if (Time.time >= t)
                {
                    executeAt.Remove(e);
                    e.Damage.ForceKill();
                    GameHUD.Message("Vojaci zničili vyššieho démona.");
                }
            }
            else
            {
                e.Damage.TakeDamage(soldierDamagePerSecond * tickInterval, null);
            }
        }
    }

    /// <summary>Vizuálna paľba vojakov na cieľ (lúč z okraja zóny).</summary>
    public void FireAt(Vector3 target)
    {
        if (box == null) return;

        Bounds b = box.bounds;
        Vector3 from = new Vector3(
            b.center.x + Random.Range(-1f, 1f) * b.extents.x,
            b.max.y,
            b.center.z + Random.Range(-1f, 1f) * b.extents.z);

        Vfx.Beam(from, target + Vector3.up, new Color(1f, 0.6f, 0.2f), 0.12f, 0.08f);
    }

    void OnDrawGizmos()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        if (b == null) return;

        Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(b.center, b.size);
    }
}