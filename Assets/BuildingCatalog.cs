using System.Collections.Generic;
using UnityEngine;

/// <summary>Definícia stavby, ktorú hráč postaví na parcele. Katalóg je v kóde (ako ItemCatalog), vzhľad je zatiaľ z kvádrov.</summary>
public class BuildingDef
{
    public string id;
    public string displayName;
    public string description;
    /// <summary>Frakcia, ktorej parcela stavbu povoľuje; None = spoločná (každá ľudská frakcia).</summary>
    public FactionId faction = FactionId.None;
    public int cost;
    public int wood, stone, scrap, crystal;
    public float buildSeconds = 60f;
    public int minLevel = 1;
    [Tooltip("Dôvera vo frakcii parcely, ktorú stavba vyžaduje")]
    public float minTrust;

    /// <summary>Bývanie: kapacita skupiny populácie v zóne parcely (0 = žiadne).</summary>
    public int housing;
    public PopulationGroup housingFor = PopulationGroup.Civilians;

    // Zatiaľ jednoduchý vzhľad: telo + strecha
    public Vector3 size = new Vector3(10f, 5f, 10f);
    public Color color = new Color(0.5f, 0.5f, 0.5f);
    public Color roofColor = new Color(0.25f, 0.25f, 0.28f);

    [Tooltip("Odolnosť stavby proti démonom a koreňom stromov")]
    public float health = 300f;
    /// <summary>Povolanie, ktorého dôvera odomyká túto stavbu (None = netreba).</summary>
    public Profession requiresProfession = Profession.None;
    public float requiresProfessionTrust;

    [Tooltip("Obranná stavba: dosah (m), poškodenie výstrelu a interval (s). 0 = nestrieľa.")]
    public float defenseRange;
    public float defenseDamage;
    public float defenseInterval = 1.2f;

    public bool IsCommon => faction == FactionId.None;
}

public static class BuildingCatalog
{
    public const string House = "house";
    public const string RebelWorkshop = "rebel_workshop";
    public const string RebelSolar = "rebel_solar";
    public const string CivilianShop = "civil_shop";
    public const string CivilianClinic = "civil_clinic";
    public const string CorpBarracks = "corp_barracks";
    public const string CorpTower = "corp_tower";
    public const string RebelBarricade = "rebel_barricade";
    public const string CorpDepot = "corp_depot";
    public const string CivilianKitchen = "civil_kitchen";
    public const string PowerPlant = "power_plant";
    public const string CorpSecurity = "corp_security";
    public const string Farm = "farm";
    public const string Shelter = "civil_shelter";

    private static readonly Dictionary<string, BuildingDef> defs = Build();

    public static BuildingDef Get(string id) => !string.IsNullOrEmpty(id) && defs.TryGetValue(id, out BuildingDef d) ? d : null;
    public static IEnumerable<BuildingDef> All => defs.Values;

    /// <summary>Stavby dostupné na parcele danej frakcie: spoločné + vlastné.</summary>
    public static void ForFaction(FactionId plotFaction, List<BuildingDef> result)
    {
        result.Clear();
        foreach (BuildingDef d in defs.Values)
            if (d.IsCommon || d.faction == plotFaction) result.Add(d);
    }

    static Dictionary<string, BuildingDef> Build()
    {
        var d = new Dictionary<string, BuildingDef>();
        void Add(BuildingDef def) { d[def.id] = def; }

        // ---------- Spoločné ----------
        Add(new BuildingDef
        {
            id = House, health = 250f, displayName = "Obytný dom", description = "Ubytovanie: zvýši kapacitu civilistov v zóne o 8.",
            cost = 120, wood = 6, stone = 4, buildSeconds = 90f, housing = 8, housingFor = PopulationGroup.Civilians,
            size = new Vector3(10f, 5f, 9f), color = new Color(0.55f, 0.5f, 0.42f), roofColor = new Color(0.3f, 0.18f, 0.15f)
        });

        // ---------- Odboj: provizórne stavby z vrakov a plechu ----------
        Add(new BuildingDef
        {
            id = RebelWorkshop, health = 320f, requiresProfession = Profession.Engineer, requiresProfessionTrust = 15f, displayName = "Dielňa odboja", description = "Provizórna dielňa z vrakov a plechov. Základ pre vylepšenia zbraní.",
            faction = FactionId.Rebels, cost = 180, scrap = 10, wood = 4, buildSeconds = 120f, minTrust = 10f,
            size = new Vector3(11f, 4.5f, 9f), color = new Color(0.42f, 0.36f, 0.32f), roofColor = new Color(0.35f, 0.4f, 0.42f)
        });
        Add(new BuildingDef
        {
            id = RebelSolar, health = 260f, requiresProfession = Profession.Electrician, requiresProfessionTrust = 25f, displayName = "Solárna elektráreň", description = "Domáca solárna a bleskovodná energia pre odboj. Zvýši bývanie odboja o 6.",
            faction = FactionId.Rebels, cost = 220, scrap = 8, crystal = 2, buildSeconds = 150f, minTrust = 20f, housing = 6, housingFor = PopulationGroup.Rebels,
            size = new Vector3(12f, 3f, 12f), color = new Color(0.3f, 0.35f, 0.4f), roofColor = new Color(0.1f, 0.2f, 0.45f)
        });

        // ---------- Civilisti ----------
        Add(new BuildingDef
        {
            id = CivilianShop, health = 250f, requiresProfession = Profession.Cook, requiresProfessionTrust = 20f, displayName = "Obchod", description = "Obchod pre civilistov: príjem a dôvera mesta.",
            faction = FactionId.Civilians, cost = 160, wood = 6, stone = 6, buildSeconds = 100f, minTrust = 10f,
            size = new Vector3(10f, 4.5f, 8f), color = new Color(0.6f, 0.52f, 0.4f), roofColor = new Color(0.55f, 0.2f, 0.18f)
        });
        Add(new BuildingDef
        {
            id = CivilianClinic, health = 280f, requiresProfession = Profession.Doctor, requiresProfessionTrust = 25f, displayName = "Zdravotné stredisko", description = "Ošetrovňa. Vyžaduje dôveru civilistov a bylinky.",
            faction = FactionId.Civilians, cost = 200, wood = 4, stone = 6, buildSeconds = 140f, minTrust = 25f,
            size = new Vector3(11f, 5f, 9f), color = new Color(0.8f, 0.82f, 0.82f), roofColor = new Color(0.7f, 0.2f, 0.2f)
        });

        Add(new BuildingDef
        {
            id = Shelter, health = 320f, displayName = "Podzemný úkryt", description = "Skrytý bunker pre preživších civilistov a informátorov v opustenej časti mesta. Prináša pasívny príjem, pojme 6 civilistov a v búrke či v noci poskytne bezpečie.",
            faction = FactionId.Civilians, cost = 150, wood = 6, stone = 8, buildSeconds = 110f, minTrust = 8f, housing = 6, housingFor = PopulationGroup.Civilians,
            size = new Vector3(9f, 2.5f, 8f), color = new Color(0.25f, 0.24f, 0.26f), roofColor = new Color(0.14f, 0.13f, 0.14f)
        });

        // ---------- Megakorporácia: sterilný industriál ----------
        Add(new BuildingDef
        {
            id = CorpBarracks, health = 450f, displayName = "Kasárne", description = "Ubytovanie vojakov: kapacita vojakov v zóne +6.",
            faction = FactionId.Megacorp, cost = 260, scrap = 12, stone = 6, buildSeconds = 140f, minTrust = 10f, housing = 6, housingFor = PopulationGroup.Soldiers,
            size = new Vector3(14f, 5f, 9f), color = new Color(0.25f, 0.27f, 0.31f), roofColor = new Color(0.15f, 0.17f, 0.2f)
        });
        Add(new BuildingDef
        {
            id = CorpTower, health = 500f, defenseRange = 38f, defenseDamage = 22f, defenseInterval = 1.1f, requiresProfession = Profession.Engineer, requiresProfessionTrust = 40f, displayName = "Obranná veža", description = "Neónom osvetlená strážna veža komplexu.",
            faction = FactionId.Megacorp, cost = 300, scrap = 14, crystal = 3, buildSeconds = 160f, minTrust = 20f, minLevel = 2,
            size = new Vector3(5f, 14f, 5f), color = new Color(0.2f, 0.36f, 0.48f), roofColor = new Color(0.2f, 0.9f, 1f)
        });

        // ---------- Ďalšie stavby ----------
        Add(new BuildingDef
        {
            id = RebelBarricade, health = 400f, defenseRange = 14f, defenseDamage = 8f, defenseInterval = 1.6f, displayName = "Barikáda odboja", description = "Pevná barikáda z vriec s pieskom a plechov. Rýchlo sa stavia a dlho vydrží.",
            faction = FactionId.Rebels, cost = 90, scrap = 6, stone = 3, buildSeconds = 60f, minTrust = 5f,
            size = new Vector3(12f, 2.5f, 3f), color = new Color(0.4f, 0.36f, 0.3f), roofColor = new Color(0.3f, 0.32f, 0.34f)
        });
        Add(new BuildingDef
        {
            id = CorpDepot, health = 380f, displayName = "Sklad korporácie", description = "Sklad zásob a techniky Megakorporácie. Čerpá energiu portálu.",
            faction = FactionId.Megacorp, cost = 200, scrap = 8, stone = 4, buildSeconds = 100f, minTrust = 5f,
            size = new Vector3(13f, 6f, 10f), color = new Color(0.24f, 0.27f, 0.3f), roofColor = new Color(0.2f, 0.9f, 1f)
        });
        Add(new BuildingDef
        {
            id = CivilianKitchen, health = 220f, requiresProfession = Profession.Cook, requiresProfessionTrust = 30f, displayName = "Jedáleň",
            description = "Spoločná kuchyňa pre civilistov: bývanie civilistov +4.", faction = FactionId.Civilians, cost = 140, wood = 5, stone = 3, buildSeconds = 80f,
            housing = 4, housingFor = PopulationGroup.Civilians,
            size = new Vector3(10f, 4.5f, 8f), color = new Color(0.62f, 0.5f, 0.38f), roofColor = new Color(0.5f, 0.3f, 0.2f)
        });

        // ---------- Megakorporácia: špecifická budova ----------
        Add(new BuildingDef
        {
            id = CorpSecurity, health = 480f, displayName = "Bezpečnostné centrum",
            description = "Monitorovacie centrum korporácie: denne zníži podozrenie o 3 a zvýši bezpečie mesta. Len v Megakorporácii.",
            faction = FactionId.Megacorp, cost = 350, scrap = 12, stone = 6, crystal = 2, buildSeconds = 150f, minTrust = 8f,
            size = new Vector3(14f, 9f, 12f), color = new Color(0.17f, 0.2f, 0.26f), roofColor = new Color(0.2f, 0.9f, 1f)
        });

        // ---------- Spoločná: pole (farma) ----------
        Add(new BuildingDef
        {
            id = Farm, health = 140f, displayName = "Pole (farma)",
            description = "Lacné pole: stačí drevo a peniaze. Najmi až 3 farmárov (civilisti) a plať im mzdu: čím vyššia mzda a viac farmárov, tým rýchlejšie a viac jedla. Úroda sa ukladá do truhly pri poli.",
            cost = 100, wood = 10, buildSeconds = 40f,
            size = new Vector3(13f, 0.25f, 13f), color = new Color(0.25f, 0.17f, 0.1f), roofColor = new Color(0.4f, 0.3f, 0.15f)
        });

        // ---------- Spoločná: elektráreň ----------
        Add(new BuildingDef
        {
            id = PowerPlant, health = 350f, displayName = "Elektráreň s generátorom",
            description = "Generátor spaľuje čierne kryštály z bane (F pri budove). Čím viac domov, tým väčšia spotreba. Bez prúdu nejdú vylepšenia v dielni a veže.",
            cost = 450, scrap = 20, stone = 15, crystal = 3, buildSeconds = 180f,
            size = new Vector3(12f, 6f, 10f), color = new Color(0.3f, 0.3f, 0.34f), roofColor = new Color(0.45f, 0.2f, 0.7f)
        });

        return d;
    }
}
