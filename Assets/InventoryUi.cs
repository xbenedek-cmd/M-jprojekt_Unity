using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>
/// Okno inventára v UI Toolkit, v rovnakom štýle ako HUD (HudUi): rám so zrezanými rohmi, neónová linka, skenovacie linky,
/// téma človek / démon. Obsah a pravidlá ostávajú v Inventory; toto okno ich len zobrazuje a posiela mu kliknutia a ťahanie.
/// Ak sa okno nepodarí postaviť, Inventory kreslí pôvodné okno cez OnGUI.
/// </summary>
public class InventoryUi : MonoBehaviour
{
    /// <summary>Okno je postavené a kreslí inventár (Inventory vtedy svoje OnGUI okno vynechá).</summary>
    public static bool Active { get; private set; }

    private static readonly Color Gold = new Color(1f, 0.82f, 0.3f);
    private static readonly Color Wearable = new Color(0.45f, 0.92f, 0.55f);
    private static readonly EquipSlot[] BodySlots = { EquipSlot.Head, EquipSlot.Top, EquipSlot.Bottom, EquipSlot.Boots };

    private Inventory inventory;
    private PlayerStats stats;
    private UIDocument document;
    private PanelSettings settings;
    private VisualElement root, window, scan;
    private HudFrame frame, infoFrame;
    private FontAsset humanFont, demonFont;
    private Texture2D scanTexture;

    private Label title, money, backpackTitle, bonusText, footer;
    private Label infoName, infoTag, infoDesc, infoStats, infoValue, infoHint;
    private InvSlot infoIcon, ghost, fists;
    private readonly InvSlot[] weaponSlots = new InvSlot[Inventory.WeaponSlots];
    private readonly InvSlot[] potionSlots = new InvSlot[Inventory.PotionSlots];
    private readonly InvSlot[] miscSlots = new InvSlot[Inventory.MiscSlots];
    private readonly InvSlot[] bodySlots = new InvSlot[4];
    private readonly Label[] bodyNames = new Label[4];
    private readonly VisualElement[] bodyWear = new VisualElement[4];
    private readonly Label[] sectionLabels = new Label[6];
    private Label fistsLabel;
    private int sections;

    private Color line, text, dim, background;
    private float torn;
    private bool demonTheme, themeSet, shown, dirty = true;
    private int lastMoney = int.MinValue, lastState = -1;

    // Ťahanie
    private InvSlot pressed, hover;
    private Vector2 pressPosition;
    private bool dragging;

    public static void Ensure(Inventory owner)
    {
        if (owner == null) return;
        foreach (InventoryUi old in FindObjectsByType<InventoryUi>(FindObjectsInactive.Include))
            if (old.inventory == owner) return;
        var go = new GameObject("InventoryUi");
        go.AddComponent<InventoryUi>().inventory = owner;
    }

    void Start()
    {
        if (inventory == null) { Destroy(gameObject); return; }
        stats = inventory.GetComponent<PlayerStats>();
        try
        {
            Build();
            inventory.Changed += MarkDirty;
            Active = true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("InventoryUi: okno sa nepodarilo postaviť, ostáva pôvodné. " + e.Message);
            Active = false;
            enabled = false;
        }
    }

    void OnDestroy()
    {
        Active = false;
        if (inventory != null) inventory.Changed -= MarkDirty;
        if (settings != null) Destroy(settings);
        if (scanTexture != null) Destroy(scanTexture);
    }

    void MarkDirty() => dirty = true;

    // ---------- Stavba ----------
    void Build()
    {
        settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("HudTheme");
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1280, 720);
        settings.screenMatchMode = PanelScreenMatchMode.Expand;   // okno sa vždy zmestí celé, aj do úzkeho okna
        settings.sortingOrder = 20;

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = settings;
        root = document.rootVisualElement;
        if (root == null) throw new System.InvalidOperationException("UIDocument nemá koreňový prvok.");

        humanFont = HudUi.OsFont("Bahnschrift", "Segoe UI", "Arial");
        demonFont = HudUi.OsFont("Impact", "Arial Black", "Arial");
        scanTexture = HudUi.BuildScanlines();

        root.style.position = Position.Absolute;
        root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
        root.style.alignItems = Align.Center;
        root.style.justifyContent = Justify.Center;
        root.style.backgroundColor = new Color(0f, 0f, 0f, 0.9f);   // lineárny priestor: nižšia alfa scénu takmer nestmaví
        root.RegisterCallback<PointerMoveEvent>(OnMove);
        root.RegisterCallback<PointerUpEvent>(OnUp);

        window = new VisualElement();
        window.style.width = 1064; window.style.height = 632;
        root.Add(window);

        frame = new HudFrame { cut = 22f };
        Stretch(frame);
        window.Add(frame);
        scan = new VisualElement { pickingMode = PickingMode.Ignore };
        Stretch(scan);
        scan.style.backgroundImage = new StyleBackground(scanTexture);
        scan.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
        scan.style.backgroundSize = new BackgroundSize(4f, 4f);
        scan.style.opacity = 0.16f;
        window.Add(scan);

        VisualElement content = new VisualElement();
        Stretch(content);
        content.style.paddingLeft = 26; content.style.paddingRight = 26; content.style.paddingTop = 14; content.style.paddingBottom = 12;
        window.Add(content);

        // Hlavička
        VisualElement header = Row(content);
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.alignItems = Align.Center;
        header.style.height = 40;
        title = Text(header, 26, true);
        title.text = "INVENTÁR";
        title.style.letterSpacing = 3;
        money = Text(header, 16, true);
        money.style.color = Gold;
        money.style.marginRight = 30;
        VisualElement rule = new VisualElement { name = "rule", pickingMode = PickingMode.Ignore };
        rule.style.height = 1; rule.style.marginTop = 2; rule.style.marginBottom = 10;
        content.Add(rule);

        VisualElement body = Row(content);
        body.style.flexGrow = 1;

        // Ľavý stĺpec: výstroj na tele a bonusy
        VisualElement left = Column(body, 204);
        Section(left, "VÝSTROJ NA TELE");
        for (int i = 0; i < BodySlots.Length; i++)
        {
            VisualElement row = Row(left);
            row.style.marginBottom = 8;
            bodySlots[i] = MakeSlot(row, Inventory.KindEquip, i, 64, 64, false);
            VisualElement texts = new VisualElement { pickingMode = PickingMode.Ignore };
            texts.style.marginLeft = 10; texts.style.flexGrow = 1; texts.style.justifyContent = Justify.Center;
            row.Add(texts);
            Label slotName = Text(texts, 11, true);
            slotName.text = Equipment.SlotName(BodySlots[i]).ToUpperInvariant();
            slotName.style.letterSpacing = 1;
            slotName.name = "dim";
            bodyNames[i] = Text(texts, 13, false);
            VisualElement wearBack = new VisualElement { pickingMode = PickingMode.Ignore };
            wearBack.style.height = 3; wearBack.style.marginTop = 4; wearBack.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            texts.Add(wearBack);
            bodyWear[i] = new VisualElement { pickingMode = PickingMode.Ignore };
            bodyWear[i].style.height = 3;
            wearBack.Add(bodyWear[i]);
        }
        Section(left, "BONUSY VÝSTROJE").style.marginTop = 6;
        bonusText = Text(left, 12, false);
        bonusText.style.whiteSpace = WhiteSpace.Normal;

        // Stred: zbrane, lektvary, batoh
        VisualElement middle = Column(body, 562);
        middle.style.marginLeft = 22;
        Section(middle, "ZBRANE  ·  koliesko myši prepína");
        VisualElement weaponRow = Row(middle);
        for (int i = 0; i < weaponSlots.Length; i++) weaponSlots[i] = MakeSlot(weaponRow, Inventory.KindWeapon, i, 132, 72, true);
        fists = MakeSlot(weaponRow, Inventory.KindFists, 0, 132, 72, false);
        fistsLabel = Text(fists, 14, true);
        fistsLabel.text = "PÄSTE";
        fistsLabel.pickingMode = PickingMode.Ignore;
        Stretch(fistsLabel);
        fistsLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        fistsLabel.style.letterSpacing = 2;

        Section(middle, "LEKTVARY A JEDLO  ·  [B] použiť vybraný, 1–5 rýchlo").style.marginTop = 10;
        VisualElement potionRow = Row(middle);
        for (int i = 0; i < potionSlots.Length; i++)
        {
            potionSlots[i] = MakeSlot(potionRow, Inventory.KindPotion, i, 104, 66, true);
            potionSlots[i].key.text = (i + 1).ToString();
        }

        backpackTitle = Section(middle, "BATOH");
        backpackTitle.style.marginTop = 10;
        VisualElement grid = Row(middle);
        grid.style.flexWrap = Wrap.Wrap;
        for (int i = 0; i < miscSlots.Length; i++) miscSlots[i] = MakeSlot(grid, Inventory.KindMisc, i, 48, 46, false);

        // Pravý stĺpec: popis predmetu
        VisualElement right = Column(body, 0);
        right.style.flexGrow = 1;
        right.style.marginLeft = 16;
        Section(right, "PREDMET");
        VisualElement info = new VisualElement { pickingMode = PickingMode.Ignore };
        info.style.flexGrow = 1;
        info.style.marginBottom = 8;
        right.Add(info);
        infoFrame = new HudFrame { cut = 12f };
        Stretch(infoFrame);
        info.Add(infoFrame);
        VisualElement infoContent = new VisualElement { pickingMode = PickingMode.Ignore };
        Stretch(infoContent);
        infoContent.style.paddingLeft = 14; infoContent.style.paddingRight = 12; infoContent.style.paddingTop = 12; infoContent.style.paddingBottom = 10;
        info.Add(infoContent);

        VisualElement head = Row(infoContent);
        head.style.alignItems = Align.Center;
        infoIcon = new InvSlot(-1, 0, false) { pickingMode = PickingMode.Ignore };
        infoIcon.style.width = 52; infoIcon.style.height = 52; infoIcon.style.flexShrink = 0;
        head.Add(infoIcon);
        infoName = Text(head, 15, true);
        infoName.style.marginLeft = 10; infoName.style.flexShrink = 1;
        infoTag = Text(infoContent, 12, true);
        infoTag.style.color = Gold;
        infoTag.style.marginTop = 6;
        infoTag.text = "★ LEGENDÁRNY";
        infoDesc = Text(infoContent, 12, false);
        infoDesc.style.marginTop = 8;
        infoStats = Text(infoContent, 12, false);
        infoStats.style.marginTop = 10;
        VisualElement spacer = new VisualElement { pickingMode = PickingMode.Ignore };
        spacer.style.flexGrow = 1;
        infoContent.Add(spacer);
        infoValue = Text(infoContent, 12, true);
        infoHint = Text(infoContent, 12, false);
        infoHint.name = "dim";

        footer = Text(content, 11, false);
        footer.name = "dim";
        footer.style.marginLeft = 12;
        footer.text = "Klik: použiť / obliecť / vyzliecť   |   Ťahaj: presunúť alebo dať na telo   |   Pravý klik alebo pustiť mimo okna: zahodiť   |   I / Esc: zavrieť";

        // Ikona ťahaného predmetu
        ghost = new InvSlot(-1, 0, false) { pickingMode = PickingMode.Ignore };
        ghost.style.position = Position.Absolute;
        ghost.style.width = 46; ghost.style.height = 46;
        ghost.style.display = DisplayStyle.None;
        root.Add(ghost);

        root.style.display = DisplayStyle.None;
    }

    static void Stretch(VisualElement e)
    {
        e.style.position = Position.Absolute;
        e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
    }

    static VisualElement Row(VisualElement parent)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.flexDirection = FlexDirection.Row;
        e.style.flexShrink = 0;
        parent.Add(e);
        return e;
    }

    static VisualElement Column(VisualElement parent, float width)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        if (width > 0f) { e.style.width = width; e.style.flexShrink = 0; }
        parent.Add(e);
        return e;
    }

    static Label Text(VisualElement parent, int size, bool bold)
    {
        var label = new Label { pickingMode = PickingMode.Ignore };
        label.style.fontSize = size;
        label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
        label.style.marginLeft = 0; label.style.marginRight = 0; label.style.marginTop = 0; label.style.marginBottom = 0;
        label.style.paddingLeft = 0; label.style.paddingRight = 0; label.style.paddingTop = 1; label.style.paddingBottom = 1;
        label.style.whiteSpace = WhiteSpace.Normal;
        parent.Add(label);
        return label;
    }

    Label Section(VisualElement parent, string caption)
    {
        Label label = Text(parent, 12, true);
        label.text = caption;
        label.style.letterSpacing = 1.5f;
        label.style.marginBottom = 6;
        if (sections < sectionLabels.Length) sectionLabels[sections++] = label;
        return label;
    }

    InvSlot MakeSlot(VisualElement parent, int kind, int index, float width, float height, bool withName)
    {
        var slot = new InvSlot(kind, index, withName);
        slot.style.width = width; slot.style.height = height;
        slot.style.marginRight = 6; slot.style.marginBottom = 6;
        slot.style.flexShrink = 0;
        slot.RegisterCallback<PointerDownEvent>(OnDown);
        slot.RegisterCallback<PointerEnterEvent>(OnEnter);
        slot.RegisterCallback<PointerLeaveEvent>(OnLeave);
        parent.Add(slot);
        return slot;
    }

    // ---------- Téma ----------
    void ApplyTheme(bool demon)
    {
        demonTheme = demon;
        themeSet = true;
        HudUi.GetTheme(demon, out line, out text, out dim, out background, out torn);

        Color solid = new Color(background.r, background.g, background.b, 0.995f);
        frame.Apply(solid, line, torn);
        infoFrame.Apply(new Color(0f, 0f, 0f, 0.35f), new Color(line.r, line.g, line.b, 0.55f), 0f);
        if (humanFont != null) root.style.unityFontDefinition = new StyleFontDefinition(humanFont);
        root.style.color = text;

        FontAsset display = demon && demonFont != null ? demonFont : humanFont;
        if (display != null) title.style.unityFontDefinition = new StyleFontDefinition(display);
        title.style.unityFontStyleAndWeight = demon && demonFont != null ? FontStyle.Normal : FontStyle.Bold;
        title.style.color = line;
        for (int i = 0; i < sections; i++) sectionLabels[i].style.color = line;
        root.Query<Label>("dim").ForEach(l => l.style.color = dim);
        root.Q<VisualElement>("rule").style.backgroundColor = new Color(line.r, line.g, line.b, 0.4f);
        fistsLabel.style.color = text;

        Color slotBack = new Color(background.r * 0.5f, background.g * 0.5f, background.b * 0.5f, 0.85f);
        foreach (InvSlot s in weaponSlots) { s.back = slotBack; s.key.style.color = dim; }
        foreach (InvSlot s in potionSlots) { s.back = slotBack; s.key.style.color = line; }
        foreach (InvSlot s in miscSlots) s.back = slotBack;
        foreach (InvSlot s in bodySlots) s.back = slotBack;
        fists.back = slotBack;
        infoIcon.back = slotBack;
        ghost.back = new Color(0f, 0f, 0f, 0.6f);
        dirty = true;
        ShowInfo(hover);
    }

    // ---------- Každý snímok ----------
    void Update()
    {
        if (!Active || inventory == null) return;

        bool open = Inventory.MenuOpen;
        if (open != shown)
        {
            shown = open;
            root.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            CancelDrag();
            hover = null;
            dirty = true;
            if (open) ShowInfo(null);
        }
        if (!open) return;

        bool demon = stats != null && stats.IsDemon;
        if (!themeSet || demon != demonTheme) ApplyTheme(demon);

        // Vytasenie zbrane a výber lektvaru sa menia aj bez udalosti Changed
        int state = inventory.EquippedWeaponIndex * 100 + inventory.SelectedPotion * 4 + (inventory.WeaponInHand ? 1 : 0);
        if (state != lastState) { lastState = state; dirty = true; }
        if (stats != null && stats.money != lastMoney)
        {
            lastMoney = stats.money;
            money.text = "PENIAZE  " + lastMoney;
        }
        if (dirty) Refresh();
    }

    void Refresh()
    {
        dirty = false;
        Color idle = new Color(line.r, line.g, line.b, 0.32f);
        Color selected = new Color(line.r, line.g, line.b, 0.07f);

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            ItemStack s = inventory.GetWeapon(i);
            bool equipped = i == inventory.EquippedWeaponIndex;
            ItemDef def = s.IsEmpty ? null : s.Def;
            weaponSlots[i].Set(def, s.count, equipped ? line : Border(def, idle), equipped ? selected : Color.clear, Source(weaponSlots[i]));
            weaponSlots[i].key.text = equipped ? (inventory.WeaponInHand ? "V RUKE" : "VYBRANÁ") : string.Empty;
        }
        bool bare = inventory.EquippedWeaponIndex < 0;
        fists.Set(null, 0, bare ? line : idle, bare ? selected : Color.clear, false);

        for (int i = 0; i < potionSlots.Length; i++)
        {
            ItemStack s = inventory.GetPotion(i);
            bool chosen = i == inventory.SelectedPotion;
            ItemDef def = s.IsEmpty ? null : s.Def;
            potionSlots[i].Set(def, s.count, chosen ? line : Border(def, idle), chosen ? selected : Color.clear, Source(potionSlots[i]));
        }

        for (int i = 0; i < miscSlots.Length; i++)
        {
            ItemStack s = inventory.GetMisc(i);
            ItemDef def = s.IsEmpty ? null : s.Def;
            Color border = def != null && def.IsLegendary ? Gold : def != null && def.slot != EquipSlot.None ? Wearable : idle;
            miscSlots[i].Set(def, s.count, border, Color.clear, Source(miscSlots[i]));
        }

        // Pri ťahaní oblečenia sa zvýrazní časť tela, na ktorú patrí
        EquipSlot wanted = dragging && pressed != null && pressed.kind == Inventory.KindMisc && pressed.def != null ? pressed.def.slot : EquipSlot.None;
        for (int i = 0; i < bodySlots.Length; i++)
        {
            ItemDef def = Equipment.DefIn(BodySlots[i]);
            bool target = wanted == BodySlots[i];
            bodySlots[i].Set(def, 1, target ? Color.white : def != null ? Border(def, Wearable) : idle, target ? selected : Color.clear, Source(bodySlots[i]));
            string label = def != null ? def.displayName : "prázdne";
            if (bodyNames[i].text != label) bodyNames[i].text = label;
            bodyNames[i].style.color = def != null ? (def.IsLegendary ? Gold : text) : dim;
            bool wears = def != null && def.durability > 0f;
            bodyWear[i].parent.style.display = wears ? DisplayStyle.Flex : DisplayStyle.None;
            if (wears)
            {
                float condition = Equipment.Condition01(BodySlots[i]);
                bodyWear[i].style.width = Length.Percent(condition * 100f);
                bodyWear[i].style.backgroundColor = Color.Lerp(new Color(0.9f, 0.3f, 0.2f), new Color(0.4f, 0.9f, 0.4f), condition);
            }
        }

        bonusText.text = inventory.BonusText;
        backpackTitle.text = $"BATOH  ·  {inventory.UsedMisc}/{Inventory.MiscSlots}";
        if (hover != null) ShowInfo(hover);
    }

    static Color Border(ItemDef def, Color normal) => def != null && def.IsLegendary ? Gold : normal;

    bool Source(InvSlot slot) => dragging && pressed == slot;

    // ---------- Popis ----------
    void ShowInfo(InvSlot slot)
    {
        if (infoName == null) return;
        ItemDef def = slot != null ? slot.def : null;
        infoIcon.Set(def, 1, def != null && def.IsLegendary ? Gold : new Color(line.r, line.g, line.b, 0.4f), Color.clear, false);
        infoIcon.style.display = def != null ? DisplayStyle.Flex : DisplayStyle.None;
        infoTag.style.display = def != null && def.IsLegendary ? DisplayStyle.Flex : DisplayStyle.None;

        if (def == null)
        {
            bool isFists = slot != null && slot.kind == Inventory.KindFists;
            infoName.text = isFists ? "Päste" : string.Empty;
            infoName.style.color = text;
            infoDesc.text = isFists ? ItemCatalog.Fists.description
                : dragging ? "Pusti predmet na iné miesto, na časť tela, alebo mimo okna (zahodiť)."
                : "Prejdi myšou po predmete a tu uvidíš jeho popis.\n\nZelený rám = dá sa obliecť.\nZlatý rám a ★ = legendárny predmet.";
            infoStats.text = string.Empty;
            infoValue.text = string.Empty;
            infoHint.text = isFists ? "Klik: bojovať bez zbrane" : string.Empty;
            return;
        }

        infoName.text = def.displayName;
        infoName.style.color = def.IsLegendary ? Gold : text;
        infoDesc.text = def.description;

        string statsText = string.Empty;
        if (def.IsWeapon)
            statsText = $"Poškodenie  ×{def.damageMultiplier:0.##}\nDosah  {def.range:0.#} m\nVýdrž na úder  {def.staminaCost:0}\nRýchlosť  ×{1f / Mathf.Max(0.1f, def.cooldownMultiplier):0.##}"
                        + (def.damageKind != DamageKind.Normal ? $"\nTyp  {def.damageKind}" : string.Empty);
        if (def.slot != EquipSlot.None)
        {
            statsText = "Časť tela  " + Equipment.SlotName(def.slot);
            if (def.equipEffects != null)
                foreach (SkillTree.Effect e in def.equipEffects) statsText += "\n• " + SkillTree.EffectText(e);
            if (def.durability > 0f) statsText += $"\nVýdrž  {def.durability:0} s v búrke";
        }
        infoStats.text = statsText;
        infoValue.text = def.price > 0 ? "Hodnota  " + def.price : string.Empty;

        switch (slot.kind)
        {
            case Inventory.KindWeapon: infoHint.text = "Klik: vziať do ruky / zasunúť"; break;
            case Inventory.KindPotion: infoHint.text = "Klik: použiť"; break;
            case Inventory.KindEquip: infoHint.text = "Klik: vyzliecť"; break;
            default: infoHint.text = def.slot != EquipSlot.None ? "Klik alebo pretiahni na telo: obliecť" : string.Empty; break;
        }
    }

    // ---------- Myš ----------
    void OnEnter(PointerEnterEvent e)
    {
        InvSlot slot = e.currentTarget as InvSlot;
        if (slot == null) return;
        hover = slot;
        slot.SetHover(true);
        if (!dragging) ShowInfo(slot);
    }

    void OnLeave(PointerLeaveEvent e)
    {
        InvSlot slot = e.currentTarget as InvSlot;
        if (slot == null) return;
        slot.SetHover(false);
        if (hover == slot)
        {
            hover = null;
            if (!dragging) ShowInfo(null);
        }
    }

    void OnDown(PointerDownEvent e)
    {
        InvSlot slot = e.currentTarget as InvSlot;
        if (slot == null) return;
        if (e.button == 1)
        {
            CancelDrag();
            inventory.RightClick(slot.kind, slot.index);
        }
        else if (e.button == 0)
        {
            pressed = slot;
            pressPosition = e.position;
            dragging = false;
        }
        e.StopPropagation();
    }

    void OnMove(PointerMoveEvent e)
    {
        if (pressed == null) return;
        Vector2 position = e.position;
        if (!dragging)
        {
            if (pressed.def == null || pressed.kind == Inventory.KindFists || (position - pressPosition).sqrMagnitude < 36f) return;
            dragging = true;
            ghost.Set(pressed.def, 1, pressed.def.IsLegendary ? Gold : Color.white, Color.clear, false);
            ghost.style.display = DisplayStyle.Flex;
            dirty = true;
            ShowInfo(null);
        }
        Vector2 local = root.WorldToLocal(position);
        ghost.style.left = local.x - 23f;
        ghost.style.top = local.y - 23f;
    }

    void OnUp(PointerUpEvent e)
    {
        if (pressed == null || e.button != 0) return;
        InvSlot from = pressed;
        bool wasDragging = dragging;
        CancelDrag();

        Vector2 position = e.position;
        InvSlot target = null;
        VisualElement picked = root.panel != null ? root.panel.Pick(position) : null;
        while (picked != null && target == null)
        {
            target = picked as InvSlot;
            picked = picked.parent;
        }

        if (wasDragging) inventory.DropOn(from.kind, from.index, target != null ? target.kind : -1, target != null ? target.index : 0, window.worldBound.Contains(position));
        else if (target == from) inventory.Click(from.kind, from.index);
        dirty = true;
        ShowInfo(hover);
    }

    void CancelDrag()
    {
        pressed = null;
        dragging = false;
        if (ghost != null) ghost.style.display = DisplayStyle.None;
        dirty = true;
    }
}
