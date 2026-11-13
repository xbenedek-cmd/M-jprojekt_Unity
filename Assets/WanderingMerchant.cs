using UnityEngine;

/// <summary>
/// Putujúci obchodník: s 40 % šancou sa raz za čas objaví v divočine na pokojnom mieste (pri zdroji bez démonov v okolí) a na ďalší deň odíde.
/// Predáva to, čo sa v meste kúpiť nedá: nákresy nástrojov 3. úrovne (bleskotvorné), kryštály a Posvätenú čepeľ.
/// </summary>
public class WanderingMerchant : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }
    public static int NearFrame { get; private set; } = -10;
    private static WanderingMerchant active;

    [SerializeField] private float interactionRange = 3.5f;
    [SerializeField] private float spawnChance = 0.4f;

    private struct Offer { public string label; public int price; public ToolKit.Kind kind; public bool tool; public string item; public int count; }
    private static readonly Offer[] offers =
    {
        new Offer { label = "Nákres: bleskotvorná sekera", price = 450, tool = true, kind = ToolKit.Kind.Axe },
        new Offer { label = "Nákres: bleskotvorný krompáč", price = 450, tool = true, kind = ToolKit.Kind.Pickaxe },
        new Offer { label = "Nákres: bleskotvorné kladivo", price = 450, tool = true, kind = ToolKit.Kind.Hammer },
        new Offer { label = "Bleskové kryštály ×3", price = 150, item = ItemCatalog.Crystal, count = 3 },
        new Offer { label = "Posvätená čepeľ", price = 400, item = ItemCatalog.BlessedBlade, count = 1 },
        new Offer { label = "★ Kolosov drvič (legendárny meč)", price = 2200, item = ItemCatalog.ColossusBlade, count = 1 },
        new Offer { label = "★ Elixír nesmrteľnosti (15 s)", price = 1000, item = ItemCatalog.ImmortalElixir, count = 1 }
    };

    private PlayerStats player;
    private int leaveDay = -1;
    private GUIStyle title;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { active = null; MenuOpen = false; }

    // Spawner: jeden neviditeľný objekt, ktorý v novom dni vytvorí / odstráni obchodníka
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<WanderingMerchantSpawner>() != null) return;
        new GameObject("WanderingMerchantSpawner").AddComponent<WanderingMerchantSpawner>();
    }

    public static bool Active => active != null;

    internal static void Spawn(Vector3 position, int day)
    {
        var go = new GameObject("PutujúciObchodník");
        go.transform.position = position;

        MerchantNPC visual = go.AddComponent<MerchantNPC>();   // jeho Awake postaví vzhľad; vlastný predaj vypneme
        visual.enabled = false;

        var merchant = go.AddComponent<WanderingMerchant>();
        merchant.leaveDay = day + 1;
        active = merchant;
    }

    internal static void Despawn()
    {
        if (active != null) Destroy(active.gameObject);
        active = null;
    }

    internal static int LeaveDay => active != null ? active.leaveDay : -1;
    internal static float SpawnChance => active != null ? active.spawnChance : 0.4f;

    void OnDisable() => Close();

    void Update()
    {
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        if (player == null || player.IsDead || JailManager.IsInJail) return;

        float distance = Vector3.Distance(transform.position, player.transform.position);
        if (distance <= interactionRange + 1f) NearFrame = Time.frameCount;
        if (!MenuOpen && distance <= interactionRange)
        {
            GameHUD.Prompt("[G] Putujúci obchodník");
            if (GameInput.GetKeyDown(MerchantNPC.TradeKey) && !Healer.MenuOpen && !MerchantNPC.MenuOpen)
            {
                if (SoulBalance.Rejected) { GameHUD.Message("Obchodník sa ti vyhýba: ľudskosť v tebe takmer zhasla.", 3f); return; }
                MenuOpen = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
        else if (MenuOpen && (distance > interactionRange + 1f || GameInput.GetKeyDown(KeyCode.Escape))) Close();
    }

    void Close()
    {
        if (!MenuOpen) return;
        MenuOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Buy(Offer o)
    {
        Inventory inventory = player.GetComponent<Inventory>();
        if (o.tool && ToolKit.Has(o.kind) && ToolKit.Tier(o.kind) >= 3) { GameHUD.Message("Tento nástroj už máš."); return; }
        if (!o.tool && (inventory == null || !inventory.CanAdd(o.item, o.count))) { GameHUD.Message("Inventár je plný."); return; }
        if (!player.TrySpend(o.price)) { GameHUD.Message("Nemáš dosť peňazí."); return; }

        if (o.tool)
        {
            ToolKit.Give(o.kind, 3);
            GameHUD.Message($"Získal si: {ToolKit.Name(o.kind)}.", 4f);
        }
        else
        {
            inventory.TryAdd(o.item, o.count);
            GameHUD.Message($"Kúpil si: {o.label}.");
        }
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup || !MenuOpen || player == null) return;
        if (title == null) title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        float width = 480f, height = 90f + offers.Length * 44f + 40f;
        Rect box = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.96f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x, box.y + 12f, width, 32f), "PUTUJÚCI OBCHODNÍK", title);
        GUI.Label(new Rect(box.x + 20f, box.y + 50f, width - 40f, 24f), $"Peniaze: {player.money}   (zajtra odíde)");

        for (int i = 0; i < offers.Length; i++)
            if (GUI.Button(new Rect(box.x + 25f, box.y + 84f + i * 44f, width - 50f, 38f), $"{offers[i].label}   {offers[i].price}")) Buy(offers[i]);

        GUI.Label(new Rect(box.x + 25f, box.yMax - 34f, width - 50f, 24f), "Esc: zavrieť");
    }
}

/// <summary>Raz za deň rozhodne, či sa obchodník objaví alebo odíde.</summary>
public class WanderingMerchantSpawner : MonoBehaviour
{
    void OnEnable() => DayNightCycle.OnNewDay += HandleNewDay;
    void OnDisable() => DayNightCycle.OnNewDay -= HandleNewDay;

    void HandleNewDay(int day)
    {
        if (WanderingMerchant.Active)
        {
            if (day >= WanderingMerchant.LeaveDay)
            {
                WanderingMerchant.Despawn();
                GameHUD.Message("Putujúci obchodník odišiel z divočiny.", 3f);
            }
            return;
        }
        if (Random.value > WanderingMerchant.SpawnChance) return;

        Vector3 spot;
        if (!FindCalmSpot(out spot)) return;

        WanderingMerchant.Spawn(spot, day);
        PlayerStats player = FindAnyObjectByType<PlayerStats>();
        float distance = player != null ? Vector3.Distance(player.transform.position, spot) : 0f;
        GameHUD.Message($"Do divočiny dorazil putujúci obchodník (asi {distance:0} m od teba). Predáva vzácne nákresy.", 6f);
    }

    // Pokojné miesto: pri zdroji suroviny v divočine (vzdialenom od mesta), kde nie je živý démon
    static bool FindCalmSpot(out Vector3 spot)
    {
        spot = Vector3.zero;
        ResourceNode[] nodes = FindObjectsByType<ResourceNode>();
        if (nodes.Length == 0) return false;

        for (int attempt = 0; attempt < 30; attempt++)
        {
            Vector3 p = nodes[Random.Range(0, nodes.Length)].transform.position;
            if (p.sqrMagnitude < 150f * 150f) continue;   // len divočina, nie mesto

            Vector3 candidate = p + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * 4f;
            if (!Physics.Raycast(candidate + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore)) continue;

            bool safe = true;
            foreach (DemonEnemy demon in DemonEnemy.All)
                if (demon != null && demon.Damage != null && !demon.Damage.IsDead && (demon.transform.position - hit.point).sqrMagnitude < 40f * 40f) { safe = false; break; }
            if (!safe) continue;

            spot = hit.point + Vector3.up * 0.9f;
            return true;
        }
        return false;
    }
}
