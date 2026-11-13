using System;
using System.Collections.Generic;
using UnityEngine;

public enum ZoneKind { None, Residential, Industrial, Defense, Commercial }

public enum Law { Rationing, WeaponBan, ForcedLabor, FreeHealth }

/// <summary>
/// Správa mesta: dane, zákony, plánované zóny parciel a civilné potreby.
///  - Spokojnosť (0–100) sa denne ťahá k cieľu podľa bývania, jedla, bezpečia a služieb mínus daňový a zákonný postih.
///  - Dane dávajú denný príjem (civilisti × sadzba × spokojnosť).
///  - Pod 35 sú protesty (žiadne dane, zberači nepracujú), pod 15 dva dni po sebe vzniká občianska vojna (ubúdajú civilisti aj vojaci).
///  - Pri nízkej spokojnosti sa slabí civilisti v noci menia na démonov; vyšší démon môže premeniť civilistu na nižšieho démona.
/// </summary>
public static class CityManagement
{
    public static readonly string[] TaxNames = { "Žiadne", "Nízke", "Štandardné", "Vysoké" };
    private static readonly int[] TaxPerCivilian = { 0, 1, 2, 4 };
    private static readonly float[] TaxPenalty = { 0f, 0f, 6f, 18f };

    public static int TaxLevel = 1;
    public static bool Curfew;          // zákaz vychádzania: bezpečie hore, spokojnosť dole
    public static bool Patrols;         // vojenské hliadky: bezpečie hore, trochu viac podozrenia
    public static float Satisfaction = 60f;
    public static int ProtestStreak;
    public static int CivilWarDays;
    public static bool Protesting;
    public static int SuppressedDays;   // potlačené protesty: pár dní pokoj, ale mesto to pamätá
    public static int Laws;             // bitová maska Law
    public static int BoostNeed = -1;   // 0 bývanie, 1 jedlo, 2 bezpečie, 3 služby (po splnenej žiadosti)
    public static int BoostDays;
    private static bool healthPaid;

    public static readonly string[] NeedNames = { "Bývanie", "Jedlo", "Bezpečie", "Služby" };

    public class Petition { public int need, daysLeft; }
    public static readonly List<Petition> Petitions = new List<Petition>();

    public static bool Has(Law law) => (Laws & (1 << (int)law)) != 0;
    public static void SetLaw(Law law, bool on) { if (on) Laws |= 1 << (int)law; else Laws &= ~(1 << (int)law); }

    public static float Housed, Fed, Safety, Services;   // posledné vypočítané potreby 0..1

    private static readonly Dictionary<string, ZoneKind> zones = new Dictionary<string, ZoneKind>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        TaxLevel = 1;
        Curfew = Patrols = Protesting = false;
        SuppressedDays = Laws = BoostDays = 0;
        BoostNeed = -1;
        healthPaid = false;
        Petitions.Clear();
        Satisfaction = 60f;
        ProtestStreak = CivilWarDays = 0;
        Housed = Fed = Safety = Services = 0.6f;
        zones.Clear();
    }

    // ---------- Zóny ----------
    public static ZoneKind ZoneOf(string plotId) => zones.TryGetValue(plotId, out ZoneKind k) ? k : ZoneKind.None;
    public static void SetZone(string plotId, ZoneKind kind) { if (kind == ZoneKind.None) zones.Remove(plotId); else zones[plotId] = kind; }

    public static ZoneKind CategoryOf(BuildingDef def)
    {
        if (def == null) return ZoneKind.None;
        if (def.housing > 0 && def.faction != FactionId.Megacorp) return ZoneKind.Residential;
        if (def.defenseRange > 0f || def.id == BuildingCatalog.CorpBarracks) return ZoneKind.Defense;
        if (def.id == BuildingCatalog.CivilianShop || def.id == BuildingCatalog.CivilianKitchen || def.id == BuildingCatalog.CorpDepot) return ZoneKind.Commercial;
        return ZoneKind.Industrial;
    }

    public static string KindName(ZoneKind k)
    {
        switch (k)
        {
            case ZoneKind.Residential: return "Obytná";
            case ZoneKind.Industrial: return "Priemyselná";
            case ZoneKind.Defense: return "Obranná";
            case ZoneKind.Commercial: return "Obchodná";
            default: return "Neurčená";
        }
    }

    /// <summary>Budova v správnej zóne vynáša o 15 % viac, v nesprávnej o 10 % menej.</summary>
    public static float ZoneFactor(BuildPlot plot)
    {
        ZoneKind zone = ZoneOf(plot.PlotId);
        if (zone == ZoneKind.None) return 1f;
        if (CategoryOf(plot.Building) != zone) return 0.9f;

        // Susedia v tej istej zóne (do 30 m) dávajú ďalší bonus: štvrte sa oplatí plánovať súvisle
        int neighbours = 0;
        foreach (BuildPlot other in BuildPlot.All)
        {
            if (other == plot || other.State != PlotState.Built || ZoneOf(other.PlotId) != zone) continue;
            if ((other.transform.position - plot.transform.position).sqrMagnitude <= 30f * 30f) neighbours++;
        }
        return 1.15f + 0.04f * Mathf.Min(4, neighbours);
    }

    // ---------- Potreby a denný vývoj ----------
    public static void Recalculate()
    {
        int civilians = Mathf.Max(1, CityPopulation.AliveCivilians());
        float housing = 6f, food = 6f, services = -0.04f * Mathf.Min(5, BuildPlot.ContaminatedCount), defense = 0f;

        foreach (BuildPlot plot in BuildPlot.All)
        {
            if (plot.State != PlotState.Built || plot.Building == null) continue;
            BuildingDef def = plot.Building;

            if (def.housingFor == PopulationGroup.Civilians) housing += def.housing;
            if (def.id == BuildingCatalog.CivilianKitchen) food += 10f;
            if (def.id == BuildingCatalog.Farm) food += 3f * FarmSystem.Staff(plot.PlotId);   // plne obsadené pole = takmer jedáleň
            if (def.id == BuildingCatalog.CivilianShop) { food += 6f; services += 0.25f; }
            if (def.id == BuildingCatalog.CivilianClinic) services += 0.3f;
            if (def.defenseRange > 0f || def.id == BuildingCatalog.CorpBarracks) defense += 0.08f;
            if (def.id == BuildingCatalog.CorpSecurity) defense += 0.15f;
        }

        Housed = Mathf.Clamp01(housing / civilians);
        Fed = Mathf.Clamp01(food / civilians * (Has(Law.Rationing) ? 1.35f : 1f));
        if (Power.PlantBuilt) services += Power.Online ? 0.15f : -0.15f;
        if (Has(Law.FreeHealth) && healthPaid) services += 0.15f;
        Services = Mathf.Clamp01(0.2f + services);
        Safety = Mathf.Clamp01(1f - Reputation.Suspicion / 125f + defense + (Curfew ? 0.12f : 0f) + (Patrols ? 0.1f : 0f) + (Has(Law.WeaponBan) ? 0.08f : 0f));
        if (BoostDays > 0 && BoostNeed >= 0)
        {
            if (BoostNeed == 0) Housed = Mathf.Clamp01(Housed + 0.15f);
            else if (BoostNeed == 1) Fed = Mathf.Clamp01(Fed + 0.15f);
            else if (BoostNeed == 2) Safety = Mathf.Clamp01(Safety + 0.15f);
            else Services = Mathf.Clamp01(Services + 0.15f);
        }
    }

    public static float TargetSatisfaction()
    {
        Recalculate();
        float target = 100f * (0.30f * Housed + 0.30f * Fed + 0.25f * Safety + 0.15f * Services) - TaxPenalty[TaxLevel] - (Curfew ? 8f : 0f)
                        - (Has(Law.Rationing) ? 4f : 0f) - (Has(Law.WeaponBan) ? 5f : 0f) - (Has(Law.ForcedLabor) ? 10f : 0f) - 2f * Mathf.Min(5, BuildPlot.ContaminatedCount);
        return Mathf.Clamp(target, 0f, 100f);
    }

    public static int DailyTax()
    {
        if (Protesting) return 0;
        return Mathf.RoundToInt(CityPopulation.AliveCivilians() * TaxPerCivilian[TaxLevel] * Mathf.Lerp(0.4f, 1.2f, Satisfaction / 100f) * (Has(Law.ForcedLabor) ? 1.25f : 1f));
    }

    /// <summary>Volá sa raz za deň (nový deň). Vráti správu pre hráča (môže byť prázdna).</summary>
    public static string DailyUpdate(PlayerStats player)
    {
        Satisfaction = Mathf.Lerp(Satisfaction, TargetSatisfaction(), 0.5f);
        if (Patrols) Reputation.AddSuspicion(1.5f);

        var messages = new List<string>();

        // Zákony, ktoré stoja denne peniaze
        healthPaid = false;
        if (Has(Law.FreeHealth) && player != null && player.TrySpend(15)) healthPaid = true;
        else if (Has(Law.FreeHealth)) messages.Add("Na bezplatné ošetrenie nie sú peniaze (15/deň).");
        if (Has(Law.WeaponBan)) Reputation.Add(FactionId.Rebels, -0.5f);

        // Žiadosti civilistov a dočasné zlepšenia
        if (BoostDays > 0 && --BoostDays == 0) BoostNeed = -1;
        for (int i = Petitions.Count - 1; i >= 0; i--)
        {
            if (--Petitions[i].daysLeft > 0) continue;
            Satisfaction = Mathf.Max(0f, Satisfaction - 8f);
            messages.Add($"Civilisti sa nedočkali odpovede na žiadosť ({NeedNames[Petitions[i].need]}): nespokojnosť rastie.");
            Petitions.RemoveAt(i);
        }
        MakePetition(messages);

        // Protesty
        if (SuppressedDays > 0)
        {
            SuppressedDays--;
            Protesting = false;
        }
        else if (Satisfaction < 35f)
        {
            ProtestStreak++;
            Protesting = true;
            messages.Add("Civilisti protestujú: žiadne dane a zberači nepracujú.");
        }
        else if (Satisfaction >= 40f)
        {
            ProtestStreak = 0;
            Protesting = false;
        }

        // Občianska vojna po dvoch dňoch pod 15
        if (CivilWarDays > 0)
        {
            CivilWarDays--;
            CityPopulation.Reduce(PopulationGroup.Civilians, UnityEngine.Random.Range(1, 3));
            CityPopulation.Reduce(PopulationGroup.Soldiers, UnityEngine.Random.Range(1, 3));
            Reputation.AddSuspicion(8f);
            messages.Add("Občianska vojna: ubúdajú civilisti aj vojaci.");
            if (CivilWarDays == 0) messages.Add("Boje utíchli.");
        }
        else if (Satisfaction < 15f && ProtestStreak >= 2)
        {
            CivilWarDays = 3;
            messages.Add("Vypukla občianska vojna!");
        }

        int securityHubs = 0;
        foreach (BuildPlot plot in BuildPlot.All)
            if (plot.State == PlotState.Built && plot.Building != null && plot.Building.id == BuildingCatalog.CorpSecurity) securityHubs++;
        if (securityHubs > 0) Reputation.AddSuspicion(-3f * securityHubs);

        int tax = DailyTax();
        if (tax > 0 && player != null)
        {
            player.AddMoney(tax);
            messages.Add($"Dane: +{tax} peňazí (spokojnosť {Satisfaction:0}).");
        }
        return string.Join(" ", messages);
    }

    // ---------- Žiadosti a protesty ----------
    static void MakePetition(List<string> messages)
    {
        if (Petitions.Count >= 3 || UnityEngine.Random.value > 0.5f) return;

        float[] needs = { Housed, Fed, Safety, Services };
        int weakest = -1;
        float lowest = 0.55f;
        for (int i = 0; i < needs.Length; i++)
        {
            bool open = false;
            foreach (Petition p in Petitions) if (p.need == i) open = true;
            if (!open && needs[i] < lowest) { lowest = needs[i]; weakest = i; }
        }
        if (weakest < 0) return;

        Petitions.Add(new Petition { need = weakest, daysLeft = 3 });
        messages.Add($"Civilisti podali žiadosť: {NeedNames[weakest]}. Rozhodni v Správe mesta.");
    }

    public static int PetitionCost(Petition p) => 60 + 30 * Mathf.Max(1, CityPopulation.AliveCivilians() / 4);

    public static string GrantPetition(Petition p, PlayerStats player)
    {
        int cost = PetitionCost(p);
        if (player == null || !player.TrySpend(cost)) return $"Treba {cost} peňazí.";
        BoostNeed = p.need;
        BoostDays = 4;
        Satisfaction = Mathf.Min(100f, Satisfaction + 6f);
        Petitions.Remove(p);
        return $"Žiadosti vyhovené: {NeedNames[p.need]} sa na 4 dni zlepší.";
    }

    public static string RefusePetition(Petition p)
    {
        Satisfaction = Mathf.Max(0f, Satisfaction - 4f);
        Petitions.Remove(p);
        return "Žiadosť zamietnutá. Civilisti sú nespokojní.";
    }

    /// <summary>Protest: vyjednávať (peniaze, upokojí) alebo potlačiť vojakmi (strach, podozrenie).</summary>
    public static string NegotiateProtest(PlayerStats player)
    {
        if (!Protesting) return "Nikto neprotestuje.";
        if (player == null || !player.TrySpend(150)) return "Vyjednávanie stojí 150 peňazí.";
        Satisfaction = Mathf.Min(100f, Satisfaction + 12f);
        ProtestStreak = 0;
        Protesting = false;
        return "Vyjednávanie pomohlo. Protesty utíchli.";
    }

    public static string SuppressProtest()
    {
        if (!Protesting) return "Nikto neprotestuje.";
        Satisfaction = Mathf.Max(0f, Satisfaction - 6f);
        Reputation.AddSuspicion(12f);
        Reputation.Add(FactionId.Civilians, -8f);
        ProtestStreak = 0;
        SuppressedDays = 2;
        Protesting = false;
        return "Vojaci rozohnali dav. Pokoj potrvá dva dni, ale ľudia si to zapamätajú.";
    }

    static void DrawCivicMenu(GUIStyle rich, ref string status)
    {
        PlayerStats player = UnityEngine.Object.FindAnyObjectByType<PlayerStats>();

        if (Protesting)
        {
            GUILayout.Space(6f);
            GUILayout.Label("<b><color=#ff6a5a>Protest civilistov</color></b>", rich);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Vyjednávať (150)", GUILayout.Height(28f))) status = NegotiateProtest(player);
            if (GUILayout.Button("Potlačiť vojakmi", GUILayout.Height(28f))) status = SuppressProtest();
            GUILayout.EndHorizontal();
        }

        if (Petitions.Count > 0)
        {
            GUILayout.Space(6f);
            GUILayout.Label("<b>Žiadosti civilistov</b>", rich);
            for (int i = Petitions.Count - 1; i >= 0; i--)
            {
                Petition p = Petitions[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{NeedNames[p.need]} (zostáva {p.daysLeft} d)", GUILayout.Width(190f));
                bool grant = GUILayout.Button($"Vyhovieť ({PetitionCost(p)})", GUILayout.Height(26f));
                bool refuse = GUILayout.Button("Zamietnuť", GUILayout.Height(26f));
                GUILayout.EndHorizontal();
                if (grant) { status = GrantPetition(p, player); break; }
                if (refuse) { status = RefusePetition(p); break; }
            }
        }
        if (BoostDays > 0 && BoostNeed >= 0) GUILayout.Label($"Dočasné zlepšenie: {NeedNames[BoostNeed]} ešte {BoostDays} d.", rich);
        if (BuildPlot.ContaminatedCount > 0) GUILayout.Label($"Kontaminovaná pôda: {BuildPlot.ContaminatedCount} parciel (znižuje služby a spokojnosť).", rich);
    }

    // ---------- Premena na démonov ----------
    /// <summary>Civilista na danom mieste sa premení na nižšieho démona (privolá sa voľný nižší démon). Vráti true, ak sa to podarilo.</summary>
    public static bool ConvertCivilian(Vector3 position, CivilianAI civilian)
    {
        DemonEnemy source = null;
        foreach (DemonEnemy demon in DemonEnemy.All)
        {
            if (demon == null || demon.tier != DemonTier.Lesser || demon.Damage == null || demon.Damage.IsDead) continue;
            if (demon.GetComponent<SurgeGuest>() != null) continue;
            source = demon;
            break;
        }
        if (source == null) return false;

        if (civilian != null)
        {
            Damageable victim = civilian.GetComponent<Damageable>();
            if (victim != null) victim.ForceKill(false);
        }
        CityPopulation.Reduce(PopulationGroup.Civilians, 1);
        source.gameObject.AddComponent<SurgeGuest>().Begin(position + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0.4f, UnityEngine.Random.Range(-1.5f, 1.5f)), 300f);
        return true;
    }

    // ---------- Ukladanie ----------
    [Serializable]
    public class SaveData
    {
        public int tax, protestStreak, civilWarDays;
        public bool curfew, patrols;
        public int laws, boostNeed = -1, boostDays, suppressed;
        public List<int> petNeeds = new List<int>();
        public List<int> petDays = new List<int>();
        public float satisfaction = 60f;
        public List<string> zonePlots = new List<string>();
        public List<int> zoneKinds = new List<int>();
    }

    public static SaveData Export()
    {
        var data = new SaveData { tax = TaxLevel, protestStreak = ProtestStreak, civilWarDays = CivilWarDays, curfew = Curfew, patrols = Patrols, satisfaction = Satisfaction };
        data.laws = Laws; data.boostNeed = BoostNeed; data.boostDays = BoostDays; data.suppressed = SuppressedDays;
        foreach (Petition p in Petitions) { data.petNeeds.Add(p.need); data.petDays.Add(p.daysLeft); }
        foreach (KeyValuePair<string, ZoneKind> pair in zones)
        {
            data.zonePlots.Add(pair.Key);
            data.zoneKinds.Add((int)pair.Value);
        }
        return data;
    }

    public static void Import(SaveData data)
    {
        ResetState();
        if (data == null) return;

        TaxLevel = Mathf.Clamp(data.tax, 0, 3);
        ProtestStreak = data.protestStreak;
        CivilWarDays = data.civilWarDays;
        Curfew = data.curfew;
        Laws = data.laws;
        BoostNeed = data.boostNeed;
        BoostDays = data.boostDays;
        SuppressedDays = data.suppressed;
        for (int i = 0; i < data.petNeeds.Count && i < data.petDays.Count; i++) Petitions.Add(new Petition { need = data.petNeeds[i], daysLeft = data.petDays[i] });
        Patrols = data.patrols;
        Satisfaction = Mathf.Clamp(data.satisfaction, 0f, 100f);
        Protesting = Satisfaction < 35f;
        for (int i = 0; i < data.zonePlots.Count && i < data.zoneKinds.Count; i++) zones[data.zonePlots[i]] = (ZoneKind)data.zoneKinds[i];
    }

    // ---------- Menu (karta "Správa mesta" v menu osady) ----------
    public static void DrawMenu(GUIStyle rich, BuildPlot selected, ref string status)
    {
        Recalculate();
        GUILayout.Label($"<b>SPRÁVA MESTA</b>   Spokojnosť: <b>{Satisfaction:0}</b>/100 " + (Protesting ? "<color=#ff6a5a>(protesty)</color>" : "") + (CivilWarDays > 0 ? " <color=#ff3030>(občianska vojna)</color>" : ""), rich);
        GUILayout.Label($"Potreby: bývanie {Housed * 100f:0} %, jedlo {Fed * 100f:0} %, bezpečie {Safety * 100f:0} %, služby {Services * 100f:0} %. Cieľ spokojnosti dnes: {TargetSatisfaction():0}. Očakávané dane: {DailyTax()} peňazí za deň.", rich);
        GUILayout.Space(6f);

        GUILayout.Label("<b>Dane</b>", rich);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < TaxNames.Length; i++)
        {
            GUI.enabled = i != TaxLevel;
            if (GUILayout.Button($"{TaxNames[i]} ({TaxPerCivilian[i]}/civilista)", GUILayout.Height(28f))) TaxLevel = i;
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.Label("<b>Zákony</b>", rich);
        Curfew = GUILayout.Toggle(Curfew, " Zákaz vychádzania (bezpečie +12 %, spokojnosť −8)");
        Patrols = GUILayout.Toggle(Patrols, " Vojenské hliadky (bezpečie +10 %, denne mierne podozrenie)");
        SetLaw(Law.Rationing, GUILayout.Toggle(Has(Law.Rationing), " Prídelový systém (jedla ako +35 %, spokojnosť −4)"));
        SetLaw(Law.WeaponBan, GUILayout.Toggle(Has(Law.WeaponBan), " Zákaz zbraní (bezpečie +8 %, spokojnosť −5, odboj −0,5 dôvery denne)"));
        SetLaw(Law.ForcedLabor, GUILayout.Toggle(Has(Law.ForcedLabor), " Povinná práca (dane +25 %, spokojnosť −10)"));
        SetLaw(Law.FreeHealth, GUILayout.Toggle(Has(Law.FreeHealth), " Bezplatné ošetrenie (služby +15 %, 15 peňazí denne)"));

        DrawCivicMenu(rich, ref status);

        GUILayout.Space(6f);
        GUILayout.Label("<b>Plánované zóny</b> (vybraná budova v karte Osada): správna zóna +15 % príjem, nesprávna −10 %", rich);
        if (selected == null) { GUILayout.Label("Najprv vyber postavenú budovu v karte Osada."); return; }

        ZoneKind now = ZoneOf(selected.PlotId);
        GUILayout.Label($"{selected.Building.displayName}: zóna {KindName(now)}, kategória budovy {KindName(CategoryOf(selected.Building))}", rich);
        GUILayout.BeginHorizontal();
        foreach (ZoneKind kind in new[] { ZoneKind.None, ZoneKind.Residential, ZoneKind.Industrial, ZoneKind.Defense, ZoneKind.Commercial })
        {
            GUI.enabled = kind != now;
            if (GUILayout.Button(KindName(kind), GUILayout.Height(28f))) SetZone(selected.PlotId, kind);
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }
}

/// <summary>Denný vývoj spokojnosti, vyplácanie daní a nočné premeny civilistov na démonov.</summary>
public class CityManagementRunner : MonoBehaviour
{
    private PlayerStats player;
    private float nextConvert;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<CityManagementRunner>() != null) return;
        new GameObject("CityManagementRunner").AddComponent<CityManagementRunner>();
    }

    void OnEnable() => DayNightCycle.OnNewDay += HandleNewDay;
    void OnDisable() => DayNightCycle.OnNewDay -= HandleNewDay;

    void HandleNewDay(int day)
    {
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        string message = CityManagement.DailyUpdate(player);
        ConvoySystem.OnNewDay(day, player);
        Buyer.OnNewDay();
        if (!string.IsNullOrEmpty(message)) GameHUD.Message(message, 6f);
    }

    void Update()
    {
        if (PauseMenu.AtStartup || Time.time < nextConvert) return;
        nextConvert = Time.time + 15f;

        DayNightCycle clock = FindAnyObjectByType<DayNightCycle>();
        if (clock == null || !clock.IsNight) return;

        // Vyšší démon nablízku civilistu: šanca, že ho premení na nižšieho démona
        foreach (DemonEnemy demon in DemonEnemy.All)
        {
            if (demon == null || demon.Tier < DemonTier.Higher || demon.tier == DemonTier.Boss || demon.Damage == null || demon.Damage.IsDead) continue;

            foreach (CivilianAI civilian in CivilianAI.All)
            {
                if (civilian == null || civilian.IsDead || !civilian.gameObject.activeInHierarchy) continue;
                if ((civilian.transform.position - demon.transform.position).sqrMagnitude > 12f * 12f) continue;
                if (UnityEngine.Random.value > 0.25f) break;

                if (CityManagement.ConvertCivilian(civilian.transform.position, civilian))
                    GameHUD.Message("Vyšší démon premenil civilistu na nižšieho démona!", 4f);
                return;
            }
        }

        // Vyčerpaní civilisti pri nízkej spokojnosti
        if (CityManagement.Satisfaction < 25f && UnityEngine.Random.value < 0.35f)
            foreach (CivilianAI civilian in CivilianAI.All)
                if (civilian != null && !civilian.IsDead && civilian.gameObject.activeInHierarchy)
                {
                    if (CityManagement.ConvertCivilian(civilian.transform.position, civilian))
                        GameHUD.Message("Slabý a vyčerpaný civilista sa premenil na démona!", 4f);
                    return;
                }
    }
}
