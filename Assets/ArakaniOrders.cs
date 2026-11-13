using UnityEngine;

/// <summary>
/// Nočné rozkazy Arakaniho, ktoré sa generujú samy (netreba ich ručne umiestňovať do scény): zabíjanie vojakov, búranie stavieb ľudí,
/// otvorenie brány mesta, sabotáž bane a infiltrácia námestia. Ak už v scéne nejaké OrderTarget sú, ostanú popri nich.
/// ArakaniFavor si z nich každú noc vyberie jeden podľa dňa.
/// </summary>
public class ArakaniOrders : MonoBehaviour
{
    private const string RootName = "ArakaniRozkazy";
    private float buildAt = -1f;
    private bool built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<ArakaniOrders>() != null) return;
        new GameObject("ArakaniOrders").AddComponent<ArakaniOrders>();
    }

    void Update()
    {
        if (built || PauseMenu.AtStartup) return;
        if (buildAt < 0f) { buildAt = Time.time + 8f; return; }   // nech sa najprv načíta svet a zóny
        if (Time.time < buildAt) return;

        built = true;
        if (GameObject.Find(RootName) != null) return;
        Build();
    }

    static OrderTarget Make(Transform root, string name, Vector3 position, string text, OrderAction action, int minDay)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = position;

        var target = go.AddComponent<OrderTarget>();
        target.orderText = text;
        target.action = action;
        target.minDay = minDay;
        target.repeatable = true;
        return target;
    }

    void Build()
    {
        var root = new GameObject(RootName).transform;
        Vector3 fallback = FindAnyObjectByType<PlayerStats>() != null ? FindAnyObjectByType<PlayerStats>().transform.position : Vector3.zero;

        // 1) Zabi vojakov Megakorporácie
        OrderTarget soldiers = Make(root, "Rozkaz_Vojaci", fallback, "Zabi 4 vojakov Megakorporácie.", OrderAction.KillSoldiers, 1);
        soldiers.requiredKills = 4;
        soldiers.favorReward = 8f; soldiers.xpReward = 40; soldiers.moneyReward = 60;
        soldiers.humanFaction = FactionId.Megacorp; soldiers.humanTrustPenalty = 6f; soldiers.suspicion = 12f;

        // 2) Zruš stavbu ľudí
        OrderTarget buildings = Make(root, "Rozkaz_Stavba", fallback, "Zrovnaj so zemou jednu ľudskú stavbu. Mesto sa nesmie cítiť bezpečne.", OrderAction.DestroyBuildings, 3);
        buildings.requiredKills = 1;
        buildings.favorReward = 12f; buildings.xpReward = 60; buildings.moneyReward = 80;
        buildings.humanFaction = FactionId.Civilians; buildings.humanTrustPenalty = 8f; buildings.suspicion = 15f;

        // 3) Otvor bránu mesta (portál do Megakorporácie)
        GameObject gate = GameObject.Find("Portal_DoMegakorporacie");
        if (gate != null)
        {
            OrderTarget open = Make(root, "Rozkaz_Brana", gate.transform.position, "Otvor bránu mesta zvnútra: drž F pri portáli do Megakorporácie.", OrderAction.Interact, 2);
            open.holdSeconds = 8f; open.radius = 4.5f;
            open.favorReward = 10f; open.xpReward = 50; open.moneyReward = 0;
            open.humanFaction = FactionId.Megacorp; open.humanTrustPenalty = 5f; open.suspicion = 20f;
        }

        // 4) Sabotáž bane
        if (MineEntrance.All.Count > 0 && MineEntrance.All[0] != null)
        {
            OrderTarget mine = Make(root, "Rozkaz_Bana", MineEntrance.All[0].transform.position, "Poškoď baňu: drž F pri vchode do Hlbinnej Bane.", OrderAction.Interact, 4);
            mine.holdSeconds = 10f; mine.radius = 5f;
            mine.favorReward = 14f; mine.xpReward = 70; mine.moneyReward = 0;
            mine.humanFaction = FactionId.Megacorp; mine.humanTrustPenalty = 8f; mine.suspicion = 18f;
        }

        // 5) Infiltrácia námestia
        if (ZoneLoader.TryGetGround("Zem_Centrum", out Bounds centrum))
        {
            OrderTarget square = Make(root, "Rozkaz_Namestie", new Vector3(centrum.center.x, centrum.max.y, centrum.center.z), "Preniknúť na námestie v Centre mesta a zotrvať tam, kým ťa nikto nepozná.", OrderAction.Reach, 2);
            square.holdSeconds = 10f; square.radius = 12f;
            square.favorReward = 8f; square.xpReward = 40; square.moneyReward = 40;
            square.humanFaction = FactionId.Civilians; square.humanTrustPenalty = 4f; square.suspicion = 8f;
        }
    }
}
