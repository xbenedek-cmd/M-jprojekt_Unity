using System;
using UnityEngine;

/// <summary>
/// Vylepšenia zbrane z dielne odboja (WorkshopMenu): ostrenie čepele (+poškodenie), dlhšia rukoväť (+dosah) a bleskový nabíjač
/// (šanca na bleskový zásah, ktorý preskočí aj na blízkeho nepriateľa). Úrovne sa ukladajú cez WorldSave.
/// </summary>
public static class WeaponUpgrades
{
    public enum Kind { Sharpen, Handle, Lightning }

    public const int MaxLevel = 3;
    private const float DamagePerLevel = 4f;
    private const float RangePerLevel = 0.15f;
    private const float LightningChancePerLevel = 0.15f;

    private static readonly int[] levels = new int[3];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState() => Array.Clear(levels, 0, levels.Length);

    public static int Level(Kind kind) => levels[(int)kind];
    public static bool CanUpgrade(Kind kind) => levels[(int)kind] < MaxLevel;

    public static void Upgrade(Kind kind)
    {
        if (!CanUpgrade(kind)) return;
        levels[(int)kind]++;
        ApplyBonus(kind);
    }

    static void ApplyBonus(Kind kind)
    {
        PlayerCombat combat = UnityEngine.Object.FindAnyObjectByType<PlayerCombat>();
        if (combat == null) return;

        if (kind == Kind.Sharpen) combat.UpgradeWeapon(DamagePerLevel, 0f);
        else if (kind == Kind.Handle) combat.UpgradeWeapon(0f, RangePerLevel);
    }

    // ---------- Bleskový nabíjač ----------
    /// <summary>Volá PlayerCombat po každom zásahu: s určitou šancou zasiahne cieľ bleskom a ten preskočí na blízkeho nepriateľa.</summary>
    public static void OnHit(Damageable target, float damage, GameObject attacker)
    {
        int level = levels[(int)Kind.Lightning];
        if (level <= 0 || target == null || attacker == null) return;
        if (!Power.Online) return;   // nabíjač potrebuje prúd
        if (UnityEngine.Random.value >= LightningChancePerLevel * level) return;

        Vector3 from = attacker.transform.position + Vector3.up * 1.2f;
        target.TakeDamage(damage * 0.5f, attacker, DamageKind.Lightning);
        Vfx.Beam(from, target.transform.position + Vector3.up * 1f, new Color(1f, 0.95f, 0.4f), 0.15f, 0.1f);
        GameAudio.Play(GameAudio.Sfx.Lightning, target.transform.position);

        // Preskok na najbližšieho iného živého nepriateľa do 6 m
        Damageable next = null;
        float best = 36f;
        foreach (Damageable d in UnityEngine.Object.FindObjectsByType<Damageable>())
        {
            if (d == target || d.IsDead || d.gameObject == attacker || d.team == CombatTeam.Civilian) continue;
            float sqr = (d.transform.position - target.transform.position).sqrMagnitude;
            if (sqr < best) { best = sqr; next = d; }
        }

        if (next == null) return;
        next.TakeDamage(damage * 0.35f, attacker, DamageKind.Lightning);
        Vfx.Beam(target.transform.position + Vector3.up, next.transform.position + Vector3.up, new Color(1f, 0.95f, 0.4f), 0.15f, 0.08f);
    }

    // ---------- Ukladanie ----------
    public static int[] Snapshot() => (int[])levels.Clone();

    public static void Restore(int[] saved)
    {
        Array.Clear(levels, 0, levels.Length);
        if (saved == null) return;

        for (int k = 0; k < levels.Length && k < saved.Length; k++)
        {
            levels[k] = Mathf.Clamp(saved[k], 0, MaxLevel);
            for (int i = 0; i < levels[k]; i++) ApplyBonus((Kind)k);   // bonus zbrane sa po načítaní uplatní znova
        }
    }
}
