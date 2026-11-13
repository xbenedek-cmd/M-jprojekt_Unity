using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Jediné miesto, kde sa kreslí HUD. Na obrazovke ostáva len základ (HP, výdrž, hlad/krv, schopnosti,
/// minimapa). Všetko ostatné je v menu (Tab, alebo M rovno na mape): postava, čas a mesiac, frakcia,
/// mapa, ovládanie. Menu zastaví hru. Vytvorí sa automaticky pri štarte hry.
/// Ostatné skripty ho používajú cez GameHUD.Prompt / Message / WorldText / MangaPanel.
/// </summary>
public class GameHUD : MonoBehaviour
{
    // ---------- API pre ostatné skripty ----------
    /// <summary>Zobrazí výzvu dolu v strede, kým ju voláš každý frame (napr. "[F] Interakcia").</summary>
    public static void Prompt(string text) => prompts.Add(new PromptEntry { text = text, frame = Time.frameCount });

    /// <summary>Krátka správa dolu v strede.</summary>
    public static void Message(string text, float seconds = 2.5f)
    {
        messages.RemoveAll(m => m.text == text);
        messages.Add(new MessageEntry { text = text, until = Time.time + seconds });
    }

    /// <summary>Text vo svete (číslo poškodenia, +liečenie...), pomaly stúpa a mizne.</summary>
    public static void WorldText(Vector3 worldPos, string text, Color color)
        => worldTexts.Add(new WorldTextEntry { pos = worldPos, text = text, color = color, time = Time.time, drift = Random.Range(-1f, 1f) });

    static readonly string[] sfxWords = { "BAM!", "BOOM!", "SLASH!", "ZAP!", "KRAK!" };
    static string panelText;
    static float panelUntil;
    static float panelDuration = 0.22f;

    /// <summary>Krátky záblesk manga panelu so zvukovým efektom (kritický zásah).</summary>
    public static void MangaPanel(string sfx = null, float seconds = 0.22f)
    {
        panelText = sfx ?? sfxWords[Random.Range(0, sfxWords.Length)];
        panelDuration = Mathf.Max(0.05f, seconds);
        panelUntil = Time.time + panelDuration;
    }

    /// <summary>Menu je otvorené (blokuje boj, schopnosti a kameru). Platí aj v snímku zatvorenia.</summary>
    public static bool MenuOpen => (instance != null && (instance.menuOpen || instance.closedFrame == Time.frameCount))
                                   || JailManager.MenuOpen || JailManager.IsInJail;

    /// <summary>Nápoveda ovládania (F1) je zapnutá; kreslí ju pravý panel HudUi.</summary>
    public static bool HelpVisible => instance != null && instance.showHelp;

    /// <summary>Minimapa sa má kresliť (HudUi podľa toho ukazuje jej rám a hodiny).</summary>
    public static bool MiniMapShown => instance != null && !instance.menuOpen && instance.miniMap != null && MiniMapVisible && !MangaDialog.Active;

    /// <summary>Naplní zoznamy aktuálnymi správami a výzvami (kreslí ich HudUi dole v strede). Texty sa môžu opakovať.</summary>
    public static void BottomLines(List<string> messageLines, List<string> promptLines)
    {
        messageLines.Clear();
        promptLines.Clear();
        for (int i = 0; i < messages.Count; i++)
            if (Time.time <= messages[i].until) messageLines.Add(messages[i].text);
        for (int i = 0; i < prompts.Count; i++)
            if (prompts[i].frame >= Time.frameCount - 1) promptLines.Add(prompts[i].text);
    }

    // ---------- farby ukazovateľov (tu ich ľahko zmeníš) ----------
    static readonly Color colHp = new Color(0.85f, 0.15f, 0.15f);
    static readonly Color colStamina = new Color(0.25f, 0.8f, 0.95f);
    static readonly Color colHunger = new Color(0.95f, 0.6f, 0.15f);
    static readonly Color colBlood = new Color(0.9f, 0.1f, 0.4f);
    static readonly Color colXp = new Color(0.7f, 0.35f, 0.95f);
    static readonly Color dim = new Color(1f, 1f, 1f, 0.55f);

    // ---------- interné ----------
    struct PromptEntry { public string text; public int frame; }
    class MessageEntry { public string text; public float until; }
    class WorldTextEntry { public Vector3 pos; public string text; public Color color; public float time, drift; }

    static readonly List<PromptEntry> prompts = new List<PromptEntry>();
    static readonly List<MessageEntry> messages = new List<MessageEntry>();
    static readonly List<WorldTextEntry> worldTexts = new List<WorldTextEntry>();
    static GameHUD instance;
    private bool diagLogged;
    private float diagTimer;

    const int TabChar = 0, TabTime = 1, TabFaction = 2, TabMap = 3, TabQuest = 4, TabSkills = 5, TabHelp = 6, TabCast = 7;
    static readonly string[] tabNames = { "Postava", "Čas a mesiac", "Frakcia", "Mapa", "Úlohy", "Skilly", "Ovládanie", "Postavy" };
    private int castSelected;

    PlayerStats stats;
    PlayerFaction faction;
    DemonSenses senses;
    FactionAbility ability;
    DayNightCycle dayNight;
    MiniMap miniMap;
    public static bool MiniMapVisible = true;

    // POSUNUTÉ DOPRAVA - predtým 210, teraz menší offset, aby text nezasahoval do stredu
    [SerializeField] private float hudRightOffset = 150f;
    // Extra posun doprava (aby bol blok pri pravom okraji)
    [SerializeField] private float hudRightExtra = 0f;
    // Maximálna šírka pravého bloku (aby sa nelámal do stredu)
    [SerializeField] private float hudRightMaxWidth = 260f;

    float nextRefresh;
    bool showHelp;

    // menu
    bool menuOpen;
    int tab;
    int closedFrame = -1;
    float savedTimeScale = 1f;
    CursorLockMode savedLock;
    bool savedCursorVisible;
    float mapZoom = 1f;
    bool wasMapTab;
    float menuOpenedAt;

    // značky na mape
    MilitaryZone[] zonesCache = new MilitaryZone[0];
    Healer[] healersCache = new Healer[0];
    float nextMapCache;

    GUIStyle label, bold, center, worldStyle, header, big, small, smallBold, smallRight, barText, tabStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        prompts.Clear();
        messages.Clear();
        worldTexts.Clear();

        if (FindAnyObjectByType<GameHUD>() == null)
            new GameObject("GameHUD").AddComponent<GameHUD>();
    }

    void Awake()
    {
        instance = this;
        HudUi.Ensure(transform);   // nový HUD (UI Toolkit); čo ešte neprevzal, kreslí ďalej OnGUI
    }

    void OnDisable()
    {
        if (menuOpen) CloseMenu();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (Time.unscaledTime >= nextRefresh && (stats == null || dayNight == null))
        {
            nextRefresh = Time.unscaledTime + 1f;
            stats = FindAnyObjectByType<PlayerStats>();
            faction = FindAnyObjectByType<PlayerFaction>();
            senses = FindAnyObjectByType<DemonSenses>();
            ability = FindAnyObjectByType<FactionAbility>();
            dayNight = FindAnyObjectByType<DayNightCycle>();
        }
        // Diagnostika: HUD a mapa sa bez hráča nekreslia, tak sa raz zapíše prečo hráč nie je nájdený
        if (stats == null && !PauseMenu.AtStartup) diagTimer += Time.unscaledDeltaTime;
        if (stats == null && !diagLogged && diagTimer > 6f)
        {
            diagLogged = true;
            PlayerStats[] all = FindObjectsByType<PlayerStats>(FindObjectsInactive.Include);
            string where = all.Length > 0 ? $"{all[0].name} (aktívny v hierarchii: {all[0].gameObject.activeInHierarchy}, enabled: {all[0].enabled})" : "žiadny";
            Debug.LogWarning($"[HudDiag] HUD nemá hráča. PlayerStats v scéne: {all.Length}, prvý: {where}. Čas {Time.unscaledTime:0}s, AtStartup={PauseMenu.AtStartup}.");
        }

        if (ability == null && stats != null) ability = stats.GetComponent<FactionAbility>();
        if (miniMap == null && stats != null) miniMap = MiniMap.Create();

        if (GameInput.GetKeyDown(KeyCode.F1)) showHelp = !showHelp;

        // "=" zapne / vypne minimapu (v dialógu sa skrýva sama)
        if (!menuOpen && !MangaDialog.Active && (GameInput.GetKeyDown(KeyCode.Equals) || GameInput.inputString.IndexOf((char)61) >= 0)) MiniMapVisible = !MiniMapVisible;
        HandleMenuInput();

        if (miniMap != null)
        {
            bool mapTab = menuOpen && tab == TabMap;
            if (mapTab && !wasMapTab) InitMapTerritory();
            wasMapTab = mapTab;

            miniMap.MapMode = mapTab;
            miniMap.MapZoom = mapZoom;
        }

        if (Time.unscaledTime >= nextMapCache)
        {
            nextMapCache = Time.unscaledTime + 2f;
            zonesCache = FindObjectsByType<MilitaryZone>();
            healersCache = FindObjectsByType<Healer>();
        }

        prompts.RemoveAll(p => p.frame < Time.frameCount - 2);
        messages.RemoveAll(m => Time.time > m.until);
        worldTexts.RemoveAll(w => Time.time - w.time > WorldTextSeconds);
    }

    // ---------- Menu: otváranie a ovládanie ----------
    void HandleMenuInput()
    {
        bool dead = stats != null && stats.IsDead;

        if (menuOpen)
        {
            if (dead || ArakaniFavor.MenuOpen || GameInput.GetKeyDown(KeyCode.Tab) || GameInput.GetKeyDown(KeyCode.Escape))
            {
                CloseMenu();
                return;
            }

            if (GameInput.GetKeyDown(KeyCode.M))
            {
                if (tab == TabMap) { CloseMenu(); return; }
                tab = TabMap;
            }

            for (int i = 0; i < tabNames.Length; i++)
                if (GameInput.GetKeyDown(KeyCode.Alpha1 + i)) tab = i;

            if (GameInput.GetKeyDown(KeyCode.RightArrow)) tab = (tab + 1) % tabNames.Length;
            if (GameInput.GetKeyDown(KeyCode.LeftArrow)) tab = (tab + tabNames.Length - 1) % tabNames.Length;

            if (tab == TabMap)
            {
                float scroll = GameInput.mouseScrollDelta.y;
                if (scroll > 0f || GameInput.GetKeyDown(KeyCode.Equals) || GameInput.GetKeyDown(KeyCode.KeypadPlus)) mapZoom *= 0.8f;
                if (scroll < 0f || GameInput.GetKeyDown(KeyCode.Minus) || GameInput.GetKeyDown(KeyCode.KeypadMinus)) mapZoom *= 1.25f;
                mapZoom = Mathf.Clamp(mapZoom, 0.3f, 2.5f);

                // Posun mapy územia klávesmi W A S D (ťahaním myšou sa posúva v DrawMapBox)
                if (miniMap != null)
                {
                    Vector2 move = new Vector2((GameInput.GetKey(KeyCode.D) ? 1f : 0f) - (GameInput.GetKey(KeyCode.A) ? 1f : 0f),
                                               (GameInput.GetKey(KeyCode.W) ? 1f : 0f) - (GameInput.GetKey(KeyCode.S) ? 1f : 0f));
                    if (move != Vector2.zero) miniMap.Pan(move * miniMap.CurrentSize * 1.2f * Time.unscaledDeltaTime);
                }
            }
            return;
        }

        if (stats == null || dead || Healer.MenuOpen) return;

        if (GameInput.GetKeyDown(KeyCode.Tab)) OpenMenu(TabChar);
        else if (GameInput.GetKeyDown(KeyCode.M)) OpenMenu(TabMap);
    }

    void OpenMenu(int startTab)
    {
        menuOpen = true;
        tab = startTab;
        menuOpenedAt = Time.unscaledTime;

        savedTimeScale = Time.timeScale;
        savedLock = Cursor.lockState;
        savedCursorVisible = Cursor.visible;

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void CloseMenu()
    {
        if (!menuOpen) return;

        menuOpen = false;
        closedFrame = Time.frameCount;

        Time.timeScale = savedTimeScale > 0f ? savedTimeScale : 1f;
        Cursor.lockState = savedLock;
        Cursor.visible = savedCursorVisible;

        if (miniMap != null) miniMap.MapMode = false;
    }

    // Cache textov HUD: reťazec sa skladá len pri zmene hodnoty (pozri HudText)
    private readonly HudText tHp = new HudText(), tMoney = new HudText(), tSusp = new HudText(),
        tInvis = new HudText(), tOverclock = new HudText(), tVis = new HudText(),
        tDistrict = new HudText(), tOutfit = new HudText(), tFaction = new HudText(),
        tSenses = new HudText(), tSenseKeys = new HudText(), tClock = new HudText();

    // ---------- Štýly ----------
    void InitStyles()
    {
        label = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
        bold = new GUIStyle(label) { fontStyle = FontStyle.Bold };
        center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        worldStyle = new GUIStyle(center) { fontSize = 19, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
        // Čísla poškodenia: hrubé systémové písmo (bez súboru v projekte); ak v systéme nie je, ostane predvolené tučné
        if (damageFont == null) damageFont = Font.CreateDynamicFontFromOSFont(new[] { "Impact", "Arial Black", "Segoe UI Black" }, 32);
        if (damageFont != null)
        {
            worldStyle.font = damageFont;
            worldStyle.fontStyle = FontStyle.Normal;
        }

        header = new GUIStyle(bold) { fontSize = 16, normal = { textColor = new Color(0.85f, 0.7f, 1f) } };
        big = new GUIStyle(bold) { fontSize = 30 };
        small = new GUIStyle(label) { fontSize = 12, wordWrap = false };
        smallBold = new GUIStyle(small) { fontStyle = FontStyle.Bold };
        smallRight = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
        barText = new GUIStyle(label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
            padding = new RectOffset(4, 0, 0, 0)
        };
        tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
    }

    void OnGUI()
    {
        if (Inventory.MenuOpen) return;   // okno inventára prekrýva celú obrazovku
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (label == null) InitStyles();

        // Iný OnGUI mohol nechať priehľadnú farbu alebo vypnuté GUI: HUD by sa nakreslil neviditeľne
        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.contentColor = Color.white;
        GUI.backgroundColor = Color.white;

        DrawVignette();

        float s = Mathf.Clamp(Screen.height / 720f, 0.85f, 1.6f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        float vh = Screen.height / s;

        if (stats != null) DrawHud(vw, vh);
        DrawBottom(vw, vh);
        DrawWorldTexts(s);
        if (menuOpen && stats != null) DrawMenu(vw, vh);

        GUI.matrix = Matrix4x4.identity;
        GUI.color = Color.white;
        GUI.contentColor = Color.white;
        GUI.backgroundColor = Color.white;

        DrawMangaPanel();
    }

    // ---------- Tunelové videnie (kláves E) ----------
    static Texture2D vignette;

    static Texture2D BuildVignette()
    {
        const int n = 128;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;

        Color[] px = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float dx = x / (n - 1f) * 2f - 1f;
                float dy = y / (n - 1f) * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;     // 0 stred, 1 roh

                float u = Mathf.Clamp01((r - 0.4f) / 0.6f);
                px[y * n + x] = new Color(1f, 1f, 1f, u * u * (3f - 2f * u));
            }
        }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    /// <summary>Textúra tunelového videnia (nový HUD ju kreslí pod svojimi panelmi).</summary>
    public static Texture2D VignetteTexture
    {
        get
        {
            if (vignette == null) vignette = BuildVignette();
            return vignette;
        }
    }

    void DrawVignette()
    {
        float b = PerceptionGlow.Blend;
        if (b <= 0.01f || HudUi.BottomActive) return;   // pri novom HUD-e je vineta pod jeho panelmi, nie cez ne
        if (vignette == null) vignette = BuildVignette();

        Color prev = GUI.color;
        GUI.color = PerceptionGlow.PlayerIsDemon
            ? new Color(0.4f, 0f, 0.03f, 0.9f * b)       // démon: krvavo červené okraje
            : new Color(0f, 0.04f, 0.18f, 0.85f * b);    // človek: tmavomodré okraje
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), vignette);
        GUI.color = prev;
    }

    // Biely panel s čiernym rámom, rýchlostnými čiarami a veľkým nápisom
    void DrawMangaPanel()
    {
        if (Time.time >= panelUntil || string.IsNullOrEmpty(panelText)) return;

        float w = Screen.width, h = Screen.height;
        float t = 1f - (panelUntil - Time.time) / panelDuration;       // 0 -> 1
        float alpha = 1f - t * t;                                      // na konci zmizne
        Vector2 mid = new Vector2(w * 0.5f, h * 0.5f);

        // Čierne pásy hore a dole
        GUI.color = new Color(0f, 0f, 0f, 0.85f * alpha);
        GUI.DrawTexture(new Rect(0f, 0f, w, h * 0.12f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0f, h * 0.88f, w, h * 0.12f), Texture2D.whiteTexture);

        // Panel: čierny rám + biela výplň
        float m = Mathf.Lerp(0.2f, 0.15f, t);
        Rect panel = new Rect(w * m, h * 0.2f, w * (1f - 2f * m), h * 0.6f);
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, 0.8f * alpha);
        GUI.DrawTexture(new Rect(panel.x + 6f, panel.y + 6f, panel.width - 12f, panel.height - 12f), Texture2D.whiteTexture);

        // Rýchlostné čiary vychádzajúce zo stredu
        GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
        for (int i = 0; i < 16; i++)
        {
            GUI.matrix = Matrix4x4.identity;
            GUIUtility.RotateAroundPivot(i * 22.5f + 8f, mid);
            GUI.DrawTexture(new Rect(mid.x + h * 0.16f, mid.y - 2f, h * 0.2f, 4f), Texture2D.whiteTexture);
        }

        // Nápis (mierne natočený, s tieňom)
        GUI.matrix = Matrix4x4.identity;
        GUIUtility.RotateAroundPivot(-8f, mid);

        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(h * 0.2f),
            fontStyle = FontStyle.Bold,
            wordWrap = false
        };
        Rect textRect = new Rect(panel.x, panel.y, panel.width, panel.height);

        style.normal.textColor = new Color(0.9f, 0.1f, 0.1f, alpha);
        GUI.Label(new Rect(textRect.x + 6f, textRect.y + 6f, textRect.width, textRect.height), panelText, style);
        style.normal.textColor = new Color(0f, 0f, 0f, alpha);
        GUI.Label(textRect, panelText, style);

        GUI.matrix = Matrix4x4.identity;
        GUI.color = Color.white;
    }

    // =====================================================================
    //  HUD NA OBRAZOVKE (len základ)
    // =====================================================================
    void DrawHud(float vw, float vh)
    {
        DrawQuestArrow(vw, vh);
        const float pad = 10f, barW = 230f;

        // Pravý blok: šírka je obmedzená, aby nezasahoval do stredu
        float rightW = Mathf.Min(hudRightMaxWidth, vw * 0.30f);

        // ĽAVÝ HORNÝ ROH: HP, pod ním tenká výdrž a tenký hlad/krv, bez medzier
        float x = pad, y = pad;

        // Ľavý panel kreslí nový HUD (HudUi); pôvodná verzia ostáva ako záloha, keby sa nepostavil
        if (HudUi.LeftPanelActive) y = pad + 200f;
        else
        {
        if (tHp.Stale(Mathf.RoundToInt(stats.Health), Mathf.RoundToInt(stats.MaxHealth)))
            tHp.Text = $"HP {stats.Health:0}/{stats.MaxHealth:0}";
        y = Bar(x, y, barW, 18f, stats.Health / stats.MaxHealth, colHp, tHp.Text, 1f);
        y = Bar(x, y, barW, 6f, stats.Stamina / stats.maxStamina, colStamina, null, 1f);

        if (stats.IsDemon)
            y = Bar(x, y, barW, 8f, stats.Blood / stats.maxBlood, colBlood, null, 0f, 0.25f);
        else
            y = Bar(x, y, barW, 8f, stats.Hunger / stats.maxHunger, colHunger, null, 0f, 0.25f);
        y += 3f;

        // Forma a peniaze v jednom riadku
        GUI.contentColor = stats.IsDemon ? new Color(1f, 0.45f, 0.55f) : new Color(0.6f, 0.8f, 1f);
        GUI.Label(new Rect(x, y, barW * 0.5f, 16f), stats.IsDemon ? "DÉMON" : "ČLOVEK", smallBold);
        GUI.contentColor = Color.white;
        if (tMoney.Stale((int)stats.money)) tMoney.Text = $"Peniaze: {stats.money}";
        GUI.Label(new Rect(x + barW * 0.5f, y, barW * 0.5f, 16f), tMoney.Text, smallRight);
        y += 19f;

        // Upozornenia (len keď sú aktuálne)
        if (stats.IsWeakened)
            y = Line(x, y, barW, stats.IsDemon ? "SLABNEŠ: potrebuješ krv!" : "HLADUJEŠ!",
                bold, new Color(1f, 0.45f, 0.3f));
        if (stats.IsDead)
            y = Line(x, y, barW, "ZOMREL/A SI - hra sa vráti do menu (R hneď)", bold, new Color(1f, 0.3f, 0.3f));
        if (Reputation.Suspicion >= 1f)
        {
            if (tSusp.Stale(Mathf.RoundToInt(Reputation.Suspicion))) tSusp.Text = $"Podozrenie mesta: {Reputation.Suspicion:0}";
            y = Line(x, y, barW, tSusp.Text, bold, new Color(1f, 0.55f, 0.3f));
        }
        if (stats.IsInvisible)
        {
            if (tInvis.Stale(Mathf.RoundToInt(stats.InvisibleLeft * 10f))) tInvis.Text = $"NEVIDITEĽNÝ {stats.InvisibleLeft:0.0} s";
            y = Line(x, y, barW, tInvis.Text, bold, new Color(0.7f, 0.5f, 1f));
        }
        if (stats.OverclockActive)
        {
            if (tOverclock.Stale(Mathf.RoundToInt(stats.OverclockLeft * 10f))) tOverclock.Text = $"OVERCLOCK {stats.OverclockLeft:0.0} s";
            y = Line(x, y, barW, tOverclock.Text, bold, new Color(1f, 0.9f, 0.2f));
        }

        // Zakrádanie, štvrť a oblečenie
        PlayerStealth stealth = PlayerStealth.Instance;
        Wardrobe wardrobe = Wardrobe.Instance;
        if (stealth != null && !stats.IsDead)
        {
            float vis = stealth.Visibility;
            Color vc = vis < 0.6f ? new Color(0.5f, 1f, 0.6f) : vis < 1f ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.5f, 0.4f);
            if (tVis.Stale(Mathf.RoundToInt(vis * 100f), stealth.IsCrouched ? 1 : 0))
                tVis.Text = $"{(stealth.IsCrouched ? "ČUPNUTÝ  |  " : "")}Viditeľnosť: {vis * 100f:0} %";
            y = Line(x, y, barW, tVis.Text, stealth.IsCrouched ? bold : label, vc);
        }
        if (wardrobe != null && !stats.IsDead)
        {
            OutfitRule rule = wardrobe.CurrentRule;
            Color rc = rule == OutfitRule.Forbidden ? new Color(1f, 0.45f, 0.35f)
                     : rule == OutfitRule.Welcome ? new Color(0.55f, 1f, 0.6f) : Color.white;
            if (tDistrict.Stale((int)wardrobe.District)) tDistrict.Text = $"Štvrť: {CityDistrict.NameOf(wardrobe.District)}";
            y = Line(x, y, barW, tDistrict.Text, label, new Color(1f, 1f, 1f, 0.8f));
            if (tOutfit.Stale((int)wardrobe.current, (int)rule))
                tOutfit.Text = $"Oblečenie: {Wardrobe.OutfitName(wardrobe.current)} ({Wardrobe.RuleText(rule)})";
            y = Line(x, y, barW, tOutfit.Text, label, rc);
        }

        }

        // PRAVÝ HORNÝ ROK (pri úzkom okne pokračuje pod ľavým): ovládanie, frakcia, schopnosti, zmysly
        // POSUNUTÉ DOPRAVA: rx = vw - rightW - pad - extra offset (aby blok bol viac pri okraji)
        bool narrow = vw < barW + rightW + pad * 4f + hudRightExtra;
        float rx = narrow ? x : vw - rightW - pad;
        float rw = rightW;
        float ry = narrow ? y + 10f : pad;

        if (!narrow)
        {
            // Nikdy neprekroč doprava cez okraj
            rx = Mathf.Max(x, rx);
            rw = Mathf.Min(rightW, vw - rx - pad);
        }

        // Pravý panel kreslí nový HUD (HudUi); pôvodná verzia ostáva ako záloha
        bool oldRight = !HudUi.RightPanelActive;
        if (oldRight)
        {
            ry = Line(rx, ry, rw, showHelp ? HelpText() : "F1 ovládanie  |  Tab menu  |  M mapa", label,
                showHelp ? Color.white : dim);
            ry += 6f;
        }

        FactionProfile p = oldRight && faction != null ? FactionData.Get(faction.Current) : null;

        if (p != null && !stats.IsDead)
        {
            if (tFaction.Stale((int)faction.Current)) tFaction.Text = $"Frakcia: {p.name}";
            ry = Line(rx, ry, rw, tFaction.Text, bold, Color.white);

            if (p.isDemonClan != stats.IsDemon)
                ry = Line(rx, ry, rw, "(frakcia nesedí s tvojou formou)", label, dim);
            else if (faction.state == FactionState.Desperate)
                ry = Line(rx, ry, rw, "NÚDZOVÝ STAV", bold, new Color(1f, 0.5f, 0.3f));
            ry += 3f;

            if (ability != null && p.ability != AbilityKind.None)
                ry = AbilityRow(rx, ry, rw, ability.abilityKey, p.abilityName,
                    ability.FormMatches, ability.CooldownLeft, ability.CooldownTotal);

            if (ability != null && p.ability2 != AbilityKind.None)
                ry = AbilityRow(rx, ry, rw, ability.abilityKey2, p.ability2Name,
                    p.isDemonClan == stats.IsDemon, ability.Cooldown2Left, ability.Cooldown2Total);
        }

        // Zmysly (len démon)
        if (oldRight && senses != null && stats.IsDemon && !stats.IsDead)
        {
            ry += 3f;
            if (tSenses.Stale((int)senses.State, (int)senses.focus, (int)senses.toggleKey))
            {
                switch (senses.State)
                {
                    case SensesState.Full:
                        tSenses.Text = "ZMYSLY: PLNÉ (krv ubúda rýchlo)"; break;
                    case SensesState.Focused:
                        tSenses.Text = $"ZMYSLY: ZAMERANIE NA {DemonSenses.StatName(senses.focus)}"; break;
                    default:
                        tSenses.Text = $"Zmysly: vypnuté [{senses.toggleKey}]"; break;
                }
            }
            ry = Line(rx, ry, rw, tSenses.Text, bold, Color.white);

            if (tSenseKeys.Stale((int)senses.focus))
                tSenseKeys.Text =
                    $"{Mark(SenseStat.Strength)}[1] Sila  {Mark(SenseStat.Speed)}[2] Rýchlosť  " +
                    $"{Mark(SenseStat.Stamina)}[3] Výdrž  {Mark(SenseStat.Faction)}[4] Klan";
            ry = Line(rx, ry, rw, tSenseKeys.Text, small, Color.white);
            ry = Line(rx, ry, rw, "[5] Cestovanie koreňmi / stromy temnoty", small, new Color(0.7f, 0.5f, 1f));

            if (senses.BloodVisionActive)
            {
                float d = senses.NearestBloodDistance;
                string near = d >= 0f ? $"najbližšia krv: {d:0} m" : "žiadna krv v dosahu";
                ry = Line(rx, ry, rw, $"KRVAVÉ VIDENIE - {near}", bold, new Color(1f, 0.35f, 0.35f));
            }
        }

        // MINIMAPA (pravý dolný roh)
        if (MiniMapShown)
        {
            Rect framed = HudUi.MapRectPixels;
            if (framed.width > 1f)
            {
                // Rám a hodiny kreslí nový HUD; sem patrí len textúra mapy a značky (prepočet z pixelov do mierky tohto GUI)
                float s = GUI.matrix.m00;
                DrawMapBox(new Rect(framed.x / s, framed.y / s, framed.width / s, framed.height / s), false);
            }
            else
            {
                const float mm = 150f;
                float mapX = vw - mm - pad - hudRightOffset * 0.4f;
                Rect r = new Rect(mapX, vh - mm - pad - 18f, mm, mm);
                DrawMapBox(r, false);

                string clock = ClockShort();
                if (clock != null)
                    GUI.Label(new Rect(r.x, r.yMax + 2f, mm, 16f), clock, small);
            }
        }
    }

    // Jeden riadok schopnosti: [X] Názov - stav (+ tenký pásik cooldownu)
    float AbilityRow(float x, float y, float w, KeyCode key, string name, bool formOk, float cdLeft, float cdTotal)
    {
        string status;
        Color col;

        if (!formOk) { status = "iná forma"; col = dim; }
        else if (cdLeft > 0f) { status = $"{cdLeft:0.0} s"; col = new Color(0.65f, 0.65f, 0.85f); }
        else { status = "PRIPRAVENÁ"; col = new Color(0.5f, 1f, 0.6f); }

        y = Line(x, y, w, $"[{key}] {name} - {status}", label, col);

        if (formOk && cdLeft > 0f)
            y = Bar(x, y - 1f, w, 3f, 1f - cdLeft / Mathf.Max(0.01f, cdTotal), new Color(0.4f, 0.4f, 0.65f), null, 3f);

        return y;
    }

    string Mark(SenseStat s) => senses != null && senses.focus == s ? "> " : "";

    // =====================================================================
    //  MENU (Tab)
    // =====================================================================
    void DrawMenu(float vw, float vh)
    {
        Fill(new Rect(0f, 0f, vw, vh), new Color(0f, 0f, 0f, 0.65f));

        float pw = Mathf.Min(820f, vw - 40f);
        float ph = Mathf.Min(540f, vh - 40f);
        Rect p = new Rect((vw - pw) * 0.5f, (vh - ph) * 0.5f, pw, ph);

        Fill(p, new Color(0.04f, 0.04f, 0.07f, 0.97f));
        Fill(new Rect(p.x, p.y, p.width, 3f), new Color(0.6f, 0.3f, 0.9f));

        // Karty
        float tw = (pw - 20f) / tabNames.Length;
        for (int i = 0; i < tabNames.Length; i++)
        {
            Rect tr = new Rect(p.x + 10f + i * tw, p.y + 10f, tw - 4f, 28f);
            GUI.backgroundColor = i == tab ? new Color(0.65f, 0.3f, 0.95f) : new Color(0.35f, 0.35f, 0.4f);
            if (GUI.Button(tr, $"{i + 1}  {tabNames[i]}", tabStyle)) tab = i;
        }
        GUI.backgroundColor = Color.white;

        Rect c = new Rect(p.x + 18f, p.y + 52f, pw - 36f, ph - 52f - 28f);

        switch (tab)
        {
            case TabChar: DrawTabCharacter(c); break;
            case TabTime: DrawTabTime(c); break;
            case TabFaction: DrawTabFaction(c); break;
            case TabMap: DrawTabMap(c); break;
            case TabQuest: DrawTabQuest(c); break;
            case TabSkills: DrawTabSkills(c); break;
            case TabCast: DrawTabCast(c); break;
            default: Line(c.x, c.y, c.width, HelpText(), label, Color.white); break;
        }

        GUI.Label(new Rect(p.x, p.yMax - 24f, pw, 20f),
            "Tab / Esc zavrie  |  1-8 alebo šípky = karty  |  M mapa", center);
    }

    // Karta Úlohy: aktuálna úloha s krokmi (to, čo kreslí QuestPanel v hre) a zoznam hlavných úloh
    void DrawTabQuest(Rect c)
    {
        Color gold = new Color(1f, 0.82f, 0.35f), done = new Color(0.45f, 0.8f, 0.5f), dim = new Color(1f, 1f, 1f, 0.45f);
        float x = c.x + 10f, y = c.y + 6f, w = c.width * 0.56f;

        y = Line(x, y, w, "AKTUÁLNA ÚLOHA", header, Color.white);
        bool active = QuestPanel.JournalTitle != null && QuestPanel.JournalTime >= menuOpenedAt - 1.5f
                      && (Prologue.Active || MainQuest.Active || Quest2.Active || QuestPanel.JournalTime >= menuOpenedAt - 0.2f);
        if (!active)
        {
            y = Line(x, y + 6f, w, "Práve nemáš žiadnu úlohu.", label, dim);
            y = Line(x, y + 4f, w, "Nové úlohy zadávajú postavy označené na mape. Porozprávaj sa s ľuďmi v meste alebo počkaj na ďalší deň.", label, dim);
        }
        else
        {
            y = Line(x, y + 4f, w, QuestPanel.JournalTitle, big, gold);
            y += 6f;
            string[] steps = QuestPanel.JournalSteps;
            if (steps != null)
                for (int i = 0; i < steps.Length; i++)
                {
                    bool isDone = i < QuestPanel.JournalCurrent, now = i == QuestPanel.JournalCurrent;
                    Fill(new Rect(x + 2f, y + 5f, 11f, 11f), isDone ? done : now ? gold : dim);
                    y = Line(x + 22f, y, w - 22f, steps[i] + (isDone ? "  (splnené)" : ""), now ? bold : label, isDone ? done : now ? Color.white : dim);
                }
            y += 10f;
            Fill(new Rect(x, y, w, 2f), new Color(1f, 1f, 1f, 0.12f));
            y = Line(x, y + 8f, w, "CIEĽ", smallBold, gold);
            y = Line(x, y, w, QuestPanel.JournalGoal, label, Color.white);
            if (QuestPanel.JournalProgress >= 0f)
                y = Bar(x, y + 6f, w, 16f, QuestPanel.JournalProgress, gold, $"{QuestPanel.JournalProgress * 100f:0} %", 4f);
        }

        // Pravý stĺpec: hlavné úlohy príbehu
        float rx = c.x + c.width * 0.62f, ry = c.y + 6f, rw = c.width * 0.36f;
        ry = Line(rx, ry, rw, "HLAVNÉ ÚLOHY", header, Color.white);
        bool prologueDone = Story.PrologueDone || Prologue.Step == PrologueStep.Done;
        ry = QuestRow(rx, ry, rw, "Noc, kedy zhasli neóny", prologueDone, Prologue.Active, done, gold, dim);
        ry = QuestRow(rx, ry, rw, MainQuest.Title, MainQuest.State == QuestState.Done || MainQuest.State == QuestState.Resolved, MainQuest.Active, done, gold, dim);
        ry = QuestRow(rx, ry, rw, Quest2.Title, Quest2.Step == Quest2Step.Done, Quest2.Active, done, gold, dim);
        Line(rx, ry + 10f, rw, "Zelená = splnené\nZlatá = prebieha\nSivá = ešte nezačatá", small, dim);
    }

    float QuestRow(float x, float y, float w, string title, bool finished, bool running, Color done, Color gold, Color dim)
    {
        Color color = finished ? done : running ? gold : dim;
        Fill(new Rect(x + 2f, y + 6f, 9f, 9f), color);
        return Line(x + 18f, y, w - 18f, title + (finished ? "  ✓" : running ? "  (prebieha)" : ""), running ? bold : label, color);
    }

    // Karta Postavy: zoznam z Postavy.txt, detail len pri stretnutých postavách
    void DrawTabCast(Rect c)
    {
        float x = c.x + 10f, y = c.y + 6f, listW = 240f;
        float ly = Line(x, y, listW, $"POSTAVY  ({StoryCast.MetCount}/{StoryCast.Defs.Length} stretnutých)", header, Color.white);
        for (int i = 0; i < StoryCast.Defs.Length; i++)
        {
            CharDef d = StoryCast.Defs[i];
            GUI.backgroundColor = i == castSelected ? d.color : Color.white;
            string text = (i == castSelected ? "► " : "") + (StoryCast.Met(d.id) ? d.name : "???");
            if (GUI.Button(new Rect(x, ly, listW, 26f), text)) castSelected = i;
            ly += 29f;
        }
        GUI.backgroundColor = Color.white;

        float dx = x + listW + 20f, dw = c.width - listW - 50f, dy = y;
        CharDef sel = StoryCast.Defs[Mathf.Clamp(castSelected, 0, StoryCast.Defs.Length - 1)];
        if (!StoryCast.Met(sel.id))
        {
            Line(dx, dy, dw, "Túto postavu si ešte nestretol. Stretneš ju v meste, v divočine, pri bossoch alebo v príbehu.", label, dim);
            return;
        }

        dy = Line(dx, dy, dw, sel.name, header, Color.Lerp(sel.color, Color.white, 0.45f));
        dy = Line(dx, dy, dw, $"{sel.title}  ·  {sel.side}", bold, dim);
        dy += 8f;
        dy = Line(dx, dy, dw, sel.bio, label, Color.white);
        dy += 8f;
        dy = Line(dx, dy, dw, "SLABINA V BOJI", bold, new Color(1f, 0.85f, 0.3f));
        dy = Line(dx, dy, dw, sel.weakness, label, Color.white);
        dy += 8f;
        dy = Line(dx, dy, dw, "AK JU PORAZÍŠ", bold, new Color(0.7f, 0.9f, 1f));
        Line(dx, dy, dw, sel.consequence, label, Color.white);
    }

    // ----- Karta: Skilly (strom schopností) -----
    private GUIStyle skillGlyphStyle, skillTipStyle;
    private string skillHover;

    // Čiara medzi dvoma bodmi v súradniciach karty. RotateAroundPivot sa nepoužíva: pri škálovanom GUI.matrix (HUD sa škáluje podľa rozlíšenia)
    // počíta pivot v neškálovaných súradniciach, takže čiary sedeli vedľa štvorčekov. Matica sa skladá ručne: najprv posun + rotácia, potom pôvodná matica.
    void DrawSkillLine(Vector2 a, Vector2 b, Color col, float width)
    {
        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 0.5f) return;

        Matrix4x4 saved = GUI.matrix;
        Color oldColor = GUI.color;
        GUI.matrix = saved * Matrix4x4.TRS(new Vector3(a.x, a.y, 0f), Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg), Vector3.one);
        GUI.color = col;
        GUI.DrawTexture(new Rect(0f, -width * 0.5f, length, width), Texture2D.whiteTexture);
        GUI.matrix = saved;
        GUI.color = oldColor;
    }

    static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    void DrawTabSkills(Rect c)
    {
        if (skillGlyphStyle == null)
        {
            skillGlyphStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false };
            skillTipStyle = new GUIStyle(label) { richText = true, wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = 13 };
        }

        float x = c.x + 12f, y = c.y + 2f, w = c.width - 24f;
        y = Line(x, y, w, $"STROM SCHOPNOSTÍ   Level {stats.skillLevel}   {(stats.skillLevel >= PlayerStats.MaxLevel ? "MAX" : $"XP {stats.skillXp}/{stats.SkillXpToNext}")}   Skill pointy: {stats.skillPoints}", header, new Color(1f, 0.9f, 0.5f));

        Rect tree = new Rect(c.x, y + 2f, c.width * 0.68f, c.yMax - y - 6f);
        Rect side = new Rect(tree.xMax + 10f, tree.y, c.xMax - tree.xMax - 14f, tree.height);

        Vector2 center = tree.center;
        float scale = Mathf.Min(tree.width * 0.5f - 24f, tree.height * 0.5f - 6f) / 4.85f;
        float size = Mathf.Clamp(scale * 0.66f, 22f, 54f);
        Vector2 mouse = Event.current.mousePosition;
        bool click = Event.current.type == EventType.MouseDown && Event.current.button == 0;

        System.Func<SkillTree.Node, Vector2> pos = n =>
        {
            float rad = n.angle * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad)) * (n.radius * scale);
        };

        // Spoje: svietia, kým sú odomknuté obe strany
        foreach (SkillTree.Node n in SkillTree.Nodes)
        {
            SkillTree.Node parent = SkillTree.Get(n.parent);
            if (parent == null) continue;
            bool lit = SkillTree.Has(n.id) && SkillTree.Has(parent.id);
            DrawSkillLine(pos(parent), pos(n), lit ? n.color : new Color(0.3f, 0.3f, 0.35f, 0.9f), lit ? 4f : 2f);
        }

        skillHover = null;
        foreach (SkillTree.Node n in SkillTree.Nodes)
        {
            Vector2 p = pos(n);
            float s = n.tier == 0 ? size * 1.25f : size;
            Rect r = new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s);

            bool unlocked = SkillTree.Has(n.id);
            SkillTree.Reason reason = SkillTree.CanUnlock(n.id, stats.skillLevel, stats.skillPoints);
            bool available = reason == SkillTree.Reason.Ok;
            bool hovered = r.Contains(mouse);
            if (hovered) skillHover = n.id;

            Color fill = unlocked ? n.color : available ? n.color * 0.55f : new Color(0.14f, 0.14f, 0.17f);
            fill.a = 1f;
            Color border = unlocked ? Color.white : available ? Color.Lerp(n.color, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f)) : new Color(0.32f, 0.32f, 0.38f);

            GUI.color = border;
            GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;

            skillGlyphStyle.normal.textColor = unlocked ? Color.black : available ? Color.white : new Color(0.55f, 0.55f, 0.6f);
            GUI.Label(r, n.glyph, skillGlyphStyle);

            if (click && hovered)
            {
                if (reason == SkillTree.Reason.Ok)
                {
                    stats.TryUnlockSkill(n.id);
                    Message($"Odomknuté: {n.name}", 2.5f);
                }
                else Message(ReasonText(n, reason), 2.5f);
                Event.current.Use();
            }
        }

        DrawSkillSide(side);
        if (skillHover != null) DrawSkillTip(SkillTree.Get(skillHover), mouse, c);
    }

    static string SkillPointWord(int n) => n == 1 ? "skill point" : n >= 2 && n <= 4 ? "skill pointy" : "skill pointov";

    string ReasonText(SkillTree.Node n, SkillTree.Reason reason)
    {
        switch (reason)
        {
            case SkillTree.Reason.AlreadyUnlocked: return $"{n.name} už máš.";
            case SkillTree.Reason.NeedParent: return $"Najprv odomkni: {SkillTree.Get(n.parent).name}.";
            case SkillTree.Reason.NeedLevel: return $"Treba level {n.minLevel} (máš {stats.skillLevel}).";
            default: return $"Treba {n.cost} {SkillPointWord(n.cost)} (máš {stats.skillPoints}).";
        }
    }

    // Pravý panel: zhrnutie aktívnych bonusov a vysvetlenie pravidiel
    void DrawSkillSide(Rect r)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = r.x + 10f, y = r.y + 8f, w = r.width - 20f;
        y = Line(x, y, w, $"Odomknuté: {SkillTree.UnlockedCount}/{SkillTree.Nodes.Count}", bold, Color.white);
        y += 4f;
        y = Line(x, y, w, "AKTÍVNE BONUSY", bold, new Color(0.6f, 0.95f, 0.6f));
        y = Line(x, y, w, SkillTree.Summary(), label, Color.white);
        y += 10f;
        y = Line(x, y, w, "Podrž kurzor nad uzlom pre podrobný popis. Klikni na žiariaci uzol, aby si ho odomkol.", label, new Color(0.8f, 0.8f, 0.85f));
        y += 6f;
        y = Line(x, y, w, "Schopnosti sa prenášajú do oboch foriem (človek aj démon); uzly označené „iba démon“ platia len v jeho podobe. Skill pointy dostávaš za každý level schopností (1 level = 1 skill point, každý 10. level 2; maximum je level 60); silnejšie schopnosti stoja viac a vyžadujú vyšší level.", label, new Color(0.8f, 0.8f, 0.85f));
        y += 10f;

        int refund = SkillTree.SpentPoints();
        GUI.enabled = refund > 0;
        if (GUI.Button(new Rect(x, y, w, 28f), $"Znova rozdeliť skill pointy (+{refund})"))
        {
            stats.RefundSkills();
            Message("Skill pointy zo stromu sú späť.", 2.5f);
        }
        GUI.enabled = true;
    }

    // Popis uzla pri kurzore: štáty, ktoré zlepšuje, cena, podmienky
    void DrawSkillTip(SkillTree.Node n, Vector2 mouse, Rect bounds)
    {
        if (n == null) return;

        var sb = new System.Text.StringBuilder();
        sb.Append($"<b><color=#{Hex(n.color)}>{n.name}</color></b>   <i>{n.branch}</i>\n");
        sb.Append($"<i>{n.story}</i>\n\n");
        sb.Append("<b>Zlepšuje:</b>\n");
        foreach (SkillTree.Effect e in n.effects)
        {
            float total = SkillTree.SumAnyForm(e.stat);
            string totalText = e.stat == SkillStat.ExtraJump || e.stat == SkillStat.Leap ? "" : $"   (spolu teraz +{total * 100f:0} %)";
            sb.Append($"• {SkillTree.EffectText(e)}{totalText}\n");
        }
        sb.Append($"\n{SkillTree.FormText(n.form)}\n");

        SkillTree.Reason reason = SkillTree.CanUnlock(n.id, stats.skillLevel, stats.skillPoints);
        sb.Append($"\nCena: <b>{n.cost}</b> {SkillPointWord(n.cost)}");
        if (n.minLevel > 1) sb.Append($"   |   Potrebný level: <b>{n.minLevel}</b>");
        if (n.parent != null) sb.Append($"\nVyžaduje: <b>{SkillTree.Get(n.parent).name}</b>");
        sb.Append("\n");
        if (reason == SkillTree.Reason.AlreadyUnlocked) sb.Append("<color=#7CFC7C><b>Odomknuté</b></color>");
        else if (reason == SkillTree.Reason.Ok) sb.Append("<color=#FFD966><b>Klikni a odomkneš</b></color>");
        else sb.Append($"<color=#FF8888>{ReasonText(n, reason)}</color>");

        float width = 320f;
        GUIContent content = new GUIContent(sb.ToString());
        float height = skillTipStyle.CalcHeight(content, width - 16f) + 14f;
        Rect box = new Rect(mouse.x + 18f, mouse.y + 14f, width, height);
        if (box.xMax > bounds.xMax) box.x = mouse.x - 18f - width;
        if (box.yMax > bounds.yMax) box.y = Mathf.Max(bounds.y, bounds.yMax - height);

        GUI.color = new Color(0.04f, 0.04f, 0.07f, 0.96f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = n.color;
        GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x + 8f, box.y + 7f, width - 16f, height - 10f), content, skillTipStyle);
    }

    // ----- Karta: Postava -----
    void DrawTabCharacter(Rect c)
    {
        float colW = c.width * 0.5f - 12f;
        float x = c.x, y = c.y;

        y = Line(x, y, colW, "POSTAVA", header, Color.white);
        y = Line(x, y, colW, $"Forma: {(stats.IsDemon ? "DÉMON" : "ČLOVEK")}", bold, Color.white);
        y = Line(x, y, colW, $"Človek Lv.{stats.humanLevel}", label, Color.white);
        y = Line(x, y, colW, $"Démon Lv.{stats.demonLevel}", label, Color.white);

        int perLevel = stats.DemonXpToNext;
        bool demonMax = perLevel <= 0;
        y = Bar(x, y, colW, 18f, demonMax ? 1f : (float)stats.demonXp / perLevel, colXp,
            demonMax ? "Démonické XP MAX" : $"Démonické XP {stats.demonXp}/{perLevel}", 4f);
        y = Bar(x, y, colW, 18f, Sanity.Value / 100f, new Color(0.65f, 0.4f, 0.95f), Sanity.Berserk ? "Psychika: BERSERK" : $"Psychika {Sanity.Value:0}/100", 4f);
        y = Bar(x, y, colW, 18f, Propaganda.Heat / 100f, new Color(1f, 0.55f, 0.15f), $"Pozornosť armády {Propaganda.Heat:0}/100", 4f);

        // Opačná forma pomaly slabne: kým si démon, klesá ľudský level, kým si človek, klesá démonický
        float decayLeft = stats.LevelDecayLeft;
        y = Line(x, y, colW, decayLeft > 0f
            ? $"Za {Mathf.CeilToInt(decayLeft)} s klesne {(stats.IsDemon ? "ľudský" : "démonický")} level o 1 (aby si ostal v jednej forme)"
            : $"{(stats.IsDemon ? "Ľudský" : "Démonický")} level je na minime a už neklesá.", label, new Color(0.8f, 0.8f, 0.85f));
        y += 8f;

        y = Bar(x, y, colW, 18f, stats.Health / stats.MaxHealth, colHp,
            $"HP {stats.Health:0}/{stats.MaxHealth:0}", 4f);
        y = Bar(x, y, colW, 18f, stats.Stamina / stats.maxStamina, colStamina,
            $"Výdrž {stats.Stamina:0}/{stats.maxStamina:0}", 4f);

        if (stats.IsDemon)
            y = Bar(x, y, colW, 18f, stats.Blood / stats.maxBlood, colBlood,
                $"Krv {stats.Blood:0}/{stats.maxBlood:0}", 8f);
        else
            y = Bar(x, y, colW, 18f, stats.Hunger / stats.maxHunger, colHunger,
                $"Hlad {stats.Hunger:0}/{stats.maxHunger:0}", 8f);

        y = Line(x, y, colW, $"Peniaze: {stats.money}", label, Color.white);

        // Pravý stĺpec
        float rx = c.x + c.width * 0.5f + 12f, ry = c.y;

        ry = Line(rx, ry, colW, "DÔVERA FRAKCIÍ", header, Color.white);
        foreach (FactionId f in FactionData.HumanFactions)
        {
            float v = Reputation.Get(f);
            ry = Line(rx, ry, colW, $"{FactionData.NameOf(f)}: {v:+0;-0;0}", label,
                v > 0f ? new Color(0.6f, 1f, 0.7f) : v < 0f ? new Color(1f, 0.55f, 0.5f) : Color.white);
        }
        ry = Line(rx, ry, colW, $"Podozrenie mesta: {Reputation.Suspicion:0}", label,
            Reputation.Suspicion >= 1f ? new Color(1f, 0.55f, 0.3f) : Color.white);
        ry += 10f;

        ry = Line(rx, ry, colW, "ARAKANI", header, Color.white);
        ArakaniFavor af = ArakaniFavor.Instance;

        if (af == null || !af.VoiceHeard)
        {
            ry = Line(rx, ry, colW, "Jeho hlas v tvojej hlave mlčí.", label, dim);
        }
        else
        {
            ry = Line(rx, ry, colW, af.HudText, label, new Color(0.85f, 0.45f, 1f));

            if (af.OrderActive)
                ry = Line(rx, ry, colW, $"Rozkaz: {af.CurrentOrderText}", label, Color.white);
            else
                ry = Line(rx, ry, colW, "Dnes nemáš žiadny rozkaz.", label, dim);

            ry += 4f;
            ry = GiftLine(rx, ry, colW, af, af.bloodGiftFavor, "-15 % míňanie krvi");
            ry = GiftLine(rx, ry, colW, af, af.nightGiftFavor, "+10 % poškodenie v noci");
            ry = GiftLine(rx, ry, colW, af, af.veilGiftFavor, $"Závoj [{af.veilKey}]: neviditeľnosť");
        }
    }

    float GiftLine(float x, float y, float w, ArakaniFavor af, float needed, string text)
    {
        bool on = af.Favor >= needed || af.Sealed;
        return Line(x, y, w, $"[{needed:0}] {text}{(on ? "" : " (zamknuté)")}", label,
            on ? new Color(0.6f, 1f, 0.7f) : dim);
    }

    // ----- Karta: Čas a mesiac -----
    void DrawTabTime(Rect c)
    {
        if (dayNight == null)
        {
            Line(c.x, c.y, c.width, "Cyklus dňa a noci sa nenašiel v scéne.", label, dim);
            return;
        }

        float x = c.x, y = c.y;
        float w = Mathf.Min(c.width, 560f);

        int h = Mathf.FloorToInt(dayNight.TimeOfDay);
        int m = Mathf.FloorToInt((dayNight.TimeOfDay - h) * 60f);

        y = Line(x, y, w, $"Deň {dayNight.Day}   {h:00}:{m:00}", big, Color.white);

        Color phaseColor;
        switch (dayNight.CurrentPhase)
        {
            case DayNightCycle.DayPhase.Night: phaseColor = new Color(0.45f, 0.55f, 1f); break;
            case DayNightCycle.DayPhase.Day: phaseColor = new Color(1f, 0.9f, 0.4f); break;
            default: phaseColor = new Color(1f, 0.6f, 0.35f); break;
        }
        y = Line(x, y, w, (dayNight.IsNight ? "NOC" : "DEŇ") + $"  -  {PhaseName(dayNight.CurrentPhase)}", header, phaseColor);
        y += 6f;

        // Graf dňa (0 - 24 h)
        Rect bar = new Rect(x, y, w, 20f);
        float dawn = dayNight.DawnStartHour, day = dayNight.DayStartHour,
              dusk = dayNight.DuskStartHour, night = dayNight.NightStartHour;

        Color cNight = new Color(0.1f, 0.12f, 0.35f), cDawn = new Color(0.9f, 0.55f, 0.3f),
              cDay = new Color(0.95f, 0.85f, 0.4f), cDusk = new Color(0.8f, 0.4f, 0.35f);

        Segment(bar, 0f, dawn, cNight);
        Segment(bar, dawn, day, cDawn);
        Segment(bar, day, dusk, cDay);
        Segment(bar, dusk, night, cDusk);
        Segment(bar, night, 24f, cNight);

        float mx = bar.x + bar.width * Mathf.Clamp01(dayNight.TimeOfDay / 24f);
        Fill(new Rect(mx - 2f, bar.y - 4f, 4f, bar.height + 8f), Color.white);

        for (int t = 0; t <= 24; t += 6)
            GUI.Label(new Rect(bar.x + bar.width * (t / 24f) - 12f, bar.yMax + 4f, 30f, 16f), t.ToString(), small);
        y = bar.yMax + 28f;

        // Mesiac
        y = Line(x, y, w, "MESIAC", header, Color.white);

        DayNightCycle.MoonPhase[] moons =
        {
            DayNightCycle.MoonPhase.NewMoon, DayNightCycle.MoonPhase.Waxing,
            DayNightCycle.MoonPhase.FullMoon, DayNightCycle.MoonPhase.Waning
        };

        float bw = (w - 12f) / 4f;
        for (int i = 0; i < moons.Length; i++)
        {
            Rect b = new Rect(x + i * (bw + 4f), y, bw, 26f);
            bool cur = dayNight.CurrentMoon == moons[i];

            Fill(b, cur ? new Color(0.9f, 0.9f, 0.55f, 0.95f) : new Color(1f, 1f, 1f, 0.1f));
            GUI.contentColor = cur ? Color.black : Color.white;
            GUI.Label(b, MoonName(moons[i]), center);
            GUI.contentColor = Color.white;
        }
        y += 34f;

        y = Line(x, y, w, MoonDescription(dayNight.CurrentMoon), label, Color.white);
        y = Line(x, y, w,
            $"Démoni: poškodenie x{dayNight.DemonDamageMultiplier:0.00}, rýchlosť x{dayNight.DemonSpeedMultiplier:0.00}, " +
            $"dosah vnímania x{dayNight.DemonAggroMultiplier:0.00}", label, new Color(1f, 0.7f, 0.6f));
        y += 4f;
        Line(x, y, w, "Zvieratá a niektorí démoni: účinky mesiaca pripravujem. Ľudia: opačný alebo žiadny vplyv.",
            label, dim);
    }

    void Segment(Rect bar, float fromHour, float toHour, Color color)
    {
        if (toHour <= fromHour) return;
        float x0 = bar.x + bar.width * Mathf.Clamp01(fromHour / 24f);
        float x1 = bar.x + bar.width * Mathf.Clamp01(toHour / 24f);
        Fill(new Rect(x0, bar.y, x1 - x0, bar.height), color);
    }

    public static string PhaseName(DayNightCycle.DayPhase p)
    {
        switch (p)
        {
            case DayNightCycle.DayPhase.Dawn: return "Úsvit";
            case DayNightCycle.DayPhase.Day: return "Deň";
            case DayNightCycle.DayPhase.Dusk: return "Súmrak";
            default: return "Noc";
        }
    }

    static string MoonName(DayNightCycle.MoonPhase m)
    {
        switch (m)
        {
            case DayNightCycle.MoonPhase.NewMoon: return "Nov";
            case DayNightCycle.MoonPhase.Waxing: return "Dorastá";
            case DayNightCycle.MoonPhase.FullMoon: return "Úplnok";
            default: return "Ubúda";
        }
    }

    static string MoonDescription(DayNightCycle.MoonPhase m)
    {
        switch (m)
        {
            case DayNightCycle.MoonPhase.NewMoon:
                return "Nov: démoni sú slabší, no takmer neviditeľní v tieňoch.";
            case DayNightCycle.MoonPhase.Waxing:
                return "Dorastá: bez zvláštneho účinku, démoni sa blížia k úplnku.";
            case DayNightCycle.MoonPhase.FullMoon:
                return "Úplnok: démoni sú extrémne silní a agresívni, pach krvi je intenzívny.";
            default:
                return "Ubúda: bez zvláštneho účinku, sila démonov slabne.";
        }
    }

    string ClockShort()
    {
        if (dayNight == null) return null;

        int h = Mathf.FloorToInt(dayNight.TimeOfDay);
        int m = Mathf.FloorToInt((dayNight.TimeOfDay - h) * 60f);
        if (tClock.Stale(dayNight.Day, h * 60 + m, dayNight.IsNight ? 1 : 0))
            tClock.Text = $"Deň {dayNight.Day}  {h:00}:{m:00}  {(dayNight.IsNight ? "noc" : "deň")}";
        return tClock.Text;
    }

    // ----- Karta: Frakcia -----
    void DrawTabFaction(Rect c)
    {
        float x = c.x, y = c.y, w = c.width;
        FactionProfile p = faction != null ? FactionData.Get(faction.Current) : null;

        if (p == null)
        {
            Line(x, y, w, "Nepatríš do žiadnej frakcie.", label, dim);
            return;
        }

        y = Line(x, y, w, p.name, header, Color.white);

        string stateText;
        Color stateCol = Color.white;
        if (p.isDemonClan != stats.IsDemon)
        {
            stateText = "Frakcia nesedí s tvojou formou - bonusy neplatia.";
            stateCol = new Color(1f, 0.6f, 0.4f);
        }
        else if (faction.state == FactionState.Desperate)
        {
            stateText = "Práve platí NÚDZOVÝ STAV.";
            stateCol = new Color(1f, 0.5f, 0.3f);
        }
        else if (faction.state == FactionState.Stable)
        {
            stateText = "Práve platí STABILNÝ BONUS.";
            stateCol = new Color(0.6f, 1f, 0.7f);
        }
        else
        {
            stateText = p.isDemonClan ? "Bonus klanu: potrebuješ krv nad 25 %." : "Bonus frakcie: potrebuješ sýtosť nad 50 %.";
            stateCol = dim;
        }
        y = Line(x, y, w, stateText, bold, stateCol);
        y += 6f;

        y = Line(x, y, w, $"Stabilný bonus: {p.stableText}", label, Color.white);
        y = Line(x, y, w, $"Núdzový stav: {p.desperateText}", label, Color.white);
        y += 10f;

        if (ability != null && p.ability != AbilityKind.None)
        {
            string cost = p.abilityCost > 0f ? $"cena {p.abilityCost:0} {(p.abilityCostIsBlood ? "krvi" : "výdrže")}, " : "";
            y = Line(x, y, w, $"[{ability.abilityKey}] {p.abilityName}", header, Color.white);
            y = Line(x, y, w, p.abilityText, label, Color.white);
            y = Line(x, y, w, $"{cost}cooldown {p.abilityCooldown:0} s", label, dim);
            y += 8f;
        }

        if (ability != null && p.ability2 != AbilityKind.None)
        {
            y = Line(x, y, w, $"[{ability.abilityKey2}] {p.ability2Name}", header, Color.white);
            y = Line(x, y, w, p.ability2Text, label, Color.white);
            y = Line(x, y, w, $"cena {p.ability2Cost:0} výdrže, cooldown {p.ability2Cooldown:0} s", label, dim);
        }
    }

    // ----- Karta: Mapa -----
    void DrawTabMap(Rect c)
    {
        float side = Mathf.Min(c.height, c.width - 230f);
        Rect mr = new Rect(c.x, c.y, side, side);
        DrawMapBox(mr, true);

        float lx = mr.xMax + 18f, ly = c.y;
        float lw = c.xMax - lx;

        ly = Line(lx, ly, lw, "MAPA", header, Color.white);
        ly = LegendRow(lx, ly, lw, Color.white, "Ty");
        ly = LegendRow(lx, ly, lw, new Color(1f, 0.2f, 0.2f), "Démon, ktorého vidno");
        ly = LegendRow(lx, ly, lw, new Color(0.3f, 1f, 0.4f), "Lekárka");
        ly = LegendRow(lx, ly, lw, new Color(0.2f, 1f, 0.3f, 0.5f), "Vojenská zóna");
        ly = LegendRow(lx, ly, lw, new Color(0.8f, 0.35f, 1f), "Rozkaz Arakaniho");
        ly = LegendRow(lx, ly, lw, new Color(1f, 0.85f, 0.25f), "Cieľ úlohy");
        float decay = stats.LevelDecayLeft;
        ly = Line(lx, ly + 4f, lw, decay > 0f
            ? $"Odpočet levelu: o {(int)(decay / 60f)}:{(int)(decay % 60f):00} klesne {(stats.IsDemon ? "ľudský" : "démonický")} level o 1"
            : "Odpočet levelu: opačná forma je na minime", label, dim);
        ly += 10f;

        // Výber územia: mapa zobrazí len vybranú zónu (bez prázdna okolo), dá sa v nej posúvať a približovať
        if (miniMap != null && miniMap.Territories.Count > 0)
        {
            ly = Line(lx, ly, lw, "ÚZEMIA", header, Color.white);
            for (int i = 0; i < miniMap.Territories.Count; i++)
            {
                string name = (miniMap.SelectedTerritory == i ? "► " : "") + miniMap.Territories[i].name;
                if (GUI.Button(new Rect(lx, ly, lw, 26f), name)) SelectMapTerritory(i);
                ly += 30f;
            }
            if (GUI.Button(new Rect(lx, ly, lw, 26f), (miniMap.SelectedTerritory < 0 ? "► " : "") + "Okolie hráča")) SelectMapTerritory(-1);
            ly += 38f;
        }

        if (miniMap != null && miniMap.HasTerritory)
            foreach (string info in miniMap.PopulationLines)
                if (info.Length > 0) ly = Line(lx, ly, lw, info, label, Color.white);

        if (miniMap != null)
            ly = Line(lx, ly, lw, $"Záber: {miniMap.CurrentSize * 2f:0} m", label, dim);
        Line(lx, ly, lw, "Koliesko myši alebo + / - mení priblíženie. Ťahaj myšou alebo W A S D pre posun. Sever je hore.", label, dim);
    }

    // Pri otvorení mapy sa vyberie územie, v ktorom hráč stojí (inak okolie hráča)
    void InitMapTerritory()
    {
        miniMap.RefreshTerritories();
        SelectMapTerritory(stats != null ? miniMap.TerritoryAt(stats.transform.position) : -1);
    }

    void SelectMapTerritory(int index)
    {
        miniMap.SelectTerritory(index);
        mapZoom = Mathf.Clamp(index >= 0 ? miniMap.FitZoom(index) : 1f, 0.3f, 2.5f);   // celé územie naraz
    }

    float LegendRow(float x, float y, float w, Color color, string text)
    {
        Fill(new Rect(x, y + 4f, 10f, 10f), color);
        return Line(x + 18f, y, w - 18f, text, label, Color.white);
    }

    // ---------- Mapa / minimapa: rám, textúra, značky ----------
    void DrawMapBox(Rect r, bool big)
    {
        Fill(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), new Color(0f, 0f, 0f, 0.9f));

        if (miniMap != null && miniMap.Texture != null)
        {
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTextureWithTexCoords(r, miniMap.Texture, miniMap.TexCoords, false);
            GUI.color = prev;
        }
        else
        {
            Fill(r, new Color(0.05f, 0.06f, 0.08f));
        }

        // Ťahanie myšou posúva mapu územia
        if (big && miniMap != null && Event.current.type == EventType.MouseDrag && r.Contains(Event.current.mousePosition))
        {
            float k = miniMap.CurrentSize * 2f / Mathf.Max(1f, r.width);
            miniMap.Pan(new Vector2(-Event.current.delta.x * k, Event.current.delta.y * k));
            Event.current.Use();
        }

        GUI.BeginGroup(r);
        DrawMarkers(new Rect(0f, 0f, r.width, r.height), big);
        GUI.EndGroup();
    }

    void DrawMarkers(Rect r, bool big)
    {
        if (miniMap == null || stats == null) return;

        float dot = big ? 8f : 5f;

        // Vojenské zóny
        foreach (MilitaryZone z in zonesCache)
        {
            if (z == null) continue;
            BoxCollider b = z.GetComponent<BoxCollider>();
            if (b == null) continue;

            Bounds bb = b.bounds;
            Vector2 a = miniMap.WorldToMap(new Vector3(bb.min.x, 0f, bb.max.z), r);
            Vector2 d = miniMap.WorldToMap(new Vector3(bb.max.x, 0f, bb.min.z), r);
            Rect zr = Rect.MinMaxRect(a.x, a.y, d.x, d.y);

            Fill(zr, new Color(0.2f, 1f, 0.3f, 0.2f));
            Color border = new Color(0.3f, 1f, 0.4f, 0.9f);
            Fill(new Rect(zr.x, zr.y, zr.width, 1f), border);
            Fill(new Rect(zr.x, zr.yMax - 1f, zr.width, 1f), border);
            Fill(new Rect(zr.x, zr.y, 1f, zr.height), border);
            Fill(new Rect(zr.xMax - 1f, zr.y, 1f, zr.height), border);
        }

        // Lekárka
        foreach (Healer hl in healersCache)
            if (hl != null) Dot(miniMap.WorldToMap(hl.transform.position, r), dot, new Color(0.3f, 1f, 0.4f));

        // Démoni, ktorých je vidno
        foreach (DemonEnemy e in DemonEnemy.All)
        {
            if (e == null || e.Damage == null || e.Damage.IsDead || !e.IsVisibleThreat) continue;
            Dot(miniMap.WorldToMap(e.transform.position, r), dot, new Color(1f, 0.2f, 0.2f));
        }

        // Cieľ rozkazu Arakaniho (pri okraji sa pripne na hranu mapy)
        ArakaniFavor af = ArakaniFavor.Instance;
        if (af != null && af.VoiceHeard && af.OrderActive && af.CurrentTarget != null)
        {
            Vector2 p = miniMap.WorldToMap(af.CurrentTarget.transform.position, r);
            p.x = Mathf.Clamp(p.x, r.x + 6f, r.xMax - 6f);
            p.y = Mathf.Clamp(p.y, r.y + 6f, r.yMax - 6f);

            Matrix4x4 old = PushRotation(p, 45f);
            Fill(new Rect(-dot * 0.6f, -dot * 0.6f, dot * 1.2f, dot * 1.2f), new Color(0.8f, 0.35f, 1f));
            GUI.matrix = old;
        }

        // Cieľ aktuálnej úlohy: zlatý kosoštvorec; mimo výrezu mapy šípka na okraji smerom k cieľu
        if (QuestMarker.Active)
        {
            Vector2 q = miniMap.WorldToMap(QuestMarker.Position, r);
            Color gold = new Color(1f, 0.85f, 0.25f);
            if (r.Contains(q))
            {
                Matrix4x4 qs = PushRotation(q, 45f);
                float s = dot * 1.7f;
                Fill(new Rect(-s * 0.6f, -s * 0.6f, s * 1.2f, s * 1.2f), Color.black);
                Fill(new Rect(-s * 0.45f, -s * 0.45f, s * 0.9f, s * 0.9f), gold);
                GUI.matrix = qs;
                if (big) GUI.Label(new Rect(q.x + 10f, q.y - 9f, 220f, 20f), QuestMarker.Label, label);
            }
            else
            {
                Vector2 mid = r.center;
                Vector2 edge = new Vector2(Mathf.Clamp(q.x, r.x + 10f, r.xMax - 10f), Mathf.Clamp(q.y, r.y + 10f, r.yMax - 10f));
                Vector2 dir = q - mid;
                DrawArrowShape(edge, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, dot * 2.2f, gold, Color.black);
            }
        }

        // Hráč: bod so šípkou smeru pohľadu (sever je hore, preto x = východ, y = -juh)
        Vector2 me = miniMap.WorldToMap(stats.transform.position, r);
        Color meColor = stats.IsDemon ? new Color(1f, 0.5f, 0.6f) : new Color(0.6f, 0.85f, 1f);
        Dot(me, dot, meColor);
        Vector3 facing = stats.transform.forward;
        if (facing.x * facing.x + facing.z * facing.z > 0.0001f)
            DrawArrowShape(me + new Vector2(facing.x, -facing.z).normalized * dot * 1.9f, Mathf.Atan2(-facing.z, facing.x) * Mathf.Rad2Deg, dot * 1.7f, meColor, Color.black);
    }

    // Otočenie v GUI: matica sa skladá ručne (RotateAroundPivot pri škálovanom HUD-e otáča okolo zlého bodu). Kreslí sa potom v lokálnych súradniciach so stredom v pivote.
    static Matrix4x4 PushRotation(Vector2 pivot, float degrees)
    {
        Matrix4x4 saved = GUI.matrix;
        GUI.matrix = saved * Matrix4x4.TRS(new Vector3(pivot.x, pivot.y, 0f), Quaternion.Euler(0f, 0f, degrees), Vector3.one);
        return saved;
    }

    // Šípka so špičkou v smere uhla (GUI súradnice, y dole, uhol rastie v smere hodinových ručičiek od osi +X)
    static void DrawArrowShape(Vector2 pos, float angle, float size, Color color, Color outline)
    {
        Matrix4x4 saved = PushRotation(pos, angle);
        Fill(new Rect(-size * 0.55f, -size * 0.2f, size * 1.0f, size * 0.4f), outline);
        Matrix4x4 head = GUI.matrix;
        GUI.matrix = head * Matrix4x4.TRS(new Vector3(size * 0.45f, 0f, 0f), Quaternion.Euler(0f, 0f, 45f), Vector3.one);
        Fill(new Rect(-size * 0.42f, -size * 0.42f, size * 0.84f, size * 0.84f), outline);
        GUI.matrix = head;
        Fill(new Rect(-size * 0.5f, -size * 0.14f, size * 0.9f, size * 0.28f), color);
        GUI.matrix = head * Matrix4x4.TRS(new Vector3(size * 0.45f, 0f, 0f), Quaternion.Euler(0f, 0f, 45f), Vector3.one);
        Fill(new Rect(-size * 0.3f, -size * 0.3f, size * 0.6f, size * 0.6f), color);
        GUI.matrix = saved;
    }

    // Šípka na obrazovke ukazuje na cieľ úlohy podľa smeru kamery a dopĺňa vzdialenosť
    void DrawQuestArrow(float vw, float vh)
    {
        if (!QuestMarker.Active || stats == null || stats.IsDead) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 to = QuestMarker.Position - stats.transform.position;
        to.y = 0f;
        float distance = to.magnitude;
        if (distance < 8f) return;   // pri cieli šípka netreba

        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return;

        // Uhol cieľa voči pohľadu kamery: 0 = rovno, kladný = vpravo
        float relative = Vector3.SignedAngle(forward, to, Vector3.up);
        Vector2 origin = new Vector2(vw * 0.5f, 74f);

        Fill(new Rect(origin.x - 150f, origin.y - 30f, 300f, 78f), new Color(0f, 0f, 0f, 0.38f));
        DrawArrowShape(origin, relative - 90f, 30f, new Color(1f, 0.85f, 0.25f), new Color(0f, 0f, 0f, 0.85f));
        GUI.Label(new Rect(origin.x - 150f, origin.y + 22f, 300f, 22f), $"{QuestMarker.Label}  –  {distance:0} m", center);
    }

    static void Dot(Vector2 p, float size, Color c)
        => Fill(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), c);

    static void Fill(Rect r, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    static string K(string id) => KeyBindings.Name(KeyBindings.Get(id));

    // Ovládanie podľa kláves, ktoré si hráč nastavil (Nastavenia → Ovládanie)
    public static string HelpText()
    {
        return $"WASD pohyb, {K("sprint")} šprint, {K("jump")} skok, {K("dash")} úhyb\n" +
               "Ľavé tlačidlo myši: útok (držanie = silný), pravé: blok\n" +
               $"{K("crouch")} / {K("crouch2")}: čupnúť (zakrádať sa), ĽTM zozadu: tiché zabitie\n" +
               $"{K("use")}: použiť / zbierať / zobrať a položiť telo, {K("weapon")}: zbraň, {K("cover")}: krytie pri stene\n" +
               $"{K("wardrobe")}: šatník, {K("appearance")}: vzhľad, {K("inventory")}: inventár, {K("potion")}: rýchly lektvar\n" +
               $"{K("trade")}: obchod a rozhovor, {K("plot")}: parcela, {K("help")}: pomôcť civilistovi\n" +
               "Tab: menu (postava, čas, frakcia, mapa, skilly), M: mapa\n" +
               $"{K("perception")}: zvýraznenie (démon: ľudia a zvieratá, človek: preskúmaní démoni)\n" +
               $"{K("senses")}: zmysly, 1-4: zameranie zmyslov\n" +
               "5 (démon): cestovanie koreňmi, stromy temnoty\n" +
               $"{K("ability1")} / {K("ability2")}: schopnosti frakcie, {K("veil")}: Závoj Arakaniho, {K("leap")}: dravý skok (démon)\n" +
               $"{K("examine")} (drž): skúmať človeka, {K("examine")}: rozhodnúť o odhalenom\n" +
               $"{K("feed")} (drž): piť krv  |  {K("use")}: lekárka\n" +
               $"{K("popHud")}: panel populácie, {K("lair")}: skrýša, {K("save")}: rýchle uloženie";
    }

    // ---------- Stred dole: výzvy a správy ----------
    void DrawBottom(float vw, float vh)
    {
        if (HudUi.BottomActive) return;   // výzvy a správy kreslí nový HUD
        var lines = new List<string>();
        var seen = new HashSet<string>();

        foreach (MessageEntry m in messages)
            if (seen.Add(m.text)) lines.Add(m.text);
        foreach (PromptEntry p in prompts)
            if (p.frame >= Time.frameCount - 1 && seen.Add(p.text)) lines.Add(p.text);

        float y = vh - 110f;   // nad lištou predmetov dole na obrazovke
        Color prev = GUI.color;

        for (int i = lines.Count - 1; i >= 0; i--)
        {
            Vector2 size = center.CalcSize(new GUIContent(lines[i]));
            Rect r = new Rect(vw / 2f - size.x / 2f - 10f, y - size.y - 4f, size.x + 20f, size.y + 4f);

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, lines[i], center);

            y -= r.height + 4f;
        }
        GUI.color = prev;
    }

    // ---------- Texty vo svete ----------
    void DrawWorldTexts(float scale)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        for (int i = 0; i < worldTexts.Count; i++)
        {
            WorldTextEntry w = worldTexts[i];
            if (!NpcOverlay.Visible(w.pos)) continue;   // texty nad postavami len do 15 m a mimo menu
            float age = Time.time - w.time;
            float u = Mathf.Clamp01(age / WorldTextSeconds);

            // Vyskočí rýchlo a spomaľuje, s miernym úletom do strany (čísla sa neprekrývajú)
            float rise = 1f - (1f - u) * (1f - u);
            Vector3 sp = cam.WorldToScreenPoint(w.pos + Vector3.up * (rise * 1.1f));
            if (sp.z <= 0f) continue;

            bool big = w.text.Length > 0 && (w.text.IndexOf('!') >= 0 || w.text.IndexOf("KRIT", System.StringComparison.Ordinal) >= 0);
            // Náraz: text sa objaví zväčšený a hneď sa stiahne na svoju veľkosť
            float pop = age < 0.14f ? Mathf.Lerp(1.9f, 1f, age / 0.14f) : 1f;
            float baseSize = big ? 26f : 19f;
            float shakeX = big && age < 0.25f ? Mathf.Sin(age * 90f) * 3f * (1f - age / 0.25f) : 0f;
            float alpha = u < 0.6f ? 1f : 1f - (u - 0.6f) / 0.4f;

            float x = sp.x / scale + w.drift * 26f * rise + shakeX;
            float y = (Screen.height - sp.y) / scale;

            worldStyle.fontSize = Mathf.RoundToInt(baseSize * pop);
            Rect rect = new Rect(x - 160f, y - 22f, 320f, 44f);

            // Tmavý obrys, aby bolo číslo čitateľné na svetlom aj tmavom pozadí
            worldStyle.normal.textColor = new Color(0.03f, 0.02f, 0.02f, alpha * 0.9f);
            const float o = 1.6f;
            GUI.Label(new Rect(rect.x - o, rect.y, rect.width, rect.height), w.text, worldStyle);
            GUI.Label(new Rect(rect.x + o, rect.y, rect.width, rect.height), w.text, worldStyle);
            GUI.Label(new Rect(rect.x, rect.y - o, rect.width, rect.height), w.text, worldStyle);
            GUI.Label(new Rect(rect.x, rect.y + o + 1f, rect.width, rect.height), w.text, worldStyle);

            worldStyle.normal.textColor = new Color(w.color.r, w.color.g, w.color.b, alpha);
            GUI.Label(rect, w.text, worldStyle);
        }
    }

    const float WorldTextSeconds = 1.1f;
    static Font damageFont;

    // ---------- Pomocné kreslenie ----------
    float Line(float x, float y, float w, string text, GUIStyle style, Color color)
    {
        var content = new GUIContent(text);
        float h = style.CalcHeight(content, w);

        Color prev = GUI.contentColor;
        GUI.contentColor = color;
        GUI.Label(new Rect(x, y, w, h), content, style);
        GUI.contentColor = prev;

        return y + h + 2f;
    }

    /// <summary>Pásik. Text je voliteľný (null = bez textu, šetrí miesto). tick = značka na danom podiele.</summary>
    float Bar(float x, float y, float w, float h, float fill, Color color, string text, float gap, float tick = -1f)
    {
        Rect r = new Rect(x, y, w, h);

        Fill(r, new Color(0f, 0f, 0f, 0.65f));
        Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fill), r.height), color);

        if (tick > 0f)
            Fill(new Rect(r.x + r.width * tick - 0.5f, r.y, 1f, r.height), new Color(1f, 1f, 1f, 0.6f));

        if (!string.IsNullOrEmpty(text))
        {
            Color prev = GUI.color;
            GUI.color = Color.white;
            GUI.Label(r, text, barText);
            GUI.color = prev;
        }

        return y + h + gap;
    }
}