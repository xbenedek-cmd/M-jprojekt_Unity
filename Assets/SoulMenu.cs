using UnityEngine;

/// <summary>
/// Na hráčovi: denný vývoj ľudskosti (rozpoltená duša), denný príjem osady, lekár v stredisku a dve menu:
/// 0 = Rozpoltená duša (investícia skill pointov do ľudskej alebo démonickej vetvy),
/// 9 = Osada (príjem budov a priraďovanie špecialistov do budov).
/// </summary>
public class SoulMenu : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }

    [SerializeField] private KeyCode soulKey = KeyCode.Alpha0;
    [SerializeField] private KeyCode settlementKey = KeyCode.Alpha9;
    [SerializeField] private float demonDrainPerSecond = 1f / 300f;
    [SerializeField] private float humanRecoverPerSecond = 1f / 90f;

    private enum Mode { None, Soul, Settlement }

    private PlayerStats player;
    private Mode mode;
    private string selectedPlot;
    private string status;
    private float nextTick, nextScare;
    private bool rejectedNotified;
    private Vector2 scroll;

    void OnEnable() => DayNightCycle.OnNewDay += PayDailyIncome;

    // Smrť (človek aj démon): po chvíli späť do hlavného menu, hra pokračuje od posledného uloženia
    void HandleDied()
    {
        if (player != null) Invoke(nameof(ReturnToMenu), 3f);
    }

    void ReturnToMenu() => PauseMenu.ReturnToMainMenu();
    void OnDisable()
    {
        DayNightCycle.OnNewDay -= PayDailyIncome;
        if (player != null) player.OnDied -= HandleDied;
        Close();
    }

    void PayDailyIncome(int day)
    {
        if (player == null || player.IsDead) return;
        int income = SettlementEconomy.DailyIncome();
        if (income <= 0) return;

        player.AddMoney(income);
        GameHUD.Message($"Osada priniesla dnes {income} peňazí.", 4f);
    }

    void Update()
    {
        if (player == null)
        {
            player = GetComponent<PlayerStats>();
            if (player != null) player.OnDied += HandleDied;
        }
        if (player == null || player.IsDead) return;

        if (PauseMenu.AtStartup || PauseMenu.IsOpen) { if (MenuOpen) Close(); return; }

        // Ľudskosť: démon ju stráca, človek ju pomaly získava späť
        if (!MenuOpen)
            SoulBalance.Add((player.IsDemon ? -demonDrainPerSecond : humanRecoverPerSecond) * Time.deltaTime);

        if (Time.time >= nextTick)
        {
            nextTick = Time.time + 1f;
            if (SettlementEconomy.ClinicHealing(player.transform.position)) player.Heal(2.5f);
        }

        // Odmietnutý hráč: civilisti sa ho boja a utekajú
        if (SoulBalance.Rejected && !Lair.Perfumed)
        {
            if (!rejectedNotified)
            {
                rejectedNotified = true;
                GameHUD.Message("Ľudia v tebe už nevidia človeka. Obchodníci ti nepredajú nič, civilisti sa ťa boja.", 5f);
            }
            if (Time.time >= nextScare)
            {
                nextScare = Time.time + 3f;
                CivilianAI.Panic(transform.position, 12f);
            }
        }
        else if (SoulBalance.Humanity > SoulBalance.RejectBelow + 5f) rejectedNotified = false;

        HandleKeys();
    }

    void HandleKeys()
    {
        bool blocked = MangaDialog.Active || Healer.MenuOpen || GameHUD.MenuOpen || PlotManager.MenuOpen || DemonNatureMenu.MenuOpen || WorkshopMenu.MenuOpen
                       || MerchantNPC.MenuOpen || WanderingMerchant.MenuOpen || CombatQte.Active || Inventory.MenuOpen;

        if (MenuOpen)
        {
            if (GameInput.GetKeyDown(KeyCode.Escape) || GameInput.GetKeyDown(mode == Mode.Soul ? soulKey : settlementKey)) Close();
            return;
        }
        if (blocked) return;

        if (GameInput.GetKeyDown(soulKey)) Open(Mode.Soul);
        else if (GameInput.GetKeyDown(settlementKey)) Open(Mode.Settlement);
    }

    void Open(Mode newMode)
    {
        mode = newMode;
        MenuOpen = true;
        status = null;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        BuildPlot near = BuildPlot.NearestBuilt(transform.position, 40f);
        selectedPlot = near != null ? near.PlotId : null;
    }

    void Close()
    {
        if (!MenuOpen) return;
        MenuOpen = false;
        mode = Mode.None;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ---------- Kreslenie ----------
    void OnGUI()
    {
        if (PauseMenu.AtStartup || !MenuOpen || player == null) return;

        float boxHeight = mode == Mode.Settlement ? Mathf.Min(720f, Screen.height - 20f) : 600f;
        Rect box = new Rect((Screen.width - 760f) * 0.5f, (Screen.height - boxHeight) * 0.5f, 760f, boxHeight);
        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.96f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(box.x + 18f, box.y + 14f, box.width - 36f, box.height - 28f));
        if (mode == Mode.Soul) DrawSoul(); else DrawSettlement();
        if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
        GUILayout.EndArea();
    }

    void DrawSoul()
    {
        GUILayout.Label("<size=22><b>ROZPOLTENÁ DUŠA</b></size>", Rich());
        GUILayout.Label($"Ľudskosť: {SoulBalance.Humanity:0} / 100" + (SoulBalance.Rejected ? "   <color=#ff6a5a>(odmietnutý: obchodníci nepredávajú, civilisti utekajú)</color>" : ""), Rich());
        Rect bar = GUILayoutUtility.GetRect(10f, 14f);
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = SoulBalance.Rejected ? new Color(0.9f, 0.3f, 0.25f) : new Color(0.55f, 0.8f, 1f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * SoulBalance.Humanity / 100f, bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.Label("Ako démon ľudskosť klesá, ničenie budov ju zráža o 2. V ľudskej podobe sa pomaly vracia.");
        GUILayout.Label($"Body duše: <b>{player.soulPoints}</b> (za každý nový level človeka alebo démona; strom schopností má vlastné skill pointy)   Smer: <b>{SoulBalance.Ending}</b> (ľudská vetva {SoulBalance.Points(true)}, démonická {SoulBalance.Points(false)})", Rich());
        GUILayout.Space(8f);

        GUILayout.Label("Stupeň 1 stojí 1 bod, stupeň 2 stojí 2, stupeň 3 stojí 3. Vyšší stupeň sa odomkne po dvoch schopnostiach nižšieho stupňa v tej istej vetve.");

        treeScroll = GUILayout.BeginScrollView(treeScroll, GUILayout.Height(330f));
        GUILayout.BeginHorizontal();
        DrawBranch("ĽUDSKÁ VETVA: preživší, remeselník, ochranca", 0);
        GUILayout.Space(14f);
        DrawBranch("DÉMONICKÁ VETVA: skaza, sila, blesky", 9);
        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();

        GUILayout.FlexibleSpace();
        GUILayout.Label($"[{soulKey}] / Esc zavrie");
    }

    private Vector2 treeScroll;

    void DrawBranch(string title, int first)
    {
        GUILayout.BeginVertical(GUILayout.Width(335f));
        GUILayout.Label($"<b>{title}</b>", Rich());
        for (int i = first; i < first + 9; i++)
        {
            if (i % 3 == 0) GUILayout.Label($"<color=#9ad>— Stupeň {(i - first) / 3 + 1} —</color>", Rich());
            PerkRow((SoulPerk)i);
        }
        GUILayout.EndVertical();
    }

    void PerkRow(SoulPerk perk)
    {
        string locked = SoulBalance.LockedReason(perk);
        bool owned = SoulBalance.Has(perk);
        int cost = SoulBalance.Cost(perk);

        string button = owned ? $"✔ {SoulBalance.Name(perk)}" : locked != null ? $"{SoulBalance.Name(perk)}  ({locked})" : $"{SoulBalance.Name(perk)}  — kúpiť ({cost})";
        GUI.enabled = !owned && locked == null && player.soulPoints >= cost;
        if (GUILayout.Button(button, GUILayout.Height(28f)))
            status = SoulBalance.Buy(perk, player) ? $"Odomknuté: {SoulBalance.Name(perk)}." : "Nemáš dosť bodov duše.";
        GUI.enabled = true;
        GUILayout.Label(SoulBalance.Description(perk), Rich());
    }

    private int settlementTab;
    private DayNightCycle clock;

    void DrawSettlement()
    {
        settlementTab = GUILayout.Toolbar(settlementTab, new[] { "Osada", "Správa mesta", "Konvoje", "Elektrina" }, GUILayout.Height(28f));
        GUILayout.Space(4f);

        if (settlementTab == 0) { DrawSettlementTab(); return; }

        BuildPlot selected = FindPlot(selectedPlot);
        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
        if (settlementTab == 1) CityManagement.DrawMenu(Rich(), selected, ref status);
        else if (settlementTab == 2) ConvoySystem.DrawMenu(Rich(), player, clock != null ? clock.Day : 1, ref status);
        else Power.DrawMenu(Rich(), player, ref status);

        GUILayout.FlexibleSpace();
        GUILayout.Label($"[{settlementKey}] / Esc zavrie");
    }

    void DrawSettlementTab()
    {
        GUILayout.Label("<size=22><b>OSADA</b></size>", Rich());
        GUILayout.Label($"Denný príjem budov: <b>{SettlementEconomy.DailyIncome()}</b> peňazí (rastie s počtom civilistov a špecialistami).", Rich());
        GUILayout.Label("Priraď špecialistov (dôvera aspoň " + SettlementEconomy.MinTrust + ", pomáhaj civilistom klávesom L): správne povolanie ×1,6, iné ×1,2. Lekár v stredisku lieči, inžinier v dielni zlacňuje výrobu.");
        GUILayout.Space(6f);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(200f));
        int count = 0;
        foreach (BuildPlot plot in BuildPlot.All)
        {
            if (plot.State != PlotState.Built || plot.Building == null) continue;
            count++;

            Profession worker = SettlementEconomy.Assigned(plot.PlotId);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{(plot.PlotId == selectedPlot ? "► " : "")}{plot.Building.displayName} ({plot.ZoneName}): +{SettlementEconomy.Profit(plot):0}/deň"
                            + (worker != Profession.None ? $"  [{ProfessionTrust.Name(worker)}]" : ""), GUILayout.Width(380f));
            if (GUILayout.Button("vybrať")) selectedPlot = plot.PlotId;
            GUILayout.EndHorizontal();
        }
        if (count == 0) GUILayout.Label("Zatiaľ nemáš postavenú žiadnu budovu.");
        GUILayout.EndScrollView();

        BuildPlot selected = FindPlot(selectedPlot);
        if (selected != null)
        {
            GUILayout.Label($"<b>{selected.Building.displayName}</b>: priradiť špecialistu", Rich());
            GUILayout.BeginHorizontal();
            foreach (Profession p in new[] { Profession.Engineer, Profession.Cook, Profession.Doctor, Profession.Electrician })
            {
                string mark = SettlementEconomy.Matches(selected.Building, p) ? "★ " : "";
                if (GUILayout.Button($"{mark}{ProfessionTrust.Name(p)} ({ProfessionTrust.Get(p):0})", GUILayout.Height(32f)))
                {
                    SettlementEconomy.Assign(selected, p, out string message);
                    status = message;
                }
            }
            if (GUILayout.Button("Odvolať", GUILayout.Height(32f)))
            {
                SettlementEconomy.Unassign(selected.PlotId);
                status = "Špecialista odvolaný.";
            }
            GUILayout.EndHorizontal();
        }

        DrawGatherers(selected);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"[{settlementKey}] / Esc zavrie");
    }

    // Zberači: na každú surovinu jeden špecialista, pracuje vo vybranej budove a ukladá do truhly pri nej
    void DrawGatherers(BuildPlot selected)
    {
        GUILayout.Space(6f);
        GUILayout.Label($"<b>ZBERAČI</b> – najatie {SettlementGatherers.HireCost} peňazí; pracujú vo vybranej budove, suroviny ukladajú do truhly pri nej (E pri truhle). Nepracujú v noci, pri poplachu ani pri výpadku portálu.", Rich());

        foreach (GatherMaterial m in System.Enum.GetValues(typeof(GatherMaterial)))
        {
            string plotId = SettlementGatherers.PlotOf(m);
            BuildPlot work = FindPlot(plotId);
            string item = ItemCatalog.Get(SettlementGatherers.ItemOf(m)).displayName.ToLowerInvariant();
            string state = plotId == null ? "voľné miesto" : work != null ? $"{work.Building.displayName}; truhla: {SettlementGatherers.Summary(plotId)}" : "budova nestojí";

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{SettlementGatherers.Role(m)}</b> ({item}): {state}", Rich(), GUILayout.Width(500f));
            if (GUILayout.Button(plotId == null ? "Najať" : "Presunúť", GUILayout.Height(24f)))
            {
                SettlementGatherers.Hire(m, selected, player, out string message);
                status = message;
            }
            GUI.enabled = plotId != null;
            if (GUILayout.Button("Prepustiť", GUILayout.Height(24f)))
            {
                SettlementGatherers.Dismiss(m);
                status = $"{SettlementGatherers.Role(m)} prepustený.";
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
    }

    static BuildPlot FindPlot(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (BuildPlot plot in BuildPlot.All)
            if (plot.PlotId == id && plot.State == PlotState.Built && plot.Building != null) return plot;
        return null;
    }

    private GUIStyle rich;
    GUIStyle Rich()
    {
        if (rich == null) rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        return rich;
    }
}
