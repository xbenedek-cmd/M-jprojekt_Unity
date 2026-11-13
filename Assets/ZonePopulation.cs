using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public enum PopulationGroup { Civilians, Rebels, Soldiers }

/// <summary>
/// Obyvatelia jednej zóny. Skutočný počet obyvateľov je číslo za každú skupinu (civilisti, odboj, vojaci); postavy vo svete sú len
/// jeho viditeľná časť (rozpočet). Postavy zóny sú vypnuté, kým je hráč ďaleko; pri priblížení sa zapne rozpočet náhodných postáv,
/// chýbajúce sa doklonujú zo šablóny. Smrť postavy zníži počet skupiny natrvalo.
///
/// Pravidlá populácie (každé growthIntervalDays dní):
///  - Rast: skupina s aspoň 2 obyvateľmi pribudne o ~10 % (min. 1), najviac po kapacitu zóny. Rastie len sama zo seba:
///    skupina s 1 alebo 0 obyvateľmi už nerastie (posledný nemá s kým), takže nikdy nepribudne z ničoho.
///  - Prestup: s určitou šancou jeden obyvateľ prejde z jednej skupiny do inej (civilista ↔ odboj ↔ vojak), ale nikdy z poslednej
///    dvojice zdroja a len do skupiny, ktorá ešte existuje (vymretá frakcia sa neobnoví).
/// Veľkosť zóny určuje BoxCollider (spúšťač). Súhrn zón je v CityPopulation.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class ZonePopulation : MonoBehaviour
{
    private const int GroupCount = 3;

    [SerializeField] private string zoneName = "Zóna";
    [Tooltip("Koľko civilistov a odboja je v zóne naraz, kým žijú všetci")]
    [SerializeField] private int civilianBudget = 18;
    [Tooltip("Koľko vojakov je v zóne naraz, kým žijú všetci")]
    [SerializeField] private int soldierBudget = 6;
    [Tooltip("Postavy sa zapnú, keď je hráč bližšie než toto od hranice zóny")]
    [SerializeField] private float enterMargin = 12f;
    [Tooltip("Postavy sa vypnú, keď je hráč ďalej než toto od hranice zóny")]
    [SerializeField] private float exitMargin = 45f;

    [Header("Rast a prestupy")]
    [SerializeField] private int growthIntervalDays = 3;
    [Range(0f, 0.5f)] [SerializeField] private float growthRate = 0.1f;
    [Tooltip("Najviac obyvateľov skupiny = začiatočný počet × toto (min. 4)")]
    [SerializeField] private float capacityFactor = 2f;
    [Range(0f, 1f)] [SerializeField] private float defectionChance = 0.35f;

    [Header("Postavy")]
    [SerializeField] private List<GameObject> civilianPool = new List<GameObject>();
    [SerializeField] private List<GameObject> soldierPool = new List<GameObject>();
    [Tooltip("Vypnutá šablóna civilistu / vojaka, z ktorej sa za behu klonujú nové postavy")]
    [SerializeField] private GameObject civilianTemplate;
    [SerializeField] private GameObject soldierTemplate;

    public GameObject SoldierTemplate => soldierTemplate;
    public int GrowthIntervalDays => growthIntervalDays;

    private class Member
    {
        public GameObject go;
        public Vector3 home;
        public Quaternion rotation;
        public Damageable damage;
        public NPCPerception perception;
        public PopulationGroup group;
        public Action onDied;
        public bool IsAlive => go != null && (damage == null || !damage.IsDead);
    }

    private readonly List<Member> members = new List<Member>();
    private readonly int[] stock = new int[GroupCount];
    private readonly int[] initial = new int[GroupCount];
    private readonly float[] fraction = new float[GroupCount];
    private readonly List<Member> shown = new List<Member>();
    private readonly List<Member> hidden = new List<Member>();
    private BoxCollider box;
    private PlayerStats player;
    private float nextTick;
    private bool active;

    public string ZoneName => zoneName;
    public int CivilianBudget => civilianBudget;
    public int SoldierBudget => soldierBudget;
    public const int MaxNpcPerZone = 30;   // najviac postáv naraz v jednej zóne (ľudia + démoni)
    public int Stock(PopulationGroup g) => stock[(int)g];
    public int InitialStock(PopulationGroup g) => initial[(int)g];

    public void Configure(string name, int civilianCount, int soldierCount, List<GameObject> civilianMembers,
        List<GameObject> soldierMembers, GameObject civilianSource, GameObject soldierSource)
    {
        zoneName = name;
        civilianBudget = civilianCount;
        soldierBudget = soldierCount;
        civilianPool = civilianMembers;
        soldierPool = soldierMembers;
        civilianTemplate = civilianSource;
        soldierTemplate = soldierSource;
    }

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        civilianBudget = Mathf.Min(civilianBudget, MaxNpcPerZone);
        soldierBudget = Mathf.Min(soldierBudget, MaxNpcPerZone - civilianBudget);
        foreach (GameObject go in civilianPool) AddMember(go, false);
        foreach (GameObject go in soldierPool) AddMember(go, true);

        for (int g = 0; g < GroupCount; g++) initial[g] = stock[g];

        // Podiel viditeľných: rozpočet / počet; skupina bez členov dostane podiel zo svojho rozpočtu
        float civilianFraction = Fraction(civilianBudget, stock[(int)PopulationGroup.Civilians] + stock[(int)PopulationGroup.Rebels]);
        fraction[(int)PopulationGroup.Civilians] = civilianFraction;
        fraction[(int)PopulationGroup.Rebels] = civilianFraction;
        fraction[(int)PopulationGroup.Soldiers] = Fraction(soldierBudget, stock[(int)PopulationGroup.Soldiers]);

        CityPopulation.Register(this);
        DayNightCycle.OnNewDay += HandleNewDay;
    }

    static float Fraction(int budget, int count) => count > 0 ? Mathf.Clamp01((float)budget / count) : 0.5f;

    void OnDestroy()
    {
        DayNightCycle.OnNewDay -= HandleNewDay;
        CityPopulation.Unregister(this);
        foreach (Member m in members)
            if (m.damage != null && m.onDied != null) m.damage.OnDied -= m.onDied;
    }

    Member AddMember(GameObject go, bool soldier)
    {
        if (go == null) return null;

        var m = new Member
        {
            go = go,
            home = go.transform.position,
            rotation = go.transform.rotation,
            damage = go.GetComponent<Damageable>(),
            perception = go.GetComponent<NPCPerception>()
        };
        m.group = soldier ? PopulationGroup.Soldiers
                : m.perception != null && m.perception.faction == FactionId.Rebels ? PopulationGroup.Rebels
                : PopulationGroup.Civilians;

        // Smrť postavy zníži počet jej skupiny natrvalo
        m.onDied = () => stock[(int)m.group] = Mathf.Max(0, stock[(int)m.group] - 1);
        if (m.damage != null) m.damage.OnDied += m.onDied;

        if (!soldier && go.GetComponent<CivilianProfile>() == null)
            go.AddComponent<CivilianProfile>().Assign(CivilianProfile.Pick(go.name, m.group == PopulationGroup.Rebels));

        members.Add(m);
        stock[(int)m.group]++;
        return m;
    }

    void Start() => Tick(true);

    void Update()
    {
        if (Time.time < nextTick) return;
        nextTick = Time.time + 0.5f;
        Tick(false);
    }

    void Tick(bool force)
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player == null) return;
        }

        PopulationPressure.Sample(Reputation.Suspicion);

        Bounds zone = box.bounds;
        Vector3 p = player.transform.position;
        bool near = Contains(zone, p, enterMargin);
        bool far = !Contains(zone, p, exitMargin);

        if (!active && (near || (force && !far))) { active = true; Refresh(); }
        else if ((active || force) && far) { active = false; HideAll(); }
    }

    static bool Contains(Bounds b, Vector3 p, float margin)
    {
        b.Expand(new Vector3(margin * 2f, 1000f, margin * 2f));
        return b.Contains(p);
    }

    // ---------- Viditeľné postavy ----------
    void Refresh()
    {
        for (int g = 0; g < GroupCount; g++) Show((PopulationGroup)g);
    }

    void HideAll()
    {
        foreach (Member m in members)
            if (m.IsAlive && m.go.activeSelf) m.go.SetActive(false);
    }

    // Zapne ceil(počet × podiel) postáv skupiny: najprv tie, čo už sú vidieť, potom náhodné; chýbajúce sa doklonujú
    void Show(PopulationGroup group)
    {
        int s = stock[(int)group];
        int target = Mathf.Min(s, Mathf.CeilToInt(s * fraction[(int)group]));

        shown.Clear();
        hidden.Clear();
        foreach (Member m in members)
        {
            if (m.group != group || !m.IsAlive) continue;
            (m.go.activeSelf ? shown : hidden).Add(m);
        }
        Shuffle(shown);
        Shuffle(hidden);

        int visible = 0;
        foreach (Member m in shown)
        {
            if (visible < target) visible++;
            else m.go.SetActive(false);
        }

        foreach (Member m in hidden)
        {
            if (visible >= target) break;
            Place(m);
            visible++;
        }

        while (visible < target && Spawn(group)) visible++;
    }

    static void Place(Member m)
    {
        m.go.transform.SetPositionAndRotation(m.home, m.rotation);
        m.go.SetActive(true);
    }

    static void Shuffle(List<Member> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // Nový obyvateľ: klon šablóny na mieste jedného z domovov skupiny
    bool Spawn(PopulationGroup group)
    {
        bool soldier = group == PopulationGroup.Soldiers;
        GameObject template = soldier ? soldierTemplate : civilianTemplate;
        if (template == null) return false;

        Vector3 home = box.bounds.center;
        var homes = new List<Vector3>();
        foreach (Member m in members)
            if (m.group == group || (!soldier && m.group != PopulationGroup.Soldiers)) homes.Add(m.home);
        if (homes.Count > 0) home = homes[Random.Range(0, homes.Count)];
        home += new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));

        GameObject clone = Instantiate(template, home, template.transform.rotation, template.transform.parent);
        clone.name = $"{group}_{members.Count + 1}";

        NPCPerception perception = clone.GetComponent<NPCPerception>();
        if (perception != null && !soldier) perception.faction = group == PopulationGroup.Rebels ? FactionId.Rebels : FactionId.None;

        // AddMember rátal člena do počtu; klon je len zobrazenie už započítaného obyvateľa
        Member added = AddMember(clone, soldier);
        added.group = group;
        stock[(int)group]--;
        clone.SetActive(true);
        return true;
    }

    // ---------- Bývanie (stavby) ----------
    private readonly int[] housing = new int[GroupCount];

    /// <summary>Hranice zóny (mesta) vo svete.</summary>
    public Bounds Area
    {
        get
        {
            if (box == null) box = GetComponent<BoxCollider>();
            return box.bounds;
        }
    }

    /// <summary>Postavy mesta nesmú opustiť zónu: bod sa vtlačí dovnútra.</summary>
    public Vector3 ClampInside(Vector3 p, float margin = 1f)
    {
        Bounds b = Area;
        p.x = Mathf.Clamp(p.x, b.min.x + margin, b.max.x - margin);
        p.z = Mathf.Clamp(p.z, b.min.z + margin, b.max.z - margin);
        return p;
    }

    /// <summary>Zóna obsahuje bod (parcely patria zóne, v ktorej ležia).</summary>
    public bool Contains(Vector3 point)
    {
        if (box == null) box = GetComponent<BoxCollider>();
        Bounds b = box.bounds;
        b.Expand(new Vector3(0f, 1000f, 0f));
        return b.Contains(point);
    }

    /// <summary>Stavba zvýši kapacitu skupiny (obytný dom, kasárne): populácia môže rásť na viac obyvateľov.</summary>
    public void AddHousing(PopulationGroup group, int capacity) => housing[(int)group] += capacity;

    /// <summary>Zmena počtu obyvateľov skupiny (občianska vojna, premena na démona); neklesne pod 0.</summary>
    public void AdjustStock(PopulationGroup group, int delta) => stock[(int)group] = Mathf.Max(0, stock[(int)group] + delta);

    // ---------- Ukladanie ----------
    [Serializable]
    public class SaveData
    {
        public string zone;
        public int civilians, rebels, soldiers;
        public int[] housing;
        public List<int> deadOriginals = new List<int>();
    }

    public SaveData Export()
    {
        var data = new SaveData
        {
            zone = zoneName,
            civilians = stock[(int)PopulationGroup.Civilians],
            rebels = stock[(int)PopulationGroup.Rebels],
            soldiers = stock[(int)PopulationGroup.Soldiers],
            housing = (int[])housing.Clone()
        };

        // Pôvodné postavy poolu (nie klony), ktoré zomreli: po načítaní sa odstránia
        int originals = civilianPool.Count + soldierPool.Count;
        for (int i = 0; i < members.Count && i < originals; i++)
            if (!members[i].IsAlive) data.deadOriginals.Add(i);
        return data;
    }

    public void Import(SaveData data)
    {
        if (data == null) return;

        foreach (int i in data.deadOriginals)
        {
            if (i < 0 || i >= members.Count || members[i].go == null) continue;
            Member m = members[i];
            if (m.damage != null && m.onDied != null) m.damage.OnDied -= m.onDied;   // smrť sa už do počtu raz započítala
            Destroy(m.go);
        }

        stock[(int)PopulationGroup.Civilians] = data.civilians;
        stock[(int)PopulationGroup.Rebels] = data.rebels;
        stock[(int)PopulationGroup.Soldiers] = data.soldiers;
        if (data.housing != null && data.housing.Length == GroupCount) Array.Copy(data.housing, housing, GroupCount);

        if (active) Refresh();
    }

    // ---------- Rast a prestupy ----------
    // Tlak (útlak korporácie = podozrenie mesta, ničenie divočiny): pri vysokom tlaku civilisti utekajú k odboju alebo do divočiny,
    // keď tlak opadne, časť utečencov sa vráti. Utekať nemôže posledný civilista.
    bool ApplyPressure(float pressure)
    {
        int civilians = stock[(int)PopulationGroup.Civilians];

        if (pressure >= 0.5f && civilians >= 2)
        {
            int flee = Mathf.Min(civilians - 1, Mathf.Max(1, Mathf.RoundToInt(civilians * 0.12f * pressure * 2f)));
            int toRebels = stock[(int)PopulationGroup.Rebels] >= 1 ? (flee + 1) / 2 : 0;
            int away = flee - toRebels;

            stock[(int)PopulationGroup.Civilians] -= flee;
            stock[(int)PopulationGroup.Rebels] += toRebels;
            PopulationPressure.Refugees += away;
            GameHUD.Message($"Civilisti utekajú z {zoneName}: {toRebels} k odboju, {away} do divočiny.", 4f);
            return true;
        }

        if (pressure < 0.25f && PopulationPressure.Refugees > 0 && civilians >= 1)
        {
            int back = Mathf.Max(1, PopulationPressure.Refugees / 4);
            stock[(int)PopulationGroup.Civilians] += back;
            PopulationPressure.Refugees -= back;
            GameHUD.Message($"Do {zoneName} sa vracajú civilisti z divočiny: {back}.", 3f);
            return true;
        }

        return false;
    }

    void HandleNewDay(int day)
    {
        bool fled = ApplyPressure(PopulationPressure.PressureForDay(day));
        if (growthIntervalDays <= 0 || day % growthIntervalDays != 0)
        {
            if (fled && active) Refresh();
            return;
        }

        bool changed = fled;
        for (int g = 0; g < GroupCount; g++)
        {
            int s = stock[g];
            if (s < 2) continue;   // posledný obyvateľ nemá s kým rásť

            int cap = Mathf.Max(4, Mathf.RoundToInt(initial[g] * capacityFactor)) + housing[g];
            int gain = Mathf.Min(cap - s, Mathf.Max(1, Mathf.RoundToInt(s * growthRate)));
            if (gain <= 0) continue;

            stock[g] += gain;
            changed = true;
        }

        if (Random.value < defectionChance && Defect()) changed = true;

        if (changed && active) Refresh();
    }

    // Jeden obyvateľ prejde z väčšej skupiny do inej, ktorá ešte existuje; zdroj musí mať aspoň 2
    bool Defect()
    {
        var options = new List<(int from, int to)>();
        for (int from = 0; from < GroupCount; from++)
            for (int to = 0; to < GroupCount; to++)
                if (from != to && stock[from] >= 2 && stock[to] >= 1) options.Add((from, to));

        if (options.Count == 0) return false;

        var pick = options[Random.Range(0, options.Count)];
        stock[pick.from]--;
        stock[pick.to]++;
        return true;
    }
}

/// <summary>Súhrn obyvateľov všetkých zón: základ pre stavanie (menej civilistov = dlhšie trvá) a udalosti.</summary>
public static class CityPopulation
{
    private static readonly List<ZonePopulation> zones = new List<ZonePopulation>();

    public static void Register(ZonePopulation zone)
    {
        if (!zones.Contains(zone)) zones.Add(zone);
    }

    public static void Unregister(ZonePopulation zone) => zones.Remove(zone);

    public static int Count(PopulationGroup group)
    {
        int n = 0;
        foreach (ZonePopulation z in zones) n += z.Stock(group);
        return n;
    }

    /// <summary>Civilisti v meste. Ak scéna nemá zóny s civilistami (pool sa nevytvoril), počíta sa DefaultCivilians, aby sa dalo stavať bez čakania na pool.</summary>
    public static int AliveCivilians() => ZoneInitialCivilians() == 0 ? DefaultCivilians : Count(PopulationGroup.Civilians) + Count(PopulationGroup.Rebels);

    /// <summary>Počet civilistov, z ktorého sa vychádza, keď scéna nemá zóny s civilistami.</summary>
    public const int DefaultCivilians = 10;

    public static int AliveSoldiers() => Count(PopulationGroup.Soldiers);

    /// <summary>Za koľko dní sa populácia znova rozrastie (podľa prvej zóny); 0, ak rast nie je zapnutý.</summary>
    public static int DaysToGrowth(int day)
    {
        foreach (ZonePopulation z in zones)
        {
            int interval = z.GrowthIntervalDays;
            if (interval > 0) return interval - (day % interval);
        }
        return 0;
    }

    public static bool HasZones => zones.Count > 0;

    /// <summary>Zóna (mesto), v ktorej leží bod, alebo null.</summary>
    public static ZonePopulation At(Vector3 point)
    {
        foreach (ZonePopulation zone in zones)
            if (zone != null && zone.Contains(point)) return zone;
        return null;
    }

    /// <summary>Uberie (delta menšia ako 0) obyvateľov skupiny zo zóny, kde je ich najviac.</summary>
    public static void Reduce(PopulationGroup group, int amount)
    {
        for (int i = 0; i < amount; i++)
        {
            ZonePopulation best = null;
            foreach (ZonePopulation zone in zones) if (best == null || zone.Stock(group) > best.Stock(group)) best = zone;
            if (best == null || best.Stock(group) <= 0) return;
            best.AdjustStock(group, -1);
        }
    }

    public static int InitialCivilians()
    {
        int zoneInitial = ZoneInitialCivilians();
        return zoneInitial == 0 ? DefaultCivilians : zoneInitial;
    }

    static int ZoneInitialCivilians()
    {
        int n = 0;
        foreach (ZonePopulation z in zones) n += z.InitialStock(PopulationGroup.Civilians) + z.InitialStock(PopulationGroup.Rebels);
        return n;
    }

    /// <summary>1 = civilistov je aspoň toľko ako na začiatku, 0 = nikto.</summary>
    public static float SurvivalRatio
    {
        get
        {
            int initial = InitialCivilians();
            return initial > 0 ? Mathf.Clamp01((float)AliveCivilians() / initial) : 1f;
        }
    }

    /// <summary>Násobok času stavby: 1 pri plnom meste, až 3 pri vyľudnenom (plán: čím menej civilistov, tým dlhšie stavba trvá).</summary>
    public static float ConstructionTimeFactor => Mathf.Lerp(3f, 1f, SurvivalRatio);
}
