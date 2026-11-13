using System.Collections;
using UnityEngine;

/// <summary>
/// Kňažka Carmilla (Klan Pekelnej Krvi), elita na kostre BossCore: lifesteal duel. Vysáva hráčovi krv na diaľku (lieči sa z poškodenia, ktoré spôsobí),
/// vyvoláva krvavé kaluže a z krvavej hmly formuje orby, ktoré strieľajú blesky.
/// Slabina podľa Postavy.txt: očistné sérum (Yumi), jed a anti-heal. Ak je hráč očistený, jej vysávanie sa obráti: dostane masívny spätný náraz,
/// je omráčená a nemôže sa liečiť. Vysávanie sa dá prerušiť aj krytom v ceste lúča alebo dostatočným poškodením počas neho.
/// Fázy: 100–70 % vysávanie a orby, 70–40 % pridáva kaluže, 40–10 % zúrivosť (rýchlejšie, viac orbov a kalúž). Pod 10 % QTE panel.
/// </summary>
public class CarmillaBoss : BossCore
{
    public const string FightId = "boss_carmilla";
    private const int MaxPools = 10;

    [Header("Carmilla")]
    [SerializeField] private float drainDps = 10f;
    [SerializeField] private float drainSeconds = 3.2f;
    [SerializeField] private float drainRange = 24f;
    [SerializeField, Range(0f, 1f)] private float staggerFraction = 0.06f;
    [SerializeField, Range(0f, 1f)] private float lifesteal = 0.7f;
    [SerializeField, Range(0f, 0.5f)] private float backlashFraction = 0.12f;
    [SerializeField] private float antiHealSeconds = 10f;
    [SerializeField] private float purifiedSeconds = 14f;
    [SerializeField] private float orbDamage = 10f;
    [SerializeField] private float poolDps = 7f;
    [SerializeField] private float poolRadius = 3.2f;
    [SerializeField] private float poolSeconds = 9f;

    public bool Purified => Time.time < purifiedUntil;
    public bool AntiHeal => Time.time < antiHealUntil;
    public int ActivePools { get { int n = 0; for (int i = 0; i < MaxPools; i++) if (poolUntil[i] > Time.time) n++; return n; } }

    private readonly Vector3[] poolPos = new Vector3[MaxPools];
    private readonly float[] poolUntil = new float[MaxPools];
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private float purifiedUntil, antiHealUntil, nextPoolFx;
    private float speedFactor = 1f;
    private int attackCounter;
    private bool drainInterrupted;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private static readonly Color Blood = new Color(0.85f, 0.05f, 0.15f);

    protected override void Awake()
    {
        base.Awake();
        bossId = "carmilla";
        bossName = "Kňažka Carmilla";
        Health.displayName = bossName;
        arenaRadius = 34f;
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    /// <summary>Očistné sérum od Yumi alebo korporátny jed: hráčova krv otrávi Carmillu pri vysávaní. Háčik pre budúce predmety.</summary>
    public static bool TryPurify(float seconds)
    {
        CarmillaBoss c = Current as CarmillaBoss;
        if (c == null || !c.InFight) return false;
        c.Purify(seconds);
        return true;
    }

    public void Purify(float seconds)
    {
        purifiedUntil = Time.time + (seconds > 0f ? seconds : purifiedSeconds);
        GameHUD.Message("Očistné sérum: tvoja krv je teraz jed pre Carmillu.", 4f);
        Vfx.Disc(player != null ? player.transform.position + Vector3.up * 0.1f : transform.position, 4f, new Color(0.5f, 1f, 0.6f, 0.6f), 0.6f);
    }

    // ---------- Začiatok a koniec ----------

    protected override void OnFightBegin()
    {
        MangaPanels.ForgetFight(FightId);
        ResetState();
        foreach (ArenaCover c in FindObjectsByType<ArenaCover>(FindObjectsInactive.Include)) c.Restore();
        GameHUD.Message("Carmilla sa lieči z tvojej krvi. Sérum od Yumi jej vysávanie obráti proti nej; stĺpy lúč blokujú.", 5f);
    }

    protected override void OnFightAborted() { ResetState(); }

    void ResetState()
    {
        transform.SetPositionAndRotation(startPosition, startRotation);
        speedFactor = 1f;
        attackCounter = 0;
        purifiedUntil = antiHealUntil = 0f;
        for (int i = 0; i < MaxPools; i++) poolUntil[i] = 0f;
        Phase = 1;
        CloseWindow();
    }

    // ---------- Lifesteal a kaluže ----------

    void Hurt(float damage)
    {
        if (player == null || player.IsDead) return;
        HurtPlayer(damage);
        if (!AntiHeal) Health.Heal(damage * lifesteal);
    }

    void Update()
    {
        if (!InFight || player == null || player.IsDead || FinisherRunning) return;
        bool fx = Time.time >= nextPoolFx;
        if (fx) nextPoolFx = Time.time + 0.2f;
        for (int i = 0; i < MaxPools; i++)
        {
            if (poolUntil[i] <= Time.time) continue;
            if (fx) Vfx.Disc(poolPos[i] + Vector3.up * 0.05f, poolRadius * 2f, new Color(0.6f, 0.02f, 0.08f, 0.55f), 0.25f);
            Vector3 d = player.transform.position - poolPos[i];
            d.y = 0f;
            if (d.magnitude < poolRadius) Hurt(poolDps * Time.deltaTime);
        }
    }

    // ---------- Poškodenie ----------

    protected override float ModifyIncoming(float amount, GameObject attacker, DamageKind kind)
    {
        if (kind == DamageKind.Blood) return amount * 0.3f;   // krv jej je vlastná
        if (AntiHeal)
        {
            if (amount > 2f) GameHUD.WorldText(transform.position + Vector3.up * 3.5f, "OTRAVENÁ", new Color(0.5f, 1f, 0.5f));
            return amount * 1.4f;
        }
        if (kind == DamageKind.Holy) return amount * 1.25f;
        return amount;
    }

    // ---------- Fázy ----------

    protected override IEnumerator OnPhaseEnter(int phase)
    {
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 12f, new Color(0.7f, 0.05f, 0.1f, 0.5f), 0.8f);
        if (phase == 2) { speedFactor = 1.2f; GameHUD.Message("Fáza 2: Krvavé kaluže na zemi. Nestoj v nich.", 4.5f); }
        else if (phase == 3) { speedFactor = 1.45f; GameHUD.Message("Fáza 3: Zúrivosť. Viac orbov a kalúž, dlhšie vysávanie.", 4.5f); }
        yield return new WaitForSeconds(1f);
    }

    protected override IEnumerator NextAttack()
    {
        attackCounter++;
        switch (Phase)
        {
            case 1:
                yield return attackCounter % 2 == 0 ? Orbs(3) : Drain();
                break;
            case 2:
                int k = attackCounter % 3;
                yield return k == 0 ? Drain() : k == 1 ? Orbs(4) : Pools(3);
                break;
            default:
                int j = attackCounter % 3;
                yield return j == 0 ? Drain() : j == 1 ? Pools(5) : Orbs(5);
                break;
        }
    }

    // ---------- Útoky ----------

    IEnumerator Hover(float seconds)
    {
        DebugState = "hover";
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            Vector3 to = FlatToPlayer();
            FaceTowards(to, 240f);
            float d = to.magnitude;
            Vector3 side = Vector3.Cross(to, Vector3.up).normalized;
            Move(side * 0.8f + (d > 14f ? to.normalized : d < 9f ? -to.normalized : Vector3.zero), 5.5f * speedFactor);
            yield return null;
        }
    }

    Vector3 Chest => transform.position + Vector3.up * 2.2f;

    /// <summary>Lúč blokuje prvý kryt/stena v ceste; hráč nesmie byť zakrytý. Vráti true, ak je cesta voľná.</summary>
    bool ClearLine(out Collider blocker)
    {
        blocker = null;
        Vector3 from = Chest;
        Vector3 to = player.transform.position + Vector3.up * 1.1f;
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        int n = Physics.RaycastNonAlloc(from, dir / dist, hits, dist, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i].collider;
            if (c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerStats>() != null) continue;
            if (hits[i].distance < best) { best = hits[i].distance; blocker = c; }
        }
        return blocker == null;
    }

    IEnumerator Drain()
    {
        DebugState = "drain";
        drainInterrupted = false;
        float tele = 1f / speedFactor;
        float t = 0f;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        while (t < tele)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 240f);
            Flash(Blood, Mathf.Lerp(0.3f, 1f, t / tele));
            Vfx.Beam(Chest, player.transform.position + Vector3.up * 1.1f, new Color(0.8f, 0.05f, 0.15f, 0.25f), 0.05f, 0.04f);
            yield return null;
        }

        float startHp = Health.Health;
        float length = drainSeconds * (Phase == 3 ? 1.3f : 1f);
        float elapsed = 0f;
        bool backlashed = false;
        while (elapsed < length && !drainInterrupted)
        {
            elapsed += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 240f);
            Vector3 target = player.transform.position + Vector3.up * 1.1f;
            if (FlatToPlayer().magnitude > drainRange) break;

            if (ClearLine(out Collider blocker))
            {
                Vfx.Beam(Chest, target, Blood, 0.08f, 0.14f);
                if (Purified)
                {
                    Backlash();
                    backlashed = true;
                    break;
                }
                Hurt(drainDps * speedFactor * Time.deltaTime);
            }
            else
            {
                // Lúč sa zasekne v kryte
                Vfx.Beam(Chest, hitPointOf(blocker), new Color(0.8f, 0.1f, 0.2f, 0.6f), 0.08f, 0.1f);
                ArenaCover cover = blocker.GetComponentInParent<ArenaCover>();
                if (cover != null) cover.Hit(40f * Time.deltaTime);
            }

            if (startHp - Health.Health > staggerFraction * Health.maxHealth)
            {
                drainInterrupted = true;
                GameHUD.WorldText(transform.position + Vector3.up * 4f, "PRERUŠENÁ", new Color(1f, 0.85f, 0.4f));
                Stun(1.5f);
            }
            yield return null;
        }
        if (!backlashed && !drainInterrupted) yield return Hover(Random.Range(0.6f, 1f) / speedFactor);
    }

    Vector3 hitPointOf(Collider c) => c != null ? c.ClosestPoint(Chest) : Chest;

    /// <summary>Hráč je očistený: krv ju otrávi. Masívny spätný náraz, omráčenie a anti-heal.</summary>
    void Backlash()
    {
        purifiedUntil = 0f;   // sérum sa spotrebovalo
        antiHealUntil = Time.time + antiHealSeconds;
        SelfDamage(Health.maxHealth * backlashFraction);
        Stun(3f);
        GameHUD.Message("Hráčova očistená krv otrávila Carmillu! Spätný náraz, nemôže sa liečiť.", 4.5f);
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 9f, new Color(0.4f, 1f, 0.5f, 0.6f), 0.7f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
    }

    IEnumerator Orbs(int count)
    {
        DebugState = "orbs";
        float tele = 0.9f / speedFactor;
        float t = 0f;
        var orbs = new Vector3[count];
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        while (t < tele)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 240f);
            Flash(Blood, Mathf.Lerp(0.3f, 1f, t / tele));
            for (int i = 0; i < count; i++)
            {
                float a = (i * 360f / count + t * 90f) * Mathf.Deg2Rad;
                orbs[i] = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.8f + Vector3.up * 3f;
                Vfx.Disc(orbs[i] - Vector3.up * 2.95f, 1.4f, new Color(0.9f, 0.1f, 0.2f, 0.5f), 0.06f);
                Vfx.Beam(orbs[i], player.transform.position + Vector3.up * 1.1f, new Color(0.8f, 0.05f, 0.15f, 0.2f), 0.05f, 0.03f);
            }
            yield return null;
        }
        for (int i = 0; i < count; i++)
        {
            FireBolt(orbs[i], player.transform.position + Vector3.up * 1.1f, orbDamage);
            yield return new WaitForSeconds(0.14f);
        }
        yield return Hover(Random.Range(0.8f, 1.3f) / speedFactor);
    }

    void FireBolt(Vector3 from, Vector3 to, float damage)
    {
        Vector3 dir = (to - from).normalized;
        int n = Physics.RaycastNonAlloc(from, dir, hits, 60f, ~0, QueryTriggerInteraction.Ignore);
        float best = 60f;
        RaycastHit bestHit = default;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform)) continue;
            if (hits[i].distance < best) { best = hits[i].distance; bestHit = hits[i]; found = true; }
        }
        Vector3 end = found ? bestHit.point : from + dir * 60f;
        Vfx.Beam(from, end, new Color(1f, 0.25f, 0.3f), 0.18f, 0.14f);
        GameAudio.Play(GameAudio.Sfx.TowerShot, from);
        if (!found) return;
        ArenaCover cover = bestHit.collider.GetComponentInParent<ArenaCover>();
        if (cover != null) cover.Hit(damage * 3f);
        else if (bestHit.collider.GetComponentInParent<PlayerStats>() != null) Hurt(damage);
    }

    IEnumerator Pools(int count)
    {
        DebugState = "pools";
        yield return Telegraph(0.8f / speedFactor, Blood);
        var pts = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 r = i == 0 ? Vector2.zero : Random.insideUnitCircle * 9f;
            pts[i] = player.transform.position + new Vector3(r.x, 0f, r.y);
            pts[i].y = transform.position.y;
        }
        float t = 0f;
        float tele = 1.1f / speedFactor;
        while (t < tele)
        {
            t += Time.deltaTime;
            for (int i = 0; i < count; i++) Vfx.Disc(pts[i] + Vector3.up * 0.05f, poolRadius * 2f, new Color(1f, 0.2f, 0.2f, 0.25f), 0.06f);
            yield return null;
        }
        for (int i = 0; i < count; i++) AddPool(pts[i]);
        GameAudio.Play(GameAudio.Sfx.Build, transform.position);
        yield return Hover(Random.Range(0.8f, 1.2f) / speedFactor);
    }

    void AddPool(Vector3 pos)
    {
        int slot = 0;
        float oldest = float.MaxValue;
        for (int i = 0; i < MaxPools; i++)
        {
            if (poolUntil[i] <= Time.time) { slot = i; break; }
            if (poolUntil[i] < oldest) { oldest = poolUntil[i]; slot = i; }
        }
        poolPos[slot] = pos;
        poolUntil[slot] = Time.time + poolSeconds;
    }

    // ---------- Finišer ----------

    protected override IEnumerator RunFinisher()
    {
        DebugState = "finisher";
        CloseWindow();
        for (int i = 0; i < MaxPools; i++) poolUntil[i] = 0f;
        PanelPage[] pages =
        {
            new PanelPage(
                PanelSpec.Narrative("Krvavé orby praskajú. Carmilla padá na kolená do vlastnej kaluže.", PanelArt.Figure),
                PanelSpec.Narrative("Rituálna dýka je na dosah. Prerušíš ju, alebo ťa pohltí.", PanelArt.Eye),
                PanelSpec.Qte(KeyCode.F, 2.4f, 15f, "Prerušenie rituálu: F v zelenom pásme!")),
            new PanelPage(PanelSpec.Narrative("", PanelArt.Blade, null, new Vector2(0.4f, 0.6f)), PanelSpec.Result()),
            new PanelPage(
                PanelSpec.Narrative("Carmilla: „Tvoja krv... mala byť čistá. Arakani ťa pohltí tak či tak.“", PanelArt.Eye),
                PanelSpec.Narrative("Rituál sa rozpadá. Mágia krvi tečie do teba.", PanelArt.Burst))
        };
        yield return PlayPanels(FightId, pages);

        Vfx.Disc(transform.position + Vector3.up * 0.3f, 10f, new Color(0.8f, 0.05f, 0.15f, 0.6f), 0.9f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        yield return new WaitForSeconds(0.6f);
        GameHUD.Message("Carmilla je porazená. Krvavý rituál prerušený, no ortodoxní uctievači Arakaniho ti nedôverujú.", 6f);
        DefeatCleanup("defeated");
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }
}
