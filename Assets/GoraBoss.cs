using System.Collections;
using UnityEngine;

/// <summary>
/// General Gora (Klan Divočiny), elita na kostre BossCore: obrovský pomalý tank. Zemetrasenia (rázová vlna), balvany a zametanie kmeňom.
/// Slabiny podľa Postavy.txt: oheň (DamageKind.Fire) obchádza 50 % brnenie a podpáli ho (trvalé DoT, obnovuje sa každým ohnivým zásahom);
/// je extrémne pomalý, takže ho dá obehnúť: zásah zozadu (zóna "back") brnenie ignoruje. Po zemetrasení je chvíľu v recovery (okno zraniteľnosti).
/// Fázy: 100–70 % zemetrasenia a balvany, 70–40 % pridáva zametanie, 40–10 % zúrivosť (rýchlejší, dážď balvanov). Pod 10 % QTE panel.
/// </summary>
public class GoraBoss : BossCore
{
    public const string FightId = "boss_gora";

    [Header("Gora")]
    [SerializeField] private float moveSpeed = 2.2f;
    [SerializeField] private float armor = 0.5f;
    [SerializeField] private float quakeDamage = 24f;
    [SerializeField] private float boulderDamage = 22f;
    [SerializeField] private float sweepDamage = 28f;
    [SerializeField] private float burnDps = 9f;
    [SerializeField] private float burnSeconds = 6f;

    public bool Burning => Time.time < burnUntil;

    private PlayerMovement movement;
    private GameObject boulder;
    private float speedFactor = 1f;
    private float burnUntil;
    private int attackCounter;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private static readonly Color Earthy = new Color(0.65f, 0.5f, 0.25f);

    protected override void Awake()
    {
        base.Awake();
        bossId = "gora";
        bossName = "General Gora";
        Health.displayName = bossName;
        arenaRadius = 32f;
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    void OnDestroy() { if (boulder != null) Destroy(boulder); }

    // ---------- Začiatok a koniec ----------

    protected override void OnFightBegin()
    {
        MangaPanels.ForgetFight(FightId);
        movement = FindAnyObjectByType<PlayerMovement>();
        ResetState();
        foreach (ArenaCover c in FindObjectsByType<ArenaCover>(FindObjectsInactive.Include)) c.Restore();
        SetZones("back", true);
        GameHUD.Message("Gora má 50 % brnenie. Oheň ho obíde a podpáli; je pomalý, obíď ho zozadu.", 5f);
    }

    protected override void OnFightAborted()
    {
        ResetState();
        HideBoulder();
    }

    void ResetState()
    {
        transform.SetPositionAndRotation(startPosition, startRotation);
        speedFactor = 1f;
        burnUntil = 0f;
        attackCounter = 0;
        Phase = 1;
        CloseWindow();
    }

    // ---------- Poškodenie ----------

    protected override float ModifyIncoming(float amount, GameObject attacker, DamageKind kind)
    {
        if (kind == DamageKind.Fire)
        {
            // Rozlíšenie zásahu od tikov DoT: tiky sú drobné a nesmú predlžovať horenie donekonečna
            if (amount > 2f)
            {
                bool wasBurning = Burning;
                Health.ApplyDot(burnDps, burnSeconds, attacker, DamageKind.Fire);
                burnUntil = Time.time + burnSeconds;
                if (!wasBurning) GameHUD.WorldText(transform.position + Vector3.up * 8f, "HORÍ!", new Color(1f, 0.5f, 0.1f));
            }
            return amount;
        }
        if (LastHitZone != null) return amount;   // zásah zozadu: brnenie nie je
        GameHUD.WorldText(transform.position + Vector3.up * 8f, "BRNENIE", Earthy);
        return amount * armor;
    }

    // ---------- Fázy ----------

    protected override IEnumerator OnPhaseEnter(int phase)
    {
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 12f, new Color(0.5f, 0.7f, 0.2f, 0.5f), 0.8f);
        if (phase == 2) GameHUD.Message("Fáza 2: Gora vytrháva kmene a zametá nimi okolie.", 4.5f);
        else if (phase == 3)
        {
            speedFactor = 1.5f;
            GameHUD.Message("Fáza 3: Zúrivosť. Gora je rýchlejší a zhadzuje balvany v dážde.", 4.5f);
        }
        yield return new WaitForSeconds(1.2f);
    }

    protected override IEnumerator NextAttack()
    {
        attackCounter++;
        float d = FlatToPlayer().magnitude;
        switch (Phase)
        {
            case 1:
                yield return attackCounter % 2 == 0 ? Quake() : Boulder(1);
                break;
            case 2:
                if (d < 11f && attackCounter % 3 == 0) yield return Sweep();
                else yield return attackCounter % 2 == 0 ? Quake() : Boulder(1);
                break;
            default:
                if (d < 11f && attackCounter % 2 == 0) yield return Sweep();
                else yield return attackCounter % 3 == 0 ? Quake() : Boulder(3);
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
            FaceTowards(to, 45f * speedFactor);   // pomalé otáčanie: dá sa obehnúť zozadu
            if (to.magnitude > 8f) Move(transform.forward, moveSpeed * speedFactor);
            yield return null;
        }
    }

    IEnumerator Quake()
    {
        DebugState = "quake";
        yield return TelegraphFacing(1.5f / speedFactor, new Color(1f, 0.6f, 0.2f));

        Vector3 c = transform.position;
        GameAudio.Play(GameAudio.Sfx.Build, c);
        float r = 0f;
        bool hit = false;
        while (r < 26f)
        {
            r += 15f * speedFactor * Time.deltaTime;
            Vfx.Disc(c + Vector3.up * 0.1f, r * 2f, new Color(0.7f, 0.5f, 0.25f, 0.35f), 0.06f);
            if (!hit)
            {
                Vector3 d = player.transform.position - c;
                d.y = 0f;
                if (Mathf.Abs(d.magnitude - r) < 1.8f && movement != null && movement.IsGrounded) { hit = true; HurtPlayer(quakeDamage); }
            }
            yield return null;
        }
        // Po zemetrasení sa Gora vzpriamuje: okno na zásah
        DebugState = "recover";
        OpenWindow(1.6f, 1.3f);
        yield return new WaitForSeconds(1.6f / speedFactor);
        CloseWindow();
    }

    IEnumerator TelegraphFacing(float seconds, Color color)
    {
        float t = 0f;
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        while (t < seconds)
        {
            t += Time.deltaTime;
            FaceTowards(FlatToPlayer(), 60f);
            Flash(color, Mathf.Lerp(0.3f, 1f, t / seconds) * (0.6f + 0.4f * Mathf.Sin(t * 20f)));
            yield return null;
        }
    }

    IEnumerator Sweep()
    {
        DebugState = "sweep";
        yield return TelegraphFacing(1.1f / speedFactor, new Color(0.6f, 1f, 0.3f));
        Vector3 origin = transform.position;
        Vfx.Disc(origin + transform.forward * 5f + Vector3.up * 0.1f, 11f, new Color(0.5f, 0.8f, 0.2f, 0.5f), 0.3f);
        GameAudio.Play(GameAudio.Sfx.TowerShot, origin);
        Vector3 to = FlatToPlayer();
        if (to.magnitude < 11f && Vector3.Angle(transform.forward, to) < 65f) HurtPlayer(sweepDamage);
        yield return new WaitForSeconds(0.5f);
        yield return Walk(Random.Range(1.5f, 2.5f));
    }

    IEnumerator Boulder(int count)
    {
        DebugState = "boulder";
        for (int i = 0; i < count; i++)
        {
            yield return TelegraphFacing(1.0f / speedFactor, new Color(0.8f, 0.7f, 0.5f));
            Vector3 from = (rig != null && rig.fist != null ? rig.fist.position : transform.position) + Vector3.up * 5f;
            Vector3 target = player.transform.position;   // zamknutý cieľ: pohyb ho zmení
            if (i > 0) { Vector2 r = Random.insideUnitCircle * 5f; target += new Vector3(r.x, 0f, r.y); }
            yield return Throw(from, target);
        }
        yield return Walk(Random.Range(1.5f, 2.2f));
    }

    IEnumerator Throw(Vector3 from, Vector3 target)
    {
        if (boulder == null)
        {
            boulder = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            boulder.name = "GoraBalvan";
            Destroy(boulder.GetComponent<Collider>());
            boulder.transform.localScale = Vector3.one * 2.6f;
            boulder.GetComponent<Renderer>().material.color = new Color(0.35f, 0.3f, 0.25f);
        }
        boulder.SetActive(true);
        float flight = 1.3f / speedFactor;
        float t = 0f;
        while (t < flight)
        {
            t += Time.deltaTime;
            float k = t / flight;
            Vector3 p = Vector3.Lerp(from, target, k);
            p.y += Mathf.Sin(k * Mathf.PI) * 9f;
            boulder.transform.position = p;
            Vfx.Disc(target + Vector3.up * 0.08f, 9f, new Color(1f, 0.3f, 0.2f, 0.3f), 0.06f);
            yield return null;
        }
        HideBoulder();
        Vfx.Disc(target + Vector3.up * 0.1f, 9f, new Color(0.7f, 0.55f, 0.3f, 0.6f), 0.4f);
        GameAudio.Play(GameAudio.Sfx.Build, target);
        Vector3 d = player.transform.position - target;
        d.y = 0f;
        if (d.magnitude < 4.5f) HurtPlayer(boulderDamage);
        foreach (ArenaCover c in ArenaCover.All)
        {
            Vector3 cd = c.transform.position - target;
            cd.y = 0f;
            if (c.Alive && cd.magnitude < 5f) { c.Hit(300f); break; }   // drví kmene, ktoré sú v ceste
        }
    }

    void HideBoulder() { if (boulder != null) boulder.SetActive(false); }

    // ---------- Finišer ----------

    protected override IEnumerator RunFinisher()
    {
        DebugState = "finisher";
        CloseWindow();
        HideBoulder();
        PanelPage[] pages =
        {
            new PanelPage(
                PanelSpec.Narrative("Kôra praská, mach tlie. Gora padá na kolená.", PanelArt.Figure),
                PanelSpec.Narrative("Zelené žily mu blednú. Jeden úder do srdca skaly.", PanelArt.Eye),
                PanelSpec.Qte(KeyCode.F, 2.4f, 15f, "Úder do srdca: F v zelenom pásme!")),
            new PanelPage(PanelSpec.Narrative("", PanelArt.Fist, null, new Vector2(0.4f, 0.6f)), PanelSpec.Result()),
            new PanelPage(
                PanelSpec.Narrative("Gora: „Les si zapamätá... tvoje meno.“", PanelArt.Eye),
                PanelSpec.Narrative("Z rozpadnutého tela vyrastá toxický totem. Teritórium je tvoje.", PanelArt.Figure))
        };
        yield return PlayPanels(FightId, pages);

        Vfx.Disc(transform.position + Vector3.up * 0.3f, 12f, new Color(0.4f, 0.8f, 0.2f, 0.6f), 0.9f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        yield return new WaitForSeconds(0.6f);
        GameHUD.Message("Gora je porazený. Získavaš Klanový Totem, no lesné beštie budú agresívnejšie.", 6f);
        DefeatCleanup("defeated");
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }
}
