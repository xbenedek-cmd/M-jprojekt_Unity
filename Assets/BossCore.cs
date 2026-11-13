using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>Pamäť porazených bossov (id → výsledok). Ukladá sa do WorldSave.bossStates; neskôr ju číta napr. Arakani.</summary>
public static class BossRegistry
{
    private static readonly Dictionary<string, string> defeated = new Dictionary<string, string>();
    private static readonly Dictionary<string, int> day = new Dictionary<string, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { defeated.Clear(); day.Clear(); }

    public static bool IsDefeated(string id) => defeated.ContainsKey(id);
    /// <summary>"killed", "spared", "defeated" alebo null, ak boss ešte nebol porazený.</summary>
    public static string Outcome(string id) => defeated.TryGetValue(id, out string o) ? o : null;
    public static int DefeatDay(string id) => day.TryGetValue(id, out int d) ? d : 0;

    public static void MarkDefeated(string id, string outcome = "defeated")
    {
        defeated[id] = outcome;
        DayNightCycle clock = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
        day[id] = clock != null ? clock.Day : 0;
    }

    public static string Export()
    {
        var sb = new StringBuilder();
        foreach (KeyValuePair<string, string> kv in defeated)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append('=').Append(day.TryGetValue(kv.Key, out int d) ? d : 0);
        }
        return sb.ToString();
    }

    public static void Import(string data)
    {
        defeated.Clear();
        day.Clear();
        if (string.IsNullOrEmpty(data)) return;
        foreach (string part in data.Split('|'))
        {
            string[] f = part.Split('=');
            if (f.Length < 2) continue;
            defeated[f[0]] = f[1];
            if (f.Length > 2 && int.TryParse(f[2], out int d)) day[f[0]] = d;
        }
    }
}

/// <summary>
/// Spoločná kostra bossov (Ren aj klanové elity): fázy podľa zdravia, slučka útokov, telegrafovanie (svetlo + zvuk), omráčenie,
/// okná zraniteľnosti, zásahové zóny, strop zdravia pred finišerom (QTE panel) a zápis porážky. Odvodený boss implementuje len
/// NextAttack() a OnPhaseEnter(); poškodenie upravuje cez ModifyIncoming(). Zdravie je v Damageable a nikdy nezíde na nulu cez bežný
/// zásah: dokončí ho finišer.
/// </summary>
[RequireComponent(typeof(Damageable))]
public abstract class BossCore : MonoBehaviour
{
    public static BossCore Current { get; protected set; }

    [Header("Boss")]
    [SerializeField] protected string bossId = "boss";
    [SerializeField] protected string bossName = "Boss";
    [SerializeField] protected float maxHealth = 1500f;
    [Tooltip("Fáza n+1 začína, keď zdravie klesne na tento podiel")]
    [SerializeField] protected float[] phaseThresholds = { 0.7f, 0.4f };
    [Tooltip("Pod týmto podielom zdravia sa spustí finišer (QTE panel); 0 = žiadny")]
    [SerializeField, Range(0f, 0.5f)] protected float finisherAt = 0.1f;
    [SerializeField] protected float arenaRadius = 30f;
    [SerializeField] protected BossModelRig rig;

    public string BossId => bossId;
    public string BossName => bossName;
    public int Phase { get; protected set; } = 1;
    public bool InFight { get; protected set; }
    public bool FinisherRunning { get; protected set; }
    public bool IsStunned => Time.time < stunUntil;
    public Damageable Health { get; private set; }
    public float HealthFraction => Health == null ? 0f : Health.Health / Mathf.Max(1f, Health.maxHealth);
    public Vector3 ArenaCenter { get; set; }
    private string debugState = "";
    /// <summary>Čo boss práve robí (ladenie, testy, prípadné HUD). Zmena vyvolá StateChanged.</summary>
    public string DebugState
    {
        get => debugState;
        protected set
        {
            if (debugState == value) return;
            debugState = value;
            StateChanged?.Invoke(this, value);
        }
    }
    public static event Action<BossCore, string> StateChanged;

    protected PlayerStats player;
    protected Rigidbody body;
    private BossHitZone[] zones;
    private Renderer[] flashers;
    private MaterialPropertyBlock block;
    private Coroutine fightLoop, attackRoutine;
    private bool attackDone;
    private int pendingPhase;
    private bool finisherRequested;
    private float stunUntil;
    private float windowUntil, windowMultiplier = 1f;
    private float flashUntil;
    private bool bypassFilter;
    /// <summary>Zásahová zóna, ktorú posledný zásah trafil (null = zásah inde); platí v ModifyIncoming.</summary>
    protected BossHitZone LastHitZone { get; private set; }

    protected virtual void Awake()
    {
        Health = GetComponent<Damageable>();
        Health.team = CombatTeam.Demon;
        Health.displayName = bossName;
        Health.SetMaxHealth(maxHealth);
        Health.DamageFilter = Filter;
        body = GetComponent<Rigidbody>();
        if (rig == null) rig = GetComponentInChildren<BossModelRig>();
        zones = GetComponentsInChildren<BossHitZone>(true);
        Renderer[] all = rig != null && rig.flashRenderers != null && rig.flashRenderers.Length > 0 ? rig.flashRenderers : GetComponentsInChildren<Renderer>(true);
        var list = new List<Renderer>(all.Length);
        foreach (Renderer rd in all)
            if (rd != null && (rig == null || rig.ventRenderers == null || Array.IndexOf(rig.ventRenderers, rd) < 0)) list.Add(rd);   // ventily svietia samostatne
        flashers = list.ToArray();
        block = new MaterialPropertyBlock();
        ArenaCenter = transform.position;
    }

    /// <summary>Boss začal telegrafovať úder (dĺžka varovania, farba); počúva ho vizuál bossa (animácia útoku, aura).</summary>
    public event Action<float, Color> Telegraphed;

    /// <summary>Pridá renderery, ktoré majú blikať pri telegrafovaní (model doplnený až za behu).</summary>
    public void AddFlashRenderers(Renderer[] extra)
    {
        if (extra == null || extra.Length == 0) return;
        var merged = new Renderer[flashers.Length + extra.Length];
        flashers.CopyTo(merged, 0);
        extra.CopyTo(merged, flashers.Length);
        flashers = merged;
    }

    protected virtual void OnEnable() { FactionAbility.OnAbilityUsed += HandleAbility; }
    protected virtual void OnDisable() { FactionAbility.OnAbilityUsed -= HandleAbility; }

    // ---------- Priebeh súboja ----------

    public void BeginFight()
    {
        if (InFight || BossRegistry.IsDefeated(bossId)) return;
        player = FindAnyObjectByType<PlayerStats>();
        InFight = true;
        FinisherRunning = false;
        finisherRequested = false;
        Phase = 1;
        pendingPhase = 0;
        stunUntil = 0f;
        windowUntil = 0f;
        Current = this;
        Health.SetMaxHealth(maxHealth);
        OnFightBegin();
        fightLoop = StartCoroutine(FightLoop());
    }

    /// <summary>Hráč zomrel alebo odišiel: boss sa vráti do východzieho stavu a súboj sa dá začať znova.</summary>
    public void AbortFight()
    {
        if (!InFight) return;
        InFight = false;
        if (fightLoop != null) StopCoroutine(fightLoop);
        if (attackRoutine != null) StopCoroutine(attackRoutine);
        fightLoop = attackRoutine = null;
        OnAttackInterrupted();
        OnFightAborted();
        Health.SetMaxHealth(maxHealth);
        if (Current == this) Current = null;
    }

    protected void FinishFight()
    {
        InFight = false;
        if (fightLoop != null) StopCoroutine(fightLoop);
        if (attackRoutine != null) StopCoroutine(attackRoutine);
        fightLoop = attackRoutine = null;
        if (Current == this) Current = null;
    }

    IEnumerator FightLoop()
    {
        while (InFight)
        {
            if (player == null || player.IsDead) { AbortFight(); yield break; }
            while (MangaPanels.Active || MangaDialog.Active) yield return null;   // scény počas panelov a dialógov

            if (pendingPhase > 0)
            {
                int p = pendingPhase;
                pendingPhase = 0;
                Phase = p;
                yield return OnPhaseEnter(p);
            }
            if (finisherRequested)
            {
                finisherRequested = false;
                FinisherRunning = true;
                yield return RunFinisher();
                yield break;
            }
            if (IsStunned) { yield return StunRoutine(); continue; }

            attackDone = false;
            attackRoutine = StartCoroutine(Wrap(NextAttack()));
            while (!attackDone && !IsStunned && !finisherRequested && pendingPhase == 0 && InFight) yield return null;
            if (!attackDone)
            {
                if (attackRoutine != null) StopCoroutine(attackRoutine);
                OnAttackInterrupted();
            }
            attackRoutine = null;
        }
    }

    IEnumerator Wrap(IEnumerator inner)
    {
        yield return inner;
        attackDone = true;
    }

    IEnumerator StunRoutine()
    {
        DebugState = "stun";
        OnStunned();
        while (IsStunned && InFight) { Flash(new Color(1f, 0.95f, 0.3f), 0.1f); yield return null; }
        OnStunEnded();
    }

    // ---------- Pre odvodené triedy ----------

    /// <summary>Jeden útok vrátane telegrafovania a pauzy po ňom; slučka ho volá dookola.</summary>
    protected abstract IEnumerator NextAttack();
    protected virtual IEnumerator OnPhaseEnter(int phase) { yield break; }
    protected virtual IEnumerator RunFinisher() { yield break; }
    protected virtual void OnFightBegin() { }
    protected virtual void OnFightAborted() { }
    protected virtual void OnAttackInterrupted() { }
    protected virtual void OnStunned() { }
    protected virtual void OnStunEnded() { }
    protected virtual void OnAbility(AbilityKind kind, Vector3 position) { }
    protected virtual void OnZoneHit(BossHitZone zone, float damage) { }

    /// <summary>Poškodenie pred zásahom do zdravia: štít, brnenie, slabiny. Vrátené 0 = blok.</summary>
    protected virtual float ModifyIncoming(float amount, GameObject attacker, DamageKind kind) => amount;

    void HandleAbility(AbilityKind kind, Vector3 position)
    {
        if (InFight) OnAbility(kind, position);
    }

    // ---------- Poškodenie ----------

    float Filter(float amount, GameObject attacker, DamageKind kind)
    {
        if (!InFight || FinisherRunning || MangaPanels.Active) return 0f;

        LastHitZone = null;
        if (!bypassFilter)
        {
            // Zásahová zóna na modeli (útočník stojí pri nej)
            BossHitZone zone = attacker != null ? ZoneAt(attacker.transform.position) : null;
            LastHitZone = zone;
            if (zone != null)
            {
                amount *= zone.multiplier;
                GameHUD.WorldText(zone.transform.position + Vector3.up * 0.5f, $"{zone.zoneId.ToUpper()} ×{zone.multiplier:0}", new Color(1f, 0.4f, 0.2f));
                OnZoneHit(zone, amount);
            }

            if (Time.time < windowUntil) amount *= windowMultiplier;
            amount = ModifyIncoming(amount, attacker, kind);
            if (amount <= 0f) return 0f;
        }

        // Zdravie neklesne pod strop finišera; dorazí ho panel (nikdy nie bežný zásah)
        float floor = Mathf.Max(1f, finisherAt * Health.maxHealth);
        float room = Health.Health - floor;
        if (room <= 0.01f) { finisherRequested = true; return 0f; }
        if (amount >= room) { amount = room; finisherRequested = true; }

        // Fázy podľa zdravia po tomto zásahu
        float after = (Health.Health - amount) / Mathf.Max(1f, Health.maxHealth);
        int target = 1;
        for (int i = 0; i < phaseThresholds.Length; i++) if (after <= phaseThresholds[i]) target = i + 2;
        if (target > Phase && target > pendingPhase) pendingPhase = target;
        return amount;
    }

    BossHitZone ZoneAt(Vector3 position)
    {
        for (int i = 0; i < zones.Length; i++)
        {
            BossHitZone z = zones[i];
            if (!z.active) continue;
            Vector3 d = z.transform.position - position;
            d.y = 0f;   // zóna je vysoko na chrbte, útočník stojí na zemi
            if (d.sqrMagnitude <= z.radius * z.radius) return z;
        }
        return null;
    }

    protected void SetZones(string id, bool active)
    {
        for (int i = 0; i < zones.Length; i++) if (zones[i].zoneId == id) zones[i].active = active;
    }

    /// <summary>Okno zraniteľnosti po ťažkom útoku: všetko poškodenie je väčšie.</summary>
    protected void OpenWindow(float seconds, float multiplier)
    {
        windowUntil = Time.time + seconds;
        windowMultiplier = multiplier;
    }

    protected void CloseWindow() { windowUntil = 0f; }
    protected bool WindowOpen => Time.time < windowUntil;

    /// <summary>Omráči bossa (preruší aktuálny útok).</summary>
    public void Stun(float seconds)
    {
        stunUntil = Mathf.Max(stunUntil, Time.time + seconds);
    }

    // ---------- Telegrafovanie a pomocné ----------

    /// <summary>Svetelné (a zvukové) varovanie pred úderom; model bliká danou farbou, jas rastie k zásahu.</summary>
    protected IEnumerator Telegraph(float seconds, Color color)
    {
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        Telegraphed?.Invoke(seconds, color);
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            Flash(color, Mathf.Lerp(0.3f, 1f, t / seconds) * (0.6f + 0.4f * Mathf.Sin(t * 28f)));
            yield return null;
        }
        Flash(color, 0f);
    }

    protected void Flash(Color color, float strength)
    {
        flashUntil = Time.time + 0.12f;
        ApplyFlash(color * Mathf.Max(0f, strength));
    }

    void ApplyFlash(Color emission)
    {
        for (int i = 0; i < flashers.Length; i++)
        {
            Renderer r = flashers[i];
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", emission);
            r.SetPropertyBlock(block);
        }
    }

    protected virtual void LateUpdate()
    {
        if (flashUntil > 0f && Time.time > flashUntil)
        {
            flashUntil = 0f;
            ApplyFlash(Color.black);
        }
    }

    protected Vector3 FlatToPlayer()
    {
        Vector3 d = player.transform.position - transform.position;
        d.y = 0f;
        return d;
    }

    protected void FaceTowards(Vector3 flatDirection, float degreesPerSecond)
    {
        if (flatDirection.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degreesPerSecond * Time.deltaTime);
    }

    /// <summary>Posun po aréne (nikdy mimo nej).</summary>
    protected void Move(Vector3 flatDirection, float speed)
    {
        Vector3 p = transform.position + flatDirection.normalized * speed * Time.deltaTime;
        Vector3 off = p - ArenaCenter;
        off.y = 0f;
        if (off.magnitude > arenaRadius) p = ArenaCenter + off.normalized * arenaRadius + Vector3.up * (p.y - ArenaCenter.y);
        transform.position = p;
    }

    protected void HurtPlayer(float damage)
    {
        if (player != null && !player.IsDead) player.TakeDamage(damage, DamageSource.Normal);
    }

    /// <summary>Posledný výsledok z PlayPanels (null = panely sa nespustili alebo neskončili výsledkom).</summary>
    protected PanelResult LastPanelResult { get; private set; }

    /// <summary>Prehrá manga panely (QTE, voľba) a počká na ich koniec; spoločné pre všetky finišery.</summary>
    protected IEnumerator PlayPanels(string fightId, PanelPage[] pages)
    {
        LastPanelResult = null;
        bool done = false;
        if (!MangaPanels.Play(fightId, PanelContext.Boss, pages, r => { LastPanelResult = r; done = true; })) done = true;
        while (!done) yield return null;
    }

    /// <summary>Beží bez zásahu hráča: bezpečný stav po dokončení (zdravie na nulu neklesne, boss sa len vypne).</summary>
    protected void DefeatCleanup(string outcome)
    {
        BossRegistry.MarkDefeated(bossId, outcome);
        FinishFight();
    }

    /// <summary>Boss si sám ublíži (spätný náraz): obíde filtre, ale strop pred finišerom a fázy platia ďalej.</summary>
    protected void SelfDamage(float amount)
    {
        if (!InFight || FinisherRunning || amount <= 0f) return;
        float floor = Mathf.Max(1f, finisherAt * Health.maxHealth);
        float room = Health.Health - floor;
        if (room <= 0.01f) { finisherRequested = true; return; }
        if (amount >= room) { amount = room; finisherRequested = true; }
        float after = (Health.Health - amount) / Mathf.Max(1f, Health.maxHealth);
        int target = 1;
        for (int i = 0; i < phaseThresholds.Length; i++) if (after <= phaseThresholds[i]) target = i + 2;
        if (target > Phase && target > pendingPhase) pendingPhase = target;
        bypassFilter = true;
        Health.TakeDamage(amount, null, DamageKind.Normal);
        bypassFilter = false;
    }

    /// <summary>Ladenie a testy: nastaví zdravie na podiel (fázy sa prepnú samy).</summary>
    public void DebugSetHealthFraction(float fraction)
    {
        float target = Mathf.Max(1f, Health.maxHealth * fraction);
        float dmg = Health.Health - target;
        if (dmg <= 0f) return;
        bypassFilter = true;
        Health.TakeDamage(dmg, null, DamageKind.Normal);
        bypassFilter = false;
    }
}
