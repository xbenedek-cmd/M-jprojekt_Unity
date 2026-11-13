using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Arakani (GDD kap. 4 a 7): nočný hlas v hlave hráča.
/// - Každú noc zadá jeden rozkaz (OrderTarget v scéne). Splnenie zvýši Dôveru (Favor), XP a peniaze.
/// - Nesplnený rozkaz: Dôvera klesá, narastá Neposlušnosť -> degradácia tela (max HP, útok, výdrž),
///   telo v noci trpí a od určitej neposlušnosti pošle Popravcov.
/// - Dôvera odomyká dary (menej krvi, silnejší v noci, Závoj na kláves Z).
/// - Pri plnej Dôvere + démonickom Lv.10 ponúkne Rituál Pekelnej Krvi (bod bez návratu).
/// Daj ho na GameManager (vedľa DayNightCycle).
/// </summary>
public class ArakaniFavor : MonoBehaviour
{
    public static ArakaniFavor Instance { get; private set; }

    /// <summary>Menu rituálu je otvorené (blokuje boj a schopnosti cez Healer.MenuOpen).</summary>
    public static bool MenuOpen { get; private set; }

    public static event System.Action OnRitualAccepted;
    public static event System.Action<bool> OnOrderFinished;   // true = splnený

    [Header("Hlas")]
    [Tooltip("Vypni, ak chceš hru bez Arakaniho (napr. pred prologom)")]
    public bool voiceActive = true;
    [Tooltip("Po očistnom rituále u lekárky hlas na toľko dní stíchne")]
    public int silenceDaysAfterCure = 3;
    [Tooltip("Sekundy od začiatku noci do prvého rozkazu")]
    public float orderDelay = 6f;

    [Header("Dôvera (Favor)")]
    [Range(0f, 100f)] public float favor = 10f;
    public float failPenalty = 10f;
    public float cureFavorLoss = 25f;

    [Header("Neposlušnosť a degradácia")]
    public int disobedience;
    public int maxDegradeLevel = 4;
    [Tooltip("Za každý stupeň: -8 % max HP a poškodenia")]
    public float degradePerLevel = 0.08f;
    public float staminaDegradePerLevel = 0.06f;
    [Tooltip("Po koľkých sekundách od rozkazu začne telo trpieť")]
    public float sufferGrace = 60f;
    public float sufferPerSecond = 1f;
    [Tooltip("Utrpenie nezabije: zastaví sa pod týmto podielom max HP")]
    [Range(0f, 1f)] public float sufferFloor = 0.25f;
    [Tooltip("Po koľkých nesplnených rozkazoch sa človek začne meniť na démona")]
    public int failuresToTransformation = 4;

    [Header("Popravcovia")]
    [Tooltip("Od akej neposlušnosti prichádzajú Popravcovia")]
    public int executionerThreshold = 3;
    public int maxExecutioners = 3;
    public float spawnDistance = 12f;
    public float executionerHealth = 150f;
    public float executionerDamage = 25f;
    public float executionerSpeed = 3.8f;
    [Tooltip("Zapni, ak má byť Popravca nezraniteľný voči bežným zbraniam")]
    public bool executionerImmune = false;
    public DamageKind executionerWeakness = DamageKind.Fire;
    [Tooltip("Vlastný model Popravcu (prázdne = tmavočervená kapsula)")]
    public GameObject executionerPrefab;

    [Header("Dary Arakaniho (podľa Dôvery)")]
    public float bloodGiftFavor = 25f;       // -15 % míňanie krvi
    public float nightGiftFavor = 50f;       // +10 % poškodenie v noci (len démon)
    public float nightGiftDamage = 0.10f;
    public float veilGiftFavor = 75f;        // Závoj: neviditeľnosť na kláves Z (len démon)
    public KeyCode veilKey => KeyBindings.Get("veil", KeyCode.Z);
    public float veilBloodCost = 15f;
    public float veilCooldown = 60f;
    public float veilSeconds = 6f;

    [Header("Rituál Pekelnej Krvi")]
    public float ritualFavor = 100f;
    public int ritualDemonLevel = 10;
    public bool ritualOnlyAtNight = true;
    public int ritualRefuseDays = 7;
    public float sealedDamageBonus = 1.5f;
    public float sealedHpBonus = 1.4f;
    public float sealedBloodDrain = 0.5f;

    [Header("Debug")]

    // ---------- stav ----------
    public float Favor => favor;
    public bool Sealed => sealedByBlood;
    public bool VoiceHeard => voiceActive && !Silenced;
    /// <summary>
    /// Po očistnom rituáli u lekárky Arakani na pár dní stíchne. Pri úplnku (najsilnejší mesiac)
    /// má väčšiu moc a jeho hlas prerazí aj cez ticho.
    /// </summary>
    public bool Silenced => dayNight != null && dayNight.Day < silencedUntilDay
                            && dayNight.CurrentMoon != DayNightCycle.MoonPhase.FullMoon;

    /// <summary>Je Arakani po očistení umlčaný, ale úplnok jeho ticho práve prelomil?</summary>
    public bool MoonBreaksSilence => dayNight != null && dayNight.Day < silencedUntilDay
                                     && dayNight.CurrentMoon == DayNightCycle.MoonPhase.FullMoon;
    public bool OrderActive => orderActive;
    public OrderTarget CurrentTarget => orderTarget;
    public string CurrentOrderText => orderActive ? orderText : null;
    public bool Suffering => suffering;
    public int DegradeLevel => (sealedByBlood || !VoiceHeard) ? 0 : Mathf.Min(disobedience, maxDegradeLevel);

    /// <summary>Riadok pre HUD.</summary>
    public string HudText
    {
        get
        {
            string s = $"Arakani: Dôvera {favor:0}/100";
            if (sealedByBlood) return s + "  |  PRASTARÁ KRV";
            if (DegradeLevel > 0) s += $"  |  Degradácia {DegradeLevel}";
            return s;
        }
    }

    // Statické násobiče pre PlayerStats (bezpečné, aj keď ArakaniFavor v scéne nie je)
    public static float MaxHpFactor => Instance != null ? Instance.CalcMaxHp() : 1f;
    public static float DamageFactor => Instance != null ? Instance.CalcDamage() : 1f;
    public static float StaminaFactor => Instance != null ? Instance.CalcStamina() : 1f;
    public static float BloodDrainFactor => Instance != null ? Instance.CalcBloodDrain() : 1f;
    public static bool BloodSealed => Instance != null && Instance.sealedByBlood;

    /// <summary>Volá PlayerStats po očistnom rituále u lekárky.</summary>
    public static void NotifyCured()
    {
        if (Instance != null) Instance.OnCured();
    }

    public static void AddFavor(float amount)
    {
        if (Instance == null || Instance.sealedByBlood) return;
        Instance.favor = Mathf.Clamp(Instance.favor + amount, 0f, 100f);
    }

    public static void AddDisobedience(int amount = 1)
    {
        if (Instance != null && !Instance.sealedByBlood)
            Instance.disobedience = Mathf.Max(0, Instance.disobedience + amount);
    }

    // ---------- privátne ----------
    private PlayerStats stats;
    private DayNightCycle dayNight;

    private bool sealedByBlood;
    private int silencedUntilDay;
    private bool moonBreakShown;
    private int nextRitualDay;

    private bool nightActive;
    private bool issuedTonight;
    private float issueAt;

    private bool orderActive;
    private OrderTarget orderTarget;
    private OrderAction orderAction;
    private string orderText;
    private float orderIssuedAt;
    private float rFavor, rTrust, rSuspicion;
    private int rXp, rMoney;
    private FactionId rFaction;
    private bool suffering;
    private int failedOrders;

    private float veilReadyAt;
    private readonly List<GameObject> executioners = new List<GameObject>();

    private GUIStyle titleStyle, textStyle, smallStyle;

    static readonly string[] intros =
    {
        "Les ťa volá. ", "Počuj ma, Hybrid. ", "Zem krváca a ty to cítiš. ",
        "Nezaváhaj. ", "Je čas splatiť dlh. "
    };

    void Awake() => Instance = this;

    void OnEnable() => DayNightCycle.OnPhaseChanged += OnPhase;

    void OnDisable()
    {
        DayNightCycle.OnPhaseChanged -= OnPhase;
        MenuOpen = false;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        MenuOpen = false;
    }

    void Start()
    {
        stats = FindAnyObjectByType<PlayerStats>();
        dayNight = FindAnyObjectByType<DayNightCycle>();

        // Hra sa mohla začať už v noci
        if (dayNight != null && dayNight.IsNight) BeginNight();
    }

    // ---------- Násobiče ----------
    float CalcMaxHp() => (1f - degradePerLevel * DegradeLevel) * (sealedByBlood ? sealedHpBonus : 1f);

    float CalcDamage()
    {
        float f = 1f - degradePerLevel * DegradeLevel;
        if (stats != null && stats.IsDemon && favor >= nightGiftFavor && dayNight != null && dayNight.IsNight)
            f *= 1f + nightGiftDamage;
        if (sealedByBlood) f *= sealedDamageBonus;
        return f;
    }

    float CalcStamina() => Mathf.Max(0.3f, 1f - staminaDegradePerLevel * DegradeLevel);

    float CalcBloodDrain()
    {
        float f = (stats != null && stats.IsDemon && favor >= bloodGiftFavor) ? 0.85f : 1f;
        if (sealedByBlood) f *= sealedBloodDrain;
        return f;
    }

    // ---------- Noc a úsvit ----------
    void OnPhase(DayNightCycle.DayPhase phase)
    {
        if (dayNight == null) dayNight = FindAnyObjectByType<DayNightCycle>();

        if (phase == DayNightCycle.DayPhase.Night) BeginNight();
        else if (phase == DayNightCycle.DayPhase.Dawn) EndNight();
    }

    void BeginNight()
    {
        nightActive = true;
        issuedTonight = false;
        issueAt = Time.time + orderDelay;

        if (!VoiceHeard) return;

        if (!sealedByBlood && disobedience >= executionerThreshold)
            SpawnExecutioners();
    }

    void EndNight()
    {
        nightActive = false;
        suffering = false;
        DespawnExecutioners();

        if (orderActive) FailOrder("Slnko vychádza a rozkaz zostal nesplnený.");
    }

    // ---------- Update ----------
    void Update()
    {
        if (stats == null)
        {
            stats = FindAnyObjectByType<PlayerStats>();
            return;
        }

        if (stats.IsDead) return;

        // Úplnok prelomí pokoj, ktorý si si vykúpil u lekárky
        if (MoonBreaksSilence)
        {
            if (!moonBreakShown)
            {
                moonBreakShown = true;
                GameHUD.Message("Úplnok: Arakani má väčšiu moc, jeho hlas prerazil cez ticho!", 5f);
            }
        }
        else
        {
            moonBreakShown = false;
        }

        if (MenuOpen)
        {
            RitualInput();
            return;
        }

        if (!Healer.MenuOpen) VeilInput();
        if (!VoiceHeard) return;

        if (nightActive && !issuedTonight && !orderActive && Time.time >= issueAt)
            IssueOrder();

        if (orderActive) UpdateOrder();
        else suffering = false;

        CheckRitual();
    }

    // ---------- Rozkazy ----------
    OrderTarget PickTarget()
    {
        int day = dayNight != null ? dayNight.Day : 1;
        var pool = new List<OrderTarget>();
        foreach (OrderTarget t in OrderTarget.All)
            if (t != null && t.IsAvailable(day)) pool.Add(t);

        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
    }

    void IssueOrder()
    {
        issuedTonight = true;

        OrderTarget t = PickTarget();
        if (t == null)
        {
            Debug.LogWarning("ArakaniFavor: v scéne nie je žiadny dostupný OrderTarget, dnes nie je rozkaz.");
            return;
        }

        orderTarget = t;
        t.BeginTracking();
        orderAction = t.action;
        orderText = t.orderText;
        orderIssuedAt = Time.time;
        orderActive = true;

        rFavor = t.favorReward;
        rXp = t.xpReward;
        rMoney = t.moneyReward;
        rFaction = t.humanFaction;
        rTrust = t.humanTrustPenalty;
        rSuspicion = t.suspicion;

        GameHUD.Message("Arakani: " + intros[Random.Range(0, intros.Length)] + t.orderText, 7f);
        GameHUD.MangaPanel("ARAKANI", 0.25f);
    }

    void UpdateOrder()
    {
        // Cieľ zmizol zo scény: pri Destroy ho niekto zničil, inak rozkaz zrušíme bez trestu
        if (orderTarget == null)
        {
            if (orderAction == OrderAction.Destroy) CompleteOrder();
            else CancelOrder();
            return;
        }

        if (orderTarget.Tick(stats.transform, Time.deltaTime))
        {
            CompleteOrder();
            return;
        }

        // Telo trpí, ak sa rozkaz príliš dlho ignoruje
        suffering = !sealedByBlood
                    && Time.time - orderIssuedAt > sufferGrace
                    && stats.Health > stats.MaxHealth * sufferFloor;

        if (suffering)
            stats.TakeDamage(sufferPerSecond * Time.deltaTime, DamageSource.Starvation);
    }

    void CompleteOrder()
    {
        int day = dayNight != null ? dayNight.Day : 1;
        if (orderTarget != null) orderTarget.MarkDone(day);

        if (!sealedByBlood)
        {
            favor = Mathf.Clamp(favor + rFavor, 0f, 100f);
            disobedience = Mathf.Max(0, disobedience - 1);
        }

        stats.AddDemonXp(rXp);
        if (rMoney > 0) stats.AddMoney(rMoney);

        Reputation.Add(rFaction, -rTrust);
        Reputation.AddSuspicion(rSuspicion);

        GameHUD.Message(sealedByBlood
            ? "Arakani: Dobre."
            : $"Arakani: Dobre. Dôvera +{rFavor:0}", 4f);

        ClearOrder();
        OnOrderFinished?.Invoke(true);
    }

    void FailOrder(string reason)
    {
        if (orderTarget != null) orderTarget.ResetProgress();

        if (!sealedByBlood)
        {
            favor = Mathf.Clamp(favor - failPenalty, 0f, 100f);
            disobedience++;
            failedOrders++;
            if (!stats.IsDemon && failedOrders >= Mathf.Max(1, failuresToTransformation))
                stats.ForceArakaniTransformation();
        }

        string extra = "";
        if (!sealedByBlood)
        {
            if (disobedience >= executionerThreshold) extra = " Pošlem Popravcov.";
            else if (DegradeLevel > 0) extra = " Tvoje telo slabne.";
        }

        GameHUD.Message("Arakani: " + reason + extra, 6f);

        ClearOrder();
        OnOrderFinished?.Invoke(false);
    }

    void CancelOrder()
    {
        if (orderTarget != null) orderTarget.ResetProgress();
        ClearOrder();
    }

    void ClearOrder()
    {
        orderActive = false;
        orderTarget = null;
        suffering = false;
    }

    // ---------- Liečenie u lekárky ----------
    void OnCured()
    {
        if (sealedByBlood) return;

        favor = Mathf.Max(0f, favor - cureFavorLoss);
        silencedUntilDay = (dayNight != null ? dayNight.Day : 1) + silenceDaysAfterCure;
        if (orderActive) CancelOrder();

        GameHUD.Message("Hlas Arakaniho v tvojej hlave slabne...", 5f);
    }

    // ---------- Popravcovia ----------
    void SpawnExecutioners()
    {
        if (stats == null) return;

        int n = Mathf.Clamp(disobedience - executionerThreshold + 1, 1, maxExecutioners);

        for (int i = 0; i < n; i++)
        {
            float a = (i + Random.value) * Mathf.PI * 2f / n;
            Vector3 p = stats.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spawnDistance;

            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore))
                p = hit.point + Vector3.up;

            executioners.Add(CreateExecutioner(p));
        }

        GameHUD.Message("Arakani poslal Popravcov!", 5f);
        GameHUD.MangaPanel("POPRAVCA", 0.3f);
    }

    GameObject CreateExecutioner(Vector3 pos)
    {
        bool hasPrefab = executionerPrefab != null;
        GameObject go = hasPrefab ? Instantiate(executionerPrefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);

        // Najprv vypnutý: Awake komponentov sa musí spustiť až po nastavení hodnôt
        go.SetActive(false);
        go.name = "Popravca";
        go.transform.position = pos;

        if (!hasPrefab)
        {
            go.transform.localScale = new Vector3(1.2f, 1.6f, 1.2f);
            go.GetComponent<Renderer>().material.color = new Color(0.45f, 0.04f, 0.08f);
        }

        Damageable d = go.GetComponent<Damageable>();
        if (d == null) d = go.AddComponent<Damageable>();
        d.displayName = "Popravca";
        d.team = CombatTeam.Demon;
        d.maxHealth = executionerHealth;
        d.immuneToNormalWeapons = executionerImmune;
        d.weakness = executionerImmune ? executionerWeakness : DamageKind.Normal;
        d.showHealthBar = true;

        DemonEnemy e = go.GetComponent<DemonEnemy>();
        if (e == null) e = go.AddComponent<DemonEnemy>();
        e.tier = DemonTier.Higher;
        e.executioner = true;
        e.attackDamage = executionerDamage;
        e.moveSpeed = executionerSpeed;
        e.Provoke();

        go.SetActive(true);
        return go;
    }

    void DespawnExecutioners()
    {
        if (executioners.Count == 0) return;

        foreach (GameObject g in executioners)
            if (g != null) Destroy(g);
        executioners.Clear();

        GameHUD.Message("Popravcovia sa s úsvitom vytratili.", 4f);
    }

    // ---------- Dar: Závoj ----------
    void VeilInput()
    {
        if (!GameInput.GetKeyDown(veilKey)) return;
        if (!stats.IsDemon || favor < veilGiftFavor) return;

        if (Time.time < veilReadyAt)
        {
            GameHUD.Message($"Závoj sa obnoví o {veilReadyAt - Time.time:0} s", 1.5f);
            return;
        }
        if (!stats.TrySpendBlood(veilBloodCost))
        {
            GameHUD.Message("Nemáš dosť krvi na Závoj.", 1.5f);
            return;
        }

        veilReadyAt = Time.time + veilCooldown;
        stats.MakeInvisible(veilSeconds);
        GameHUD.Message("Závoj Arakaniho: nikto ťa nevidí.", 2f);
    }

    // ---------- Rituál Pekelnej Krvi ----------
    void CheckRitual()
    {
        if (sealedByBlood || !stats.IsDemon) return;
        if (!Story.CrossroadsReady || Inventory.MenuOpen) return;   // rozhodnutie o ceste až po 5. úlohe a nie počas inventára
        if (ritualOnlyAtNight && (dayNight == null || !dayNight.IsNight)) return;
        if (favor < ritualFavor || stats.demonLevel < ritualDemonLevel) return;
        if (dayNight != null && dayNight.Day < nextRitualDay) return;

        MenuOpen = true;
    }

    void RitualInput()
    {
        if (GameInput.GetKeyDown(KeyCode.Alpha1)) AcceptRitual();
        else if (GameInput.GetKeyDown(KeyCode.Alpha2)) RefuseRitual();
    }

    void AcceptRitual()
    {
        sealedByBlood = true;
        favor = 100f;
        disobedience = 0;
        MenuOpen = false;

        // Mesto ťa označí za cieľ číslo 1 (civilisti a rebeli ťa môžu vnímať inak)
        Reputation.Add(FactionId.Megacorp, -100f);
        Reputation.AddSuspicion(100f);

        GameHUD.Message("Vypil si Prastarú Krv. Cesty späť niet.", 7f);
        GameHUD.MangaPanel("PEKELNÁ KRV", 0.6f);
        OnRitualAccepted?.Invoke();
    }

    void RefuseRitual()
    {
        MenuOpen = false;
        favor = Mathf.Max(0f, favor - 40f);
        disobedience++;
        nextRitualDay = (dayNight != null ? dayNight.Day : 1) + ritualRefuseDays;

        GameHUD.Message("Arakani: Odmietol si dar. Ešte oľutuješ.", 6f);
    }

    // ---------- Debug ----------

    // ---------- Zobrazenie ----------
    void InitStyles()
    {
        if (titleStyle != null) return;

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.85f, 0.45f, 1f) }
        };
        textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = Color.white }
        };
        smallStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 1f, 1f, 0.75f) }
        };
    }

    static void Fill(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (stats == null || stats.IsDead) return;
        InitStyles();

        float s = Mathf.Clamp(Screen.height / 900f, 0.8f, 1.6f);
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        float vh = Screen.height / s;

        if (MenuOpen) DrawRitual(vw, vh);
        else if (orderActive && VoiceHeard) DrawOrder(vw);

        GUI.matrix = old;

        if (!MenuOpen && orderActive && orderTarget != null && VoiceHeard && orderTarget.action != OrderAction.KillSoldiers && orderTarget.action != OrderAction.DestroyBuildings && orderTarget.action != OrderAction.KillCivilians) DrawMarker();
    }

    void DrawOrder(float vw)
    {
        const float w = 400f, h = 84f;
        Rect r = new Rect((vw - w) * 0.5f, 10f, w, h);

        Fill(r, new Color(0f, 0f, 0f, 0.7f));
        Fill(new Rect(r.x, r.y, r.width, 2f), new Color(0.7f, 0.3f, 1f, 0.9f));

        GUI.Label(new Rect(r.x, r.y + 4f, w, 22f), "ROZKAZ ARAKANIHO", titleStyle);
        GUI.Label(new Rect(r.x + 10f, r.y + 26f, w - 20f, 24f), orderText, textStyle);

        string info = "do úsvitu";
        if (orderTarget != null)
        {
            bool located = orderTarget.action == OrderAction.Interact || orderTarget.action == OrderAction.Reach || orderTarget.action == OrderAction.Destroy;
            info = located ? $"{orderTarget.DistanceTo(stats.transform.position):0} m  |  do úsvitu" : $"postup {orderTarget.Progress * 100f:0} %  |  do úsvitu";

            if (orderTarget.action == OrderAction.Interact) info += $"  |  drž [{orderTarget.interactKey}] pri cieli";
            else if (orderTarget.action == OrderAction.Reach) info += "  |  zostaň pri cieli";
        }
        if (suffering) info = "TELO TRPÍ - splň rozkaz!";
        GUI.Label(new Rect(r.x, r.y + 50f, w, 20f), info, suffering ? textStyle : smallStyle);

        float p = orderTarget != null ? orderTarget.Progress : 0f;
        if (p > 0f)
        {
            Rect bar = new Rect(r.x + 10f, r.yMax - 10f, w - 20f, 5f);
            Fill(bar, new Color(0f, 0f, 0f, 0.8f));
            Fill(new Rect(bar.x, bar.y, bar.width * p, bar.height), new Color(0.8f, 0.35f, 1f));
        }
    }

    // Značka nad cieľom rozkazu (v pixeloch obrazovky)
    void DrawMarker()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(orderTarget.transform.position + Vector3.up * 2.5f);
        if (sp.z <= 0f) return;

        float x = sp.x, y = Screen.height - sp.y;
        if (x < 0f || x > Screen.width || y < 0f || y > Screen.height) return;

        Fill(new Rect(x - 7f, y - 7f, 14f, 14f), new Color(0.8f, 0.35f, 1f, 0.95f));
        GUI.Label(new Rect(x - 50f, y - 30f, 100f, 20f),
            $"{orderTarget.DistanceTo(stats.transform.position):0} m", smallStyle);
    }

    void DrawRitual(float vw, float vh)
    {
        const float w = 560f, h = 250f;
        Rect r = new Rect((vw - w) * 0.5f, (vh - h) * 0.5f, w, h);

        Fill(new Rect(0f, 0f, vw, vh), new Color(0f, 0f, 0f, 0.55f));
        Fill(r, new Color(0.08f, 0f, 0.03f, 0.95f));
        Fill(new Rect(r.x, r.y, r.width, 3f), new Color(0.8f, 0.05f, 0.1f));

        GUI.Label(new Rect(r.x, r.y + 12f, w, 28f), "RITUÁL PEKELNEJ KRVI", titleStyle);
        GUI.Label(new Rect(r.x + 20f, r.y + 46f, w - 40f, 90f),
            "Arakani ti ponúka vypiť svoju Prastarú Krv.\n" +
            "Získaš nepredstaviteľnú silu, no definitívne prestaneš byť človekom.\n" +
            "Mesto ťa označí za cieľ číslo 1 a návrat k ľuďom už nebude možný.", textStyle);

        GUI.Label(new Rect(r.x + 20f, r.y + 150f, w - 40f, 26f), "[1]  Vypiť krv (bod bez návratu)", textStyle);
        GUI.Label(new Rect(r.x + 20f, r.y + 180f, w - 40f, 26f), "[2]  Odmietnuť (stratíš Dôveru)", textStyle);
    }
}