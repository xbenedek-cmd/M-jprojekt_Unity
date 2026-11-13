using System.Collections;
using UnityEngine;

/// <summary>
/// Lord Raizen (Klan Blesku), elita na kostre BossCore. Extrémne rýchly: bleskové výpady a reťazové výboje; bežný zásah ho sotva zasiahne
/// a blesk ho lieči. Slabina podľa Postavy.txt: uzemnenie. Zemný útok (Klan Divočiny, DamageKind.Earth) ho uzemní a znehybní;
/// to isté sa stane, keď sa pri výpade zrazí s kamennou zábranou (ArenaCover), takže ho dá uzemniť aj bez Divočiny.
/// Fázy: 100–70 % výpady a bleskové reťaze, 70–40 % pridáva búrku, 40–10 % rýchlejšie kombá a krátke vyčerpanie po nich. Pod 10 % QTE panel.
/// </summary>
public class RaizenBoss : BossCore
{
    public const string FightId = "boss_raizen";

    [Header("Raizen")]
    [SerializeField] private float hoverSpeed = 13f;
    [SerializeField] private float dashDamage = 20f;
    [SerializeField] private float boltDamage = 13f;
    [SerializeField] private float stormDamage = 15f;
    [SerializeField] private float groundedSeconds = 4f;
    [SerializeField] private float groundCooldown = 6f;

    public bool Grounded => Time.time < groundedUntil;

    private float groundedUntil, nextGroundAt;
    private float speedFactor = 1f;
    private int attackCounter;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private readonly RaycastHit[] hits = new RaycastHit[24];
    private static readonly Color Neon = new Color(0.4f, 0.85f, 1f);

    protected override void Awake()
    {
        base.Awake();
        bossId = "raizen";
        bossName = "Lord Raizen";
        Health.displayName = bossName;
        arenaRadius = 34f;
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    // ---------- Začiatok a koniec ----------

    protected override void OnFightBegin()
    {
        MangaPanels.ForgetFight(FightId);
        ResetState();
        foreach (ArenaCover c in FindObjectsByType<ArenaCover>(FindObjectsInactive.Include)) c.Restore();
        GameHUD.Message("Raizen je príliš rýchly na zásah. Uzemni ho: zemný útok, alebo ho nechaj naraziť do kameňa.", 5f);
    }

    protected override void OnFightAborted() { ResetState(); }

    void ResetState()
    {
        transform.SetPositionAndRotation(startPosition, startRotation);
        speedFactor = 1f;
        groundedUntil = 0f;
        nextGroundAt = 0f;
        attackCounter = 0;
        Phase = 1;
        CloseWindow();
    }

    // ---------- Uzemnenie ----------

    void Ground(string why)
    {
        if (Grounded) return;
        float seconds = groundedSeconds * (Phase == 3 ? 0.75f : 1f);
        groundedUntil = Time.time + seconds;
        nextGroundAt = groundedUntil + groundCooldown;
        GameHUD.Message(why, 3.5f);
        Vfx.Disc(transform.position + Vector3.up * 0.1f, 5f, new Color(0.55f, 0.45f, 0.3f, 0.6f), 0.6f);
        OpenWindow(seconds, 1.8f);
        Stun(seconds);
    }

    protected override void OnStunned()
    {
        if (Grounded) GameHUD.WorldText(transform.position + Vector3.up * 4f, "UZEMNENÝ", new Color(0.8f, 0.65f, 0.3f));
    }

    protected override float ModifyIncoming(float amount, GameObject attacker, DamageKind kind)
    {
        if (Grounded) return amount;
        if (kind == DamageKind.Earth && Time.time >= nextGroundAt)
        {
            Ground("Zemný útok uzemnil Raizena! Skrat ho znehybnil.");
            return amount;
        }
        if (kind == DamageKind.Lightning)
        {
            GameHUD.WorldText(transform.position + Vector3.up * 4f, "POHLTENÉ", Neon);
            return 0f;
        }
        GameHUD.WorldText(transform.position + Vector3.up * 4f, "UHOL", Neon);
        return amount * 0.25f;   // rýchlosť: väčšinu zásahov kľučkou obíde
    }

    // ---------- Fázy ----------

    protected override IEnumerator OnPhaseEnter(int phase)
    {
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 10f, new Color(0.4f, 0.85f, 1f, 0.5f), 0.8f);
        if (phase == 2)
        {
            speedFactor = 1.3f;
            GameHUD.Message("Fáza 2: Raizen privoláva búrku. Pozor na značky na zemi.", 4.5f);
        }
        else if (phase == 3)
        {
            speedFactor = 1.6f;
            GameHUD.Message("Fáza 3: Preťaženie. Kombá výpadov, po nich je vyčerpaný.", 4.5f);
        }
        yield return new WaitForSeconds(1.2f);
    }

    protected override IEnumerator NextAttack()
    {
        attackCounter++;
        switch (Phase)
        {
            case 1:
                yield return attackCounter % 2 == 0 ? ChainBolt() : Dash(0.9f);
                break;
            case 2:
                int k = attackCounter % 3;
                yield return k == 0 ? Storm() : k == 1 ? Dash(0.7f) : ChainBolt();
                break;
            default:
                if (attackCounter % 3 == 0) yield return Storm();
                else yield return DashCombo();
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
            FaceTowards(to, 360f);
            float d = to.magnitude;
            Vector3 side = Vector3.Cross(to, Vector3.up).normalized;
            Vector3 dir = side + (d > 11f ? to.normalized : d < 7f ? -to.normalized : Vector3.zero);
            Move(dir, hoverSpeed * speedFactor);
            yield return null;
        }
    }

    static float SegmentDistance(Vector3 a, Vector3 b, Vector3 p)
    {
        a.y = b.y = p.y = 0f;
        Vector3 ab = b - a;
        float len = ab.sqrMagnitude;
        float t = len < 0.0001f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / len);
        return Vector3.Distance(p, a + ab * t);
    }

    IEnumerator Dash(float tele)
    {
        DebugState = "dash";
        tele /= speedFactor;
        Vector3 from = transform.position;
        Vector3 target = DashTarget();
        float t = 0f;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        while (t < tele)
        {
            t += Time.deltaTime;
            FaceTowards(target - transform.position, 720f);
            if (t < tele * 0.6f) target = DashTarget();   // posledná chvíľa je zamknutá: dá sa uhnúť
            Flash(Neon, Mathf.Lerp(0.3f, 1f, t / tele));
            Vfx.Beam(from + Vector3.up, target + Vector3.up, new Color(0.4f, 0.85f, 1f, 0.45f), 0.05f, 0.3f);
            yield return null;
        }

        // Zrážka s kamennou zábranou na dráhe výpadu
        Vector3 end = target;
        ArenaCover hitCover = null;
        float bestAlong = float.MaxValue;
        foreach (ArenaCover c in ArenaCover.All)
        {
            if (!c.Alive || SegmentDistance(from, target, c.transform.position) > 2.6f) continue;
            float along = (c.transform.position - from).sqrMagnitude;
            if (along < bestAlong) { bestAlong = along; hitCover = c; }
        }
        if (hitCover != null)
        {
            Vector3 dirFlat = hitCover.transform.position - from;
            dirFlat.y = 0f;
            end = hitCover.transform.position - dirFlat.normalized * 2.8f;
            end.y = from.y;
        }

        bool hitPlayer = SegmentDistance(from, end, player.transform.position) < 1.8f;
        CombatFx.Lightning(from + Vector3.up, end + Vector3.up, new Color(0.7f, 0.95f, 1f), 0.25f, 0.4f);
        GameAudio.Play(GameAudio.Sfx.TowerShot, from);
        float move = 0.1f;
        float m = 0f;
        while (m < move)
        {
            m += Time.deltaTime;
            transform.position = Vector3.Lerp(from, end, Mathf.Clamp01(m / move));
            yield return null;
        }
        transform.position = end;
        if (hitPlayer) HurtPlayer(dashDamage);

        if (hitCover != null)
        {
            hitCover.Collapse();
            Ground("Raizen narazil do kameňa a uzemnil sa!");
            yield break;
        }
        yield return Hover(Random.Range(0.6f, 1f) / speedFactor);
    }

    Vector3 DashTarget()
    {
        Vector3 dir = FlatToPlayer().normalized;
        Vector3 p = player.transform.position + dir * 3f;   // preletí o kúsok ďalej
        p.y = transform.position.y;
        Vector3 off = p - ArenaCenter;
        off.y = 0f;
        if (off.magnitude > arenaRadius) p = ArenaCenter + off.normalized * arenaRadius + Vector3.up * (p.y - ArenaCenter.y);
        return p;
    }

    IEnumerator DashCombo()
    {
        for (int i = 0; i < 3; i++)
        {
            yield return Dash(0.5f);
            if (Grounded) yield break;
        }
        DebugState = "exhausted";
        OpenWindow(1.6f, 1.4f);
        GameHUD.WorldText(transform.position + Vector3.up * 4f, "VYČERPANÝ", new Color(1f, 0.9f, 0.4f));
        float t = 0f;
        while (t < 1.6f) { t += Time.deltaTime; Flash(Neon, 0.25f); yield return null; }
        CloseWindow();
    }

    IEnumerator ChainBolt()
    {
        DebugState = "bolt";
        float tele = 0.8f / speedFactor;
        float t = 0f;
        while (t < tele)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 540f);
            Flash(Neon, Mathf.Lerp(0.3f, 1f, t / tele));
            Vfx.Beam(BoltOrigin(), player.transform.position + Vector3.up, new Color(0.4f, 0.85f, 1f, 0.35f), 0.05f, 0.05f);
            yield return null;
        }
        Vector3 impact = FireBolt(BoltOrigin(), player.transform.position + Vector3.up, boltDamage);
        // Reťaz: výboj preskočí na dva ďalšie body okolo miesta zásahu
        Vector3 prev = impact;
        for (int i = 0; i < 2; i++)
        {
            Vector2 r = Random.insideUnitCircle.normalized * Random.Range(3f, 6f);
            Vector3 next = impact + new Vector3(r.x, 0f, r.y);
            CombatFx.Lightning(prev, next, Neon, 0.2f, 0.15f);
            if (Vector3.Distance(new Vector3(next.x, 0f, next.z), new Vector3(player.transform.position.x, 0f, player.transform.position.z)) < 1.6f) HurtPlayer(boltDamage * 0.7f);
            prev = next;
            yield return new WaitForSeconds(0.12f);
        }
        yield return Hover(Random.Range(0.8f, 1.3f) / speedFactor);
    }

    Vector3 BoltOrigin() => (rig != null && rig.muzzleRight != null ? rig.muzzleRight.position : transform.position + Vector3.up * 2f);

    /// <summary>Výboj zasiahne prvú vec v ceste: kamenná zábrana ho pohltí, hráč dostane zásah. Vráti bod dopadu.</summary>
    Vector3 FireBolt(Vector3 from, Vector3 to, float damage)
    {
        Vector3 dir = (to - from).normalized;
        int n = Physics.RaycastNonAlloc(from, dir, hits, 70f, ~0, QueryTriggerInteraction.Ignore);
        float best = 70f;
        RaycastHit bestHit = default;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = hits[i];
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best) { best = h.distance; bestHit = h; found = true; }
        }
        Vector3 end = found ? bestHit.point : from + dir * 70f;
        CombatFx.Lightning(from, end, new Color(0.75f, 0.95f, 1f), 0.2f, 0.18f);
        GameAudio.Play(GameAudio.Sfx.TowerShot, from);
        if (!found) return end;
        ArenaCover cover = bestHit.collider.GetComponentInParent<ArenaCover>();
        if (cover != null) cover.Hit(damage * 3f);
        else if (bestHit.collider.GetComponentInParent<PlayerStats>() != null) HurtPlayer(damage);
        return end;
    }

    IEnumerator Storm()
    {
        DebugState = "storm";
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        const int count = 5;
        var points = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 r = i == 0 ? Vector2.zero : Random.insideUnitCircle * 7f;
            points[i] = player.transform.position + new Vector3(r.x, 0f, r.y);
        }
        float tele = 1.1f / speedFactor;
        float t = 0f;
        while (t < tele)
        {
            t += Time.deltaTime;
            Flash(Neon, 0.6f);
            for (int i = 0; i < count; i++) Vfx.Disc(points[i] + Vector3.up * 0.05f, 3f, new Color(1f, 0.9f, 0.3f, 0.35f), 0.05f);
            yield return null;
        }
        for (int i = 0; i < count; i++)
        {
            CombatFx.Lightning(points[i] + Vector3.up * 30f, points[i], new Color(0.8f, 0.95f, 1f), 0.25f, 0.5f);
            Vfx.Disc(points[i] + Vector3.up * 0.1f, 3f, new Color(0.6f, 0.9f, 1f, 0.6f), 0.3f);
            Vector3 d = player.transform.position - points[i];
            d.y = 0f;
            if (d.magnitude < 3f) HurtPlayer(stormDamage);
        }
        GameAudio.Play(GameAudio.Sfx.TowerShot, transform.position);
        yield return Hover(Random.Range(0.8f, 1.2f) / speedFactor);
    }

    // ---------- Finišer ----------

    protected override IEnumerator RunFinisher()
    {
        DebugState = "finisher";
        CloseWindow();
        PanelPage[] pages =
        {
            new PanelPage(
                PanelSpec.Narrative("Raizen sa zadrháva. Z rohu mu šľahá nekontrolovaný výboj.", PanelArt.Figure),
                PanelSpec.Narrative("Prvýkrát stojí. Teraz!", PanelArt.Eye),
                PanelSpec.Qte(KeyCode.F, 2.4f, 15f, "Úder do rohu: F v zelenom pásme!")),
            new PanelPage(PanelSpec.Narrative("", PanelArt.Burst, null, new Vector2(0.4f, 0.6f)), PanelSpec.Result()),
            new PanelPage(
                PanelSpec.Narrative("Raizen: „Polokrvník... Takže predsa nie si slabý.“", PanelArt.Eye),
                PanelSpec.Narrative("Roh praská. Blesk z neho prúdi do teba. Si na rebríčku vyššie.", PanelArt.Figure))
        };
        yield return PlayPanels(FightId, pages);

        Vfx.Disc(transform.position + Vector3.up * 0.3f, 9f, new Color(0.5f, 0.9f, 1f, 0.6f), 0.9f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        yield return new WaitForSeconds(0.6f);
        GameHUD.Message("Raizen je porazený. Získavaš jeho kyber-rohy s bleskovou aurou.", 6f);
        DefeatCleanup("defeated");
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }
}
