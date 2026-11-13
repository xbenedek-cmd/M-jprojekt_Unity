using System;
using System.Collections.Generic;

public enum FactionId
{
    None,
    // Ľudské frakcie (hlad)
    Megacorp, Rebels, Civilians,
    // Démonické klany (krv)
    ShadowClan, WildClan, AshClan, BloodClan, LightningClan
}

/// <summary>
/// Bonusy / postihy ako priráhy (0.10 = +10 %, -0.20 = -20 %).
/// armor: +0.15 = prijímaš o 15 % menej poškodenia, -0.15 = o 15 % viac.
/// stamina: +0.15 = lacnejšia výdrž, -0.20 = drahšia.
/// </summary>
[Serializable]
public struct FactionMods
{
    public float damage;
    public float moveSpeed;
    public float armor;
    public float maxHp;
    public float stamina;
    public float crit;
    public float attackSpeed;
    public float lifesteal;
}

public enum AbilityKind
{
    None,
    EmpPulse, HologramDecoy, FirstAid,                        // ľudské frakcie
    CorporateStrike, Overclock, MobRally,                     // druhé schopnosti ľudí (kláves C)
    ShadowStep, FeralRush, FireBurst, BloodSurge, ChainLightning   // démonické klany
}

public class FactionProfile
{
    // Aktívna schopnosť (kláves X)
    public AbilityKind ability;
    public string abilityName;
    public string abilityText;
    public float abilityCooldown;
    public float abilityCost;
    public bool abilityCostIsBlood;

    // Druhá schopnosť (kláves C), platí sa výdržou
    public AbilityKind ability2;
    public string ability2Name;
    public string ability2Text;
    public float ability2Cooldown;
    public float ability2Cost;

    public FactionId id;
    public string name;
    public bool isDemonClan;
    public FactionMods stable;
    public FactionMods desperate;
    public string stableText;
    public string desperateText;
}

/// <summary>
/// Tabuľka frakcií a klanov podľa GDD. Percentá sú približné - dolaď ich tu.
/// Ľudia: stabilný stav pri hlade nad 50 %, núdzový pod 25 %.
/// Démoni: stabilný stav pri krvi nad 25 %, zúfalý pod 25 % po zvolení schopnosti klanu (kláves 4).
/// </summary>
public static class FactionData
{
    static readonly Dictionary<FactionId, FactionProfile> table = Build();

    public static readonly FactionId[] HumanFactions = { FactionId.Megacorp, FactionId.Rebels, FactionId.Civilians };

    public static bool IsResistance(FactionId id) => id == FactionId.Rebels;

    public static FactionProfile Get(FactionId id)
    {
        table.TryGetValue(id, out FactionProfile p);
        return p;
    }

    public static string NameOf(FactionId id)
    {
        FactionProfile p = Get(id);
        return p != null ? p.name : "Žiadna";
    }

    static Dictionary<FactionId, FactionProfile> Build()
    {
        var t = new Dictionary<FactionId, FactionProfile>();

        // ---------- ĽUDIA ----------
        Add(t, new FactionProfile
        {
            id = FactionId.Megacorp,
            name = "Vojenská Megakorporácia",
            isDemonClan = false,
            stable = new FactionMods { damage = 0.10f, armor = 0.15f },
            desperate = new FactionMods { damage = 0.30f, stamina = -0.20f, moveSpeed = -0.20f },
            stableText = "+10 % poškodenie, +15 % brnenie",
            desperateText = "Núdzový protokol: +30 % poškodenie, -20 % výdrž a rýchlosť"
        });
        Add(t, new FactionProfile
        {
            id = FactionId.Rebels,
            name = "Odboj (Resistance)",
            isDemonClan = false,
            stable = new FactionMods { attackSpeed = 0.10f, crit = 0.10f },
            desperate = new FactionMods { crit = 0.35f, maxHp = -0.25f, armor = -0.15f },
            stableText = "+10 % rýchlosť útoku, +10 % šanca na kritický zásah",
            desperateText = "Adrenalínový overclock: +35 % kritický zásah, -25 % max HP, +15 % prijaté poškodenie"
        });
        Add(t, new FactionProfile
        {
            id = FactionId.Civilians,
            name = "Civilisti",
            isDemonClan = false,
            stable = new FactionMods { stamina = 0.15f },
            desperate = new FactionMods { moveSpeed = 0.25f, damage = -0.20f, armor = -0.20f },
            stableText = "-15 % spotreba výdrže",
            desperateText = "Zúfalý boj o prežitie: +25 % rýchlosť, -20 % poškodenie a brnenie"
        });

        // ---------- DÉMONICKÉ KLANY ----------
        Add(t, new FactionProfile
        {
            id = FactionId.ShadowClan,
            name = "Klan Tieňa (Temní démoni)",
            isDemonClan = true,
            stable = new FactionMods { damage = 0.10f },
            desperate = new FactionMods { damage = 0.30f, maxHp = -0.10f, armor = -0.10f, moveSpeed = -0.10f },
            stableText = "+10 % poškodenie",
            desperateText = "+30 % poškodenie, neviditeľnosť 5 s, ostatné staty -10 %"
        });
        Add(t, new FactionProfile
        {
            id = FactionId.WildClan,
            name = "Klan Divočiny (Prírodní démoni)",
            isDemonClan = true,
            stable = new FactionMods { maxHp = 0.10f, armor = 0.10f },
            // V dokumente je rýchlosť -90 %, tu je -50 % (asi preklep). Uprav podľa potreby.
            desperate = new FactionMods { armor = 0.50f, moveSpeed = -0.50f, attackSpeed = -0.25f, crit = -0.10f },
            stableText = "+10 % max HP, +10 % brnenie",
            desperateText = "Kôra: +50 % brnenie, -50 % rýchlosť, -25 % rýchlosť útoku, -10 % kritický zásah"
        });
        Add(t, new FactionProfile
        {
            id = FactionId.AshClan,
            name = "Klan Popola (Ohniví démoni)",
            isDemonClan = true,
            stable = new FactionMods { damage = 0.10f },
            desperate = new FactionMods { damage = 0.30f, maxHp = -0.40f, armor = -0.40f, stamina = -0.40f },
            stableText = "+10 % poškodenie, zásahy podpália cieľ",
            desperateText = "+30 % poškodenie, zásahy podpália cieľ, ostatné staty -40 %"
        });
        Add(t, new FactionProfile
        {
            id = FactionId.BloodClan,
            name = "Klan Pekelnej Krvi (Krvaví démoni)",
            isDemonClan = true,
            stable = new FactionMods { lifesteal = 0.10f },
            desperate = new FactionMods { lifesteal = 0.30f, armor = -0.15f },
            stableText = "+10 % lifesteal",
            desperateText = "+30 % lifesteal, +0,2 krvi za úder, brnenie -15 %"
        });
        Add(t, new FactionProfile
        {
            id = FactionId.LightningClan,
            name = "Klan Blesku (Kyber-démoni)",
            isDemonClan = true,
            stable = new FactionMods { moveSpeed = 0.10f, attackSpeed = 0.10f },
            desperate = new FactionMods { attackSpeed = 0.30f, moveSpeed = 0.30f, maxHp = -0.20f, armor = -0.20f, stamina = -0.20f },
            stableText = "+10 % rýchlosť pohybu a útoku",
            desperateText = "+30 % rýchlosť útoku a šprintu, zásah preskočí na až 5 cieľov, ostatné staty -20 %"
        });

        // ---------- AKTÍVNE SCHOPNOSTI (kláves X) ----------
        SetAbility(t, FactionId.Megacorp, AbilityKind.EmpPulse, "EMP impulz",
            "Spomalí nepriateľov do 8 m o 65 % na 4 s a preruší ich úder", 15f, 25f, false);
        SetAbility(t, FactionId.Rebels, AbilityKind.HologramDecoy, "Holografický klam",
            "Prízrak na 5 s odláka nepriateľov od teba", 12f, 20f, false);
        SetAbility(t, FactionId.Civilians, AbilityKind.FirstAid, "Núdzová lekárnička",
            "Okamžite vylieči 30 % max HP", 25f, 10f, false);

        SetAbility(t, FactionId.ShadowClan, AbilityKind.ShadowStep, "Tieniaci skok",
            "Teleport za chrbát najbližšieho cieľa, tvoj ďalší zásah do 3 s je kritický", 8f, 8f, true);
        SetAbility(t, FactionId.WildClan, AbilityKind.FeralRush, "Krvavé šialenstvo",
            "Bleskový výpad vpred, zasiahnutí krvácajú 4 s", 6f, 8f, true);
        SetAbility(t, FactionId.AshClan, AbilityKind.FireBurst, "Ohnivý výbuch",
            "3 výbuchy v okolí 3 m, podpália nepriateľov (5 HP/s)", 14f, 12f, true);
        SetAbility(t, FactionId.BloodClan, AbilityKind.BloodSurge, "Krvavý závan",
            "Zraní všetkých do 4 m, polovicu poškodenia dostaneš ako HP, +2 krvi za cieľ", 12f, 6f, true);
        SetAbility(t, FactionId.LightningClan, AbilityKind.ChainLightning, "Reťazový blesk",
            "Blesk zasiahne najbližší cieľ a preskočí až na 5 ďalších", 10f, 8f, true);

        // ---------- DRUHÉ SCHOPNOSTI (kláves C) ----------
        SetAbility2(t, FactionId.Megacorp, AbilityKind.CorporateStrike, "Scannovací dron",
             "Dron preletí okolie, automaticky odhalí maskovaných démonov a označí ich (+25 % poškodenie)", 30f, 20f);
        SetAbility2(t, FactionId.Rebels, AbilityKind.Overclock, "Nelegálny overclock",
            "6 s: +40 % rýchlosť útoku, +25 % rýchlosť pohybu, stojí 15 HP", 20f, 10f);
        SetAbility2(t, FactionId.Civilians, AbilityKind.MobRally, "Sila komunity",
            "Civilisti do 15 m udrú najbližšieho démona (až 5 civilistov)", 30f, 15f);

        return t;
    }

    static void SetAbility(Dictionary<FactionId, FactionProfile> t, FactionId id, AbilityKind kind,
        string name, string text, float cooldown, float cost, bool costIsBlood)
    {
        FactionProfile p = t[id];
        p.ability = kind;
        p.abilityName = name;
        p.abilityText = text;
        p.abilityCooldown = cooldown;
        p.abilityCost = cost;
        p.abilityCostIsBlood = costIsBlood;
    }

    static void SetAbility2(Dictionary<FactionId, FactionProfile> t, FactionId id, AbilityKind kind,
        string name, string text, float cooldown, float cost)
    {
        FactionProfile p = t[id];
        p.ability2 = kind;
        p.ability2Name = name;
        p.ability2Text = text;
        p.ability2Cooldown = cooldown;
        p.ability2Cost = cost;
    }

    static void Add(Dictionary<FactionId, FactionProfile> t, FactionProfile p) => t[p.id] = p;
}
