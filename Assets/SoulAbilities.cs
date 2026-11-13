using UnityEngine;

/// <summary>
/// Aktívne schopnosti a údernové efekty démonickej vetvy výskumného stromu (SoulBalance):
/// stredné tlačidlo myši = Blesková reťaz (3 ciele + omráčenie), kláves 6 = Hnev Arakaniho (blesková anomália s tlakovými vlnami),
/// a pasívne efekty po údere (statický výboj, Temná vitalita, tlaková vlna v Hneve).
/// </summary>
public class SoulAbilities : MonoBehaviour
{
    public static SoulAbilities Instance { get; private set; }

    [SerializeField] private KeyCode wrathKey = KeyCode.Alpha6;
    [SerializeField] private float chainStamina = 25f, chainCooldown = 6f, chainRange = 14f, chainJump = 9f, chainStun = 1.8f;
    [SerializeField] private float wrathBlood = 40f, wrathSeconds = 12f, wrathCooldown = 90f, pulseInterval = 0.7f, pulseRadius = 6f;

    private static readonly Collider[] hits = new Collider[24];

    private PlayerStats stats;
    private PlayerCombat combat;
    private float chainReady, wrathReady, wrathUntil, nextPulse;

    public bool WrathActive => Time.time < wrathUntil;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (stats == null)
        {
            stats = GetComponent<PlayerStats>();
            combat = GetComponent<PlayerCombat>();
            return;
        }
        if (stats.IsDead || !stats.IsDemon || SoulMenu.MenuOpen || LairMenu.MenuOpen || Cursor.lockState != CursorLockMode.Locked) return;

        if (SoulBalance.CanChainLightning && GameInput.GetMouseButtonDown(2)) ChainLightning();
        if (SoulBalance.CanWrath && GameInput.GetKeyDown(wrathKey)) StartWrath();

        if (WrathActive && Time.time >= nextPulse)
        {
            nextPulse = Time.time + pulseInterval;
            Pulse();
        }
    }

    float BaseDamage => combat != null ? combat.baseDamage : 20f;

    // Blesk hráča: základ × forma × počasie / Búrkové srdce
    float LightningDamage(float multiplier) => BaseDamage * stats.DamageMultiplier * multiplier * WeatherSystem.PlayerLightningFactor;

    void ChainLightning()
    {
        if (Time.time < chainReady) { GameHUD.Message("Blesková reťaz sa ešte nabíja.", 1.5f); return; }

        Damageable current = NearestEnemy(transform.position, chainRange, null);
        if (current == null) { GameHUD.Message("Žiadny cieľ v dosahu.", 1.5f); return; }
        if (!stats.UseStamina(chainStamina)) { GameHUD.Message("Nemáš dosť výdrže.", 1.5f); return; }

        chainReady = Time.time + chainCooldown;
        Color bolt = new Color(0.6f, 0.85f, 1f);
        var visited = new System.Collections.Generic.HashSet<Damageable>();
        Vector3 from = transform.position + Vector3.up * 1.2f;

        for (int i = 0; i < 3 && current != null; i++)
        {
            visited.Add(current);
            Vector3 to = current.transform.position + Vector3.up * 1f;
            CombatFx.Lightning(from, to, bolt, 0.13f);
            Strike(current, LightningDamage(0.8f), chainStun);

            from = to;
            current = NearestEnemy(current.transform.position, chainJump, visited);
        }
        GameAudio.Play(GameAudio.Sfx.Lightning, transform.position);
    }

    void StartWrath()
    {
        if (WrathActive) return;
        if (Time.time < wrathReady) { GameHUD.Message($"Hnev Arakaniho sa nabíja ({wrathReady - Time.time:0} s).", 1.5f); return; }
        if (!stats.TrySpendBlood(wrathBlood)) { GameHUD.Message($"Hnev Arakaniho stojí {wrathBlood:0} krvi.", 2f); return; }

        wrathUntil = Time.time + wrathSeconds;
        wrathReady = wrathUntil + wrathCooldown;
        nextPulse = Time.time;
        stats.StartOverclock(wrathSeconds, 0.35f, 0.25f);
        GameHUD.Message("HNEV ARAKANIHO: si čistá bleskovú anomália!", 3f);
        GameAudio.Play(GameAudio.Sfx.Lightning, transform.position);
    }

    // Tlaková vlna okolo hráča
    void Pulse()
    {
        Vector3 center = transform.position;
        Vfx.Disc(center + Vector3.up * 0.1f, pulseRadius, new Color(0.5f, 0.8f, 1f, 0.5f), 0.4f);

        int n = Physics.OverlapSphereNonAlloc(center, pulseRadius, hits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Damageable d = hits[i].GetComponentInParent<Damageable>();
            if (d == null || !Targeting.IsEnemy(stats, d)) continue;
            Strike(d, LightningDamage(0.6f), 0f);
        }
    }

    void Strike(Damageable target, float damage, float stunSeconds)
    {
        HitResult r = target.TakeDamage(damage, gameObject, DamageKind.Lightning);
        if (r == HitResult.Immune || r == HitResult.AlreadyDead) return;

        GameHUD.WorldText(target.transform.position + Vector3.up * 2.2f, $"{damage:0}", new Color(0.6f, 0.85f, 1f));
        if (stunSeconds > 0f && r != HitResult.Killed)
        {
            DemonEnemy demon = target.GetComponent<DemonEnemy>();
            if (demon != null) demon.Stun(stunSeconds);
            CivilianAI civilian = target.GetComponent<CivilianAI>();
            if (civilian != null) civilian.Stun(stunSeconds);
        }
        if (r == HitResult.Killed) HealOnKill();
    }

    Damageable NearestEnemy(Vector3 origin, float range, System.Collections.Generic.HashSet<Damageable> exclude)
    {
        Damageable best = null;
        float bestDistance = range;
        foreach (Damageable d in FindObjectsByType<Damageable>())
        {
            if (!Targeting.IsEnemy(stats, d) || (exclude != null && exclude.Contains(d))) continue;
            float dist = Vector3.Distance(origin, d.transform.position);
            if (dist < bestDistance) { bestDistance = dist; best = d; }
        }
        return best;
    }

    void HealOnKill()
    {
        float fraction = SoulBalance.KillHealFraction;
        if (fraction > 0f) stats.Heal(stats.MaxHealth * fraction);
    }

    /// <summary>Volá PlayerCombat po zásahu: Temná vitalita, Bleskový náboj a tlaková vlna Hnevu pri údere.</summary>
    public static void AfterHit(PlayerStats player, Damageable target, float damage, HitResult result)
    {
        if (player == null || !player.IsDemon || Instance == null) return;

        if (result == HitResult.Killed) Instance.HealOnKill();

        if (SoulBalance.StaticCharge && Random.value < 0.35f)
        {
            Vector3 center = target.transform.position;
            Vfx.Disc(center + Vector3.up * 0.1f, 3.5f, new Color(0.6f, 0.85f, 1f, 0.5f), 0.3f);

            int n = Physics.OverlapSphereNonAlloc(center, 3.5f, hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Damageable d = hits[i].GetComponentInParent<Damageable>();
                if (d == null || d == target || !Targeting.IsEnemy(player, d)) continue;
                d.TakeDamage(damage * 0.5f * WeatherSystem.PlayerLightningFactor, Instance.gameObject, DamageKind.Lightning);
            }
        }

        if (Instance.WrathActive) Instance.Pulse();
    }
}
