using UnityEngine;

/// <summary>
/// Elektrina mesta: elektráreň s generátorom spaľuje čierne kryštály z bane. Čím viac domov a budov, tým väčšia spotreba.
/// Bez elektriny nejdú vylepšenia v dielni, bleskový nabíjač, veže korporácie ani zdravotné stredisko, klesá príjem budov a spokojnosť.
/// </summary>
public static class Power
{
    public const float UnitsPerCrystal = 60f;
    public const float CapacityPerPlant = 600f;
    const float DayLength = 240f;   // rovnaké ako dayLengthSeconds v DayNightCycle

    public static float Stored;
    public static int Plants { get; private set; }
    public static float DemandPerDay { get; private set; }
    public static int Houses { get; private set; }

    static bool warnedLow, warnedOut;
    static float recountTimer = 99f;

    public static bool PlantBuilt => Plants > 0;
    public static bool Online => Plants > 0 && Stored > 0f;
    public static float Capacity => Plants * CapacityPerPlant;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        Stored = 0f;
        Plants = 0;
        DemandPerDay = 0f;
        Houses = 0;
        warnedLow = warnedOut = false;
    }

    public static float Export() => Stored;
    public static void Import(float stored) { ResetState(); Stored = Mathf.Max(0f, stored); }

    /// <summary>Prepočíta spotrebu podľa postavených budov.</summary>
    public static void Recount()
    {
        int plants = 0, houses = 0, others = 0;
        foreach (BuildPlot plot in BuildPlot.All)
        {
            if (plot.State != PlotState.Built || plot.Building == null) continue;
            BuildingDef def = plot.Building;
            if (def.id == BuildingCatalog.PowerPlant) { plants++; continue; }
            if (def.housing > 0 && def.housingFor == PopulationGroup.Civilians) houses++;
            else if (def.id != BuildingCatalog.RebelBarricade) others++;
        }
        Plants = plants;
        Houses = houses;
        DemandPerDay = plants > 0 ? (10f + houses * 12f + others * 6f) * SpecialGoods.PowerDemandFactor : 0f;
    }

    public static void Tick(float dt)
    {
        recountTimer += dt;
        if (recountTimer >= 1f) { recountTimer = 0f; Recount(); }
        if (Plants == 0) return;

        Stored = Mathf.Min(Capacity, Stored);
        if (Stored <= 0f) return;

        Stored = Mathf.Max(0f, Stored - DemandPerDay / DayLength * dt);
        if (Stored <= 0f && !warnedOut)
        {
            warnedOut = true;
            GameHUD.Message("Generátor sa vypol! Mesto je bez elektriny. Doplň čierne kryštály.", 6f);
        }
        else if (Stored > 0f) warnedOut = false;

        bool low = Stored < Capacity * 0.15f && Stored > 0f;
        if (low && !warnedLow) { warnedLow = true; GameHUD.Message("Generátoru dochádza palivo (čierne kryštály).", 4f); }
        if (!low) warnedLow = false;
    }

    public static float DaysLeft => DemandPerDay > 0f ? Stored / DemandPerDay : 0f;

    public static BuildPlot NearestPlant(Vector3 position, float reach = 6f)
    {
        foreach (BuildPlot plot in BuildPlot.All)
        {
            if (plot.State != PlotState.Built || plot.Building == null || plot.Building.id != BuildingCatalog.PowerPlant) continue;
            Vector3 d = plot.transform.position - position;
            d.y = 0f;
            if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z)) - plot.Side * 0.5f <= reach) return plot;
        }
        return null;
    }

    /// <summary>Vloží čierne kryštály z inventára do generátora. Vráti počet vložených.</summary>
    public static int Refuel(Inventory inventory)
    {
        if (inventory == null || Plants == 0) return 0;
        int room = Mathf.FloorToInt((Capacity - Stored) / UnitsPerCrystal);
        int count = Mathf.Min(room, inventory.CountOf(ItemCatalog.BlackCrystal));
        if (count <= 0 || !inventory.TryRemove(ItemCatalog.BlackCrystal, count)) return 0;
        Stored += count * UnitsPerCrystal;
        return count;
    }

    public static void DrawMenu(GUIStyle rich, PlayerStats player, ref string status)
    {
        Recount();
        GUILayout.Label("<b>ELEKTRINA</b>", rich);
        if (Plants == 0)
        {
            GUILayout.Label("Mesto nemá elektráreň. Postav <b>Elektráreň s generátorom</b> na parcele. Bez elektriny nefungujú vylepšenia zbraní a nástrojov v dielni.", rich);
        }
        else
        {
            GUILayout.Label($"Stav: {(Online ? "<color=#7CFC7C>ONLINE</color>" : "<color=#ff5a5a>VYPNUTÉ</color>")}   Palivo: <b>{Stored / UnitsPerCrystal:0.0}</b> / {Capacity / UnitsPerCrystal:0} čiernych kryštálov", rich);
            GUILayout.Label($"Spotreba: <b>{DemandPerDay / UnitsPerCrystal:0.0}</b> kryštálu za deň ({Houses} domov × 0,2 + ostatné budovy). Vydrží ešte cca {DaysLeft:0.0} dňa.", rich);

            Inventory inv = player.GetComponent<Inventory>();
            bool near = NearestPlant(player.transform.position, 20f) != null;
            GUI.enabled = near && inv != null && inv.CountOf(ItemCatalog.BlackCrystal) > 0;
            if (GUILayout.Button(near ? $"Vložiť čierne kryštály (máš {(inv != null ? inv.CountOf(ItemCatalog.BlackCrystal) : 0)})" : "Vložiť čierne kryštály (musíš byť pri elektrárni)", GUILayout.Height(28f)))
            {
                int n = Refuel(inv);
                status = n > 0 ? $"Do generátora vložených {n} kryštálov." : "Generátor je plný.";
            }
            GUI.enabled = true;
        }

        GUILayout.Space(8f);
        GUILayout.Label("<b>Na elektrine závisí:</b> vylepšenia a opravy v dielni, bleskový nabíjač zbrane, obranné veže korporácie, zdravotné stredisko, služby mesta (spokojnosť) a príjem nedomových budov (bez prúdu len 60 %).", rich);

        GUILayout.Space(8f);
        Inventory items = player.GetComponent<Inventory>();
        int gold = items != null ? items.CountOf(ItemCatalog.Gold) : 0;
        GUILayout.Label($"<b>Zlato</b>: máš {gold}. Predaj za 70 peňazí/ks.", rich);
        GUI.enabled = gold > 0;
        if (GUILayout.Button("Predať všetko zlato", GUILayout.Height(28f)) && items != null && items.TryRemove(ItemCatalog.Gold, gold))
        {
            player.AddMoney(gold * 70);
            status = $"Predané zlato: +{gold * 70} peňazí.";
        }
        GUI.enabled = true;
    }
}

/// <summary>Beh elektriny: spotreba paliva a vkladanie kryštálov klávesom F pri elektrárni.</summary>
public class PowerRunner : MonoBehaviour
{
    private PlayerStats player;
    private Inventory inventory;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<PowerRunner>() != null) return;
        new GameObject("PowerRunner").AddComponent<PowerRunner>();
    }

    void Update()
    {
        if (PauseMenu.AtStartup) return;
        Power.Tick(Time.deltaTime);

        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null) inventory = player.GetComponent<Inventory>();
            return;
        }
        if (player.IsDead || SoulMenu.MenuOpen || LairMenu.MenuOpen || MangaDialog.Active || Power.Plants == 0) return;
        if (Power.NearestPlant(player.transform.position) == null) return;

        int have = inventory != null ? inventory.CountOf(ItemCatalog.BlackCrystal) : 0;
        GameHUD.Prompt($"F – vložiť čierne kryštály do generátora (máš {have}, palivo {Power.Stored / Power.UnitsPerCrystal:0.0}/{Power.Capacity / Power.UnitsPerCrystal:0})");
        if (!GameInput.GetKeyDown(KeyBindings.Get("use", KeyCode.F))) return;

        int n = Power.Refuel(inventory);
        GameHUD.Message(n > 0 ? $"Generátor nabitý: +{n} čiernych kryštálov." : have > 0 ? "Generátor je plný." : "Nemáš čierne kryštály. Vyťaž ich v bani.", 3f);
    }
}
