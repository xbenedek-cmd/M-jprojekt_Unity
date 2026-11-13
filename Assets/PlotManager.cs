using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ovládanie stavania: pri parcele sa zobrazí výzva, kláves otvorí ponuku (kúpa parcely, výber stavby, odstránenie ruín).
/// Drží aj modely ruín (tehlové múry z Assets/Materials/Stavby_do_hry), ktoré používajú zničené parcely.
/// </summary>
public class PlotManager : MonoBehaviour
{
    public static PlotManager Instance { get; private set; }
    public static bool MenuOpen { get; private set; }
    public static GameObject[] RuinPrefabs => Instance != null ? Instance.ruinPrefabs : null;
    /// <summary>Kamenný materiál pre modely ruín (modely sú bez textúry, bez neho sú biele).</summary>
    public static Material RuinMaterial => Instance != null ? Instance.ruinMaterial : null;
    [SerializeField] private Material ruinMaterial;
    public void SetRuinMaterial(Material m) { ruinMaterial = m; }
    /// <summary>Model stoličky do domu (alebo null: stolička z kvádrov) a natočenie, ak je model k stolu otočený chrbtom.</summary>
    public static GameObject ChairPrefab => Instance != null ? Instance.chairPrefab : null;
    public static float ChairYawOffset => Instance != null ? Instance.chairYawOffset : 0f;

    /// <summary>Modely na stavbu: trosky (čistenie ruín), trám (lešenie) a krb (do domu); natočenie krbu, ak model nesmeruje čelom k dverám.</summary>
    public static GameObject[] RubblePrefabs => Instance != null ? Instance.rubblePrefabs : null;
    public static GameObject BeamPrefab => Instance != null ? Instance.beamPrefab : null;
    public static GameObject FireplacePrefab => Instance != null ? Instance.fireplacePrefab : null;
    public static float FireplaceYaw => Instance != null ? Instance.fireplaceYaw : 180f;
    [SerializeField] private GameObject[] rubblePrefabs;
    [SerializeField] private GameObject beamPrefab;
    [SerializeField] private GameObject fireplacePrefab;
    [SerializeField] private float fireplaceYaw = 180f;
    public void SetConstruction(GameObject[] rubble, GameObject beam, GameObject fireplace) { rubblePrefabs = rubble; beamPrefab = beam; fireplacePrefab = fireplace; }

    /// <summary>Dekorácie (debny, vrecia s pieskom, stoličky...) pre stavby danej frakcie: vlastné + spoločné; null, ak nie sú.</summary>
    public static GameObject[] PropsFor(FactionId faction)
    {
        if (Instance == null) return null;
        GameObject[] own = faction == FactionId.Rebels ? Instance.rebelProps : faction == FactionId.Megacorp ? Instance.corpProps : Instance.civilianProps;
        if (own == null || own.Length == 0) return Instance.commonProps;
        return own;
    }

    private KeyCode openKey => KeyBindings.Get("plot", KeyCode.P);
    [Tooltip("Do akej vzdialenosti od okraja parcely sa dá ovládať")]
    [SerializeField] private float reach = 3f;
    [Tooltip("Voliteľné modely ruín (tehlový múr, oblúk dverí, okno, komín); bez nich sa použijú kvádre")]
    [SerializeField] private GameObject[] ruinPrefabs;
    [Tooltip("Model stoličky (Wooden_Chair); prázdne = stolička z kvádrov")]
    [SerializeField] private GameObject chairPrefab;
    [Tooltip("Ak sú stoličky otočené chrbtom k stolu, daj 180 (alebo 90 / -90)")]
    [SerializeField] private float chairYawOffset;
    [Header("Dekorácie okolo stavieb (modely z Stavby_do_hry)")]
    [SerializeField] private GameObject[] rebelProps;
    [SerializeField] private GameObject[] corpProps;
    [SerializeField] private GameObject[] civilianProps;
    [SerializeField] private GameObject[] commonProps;

    private PlayerStats player;
    private Inventory inventory;
    private BuildPlot near;
    private BuildPlot open;
    private string message;
    private float messageUntil;
    private readonly List<BuildingDef> options = new List<BuildingDef>();
    private GUIStyle titleStyle, textStyle, hintStyle, labelSmall;
    private Vector2 scroll;

    public void SetRuinPrefabs(GameObject[] prefabs) => ruinPrefabs = prefabs;
    public void SetChair(GameObject prefab) => chairPrefab = prefab;

    public void SetDecor(GameObject[] rebel, GameObject[] corp, GameObject[] civilian, GameObject[] common)
    {
        rebelProps = rebel;
        corpProps = corp;
        civilianProps = civilian;
        commonProps = common;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) { Instance = null; MenuOpen = false; }
    }

    void Update()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null) inventory = player.GetComponent<Inventory>();
            return;
        }

        near = FindNear();
        labelPlot = FindNear(LabelRange);

        if (MenuOpen)
        {
            if (open == null || near != open || GameInput.GetKeyDown(KeyCode.Escape) || GameInput.GetKeyDown(openKey) || player.IsDead) Close();
            return;
        }

        if (near != null && GameInput.GetKeyDown(openKey) && !Blocked())
        {
            if (player.IsDemon) GameHUD.Message("Démon nestavia parcely – iba stromy a prírodu (5). Budovy môže ničiť.", 3f);
            else Open(near);
        }
    }

    static bool Blocked() => JailManager.MenuOpen || JailManager.IsInJail || Wardrobe.MenuOpen || Inventory.MenuOpen || PlayerAppearance.MenuOpen
                             || DetectiveSystem.MenuOpen || MerchantNPC.MenuOpen || GameHUD.MenuOpen;

    // Popisok parcely je vidno do 10 m od jej okraja; ovládať (P) sa dá len z dosahu `reach`
    private const float LabelRange = 10f;
    private BuildPlot labelPlot;

    BuildPlot FindNear() => FindNear(reach);

    BuildPlot FindNear(float reach)
    {
        BuildPlot best = null;
        float bestDistance = float.MaxValue;
        Vector3 p = player.transform.position;

        foreach (BuildPlot plot in BuildPlot.All)
        {
            Vector3 d = plot.transform.position - p;
            d.y = 0f;
            float edge = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z)) - plot.Side * 0.5f;   // vzdialenosť od okraja štvorca
            if (edge <= reach && d.sqrMagnitude < bestDistance)
            {
                best = plot;
                bestDistance = d.sqrMagnitude;
            }
        }
        return best;
    }

    void Open(BuildPlot plot)
    {
        open = plot;
        MenuOpen = true;
        message = null;
        scroll = Vector2.zero;
        BuildingCatalog.ForFaction(plot.Owner, options);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Close()
    {
        MenuOpen = false;
        open = null;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Say(string text)
    {
        message = text;
        messageUntil = Time.unscaledTime + 3f;
    }

    // ---------- Zobrazenie ----------
    void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
        hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        hintStyle.normal.textColor = Color.white;
        labelSmall = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        labelSmall.normal.textColor = new Color(0.85f, 0.9f, 1f);
    }

    // Popisok parcely tesne nad ňou (len keď je hráč pri nej): [P] Parcela, kde leží, čo je na nej a koľko stojí
    void DrawPlotLabel(BuildPlot plot, bool inReach)
    {
        string status;
        switch (plot.State)
        {
            case PlotState.ForSale:
                status = $"na predaj: {plot.Price} peňazí" + (Reputation.Get(plot.Owner) < plot.RequiredTrust ? $" (treba dôveru {plot.RequiredTrust:0})" : "");
                break;
            case PlotState.Clearing: status = "čistí sa"; break;
            case PlotState.Ready: status = "pripravená na stavbu"; break;
            case PlotState.Building: status = $"stavia sa: {plot.Building?.displayName} ({plot.Progress01 * 100f:0} %)"; break;
            case PlotState.Built: status = $"{plot.Building?.displayName} (odolnosť {plot.Health01 * 100f:0} %)"; break;
            default: status = plot.Contaminated ? "KONTAMINOVANÁ pôda (treba rituál)" : $"ruiny, odstránenie {plot.RuinFee} peňazí"; break;
        }

        string line1 = inReach ? $"[{openKey}] Parcela {plot.PlotId}" : $"Parcela {plot.PlotId}";
        string line2 = $"{plot.ZoneName} · {FactionName(plot.Owner)} · {status}";

        Camera cam = Camera.main;
        Vector3 screen = cam != null ? cam.WorldToScreenPoint(plot.transform.position + Vector3.up * 3.5f) : Vector3.zero;
        float x = screen.z > 0f ? screen.x : Screen.width * 0.5f;
        float y = screen.z > 0f ? Screen.height - screen.y : Screen.height - 140f;

        float w = 440f, h = 52f;
        Rect box = new Rect(Mathf.Clamp(x - w * 0.5f, 8f, Screen.width - w - 8f), Mathf.Clamp(y - h, 8f, Screen.height - h - 8f), w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(box.x, box.y + 3f, w, 24f), line1, hintStyle);
        GUI.Label(new Rect(box.x, box.y + 26f, w, 22f), line2, labelSmall);
    }

    static string FactionName(FactionId f) =>
        f == FactionId.Rebels ? "Odboj" : f == FactionId.Megacorp ? "Megakorporácia" : f == FactionId.Civilians ? "Civilisti" : "Spoločné";

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (player == null) return;
        EnsureStyles();

        if (!MenuOpen)
        {
            if (labelPlot != null && !Blocked() && !PauseMenu.IsOpen && !MangaDialog.Active) DrawPlotLabel(labelPlot, labelPlot == near);
            return;
        }

        if (open == null) return;

        float w = 580f, h = Mathf.Min(Screen.height - 40f, 600f);
        Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.92f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(box.x + 20f, box.y + 14f, w - 40f, h - 28f));
        GUILayout.Label($"Parcela {open.PlotId} – {FactionName(open.Owner)}", titleStyle);
        GUILayout.Label($"Peniaze: {player.money}   Dôvera ({FactionName(open.Owner)}): {Reputation.Get(open.Owner):0}   Civilisti: {CityPopulation.AliveCivilians()} (stavba ×{CityPopulation.ConstructionTimeFactor:0.0} dlhšie)", textStyle);
        GUILayout.Space(8f);
        scroll = GUILayout.BeginScrollView(scroll, false, true, GUILayout.ExpandHeight(true));   // dlhý zoznam stavieb sa dá posúvať (koliesko myši)

        switch (open.State)
        {
            case PlotState.ForSale:
                GUILayout.Label($"Parcela na predaj. Cena {open.Price}, vyžaduje dôveru {open.RequiredTrust:0}. Po kúpe sa musí vyčistiť.", textStyle);
                if (GUILayout.Button($"Kúpiť parcelu ({open.Price})", GUILayout.Height(34f)))
                {
                    if (open.Buy(player, out string msg)) Say("Parcela kúpená, začína sa čistenie."); else Say(msg);
                }
                break;

            case PlotState.Clearing:
                GUILayout.Label($"Parcela sa čistí… zostáva {open.SecondsLeft:0} s.", textStyle);
                break;

            case PlotState.Ready:
                GUILayout.Label("Parcela je pripravená. Vyber stavbu:", textStyle);
                foreach (BuildingDef def in options) DrawOption(def);
                break;

            case PlotState.Building:
                GUILayout.Label($"Stavba: {open.Building?.displayName}. Zostáva {open.SecondsLeft:0} s ({open.Progress01 * 100f:0} %).", textStyle);
                break;

            case PlotState.Built:
                GUILayout.Label($"Stojí: {open.Building?.displayName}. {open.Building?.description}  Odolnosť: {open.Health01 * 100f:0} %", textStyle);
                if (open.Building != null && open.Building.id == BuildingCatalog.Farm)
                {
                    string farmStatus = null;
                    FarmSystem.DrawMenu(open, player, textStyle, ref farmStatus);
                    if (farmStatus != null) Say(farmStatus);
                }
                break;

            case PlotState.Ruined:
                if (open.Contaminated)
                {
                    GUILayout.Label(open.Cleansing
                        ? $"Prebieha očistný rituál… zostáva {open.CleanseLeft:0} s."
                        : $"Pôda je kontaminovaná po opakovaných nočných útokoch démonov. Obnova vyžaduje drahý očistný rituál: {open.CleanseMoney} peňazí, 3 bleskové kryštály a 1 zlato (trvá {BuildPlot.CleanseSeconds:0} s).", textStyle);
                    GUI.enabled = !open.Cleansing;
                    if (GUILayout.Button($"Vykonať očistný rituál ({open.CleanseMoney} + 3 kryštály + zlato)", GUILayout.Height(34f)))
                    {
                        if (open.Cleanse(player, inventory, out string cmsg)) Say("Rituál sa začal."); else if (cmsg != null) Say(cmsg);
                    }
                    GUI.enabled = true;
                    break;
                }
                GUILayout.Label($"Na parcele sú ruiny ({open.Building?.displayName}). Odstránenie ruín stojí {open.RuinFee}, potom sa parcela znova čistí.", textStyle);
                if (GUILayout.Button($"Odstrániť ruiny ({open.RuinFee})", GUILayout.Height(34f)))
                {
                    if (open.RemoveRuins(player, out string msg)) Say("Ruiny sa odstraňujú."); else if (msg != null) Say(msg);
                }
                break;
        }

        GUILayout.EndScrollView();
        GUILayout.Space(6f);
        if (message != null && Time.unscaledTime < messageUntil) GUILayout.Label(message, textStyle);
        if (GUILayout.Button("Zavrieť", GUILayout.Height(28f))) Close();
        GUILayout.EndArea();
    }

    void DrawOption(BuildingDef def)
    {
        bool ok = open.CanBuild(def, player, inventory, out string reason);

        string cost = $"{def.cost}";
        if (def.wood > 0) cost += $", drevo {def.wood}";
        if (def.stone > 0) cost += $", kameň {def.stone}";
        if (def.scrap > 0) cost += $", šrot {def.scrap}";
        if (def.crystal > 0) cost += $", kryštál {def.crystal}";
        float seconds = def.buildSeconds * CityPopulation.ConstructionTimeFactor * ProfessionTrust.BuildSpeedFactor;

        GUILayout.Label($"{def.displayName}: {def.description}\n   Cena: {cost}. Čas: {seconds:0} s." + (ok ? "" : $"  Chýba: {reason}"), textStyle);
        GUI.enabled = ok;
        if (GUILayout.Button($"Postaviť: {def.displayName}", GUILayout.Height(28f)))
        {
            if (open.StartBuilding(def, player, inventory, out string msg)) Say("Stavba sa začala.");
            else if (msg != null) Say(msg);
        }
        GUI.enabled = true;
    }
}
