using System.Collections.Generic;
using UnityEngine;

public enum SoldierState { Patrol, Suspicious, Alert, Dead }

/// <summary>
/// Vojak Megakorporácie. Hliadkuje, má zorný kužeľ a ukazovateľ odhalenia (nad hlavou).
/// Odhalenie rastie podľa toho, ako ťa vidí: vzdialenosť, čupnutie, tma, chrbát, oblečenie (Wardrobe)
/// a či si démon vo vojenskej zóne. Pri podozrení ťa ide skontrolovať, pri odhalení strieľa a privolá ostatných.
/// Hľadá aj nájdené telá. Pridaj ho NPC, ktoré má Damageable a NPCPerception (vzhľad).
/// </summary>
[RequireComponent(typeof(Damageable))]
[RequireComponent(typeof(NPCPerception))]
public class SoldierAI : MonoBehaviour
{
    public static readonly List<SoldierAI> All = new List<SoldierAI>();

    public static void ClearPlayerHostility()
    {
        foreach (SoldierAI soldier in All)
            if (soldier != null) soldier.ClearPlayerHostilityState();
    }

    void ClearPlayerHostilityState()
    {
        ReleaseBody();
        reportedHostile = false;
        reportHostileUntil = 0f;
        combatLocked = false;
        detection = 0f;
        crimeKnownUntil = 0f;
        if (reportedDemon != null && reportedDemon.Damage != null && !reportedDemon.Damage.IsDead)
            state = SoldierState.Alert;
        else
            state = SoldierState.Patrol;
    }

    [Header("Zrak")]
    // Vojak vidí do 14 m (čupnutie a pod. dosah skracujú)
    public const float SightRadius = 14f;
    public const float MaxSightRadius = 20f;
    [Range(40f, 360f)] public float viewAngle = 120f;
    [Tooltip("Do tejto vzdialenosti ťa zbadá aj za chrbtom")]
    public float closeAwareness = 2.2f;
    [Tooltip("Rýchlosť odhalenia (1/s) pri plnej viditeľnosti")]
    public float detectRate = 0.6f;
    public float loseRate = 0.25f;
    public float suspiciousAt = 0.35f;
    [Tooltip("Sekundy, počas ktorých pri poplachu hľadá hráča po strate zraku")]
    public float alertMemory = 8f;
    [Tooltip("Sekundy, počas ktorých vojak po tom, čo ťa naposledy videl/zaznamenal zločin, hľadá a naháňa")]
    public float huntSeconds = 30f;

    [Header("Pohyb")]
    public float patrolSpeed = 1.6f;
    public float investigateSpeed = 2.4f;
    public float chaseSpeed = 4.2f;
    public float turnSpeed = 8f;
    [Tooltip("Najbližšie, kam sa vojak pri naháňaní k cieľu priblíži")]
    public float contactDistance = 2f;
    [Tooltip("Do tejto vzdialenosti od hráča (po zločine) vojak spustí zatknutie")]
    public float arrestRange = 3f;
    [Tooltip("Keď vojak naháňa hráča na zatknutie, bližšie než táto vzdialenosť už nestrieľa")]
    public float arrestFireCutoff = 6f;

    [Header("Paľba")]
    public float shootRange = 14f;
    public float keepDistance = 8f;
    public float shootInterval = 1.1f;
    [Tooltip("Zrýchlenie paľby (1.25 = o 25 % častejšie)")]
    public float attackSpeedBoost = 1.25f;
    public float shootDamage = 7f;
    [Range(0f, 1f)] public float hitChance = 0.7f;

    [Header("Poplach")]
    public float callRadius = 16f;
    public float suspicionOnAlert = 20f;

    private Damageable dmg;
    private NPCWander wander;
    private NPCPerception perception;
    private PlayerStats player;
    private Rigidbody rb;

    private SoldierState state = SoldierState.Patrol;
    private float detection;
    private Vector3 lastKnown;
    private float alertUntil;
    private float nextSense;
    private float nextBodyScan;
    private float crimeKnownUntil;   // tento vojak vie o zločine hráča (videl ho, našiel stopu/telo, dostal hlásenie)
    private float investigateUntil;   // nájdené telo: vojak ho ide skontrolovať, aj keď hráča nepodozrieva
    private float nextShot;
    private float nextText;
    private bool hitByPlayer;
    private bool gearBuilt;
    private MilitaryZone militaryZone;
    private bool reportedHostile;
    private float reportHostileUntil;
    private DemonEnemy reportedDemon;
    private bool combatLocked;
    private Vector3 patrolOrigin;
    private Vector3 patrolDestination;
    private PatrolRoute route;            // trasa hliadky vo vojenskej zóne
    private ZonePopulation cityZone;      // z mesta nevyjde
    private float patrolWaitUntil;
    private Vector3 patrolStuckPos;
    private float nextPatrolStuckCheck;
    private StealthWorld.Body targetBody; // telo, ktoré ide skontrolovať
    private float examineTimer;

    public SoldierState State => state;
    public float Detection => detection;
    public bool IsDead => dmg == null || dmg.IsDead || state == SoldierState.Dead;
    public bool CanBeTakenDown => !IsDead && state != SoldierState.Alert && detection < 0.9f;

    Vector3 Pos => rb != null ? rb.position : transform.position;

    void Awake()
    {
        dmg = GetComponent<Damageable>();
        perception = GetComponent<NPCPerception>();
        if (GetComponent<NPCGrounding>() == null) gameObject.AddComponent<NPCGrounding>();
        dmg.OnDamaged += HandleDamaged;
        dmg.OnDied += HandleDied;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        cityZone = CityPopulation.At(transform.position);
    }

    void OnDisable() => All.Remove(this);

    void OnDestroy()
    {
        if (dmg != null)
        {
            dmg.OnDamaged -= HandleDamaged;
            dmg.OnDied -= HandleDied;
        }
    }

    // Kinematické telo vojaka by pri prieniku do dynamického hráča generovalo obrovský depenetračný impulz
    void IgnorePlayerCollision()
    {
        if (player == null) return;

        Collider[] mine = GetComponentsInChildren<Collider>();
        Collider[] theirs = player.GetComponentsInChildren<Collider>();
        foreach (Collider a in mine)
            foreach (Collider b in theirs)
                if (a != b) Physics.IgnoreCollision(a, b, true);
    }

    void Start()
    {
        player = FindAnyObjectByType<PlayerStats>();
        IgnorePlayerCollision();
        wander = GetComponent<NPCWander>();
        militaryZone = MilitaryZone.At(transform.position);
        PatrolRoute streetRoute = null;
        if (militaryZone == null && RoadGraph.HasPatrols(transform.position))
        {
            // Hliadka po uliciach mesta (pevné trasy z RoadGraph) – vojak sa postaví na bod svojej trasy
            streetRoute = PatrolPlanner.BuildFixed(transform);
            PatrolPlanner.PlaceOnRoute(transform, streetRoute, player != null ? player.transform : null);
        }
        patrolOrigin = transform.position;
        cityZone = CityPopulation.At(transform.position);
        if (militaryZone != null) route = PatrolPlanner.Build(transform, cityZone, militaryZone.Area, PatrolRole.Soldier);
        else if (streetRoute != null) route = streetRoute;
        ChoosePatrolPoint();

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        BuildGear();
    }

    void Patrol()
    {
        // Na bode trasy sa rozhliada
        if (Time.time < patrolWaitUntil)
        {
            Face(Quaternion.Euler(0f, 50f * Mathf.Sin(Time.time * 1.1f), 0f) * transform.forward);
            patrolStuckPos = Pos;
            nextPatrolStuckCheck = Time.time + 3f;
            return;
        }

        Vector3 to = patrolDestination - Pos;
        to.y = 0f;
        if (to.sqrMagnitude <= 1.2f * 1.2f)
        {
            if (route != null && route.Valid)
            {
                Vector2 d = route.CurrentDwell;
                patrolWaitUntil = Time.time + (d.y > 0.01f ? Random.Range(d.x, d.y) : 0f);
                route.Advance();
            }
            ChoosePatrolPoint();
            return;
        }

        // Zaseknutý vojak preskočí bod trasy
        if (route != null && Time.time >= nextPatrolStuckCheck)
        {
            nextPatrolStuckCheck = Time.time + 3f;
            if ((Pos - patrolStuckPos).sqrMagnitude < 0.25f) { route.Advance(); ChoosePatrolPoint(); return; }
            patrolStuckPos = Pos;
        }

        Face(to);
        MoveBy(to.normalized * patrolSpeed * Time.fixedDeltaTime);
    }

    void ChoosePatrolPoint()
    {
        if (route != null && route.Valid)
        {
            patrolDestination = route.Current;
            return;
        }

        Vector2 offset = Random.insideUnitCircle * 6f;
        patrolDestination = patrolOrigin + new Vector3(offset.x, 0f, offset.y);
        if (militaryZone != null) patrolDestination = militaryZone.ClampInside(patrolDestination, 0.65f);
    }

    public void ReceiveFalseReport()
    {
        crimeKnownUntil = Time.time + huntSeconds;
        reportedHostile = true;
        reportHostileUntil = Time.time + 30f;
        if (player != null) lastKnown = player.transform.position;
        BecomeAlert(false);
    }

    /// <summary>Svedok nahlásil démona (hráča) pri ničení budovy: vojaci v okolí ho poznajú a idú po ňom.</summary>
    public static void ReportPlayerDemon(Vector3 position, float radius)
    {
        foreach (SoldierAI soldier in All)
            if (soldier != null && !soldier.IsDead && (soldier.transform.position - position).sqrMagnitude <= radius * radius)
                soldier.ReceiveFalseReport();
    }

    /// <summary>Svedok (civilista) zakričal: len vojaci v okolí sa dozvedia o zločine a idú sa pozrieť na miesto.</summary>
    public static void ReportCrimeAt(Vector3 witnessPos, float radius, Vector3 crimePos)
    {
        foreach (SoldierAI soldier in All)
            if (soldier != null && !soldier.IsDead && (soldier.transform.position - witnessPos).sqrMagnitude <= radius * radius)
                soldier.LearnCrime(crimePos);
        CityGuardNPC.ReportCrimeAt(witnessPos, radius, crimePos);
    }

    void LearnCrime(Vector3 where)
    {
        crimeKnownUntil = Time.time + huntSeconds;
        lastKnown = where;
        investigateUntil = Time.time + 10f;
        detection = Mathf.Max(detection, 0.5f);
        if (state == SoldierState.Patrol) state = SoldierState.Suspicious;
    }

    public static void ReportHumanToSoldiers()
    {
        foreach (SoldierAI soldier in All)
            if (soldier != null && !soldier.IsDead) soldier.ReceiveFalseReport();
    }

    public void ReceiveDemonReport(DemonEnemy demon)
    {
        if (demon == null || demon.Damage == null || demon.Damage.IsDead) return;
        reportedHostile = false;
        reportHostileUntil = 0f;
        reportedDemon = demon;
        state = SoldierState.Alert;
        alertUntil = Time.time + alertMemory;
        lastKnown = demon.transform.position;
    }

    /// <summary>Vojaci v dosahu sa rozbehnú k démonovi (napr. keď búra stavbu).</summary>
    public static void ReportDemonNear(Vector3 position, float radius, DemonEnemy demon)
    {
        foreach (SoldierAI soldier in All)
            if (soldier != null && !soldier.IsDead && (soldier.transform.position - position).sqrMagnitude <= radius * radius)
                soldier.ReceiveDemonReport(demon);
    }

    public static void ReportDemonToSoldiers(DemonEnemy demon)
    {
        foreach (SoldierAI soldier in All)
            if (soldier != null && !soldier.IsDead) soldier.ReceiveDemonReport(demon);
    }

    // Prilba a puška na modeli (len prototyp)
    void BuildGear()
    {
        if (gearBuilt || perception == null || perception.humanVisual == null) return;
        gearBuilt = true;

        Transform parent = perception.humanVisual.transform;

        GameObject helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        helmet.name = "Helmet";
        Destroy(helmet.GetComponent<Collider>());
        helmet.transform.SetParent(parent, false);
        helmet.transform.localPosition = new Vector3(0f, 0.78f, 0f);
        helmet.transform.localScale = new Vector3(0.62f, 0.34f, 0.62f);
        helmet.GetComponent<Renderer>().material.color = new Color(0.12f, 0.16f, 0.12f);

        GameObject rifle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rifle.name = "Rifle";
        Destroy(rifle.GetComponent<Collider>());
        rifle.transform.SetParent(parent, false);
        rifle.transform.localPosition = new Vector3(0.38f, 0.2f, 0.45f);
        rifle.transform.localScale = new Vector3(0.1f, 0.12f, 0.95f);
        rifle.GetComponent<Renderer>().material.color = new Color(0.08f, 0.08f, 0.09f);
    }

    // ---------- Vnímanie ----------
    void Update()
    {
        if (IsDead || JailManager.IsInJail || JailManager.MenuOpen) return;

        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            IgnorePlayerCollision();
            return;
        }

        if (Time.time >= nextSense)
        {
            nextSense = Time.time + 0.1f;
            Sense(0.1f);
        }

        if (Time.time >= nextBodyScan)
        {
            nextBodyScan = Time.time + 0.5f;
            ScanBodies();
            ScanTrail();
        }
    }

    /// <summary>Ako dobre vidí hráča: 0 = vôbec, do 1 = zblízka a na očiach.</summary>
    bool IsPlayerCurrentThreat()
    {
        if (player == null || player.IsDead || player.IsInvisible) return false;

        return KnowsCrime || IsDemonInMilitaryZone() || (player.IsDemon && DemonRaid.Wanted);
    }

    // Zločin je čerstvý (30 s) a TENTO vojak o ňom vie
    bool KnowsCrime => HasRecentCrime() && Time.time < crimeKnownUntil;

    // Hráč je cieľ aj vtedy, keď už zločin zastaral: kým vojak v poplachu naháňa a nestratil ho z dohľadu na viac než huntSeconds
    bool IsPlayerTarget()
    {
        if (player == null || player.IsDead || player.IsInvisible) return false;
        if (IsPlayerCurrentThreat()) return true;
        return state == SoldierState.Alert && reportedDemon == null && Time.time <= alertUntil;
    }

    static bool HasRecentCrime()
    {
        return CivilianAI.LastPlayerCrimeTime >= 0f && Time.time - CivilianAI.LastPlayerCrimeTime <= 30f;
    }

    // Démon vo vojenskej zóne vyčnieva: vojak ho začne podozrievať (rastie detekčný meter), nie strieľať hneď
    bool IsDemonInMilitaryZone()
    {
        return player != null && player.IsDemon && !Lair.Disguised && MilitaryZone.At(player.transform.position) != null;
    }

    float SeeStrength()
    {
        if (!IsPlayerTarget()) return 0f;

        Vector3 to = player.transform.position - Pos;
        to.y = 0f;
        float dist = to.magnitude;

        // Základ 14 m. V poplachu je vojak pozornejší (+30 %), pri vysokom podozrení mesta mierne (+15 %); strop 20 m
        float range = SightRadius * PlayerStealth.RangeFactor(transform);
        if (state == SoldierState.Alert) range *= 1.3f;
        if (Reputation.Suspicion >= 80f) range *= 1.15f;
        range = Mathf.Min(range, MaxSightRadius);
        if (dist > range) return 0f;

        bool close = dist <= closeAwareness;
        if (!close && Vector3.Angle(transform.forward, to) > viewAngle * 0.5f) return 0f;

        if (!StealthWorld.HasLineOfSight(Pos + Vector3.up * 0.8f, player.transform.position + Vector3.up * 0.3f))
            return 0f;

        return Mathf.Clamp01(1f - dist / Mathf.Max(0.1f, range)) * 0.8f + 0.2f;
    }

    void Sense(float dt)
    {
        if (reportedDemon != null)
        {
            if (reportedDemon.Damage == null || reportedDemon.Damage.IsDead)
                reportedDemon = null;
            else
            {
                state = SoldierState.Alert;
                lastKnown = reportedDemon.transform.position;
                return;
            }
        }

        if (!IsPlayerTarget())
        {
            if (state == SoldierState.Suspicious && Time.time < investigateUntil) return;
            if (state == SoldierState.Alert || state == SoldierState.Suspicious)
            {
                state = SoldierState.Patrol;
                detection = 0f;
                combatLocked = false;
            }
            return;
        }

        if (reportedHostile && Time.time <= reportHostileUntil)
        {
            if (player != null) lastKnown = player.transform.position;
            if (state != SoldierState.Alert) BecomeAlert(false);
            return;
        }
        reportedHostile = false;
        float see = SeeStrength();
        if (see > 0f && DemonRaid.Wanted) DemonRaid.Extend();   // kým ho vidí, hľadanie neutícha

        // Obyčajného občana si vojak nevšíma: odhalenie rastie len, ak hráč vyčnieva
        // (zakázané oblečenie, démon vo vojenskej zóne, hľadaný) alebo už je poplach
        bool suspicious = Wardrobe.Instance == null || Wardrobe.IsSuspicious || state == SoldierState.Alert || KnowsCrime || IsDemonInMilitaryZone() || DemonRaid.Wanted;

        if (see > 0f && suspicious)
        {
            lastKnown = player.transform.position;
            detection += see * detectRate * Wardrobe.SuspicionMultiplier() * dt;
        }
        else if (state != SoldierState.Alert)
        {
            detection -= loseRate * dt;
        }
        detection = Mathf.Clamp01(detection);

        if (see > 0f && !suspicious) see = 0f;   // nevidí v ňom hrozbu

        if (state == SoldierState.Alert)
        {
            // Kým ťa vidí, hon pokračuje; ak ťa nenájde do huntSeconds, prestane
            if (see > 0f) alertUntil = Time.time + huntSeconds;
            else if (Time.time > alertUntil)
            {
                state = SoldierState.Suspicious;
                combatLocked = false;
                detection = 0.5f;
                Say("Kam zmizol...?", new Color(1f, 0.85f, 0.4f));
            }
            return;
        }

        if (detection >= 1f)
        {
            BecomeAlert(true);
        }
        else if (detection >= suspiciousAt)
        {
            if (state != SoldierState.Suspicious) Say("Hm? Kto je tam?", new Color(1f, 0.85f, 0.4f));
            state = SoldierState.Suspicious;
        }
        else if (state == SoldierState.Suspicious && detection < 0.1f && Time.time >= investigateUntil)
        {
            state = SoldierState.Patrol;
        }
    }

    // ---------- Poplach ----------
    void BecomeAlert(bool addSuspicion)
    {
        if (IsDead || state == SoldierState.Alert) return;
        ReleaseBody();

        state = SoldierState.Alert;
        combatLocked = true;
        detection = 1f;
        crimeKnownUntil = Time.time + huntSeconds;
        alertUntil = Time.time + huntSeconds;
        if (player != null) lastKnown = player.transform.position;

        Say("Stoj! Poplach!", new Color(1f, 0.35f, 0.3f));

        if (addSuspicion)
        {
            Reputation.AddSuspicion(suspicionOnAlert);
            GameHUD.Message($"Vojaci ťa odhalili! Podozrenie mesta +{suspicionOnAlert:0}", 3.5f);
        }

        // Privolá ostatných vojakov v okolí
        float callSqr = callRadius * callRadius;
        foreach (SoldierAI s in All)
        {
            if (s == null || s == this || s.IsDead || s.state == SoldierState.Alert) continue;
            if ((s.transform.position - transform.position).sqrMagnitude <= callSqr)
                s.AlertTo(lastKnown);
        }
    }

    /// <summary>Iný vojak alebo svedok zavolal poplach.</summary>
    public void AlertTo(Vector3 where)
    {
        if (IsDead) return;
        crimeKnownUntil = Time.time + huntSeconds;
        alertUntil = Time.time + huntSeconds;
        lastKnown = where;
        if (state != SoldierState.Alert) BecomeAlert(false);
    }

    // Krvavé stopy po zabití: vojak, ktorý ich vidí, ide po najnovšej (smerom k hráčovi)
    void ScanTrail()
    {
        if (state == SoldierState.Alert) return;
        if (!PlayerBloodTrail.TryFindClue(transform, SightRadius, viewAngle, out Vector3 clue)) return;

        // Stopa krvi dokazuje zabitie: zločin sa tým "objaví", aj keď ho nikto nevidel
        if (!HasRecentCrime()) CivilianAI.LastPlayerCrimeTime = Time.time;
        bool first = Time.time >= investigateUntil;
        LearnCrime(clue);
        if (first) Say("Krv... niekto tadiaľto šiel!", new Color(1f, 0.5f, 0.3f));
    }

    void ScanBodies()
    {
        if (targetBody != null) return;   // jedno telo naraz

        foreach (StealthWorld.Body b in StealthWorld.Bodies)
        {
            if (b.examined || b.claimed) continue;
            if (!StealthWorld.CanSeePoint(transform, b.pos, SightRadius, viewAngle)) continue;

            // Hráč nesie telo: pristihnutý pri zločine
            if (b.corpse != null && b.corpse.IsCarried && player != null && !player.IsDead && !player.IsInvisible)
            {
                CivilianAI.LastPlayerCrimeTime = Time.time;
                lastKnown = player.transform.position;
                BecomeAlert(true);
                return;
            }

            if (!b.found)
            {
                b.found = true;
                Reputation.AddSuspicion(15f);
                if (b.corpse != null) KillConsequences.OnBodyDiscovered(b.corpse);
                Say("Telo! Niekto tu bol!", new Color(1f, 0.5f, 0.3f));
                GameHUD.Message("Vojak našiel telo! Podozrenie mesta +15", 4f);
            }

            // Ide telo skontrolovať; po prehliadke ho dá odviezť
            b.claimed = true;
            targetBody = b;
            examineTimer = 0f;
            if (HasRecentCrime()) crimeKnownUntil = Time.time + huntSeconds;
            lastKnown = b.pos;
            if (state == SoldierState.Patrol) state = SoldierState.Suspicious;
            investigateUntil = Time.time + 10f;
            detection = Mathf.Max(detection, 0.5f);
            return;
        }
    }

    void ReleaseBody()
    {
        if (targetBody != null) targetBody.claimed = false;
        targetBody = null;
        examineTimer = 0f;
    }

    void FinishExamine()
    {
        StealthWorld.Body body = targetBody;
        targetBody = null;
        examineTimer = 0f;
        investigateUntil = Time.time + 1f;
        Say("Odvezte ho.", new Color(1f, 0.85f, 0.4f));
        GameHUD.Message("Vojak skontroloval telo a dal ho odviezť.", 3f);
        StealthWorld.ExamineBody(body);
    }

    const float ExamineSeconds = 3.5f;

    // ---------- Pohyb a paľba ----------
    void FixedUpdate()
    {
        if (IsDead || player == null || JailManager.IsInJail || JailManager.MenuOpen) return;

        switch (state)
        {
            case SoldierState.Patrol:
                if (militaryZone != null || (route != null && route.Valid))   // vojenská zóna alebo pevná hliadka po uliciach
                {
                    if (wander != null) wander.enabled = false;
                    Patrol();
                }
                else if (wander != null)
                {
                    wander.speed = patrolSpeed;
                    wander.enabled = true;
                }
                break;

            case SoldierState.Suspicious:
                if (wander != null) wander.enabled = false;
                Investigate();
                break;

            case SoldierState.Alert:
                if (wander != null) wander.enabled = false;
                Combat();
                break;
        }
    }

    void Investigate()
    {
        if (targetBody != null)
        {
            if (targetBody.examined || (targetBody.corpse != null && targetBody.corpse.IsHidden)) ReleaseBody();
            else lastKnown = targetBody.pos;   // nesené telo sa hýbe
        }

        Vector3 to = lastKnown - Pos;
        to.y = 0f;
        float arrive = targetBody != null ? 2.5f : 3f;

        if (to.sqrMagnitude > arrive * arrive)
        {
            Face(to);
            MoveBy(to.normalized * investigateSpeed * Time.fixedDeltaTime);
        }
        else if (targetBody != null)
        {
            Face(to);
            investigateUntil = Mathf.Max(investigateUntil, Time.time + 2f);
            examineTimer += Time.fixedDeltaTime;
            if (examineTimer >= ExamineSeconds) FinishExamine();
        }
        else
        {
            // Rozhliada sa na mieste
            Face(Quaternion.Euler(0f, 60f * Mathf.Sin(Time.time * 1.3f), 0f) * transform.forward);
        }
    }

    void Combat()
    {
        if (!IsPlayerTarget() && reportedDemon == null)
        {
            state = SoldierState.Patrol;
            detection = 0f;
            combatLocked = false;
            return;
        }

        if (reportedDemon != null)
        {
            CombatDemon();
            return;
        }

        Vector3 to = player.transform.position - Pos;
        to.y = 0f;
        float dist = to.magnitude;

        bool sees = reportedHostile || SeeStrength() > 0f;
        if (!sees) to = lastKnown - Pos;   // ide na posledné známe miesto
        to.y = 0f;

        if (to.sqrMagnitude > 0.01f) Face(to);

        // Zločin: vojak hráča dobehne a zatkne ho (pokuta alebo väzenie), namiesto aby ho strieľal z diaľky
        // Démona nezatýkajú: strieľajú
        bool arrestChase = (HasRecentCrime() || Time.time < crimeKnownUntil) && !player.IsDead && !player.IsDemon && JailManager.Instance != null;
        if (arrestChase && sees && dist <= arrestRange)
        {
            Say("Stoj! Si zatknutý!", new Color(1f, 0.35f, 0.3f));
            JailManager.Instance.StartArrest();
            return;
        }

        if (arrestChase || dist > keepDistance || !sees)
        {
            // Zastaví sa pred cieľom a krok skráti, aby ho nepreskočil (jerk a prienik do hráča)
            float gap = to.magnitude - contactDistance;
            if (gap > 0f) MoveBy(to.normalized * Mathf.Min(chaseSpeed * Time.fixedDeltaTime, gap));
        }
        else if (dist < 4f)
        {
            MoveBy(-to.normalized * patrolSpeed * Time.fixedDeltaTime);   // ustupuje
        }

        // Pri naháňaní na zatknutie už z blízka nestrieľa
        bool holdFire = arrestChase && dist <= arrestFireCutoff;
        if (sees && !holdFire && dist <= shootRange && Time.time >= nextShot)
        {
            nextShot = Time.time + shootInterval / attackSpeedBoost * Random.Range(0.9f, 1.3f);
            Fire();
        }
    }

    void CombatDemon()
    {
        Vector3 to = reportedDemon.transform.position - Pos;
        to.y = 0f;
        float dist = to.magnitude;
        if (to.sqrMagnitude > 0.01f) Face(to);

        if (dist > keepDistance)
            MoveBy(to.normalized * chaseSpeed * Time.fixedDeltaTime);
        else if (dist < 4f)
            MoveBy(-to.normalized * patrolSpeed * Time.fixedDeltaTime);

        if (dist <= shootRange && Time.time >= nextShot)
        {
            Vector3 muzzle = Pos + Vector3.up * 0.7f + transform.forward * 0.6f;
            Vector3 target = reportedDemon.transform.position + Vector3.up * 0.9f;
            if (StealthWorld.HasLineOfSight(muzzle, target))
            {
                nextShot = Time.time + shootInterval / attackSpeedBoost;
                reportedDemon.Damage.TakeDamage(shootDamage, gameObject, DamageKind.Normal);
            }
        }
    }

    void Fire()
    {
        Vector3 muzzle = Pos + Vector3.up * 0.7f + transform.forward * 0.6f;
        Vector3 target = player.transform.position + Vector3.up * 0.6f;

        if (!StealthWorld.HasLineOfSight(muzzle, target))
        {
            Vfx.Beam(muzzle, muzzle + transform.forward * 0.4f, new Color(1f, 0.85f, 0.4f), 0.04f, 0.02f);
            return;
        }

        float chance = hitChance * (PlayerStealth.Instance != null && PlayerStealth.Instance.IsCrouched ? 0.8f : 1f);
        bool hit = Random.value < chance;

        Vector3 end = hit ? target : target + Random.insideUnitSphere * 1.4f;
        Vfx.Beam(muzzle, end, new Color(1f, 0.85f, 0.4f), 0.08f, 0.05f);

        if (hit) player.TakeDamage(shootDamage);
    }

    void MoveBy(Vector3 delta)
    {
        Vector3 from = rb != null ? rb.position : transform.position;
        delta *= GameSpeed.Npc;
        delta = NpcSteering.Adjust(from, delta, transform);   // vyhýba sa pevným predmetom
        Vector3 position = from + delta;
        if (militaryZone != null) position = militaryZone.ClampInside(position, 0.65f);
        if (cityZone != null) position = cityZone.ClampInside(position, 1f);   // z mesta nevyjde
        if (rb != null) rb.MovePosition(position);
        else transform.position = position;
    }

    void Face(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(dir);
        if (rb != null) rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, turnSpeed * Time.fixedDeltaTime));
        else transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.fixedDeltaTime);
    }

    void Say(string text, Color color)
    {
        if (Time.time < nextText) return;
        nextText = Time.time + 2f;
        GameHUD.WorldText(transform.position + Vector3.up * 2.8f, text, color);
    }

    // ---------- Udalosti ----------
    void HandleDamaged(float amount, GameObject attacker)
    {
        if (attacker == null) return;

        bool byPlayer = attacker.GetComponent<PlayerStats>() != null;
        if (byPlayer)
        {
            hitByPlayer = true;
            CivilianAI.LastPlayerCrimeTime = Time.time;
        }

        if (dmg.Health <= 0f) return;   // smrteľný zásah rieši HandleDied

        if (byPlayer && player != null)
        {
            lastKnown = player.transform.position;
            BecomeAlert(true);
        }
    }

    void HandleDied()
    {
        ReleaseBody();
        state = SoldierState.Dead;
        if (wander != null) wander.enabled = false;

        if (!hitByPlayer || StealthWorld.IsSilentKillNow) return;

        // Hlučné zabitie vojaka: svedkovia a tvrdá reakcia mesta
        Vector3 pos = transform.position;
        StealthWorld.RegisterBody(pos, true, dmg.displayName, dmg);   // trest za zabitie rieši KillConsequences

        int w = StealthWorld.CountWitnesses(pos, transform, 14f, 140f);
        if (w > 0)
        {
            float add = Mathf.Min(30f, 10f * w);
            Reputation.AddSuspicion(add);
            GameHUD.Message($"Zabil si vojaka pred svedkami! Podozrenie mesta +{add:0}", 4f);
            if (player != null) StealthWorld.AlarmWitnesses(pos, transform, player.transform.position, 14f, 140f);
        }
    }

    // ---------- Ukazovateľ nad hlavou ----------
    private static GUIStyle markStyle;
    private static Camera guiCam;

    void OnGUI()
    {
        if (IsDead || player == null) return;   // OnGUI beží na každom vojakovi: najlacnejšie kontroly prvé
        if (state == SoldierState.Patrol && detection <= 0.05f) return;   // nič na kreslenie
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!NpcOverlay.Visible(transform.position)) return;

        if (guiCam == null) guiCam = Camera.main;
        Camera cam = guiCam;
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.6f);
        if (p.z <= 0f || p.z > 40f) return;

        float sx = p.x;
        float sy = Screen.height - p.y;

        if (state == SoldierState.Suspicious || state == SoldierState.Alert)
        {
            bool alert = state == SoldierState.Alert;
            if (markStyle == null)
                markStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 24,
                    fontStyle = FontStyle.Bold
                };
            markStyle.normal.textColor = alert ? new Color(1f, 0.25f, 0.2f) : new Color(1f, 0.85f, 0.2f);
            GUI.Label(new Rect(sx - 20f, sy - 16f, 40f, 32f), alert ? "!" : "?", markStyle);
        }
        else if (detection > 0.05f)
        {
            // Odhaľovací pásik
            Rect bar = new Rect(sx - 22f, sy, 44f, 5f);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(1f, 1f, 1f), new Color(1f, 0.5f, 0.1f), detection);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * detection, bar.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
