using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// NPC démoni v zónach. Streamovanie: démon existuje fyzicky len keď je hráč blízko (inak je vypnutý, hra si pamätá jeho slot).
/// Limit: v jednej zóne najviac ZonePopulation.MaxNpcPerZone (30) postáv spolu s ľuďmi; prebytok pri štarte zmizne.
/// Rast: raz za deň môže v zóne pribudnúť nižší démon (z vypnutého prototypu), kým je zóna pod cieľom
/// (min(kapacita, 2 × pôvodný počet + 4)). Šanca rastie s tlakom na mesto, so stromami temnoty a totemami, klesá s obrannými vežami.
/// Povýšenie: nižší → stredný po niekoľkých dňoch, stredný → vyšší až keď má hráč dosť vysoký súčet úrovní (HigherUnlockTotalLevel)
/// a vo zóne je miesto (najviac ~1/5 kapacity). Bossovia, vodcovia, Arakani, popravcovia a spojenci sa nestreamujú.
/// Stav rastu sa neukladá (po načítaní hry sa zóny začnú od scény).
/// </summary>
public class DemonStreamer : MonoBehaviour
{
    public const int HigherUnlockTotalLevel = 20;   // humanLevel + demonLevel
    private const float ActivateRange = 70f;
    private const float DeactivateRange = 100f;
    private const int GrowthStartDay = 3;
    private const int MidAgeDays = 5, HigherAgeDays = 10;

    private class ZoneData
    {
        public Object key;
        public int allowance, target;
        public GameObject prototype;
        public readonly List<Vector3> homes = new List<Vector3>();
        public readonly List<Slot> slots = new List<Slot>();
    }

    private class Slot
    {
        public GameObject go;
        public DemonEnemy demon;
        public Damageable damage;
        public ZoneData zone;
        public bool dead, active;
        public int ageDays;
    }

    private readonly Dictionary<Object, ZoneData> zoneData = new Dictionary<Object, ZoneData>();
    private readonly List<Slot> slots = new List<Slot>();
    private Terrain[] terrains;
    private ZonePopulation[] populations;
    private PlayerStats player;
    private float nextTick;
    private float buildAt = -1f;
    private bool built;

    public static int Remembered { get; private set; }
    public static int Active { get; private set; }

    private static DemonStreamer instance;

    /// <summary>Zapamätaní démoni v oblasti (aj v nenačítanej zóne): koľko žije a koľko ich tam vôbec je.</summary>
    public static void CountIn(Bounds area, out int alive, out int total)
    {
        alive = 0;
        total = 0;
        if (instance == null) return;
        foreach (Slot s in instance.slots)
        {
            if (s.go == null) continue;
            Vector3 p = s.go.transform.position;
            if (p.x < area.min.x || p.x > area.max.x || p.z < area.min.z || p.z > area.max.z) continue;
            total++;
            if (!s.dead) alive++;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { Remembered = 0; Active = 0; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<DemonStreamer>() != null) return;
        new GameObject("DemonStreamer").AddComponent<DemonStreamer>();
    }

    void OnEnable() { instance = this; DayNightCycle.OnNewDay += HandleNewDay; }
    void OnDisable() { if (instance == this) instance = null; DayNightCycle.OnNewDay -= HandleNewDay; }

    void Update()
    {
        if (PauseMenu.AtStartup) return;
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null && buildAt < 0f) buildAt = Time.time + 2f;   // chvíľa, kým sa zóny a ZonePopulation zaregistrujú
            return;
        }
        if (!built)
        {
            if (Time.time >= buildAt) Build();
            return;
        }
        if (Time.time < nextTick) return;
        nextTick = Time.time + 0.5f;
        Tick();
    }

    static bool Streamable(DemonEnemy d)
    {
        if (d == null || d.tier >= DemonTier.Boss || d.executioner || d.allied) return false;
        if (d.GetComponent<SurgeGuest>() != null) return false;
        Damageable dmg = d.GetComponent<Damageable>();
        return dmg != null && !dmg.IsDead;
    }

    // Zóna = zóna obyvateľov, v ktorej bod leží, inak terén divočiny, inak spoločná (0)
    Object ZoneKey(Vector3 p, out int humans)
    {
        humans = 0;
        foreach (ZonePopulation z in populations)
            if (z != null && z.Contains(p)) { humans = z.CivilianBudget + z.SoldierBudget; return z; }
        foreach (Terrain t in terrains)
        {
            if (t == null) continue;
            Vector3 o = t.transform.position, s = t.terrainData.size;
            if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t;
        }
        return this;   // spoločná zóna mimo terénov a zón obyvateľov
    }

    void Build()
    {
        built = true;
        terrains = Terrain.activeTerrains;
        populations = FindObjectsByType<ZonePopulation>();

        var perZone = new Dictionary<Object, List<Slot>>();
        foreach (DemonEnemy d in FindObjectsByType<DemonEnemy>(FindObjectsInactive.Include))
        {
            if (!Streamable(d)) continue;
            Object key = ZoneKey(d.transform.position, out int humans);
            if (!zoneData.TryGetValue(key, out ZoneData zd)) { zd = new ZoneData { key = key, allowance = Mathf.Max(0, ZonePopulation.MaxNpcPerZone - humans) }; zoneData[key] = zd; perZone[key] = new List<Slot>(); }
            perZone[key].Add(new Slot { go = d.gameObject, demon = d, damage = d.GetComponent<Damageable>(), zone = zd, active = d.gameObject.activeSelf });
        }

        int removed = 0;
        foreach (KeyValuePair<Object, List<Slot>> pair in perZone)
        {
            ZoneData zd = zoneData[pair.Key];
            List<Slot> list = pair.Value;

            // Prototyp pre rast: vypnutá kópia nižšieho démona z tejto zóny
            foreach (Slot s in list)
                if (s.demon.tier == DemonTier.Lesser) { zd.prototype = MakePrototype(s.go, zd.key); break; }

            for (int i = list.Count - 1; i >= zd.allowance; i--)
            {
                Destroy(list[i].go);
                list.RemoveAt(i);
                removed++;
            }
            foreach (Slot s in list) zd.homes.Add(s.go.transform.position);
            zd.target = Mathf.Min(zd.allowance, list.Count * 2 + 4);
            zd.slots.AddRange(list);
            slots.AddRange(list);
        }

        Tick();
        if (removed > 0) Debug.Log($"DemonStreamer: v zónach bol prekročený limit {ZonePopulation.MaxNpcPerZone} postáv, odstránených {removed} démonov.");
    }

    GameObject MakePrototype(GameObject source, Object key)
    {
        GameObject copy = Instantiate(source, transform);
        copy.name = "PrototypDemona_" + key.name;
        copy.SetActive(false);
        return copy;
    }

    // ---------- Denný rast a povýšenie ----------
    void HandleNewDay(int day)
    {
        if (!built || player == null) return;

        foreach (Slot s in slots) if (!s.dead) s.ageDays++;
        if (day >= GrowthStartDay) foreach (ZoneData zd in zoneData.Values) GrowZone(zd);
        foreach (Slot s in slots) Promote(s);
    }

    float GrowthChance(ZoneData zd)
    {
        float chance = 0.2f + 0.5f * PopulationPressure.CurrentPressure;

        int towers = 0;
        foreach (BuildPlot plot in BuildPlot.All)
            if (plot.State == PlotState.Built && plot.Building != null && plot.Building.defenseRange > 0f && ZoneKey(plot.transform.position, out _) == zd.key) towers++;
        chance -= 0.12f * towers;

        int trees = 0;
        foreach (DemonTree tree in DemonTree.All)
            if (tree != null && !tree.IsAncient && tree.Stage >= 1 && ZoneKey(tree.transform.position, out _) == zd.key) trees++;
        chance += 0.08f * Mathf.Min(trees, 4);

        foreach (Totem totem in Lair.Totems)
            if (totem != null && ZoneKey(totem.transform.position, out _) == zd.key) { chance *= 1.25f; break; }

        return Mathf.Clamp(chance, 0.05f, 0.9f);
    }

    void GrowZone(ZoneData zd)
    {
        if (zd.prototype == null || zd.homes.Count == 0) return;

        int alive = 0;
        foreach (Slot s in zd.slots) if (!s.dead) alive++;
        if (alive >= zd.target) return;
        if (Random.value > GrowthChance(zd)) return;

        Vector3 home = zd.homes[Random.Range(0, zd.homes.Count)];
        Vector3 position = home + new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f));

        GameObject copy = Instantiate(zd.prototype, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), zd.prototype.transform.parent);
        copy.name = zd.prototype.name.Replace("PrototypDemona_", "Demon_rast_");
        copy.SetActive(false);   // zapne ho streamovanie, keď bude hráč blízko

        var slot = new Slot { go = copy, demon = copy.GetComponent<DemonEnemy>(), damage = copy.GetComponent<Damageable>(), zone = zd, active = false };
        if (slot.demon == null || slot.damage == null) { Destroy(copy); return; }
        zd.slots.Add(slot);
        slots.Add(slot);
    }

    void Promote(Slot s)
    {
        if (s.dead || s.go == null || s.demon == null) return;

        if (s.demon.tier == DemonTier.Lesser && s.ageDays >= MidAgeDays && Random.value < 0.15f)
        {
            s.demon.tier = DemonTier.Mid;
            s.damage.maxHealth *= 1.3f;
            s.demon.attackDamage *= 1.2f;
            return;
        }

        if (s.demon.tier != DemonTier.Mid || s.ageDays < HigherAgeDays || Random.value >= 0.1f) return;
        if (player.humanLevel + player.demonLevel < HigherUnlockTotalLevel) return;   // vyšší démoni až neskôr v hre

        int higher = 0;
        foreach (Slot other in s.zone.slots) if (!other.dead && other.demon != null && other.demon.tier == DemonTier.Higher) higher++;
        if (higher >= Mathf.Max(1, s.zone.allowance / 5)) return;

        s.demon.tier = DemonTier.Higher;
        s.damage.immuneToNormalWeapons = true;   // vyšší démon: len najlepší meč alebo slabina
        s.damage.maxHealth *= 1.5f;
        s.demon.attackDamage *= 1.3f;
        GameHUD.Message("Cítiš silnejšieho démona: niekde sa zrodil vyšší démon.", 4f);
    }

    // ---------- Streamovanie ----------
    void Tick()
    {
        Vector3 p = player.transform.position;
        int remembered = 0, active = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            Slot s = slots[i];
            if (s.dead) continue;
            if (s.go == null || (s.damage != null && s.damage.IsDead)) { s.dead = true; continue; }
            remembered++;

            float sqr = (s.go.transform.position - p).sqrMagnitude;
            if (s.active)
            {
                bool busy = s.demon.IsChasing || s.demon.allied || s.go.GetComponent<SurgeGuest>() != null;
                if (sqr > DeactivateRange * DeactivateRange && !busy) { s.go.SetActive(false); s.active = false; }
            }
            else if (sqr <= ActivateRange * ActivateRange)
            {
                s.go.SetActive(true);
                s.active = true;
            }
            if (s.active) active++;
        }

        Remembered = remembered;
        Active = active;
    }
}
