using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Príjem osady a špecialisti v budovách. Každá postavená budova denne prináša peniaze (tým viac, čím viac civilistov v meste žije).
/// Civilistu s povolaním (dôvera aspoň MinTrust) možno priradiť do budovy: správne povolanie prináša ×1,6, iné ×1,2.
/// Navyše: lekár v zdravotnom stredisku lieči hráča v okolí, inžinier v dielni zlacňuje výrobu.
/// </summary>
public static class SettlementEconomy
{
    public const float MinTrust = 15f;
    public const float ClinicHealRange = 45f;

    private static readonly Dictionary<string, Profession> assigned = new Dictionary<string, Profession>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState() => assigned.Clear();

    public static Profession Assigned(string plotId) => assigned.TryGetValue(plotId, out Profession p) ? p : Profession.None;

    public static bool Matches(BuildingDef def, Profession p)
    {
        if (def == null) return false;
        switch (p)
        {
            case Profession.Engineer: return def.id == BuildingCatalog.RebelWorkshop || def.id == BuildingCatalog.CorpTower || def.id == BuildingCatalog.CorpDepot;
            case Profession.Electrician: return def.id == BuildingCatalog.RebelSolar;
            case Profession.Cook: return def.id == BuildingCatalog.CivilianShop || def.id == BuildingCatalog.CivilianKitchen;
            case Profession.Doctor: return def.id == BuildingCatalog.CivilianClinic;
            default: return false;
        }
    }

    /// <summary>Priradí povolanie do budovy (povolanie sa môže naraz venovať len jednej budove).</summary>
    public static bool Assign(BuildPlot plot, Profession p, out string message)
    {
        if (plot.State != PlotState.Built) { message = "Budova nie je postavená."; return false; }
        if (ProfessionTrust.Get(p) < MinTrust) { message = $"{ProfessionTrust.Name(p)} ti ešte neverí (treba {MinTrust:0} dôvery, pomôž mu klávesom L)."; return false; }

        string previous = null;
        foreach (KeyValuePair<string, Profession> pair in assigned)
            if (pair.Value == p) { previous = pair.Key; break; }
        if (previous != null) assigned.Remove(previous);

        assigned[plot.PlotId] = p;
        message = $"{ProfessionTrust.Name(p)} pracuje v: {plot.Building.displayName}.";
        return true;
    }

    public static void Unassign(string plotId) => assigned.Remove(plotId);

    static float Occupancy()
    {
        float civilians = CityPopulation.HasZones ? CityPopulation.Count(PopulationGroup.Civilians) : CityPopulation.AliveCivilians();
        float housing = 6f;
        foreach (BuildPlot plot in BuildPlot.All)
            if (plot.State == PlotState.Built && plot.Building != null && plot.Building.housingFor == PopulationGroup.Civilians) housing += plot.Building.housing;
        return Mathf.Clamp(0.4f + 0.6f * civilians / Mathf.Max(1f, housing), 0.4f, 1.2f);
    }

    public static float Profit(BuildPlot plot) => Profit(plot, Occupancy());

    static float Profit(BuildPlot plot, float occupancy)
    {
        BuildingDef def = plot.Building;
        if (plot.State != PlotState.Built || def == null) return 0f;

        float profit = def.cost * 0.05f + def.housing * 3f;
        if (def.defenseRange > 0f) profit *= 0.3f;   // obranné stavby neprinášajú veľa
        if (Power.PlantBuilt && !Power.Online && def.housing == 0) profit *= 0.6f;   // bez prúdu

        Profession worker = Assigned(plot.PlotId);
        if (worker != Profession.None) profit *= Matches(def, worker) ? 1.6f : 1.2f;
        return profit * occupancy * SoulBalance.ProfitFactor * CityManagement.ZoneFactor(plot);
    }

    public static int DailyIncome()
    {
        float occupancy = Occupancy(), sum = 0f;
        foreach (BuildPlot plot in BuildPlot.All) sum += Profit(plot, occupancy);
        return Mathf.RoundToInt(sum);
    }

    static bool HasActive(Profession p, string buildingId)
    {
        foreach (KeyValuePair<string, Profession> pair in assigned)
        {
            if (pair.Value != p) continue;
            foreach (BuildPlot plot in BuildPlot.All)
                if (plot.PlotId == pair.Key && plot.State == PlotState.Built && plot.Building != null && plot.Building.id == buildingId) return true;
        }
        return false;
    }

    /// <summary>Zľava na výrobu v dielni (0..1), ak v nej pracuje inžinier.</summary>
    public static float CraftDiscount => HasActive(Profession.Engineer, BuildingCatalog.RebelWorkshop) ? 0.25f : 0f;
    public static int Discounted(int cost) => Mathf.CeilToInt(cost * (1f - CraftDiscount));

    /// <summary>Lekár v zdravotnom stredisku blízko hráča.</summary>
    public static bool ClinicHealing(Vector3 playerPosition)
    {
        if (!HasActive(Profession.Doctor, BuildingCatalog.CivilianClinic)) return false;
        if (Power.PlantBuilt && !Power.Online) return false;
        foreach (BuildPlot plot in BuildPlot.All)
            if (plot.State == PlotState.Built && plot.Building != null && plot.Building.id == BuildingCatalog.CivilianClinic
                && (plot.transform.position - playerPosition).sqrMagnitude < ClinicHealRange * ClinicHealRange) return true;
        return false;
    }

    // ---------- Ukladanie (spolu s dušou) ----------
    [System.Serializable]
    class Extra
    {
        public SoulBalance.SaveData soul;
        public FarmSystem.SaveData farm;
        public StoryCast.SaveData cast;
        public SettlementGatherers.SaveData gatherers;
        public CityManagement.SaveData city;
        public Story.SaveData story;
        public ConvoySystem.SaveData convoy;
        public Lair.SaveData lair;
        public float power;
        public int specials;
        public List<float> buyerPrices = new List<float>();
        public List<string> plotIds = new List<string>();
        public List<int> professions = new List<int>();
    }

    public static string ExportAll()
    {
        var data = new Extra { soul = SoulBalance.Export(), gatherers = SettlementGatherers.Export(), city = CityManagement.Export(), story = Story.Export(), convoy = ConvoySystem.Export(), lair = Lair.Export(), cast = StoryCast.Export(), farm = FarmSystem.Export(), power = Power.Export(), specials = SpecialGoods.Export(), buyerPrices = Buyer.Export() };
        foreach (KeyValuePair<string, Profession> pair in assigned)
        {
            data.plotIds.Add(pair.Key);
            data.professions.Add((int)pair.Value);
        }
        return JsonUtility.ToJson(data);
    }

    public static void ImportAll(string json)
    {
        assigned.Clear();
        if (string.IsNullOrEmpty(json)) { SoulBalance.Import(null); SettlementGatherers.Import(null); CityManagement.Import(null); Story.Import(null); ConvoySystem.Import(null); Lair.Import(null); Power.Import(0f); SpecialGoods.Import(0); Buyer.Import(null); StoryCast.Import(null); FarmSystem.Import(null); return; }

        Extra data = JsonUtility.FromJson<Extra>(json);
        SoulBalance.Import(data != null ? data.soul : null);
        SettlementGatherers.Import(data != null ? data.gatherers : null);
        CityManagement.Import(data != null ? data.city : null);
        Story.Import(data != null ? data.story : null);
        ConvoySystem.Import(data != null ? data.convoy : null);
        Lair.Import(data != null ? data.lair : null);
        Power.Import(data != null ? data.power : 0f);
        SpecialGoods.Import(data != null ? data.specials : 0);
        Buyer.Import(data != null ? data.buyerPrices : null);
        StoryCast.Import(data != null ? data.cast : null);
        FarmSystem.Import(data != null ? data.farm : null);
        if (data == null) return;
        for (int i = 0; i < data.plotIds.Count && i < data.professions.Count; i++) assigned[data.plotIds[i]] = (Profession)data.professions[i];
    }
}
