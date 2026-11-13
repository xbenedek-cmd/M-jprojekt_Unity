using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hlavné menu (pri štarte hry) a pauza (Esc): pokračovanie, nová hra a načítanie zo 3 slotov, uloženie do slotu, nastavenia
/// (hlasitosť hlavná / hudba / efekty, zobrazenie hlavného menu pri štarte), návrat do hlavného menu a koniec.
/// Kým je menu otvorené, hra stojí (Time.timeScale = 0). Načítanie uloženého sveta čaká na výber v hlavnom menu (WorldSave).
/// </summary>
public class PauseMenu : MonoBehaviour
{
    private enum State { Closed, Main, Pause, LoadSlots, NewGameSlots, SaveSlots, Settings, Controls }

    public static PauseMenu Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.state != State.Closed;
    /// <summary>Hlavné menu pri štarte ešte nič nevybralo (svet sa zatiaľ nenačíta).</summary>
    public static bool AtStartup { get; private set; }

    private State state = State.Closed;
    private State previous = State.Closed;
    private bool menuAtStartup;
    private bool anyMenuLastFrame;
    private string message;
    private float messageUntil;
    private GUIStyle title, label, button;
    private string waitingId;          // akcia, ktorej sa práve priraďuje nová klávesa
    private Vector2 controlsScroll;
    private GUIStyle rowLabel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<PauseMenu>() != null) return;
        new GameObject("PauseMenu").AddComponent<PauseMenu>();
    }

    void Awake()
    {
        Instance = this;

        // Po "Nová hra" sa scéna znova načíta bez hlavného menu; inak sa menu ukáže pri štarte, ak je zapnuté
        bool skipOnce = PlayerPrefs.GetInt("skip_menu_once", 0) == 1;
        PlayerPrefs.DeleteKey("skip_menu_once");
        // "Hlavné menu" z pauzy ho vynúti, aj keď je pri štarte vypnuté
        bool forced = PlayerPrefs.GetInt("force_menu_once", 0) == 1;
        PlayerPrefs.DeleteKey("force_menu_once");
        bool menuWanted = forced || PlayerPrefs.GetInt("menu_on_start", 1) == 1;
        if (forced) skipOnce = false;

        AtStartup = false;
        if (!skipOnce && menuWanted)
        {
            menuAtStartup = true;
            AtStartup = true;
            Open(State.Main);
            MenuSanctuary.Show();   // pozadie: démon v temnej svätyni
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (state != State.Closed) Time.timeScale = 1f;
        MenuSanctuary.Hide();
        AtStartup = false;
    }

    void Open(State next)
    {
        previous = state;
        state = next;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void CloseAll()
    {
        state = State.Closed;
        AtStartup = false;
        MenuSanctuary.Hide();
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // Kurzor musí byť pri menu voľný: CameraFollow ho pri štarte (Start) zamyká až po tomto Awake, takže by sa nedalo klikať
        if (state != State.Closed && (Cursor.lockState != CursorLockMode.None || !Cursor.visible))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        bool anyOpen = Healer.MenuOpen;   // zahŕňa aj túto pauzu
        bool otherMenu = anyMenuLastFrame && state == State.Closed;
        anyMenuLastFrame = anyOpen;

        if (waitingId != null)
        {
            if (GameInput.GetKeyDown(KeyCode.Escape)) waitingId = null;   // zrušenie priraďovania
            return;
        }

        if (!GameInput.GetKeyDown(KeyCode.Escape)) return;

        if (state == State.Closed)
        {
            if (!otherMenu && !Healer.MenuOpen && !JailManager.IsInJail) Open(State.Pause);
        }
        else if (state == State.Pause) CloseAll();
        else if (state == State.Controls) state = State.Settings;
        else if (state != State.Main) state = AtStartup ? State.Main : State.Pause;   // Esc v podmenu: späť do hlavného menu (so svätyňou) pri štarte, inak do pauzy
    }

    void Say(string text)
    {
        message = text;
        messageUntil = Time.unscaledTime + 3f;
    }

    // ---------- Zobrazenie ----------
    void OnGUI()
    {
        if (state == State.Closed) return;

        // Priraďovanie klávesy: prvá stlačená klávesa (okrem Esc) sa uloží
        Event ev = Event.current;
        if (waitingId != null && ev.type == EventType.KeyDown && ev.keyCode != KeyCode.None && ev.keyCode != KeyCode.Escape)
        {
            KeyBindings.Set(waitingId, ev.keyCode);
            waitingId = null;
            ev.Use();
        }

        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 17 };
        }

        // Hlavné menu so svätyňou: démon je vidieť vpravo, menu je vľavo na tmavom páse; inak celoobrazovkové stmavenie
        bool sanctuary = state == State.Main && MenuSanctuary.Instance != null;
        float w = 460f, h = 540f;
        Rect area;
        if (sanctuary)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0f, 0f, w + 110f, Screen.height), Texture2D.whiteTexture);
            area = new Rect(50f, (Screen.height - h) * 0.5f, w, h);
        }
        else
        {
            GUI.color = new Color(0f, 0f, 0f, state == State.Main ? 0.92f : 0.75f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }
        GUI.color = Color.white;
        GUILayout.BeginArea(area);


        switch (state)
        {
            case State.Main: DrawMain(); break;
            case State.Pause: DrawPause(); break;
            case State.LoadSlots: DrawSlots("Načítať hru", slot => LoadSlot(slot), true); break;
            case State.NewGameSlots: DrawSlots("Nová hra: vyber slot (prepíše sa)", slot => NewGame(slot), false); break;
            case State.SaveSlots: DrawSlots("Uložiť hru", slot => { WorldSave.SaveToSlot(slot); Say($"Uložené do slotu {slot}."); }, false); break;
            case State.Settings: DrawSettings(); break;
            case State.Controls: DrawControls(); break;
        }

        if (message != null && Time.unscaledTime < messageUntil) GUILayout.Label(message, label);
        GUILayout.EndArea();
    }

    bool Button(string text, bool enabled = true)
    {
        GUI.enabled = enabled;
        bool pressed = GUILayout.Button(text, button, GUILayout.Height(40f));
        GUI.enabled = true;
        GUILayout.Space(6f);
        return pressed;
    }

    void DrawMain()
    {
        GUILayout.Space(20f);
        GUILayout.Label("Progeny of the Ashes", title);
        GUILayout.Space(24f);

        bool hasSave = WorldSave.SlotSummary(WorldSave.Slot) != null;
        if (Button(hasSave ? $"Pokračovať (slot {WorldSave.Slot}: {WorldSave.SlotSummary(WorldSave.Slot)})" : "Pokračovať (žiadne uložené)", hasSave)) CloseAll();
        if (Button("Nová hra")) state = State.NewGameSlots;
        if (Button("Načítať hru")) state = State.LoadSlots;
        if (Button("Nastavenia")) { previous = State.Main; state = State.Settings; }
        if (Button("Koniec")) Quit();
    }

    void DrawPause()
    {
        GUILayout.Space(20f);
        GUILayout.Label("Pauza", title);
        GUILayout.Space(24f);

        if (Button("Pokračovať")) CloseAll();
        if (Button("Uložiť hru")) state = State.SaveSlots;
        if (Button("Nastavenia")) { previous = State.Pause; state = State.Settings; }
        if (Button("Hlavné menu")) ReloadScene(false);
        if (Button("Koniec")) Quit();
    }

    void DrawSlots(string heading, System.Action<int> choose, bool onlyExisting)
    {
        GUILayout.Space(20f);
        GUILayout.Label(heading, title);
        GUILayout.Space(20f);

        for (int slot = 1; slot <= WorldSave.SlotCount; slot++)
        {
            string summary = WorldSave.SlotSummary(slot);
            string text = $"Slot {slot}: {(summary ?? "prázdny")}";
            if (Button(text, !onlyExisting || summary != null)) choose(slot);
        }

        if (Button("Späť")) state = menuAtStartup && AtStartup ? State.Main : State.Pause;
    }

    void DrawSettings()
    {
        GUILayout.Space(20f);
        GUILayout.Label("Nastavenia", title);
        GUILayout.Space(20f);

        GUILayout.Label($"Hlavná hlasitosť: {GameAudio.MasterVolume * 100f:0} %", label);
        GameAudio.MasterVolume = GUILayout.HorizontalSlider(GameAudio.MasterVolume, 0f, 1f);
        GUILayout.Label($"Hudba: {GameAudio.MusicVolume * 100f:0} %", label);
        GameAudio.MusicVolume = GUILayout.HorizontalSlider(GameAudio.MusicVolume, 0f, 1f);
        GUILayout.Label($"Efekty: {GameAudio.SfxVolume * 100f:0} %", label);
        GameAudio.SfxVolume = GUILayout.HorizontalSlider(GameAudio.SfxVolume, 0f, 1f);
        GUILayout.Space(12f);

        bool menuOnStart = PlayerPrefs.GetInt("menu_on_start", 1) == 1;
        bool toggled = GUILayout.Toggle(menuOnStart, " Zobraziť hlavné menu pri štarte hry");
        if (toggled != menuOnStart) PlayerPrefs.SetInt("menu_on_start", toggled ? 1 : 0);
        GUILayout.Space(8f);
        GUILayout.Label("Obtiažnosť (nočné útoky démonov na mesto):", label);
        int diff = GUILayout.Toolbar((int)Difficulty.Level, Difficulty.Names, GUILayout.Height(30f));
        if (diff != (int)Difficulty.Level) Difficulty.Level = (GameDifficulty)diff;
        GUILayout.Space(8f);
        if (Button("Ovládanie (klávesy)")) state = State.Controls;
        GUILayout.Space(16f);

        if (Button("Späť")) state = previous == State.Main && AtStartup ? State.Main : State.Pause;
    }

    void DrawControls()
    {
        if (rowLabel == null) rowLabel = new GUIStyle(label) { alignment = TextAnchor.MiddleLeft, fontSize = 15 };

        GUILayout.Space(10f);
        GUILayout.Label("Ovládanie", title);
        GUILayout.Label("Klikni na kláves a stlač novú (Esc zruší). Pohyb (WASD), myš a menu (Tab, 0, 5–9) sú pevné.", label);
        GUILayout.Space(6f);

        controlsScroll = GUILayout.BeginScrollView(controlsScroll, GUILayout.Height(330f));
        foreach (KeyBindings.Entry e in KeyBindings.All)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(e.label, rowLabel, GUILayout.Width(250f));
            bool waiting = waitingId == e.id;
            string text = waiting ? "Stlač kláves…" : KeyBindings.Name(KeyBindings.Get(e.id, e.def));
            if (GUILayout.Button(text, button, GUILayout.Height(28f), GUILayout.Width(150f))) waitingId = waiting ? null : e.id;
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }
        GUILayout.EndScrollView();

        GUILayout.Space(8f);
        if (Button("Obnoviť predvolené")) { KeyBindings.ResetAll(); waitingId = null; }
        if (Button("Späť")) { waitingId = null; state = State.Settings; }
    }

    // ---------- Akcie ----------
    void LoadSlot(int slot)
    {
        WorldSave.Slot = slot;
        if (AtStartup) CloseAll();       // načíta sa hneď po zatvorení menu (WorldSave čakal na výber)
        else ReloadScene(true);          // z pauzy: nová scéna s načítaním vybraného slotu
    }

    void NewGame(int slot)
    {
        WorldSave.Slot = slot;
        string path = WorldSave.PathFor(slot);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

        ResetStatics();
        ReloadScene(true);
    }

    // Statické stavy (dôvera, povolania, tlak, vylepšenia, inventár) sa pri novej hre vynulujú
    static void ResetStatics()
    {
        Reputation.ResetState();
        ProfessionTrust.ResetState();
        PopulationPressure.ResetState();
        DemonNature.ResetState();
        WeaponUpgrades.ResetState();
        DemonRaid.ResetState();
        ToolKit.ResetState();

        // Príbeh a ekonomika: statické stavy prežijú načítanie scény, bez resetu by hra "pamätala" dokončený prológ a nespustila by ho
        Story.ResetState();
        Prologue.Restart();
        SoulBalance.Import(null);
        SettlementGatherers.Import(null);
        CityManagement.Import(null);
        ConvoySystem.Import(null);
        Lair.Import(null);
        Power.Import(0f);
        SpecialGoods.Import(0);
        Buyer.Import(null);
        StoryCast.Import(null);   // zároveň vynuluje hlavný quest
        FarmSystem.Import(null);

        CivilianAI.LastPlayerCrimeTime = -999f;
        PlayerPrefs.DeleteKey("POA.Inventory");

        // Strom schopností a levely schopností sa pri novej hre vynulujú (boli v PlayerPrefs, takže prežili novú hru)
        SkillTree.Clear();
        Equipment.Clear();
        foreach (string key in new[] { "POA.SkillLevel", "POA.SkillXp", "POA.SkillPoints", "POA.SoulPoints", "POA.SkillNodes", "POA.SkillDamage", "POA.SkillInvestigation", "POA.SkillDoubleJump", "POA.SkillReach" })
            PlayerPrefs.DeleteKey(key);
    }

    void ReloadScene(bool skipMenu)
    {
        PlayerPrefs.SetInt("skip_menu_once", skipMenu ? 1 : 0);
        PlayerPrefs.SetInt("force_menu_once", skipMenu ? 0 : 1);
        PlayerPrefs.Save();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    /// <summary>Návrat do hlavného menu (napr. po smrti démona): hra pokračuje z posledného uloženia, ktoré hráč vyberie.</summary>
    public static void ReturnToMainMenu()
    {
        PlayerPrefs.SetInt("skip_menu_once", 0);
        PlayerPrefs.SetInt("force_menu_once", 1);
        PlayerPrefs.Save();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
