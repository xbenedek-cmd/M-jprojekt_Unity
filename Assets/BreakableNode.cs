using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zničiteľný objekt v Divočine: strom (sekera), skala (krompáč) alebo železný blok / vrak (kladivo). Úderom nástroja (ToolKit.TryUse)
/// sa poškodzuje; po zničení dá 3–10 surovín podľa pevnosti (silnejší = viac) a po čase sa obnoví.
/// Strom vyrastie po 25 min sám, po 10 min ak pri pňovi zostal démon, a hneď, keď démon (hráč) pri ňom zasadí strom temnoty (DemonTree.Plant).
/// </summary>
public class BreakableNode : MonoBehaviour
{
    public static readonly List<BreakableNode> All = new List<BreakableNode>();
    public static BreakableNode Last { get; private set; }
    public static float LastHitAt { get; private set; }

    public float Health => health;
    public float MaxHealth => maxHealth;

    [SerializeField] private ToolKit.Kind requires = ToolKit.Kind.Axe;
    [SerializeField] private string dropItem = ItemCatalog.Wood;
    [SerializeField] private string label = "Strom";
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private bool isTree = true;
    [SerializeField] private int minTier = 0;   // najslabší použiteľný typ nástroja (0 drevený, 1 železný, 2 oceľový, 3 bleskotvorný)

    private const float DemonRegrowSeconds = 600f;
    private const float NaturalTreeSeconds = 1500f;
    private const float RockRegrowSeconds = 1800f;
    private const float DemonNearRadius = 30f;

    public ToolKit.Kind Requires => requires;
    public int MinTier => minTier;
    public string Label => label;

    private float health;
    private int dropMin, dropMax;
    private Renderer[] renderers;
    private Collider[] colliders;
    private Quaternion restRotation;
    private float fallStart = -1f;
    private Vector3 fallAxis;
    private float felledAt;
    private float nextDemonCheck;
    private bool down;

    /// <summary>Nastaví druh, odmenu a pevnosť. Počet surovín 3–10 rastie s pevnosťou (hp 40 → ~3, hp 300 → ~10).</summary>
    public void Configure(ToolKit.Kind tool, string item, string name, float hp, bool tree, int minimumTier = 0)
    {
        minTier = minimumTier;
        requires = tool;
        dropItem = item;
        label = name;
        maxHealth = hp;
        isTree = tree;
        health = hp;
        ComputeDrops();
    }

    void ComputeDrops()
    {
        float strength = Mathf.InverseLerp(40f, 300f, maxHealth);
        float basis = Mathf.Lerp(3f, 10f, strength);
        dropMin = Mathf.Clamp(Mathf.RoundToInt(basis - 1f), 3, 10);
        dropMax = Mathf.Clamp(Mathf.RoundToInt(basis + 1f), dropMin, 10);
    }

    void Awake()
    {
        resource = GetComponent<ResourceNode>();
        stump = transform.Find("Pen");
        if (stump != null) stump.gameObject.SetActive(false);
        renderers = CollectRenderers();
        colliders = CollectColliders();
        restRotation = transform.rotation;
        health = maxHealth;
        ComputeDrops();
    }

    /// <summary>Po pridaní kolízie za behu (RockGuard) sa zoznam kolízií načíta znova.</summary>
    public void RefreshColliders() => colliders = CollectColliders();

    public bool IsTree => isTree;
    public string DropItem => dropItem;

    /// <summary>Zber zberačom osady (bez hráča): objekt padne / sa rozpadne a vráti množstvo surovín; 0, ak už je zničený.</summary>
    public int AutoHarvest()
    {
        if (down) return 0;

        Vector3 away = Random.insideUnitSphere;
        away.y = 0f;
        away = away.sqrMagnitude < 0.01f ? Vector3.forward : away.normalized;
        fallAxis = Vector3.Cross(Vector3.up, away).normalized;
        health = 0f;
        fallStart = Time.time;
        down = true;
        felledAt = Time.time;
        nextDemonCheck = Time.time + DemonRegrowSeconds;
        if (resource != null) resource.Deplete();
        return Mathf.Max(1, Mathf.CeilToInt(Random.Range(dropMin, dropMax + 1) * SoulBalance.GatherFactor));
    }

    // Peň (potomok "Pen") sa ukáže po zrúbaní stromu; jeho vykresľovače a kolízie nepatria stromu
    private Transform stump;
    private ResourceNode resource;   // kopa kameňov: zbieranie (F) aj ničenie krompáčom naraz

    Renderer[] CollectRenderers()
    {
        var list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (stump == null || !r.transform.IsChildOf(stump)) list.Add(r);
        return list.ToArray();
    }

    Collider[] CollectColliders()
    {
        var list = new List<Collider>();
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (stump == null || !c.transform.IsChildOf(stump)) list.Add(c);
        return list.ToArray();
    }

    // Diagnostika (RockDebug, kláves F8)
    public bool IsDown => down;
    public int ColliderCount => colliders != null ? colliders.Length : -1;
    public float DistanceTo(Vector3 position) => DistanceFrom(position);

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // Vodorovná vzdialenosť k najbližšej kolízii objektu (kmeň / kameň), nie ku stredu: veľké skaly majú stred ďaleko
    float DistanceFrom(Vector3 position) => DistanceFrom(position, out _);

    // closest = najbližší bod kolízie (podľa neho sa kontroluje smer úderu: stred skupiny môže ležať ďaleko od modelu)
    float DistanceFrom(Vector3 position, out Vector3 closest)
    {
        float best = float.MaxValue;
        closest = transform.position;
        foreach (Collider c in colliders)
        {
            if (c == null || !c.enabled) continue;

            Vector3 p;
            if (c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex)) p = c.ClosestPoint(position);
            else p = c.bounds.ClosestPoint(position);

            Vector3 d = p - position;
            d.y = 0f;
            if (d.magnitude < best) { best = d.magnitude; closest = p; }
        }
        return best == float.MaxValue ? (transform.position - position).magnitude : best;
    }

    public static BreakableNode Nearest(Vector3 position, Vector3 aim, float reach)
    {
        BreakableNode best = null;
        float bestDistance = reach;

        for (int i = 0; i < All.Count; i++)
        {
            BreakableNode n = All[i];
            if (n == null || n.down || (n.resource != null && n.resource.IsDepleted)) continue;

            float distance = n.DistanceFrom(position, out Vector3 closest);
            if (distance > bestDistance) continue;

            Vector3 to = closest - position;
            to.y = 0f;
            if (distance > 0.6f && to.sqrMagnitude > 1f && Vector3.Angle(aim, to) > 80f) continue;

            best = n;
            bestDistance = distance;
        }
        return best;
    }

    public void Hit(float damage, Transform by)
    {
        if (down || damage <= 0f) return;

        health -= damage;
        Last = this;           // ukazovateľ zostávajúceho života nad cieľom (BreakableHud)
        LastHitAt = Time.time;
        GameAudio.Play(GameAudio.Sfx.SwordHit, transform.position);
        if (health > 0f) return;

        // Strom padá od hráča; skala a blok sa len rozpadnú
        Vector3 away = transform.position - by.position;
        away.y = 0f;
        away = away.sqrMagnitude < 0.01f ? transform.forward : away.normalized;
        fallAxis = Vector3.Cross(Vector3.up, away).normalized;
        fallStart = Time.time;
        down = true;
        felledAt = Time.time;
        if (resource != null) resource.Deplete();
        nextDemonCheck = Time.time + DemonRegrowSeconds;

        ItemDef def = ItemCatalog.Get(dropItem);
        Inventory inventory = by.GetComponent<Inventory>();
        int amount = Mathf.CeilToInt(Random.Range(dropMin, dropMax + 1) * SoulBalance.GatherFactor);
        if (dropItem != ItemCatalog.Gold && dropItem != ItemCatalog.BlackCrystal && Random.value < SkillTree.Sum(SkillStat.RareLoot)) amount *= 2;   // Kovkop: dvojnásobný úlovok všetkých surovín okrem zlata a čierneho kryštálu
        string what = def != null ? def.displayName : dropItem;
        if (inventory != null && inventory.TryAdd(dropItem, amount)) GameHUD.Message($"{label} zničený: {what} x{amount}", 2.5f);
        else GameHUD.Message($"{label} zničený (inventár je plný, {what} prepadlo).", 2.5f);
    }

    /// <summary>Démon zasadil strom: pne v okolí vyrastú hneď.</summary>
    public static void GrowNear(Vector3 position, float radius)
    {
        for (int i = 0; i < All.Count; i++)
        {
            BreakableNode n = All[i];
            if (n != null && n.down && n.isTree && (n.transform.position - position).sqrMagnitude <= radius * radius) n.Regrow();
        }
    }

    bool DemonNearby()
    {
        for (int i = 0; i < DemonEnemy.All.Count; i++)
        {
            DemonEnemy d = DemonEnemy.All[i];
            if (d == null || d.Damage == null || d.Damage.IsDead) continue;
            if ((d.transform.position - transform.position).sqrMagnitude <= DemonNearRadius * DemonNearRadius) return true;
        }
        return false;
    }

    void Regrow()
    {
        down = false;
        fallStart = -1f;
        health = maxHealth;
        transform.rotation = restRotation;
        SetVisible(true);
        if (stump != null) stump.gameObject.SetActive(false);
    }

    void Update()
    {
        if (down)
        {
            if (fallStart >= 0f)
            {
                // Strom sa prevalí, ostatné zmiznú hneď
                float u = isTree ? (Time.time - fallStart) / 0.8f : 1f;
                if (isTree) transform.rotation = Quaternion.AngleAxis(Mathf.Lerp(0f, 88f, u * u), fallAxis) * restRotation;
                if (u >= 1f)
                {
                    fallStart = -1f;
                    if (isTree) transform.rotation = restRotation;   // peň stojí rovno, nie naklonený po páde
                    if (stump != null) stump.gameObject.SetActive(true);
                    SetVisible(false);
                }
                return;
            }

            float elapsed = Time.time - felledAt;
            if (!isTree) { if (elapsed >= RockRegrowSeconds) Regrow(); return; }

            if (elapsed >= NaturalTreeSeconds) Regrow();
            else if (elapsed >= DemonRegrowSeconds && Time.time >= nextDemonCheck)
            {
                nextDemonCheck = Time.time + 5f;
                if (DemonNearby()) Regrow();   // démon pri pni ho "vyživuje"
            }
            return;
        }
    }

    void SetVisible(bool value)
    {
        foreach (Light l in GetComponentsInChildren<Light>(true)) l.enabled = value;   // žiara rudy v bani zhasne s ňou
        foreach (Renderer r in renderers) if (r != null) r.enabled = value;
        foreach (Collider c in colliders) if (c != null) c.enabled = value;
    }
}

/// <summary>
/// Pri údere do stromu, skaly alebo bloku sa nad ním na chvíľu ukáže zostávajúci život: "x / y" (x = koľko poškodenia ešte treba,
/// y = celková pevnosť) a pásik. Zmizne 2,5 s po poslednom údere. Vytvára sa samo pri štarte hry.
/// </summary>
public class BreakableHud : MonoBehaviour
{
    private const float ShowSeconds = 2.5f;
    private GUIStyle style;
    private BreakableNode cachedNode;
    private Vector3 cachedTop;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<BreakableHud>() != null) return;
        new GameObject("BreakableHud").AddComponent<BreakableHud>();
    }

    void OnGUI()
    {
        BreakableNode node = BreakableNode.Last;
        if (PauseMenu.AtStartup || node == null || Time.time - BreakableNode.LastHitAt > ShowSeconds) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // Bod nad objektom: počíta sa raz pre objekt (nie každý snímok). Vysoký strom alebo skupina má horný okraj mimo obrazovky,
        // preto sa pásik vždy pritlačí do viditeľnej oblasti (nikdy nezmizne za okrajom).
        if (node != cachedNode)
        {
            cachedNode = node;
            cachedTop = node.transform.position + Vector3.up * 2f;
            Renderer[] renderers = node.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
                float height = Mathf.Min(b.max.y - b.min.y, 12f);
                cachedTop = new Vector3(b.center.x, b.min.y + height + 0.4f, b.center.z);
            }
        }

        Vector3 sp = cam.WorldToScreenPoint(cachedTop);
        float x = sp.z > 0f ? Mathf.Clamp(sp.x, 100f, Screen.width - 100f) : Screen.width * 0.5f;
        float y = sp.z > 0f ? Mathf.Clamp(Screen.height - sp.y, 70f, Screen.height - 120f) : Screen.height * 0.3f;

        if (style == null)
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };

        float fade = Mathf.Clamp01((ShowSeconds - (Time.time - BreakableNode.LastHitAt)) / 0.6f);
        float fill = node.MaxHealth > 0f ? Mathf.Clamp01(node.Health / node.MaxHealth) : 0f;

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f * fade);
        GUI.DrawTexture(new Rect(x - 62f, y - 4f, 124f, 10f), Texture2D.whiteTexture);
        GUI.color = new Color(0.9f, 0.7f, 0.25f, fade);
        GUI.DrawTexture(new Rect(x - 60f, y - 2f, 120f * fill, 6f), Texture2D.whiteTexture);

        style.normal.textColor = new Color(1f, 1f, 1f, fade);
        GUI.Label(new Rect(x - 90f, y - 28f, 180f, 22f), $"{node.Label}  {Mathf.CeilToInt(Mathf.Max(0f, node.Health))} / {Mathf.CeilToInt(node.MaxHealth)}", style);
        GUI.color = prev;
    }
}
