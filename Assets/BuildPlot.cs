using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlotState { ForSale, Clearing, Ready, Building, Built, Ruined }

/// <summary>
/// Parcela (štvorec) v meste. Postup: kúpiť (cena + dôvera frakcie) → vyčistenie (čaká sa) → postaviť stavbu (materiál, peniaze, level,
/// dôvera; trvá podľa počtu civilistov) → stavba stojí. Zničená stavba = ruiny; odstránenie za poplatok, potom znova vyčistenie.
/// Stav sa ukladá (WorldSave). Ovláda sa cez PlotManager (kláves pri parcele).
/// </summary>
public class BuildPlot : MonoBehaviour
{
    public static readonly List<BuildPlot> All = new List<BuildPlot>();

    [SerializeField] private string plotId = "P_00";
    [Tooltip("Frakcia, v ktorej území parcela leží (určuje dostupné stavby a dôveru)")]
    [SerializeField] private FactionId owner = FactionId.Civilians;
    [SerializeField] private float side = 18f;
    [SerializeField] private int price = 150;
    [SerializeField] private float requiredTrust = 0f;
    [SerializeField] private float clearSeconds = 30f;
    [SerializeField] private int ruinFee = 80;

    private PlotState state = PlotState.ForSale;
    private string buildingId;
    private float phaseTotal;
    private float phaseLeft;
    private GameObject visual;
    private ZonePopulation zone;
    private int appliedHousing;
    private PopulationGroup appliedGroup;
    private float health;
    private float nextDamageMessage;
    private float nextAlert;
    private bool contaminated;
    private float cleanseLeft;
    private int raidedNights;

    public const float CleanseSeconds = 120f;
    public static int ContaminatedCount { get { int n = 0; foreach (BuildPlot p in All) if (p.contaminated) n++; return n; } }
    public bool Contaminated => contaminated;
    public bool Cleansing => contaminated && cleanseLeft > 0f;
    public float CleanseLeft => cleanseLeft;
    public int RaidedNights => raidedNights;
    public int CleanseMoney => Mathf.RoundToInt(300f * Difficulty.CleanseCost);

    public string PlotId => plotId;
    public FactionId Owner => owner;
    public float Side => side;
    public int Price => price;
    public float RequiredTrust => requiredTrust;
    public int RuinFee => ruinFee;
    public PlotState State => state;
    public BuildingDef Building => BuildingCatalog.Get(buildingId);
    public float SecondsLeft => phaseLeft;
    /// <summary>Názov zóny, v ktorej parcela leží (napr. "Mesto odboja").</summary>
    public string ZoneName => zone != null ? zone.ZoneName : "Mesto";
    public float Progress01 => phaseTotal > 0.01f ? 1f - phaseLeft / phaseTotal : 1f;
    /// <summary>Zdravie postavenej stavby 0..1 (0 = zničená).</summary>
    public float Health01 => Building != null && Building.health > 0f ? Mathf.Clamp01(health / Building.health) : 0f;

    public void Configure(string id, FactionId faction, int cost, float trust)
    {
        plotId = id;
        owner = faction;
        price = cost;
        requiredTrust = trust;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    void Start()
    {
        foreach (ZonePopulation z in FindObjectsByType<ZonePopulation>())
            if (z.Contains(transform.position)) { zone = z; break; }

        Rebuild();
    }

    void Update()
    {
        if (Cleansing)
        {
            cleanseLeft -= Time.deltaTime;
            if (cleanseLeft <= 0f)
            {
                cleanseLeft = 0f;
                contaminated = false;
                raidedNights = 0;
                Rebuild();
                GameHUD.Message($"Parcela {plotId} je očistená od kontaminácie.", 4f);
            }
        }
        if (state != PlotState.Clearing && state != PlotState.Building) return;

        phaseLeft -= Time.deltaTime;
        if (state == PlotState.Building && visual != null) UpdateScaffold();
        if (phaseLeft > 0f) return;

        phaseLeft = 0f;
        SetState(state == PlotState.Clearing ? PlotState.Ready : PlotState.Built);
        if (state == PlotState.Built)
        {
            GameHUD.Message($"Stavba dokončená: {Building?.displayName}", 4f);
            PlayerStats builder = FindAnyObjectByType<PlayerStats>();
            if (builder != null) { builder.AddSoulProgress(1f); builder.AddSkillXp(40); }   // stavanie dáva XP
            GameAudio.Play(GameAudio.Sfx.Build, transform.position);
        }
        else GameHUD.Message($"Parcela {plotId} je vyčistená a pripravená.", 3f);
    }

    // ---------- Akcie ----------
    public bool Buy(PlayerStats player, out string message)
    {
        message = null;
        if (state != PlotState.ForSale) { message = "Parcela nie je na predaj."; return false; }
        if (Reputation.Get(owner) < requiredTrust) { message = $"Chýba dôvera ({requiredTrust:0})."; return false; }
        if (!player.TrySpend(price)) { message = $"Nemáš dosť peňazí ({price})."; return false; }

        StartPhase(PlotState.Clearing, clearSeconds * CityPopulation.ConstructionTimeFactor);
        return true;
    }

    public bool CanBuild(BuildingDef def, PlayerStats player, Inventory inventory, out string reason)
    {
        reason = null;
        if (state != PlotState.Ready) { reason = "Parcela nie je pripravená."; return false; }
        if (player.humanLevel < def.minLevel) { reason = $"Level {def.minLevel}"; return false; }
        if (Reputation.Get(owner) < def.minTrust) { reason = $"Dôvera {def.minTrust:0}"; return false; }
        if (def.requiresProfession != Profession.None && ProfessionTrust.Get(def.requiresProfession) < def.requiresProfessionTrust)
        {
            reason = $"Dôvera: {ProfessionTrust.Name(def.requiresProfession)} {def.requiresProfessionTrust:0}";
            return false;
        }
        if (player.money < def.cost) { reason = $"Peniaze {def.cost}"; return false; }
        if (inventory == null) { reason = "Inventár"; return false; }

        if (Missing(inventory, ItemCatalog.Wood, def.wood, out reason)) return false;
        if (Missing(inventory, ItemCatalog.Stone, def.stone, out reason)) return false;
        if (Missing(inventory, ItemCatalog.Scrap, def.scrap, out reason)) return false;
        if (Missing(inventory, ItemCatalog.Crystal, def.crystal, out reason)) return false;
        return true;
    }

    static bool Missing(Inventory inventory, string itemId, int need, out string reason)
    {
        reason = null;
        if (need <= 0 || inventory.CountOf(itemId) >= need) return false;
        reason = $"{ItemCatalog.Get(itemId).displayName} {need}";
        return true;
    }

    public bool StartBuilding(BuildingDef def, PlayerStats player, Inventory inventory, out string message)
    {
        if (!CanBuild(def, player, inventory, out message)) return false;

        player.TrySpend(def.cost);
        inventory.TryRemove(ItemCatalog.Wood, def.wood);
        inventory.TryRemove(ItemCatalog.Stone, def.stone);
        inventory.TryRemove(ItemCatalog.Scrap, def.scrap);
        inventory.TryRemove(ItemCatalog.Crystal, def.crystal);

        buildingId = def.id;
        StartPhase(PlotState.Building, def.buildSeconds * CityPopulation.ConstructionTimeFactor * ProfessionTrust.BuildSpeedFactor);
        return true;
    }

    public bool RemoveRuins(PlayerStats player, out string message)
    {
        message = null;
        if (state != PlotState.Ruined) return false;
        if (contaminated) { message = "Pôda je kontaminovaná: najprv ju očisti rituálom."; return false; }
        if (!player.TrySpend(ruinFee)) { message = $"Nemáš dosť peňazí ({ruinFee})."; return false; }

        buildingId = null;
        StartPhase(PlotState.Clearing, clearSeconds * CityPopulation.ConstructionTimeFactor);
        return true;
    }

    /// <summary>Očistný rituál kontaminovanej pôdy: peniaze + vzácne suroviny, potom 2 minúty trvá.</summary>
    public bool Cleanse(PlayerStats player, Inventory inventory, out string message)
    {
        message = null;
        if (!contaminated) return false;
        if (cleanseLeft > 0f) { message = "Rituál už prebieha."; return false; }
        int money = CleanseMoney;
        if (inventory == null || player.money < money || inventory.CountOf(ItemCatalog.Crystal) < 3 || inventory.CountOf(ItemCatalog.Gold) < 1)
        {
            message = $"Rituál vyžaduje {money} peňazí, 3 kryštály a 1 zlato.";
            return false;
        }
        player.TrySpend(money);
        inventory.TryRemove(ItemCatalog.Crystal, 3);
        inventory.TryRemove(ItemCatalog.Gold, 1);
        cleanseLeft = CleanseSeconds;
        Rebuild();
        return true;
    }

    /// <summary>Ráno po nočnom útoku: stavba bola napadnutá (poč. nocí) a ak padla, pôda sa môže kontaminovať.</summary>
    public void ApplyRaidOutcome(bool attacked, bool destroyedNow)
    {
        if (!attacked) { raidedNights = Mathf.Max(0, raidedNights - 1); return; }
        raidedNights++;
        if (state == PlotState.Ruined && !contaminated && destroyedNow && raidedNights >= Difficulty.ContaminationNights)
        {
            contaminated = true;
            cleanseLeft = 0f;
            Rebuild();
            GameHUD.Message($"Pôda parcely {plotId} je trvalo kontaminovaná. Obnova vyžaduje očistný rituál.", 6f);
        }
    }

    // Obrana: veža, barikáda alebo kasárne do 60 m
    public bool IsDefended()
    {
        foreach (BuildPlot other in All)
        {
            if (other == this || other.state != PlotState.Built || other.Building == null) continue;
            BuildingDef d = other.Building;
            if (d.defenseRange <= 0f && d.id != BuildingCatalog.CorpBarracks) continue;

            // Chráni len vlastná strana (korporácia nebráni mesto odboja a naopak) a len funkčná obrana
            if ((other.owner == FactionId.Megacorp) != (owner == FactionId.Megacorp)) continue;
            DefenseTower tower = other.GetComponent<DefenseTower>();
            if (tower != null && (!tower.enabled || (tower.needsPower && !Power.Online))) continue;
            Vector3 diff = other.transform.position - transform.position;
            diff.y = 0f;
            if (diff.sqrMagnitude <= 60f * 60f) return true;
        }
        return false;
    }

    /// <summary>Stavba je zničená (démoni, výbuch): z parcely ostanú ruiny a bývanie sa odoberie.</summary>
    public void DestroyBuilding()
    {
        if (state != PlotState.Built) return;
        SetState(PlotState.Ruined);
        GameHUD.Message($"Stavba na parcele {plotId} je zničená.", 4f);
    }

    // ---------- Stav ----------
    void StartPhase(PlotState next, float seconds)
    {
        phaseTotal = Mathf.Max(1f, seconds);
        phaseLeft = phaseTotal;
        SetState(next);
    }

    /// <summary>Poškodenie stavby (démoni, korene stromov). Pri nule sa stavba zničí a ostanú ruiny.</summary>
    public void TakeDamage(float amount, GameObject source)
    {
        if (state != PlotState.Built || amount <= 0f) return;

        amount *= SoulBalance.StructureDamageFactor;
        health -= amount;
        Alert(source);
        if (Time.time >= nextDamageMessage && health > 0f)
        {
            nextDamageMessage = Time.time + 15f;
            GameHUD.Message($"Stavba na parcele {plotId} je napadnutá ({Health01 * 100f:0} %).", 3f);
        }
        if (health <= 0f) DestroyBuilding();
    }

    // Modely ruín sú bez textúry (biele): dostanú kamenný materiál z PlotManager
    static void ApplyRuinMaterial(GameObject piece)
    {
        Material stone = PlotManager.RuinMaterial;
        if (stone == null) return;

        foreach (Renderer r in piece.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = stone;
            r.sharedMaterials = mats;
        }
    }

    /// <summary>Úder démona (hráča) do stavby: svedkovia ho nahlásia, po zničení dostane odmenu.</summary>
    public void TakeDamageFromPlayer(float amount, PlayerStats player)
    {
        if (state != PlotState.Built || amount <= 0f) return;

        health -= amount;
        bool destroyed = health <= 0f;
        if (destroyed) DestroyBuilding();
        DemonRaid.OnPlayerHitBuilding(this, player, destroyed);
    }

    // Útok na stavbu: vojaci v okolí sa rozbehnú k démonovi a civilisti pri parcele utekajú
    void Alert(GameObject attacker)
    {
        if (Time.time < nextAlert) return;
        nextAlert = Time.time + 5f;

        DemonEnemy demon = attacker != null ? attacker.GetComponent<DemonEnemy>() : null;
        if (demon != null) SoldierAI.ReportDemonNear(transform.position, 80f, demon);
        CivilianAI.Panic(transform.position, 25f);
    }

    // Obranná stavba (veža, barikáda) dostane strieľajúci komponent, kým stojí
    void ApplyDefense()
    {
        BuildingDef def = Building;
        DefenseTower tower = GetComponent<DefenseTower>();

        if (state == PlotState.Built && def != null && def.defenseRange > 0f)
        {
            if (tower == null) tower = gameObject.AddComponent<DefenseTower>();
            tower.Configure(def.defenseRange, def.defenseDamage, def.defenseInterval, def.size.y);
            tower.needsPower = def.id == BuildingCatalog.CorpTower;
            tower.enabled = true;
        }
        else if (tower != null) tower.enabled = false;
    }

    /// <summary>Najbližšia postavená stavba v dosahu (vodorovne), alebo null.</summary>
    public static BuildPlot NearestBuilt(Vector3 position, float range)
    {
        BuildPlot best = null;
        float bestDistance = range * range;

        foreach (BuildPlot plot in All)
        {
            if (plot.state != PlotState.Built) continue;

            Vector3 d = plot.transform.position - position;
            d.y = 0f;
            if (d.sqrMagnitude < bestDistance)
            {
                best = plot;
                bestDistance = d.sqrMagnitude;
            }
        }
        return best;
    }

    void SetState(PlotState next)
    {
        if (next == PlotState.Built) health = Building != null ? Building.health : 0f;
        state = next;
        ApplyDefense();
        ApplyHousing();
        Rebuild();
    }

    // Bývanie sa pripočíta, len kým stavba stojí
    void ApplyHousing()
    {
        if (zone != null && appliedHousing != 0)
        {
            zone.AddHousing(appliedGroup, -appliedHousing);
            appliedHousing = 0;
        }

        BuildingDef def = Building;
        if (zone != null && state == PlotState.Built && def != null && def.housing > 0)
        {
            appliedHousing = def.housing;
            appliedGroup = def.housingFor;
            zone.AddHousing(appliedGroup, appliedHousing);
        }
    }

    // ---------- Ukladanie ----------
    [Serializable]
    public class SaveData
    {
        public string plotId;
        public int state;
        public string buildingId;
        public float total, left, health;
        public bool contaminated;
        public float cleanse;
        public int raided;
    }

    public SaveData Export() => new SaveData { plotId = plotId, state = (int)state, buildingId = buildingId, total = phaseTotal, left = phaseLeft, health = health, contaminated = contaminated, cleanse = cleanseLeft, raided = raidedNights };

    public void Import(SaveData data)
    {
        buildingId = string.IsNullOrEmpty(data.buildingId) ? null : data.buildingId;
        contaminated = data.contaminated;
        cleanseLeft = data.cleanse;
        raidedNights = data.raided;
        phaseTotal = data.total;
        phaseLeft = data.left;
        SetState((PlotState)data.state);
        if (state == PlotState.Built && data.health > 0f) health = data.health;
    }

    // ---------- Vzhľad ----------
    void Rebuild()
    {
        if (visual != null) Destroy(visual);
        visual = new GameObject("PlotVisual");
        visual.transform.SetParent(transform, false);

        float half = side * 0.5f - 1f;
        switch (state)
        {
            case PlotState.ForSale:
                Post("Tabula", new Vector3(-half, 0f, -half), new Vector3(0.25f, 2.2f, 0.25f), new Color(0.9f, 0.75f, 0.2f), true);
                Post("Tabula2", new Vector3(-half, 2f, -half), new Vector3(1.6f, 0.9f, 0.1f), new Color(0.9f, 0.75f, 0.2f), true);
                break;

            case PlotState.Clearing:
                for (int i = 0; i < 7; i++)
                {
                    var r = new System.Random(plotId.GetHashCode() + i);
                    Vector3 at = new Vector3((float)(r.NextDouble() * 2 - 1) * half, 0f, (float)(r.NextDouble() * 2 - 1) * half);

                    // Čistenie ruín: hromady trosiek (tehly, stavebný odpad) z balíka, inak kvádre
                    GameObject rubble = PickRubble(r);
                    if (rubble != null && PlaceModel(rubble, at, 2f + (float)r.NextDouble() * 1.8f, (float)r.NextDouble() * 360f, true, false) != null) continue;

                    Post("Sutina", at, new Vector3(1.5f + (float)r.NextDouble() * 1.5f, 0.6f + (float)r.NextDouble(), 1.5f + (float)r.NextDouble() * 1.5f), new Color(0.3f, 0.26f, 0.22f), true);
                }
                break;

            case PlotState.Ready:
                foreach (Vector2 c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                    Post("Stlpik", new Vector3(c.x * half, 0f, c.y * half), new Vector3(0.3f, 1.2f, 0.3f), new Color(0.85f, 0.85f, 0.85f), true);
                break;

            case PlotState.Building:
                BuildScaffold();
                break;

            case PlotState.Built:
                BuildBuilding();
                break;

            case PlotState.Ruined:
                BuildRuins();
                break;
        }

        if (contaminated) BuildContamination(half);
    }

    // Kontaminácia: tmavá fialová škvrna na zemi, ostré krištáľové tŕne a slabá žiara; počas rituálu sa rozsvieti
    void BuildContamination(float half)
    {
        bool ritual = Cleansing;
        Color dark = ritual ? new Color(0.35f, 0.15f, 0.6f) : new Color(0.12f, 0.04f, 0.16f);
        Post("Kontaminacia", Vector3.zero, new Vector3(side - 1f, 0.06f, side - 1f), dark, false);
        var r = new System.Random(plotId.GetHashCode() + 77);
        for (int i = 0; i < 9; i++)
        {
            Vector3 at = new Vector3((float)(r.NextDouble() * 2 - 1) * half, 0.05f, (float)(r.NextDouble() * 2 - 1) * half);
            float h = 0.8f + (float)r.NextDouble() * 1.8f;
            Post("Tren", at, new Vector3(0.35f, h, 0.35f), ritual ? new Color(0.7f, 0.5f, 1f) : new Color(0.28f, 0.08f, 0.38f), false);
        }
        var glow = new GameObject("KontaminaciaSvetlo");
        glow.transform.SetParent(visual.transform, false);
        glow.transform.localPosition = Vector3.up * 1.2f;
        var light = glow.AddComponent<Light>();
        light.color = new Color(0.6f, 0.2f, 0.9f);
        light.range = side * 0.8f;
        light.intensity = ritual ? 2.2f : 0.7f;
    }

    void Post(string name, Vector3 basePos, Vector3 size, Color color, bool collider)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(visual.transform, false);
        g.transform.localPosition = basePos + Vector3.up * (size.y * 0.5f);
        g.transform.localScale = size;
        if (!collider) Destroy(g.GetComponent<Collider>());
        g.GetComponent<Renderer>().material.color = color;
    }

    GameObject PickRubble(System.Random r)
    {
        GameObject[] all = PlotManager.RubblePrefabs;
        if (all == null || all.Length == 0) return null;

        for (int attempt = 0; attempt < all.Length; attempt++)
        {
            GameObject g = all[r.Next(all.Length)];
            if (g != null) return g;
        }
        return null;
    }

    // Model z prefabu do vizuálu parcely: mierka na najdlhší rozmer (alebo výšku, ak upright = dlhá os sa postaví zvislo), základ na zemi,
    // otočenie okolo stredu, kolízia z rozmerov v lokálnom priestore. Vráti null, ak model nemá vykreslenie.
    GameObject PlaceModel(GameObject prefab, Vector3 basePos, float targetSize, float yaw, bool collider, bool upright)
    {
        if (prefab == null || visual == null) return null;

        GameObject g = Instantiate(prefab, visual.transform);
        LightMeshTable.Apply(g);
        g.name = prefab.name;
        g.transform.localPosition = Vector3.zero;
        g.transform.localRotation = Quaternion.identity;
        g.transform.localScale = Vector3.one;

        Renderer[] rs = g.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) { Destroy(g); return null; }

        Bounds b = WorldBounds(rs);
        if (upright)
        {
            if (b.size.x > b.size.y && b.size.x >= b.size.z) g.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            else if (b.size.z > b.size.y && b.size.z > b.size.x) g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            b = WorldBounds(rs);
        }

        float longest = upright ? b.size.y : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        g.transform.localScale = Vector3.one * (targetSize / Mathf.Max(0.001f, longest));
        b = WorldBounds(rs);

        Vector3 worldBase = visual.transform.TransformPoint(basePos);
        g.transform.position += new Vector3(worldBase.x - b.center.x, worldBase.y - b.min.y, worldBase.z - b.center.z);
        if (collider) AddLocalBoundsCollider(g);
        g.transform.RotateAround(worldBase, Vector3.up, yaw);
        return g;
    }

    static Bounds WorldBounds(Renderer[] rs)
    {
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    // Kolízia v lokálnom priestore objektu (funguje aj pre natočený model; svetové bounds by sa pri natočení otočili druhýkrát)
    static void AddLocalBoundsCollider(GameObject root)
    {
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
        Bounds bounds = default;
        bool first = true;
        foreach (Renderer r in rs)
        {
            Bounds lb = r.localBounds;
            Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; } else bounds.Encapsulate(p);
            }
        }

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size;
    }

    private Transform scaffoldBody;

    void BuildScaffold()
    {
        BuildingDef def = Building;
        Vector3 size = def != null ? def.size : new Vector3(10f, 5f, 10f);
        float half = side * 0.5f - 1f;
        var rnd = new System.Random(plotId.GetHashCode());
        GameObject beam = PlotManager.BeamPrefab;
        foreach (Vector2 c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
        {
            // Lešenie z drevených trámov (zvislé) a pri stavbe poukladané tehly a odpad; bez modelov kvádre
            Vector3 at = new Vector3(c.x * half * 0.6f, 0f, c.y * half * 0.6f);
            if (beam == null || PlaceModel(beam, at, size.y + 1f, (float)rnd.NextDouble() * 360f, true, true) == null)
                Post("Lesenie", at, new Vector3(0.25f, size.y + 1f, 0.25f), new Color(0.55f, 0.4f, 0.2f), true);

            GameObject pile = PickRubble(rnd);
            if (pile != null) PlaceModel(pile, new Vector3(c.x * half * 0.92f, 0f, c.y * half * 0.92f), 1.5f + (float)rnd.NextDouble(), (float)rnd.NextDouble() * 360f, true, false);
        }

        // Dva trámy poležiačky pri stavbe
        if (beam != null)
            for (int i = 0; i < 2; i++)
                PlaceModel(beam, new Vector3((i == 0 ? -1f : 1f) * half * 0.35f, 0f, -half * 0.95f), 3.2f, 70f + 40f * i, true, false);

        Post("Telo", Vector3.zero, new Vector3(size.x, 0.2f, size.z), (def != null ? def.color : Color.gray) * 0.7f, false);   // bez kolízie: rastúci blok by hráča zatlačil alebo uväznil
        scaffoldBody = visual.transform.Find("Telo");
        UpdateScaffold();
    }

    // Stavba rastie podľa postupu
    void UpdateScaffold()
    {
        BuildingDef def = Building;
        if (scaffoldBody == null || def == null) return;

        float h = Mathf.Max(0.2f, def.size.y * Progress01);
        scaffoldBody.localScale = new Vector3(def.size.x, h, def.size.z);
        scaffoldBody.localPosition = Vector3.up * (h * 0.5f);
    }

    void BuildBuilding()
    {
        BuildingDef def = Building;
        if (def == null) return;

        if (def.id == BuildingCatalog.Farm) { BuildFarm(); return; }

        if (def.id == BuildingCatalog.House) BuildHouse(def);   // dom je dutý: dá sa vojsť dnu
        else Post("Stavba", Vector3.zero, def.size, def.color, true);
        Post("Strecha", Vector3.up * def.size.y, new Vector3(def.size.x + 0.6f, 0.4f, def.size.z + 0.6f), def.roofColor, false);

        // Neónový pás pre stavby s neónovou strechou (Megakorporácia)
        if (def.faction == FactionId.Megacorp)
        {
            GameObject strip = visual.transform.Find("Strecha").gameObject;
            Material m = strip.GetComponent<Renderer>().material;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", def.roofColor * 1.6f);
        }

        if (def.id != BuildingCatalog.House) PlaceProps(def);   // dom má len interiér: stôl, 6 stoličiek a pec
    }

    // Pole: orná pôda s hriadkami (úroda rastie podľa FarmSystem), plot, strašiak a maličký prístrešok; farmárov kreslí FarmVisual
    void BuildFarm()
    {
        float half = 6.5f;
        Post("Pole", Vector3.zero, new Vector3(13f, 0.25f, 13f), new Color(0.24f, 0.16f, 0.09f), true);

        const int rows = 7;
        for (int i = 0; i < rows; i++)
        {
            float x = -5.4f + i * 1.8f;
            Post("Hriadka_" + i, new Vector3(x, 0.25f, 0f), new Vector3(0.95f, 0.2f, 12f), new Color(0.34f, 0.22f, 0.12f), false);
            Post("Rad_" + i, new Vector3(x, 0.45f, 0f), new Vector3(0.6f, 0.1f, 11.4f), new Color(0.25f, 0.55f, 0.2f), false);
        }

        // Plot z kolov a priečok
        Color wood = new Color(0.35f, 0.24f, 0.13f);
        for (int i = -3; i <= 3; i++)
        {
            float t = i * 2f;
            Post("Kol", new Vector3(t, 0.25f, half + 0.3f), new Vector3(0.18f, 1.1f, 0.18f), wood, false);
            Post("Kol", new Vector3(t, 0.25f, -half - 0.3f), new Vector3(0.18f, 1.1f, 0.18f), wood, false);
            Post("Kol", new Vector3(half + 0.3f, 0.25f, t), new Vector3(0.18f, 1.1f, 0.18f), wood, false);
            Post("Kol", new Vector3(-half - 0.3f, 0.25f, t), new Vector3(0.18f, 1.1f, 0.18f), wood, false);
        }
        Post("Priecka", new Vector3(0f, 0.85f, half + 0.3f), new Vector3(13.6f, 0.1f, 0.1f), wood, false);
        Post("Priecka", new Vector3(0f, 0.85f, -half - 0.3f), new Vector3(13.6f, 0.1f, 0.1f), wood, false);
        Post("Priecka", new Vector3(half + 0.3f, 0.85f, 0f), new Vector3(0.1f, 0.1f, 13.6f), wood, false);
        Post("Priecka", new Vector3(-half - 0.3f, 0.85f, 0f), new Vector3(0.1f, 0.1f, 13.6f), wood, false);

        // Strašiak
        Post("StrasiakKol", new Vector3(-4.2f, 0.25f, 3.5f), new Vector3(0.14f, 2.0f, 0.14f), wood, false);
        Post("StrasiakRuky", new Vector3(-4.2f, 1.7f, 3.5f), new Vector3(1.4f, 0.12f, 0.12f), wood, false);
        Post("StrasiakHlava", new Vector3(-4.2f, 2.05f, 3.5f), new Vector3(0.4f, 0.4f, 0.4f), new Color(0.8f, 0.7f, 0.4f), false);
        Post("StrasiakKlobuk", new Vector3(-4.2f, 2.4f, 3.5f), new Vector3(0.6f, 0.12f, 0.6f), new Color(0.3f, 0.2f, 0.1f), false);

        // Prístrešok na náradie v rohu
        Post("Pristresok", new Vector3(5.2f, 0.25f, -5.2f), new Vector3(2.2f, 1.8f, 2.2f), new Color(0.42f, 0.3f, 0.18f), true);
        Post("PristresokStrecha", new Vector3(5.2f, 2.05f, -5.2f), new Vector3(2.8f, 0.2f, 2.8f), new Color(0.3f, 0.15f, 0.1f), false);

        visual.AddComponent<FarmVisual>().Init(plotId, rows);
    }

    // Dekorácie okolo stavby (modely z Stavby_do_hry podľa frakcie): rozmiestnené po obvode, rozloženie je pri každej parcele stále
    void PlaceProps(BuildingDef def)
    {
        GameObject[] props = PlotManager.PropsFor(def.faction == FactionId.None ? owner : def.faction);
        if (props == null || props.Length == 0) return;

        var r = new System.Random(plotId.GetHashCode() ^ 0x5bd1);
        float ring = Mathf.Max(def.size.x, def.size.z) * 0.5f + 1.5f;
        int count = Mathf.Min(6, 3 + props.Length / 2);

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = props[r.Next(props.Length)];
            // Stoličky a kamene pôsobia vonku pri dome rušivo (už uložené zoznamy v scéne ich ešte môžu obsahovať)
            if (prefab == null || prefab.name.Contains("chair", System.StringComparison.OrdinalIgnoreCase)
                || prefab.name.Contains("stone", System.StringComparison.OrdinalIgnoreCase)) continue;

            float angle = (i + (float)r.NextDouble() * 0.6f) / count * Mathf.PI * 2f;
            Vector3 position = new Vector3(Mathf.Cos(angle) * ring, 0f, Mathf.Sin(angle) * ring);
            position.x = Mathf.Clamp(position.x, -side * 0.5f + 1f, side * 0.5f - 1f);
            position.z = Mathf.Clamp(position.z, -side * 0.5f + 1f, side * 0.5f - 1f);

            GameObject prop = Instantiate(prefab, visual.transform);
            prop.transform.localPosition = position;
            AddBoundsCollider(prop);
            prop.transform.localRotation = Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f);
        }
    }

    // Modely z FBX nemajú kolíziu: pridá sa kváder podľa veľkosti vykreslenia (volať, kým má model nulové natočenie)
    static void AddBoundsCollider(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        Vector3 scale = root.transform.lossyScale;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(bounds.center);
        box.size = new Vector3(bounds.size.x / Mathf.Max(0.0001f, scale.x), bounds.size.y / Mathf.Max(0.0001f, scale.y), bounds.size.z / Mathf.Max(0.0001f, scale.z));
    }

    // Obytný dom: podlaha, štyri steny s dverami na južnej strane (-z), vo vnútri stôl a stoličky. Strechu pridá BuildBuilding.
    void BuildHouse(BuildingDef def)
    {
        const float t = 0.4f;                  // hrúbka steny
        const float doorWidth = 2.4f, doorHeight = 2.6f;
        float hx = def.size.x * 0.5f, hz = def.size.z * 0.5f, h = def.size.y;
        Color wall = def.color;

        Post("Podlaha", Vector3.zero, new Vector3(def.size.x - 0.2f, 0.1f, def.size.z - 0.2f), new Color(0.3f, 0.24f, 0.18f), true);

        Post("SteneZadna", new Vector3(0f, 0f, hz - t * 0.5f), new Vector3(def.size.x, h, t), wall, true);
        Post("StenaVlavo", new Vector3(-hx + t * 0.5f, 0f, 0f), new Vector3(t, h, def.size.z - 2f * t), wall, true);
        Post("StenaVpravo", new Vector3(hx - t * 0.5f, 0f, 0f), new Vector3(t, h, def.size.z - 2f * t), wall, true);

        // Predná stena s priechodom: dva kusy a preklad nad dverami
        float segment = (def.size.x - doorWidth) * 0.5f;
        float segmentCenter = doorWidth * 0.5f + segment * 0.5f;
        Post("StenaPredna1", new Vector3(-segmentCenter, 0f, -hz + t * 0.5f), new Vector3(segment, h, t), wall, true);
        Post("StenaPredna2", new Vector3(segmentCenter, 0f, -hz + t * 0.5f), new Vector3(segment, h, t), wall, true);
        Post("Preklad", new Vector3(0f, doorHeight, -hz + t * 0.5f), new Vector3(doorWidth, h - doorHeight, t), wall, true);

        // Krb pri zadnej stene (model z balíka) s komínom: kamenný stĺp na vonkajšej strane zadnej steny, ktorý vyčnieva nad strechu
        float fireX = -hx * 0.45f;
        GameObject fireplace = PlotManager.FireplacePrefab;
        if (fireplace != null)
            PlaceModel(fireplace, new Vector3(fireX, 0.1f, hz - t - 0.55f), 1.9f, PlotManager.FireplaceYaw, true, false);
        else
            Post("Krb", new Vector3(fireX, 0f, hz - t - 0.4f), new Vector3(1.6f, 1.1f, 0.8f), new Color(0.35f, 0.33f, 0.32f), true);

        Color brick = new Color(0.4f, 0.34f, 0.3f);
        Post("Komin", new Vector3(fireX, 0f, hz + 0.45f), new Vector3(1.1f, h + 1.7f, 0.9f), brick, true);
        Post("KominHlava", new Vector3(fireX, h + 1.7f, hz + 0.45f), new Vector3(1.4f, 0.25f, 1.2f), brick * 0.8f, false);

        // Stôl uprostred (doska + štyri nohy)
        Color wood = new Color(0.45f, 0.3f, 0.18f);
        Post("StolDoska", new Vector3(0f, 0.72f, 0f), new Vector3(2.2f, 0.1f, 1.1f), wood, true);
        foreach (Vector2 leg in new[] { new Vector2(-0.95f, -0.4f), new Vector2(0.95f, -0.4f), new Vector2(-0.95f, 0.4f), new Vector2(0.95f, 0.4f) })
            Post("StolNoha", new Vector3(leg.x, 0f, leg.y), new Vector3(0.1f, 0.72f, 0.1f), wood, true);

        // Šesť stoličiek okolo stola, čelom k stolu: dve na každej dlhej strane a jedna na každom konci
        foreach (Vector2 pos in new[] { new Vector2(-0.55f, 0.95f), new Vector2(0.55f, 0.95f), new Vector2(-0.55f, -0.95f), new Vector2(0.55f, -0.95f), new Vector2(1.6f, 0f), new Vector2(-1.6f, 0f) })
            PlaceChair(new Vector3(pos.x, 0.05f, pos.y), wood);
    }

    // Stolička: model zo Stavby_do_hry (PlotManager.ChairPrefab), inak sedadlo s operadlom z kvádrov
    void PlaceChair(Vector3 position, Color wood)
    {
        Vector3 toTable = -new Vector3(position.x, 0f, position.z);
        Quaternion facing = Quaternion.LookRotation(toTable.sqrMagnitude > 0.01f ? toTable.normalized : Vector3.forward);

        GameObject prefab = PlotManager.ChairPrefab;
        if (prefab != null)
        {
            GameObject chair = Instantiate(prefab, visual.transform);
            chair.transform.localPosition = position;
            chair.transform.localRotation = Quaternion.identity;
            AddBoundsCollider(chair);
            chair.transform.localRotation = facing * Quaternion.Euler(0f, PlotManager.ChairYawOffset, 0f);
            return;
        }

        GameObject root = new GameObject("Stolicka");
        root.transform.SetParent(visual.transform, false);
        root.transform.localPosition = position;
        root.transform.localRotation = facing;

        Transform saved = visual.transform;
        visual = root;   // Post() pridáva diely do aktuálneho "visual"
        Post("Sedadlo", new Vector3(0f, 0.42f, 0f), new Vector3(0.45f, 0.06f, 0.45f), wood, true);
        Post("Operadlo", new Vector3(0f, 0.48f, -0.2f), new Vector3(0.45f, 0.5f, 0.05f), wood, true);
        foreach (Vector2 leg in new[] { new Vector2(-0.18f, -0.18f), new Vector2(0.18f, -0.18f), new Vector2(-0.18f, 0.18f), new Vector2(0.18f, 0.18f) })
            Post("Noha", new Vector3(leg.x, 0f, leg.y), new Vector3(0.05f, 0.42f, 0.05f), wood, false);
        visual = saved.gameObject;
    }

    // Ruiny kopírujú pôdorys zničenej stavby: dva kusy múru na každej strane, rovnobežne s obvodom (otočenie len o násobky 90°, vzpriamené),
    // a pár úlomkov vnútri. Modely ruín (tehlové múry zo Stavby_do_hry) sa použijú, ak ich PlotManager má, inak kvádre.
    void BuildRuins()
    {
        BuildingDef def = Building;
        Vector3 size = def != null ? def.size : new Vector3(10f, 5f, 10f);
        float hx = size.x * 0.5f, hz = size.z * 0.5f;
        var r = new System.Random(plotId.GetHashCode());
        Color stone = new Color(0.28f, 0.26f, 0.25f);

        // Modely s dverami v názve sa vynechajú: vchod robí voľná medzera v múre
        var usable = new System.Collections.Generic.List<GameObject>();
        GameObject[] all = PlotManager.RuinPrefabs;
        if (all != null)
            foreach (GameObject g in all)
                if (g != null && !g.name.Contains("door", System.StringComparison.OrdinalIgnoreCase) && !g.name.Contains("gate", System.StringComparison.OrdinalIgnoreCase))
                    usable.Add(g);
        GameObject[] prefabs = usable.ToArray();
        int index = 0;
        for (int sideIndex = 0; sideIndex < 4; sideIndex++)
            for (int k = 0; k < 2; k++, index++)
            {
                bool alongX = sideIndex < 2;                    // strany ±z bežia pozdĺž osi X, strany ±x pozdĺž Z
                float sign = sideIndex % 2 == 0 ? 1f : -1f;
                float along = (k == 0 ? -0.5f : 0.5f) * (alongX ? hx : hz) * (0.8f + 0.4f * (float)r.NextDouble());
                Vector3 pos = alongX ? new Vector3(along, 0f, sign * hz) : new Vector3(sign * hx, 0f, along);
                Quaternion rot = Quaternion.Euler(0f, alongX ? (sign > 0f ? 0f : 180f) : (sign > 0f ? 90f : 270f), 0f);

                // Južná strana (-z) = vchod ako v dome: medzi kusmi múru ostane voľný priechod
                bool entrance = alongX && sign < 0f;
                const float entranceHalf = 1.4f;
                float sideLength = alongX ? hx : hz;

                if (prefabs != null && prefabs.Length > 0 && prefabs[index % prefabs.Length] != null)
                {
                    GameObject piece = Instantiate(prefabs[index % prefabs.Length], visual.transform);
                    ApplyRuinMaterial(piece);
                    piece.transform.localPosition = pos;
                    piece.transform.localRotation = Quaternion.identity;
                    if (entrance)
                    {
                        // Kus sa odsunie tak, aby jeho vnútorný okraj bol za priechodom
                        float half = 1f;
                        Renderer[] rs = piece.GetComponentsInChildren<Renderer>();
                        if (rs.Length > 0)
                        {
                            Bounds b = rs[0].bounds;
                            for (int j = 1; j < rs.Length; j++) b.Encapsulate(rs[j].bounds);
                            half = b.extents.x;
                        }
                        float shifted = (k == 0 ? -1f : 1f) * Mathf.Max(Mathf.Abs(along), entranceHalf + half);
                        piece.transform.localPosition = new Vector3(shifted, 0f, sign * hz);
                    }
                    AddBoundsCollider(piece);                    // kolízia sa počíta bez natočenia
                    piece.transform.localRotation = rot;
                }
                else
                {
                    float len = 3.5f;
                    if (entrance) pos.x = (k == 0 ? -1f : 1f) * Mathf.Max(Mathf.Abs(along), entranceHalf + len * 0.5f);
                    Vector3 s = alongX ? new Vector3(len, 0.8f + (float)r.NextDouble() * 1.8f, 0.6f) : new Vector3(0.6f, 0.8f + (float)r.NextDouble() * 1.8f, len);
                    Post("Ruina", pos, s, stone, true);
                }
            }

        // Úlomky vnútri
        for (int i = 0; i < 4; i++)
        {
            Vector3 pos = new Vector3((float)(r.NextDouble() * 2 - 1) * hx * 0.6f, 0f, (float)(r.NextDouble() * 2 - 1) * hz * 0.6f);
            Post("Ulomok", pos, new Vector3(0.6f + (float)r.NextDouble() * 0.8f, 0.3f + (float)r.NextDouble() * 0.5f, 0.6f + (float)r.NextDouble() * 0.8f), stone, true);
        }
    }
}
