using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Skúmanie démonovej prírody: pevnejšie stromy (ľudia ich ťažšie klčujú).</summary>
public static class DemonNature
{
    public const int MaxHardness = 3;

    /// <summary>0 = obyčajné stromy, 3 = najpevnejšie (zvyšuje zdravie stromov, spomaľuje klčovanie).</summary>
    public static int Hardness { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState() => Hardness = 0;

    public static bool CanResearch => Hardness < MaxHardness;
    public static void Research() => Hardness = Mathf.Min(MaxHardness, Hardness + 1);
    public static void Restore(int hardness) => Hardness = Mathf.Clamp(hardness, 0, MaxHardness);
}

/// <summary>
/// Strom temnoty, ktorý zasadil hráč-démon. Rastie po dňoch (výhonok → mladý → dospelý → prastarý), jeho korene prorastajú betónové základy:
/// stavby v jeho dosahu sa denne poškodzujú, kým ich ľudia nevyklčujú. Ľudia v zóne ho denne klčujú podľa svojho počtu
/// (pevnejší strom odoláva dlhšie); hráč v ľudskej podobe ho môže vyklčovať aj sám (kláves F). Dospelé stromy tvoria sieť koreňov,
/// ktorou sa démon môže presúvať (DemonNatureMenu).
/// </summary>
public class DemonTree : MonoBehaviour
{
    public static readonly List<DemonTree> All = new List<DemonTree>();

    private static readonly float[] StageRadius = { 2f, 5f, 9f, 14f };
    private static readonly float[] StageRootDamage = { 0f, 10f, 25f, 45f };   // poškodenie stavieb v dosahu za deň
    private static readonly int[] StageDays = { 1, 3, 6 };                     // vek (dni) potrebný na prechod do ďalšieho stupňa
    private const float ClearingPerPerson = 0.8f;
    private const float PlayerChopDamage = 35f;

    [SerializeField] private int stage;
    private bool ancient;
    public bool IsAncient => ancient;
    private int ageDays;
    /// <summary>Za výsadbu tohto stromu hráč dostal odmenu (len tie rastú s odmenou; denný limit proti farmeniu).</summary>
    [System.NonSerialized] public bool rewarded;
    private float health;
    private GameObject visual;
    private PlayerStats player;
    private static Material barkMaterial, crownMaterial, glowMaterial;

    public int Stage => stage;
    public bool IsMature => stage >= 2;
    public float Radius => StageRadius[stage];
    public float MaxHealth => (60f + 80f * stage) * (1f + 0.6f * DemonNature.Hardness);

    /// <summary>Zasadí nový strom (výhonok) na danú pozíciu.</summary>
    public static DemonTree Plant(Vector3 position)
    {
        BreakableNode.GrowNear(position, 5f);   // pne zničených stromov v okolí znova vyrastú
        return Create(position, 0, 0, 1f);
    }

    /// <summary>Prastarý koreňový uzol: dospelý, nezničiteľný a neukladá sa. Umožní démonovi cestovať koreňmi do mesta a divočín.</summary>
    public static DemonTree PlantAncient(Vector3 position)
    {
        DemonTree tree = Create(position, 3, 99, 1f);
        tree.ancient = true;
        return tree;
    }

    static DemonTree Create(Vector3 position, int stage, int ageDays, float healthFraction)
    {
        var go = new GameObject("Strom temnoty");
        go.transform.position = position;

        DemonTree tree = go.AddComponent<DemonTree>();
        tree.stage = Mathf.Clamp(stage, 0, StageRadius.Length - 1);
        tree.ageDays = ageDays;
        tree.health = tree.MaxHealth * Mathf.Clamp01(healthFraction);
        return tree;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        DayNightCycle.OnNewDay += HandleNewDay;
    }

    void OnDisable()
    {
        All.Remove(this);
        DayNightCycle.OnNewDay -= HandleNewDay;
    }

    void Start()
    {
        if (health <= 0f) health = MaxHealth;
        Rebuild();
    }

    void Update()
    {
        PulseGlow();
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            return;
        }

        // Prastarý koreňový uzol slúži len démonovi (cestovanie koreňmi): ako človek ho nevidíš ani nenarazíš
        if (ancient && visual != null && visual.activeSelf != player.IsDemon) visual.SetActive(player.IsDemon);

        // Hráč v ľudskej podobe strom vyklčuje (F pri strome)
        if (ancient) return;
        if (player.IsDemon || player.IsDead || Healer.MenuOpen || !GameInput.GetKeyDown(KeyBindings.Get("use", KeyCode.F))) return;

        Vector3 d = player.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude > 2.5f + stage * 0.4f) return;

        Chop(PlayerChopDamage, true);
    }

    void HandleNewDay(int day)
    {
        if (ancient) return;
        ageDays++;
        if (stage < StageDays.Length && ageDays >= StageDays[stage])
        {
            stage++;
            Rebuild();
            if (player != null && !ancient && rewarded)
                GameHUD.Message("Tvoj strom vyrástol" + DemonNatureMenu.NatureReward(player, 10, 10, 0.3f) + ".", 3f);
            if (stage == 2) GameHUD.Message("Strom temnoty dospel: je súčasťou siete koreňov.", 3f);
        }

        // Korene prorastajú stavby v dosahu
        float rootDamage = StageRootDamage[stage];
        if (rootDamage > 0f)
        {
            foreach (BuildPlot plot in BuildPlot.All)
            {
                if (plot.State != PlotState.Built) continue;

                Vector3 d = plot.transform.position - transform.position;
                d.y = 0f;
                if (d.magnitude <= Radius + plot.Side * 0.5f) plot.TakeDamage(rootDamage, gameObject);
            }
        }

        // Ľudia v zóne ho klčujú (vojak 8, odboj 3, civilista 1 na osobu), pevnejší strom odoláva
        float humans = HumansNearby();
        if (humans > 0f) Chop(humans * ClearingPerPerson / (1f + DemonNature.Hardness), false);
        else health = Mathf.Min(MaxHealth, health + MaxHealth * 0.1f);   // bez ľudí sa regeneruje
    }

    float HumansNearby()
    {
        foreach (ZonePopulation zone in FindObjectsByType<ZonePopulation>())
        {
            if (!zone.Contains(transform.position)) continue;
            return zone.Stock(PopulationGroup.Civilians) + 3f * zone.Stock(PopulationGroup.Rebels) + 8f * zone.Stock(PopulationGroup.Soldiers);
        }
        return 0f;
    }

    /// <summary>Strom poškodila obrana mesta (veža, oheň).</summary>
    public void Burn(float damage) => Chop(damage, false);

    void Chop(float damage, bool byPlayer)
    {
        if (ancient) return;
        health -= damage;
        if (health > 0f)
        {
            if (byPlayer) GameHUD.Message($"Klčuješ strom temnoty ({Mathf.Max(0f, health / MaxHealth) * 100f:0} %).", 1.5f);
            return;
        }

        GameHUD.Message("Strom temnoty bol vyklčovaný.", 3f);
        Destroy(gameObject);
    }

    // ---------- Vzhľad ----------
    static void EnsureMaterials()
    {
        if (barkMaterial != null) return;

        // Tmavofialová kôra, fialová koruna so slabou žiarou, modré svietiace žily; púčiky a runy ružovo-fialové
        barkMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.1f, 0.06f, 0.15f) };
        barkMaterial.SetFloat("_Glossiness", 0.15f);
        crownMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.24f, 0.1f, 0.42f) };
        crownMaterial.SetFloat("_Glossiness", 0.2f);
        crownMaterial.EnableKeyword("_EMISSION");
        crownMaterial.SetColor("_EmissionColor", new Color(0.07f, 0.02f, 0.14f));
        glowMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.2f, 0.6f, 1f) };
        glowMaterial.EnableKeyword("_EMISSION");
        glowMaterial.SetColor("_EmissionColor", GlowColor * 2f);
        budMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.9f, 0.3f, 1f) };
        budMaterial.EnableKeyword("_EMISSION");
        budMaterial.SetColor("_EmissionColor", new Color(0.9f, 0.3f, 1f) * 1.8f);
    }

    private static readonly Color GlowColor = new Color(0.2f, 0.65f, 1f);
    private static Material budMaterial;
    private static int pulseFrame = -1;

    // Žily a púčiky jemne pulzujú (jeden zápis do spoločného materiálu za snímok, nie za strom)
    static void PulseGlow()
    {
        if (glowMaterial == null || pulseFrame == Time.frameCount) return;
        pulseFrame = Time.frameCount;
        float k = 1.6f + 0.7f * Mathf.Sin(Time.time * 1.5f);
        glowMaterial.SetColor("_EmissionColor", GlowColor * k);
        budMaterial.SetColor("_EmissionColor", new Color(0.9f, 0.3f, 1f) * (1.3f + 0.6f * Mathf.Sin(Time.time * 1.1f + 1.3f)));
    }

    GameObject Part(PrimitiveType type, Vector3 position, Vector3 scale, Quaternion rotation, Material material, bool collider = false)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        if (!collider) UnityEngine.Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(visual.transform, false);
        g.transform.localPosition = position;
        g.transform.localRotation = rotation;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = material;
        return g;
    }

    // Pokrútený strom temnoty: zužujúci sa kmeň z častí, korene, konáre so svietiacimi koncami, fialová koruna s púčikmi a modré žily.
    // Prastarý koreňový uzol má navyše kruh svietiacich rún na zemi. Tvar je pre jeden strom stále rovnaký (podľa polohy).
    void Rebuild()
    {
        EnsureMaterials();
        if (visual != null) Destroy(visual);

        visual = new GameObject("Vzhľad");
        visual.transform.SetParent(transform, false);

        var rnd = new System.Random(Mathf.RoundToInt(transform.position.x * 31f + transform.position.z * 17f) ^ 0x2f1d);
        float R() => (float)rnd.NextDouble();

        float height = 1f + 1.6f * stage;
        float width = 0.25f + 0.22f * stage;

        // Kmeň: 1 časť (výhonok) alebo 3 zužujúce sa časti, mierne vychýlené; kolíziu má len spodná časť
        int segments = stage == 0 ? 1 : 3;
        float segHeight = height / segments;
        Vector3 offset = Vector3.zero;
        for (int k = 0; k < segments; k++)
        {
            float w = width * (1f - 0.25f * k);
            Quaternion tilt = Quaternion.Euler((R() - 0.5f) * 9f, 0f, (R() - 0.5f) * 9f);
            Part(PrimitiveType.Cylinder, offset + Vector3.up * segHeight * (k + 0.5f), new Vector3(w, segHeight * 0.55f, w), tilt, barkMaterial, k == 0);
            offset += new Vector3((R() - 0.5f) * 0.14f * width, 0f, (R() - 0.5f) * 0.14f * width);
        }

        // Korene vystupujúce zo zeme
        if (stage > 0)
        {
            int roots = 3 + stage;
            for (int i = 0; i < roots; i++)
            {
                float a = (i + R() * 0.6f) * Mathf.PI * 2f / roots;
                Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 dir = (outward - Vector3.up * 0.4f).normalized;
                float len = width * 2.2f + 0.4f;
                float thick = 0.1f + 0.05f * stage;
                Part(PrimitiveType.Cylinder, outward * width * 0.5f + Vector3.up * 0.12f + dir * len * 0.5f, new Vector3(thick, len * 0.5f, thick),
                    Quaternion.FromToRotation(Vector3.up, dir), barkMaterial);
            }
        }

        // Konáre s modro svietiacimi koncami
        Vector3 crownCenter = offset + Vector3.up * (height + 0.3f + 0.25f * stage);
        if (stage > 0)
        {
            int branches = 2 + stage * 2;
            for (int i = 0; i < branches; i++)
            {
                float a = R() * Mathf.PI * 2f;
                float lean = Mathf.Lerp(35f, 65f, R()) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(lean) * Mathf.Cos(a), Mathf.Cos(lean), Mathf.Sin(lean) * Mathf.Sin(a));
                float len = 0.8f + 0.5f * stage + R() * 0.5f;
                Vector3 start = offset + Vector3.up * height * Mathf.Lerp(0.5f, 0.92f, R());
                float thick = 0.07f + 0.03f * stage;
                Part(PrimitiveType.Cylinder, start + dir * len * 0.5f, new Vector3(thick, len * 0.5f, thick), Quaternion.FromToRotation(Vector3.up, dir), barkMaterial);
                float tip = 0.1f + 0.03f * stage;
                Part(PrimitiveType.Sphere, start + dir * len, Vector3.one * tip * 2f, Quaternion.identity, glowMaterial);
            }
        }

        // Koruna z viacerých splošťených fialových zhlukov
        int blobs = 2 + stage;
        float crownSize = 1.0f + 0.9f * stage;
        for (int i = 0; i < blobs; i++)
        {
            float a = R() * Mathf.PI * 2f;
            float spread = (stage == 0 ? 0.1f : 0.45f + 0.4f * stage) * R();
            float size = crownSize * (0.65f + 0.35f * R());
            Part(PrimitiveType.Sphere, crownCenter + new Vector3(Mathf.Cos(a) * spread, (R() - 0.5f) * 0.4f, Mathf.Sin(a) * spread),
                new Vector3(size, size * 0.62f, size), Quaternion.identity, crownMaterial);
        }

        // Ružovo-fialové púčiky na korune
        int buds = 3 + 3 * stage;
        for (int i = 0; i < buds; i++)
        {
            float u = R() * Mathf.PI * 2f;
            float v = R() * 0.9f;
            float bud = 0.1f + 0.06f * R();
            Part(PrimitiveType.Sphere, crownCenter + new Vector3(Mathf.Cos(u) * Mathf.Sqrt(1f - v * v) * crownSize * 0.5f, v * crownSize * 0.32f, Mathf.Sin(u) * Mathf.Sqrt(1f - v * v) * crownSize * 0.5f),
                Vector3.one * bud * 2f, Quaternion.identity, budMaterial);
        }

        // Modré žily pozdĺž kmeňa (sledujú jeho zúženie)
        int veins = 2 + stage;
        for (int i = 0; i < veins; i++)
        {
            float a = (i + R() * 0.5f) * Mathf.PI * 2f / veins;
            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float r = width * 0.5f * 0.72f;
            float len = height * 0.8f;
            Vector3 axis = (Vector3.up - radial * (width * 0.25f / Mathf.Max(0.5f, height))).normalized;
            float thin = 0.035f + 0.01f * stage;
            Part(PrimitiveType.Cylinder, offset * 0.5f + radial * r + Vector3.up * (len * 0.5f + 0.05f), new Vector3(thin, len * 0.5f, thin),
                Quaternion.FromToRotation(Vector3.up, axis), glowMaterial);
        }

        // Prastarý koreňový uzol: kruh svietiacich rún na zemi, aby bol na prvý pohľad iný než bežný strom
        if (ancient)
        {
            const int runes = 8;
            for (int i = 0; i < runes; i++)
            {
                float a = i * Mathf.PI * 2f / runes;
                Part(PrimitiveType.Cube, new Vector3(Mathf.Cos(a), 0.06f, Mathf.Sin(a)) * 2.7f, new Vector3(0.4f, 0.12f, 0.16f),
                    Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 0f), i % 2 == 0 ? glowMaterial : budMaterial);
            }
        }
    }

    // ---------- Ukladanie ----------
    [Serializable]
    public class SaveData
    {
        public Vector3 position;
        public int stage, ageDays;
        public float healthFraction;
        public bool rewarded;
    }

    public SaveData Export() => new SaveData { position = transform.position, stage = stage, ageDays = ageDays, healthFraction = health / Mathf.Max(1f, MaxHealth), rewarded = rewarded };

    /// <summary>Načíta stromy zo save: existujúce sa odstránia a vytvoria sa uložené.</summary>
    public static void RestoreAll(List<SaveData> saved)
    {
        foreach (DemonTree tree in new List<DemonTree>(All))
            if (!tree.ancient)
            UnityEngine.Object.Destroy(tree.gameObject);

        foreach (SaveData s in saved)
        {
            DemonTree restored = Create(s.position, s.stage, s.ageDays, s.healthFraction);
            if (restored != null) restored.rewarded = s.rewarded;
        }
    }
}
