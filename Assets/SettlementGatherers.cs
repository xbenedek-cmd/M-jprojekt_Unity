using System;
using System.Collections.Generic;
using UnityEngine;

public enum GatherMaterial { Wood, Stone, Scrap, Crystal, Herb }

/// <summary>
/// Zberači osady: na každú surovinu sa dá najať jeden špecialista (lesník, baník, zberač šrotu, zberač kryštálov, bylinkár) a priradiť ku
/// postavenej budove. Pravidelne vyberá reálne zdroje vo svete (stromy, skaly, kopy, kryštály, byliny; zdroj sa zničí / vyčerpá a po čase obnoví
/// rovnako ako pri hráčovi) a ukladá suroviny do truhly pri budove. Hráč si ich vezme klávesom E pri truhle.
/// Zberač je "dostupný" len ak: mesto má civilistov, nie je noc, podozrenie mesta nie je vysoké, portály nemajú výpadok
/// a pri cieľovom zdroji nie je živý démon. Stromy dorastajú sami (rýchlejšie pri živom démonovi), takže zdroje sa obnovujú.
/// </summary>
public static class SettlementGatherers
{
    public const int HireCost = 150;
    public const int ChestCapacityPerItem = 250;
    public const float HarvestInterval = 40f;      // sekúnd medzi dvoma zbermi jedného zberača
    public const float SearchRadius = 900f;        // zóny divočín ležia stovky metrov od mesta
    public const float DemonSafeDistance = 28f;    // pri cieli nesmie byť živý démon
    public const int MinCivilians = 2;
    public const float MaxSuspicion = 70f;

    private static readonly Dictionary<GatherMaterial, string> assigned = new Dictionary<GatherMaterial, string>();
    private static readonly Dictionary<string, Dictionary<string, int>> chests = new Dictionary<string, Dictionary<string, int>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        assigned.Clear();
        chests.Clear();
    }

    public static string Role(GatherMaterial m)
    {
        switch (m)
        {
            case GatherMaterial.Wood: return "Lesník";
            case GatherMaterial.Stone: return "Baník";
            case GatherMaterial.Scrap: return "Zberač šrotu";
            case GatherMaterial.Crystal: return "Zberač kryštálov";
            default: return "Bylinkár";
        }
    }

    public static string ItemOf(GatherMaterial m)
    {
        switch (m)
        {
            case GatherMaterial.Wood: return ItemCatalog.Wood;
            case GatherMaterial.Stone: return ItemCatalog.Stone;
            case GatherMaterial.Scrap: return ItemCatalog.Scrap;
            case GatherMaterial.Crystal: return ItemCatalog.Crystal;
            default: return ItemCatalog.Herb;
        }
    }

    public static string PlotOf(GatherMaterial m) => assigned.TryGetValue(m, out string id) ? id : null;
    public static IEnumerable<KeyValuePair<GatherMaterial, string>> Assignments => assigned;

    public static bool Hire(GatherMaterial m, BuildPlot plot, PlayerStats player, out string message)
    {
        if (plot == null || plot.State != PlotState.Built) { message = "Vyber postavenú budovu, v ktorej bude zberač pracovať (truhla bude pri nej)."; return false; }
        if (CityPopulation.AliveCivilians() < MinCivilians) { message = "V meste je primálo civilistov na najatie zberača."; return false; }

        bool already = PlotOf(m) != null;
        if (!already && !player.TrySpend(HireCost)) { message = $"Zberač stojí {HireCost} peňazí."; return false; }

        assigned[m] = plot.PlotId;
        if (!chests.ContainsKey(plot.PlotId)) chests[plot.PlotId] = new Dictionary<string, int>();
        message = already ? $"{Role(m)} pracuje teraz v: {plot.Building.displayName}." : $"Najatý: {Role(m)} (zbiera {ItemCatalog.Get(ItemOf(m)).displayName.ToLowerInvariant()}), truhla pri: {plot.Building.displayName}.";
        return true;
    }

    public static void Dismiss(GatherMaterial m) => assigned.Remove(m);

    // ---------- Truhla ----------
    public static Dictionary<string, int> Chest(string plotId)
    {
        if (!chests.TryGetValue(plotId, out Dictionary<string, int> content)) chests[plotId] = content = new Dictionary<string, int>();
        return content;
    }

    public static int Stored(string plotId, string item) => chests.TryGetValue(plotId, out var content) && content.TryGetValue(item, out int n) ? n : 0;

    public static int TotalStored(string plotId)
    {
        int total = 0;
        if (chests.TryGetValue(plotId, out var content)) foreach (int n in content.Values) total += n;
        return total;
    }

    public static string Summary(string plotId)
    {
        var parts = new List<string>();
        if (chests.TryGetValue(plotId, out var content))
            foreach (KeyValuePair<string, int> pair in content)
                if (pair.Value > 0) parts.Add($"{ItemCatalog.Get(pair.Key).displayName} {pair.Value}");
        return parts.Count > 0 ? string.Join(", ", parts) : "prázdna";
    }

    /// <summary>Hráč si zoberie z truhly všetko, čo sa mu zmestí do inventára. Vráti počet kusov.</summary>
    public static int TakeAll(string plotId, Inventory inventory)
    {
        if (inventory == null || !chests.TryGetValue(plotId, out var content)) return 0;

        int taken = 0;
        foreach (string item in new List<string>(content.Keys))
        {
            int count = content[item];
            int n = count;
            while (n > 0 && !inventory.TryAdd(item, n)) n--;   // zmestí sa len časť, ak je inventár takmer plný
            if (n <= 0) continue;

            content[item] = count - n;
            taken += n;
        }
        return taken;
    }

    /// <summary>Pole (FarmSystem) ukladá úrodu do tej istej truhly pri budove.</summary>
    public static void AddToChest(string plotId, string item, int amount) => Deposit(plotId, item, amount);

    static void Deposit(string plotId, string item, int amount)
    {
        Dictionary<string, int> content = Chest(plotId);
        content.TryGetValue(item, out int current);
        content[item] = Mathf.Min(ChestCapacityPerItem, current + amount);
    }

    // ---------- Zber ----------
    /// <summary>Skúsi zber pre danú surovinu; vráti správu (null = nič sa nestalo).</summary>
    public static bool TryHarvest(GatherMaterial m, out string reasonUnavailable)
    {
        reasonUnavailable = null;
        string plotId = PlotOf(m);
        if (plotId == null) return false;

        BuildPlot plot = null;
        foreach (BuildPlot p in BuildPlot.All)
            if (p.PlotId == plotId && p.State == PlotState.Built) { plot = p; break; }
        if (plot == null) { reasonUnavailable = "budova nestojí"; return false; }

        if (CityPopulation.AliveCivilians() < MinCivilians) { reasonUnavailable = "málo civilistov"; return false; }
        if (CityManagement.Protesting) { reasonUnavailable = "protesty"; return false; }
        if (Reputation.Suspicion >= MaxSuspicion) { reasonUnavailable = "mesto je v poplachu"; return false; }
        if (PortalStability.Outage) { reasonUnavailable = "portál má výpadok"; return false; }
        DayNightCycle clock = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
        if (clock != null && clock.IsNight) { reasonUnavailable = "je noc"; return false; }
        if (Stored(plotId, ItemOf(m)) >= ChestCapacityPerItem) { reasonUnavailable = "truhla je plná"; return false; }

        string item = ItemOf(m);
        Vector3 origin = plot.transform.position;

        // Zdroje danej suroviny zoradené podľa vzdialenosti od budovy; berie sa jeden z piatich najbližších bezpečných
        var candidates = new List<(float distance, BreakableNode node, ResourceNode resource)>();
        foreach (BreakableNode n in BreakableNode.All)
        {
            if (n == null || n.IsDown || n.DropItem != item) continue;
            if (m == GatherMaterial.Wood && !n.IsTree) continue;
            AddCandidate(candidates, origin, n.transform.position, n, null);
        }
        foreach (ResourceNode r in ResourceNode.All)
        {
            if (r == null || r.IsDepleted || r.ItemId != item) continue;
            AddCandidate(candidates, origin, r.transform.position, null, r);
        }

        if (candidates.Count == 0 && ZoneLoader.HasUnloadedZones)
        {
            // Zóny sú vypnuté (načítavajú sa podľa polohy): zber prebehne "v zákulisí"
            Deposit(plotId, item, UnityEngine.Random.Range(2, 5));
            return true;
        }
        if (candidates.Count == 0) { reasonUnavailable = "nie sú dostupné zdroje"; return false; }

        candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
        int pick = UnityEngine.Random.Range(0, Mathf.Min(5, candidates.Count));
        var chosen = candidates[pick];

        int amount = chosen.node != null ? chosen.node.AutoHarvest() : chosen.resource.AutoHarvest();
        if (amount <= 0) return false;

        Deposit(plotId, item, amount);
        return true;
    }

    static void AddCandidate(List<(float, BreakableNode, ResourceNode)> list, Vector3 origin, Vector3 position, BreakableNode node, ResourceNode resource)
    {
        Vector3 d = position - origin;
        d.y = 0f;
        if (d.sqrMagnitude > SearchRadius * SearchRadius) return;

        foreach (DemonEnemy demon in DemonEnemy.All)
        {
            if (demon == null || demon.Damage == null || demon.Damage.IsDead) continue;
            if ((demon.transform.position - position).sqrMagnitude < DemonSafeDistance * DemonSafeDistance) return;
        }
        list.Add((d.magnitude, node, resource));
    }

    // ---------- Ukladanie ----------
    [Serializable]
    public class SaveData
    {
        public List<int> materials = new List<int>();
        public List<string> materialPlots = new List<string>();
        public List<string> chestPlots = new List<string>();
        public List<string> chestItems = new List<string>();
        public List<int> chestCounts = new List<int>();
    }

    public static SaveData Export()
    {
        var data = new SaveData();
        foreach (KeyValuePair<GatherMaterial, string> pair in assigned)
        {
            data.materials.Add((int)pair.Key);
            data.materialPlots.Add(pair.Value);
        }
        foreach (KeyValuePair<string, Dictionary<string, int>> chest in chests)
            foreach (KeyValuePair<string, int> item in chest.Value)
            {
                if (item.Value <= 0) continue;
                data.chestPlots.Add(chest.Key);
                data.chestItems.Add(item.Key);
                data.chestCounts.Add(item.Value);
            }
        return data;
    }

    public static void Import(SaveData data)
    {
        ResetState();
        if (data == null) return;

        for (int i = 0; i < data.materials.Count && i < data.materialPlots.Count; i++) assigned[(GatherMaterial)data.materials[i]] = data.materialPlots[i];
        for (int i = 0; i < data.chestPlots.Count && i < data.chestItems.Count && i < data.chestCounts.Count; i++)
            Deposit(data.chestPlots[i], data.chestItems[i], data.chestCounts[i]);
    }
}

/// <summary>Beží v scéne: spúšťa zber zberačov a stará sa o truhly pri budovách.</summary>
public class SettlementGathererRunner : MonoBehaviour
{
    private readonly float[] nextHarvest = new float[5];
    private readonly Dictionary<string, SettlementChest> chestObjects = new Dictionary<string, SettlementChest>();
    private float nextChestCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<SettlementGathererRunner>() != null) return;
        new GameObject("SettlementGatherers").AddComponent<SettlementGathererRunner>();
    }

    void Update()
    {
        if (PauseMenu.AtStartup) return;

        foreach (KeyValuePair<GatherMaterial, string> pair in SettlementGatherers.Assignments)
        {
            int index = (int)pair.Key;
            if (Time.time < nextHarvest[index]) continue;

            nextHarvest[index] = Time.time + SettlementGatherers.HarvestInterval;
            SettlementGatherers.TryHarvest(pair.Key, out _);
            break;   // jeden zber za snímok (modifikácia slovníka cez vkladanie do truhly je nezávislá, ale iterácia sa tak nenaruší)
        }

        if (Time.time >= nextChestCheck)
        {
            nextChestCheck = Time.time + 2f;
            SyncChests();
        }
    }

    // Truhla stojí pri každej budove, kde pracuje aspoň jeden zberač; zmizne s budovou
    void SyncChests()
    {
        var needed = new HashSet<string>();
        foreach (KeyValuePair<GatherMaterial, string> pair in SettlementGatherers.Assignments) needed.Add(pair.Value);
        foreach (KeyValuePair<string, FarmSystem.Farm> farm in FarmSystem.All)
            if (farm.Value.farmers > 0 || SettlementGatherers.TotalStored(farm.Key) > 0) needed.Add(farm.Key);

        foreach (BuildPlot plot in BuildPlot.All)
        {
            bool want = needed.Contains(plot.PlotId) && plot.State == PlotState.Built;
            bool has = chestObjects.TryGetValue(plot.PlotId, out SettlementChest chest) && chest != null;

            if (want && !has) chestObjects[plot.PlotId] = SettlementChest.Create(plot);
            else if (!want && has) { Destroy(chest.gameObject); chestObjects.Remove(plot.PlotId); }
        }
    }
}

/// <summary>Truhla zberačov pri budove: E vezme všetko, čo sa zmestí do inventára.</summary>
public class SettlementChest : MonoBehaviour
{
    private string plotId;
    private PlayerStats player;
    private Inventory inventory;

    public static SettlementChest Create(BuildPlot plot)
    {
        var root = new GameObject("TruhlaZberacov");
        root.transform.position = ChestPosition(plot);

        Material wood = Make(new Color(0.42f, 0.27f, 0.14f), 0.1f);
        Material metal = Make(new Color(0.55f, 0.55f, 0.58f), 0.7f);

        Piece(root.transform, "Telo", new Vector3(0f, 0.3f, 0f), new Vector3(1.1f, 0.6f, 0.7f), wood, true);
        Piece(root.transform, "Vrchnak", new Vector3(0f, 0.68f, 0f), new Vector3(1.14f, 0.16f, 0.74f), wood, false);
        Piece(root.transform, "Pas1", new Vector3(-0.32f, 0.45f, 0f), new Vector3(0.1f, 0.78f, 0.76f), metal, false);
        Piece(root.transform, "Pas2", new Vector3(0.32f, 0.45f, 0f), new Vector3(0.1f, 0.78f, 0.76f), metal, false);

        SettlementChest chest = root.AddComponent<SettlementChest>();
        chest.plotId = plot.PlotId;
        return chest;
    }

    static Vector3 ChestPosition(BuildPlot plot)
    {
        Vector3 p = plot.transform.position + plot.transform.right * (plot.Side * 0.5f + 1.5f);
        if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
        return p;
    }

    static Material Make(Color color, float metallic)
    {
        var m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Metallic", metallic);
        return m;
    }

    static void Piece(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collider)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = position;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Destroy(g.GetComponent<Collider>());
    }

    void Update()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            inventory = player != null ? player.GetComponent<Inventory>() : null;
            return;
        }
        if (player.IsDead || SoulMenu.MenuOpen || PauseMenu.IsOpen || (player.transform.position - transform.position).sqrMagnitude > 3.2f * 3.2f) return;

        GameHUD.Prompt($"[G] Truhla (úroda a suroviny): {SettlementGatherers.Summary(plotId)}");
        if (!GameInput.GetKeyDown(MerchantNPC.TradeKey)) return;

        int taken = SettlementGatherers.TakeAll(plotId, inventory);
        GameHUD.Message(taken > 0 ? $"Z truhly si vzal {taken} ks surovín." : "Truhla je prázdna alebo máš plný inventár.", 2.5f);
    }
}
