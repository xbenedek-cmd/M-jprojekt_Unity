using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Aktívna schopnosť frakcie / klanu na jednu klávesu (X) s cooldownom a cenou.
/// Ľudia platia výdržou, démoni krvou (krv je démonická mana).
/// Dáta schopností (názov, cooldown, cena) sú v FactionData.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class FactionAbility : MonoBehaviour
{
    public KeyCode abilityKey => KeyBindings.Get("ability1", KeyCode.X);
    public KeyCode abilityKey2 => KeyBindings.Get("ability2", KeyCode.C);
    [Header("Ovládanie")]

    public FactionProfile Profile => faction != null ? FactionData.Get(faction.Current) : null;
    public float CooldownLeft => Mathf.Max(0f, cooldownEnd - Time.time);
    public float CooldownTotal { get; private set; }
    public float Cooldown2Left => Mathf.Max(0f, cooldown2End - Time.time);
    public float Cooldown2Total { get; private set; }

    /// <summary>Druhá schopnosť (C) existuje len u ľudských frakcií a platí len v ľudskej forme.</summary>
    public bool HasSecond
    {
        get { FactionProfile p = Profile; return p != null && p.ability2 != AbilityKind.None; }
    }

    /// <summary>Schopnosť sa dá použiť len v forme, ktorá patrí frakcii (človek = ľudská frakcia, démon = klan).</summary>
    public bool FormMatches
    {
        get
        {
            FactionProfile p = Profile;
            return stats != null && p != null && p.ability != AbilityKind.None && p.isDemonClan == stats.IsDemon;
        }
    }

    private PlayerStats stats;
    private PlayerFaction faction;
    private PlayerMovement movement;
    private PlayerCombat combat;
    private float cooldownEnd;
    private float cooldown2End;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        faction = GetComponent<PlayerFaction>();
        movement = GetComponent<PlayerMovement>();
        combat = GetComponent<PlayerCombat>();
    }

    void Update()
    {
        if (stats == null || stats.IsDead || Healer.MenuOpen) return;

        // Odmena z questu „Šepot v Dymových Horách“ (cesta Arakaniho): pasívny Tieniaci skok na klávese Z
        if (Quest2.ShadowStepUnlocked && GameInput.GetKeyDown(KeyBindings.Get("shadowstep", KeyCode.Z)) && !CombatQte.Active)
            TryPassiveShadowStep();

        if (GameInput.GetKeyDown(abilityKey))
            TryUse(false);
        else if (GameInput.GetKeyDown(abilityKey2))
            TryUse(true);
    }

    private float passiveStepReadyAt;

    void TryPassiveShadowStep()
    {
        if (Time.time < passiveStepReadyAt) { GameHUD.Message($"Tieniaci skok: ešte {passiveStepReadyAt - Time.time:0.0} s"); return; }
        if (stats.Stamina < 20f) { GameHUD.Message("Máš málo výdrže."); return; }
        if (!ShadowStep()) return;   // bez cieľa sa nič neminie
        stats.UseStamina(20f);
        passiveStepReadyAt = Time.time + 12f;
    }

    void TryUse(bool second)
    {
        FactionProfile p = Profile;
        AbilityKind kind = p == null ? AbilityKind.None : (second ? p.ability2 : p.ability);

        if (kind == AbilityKind.None)
        {
            GameHUD.Message(second ? "Tvoja frakcia nemá druhú schopnosť." : "Tvoja frakcia nemá aktívnu schopnosť.");
            return;
        }
        if (p.isDemonClan != stats.IsDemon)
        {
            GameHUD.Message("Schopnosť tejto frakcie sa dá použiť len v jej forme.");
            return;
        }

        float left = second ? Cooldown2Left : CooldownLeft;
        string abilityName = second ? p.ability2Name : p.abilityName;
        float cost = second ? p.ability2Cost : p.abilityCost;
        bool costIsBlood = !second && p.abilityCostIsBlood;
        float cooldown = second ? p.ability2Cooldown : p.abilityCooldown;

        if (left > 0f)
        {
            GameHUD.Message($"{abilityName}: ešte {left:0.0} s");
            return;
        }

        // Zdroje (vyberie sa až po úspešnom použití)
        if (costIsBlood)
        {
            if (stats.Blood < cost) { GameHUD.Message("Máš málo krvi."); return; }
        }
        else if (stats.Stamina < cost)
        {
            GameHUD.Message("Máš málo výdrže.");
            return;
        }

        if (!Execute(kind)) return;
        OnAbilityUsed?.Invoke(kind, transform.position);

        if (costIsBlood) stats.TrySpendBlood(cost);
        else if (cost > 0f) stats.UseStamina(cost);

        if (second)
        {
            Cooldown2Total = cooldown;
            cooldown2End = Time.time + cooldown;
        }
        else
        {
            CooldownTotal = cooldown;
            cooldownEnd = Time.time + cooldown;
        }
    }

    // Vráti false, ak sa schopnosť nepoužila (napr. nie je cieľ) - vtedy sa nič neminie.
    /// <summary>Použitá schopnosť frakcie (bossovia reagujú na EMP a blesk).</summary>
    public static event System.Action<AbilityKind, Vector3> OnAbilityUsed;

    bool Execute(AbilityKind kind)
    {
        switch (kind)
        {
            case AbilityKind.EmpPulse: return EmpPulse();
            case AbilityKind.HologramDecoy: return Decoy();
            case AbilityKind.FirstAid: return FirstAid();
            case AbilityKind.ShadowStep: return ShadowStep();
            case AbilityKind.FeralRush: return FeralRush();
            case AbilityKind.FireBurst: return FireBurst();
            case AbilityKind.BloodSurge: return BloodSurge();
            case AbilityKind.ChainLightning: return ChainLightning();
            case AbilityKind.CorporateStrike: return CorporateStrike();
            case AbilityKind.Overclock: return Overclock();
            case AbilityKind.MobRally: return MobRally();
        }
        return false;
    }

    // ================= Megakorporácia: EMP impulz =================
    bool EmpPulse()
    {
        int n = 0;
        foreach (DemonEnemy e in FindObjectsByType<DemonEnemy>())
        {
            if (Vector3.Distance(e.transform.position, transform.position) > 8f) continue;
            e.ApplySlow(0.35f, 4f);
            n++;
        }

        CombatFx.Emp(transform.position + Vector3.down * 0.45f, 8f);
        GameHUD.Message(n > 0 ? $"EMP: spomalených nepriateľov: {n}" : "EMP: nikto v dosahu");
        return true;
    }

    // ================= Rebelanti: Holografický klam =================
    bool Decoy()
    {
        HologramDecoy.Spawn(transform.position + AimForward() * 1.5f, transform.rotation, 5f, transform);
        GameHUD.Message("Hologram odláka nepriateľov na 5 s");
        return true;
    }

    // ================= Civilisti: Núdzová lekárnička =================
    bool FirstAid()
    {
        float amount = stats.MaxHealth * 0.3f;
        stats.Heal(amount);
        GameHUD.WorldText(transform.position + Vector3.up * 2f, $"+{amount:0} HP", new Color(0.4f, 1f, 0.5f));
        return true;
    }

    // ================= Klan Tieňa: Tieniaci skok =================
    bool ShadowStep()
    {
        Damageable target = Nearest(12f, null);
        if (target == null)
        {
            GameHUD.Message("Žiadny cieľ v dosahu.");
            return false;
        }

        Vector3 dir = target.transform.position - transform.position;
        dir.y = 0f;
        dir.Normalize();

        Vector3 from = transform.position;
        Vector3 dest = target.transform.position + dir * 1.4f;     // za chrbtom cieľa
        dest.y = transform.position.y;

        Color purple = new Color(0.5f, 0.1f, 0.8f);

        if (movement != null) movement.Teleport(dest, -dir);
        else transform.position = dest;

        CombatFx.ShadowStep(from + Vector3.down * 0.45f, dest + Vector3.down * 0.45f);
        stats.ArmGuaranteedCrit(3f);
        GameHUD.WorldText(transform.position + Vector3.up * 2f, "Tieniaci skok!", purple);
        return true;
    }

    // ================= Klan Divočiny: Krvavé šialenstvo =================
    bool FeralRush()
    {
        Vector3 dir = AimForward();
        if (movement != null) movement.Dash(dir, 18f, 0.3f);
        CombatFx.FeralRush(transform);
        StartCoroutine(RushRoutine());
        return true;
    }

    IEnumerator RushRoutine()
    {
        var hit = new HashSet<Damageable>();
        float end = Time.time + 0.3f;

        while (Time.time < end)
        {
            foreach (Damageable d in TargetsIn(transform.position, 1.5f))
            {
                if (!hit.Add(d)) continue;
                Hit(d, AbilityDamage(1.2f), new Color(0.9f, 0.2f, 0.2f), DamageKind.Earth);
                d.ApplyDot(4f, 4f, gameObject, DamageKind.Earth);     // krvácanie
            }
            yield return new WaitForSeconds(0.03f);
        }
    }

    // ================= Klan Popola: Ohnivý výbuch =================
    bool FireBurst()
    {
        StartCoroutine(FireRoutine());
        return true;
    }

    IEnumerator FireRoutine()
    {
        for (int i = 0; i < 3; i++)
        {
            Vector3 c = transform.position;
            CombatFx.FireWave(c + Vector3.down * 0.45f, 3f, i);

            foreach (Damageable d in TargetsIn(c, 3f))
            {
                Hit(d, AbilityDamage(0.6f), new Color(1f, 0.6f, 0.2f), DamageKind.Fire);
                d.ApplyDot(5f, 4f, gameObject, DamageKind.Fire);     // horenie: 5 HP/s
            }
            yield return new WaitForSeconds(0.35f);
        }
    }

    // ================= Klan Pekelnej Krvi: Krvavý závan =================
    bool BloodSurge()
    {
        List<Damageable> targets = TargetsIn(transform.position, 4f);
        if (targets.Count == 0)
        {
            GameHUD.Message("Žiadny cieľ v dosahu.");
            return false;
        }

        CombatFx.HitSpark(transform.position, Vector3.up, new Color(0.9f, 0.05f, 0.15f), 0.6f);

        float total = 0f;
        foreach (Damageable d in targets)
        {
            float dmg = AbilityDamage(0.8f);
            total += dmg;
            Hit(d, dmg, new Color(0.9f, 0.1f, 0.2f), DamageKind.Blood);
            CombatFx.BloodOrbs(d.transform.position, transform, 4);
            stats.DrinkBlood(2f);
        }

        stats.Heal(total * 0.5f);
        return true;
    }

    // ================= Klan Blesku: Reťazový blesk =================
    bool ChainLightning()
    {
        Damageable first = Nearest(12f, null);
        if (first == null)
        {
            GameHUD.Message("Žiadny cieľ v dosahu.");
            return false;
        }

        Color bolt = new Color(0.6f, 0.85f, 1f);
        var visited = new HashSet<Damageable>();
        Vector3 from = transform.position + Vector3.up * 0.5f;
        Damageable current = first;

        // Prvý cieľ + až 5 ďalších, ktoré blesk preskočí
        for (int i = 0; i < 6 && current != null; i++)
        {
            visited.Add(current);
            Vector3 to = current.transform.position + Vector3.up * 0.5f;

            CombatFx.Lightning(from, to, bolt, i == 0 ? 0.14f : 0.1f);
            Hit(current, AbilityDamage(0.7f) * WeatherSystem.PlayerLightningFactor, bolt, DamageKind.Lightning);

            from = to;
            current = NearestTo(current.transform.position, 6f, visited);
        }
        return true;
    }

    // ================= Megakorporácia: Corporate Strike =================
    bool CorporateStrike()
    {
        CorpDrone.Spawn(transform.position);
        GameHUD.Message("Scannovací dron bol vyslaný.");
        return true;
    }

    // ================= Rebelanti: Nelegálny overclock =================
    bool Overclock()
    {
        const float hpCost = 15f;
        if (stats.Health <= hpCost + 5f)
        {
            GameHUD.Message("Máš príliš málo HP na overclock.");
            return false;
        }

        stats.TakeDamage(hpCost, DamageSource.Normal);
        stats.StartOverclock(6f, 0.4f, 0.25f);
        GameHUD.WorldText(transform.position + Vector3.up * 2f, "OVERCLOCK!", new Color(1f, 0.9f, 0.2f));
        return true;
    }

    // ================= Civilisti: Sila komunity =================
    bool MobRally()
    {
        int n = 0;
        Color c = new Color(1f, 0.9f, 0.4f);

        foreach (NPCPerception npc in FindObjectsByType<NPCPerception>())
        {
            if (n >= 5) break;
            if (npc.kind != NPCKind.Civilian) continue;

            Damageable civ = npc.GetComponent<Damageable>();
            if (civ == null || civ.IsDead) continue;
            if (Vector3.Distance(civ.transform.position, transform.position) > 15f) continue;

            Damageable enemy = NearestDemon(civ.transform.position, 12f);
            if (enemy == null) continue;

            Vfx.Beam(civ.transform.position + Vector3.up, enemy.transform.position + Vector3.up, c);
            Hit(enemy, 10f, c);
            n++;
        }

        if (n == 0)
        {
            GameHUD.Message("Žiadni civilisti nablízku nemajú na koho zaútočiť.");
            return false;
        }

        GameHUD.Message($"Sila komunity: zaútočilo civilistov: {n}");
        return true;
    }

    Damageable NearestDemon(Vector3 origin, float maxDist)
    {
        Damageable best = null;
        float bestDist = maxDist;

        foreach (Damageable d in FindObjectsByType<Damageable>())
        {
            if (d.IsDead || d.team != CombatTeam.Demon) continue;

            float dist = Vector3.Distance(origin, d.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = d;
            }
        }
        return best;
    }

    // ================= Pomocné =================
    Vector3 AimForward()
    {
        Camera cam = Camera.main;
        Vector3 f = cam != null ? cam.transform.forward : transform.forward;
        f.y = 0f;
        return f.sqrMagnitude < 0.01f ? transform.forward : f.normalized;
    }

    float AbilityDamage(float mult)
    {
        float baseDmg = combat != null ? combat.baseDamage : 20f;
        return baseDmg * stats.DamageMultiplier * mult;
    }

    void Hit(Damageable d, float dmg, Color color, DamageKind kind = DamageKind.Normal)
    {
        bool weak = d.immuneToNormalWeapons && d.IsWeakTo(kind);
        HitResult r = d.TakeDamage(dmg, gameObject, kind);
        Vector3 pos = d.transform.position + Vector3.up * 2.2f;

        if (r == HitResult.Blocked) GameHUD.WorldText(pos, "Blok!", Color.gray);
        else if (r == HitResult.Immune) GameHUD.WorldText(pos, "Nezraniteľný!", Color.gray);
        else if (weak) GameHUD.WorldText(pos, $"SLABINA! {dmg:0}", new Color(1f, 0.9f, 0.3f));
        else GameHUD.WorldText(pos, $"{dmg:0}", color);
    }

    List<Damageable> TargetsIn(Vector3 center, float radius)
    {
        var list = new List<Damageable>();
        var seen = new HashSet<Damageable>();

        foreach (Collider c in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            Damageable d = c.GetComponentInParent<Damageable>();
            if (d != null && Targeting.IsEnemy(stats, d) && seen.Add(d)) list.Add(d);
        }
        return list;
    }

    Damageable Nearest(float maxDist, HashSet<Damageable> exclude)
        => NearestTo(transform.position, maxDist, exclude);

    Damageable NearestTo(Vector3 origin, float maxDist, HashSet<Damageable> exclude)
    {
        Damageable best = null;
        float bestDist = maxDist;

        foreach (Damageable d in FindObjectsByType<Damageable>())
        {
            if (!Targeting.IsEnemy(stats, d) || (exclude != null && exclude.Contains(d))) continue;

            float dist = Vector3.Distance(origin, d.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = d;
            }
        }
        return best;
    }
}
