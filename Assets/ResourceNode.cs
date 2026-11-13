using UnityEngine;

/// <summary>
/// Zdroj suroviny vo svete (Divočina: drevo, kameň, byliny, kryštály, šrot). Pri priblížení sa zobrazí výzva, kláves gatherKey
/// surovinu zoberie do inventára. Zdroj sa po čase obnoví. Pri viacerých zdrojoch v dosahu sa použije najbližší.
/// </summary>
public class ResourceNode : MonoBehaviour
{
    [SerializeField] private string itemId = ItemCatalog.Wood;
    [SerializeField] private int minAmount = 1;
    [SerializeField] private int maxAmount = 3;
    [SerializeField] private float range = 2.4f;
    [SerializeField] private float respawnSeconds = 120f;
    private KeyCode gatherKey => KeyBindings.Get("use", KeyCode.F);

    // Najbližší zdroj v dosahu v tomto snímku (vyberie sa v Update, použije v LateUpdate a OnGUI)
    private static ResourceNode best;
    private static float bestDistance;
    private static int bestFrame = -1;
    private static PlayerStats player;
    private static Inventory inventory;
    private static float nextPlayerSearch;

    private Renderer[] renderers;
    private Collider[] colliders;
    private float regrowAt;
    private bool depleted;

    public static readonly System.Collections.Generic.List<ResourceNode> All = new System.Collections.Generic.List<ResourceNode>();

    public string ItemId => itemId;

    /// <summary>Zber zberačom osady (bez hráča): vráti množstvo, alebo 0, ak je zdroj vyčerpaný. Zdroj sa po čase obnoví ako pri zbere hráčom.</summary>
    public int AutoHarvest()
    {
        if (depleted || (breakable != null && breakable.IsDown)) return 0;
        int amount = Random.Range(minAmount, maxAmount + 1);
        SetDepleted(true);
        regrowAt = Time.time + respawnSeconds;
        return amount;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    public void SetRange(float value) => range = value;

    // Skala, ktorá sa dá aj zbierať (F), aj ničiť krompáčom: oba systémy sa musia zhodnúť, či je objekt ešte na svete
    private BreakableNode breakable;
    public bool IsDepleted => depleted;
    public void LinkBreakable() => breakable = GetComponent<BreakableNode>();

    public void Deplete()
    {
        SetDepleted(true);
        regrowAt = Time.time + respawnSeconds;
    }

    public void Configure(string id, int min, int max)
    {
        itemId = id;
        minAmount = min;
        maxAmount = max;
    }

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        breakable = GetComponent<BreakableNode>();
    }

    void Update()
    {
        if (depleted)
        {
            if (Time.time >= regrowAt && (breakable == null || !breakable.IsDown)) SetDepleted(false);
            return;
        }

        if (bestFrame != Time.frameCount)
        {
            bestFrame = Time.frameCount;
            best = null;
            bestDistance = float.MaxValue;
        }

        if (!FindPlayer() || player.IsDead || JailManager.MenuOpen || JailManager.IsInJail) return;

        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        float d = to.magnitude;
        if (d <= range && d < bestDistance)
        {
            best = this;
            bestDistance = d;
        }
    }

    void LateUpdate()
    {
        if (best != this || depleted || bestFrame != Time.frameCount) return;
        if (!GameInput.GetKeyDown(gatherKey)) return;

        ItemDef def = ItemCatalog.Get(itemId);
        if (def == null || inventory == null) return;

        int amount = Random.Range(minAmount, maxAmount + 1);
        if (!inventory.TryAdd(itemId, amount))
        {
            GameHUD.Message($"Inventár je plný: {def.displayName}", 2f);
            return;
        }

        GameHUD.Message($"Zobrané: {def.displayName} x{amount}", 2f);
        SetDepleted(true);
        if (player != null && player.IsDemon) PopulationPressure.AddNatureDamage(1f);   // ničenie divočiny v podobe démona zvyšuje tlak na civilistov
        regrowAt = Time.time + respawnSeconds;
    }

    static bool FindPlayer()
    {
        if (player != null && inventory != null) return true;
        if (Time.unscaledTime < nextPlayerSearch) return false;
        nextPlayerSearch = Time.unscaledTime + 1f;

        player = FindAnyObjectByType<PlayerStats>();
        inventory = player != null ? player.GetComponent<Inventory>() : null;
        return player != null && inventory != null;
    }

    void SetDepleted(bool value)
    {
        depleted = value;
        if (value) RenderDebug.Hidden(this, "ResourceNode vyčerpaný (Renderer vypnutý)");
        foreach (Renderer r in renderers) if (r != null) r.enabled = !value;
        foreach (Collider c in colliders) if (c != null) c.enabled = !value;
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (best != this || depleted) return;

        ItemDef def = ItemCatalog.Get(itemId);
        Camera cam = Camera.main;
        if (def == null || cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.2f);
        if (p.z <= 0f) return;

        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(p.x - 150f, Screen.height - p.y - 14f, 300f, 28f), $"[{gatherKey}] Zbierať: {def.displayName}", style);
    }
}
