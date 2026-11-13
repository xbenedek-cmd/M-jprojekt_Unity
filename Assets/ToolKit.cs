using UnityEngine;

/// <summary>
/// Nástroje hráča: sekera (stromy), krompáč (skaly) a kladivo (železné bloky a vraky). Každý druh má 4 typy od najslabšieho po najsilnejší
/// (farebne odlíšené), vlastnú životnosť (míňa sa údermi) a vylepšenia 0–3 (viac poškodenia aj životnosti). Hráč drží naraz jeden nástroj
/// od každého druhu; kúpou / výrobou lepšieho typu sa starý nahradí. Nástroje sa kupujú u obchodníka a vyrábajú, vylepšujú a opravujú
/// v dielni (WorkshopMenu). Použitie rieši BreakableNode. Stav sa ukladá cez WorldSave.
/// </summary>
public static class ToolKit
{
    public enum Kind { Axe, Pickaxe, Hammer }

    public const int Kinds = 3;
    public const int Tiers = 4;
    public const int MaxUpgrade = 3;

    private static readonly string[][] names =
    {
        new[] { "Drevená sekera", "Železná sekera", "Oceľová sekera", "Bleskotvorná sekera" },
        new[] { "Drevený krompáč", "Železný krompáč", "Oceľový krompáč", "Bleskotvorný krompáč" },
        new[] { "Kováčske kladivo", "Veľké kladivo", "Oceľové búracie kladivo", "Bleskotvorné búracie kladivo" }
    };

    /// <summary>Čo daný nástroj ničí (do hlášok).</summary>
    public static string Targets(Kind kind) => kind == Kind.Axe ? "Stromy" : kind == Kind.Pickaxe ? "Skaly" : "Železné bloky";
    public static string KindName(Kind kind) => kind == Kind.Axe ? "Sekera" : kind == Kind.Pickaxe ? "Krompáč" : "Kladivo";

    // Farby typov: drevo, železo, oceľ (modrá), blesk (žltá so žiarením)
    public static readonly Color[] TierColors =
    {
        new Color(0.55f, 0.38f, 0.2f), new Color(0.62f, 0.64f, 0.68f), new Color(0.3f, 0.55f, 0.95f), new Color(1f, 0.82f, 0.2f)
    };

    private static readonly float[] baseDamage = { 10f, 18f, 30f, 48f };
    private static readonly float[] baseDurability = { 60f, 110f, 180f, 300f };
    private static readonly int[] repairMoney = { 10, 20, 35, 60 };

    // Stav: typ (-1 = nemá), životnosť, vylepšenie
    private static readonly int[] tier = { -1, -1, -1 };
    private static readonly float[] durability = new float[Kinds];
    private static readonly int[] upgrade = new int[Kinds];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        for (int i = 0; i < Kinds; i++) { tier[i] = -1; durability[i] = 0f; upgrade[i] = 0; }
    }

    public static bool Has(Kind kind) => tier[(int)kind] >= 0;
    public static int Tier(Kind kind) => tier[(int)kind];
    public static int Upgrade(Kind kind) => upgrade[(int)kind];
    public static float Durability(Kind kind) => durability[(int)kind];
    public static string Name(Kind kind, int t) => names[(int)kind][Mathf.Clamp(t, 0, Tiers - 1)];
    public static string Name(Kind kind) => Has(kind) ? Name(kind, tier[(int)kind]) : "žiadny";
    public static Color ColorOf(int t) => TierColors[Mathf.Clamp(t, 0, Tiers - 1)];

    public static float Damage(Kind kind) => Has(kind) ? baseDamage[tier[(int)kind]] * (1f + 0.25f * upgrade[(int)kind]) : 0f;
    public static float MaxDurability(Kind kind) => Has(kind) ? baseDurability[tier[(int)kind]] * (1f + 0.3f * upgrade[(int)kind]) : 0f;
    public static float MaxDurability(int t, int up) => baseDurability[Mathf.Clamp(t, 0, Tiers - 1)] * (1f + 0.3f * up);
    public static float Damage(int t, int up) => baseDamage[Mathf.Clamp(t, 0, Tiers - 1)] * (1f + 0.25f * up);

    /// <summary>Nový nástroj daného typu (nahradí starý, vylepšenia sa vynulujú).</summary>
    public static void Give(Kind kind, int t)
    {
        int k = (int)kind;
        tier[k] = Mathf.Clamp(t, 0, Tiers - 1);
        upgrade[k] = 0;
        durability[k] = MaxDurability(kind);
    }

    public static bool CanUpgrade(Kind kind) => Has(kind) && upgrade[(int)kind] < MaxUpgrade;

    public static void DoUpgrade(Kind kind)
    {
        if (!CanUpgrade(kind)) return;
        float missing = MaxDurability(kind) - durability[(int)kind];
        upgrade[(int)kind]++;
        durability[(int)kind] = MaxDurability(kind) - missing;   // vylepšenie opravu nenahrádza
    }

    public static int RepairCost(Kind kind) => Has(kind) ? repairMoney[tier[(int)kind]] : 0;
    public static bool NeedsRepair(Kind kind) => Has(kind) && durability[(int)kind] < MaxDurability(kind) - 0.5f;
    public static void Repair(Kind kind) { if (Has(kind)) durability[(int)kind] = MaxDurability(kind); }

    /// <summary>Jeden úder nástrojom: ubudne životnosť, pri nule sa nástroj zlomí. Vráti poškodenie (0, ak nástroj nie je).</summary>
    public static float Use(Kind kind)
    {
        int k = (int)kind;
        if (tier[k] < 0) return 0f;

        float damage = Damage(kind);
        durability[k] -= 1f;
        if (durability[k] <= 0f)
        {
            GameHUD.Message($"{Name(kind)} sa zlomil(a)!", 3f);
            tier[k] = -1;
            upgrade[k] = 0;
            durability[k] = 0f;
        }
        else if (durability[k] <= 10f && Mathf.Approximately(durability[k] % 5f, 0f))
            GameHUD.Message($"{Name(kind)}: životnosť {durability[k]:0}", 1.5f);
        return damage;
    }

    // ---------- Ukladanie (po trojiciach: typ, životnosť, vylepšenie; staré uloženia mali len sekeru a krompáč) ----------
    public static float[] Snapshot()
    {
        var data = new float[Kinds * 3];
        for (int k = 0; k < Kinds; k++)
        {
            data[k * 3] = tier[k];
            data[k * 3 + 1] = durability[k];
            data[k * 3 + 2] = upgrade[k];
        }
        return data;
    }

    public static void Restore(float[] saved)
    {
        ResetState();
        if (saved == null) return;

        for (int k = 0; k < Kinds && k * 3 + 2 < saved.Length; k++)
        {
            tier[k] = Mathf.Clamp((int)saved[k * 3], -1, Tiers - 1);
            durability[k] = Mathf.Max(0f, saved[k * 3 + 1]);
            upgrade[k] = Mathf.Clamp((int)saved[k * 3 + 2], 0, MaxUpgrade);
            if (tier[k] < 0) { durability[k] = 0f; upgrade[k] = 0; }
        }
    }

    // ---------- Použitie ----------
    private static float nextHint;

    /// <summary>Je pred hráčom objekt, ktorý sa dá práve teraz nástrojom poškodiť (správny druh a dosť silný typ)?</summary>
    public static bool WouldUse(Transform player, Vector3 aim, float reach = 2.4f)
    {
        BreakableNode node = BreakableNode.Nearest(player.position, aim, reach);
        return node != null && Has(node.Requires) && tier[(int)node.Requires] >= node.MinTier;
    }

    /// <summary>
    /// Úder hráča (ľavé tlačidlo): ak je pred ním zničiteľný objekt (strom / skala / železný blok) a hráč má správny nástroj, objekt sa poškodí.
    /// Bez nástroja sa len raz za chvíľu pripomenie, čo treba. Vráti true, ak sa udrelo do objektu.
    /// </summary>
    public static bool TryUse(Transform player, Vector3 aim, float reach = 2.4f)
    {
        BreakableNode node = BreakableNode.Nearest(player.position, aim, reach);
        if (node == null) return false;

        Kind kind = node.Requires;
        if (!Has(kind))
        {
            if (Time.time >= nextHint)
            {
                nextHint = Time.time + 4f;
                GameHUD.Message($"{Targets(kind)} sa dajú ničiť len nástrojom: {KindName(kind).ToLowerInvariant()} (obchodník alebo dielňa odboja).", 3f);
            }
            return false;
        }

        if (tier[(int)kind] < node.MinTier)
        {
            if (Time.time >= nextHint)
            {
                nextHint = Time.time + 4f;
                GameHUD.Message($"{node.Label}: potrebuješ aspoň {Name(kind, node.MinTier).ToLowerInvariant()}.", 3f);
            }
            return false;
        }

        int heldTier = tier[(int)kind];   // pred Use: pri zlomení sa typ vynuluje
        float damage = Use(kind);
        HeldToolVisual.Show(player, kind, heldTier, 5f);
        node.Hit(damage, player);
        return true;
    }
}
