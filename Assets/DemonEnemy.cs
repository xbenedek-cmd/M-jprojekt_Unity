using System.Collections.Generic;
using UnityEngine;

public enum DemonTier { Lesser, Mid, Higher, Boss, Leader, Arakani }

/// <summary>
/// Nepriateľský démon: naháňa hráča a útočí s krátkym nabíjaním (dá sa uhnúť).
/// Smrteľný zásah od neho premení človeka na démona (a hráč sa stane členom jeho klanu).
/// Na hráča-démona neútočí, kým ho hráč sám nenapadne.
/// </summary>
[RequireComponent(typeof(Damageable))]
public class DemonEnemy : MonoBehaviour
{
    [Header("Správanie")]
    [Tooltip("Zapni pre hru podľa GDD (útočia po zotmení). Vypnuté = útočia stále (na testovanie).")]
    public bool onlyAtNight = false;
    [Tooltip("Klan démona. Ak ním hráča zabijú, hráč sa stane členom tohto klanu.")]
    public FactionId clan = FactionId.None;
    [Header("Útoky na ľudí")]
    [Tooltip("Démon napáda aj civilistov (nie len hráča). Vyšší démoni tiež.")]
    public bool attacksCivilians = true;
    [Tooltip("Civilistov napáda len po zotmení (GDD). Vypnuté = napáda ich stále.")]
    public bool civiliansOnlyAtNight = true;
    [Tooltip("Dosah, z ktorého si všimne civilistu (násobok bežného dosahu démona)")]
    public float civilianAggroMultiplier = 1.4f;
    [Tooltip("Target Lock: vybraný cieľ (vojak, civilista, odboj) drží do tejto vzdialenosti, kým sa neschová")]
    public float targetLockRange = 10f;
    [Tooltip("Ako dlho môže byť cieľ zakrytý, kým ho démon pustí (s)")]
    public float lockLosGrace = 0.75f;

    [Header("Úroveň démona")]
    [Tooltip("Nižší a strední: musia sa skrývať v tieni, slnko ich ničí. Vyšší, boss, vodca frakcie a Arakani: slnko im nevadí, prenasledujú od 10 m a neprestanú.")]
    public DemonTier tier = DemonTier.Lesser;
    [Tooltip("Nižší démon ťa začne prenasledovať, len keď ťa uvidí z tejto vzdialenosti (alebo keď ho udrieš)")]
    public float lesserAggroRange = 5f;
    public float midAggroRange = 7f;
    public float higherAggroRange = 10f;
    [Tooltip("Nižší a strední démon ťa prestanú prenasledovať nad touto vzdialenosťou")]
    public float loseRange = 16f;

    [Header("Slnko a tieň")]
    [Tooltip("Poškodenie za sekundu na priamom slnku (stredný démon len 60 %)")]
    public float sunDamagePerSecond = 8f;
    public float shadeSearchRadius = 15f;

    [Header("Pohyb")]
    public float moveSpeed = 3.2f;
    public float turnSpeed = 10f;
    [Tooltip("Uhlová rýchlosť otáčania = turnSpeed x toto (stupne za sekundu)")]
    public float turnDegreesPerSpeed = 30f;
    [Tooltip("Najbližšie, kam sa démon priblíži k cieľu (m); bojová vzdialenosť je max(toto, 80 % dosahu útoku)")]
    public float minStopDistance = 1.2f;
    private bool playerEngaged, humanEngaged;

    [Header("Úkryt pred slnkom (nižší a strední)")]
    [Tooltip("Ako dlho po dobehnutí do tieňa oddychuje, kým sa vráti k cieľu")]
    public float shadeRestSeconds = 3f;
    [Tooltip("Po oddychu najmenej toľko sekúnd znova neutečie do tieňa")]
    public float shadeCooldownSeconds = 2f;
    [Header("Stabilita cieľa")]
    [Tooltip("Po začatí naháňania hráča ho démon drží najmenej toľko sekúnd, aj keď sa podmienky na chvíľu zmenia")]
    public float chaseMinSeconds = 2f;
    private float chaseStartTime;
    private bool seekingShade, shadeResting;
    private float shadeRestUntil, shadeCooldownUntil;

    [Header("Útok")]
    public float attackRange = 1.8f;
    public float attackDamage = 15f;
    public float attackCooldown = 1.5f;
    [Tooltip("Čas nabíjania úderu - počas neho sa dá uhnúť")]
    public float windUp = 0.45f;
    [Tooltip("Zrýchlenie útokov (1.25 = o 25 % častejšie a rýchlejšie nabíjanie)")]
    public float attackSpeedBoost = 1.25f;

    [Header("Variant útoku (Elbow Punch)")]
    [Tooltip("Šanca, že sa použije silnejší výpad (Illegal Elbow Punch) namiesto bežného úderu")]
    [Range(0f, 1f)] public float elbowChance = 0.5f;
    public float elbowDamageMultiplier = 1.35f;
    [Tooltip("Silnejší výpad sa nabíja dlhšie (dá sa lepšie uhnúť)")]
    public float elbowWindUpMultiplier = 1.2f;

    public float AttackCooldown => attackCooldown / Mathf.Max(0.1f, attackSpeedBoost);
    public float WindUp => windUp / Mathf.Max(0.1f, attackSpeedBoost) * (attackVariant == 1 ? elbowWindUpMultiplier : 1f);

    // 0 = bežný úder (Punching), 1 = silnejší výpad (Elbow Punch); volí sa pri začiatku každého útoku
    private int attackVariant;
    public int AttackVariant => attackVariant;
    float VariantDamage => attackVariant == 1 ? elbowDamageMultiplier : 1f;

    void RollAttackVariant()
    {
        attackVariant = Random.value < elbowChance ? 1 : 0;
    }
    /// <summary>Práve nabíja úder (pre animáciu útoku).</summary>
    public bool IsAttackWindup => ScriptedAttack || attacking || civAttacking || soldierAttacking || buildingAttacking;
    /// <summary>Scénar (prológ): AI sa nespúšťa, pohyb riadi skript; ScriptedAttack len pustí animáciu útoku.</summary>
    [System.NonSerialized] public bool Scripted, ScriptedAttack;

    private PlayerStats player;
    private DayNightCycle dayNight;
    private Damageable dmg;
    private NPCWander wander;
    private Rigidbody rb;

    private bool chasing;
    private bool attacking;
    private bool provoked;
    private float nextAttackTime;
    private float hitTime;
    private float slowUntil;
    private float slowFactor = 1f;
    private float stunUntil;

    [Tooltip("Spojenec (kontaktovaný hráčom-démonom) na hráča neútočí, kým ho hráč nenapadne")]
    public bool allied;

    [Tooltip("Popravca od Arakaniho: neprestane loviť ani hráča-démona")]
    public bool executioner;

    private NPCPerception maskPerception;
    private CivilianAI civTarget;
    private bool civAttacking;
    private float civHitTime;
    private float nextCivScan;
    private float nextLosCheck;
    private float lostSightSince = -1f;
    private readonly Collider[] scanBuffer = new Collider[32];
    private SoldierAI soldierTarget;
    private bool soldierAttacking;
    private BuildPlot buildingTarget;
    private bool buildingAttacking;
    private float buildingHitTime;
    private float nextBuildingScan;
    private float soldierHitTime;

    private float nextSunCheck;
    private float nextSunText;
    private float nextShadeSearch;
    private bool sunExposed;
    private bool hasShade;
    private Vector3 shadePoint;
    private ReflectiveSurface mirrorTarget;
    private Vector3 mirrorLoiterPoint;
    private float nextMirrorScan;

    /// <summary>Skutočná úroveň: nezraniteľný démon je vždy aspoň vyšší.</summary>
    public DemonTier Tier => (dmg != null && dmg.immuneToNormalWeapons && tier < DemonTier.Higher) ? DemonTier.Higher : tier;
    public bool IsHigher => Tier >= DemonTier.Higher;
    public bool IsChasing => chasing;

    /// <summary>Už o hráčovi vie (prenasleduje ho alebo ho nahnevali) - tiché zabitie na neho nefunguje.</summary>
    public bool IsAlertedToPlayer => chasing || provoked;
    public bool IsHuntingCivilian => civTarget != null || soldierTarget != null;
    public bool OfficiallyReportedToGuards => dmg != null && dmg.OfficiallyReportedDemon;

    public void MarkOfficiallyReported()
    {
        if (dmg != null) dmg.MarkOfficiallyReportedDemon();
    }

    float AggroRange
    {
        get
        {
            float r = Tier == DemonTier.Lesser ? lesserAggroRange : Tier == DemonTier.Mid ? midAggroRange : higherAggroRange;
            return r * (dayNight != null ? dayNight.DemonAggroMultiplier : 1f);
        }
    }

    float MoonSpeed => (dayNight != null ? dayNight.DemonSpeedMultiplier : 1f) * GameSpeed.Npc;
    float MoonDamage => dayNight != null ? dayNight.DemonDamageMultiplier : 1f;

    void Awake()
    {
        dmg = GetComponent<Damageable>();
        dmg.OnDamaged += HandleDamaged;
        if (GetComponent<NPCGrounding>() == null) gameObject.AddComponent<NPCGrounding>();
    }

    /// <summary>Všetci živí démoni v scéne (vojenská zóna ich takto nájde bez hľadania).</summary>
    public static readonly List<DemonEnemy> All = new List<DemonEnemy>();

    public Damageable Damage => dmg;

    /// <summary>Boss, vodca frakcie a Arakani - vojaci na nich nestrieľajú.</summary>
    public bool IsBossClass => Tier >= DemonTier.Boss;

    /// <summary>
    /// Vidia v ňom vojaci démona? Nižší a strední sú to na prvý pohľad, vyšší len keď ťa
    /// prenasleduje alebo mu spadla maska (detektívka).
    /// </summary>
    public bool IsVisibleThreat
    {
        get
        {
            if (Tier < DemonTier.Higher) return true;
            if (chasing || civTarget != null) return true;
            NPCPerception n = GetComponent<NPCPerception>();
            return n != null && n.Revealed;
        }
    }

    void OnEnable()
    {
        PlayerStats.OnFormChanged += HandleFormChanged;
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable()
    {
        PlayerStats.OnFormChanged -= HandleFormChanged;
        All.Remove(this);
    }

    void OnDestroy()
    {
        if (dmg != null) dmg.OnDamaged -= HandleDamaged;
    }

    void Start()
    {
        player = FindAnyObjectByType<PlayerStats>();
        dayNight = FindAnyObjectByType<DayNightCycle>();
        wander = GetComponent<NPCWander>();
        maskPerception = GetComponent<NPCPerception>();

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void HandleDamaged(float amount, GameObject attacker)
    {
        if (attacker == null) return;   // slnko a iné okolie ho nenahnevajú
        provoked = true;    // keď ho niekto udrie, nahnevá sa aj cez deň

        // Napadnutý hráčom: vyšší démon okamžite odhodí masku (rovnako ako po úspešnom skúmaní T).
        // Zásah, ktorý ho zabije, sa preskočí: premena by sa prehrala tesne pred animáciou smrti.
        if (dmg.Health > 0f && attacker.GetComponent<PlayerStats>() != null) RevealMaskedDemon();
    }

    /// <summary>Vyšší démon v ľudskej maske sa premení na démona (animácia premeny, potom boj). Iným démonom nerobí nič.</summary>
    void RevealMaskedDemon()
    {
        if (maskPerception == null || maskPerception.kind != NPCKind.HigherDemon) return;
        maskPerception.Unmask();
    }

    // Keď sa hráč premení na démona, všetci sa upokoja (aj ten, čo ho práve zabil)
    void HandleFormChanged(PlayerForm form)
    {
        if (form != PlayerForm.Demon || executioner) return;

        provoked = false;
        chasing = false;
        attacking = false;
    }

    /// <summary>Nahnevá démona proti hráčovi (aj cez deň), zruší spojenectvo.</summary>
    public void Provoke()
    {
        allied = false;
        provoked = true;
        RevealMaskedDemon();   // podráždený vyšší démon už nepredstiera človeka
    }

    /// <summary>Hráč odrazil / uhol jeho úder: démon je omráčený a chvíľu neútočí.</summary>
    public void Parried(float stunSeconds)
    {
        Stun(stunSeconds);
        nextAttackTime = Time.time + stunSeconds + 0.5f;
    }

    /// <summary>Omráči démona: nehýbe sa a neútočí.</summary>
    public void Stun(float seconds)
    {
        stunUntil = Time.time + seconds;
        attacking = false;
        civAttacking = false;
    }

    /// <summary>Spomalenie (EMP impulz). Preruší aj práve nabíjaný úder.</summary>
    public void ApplySlow(float factor, float seconds)
    {
        slowFactor = factor;
        slowUntil = Time.time + seconds;

        if (attacking)
        {
            attacking = false;
            nextAttackTime = Time.time + AttackCooldown * 0.5f;
        }
    }

    bool IsHostileNow()
    {
        if (player == null || player.IsDead) return false;
        if (allied && !provoked) return false;

        // Maskovaný vyšší démon medzi ľuďmi na hráča neútočí, kým ho hráč nenapadne alebo neodhalí
        if (!provoked && maskPerception != null && maskPerception.kind == NPCKind.HigherDemon && !maskPerception.Revealed)
            return false;

        // Démoni neútočia na hráča-démona, kým ich on sám nenapadne
        if (player.IsDemon && !provoked) return false;
        if (provoked) return true;
        if (!onlyAtNight) return true;
        return dayNight != null && dayNight.IsNight;
    }

    void FixedUpdate()
    {
        if (Scripted) return;
        if (dmg.IsDead || player == null) return;
        if (JailManager.IsInJail)
        {
            attacking = false;
            civAttacking = false;
            soldierAttacking = false;
            return;
        }
        if (Time.time < stunUntil) { attacking = false; civAttacking = false; buildingAttacking = false; return; }

        // Cieľ: hologram (ak je) alebo hráč
        HologramDecoy decoy = HologramDecoy.Current;
        Transform target = decoy != null ? decoy.transform : player.transform;

        Vector3 to = target.position - rb.position;
        to.y = 0f;
        float dist = to.magnitude;

        bool hostile = IsHostileNow();
        bool seen = !player.IsInvisible;     // neviditeľného hráča nikto nevidí

        if (!chasing && hostile && seen && ((provoked && dist <= loseRange) || dist <= AggroRange * PlayerStealth.RangeFactor(transform)))
        {
            chasing = true;
            playerEngaged = false;
            chaseStartTime = Time.time;
            RevealMaskedDemon();   // kto naháňa hráča, ide v pravej podobe
            ReleaseHumanTarget();   // hráč je teraz primárny cieľ; civilista/vojak sa nezdržiava vedľa
        }
        else if (chasing && !seen)
        {
            chasing = false;
            attacking = false;
        }
        else if (chasing)
        {
            // Vyšší démon neprestane: len keď hráč zomrie alebo sa stane démonom
            bool stop = IsHigher
                ? (player.IsDead || (player.IsDemon && !executioner))
                : (!hostile || dist > loseRange);

            // Cieľ je zafixovaný aspoň chaseMinSeconds: krátke výkyvy podmienok (hostile, vzdialenosť) ho nezrušia
            if (stop && Time.time < chaseStartTime + chaseMinSeconds && !player.IsDead) stop = false;

            if (stop)
            {
                chasing = false;
                attacking = false;
                provoked = false;
            }
        }

        UpdateSun();
        if (dmg.IsDead) return;

        // Nižší a strední démoni sa cez deň držia v tieni
        bool lowTier = Tier < DemonTier.Higher;
        bool hideFromSun = lowTier && dayNight != null && dayNight.SunStrength > 0.5f;

        if (lowTier && HandleSunShelter(dist))
        {
            if (wander != null) wander.enabled = false;
            return;
        }

        if (!chasing)
        {
            // Hráča nenaháňa: môže loviť ľudí (aj vyšší démon)
            HuntHumansAndSoldiers();
            HuntBuildings();
            bool mirrorLoitering = !HasHumanTarget() && buildingTarget == null && !hideFromSun && UpdateMirrorLoiter();
            if (wander != null) wander.enabled = !hideFromSun && !HasHumanTarget() && buildingTarget == null && !mirrorLoitering;
            return;
        }

        if (HasHumanTarget()) ReleaseHumanTarget();
        buildingTarget = null;
        buildingAttacking = false;
        if (wander != null) wander.enabled = false;

        Vector3 dir = dist > 0.01f ? to / dist : transform.forward;
        TurnToward(dir);

        // Nabíjanie úderu: ak cieľ za ten čas odíde z dosahu, minie
        if (attacking)
        {
            if (Time.time >= hitTime)
            {
                attacking = false;
                nextAttackTime = Time.time + AttackCooldown;

                if (dist <= attackRange * 1.4f)
                {
                    if (decoy != null) decoy.Absorb();
                    else
                    {
                        float hitDamage = attackDamage * VariantDamage * MoonDamage;
                        if (!CombatQte.Intercept(this, hitDamage)) player.TakeDamage(hitDamage, DamageSource.Demon, clan);   // po 2–3 úderoch hráč uhýba (spomalenie)
                    }
                }

            }
            return;
        }

        float speed = moveSpeed * MoonSpeed * (Time.time < slowUntil ? slowFactor : 1f);

        if (KeepApproaching(dist, ref playerEngaged))
        {
            Advance(dir, speed);
        }
        else
        {
            KeepSeparation(dir, dist);
            if (Time.time >= nextAttackTime)
            {
                attacking = true;
                RollAttackVariant();
                hitTime = Time.time + WindUp;
            }
        }
    }

    // ---------- Pohyb: plynulé otáčanie, zastavenie v bojovej vzdialenosti ----------
    // Plynulé otáčanie konštantnou uhlovou rýchlosťou (bez zasekávania); počas otáčania pokračuje bežná chôdza
    void TurnToward(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, target, turnSpeed * turnDegreesPerSpeed * Time.fixedDeltaTime));
    }

    // Vráti true, kým sa démon má približovať. Zastaví sa pri stopDistance a znova sa rozbehne až keď cieľ odíde
    // za attackRange x 1.15 (hysteréza: bez stop-go na hranici dosahu a bez prechodu cez cieľ).
    bool KeepApproaching(float dist, ref bool engaged)
    {
        float stop = Mathf.Max(minStopDistance, attackRange * 0.8f);
        if (engaged)
        {
            if (dist > attackRange * 1.15f) engaged = false;
        }
        else if (dist <= stop)
        {
            engaged = true;
        }
        return !engaged;
    }

    // Krok vpred; pri veľkom uhle k cieľu spomalí, aby sa najprv natočil a neklzal bokom
    void Advance(Vector3 dir, float speed)
    {
        float facing = Mathf.Clamp01(Vector3.Dot(rb.rotation * Vector3.forward, dir) * 0.5f + 0.5f);
        float factor = Mathf.Lerp(0.45f, 1f, facing);
        Vector3 step = NpcSteering.Adjust(rb.position, dir * speed * factor * Time.fixedDeltaTime, transform);   // vyhýba sa pevným predmetom
        rb.MovePosition(rb.position + step);
    }

    // Ak je cieľ príliš blízko (hráč naňho vbehol), démon ustúpi, nie prejde cez neho
    void KeepSeparation(Vector3 dir, float dist)
    {
        float minDist = minStopDistance * 0.75f;
        if (dist >= minDist) return;
        rb.MovePosition(rb.position - dir * moveSpeed * 0.6f * Time.fixedDeltaTime);
    }

    bool UpdateMirrorLoiter()
    {
        if (Time.time >= nextMirrorScan)
        {
            nextMirrorScan = Time.time + 0.5f;
            ReflectiveSurface candidate = ReflectiveSurface.Find(rb.position, rb.position, ReflectiveSurface.MaxCheckRadius);
            if (candidate != mirrorTarget)
            {
                mirrorTarget = candidate;
                if (mirrorTarget != null)
                {
                    Vector3 away = rb.position - mirrorTarget.transform.position;
                    away.y = 0f;
                    if (away.sqrMagnitude < 0.01f) away = transform.forward;
                    mirrorLoiterPoint = mirrorTarget.transform.position + away.normalized * 1.1f;
                    mirrorLoiterPoint.y = rb.position.y;
                }
            }
        }

        if (mirrorTarget == null) return false;

        Vector3 toPoint = mirrorLoiterPoint - rb.position;
        toPoint.y = 0f;
        if (toPoint.sqrMagnitude > 0.08f)
        {
            Vector3 dir = toPoint.normalized;
            TurnToward(dir);
            rb.MovePosition(rb.position + dir * moveSpeed * 0.55f * Time.fixedDeltaTime);
        }
        else
        {
            Vector3 tangent = Vector3.Cross(Vector3.up, mirrorLoiterPoint - mirrorTarget.transform.position).normalized;
            TurnToward(tangent);
            rb.MovePosition(rb.position + tangent * moveSpeed * 0.18f * Time.fixedDeltaTime);
        }
        return true;
    }

    // ---------- Útoky na ľudí ----------
    // ---------- Búranie stavieb ----------
    [Header("Búranie stavieb")]
    [Tooltip("Stredný, vyšší démon a boss, ktorý nehonia hráča ani ľudí, idú búrať najbližšiu postavenú stavbu (parcely)")]
    public bool demolishesBuildings = true;
    public float buildingScanRange = 45f;
    [Tooltip("Poškodenie stavby = poškodenie démona × toto (stavba má stovky HP)")]
    public float buildingDamageFactor = 3f;

    bool CanDemolish() => demolishesBuildings && tier >= DemonTier.Mid && !allied && !dmg.IsDead;

    // Bez cieľa (hráč, civilista, vojak) démon ide k najbližšej postavenej stavbe a búra ju; zničená stavba sa zmení na ruiny
    void HuntBuildings()
    {
        if (!CanDemolish() || HasHumanTarget())
        {
            buildingTarget = null;
            buildingAttacking = false;
            return;
        }

        if (buildingTarget != null && buildingTarget.State != PlotState.Built)
        {
            buildingTarget = null;
            buildingAttacking = false;
        }

        if (buildingTarget == null)
        {
            if (Time.time < nextBuildingScan) return;
            nextBuildingScan = Time.time + 1.5f;
            buildingTarget = BuildPlot.NearestBuilt(rb.position, buildingScanRange);
            if (buildingTarget == null) return;
        }

        Vector3 to = buildingTarget.transform.position - rb.position;
        to.y = 0f;
        float dist = to.magnitude;
        float reach = buildingTarget.Side * 0.5f + attackRange * 0.8f;   // dosah od stredu parcely: po okraj stavby
        Vector3 dir = dist > 0.01f ? to / dist : transform.forward;
        TurnToward(dir);

        if (buildingAttacking)
        {
            if (Time.time >= buildingHitTime)
            {
                buildingAttacking = false;
                nextAttackTime = Time.time + AttackCooldown;
                if (dist <= reach * 1.2f && buildingTarget != null)
                    buildingTarget.TakeDamage(attackDamage * VariantDamage * MoonDamage * buildingDamageFactor, gameObject);
            }
            return;
        }

        if (dist > reach)
        {
            Advance(dir, moveSpeed * MoonSpeed * (Time.time < slowUntil ? slowFactor : 1f));
        }
        else if (Time.time >= nextAttackTime)
        {
            buildingAttacking = true;
            RollAttackVariant();
            buildingHitTime = Time.time + WindUp;
        }
    }

    bool CanHuntCivilians()
    {
        if (!attacksCivilians || dmg.IsDead) return false;
        if (civiliansOnlyAtNight && (dayNight == null || !dayNight.IsNight)) return false;
        return true;
    }

    bool HasHumanTarget()
    {
        return civTarget != null || soldierTarget != null;
    }

    void HuntHumansAndSoldiers()
    {
        if (!CanHuntCivilians())
        {
            civTarget = null;
            civAttacking = false;
            soldierTarget = null;
            soldierAttacking = false;
            return;
        }

        if (HasHumanTarget() && !IsTargetLockValid())
        {
            ReleaseHumanTarget();
            nextCivScan = 0f;   // až po uvoľnení môže hneď hľadať nový cieľ
        }

        if (!HasHumanTarget() && Time.time >= nextCivScan)
        {
            nextCivScan = Time.time + 0.4f;
            AcquireNearestTarget();
        }
        if (!HasHumanTarget()) return;

        if (soldierTarget != null)
        {
            HuntSoldier();
            return;
        }

        Vector3 to = civTarget.transform.position - rb.position;
        to.y = 0f;
        float dist = to.magnitude;
        Vector3 dir = dist > 0.01f ? to / dist : transform.forward;

        TurnToward(dir);

        // Nabíjanie úderu: ak civilista za ten čas odíde z dosahu, minie
        if (civAttacking)
        {
            if (Time.time >= civHitTime)
            {
                civAttacking = false;
                nextAttackTime = Time.time + AttackCooldown;

                if (dist <= attackRange * 1.4f && !civTarget.IsDead)
                    civTarget.Damage.TakeDamage(attackDamage * VariantDamage * MoonDamage, gameObject);
            }
            return;
        }

        float speed = moveSpeed * MoonSpeed * (Time.time < slowUntil ? slowFactor : 1f);

        if (KeepApproaching(dist, ref humanEngaged))
        {
            Advance(dir, speed);
        }
        else
        {
            KeepSeparation(dir, dist);
            if (Time.time >= nextAttackTime)
            {
                civAttacking = true;
                RollAttackVariant();
                civHitTime = Time.time + WindUp;
            }
        }
    }

    Transform LockedTransform => soldierTarget != null ? soldierTarget.transform : civTarget != null ? civTarget.transform : null;

    void ReleaseHumanTarget()
    {
        civTarget = null;
        soldierTarget = null;
        humanEngaged = false;
        civAttacking = false;
        soldierAttacking = false;
        lostSightSince = -1f;
    }

    // Target Lock: cieľ drží, kým je do targetLockRange a viditeľný (krátka tolerancia na zábleskové zákryty)
    bool IsTargetLockValid()
    {
        if ((civTarget != null && civTarget.IsDead) || (soldierTarget != null && soldierTarget.IsDead)) return false;

        Transform t = LockedTransform;
        if (t == null) return false;
        if ((t.position - rb.position).sqrMagnitude > targetLockRange * targetLockRange) return false;

        if (Time.time < nextLosCheck) return true;
        nextLosCheck = Time.time + 0.2f;

        if (HasSightTo(t))
        {
            lostSightSince = -1f;
            return true;
        }

        if (lostSightSince < 0f) lostSightSince = Time.time;
        return Time.time - lostSightSince < lockLosGrace;
    }

    bool HasSightTo(Transform t)
    {
        return StealthWorld.HasLineOfSight(rb.position + Vector3.up * 1.2f, t.position + Vector3.up * 1.0f);
    }

    // Najbližší vojak / civilista / člen odboja v okolí (bez alokácií, jeden OverlapSphere)
    void AcquireNearestTarget()
    {
        float range = Mathf.Min(AggroRange * civilianAggroMultiplier, targetLockRange);
        int count = Physics.OverlapSphereNonAlloc(rb.position, range, scanBuffer, ~0, QueryTriggerInteraction.Ignore);

        float bestSqr = float.MaxValue;
        CivilianAI bestCiv = null;
        SoldierAI bestSoldier = null;

        for (int i = 0; i < count; i++)
        {
            Collider c = scanBuffer[i];
            if (c.transform.IsChildOf(transform)) continue;

            SoldierAI soldier = c.GetComponentInParent<SoldierAI>();
            CivilianAI civilian = soldier == null ? c.GetComponentInParent<CivilianAI>() : null;
            if (soldier == null && civilian == null) continue;
            if (soldier != null ? soldier.IsDead : (civilian.IsDead || civilian.IsHealer)) continue;

            Transform t = soldier != null ? soldier.transform : civilian.transform;
            float sqr = (t.position - rb.position).sqrMagnitude;
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            bestSoldier = soldier;
            bestCiv = civilian;
        }

        // Výhľad sa overuje len pre víťaza (raycast je drahší než porovnanie vzdialeností)
        Transform best = bestSoldier != null ? bestSoldier.transform : bestCiv != null ? bestCiv.transform : null;
        if (best == null || !HasSightTo(best)) return;

        soldierTarget = bestSoldier;
        humanEngaged = false;
        civTarget = bestCiv;
        lostSightSince = -1f;
        nextLosCheck = Time.time + 0.2f;
    }

    void HuntSoldier()
    {
        Vector3 to = soldierTarget.transform.position - rb.position;
        to.y = 0f;
        float dist = to.magnitude;
        Vector3 dir = dist > 0.01f ? to / dist : transform.forward;

        TurnToward(dir);

        if (soldierAttacking)
        {
            if (Time.time >= soldierHitTime)
            {
                soldierAttacking = false;
                nextAttackTime = Time.time + AttackCooldown;
                if (dist <= attackRange * 1.4f && !soldierTarget.IsDead)
                    soldierTarget.GetComponent<Damageable>().TakeDamage(attackDamage * VariantDamage * MoonDamage, gameObject);
            }
            return;
        }

        float speed = moveSpeed * MoonSpeed * (Time.time < slowUntil ? slowFactor : 1f);
        if (KeepApproaching(dist, ref humanEngaged))
        {
            Advance(dir, speed);
        }
        else
        {
            KeepSeparation(dir, dist);
            if (Time.time >= nextAttackTime)
            {
                soldierAttacking = true;
                RollAttackVariant();
                soldierHitTime = Time.time + WindUp;
            }
        }
    }

    // ---------- Slnko a tieň ----------
    void UpdateSun()
    {
        if (IsHigher || dayNight == null || dayNight.SunStrength <= 0f)
        {
            sunExposed = false;
            return;
        }

        if (Time.time >= nextSunCheck)
        {
            nextSunCheck = Time.time + 0.25f;
            sunExposed = dayNight.IsSunlit(rb.position);
        }
        if (!sunExposed) return;

        float mult = Tier == DemonTier.Mid ? 0.6f : 1f;
        dmg.TakeDamage(sunDamagePerSecond * dayNight.SunStrength * mult * Time.fixedDeltaTime);

        if (Time.time >= nextSunText && !dmg.IsDead)
        {
            nextSunText = Time.time + 1f;
            GameHUD.WorldText(transform.position + Vector3.up * 2.5f, "Slnko!", new Color(1f, 0.6f, 0.1f));
        }
    }

    // Slnko a tieň ako záväzné stavy, nie podmienka vyhodnocovaná každý krok:
    //  hľadanie tieňa -> beh na JEDEN zvolený bod (neprepočítava sa) -> oddych v tieni -> až potom bežné správanie.
    // Bez toho démon utekal do tieňa, tam ho slnko prestalo páliť, vrátil sa za hráčom na slnko a znova utekal (W/S).
    // Vráti true, ak tento krok patrí úkrytu (beh do tieňa alebo oddych).
    bool HandleSunShelter(float distToTarget)
    {
        // Už v boji nablízku (alebo ho práve niekto bije) neutečie - dobojuje
        bool closeFight = chasing && distToTarget <= attackRange * 2f;

        if (shadeResting)
        {
            if (Time.time < shadeRestUntil && !closeFight)
            {
                attacking = false;
                return true;
            }
            shadeResting = false;
            shadeCooldownUntil = Time.time + shadeCooldownSeconds;   // hneď po odpočinku nevybehne do slnka a naspäť
        }

        if (!seekingShade)
        {
            if (!sunExposed || closeFight || Time.time < shadeCooldownUntil) return false;
            if (Time.time < nextShadeSearch) return false;

            nextShadeSearch = Time.time + 1f;
            if (!FindShade(out shadePoint)) return false;   // žiadny tieň v dosahu: správa sa normálne
            seekingShade = true;
        }

        // Dorazil, alebo ho slnko už nepáli: zastaví sa a oddychuje
        Vector3 to = shadePoint - rb.position;
        to.y = 0f;
        if (!sunExposed || to.sqrMagnitude < 0.09f || closeFight)
        {
            seekingShade = false;
            shadeResting = !closeFight;
            shadeRestUntil = Time.time + shadeRestSeconds;
            return shadeResting;
        }

        Vector3 dir = to.normalized;
        attacking = false;
        TurnToward(dir);
        Advance(dir, moveSpeed * 1.3f * MoonSpeed);
        return true;
    }

    private static readonly float[] ShadeRadii = { 3f, 6f, 10f, 0f };

    bool FindShade(out Vector3 point)
    {
        point = rb.position;
        ShadeRadii[3] = shadeSearchRadius;
        float[] radii = ShadeRadii;

        foreach (float r in radii)
        {
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                Vector3 p = rb.position + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

                // Bod vo vnútri steny alebo objektu sa nepočíta
                if (Physics.CheckSphere(p + Vector3.up * 0.5f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) continue;

                if (!dayNight.IsSunlit(p))
                {
                    point = p;
                    return true;
                }
            }
        }
        return false;
    }

    private static GUIStyle warnStyle;
    private static Camera guiCam;

    // Varovanie nad hlavou počas nabíjania úderu
    void OnGUI()
    {
        if (!(attacking || civAttacking)) return;   // OnGUI beží na každom démonovi: najlacnejšia kontrola prvá
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (dmg == null || dmg.IsDead) return;
        if (!NpcOverlay.Visible(transform.position)) return;

        if (guiCam == null) guiCam = Camera.main;
        Camera cam = guiCam;
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 3.4f);
        if (p.z <= 0f) return;

        if (warnStyle == null)
            warnStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.2f, 0.2f) }
            };
        GUI.Label(new Rect(p.x - 20f, Screen.height - p.y - 15f, 40f, 30f), "!", warnStyle);
    }
}