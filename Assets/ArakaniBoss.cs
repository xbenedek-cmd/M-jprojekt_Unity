using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ArakaniElement { Shadow, Fire, Lightning, Blood }

/// <summary>
/// Arakani, finálny boss v Prapôvodnom Jadre. Päť fáz na kostre BossCore: Tieň, Oheň, Blesk, Krv a záverečná fáza podľa konca (ľudská cesta: zničiť ho,
/// démonská: zradiť ho a zaujať jeho miesto). Medzi fázami privolá echo elity súvisiacej s predošlým živlom (Kuro, Ignis, Raizen, Carmilla),
/// ale len ak tá elita ešte nebola porazená (BossRegistry). Imúnny voči všetkému okrem útoku v hybridnej (démonickej) forme s čepeľou namočenou
/// v Yuminom sére (kláves R, obmedzený počet fľaštičiek, okno platí pár sekúnd); obyčajný útok ukáže „Nezraniteľný“.
/// Démonský koniec: po zrade už sérum netreba, stačí útok v hybridnej forme, a Arakani útočí zmesou všetkých živlov.
/// Pred bojom a po ňom manga panely (QTE na dobitie, voľba konca).
/// </summary>
public class ArakaniBoss : BossCore
{
    public const string FightId = "boss_arakani";

    [Header("Arakani")]
    [SerializeField] private int serumVials = 7;
    [SerializeField] private float bladeSeconds = 10f;
    [SerializeField, Range(0f, 0.1f)] private float bladeMinFraction = 0.01f;
    [SerializeField, Range(0f, 0.1f)] private float bladeMaxFraction = 0.03f;
    [SerializeField] private float slamDamage = 22f;
    [SerializeField] private float blastDamage = 18f;
    [SerializeField] private float arcDamage = 16f;
    [SerializeField] private float drainDps = 12f;
    [SerializeField] private float floorFireDps = 15f;
    [SerializeField] private float revealSeconds = 8f;
    [SerializeField] private Transform[] pillars;
    [SerializeField] private Light[] arenaLights;

    public int SerumCharges { get; private set; }
    public bool BladeActive => Time.time < bladeUntil;
    public float BladeLeft => Mathf.Max(0f, bladeUntil - Time.time);
    public ArakaniElement Element => (ArakaniElement)Mathf.Clamp(Phase - 1, 0, 3);
    public bool Hidden { get; private set; }
    public bool Revealed => Time.time < revealedUntil;
    public bool FinalPhase => Phase >= 5;
    public bool Betrayed { get; private set; }
    public bool FloorBurning { get; private set; }
    public PlayerStats PlayerRef => player;
    public int AliveSummons { get { int n = 0; foreach (ArakaniSummon s in summons) if (s != null && s.Alive) n++; return n; } }

    private readonly List<ArakaniSummon> summons = new List<ArakaniSummon>();
    private readonly Vector3[] safeSpots = new Vector3[3];
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private Renderer[] bodyRenderers;
    private float[] lightBase;
    private float bladeUntil, revealedUntil, noHideUntil, hiddenSince;
    private float speedFactor = 1f;
    private int attackCounter;
    private bool introPlayed, draining;
    private float drainTaken;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private float nextFloorFx;
    // Elita podľa práve skončeného živlu: Tieň→Kuro, Oheň→Ignis, Blesk→Raizen, Krv→Carmilla
    private static readonly SummonKind[] Echoes = { SummonKind.Kuro, SummonKind.Ignis, SummonKind.Raizen, SummonKind.Carmilla };
    private static readonly Color[] ElementColor =
    {
        new Color(0.45f, 0.2f, 0.8f), new Color(1f, 0.4f, 0.1f), new Color(0.4f, 0.85f, 1f), new Color(0.85f, 0.05f, 0.15f)
    };

    protected override void Awake()
    {
        maxHealth = 3000f;
        phaseThresholds = new[] { 0.8f, 0.6f, 0.4f, 0.2f };
        finisherAt = 0.08f;
        base.Awake();
        bossId = "arakani";
        bossName = "Arakani";
        Health.displayName = bossName;
        arenaRadius = 28f;
        bodyRenderers = GetComponentsInChildren<Renderer>(true);
        lightBase = new float[arenaLights != null ? arenaLights.Length : 0];
        for (int i = 0; i < lightBase.Length; i++) if (arenaLights[i] != null) lightBase[i] = arenaLights[i].intensity;
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    // ---------- Začiatok a koniec ----------

    protected override void OnFightBegin()
    {
        MangaPanels.ForgetFight(FightId + "_intro");
        MangaPanels.ForgetFight(FightId + "_choice");
        MangaPanels.ForgetFight(FightId + "_end");
        ResetState();
        SerumCharges = serumVials;
        GameHUD.Message("Arakani je imúnny. Čepeľ namoč v Yuminom sére [R] a útoč v hybridnej forme.", 6f);
    }

    protected override void OnFightAborted() { ResetState(); }

    void ResetState()
    {
        transform.SetPositionAndRotation(startPosition, startRotation);
        speedFactor = 1f;
        attackCounter = 0;
        introPlayed = false;
        draining = false;
        Betrayed = false;
        FloorBurning = false;
        bladeUntil = revealedUntil = noHideUntil = 0f;
        Phase = 1;
        DissolveSummons();
        SetHidden(false);
        SetDark(false);
        Tint(ElementColor[0]);
        CloseWindow();
    }

    // ---------- Sérum a čepeľ ----------

    /// <summary>Namočí čepeľ do séra: na pár sekúnd sa útok v hybridnej forme dostane cez imunitu. Fľaštičiek je obmedzene.</summary>
    public bool DipBlade()
    {
        if (!InFight || FinisherRunning || BladeActive || SerumCharges <= 0) return false;
        SerumCharges--;
        bladeUntil = Time.time + bladeSeconds;
        string hint = player != null && player.IsDemon ? "Teraz udri." : "Útoč v hybridnej (démonickej) forme!";
        GameHUD.Message($"Čepeľ namočená v Yuminom sére ({SerumCharges} zostáva). {hint}", 4f);
        Vfx.Disc(player.transform.position + Vector3.up * 0.1f, 4f, new Color(0.5f, 1f, 0.6f, 0.6f), 0.5f);
        return true;
    }

    void Update()
    {
        if (!InFight || player == null || player.IsDead) return;

        if (!FinisherRunning && !MangaPanels.Active)
        {
            if (GameInput.GetKeyDown(KeyCode.R)) DipBlade();
            if (!Betrayed) GameHUD.Prompt(BladeActive ? $"Čepeľ so sérom: {BladeLeft:0} s" : SerumCharges > 0 ? $"[R] Namočiť čepeľ do séra ({SerumCharges})" : "Sérum došlo");
        }

        // Krvavé videnie odhalí skrytého Arakaniho
        if (Hidden && PerceptionToggle.IsDemonVision && FlatToPlayer().magnitude < 45f) Reveal("Krvavé videnie odhalilo Arakaniho!");

        // Skrytý, ale nikto ho neodhalil: po čase sa sám vynorí, aby sa boj nezasekol
        if (Hidden && Time.time - hiddenSince > 16f) Reveal("Arakani sa vynoril z tmy.");

        if (FloorBurning && !FinisherRunning)
        {
            if (Time.time >= nextFloorFx)
            {
                nextFloorFx = Time.time + 0.3f;
                Vfx.Disc(ArenaCenter + Vector3.up * 0.06f, 76f, new Color(1f, 0.35f, 0.05f, 0.35f), 0.35f);
                for (int i = 0; i < safeSpots.Length; i++) Vfx.Disc(safeSpots[i] + Vector3.up * 0.1f, 9f, new Color(0.05f, 0.1f, 0.25f, 0.8f), 0.35f);
            }
            if (!InSafeSpot(player.transform.position)) HurtPlayer(floorFireDps * Time.deltaTime);
        }
    }

    bool InSafeSpot(Vector3 p)
    {
        for (int i = 0; i < safeSpots.Length; i++)
        {
            Vector3 d = p - safeSpots[i];
            d.y = 0f;
            if (d.magnitude < 4.5f) return true;
        }
        return false;
    }

    // ---------- Viditeľnosť, tma, farba ----------

    void SetHidden(bool hidden)
    {
        Hidden = hidden;
        if (hidden) hiddenSince = Time.time;
        foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = !hidden;
    }

    void SetDark(bool dark)
    {
        for (int i = 0; i < lightBase.Length; i++) if (arenaLights[i] != null) arenaLights[i].intensity = dark ? lightBase[i] * 0.08f : lightBase[i];
    }

    void Tint(Color c)
    {
        if (rig == null || rig.ventRenderers == null) return;
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in rig.ventRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_Color", c);
            block.SetColor("_EmissionColor", c * 1.8f);
            r.SetPropertyBlock(block);
        }
    }

    /// <summary>Tieňová fáza: odhalenie krvavým videním alebo plošným ohňom. Odhalený je zraniteľný pár sekúnd a znova sa neskryje.</summary>
    public bool Reveal(string why)
    {
        if (!Hidden) return false;
        SetHidden(false);
        SetDark(false);
        revealedUntil = Time.time + revealSeconds;
        noHideUntil = revealedUntil + 4f;
        GameHUD.Message(why, 3.5f);
        GameHUD.WorldText(transform.position + Vector3.up * 28f, "ODHALENÝ", new Color(1f, 0.85f, 0.4f));
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 18f, new Color(1f, 0.6f, 0.3f, 0.5f), 0.7f);
        return true;
    }

    protected override void OnAbility(AbilityKind kind, Vector3 position)
    {
        if (kind != AbilityKind.FireBurst || !Hidden) return;
        Vector3 d = transform.position - position;
        d.y = 0f;
        if (d.magnitude <= 30f) Reveal("Plošný ohnivý útok odhalil Arakaniho!");
    }

    // ---------- Poškodenie ----------

    protected override float ModifyIncoming(float amount, GameObject attacker, DamageKind kind)
    {
        if (Hidden)
        {
            if (kind == DamageKind.Fire && amount > 2f) Reveal("Oheň odhalil Arakaniho!");
            else GameHUD.WorldText(transform.position + Vector3.up * 28f, "SKRYTÝ", ElementColor[0]);
            return 0f;
        }

        PlayerStats p = attacker != null ? attacker.GetComponent<PlayerStats>() : null;
        bool hybrid = p != null && p.IsDemon;
        bool open = Betrayed ? hybrid : hybrid && BladeActive;
        if (!open)
        {
            GameHUD.WorldText(transform.position + Vector3.up * 28f, "Nezraniteľný", new Color(0.8f, 0.8f, 0.9f));
            return 0f;
        }

        float max = Health.maxHealth;
        amount = Betrayed ? Mathf.Clamp(amount * 2f, 0.004f * max, 0.012f * max) : Mathf.Clamp(amount * 4f, bladeMinFraction * max, bladeMaxFraction * max);
        if (draining) drainTaken += amount;
        return amount;
    }

    // ---------- Fázy ----------

    protected override IEnumerator OnPhaseEnter(int phase)
    {
        // Koniec predošlého živlu: upratať a privolať jeho elitu (ak ešte nepadla)
        FloorBurning = false;
        draining = false;
        SetHidden(false);
        SetDark(false);
        CloseWindow();
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 24f, ElementColor[Mathf.Clamp(phase - 1, 0, 3)] * 0.9f, 0.9f);

        yield return Intermission(Echoes[Mathf.Clamp(phase - 2, 0, 3)]);

        if (phase <= 4)
        {
            Tint(ElementColor[phase - 1]);
            speedFactor = 1f + 0.12f * (phase - 1);
            string[] names = { "", "Oheň: plošné výbuchy a horiaca podlaha, bezpečné sú len tmavé miesta.", "Blesk: reťazové blesky medzi stĺpmi, drž sa pri zemi alebo za kameňom.", "Krv: vysáva ti krv a lieči sa, preruš ho." };
            GameHUD.Message("Fáza " + phase + " - " + names[phase - 1], 5f);
        }
        else
        {
            yield return FinalChoice();
        }
        yield return new WaitForSeconds(1f);
    }

    IEnumerator Intermission(SummonKind kind)
    {
        string id = ArakaniSummon.IdOf(kind);
        if (BossRegistry.IsDefeated(id))
        {
            GameHUD.Message(ArakaniSummon.NameOf(kind) + " už padla/padol. Arakani nemá koho privolať.", 4f);
            yield return new WaitForSeconds(1.2f);
            yield break;
        }

        GameHUD.Message("Arakani privoláva elitu: " + ArakaniSummon.NameOf(kind) + ". Zničte echo.", 4.5f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        Vector3 pos = ArenaCenter + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * 12f;
        pos.y = ArenaCenter.y;
        summons.RemoveAll(s => s == null);
        summons.Add(ArakaniSummon.Spawn(kind, pos, this));
        yield return new WaitForSeconds(0.5f);
        while (AliveSummons > 0 && InFight) yield return null;
    }

    void DissolveSummons()
    {
        foreach (ArakaniSummon s in summons) if (s != null) s.Dissolve();
    }

    /// <summary>Voľba konca: zničiť Arakaniho (ľudská cesta) alebo ho zradiť a zaujať jeho miesto (démonská).</summary>
    IEnumerator FinalChoice()
    {
        PanelPage[] pages =
        {
            new PanelPage(
                PanelSpec.Narrative("Posledný živel zhasína. Arakani sa vzpriamuje, jadro pod ním pulzuje.", PanelArt.Figure),
                PanelSpec.Narrative("Hlas v tvojej hlave je tichší. Môžeš ho zničiť. Alebo mu vziať miesto.", PanelArt.Eye)),
            new PanelPage(
                PanelSpec.Choice("Zničiť ho (ľudská cesta)", PanelArt.Blade),
                PanelSpec.Choice("Zradiť ho a zaujať jeho miesto (démonská cesta)", PanelArt.Fist))
        };
        yield return PlayPanels(FightId + "_choice", pages);
        Betrayed = LastPanelResult != null && LastPanelResult.LastChoice == 1;
        speedFactor = Betrayed ? 1.6f : 1.4f;
        if (Betrayed) GameHUD.Message("Zradil si ho. Arakani ťa napáda ako zradcu: už nepotrebuje sérum, stačí hybridný útok.", 6f);
        else GameHUD.Message("Posledná fáza: Arakani sa bráni všetkými živlami. Sérum, kým ho máš.", 6f);
    }

    protected override IEnumerator NextAttack()
    {
        if (!introPlayed)
        {
            introPlayed = true;
            yield return PlayIntro();
        }
        attackCounter++;
        yield return Idle(0.4f);

        if (FinalPhase)
        {
            int k = attackCounter % 4;
            yield return k == 0 ? ShadowSlam() : k == 1 ? FireBlast() : k == 2 ? LightningArcs() : (Betrayed ? FloorFire() : BloodDrain());
            if (Betrayed) yield return LightningArcs();
            yield break;
        }

        switch (Element)
        {
            case ArakaniElement.Shadow:
                if (!Hidden && Time.time >= noHideUntil) yield return Vanish();
                else yield return attackCounter % 3 == 0 ? ShadowFan() : ShadowSlam();
                break;
            case ArakaniElement.Fire:
                yield return attackCounter % 3 == 0 ? FloorFire() : FireBlast();
                break;
            case ArakaniElement.Lightning:
                yield return LightningArcs();
                break;
            default:
                yield return attackCounter % 2 == 0 ? BloodDrain() : ShadowSlam();
                break;
        }
    }

    IEnumerator PlayIntro()
    {
        PanelPage[] pages =
        {
            new PanelPage(
                PanelSpec.Narrative("Prapôvodné Jadro. Pod mestom, nad priepasťou, z ktorej stúpa dym.", PanelArt.Skyline),
                PanelSpec.Narrative("Arakani stojí v strede. Jeho hlas poznáš celý čas, teraz má telo.", PanelArt.Eye)),
            new PanelPage(
                PanelSpec.Narrative("Arakani: „Priniesol si mi sám seba. Ako vždy.“", PanelArt.Figure),
                PanelSpec.Narrative("Sérum od Yumi je tvoja jediná šanca. Fľaštičiek je málo.", PanelArt.Blade))
        };
        yield return PlayPanels(FightId + "_intro", pages);
    }

    // ---------- Útoky ----------

    IEnumerator Idle(float seconds)
    {
        DebugState = "idle";
        float t = 0f;
        while (t < seconds / speedFactor)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 60f);
            yield return null;
        }
    }

    IEnumerator Vanish()
    {
        DebugState = "vanish";
        Vfx.Disc(transform.position + Vector3.up * 0.1f, 20f, new Color(0.1f, 0.02f, 0.2f, 0.8f), 0.8f);
        SetHidden(true);
        SetDark(true);
        GameHUD.Message("Arakani zhasol svetlá a zmizol. Odhalí ho krvavé videnie alebo plošný ohnivý útok.", 5f);
        yield return new WaitForSeconds(0.6f);
        for (int i = 0; i < 3 && Hidden; i++) yield return ShadowSlam();
    }

    IEnumerator ShadowSlam()
    {
        DebugState = "slam";
        Vector3 target = player.transform.position;
        float tele = 0.9f / speedFactor;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        for (float t = 0f; t < tele; t += Time.deltaTime)
        {
            if (!Hidden) Flash(ElementColor[0], Mathf.Lerp(0.3f, 1f, t / tele));
            if (t < tele * 0.6f) target = player.transform.position;
            Vfx.Disc(new Vector3(target.x, ArenaCenter.y + 0.06f, target.z), 9f, new Color(0.5f, 0.2f, 0.9f, Hidden ? 0.3f : 0.45f), 0.06f);
            yield return null;
        }
        Vfx.Beam(transform.position + Vector3.up * 18f, target, new Color(0.7f, 0.4f, 1f), 0.25f, 0.8f);
        Vfx.Disc(new Vector3(target.x, ArenaCenter.y + 0.1f, target.z), 9f, new Color(0.4f, 0.1f, 0.7f, 0.7f), 0.4f);
        GameAudio.Play(GameAudio.Sfx.Build, target);
        Vector3 d = player.transform.position - target;
        d.y = 0f;
        if (d.magnitude < 4.5f) HurtPlayer(slamDamage);
        yield return new WaitForSeconds(0.4f / speedFactor);
    }

    IEnumerator ShadowFan()
    {
        DebugState = "fan";
        Vector3 origin = transform.position + Vector3.up * 14f;
        float tele = 1f / speedFactor;
        for (float t = 0f; t < tele; t += Time.deltaTime)
        {
            FaceTowards(FlatToPlayer(), 90f);
            Vector3 dir = FlatToPlayer().normalized;
            for (int i = -2; i <= 2; i++) Vfx.Beam(origin, origin + Quaternion.Euler(0f, i * 18f, 0f) * dir * 40f - Vector3.up * 13f, new Color(0.5f, 0.2f, 0.9f, 0.3f), 0.05f, 0.05f);
            yield return null;
        }
        Vector3 aim = FlatToPlayer().normalized;
        for (int i = -2; i <= 2; i++)
        {
            Vector3 end = origin + Quaternion.Euler(0f, i * 18f, 0f) * aim * 40f - Vector3.up * 13f;
            Vfx.Beam(origin, end, new Color(0.8f, 0.6f, 1f), 0.2f, 0.3f);
            Vector3 a = new Vector3(origin.x, 0f, origin.z), b = new Vector3(end.x, 0f, end.z), p = player.transform.position;
            p.y = 0f;
            Vector3 ab = b - a;
            float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
            if (Vector3.Distance(p, a + ab * k) < 1.6f) { HurtPlayer(slamDamage * 0.8f); break; }
        }
        GameAudio.Play(GameAudio.Sfx.TowerShot, transform.position);
        yield return new WaitForSeconds(0.5f / speedFactor);
    }

    IEnumerator FireBlast()
    {
        DebugState = "blast";
        const int count = 4;
        var pts = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 r = i == 0 ? Vector2.zero : Random.insideUnitCircle * 10f;
            pts[i] = player.transform.position + new Vector3(r.x, 0f, r.y);
            pts[i].y = ArenaCenter.y;
        }
        float tele = 1.2f / speedFactor;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        for (float t = 0f; t < tele; t += Time.deltaTime)
        {
            Flash(ElementColor[1], 0.6f);
            for (int i = 0; i < count; i++) Vfx.Disc(pts[i] + Vector3.up * 0.06f, 10f, new Color(1f, 0.3f, 0.1f, 0.35f), 0.06f);
            yield return null;
        }
        for (int i = 0; i < count; i++)
        {
            Vfx.Beam(pts[i] + Vector3.up * 30f, pts[i], new Color(1f, 0.6f, 0.2f), 0.25f, 0.7f);
            Vfx.Disc(pts[i] + Vector3.up * 0.1f, 10f, new Color(1f, 0.45f, 0.1f, 0.65f), 0.45f);
            Vector3 d = player.transform.position - pts[i];
            d.y = 0f;
            if (d.magnitude < 5f) HurtPlayer(blastDamage);
        }
        GameAudio.Play(GameAudio.Sfx.Build, transform.position);
        yield return new WaitForSeconds(0.5f / speedFactor);
    }

    IEnumerator FloorFire()
    {
        DebugState = "floor";
        for (int i = 0; i < safeSpots.Length; i++)
        {
            Vector2 r = Random.insideUnitCircle * 22f;
            safeSpots[i] = ArenaCenter + new Vector3(r.x, 0.1f, r.y);
        }
        GameHUD.Message("Podlaha horí. Bezpečné sú len tmavé miesta!", 3f);
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        float warn = 2f / speedFactor;
        for (float t = 0f; t < warn; t += Time.deltaTime)
        {
            Flash(ElementColor[1], 0.7f);
            for (int i = 0; i < safeSpots.Length; i++) Vfx.Disc(safeSpots[i] + Vector3.up * 0.1f, 9f, new Color(0.05f, 0.1f, 0.3f, 0.8f), 0.06f);
            yield return null;
        }
        FloorBurning = true;
        yield return new WaitForSeconds(4.5f);
        FloorBurning = false;
        yield return new WaitForSeconds(0.5f);
    }

    Vector3 PillarTop(int i) => pillars[i].position + Vector3.up * 8f;

    IEnumerator LightningArcs()
    {
        DebugState = "arcs";
        if (pillars == null || pillars.Length == 0) yield break;
        const int count = 3;
        var idx = new int[count];
        for (int i = 0; i < count; i++) idx[i] = Random.Range(0, pillars.Length);
        float tele = 1f / speedFactor;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        for (float t = 0f; t < tele; t += Time.deltaTime)
        {
            Flash(ElementColor[2], Mathf.Lerp(0.3f, 1f, t / tele));
            for (int i = 0; i < count; i++)
                Vfx.Beam(PillarTop(idx[i]), player.transform.position + Vector3.up * 1.1f, new Color(0.4f, 0.85f, 1f, 0.3f), 0.05f, 0.05f);
            yield return null;
        }
        for (int i = 0; i < count; i++)
        {
            Vector3 from = PillarTop(idx[i]);
            Vector3 to = player.transform.position + Vector3.up * 1.1f;
            if (i > 0) CombatFx.Lightning(PillarTop(idx[i - 1]), from, new Color(0.7f, 0.95f, 1f), 0.2f, 0.12f);   // reťaz medzi stĺpmi
            ArenaCover blocker = FirstCoverOnLine(from, to, pillars[idx[i]]);
            if (blocker != null)
            {
                CombatFx.Lightning(from, blocker.transform.position + Vector3.up, new Color(0.7f, 0.95f, 1f), 0.2f, 0.15f);
                blocker.Hit(60f);   // kameň blesk pohltí
                continue;
            }
            CombatFx.Lightning(from, to, new Color(0.8f, 0.97f, 1f), 0.2f, 0.18f);
            GameAudio.Play(GameAudio.Sfx.TowerShot, from);
            PlayerMovement pm = FindAnyObjectByType<PlayerMovement>();
            HurtPlayer(pm != null && !pm.IsGrounded ? arcDamage * 1.6f : arcDamage);   // vo vzduchu to bolí viac
            yield return new WaitForSeconds(0.12f);
        }
        yield return new WaitForSeconds(0.6f / speedFactor);
    }

    ArenaCover FirstCoverOnLine(Vector3 from, Vector3 to, Transform ignoreRoot)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        int n = Physics.RaycastNonAlloc(from, dir / dist, hits, dist, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        ArenaCover found = null;
        for (int i = 0; i < n; i++)
        {
            Collider c = hits[i].collider;
            if (c.transform.IsChildOf(transform) || c.transform.IsChildOf(ignoreRoot) || c.GetComponentInParent<PlayerStats>() != null) continue;
            ArenaCover cover = c.GetComponentInParent<ArenaCover>();
            if (cover != null && hits[i].distance < best) { best = hits[i].distance; found = cover; }
        }
        return found;
    }

    IEnumerator BloodDrain()
    {
        DebugState = "drain";
        draining = true;
        drainTaken = 0f;
        float tele = 1f / speedFactor;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        Vector3 chest = transform.position + Vector3.up * 14f;
        for (float t = 0f; t < tele; t += Time.deltaTime)
        {
            FaceTowards(FlatToPlayer(), 90f);
            Flash(ElementColor[3], Mathf.Lerp(0.3f, 1f, t / tele));
            Vfx.Beam(chest, player.transform.position + Vector3.up * 1.1f, new Color(0.8f, 0.05f, 0.15f, 0.25f), 0.05f, 0.05f);
            yield return null;
        }

        float healed = 0f, healCap = 0.05f * Health.maxHealth;
        for (float t = 0f; t < 4f && drainTaken < 0.02f * Health.maxHealth; t += Time.deltaTime)
        {
            Vector3 target = player.transform.position + Vector3.up * 1.1f;
            ArenaCover blocker = FirstCoverOnLine(chest, target, transform);
            if (blocker != null)
            {
                Vfx.Beam(chest, blocker.transform.position + Vector3.up, new Color(0.8f, 0.1f, 0.2f, 0.6f), 0.08f, 0.1f);
                blocker.Hit(50f * Time.deltaTime);
                if (t > 1.5f) break;   // kryt lúč nevydrží donekonečna, ale vysávanie sa zdrží
            }
            else
            {
                Vfx.Beam(chest, target, ElementColor[3], 0.08f, 0.18f);
                float dmg = drainDps * speedFactor * Time.deltaTime;
                HurtPlayer(dmg);
                float heal = Mathf.Min(dmg * 0.4f, healCap - healed);
                if (heal > 0f) { Health.Heal(heal); healed += heal; }
            }
            yield return null;
        }
        bool interrupted = drainTaken >= 0.02f * Health.maxHealth;
        draining = false;
        if (interrupted)
        {
            GameHUD.WorldText(transform.position + Vector3.up * 28f, "PRERUŠENÝ", new Color(1f, 0.85f, 0.4f));
            Stun(2f);
        }
        yield return new WaitForSeconds(0.5f / speedFactor);
    }

    // ---------- Finišer ----------

    protected override IEnumerator RunFinisher()
    {
        DebugState = "finisher";
        CloseWindow();
        FloorBurning = false;
        draining = false;
        SetHidden(false);
        SetDark(false);
        DissolveSummons();

        bool demonEnding = Betrayed;
        PanelPage[] pages = demonEnding
            ? new[]
            {
                new PanelPage(
                    PanelSpec.Narrative("Arakani klesá. Jadro sa trhá a jeho moc tečie k tebe.", PanelArt.Figure),
                    PanelSpec.Narrative("Zradca prijíma trón. Teraz ho vezmi celý.", PanelArt.Eye),
                    PanelSpec.Qte(KeyCode.F, 2.4f, 15f, "Pohltenie jadra: F v zelenom pásme!")),
                new PanelPage(PanelSpec.Narrative("", PanelArt.Burst, null, new Vector2(0.4f, 0.6f)), PanelSpec.Result()),
                new PanelPage(
                    PanelSpec.Narrative("Arakani: „Zradil si ma... presne ako som ťa učil.“", PanelArt.Eye),
                    PanelSpec.Narrative("Hlas v hlave nezmizol. Teraz je to tvoj vlastný. Mesto patrí tebe.", PanelArt.Skyline))
            }
            : new[]
            {
                new PanelPage(
                    PanelSpec.Narrative("Arakani padá na kolená. Jadro pod ním praská.", PanelArt.Figure),
                    PanelSpec.Narrative("Čepeľ so sérom, posledný úder do srdca.", PanelArt.Eye),
                    PanelSpec.Qte(KeyCode.F, 2.4f, 15f, "Zničenie jadra: F v zelenom pásme!")),
                new PanelPage(PanelSpec.Narrative("", PanelArt.Blade, null, new Vector2(0.4f, 0.6f)), PanelSpec.Result()),
                new PanelPage(
                    PanelSpec.Narrative("Arakani: „Bol som len hlas...“", PanelArt.Eye),
                    PanelSpec.Narrative("Ticho. Prvýkrát po dlhom čase v tvojej hlave nie je nikto iný.", PanelArt.Skyline))
            };
        yield return PlayPanels(FightId + "_end", pages);

        Vfx.Disc(transform.position + Vector3.up * 0.3f, 30f, demonEnding ? new Color(0.8f, 0.1f, 0.3f, 0.6f) : new Color(0.7f, 0.9f, 1f, 0.6f), 1.2f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        yield return new WaitForSeconds(0.8f);

        ArakaniFavor fav = ArakaniFavor.Instance;
        if (demonEnding)
        {
            ArakaniFavor.AddFavor(100f);
            GameHUD.Message("Zradil si Arakaniho a zaujal jeho miesto. Démonská cesta.", 7f);
            DefeatCleanup("demon");
        }
        else
        {
            if (fav != null) fav.voiceActive = false;   // hlas stíchol navždy
            GameHUD.Message("Arakani je zničený. Hlas v tvojej hlave stíchol. Ľudská cesta.", 7f);
            DefeatCleanup("human");
        }
        foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }
}
