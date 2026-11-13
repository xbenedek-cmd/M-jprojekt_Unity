using System;
using System.Collections.Generic;
using UnityEngine;

public enum Mutation { Claws, Hide, Muscles, Hunger, Convert, Enslave, Totems, BoneArmor, BloodRegen, PackLord, SwiftConvert, Shroud, Predator, AncientBlood }

/// <summary>
/// Demonické mechaniky a skrýša: mutačný strom (od démonickej úrovne 10), premieňanie ľudí, zotročenie zvierat,
/// totemy územia a tajná skrýša s maskovacou dielňou (parfum, falošné doklady, uniforma). Stav je statický a ukladá sa s osadou.
/// </summary>
public static class Lair
{
    public const int MinLevel = 10;

    public static readonly string[] Names =
    {
        "Pazúry", "Hrubá koža", "Rýchle svaly", "Krvavý hltač", "Premena ľudí", "Zotročenie príšer", "Totemy územia",
        "Kostený pancier", "Krvavá regenerácia", "Pán svorky", "Rýchla premena", "Temný plášť", "Nočný lovec", "Prastará krv"
    };
    public static readonly string[] Descriptions =
    {
        "Poškodenie démona +15 %.",
        "Prijaté poškodenie −15 %.",
        "Rýchlosť démona +10 %.",
        "Krv ubúda o 30 % pomalšie. (vyžaduje aspoň jednu mutáciu zo Stupňa 1)",
        "Premeň civilistu do 4 m na nižšieho démona-služobníka. Stojí 30 krvi a 3 ľudskosti. (vyžaduje Stupeň 1)",
        "Zotroč zviera (vlk, krkavec) do 8 m: chodí s tebou a útočí na nepriateľských démonov. Stojí 20 krvi. (vyžaduje Stupeň 1)",
        "Postav totem, ktorý v okruhu 25 m lieči a dopĺňa krv. Priťahuje Chytačov príšer. (vyžaduje Stupeň 1)",
        "Prijaté poškodenie ďalších −15 %. (Stupeň 3: vyžaduje 2 mutácie zo Stupňa 2)",
        "Pri krvi nad 15 sa liečiš 1,2 zdravia za sekundu. (Stupeň 3)",
        "Zotročených zvierat môžeš mať 5 a totemov 4. (Stupeň 3)",
        "Premena ľudí stojí len 15 krvi a dosiahne na 6 m. (Stupeň 3; vyžaduje Premenu ľudí)",
        "Podozrenie rastie o 20 % pomalšie a parfum vydrží o polovicu dlhšie. (Stupeň 4: vyžaduje 2 mutácie zo Stupňa 3)",
        "Rýchlosť +10 % a krv ubúda o 15 % pomalšie. (Stupeň 4)",
        "Poškodenie +20 %, prijaté poškodenie −10 %. Kto ju má, má v sebe kus Arakaniho. (Stupeň 4)"
    };
    static readonly int[] Costs = { 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4 };

    static int owned;
    public static int InfiltratorsKilled;

    // ---------- Mutácie ----------
    public static bool Has(Mutation m) => (owned & (1 << (int)m)) != 0;
    public static int Cost(Mutation m) => Costs[(int)m];

    public static int PointsTotal(PlayerStats p) => p == null ? 0 : Mathf.Max(0, p.demonLevel - MinLevel + 1);

    public static int Spent
    {
        get
        {
            int sum = 0;
            for (int i = 0; i < Costs.Length; i++) if ((owned & (1 << i)) != 0) sum += Costs[i];
            return sum;
        }
    }

    public static int PointsFree(PlayerStats p) => PointsTotal(p) - Spent;

    static bool HasTierOne => Has(Mutation.Claws) || Has(Mutation.Hide) || Has(Mutation.Muscles);

    static int OwnedIn(int from, int to)
    {
        int n = 0;
        for (int i = from; i < to; i++) if ((owned & (1 << i)) != 0) n++;
        return n;
    }

    /// <summary>Prečo sa mutácia nedá kúpiť (null = dá sa).</summary>
    public static string Blocked(Mutation m, PlayerStats p)
    {
        if (Has(m)) return "získaná";
        if (p == null || p.demonLevel < MinLevel) return $"od démonickej úrovne {MinLevel}";
        int index = (int)m;
        if (index >= 3 && index < 7 && !HasTierOne) return "najprv Stupeň 1";
        if (index >= 7 && index < 11 && OwnedIn(3, 7) < 2) return "najprv 2 mutácie zo Stupňa 2";
        if (index >= 11 && OwnedIn(7, 11) < 2) return "najprv 2 mutácie zo Stupňa 3";
        if (m == Mutation.SwiftConvert && !Has(Mutation.Convert)) return "najprv Premena ľudí";
        if (PointsFree(p) < Costs[(int)m]) return "málo bodov";
        return null;
    }

    public static bool Buy(Mutation m, PlayerStats p)
    {
        if (Blocked(m, p) != null) return false;
        owned |= 1 << (int)m;
        return true;
    }

    // Faktory platia len v podobe démona (volajúci to ošetrí)
    public static float DamageFactor => (Has(Mutation.Claws) ? 1.15f : 1f) * (Has(Mutation.AncientBlood) ? 1.2f : 1f);
    public static float TakenFactor => (Has(Mutation.Hide) ? 0.85f : 1f) * (Has(Mutation.BoneArmor) ? 0.85f : 1f) * (Has(Mutation.AncientBlood) ? 0.9f : 1f);
    public static float SpeedFactor => (Has(Mutation.Muscles) ? 1.1f : 1f) * (Has(Mutation.Predator) ? 1.1f : 1f);
    public static float BloodDrainFactor => (Has(Mutation.Hunger) ? 0.7f : 1f) * (Has(Mutation.Predator) ? 0.85f : 1f);

    // ---------- Skrýša a maskovanie ----------
    public static bool HideoutBuilt;
    public static Vector3 HideoutPos;
    public static float HideoutYaw;
    public static GameObject HideoutObject;
    static float perfumeUntil, docsUntil, uniformUntil;
    internal static bool NeedRebuild;

    public static bool Perfumed => Time.time < perfumeUntil;
    public static bool DocsActive => Time.time < docsUntil;
    public static bool UniformActive => Time.time < uniformUntil;
    public static bool Disguised => DocsActive || UniformActive;
    public static float SuspicionFactor => (UniformActive ? 0.4f : 1f) * (Has(Mutation.Shroud) ? 0.8f : 1f);

    public static float PerfumeLeft => Mathf.Max(0f, perfumeUntil - Time.time);
    public static float DocsLeft => Mathf.Max(0f, docsUntil - Time.time);
    public static float UniformLeft => Mathf.Max(0f, uniformUntil - Time.time);

    public const float PerfumeSeconds = 360f, DocsSeconds = 240f, UniformSeconds = 480f;

    public static void ActivatePerfume() => perfumeUntil = Time.time + PerfumeSeconds * (Has(Mutation.Shroud) ? 1.5f : 1f);
    public static void ActivateDocs() => docsUntil = Time.time + DocsSeconds;
    public static void ActivateUniform() => uniformUntil = Time.time + UniformSeconds;

    public static bool NearHideout(Vector3 position, float range = 14f) =>
        HideoutBuilt && (position - HideoutPos).sqrMagnitude <= range * range;

    // ---------- Totemy a zvieratá ----------
    public static readonly List<Totem> Totems = new List<Totem>();
    public static readonly List<TamedBeast> Beasts = new List<TamedBeast>();
    static readonly List<Vector3> savedTotems = new List<Vector3>();
    public static int MaxTotems => Has(Mutation.PackLord) ? 4 : 3;
    public static int MaxBeasts => Has(Mutation.PackLord) ? 5 : 3;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        owned = 0;
        InfiltratorsKilled = 0;
        HideoutBuilt = false;
        HideoutObject = null;
        perfumeUntil = docsUntil = uniformUntil = 0f;
        NeedRebuild = false;
        Totems.Clear();
        Beasts.Clear();
        savedTotems.Clear();
    }

    // ---------- Ukladanie ----------
    [Serializable]
    public class SaveData
    {
        public int mutations, infiltratorsKilled;
        public bool hideout;
        public float hx, hy, hz, hyaw;
        public float perfume, docs, uniform;
        public List<float> totems = new List<float>();
    }

    public static SaveData Export()
    {
        var data = new SaveData
        {
            mutations = owned, infiltratorsKilled = InfiltratorsKilled, hideout = HideoutBuilt,
            hx = HideoutPos.x, hy = HideoutPos.y, hz = HideoutPos.z, hyaw = HideoutYaw,
            perfume = PerfumeLeft, docs = DocsLeft, uniform = UniformLeft
        };
        foreach (Totem t in Totems)
        {
            if (t == null) continue;
            data.totems.Add(t.transform.position.x);
            data.totems.Add(t.transform.position.y);
            data.totems.Add(t.transform.position.z);
        }
        return data;
    }

    public static void Import(SaveData data)
    {
        foreach (Totem t in Totems.ToArray()) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
        if (HideoutObject != null) UnityEngine.Object.Destroy(HideoutObject);
        ResetState();
        if (data == null) return;

        owned = data.mutations;
        InfiltratorsKilled = data.infiltratorsKilled;
        HideoutBuilt = data.hideout;
        HideoutPos = new Vector3(data.hx, data.hy, data.hz);
        HideoutYaw = data.hyaw;
        perfumeUntil = Time.time + data.perfume;
        docsUntil = Time.time + data.docs;
        uniformUntil = Time.time + data.uniform;
        for (int i = 0; i + 2 < data.totems.Count; i += 3) savedTotems.Add(new Vector3(data.totems[i], data.totems[i + 1], data.totems[i + 2]));
        NeedRebuild = HideoutBuilt || savedTotems.Count > 0;
    }

    internal static void Rebuild()
    {
        NeedRebuild = false;
        if (HideoutBuilt && HideoutObject == null) HideoutObject = BuildHideoutVisual(HideoutPos, HideoutYaw);
        foreach (Vector3 p in savedTotems) Totem.Spawn(p);
        savedTotems.Clear();
    }

    // ---------- Akcie ----------
    public static Vector3 Ground(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out RaycastHit hit, 120f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return p;
    }

    public static string BuildHideout(PlayerStats player)
    {
        if (HideoutBuilt) return "Skrýšu už máš.";
        foreach (BuildPlot plot in BuildPlot.All)
            if ((plot.transform.position - player.transform.position).sqrMagnitude < 120f * 120f)
                return "Skrýša musí byť mimo mesta (aspoň 120 m od parciel).";

        Inventory inv = player.GetComponent<Inventory>();
        if (player.money < 300 || inv == null || inv.CountOf(ItemCatalog.Stone) < 15) return "Treba 300 peňazí a 15 kameňov.";
        player.TrySpend(300);
        inv.TryRemove(ItemCatalog.Stone, 15);

        HideoutPos = Ground(player.transform.position + player.transform.forward * 4f);
        HideoutYaw = player.transform.eulerAngles.y + 180f;
        HideoutBuilt = true;
        HideoutObject = BuildHideoutVisual(HideoutPos, HideoutYaw);
        return "Tajná skrýša je hotová. Tu si môžeš vyrobiť parfum, falošné doklady a uniformu.";
    }

    public static string Craft(string what, PlayerStats player)
    {
        if (!NearHideout(player.transform.position)) return "Musíš byť v skrýši.";
        Inventory inv = player.GetComponent<Inventory>();
        if (inv == null) return "Chýba inventár.";

        int money, scrap, crystal;
        switch (what)
        {
            case "perfume": money = 60; scrap = 0; crystal = 1; break;
            case "docs": money = 120; scrap = 5; crystal = 0; break;
            default: money = 200; scrap = 10; crystal = 0; break;
        }
        if (player.money < money || inv.CountOf(ItemCatalog.Scrap) < scrap || inv.CountOf(ItemCatalog.Crystal) < crystal)
            return $"Treba {money} peňazí" + (scrap > 0 ? $", {scrap} šrotu" : "") + (crystal > 0 ? $", {crystal} kryštál" : "") + ".";

        player.TrySpend(money);
        if (scrap > 0) inv.TryRemove(ItemCatalog.Scrap, scrap);
        if (crystal > 0) inv.TryRemove(ItemCatalog.Crystal, crystal);

        if (what == "perfume") { ActivatePerfume(); return "Parfum maskuje tvoj pach (6 min): civilisti sa ťa nebojú ani ako démona."; }
        if (what == "docs") { ActivateDocs(); return "Falošné doklady platia 4 min: vojaci ťa vo vojenských zónach nepodozrievajú."; }
        ActivateUniform();
        return "Uniforma ťa chráni 8 min: vojaci ti veria a podozrenie rastie pomalšie.";
    }

    public static string ConvertCivilian(PlayerStats player)
    {
        if (!Has(Mutation.Convert)) return "Chýba mutácia Premena ľudí.";
        if (!player.IsDemon) return "Môže to len démon.";

        CivilianAI target = null;
        float best = Has(Mutation.SwiftConvert) ? 36f : 16f;
        foreach (CivilianAI c in CivilianAI.All)
        {
            if (c == null || c.IsDead || !c.gameObject.activeInHierarchy) continue;
            float d = (c.transform.position - player.transform.position).sqrMagnitude;
            if (d < best) { best = d; target = c; }
        }
        if (target == null) return Has(Mutation.SwiftConvert) ? "V okolí 6 m nie je civilista." : "V okolí 4 m nie je civilista.";

        DemonEnemy source = null;
        foreach (DemonEnemy d in DemonEnemy.All)
        {
            if (d == null || d.tier != DemonTier.Lesser || d.Damage == null || d.Damage.IsDead || d.GetComponent<SurgeGuest>() != null) continue;
            source = d;
            break;
        }
        if (source == null) return "Nemáš koho privolať: žiadny voľný nižší démon.";
        float convertCost = Has(Mutation.SwiftConvert) ? 15f : 30f;
        if (!player.TrySpendBlood(convertCost)) return $"Málo krvi (treba {convertCost:0}).";

        Vector3 pos = target.transform.position;
        Damageable victim = target.GetComponent<Damageable>();
        if (victim != null) victim.ForceKill(false);
        CityPopulation.Reduce(PopulationGroup.Civilians, 1);

        source.gameObject.AddComponent<SurgeGuest>().Begin(pos + new Vector3(0.8f, 0.3f, 0.8f), 900f);
        source.allied = true;
        SoulBalance.Add(-3f);
        ArakaniFavor.AddFavor(3f);
        return "Civilista sa premenil na démona-služobníka. Ľudskosť −3.";
    }

    public static string EnslaveBeast(PlayerStats player)
    {
        if (!Has(Mutation.Enslave)) return "Chýba mutácia Zotročenie príšer.";
        if (!player.IsDemon) return "Môže to len démon.";
        if (Beasts.Count >= MaxBeasts) return $"Môžeš mať najviac {MaxBeasts} zvieratá.";

        BloodSource best = null;
        float bestD = 64f;
        foreach (BloodSource b in BloodSource.All)
        {
            if (b == null || b.IsDead || !b.IsAnimal || b.GetComponent<TamedBeast>() != null) continue;
            float d = (b.transform.position - player.transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = b; }
        }
        if (best == null) return "V okolí 8 m nie je žiadne zviera.";
        if (!player.TrySpendBlood(20f)) return "Málo krvi (treba 20).";

        best.gameObject.AddComponent<TamedBeast>();
        return $"{best.displayName} ti slúži.";
    }

    public static string PlaceTotem(PlayerStats player)
    {
        if (!Has(Mutation.Totems)) return "Chýba mutácia Totemy územia.";
        if (Totems.Count >= MaxTotems) return $"Najviac {MaxTotems} totemy.";

        Inventory inv = player.GetComponent<Inventory>();
        if (player.money < 50 || inv == null || inv.CountOf(ItemCatalog.Crystal) < 3 || inv.CountOf(ItemCatalog.Stone) < 10)
            return "Treba 50 peňazí, 3 kryštály a 10 kameňov.";
        player.TrySpend(50);
        inv.TryRemove(ItemCatalog.Crystal, 3);
        inv.TryRemove(ItemCatalog.Stone, 10);

        Totem.Spawn(Ground(player.transform.position + player.transform.forward * 2.5f));
        return "Totem označil územie. Pozor na Chytačov príšer.";
    }

    // ---------- Vzhľad skrýše ----------
    internal static GameObject Prim(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Color color, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        if (!collider) UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }

    static GameObject BuildHideoutVisual(Vector3 pos, float yaw)
    {
        var root = new GameObject("Skrysa");
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Color wood = new Color(0.22f, 0.15f, 0.1f), cloth = new Color(0.12f, 0.12f, 0.14f);
        Prim(root.transform, PrimitiveType.Cube, new Vector3(0f, 1.1f, -1.9f), new Vector3(4f, 2.2f, 0.2f), wood, true);
        Prim(root.transform, PrimitiveType.Cube, new Vector3(-1.9f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 4f), wood, true);
        Prim(root.transform, PrimitiveType.Cube, new Vector3(1.9f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 4f), wood, true);
        GameObject roof = Prim(root.transform, PrimitiveType.Cube, new Vector3(0f, 2.3f, 0f), new Vector3(4.4f, 0.15f, 4.4f), cloth, true);
        roof.name = "Strecha";
        Prim(root.transform, PrimitiveType.Cube, new Vector3(-0.9f, 0.45f, -1.2f), new Vector3(1.8f, 0.9f, 0.8f), new Color(0.3f, 0.2f, 0.12f), true);   // dielenský stôl
        Prim(root.transform, PrimitiveType.Cube, new Vector3(1.2f, 0.3f, -1.3f), new Vector3(0.7f, 0.6f, 0.7f), wood, true);                       // debna
        GameObject lamp = Prim(root.transform, PrimitiveType.Sphere, new Vector3(0f, 1.9f, -1.5f), new Vector3(0.18f, 0.18f, 0.18f), new Color(1f, 0.55f, 0.2f), false);
        var light = lamp.AddComponent<Light>();
        light.color = new Color(1f, 0.6f, 0.3f);
        light.range = 8f;
        light.intensity = 1.1f;
        return root;
    }
}

/// <summary>Totem územia: lieči a dopĺňa krv démonovi v okruhu, priťahuje Chytačov príšer (vojaci blízko ho ničia).</summary>
public class Totem : MonoBehaviour
{
    public const float Radius = 25f;
    private float health = 150f;
    private float nextTick;
    private PlayerStats player;

    public static Totem Spawn(Vector3 position)
    {
        var root = new GameObject("Totem");
        root.transform.position = position;
        Color bone = new Color(0.75f, 0.72f, 0.62f);
        Lair.Prim(root.transform, PrimitiveType.Cylinder, new Vector3(0f, 1.6f, 0f), new Vector3(0.45f, 1.6f, 0.45f), new Color(0.2f, 0.12f, 0.1f), true);
        Lair.Prim(root.transform, PrimitiveType.Sphere, new Vector3(0f, 3.35f, 0f), new Vector3(0.7f, 0.7f, 0.7f), bone, false);
        GameObject eye = Lair.Prim(root.transform, PrimitiveType.Sphere, new Vector3(0f, 3.4f, 0.3f), new Vector3(0.22f, 0.12f, 0.12f), new Color(1f, 0.1f, 0.1f), false);
        var light = eye.AddComponent<Light>();
        light.color = new Color(1f, 0.2f, 0.15f);
        light.range = 9f;
        light.intensity = 1.4f;
        return root.AddComponent<Totem>();
    }

    void OnEnable() { if (!Lair.Totems.Contains(this)) Lair.Totems.Add(this); }
    void OnDisable() => Lair.Totems.Remove(this);

    void Update()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + 1f;
        if (player == null) player = FindAnyObjectByType<PlayerStats>();

        if (player != null && player.IsDemon && !player.IsDead && (player.transform.position - transform.position).sqrMagnitude <= Radius * Radius)
        {
            player.Heal(1.2f);
            player.DrinkBlood(0.5f);
        }

        // Chytači príšer: vojaci pri totemi ho ničia
        foreach (SoldierAI soldier in SoldierAI.All)
        {
            if (soldier == null || soldier.IsDead || soldier.GetComponent<SurgeGuest>() == null) continue;
            if ((soldier.transform.position - transform.position).sqrMagnitude > 6f * 6f) continue;
            health -= 15f;
        }
        if (health <= 0f)
        {
            GameHUD.Message("Chytači príšer zničili totem!", 4f);
            Destroy(gameObject);
        }
    }
}

/// <summary>Zotročené zviera: chodí s hráčom a útočí na nepriateľských démonov.</summary>
public class TamedBeast : MonoBehaviour
{
    private PlayerStats player;
    private Damageable damage;
    private float nextAttack, nextGround;
    private Rigidbody rb;
    private const float Speed = 5f;

    void OnEnable() { if (!Lair.Beasts.Contains(this)) Lair.Beasts.Add(this); }
    void OnDisable() => Lair.Beasts.Remove(this);

    void Start()
    {
        player = FindAnyObjectByType<PlayerStats>();
        damage = GetComponent<Damageable>();
        rb = GetComponent<Rigidbody>();
        NPCWander wander = GetComponent<NPCWander>();
        if (wander != null) wander.enabled = false;
    }

    void Update()
    {
        if (player == null || (damage != null && damage.IsDead)) { if (damage != null && damage.IsDead) Destroy(this); return; }

        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        if (dist > 45f) { Place(player.transform.position + player.transform.right * 2f); return; }
        if (dist > 3.5f)
        {
            Vector3 dir = to / Mathf.Max(0.01f, dist);
            Place(transform.position + dir * Speed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
        }

        if (Time.time < nextAttack) return;
        foreach (DemonEnemy demon in DemonEnemy.All)
        {
            if (demon == null || demon.allied || !demon.IsChasing || demon.tier >= DemonTier.Boss || demon.Damage == null || demon.Damage.IsDead) continue;
            if ((demon.transform.position - transform.position).sqrMagnitude > 9f * 9f) continue;

            Vector3 toDemon = demon.transform.position - transform.position;
            toDemon.y = 0f;
            if (toDemon.sqrMagnitude > 2.2f * 2.2f) Place(transform.position + toDemon.normalized * Speed * 1.4f * Time.deltaTime);
            else
            {
                demon.Damage.TakeDamage(8f, gameObject);
                nextAttack = Time.time + 1.2f;
            }
            break;
        }
    }

    void Place(Vector3 position)
    {
        if (Time.time >= nextGround)
        {
            nextGround = Time.time + 0.15f;
            position.y = Lair.Ground(position).y;
        }
        else position.y = transform.position.y;

        transform.position = position;
        if (rb != null) rb.position = position;
    }
}

/// <summary>Infiltrátor: vyšší démon s ľudskou maskou. Kým nie je odhalený, denne oslabuje mesto. Odhalenie a zabitie dá odmenu.</summary>
public class Infiltrator : MonoBehaviour
{
    public static readonly List<Infiltrator> All = new List<Infiltrator>();

    private Damageable damage;
    private NPCPerception perception;
    private bool announced, rewarded;
    private float nextCheck;

    public bool Revealed => perception != null && perception.Revealed;
    public bool Alive => damage != null && !damage.IsDead;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    void Start()
    {
        damage = GetComponent<Damageable>();
        perception = GetComponent<NPCPerception>();
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;

        if (!announced && Revealed && Alive)
        {
            announced = true;
            GameHUD.Message("Infiltrátor odhalený! Maska mu spadla.", 4f);
        }
        if (!rewarded && damage != null && damage.IsDead)
        {
            rewarded = true;
            Lair.InfiltratorsKilled++;
            CityManagement.Satisfaction = Mathf.Min(100f, CityManagement.Satisfaction + 5f);
            PlayerStats p = FindAnyObjectByType<PlayerStats>();
            if (p != null) p.AddMoney(100);
            GameHUD.Message("Infiltrátor mŕtvy: mesto si vydýchlo (+5 spokojnosť, +100 peňazí).", 5f);
        }
    }
}

/// <summary>Beh: znovuvytvorenie skrýše po načítaní, výber infiltrátorov, denné následky a Chytači príšer pri totemoch.</summary>
public class LairRunner : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<LairRunner>() != null) return;
        new GameObject("LairRunner").AddComponent<LairRunner>();
    }

    void OnEnable() => DayNightCycle.OnNewDay += HandleNewDay;
    void OnDisable() => DayNightCycle.OnNewDay -= HandleNewDay;

    private PlayerStats regenPlayer;

    void Update()
    {
        TickRegen();
        if (Lair.NeedRebuild && !PauseMenu.AtStartup && FindAnyObjectByType<PlayerStats>() != null) Lair.Rebuild();
    }

    void TickRegen()
    {
        if (!Lair.Has(Mutation.BloodRegen) || PauseMenu.AtStartup) return;
        if (regenPlayer == null) { regenPlayer = FindAnyObjectByType<PlayerStats>(); return; }
        if (regenPlayer.IsDemon && !regenPlayer.IsDead && regenPlayer.Blood > 15f) regenPlayer.Heal(1.2f * Time.deltaTime);
    }

    void HandleNewDay(int day)
    {
        if (!Story.PrologueDone || Story.Act < 1) return;

        // Infiltrátori: vyššie maskované démony označíme, počet rastie s dňami
        int wanted = Mathf.Min(3, 1 + day / 12);
        int living = 0;
        foreach (Infiltrator i in Infiltrator.All) if (i != null && i.Alive) living++;
        foreach (DemonEnemy demon in FindObjectsByType<DemonEnemy>(FindObjectsInactive.Include))
        {
            if (living >= wanted) break;
            if (demon == null || demon.Tier != DemonTier.Higher || demon.Damage == null || demon.Damage.IsDead) continue;
            if (demon.GetComponent<NPCPerception>() == null || demon.GetComponent<Infiltrator>() != null || demon.GetComponent<SurgeGuest>() != null) continue;

            demon.gameObject.AddComponent<Infiltrator>();
            if (demon.GetComponent<DemonMarks>() == null)
            {
                var marks = demon.gameObject.AddComponent<DemonMarks>();
                marks.teeth = UnityEngine.Random.value < 0.6f;
                marks.bodyAnomaly = UnityEngine.Random.value < 0.4f;
            }
            living++;
        }

        // Neodhalení infiltrátori šíria strach
        foreach (Infiltrator i in Infiltrator.All)
        {
            if (i == null || !i.Alive || i.Revealed) continue;
            CityManagement.Satisfaction = Mathf.Max(0f, CityManagement.Satisfaction - 4f);
            if (UnityEngine.Random.value < 0.25f) CityPopulation.Reduce(PopulationGroup.Civilians, 1);
            GameHUD.Message("V meste sa stráca spokojnosť a ľudia: medzi nimi chodí infiltrátor.", 5f);
            break;
        }

        // Chytači príšer prídu k totemom
        foreach (Totem totem in Lair.Totems)
        {
            if (totem == null || UnityEngine.Random.value > 0.3f) continue;
            int sent = 0;
            foreach (SoldierAI soldier in SoldierAI.All)
            {
                if (sent >= 2) break;
                if (soldier == null || soldier.IsDead || soldier.GetComponent<SurgeGuest>() != null) continue;

                soldier.gameObject.AddComponent<SurgeGuest>().Begin(totem.transform.position + new Vector3(UnityEngine.Random.Range(-6f, 6f), 0.4f, UnityEngine.Random.Range(-6f, 6f)), 240f);
                soldier.ReceiveFalseReport();
                sent++;
            }
            if (sent > 0) GameHUD.Message("Chytači príšer sa zjavili pri tvojom totemi!", 5f);
        }
    }
}

/// <summary>
/// Prastaré koreňové uzly: démon nemôže použiť portály, takže pri bráne každej divočiny (Portal_z*), pri portáli mesta a v centre mesta
/// (objekt KorenovyUzol_Mesto) stojí nezničiteľný strom, ktorý je súčasťou siete koreňov (menu Príroda démona).
/// </summary>
public class RootNodeRunner : MonoBehaviour
{
    private bool done;
    private float nextTry;


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<RootNodeRunner>() != null) return;
        new GameObject("RootNodeRunner").AddComponent<RootNodeRunner>();
    }

    void Update()
    {
        if (done || PauseMenu.AtStartup || Time.time < nextTry) return;
        nextTry = Time.time + 1f;
        if (PortalGate.All.Count == 0) return;
        done = true;

        foreach (PortalGate gate in PortalGate.All)
        {
            if (gate == null) continue;
            string n = gate.gameObject.name;
            if (n.StartsWith("Portal_z")) Plant(gate.transform.position + gate.transform.right * 8f);
            else if (n == "Portal_DoMegakorporacie") Plant(gate.transform.position - gate.transform.right * 9f - gate.transform.forward * 3f);
        }
        GameObject hub = GameObject.Find("KorenovyUzol_Mesto");
        if (hub != null) Plant(hub.transform.position);
    }

    static void Plant(Vector3 position)
    {
        position = Lair.Ground(position);
        foreach (DemonTree tree in DemonTree.All)
            if (tree != null && tree.IsAncient && (tree.transform.position - position).sqrMagnitude < 25f) return;
        DemonTree.PlantAncient(position);
    }
}
