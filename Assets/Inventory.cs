using System;
using UnityEngine;

[Serializable]
public struct ItemStack
{
    public string id;
    public int count;

    public bool IsEmpty => string.IsNullOrEmpty(id) || count <= 0;
    public ItemDef Def => ItemCatalog.Get(id);
}

[Serializable]
class InventorySave
{
    public ItemStack[] weapons;
    public ItemStack[] potions;
    public ItemStack[] misc;
    public int equipped;
    public int selectedPotion;
}

/// <summary>
/// Inventár hráča: 3 miesta na zbrane, 5 na lektvary (stoh do 5) a batoh s 50 miestami na ostatné predmety.
/// Otvára sa klávesou I. Klik na zbraň ju vytasí / zasunie, klik na lektvar ho použije, klik na oblečenie ho oblečie, pravý klik predmet zahodí.
/// Predmety sa dajú ťahať myšou: medzi miestami, na časť tela (obliecť) a mimo okna (zahodiť).
/// Rýchle použitie lektvaru: B, výber lektvaru: [ a ]. Koliesko myši prepína zbrane (rieši PlayerWeapons).
/// Stav sa ukladá do PlayerPrefs.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class Inventory : MonoBehaviour
{
    public static Inventory Instance { get; private set; }
    public static bool MenuOpen { get; private set; }

    public const int WeaponSlots = 3;
    public const int PotionSlots = 5;
    public const int MiscSlots = 50;
    private const int MiscColumns = 10;
    private const string SaveKey = "POA.Inventory";

    public KeyCode toggleKey => KeyBindings.Get("inventory", KeyCode.I);
    public KeyCode quickPotionKey => KeyBindings.Get("potion", KeyCode.B);
    public KeyCode prevPotionKey => KeyBindings.Get("potionPrev", KeyCode.LeftBracket);
    public KeyCode nextPotionKey => KeyBindings.Get("potionNext", KeyCode.RightBracket);
    [Header("Ovládanie")]

    [Header("Nastavenia")]
    [Tooltip("Pri prvom spustení (bez uloženého stavu) dostane hráč meč a pár lektvarov")]
    public bool giveStartingItems = true;
    public bool saveToPrefs = true;
    [Tooltip("Pauza medzi dvoma použitiami lektvaru (s)")]
    public float potionCooldown = 1f;

    private readonly ItemStack[] weapons = new ItemStack[WeaponSlots];
    private readonly ItemStack[] potions = new ItemStack[PotionSlots];
    private readonly ItemStack[] misc = new ItemStack[MiscSlots];
    private int equippedWeapon = -1;
    private int selectedPotion;
    private float nextPotionTime;

    private PlayerStats stats;
    private PlayerWeapons weaponsController;
    private int closedFrame = -1;

    // Texty HUD sa skladajú len pri zmene, nie každý frame
    private string weaponLabel = "Päste";
    private readonly string[] potionLabels = new string[PotionSlots];

    // GUI
    private GUIStyle titleStyle, textStyle, smallStyle, centerStyle, slotNameStyle, countStyle, keyStyle, sectionStyle, starStyle, buffStyle;
    private ItemDef hoverDef;
    private string hoverExtra;

    public event Action Changed;

    public ItemStack GetWeapon(int i) => weapons[i];
    public ItemStack GetPotion(int i) => potions[i];
    public ItemStack GetMisc(int i) => misc[i];
    public int EquippedWeaponIndex => equippedWeapon;
    public int SelectedPotion => selectedPotion;
    public ItemDef EquippedWeapon =>
        equippedWeapon >= 0 && !weapons[equippedWeapon].IsEmpty ? weapons[equippedWeapon].Def : null;

    /// <summary>Zatvorenie inventára v tomto frame (iné skripty ho nesmú vyhodnotiť ako klik do sveta).</summary>
    public static bool JustClosed => Instance != null && Instance.closedFrame == Time.frameCount;

    // ---------- Životný cyklus ----------
    void Awake()
    {
        Instance = this;
        stats = GetComponent<PlayerStats>();
        weaponsController = GetComponent<PlayerWeapons>();

        if (!(saveToPrefs && Load())) GiveStartingItems();
        RefreshLabels();
        InventoryUi.Ensure(this);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        MenuOpen = false;
    }

    void OnDisable()
    {
        if (MenuOpen) SetMenu(false);
    }

    void GiveStartingItems()
    {
        if (!giveStartingItems) return;
        TryAdd(ItemCatalog.IronSword);
        equippedWeapon = 0;
        TryAdd(ItemCatalog.HealthPotion, 2);
        TryAdd(ItemCatalog.StaminaPotion, 1);
    }

    // ---------- Pridávanie / odoberanie ----------
    ItemStack[] ArrayFor(ItemDef def) =>
        def.type == ItemType.Weapon ? weapons : def.type == ItemType.Potion ? potions : misc;

    /// <summary>Koľko kusov daného predmetu sa ešte zmestí (voľné miesta + dopĺňanie stohov).</summary>
    public int FreeSpaceFor(string id)
    {
        ItemDef def = ItemCatalog.Get(id);
        if (def == null) return 0;

        ItemStack[] array = ArrayFor(def);
        int space = 0;
        for (int i = 0; i < array.Length; i++)
        {
            if (array[i].IsEmpty) space += def.maxStack;
            else if (array[i].id == id) space += def.maxStack - array[i].count;
        }
        return space;
    }

    public bool CanAdd(string id, int count = 1) => count > 0 && FreeSpaceFor(id) >= count;

    /// <summary>Pridá predmet; všetko alebo nič (ak sa nezmestí, nepridá sa nič).</summary>
    public bool TryAdd(string id, int count = 1)
    {
        if (!CanAdd(id, count)) return false;

        ItemDef def = ItemCatalog.Get(id);
        ItemStack[] array = ArrayFor(def);
        int left = count;

        for (int i = 0; i < array.Length && left > 0; i++)
        {
            if (array[i].IsEmpty || array[i].id != id || array[i].count >= def.maxStack) continue;
            int add = Math.Min(left, def.maxStack - array[i].count);
            array[i].count += add;
            left -= add;
        }
        for (int i = 0; i < array.Length && left > 0; i++)
        {
            if (!array[i].IsEmpty) continue;
            int add = Math.Min(left, def.maxStack);
            array[i] = new ItemStack { id = id, count = add };
            left -= add;
        }

        NotifyChanged();
        return true;
    }

    /// <summary>Koľko kusov predmetu je v inventári (všetky stohy).</summary>
    public int CountOf(string id)
    {
        ItemDef def = ItemCatalog.Get(id);
        if (def == null) return 0;

        ItemStack[] array = ArrayFor(def);
        int total = 0;
        for (int i = 0; i < array.Length; i++)
            if (!array[i].IsEmpty && array[i].id == id) total += array[i].count;
        return total;
    }

    /// <summary>Odoberie daný počet kusov (všetko alebo nič).</summary>
    public bool TryRemove(string id, int count)
    {
        if (count <= 0) return true;
        if (CountOf(id) < count) return false;

        ItemStack[] array = ArrayFor(ItemCatalog.Get(id));
        int left = count;
        for (int i = array.Length - 1; i >= 0 && left > 0; i--)
        {
            if (array[i].IsEmpty || array[i].id != id) continue;
            int take = Math.Min(left, array[i].count);
            array[i].count -= take;
            left -= take;
            if (array[i].count <= 0) array[i] = default;
        }

        NotifyChanged();
        return true;
    }

    void ConsumeOne(ItemStack[] array, int slot)
    {
        array[slot].count--;
        if (array[slot].count <= 0) array[slot] = default;
    }

    // ---------- Zbrane ----------
    /// <summary>Nastaví zbraň v ruke (index slotu) alebo päste (-1). Samotné vytasenie rieši PlayerWeapons.</summary>
    public void SetEquippedWeapon(int index)
    {
        if (index >= WeaponSlots) return;
        if (index >= 0 && weapons[index].IsEmpty) return;
        equippedWeapon = index;
        NotifyChanged();
    }

    /// <summary>Ďalšia / predchádzajúca zbraň v poradí: päste, slot 1, 2, 3 (len obsadené).</summary>
    public void CycleWeapon(int direction)
    {
        int options = WeaponSlots + 1;   // pozícia 0 = päste
        int pos = equippedWeapon + 1;

        for (int step = 1; step <= options; step++)
        {
            int next = ((pos + direction * step) % options + options) % options;
            int slot = next - 1;
            if (slot >= 0 && weapons[slot].IsEmpty) continue;

            if (weaponsController != null) weaponsController.EquipFromInventory(slot);
            else SetEquippedWeapon(slot);
            return;
        }
    }

    // ---------- Lektvary ----------
    public bool UsePotion(int slot)
    {
        if (slot < 0 || slot >= PotionSlots || potions[slot].IsEmpty || stats.IsDead) return false;
        if (Time.time < nextPotionTime) return false;

        ItemDef def = potions[slot].Def;
        switch (def.effect)
        {
            case PotionEffect.Heal:
                if (stats.Health >= stats.MaxHealth - 0.5f) { GameHUD.Message("Máš plné zdravie.", 1.5f); return false; }
                stats.Heal(def.amount);
                break;
            case PotionEffect.Stamina:
                stats.RestoreStamina(def.amount);
                break;
            case PotionEffect.Blood:
                if (!stats.IsDemon) { GameHUD.Message("Krv môže piť len démon.", 2f); return false; }
                stats.DrinkBlood(def.amount);
                break;
            case PotionEffect.ScentMask:
                PlayerStealth.ScentMaskUntil = Time.time + def.duration;
                break;
            case PotionEffect.Invulnerable:
                stats.InvulnerableUntil = Mathf.Max(stats.InvulnerableUntil, Time.time + def.duration);
                break;
            case PotionEffect.Rage:
                stats.RageUntil = Mathf.Max(stats.RageUntil, Time.time + def.duration);
                break;
            case PotionEffect.FullRestore:
                stats.Heal(stats.MaxHealth);
                stats.RestoreStamina(100000f);
                break;
            case PotionEffect.Food:
                if (stats.IsDemon) { GameHUD.Message("Démon sa živí krvou, jedlo mu nepomôže.", 2f); return false; }
                if (stats.Hunger >= stats.maxHunger - 0.5f) { GameHUD.Message("Si sýty.", 1.5f); return false; }
                stats.Eat(def.amount);
                break;
        }

        GameHUD.Message($"Použité: {def.displayName}", 1.8f);
        ConsumeOne(potions, slot);
        nextPotionTime = Time.time + potionCooldown;
        NotifyChanged();
        return true;
    }

    void SelectPotion(int direction)
    {
        selectedPotion = ((selectedPotion + direction) % PotionSlots + PotionSlots) % PotionSlots;
        RefreshLabels();
        ItemStack s = potions[selectedPotion];
        GameHUD.Message(s.IsEmpty ? "Lektvar: prázdny slot" : $"Lektvar: {s.Def.displayName}", 1.2f);
    }

    // ---------- Zahodenie ----------
    void Drop(ItemStack[] array, int slot, bool isWeapon)
    {
        if (array[slot].IsEmpty) return;

        Vector3 pos = transform.position + transform.forward * 1.6f;
        ItemPickup.Spawn(array[slot].id, array[slot].count, pos);
        array[slot] = default;

        if (isWeapon && equippedWeapon == slot)
        {
            equippedWeapon = -1;
        }
        NotifyChanged();
    }

    // ---------- Zmeny a ukladanie ----------
    void NotifyChanged()
    {
        bonusDirty = true;
        RefreshLabels();
        if (saveToPrefs) Save();
        Changed?.Invoke();
    }

    void RefreshLabels()
    {
        ItemDef w = EquippedWeapon;
        weaponLabel = w != null ? w.displayName : "Päste";
        for (int i = 0; i < PotionSlots; i++)
            potionLabels[i] = potions[i].IsEmpty ? string.Empty : "x" + potions[i].count;
    }

    void Save()
    {
        var data = new InventorySave
        {
            weapons = weapons, potions = potions, misc = misc, equipped = equippedWeapon, selectedPotion = selectedPotion
        };
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
    }

    bool Load()
    {
        if (!PlayerPrefs.HasKey(SaveKey)) return false;

        InventorySave data = JsonUtility.FromJson<InventorySave>(PlayerPrefs.GetString(SaveKey));
        if (data == null || data.weapons == null || data.potions == null || data.misc == null) return false;

        CopyValid(data.weapons, weapons, ItemType.Weapon);
        CopyValid(data.potions, potions, ItemType.Potion);
        CopyValid(data.misc, misc, ItemType.Misc);
        equippedWeapon = data.equipped >= 0 && data.equipped < WeaponSlots && !weapons[data.equipped].IsEmpty ? data.equipped : -1;
        selectedPotion = Mathf.Clamp(data.selectedPotion, 0, PotionSlots - 1);
        return true;
    }

    // Uložený predmet, ktorý už v katalógu nie je (alebo je na zlom mieste), sa zahodí
    static void CopyValid(ItemStack[] from, ItemStack[] to, ItemType type)
    {
        for (int i = 0; i < to.Length; i++)
        {
            to[i] = default;
            if (i >= from.Length || from[i].IsEmpty) continue;

            ItemDef def = from[i].Def;
            if (def == null || def.type != type) continue;
            to[i] = new ItemStack { id = from[i].id, count = Mathf.Clamp(from[i].count, 1, def.maxStack) };
        }
    }

    [ContextMenu("Resetovať inventár")]
    void ResetInventory()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        Array.Clear(weapons, 0, weapons.Length);
        Array.Clear(potions, 0, potions.Length);
        Array.Clear(misc, 0, misc.Length);
        equippedWeapon = -1;
        GiveStartingItems();
        NotifyChanged();
    }

    // ---------- Vstup ----------
    void Update()
    {
        if (stats == null) return;
        if (weaponsController == null) weaponsController = GetComponent<PlayerWeapons>();

        if (stats.IsDead)
        {
            if (MenuOpen) SetMenu(false);
            return;
        }

        if (GameInput.GetKeyDown(toggleKey) || (MenuOpen && GameInput.GetKeyDown(KeyCode.Escape)))
        {
            if (MenuOpen) SetMenu(false);
            else if (!Healer.MenuOpen) SetMenu(true);
            return;
        }

        if (MenuOpen || Healer.MenuOpen) return;

        if (GameInput.GetKeyDown(quickPotionKey)) UsePotion(selectedPotion);
        DemonSenses senses = GetComponent<DemonSenses>();
        if (senses == null || !senses.SensesOn) QuickPotionKeys();   // pri zapnutých zmysloch 1–4 menia zameranie
        if (GameInput.GetKeyDown(prevPotionKey)) SelectPotion(-1);
        if (GameInput.GetKeyDown(nextPotionKey)) SelectPotion(1);
    }

    // Rýchle použitie lektvaru: čísla 1–5 a znaky + ľ š č ť (číselný rad slovenskej klávesnice bez Shiftu), podľa poradia políčok
    private static readonly KeyCode[] QuickKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5 };
    private static readonly string QuickChars = "+ľščť";
    private static readonly string[] QuickLabels = { "+", "ľ", "š", "č", "ť" };
    private int lastQuickFrame = -1;

    void QuickPotionKeys()
    {
        int slot = -1;
        for (int i = 0; i < PotionSlots && i < QuickKeys.Length; i++)
            if (GameInput.GetKeyDown(QuickKeys[i])) { slot = i; break; }

        // Ak rozloženie klávesnice nahlási len znak (ľ, š, č, ť, +), použije sa znak
        if (slot < 0 && !string.IsNullOrEmpty(GameInput.inputString))
            foreach (char c in GameInput.inputString)
            {
                int index = QuickChars.IndexOf(char.ToLowerInvariant(c));
                if (index >= 0 && index < PotionSlots) { slot = index; break; }
            }

        if (slot < 0 || Time.frameCount == lastQuickFrame) return;

        lastQuickFrame = Time.frameCount;
        selectedPotion = slot;
        UsePotion(slot);
    }

    void SetMenu(bool open)
    {
        if (MenuOpen == open) return;
        MenuOpen = open;
        if (!open) closedFrame = Time.frameCount;
        pressKind = -1;
        dragging = false;
        bonusDirty = true;

        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    // ---------- GUI ----------
    private const float PanelW = 1000f, PanelH = 640f;
    public const int KindWeapon = 0, KindPotion = 1, KindMisc = 2, KindEquip = 3, KindFists = 4;
    private static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
    private static readonly Color SlotBorder = new Color(0.32f, 0.33f, 0.44f);
    private static readonly EquipSlot[] BodySlots = { EquipSlot.Head, EquipSlot.Top, EquipSlot.Bottom, EquipSlot.Boots };

    // Ťahanie myšou: stlačené miesto, miesto pod kurzorom a či sa už ťahá (po pár pixeloch pohybu)
    private int pressKind = -1, pressIndex, overKind = -1, overIndex;
    private Vector2 pressPos;
    private bool dragging;
    private string bonusText;
    private bool bonusDirty = true;
    private int usedMisc;
    private readonly System.Text.StringBuilder builder = new System.Text.StringBuilder(256);
    private string buffLabel;
    private int buffKey = -1;

    void EnsureStyles()
    {
        if (titleStyle != null) return;

        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        centerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
        slotNameStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.LowerCenter, wordWrap = true, clipping = TextClipping.Clip };
        countStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
        keyStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerLeft };
        keyStyle.normal.textColor = new Color(1f, 0.9f, 0.5f);
        sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        sectionStyle.normal.textColor = new Color(0.72f, 0.74f, 0.86f);
        starStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
        starStyle.normal.textColor = Gold;
        buffStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (stats == null || stats.IsDead) return;
        EnsureStyles();

        // Okno inventára sa zmenší tak, aby sa zmestilo aj do úzkeho okna
        float s = MenuOpen ? Mathf.Min(1.6f, Mathf.Min(Screen.height / (PanelH + 40f), Screen.width / (PanelW + 20f)))
                           : Mathf.Clamp(Screen.height / 720f, 0.85f, 1.6f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        float vh = Screen.height / s;

        if (MenuOpen) { if (!InventoryUi.Active) DrawMenu(vw, vh); }   // okno kreslí InventoryUi; toto je len záloha
        else if (!Healer.MenuOpen) DrawHud(vw, vh);

        GUI.matrix = previous;
    }

    static void Fill(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    static void Frame(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    ItemStack StackAt(int kind, int index)
    {
        switch (kind)
        {
            case KindWeapon: return weapons[index];
            case KindPotion: return potions[index];
            case KindMisc: return misc[index];
            case KindEquip:
                string id = Equipment.IdIn(BodySlots[index]);
                return id != null ? new ItemStack { id = id, count = 1 } : default;
            default: return default;
        }
    }

    static void DrawIcon(Rect r, ItemDef def)
    {
        Fill(r, def.color);
        Fill(new Rect(r.x, r.y + r.height * 0.62f, r.width, r.height * 0.38f), new Color(0f, 0f, 0f, 0.22f));   // tieň dole dáva ikone objem
        Fill(new Rect(r.x, r.y, r.width, 2f), new Color(1f, 1f, 1f, 0.28f));
    }

    // Jedno miesto: pozadie, ikona, počet, zlatý rám legendárnych predmetov. Vstup myšou sa len zaznamená, vyhodnotí ho DrawMenu.
    void DrawSlot(Rect r, int kind, int index, Color border, string badge, bool showName)
    {
        Event e = Event.current;
        bool over = r.Contains(e.mousePosition);
        ItemStack stack = StackAt(kind, index);
        bool source = dragging && pressKind == kind && pressIndex == index;

        Fill(r, over ? new Color(0.2f, 0.2f, 0.29f, 1f) : new Color(0.09f, 0.09f, 0.14f, 1f));
        if (!stack.IsEmpty && stack.Def != null)
        {
            ItemDef def = stack.Def;
            float pad = Mathf.Min(8f, r.width * 0.14f);
            float nameH = showName ? 24f : 0f;
            Color prev = GUI.color;
            if (source) GUI.color = new Color(1f, 1f, 1f, 0.3f);
            DrawIcon(new Rect(r.x + pad, r.y + pad, r.width - pad * 2f, r.height - pad * 2f - nameH), def);
            if (showName) GUI.Label(new Rect(r.x + 2f, r.y, r.width - 4f, r.height - 2f), def.displayName, slotNameStyle);
            if (stack.count > 1) GUI.Label(new Rect(r.x, r.y, r.width - 4f, r.height - nameH - 2f), stack.count.ToString(), countStyle);
            if (def.IsLegendary) GUI.Label(new Rect(r.x + 3f, r.y + 1f, 20f, 18f), "★", starStyle);
            GUI.color = prev;
            if (def.IsLegendary && border == SlotBorder) border = Gold;
            if (over && !dragging)
            {
                hoverDef = def;
                hoverExtra = badge;
            }
        }
        Frame(r, over && dragging ? Color.white : border, over && dragging ? 2f : 1.5f);

        if (!over) return;
        overKind = kind;
        overIndex = index;
        if (e.type != EventType.MouseDown) return;
        if (e.button == 0)
        {
            pressKind = kind;
            pressIndex = index;
            pressPos = e.mousePosition;
            dragging = false;
        }
        else if (e.button == 1) RightClick(kind, index);
        e.Use();
    }

    void DrawMenu(float vw, float vh)
    {
        Event e = Event.current;
        Rect panel = new Rect((vw - PanelW) * 0.5f, (vh - PanelH) * 0.5f, PanelW, PanelH);
        Fill(new Rect(0f, 0f, vw, vh), new Color(0f, 0f, 0f, 0.55f));
        Fill(panel, new Color(0.035f, 0.035f, 0.06f, 0.97f));
        Fill(new Rect(panel.x, panel.y, panel.width, 50f), new Color(0.08f, 0.07f, 0.12f, 1f));
        Fill(new Rect(panel.x, panel.y + 50f, panel.width, 2f), new Color(0.6f, 0.5f, 0.2f));
        Frame(panel, new Color(0.6f, 0.5f, 0.2f), 2f);

        hoverDef = null;
        hoverExtra = null;
        overKind = -1;

        GUI.Label(new Rect(panel.x + 24f, panel.y + 8f, 400f, 36f), "INVENTÁR", titleStyle);
        Color prevContent = GUI.contentColor;
        GUI.contentColor = Gold;
        GUI.Label(new Rect(panel.xMax - 224f, panel.y + 14f, 200f, 28f), $"Peniaze: {stats.money}", textStyle);
        GUI.contentColor = prevContent;

        // --- Ľavý stĺpec: výstroj na tele a súčet bonusov ---
        float lx = panel.x + 24f, top = panel.y + 62f;
        GUI.Label(new Rect(lx, top, 190f, 22f), "VÝSTROJ NA TELE", sectionStyle);
        for (int i = 0; i < BodySlots.Length; i++)
            DrawBodySlot(new Rect(lx, top + 26f + i * 82f, 190f, 76f), i);

        Rect bonus = new Rect(lx, top + 26f + BodySlots.Length * 82f + 4f, 190f, PanelH - 62f - 26f - BodySlots.Length * 82f - 4f - 44f);
        Fill(bonus, new Color(0.07f, 0.07f, 0.11f, 1f));
        Frame(bonus, new Color(0.25f, 0.26f, 0.36f), 1f);
        if (bonusDirty) RebuildBonusText();
        GUI.Label(new Rect(bonus.x + 8f, bonus.y + 4f, bonus.width - 16f, 20f), "BONUSY VÝSTROJE", sectionStyle);
        GUI.Label(new Rect(bonus.x + 8f, bonus.y + 24f, bonus.width - 16f, bonus.height - 28f), bonusText, smallStyle);

        // --- Stred: zbrane, lektvary, batoh ---
        float mx = panel.x + 234f, y = top;
        GUI.Label(new Rect(mx, y, 520f, 22f), "ZBRANE   (koliesko myši prepína)", sectionStyle);
        y += 24f;
        for (int i = 0; i < WeaponSlots; i++)
        {
            bool equipped = i == equippedWeapon;
            bool inHand = equipped && weaponsController != null && weaponsController.IsDrawn;
            Rect r = new Rect(mx + i * 130f, y, 124f, 80f);
            DrawSlot(r, KindWeapon, i, equipped ? Gold : SlotBorder, inHand ? "V ruke" : equipped ? "Vybraná (zasunutá)" : "Klik: vziať do ruky", true);
            if (equipped) GUI.Label(new Rect(r.x + 22f, r.y + 2f, r.width - 24f, 18f), inHand ? "V RUKE" : "VYBRANÁ", smallStyle);
        }
        Rect fists = new Rect(mx + WeaponSlots * 130f, y, 124f, 80f);
        DrawSlot(fists, KindFists, 0, equippedWeapon < 0 ? Gold : SlotBorder, null, false);
        GUI.Label(fists, "Päste", centerStyle);
        y += 90f;

        GUI.Label(new Rect(mx, y, 520f, 22f), "LEKTVARY A JEDLO   ([B] použiť vybraný, 1–5 rýchlo)", sectionStyle);
        y += 24f;
        for (int i = 0; i < PotionSlots; i++)
        {
            Rect r = new Rect(mx + i * 104f, y, 98f, 72f);
            DrawSlot(r, KindPotion, i, i == selectedPotion ? new Color(0.3f, 0.9f, 1f) : SlotBorder, "Klik: použiť", true);
            if (i < QuickLabels.Length) GUI.Label(new Rect(r.x + 4f, r.y + 2f, 30f, r.height - 26f), (i + 1).ToString(), keyStyle);
        }
        y += 82f;

        GUI.Label(new Rect(mx, y, 520f, 22f), $"BATOH   ({usedMisc}/{MiscSlots})", sectionStyle);
        y += 24f;
        for (int i = 0; i < MiscSlots; i++)
        {
            Rect r = new Rect(mx + (i % MiscColumns) * 52f, y + (i / MiscColumns) * 52f, 48f, 48f);
            bool wearable = !misc[i].IsEmpty && misc[i].Def != null && misc[i].Def.slot != EquipSlot.None;
            DrawSlot(r, KindMisc, i, wearable ? new Color(0.45f, 0.75f, 0.4f) : SlotBorder, wearable ? "Klik alebo pretiahni na telo: obliecť" : null, false);
        }

        // --- Pravý stĺpec: popis predmetu pod kurzorom ---
        Rect info = new Rect(panel.x + 774f, top, 202f, PanelH - 62f - 44f);
        Fill(info, new Color(0.07f, 0.07f, 0.11f, 1f));
        Frame(info, hoverDef != null && hoverDef.IsLegendary ? Gold : new Color(0.25f, 0.26f, 0.36f), 1f);
        DrawInfo(info);

        GUI.Label(new Rect(panel.x + 24f, panel.yMax - 34f, panel.width - 48f, 26f),
                  "Klik: použiť / obliecť / vyzliecť   |   Ťahaj: presunúť alebo dať na telo   |   Pravý klik alebo pustiť mimo okna: zahodiť   |   I / Esc: zavrieť", smallStyle);

        // --- Vyhodnotenie myši ---
        if (pressKind >= 0 && e.type == EventType.MouseDrag && !dragging && pressKind != KindFists
            && !StackAt(pressKind, pressIndex).IsEmpty && (e.mousePosition - pressPos).sqrMagnitude > 36f)
            dragging = true;

        if (pressKind >= 0 && e.rawType == EventType.MouseUp && e.button == 0)
        {
            int kind = pressKind, index = pressIndex;
            bool wasDragging = dragging;
            pressKind = -1;
            dragging = false;
            if (wasDragging) DropOn(kind, index, overKind, overIndex, panel.Contains(e.mousePosition));
            else if (overKind == kind && overIndex == index) Click(kind, index);
            if (e.type != EventType.Used) e.Use();
        }

        if (dragging && e.type == EventType.Repaint)
        {
            ItemStack held = StackAt(pressKind, pressIndex);
            if (!held.IsEmpty && held.Def != null)
            {
                Rect icon = new Rect(e.mousePosition.x - 20f, e.mousePosition.y - 20f, 40f, 40f);
                DrawIcon(icon, held.Def);
                Frame(icon, held.Def.IsLegendary ? Gold : Color.white, 1.5f);
            }
        }
    }

    // Časť tela: ikona oblečeného kúsku, názov a stav; prázdna časť ukáže, čo sem patrí
    void DrawBodySlot(Rect r, int index)
    {
        EquipSlot slot = BodySlots[index];
        ItemDef def = Equipment.DefIn(slot);

        // Pri ťahaní oblečenia z batohu sa zvýrazní časť tela, na ktorú patrí
        bool target = dragging && pressKind == KindMisc && !misc[pressIndex].IsEmpty && misc[pressIndex].Def != null && misc[pressIndex].Def.slot == slot;
        Rect icon = new Rect(r.x, r.y, 76f, r.height);
        DrawSlot(icon, KindEquip, index, target ? Color.white : def != null ? new Color(0.45f, 0.75f, 0.4f) : SlotBorder, "Klik: vyzliecť", false);

        GUI.Label(new Rect(r.x + 84f, r.y + 4f, r.width - 84f, 18f), Equipment.SlotName(slot).ToUpperInvariant(), sectionStyle);
        GUI.Label(new Rect(r.x + 84f, r.y + 22f, r.width - 84f, 40f), def != null ? def.displayName : "prázdne", smallStyle);
        if (def != null && def.durability > 0f)
        {
            float condition = Equipment.Condition01(slot);
            Fill(new Rect(r.x + 84f, r.yMax - 8f, r.width - 88f, 4f), new Color(0f, 0f, 0f, 0.5f));
            Fill(new Rect(r.x + 84f, r.yMax - 8f, (r.width - 88f) * condition, 4f), Color.Lerp(new Color(0.9f, 0.3f, 0.2f), new Color(0.4f, 0.9f, 0.4f), condition));
        }
    }

    /// <summary>Súčet bonusov oblečenej výstroje ako text (pre okno inventára).</summary>
    public string BonusText { get { if (bonusDirty) RebuildBonusText(); return bonusText; } }
    public int UsedMisc { get { if (bonusDirty) RebuildBonusText(); return usedMisc; } }
    /// <summary>Vybraná zbraň je práve vytasená.</summary>
    public bool WeaponInHand => weaponsController != null && weaponsController.IsDrawn;

    void RebuildBonusText()
    {
        bonusDirty = false;
        usedMisc = 0;
        for (int i = 0; i < MiscSlots; i++) if (!misc[i].IsEmpty) usedMisc++;

        builder.Length = 0;
        foreach (SkillStat stat in (SkillStat[])Enum.GetValues(typeof(SkillStat)))
        {
            float value = Equipment.Bonus(stat);
            if (value <= 0f) continue;
            builder.Append(SkillTree.EffectText(new SkillTree.Effect(stat, value))).Append('\n');
        }
        bonusText = builder.Length > 0 ? builder.ToString() : "Žiadne. Obleč si niečo z batohu: klikni na predmet so zeleným rámom alebo ho pretiahni na telo.";
    }

    public void Click(int kind, int index)
    {
        switch (kind)
        {
            case KindWeapon: if (!weapons[index].IsEmpty) ActivateWeapon(index); break;
            case KindPotion: selectedPotion = index; UsePotion(index); break;
            case KindMisc: EquipFromMisc(index); break;
            case KindEquip: UnequipSlot(BodySlots[index]); break;
            case KindFists: ActivateWeapon(-1); break;
        }
    }

    public void RightClick(int kind, int index)
    {
        switch (kind)
        {
            case KindWeapon: Drop(weapons, index, true); break;
            case KindPotion: Drop(potions, index, false); break;
            case KindMisc: Drop(misc, index, false); break;
            case KindEquip: UnequipSlot(BodySlots[index]); break;
        }
    }

    public void DropOn(int fromKind, int fromIndex, int toKind, int toIndex, bool insidePanel)
    {
        if (toKind < 0)
        {
            if (!insidePanel) RightClick(fromKind, fromIndex);   // pustené mimo okna = zahodiť (oblečenie sa len vyzlečie)
            return;
        }
        if (fromKind == toKind && fromIndex == toIndex) return;

        if (fromKind == toKind && fromKind <= KindMisc)
        {
            ItemStack[] array = fromKind == KindWeapon ? weapons : fromKind == KindPotion ? potions : misc;
            Move(array, fromIndex, toIndex);
            if (fromKind == KindWeapon)
            {
                if (equippedWeapon == fromIndex) equippedWeapon = toIndex;
                else if (equippedWeapon == toIndex) equippedWeapon = array[fromIndex].IsEmpty ? -1 : fromIndex;
            }
            NotifyChanged();
        }
        else if (fromKind == KindMisc && toKind == KindEquip) EquipFromMisc(fromIndex);
        else if (fromKind == KindEquip && toKind == KindMisc) UnequipSlot(BodySlots[fromIndex]);
    }

    // Presun medzi miestami: rovnaké predmety sa zlejú do stohu, inak sa miesta vymenia
    static void Move(ItemStack[] array, int from, int to)
    {
        ItemStack a = array[from], b = array[to];
        ItemDef def = a.Def;
        if (!b.IsEmpty && def != null && b.id == a.id && b.count < def.maxStack)
        {
            int moved = Math.Min(a.count, def.maxStack - b.count);
            array[to].count += moved;
            array[from].count -= moved;
            if (array[from].count <= 0) array[from] = default;
            return;
        }
        array[from] = b;
        array[to] = a;
    }

    void DrawInfo(Rect r)
    {
        float x = r.x + 10f, w = r.width - 20f;
        if (hoverDef == null)
        {
            GUI.Label(new Rect(x, r.y + 10f, w, r.height - 20f),
                dragging ? "Pusti predmet na iné miesto, na časť tela, alebo mimo okna (zahodiť)." : "Prejdi myšou po predmete a tu uvidíš jeho popis.\n\nZelený rám = dá sa obliecť.\nZlatý rám a ★ = legendárny predmet.", smallStyle);
            return;
        }

        float y = r.y + 10f;
        DrawIcon(new Rect(x, y, 44f, 44f), hoverDef);
        Frame(new Rect(x, y, 44f, 44f), hoverDef.IsLegendary ? Gold : SlotBorder, 1.5f);
        Color prev = GUI.contentColor;
        if (hoverDef.IsLegendary) GUI.contentColor = Gold;
        GUI.Label(new Rect(x + 52f, y, w - 52f, 44f), hoverDef.displayName, textStyle);
        GUI.contentColor = prev;
        y += 52f;
        if (hoverDef.IsLegendary)
        {
            GUI.Label(new Rect(x, y, w, 20f), "★ LEGENDÁRNY", starStyle);
            y += 22f;
        }
        GUI.Label(new Rect(x, y, w, 96f), hoverDef.description, smallStyle);
        y += 100f;

        if (hoverDef.IsWeapon)
        {
            GUI.Label(new Rect(x, y, w, 110f),
                      $"Poškodenie: x{hoverDef.damageMultiplier:0.##}\nDosah: {hoverDef.range:0.#} m\nVýdrž na úder: {hoverDef.staminaCost:0}\nRýchlosť: x{1f / Mathf.Max(0.1f, hoverDef.cooldownMultiplier):0.##}" +
                      (hoverDef.damageKind != DamageKind.Normal ? $"\nTyp: {hoverDef.damageKind}" : string.Empty), smallStyle);
            y += 112f;
        }
        if (hoverDef.slot != EquipSlot.None)
        {
            string effects = string.Empty;
            if (hoverDef.equipEffects != null)
                foreach (SkillTree.Effect e in hoverDef.equipEffects) effects += "• " + SkillTree.EffectText(e) + "\n";
            if (hoverDef.durability > 0f) effects += $"Výdrž: {hoverDef.durability:0} s v búrke";
            GUI.Label(new Rect(x, y, w, 130f), $"Časť tela: {Equipment.SlotName(hoverDef.slot)}\n{effects}", smallStyle);
            y += 132f;
        }
        if (hoverDef.price > 0) GUI.Label(new Rect(x, r.yMax - 52f, w, 20f), $"Hodnota: {hoverDef.price}", smallStyle);
        if (hoverExtra != null) GUI.Label(new Rect(x, r.yMax - 30f, w, 24f), hoverExtra, smallStyle);
    }

    // Pri zatvorenom inventári sa rýchlo vidí, čo je v ruke a aké lektvary sú po ruke
    void DrawHud(float vw, float vh)
    {
        float totalWidth = 84f + PotionSlots * 52f + 12f;
        float x = (vw - totalWidth) * 0.5f;
        float y = vh - 76f;

        DrawBuffs(vw, y - 30f);

        HudUi.GetTheme(stats.IsDemon, out Color line, out Color text, out Color dim, out Color back, out float torn);
        Color idle = new Color(line.r, line.g, line.b, 0.35f);
        back.a = 0.8f;

        bool armed = weaponsController != null && weaponsController.IsDrawn && EquippedWeapon != null;
        Rect weaponRect = new Rect(x, y, 76f, 64f);
        Fill(weaponRect, back);
        if (armed) Fill(new Rect(weaponRect.x + 8f, weaponRect.y + 6f, weaponRect.width - 16f, weaponRect.height - 26f), EquippedWeapon.color);
        GUI.Label(new Rect(weaponRect.x, weaponRect.y, weaponRect.width, weaponRect.height - 2f), armed ? weaponLabel : "Päste", slotNameStyle);
        Frame(weaponRect, armed ? line : idle, armed ? 2f : 1f);
        Fill(new Rect(weaponRect.x, weaponRect.y + 4f, 3f, 20f), line);   // akcent ako na paneloch HUD-u

        for (int i = 0; i < PotionSlots; i++)
        {
            Rect r = new Rect(x + 88f + i * 52f, y + 8f, 46f, 52f);
            Fill(r, back);
            if (!potions[i].IsEmpty)
            {
                Fill(new Rect(r.x + 8f, r.y + 6f, r.width - 16f, r.height - 24f), potions[i].Def.color);
                GUI.Label(new Rect(r.x, r.y + 2f, r.width - 3f, 16f), potionLabels[i], countStyle);
            }
            if (i < QuickLabels.Length) GUI.Label(new Rect(r.x + 3f, r.y + r.height - 18f, 20f, 16f), QuickLabels[i], keyStyle);   // klávesa rýchleho použitia
            Frame(r, i == selectedPotion ? line : idle, i == selectedPotion ? 2f : 1f);
        }
    }

    // Odpočet elixírov nad lištou lektvarov (krátka nezraniteľnosť úhybu sa neukazuje)
    void DrawBuffs(float vw, float y)
    {
        int immortal = Mathf.CeilToInt(stats.InvulnerableUntil - Time.time);
        int rage = Mathf.CeilToInt(stats.RageUntil - Time.time);
        if (immortal < 2) immortal = 0;
        if (rage < 1) rage = 0;
        if (immortal == 0 && rage == 0) return;

        int key = immortal * 1000 + rage;
        if (key != buffKey)
        {
            buffKey = key;
            buffLabel = immortal > 0 && rage > 0 ? $"NESMRTEĽNÝ {immortal} s   |   KRV TITANOV {rage} s" : immortal > 0 ? $"NESMRTEĽNÝ {immortal} s" : $"KRV TITANOV {rage} s";
        }
        Rect r = new Rect((vw - 360f) * 0.5f, y, 360f, 26f);
        Fill(r, new Color(0.05f, 0.04f, 0.02f, 0.8f));
        Frame(r, Gold, 1.5f);
        Color prev = GUI.contentColor;
        GUI.contentColor = Gold;
        GUI.Label(r, buffLabel, buffStyle);
        GUI.contentColor = prev;
    }

    void EquipFromMisc(int index)
    {
        ItemDef def = misc[index].Def;
        if (def == null || def.slot == EquipSlot.None) return;

        string id = misc[index].id;
        misc[index] = default;
        string previous = Equipment.Equip(id);
        if (previous != null) TryAdd(previous);   // uvoľnené miesto je práve voľné
        GameHUD.Message($"Oblečené: {def.displayName}", 2.5f);
        NotifyChanged();
    }

    void UnequipSlot(EquipSlot slot)
    {
        string id = Equipment.Unequip(slot);
        if (id == null) return;
        if (!TryAdd(id))
        {
            Equipment.Equip(id);   // nie je miesto: kúsok ostáva oblečený
            GameHUD.Message("Inventár je plný.", 2.5f);
            return;
        }
        NotifyChanged();
    }

    void ActivateWeapon(int slot)
    {
        if (weaponsController != null) weaponsController.EquipFromInventory(slot);
        else SetEquippedWeapon(slot);
    }
}
