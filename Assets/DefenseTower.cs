using UnityEngine;

/// <summary>
/// Obrana mesta: postavená veža (alebo barikáda) strieľa na najbližšieho démona v dosahu a ak žiadny nie je, na stromy temnoty.
/// Na nezraniteľných démonov používa ich slabinu (veža má "všetky druhy munície"). Pridáva ju BuildPlot, kým obranná stavba stojí.
/// </summary>
public class DefenseTower : MonoBehaviour
{
    private float range = 30f;
    private float damage = 15f;
    private float interval = 1.2f;
    [System.NonSerialized] public bool needsPower;
    private float muzzleHeight = 6f;
    private float nextShot;
    private float nextScan;
    private DemonEnemy targetDemon;
    private DemonTree targetTree;

    public void Configure(float rangeMeters, float damagePerShot, float intervalSeconds, float buildingHeight)
    {
        range = rangeMeters;
        damage = damagePerShot;
        interval = Mathf.Max(0.2f, intervalSeconds * SoulBalance.DefenseIntervalFactor);
        muzzleHeight = buildingHeight + 0.5f;
    }

    void OnEnable() => nextShot = Time.time + 1f;

    void Update()
    {
        if (Time.time < nextShot) return;
        if (needsPower && !Power.Online) return;

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 0.5f;
            AcquireTarget();
        }

        if (targetDemon == null && targetTree == null) return;
        nextShot = Time.time + interval;
        Fire();
    }

    void AcquireTarget()
    {
        targetDemon = null;
        targetTree = null;

        Vector3 origin = transform.position;
        float best = range * range;

        foreach (DemonEnemy demon in DemonEnemy.All)
        {
            if (demon == null || demon.allied || demon.Damage == null || demon.Damage.IsDead) continue;

            // Neodhalený infiltrátor v ľudskej podobe nie je cieľ (rovnako ako pre strážnikov)
            NPCPerception disguise = demon.GetComponent<NPCPerception>();
            if (disguise != null && !disguise.Revealed && !demon.IsChasing) continue;

            float d = HorizontalSqr(demon.transform.position - origin);
            if (d < best) { best = d; targetDemon = demon; }
        }
        if (targetDemon != null) return;

        best = range * range;
        foreach (DemonTree tree in DemonTree.All)
        {
            if (tree == null || tree.Stage < 1 || tree.IsAncient) continue;

            float d = HorizontalSqr(tree.transform.position - origin);
            if (d < best) { best = d; targetTree = tree; }
        }
    }

    static float HorizontalSqr(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude;
    }

    void Fire()
    {
        Vector3 muzzle = transform.position + Vector3.up * muzzleHeight;

        if (targetDemon != null && targetDemon.Damage != null && !targetDemon.Damage.IsDead)
        {
            Damageable dmg = targetDemon.Damage;
            DamageKind kind = DamageKind.Normal;   // nezraniteľných vyšších démonov veža nezraní
            CombatFx.Lightning(muzzle, targetDemon.transform.position + Vector3.up * 1f, new Color(0.3f, 0.9f, 1f), 0.15f, 0.1f);
            dmg.TakeDamage(damage, gameObject, kind);
            GameAudio.Play(GameAudio.Sfx.TowerShot, muzzle);
        }
        else if (targetTree != null)
        {
            Vfx.Beam(muzzle, targetTree.transform.position + Vector3.up * 2f, new Color(1f, 0.55f, 0.2f), 0.2f, 0.12f);
            targetTree.Burn(damage * 1.5f);
            GameAudio.Play(GameAudio.Sfx.TowerShot, muzzle);
        }
    }
}
