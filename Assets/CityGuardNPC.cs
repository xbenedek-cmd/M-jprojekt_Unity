using UnityEngine;

/// <summary>
/// Mestský strážnik. Hliadkuje po svojej trase (PatrolPlanner) a nevyjde z mesta. O zločine hráča vie len vtedy, keď ho sám videl,
/// dostal hlásenie od svedka, nájde telo alebo krvavú stopu. Potom hráča naháňa, kým ho vidí; ak ho nenájde do huntSeconds, prestane.
/// Nájdené telo skontroluje a dá odviezť.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CityGuardNPC : MonoBehaviour
{
    public static readonly System.Collections.Generic.List<CityGuardNPC> All = new System.Collections.Generic.List<CityGuardNPC>();
    public DistrictWard district = DistrictWard.Military;
    public float speed = 6f;
    public float turnSpeed = 10f;
    public float patrolRadius = 8f;
    public float stopDistance = 4f;

    [Header("Boj")]
    public float alertRange = 50f;
    public float attackRange = 10f;
    public float damagePerSecond = 40f;

    [Header("Zatknutie a vyšetrovanie")]
    public float arrestRange = 2.5f;
    public float corpseInvestigateRange = 15f;
    [Tooltip("Ako dlho vojak obzerá telo, kým ho nahlási")]
    public float investigateSeconds = 5f;
    [Tooltip("Ako dlho po náleze tela hľadá vraha")]
    public float searchSeconds = 15f;
    public float searchRadius = 12f;
    [Tooltip("Sekundy, počas ktorých strážnik po tom, čo ťa naposledy videl, ťa hľadá a naháňa")]
    public float huntSeconds = 30f;

    private Vector3 origin;
    private Vector3 destination;
    private Rigidbody rb;
    private Damageable damage;
    private DemonEnemy targetDemon;
    private float nextTargetScan;
    private float nextFire;
    private float immuneTimer = 0f;

    private PlayerStats playerStats;
    private Transform player;
    private bool isChasingPlayer = false;
    private bool hasArrested = false;

    private Damageable targetCorpse;
    private float nextCorpseScan;
    private float investigateStart = -1f;
    private float searchUntil;
    private bool searchingKiller;
    private Vector3 searchCenter;
    private Vector3 searchPoint;
    private float nextSearchPick;

    // Vedomosť o zločine (len tento strážnik)
    private float crimeKnownUntil;
    private float huntUntil;
    private Vector3 lastSeenPlayer;
    private bool seesPlayerNow;

    private PatrolRoute route;
    private ZonePopulation cityZone;
    private float patrolWaitUntil;
    private Vector3 patrolStuckPos;
    private float nextPatrolStuckCheck;

    private Vector3 currentMoveDir;
    private Vector3 currentLookDir;
    private bool shouldMove;

    public bool IsDead => damage != null && damage.IsDead;
    public bool CanBeTakenDown => !IsDead && !isChasingPlayer && targetDemon == null;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        damage = GetComponent<Damageable>();
        if (GetComponent<NPCGrounding>() == null) gameObject.AddComponent<NPCGrounding>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        origin = transform.position;

        if (TryGetComponent(out DistrictOutfit outfit))
            outfit.district = district;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        cityZone = CityPopulation.At(transform.position);
    }

    void OnDisable() { All.Remove(this); }

    public static void ClearPlayerHostility()
    {
        foreach (CityGuardNPC guard in All)
        {
            if (guard == null) continue;
            guard.isChasingPlayer = false;
            guard.hasArrested = false;
            guard.searchUntil = 0f;
            guard.searchingKiller = false;
            guard.crimeKnownUntil = 0f;
            guard.huntUntil = 0f;
        }
    }

    void Start()
    {
        playerStats = FindAnyObjectByType<PlayerStats>();
        if (playerStats != null) player = playerStats.transform;

        cityZone = CityPopulation.At(transform.position);
        route = PatrolPlanner.Build(transform, cityZone, null, PatrolRole.Guard);
        ChooseNextPatrolPoint();

        if (TryGetComponent(out DistrictOutfit outfit))
            outfit.ApplyDistrictColors();
    }

    // ---------- Vedomosť o zločine ----------
    /// <summary>Svedok alebo iný vojak: strážnik vie, že hráč spáchal zločin, a začne ho hľadať.</summary>
    public void AlertTo(Vector3 where)
    {
        if (IsDead) return;
        crimeKnownUntil = Time.time + huntSeconds;
        huntUntil = Time.time + huntSeconds;
        lastSeenPlayer = where;
    }

    /// <summary>Strážnik sa dozvedel o zločine (volanie svedka, stopa): ide sa pozrieť na miesto a hľadá vraha.</summary>
    public void LearnCrime(Vector3 where)
    {
        if (IsDead) return;
        crimeKnownUntil = Time.time + huntSeconds;
        searchingKiller = true;
        searchCenter = where;
        searchUntil = Time.time + searchSeconds;
        PickSearchPoint();
    }

    public static void ReportCrimeAt(Vector3 witnessPos, float radius, Vector3 crimePos)
    {
        foreach (CityGuardNPC guard in All)
            if (guard != null && !guard.IsDead && (guard.transform.position - witnessPos).sqrMagnitude <= radius * radius)
                guard.LearnCrime(crimePos);
    }

    static bool HasRecentCrime()
    {
        return CivilianAI.LastPlayerCrimeTime >= 0f && Time.time - CivilianAI.LastPlayerCrimeTime <= 30f;
    }

    // Vidí strážnik hráča? Dosah 14 m (v honbe viac, strop 20 m), zorný kužeľ a výhľad
    bool SeesPlayer()
    {
        if (player == null || playerStats == null || playerStats.IsDead || playerStats.IsInvisible) return false;

        Vector3 to = player.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        float range = SoldierAI.SightRadius * PlayerStealth.RangeFactor(transform);
        if (Time.time <= huntUntil) range *= 1.3f;
        range = Mathf.Min(range, SoldierAI.MaxSightRadius);
        if (dist > range) return false;

        if (dist > 2.2f && Vector3.Angle(transform.forward, to) > 60f) return false;
        return StealthWorld.HasLineOfSight(transform.position + Vector3.up * 1.4f, player.position + Vector3.up * 0.8f);
    }

    // True, kým strážnik hráča naháňa (vie o zločine a vidí ho, alebo ho videl pred menej než huntSeconds)
    bool UpdateHunt()
    {
        seesPlayerNow = false;
        if (player == null || playerStats == null || playerStats.IsDead) return false;

        bool knows = HasRecentCrime() && Time.time < crimeKnownUntil;
        if (!knows && Time.time > huntUntil) return false;

        if (SeesPlayer())
        {
            seesPlayerNow = true;
            huntUntil = Time.time + huntSeconds;
            lastSeenPlayer = player.position;
            return true;
        }
        return Time.time <= huntUntil;
    }

    void Update()
    {
        if (IsDead) { shouldMove = false; return; }

        ScanForTargets();
        ScanForCorpses();

        shouldMove = false;

        if (JailManager.MenuOpen || JailManager.IsInJail) return;

        bool playerIsCriminal = UpdateHunt();

        // 1. Prioritný cieľ: Odhalený démon
        if (targetDemon != null && targetDemon.Damage != null && !targetDemon.Damage.IsDead)
        {
            isChasingPlayer = false;
            Vector3 toDemon = targetDemon.transform.position - transform.position;
            toDemon.y = 0f;
            float distSqr = toDemon.sqrMagnitude;
            currentLookDir = toDemon.normalized;

            if (distSqr <= attackRange * attackRange)
            {
                if (Time.time >= nextFire)
                {
                    nextFire = Time.time + 0.25f;
                    Vfx.Beam(transform.position + Vector3.up * 1.5f, targetDemon.transform.position + Vector3.up * 1f, new Color(1f, 0.6f, 0.2f), 0.1f, 0.08f);
                    HitResult res = targetDemon.Damage.TakeDamage(damagePerSecond * 0.25f, gameObject, DamageKind.Normal);

                    // Ak je démon nezraniteľný (vyšší démon), vojaci ho po 2s sústredenej paľby zničia
                    if (res == HitResult.Immune)
                    {
                        immuneTimer += 0.25f;
                        if (immuneTimer >= 2f)
                        {
                            targetDemon.Damage.ForceKill();
                            GameHUD.Message("Vojaci zničili vyššieho démona!", 3f);
                        }

                    }
                    else immuneTimer = 0f;
                }
                return;
            }
            else
            {
                currentMoveDir = currentLookDir;
                shouldMove = true;
                return;
            }
        }
        else immuneTimer = 0f;

        // 2. Ak nie je démon, ale strážnik vie o zločine hráča -> naháňa ho a zatkne
        if (playerIsCriminal)
        {
            if (!isChasingPlayer)
            {
                isChasingPlayer = true;
                hasArrested = false;
                GameHUD.Message("Strážnik ťa spoznal ako zločinca a beží ťa zatknúť!", 2f);
            }

            Vector3 goal = seesPlayerNow ? player.position : lastSeenPlayer;
            Vector3 toGoal = goal - transform.position;
            toGoal.y = 0f;

            if (seesPlayerNow && toGoal.sqrMagnitude <= arrestRange * arrestRange)
            {
                currentLookDir = toGoal.normalized;
                if (!hasArrested)
                {
                    hasArrested = true;
                    if (JailManager.Instance != null) JailManager.Instance.StartArrest();
                }
                return;
            }

            // Dorazil na posledné známe miesto a hráča nevidí: prehľadáva okolie
            if (!seesPlayerNow && toGoal.sqrMagnitude < 9f)
            {
                searchCenter = lastSeenPlayer;
                if (Time.time >= nextSearchPick) { nextSearchPick = Time.time + 3f; PickSearchPoint(); }
                toGoal = searchPoint - transform.position;
                toGoal.y = 0f;
            }

            currentLookDir = toGoal.sqrMagnitude > 0.01f ? toGoal.normalized : currentLookDir;
            currentMoveDir = currentLookDir;
            shouldMove = toGoal.sqrMagnitude > 0.25f;
            return;
        }
        else
        {
            isChasingPlayer = false;
            hasArrested = false;
        }

        // 3. Vyšetrovanie mŕtvoly
        if (targetCorpse != null && Attracts(targetCorpse))
        {
            Vector3 toCorpse = targetCorpse.transform.position - transform.position;
            toCorpse.y = 0f;
            float distSqr = toCorpse.sqrMagnitude;
            currentLookDir = toCorpse.normalized;

            if (distSqr <= 4f)
            {
                currentLookDir = Vector3.zero; // Prestane sa otáčať
                if (investigateStart < 0f) investigateStart = Time.time;

                // Hráč stojí pri čerstvej mŕtvole démona -> nie je to zločin
                if (player != null && !playerStats.IsDead && targetCorpse.KilledByPlayer
                    && targetCorpse.GetComponent<DemonEnemy>() != null)
                {
                    GameHUD.Message("Vojak si ťa nevšimol ako zločinca pri mŕtvole démona.", 2f);
                    targetCorpse.MarkReported();
                    targetCorpse = null;
                    investigateStart = -1f;
                    return;
                }

                // Hráč stojí pri čerstvej mŕtvole, ktorú zabil on, a strážnik ho vidí -> zatknutie
                if (player != null && !playerStats.IsDead && targetCorpse.KilledByPlayer && SeesPlayer())
                {
                    float distToPlayerSqr = (player.position - targetCorpse.transform.position).sqrMagnitude;
                    if (distToPlayerSqr <= 25f && Time.time - targetCorpse.DeathTime < 20f)
                    {
                        GameHUD.Message("Vojak si ťa všimol pri mŕtvole!", 2f);
                        CivilianAI.LastPlayerCrimeTime = Time.time;
                        AlertTo(player.position);
                        if (JailManager.Instance != null) JailManager.Instance.StartArrest();
                    }
                }

                // Obzrel telo -> nahlási ho, dá odviezť, a ak ho zabil hráč, začne hľadať vraha
                if (Time.time - investigateStart >= investigateSeconds)
                {
                    Damageable examined = targetCorpse;
                    searchingKiller = examined.KilledByPlayer;
                    searchCenter = examined.transform.position;
                    searchUntil = Time.time + searchSeconds;
                    if (searchingKiller) crimeKnownUntil = Time.time + huntSeconds;
                    PickSearchPoint();

                    examined.MarkReported();
                    KillConsequences.OnBodyDiscovered(examined);
                    examined.RemoveCorpse();   // telo odvezú
                    targetCorpse = null;
                    investigateStart = -1f;

                    GameHUD.Message(searchingKiller ? "Vojak skontroloval telo, dal ho odviezť a hľadá vraha!" : "Vojak skontroloval telo a dal ho odviezť.", 2f);
                }
                return;
            }
            else
            {
                investigateStart = -1f;
                currentMoveDir = currentLookDir;
                shouldMove = true;
                return;
            }
        }
        else investigateStart = -1f;

        // 3b. Pátranie po vrahovi v okolí nájdeného tela
        if (Time.time < searchUntil)
        {
            SearchArea();
            return;
        }

        // 4. Ak nič z toho, strážnik hliadkuje po svojej trase
        Patrol();
    }

    void FixedUpdate()
    {
        if (IsDead || JailManager.MenuOpen || JailManager.IsInJail) return;

        if (currentLookDir.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(currentLookDir, Vector3.up);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, look, turnSpeed * Time.fixedDeltaTime));
        }

        if (shouldMove)
        {
            float step = speed * Time.fixedDeltaTime;

            if (targetDemon != null || isChasingPlayer || targetCorpse != null)
            {
                MoveTo(rb.position + NpcSteering.Adjust(rb.position, currentMoveDir * step, transform));
            }
            else
            {
                if (!Physics.SphereCast(rb.position + Vector3.up * 0.5f, 0.4f, currentMoveDir, out RaycastHit hit, step, ~0, QueryTriggerInteraction.Ignore))
                {
                    MoveTo(rb.position + currentMoveDir * step);
                }
            }
        }
    }

    void MoveTo(Vector3 position)
    {
        if (cityZone != null) position = cityZone.ClampInside(position, 1f);   // z mesta nevyjde
        rb.MovePosition(position);
    }

    // Mŕtvola, ktorú má strážnik vyšetriť: telo človeka, kým ho nik neskontroloval; inak čerstvá mŕtvola
    static bool Attracts(Damageable c) => c != null && (c.IsHumanBody ? !c.IsHidden && !c.IsReported : c.AttractsAttention);

    void ScanForTargets()
    {
        // OPRAVA: Ak už máme cieľ a je živý, nehľadáme iný a nepúšťame ho!
        if (targetDemon != null)
        {
            if (targetDemon.Damage == null || targetDemon.Damage.IsDead)
            {
                targetDemon = null; // Cieľ zomrel, uvoľníme ho
            }
            else
            {
                return; // Cieľ žije, ostaneme pri ňom a ideme ho zabiť
            }
        }

        if (Time.time < nextTargetScan) return;
        nextTargetScan = Time.time + 0.5f;

        DemonEnemy nearest = null;
        float bestSqr = alertRange * alertRange;
        Vector3 myPos = transform.position;

        // DemonEnemy.All namiesto FindObjectsByType (alokácia pola + prehladanie scény kazdých 0,5 s na kazdého strázcu)
        foreach (DemonEnemy e in DemonEnemy.All)
        {
            if (e == null || e.Damage == null || e.Damage.IsDead) continue;

            float d = (e.transform.position - myPos).sqrMagnitude;
            if (d >= bestSqr) continue;   // lacná vzdialenost pred GetComponent

            if (e.GetComponent<PlayerStats>() != null) continue;

            NPCPerception npc = e.GetComponent<NPCPerception>();
            bool isRevealed = npc != null && npc.Revealed;
            bool isAggro = e.IsChasing;

            if (!isRevealed && !isAggro) continue;

            bestSqr = d;
            nearest = e;
        }

        if (nearest != null)
        {
            targetDemon = nearest;
            GameHUD.Message($"Vojak si všimol démona a beží ho zabiť!", 2f);
        }
    }

    void ScanForCorpses()
    {
        if (Time.time < nextCorpseScan) return;
        nextCorpseScan = Time.time + 1f;

        // Krvavé stopy po zabití: strážnik ide po najnovšej smerom k hráčovi
        if (PlayerBloodTrail.TryFindClue(transform, SoldierAI.SightRadius, 120f, out Vector3 clue))
        {
            if (!HasRecentCrime()) CivilianAI.LastPlayerCrimeTime = Time.time;
            LearnCrime(clue);
        }

        if (targetCorpse != null && Attracts(targetCorpse)) return;

        targetCorpse = null;
        float bestSqr = corpseInvestigateRange * corpseInvestigateRange;
        Vector3 eye = transform.position + Vector3.up * 1.6f;

        foreach (Damageable corpse in Damageable.AllCorpses)
        {
            if (!Attracts(corpse)) continue;
            // A reported demon is an authorized military target, never a player crime.
            if (corpse.OfficiallyReportedDemon) continue;

            float d = (corpse.transform.position - transform.position).sqrMagnitude;
            if (d >= bestSqr || !Corpse.IsVisibleFrom(eye, corpse, corpseInvestigateRange)) continue;

            // Hráč nesie telo: strážnik ho na mieste odhalí
            if (corpse.IsCarried)
            {
                if (SeesPlayer())
                {
                    CivilianAI.LastPlayerCrimeTime = Time.time;
                    AlertTo(player.position);
                    GameHUD.Message("Strážnik ťa videl niesť telo!", 2f);
                }
                continue;
            }

            bestSqr = d;
            targetCorpse = corpse;
        }
    }

    void SearchArea()
    {
        // Vrah (hráč) na dohľad -> zločinec, strážnik ho začne naháňať
        if (searchingKiller && SeesPlayer())
        {
            CivilianAI.LastPlayerCrimeTime = Time.time;
            AlertTo(player.position);
            searchUntil = 0f;
            GameHUD.Message("Vojak ťa spoznal ako vraha!", 2f);
            return;
        }

        Vector3 toPoint = searchPoint - transform.position;
        toPoint.y = 0f;

        if (toPoint.sqrMagnitude <= stopDistance * stopDistance)
        {
            PickSearchPoint();
            return;
        }

        currentLookDir = toPoint.normalized;
        currentMoveDir = currentLookDir;
        shouldMove = true;
    }

    void PickSearchPoint()
    {
        Vector2 offset = Random.insideUnitCircle * searchRadius;
        searchPoint = searchCenter + new Vector3(offset.x, 0f, offset.y);
        if (cityZone != null) searchPoint = cityZone.ClampInside(searchPoint, 1f);
    }

    void Patrol()
    {
        // Na bode trasy sa rozhliada
        if (Time.time < patrolWaitUntil)
        {
            currentLookDir = Quaternion.Euler(0f, 50f * Mathf.Sin(Time.time * 1.1f), 0f) * transform.forward;
            patrolStuckPos = transform.position;
            nextPatrolStuckCheck = Time.time + 3f;
            return;
        }

        Vector3 toTarget = destination - transform.position;
        toTarget.y = 0f;
        bool onRoute = route != null && route.Valid;
        float arrive = onRoute ? 1.2f : stopDistance;

        if (toTarget.sqrMagnitude <= arrive * arrive)
        {
            if (onRoute)
            {
                Vector2 d = route.CurrentDwell;
                patrolWaitUntil = Time.time + (d.y > 0.01f ? Random.Range(d.x, d.y) : 0f);
                route.Advance();
            }
            ChooseNextPatrolPoint();
            return;
        }

        // Zaseknutý strážnik preskočí bod trasy
        if (onRoute && Time.time >= nextPatrolStuckCheck)
        {
            nextPatrolStuckCheck = Time.time + 3f;
            if ((transform.position - patrolStuckPos).sqrMagnitude < 0.25f) { route.Advance(); ChooseNextPatrolPoint(); return; }
            patrolStuckPos = transform.position;
        }

        currentLookDir = toTarget.normalized;
        currentMoveDir = currentLookDir;
        shouldMove = true;
    }

    void ChooseNextPatrolPoint()
    {
        if (route != null && route.Valid)
        {
            destination = route.Current;
            return;
        }

        Vector2 offset = Random.insideUnitCircle * patrolRadius;
        destination = origin + new Vector3(offset.x, 0f, offset.y);
        if (cityZone != null) destination = cityZone.ClampInside(destination, 1f);
    }
}
