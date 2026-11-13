using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Ukladanie stavu sveta do JSON (persistentDataPath/world_state.json): deň a čas, hráč (poloha, peniaze, levely), dôvera frakcií,
/// obyvatelia zón (počty, mŕtvi), parcely a stavby, mŕtvi démoni a poloha bossov. Uloženie: nový deň, každých 90 s, F9 a pri ukončení.
/// Načítanie sa spraví samo krátko po štarte. Inventár si ukladá sám (Inventory).
/// Zmazanie uloženého stavu: menu Progeny → Testy a pomocné → Zmazať uložený stav sveta.
/// </summary>
public class WorldSave : MonoBehaviour
{
    public const string FileName = "world_state.json";
    const float AutosaveSeconds = 90f;
    static KeyCode SaveKey => KeyBindings.Get("save", KeyCode.F9);

    public const int SlotCount = 3;

    /// <summary>Aktuálny slot uloženia (1–3); slot 1 používa pôvodný súbor world_state.json.</summary>
    public static int Slot
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt("save_slot", 1), 1, SlotCount);
        set => PlayerPrefs.SetInt("save_slot", Mathf.Clamp(value, 1, SlotCount));
    }

    public static string PathFor(int slot) => Path.Combine(Application.persistentDataPath, slot == 1 ? FileName : $"world_state_slot{slot}.json");
    public static string FilePath => PathFor(Slot);
    public static WorldSave Current { get; private set; }

    [Serializable]
    class DayProbe { public int day; }

    /// <summary>Krátky popis slotu ("Deň 12") alebo null, ak je prázdny.</summary>
    public static string SlotSummary(int slot)
    {
        string path = PathFor(slot);
        if (!File.Exists(path)) return null;

        try
        {
            DayProbe probe = JsonUtility.FromJson<DayProbe>(File.ReadAllText(path));
            return $"Deň {(probe != null ? Mathf.Max(1, probe.day) : 1)}  ({File.GetLastWriteTime(path):d.M. HH:mm})";
        }
        catch (Exception)
        {
            return "poškodený";
        }
    }

    /// <summary>Uloží svet do daného slotu (volá pauza).</summary>
    public static void SaveToSlot(int slot)
    {
        Slot = slot;
        if (Current != null) Current.Save(true);
    }

    [Serializable]
    class BossPosition { public string name; public Vector3 position; public Vector3 home; }

    [Serializable]
    class WorldData
    {
        public int version = 1;
        public int day;
        public float hour;
        public Vector3 playerPosition;
        public Vector3 playerForward;
        public int money, humanLevel, demonLevel;
        public bool hasSkills;
        public int skillLevel, skillXp, skillPoints, soulPoints, demonXp;
        public string skillNodes;
        public float formTimer, soulProgress;
        public float sanity = 100f;
        public float heat;
        public int nextBroadcastDay;
        public float recentRaid;
        public List<int> factionIds = new List<int>();
        public List<float> factionTrust = new List<float>();
        public float suspicion;
        public List<ZonePopulation.SaveData> zones = new List<ZonePopulation.SaveData>();
        public List<BuildPlot.SaveData> plots = new List<BuildPlot.SaveData>();
        public List<string> deadDemons = new List<string>();
        public List<BossPosition> bosses = new List<BossPosition>();
        public List<DemonTree.SaveData> trees = new List<DemonTree.SaveData>();
        public int hardness, refugees;
        public float natureDamage;
        public int[] weaponUpgrades;
        public float[] tools;
        public float portalStability = 100f;
        public int portalOutageUntil = -1;
        public List<float> professionTrust = new List<float>();
        public string extra;
        public string bossStates;
    }

    private readonly HashSet<string> deadDemons = new HashSet<string>();
    private readonly HashSet<DemonEnemy> tracked = new HashSet<DemonEnemy>();
    private PlayerStats player;
    private float nextAutosave;
    private float nextScan;
    private bool loaded;
    private Vector3 safePoint;          // posledné miesto na zemi (záchrana pri páde do prázdna)
    private float nextSafeCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<WorldSave>() != null) return;
        new GameObject("WorldSave").AddComponent<WorldSave>();
    }

    IEnumerator Start()
    {
        Current = this;
        DayNightCycle.OnNewDay += HandleNewDay;
        nextAutosave = Time.unscaledTime + AutosaveSeconds;

        PlayerStats early = FindAnyObjectByType<PlayerStats>();
        if (early != null) safePoint = early.transform.position;

        // Čas dňa sa nastaví hneď, nie až o sekundu neskôr (inak obloha a svetlo na chvíľu preskočia)
        ApplySavedClock();

        // Počkať, kým sa zóny, parcely a postavy inicializujú (Awake/Start)
        yield return new WaitForSecondsRealtime(1.5f);
        while (PauseMenu.AtStartup) yield return null;   // hlavné menu ešte nevybralo slot
        player = FindAnyObjectByType<PlayerStats>();
        if (File.Exists(FilePath)) Load();
        loaded = true;
    }

    void ApplySavedClock()
    {
        if (!File.Exists(FilePath)) return;

        try
        {
            WorldData data = JsonUtility.FromJson<WorldData>(File.ReadAllText(FilePath));
            DayNightCycle clock = FindAnyObjectByType<DayNightCycle>();
            if (data != null && data.version == 1 && data.day > 0 && clock != null) clock.SetClock(data.day, data.hour);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"WorldSave: čas sa nedá načítať: {e.Message}");
        }
    }

    void OnDestroy()
    {
        DayNightCycle.OnNewDay -= HandleNewDay;
        if (Current == this) Current = null;
    }

    void OnApplicationQuit()
    {
        if (loaded) Save(false);
    }

    void HandleNewDay(int day)
    {
        if (loaded) Save(false);
    }

    void Update()
    {
        if (!loaded) return;

        RescueFromVoid();

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 3f;
            TrackDemons();
        }

        if (GameInput.GetKeyDown(SaveKey)) Save(true);
        else if (Time.unscaledTime >= nextAutosave)
        {
            nextAutosave = Time.unscaledTime + AutosaveSeconds;
            Save(false);
        }
    }

    // Hráč, ktorý padá pod posledné pevné miesto (zem chýba), sa vráti na posledné miesto, kde stál na zemi
    private bool wasInMine;

    void RescueFromVoid()
    {
        if (MineSystem.Inside) { wasInMine = true; return; }   // baňa leží hlboko pod mapou schválne
        if (wasInMine) { wasInMine = false; if (player != null) safePoint = player.transform.position; nextSafeCheck = 0f; }

        if (player == null) return;

        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement == null) return;

        if (movement.IsGrounded && Time.unscaledTime >= nextSafeCheck)
        {
            nextSafeCheck = Time.unscaledTime + 1f;
            safePoint = player.transform.position;
        }
        else if (player.transform.position.y < safePoint.y - 40f)
        {
            movement.Teleport(safePoint + Vector3.up * 1f, player.transform.forward);
            GameHUD.Message("Spadol si mimo mapy, vraciam ťa na pevnú zem.", 3f);
        }
    }

    // Smrť démona sa zapamätá hneď (mŕtvola sa môže neskôr zničiť)
    void TrackDemons()
    {
        foreach (DemonEnemy demon in FindObjectsByType<DemonEnemy>(FindObjectsInactive.Include))
        {
            if (demon.Damage == null || demon.tier != DemonTier.Boss || !tracked.Add(demon)) continue;   // bežní démoni sa generujú nanovo, trvale mŕtvi sú len bossovia

            string name = demon.name;
            if (demon.Damage.IsDead) deadDemons.Add(name);
            else demon.Damage.OnDied += () => deadDemons.Add(name);
        }
    }

    // ---------- Uloženie ----------
    void Save(bool announce)
    {
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        if (player == null || player.IsDead || JailManager.IsInJail || JailManager.MenuOpen) return;

        var data = new WorldData();

        DayNightCycle clock = FindAnyObjectByType<DayNightCycle>();
        if (clock != null) { data.day = clock.Day; data.hour = clock.TimeOfDay; }

        data.playerPosition = player.transform.position;
        data.playerForward = player.transform.forward;
        data.money = player.money;
        data.humanLevel = player.humanLevel;
        data.demonLevel = player.demonLevel;
        data.hasSkills = true;
        data.skillLevel = player.skillLevel;
        data.skillXp = player.skillXp;
        data.skillPoints = player.skillPoints;
        data.soulPoints = player.soulPoints;
        data.demonXp = player.demonXp;
        data.skillNodes = SkillTree.Export();
        data.formTimer = player.FormTimerValue;
        data.soulProgress = player.SoulProgressValue;
        data.sanity = Sanity.Value;
        data.heat = Propaganda.Heat;
        data.nextBroadcastDay = Propaganda.NextAllowedDay;
        data.recentRaid = DemonRaid.Recent;

        foreach (FactionId id in Enum.GetValues(typeof(FactionId)))
        {
            data.factionIds.Add((int)id);
            data.factionTrust.Add(Reputation.Get(id));
        }
        data.suspicion = Reputation.Suspicion;

        foreach (ZonePopulation zone in FindObjectsByType<ZonePopulation>()) data.zones.Add(zone.Export());
        foreach (BuildPlot plot in BuildPlot.All) data.plots.Add(plot.Export());

        data.deadDemons.AddRange(deadDemons);
        foreach (DemonTree tree in DemonTree.All) if (!tree.IsAncient) data.trees.Add(tree.Export());
        data.hardness = DemonNature.Hardness;
        data.weaponUpgrades = WeaponUpgrades.Snapshot();
        data.tools = ToolKit.Snapshot();
        data.professionTrust.AddRange(ProfessionTrust.Snapshot());
        data.extra = SettlementEconomy.ExportAll();
        data.bossStates = BossRegistry.Export();
        data.refugees = PopulationPressure.Refugees;
        data.natureDamage = PopulationPressure.NatureDamage;
        if (PortalStability.Instance != null) PortalStability.Instance.Export(out data.portalStability, out data.portalOutageUntil);
        foreach (DemonEnemy demon in FindObjectsByType<DemonEnemy>())
            if (demon.tier == DemonTier.Boss && demon.Damage != null && !demon.Damage.IsDead)
            {
                BossPost post = demon.GetComponent<BossPost>();
                data.bosses.Add(new BossPosition { name = demon.name, position = demon.transform.position, home = post != null ? post.Home : demon.transform.position });
            }

        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            if (announce) GameHUD.Message($"Stav sveta uložený (deň {data.day}).", 2.5f);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"WorldSave: uloženie zlyhalo: {e.Message}");
        }
    }

    // ---------- Načítanie ----------
    void Load()
    {
        WorldData data;
        try
        {
            data = JsonUtility.FromJson<WorldData>(File.ReadAllText(FilePath));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"WorldSave: súbor sa nedá prečítať: {e.Message}");
            return;
        }
        if (data == null || data.version != 1) return;

        DayNightCycle clock = FindAnyObjectByType<DayNightCycle>();
        if (clock != null && data.day > 0) clock.SetClock(data.day, data.hour);

        if (player != null)
        {
            player.money = data.money;
            player.humanLevel = data.humanLevel;
            player.demonLevel = data.demonLevel;
            if (data.hasSkills)
            {
                player.demonXp = data.demonXp;
                player.ImportSkills(data.skillLevel, data.skillXp, data.skillPoints, data.soulPoints, data.skillNodes);   // schopnosti patria k slotu, nie k poslednému hraniu
                player.FormTimerValue = data.formTimer;      // odpočet odrátavania levelov pokračuje tam, kde skončil
                player.SoulProgressValue = data.soulProgress;
                Sanity.Set(data.sanity > 0f ? data.sanity : 100f);
                Propaganda.Set(data.heat, data.nextBroadcastDay);
                DemonRaid.Recent = data.recentRaid;
            }

            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            // Poloha sa obnoví len ak je pod ňou zem (scéna sa mohla zmeniť, uložená poloha môže byť v prázdne)
            if (movement != null && Physics.Raycast(data.playerPosition + Vector3.up * 3f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore))
                movement.Teleport(data.playerPosition, data.playerForward);
            else if (movement != null)
                Debug.LogWarning("WorldSave: uložená poloha hráča nemá pod sebou zem, hráč ostáva na štarte.");
        }

        for (int i = 0; i < data.factionIds.Count && i < data.factionTrust.Count; i++)
        {
            var id = (FactionId)data.factionIds[i];
            Reputation.Add(id, data.factionTrust[i] - Reputation.Get(id));
        }
        Reputation.AddSuspicion(data.suspicion - Reputation.Suspicion);

        foreach (ZonePopulation zone in FindObjectsByType<ZonePopulation>())
            foreach (ZonePopulation.SaveData zd in data.zones)
                if (zd.zone == zone.ZoneName) zone.Import(zd);

        foreach (BuildPlot plot in BuildPlot.All)
            foreach (BuildPlot.SaveData pd in data.plots)
                if (pd.plotId == plot.PlotId) plot.Import(pd);

        foreach (string name in data.deadDemons) deadDemons.Add(name);   // (platí len pre bossov, pozri nižšie)
        foreach (DemonEnemy demon in FindObjectsByType<DemonEnemy>(FindObjectsInactive.Include))
        {
            if (demon.tier == DemonTier.Boss && data.deadDemons.Contains(demon.name)) { Destroy(demon.gameObject); continue; }

            foreach (BossPosition boss in data.bosses)
                if (boss.name == demon.name)
                {
                    demon.transform.position = boss.position;
                    BossPost post = demon.GetComponent<BossPost>();
                    if (post != null && boss.home != Vector3.zero) post.SetHome(boss.home);   // starší save nemá home
                    Rigidbody rb = demon.GetComponent<Rigidbody>();
                    if (rb != null) rb.position = boss.position;
                }
        }

        DemonTree.RestoreAll(data.trees);
        DemonNature.Restore(data.hardness);
        WeaponUpgrades.Restore(data.weaponUpgrades);
        ToolKit.Restore(data.tools);
        ProfessionTrust.Restore(data.professionTrust != null ? data.professionTrust.ToArray() : null);
        SettlementEconomy.ImportAll(data.extra);
        BossRegistry.Import(data.bossStates);
        PopulationPressure.Refugees = data.refugees;
        PopulationPressure.NatureDamage = data.natureDamage;
        if (PortalStability.Instance != null) PortalStability.Instance.Import(data.portalStability, data.portalOutageUntil);

        GameHUD.Message($"Stav sveta načítaný (deň {data.day}).", 3f);
    }
}
