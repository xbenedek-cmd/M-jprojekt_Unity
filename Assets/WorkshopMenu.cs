using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dielňa odboja: pri postavenej dielni (parcela s budovou "Dielňa odboja") kláves 8 otvorí menu výroby a vylepšovania.
///  - Vylepšenia zbrane (3 druhy × 3 úrovne): ostrenie čepele, dlhšia rukoväť, bleskový nabíjač (WeaponUpgrades).
///  - Výroba: lektvary z byliniek a surovín, oceľový meč, posvätená čepeľ.
/// Suroviny z Divočín (drevo, kameň, kryštál, šrot, bylina) tak slúžia aj v boji, nie len na stavbu.
/// </summary>
public class WorkshopMenu : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }

    [SerializeField] private KeyCode openKey = KeyCode.Alpha8;
    [SerializeField] private float reachFromEdge = 5f;

    private class Recipe
    {
        public string name, description;
        public int money, wood, stone, scrap, crystal, herb, gold;
        public string resultItem;                        // výroba predmetu
        public WeaponUpgrades.Kind? upgrade;             // alebo vylepšenie zbrane
        public int toolTier = -1;                        // výroba nástroja (typ 0–3)
        public ToolKit.Kind toolKind;                    // ktorý nástroj (sekera / krompáč / kladivo)
        public bool toolUpgrade, toolRepair;             // vylepšenie / oprava sekery
    }

    private readonly List<Recipe> recipes = new List<Recipe>();
    private PlayerStats player;
    private Inventory inventory;
    private BuildPlot nearWorkshop;
    private Vector2 scroll;
    private string message;
    private float messageUntil;
    private GUIStyle titleStyle, textStyle, hintStyle;

    void Awake() => BuildRecipes();

    void BuildRecipes()
    {
        recipes.Add(new Recipe { name = "Liečivý lektvar", description = "Vylieči 40 HP.", herb = 2, wood = 1, resultItem = ItemCatalog.HealthPotion });
        recipes.Add(new Recipe { name = "Lektvar výdrže", description = "Úplne obnoví výdrž.", herb = 1, scrap = 1, resultItem = ItemCatalog.StaminaPotion });
        recipes.Add(new Recipe { name = "Tmavá kapucňa", description = "Hlava: menší dosah odhalenia.", scrap = 2, herb = 1, resultItem = ItemCatalog.Hood });
        recipes.Add(new Recipe { name = "Kožená bunda", description = "Vrch: -5 % poškodenia.", money = 30, scrap = 4, herb = 2, resultItem = ItemCatalog.LeatherJacket });
        recipes.Add(new Recipe { name = "Vojenská vesta", description = "Vrch: -9 % poškodenia.", money = 90, scrap = 8, crystal = 1, resultItem = ItemCatalog.MilitaryVest });
        recipes.Add(new Recipe { name = "Pracovné nohavice", description = "Spodok: +8 % výdrže.", money = 20, scrap = 3, resultItem = ItemCatalog.WorkPants });
        recipes.Add(new Recipe { name = "Taktické nohavice", description = "Spodok: +5 % výdrže, kratší úhyb.", money = 60, scrap = 5, herb = 1, resultItem = ItemCatalog.TacticalPants });
        recipes.Add(new Recipe { name = "Tiché topánky", description = "Topánky: rýchly pohyb v čupe.", money = 40, scrap = 3, herb = 2, resultItem = ItemCatalog.QuietBoots });
        recipes.Add(new Recipe { name = "Vojenské čižmy", description = "Topánky: +5 % rýchlosť, ochrana.", money = 70, scrap = 6, crystal = 1, resultItem = ItemCatalog.MilitaryBoots });
        recipes.Add(new Recipe { name = "Filtračná maska", description = "Chráni pred popolovou búrkou (stačí ju mať v inventári).", herb = 2, scrap = 3, resultItem = ItemCatalog.Mask });
        recipes.Add(new Recipe { name = "Sérum proti pachu", description = "60 s ťa spozorujú z menšej vzdialenosti.", herb = 3, crystal = 1, resultItem = ItemCatalog.ScentSerum });
        recipes.Add(new Recipe { name = "Krvavá fľaša", description = "Doplní 30 krvi (len démon).", herb = 2, stone = 1, resultItem = ItemCatalog.BloodVial });
        recipes.Add(new Recipe { name = "Oceľový meč", description = "Silnejší meč s dlhším dosahom.", money = 60, scrap = 8, stone = 4, resultItem = ItemCatalog.SteelSword });
        recipes.Add(new Recipe { name = "Posvätená čepeľ", description = "Zasiahne aj nezraniteľných vyšších démonov.", money = 100, scrap = 6, crystal = 3, resultItem = ItemCatalog.BlessedBlade });
    }

    void Update()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null) inventory = player.GetComponent<Inventory>();
            return;
        }

        nearWorkshop = player.IsDead ? null : FindWorkshop();

        if (MenuOpen)
        {
            if (nearWorkshop == null || GameInput.GetKeyDown(KeyCode.Escape) || GameInput.GetKeyDown(openKey)) Close();
            return;
        }

        if (nearWorkshop != null && GameInput.GetKeyDown(openKey) && !Blocked()) Open();
    }

    static bool Blocked() => Healer.MenuOpen || JailManager.MenuOpen || JailManager.IsInJail;

    BuildPlot FindWorkshop()
    {
        Vector3 p = player.transform.position;
        foreach (BuildPlot plot in BuildPlot.All)
        {
            if (plot.State != PlotState.Built || plot.Building == null || plot.Building.id != BuildingCatalog.RebelWorkshop) continue;

            Vector3 d = plot.transform.position - p;
            d.y = 0f;
            if (Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z)) - plot.Side * 0.5f <= reachFromEdge) return plot;
        }
        return null;
    }

    void Open()
    {
        MenuOpen = true;
        message = null;
        scroll = Vector2.zero;
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

    // ---------- Náklady a výroba ----------
    static int LevelCost(int level) => level + 1;   // úroveň 0→1 stojí 1×, 1→2 2×, 2→3 3×

    Recipe UpgradeRecipe(WeaponUpgrades.Kind kind)
    {
        int n = LevelCost(WeaponUpgrades.Level(kind));
        var r = new Recipe { upgrade = kind, money = 60 * n, scrap = 4 * n, stone = 2 * n };
        if (kind == WeaponUpgrades.Kind.Lightning) r.crystal = n;
        if (n >= WeaponUpgrades.MaxLevel) r.gold = 3;   // posledné vylepšenie potrebuje zlato

        switch (kind)
        {
            case WeaponUpgrades.Kind.Sharpen: r.name = "Ostrenie čepele"; r.description = "+4 poškodenie zbrane."; break;
            case WeaponUpgrades.Kind.Handle: r.name = "Dlhšia rukoväť"; r.description = "+0,15 dosahu zbrane."; break;
            default: r.name = "Bleskový nabíjač"; r.description = "15 % šanca za úroveň na bleskový zásah, ktorý preskočí na blízkeho nepriateľa."; break;
        }
        return r;
    }

    // ---------- Sekera: výroba 4 typov, vylepšenie a oprava ----------
    static Recipe ToolCraft(ToolKit.Kind kind, int tier)
    {
        var r = new Recipe { toolTier = tier, toolKind = kind, name = ToolKit.Name(kind, tier) };
        string stats = $"poškodenie {ToolKit.Damage(tier, 0):0}, životnosť {ToolKit.MaxDurability(tier, 0):0}";
        switch (tier)
        {
            case 0: r.wood = 8; r.stone = 2; r.description = $"Základný nástroj ({stats})."; break;
            case 1: r.money = 40; r.wood = 4; r.scrap = 6; r.description = $"Železný nástroj ({stats})."; break;
            case 2: r.money = 90; r.scrap = 10; r.stone = 4; r.description = $"Oceľový nástroj ({stats})."; break;
            default: r.money = Mathf.CeilToInt(160 * SoulBalance.ToolCostFactor); r.scrap = Mathf.CeilToInt(8 * SoulBalance.ToolCostFactor); r.crystal = Mathf.Max(1, Mathf.CeilToInt(4 * SoulBalance.ToolCostFactor)); r.gold = 3; r.description = $"Najsilnejší nástroj ({stats})."; break;
        }
        return r;
    }

    static Recipe ToolUpgradeRecipe(ToolKit.Kind kind)
    {
        int n = ToolKit.Upgrade(kind) + 1;
        return new Recipe
        {
            toolUpgrade = true, gold = n >= ToolKit.MaxUpgrade ? 2 : 0, toolKind = kind, name = $"Vylepšiť: {ToolKit.KindName(kind).ToLowerInvariant()}", money = 50 * n, scrap = 3 * n, wood = 2 * n,
            description = $"+25 % poškodenia a +30 % životnosti ({ToolKit.Name(kind)}, vylepšenie {n - 1}/{ToolKit.MaxUpgrade})."
        };
    }

    static Recipe ToolRepairRecipe(ToolKit.Kind kind) => new Recipe
    {
        toolRepair = true, toolKind = kind, name = $"Opraviť: {ToolKit.KindName(kind).ToLowerInvariant()}", money = ToolKit.RepairCost(kind), scrap = 2,
        description = $"Obnoví životnosť ({ToolKit.Durability(kind):0}/{ToolKit.MaxDurability(kind):0})."
    };

    string Missing(Recipe r)
    {
        if ((r.upgrade.HasValue || r.toolUpgrade || r.toolRepair) && !Power.Online) return Power.PlantBuilt ? "elektráreň nemá palivo" : "treba elektráreň";
        if (r.toolUpgrade && !ToolKit.CanUpgrade(r.toolKind)) return ToolKit.Has(r.toolKind) ? "maximálne vylepšený" : "nemáš nástroj";
        if (r.toolRepair && !ToolKit.NeedsRepair(r.toolKind)) return ToolKit.Has(r.toolKind) ? "nástroj je v poriadku" : "nemáš nástroj";
        if (r.toolTier >= 0 && ToolKit.Has(r.toolKind) && ToolKit.Tier(r.toolKind) >= r.toolTier) return "už máš rovnaký alebo lepší";
        if (player.money < SettlementEconomy.Discounted(r.money)) return $"peniaze {SettlementEconomy.Discounted(r.money)}";
        if (inventory == null) return "inventár";
        if (inventory.CountOf(ItemCatalog.Wood) < r.wood) return $"drevo {r.wood}";
        if (inventory.CountOf(ItemCatalog.Stone) < r.stone) return $"kameň {r.stone}";
        if (inventory.CountOf(ItemCatalog.Scrap) < r.scrap) return $"šrot {r.scrap}";
        if (inventory.CountOf(ItemCatalog.Crystal) < r.crystal) return $"kryštál {r.crystal}";
        if (inventory.CountOf(ItemCatalog.Herb) < r.herb) return $"bylina {r.herb}";
        if (inventory.CountOf(ItemCatalog.Gold) < r.gold) return $"zlato {r.gold}";
        if (r.resultItem != null && !inventory.CanAdd(r.resultItem)) return "miesto v inventári";
        return null;
    }

    void Make(Recipe r)
    {
        string missing = Missing(r);
        if (missing != null) { Say($"Chýba: {missing}."); return; }

        player.TrySpend(SettlementEconomy.Discounted(r.money));
        inventory.TryRemove(ItemCatalog.Wood, r.wood);
        inventory.TryRemove(ItemCatalog.Stone, r.stone);
        inventory.TryRemove(ItemCatalog.Scrap, r.scrap);
        inventory.TryRemove(ItemCatalog.Crystal, r.crystal);
        inventory.TryRemove(ItemCatalog.Herb, r.herb);
        inventory.TryRemove(ItemCatalog.Gold, r.gold);

        if (r.toolTier >= 0)
        {
            ToolKit.Give(r.toolKind, r.toolTier);
            Say($"Vyrobené: {r.name}.");
        }
        else if (r.toolUpgrade)
        {
            ToolKit.DoUpgrade(r.toolKind);
            Say($"{ToolKit.KindName(r.toolKind)} vylepšený na {ToolKit.Upgrade(r.toolKind)}/{ToolKit.MaxUpgrade}.");
        }
        else if (r.toolRepair)
        {
            ToolKit.Repair(r.toolKind);
            Say($"{ToolKit.KindName(r.toolKind)} opravený.");
        }
        else if (r.upgrade.HasValue)
        {
            WeaponUpgrades.Upgrade(r.upgrade.Value);
            Say($"{r.name}: úroveň {WeaponUpgrades.Level(r.upgrade.Value)}.");
        }
        else
        {
            inventory.TryAdd(r.resultItem);
            Say($"Vyrobené: {r.name}.");
        }
        GameAudio.Play(GameAudio.Sfx.Craft, player.transform.position);
    }

    // ---------- Zobrazenie ----------
    static string CostText(Recipe r)
    {
        var parts = new List<string>();
        if (r.money > 0) parts.Add($"{SettlementEconomy.Discounted(r.money)} peňazí");
        if (r.wood > 0) parts.Add($"drevo {r.wood}");
        if (r.stone > 0) parts.Add($"kameň {r.stone}");
        if (r.scrap > 0) parts.Add($"šrot {r.scrap}");
        if (r.crystal > 0) parts.Add($"kryštál {r.crystal}");
        if (r.herb > 0) parts.Add($"bylina {r.herb}");
        if (r.gold > 0) parts.Add($"zlato {r.gold}");
        return string.Join(", ", parts);
    }

    void DrawRecipe(Recipe r, string buttonText, bool enabled)
    {
        string missing = Missing(r);
        GUILayout.Label($"{r.name}: {r.description}\n   Cena: {CostText(r)}" + (missing != null ? $"   (chýba: {missing})" : ""), textStyle);
        GUI.enabled = enabled && missing == null;
        if (GUILayout.Button(buttonText, GUILayout.Height(26f))) Make(r);
        GUI.enabled = true;
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (player == null) return;

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            hintStyle.normal.textColor = Color.white;
        }

        if (!MenuOpen)
        {
            if (nearWorkshop != null && !Blocked())
                GUI.Label(new Rect(Screen.width * 0.5f - 250f, Screen.height - 170f, 500f, 30f), $"[{openKey}] Dielňa odboja: výroba a vylepšenia", hintStyle);
            return;
        }

        float w = 600f, h = Mathf.Min(Screen.height - 40f, 620f);
        Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.92f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(box.x + 20f, box.y + 14f, w - 40f, h - 28f));
        GUILayout.Label("Dielňa odboja", titleStyle);
        GUILayout.Label($"Peniaze {player.money}   Drevo {inventory?.CountOf(ItemCatalog.Wood)}   Kameň {inventory?.CountOf(ItemCatalog.Stone)}   Šrot {inventory?.CountOf(ItemCatalog.Scrap)}   " +
                        $"Kryštál {inventory?.CountOf(ItemCatalog.Crystal)}   Bylina {inventory?.CountOf(ItemCatalog.Herb)}", textStyle);
        GUILayout.Space(6f);

        scroll = GUILayout.BeginScrollView(scroll, false, true, GUILayout.ExpandHeight(true));

        GUILayout.Label("Vylepšenia zbrane", titleStyle);
        foreach (WeaponUpgrades.Kind kind in System.Enum.GetValues(typeof(WeaponUpgrades.Kind)))
        {
            Recipe r = UpgradeRecipe(kind);
            int level = WeaponUpgrades.Level(kind);
            DrawRecipe(r, WeaponUpgrades.CanUpgrade(kind) ? $"Vylepšiť na úroveň {level + 1}" : "Maximálna úroveň", WeaponUpgrades.CanUpgrade(kind));
            GUILayout.Label($"   Úroveň {level}/{WeaponUpgrades.MaxLevel}", textStyle);
        }

        GUILayout.Space(8f);
        GUILayout.Label("Nástroje (stromy – sekera, skaly – krompáč, železné bloky – kladivo)", titleStyle);
        Color previous = GUI.contentColor;
        foreach (ToolKit.Kind kind in System.Enum.GetValues(typeof(ToolKit.Kind)))
        {
            GUILayout.Space(4f);
            GUI.contentColor = ToolKit.Has(kind) ? ToolKit.ColorOf(ToolKit.Tier(kind)) : Color.gray;
            GUILayout.Label(ToolKit.Has(kind)
                ? $"{ToolKit.KindName(kind)}: {ToolKit.Name(kind)}  –  poškodenie {ToolKit.Damage(kind):0}, životnosť {ToolKit.Durability(kind):0}/{ToolKit.MaxDurability(kind):0}, vylepšenie {ToolKit.Upgrade(kind)}/{ToolKit.MaxUpgrade}"
                : $"{ToolKit.KindName(kind)}: nemáš.", textStyle);
            for (int tier = 0; tier < ToolKit.Tiers; tier++)
            {
                GUI.contentColor = ToolKit.ColorOf(tier);
                DrawRecipe(ToolCraft(kind, tier), "Vyrobiť", true);
            }
            GUI.contentColor = previous;
            DrawRecipe(ToolUpgradeRecipe(kind), "Vylepšiť", true);
            DrawRecipe(ToolRepairRecipe(kind), "Opraviť", true);
        }

        GUILayout.Space(8f);
        GUILayout.Label("Výroba", titleStyle);
        foreach (Recipe r in recipes) DrawRecipe(r, "Vyrobiť", true);

        GUILayout.EndScrollView();
        GUILayout.Space(6f);
        if (message != null && Time.unscaledTime < messageUntil) GUILayout.Label(message, textStyle);
        if (GUILayout.Button("Zavrieť", GUILayout.Height(28f))) Close();
        GUILayout.EndArea();
    }
}
