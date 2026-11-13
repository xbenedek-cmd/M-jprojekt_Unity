using UnityEngine;

/// <summary>Tieňová ilúzia Lady Kuro: slabý klon, ktorý naháňa hráča. Rozpadne sa pri akomkoľvek zásahu, ohni alebo svetle.</summary>
[RequireComponent(typeof(Damageable))]
public class KuroIllusion : MonoBehaviour
{
    private KuroBoss owner;
    private Damageable health;
    private float nextStrikeAt, dieAt;
    private float speed = 5f;
    private float damage = 8f;

    public bool Alive => gameObject.activeSelf;

    void Awake()
    {
        health = GetComponent<Damageable>();
        health.team = CombatTeam.Demon;
        health.displayName = "Tieňová ilúzia";
        health.SetMaxHealth(10f);
        health.DamageFilter = (amount, attacker, kind) => { Dissolve(); return 0f; };   // zásah ju nikdy nezabije, len rozpustí
    }

    public void Activate(KuroBoss boss, Vector3 position, float lifeSeconds, float strikeDamage)
    {
        owner = boss;
        damage = strikeDamage;
        transform.position = position;
        dieAt = Time.time + lifeSeconds;
        nextStrikeAt = Time.time + 1.2f;
        gameObject.SetActive(true);
    }

    void Update()
    {
        if (owner == null || owner.PlayerRef == null || owner.PlayerRef.IsDead) { Dissolve(); return; }
        if (Time.time >= dieAt) { Dissolve(); return; }

        Vector3 to = owner.PlayerRef.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        if (to.magnitude > 2.2f) transform.position += to.normalized * speed * Time.deltaTime;
        else if (Time.time >= nextStrikeAt)
        {
            nextStrikeAt = Time.time + 1.6f;
            owner.PlayerRef.TakeDamage(damage, DamageSource.Normal);
            Vfx.Beam(transform.position + Vector3.up * 1.4f, owner.PlayerRef.transform.position + Vector3.up * 1.2f, new Color(0.6f, 0.2f, 0.9f), 0.15f, 0.12f);
        }
    }

    public void Dissolve()
    {
        if (!gameObject.activeSelf) return;
        Vfx.Disc(transform.position + Vector3.up * 0.1f, 4f, new Color(0.5f, 0.2f, 0.8f, 0.5f), 0.4f);
        gameObject.SetActive(false);
    }
}
