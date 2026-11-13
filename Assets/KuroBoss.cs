using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lady Kuro (Klan Tieňa), elita na kostre BossCore: tieňové ilúzie (klony) a dočasná neviditeľnosť, útoky bleskovými rezmi zo strán a zozadu.
/// Neviditeľná je takmer nezraniteľná (poškodenie ×0,1). Slabina podľa Postavy.txt: oheň alebo silné svetlo. Zásah ohňom, ohnivá schopnosť
/// Klanu Popola alebo svetlomet (Reveal) rozpustí všetky ilúzie, odhalí ju, na chvíľu omráči a zabráni jej znova zmiznúť.
/// Bez ohňa sa dá vyhrať aj tak: ilúzie sa rozpúšťajú zásahom a neviditeľnosť trvá len niekoľko sekúnd.
/// Fázy: 100–70 % dve ilúzie a reznice, 70–40 % tri ilúzie a rýchlejšie, 40–10 % štyri ilúzie a séria rezov. Pod 10 % QTE panel.
/// </summary>
public class KuroBoss : BossCore
{
    public const string FightId = "boss_kuro";

    [Header("Kuro")]
    [SerializeField] private float slashDamage = 18f;
    [SerializeField] private float fanDamage = 12f;
    [SerializeField] private float illusionDamage = 8f;
    [SerializeField] private float invisibleFactor = 0.1f;
    [SerializeField] private float revealSeconds = 6f;
    [SerializeField] private float noVanishAfterReveal = 9f;
    [SerializeField] private GameObject illusionTemplate;

    public PlayerStats PlayerRef => player;
    public bool Invisible { get; private set; }
    public int AliveIllusions { get { int n = 0; foreach (KuroIllusion i in pool) if (i != null && i.Alive) n++; return n; } }

    private readonly List<KuroIllusion> pool = new List<KuroIllusion>();
    private Renderer[] bodyRenderers;
    private float noVanishUntil;
    private float speedFactor = 1f;
    private int attackCounter;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private static readonly Color Violet = new Color(0.7f, 0.25f, 1f);

    protected override void Awake()
    {
        base.Awake();
        bossId = "kuro";
        bossName = "Lady Kuro";
        Health.displayName = bossName;
        arenaRadius = 34f;
        bodyRenderers = GetComponentsInChildren<Renderer>(true);
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    void OnDestroy()
    {
        foreach (KuroIllusion i in pool) if (i != null) Destroy(i.gameObject);
    }

    // ---------- Začiatok a koniec ----------

    protected override void OnFightBegin()
    {
        MangaPanels.ForgetFight(FightId);
        ResetState();
        GameHUD.Message("Kuro sa skrýva v tieňoch. Oheň alebo svetlo (svetlomety) rozpustia ilúzie a odhalia ju.", 5f);
    }

    protected override void OnFightAborted() { ResetState(); }

    void ResetState()
    {
        transform.SetPositionAndRotation(startPosition, startRotation);
        speedFactor = 1f;
        attackCounter = 0;
        noVanishUntil = 0f;
        Phase = 1;
        DissolveAll();
        SetVisible(true);
        CloseWindow();
    }

    protected override void OnAttackInterrupted()
    {
        // Omráčenie alebo odhalenie preruší útok: nesmie zostať neviditeľná v zlom stave
        if (Invisible && !FinisherRunning) SetVisible(true);
    }

    // ---------- Neviditeľnosť, ilúzie, odhalenie ----------

    void SetVisible(bool visible)
    {
        Invisible = !visible;
        foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = visible;
    }

    void DissolveAll()
    {
        foreach (KuroIllusion i in pool) if (i != null) i.Dissolve();
    }

    KuroIllusion GetIllusion()
    {
        foreach (KuroIllusion i in pool) if (i != null && !i.Alive) return i;
        GameObject template = illusionTemplate != null ? illusionTemplate : gameObject;
        GameObject go = Instantiate(template, transform.parent);
        go.name = "KuroIlúzia";
        // Klon nesmie byť druhý boss: zo šablóny nechá len vzhľad
        foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is Damageable)) Destroy(mb);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = true;   // šablóna môže byť práve neviditeľná
        BossVisuals.Reanimate(go);   // model démona prišiel o animátor spolu s ostatnými skriptmi
        var ill = go.AddComponent<KuroIllusion>();
        pool.Add(ill);
        go.SetActive(false);
        return ill;
    }

    /// <summary>Svetlo, oheň alebo ohnivá schopnosť: rozpustí ilúzie, odhalí Kuro a dočasne jej zabráni zmiznúť. Vráti false, ak nebolo čo odhaliť.</summary>
    public bool Reveal(string why)
    {
        if (!InFight || FinisherRunning) return false;
        if (!Invisible && AliveIllusions == 0) return false;
        DissolveAll();
        SetVisible(true);
        noVanishUntil = Time.time + noVanishAfterReveal;
        OpenWindow(revealSeconds, 1.6f);
        Stun(1.4f);
        GameHUD.Message(why, 3.5f);
        GameHUD.WorldText(transform.position + Vector3.up * 3.5f, "ODHALENÁ", new Color(1f, 0.85f, 0.4f));
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 8f, new Color(1f, 0.8f, 0.4f, 0.5f), 0.6f);
        return true;
    }

    /// <summary>Háčik pre budúce svetelné zbrane (ohňostroj, svetlica): odhalí Kuro, ak je bod dosť blízko.</summary>
    public static bool TryLight(Vector3 position, float radius)
    {
        KuroBoss k = Current as KuroBoss;
        if (k == null || !k.InFight) return false;
        Vector3 d = k.transform.position - position;
        d.y = 0f;
        return d.magnitude <= radius + 8f && k.Reveal("Silné svetlo rozpustilo ilúzie!");
    }

    protected override void OnAbility(AbilityKind kind, Vector3 position)
    {
        if (kind != AbilityKind.FireBurst) return;
        Vector3 d = transform.position - position;
        d.y = 0f;
        if (d.magnitude <= 14f) Reveal("Plamene rozpustili ilúzie a odhalili Kuro!");
    }

    protected override float ModifyIncoming(float amount, GameObject attacker, DamageKind kind)
    {
        if (kind == DamageKind.Fire)
        {
            if (amount > 2f) Reveal("Oheň rozpustil tiene!");
            return amount * 1.3f;
        }
        if (Invisible)
        {
            if (amount > 2f) GameHUD.WorldText(transform.position + Vector3.up * 3.5f, "NEVIDITEĽNÁ", Violet);
            return amount * invisibleFactor;
        }
        return amount;
    }

    // ---------- Fázy ----------

    protected override IEnumerator OnPhaseEnter(int phase)
    {
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 12f, new Color(0.5f, 0.2f, 0.8f, 0.5f), 0.8f);
        if (phase == 2) { speedFactor = 1.25f; GameHUD.Message("Fáza 2: Viac ilúzií, rýchlejšie reznice.", 4.5f); }
        else if (phase == 3) { speedFactor = 1.5f; GameHUD.Message("Fáza 3: Séria rezov zo všetkých strán.", 4.5f); }
        yield return new WaitForSeconds(1f);
    }

    int IllusionCount => Phase == 1 ? 2 : Phase == 2 ? 3 : 4;

    protected override IEnumerator NextAttack()
    {
        attackCounter++;
        if (!Invisible && Time.time >= noVanishUntil && attackCounter % 3 == 0) { yield return VanishAssault(); yield break; }
        if (Phase == 3 && attackCounter % 2 == 0) { yield return SlashSeries(); yield break; }
        yield return attackCounter % 2 == 0 ? BladeFan() : Reposition(1f);
        if (Phase >= 2) yield return Slash(0.7f);
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
            FaceTowards(to, 360f);
            float d = to.magnitude;
            Vector3 side = Vector3.Cross(to, Vector3.up).normalized;
            Move(side + (d > 9f ? to.normalized : d < 5f ? -to.normalized : Vector3.zero), 8f * speedFactor);
            yield return null;
        }
    }

    Vector3 BehindPlayer(float distance, float angleJitter)
    {
        Vector3 fwd = player.transform.forward;
        fwd.y = 0f;
        fwd = Quaternion.Euler(0f, angleJitter, 0f) * fwd.normalized;
        Vector3 p = player.transform.position - fwd * distance;
        p.y = transform.position.y;
        Vector3 off = p - ArenaCenter;
        off.y = 0f;
        if (off.magnitude > arenaRadius) p = ArenaCenter + off.normalized * arenaRadius + Vector3.up * (p.y - ArenaCenter.y);
        return p;
    }

    /// <summary>Preskok k hráčovi a rez; slabé fialové znamenie na zemi je jediné varovanie, keď je neviditeľná.</summary>
    IEnumerator Slash(float tele)
    {
        DebugState = "slash";
        tele /= speedFactor;
        Vector3 dest = BehindPlayer(2.4f, Random.Range(-70f, 70f));
        float t = 0f;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        while (t < tele)
        {
            t += Time.deltaTime;
            Vfx.Disc(dest + Vector3.up * 0.06f, 5f, new Color(0.6f, 0.2f, 0.9f, Invisible ? 0.28f : 0.4f), 0.06f);
            if (!Invisible) Flash(Violet, Mathf.Lerp(0.3f, 1f, t / tele));
            yield return null;
        }
        Vector3 from = transform.position;
        transform.position = dest;
        Vector3 to = FlatToPlayer();
        if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        Vfx.Beam(from + Vector3.up, dest + Vector3.up, new Color(0.7f, 0.3f, 1f, 0.7f), 0.15f, 0.15f);
        Vfx.Beam(dest + Vector3.up * 2f, player.transform.position + Vector3.up * 0.2f, new Color(0.9f, 0.7f, 1f), 0.18f, 0.2f);
        GameAudio.Play(GameAudio.Sfx.TowerShot, dest);
        if (to.magnitude < 3.6f) HurtPlayer(slashDamage);
        yield return new WaitForSeconds(0.5f / speedFactor);
    }

    IEnumerator SlashSeries()
    {
        for (int i = 0; i < 3; i++) yield return Slash(0.45f);
        DebugState = "exhausted";
        yield return new WaitForSeconds(0.8f);
    }

    IEnumerator BladeFan()
    {
        DebugState = "fan";
        float tele = 0.9f / speedFactor;
        float t = 0f;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        Vector3 origin = transform.position + Vector3.up * 1.4f;
        while (t < tele)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 360f);
            if (!Invisible) Flash(Violet, Mathf.Lerp(0.3f, 1f, t / tele));
            Vector3 dir = FlatToPlayer().normalized;
            for (int i = -1; i <= 1; i++)
                Vfx.Beam(origin, origin + Quaternion.Euler(0f, i * 22f, 0f) * dir * 25f, new Color(0.6f, 0.2f, 0.9f, 0.3f), 0.05f, 0.05f);
            yield return null;
        }
        Vector3 aim = FlatToPlayer().normalized;
        for (int i = -1; i <= 1; i++)
        {
            Vector3 end = origin + Quaternion.Euler(0f, i * 22f, 0f) * aim * 25f;
            Vfx.Beam(origin, end, new Color(0.9f, 0.7f, 1f), 0.2f, 0.18f);
            // Zásah, ak hráč stojí blízko lúča
            Vector3 a = new Vector3(origin.x, 0f, origin.z), b = new Vector3(end.x, 0f, end.z), p = player.transform.position;
            p.y = 0f;
            Vector3 ab = b - a;
            float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            if (Vector3.Distance(p, a + ab * k) < 1.3f) { HurtPlayer(fanDamage); break; }
        }
        GameAudio.Play(GameAudio.Sfx.TowerShot, transform.position);
        yield return Reposition(Random.Range(0.8f, 1.2f));
    }

    IEnumerator VanishAssault()
    {
        DebugState = "vanish";
        Vfx.Disc(transform.position + Vector3.up * 0.1f, 8f, new Color(0.2f, 0.05f, 0.35f, 0.7f), 0.6f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        SetVisible(false);

        int n = IllusionCount;
        for (int i = 0; i < n; i++)
        {
            float a = (i * 360f / n + Random.Range(0f, 40f)) * Mathf.Deg2Rad;
            Vector3 p = player.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 9f;
            p.y = transform.position.y;
            Vector3 off = p - ArenaCenter;
            off.y = 0f;
            if (off.magnitude > arenaRadius) p = ArenaCenter + off.normalized * arenaRadius + Vector3.up * (p.y - ArenaCenter.y);
            GetIllusion().Activate(this, p, 12f, illusionDamage);
            Vfx.Disc(p + Vector3.up * 0.1f, 4f, new Color(0.5f, 0.2f, 0.8f, 0.5f), 0.4f);
        }
        yield return new WaitForSeconds(0.8f);

        int strikes = Phase == 3 ? 3 : 2;
        for (int i = 0; i < strikes; i++) yield return Slash(0.9f);

        yield return new WaitForSeconds(0.6f);
        SetVisible(true);
        noVanishUntil = Time.time + 5f;
        GameHUD.WorldText(transform.position + Vector3.up * 3.5f, "VYNORILA SA", Violet);
        DebugState = "reappear";
        yield return new WaitForSeconds(0.5f);
    }

    // ---------- Finišer ----------

    protected override IEnumerator RunFinisher()
    {
        DebugState = "finisher";
        CloseWindow();
        DissolveAll();
        SetVisible(true);
        PanelPage[] pages =
        {
            new PanelPage(
                PanelSpec.Narrative("Posledná ilúzia sa rozpadá. Kuro kľačí a tmavne jej kimono.", PanelArt.Figure),
                PanelSpec.Narrative("Tiene sa jej vracajú do rúk. Úder, kým je odhalená.", PanelArt.Eye),
                PanelSpec.Qte(KeyCode.F, 2.4f, 15f, "Úder do srdca tieňa: F v zelenom pásme!")),
            new PanelPage(PanelSpec.Narrative("", PanelArt.Blade, null, new Vector2(0.4f, 0.6f)), PanelSpec.Result()),
            new PanelPage(
                PanelSpec.Narrative("Kuro: „Naučila som ťa vidieť tmu... a teraz mi ju berieš.“", PanelArt.Eye),
                PanelSpec.Narrative("Čepeľ Tieňa zostáva v tvojich rukách. Ticho splynutie je preč.", PanelArt.Blade))
        };
        yield return PlayPanels(FightId, pages);

        Vfx.Disc(transform.position + Vector3.up * 0.3f, 10f, new Color(0.5f, 0.2f, 0.8f, 0.6f), 0.9f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        yield return new WaitForSeconds(0.6f);
        GameHUD.Message("Lady Kuro je porazená. Získavaš Čepeľ Tieňa, no strácaš tiché splynutie s davom.", 6f);
        DefeatCleanup("defeated");
        foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }
}
