using UnityEngine;

/// <summary>Výskumný strom: 9 schopností ľudskej a 9 démonickej vetvy (3 stupne po 3). Poradie určuje vetvu a stupeň.</summary>
public enum SoulPerk
{
    // Ľudská vetva
    Apprentice, SteadyHand, Steward,                  // stupeň 1
    MasterBuilder, SurgicalCut, TradeInstinct,        // stupeň 2
    RebelEngineer, DefenseDoctrine, Cohesion,         // stupeň 3
    // Démonická vetva
    CrushingFists, DarkVitality, LightningCharge,     // stupeň 1
    Demolisher, AdrenalineSprint, ChainLightning,     // stupeň 2
    StormHeart, Unstoppable, ArakaniWrath             // stupeň 3
}

/// <summary>
/// Rozpoltená duša: "ľudskosť" (0–100) klesá, keď hráč dlho žije ako démon alebo ničí budovy, a pomaly sa vracia v ľudskej podobe.
/// Pod hranicou odmietnutia ho obchodníci nepredajú nič a civilisti sa ho boja (utekajú z mesta, pozri PopulationPressure).
/// Skill pointy sa investujú do ľudskej alebo démonickej vetvy výskumného stromu. Stupeň N stojí N bodov
/// a odomkne sa po dvoch schopnostiach predchádzajúceho stupňa v tej istej vetve.
/// </summary>
public static class SoulBalance
{
    public const float RejectBelow = 20f;
    private const float StartValue = 70f;
    public const int PerkCount = 18;

    private static float humanity = StartValue;
    private static readonly bool[] perks = new bool[PerkCount];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        humanity = StartValue;
        System.Array.Clear(perks, 0, perks.Length);
    }

    public static float Humanity => humanity;
    public static bool Rejected => humanity < RejectBelow;
    public static void Add(float delta) => humanity = Mathf.Clamp(humanity + delta, 0f, 100f);

    public static bool Has(SoulPerk p) => perks[(int)p];
    public static bool IsHumanBranch(SoulPerk p) => (int)p < 9;
    public static int Tier(SoulPerk p) => (int)p % 9 / 3 + 1;
    public static int Cost(SoulPerk p) => Tier(p);

    static int OwnedInTier(bool human, int tier)
    {
        int n = 0;
        for (int i = 0; i < PerkCount; i++)
            if (perks[i] && IsHumanBranch((SoulPerk)i) == human && Tier((SoulPerk)i) == tier) n++;
        return n;
    }

    /// <summary>Null, ak sa dá odomknúť; inak dôvod.</summary>
    public static string LockedReason(SoulPerk p)
    {
        if (Has(p)) return "odomknuté";
        int tier = Tier(p);
        if (tier > 1 && OwnedInTier(IsHumanBranch(p), tier - 1) < 2) return $"treba 2 schopnosti stupňa {tier - 1}";
        return null;
    }

    public static string Name(SoulPerk p)
    {
        switch (p)
        {
            case SoulPerk.Apprentice: return "Učeň remesiel";
            case SoulPerk.SteadyHand: return "Pevná ruka";
            case SoulPerk.Steward: return "Hospodárny správca";
            case SoulPerk.MasterBuilder: return "Majster staviteľ";
            case SoulPerk.SurgicalCut: return "Chirurgický rez";
            case SoulPerk.TradeInstinct: return "Obchodný inštinkt";
            case SoulPerk.RebelEngineer: return "Inžinier odboja";
            case SoulPerk.DefenseDoctrine: return "Obranná doktrína";
            case SoulPerk.Cohesion: return "Súdržnosť komunity";
            case SoulPerk.CrushingFists: return "Drvivé päste";
            case SoulPerk.DarkVitality: return "Temná vitalita";
            case SoulPerk.LightningCharge: return "Bleskový náboj";
            case SoulPerk.Demolisher: return "Ničiteľ štruktúr";
            case SoulPerk.AdrenalineSprint: return "Adrenalínový šprint";
            case SoulPerk.ChainLightning: return "Blesková reťaz";
            case SoulPerk.StormHeart: return "Búrkové srdce";
            case SoulPerk.Unstoppable: return "Nezastaviteľná sila";
            default: return "Hnev Arakaniho";
        }
    }

    public static string Description(SoulPerk p)
    {
        switch (p)
        {
            case SoulPerk.Apprentice: return "Stavby sa stavajú o 15 % rýchlejšie.";
            case SoulPerk.SteadyHand: return "+10 % šanca na kritický zásah.";
            case SoulPerk.Steward: return "+15 % surovín z ťaženia nástrojmi (drevo, kameň, šrot).";
            case SoulPerk.MasterBuilder: return "Budovy a veže vydržia o 25 % viac útokov nepriateľov.";
            case SoulPerk.SurgicalCut: return "Kritické zásahy (slabé miesta) spôsobia o 20 % viac poškodenia.";
            case SoulPerk.TradeInstinct: return "Obchodníci predávajú o 15 % lacnejšie, osada vynáša o 10 % viac.";
            case SoulPerk.RebelEngineer: return "Nástroje najvyššej úrovne (3) v dielni stoja o 35 % menej.";
            case SoulPerk.DefenseDoctrine: return "Veže a barikády strieľajú o 20 % rýchlejšie.";
            case SoulPerk.Cohesion: return "Dôvera povolaní civilistov rastie dvakrát rýchlejšie.";
            case SoulPerk.CrushingFists: return "Ako démon spôsobuješ o 20 % viac poškodenia.";
            case SoulPerk.DarkVitality: return "Zabitie nepriateľa alebo zničenie budovy ťa vylieči o 5 % zdravia.";
            case SoulPerk.LightningCharge: return "Údery majú 35 % šancu na statický výboj: 50 % poškodenia aj okolitým nepriateľom.";
            case SoulPerk.Demolisher: return "Búranie budov dáva o 50 % viac peňazí a skúseností.";
            case SoulPerk.AdrenalineSprint: return "Ako démon míňaš výdrž o 30 % pomalšie.";
            case SoulPerk.ChainLightning: return "Stredné tlačidlo myši: blesk preskočí na 3 ciele a omráči ich (výdrž 25, pauza 6 s).";
            case SoulPerk.StormHeart: return "Počas búrky alebo v Bleskovom kaňone sú tvoje blesky o 50 % silnejšie.";
            case SoulPerk.Unstoppable: return "Ako démon si imúnny voči omráčeniu (hráča zatiaľ nič neomráči, ochrana je pripravená).";
            default: return "Kláves 6: 12 s bleskovej anomálie, vyššia rýchlosť a sila, tlakové vlny okolo teba (krv 40, pauza 90 s).";
        }
    }

    public static int Points(bool human)
    {
        int n = 0;
        for (int i = 0; i < PerkCount; i++)
            if (perks[i] && IsHumanBranch((SoulPerk)i) == human) n += Cost((SoulPerk)i);
        return n;
    }

    /// <summary>Smer, ktorým sa hráč uberá (zatiaľ len popis; zakončenie hry z neho vyjde neskôr).</summary>
    public static string Ending
    {
        get
        {
            int h = Points(true), d = Points(false);
            if (h > d && humanity >= 50f) return "Strážca ľudí";
            if (d > h && humanity < 50f) return "Pán koreňov";
            return "Rozpoltená duša";
        }
    }

    public static bool Buy(SoulPerk p, PlayerStats stats)
    {
        if (stats == null || LockedReason(p) != null || stats.soulPoints < Cost(p)) return false;
        for (int i = 0; i < Cost(p); i++) stats.TrySpendSoulPoint();
        perks[(int)p] = true;
        return true;
    }

    // ---------- Ľudská vetva ----------
    public static float BuildTimeFactor => Has(SoulPerk.Apprentice) ? 0.85f : 1f;
    public static float CritBonus => Has(SoulPerk.SteadyHand) ? 0.1f : 0f;
    public static float GatherFactor => (Has(SoulPerk.Steward) ? 1.15f : 1f) * (1f + SkillTree.Sum(SkillStat.Gather));
    public static float StructureDamageFactor => Has(SoulPerk.MasterBuilder) ? 0.8f : 1f;
    public static float CritDamageFactor => Has(SoulPerk.SurgicalCut) ? 1.2f : 1f;
    public static float BuyFactor => Has(SoulPerk.TradeInstinct) ? 0.85f : 1f;
    public static float ProfitFactor => Has(SoulPerk.TradeInstinct) ? 1.1f : 1f;
    public static float ToolCostFactor => Has(SoulPerk.RebelEngineer) ? 0.65f : 1f;
    public static float DefenseIntervalFactor => Has(SoulPerk.DefenseDoctrine) ? 0.8f : 1f;
    public static float TrustGainFactor => Has(SoulPerk.Cohesion) ? 2f : 1f;

    // ---------- Démonická vetva ----------
    public static float DamageFactor(bool demon) => demon && Has(SoulPerk.CrushingFists) ? 1.2f : 1f;
    public static float KillHealFraction => Has(SoulPerk.DarkVitality) ? 0.05f : 0f;
    public static bool StaticCharge => Has(SoulPerk.LightningCharge);
    public static float RaidRewardFactor => Has(SoulPerk.Demolisher) ? 1.5f : 1f;
    public static float DemonStaminaFactor => Has(SoulPerk.AdrenalineSprint) ? 0.7f : 1f;
    public static bool CanChainLightning => Has(SoulPerk.ChainLightning);
    public static bool StunImmune => Has(SoulPerk.Unstoppable);
    public static bool CanWrath => Has(SoulPerk.ArakaniWrath);

    /// <summary>Búrkové srdce: blesky hráča ×1,5 počas búrky alebo v Bleskovom kaňone.</summary>
    public static float StormHeartFactor(bool stormOrCanyon) => Has(SoulPerk.StormHeart) && stormOrCanyon ? 1.5f : 1f;

    // ---------- Ukladanie ----------
    [System.Serializable]
    public class SaveData
    {
        public float humanity = StartValue;
        public bool[] perks;
    }

    public static SaveData Export() => new SaveData { humanity = humanity, perks = (bool[])perks.Clone() };

    public static void Import(SaveData data)
    {
        ResetState();
        if (data == null) return;
        humanity = Mathf.Clamp(data.humanity, 0f, 100f);
        if (data.perks != null && data.perks.Length == PerkCount) System.Array.Copy(data.perks, perks, PerkCount);   // starší strom (6 bodov) sa zahodí
    }
}
