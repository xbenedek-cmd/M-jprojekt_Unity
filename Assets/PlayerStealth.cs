using UnityEngine;

/// <summary>
/// Zakrádanie (človek aj démon):
///  - Ľavý Ctrl: čupnutie (prepínač). Čupnutý hráč je pomalší, nižší, menej viditeľný a menej počuteľný.
///  - Viditeľnosť závisí od pohybu, šprintu, tmy/tieňa, hluku (útok) a od toho, či si za chrbtom pozorovateľa.
///  - Ľavé tlačidlo myši zozadu na nič netušiaceho cieľa (čupnutý): tiché zabitie.
///    Funguje na civilistov, vojakov, zvieratá a nižších/stredných démonov. Vyšší démon, boss a bdelý cieľ nie.
///  - Svedkovia (civilisti, vojaci) zvyšujú podozrenie mesta, nájdené telo tiež (vojaci ho hľadajú).
/// Pridá sa na hráča sám pri štarte hry.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerStealth : MonoBehaviour
{
    public static PlayerStealth Instance { get; private set; }

    public KeyCode crouchKey => KeyBindings.Get("crouch", KeyCode.LeftControl);
    public KeyCode altCrouchKey => KeyBindings.Get("crouch2", KeyCode.C);
    [Header("Čupnutie")]
    [Tooltip("Zapnuté: Ctrl prepína. Vypnuté: čupíš, kým držíš Ctrl.")]
    public bool toggleCrouch = true;
    public float crouchSpeedFactor = 0.45f;
    [Range(0.3f, 1f)] public float crouchScaleY = 0.6f;
    public float scaleSpeed = 8f;
    [Tooltip("O koľko sa pri čupnutí zníži kamera")]
    public float cameraDrop = 0.5f;

    [Header("Viditeľnosť (1 = chôdza vo dne)")]
    public float crouchVisibility = 0.5f;
    public float standingStillVisibility = 0.8f;
    public float sprintVisibility = 1.5f;
    public float nightVisibility = 0.75f;
    public float shadeVisibility = 0.8f;
    public float noiseVisibility = 1.6f;
    public float noiseSeconds = 2.5f;
    [Tooltip("Keď si za chrbtom pozorovateľa, vidí ťa takto menej")]
    public float behindWatcherFactor = 0.5f;

    public KeyCode hideBodyKey => KeyBindings.Get("use", KeyCode.F);
    [Header("Skrývanie tiel")]
    public float hideBodyRange = 2.5f;

    [Header("Tiché zabitie")]
    public float takedownRange = 1.9f;
    [Tooltip("Do akého uhlu od chrbta cieľa musíš byť")]
    public float takedownBackAngle = 70f;
    public float takedownCooldown = 1.2f;
    public bool requireCrouch = true;
    public float witnessRadius = 14f;
    public float witnessFov = 140f;
    public int suspicionPerWitness = 15;
    public int maxWitnessSuspicion = 40;

    public bool IsCrouched { get; private set; }
    public bool IsCrouching => IsCrouched;   // starší názov, ostáva pre kompatibilitu
    public float Visibility { get; private set; } = 1f;
    public float SpeedFactor => IsCrouched ? crouchSpeedFactor * (1f + SkillTree.Sum(SkillStat.CrouchSpeed)) : 1f;

    /// <summary>Plynulé zníženie kamery pri čupnutí (CameraFollow).</summary>
    /// <summary>Do tohto času platí sérum proti pachu (zmenší dosah, z ktorého si hráča všímajú).</summary>
    public static float ScentMaskUntil;
    public const float ScentMaskFactor = 0.6f;

    public static float CameraDrop => Instance != null ? Instance.dropNow : 0f;

    private PlayerStats stats;
    private PlayerMovement movement;
    private Rigidbody rb;
    private Collider col;
    private DayNightCycle dayNight;

    private Vector3 baseScale;
    private float dropNow;
    private float noiseUntil;
    private float nextTakedown;
    private Damageable candidate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        PlayerStats p = FindAnyObjectByType<PlayerStats>();
        if (p == null) return;

        if (p.GetComponent<PlayerStealth>() == null) p.gameObject.AddComponent<PlayerStealth>();
        if (p.GetComponent<Wardrobe>() == null) p.gameObject.AddComponent<Wardrobe>();
    }

    void OnEnable() => Instance = this;

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        movement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        dayNight = FindAnyObjectByType<DayNightCycle>();
        baseScale = transform.localScale;
        visual = GetComponent<PlayerVisual>();
    }

    private PlayerVisual visual;

    // ---------- Pomocné pre ostatné skripty ----------
    /// <summary>Hluk (útok, výbuch): hráča na chvíľu vidno a počuť viac.</summary>
    public void MakeNoise() => noiseUntil = Time.time + noiseSeconds;

    /// <summary>
    /// Násobič dosahu, z ktorého ťa daný pozorovateľ zbadá (viditeľnosť + či si mu za chrbtom).
    /// Bez PlayerStealth je 1.
    /// </summary>
    public static float RangeFactor(Transform watcher)
    {
        PlayerStealth s = Instance;
        if (s == null || watcher == null) return 1f;

        float f = s.Visibility;
        f *= Mathf.Clamp(1f - SkillTree.Sum(SkillStat.StealthRange), 0.3f, 1f);   // strom schopností: Tichý krok
        if (Time.time < ScentMaskUntil) f *= ScentMaskFactor;   // sérum proti pachu
        f *= PlayerCover.VisibilityFrom(watcher.position);       // krytie pri stene (nie voči tým, čo sú naproti)

        Vector3 to = s.transform.position - watcher.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f && Vector3.Angle(watcher.forward, to) > 100f)
            f *= s.behindWatcherFactor;

        return f;
    }

    // ---------- Hlavná slučka ----------
    void Update()
    {
        if (stats == null) return;

        if (stats.IsDead)
        {
            SetCrouch(false, true);
            return;
        }

        if (!Healer.MenuOpen)
        {
            if (toggleCrouch)
            {
                if (GameInput.GetKeyDown(crouchKey) || GameInput.GetKeyDown(altCrouchKey)) SetCrouch(!IsCrouched, false);
            }
            else
            {
                SetCrouch(GameInput.GetKey(crouchKey) || GameInput.GetKey(altCrouchKey), false);
            }
        }

        UpdateScale();
        UpdateVisibility();
        if (!HandleHideBody()) UpdateTakedownPrompt();
        else candidate = null;
    }

    // ---------- Nosenie tiel (kláves „použiť“ pri mŕtvom človeku) ----------
    private BodyCarry carry;

    bool HandleHideBody()
    {
        if (carry == null)
        {
            carry = GetComponent<BodyCarry>();
            if (carry == null) carry = gameObject.AddComponent<BodyCarry>();
        }
        return carry.Tick();
    }

    void SetCrouch(bool want, bool force)
    {
        if (want == IsCrouched) return;

        // Pod nízkym stropom sa nedá postaviť
        if (!want && !force && CeilingBlocked())
        {
            GameHUD.Message("Nad tebou je nízky strop, nemôžeš sa postaviť.", 2f);
            return;
        }

        IsCrouched = want;
    }

    // Postava s modelom: čupnutie je zníženie kapsuly (nie škálovanie), takže sa nezmenší model ani meč
    bool UsesCapsuleCrouch => visual != null && visual.HasModel && col is CapsuleCollider;

    bool CeilingBlocked()
    {
        if (col == null) return false;

        if (UsesCapsuleCrouch)
        {
            float missing = visual.capsuleHeight - ((CapsuleCollider)col).height;
            Vector3 top = new Vector3(col.bounds.center.x, col.bounds.max.y, col.bounds.center.z);
            return Physics.Raycast(top, Vector3.up, missing + 0.1f, ~0, QueryTriggerInteraction.Ignore);
        }

        // Spodok ostáva na zemi: hore treba miesto do výšky postavy v stoji
        float standHalf = col.bounds.extents.y / Mathf.Max(0.01f, transform.localScale.y) * baseScale.y;
        float needed = 2f * standHalf - col.bounds.extents.y;
        return Physics.Raycast(transform.position, Vector3.up, needed + 0.1f, ~0, QueryTriggerInteraction.Ignore);
    }

    // Postava sa zníži (škálovaním), spodok ostáva na zemi
    void UpdateScale()
    {
        if (UsesCapsuleCrouch)
        {
            UpdateCapsuleCrouch();
            return;
        }

        float wantY = baseScale.y * (IsCrouched ? crouchScaleY : 1f);
        float oldY = transform.localScale.y;
        float newY = Mathf.MoveTowards(oldY, wantY, scaleSpeed * baseScale.y * Time.deltaTime);

        if (!Mathf.Approximately(newY, oldY))
        {
            float height = col != null ? col.bounds.size.y / Mathf.Max(0.01f, oldY) : 1f;   // výška pri škále 1
            float shift = (newY - oldY) * 0.5f * height;

            transform.localScale = new Vector3(baseScale.x, newY, baseScale.z);

            if (rb != null) rb.position += Vector3.up * shift;
            else transform.position += Vector3.up * shift;
        }

        float wantDrop = IsCrouched ? cameraDrop : 0f;
        dropNow = Mathf.MoveTowards(dropNow, wantDrop, 3f * Time.deltaTime);
    }

    // Spodok kapsuly ostáva na zemi, znižuje sa jej vrch; postava sa nakloní (alebo prehrá klip čupnutia)
    void UpdateCapsuleCrouch()
    {
        CapsuleCollider capsule = (CapsuleCollider)col;
        float stand = visual.capsuleHeight;
        float crouched = stand * crouchScaleY;

        float target = IsCrouched ? crouched : stand;
        float height = Mathf.MoveTowards(capsule.height, target, scaleSpeed * stand * Time.deltaTime);
        if (!Mathf.Approximately(height, capsule.height))
        {
            capsule.height = height;
            capsule.center = new Vector3(capsule.center.x, -stand * 0.5f + height * 0.5f, capsule.center.z);
        }

        if (visual.ModelAnimator != null)
            visual.ModelAnimator.SetCrouch(Mathf.InverseLerp(stand, crouched, height));

        float wantDrop = IsCrouched ? cameraDrop : 0f;
        dropNow = Mathf.MoveTowards(dropNow, wantDrop, 3f * Time.deltaTime);
    }

    void UpdateVisibility()
    {
        float v = 1f;

        if (IsCrouched) v *= crouchVisibility;

        float speed = 0f;
        if (rb != null)
        {
            Vector3 vel = rb.linearVelocity;
            vel.y = 0f;
            speed = vel.magnitude;
        }
        float walk = movement != null ? movement.moveSpeed : 5f;

        if (speed < 0.3f) v *= standingStillVisibility;
        else if (speed > walk * 1.4f) v *= sprintVisibility;

        if (dayNight != null)
        {
            if (dayNight.IsNight) v *= nightVisibility;
            else if (!dayNight.IsSunlit(transform.position)) v *= shadeVisibility;
        }

        if (Time.time < noiseUntil) v *= noiseVisibility;
        if (stats.IsInvisible) v = 0f;

        Visibility = v;
    }

    // ---------- Tiché zabitie ----------
    void UpdateTakedownPrompt()
    {
        candidate = null;
        if (Healer.MenuOpen || Time.time < nextTakedown) return;
        if (requireCrouch && !IsCrouched && !stats.IsInvisible) return;

        candidate = FindTakedownTarget();
        if (candidate != null)
            GameHUD.Prompt($"[ĽTM] Tiché zabitie: {candidate.displayName}");
    }

    Damageable FindTakedownTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, takedownRange, ~0, QueryTriggerInteraction.Ignore);

        Damageable best = null;
        float bestDist = float.MaxValue;

        foreach (Collider h in hits)
        {
            Damageable d = h.GetComponentInParent<Damageable>();
            if (d == null || !IsTakedownTarget(d)) continue;

            float dist = (d.transform.position - transform.position).sqrMagnitude;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = d;
            }
        }
        return best;
    }

    bool IsTakedownTarget(Damageable d)
    {
        if (d.IsDead || d.immuneToNormalWeapons) return false;

        CivilianAI civ = d.GetComponent<CivilianAI>();
        if (civ != null && (civ.IsHealer || civ.Mode != CivilianMode.Calm)) return false;

        DemonEnemy demon = d.GetComponent<DemonEnemy>();
        if (demon != null && (demon.IsHigher || demon.IsBossClass || demon.executioner || demon.IsAlertedToPlayer)) return false;

        // Spojenec hráča-démona sa nezabíja (okrem Popravcu)
        if (demon != null && stats.IsDemon && demon.allied && !demon.executioner) return false;

        SoldierAI soldier = d.GetComponent<SoldierAI>();
        if (soldier != null && !soldier.CanBeTakenDown) return false;

        CityGuardNPC guard = d.GetComponent<CityGuardNPC>();
        if (guard != null && !guard.CanBeTakenDown) return false;

        // Hráč musí byť cieľovi za chrbtom
        Vector3 toPlayer = transform.position - d.transform.position;
        toPlayer.y = 0f;
        if (toPlayer.magnitude > takedownRange) return false;
        if (toPlayer.sqrMagnitude > 0.0025f && Vector3.Angle(-d.transform.forward, toPlayer) > takedownBackAngle)
            return false;

        return true;
    }

    /// <summary>Volá PlayerCombat pri kliknutí. Vráti true, ak sa vykonalo tiché zabitie (útok sa nerobí).</summary>
    public bool TryTakedown()
    {
        if (stats == null || stats.IsDead || Time.time < nextTakedown) return false;
        if (requireCrouch && !IsCrouched && !stats.IsInvisible) return false;

        Damageable target = FindTakedownTarget();
        if (target == null) return false;

        DoTakedown(target);
        return true;
    }

    void DoTakedown(Damageable d)
    {
        nextTakedown = Time.time + takedownCooldown * Mathf.Clamp(1f - SkillTree.Sum(SkillStat.TakedownCooldown), 0.2f, 1f);

        Vector3 pos = d.transform.position;
        string victim = d.displayName;
        CivilianAI civVictim = d.GetComponent<CivilianAI>();
        bool soldier = d.GetComponent<SoldierAI>() != null || d.GetComponent<CityGuardNPC>() != null;
        bool rebel = civVictim != null && civVictim.IsResistance;
        bool human = civVictim != null || soldier;
        DemonEnemy demon = d.GetComponent<DemonEnemy>();

        if (movement != null) movement.FaceDirection(pos - transform.position);

        // Svedkov rátame ešte pred zabitím
        int witnesses = human && !stats.IsInvisible
            ? StealthWorld.CountWitnesses(pos, d.transform, witnessRadius * Mathf.Max(0.6f, Visibility), witnessFov)
            : 0;

        StealthWorld.SilentKillFrame = Time.frameCount;
        d.TakeDamage(d.maxHealth * 10f, gameObject);

        Vfx.Disc(pos + Vector3.down * 0.9f, 1.4f, new Color(0.2f, 0.1f, 0.3f), 0.3f);
        GameHUD.WorldText(pos + Vector3.up * 2.2f, "Tiché zabitie", new Color(0.8f, 0.7f, 1f));

        StealthWorld.RegisterBody(pos, human, victim);

        // Človek, ktorý potichu zabil démona, si u civilistov zlepší povesť
        if (demon != null && !stats.IsDemon) Reputation.Add(FactionId.Civilians, 2f);

        // Zabiť démona z cudzieho klanu sa nezabudne
        if (demon != null && stats.IsDemon && demon.clan != FactionId.None)
            Reputation.Add(demon.clan, -6f);

        if (!human) return;

        if (witnesses > 0)
        {
            float add = Mathf.Min(maxWitnessSuspicion, suspicionPerWitness * witnesses);
            Reputation.AddSuspicion(add);
            StealthWorld.AlarmWitnesses(pos, d.transform, transform.position, witnessRadius, witnessFov);
            GameHUD.Message($"Svedkovia! Podozrenie mesta +{add:0}", 4f);
        }
        else
        {
            GameHUD.Message("Tiché zabitie bez svedkov. Skry telo, kým ho niekto nenájde.", 4f);
        }
    }
}
