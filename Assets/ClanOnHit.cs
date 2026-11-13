using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pasívne efekty klanov, ktoré sa spustia pri zásahu základným útokom (volá ich PlayerCombat).
/// Klan Popola: zásah podpáli cieľ. Klan Pekelnej Krvi: v zúfalom stave +krv za úder.
/// Klan Blesku: v zúfalom stave zásah preskočí na ďalšie ciele.
/// Klan Tieňa: vstup do zúfalého stavu = neviditeľnosť (DemonEnemy ťa nevidí).
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class ClanOnHit : MonoBehaviour
{
    [Header("Klan Popola")]
    public float burnSeconds = 3f;
    [Tooltip("Podiel zásahu, ktorý za sekundu spáli (0.1 = 10 %)")]
    public float burnFractionPerSecond = 0.10f;

    [Header("Klan Pekelnej Krvi (zúfalý stav)")]
    public float bloodPerHit = 0.2f;

    [Header("Klan Blesku (zúfalý stav)")]
    public int chainTargets = 5;
    public float chainRange = 6f;

    [Header("Klan Tieňa (zúfalý stav)")]
    public float invisibilitySeconds = 5f;
    public float invisibilityCooldown = 30f;

    private PlayerStats stats;
    private PlayerFaction faction;
    private FactionState lastState;
    private float nextInvisibility;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        faction = GetComponent<PlayerFaction>();
    }

    void Update()
    {
        if (stats == null || faction == null) return;

        // Vstup do zúfalého stavu Klanu Tieňa = neviditeľnosť
        FactionState s = faction.state;
        if (s != lastState)
        {
            lastState = s;

            if (s == FactionState.Desperate && faction.Current == FactionId.ShadowClan
                && stats.IsDemon && Time.time >= nextInvisibility)
            {
                stats.MakeInvisible(invisibilitySeconds);
                nextInvisibility = Time.time + invisibilityCooldown;
                GameHUD.Message($"Neviditeľnosť na {invisibilitySeconds:0} s");
            }
        }
    }

    /// <summary>Zavolá PlayerCombat po úspešnom zásahu.</summary>
    public void OnHit(Damageable target, float damage)
    {
        if (stats == null || faction == null || target == null) return;
        if (!stats.IsDemon || faction.state == FactionState.None) return;

        bool desperate = faction.state == FactionState.Desperate;

        switch (faction.Current)
        {
            case FactionId.AshClan:
                target.ApplyDot(Mathf.Max(1f, damage * burnFractionPerSecond), burnSeconds, gameObject, DamageKind.Fire);
                break;

            case FactionId.BloodClan:
                if (desperate) stats.DrinkBlood(bloodPerHit);
                break;

            case FactionId.LightningClan:
                if (desperate) Chain(target, damage);
                break;
        }
    }

    // Blesk preskočí z cieľa na najbližšie ďalšie (sila útoku sa nemení)
    void Chain(Damageable first, float damage)
    {
        Color bolt = new Color(0.6f, 0.85f, 1f);
        var visited = new HashSet<Damageable> { first };
        Damageable current = first;

        for (int i = 0; i < chainTargets; i++)
        {
            Damageable next = NearestTo(current.transform.position, chainRange, visited);
            if (next == null) break;
            visited.Add(next);

            CombatFx.Lightning(current.transform.position + Vector3.up * 0.5f,
                     next.transform.position + Vector3.up * 0.5f, bolt);

            HitResult r = next.TakeDamage(damage, gameObject, DamageKind.Lightning);
            Vector3 pos = next.transform.position + Vector3.up * 2.2f;
            if (r == HitResult.Immune) GameHUD.WorldText(pos, "Nezraniteľný!", Color.gray);
            else GameHUD.WorldText(pos, $"{damage:0}", bolt);

            current = next;
        }
    }

    Damageable NearestTo(Vector3 origin, float maxDist, HashSet<Damageable> exclude)
    {
        Damageable best = null;
        float bestDist = maxDist;

        foreach (Damageable d in FindObjectsByType<Damageable>())
        {
            if (!Targeting.IsEnemy(stats, d) || exclude.Contains(d)) continue;

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
