using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

/// <summary>
/// Nový HUD v UI Toolkit (postupne nahrádza kreslenie cez OnGUI v GameHUD). Všetko sa stavia kódom, bez súborov UXML/USS.
/// Štýl: cyberpunk rozhranie s ostrými hranami, neónovými linkami, skenovacími linkami a jemným šumom;
/// ľudská forma je čistá a chladná, démonická červená s roztrhaným okrajom.
/// Hotové časti: ľavý panel (HP, výdrž, hlad/krv, forma, peniaze, stavy, zakrádanie, štvrť, oblečenie)
/// a pravý panel (frakcia a bonusy, schopnosti s kruhovým cooldownom, zmysly démona, nápoveda F1).
/// Ak sa panel nepodarí postaviť, GameHUD kreslí pôvodnú verziu.
/// </summary>
public class HudUi : MonoBehaviour
{
    /// <summary>Ľavý panel kreslí nový HUD (GameHUD ho potom v OnGUI vynechá).</summary>
    public static bool LeftPanelActive { get; private set; }
    /// <summary>Spodný okraj ľavého panelu v pixeloch obrazovky (panel úloh sa kreslí až pod ním); 0 = panel nie je vidno.</summary>
    public static float LeftBottomPixels { get; private set; }
    /// <summary>Pravý panel (frakcia, schopnosti, zmysly, nápoveda) kreslí nový HUD.</summary>
    public static bool RightPanelActive { get; private set; }
    /// <summary>Spodný okraj pravého panelu v pixeloch obrazovky (panel populácie sa kreslí pod ním); 0 = panel nie je vpravo hore.</summary>
    public static float RightBottomPixels { get; private set; }
    /// <summary>Výzvy a správy dole v strede kreslí nový HUD.</summary>
    public static bool BottomActive { get; private set; }
    /// <summary>Miesto pre textúru minimapy v pixeloch obrazovky (rám a hodiny sú v novom HUD-e); nulová šírka = minimapu kreslí GameHUD po starom.</summary>
    public static Rect MapRectPixels { get; private set; }

    private struct Theme
    {
        public Color line, text, dim, background, hp, stamina, need;
        public float torn;
    }

    private static readonly Theme Human = new Theme
    {
        line = new Color(0.35f, 0.86f, 1f, 0.95f), text = new Color(0.9f, 0.97f, 1f), dim = new Color(0.6f, 0.75f, 0.85f, 0.8f),
        background = new Color(0.015f, 0.045f, 0.075f, 0.74f),
        hp = new Color(0.95f, 0.25f, 0.3f), stamina = new Color(0.3f, 0.85f, 1f), need = new Color(1f, 0.65f, 0.18f), torn = 0f
    };

    private static readonly Theme Demon = new Theme
    {
        line = new Color(1f, 0.16f, 0.22f, 0.95f), text = new Color(1f, 0.88f, 0.86f), dim = new Color(0.95f, 0.55f, 0.55f, 0.8f),
        background = new Color(0.07f, 0.008f, 0.014f, 0.9f),
        hp = new Color(1f, 0.2f, 0.22f), stamina = new Color(0.85f, 0.45f, 1f), need = new Color(0.95f, 0.05f, 0.3f), torn = 1f
    };

    /// <summary>Farby témy HUD-u (človek / démon) pre ostatné okná v rovnakom štýle.</summary>
    public static void GetTheme(bool demon, out Color line, out Color text, out Color dim, out Color background, out float torn)
    {
        Theme t = demon ? Demon : Human;
        line = t.line; text = t.text; dim = t.dim; background = t.background; torn = t.torn;
    }

    private UIDocument document;
    private PanelSettings settings;
    private VisualElement root, left, scan, noise;
    private HudFrame leftFrame;
    private HudFormIcon formIcon;
    private HudSegmentBar hpBar, staminaBar, needBar;
    private Label hpText, needTag, formText, moneyText;
    private Label weakLine, deadLine, suspicionLine, invisibleLine, overclockLine, stealthLine, districtLine, outfitLine;
    private Texture2D scanTexture, noiseTexture;
    private FontAsset humanFont, demonFont;

    // Pravý panel
    private class AbilitySlot
    {
        public VisualElement row;
        public HudCooldownRing ring;
        public Label key, name, status;
        public int lastKey = -1, lastStatus = int.MinValue;
        public string lastName;
        public bool wasCooling;
        public float readyAt = -10f;
    }

    private const float RightWidth = 300f, HelpWidth = 440f;
    private static readonly Color ReadyColor = new Color(0.5f, 1f, 0.6f);
    private static readonly string[] SenseChipNames = { "[1] Sila", "[2] Rýchlosť", "[3] Výdrž", "[4] Klan" };

    private VisualElement right, scanRight, noiseRight, factionBlock, sensesBlock;
    private HudFrame rightFrame;
    private Label factionTag, factionName, factionState, factionBonus, sensesTitle, rootsLine, bloodLine, hintLine, helpTitle, helpText;
    private readonly Label[] senseChips = new Label[4];
    private AbilitySlot ability1, ability2;
    private PlayerFaction faction;
    private FactionAbility ability;
    private DemonSenses senses;
    private bool rightStacked, rightPlaced, helpShown;
    private int lastFaction = -1, lastFactionState = -1, lastSenseState = -1, lastSenseFocus = -1, lastSenseKey = -1, lastBloodMeters = int.MinValue;

    // Minimapa s hodinami
    private VisualElement map, mapHolder, scanMap;
    private HudFrame mapFrame;
    private HudClockIcon clockIcon;
    private HudDayBar dayBar;
    private Label clockTime, clockPhase, clockDay;
    private DayNightCycle dayNight;
    private int lastMinute = -1, lastDay = -1, lastPhase = -1;
    private float nextClockSearch;

    private VisualElement vignette;
    private float lastBlend = -1f;
    private bool lastBlendDemon;

    // Výzvy a správy dole
    private class BottomSlot
    {
        public Label label;
        public string text;
        public bool live, seen;
        public float shownAt, goneAt, lastAlpha = -1f;
    }

    private const float FadeIn = 0.2f, FadeOut = 0.16f;
    private VisualElement bottom;
    private readonly BottomSlot[] messageSlots = new BottomSlot[4], promptSlots = new BottomSlot[4];
    private readonly System.Collections.Generic.List<string> messageLines = new System.Collections.Generic.List<string>(8),
                                                             promptLines = new System.Collections.Generic.List<string>(8);
    private readonly System.Text.StringBuilder richBuilder = new System.Text.StringBuilder(96);
    // Nadpisy: v démonickej téme hrubé písmo, bežný text ostáva čitateľný
    private readonly System.Collections.Generic.List<Label> displayLabels = new System.Collections.Generic.List<Label>(12);

    private PlayerStats stats;
    private float nextSearch, nextNoise;
    private float hpGhost = 1f;
    private bool themeDemon, themeSet;

    // Posledné zobrazené hodnoty: texty sa prepisujú len pri zmene (žiadne reťazce každý snímok)
    private int lastHp = -1, lastMaxHp = -1, lastMoney = int.MinValue, lastSuspicion = -1, lastInvisible = -1, lastOverclock = -1;
    private int lastVisibility = -1, lastCrouch = -1, lastDistrict = -1, lastOutfit = -1, lastRule = -1, lastForm = -1;

    /// <summary>Vytvorí nový HUD pri GameHUD (volá sa pri každom načítaní scény).</summary>
    public static void Ensure(Transform owner)
    {
        if (owner == null || owner.GetComponentInChildren<HudUi>() != null) return;
        var go = new GameObject("HudUi");
        go.transform.SetParent(owner, false);
        go.AddComponent<HudUi>();
    }

    void Awake()
    {
        LeftPanelActive = false;
        LeftBottomPixels = 0f;
        RightPanelActive = false;
        RightBottomPixels = 0f;
        BottomActive = false;
        MapRectPixels = default(Rect);
        try
        {
            Build();
            LeftPanelActive = true;
            RightPanelActive = true;
        }
        catch (System.Exception e)
        {
            // Bezpečnostná poistka: bez nového panelu ostane pôvodný HUD z GameHUD
            Debug.LogWarning("HudUi: nový HUD sa nepodarilo postaviť, ostáva pôvodný. " + e);
            if (document != null) document.enabled = false;
        }
    }

    void OnDestroy()
    {
        LeftPanelActive = false;
        LeftBottomPixels = 0f;
        RightPanelActive = false;
        RightBottomPixels = 0f;
        BottomActive = false;
        MapRectPixels = default(Rect);
        if (settings != null) Destroy(settings);
        if (scanTexture != null) Destroy(scanTexture);
        if (noiseTexture != null) Destroy(noiseTexture);
    }

    // ---------- Stavba ----------
    void Build()
    {
        settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("HudTheme");
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1280, 720);
        settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        settings.match = 1f;          // mierka podľa výšky okna, ako starý HUD
        settings.sortingOrder = -10;  // pod ponukami kreslenými cez OnGUI

        document = gameObject.AddComponent<UIDocument>();
        document.panelSettings = settings;
        root = document.rootVisualElement;
        if (root == null) throw new System.InvalidOperationException("UIDocument nemá koreňový prvok.");

        root.pickingMode = PickingMode.Ignore;   // HUD nesmie brať kliknutia hre ani ponukám
        root.style.position = Position.Absolute;
        root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;

        humanFont = OsFont("Bahnschrift", "Segoe UI", "Arial");
        demonFont = OsFont("Impact", "Arial Black", "Arial");
        scanTexture = BuildScanlines();
        noiseTexture = BuildNoise(64);

        // Tunelové videnie (vnímanie): prvé v poradí, teda pod všetkými panelmi
        vignette = Box(root);
        Stretch(vignette);
        vignette.style.backgroundImage = new StyleBackground(GameHUD.VignetteTexture);
        Show(vignette, false);

        BuildLeft();
        BuildRight();
        BuildMap();
        BuildBottom();
        displayLabels.Add(hpText); displayLabels.Add(formText); displayLabels.Add(factionName);
        displayLabels.Add(helpTitle); displayLabels.Add(sensesTitle); displayLabels.Add(clockTime);
        displayLabels.Add(ability1.name); displayLabels.Add(ability2.name);
        ApplyTheme(false);
        root.style.display = DisplayStyle.None;
    }

    internal static FontAsset OsFont(params string[] families)
    {
        for (int i = 0; i < families.Length; i++)
        {
            FontAsset font = FontAsset.CreateFontAsset(families[i], "Regular", 72);
            if (font == null) font = FontAsset.CreateFontAsset(families[i], "Bold", 72);
            if (font != null) return font;
        }
        return null;   // ostane predvolené písmo témy
    }

    void BuildLeft()
    {
        left = Box(root);
        left.style.position = Position.Absolute;
        left.style.left = 14; left.style.top = 14;
        left.style.width = 322;

        leftFrame = new HudFrame();
        Stretch(leftFrame);
        left.Add(leftFrame);

        scan = Overlay(left, scanTexture, 4f, 0.2f);
        noise = Overlay(left, noiseTexture, 64f, 0.06f);

        VisualElement content = Box(left);
        content.style.paddingLeft = 12; content.style.paddingRight = 14;
        content.style.paddingTop = 9; content.style.paddingBottom = 9;

        // Riadok 1: ikona formy + pruhy
        VisualElement top = Box(content);
        top.style.flexDirection = FlexDirection.Row;
        top.style.alignItems = Align.Center;

        formIcon = new HudFormIcon();
        formIcon.style.width = 52; formIcon.style.height = 52;
        formIcon.style.marginRight = 10;
        formIcon.style.flexShrink = 0;
        top.Add(formIcon);

        VisualElement bars = Box(top);
        bars.style.flexGrow = 1;

        VisualElement hpHolder = Box(bars);
        hpHolder.style.height = 19;
        hpBar = new HudSegmentBar { segments = 20, skew = 6f, gap = 2f };
        Stretch(hpBar);
        hpHolder.Add(hpBar);
        hpText = Text(hpHolder, 12, true);
        Stretch(hpText);
        hpText.style.unityTextAlign = TextAnchor.MiddleCenter;
        hpText.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };

        staminaBar = new HudSegmentBar { segments = 28, skew = 3f, gap = 1.5f };
        staminaBar.style.height = 7; staminaBar.style.marginTop = 4;
        bars.Add(staminaBar);

        VisualElement needRow = Box(bars);
        needRow.style.flexDirection = FlexDirection.Row;
        needRow.style.alignItems = Align.Center;
        needRow.style.marginTop = 4;
        needTag = Text(needRow, 10, true);
        needTag.style.width = 36;
        needBar = new HudSegmentBar { segments = 16, skew = 4f, gap = 2f, tick = 0.25f };
        needBar.style.height = 10; needBar.style.flexGrow = 1;
        needRow.Add(needBar);

        // Riadok 2: forma a peniaze
        VisualElement row = Box(content);
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginTop = 5;
        formText = Text(row, 14, true);
        formText.style.letterSpacing = 3;
        moneyText = Text(row, 13, false);

        // Stavy: zobrazujú sa len keď platia
        VisualElement lines = Box(content);
        lines.style.marginTop = 2;
        weakLine = Text(lines, 13, true);
        deadLine = Text(lines, 13, true);
        suspicionLine = Text(lines, 13, true);
        invisibleLine = Text(lines, 13, true);
        overclockLine = Text(lines, 13, true);
        stealthLine = Text(lines, 12, false);
        districtLine = Text(lines, 12, false);
        outfitLine = Text(lines, 12, false);

        deadLine.text = "ZOMREL/A SI - hra sa vráti do menu (R hneď)";
        Show(weakLine, false); Show(deadLine, false); Show(suspicionLine, false);
        Show(invisibleLine, false); Show(overclockLine, false);
    }

    static VisualElement Box(VisualElement parent)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        parent.Add(e);
        return e;
    }

    static void Stretch(VisualElement e)
    {
        e.style.position = Position.Absolute;
        e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
    }

    // Opakovaná textúra cez celý panel (skenovacie linky, šum)
    static VisualElement Overlay(VisualElement parent, Texture2D texture, float tile, float opacity)
    {
        VisualElement e = Box(parent);
        Stretch(e);
        e.style.backgroundImage = new StyleBackground(texture);
        e.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
        e.style.backgroundSize = new BackgroundSize(tile, tile);
        e.style.opacity = opacity;
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

    static void Show(VisualElement e, bool visible)
    {
        DisplayStyle wanted = visible ? DisplayStyle.Flex : DisplayStyle.None;
        if (e.style.display != wanted) e.style.display = wanted;
    }

    internal static Texture2D BuildScanlines()
    {
        var tex = new Texture2D(1, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixels32(new[] { new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 90), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0) });
        tex.Apply(false, true);
        return tex;
    }

    static Texture2D BuildNoise(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[size * size];
        var random = new System.Random(11);
        for (int i = 0; i < pixels.Length; i++)
        {
            byte v = (byte)random.Next(256);
            pixels[i] = new Color32(v, v, v, (byte)random.Next(60, 256));
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    // ---------- Téma ----------
    void ApplyTheme(bool demon)
    {
        themeDemon = demon;
        themeSet = true;
        Theme t = demon ? Demon : Human;

        leftFrame.Apply(t.background, t.line, t.torn);
        hpBar.fill = t.hp;
        staminaBar.fill = t.stamina;
        needBar.fill = t.need;
        hpBar.MarkDirtyRepaint(); staminaBar.MarkDirtyRepaint(); needBar.MarkDirtyRepaint();

        if (humanFont != null) root.style.unityFontDefinition = new StyleFontDefinition(humanFont);
        FontAsset display = demon && demonFont != null ? demonFont : humanFont;
        for (int i = 0; i < displayLabels.Count; i++)
        {
            if (display != null) displayLabels[i].style.unityFontDefinition = new StyleFontDefinition(display);
            // Impact je hrubý sám o sebe; umelé stučnenie by ho zlialo
            displayLabels[i].style.unityFontStyleAndWeight = demon && demonFont != null ? FontStyle.Normal : FontStyle.Bold;
        }
        root.style.color = t.text;

        needTag.text = demon ? "KRV" : "HLAD";
        needTag.style.color = t.dim;
        formText.text = demon ? "DÉMON" : "ČLOVEK";
        formText.style.color = t.line;
        moneyText.style.color = t.text;
        districtLine.style.color = t.dim;
        deadLine.style.color = new Color(1f, 0.3f, 0.3f);
        weakLine.style.color = new Color(1f, 0.5f, 0.3f);
        suspicionLine.style.color = new Color(1f, 0.6f, 0.3f);
        invisibleLine.style.color = new Color(0.75f, 0.55f, 1f);
        overclockLine.style.color = new Color(1f, 0.9f, 0.25f);
        scan.style.opacity = demon ? 0.24f : 0.2f;
        noise.style.opacity = demon ? 0.07f : 0.05f;

        rightFrame.Apply(t.background, t.line, t.torn);
        scanRight.style.opacity = scan.style.opacity;
        noiseRight.style.opacity = noise.style.opacity;
        factionTag.style.color = t.dim;
        factionName.style.color = t.line;
        rootsLine.style.color = t.dim;
        bloodLine.style.color = new Color(1f, 0.4f, 0.4f);
        hintLine.style.color = t.dim;
        helpTitle.style.color = t.line;
        helpText.style.color = t.text;
        ability1.ring.Colors(t.line, ReadyColor);
        ability2.ring.Colors(t.line, ReadyColor);
        mapFrame.Apply(t.background, t.line, t.torn);
        scanMap.style.opacity = scan.style.opacity;
        clockTime.style.color = t.text;
        clockDay.style.color = t.dim;
        lastPhase = -1;

        Color slotBack = new Color(t.background.r, t.background.g, t.background.b, 0.86f);
        for (int i = 0; i < messageSlots.Length; i++)
        {
            StyleSlot(messageSlots[i], slotBack, t.line, t.text);
            StyleSlot(promptSlots[i], slotBack, t.line, t.text);
        }

        // Farby závislé od témy sa prepíšu pri najbližšom snímku
        lastFaction = -1; lastSenseState = -1;
        ability1.lastStatus = int.MinValue; ability2.lastStatus = int.MinValue;
    }

    // ---------- Každý snímok ----------
    void Update()
    {
        if (!LeftPanelActive) return;

        if (stats == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + 1f;
            stats = FindAnyObjectByType<PlayerStats>();
        }

        bool visible = stats != null && !PauseMenu.AtStartup;
        Show(root, visible);
        // Panel je v jednotkách 720 na výšku okna; ostatné OnGUI panely počítajú v pixeloch
        LeftBottomPixels = visible ? left.worldBound.yMax * (Screen.height / 720f) : 0f;
        BottomActive = visible;
        if (!visible) { RightBottomPixels = 0f; MapRectPixels = default(Rect); return; }

        float blend = PerceptionGlow.Blend;
        Show(vignette, blend > 0.01f);
        if (blend > 0.01f && (Mathf.Abs(blend - lastBlend) > 0.01f || PerceptionGlow.PlayerIsDemon != lastBlendDemon))
        {
            lastBlend = blend;
            lastBlendDemon = PerceptionGlow.PlayerIsDemon;
            vignette.style.unityBackgroundImageTintColor = lastBlendDemon
                ? new Color(0.4f, 0f, 0.03f, 0.9f * blend)       // démon: krvavo červené okraje
                : new Color(0f, 0.04f, 0.18f, 0.85f * blend);    // človek: tmavomodré okraje
        }

        if (!themeSet || themeDemon != stats.IsDemon) ApplyTheme(stats.IsDemon);
        Theme t = themeDemon ? Demon : Human;
        float time = Time.unscaledTime;

        // Šum sa posúva po skokoch, nie plynulo (digitálne zrnenie)
        if (time >= nextNoise)
        {
            nextNoise = time + 0.07f;
            noise.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, Random.Range(0, 64));
            noise.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, Random.Range(0, 64));
            noiseRight.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, Random.Range(0, 64));
            noiseRight.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, Random.Range(0, 64));
        }

        // HP: strata chvíľu ostane svetlá a potom dobehne
        float hp = stats.MaxHealth > 0f ? stats.Health / stats.MaxHealth : 0f;
        hpGhost = hp >= hpGhost ? hp : Mathf.MoveTowards(hpGhost, hp, 0.45f * Time.unscaledDeltaTime);
        float hpGlow = hp < 0.25f ? 0.5f + 0.5f * Mathf.Sin(time * 9f) : 0f;
        hpBar.Set(hp, hpGhost, hpGlow);

        staminaBar.Set(stats.maxStamina > 0f ? stats.Stamina / stats.maxStamina : 0f, 0f, 0f);

        // Hlad / krv: pod prahom pulzuje, krv výraznejšie a rýchlejšie
        float need = stats.IsDemon ? stats.Blood / Mathf.Max(0.01f, stats.maxBlood) : stats.Hunger / Mathf.Max(0.01f, stats.maxHunger);
        float pulse = 0f;
        if (need < 0.25f)
        {
            float speed = stats.IsDemon ? Mathf.Lerp(13f, 6f, need / 0.25f) : 5f;
            pulse = 0.5f + 0.5f * Mathf.Sin(time * speed);
        }
        needBar.Set(need, 0f, pulse);
        needTag.style.color = pulse > 0f ? Color.Lerp(t.dim, Color.white, pulse) : t.dim;

        Color iconBody = stats.IsDemon ? t.need : t.stamina;
        formIcon.Set(stats.IsDemon, t.line, iconBody, stats.IsDemon ? pulse : 0f);
        // Démon bez krvi: rám ľavého panelu bije spolu s pruhom krvi
        if (stats.IsDemon) leftFrame.SetLine(Color.Lerp(t.line, new Color(1f, 0.75f, 0.7f, 1f), pulse * 0.6f));

        UpdateTexts();
        UpdateRight(t, time);
        UpdateMap(t);
        UpdateBottom(time);
    }

    void UpdateTexts()
    {
        int hpNow = Mathf.RoundToInt(stats.Health), hpMax = Mathf.RoundToInt(stats.MaxHealth);
        if (hpNow != lastHp || hpMax != lastMaxHp)
        {
            lastHp = hpNow; lastMaxHp = hpMax;
            hpText.text = $"HP {hpNow}/{hpMax}";
        }

        int money = (int)stats.money;
        if (money != lastMoney)
        {
            lastMoney = money;
            moneyText.text = $"Peniaze: {money}";
        }

        int form = stats.IsDemon ? 1 : 0;
        Show(weakLine, stats.IsWeakened);
        if (stats.IsWeakened && form != lastForm) weakLine.text = stats.IsDemon ? "SLABNEŠ: potrebuješ krv!" : "HLADUJEŠ!";
        lastForm = stats.IsWeakened ? form : -1;

        Show(deadLine, stats.IsDead);

        bool suspicious = Reputation.Suspicion >= 1f;
        Show(suspicionLine, suspicious);
        if (suspicious)
        {
            int s = Mathf.RoundToInt(Reputation.Suspicion);
            if (s != lastSuspicion) { lastSuspicion = s; suspicionLine.text = $"Podozrenie mesta: {s}"; }
        }

        Show(invisibleLine, stats.IsInvisible);
        if (stats.IsInvisible)
        {
            int v = Mathf.RoundToInt(stats.InvisibleLeft * 10f);
            if (v != lastInvisible) { lastInvisible = v; invisibleLine.text = $"NEVIDITEĽNÝ {stats.InvisibleLeft:0.0} s"; }
        }

        Show(overclockLine, stats.OverclockActive);
        if (stats.OverclockActive)
        {
            int v = Mathf.RoundToInt(stats.OverclockLeft * 10f);
            if (v != lastOverclock) { lastOverclock = v; overclockLine.text = $"OVERCLOCK {stats.OverclockLeft:0.0} s"; }
        }

        // Zakrádanie, štvrť a oblečenie
        PlayerStealth stealth = PlayerStealth.Instance;
        bool showStealth = stealth != null && !stats.IsDead;
        Show(stealthLine, showStealth);
        if (showStealth)
        {
            float vis = stealth.Visibility;
            int percent = Mathf.RoundToInt(vis * 100f), crouch = stealth.IsCrouched ? 1 : 0;
            if (percent != lastVisibility || crouch != lastCrouch)
            {
                lastVisibility = percent; lastCrouch = crouch;
                stealthLine.text = $"{(stealth.IsCrouched ? "ČUPNUTÝ  |  " : "")}Viditeľnosť: {percent} %";
                stealthLine.style.unityFontStyleAndWeight = stealth.IsCrouched ? FontStyle.Bold : FontStyle.Normal;
                stealthLine.style.color = vis < 0.6f ? new Color(0.5f, 1f, 0.6f) : vis < 1f ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.5f, 0.4f);
            }
        }

        Wardrobe wardrobe = Wardrobe.Instance;
        bool showWardrobe = wardrobe != null && !stats.IsDead;
        Show(districtLine, showWardrobe);
        Show(outfitLine, showWardrobe);
        if (showWardrobe)
        {
            OutfitRule rule = wardrobe.CurrentRule;
            int district = (int)wardrobe.District, outfit = (int)wardrobe.current;
            if (district != lastDistrict)
            {
                lastDistrict = district;
                districtLine.text = $"Štvrť: {CityDistrict.NameOf(wardrobe.District)}";
            }
            if (outfit != lastOutfit || (int)rule != lastRule)
            {
                lastOutfit = outfit; lastRule = (int)rule;
                outfitLine.text = $"Oblečenie: {Wardrobe.OutfitName(wardrobe.current)} ({Wardrobe.RuleText(rule)})";
                outfitLine.style.color = rule == OutfitRule.Forbidden ? new Color(1f, 0.45f, 0.35f)
                                       : rule == OutfitRule.Welcome ? new Color(0.55f, 1f, 0.6f) : new Color(0.92f, 0.95f, 1f);
            }
        }
    }

    // ---------- Minimapa s hodinami ----------
    void BuildMap()
    {
        map = Box(root);
        map.style.position = Position.Absolute;
        map.style.right = 14; map.style.bottom = 14;

        mapFrame = new HudFrame { seed = 21, cut = 10f };
        Stretch(mapFrame);
        map.Add(mapFrame);
        scanMap = Overlay(map, scanTexture, 4f, 0.2f);

        VisualElement content = Box(map);
        content.style.paddingLeft = 9; content.style.paddingRight = 9;
        content.style.paddingTop = 9; content.style.paddingBottom = 7;

        // Textúru mapy a značky sem kreslí GameHUD (OnGUI je nad týmto panelom)
        mapHolder = Box(content);
        mapHolder.style.width = 150; mapHolder.style.height = 150;

        VisualElement row = Box(content);
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 6;
        clockIcon = new HudClockIcon();
        clockIcon.style.width = 18; clockIcon.style.height = 18;
        clockIcon.style.marginRight = 5;
        row.Add(clockIcon);
        clockTime = Text(row, 15, true);
        clockPhase = Text(row, 11, false);
        clockPhase.style.marginLeft = 6;
        clockPhase.style.flexGrow = 1;
        clockDay = Text(row, 11, true);
        clockDay.style.letterSpacing = 1;

        dayBar = new HudDayBar();
        dayBar.style.height = 4; dayBar.style.marginTop = 6;
        content.Add(dayBar);
    }

    void UpdateMap(Theme t)
    {
        bool shown = GameHUD.MiniMapShown;
        Show(map, shown);
        if (!shown) { MapRectPixels = default(Rect); return; }

        float toPixels = Screen.height / 720f;
        Rect holder = mapHolder.worldBound;
        // Prvý snímok ešte nie je rozloženie spočítané
        MapRectPixels = float.IsNaN(holder.width) || holder.width < 1f ? default(Rect)
            : new Rect(holder.x * toPixels, holder.y * toPixels, holder.width * toPixels, holder.height * toPixels);

        if (dayNight == null)
        {
            if (Time.unscaledTime < nextClockSearch) return;
            nextClockSearch = Time.unscaledTime + 1f;
            dayNight = FindAnyObjectByType<DayNightCycle>();
            if (dayNight == null) return;
        }

        float hour = dayNight.TimeOfDay;
        int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f);
        if (h * 60 + m != lastMinute)
        {
            lastMinute = h * 60 + m;
            clockTime.text = $"{h:00}:{m:00}";
        }
        if (dayNight.Day != lastDay)
        {
            lastDay = dayNight.Day;
            clockDay.text = $"DEŇ {lastDay}";
        }

        DayNightCycle.DayPhase phase = dayNight.CurrentPhase;
        if ((int)phase != lastPhase)
        {
            lastPhase = (int)phase;
            Color c = phase == DayNightCycle.DayPhase.Night ? new Color(0.6f, 0.7f, 1f)
                    : phase == DayNightCycle.DayPhase.Day ? new Color(1f, 0.9f, 0.45f) : new Color(1f, 0.65f, 0.4f);
            clockPhase.text = GameHUD.PhaseName(phase);
            clockPhase.style.color = c;
            clockIcon.Set(phase, c);
        }
        dayBar.Set(hour, dayNight.DawnStartHour, dayNight.DayStartHour, dayNight.DuskStartHour, dayNight.NightStartHour, t.text);
    }

    // ---------- Výzvy a správy dole ----------
    void BuildBottom()
    {
        bottom = Box(root);
        bottom.style.position = Position.Absolute;
        bottom.style.left = 0; bottom.style.right = 0;
        bottom.style.bottom = 112;   // nad lištou predmetov
        bottom.style.alignItems = Align.Center;

        for (int i = 0; i < messageSlots.Length; i++) messageSlots[i] = BuildSlot(false);
        for (int i = 0; i < promptSlots.Length; i++) promptSlots[i] = BuildSlot(true);
    }

    BottomSlot BuildSlot(bool prompt)
    {
        var slot = new BottomSlot { label = Text(bottom, 14, false) };
        Label l = slot.label;
        l.enableRichText = true;
        l.style.marginTop = 4;
        l.style.paddingLeft = 14; l.style.paddingRight = 14;
        l.style.paddingTop = 3; l.style.paddingBottom = 3;
        l.style.maxWidth = Length.Percent(70f);
        l.style.unityTextAlign = TextAnchor.MiddleCenter;
        // Správa má linky po bokoch, výzva podčiarknutie
        if (prompt) l.style.borderBottomWidth = 1.5f;
        else { l.style.borderLeftWidth = 2; l.style.borderRightWidth = 2; }
        Show(l, false);
        return slot;
    }

    void StyleSlot(BottomSlot slot, Color back, Color line, Color text)
    {
        Label l = slot.label;
        l.style.backgroundColor = back;
        l.style.color = text;
        l.style.borderLeftColor = line; l.style.borderRightColor = line; l.style.borderBottomColor = line;
        if (slot.text != null) l.text = Rich(slot.text);   // farba kláves v texte závisí od témy
    }

    void UpdateBottom(float time)
    {
        GameHUD.BottomLines(messageLines, promptLines);
        UpdateSlots(messageSlots, messageLines, time);
        UpdateSlots(promptSlots, promptLines, time);
    }

    void UpdateSlots(BottomSlot[] slots, System.Collections.Generic.List<string> lines, float time)
    {
        for (int i = 0; i < slots.Length; i++) slots[i].seen = false;

        for (int n = 0; n < lines.Count; n++)
        {
            string line = lines[n];
            if (string.IsNullOrEmpty(line)) continue;
            BottomSlot found = null, free = null;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].text == null) { if (free == null) free = slots[i]; }
                else if (slots[i].text == line) { found = slots[i]; break; }
            }
            if (found != null)
            {
                found.seen = true;
                found.live = true;   // text sa vrátil počas miznutia: znova sa rozsvieti
                continue;
            }
            if (free == null) continue;
            free.text = line;
            free.label.text = Rich(line);
            free.live = true;
            free.seen = true;
            free.shownAt = time;
            free.lastAlpha = -1f;
            Show(free.label, true);
        }

        for (int i = 0; i < slots.Length; i++)
        {
            BottomSlot slot = slots[i];
            if (slot.text == null) continue;
            if (!slot.seen && slot.live) { slot.live = false; slot.goneAt = time; }

            float appear = Mathf.Clamp01((time - slot.shownAt) / FadeIn);
            float alpha = appear * (2f - appear);   // rýchly nábeh, mäkké dobehnutie
            if (!slot.live)
            {
                float gone = Mathf.Clamp01((time - slot.goneAt) / FadeOut);
                if (gone >= 1f)
                {
                    slot.text = null;
                    Show(slot.label, false);
                    continue;
                }
                alpha *= 1f - gone;
            }
            if (Mathf.Abs(alpha - slot.lastAlpha) < 0.01f) continue;
            slot.lastAlpha = alpha;
            slot.label.style.opacity = alpha;
            slot.label.style.translate = new Translate(0f, (1f - appear) * 10f);
        }
    }

    // Klávesy v hranatých zátvorkách ("[F] Interakcia") sa zvýraznia farbou témy
    string Rich(string text)
    {
        int open = text.IndexOf('[');
        if (open < 0) return text;

        Theme t = themeDemon ? Demon : Human;
        string hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(t.line, Color.white, 0.25f));
        richBuilder.Length = 0;
        int from = 0;
        while (open >= 0)
        {
            int close = text.IndexOf(']', open + 1);
            if (close < 0) break;
            if (close - open <= 14)
            {
                richBuilder.Append(text, from, open - from);
                richBuilder.Append("<b><color=#").Append(hex).Append('>');
                richBuilder.Append(text, open, close - open + 1);
                richBuilder.Append("</color></b>");
                from = close + 1;
            }
            open = text.IndexOf('[', close + 1);
        }
        richBuilder.Append(text, from, text.Length - from);
        return richBuilder.ToString();
    }

    // ---------- Pravý panel ----------
    void BuildRight()
    {
        right = Box(root);
        right.style.position = Position.Absolute;
        right.style.width = RightWidth;

        rightFrame = new HudFrame { seed = 9 };
        Stretch(rightFrame);
        right.Add(rightFrame);

        scanRight = Overlay(right, scanTexture, 4f, 0.2f);
        noiseRight = Overlay(right, noiseTexture, 64f, 0.06f);

        VisualElement content = Box(right);
        content.style.paddingLeft = 12; content.style.paddingRight = 14;
        content.style.paddingTop = 9; content.style.paddingBottom = 8;

        // Frakcia a jej bonusy
        factionBlock = Box(content);
        factionTag = Text(factionBlock, 10, true);
        factionTag.text = "FRAKCIA";
        factionTag.style.letterSpacing = 3;
        factionName = Text(factionBlock, 15, true);
        factionState = Text(factionBlock, 12, true);
        factionBonus = Text(factionBlock, 12, false);

        ability1 = BuildAbility(factionBlock);
        ability2 = BuildAbility(factionBlock);

        // Zmysly démona
        sensesBlock = Box(content);
        sensesBlock.style.marginTop = 7;
        sensesTitle = Text(sensesBlock, 12, true);
        VisualElement chips = Box(sensesBlock);
        chips.style.flexDirection = FlexDirection.Row;
        chips.style.flexWrap = Wrap.Wrap;
        chips.style.marginTop = 3;
        for (int i = 0; i < senseChips.Length; i++)
        {
            Label chip = Text(chips, 11, false);
            chip.text = SenseChipNames[i];
            chip.style.paddingLeft = 5; chip.style.paddingRight = 5;
            chip.style.marginRight = 4; chip.style.marginBottom = 3;
            chip.style.borderLeftWidth = 2;
            senseChips[i] = chip;
        }
        rootsLine = Text(sensesBlock, 11, false);
        rootsLine.text = "[5] Cestovanie koreňmi / stromy temnoty";
        bloodLine = Text(sensesBlock, 12, true);

        // Nápoveda
        hintLine = Text(content, 11, false);
        hintLine.style.marginTop = 6;
        hintLine.text = "F1 ovládanie  |  Tab menu  |  M mapa";
        helpTitle = Text(content, 12, true);
        helpTitle.text = "OVLÁDANIE";
        helpTitle.style.letterSpacing = 3;
        helpTitle.style.marginTop = 6;
        helpText = Text(content, 11, false);

        Show(factionState, false); Show(bloodLine, false); Show(helpTitle, false); Show(helpText, false);
    }

    AbilitySlot BuildAbility(VisualElement parent)
    {
        var slot = new AbilitySlot();
        slot.row = Box(parent);
        slot.row.style.flexDirection = FlexDirection.Row;
        slot.row.style.alignItems = Align.Center;
        slot.row.style.marginTop = 6;

        VisualElement holder = Box(slot.row);
        holder.style.width = 40; holder.style.height = 40;
        holder.style.marginRight = 9;
        holder.style.flexShrink = 0;
        slot.ring = new HudCooldownRing();
        Stretch(slot.ring);
        holder.Add(slot.ring);
        slot.key = Text(holder, 14, true);
        Stretch(slot.key);
        slot.key.style.unityTextAlign = TextAnchor.MiddleCenter;
        slot.key.style.whiteSpace = WhiteSpace.NoWrap;

        VisualElement texts = Box(slot.row);
        texts.style.flexGrow = 1;
        texts.style.flexShrink = 1;
        slot.name = Text(texts, 13, true);
        slot.status = Text(texts, 11, false);
        return slot;
    }

    void UpdateRight(Theme t, float time)
    {
        // Úzke okno: pravý panel by prekryl ľavý, preto ide pod neho (ako starý HUD)
        float toPixels = Screen.height / 720f;
        float rootWidth = Screen.width / toPixels;   // panel je v jednotkách 720 na výšku okna
        bool help = GameHUD.HelpVisible;
        float width = help ? Mathf.Min(HelpWidth, Mathf.Max(RightWidth, rootWidth - 28f)) : RightWidth;
        bool stacked = rootWidth > 1f && rootWidth < left.worldBound.xMax + width + 28f;
        if (stacked != rightStacked || help != helpShown || !rightPlaced)
        {
            rightPlaced = true;
            rightStacked = stacked;
            right.style.width = width;
            if (stacked)
            {
                right.style.right = StyleKeyword.Auto;
                right.style.left = 14;
            }
            else
            {
                right.style.left = StyleKeyword.Auto;
                right.style.right = 14;
                right.style.top = 14;
            }
        }
        if (stacked) right.style.top = left.worldBound.yMax + 8f;

        if (help != helpShown)
        {
            helpShown = help;
            Show(helpTitle, help);
            Show(helpText, help);
            if (help) helpText.text = GameHUD.HelpText();   // klávesy sa mohli zmeniť v nastaveniach
            hintLine.text = help ? "F1 skryje ovládanie" : "F1 ovládanie  |  Tab menu  |  M mapa";
        }

        if (faction == null) faction = stats.GetComponent<PlayerFaction>();
        if (ability == null) ability = stats.GetComponent<FactionAbility>();
        if (senses == null) senses = stats.GetComponent<DemonSenses>();

        FactionProfile p = faction != null ? FactionData.Get(faction.Current) : null;
        bool showFaction = p != null && !stats.IsDead;
        Show(factionBlock, showFaction);
        if (showFaction)
        {
            bool mismatch = p.isDemonClan != stats.IsDemon;
            bool desperate = !mismatch && faction.state == FactionState.Desperate;
            int stateCode = mismatch ? 1 : desperate ? 2 : 0;
            if ((int)faction.Current != lastFaction || stateCode != lastFactionState)
            {
                lastFaction = (int)faction.Current;
                lastFactionState = stateCode;
                factionName.text = p.name;
                Show(factionState, stateCode != 0);
                factionState.text = mismatch ? "frakcia nesedí s tvojou formou" : "NÚDZOVÝ STAV";
                factionState.style.color = mismatch ? t.dim : new Color(1f, 0.5f, 0.3f);
                string bonus = desperate ? p.desperateText : p.stableText;
                Show(factionBonus, !mismatch && !string.IsNullOrEmpty(bonus));
                factionBonus.text = bonus;
                factionBonus.style.color = desperate ? new Color(1f, 0.75f, 0.55f) : t.text;
            }

            bool has1 = ability != null && p.ability != AbilityKind.None;
            bool has2 = ability != null && p.ability2 != AbilityKind.None;
            Show(ability1.row, has1);
            Show(ability2.row, has2);
            if (has1) UpdateAbility(ability1, ability.abilityKey, p.abilityName, ability.FormMatches, ability.CooldownLeft, ability.CooldownTotal, t, time);
            if (has2) UpdateAbility(ability2, ability.abilityKey2, p.ability2Name, !mismatch, ability.Cooldown2Left, ability.Cooldown2Total, t, time);
        }

        bool showSenses = senses != null && stats.IsDemon && !stats.IsDead;
        Show(sensesBlock, showSenses);
        if (showSenses)
        {
            int state = (int)senses.State, focus = (int)senses.focus, key = (int)senses.toggleKey;
            if (state != lastSenseState || focus != lastSenseFocus || key != lastSenseKey)
            {
                lastSenseState = state; lastSenseFocus = focus; lastSenseKey = key;
                switch (senses.State)
                {
                    case SensesState.Full: sensesTitle.text = "ZMYSLY: PLNÉ (krv ubúda rýchlo)"; break;
                    case SensesState.Focused: sensesTitle.text = $"ZMYSLY: ZAMERANIE NA {DemonSenses.StatName(senses.focus)}"; break;
                    default: sensesTitle.text = $"ZMYSLY: vypnuté [{KeyBindings.Name(senses.toggleKey)}]"; break;
                }
                sensesTitle.style.color = senses.State == SensesState.Off ? t.dim : t.line;
                PaintChips(t);
            }

            bool vision = senses.BloodVisionActive;
            Show(bloodLine, vision);
            if (vision)
            {
                float d = senses.NearestBloodDistance;
                int meters = d >= 0f ? Mathf.RoundToInt(d) : -1;
                if (meters != lastBloodMeters)
                {
                    lastBloodMeters = meters;
                    bloodLine.text = meters >= 0 ? $"KRVAVÉ VIDENIE - najbližšia krv: {meters} m" : "KRVAVÉ VIDENIE - žiadna krv v dosahu";
                }
            }
        }

        RightBottomPixels = stacked ? 0f : right.worldBound.yMax * toPixels;
        if (stacked) LeftBottomPixels = right.worldBound.yMax * toPixels;
    }

    void PaintChips(Theme t)
    {
        for (int i = 0; i < senseChips.Length; i++)
        {
            bool on = senses != null && (int)senses.focus == i;
            Label chip = senseChips[i];
            chip.style.color = on ? Color.white : t.dim;
            chip.style.unityFontStyleAndWeight = on ? FontStyle.Bold : FontStyle.Normal;
            chip.style.backgroundColor = on ? new Color(t.line.r, t.line.g, t.line.b, 0.32f) : new Color(1f, 1f, 1f, 0.05f);
            chip.style.borderLeftColor = on ? t.line : new Color(t.line.r, t.line.g, t.line.b, 0.25f);
        }
    }

    void UpdateAbility(AbilitySlot slot, KeyCode key, string abilityName, bool formOk, float left, float total, Theme t, float time)
    {
        if ((int)key != slot.lastKey)
        {
            slot.lastKey = (int)key;
            string keyName = KeyBindings.Name(key);
            slot.key.text = keyName;
            slot.key.style.fontSize = keyName.Length > 2 ? 9 : 14;
        }
        if (!ReferenceEquals(abilityName, slot.lastName))
        {
            slot.lastName = abilityName;
            slot.name.text = abilityName;
        }

        bool cooling = formOk && left > 0f;
        // Záblesk v okamihu nabitia
        if (slot.wasCooling && !cooling && formOk) slot.readyAt = time;
        slot.wasCooling = cooling;
        float flash = Mathf.Clamp01(1f - (time - slot.readyAt) / 0.5f);
        slot.ring.Set(cooling ? 1f - left / Mathf.Max(0.01f, total) : 1f, formOk, flash);

        int code = !formOk ? -2 : cooling ? Mathf.CeilToInt(left * 10f) : -1;
        if (code != slot.lastStatus)
        {
            slot.lastStatus = code;
            slot.status.text = !formOk ? "iná forma" : cooling ? $"{left:0.0} s" : "PRIPRAVENÁ";
            slot.status.style.color = !formOk ? t.dim : cooling ? t.text : ReadyColor;
            slot.name.style.color = formOk ? t.text : t.dim;
            slot.key.style.color = formOk && !cooling ? Color.white : t.dim;
        }
    }
}
