using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fázy bossa podľa života: 100–66 % normálny boj; 2. fáza (pod 66 %): odhodí plášť (silnejšie žiarenie), je rýchlejší a silnejší a udiera šokovými
/// výbojmi okolo seba; 3. fáza (pod 33 %): ešte rýchlejší, strieľa blesky na miesto, kde hráč stojí (telegrafované kruhom). Pri zmene fázy
/// je krátko omráčený. Pridáva ho BossPost bossovi automaticky.
/// </summary>
[RequireComponent(typeof(DemonEnemy))]
public class BossPhases : MonoBehaviour
{
    public int Phase { get; private set; } = 1;

    [SerializeField] private float phase2At = 0.66f;
    [SerializeField] private float phase3At = 0.33f;
    [SerializeField] private float shockInterval = 7f;
    [SerializeField] private float shockRadius = 7f;
    [SerializeField] private float shockDamage = 18f;
    [SerializeField] private float strikeInterval = 4f;
    [SerializeField] private float strikeRadius = 3f;
    [SerializeField] private float strikeDamage = 25f;
    [SerializeField] private float telegraphSeconds = 1.1f;
    [SerializeField] private float engageRange = 28f;

    private struct Strike
    {
        public Vector3 position;
        public float time, radius, damage;
    }

    private DemonEnemy enemy;
    private Damageable health;
    private PlayerStats player;
    private float baseSpeed, baseDamage, baseCooldown;
    private float nextSpecial;
    private readonly List<Strike> strikes = new List<Strike>();
    private Renderer[] renderers;
    private MaterialPropertyBlock block;

    void Start()
    {
        enemy = GetComponent<DemonEnemy>();
        health = GetComponent<Damageable>();
        baseSpeed = enemy.moveSpeed;
        baseDamage = enemy.attackDamage;
        baseCooldown = enemy.attackCooldown;
        nextSpecial = Time.time + 5f;
    }

    void Update()
    {
        if (health == null || health.IsDead) return;

        float fraction = health.Health / Mathf.Max(1f, health.maxHealth);
        int target = fraction <= phase3At ? 3 : fraction <= phase2At ? 2 : 1;
        if (target > Phase) EnterPhase(target);

        ResolveStrikes();

        if (Phase < 2 || Time.time < nextSpecial) return;
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        if (player == null || player.IsDead) return;

        Vector3 toPlayer = player.transform.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.magnitude > engageRange) return;

        if (Phase == 2)
        {
            // Šokový výboj okolo bossa
            strikes.Add(new Strike { position = transform.position, time = Time.time + telegraphSeconds, radius = shockRadius, damage = shockDamage });
            Vfx.Disc(transform.position + Vector3.up * 0.1f, shockRadius, new Color(1f, 0.9f, 0.3f, 0.5f), telegraphSeconds);
            nextSpecial = Time.time + shockInterval;
        }
        else
        {
            // Blesk na miesto, kde hráč práve stojí
            Vector3 at = player.transform.position;
            strikes.Add(new Strike { position = at, time = Time.time + telegraphSeconds, radius = strikeRadius, damage = strikeDamage });
            Vfx.Disc(new Vector3(at.x, at.y - 0.8f, at.z), strikeRadius, new Color(0.4f, 0.8f, 1f, 0.6f), telegraphSeconds);
            nextSpecial = Time.time + strikeInterval;
        }
    }

    void EnterPhase(int phase)
    {
        Phase = phase;
        enemy.Stun(1.8f);

        // Ďalšie fázy: rýchlejší, silnejší a rýchlejšie útočí
        float speedFactor = phase == 2 ? 1.2f : 1.4f;
        float damageFactor = phase == 2 ? 1.2f : 1.45f;
        float cooldownFactor = phase == 2 ? 0.85f : 0.7f;
        enemy.moveSpeed = baseSpeed * speedFactor;
        enemy.attackDamage = baseDamage * damageFactor;
        enemy.attackCooldown = baseCooldown * cooldownFactor;

        Vfx.Disc(transform.position + Vector3.up * 0.1f, 9f, new Color(0.4f, 0.8f, 1f, 0.6f), 0.9f);
        GameAudio.Play(GameAudio.Sfx.Lightning, transform.position);
        GameHUD.Message(phase == 2
            ? $"{health.displayName} odhodil plášť! Šokové výboje."
            : $"{health.displayName} sa rozzúril: blesky dopadajú tam, kde stojíš!", 4f);

        BoostGlow(phase == 2 ? 1.6f : 3f);
        nextSpecial = Time.time + 3f;
    }

    // Plášť padá: model žiari silnejšie (emisia modrého blesku), v tretej fáze ešte viac
    void BoostGlow(float intensity)
    {
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
        if (block == null) block = new MaterialPropertyBlock();

        block.SetColor("_EmissionColor", new Color(0.2f, 0.55f, 1f) * intensity);
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            r.SetPropertyBlock(block);
        }
    }

    void ResolveStrikes()
    {
        for (int i = strikes.Count - 1; i >= 0; i--)
        {
            Strike s = strikes[i];
            if (Time.time < s.time) continue;
            strikes.RemoveAt(i);

            CombatFx.Lightning(s.position + Vector3.up * 14f, s.position, new Color(0.7f, 0.9f, 1f), 0.25f, 0.3f);
            GameAudio.Play(GameAudio.Sfx.Lightning, s.position);

            if (player == null || player.IsDead) continue;
            Vector3 d = player.transform.position - s.position;
            d.y = 0f;
            if (d.magnitude <= s.radius) player.TakeDamage(s.damage * WeatherSystem.LightningFactor, DamageSource.Demon, enemy.clan);
        }
    }
}
