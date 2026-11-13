using UnityEngine;

/// <summary>
/// Menu démonovej prírody (len v podobe démona): kláves 5 otvorí ponuku.
///  - Zasadiť strom temnoty (krv + bylina): rastie, prorastá stavby ľudí.
///  - Skúmať pevnejšie stromy (krv + bleskové kryštály): ľudia ich ťažšie klčujú.
///  - Presun koreňmi: démon sa vynorí pri ľubovoľnom dospelom strome (krv, pauza). Démon nepoužíva portály.
/// </summary>
public class DemonNatureMenu : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }

    [SerializeField] private KeyCode openKey = KeyCode.Alpha5;
    [SerializeField] private float plantBloodCost = 25f;
    [SerializeField] private int plantHerbCost = 1;
    [SerializeField] private float researchBloodCost = 20f;
    [SerializeField] private float rootBloodCost = 10f;
    [SerializeField] private float rootCooldownSeconds = 120f;
    [Tooltip("Cestovať sa dá len v blízkosti stromu (m): korene vedú cez strom")]
    [SerializeField] private float treeReach = 5f;
    [Header("Preskočenie pauzy (platba)")]
    [SerializeField] private int skipCrystals = 2;
    [SerializeField] private int skipMoney = 150;
    [SerializeField] private float skipBlood = 60f;
    [Tooltip("Najmenšia vzdialenosť medzi stromami (m)")]
    [SerializeField] private float treeSpacing = 4f;

    private PlayerStats player;
    private Inventory inventory;
    private PlayerMovement movement;
    private float nextRootTravel;
    private string message;
    private float messageUntil;
    private GUIStyle titleStyle, textStyle;

    void Update()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null)
            {
                inventory = player.GetComponent<Inventory>();
                movement = player.GetComponent<PlayerMovement>();
            }
            return;
        }

        if (MenuOpen)
        {
            if (!player.IsDemon || player.IsDead || GameInput.GetKeyDown(KeyCode.Escape) || GameInput.GetKeyDown(openKey)) Close();
            return;
        }

        if (GameInput.GetKeyDown(openKey) && player.IsDemon && !player.IsDead && !Blocked()) Open();
    }

    static bool Blocked() => Healer.MenuOpen || JailManager.MenuOpen || JailManager.IsInJail;

    // Ciele cestovania sa zostavia raz pri otvorení menu (nie každý OnGUI): prastaré uzly prvé, potom vlastné stromy podľa vzdialenosti
    private readonly System.Collections.Generic.List<DemonTree> destinations = new System.Collections.Generic.List<DemonTree>();
    private readonly System.Collections.Generic.List<string> destinationLabels = new System.Collections.Generic.List<string>();
    private Vector2 travelScroll;

    void BuildDestinations()
    {
        destinations.Clear();
        destinationLabels.Clear();
        Vector3 here = player.transform.position;

        foreach (DemonTree tree in DemonTree.All)
            if (tree != null && tree.IsMature && (tree.transform.position - here).sqrMagnitude > 4f) destinations.Add(tree);

        destinations.Sort((a, b) =>
        {
            if (a.IsAncient != b.IsAncient) return a.IsAncient ? -1 : 1;
            return (a.transform.position - here).sqrMagnitude.CompareTo((b.transform.position - here).sqrMagnitude);
        });

        foreach (DemonTree tree in destinations)
        {
            float distance = Vector3.Distance(tree.transform.position, here);
            string kind = tree.IsAncient ? "Koreňový uzol" : "Tvoj strom";
            destinationLabels.Add($"{kind}: {ZoneLoader.ZoneNameAt(tree.transform.position)}  ({distance:0} m)");
        }
    }

    private bool nearTree;

    // Strom v dosahu: strom temnoty alebo ktorýkoľvek živý strom v Divočine (hráč pri otvorení menu stojí, menu blokuje pohyb)
    bool IsNearAnyTree()
    {
        Vector3 here = player.transform.position;
        float sq = treeReach * treeReach;
        foreach (DemonTree t in DemonTree.All)
            if (t != null && (t.transform.position - here).sqrMagnitude <= sq) return true;
        foreach (BreakableNode n in BreakableNode.All)
            if (n != null && n.IsTree && !n.IsDown && (n.transform.position - here).sqrMagnitude <= sq) return true;
        return false;
    }

    void Open()
    {
        MenuOpen = true;
        message = null;
        nearTree = IsNearAnyTree();
        BuildDestinations();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Close()
    {
        MenuOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Say(string text)
    {
        message = text;
        messageUntil = Time.unscaledTime + 3f;
    }

    private const int MaxRewardedPerDay = 5;
    private static int rewardDay = -1, rewardCount;

    // Odmena za strom je raz denne obmedzená na MaxRewardedPerDay stromov
    static bool TreeRewardAllowed()
    {
        DayNightCycle clock = FindAnyObjectByType<DayNightCycle>();
        int day = clock != null ? clock.Day : 0;
        if (day != rewardDay) { rewardDay = day; rewardCount = 0; }
        if (rewardCount >= MaxRewardedPerDay) return false;
        rewardCount++;
        return true;
    }

    /// <summary>Odmena za starostlivosť o prírodu: buď peniaze, buď XP (nie oboje) a kúsok duše. Vráti text do hlášky.</summary>
    public static string NatureReward(PlayerStats player, int money, int xp, float soul)
    {
        player.AddSoulProgress(soul);
        if (Random.value < 0.5f) { player.AddMoney(money); return $" (+{money} peňazí)"; }
        player.AddDemonXp(xp);
        return $" (+{xp} XP)";
    }

    string GiveNatureReward(int money, int xp, float soul) => NatureReward(player, money, xp, soul);

    // ---------- Akcie ----------
    void Plant()
    {
        if (player.Blood < plantBloodCost) { Say($"Chýba krv ({plantBloodCost:0})."); return; }
        if (inventory == null || inventory.CountOf(ItemCatalog.Herb) < plantHerbCost) { Say($"Chýba {ItemCatalog.Get(ItemCatalog.Herb).displayName} ({plantHerbCost})."); return; }

        Vector3 position = player.transform.position + player.transform.forward * 2.5f;
        if (Physics.Raycast(position + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore))
            position.y = hit.point.y;
        else position.y = player.transform.position.y - 0.9f;

        foreach (DemonTree other in DemonTree.All)
            if ((other.transform.position - position).sqrMagnitude < treeSpacing * treeSpacing) { Say("Príliš blízko iného stromu."); return; }

        if (!player.TrySpendBlood(plantBloodCost)) { Say($"Chýba krv ({plantBloodCost:0})."); return; }
        inventory.TryRemove(ItemCatalog.Herb, plantHerbCost);
        DemonTree planted = DemonTree.Plant(position);

        // Starostlivosť o prírodu: buď peniaze, buď XP (+ kúsok duše); najviac MaxRewardedPerDay stromov za deň, aby sa to nedalo farmiť
        string reward = "";
        if (TreeRewardAllowed())
        {
            planted.rewarded = true;
            reward = GiveNatureReward(20, 15, 0.5f);
        }
        else reward = " (denný limit odmien za stromy je vyčerpaný)";
        Say($"Strom temnoty zasadený{reward}. Rastie a o pár dní prerastie okolie.");
    }

    void Research()
    {
        if (!DemonNature.CanResearch) { Say("Všetky vylepšenia sú preskúmané."); return; }

        int crystals = DemonNature.Hardness + 1;
        if (player.Blood < researchBloodCost) { Say($"Chýba krv ({researchBloodCost:0})."); return; }
        if (inventory == null || inventory.CountOf(ItemCatalog.Crystal) < crystals) { Say($"Chýba Bleskový kryštál ({crystals})."); return; }

        if (!player.TrySpendBlood(researchBloodCost)) return;
        inventory.TryRemove(ItemCatalog.Crystal, crystals);
        DemonNature.Research();
        Say($"Preskúmané: pevnejšie stromy (úroveň {DemonNature.Hardness}).");
    }

    private enum SkipPay { None, Crystal, Money, Blood }
    private SkipPay skipChoice = SkipPay.Blood;

    void TravelTo(DemonTree tree, SkipPay pay = SkipPay.None)
    {
        if (tree == null) return;
        if (!nearTree) { Say($"Korene vedú len cez strom: choď bližšie ako {treeReach:0} m k stromu."); return; }

        bool onCooldown = Time.time < nextRootTravel;
        if (onCooldown && pay == SkipPay.None) { Say($"Korene sa ešte regenerujú ({nextRootTravel - Time.time:0} s). Pauzu môžeš preskočiť platbou."); return; }

        // Všetko sa overí vopred, aby sa pri nedostatku niečoho nič nestrhlo
        float bloodNeeded = rootBloodCost + (onCooldown && pay == SkipPay.Blood ? skipBlood : 0f);
        if (player.Blood < bloodNeeded) { Say($"Chýba krv ({bloodNeeded:0})."); return; }
        if (onCooldown && pay == SkipPay.Crystal && (inventory == null || inventory.CountOf(ItemCatalog.Crystal) < skipCrystals)) { Say($"Chýba Bleskový kryštál ({skipCrystals})."); return; }
        if (onCooldown && pay == SkipPay.Money && player.money < skipMoney) { Say($"Chýbajú peniaze ({skipMoney})."); return; }

        player.TrySpendBlood(bloodNeeded);
        if (onCooldown && pay == SkipPay.Crystal) inventory.TryRemove(ItemCatalog.Crystal, skipCrystals);
        else if (onCooldown && pay == SkipPay.Money) player.TrySpend(skipMoney);

        nextRootTravel = Time.time + rootCooldownSeconds;
        Vector3 arrival = tree.transform.position + tree.transform.forward * (1.5f + tree.Stage * 0.4f) + Vector3.up * 1f;
        if (movement != null) movement.TeleportGrounded(arrival, tree.transform.forward);
        Close();
        GameHUD.Message("Vynoril si sa z koreňov.", 2f);
    }

    // ---------- Zobrazenie ----------
    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!MenuOpen || player == null) return;

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
        }

        float w = 600f, h = 580f;
        Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.92f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(box.x + 20f, box.y + 14f, w - 40f, h - 28f));
        GUILayout.Label("Príroda démona", titleStyle);
        GUILayout.Label($"Krv: {player.Blood:0}   Bylina: {inventory?.CountOf(ItemCatalog.Herb)}   Kryštál: {inventory?.CountOf(ItemCatalog.Crystal)}   Stromy: {DemonTree.All.Count}   Pevnosť: {DemonNature.Hardness}/{DemonNature.MaxHardness}", textStyle);
        GUILayout.Space(8f);

        if (GUILayout.Button($"Zasadiť strom temnoty ({plantBloodCost:0} krvi, bylina {plantHerbCost})", GUILayout.Height(30f))) Plant();

        string researchText = DemonNature.CanResearch
            ? $"Skúmať pevnejšie stromy ({researchBloodCost:0} krvi, kryštál {DemonNature.Hardness + 1})"
            : "Pevnejšie stromy: preskúmané";
        GUI.enabled = DemonNature.CanResearch;
        if (GUILayout.Button(researchText, GUILayout.Height(30f))) Research();
        GUI.enabled = true;

        GUILayout.Space(8f);
        GUILayout.Label($"Cestovanie koreňmi ({rootBloodCost:0} krvi, pauza {rootCooldownSeconds:0} s): prastaré uzly a tvoje dospelé stromy", textStyle);
        if (!nearTree) GUILayout.Label($"Si ďaleko od stromu. Korene sa dajú použiť len do {treeReach:0} m od ľubovoľného stromu.", textStyle);

        float wait = nextRootTravel - Time.time;
        bool cooling = wait > 0f;
        if (cooling)
        {
            GUILayout.Label($"Korene sa regenerujú ešte {wait:0} s. Pauzu preskočíš platbou (použije sa pri výbere cieľa):", textStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(skipChoice == SkipPay.Crystal, $"{skipCrystals}× kryštál", "Button")) skipChoice = SkipPay.Crystal;
            if (GUILayout.Toggle(skipChoice == SkipPay.Money, $"{skipMoney} peňazí", "Button")) skipChoice = SkipPay.Money;
            if (GUILayout.Toggle(skipChoice == SkipPay.Blood, $"+{skipBlood:0} krvi", "Button")) skipChoice = SkipPay.Blood;
            GUILayout.EndHorizontal();
        }

        travelScroll = GUILayout.BeginScrollView(travelScroll, GUILayout.Height(cooling ? 120f : 170f));
        GUI.enabled = nearTree;
        for (int i = 0; i < destinations.Count; i++)
            if (GUILayout.Button(destinationLabels[i], GUILayout.Height(26f))) TravelTo(destinations[i], cooling ? skipChoice : SkipPay.None);
        GUI.enabled = true;
        if (destinations.Count == 0) GUILayout.Label("Zatiaľ nie je kam: žiadny koreňový uzol ani dospelý strom (tvoje stromy dorastú za niekoľko dní).", textStyle);
        GUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        if (message != null && Time.unscaledTime < messageUntil) GUILayout.Label(message, textStyle);
        if (GUILayout.Button("Zavrieť", GUILayout.Height(28f))) Close();
        GUILayout.EndArea();
    }
}
