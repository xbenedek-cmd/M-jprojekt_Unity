using System.Collections;
using UnityEngine;

/// <summary>
/// Lord Ignis (Klan Popola), elita na kostre BossCore: roztavené telo, ohnivé výbuchy, tlakové vlny a podlaha, ktorá sa mení na lávu
/// (bezpečný kruh sa zmenšuje s fázami). Imúnny voči ohňu; roztavený berie len časť poškodenia.
/// Slabina podľa Postavy.txt: voda a chlad. Cool() (vodný kanón, chladiaci granát, chladiaca stanica v aréne) mu stuhne lávu na krehký bazalt:
/// brnenie je preč, poškodenie je masívne a krátko je omráčený. Do hry zatiaľ nie je vodný kanón ani granát, preto aréna obsahuje chladiace stanice
/// (CoolantStation) a verejné IgnisBoss.TryCool(), ktoré sa dá neskôr zavolať z ľubovoľnej zbrane.
/// </summary>
public class IgnisBoss : BossCore
{
    public const string FightId = "boss_ignis";

    [Header("Ignis")]
    [SerializeField] private float moveSpeed = 3.4f;
    [SerializeField] private float moltenFactor = 0.35f;
    [SerializeField] private float basaltFactor = 2.5f;
    [SerializeField] private float basaltSeconds = 7f;
    [SerializeField] private float fireballDamage = 22f;
    [SerializeField] private float waveDamage = 20f;
    [SerializeField] private float clawDamage = 26f;
    [SerializeField] private float lavaDps = 12f;
    [SerializeField] private float[] safeRadiusByPhase = { 32f, 24f, 17f };
    [SerializeField] private Transform safeDisc;
    [SerializeField] private Renderer[] bodyRenderers;

    public bool Basalt => Time.time < basaltUntil;
    public float SafeRadius { get; private set; } = 40f;

    private float basaltUntil, nextCoolAt;
    private float speedFactor = 1f;
    private int attackCounter;
    private bool tintBasalt;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private MaterialPropertyBlock tintBlock;
    private static readonly Color MoltenBody = new Color(0.22f, 0.14f, 0.1f);
    private static readonly Color BasaltBody = new Color(0.55f, 0.6f, 0.68f);
    private static readonly Color LavaGlow = new Color(1f, 0.45f, 0.1f) * 1.6f;

    protected override void Awake()
    {
        base.Awake();
        bossId = "ignis";
        bossName = "Lord Ignis";
        Health.displayName = bossName;
        arenaRadius = 34f;
        tintBlock = new MaterialPropertyBlock();
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    /// <summary>Háčik pre budúce vodné kanóny a chladiace granáty: ochladí Ignisa, ak je bod zásahu dosť blízko.</summary>
    public static bool TryCool(Vector3 position, float radius)
    {
        IgnisBoss ig = Current as IgnisBoss;
        if (ig == null || !ig.InFight) return false;
        Vector3 d = ig.transform.position - position;
        d.y = 0f;
        if (d.magnitude > radius + 3f) return false;
        return ig.Cool();
    }

    public bool Cool()
    {
        if (!InFight || FinisherRunning || Basalt || Time.time < nextCoolAt) return false;
        basaltUntil = Time.time + basaltSeconds;
        nextCoolAt = basaltUntil + 5f;
        Stun(2.2f);
        GameHUD.Message("Chlad zmrazil Ignisovu lávu na krehký bazalt! Brnenie je preč.", 4f);
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 10f, new Color(0.6f, 0.85f, 1f, 0.6f), 0.7f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        return true;
    }

    // ---------- Začiatok a koniec ----------

    protected override void OnFightBegin()
    {
        MangaPanels.ForgetFight(FightId);
        ResetState();
        GameHUD.Message("Ignis je z lávy. Zmraz ho vodou: chladiace stanice v aréne. Podlaha sa mení na lávu.", 5f);
    }

    protected override void OnFightAborted() { ResetState(); }

    void ResetState()
    {
        transform.SetPositionAndRotation(startPosition, startRotation);
        speedFactor = 1f;
        basaltUntil = 0f;
        nextCoolAt = 0f;
        attackCounter = 0;
        Phase = 1;
        SafeRadius = 40f;
        CloseWindow();
        ApplyTint(false);
    }

    void Update()
    {
        if (!InFight && !FinisherRunning)
        {
            if (SafeRadius < 40f) { SafeRadius = Mathf.MoveTowards(SafeRadius, 40f, 8f * Time.deltaTime); UpdateDisc(); }
            return;
        }

        float target = FinisherRunning ? 40f : safeRadiusByPhase[Mathf.Clamp(Phase - 1, 0, safeRadiusByPhase.Length - 1)];
        SafeRadius = Mathf.MoveTowards(SafeRadius, target, 1.2f * Time.deltaTime);
        UpdateDisc();

        if (basaltUntil > 0f && tintBasalt != Basalt) ApplyTint(Basalt);
        if (player != null && !player.IsDead && !FinisherRunning)
        {
            Vector3 d = player.transform.position - ArenaCenter;
            d.y = 0f;
            if (d.magnitude > SafeRadius) HurtPlayer(lavaDps * Time.deltaTime);
        }
    }

    void UpdateDisc()
    {
        if (safeDisc == null) return;
        Vector3 s = safeDisc.localScale;
        safeDisc.localScale = new Vector3(SafeRadius * 2f, s.y, SafeRadius * 2f);
    }

    void ApplyTint(bool basalt)
    {
        tintBasalt = basalt;
        if (bodyRenderers != null)
            foreach (Renderer r in bodyRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(tintBlock);
                tintBlock.SetColor("_Color", basalt ? BasaltBody : MoltenBody);
                r.SetPropertyBlock(tintBlock);
            }
        if (rig != null && rig.ventRenderers != null)
            foreach (Renderer r in rig.ventRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(tintBlock);
                tintBlock.SetColor("_EmissionColor", basalt ? Color.black : LavaGlow);
                tintBlock.SetColor("_Color", basalt ? BasaltBody : new Color(1f, 0.4f, 0.08f));
                r.SetPropertyBlock(tintBlock);
            }
    }

    // ---------- Poškodenie ----------

    protected override float ModifyIncoming(float amount, GameObject attacker, DamageKind kind)
    {
        if (kind == DamageKind.Fire)
        {
            if (amount > 2f) GameHUD.WorldText(transform.position + Vector3.up * 8f, "IMÚNNY", new Color(1f, 0.5f, 0.1f));
            return 0f;
        }
        if (Basalt) return amount * basaltFactor;
        if (amount > 2f) GameHUD.WorldText(transform.position + Vector3.up * 8f, "LÁVA", new Color(1f, 0.5f, 0.1f));
        return amount * moltenFactor;
    }

    protected override void OnStunned()
    {
        GameHUD.WorldText(transform.position + Vector3.up * 8f, "BAZALT", new Color(0.7f, 0.85f, 1f));
    }

    // ---------- Fázy ----------

    protected override IEnumerator OnPhaseEnter(int phase)
    {
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 14f, new Color(1f, 0.4f, 0.1f, 0.5f), 0.8f);
        if (phase == 2) { speedFactor = 1.25f; GameHUD.Message("Fáza 2: Lávy pribúda, bezpečný priestor sa zmenšuje.", 4.5f); }
        else if (phase == 3) { speedFactor = 1.5f; GameHUD.Message("Fáza 3: Peklo. Ohnivé stĺpy a ešte menej miesta.", 4.5f); }
        yield return new WaitForSeconds(1.2f);
    }

    protected override IEnumerator NextAttack()
    {
        attackCounter++;
        float d = FlatToPlayer().magnitude;
        switch (Phase)
        {
            case 1:
                yield return attackCounter % 2 == 0 ? PressureWave() : Fireballs(1);
                break;
            case 2:
                if (d < 10f && attackCounter % 3 == 0) yield return ClawSweep();
                else yield return attackCounter % 2 == 0 ? PressureWave() : Fireballs(2);
                break;
            default:
                if (d < 10f && attackCounter % 2 == 0) yield return ClawSweep();
                else yield return attackCounter % 3 == 0 ? PressureWave() : FirePillars();
                break;
        }
    }

    // ---------- Útoky ----------

    IEnumerator Walk(float seconds)
    {
        DebugState = "walk";
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            Vector3 to = FlatToPlayer();
            FaceTowards(to, 120f * speedFactor);
            if (to.magnitude > 9f) Move(to, moveSpeed * speedFactor);
            yield return null;
        }
    }

    IEnumerator TelegraphFacing(float seconds, Color color)
    {
        float t = 0f;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        while (t < seconds)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 120f);
            Flash(color, Mathf.Lerp(0.3f, 1f, t / seconds) * (0.6f + 0.4f * Mathf.Sin(t * 24f)));
            yield return null;
        }
    }

    IEnumerator Fireballs(int count)
    {
        DebugState = "fireball";
        yield return TelegraphFacing(0.9f / speedFactor, new Color(1f, 0.5f, 0.1f));
        var targets = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 r = i == 0 ? Vector2.zero : Random.insideUnitCircle.normalized * 6f;
            targets[i] = player.transform.position + new Vector3(r.x, 0f, r.y);
        }
        float tele = 1.2f / speedFactor;
        float t = 0f;
        while (t < tele)
        {
            t += Time.deltaTime;
            for (int i = 0; i < count; i++) Vfx.Disc(targets[i] + Vector3.up * 0.08f, 10f, new Color(1f, 0.3f, 0.1f, 0.35f), 0.06f);
            yield return null;
        }
        for (int i = 0; i < count; i++) Explode(targets[i], 5f, fireballDamage);
        yield return Walk(Random.Range(1.2f, 1.8f));
    }

    void Explode(Vector3 at, float radius, float damage)
    {
        Vfx.Beam(at + Vector3.up * 28f, at, new Color(1f, 0.6f, 0.2f), 0.25f, 0.6f);
        Vfx.Disc(at + Vector3.up * 0.1f, radius * 2f, new Color(1f, 0.45f, 0.1f, 0.65f), 0.45f);
        GameAudio.Play(GameAudio.Sfx.Build, at);
        Vector3 d = player.transform.position - at;
        d.y = 0f;
        if (d.magnitude < radius) HurtPlayer(damage);
    }

    IEnumerator PressureWave()
    {
        DebugState = "wave";
        yield return TelegraphFacing(1.3f / speedFactor, new Color(1f, 0.7f, 0.2f));
        Vector3 c = transform.position;
        GameAudio.Play(GameAudio.Sfx.TowerShot, c);
        float r = 0f;
        bool hit = false;
        PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
        while (r < 28f)
        {
            r += 14f * speedFactor * Time.deltaTime;
            Vfx.Disc(c + Vector3.up * 0.1f, r * 2f, new Color(1f, 0.55f, 0.15f, 0.3f), 0.06f);
            if (!hit)
            {
                Vector3 d = player.transform.position - c;
                d.y = 0f;
                if (Mathf.Abs(d.magnitude - r) < 1.8f && (pm == null || pm.IsGrounded)) { hit = true; HurtPlayer(waveDamage); }
            }
            yield return null;
        }
        yield return Walk(Random.Range(0.8f, 1.2f));
    }

    IEnumerator ClawSweep()
    {
        DebugState = "claws";
        yield return TelegraphFacing(0.8f / speedFactor, new Color(1f, 0.35f, 0.1f));
        Vfx.Disc(transform.position + transform.forward * 4f + Vector3.up * 0.1f, 9f, new Color(1f, 0.4f, 0.1f, 0.5f), 0.3f);
        GameAudio.Play(GameAudio.Sfx.TowerShot, transform.position);
        Vector3 to = FlatToPlayer();
        if (to.magnitude < 9.5f && Vector3.Angle(transform.forward, to) < 70f) HurtPlayer(clawDamage);
        yield return new WaitForSeconds(0.4f);
        yield return Walk(Random.Range(1f, 1.6f));
    }

    IEnumerator FirePillars()
    {
        DebugState = "pillars";
        yield return TelegraphFacing(0.8f / speedFactor, new Color(1f, 0.5f, 0.1f));
        const int count = 6;
        var pts = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 r = i == 0 ? Vector2.zero : Random.insideUnitCircle * 9f;
            pts[i] = player.transform.position + new Vector3(r.x, 0f, r.y);
        }
        float tele = 1.1f / speedFactor;
        float t = 0f;
        while (t < tele)
        {
            t += Time.deltaTime;
            for (int i = 0; i < count; i++) Vfx.Disc(pts[i] + Vector3.up * 0.08f, 7f, new Color(1f, 0.3f, 0.1f, 0.35f), 0.06f);
            yield return null;
        }
        for (int i = 0; i < count; i++) Explode(pts[i], 3.5f, fireballDamage * 0.8f);
        yield return Walk(Random.Range(1f, 1.5f));
    }

    // ---------- Finišer ----------

    protected override IEnumerator RunFinisher()
    {
        DebugState = "finisher";
        CloseWindow();
        PanelPage[] pages =
        {
            new PanelPage(
                PanelSpec.Narrative("Bazalt praská. Ignis sa potkýna, lávou mu steká žiara.", PanelArt.Figure),
                PanelSpec.Narrative("Pod hruďou svieti jadro. Jeden úder.", PanelArt.Eye),
                PanelSpec.Qte(KeyCode.F, 2.4f, 15f, "Úder do jadra: F v zelenom pásme!")),
            new PanelPage(PanelSpec.Narrative("", PanelArt.Burst, null, new Vector2(0.4f, 0.6f)), PanelSpec.Result()),
            new PanelPage(
                PanelSpec.Narrative("Ignis: „Mesto malo horieť... Ty si chladný odpad.“", PanelArt.Eye),
                PanelSpec.Narrative("Plamene ustupujú. Mesto zostáva celé.", PanelArt.Skyline))
        };
        yield return PlayPanels(FightId, pages);

        Vfx.Disc(transform.position + Vector3.up * 0.3f, 12f, new Color(1f, 0.5f, 0.2f, 0.6f), 0.9f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        yield return new WaitForSeconds(0.6f);
        GameHUD.Message("Lord Ignis je porazený. Mesto je zachránené pred spálením.", 6f);
        DefeatCleanup("defeated");
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }
}
