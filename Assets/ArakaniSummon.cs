using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum SummonKind { Raizen, Kuro, Ignis, Carmilla }

/// <summary>
/// Echo klanovej elity privolané Arakanim medzi fázami finále. Prichádza len elita, ktorá ešte nebola porazená (BossRegistry). Je to zjednodušený
/// súboj s jedným charakteristickým útokom danej elity; musí sa zničiť, kým sa začne ďalšia fáza. Zásah ju nikdy nezabije cez Damageable (vlastné HP).
/// </summary>
[RequireComponent(typeof(Damageable))]
public class ArakaniSummon : MonoBehaviour
{
    private static readonly Dictionary<SummonKind, ArakaniSummon> pool = new Dictionary<SummonKind, ArakaniSummon>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { pool.Clear(); }

    public const float BaseHealth = 320f;

    public SummonKind Kind { get; private set; }
    public bool Alive => gameObject.activeSelf;

    private ArakaniBoss owner;
    private Damageable health;
    private float hp, nextAttackAt, speed = 5.5f;
    private bool attacking;

    public static string IdOf(SummonKind k) => k == SummonKind.Raizen ? "raizen" : k == SummonKind.Kuro ? "kuro" : k == SummonKind.Ignis ? "ignis" : "carmilla";
    public static string NameOf(SummonKind k) => k == SummonKind.Raizen ? "Raizen" : k == SummonKind.Kuro ? "Lady Kuro" : k == SummonKind.Ignis ? "Lord Ignis" : "Carmilla";
    static Color ColorOf(SummonKind k) => k == SummonKind.Raizen ? new Color(0.4f, 0.85f, 1f) : k == SummonKind.Kuro ? new Color(0.5f, 0.2f, 0.8f) : k == SummonKind.Ignis ? new Color(1f, 0.4f, 0.1f) : new Color(0.8f, 0.05f, 0.15f);

    public static ArakaniSummon Spawn(SummonKind kind, Vector3 position, ArakaniBoss boss)
    {
        if (!pool.TryGetValue(kind, out ArakaniSummon s) || s == null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Echo_" + NameOf(kind);
            go.transform.localScale = new Vector3(1.6f, 1.7f, 1.6f);
            Color c = ColorOf(kind);
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", c * 0.6f);
            block.SetColor("_EmissionColor", c * 0.8f);
            Renderer r = go.GetComponent<Renderer>();
            r.material.EnableKeyword("_EMISSION");
            r.SetPropertyBlock(block);
            s = go.AddComponent<ArakaniSummon>();
            s.Kind = kind;
            pool[kind] = s;
        }
        s.Activate(boss, position);
        return s;
    }

    void Awake()
    {
        health = GetComponent<Damageable>();
        health.team = CombatTeam.Demon;
        health.SetMaxHealth(BaseHealth);
        health.DamageFilter = (amount, attacker, kind) => { Hit(amount); return 0f; };
    }

    void Activate(ArakaniBoss boss, Vector3 position)
    {
        owner = boss;
        health.displayName = "Echo: " + NameOf(Kind);
        hp = BaseHealth;
        attacking = false;
        nextAttackAt = Time.time + 1.5f;
        transform.position = position + Vector3.up * 1.7f;
        gameObject.SetActive(true);
        Vfx.Disc(position + Vector3.up * 0.1f, 8f, ColorOf(Kind) * 0.8f, 0.6f);
    }

    void Hit(float amount)
    {
        if (amount <= 0f || !Alive) return;
        hp -= amount;
        GameHUD.WorldText(transform.position + Vector3.up * 2.5f, Mathf.RoundToInt(amount).ToString(), ColorOf(Kind));
        if (hp <= 0f) Dissolve();
    }

    public void Dissolve()
    {
        if (!Alive) return;
        StopAllCoroutines();
        Vfx.Disc(transform.position - Vector3.up * 1.6f, 7f, ColorOf(Kind) * 0.8f, 0.5f);
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (owner == null || owner.PlayerRef == null || owner.PlayerRef.IsDead) { Dissolve(); return; }
        if (attacking) return;

        Vector3 to = owner.PlayerRef.transform.position - transform.position;
        to.y = 0f;
        float d = to.magnitude;
        if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        if (d > 9f) transform.position += to.normalized * speed * Time.deltaTime;
        else if (d < 5f) transform.position -= to.normalized * speed * 0.6f * Time.deltaTime;

        if (Time.time >= nextAttackAt) StartCoroutine(Attack());
    }

    IEnumerator Attack()
    {
        attacking = true;
        PlayerStats p = owner.PlayerRef;
        Vector3 ground = new Vector3(0f, transform.position.y - 1.7f, 0f);
        Color c = ColorOf(Kind);
        GameAudio.Play(GameAudio.Sfx.Alert, transform.position);

        switch (Kind)
        {
            case SummonKind.Raizen:   // bleskový výpad po priamke
            {
                Vector3 from = transform.position;
                Vector3 end = p.transform.position + (p.transform.position - from).normalized * 3f;
                end.y = from.y;
                for (float t = 0f; t < 0.8f; t += Time.deltaTime) { Vfx.Beam(from, end, new Color(c.r, c.g, c.b, 0.4f), 0.05f, 0.3f); yield return null; }
                Vfx.Beam(from, end, Color.white, 0.2f, 0.4f);
                Vector3 a = from, b = end; a.y = b.y = 0f;
                Vector3 pp = p.transform.position; pp.y = 0f;
                Vector3 ab = b - a;
                float k = Mathf.Clamp01(Vector3.Dot(pp - a, ab) / Mathf.Max(0.01f, ab.sqrMagnitude));
                if (Vector3.Distance(pp, a + ab * k) < 2f) p.TakeDamage(14f, DamageSource.Demon);
                transform.position = end;
                break;
            }
            case SummonKind.Kuro:     // preskok za hráča a rez
            {
                Vector3 fwd = p.transform.forward; fwd.y = 0f;
                Vector3 dest = p.transform.position - fwd.normalized * 2.4f;
                dest.y = transform.position.y;
                for (float t = 0f; t < 0.7f; t += Time.deltaTime) { Vfx.Disc(new Vector3(dest.x, ground.y + 0.06f, dest.z), 5f, new Color(0.6f, 0.2f, 0.9f, 0.35f), 0.06f); yield return null; }
                transform.position = dest;
                Vfx.Beam(dest + Vector3.up, p.transform.position + Vector3.up, new Color(0.9f, 0.7f, 1f), 0.18f, 0.2f);
                if ((p.transform.position - dest).magnitude < 3.6f) p.TakeDamage(14f, DamageSource.Demon);
                break;
            }
            case SummonKind.Ignis:    // ohnivý výbuch na hráčovi
            {
                Vector3 target = p.transform.position;
                for (float t = 0f; t < 1.1f; t += Time.deltaTime) { Vfx.Disc(new Vector3(target.x, ground.y + 0.06f, target.z), 8f, new Color(1f, 0.3f, 0.1f, 0.35f), 0.06f); yield return null; }
                Vfx.Disc(new Vector3(target.x, ground.y + 0.1f, target.z), 8f, new Color(1f, 0.45f, 0.1f, 0.65f), 0.4f);
                Vector3 d = p.transform.position - target; d.y = 0f;
                if (d.magnitude < 4f) p.TakeDamage(16f, DamageSource.Demon);
                break;
            }
            default:                  // Carmilla: vysávanie lúčom
            {
                for (float t = 0f; t < 2f; t += Time.deltaTime)
                {
                    Vfx.Beam(transform.position + Vector3.up, p.transform.position + Vector3.up, new Color(0.8f, 0.05f, 0.15f), 0.08f, 0.12f);
                    p.TakeDamage(8f * Time.deltaTime, DamageSource.Demon);
                    yield return null;
                }
                break;
            }
        }
        attacking = false;
        nextAttackAt = Time.time + 3.2f;
    }
}
