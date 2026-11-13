using UnityEngine;

/// <summary>
/// Vojenská debna v Divočine (z tábora s okopmi): kláves F ju otvorí a hráč dostane náhodnú korisť – šrot, lektvary, kryštály, peniaze,
/// automatické vylepšenie zbrane (WeaponUpgrades) alebo lepší nástroj. Otvorí sa raz.
/// </summary>
public class LootCrate : MonoBehaviour
{
    [SerializeField] private float range = 2.6f;
    private KeyCode openKey => KeyBindings.Get("use", KeyCode.F);

    private static LootCrate best;
    private static float bestDistance;
    private static int bestFrame = -1;
    private static PlayerStats player;
    private static Inventory inventory;
    private static float nextPlayerSearch;

    private bool opened;
    private bool mineChest;

    /// <summary>Truhla v bani: korisť je bohatšia na zlato a čierne kryštály.</summary>
    public void MakeMineChest() => mineChest = true;

    void Update()
    {
        if (opened) return;

        if (bestFrame != Time.frameCount)
        {
            bestFrame = Time.frameCount;
            best = null;
            bestDistance = float.MaxValue;
        }

        if (!FindPlayer() || player.IsDead || Healer.MenuOpen) return;

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
        if (best != this || opened || bestFrame != Time.frameCount) return;
        if (!GameInput.GetKeyDown(openKey)) return;

        opened = true;
        Open();
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

    void Open()
    {
        if (mineChest) { OpenMineChest(); return; }

        float roll = Random.value;
        string text;

        if (roll < 0.22f)
        {
            int n = Random.Range(3, 8);
            text = inventory.TryAdd(ItemCatalog.Scrap, n) ? $"Kovový šrot x{n}" : "šrot (inventár je plný)";
        }
        else if (roll < 0.34f) text = FoodLoot();
        else if (roll < 0.46f)
        {
            string potion = Random.value < 0.5f ? ItemCatalog.HealthPotion : ItemCatalog.StaminaPotion;
            int n = Random.Range(1, 3);
            text = inventory.TryAdd(potion, n) ? $"{ItemCatalog.Get(potion).displayName} x{n}" : "lektvary (inventár je plný)";
        }
        else if (roll < 0.62f)
        {
            text = TryWeaponUpgrade() ?? CrystalLoot();
        }
        else if (roll < 0.72f) text = CrystalLoot();
        else if (roll < 0.82f)
        {
            int money = Random.Range(40, 121);
            player.AddMoney(money);
            text = $"{money} peňazí";
        }
        else if (roll < 0.90f) text = TryOutfit() ?? CrystalLoot();
        else text = TryToolFind() ?? CrystalLoot();

        GameHUD.Message($"Debna: {text}", 4f);
        GameAudio.Play(GameAudio.Sfx.Craft, transform.position);

        // Otvorená debna sa len zatemní (kolízia ostáva)
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_Color", new Color(0.35f, 0.35f, 0.35f));
            r.SetPropertyBlock(block);
        }
    }

    void OpenMineChest()
    {
        float roll = Random.value;
        string text;
        if (roll < 0.38f)
        {
            int n = Random.Range(2, 6);
            text = inventory.TryAdd(ItemCatalog.Gold, n) ? $"Zlato x{n}" : "zlato (inventár je plný)";
        }
        else if (roll < 0.55f)
        {
            int n = Random.Range(1, 3);
            text = inventory.TryAdd(ItemCatalog.BlackCrystal, n) ? $"Čierny kryštál x{n}" : "čierny kryštál (inventár je plný)";
        }
        else if (roll < 0.72f)
        {
            int money = Random.Range(80, 221);
            player.AddMoney(money);
            text = $"{money} peňazí";
        }
        else if (roll < 0.80f) text = CrystalLoot();
        else if (roll < 0.90f) text = FoodLoot();
        else if (roll < 0.95f) text = TryWeaponUpgrade() ?? CrystalLoot();
        else text = TryToolFind() ?? CrystalLoot();

        GameHUD.Message($"Truhla: {text}", 4f);
        GameAudio.Play(GameAudio.Sfx.Craft, transform.position);
        DimAfterOpen();
    }

    void DimAfterOpen()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_Color", new Color(0.35f, 0.35f, 0.35f));
            r.SetPropertyBlock(block);
        }
    }

    // Jedlo v truhliciach: chlieb alebo zelenina
    string FoodLoot()
    {
        bool bread = Random.value < 0.5f;
        string id = bread ? ItemCatalog.Bread : ItemCatalog.Vegetables;
        int n = bread ? Random.Range(1, 4) : Random.Range(2, 6);
        return inventory.TryAdd(id, n) ? $"{ItemCatalog.Get(id).displayName} x{n}" : "jedlo (inventár je plný)";
    }

    string CrystalLoot()
    {
        int n = Random.Range(1, 3);
        return inventory.TryAdd(ItemCatalog.Crystal, n) ? $"Bleskový kryštál x{n}" : "kryštály (inventár je plný)";
    }

    // Náhodné vylepšenie zbrane, ktoré ešte nie je na maxime
    static string TryWeaponUpgrade()
    {
        var options = new System.Collections.Generic.List<WeaponUpgrades.Kind>();
        foreach (WeaponUpgrades.Kind kind in System.Enum.GetValues(typeof(WeaponUpgrades.Kind)))
            if (WeaponUpgrades.CanUpgrade(kind)) options.Add(kind);
        if (options.Count == 0) return null;

        WeaponUpgrades.Kind chosen = options[Random.Range(0, options.Count)];
        WeaponUpgrades.Upgrade(chosen);
        string name = chosen == WeaponUpgrades.Kind.Sharpen ? "ostrenie čepele" : chosen == WeaponUpgrades.Kind.Handle ? "dlhšia rukoväť" : "bleskový nabíjač";
        return $"vylepšenie zbrane – {name} (úroveň {WeaponUpgrades.Level(chosen)})";
    }

    // Nové oblečenie do šatníka (uniforma Megakorporácie / odev z podsvetia), ktoré hráč ešte nemá odomknuté
    static string TryOutfit()
    {
        Wardrobe wardrobe = Wardrobe.Instance;
        if (wardrobe == null) return null;

        var locked = new System.Collections.Generic.List<OutfitId>();
        foreach (OutfitId o in new[] { OutfitId.Officer, OutfitId.Underworld })
            if (!wardrobe.IsUnlocked(o)) locked.Add(o);
        if (locked.Count == 0) return null;

        OutfitId chosen = locked[Random.Range(0, locked.Count)];
        wardrobe.Unlock(chosen);
        return $"oblečenie – {Wardrobe.OutfitName(chosen)} (nájdeš ho v šatníku)";
    }

    // Lepší nástroj (železný alebo oceľový typ), ak taký hráč ešte nemá
    static string TryToolFind()
    {
        ToolKit.Kind kind = (ToolKit.Kind)Random.Range(0, ToolKit.Kinds);
        int tier = Random.value < 0.7f ? 1 : 2;
        if (ToolKit.Has(kind) && ToolKit.Tier(kind) >= tier) return null;

        ToolKit.Give(kind, tier);
        return $"nástroj – {ToolKit.Name(kind)}";
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;
        if (best != this || opened) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.4f);
        if (p.z <= 0f) return;

        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(p.x - 150f, Screen.height - p.y - 14f, 300f, 28f), mineChest ? $"[{openKey}] Otvoriť truhlu" : $"[{openKey}] Otvoriť vojenskú debnu", style);
    }
}
