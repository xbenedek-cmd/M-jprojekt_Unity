using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Dron, ktorý napája štít: obieha Rena, občas vystrelí (s varovaním) a dá sa zostreliť. Zostrelenie oboch zhodí štít.</summary>
public class RenDrone : MonoBehaviour
{
    private RenBoss owner;
    private Damageable hp;
    private Renderer rend;
    private MaterialPropertyBlock block;
    private float angle, nextShot, warnUntil;
    private bool dead;
    private float floorY;

    public static RenDrone Spawn(RenBoss owner, float startAngle, Material material)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "Dron_Ren";
        g.transform.localScale = Vector3.one * 0.9f;
        if (material != null) g.GetComponent<Renderer>().sharedMaterial = material;
        var d = g.AddComponent<RenDrone>();
        d.owner = owner;
        d.angle = startAngle;
        d.floorY = owner.ArenaCenter.y;
        d.nextShot = Time.time + Random.Range(1.5f, 3f);
        g.transform.position = owner.transform.position + Vector3.up * 4f;
        return d;
    }

    void Awake()
    {
        hp = gameObject.AddComponent<Damageable>();
        hp.team = CombatTeam.Demon;
        hp.displayName = "Dron Rena";
        hp.SetMaxHealth(45f);
        hp.DamageFilter = (a, at, k) => dead ? 0f : Mathf.Min(a, hp.Health - 1f);   // nikdy nie smrť cez Damageable (nechce odmeny ani reputáciu)
        hp.OnDamaged += (a, at) => { if (hp.Health <= 1.01f) Break(); };
        rend = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (dead || owner == null || !owner.InFight) { if (!dead && (owner == null || !owner.InFight)) Destroy(gameObject); return; }
        PlayerStats p = owner.PlayerRef;
        if (p == null) return;

        // Obieha Rena, ale ťahá sa k hráčovi, aby ho dalo zostreliť na blízko
        angle += 45f * Time.deltaTime;
        Vector3 center = Vector3.Lerp(owner.transform.position, p.transform.position, 0.45f);
        Vector3 target = center + new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad)) * 4.5f;
        target.y = floorY + 1.6f + Mathf.Sin(Time.time * 2f + angle) * 0.25f;
        transform.position = Vector3.MoveTowards(transform.position, target, 6f * Time.deltaTime);

        bool warn = Time.time < warnUntil;
        rend.GetPropertyBlock(block);
        block.SetColor("_EmissionColor", warn ? new Color(2.5f, 0.2f, 0.1f) * (0.6f + 0.4f * Mathf.Sin(Time.time * 30f)) : new Color(0f, 1.2f, 1.6f));
        rend.SetPropertyBlock(block);

        if (Time.time >= nextShot && !warn)
        {
            warnUntil = Time.time + 0.7f;   // telegraf: červené blikanie
            nextShot = Time.time + 3.8f;
            GameAudio.Play(GameAudio.Sfx.Alert, transform.position);
        }
        else if (warn && Time.time + Time.deltaTime >= warnUntil)
        {
            owner.FireLaser(transform.position, p.transform.position, 7f, new Color(1f, 0.3f, 0.2f));
        }
    }

    void Break()
    {
        if (dead) return;
        dead = true;
        Vfx.Disc(transform.position, 3f, new Color(1f, 0.7f, 0.2f, 0.7f), 0.4f);
        GameAudio.Play(GameAudio.Sfx.TowerShot, transform.position);
        owner?.NotifyDroneDown(this);
        Destroy(gameObject);
    }
}
