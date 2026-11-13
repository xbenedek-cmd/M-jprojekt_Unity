using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Veliteľ Ren v mecha obleku. Fáza 1 (100–70 %): štít zapnutý (vlastné HP; zhodí ho EMP, reťazový blesk alebo zostrelenie oboch dronov),
/// laserové salvy a drony. Fáza 2 (70–40 %): prehriatie, pri dlhej paľbe sa otvoria ventily na chrbte (zásah zozadu ×3). Fáza 3 (40–10 %):
/// zúfalstvo, blízky boj; v pauze medzi útokmi ho paralyzuje elektrický zásah. Posledných 10 %: QTE panel (prerazenie kokpitu) a voľba zabiť/ušetriť.
/// Model je vymeniteľný: všetko ostatné sa viaže len na BossModelRig (kotvy) a BossHitZone (ventily).
/// </summary>
public class RenBoss : BossCore
{
    public const string FightId = "boss_ren";

    [Header("Ren")]
    [SerializeField] private float moveSpeed = 3.4f;
    [SerializeField] private float salvoDamage = 9f;
    [SerializeField] private float laserDps = 20f;
    [SerializeField] private float punchDamage = 24f;
    [SerializeField] private float stompDamage = 26f;
    [SerializeField] private float flameDps = 14f;
    [SerializeField] private float shieldMax = 100f;
    [SerializeField] private float shieldDownSeconds = 9f;
    [SerializeField] private float coverShotEvery = 14f;
    [SerializeField] private Material droneMaterial;

    public PlayerStats PlayerRef => player;
    public bool ShieldUp { get; private set; }
    public float ShieldHp { get; private set; }
    public int AliveDrones => drones.Count;
    public bool Exposed { get; private set; }

    private readonly List<RenDrone> drones = new List<RenDrone>();
    private float shieldRestoreAt, nextCoverAt;
    private int ventHits;
    private bool ventsOpen;
    private int attackCounter;
    private float speedFactor = 1f;
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private Vector3 startPosition;
    private Quaternion startRotation;

    protected override void Awake()
    {
        base.Awake();
        bossId = "ren";
        bossName = "Veliteľ Ren";
        Health.displayName = bossName;
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    // ---------- Začiatok a koniec ----------

    protected override void OnFightBegin()
    {
        MangaPanels.ForgetFight(FightId);   // nový súboj = panely smú znova
        transform.SetPositionAndRotation(startPosition, startRotation);
        speedFactor = 1f;
        Exposed = false;
        SetVentGlow(false);
        SetZones("vent", false);
        foreach (ArenaCover c in FindObjectsByType<ArenaCover>(FindObjectsInactive.Include)) c.Restore();
        nextCoverAt = Time.time + 12f;
        RaiseShield();
    }

    protected override void OnFightAborted()
    {
        ClearDrones();
        SetShield(false);
        Exposed = false;
        SetVentGlow(false);
        SetZones("vent", false);
        transform.SetPositionAndRotation(startPosition, startRotation);
        Phase = 1;
    }

    void ClearDrones()
    {
        foreach (RenDrone d in drones) if (d != null) Destroy(d.gameObject);
        drones.Clear();
    }

    // ---------- Štít a drony ----------

    void RaiseShield()
    {
        ShieldHp = shieldMax;
        SetShield(true);
        ClearDrones();
        for (int i = 0; i < 2; i++) drones.Add(RenDrone.Spawn(this, i * 180f, droneMaterial));
        GameHUD.Message("Ren zapína štít. Dva drony ho napájajú.", 3f);
    }

    void SetShield(bool on)
    {
        ShieldUp = on;
        if (rig != null && rig.shieldVisual != null) rig.shieldVisual.SetActive(on);
    }

    void BreakShield(string why)
    {
        if (!ShieldUp) return;
        SetShield(false);
        ShieldHp = 0f;
        shieldRestoreAt = Time.time + shieldDownSeconds;
        ClearDrones();
        GameHUD.Message(why, 3.5f);
        GameHUD.WorldText(transform.position + Vector3.up * 5f, "ŠTÍT DOLE!", new Color(0.4f, 0.9f, 1f));
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 9f, new Color(0.3f, 0.8f, 1f, 0.5f), 0.6f);
        OpenWindow(shieldDownSeconds - 1f, 1f);
        Stun(1.8f);   // preťaženie generátora ho na chvíľu zastaví
    }

    public void NotifyDroneDown(RenDrone d)
    {
        drones.Remove(d);
        if (ShieldUp && drones.Count == 0) BreakShield("Oba drony sú zostrelené: štít stratil napájanie!");
    }

    protected override void OnAbility(AbilityKind kind, Vector3 position)
    {
        if (!ShieldUp) return;
        if (kind == AbilityKind.EmpPulse && (position - transform.position).magnitude <= 14f)
            BreakShield("EMP impulz preťažil štít!");   // EMP má proti mecha väčší dosah než na bežných nepriateľoch
    }

    protected override float ModifyIncoming(float amount, GameObject attacker, DamageKind kind)
    {
        if (ShieldUp)
        {
            if (kind == DamageKind.Lightning) BreakShield("Reťazový blesk preťažil generátor štítu!");
            else GameHUD.WorldText(transform.position + Vector3.up * 4f, "ŠTÍT", new Color(0.4f, 0.8f, 1f));
            return 0f;
        }

        if (Phase == 2)
        {
            // Mimo ventilov takmer nič (brnenie); zásah do ventilov ×3 už pridala zóna
            if (LastHitZone == null) amount *= ventsOpen ? 0.08f : 0.5f;
            return amount;
        }

        if (Phase == 3 && Exposed && kind == DamageKind.Lightning && !IsStunned)
        {
            Stun(3.5f);
            GameHUD.Message("Elektrický zásah paralyzoval mecha!", 3f);
        }
        return amount;
    }

    protected override void OnZoneHit(BossHitZone zone, float damage)
    {
        if (ventsOpen) ventHits++;
    }

    protected override void OnStunned()
    {
        GameHUD.WorldText(transform.position + Vector3.up * 5f, "PARALYZOVANÝ", new Color(1f, 0.95f, 0.3f));
    }

    // ---------- Fázy ----------

    protected override IEnumerator OnPhaseEnter(int phase)
    {
        Exposed = false;
        if (phase >= 2) { SetShield(false); ClearDrones(); CloseWindow(); }   // aj pri preskočení fázy veľkým zásahom
        if (phase == 2)
        {
            GameHUD.Message("Fáza 2: Prehriatie. Pri dlhej paľbe sa mu otvoria chladiace ventily na chrbte.", 5f);
            Vfx.Disc(transform.position + Vector3.up * 0.2f, 10f, new Color(1f, 0.5f, 0.2f, 0.5f), 0.8f);
            yield return new WaitForSeconds(1.5f);
        }
        else if (phase == 3)
        {
            ventsOpen = false;
            SetZones("vent", false);
            SetVentGlow(false);
            speedFactor = 1.5f;
            GameHUD.Message("Fáza 3: Mecha je poškodená. Ren ide do boja zblízka. V pauze medzi útokmi ho paralyzuje elektrina.", 5f);
            Vfx.Disc(transform.position + Vector3.up * 0.2f, 11f, new Color(1f, 0.2f, 0.1f, 0.5f), 0.8f);
            yield return new WaitForSeconds(1.5f);
        }
    }

    protected override IEnumerator NextAttack()
    {
        // Obnova štítu po vypršaní okna (len vo fáze 1)
        if (Phase == 1 && !ShieldUp && Time.time >= shieldRestoreAt) { RaiseShield(); CloseWindow(); }

        attackCounter++;
        if (Phase < 3 && Time.time >= nextCoverAt && FirstCoverNearPlayer() != null)
        {
            nextCoverAt = Time.time + coverShotEvery;
            yield return DestroyCover();
            yield break;
        }

        switch (Phase)
        {
            case 1:
                yield return Random.value < 0.8f ? Salvo() : Reposition(1.5f);
                break;
            case 2:
                yield return attackCounter % 2 == 0 ? LongLaser() : Salvo();
                break;
            default:
                yield return CloseCombatAttack();
                break;
        }
    }

    // ---------- Útoky ----------

    IEnumerator Reposition(float seconds)
    {
        DebugState = "move";
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            Vector3 to = FlatToPlayer();
            FaceTowards(to, 90f * speedFactor);
            float d = to.magnitude;
            float want = Phase == 3 ? 4f : 14f;
            if (d > want + 1.5f) Move(to, moveSpeed * speedFactor);
            else if (d < want - 2f) Move(-to, moveSpeed * speedFactor);
            else Move(Vector3.Cross(to, Vector3.up), moveSpeed * 0.6f * speedFactor);   // krúži
            yield return null;
        }
    }

    Transform Muzzle(int i) => rig != null ? (i % 2 == 0 ? rig.muzzleLeft : rig.muzzleRight) : transform;

    IEnumerator Salvo()
    {
        DebugState = "salvo";
        float tele = 0.9f / speedFactor;
        float t = 0f;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        while (t < tele)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 140f);
            Flash(new Color(1f, 0.2f, 0.1f), Mathf.Lerp(0.3f, 1f, t / tele));
            // Tenké zameriavacie lúče ukazujú, kam to namieri
            Vfx.Beam(Muzzle(0).position, player.transform.position, new Color(1f, 0.2f, 0.2f, 0.4f), 0.05f, 0.04f);
            Vfx.Beam(Muzzle(1).position, player.transform.position, new Color(1f, 0.2f, 0.2f, 0.4f), 0.05f, 0.04f);
            yield return null;
        }
        for (int i = 0; i < 6; i++)
        {
            FireLaser(Muzzle(i).position, player.transform.position, salvoDamage, new Color(1f, 0.25f, 0.2f));
            yield return new WaitForSeconds(0.17f);
        }
        yield return Reposition(Random.Range(1.2f, 2f));
    }

    /// <summary>Laser: zasiahne prvú vec v ceste (kryt sa poškodí, hráč dostane zásah).</summary>
    public void FireLaser(Vector3 from, Vector3 to, float damage, Color color)
    {
        Vector3 dir = (to - from).normalized;
        int n = Physics.RaycastNonAlloc(from, dir, hits, 60f, ~0, QueryTriggerInteraction.Ignore);
        float best = 60f;
        RaycastHit bestHit = default;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<RenDrone>() != null) continue;
            if (h.distance < best) { best = h.distance; bestHit = h; found = true; }
        }
        Vector3 end = found ? bestHit.point : from + dir * 60f;
        Vfx.Beam(from, end, color, 0.14f, 0.13f);
        GameAudio.Play(GameAudio.Sfx.TowerShot, from);
        if (!found) return;

        ArenaCover cover = bestHit.collider.GetComponentInParent<ArenaCover>();
        if (cover != null) { cover.Hit(damage * 3f); return; }
        if (bestHit.collider.GetComponentInParent<PlayerStats>() != null) HurtPlayer(damage);
    }

    ArenaCover FirstCoverNearPlayer()
    {
        ArenaCover best = null;
        float bd = float.MaxValue;
        foreach (ArenaCover c in ArenaCover.All)
        {
            if (!c.Alive) continue;
            float d = (c.transform.position - player.transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    IEnumerator DestroyCover()
    {
        DebugState = "cover";
        ArenaCover target = FirstCoverNearPlayer();
        if (target == null) yield break;
        float t = 0f;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        while (t < 0.9f && target != null)
        {
            t += Time.deltaTime;
            FaceTowards(target.transform.position - transform.position, 160f);
            Flash(new Color(1f, 0.6f, 0.1f), 0.8f);
            Vfx.Beam(Muzzle(0).position, target.transform.position + Vector3.up, new Color(1f, 0.6f, 0.1f, 0.5f), 0.05f, 0.05f);
            yield return null;
        }
        for (int i = 0; i < 4 && target != null && target.Alive; i++)
        {
            FireLaser(Muzzle(i).position, target.transform.position + Vector3.up, 60f, new Color(1f, 0.6f, 0.1f));
            yield return new WaitForSeconds(0.2f);
        }
        if (target != null && target.Alive) target.Collapse();
        yield return Reposition(0.8f);
    }

    // Fáza 2: dlhá paľba, otvorené ventily
    IEnumerator LongLaser()
    {
        DebugState = "long-laser";
        yield return Telegraph(1.1f, new Color(1f, 0.15f, 0.05f));

        ventsOpen = true;
        ventHits = 0;
        SetZones("vent", true);
        SetVentGlow(true);
        GameHUD.Message("Ventily na chrbte sú otvorené! Obehni ho a udri zozadu.", 3f);

        float t = 0f, nextTick = 0f;
        const float duration = 4.2f;
        while (t < duration && ventHits < 2)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 32f);   // pomaly sa otáča: dá sa obehnúť
            Vector3 from = Muzzle(0).position;
            Vector3 dir = transform.forward;
            Vector3 end = from + dir * 40f;
            Vfx.Beam(from, end, new Color(1f, 0.2f, 0.1f, 0.9f), 0.06f, 0.35f);

            if (Time.time >= nextTick)
            {
                nextTick = Time.time + 0.2f;
                // Hráč je v lúči, ak je blízko priamky a nie je za krytom
                Vector3 toP = player.transform.position - from;
                float along = Vector3.Dot(toP, dir);
                if (along > 0f)
                {
                    Vector3 closest = from + dir * along;
                    if (Vector3.Distance(closest, player.transform.position) < 1.2f && !Blocked(from, player.transform.position))
                        HurtPlayer(laserDps * 0.2f);
                }
            }
            yield return null;
        }

        ventsOpen = false;
        SetZones("vent", false);
        SetVentGlow(false);

        if (ventHits >= 2)
        {
            // Po dvoch zásahoch sa otočí a odskočí
            GameHUD.Message("Ren sa otáča a odskakuje!", 2f);
            float turn = 0f;
            while (turn < 0.4f) { turn += Time.deltaTime; FaceTowards(FlatToPlayer(), 600f); yield return null; }
            Vector3 away = -FlatToPlayer().normalized;
            float leap = 0f;
            while (leap < 0.5f) { leap += Time.deltaTime; Move(away, 18f); yield return null; }
        }
        yield return Reposition(1.2f);
    }

    bool Blocked(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        int n = Physics.RaycastNonAlloc(from, dir.normalized, hits, dir.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (hits[i].collider.GetComponentInParent<ArenaCover>() != null) return true;
        return false;
    }

    // Fáza 3
    IEnumerator CloseCombatAttack()
    {
        Vector3 to = FlatToPlayer();
        if (to.magnitude > 6f)
        {
            // Najprv sa priblíži (rýchlejšie než predtým)
            float t = 0f;
            while (t < 1.2f && FlatToPlayer().magnitude > 4.5f)
            {
                t += Time.deltaTime;
                FaceTowards(FlatToPlayer(), 240f);
                Move(FlatToPlayer(), moveSpeed * 2.2f * speedFactor);
                yield return null;
            }
        }

        int pick = Random.Range(0, 3);
        if (pick == 0) yield return Punch();
        else if (pick == 1) yield return Stomp();
        else yield return Flamethrower();

        // Pauza po útoku: tu ho paralyzuje elektrický zásah
        yield return ExposedPause(1.7f);
    }

    IEnumerator ExposedPause(float seconds)
    {
        DebugState = "exposed";
        Exposed = true;
        float t = 0f;
        while (t < seconds && !IsStunned)
        {
            t += Time.deltaTime;
            Flash(new Color(1f, 0.95f, 0.3f), 0.5f + 0.5f * Mathf.Sin(t * 20f));   // iskrenie: signál, že je odkrytý
            FaceTowards(FlatToPlayer(), 60f);
            yield return null;
        }
        Exposed = false;
    }

    IEnumerator Punch()
    {
        DebugState = "punch";
        float t = 0f;
        while (t < 0.65f / speedFactor) { t += Time.deltaTime; FaceTowards(FlatToPlayer(), 200f); Flash(new Color(1f, 0.3f, 0.1f), 0.9f); yield return null; }
        Vector3 fwd = transform.forward;
        float lunge = 0f;
        while (lunge < 0.18f) { lunge += Time.deltaTime; Move(fwd, 16f); yield return null; }

        Vector3 to = FlatToPlayer();
        if (to.magnitude < 4.6f && Vector3.Angle(fwd, to) < 70f) HurtPlayer(punchDamage);
        Vfx.Disc(transform.position + fwd * 3f, 2.2f, new Color(1f, 0.6f, 0.2f, 0.6f), 0.25f);
        GameAudio.Play(GameAudio.Sfx.SwingHeavy, transform.position);
        yield return new WaitForSeconds(0.25f);
    }

    IEnumerator Stomp()
    {
        DebugState = "stomp";
        const float radius = 12f;
        float tele = 0.95f / speedFactor;
        float t = 0f;
        while (t < tele)
        {
            t += Time.deltaTime;
            Flash(new Color(1f, 0.5f, 0.1f), 0.9f);
            Vfx.Disc(transform.position + Vector3.up * 0.1f, radius * (t / tele), new Color(1f, 0.4f, 0.1f, 0.25f), 0.08f);
            yield return null;
        }

        // Rázová vlna: preskoč ju (skok); zasiahne len toho, kto stojí na zemi
        GameAudio.Play(GameAudio.Sfx.SwingHeavy, transform.position);
        float wave = 0f;
        bool hit = false;
        Vector3 origin = transform.position;
        while (wave < radius)
        {
            wave += 15f * Time.deltaTime;
            Vfx.Disc(origin + Vector3.up * 0.1f, wave, new Color(1f, 0.7f, 0.3f, 0.55f), 0.1f);
            Vector3 d = player.transform.position - origin;
            float footHeight = player.transform.position.y - origin.y;
            d.y = 0f;
            if (!hit && Mathf.Abs(d.magnitude - wave) < 1.1f && footHeight < 1.4f) { hit = true; HurtPlayer(stompDamage); }
            yield return null;
        }
        OpenWindow(2.5f, 1.5f);   // po ťažkom útoku je odkrytý
        GameHUD.WorldText(transform.position + Vector3.up * 5f, "ODKRYTÝ!", new Color(1f, 0.9f, 0.3f));
    }

    IEnumerator Flamethrower()
    {
        DebugState = "flame";
        yield return Telegraph(0.85f / speedFactor, new Color(1f, 0.55f, 0.1f));
        float t = 0f, tick = 0f;
        while (t < 2.1f)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 70f);
            Vector3 from = rig != null && rig.flame != null ? rig.flame.position : transform.position + Vector3.up * 2f;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 d = Quaternion.AngleAxis(i * 14f, Vector3.up) * transform.forward;
                Vfx.Beam(from, from + d * 8f, new Color(1f, 0.5f, 0.1f, 0.8f), 0.08f, 0.5f);
            }
            if (Time.time >= tick)
            {
                tick = Time.time + 0.25f;
                Vector3 to = player.transform.position - transform.position;
                to.y = 0f;
                if (to.magnitude < 8.5f && Vector3.Angle(transform.forward, to) < 30f) HurtPlayer(flameDps * 0.25f);
            }
            yield return null;
        }
    }

    // ---------- Finišer ----------

    protected override IEnumerator RunFinisher()
    {
        DebugState = "finisher";
        CloseWindow();
        ventsOpen = false;
        SetZones("vent", false);
        SetVentGlow(false);
        ClearDrones();
        SetShield(false);
        Exposed = false;

        PanelResult result = null;
        PanelPage[] pages =
        {
            new PanelPage(
                PanelSpec.Narrative("Mecha klesá na koleno. Zo švov šľahajú iskry.", PanelArt.Figure),
                PanelSpec.Narrative("Kokpit je odkrytý. Teraz alebo nikdy!", PanelArt.Eye),
                PanelSpec.Qte(KeyCode.F, 2.4f, 15f, "Prerazenie kokpitu: F v zelenom pásme!")),
            new PanelPage(PanelSpec.Narrative("", PanelArt.Fist, null, new Vector2(0.4f, 0.6f)), PanelSpec.Result()),
            new PanelPage(
                PanelSpec.Narrative("Ren: „Bola som pri tom, keď si zomrel. Nechala som ťa na smetisku. Mala som ťa dorobiť.“", PanelArt.Eye),
                PanelSpec.Narrative("Kokpit sa otvára. Ren dvíha zrak, nie zbraň.", PanelArt.Figure)),
            new PanelPage(
                PanelSpec.Choice("Zabiť ju", PanelArt.Blade),
                PanelSpec.Choice("Ušetriť ju", PanelArt.Figure))
        };

        bool done = false;
        if (!MangaPanels.Play(FightId, PanelContext.Boss, pages, r => { result = r; done = true; })) done = true;
        while (!done) yield return null;

        bool spare = result == null || result.LastChoice != 0;   // bez voľby = ušetrená
        yield return Resolve(spare);
    }

    IEnumerator Resolve(bool spare)
    {
        Vfx.Disc(transform.position + Vector3.up * 0.3f, 8f, spare ? new Color(0.5f, 0.8f, 1f, 0.5f) : new Color(1f, 0.5f, 0.1f, 0.7f), 0.9f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        yield return new WaitForSeconds(0.6f);

        if (spare)
        {
            Reputation.Add(FactionId.Civilians, 20f);
            Reputation.Add(FactionId.Rebels, 10f);
            GameHUD.Message("Ušetrila si Rena. Ľudia sa o tom dozvedia. Ren sa vráti.", 6f);
            DefeatCleanup("spared");
        }
        else
        {
            ArakaniFavor a = ArakaniFavor.Instance;
            if (a != null) a.favor = Mathf.Min(100f, a.favor + 15f);
            Reputation.Add(FactionId.Megacorp, -40f);
            Reputation.AddSuspicion(-10f);
            StreetChaos.Start(5);
            GameHUD.Message("Ren je mŕtva. Arakani je spokojný. Megakorporácia stráca velenie, v uliciach je chaos.", 7f);
            DefeatCleanup("killed");
        }

        // Mecha zmizne (zabitá: zostane vrak)
        if (spare) { foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false; }
        else ApplyWreck();
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        SetShield(false);
    }

    void ApplyWreck()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            b.SetColor("_Color", new Color(0.08f, 0.08f, 0.09f));
            b.SetColor("_EmissionColor", Color.black);
            r.SetPropertyBlock(b);
        }
    }

    void SetVentGlow(bool on)
    {
        if (rig == null || rig.ventRenderers == null) return;
        var b = new MaterialPropertyBlock();
        foreach (Renderer r in rig.ventRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(b);
            b.SetColor("_EmissionColor", on ? new Color(3f, 0.25f, 0.1f) : Color.black);
            r.SetPropertyBlock(b);
        }
    }
}
