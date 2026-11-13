using System;
using System.Collections.Generic;
using UnityEngine;

public enum StoryPath { None, Human, Demon, Balance }

/// <summary>
/// Príbeh podľa GDD: prológ (konvoj, masový hrob, invertované vnímanie), akt I (dvojitý život, Arakaniho hlas v noci), akt II (rozcestie:
/// cesta človeka / démona / rovnováhy so zadanými cieľmi), finále a štyri konce A–D. Stav je statický a ukladá sa so svetom.
/// </summary>
public static class Story
{
    public static bool PrologueDone;
    public static int Act;                       // 0 = prológ, 1 = akt I, 2 = akt II
    public static StoryPath Path;
    public static string Ending;                 // null = hra pokračuje; inak "A1", "A2", "B1", "B2", "C1", "C2", "D"
    public static int BossKills;
    public static int NightLine;

    public const int ActTwoDay = 8;              // od tohto dňa (a po splnení úloh) príde rozcestie
    public const int CrossroadsQuestGate = 5;    // rozcestie sa ukáže až po 5. úlohe (prológ, Tiene, Šepot, 4., 5.)
    public static int ExtraQuestsDone;           // splnené úlohy 4 a 5 (zatiaľ nie sú v hre; budúce úlohy zvýšia toto číslo)

    /// <summary>Počet splnených hlavných úloh: prológ, Tiene v Hlbokom lese, Šepot v Dymových Horách + ďalšie.</summary>
    public static int CompletedQuests =>
        (PrologueDone || Prologue.Step == PrologueStep.Done ? 1 : 0)
        + (MainQuest.State == QuestState.Done || MainQuest.State == QuestState.Resolved ? 1 : 0)
        + (Quest2.Step == Quest2Step.Done ? 1 : 0)
        + ExtraQuestsDone;

    public static bool CrossroadsReady => CompletedQuests >= CrossroadsQuestGate;
    public const int FailureDay = 45;            // bez dokončenia cesty do tohto dňa: koniec C

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        PrologueDone = false;
        Act = 0;
        Path = StoryPath.None;
        Ending = null;
        BossKills = 0;
        NightLine = 0;
        ExtraQuestsDone = 0;
        Quest2.ResetState();
    }

    [Serializable]
    public class SaveData
    {
        public bool prologueDone;
        public int act, path, bossKills, nightLine, destroyed, extraQuests;
        public string ending;
        public string q2;
    }

    public static SaveData Export() => new SaveData { prologueDone = PrologueDone, act = Act, path = (int)Path, bossKills = BossKills, nightLine = NightLine, destroyed = DemonRaid.Destroyed, ending = Ending, q2 = Quest2.Export(), extraQuests = ExtraQuestsDone };

    /// <summary>Starší save bez príbehu: prológ sa nehrá znova, hra ide ako akt I.</summary>
    public static void Import(SaveData data)
    {
        ResetState();
        if (data == null)
        {
            PrologueDone = true;
            Act = 1;
            return;
        }
        PrologueDone = data.prologueDone;
        Act = data.act;
        Path = (StoryPath)data.path;
        BossKills = data.bossKills;
        NightLine = data.nightLine;
        DemonRaid.Destroyed = data.destroyed;
        Ending = string.IsNullOrEmpty(data.ending) ? null : data.ending;
        ExtraQuestsDone = data.extraQuests;
        Quest2.Import(data.q2);
    }
}

public class StoryDirector : MonoBehaviour
{
    // ---------- Texty ----------
    static readonly string[] prologue =
    {
        "Si mladý dôstojník Vojenskej Megakorporácie. Dostal si rozkaz: eskortovať elitný konvoj s nebezpečným nákladom na okraj mesta, do divočiny.",
        "Konvoj havaruje. Pri troskách nájdeš pravdu: Megakorporácia nevyhadzuje do lesov len toxický odpad. Tajne tam ukladá aj telá civilistov, rebelov a nepohodlných ľudí.",
        "Prastará duchovná energia zeme, Kami, otrávená týmto odpadom, zrodí prvú vlnu beštií. Vyšší Démon napadne tvoju jednotku. Zomieraš s prestreleným brnením.",
        "Ale nezomrieš. Tvoje telo pohltí duchovný odpad a toxická mágia. Prebudíš sa v masovom hrobe. Je noc.",
        "Vraciaš sa do ulíc. Všade vidíš ohavné monštrá s neónovými očami. Kričia na teba a útočia. V skutočnosti sú to vystrašení strážcovia a civilisti.",
        "Keď vyjde slnko, zrak sa ti vráti. Nikto na teba nezaútočí. Si jediný Hybrid: genetická chyba, alebo odpoveď sveta na katastrofu. Začína tvoj dvojitý život."
    };

    static readonly string[] actOneNight =
    {
        "Počuješ ma, Hybrid? Som tvoj pôvod, hniezdo lesa. Príď.",
        "Ľudia ťa nikdy neprijmú. Mesto ťa zradilo skôr, než si sa narodil.",
        "Les ťa volá. Tma ťa pozná. Splň, čo žiadam, a nebudeš trpieť.",
        "Každý konvoj, ktorý zastavíš, je jeden dych pre prírodu.",
        "Cítiš to v kostiach? Si viac môj, než si pripúšťaš.",
        "Mesto ti dá strechu. Ja ti dám silu. Vyber si.",
        "Neposlúchaš? Tvoje telo si to zapamätá.",
        "Spomeň si, kto ťa pochoval v masovom hrobe."
    };

    static readonly string[] actTwoNight =
    {
        "Ďalej to nemá zmysel zdržiavať. Rozhodol si sa, Hybrid?",
        "Moja krv čaká. Stačí sa napiť.",
        "Ľudia sa trasú. Cítim ich strach až sem.",
        "Rovnováha je len príbeh pre slabých. Alebo pre múdrych."
    };

    // ---------- Beh ----------
    private PlayerStats player;
    private float startDelay;
    private bool crossroadsOpen;
    private bool endingChoiceOpen;
    private float nextCheck;
    private readonly HashSet<DemonEnemy> countedBosses = new HashSet<DemonEnemy>();
    private GUIStyle title, body, button;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<StoryDirector>() != null) return;
        new GameObject("StoryDirector").AddComponent<StoryDirector>();
    }

    void OnEnable()
    {
        DayNightCycle.OnPhaseChanged += OnPhase;
        DayNightCycle.OnNewDay += OnNewDay;
    }

    void OnDisable()
    {
        DayNightCycle.OnPhaseChanged -= OnPhase;
        DayNightCycle.OnNewDay -= OnNewDay;
    }

    void Update()
    {
        if (PauseMenu.AtStartup) { startDelay = 0f; return; }

        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            return;
        }

        startDelay += Time.unscaledDeltaTime;
        if (startDelay < 4f || MangaDialog.Active || player.IsDead) return;   // po načítaní uloženého sveta

        if (!Story.PrologueDone)
        {
            if (Prologue.CanRun())
            {
                Prologue.Begin();   // hrateľný prológ: drevorubači, Divočina, prepad, smrť a prebudenie
                return;
            }

            Story.PrologueDone = true;
            Story.Act = 1;
            MangaDialog.Show("Prológ", prologue, () => GameHUD.Message("Prológ skončil. Cez deň buduj mesto, v noci počúvaj hlas lesa.", 6f));
            return;
        }

        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 2f;
        if (!Story.CrossroadsReady && Story.Path == StoryPath.None && Story.Act >= 2 && Story.Ending == null)
        {
            // starý save: rozcestie prišlo podľa dňa, hoci ešte nie sú splnené 5 úlohy
            Story.Act = 1;
            if (crossroadsOpen) { crossroadsOpen = false; EndModal(); }
        }
        if (Story.CrossroadsReady && Story.Path == StoryPath.None)
        {
            DayNightCycle clock = FindAnyObjectByType<DayNightCycle>();
            if (clock != null) TryActTwo(clock.Day);
        }
        CountBossKills();
        if (Story.Ending == null && Story.Act == 2) CheckFinale();
    }

    void CountBossKills()
    {
        foreach (DemonEnemy demon in DemonEnemy.All)
            if (demon != null && demon.tier == DemonTier.Boss && demon.Damage != null && demon.Damage.IsDead && countedBosses.Add(demon))
                Story.BossKills++;
    }

    // ---------- Noc a dni ----------
    void OnPhase(DayNightCycle.DayPhase phase)
    {
        if (phase != DayNightCycle.DayPhase.Night || Story.Ending != null || Story.Act < 1) return;
        ArakaniFavor arakani = ArakaniFavor.Instance;
        if (arakani == null || !arakani.VoiceHeard) return;

        string[] lines = Story.Act >= 2 ? actTwoNight : actOneNight;
        MangaDialog.Whisper("Arakani", lines[Story.NightLine % lines.Length], 8f);
        Story.NightLine++;
    }

    void OnNewDay(int day)
    {
        TryActTwo(day);
        if (Story.Act == 2 && Story.Ending == null && day >= Story.FailureDay && !GoalsMet(Story.Path)) Finish(Story.Path == StoryPath.Demon ? "C2" : "C1");
    }

    /// <summary>Akt II a rozcestie: až keď je splnených 5 úloh (nie len podľa dňa).</summary>
    void TryActTwo(int day)
    {
        if (Story.Ending != null || !Story.PrologueDone || !Story.CrossroadsReady) return;

        if (Story.Act == 1 && day >= Story.ActTwoDay)
        {
            Story.Act = 2;
            MangaDialog.Show("Akt II", new[]
            {
                "Mesto sa mení pod tvojimi rukami a hlas v tvojej hlave sa stáva hlasnejším. Prišiel čas, kedy musíš zvoliť.",
                "Ľudská cesta: bojuj proti nákaze a postav mesto. Démonická cesta: prijmi Arakaniho krv a zničí civilizáciu. Cesta rovnováhy: zníž oboje a hľadaj tretí spôsob."
            }, () => crossroadsOpen = true);
        }
        else if (Story.Act == 2 && Story.Path == StoryPath.None && !crossroadsOpen) crossroadsOpen = true;
    }

    // ---------- Ciele ciest ----------
    public static int BuiltCount()
    {
        int n = 0;
        foreach (BuildPlot plot in BuildPlot.All) if (plot.State == PlotState.Built) n++;
        return n;
    }

    public static List<(string text, bool done)> Goals(StoryPath path)
    {
        var goals = new List<(string, bool)>();
        float humanity = SoulBalance.Humanity;
        float favor = ArakaniFavor.Instance != null ? ArakaniFavor.Instance.Favor : 0f;

        switch (path)
        {
            case StoryPath.Human:
                goals.Add(("Postaviť aspoň 6 budov", BuiltCount() >= 6));
                goals.Add(("Ľudskosť aspoň 60", humanity >= 60f));
                goals.Add(("Spokojnosť mesta aspoň 55", CityManagement.Satisfaction >= 55f));
                goals.Add(("Poraziť aspoň 1 bossa", Story.BossKills >= 1));
                break;
            case StoryPath.Demon:
                goals.Add(("Prijať Rituál Pekelnej Krvi", ArakaniFavor.BloodSealed));
                goals.Add(("Zničiť aspoň 5 budov", DemonRaid.Destroyed >= 5));
                goals.Add(("Dôvera Arakaniho aspoň 80", favor >= 80f));
                break;
            case StoryPath.Balance:
                goals.Add(("Postaviť aspoň 4 budovy", BuiltCount() >= 4));
                goals.Add(("Ľudskosť medzi 35 a 75", humanity >= 35f && humanity <= 75f));
                goals.Add(("Dôvera Arakaniho aspoň 40", favor >= 40f));
                goals.Add(("Poraziť aspoň 2 bossov", Story.BossKills >= 2));
                break;
        }
        return goals;
    }

    static bool GoalsMet(StoryPath path)
    {
        var goals = Goals(path);
        if (goals.Count == 0) return false;
        foreach (var g in goals) if (!g.done) return false;
        return true;
    }

    void CheckFinale()
    {
        if (Story.Path == StoryPath.None || endingChoiceOpen || crossroadsOpen || MangaDialog.Active || Inventory.MenuOpen) return;

        // Koniec C: úplné zlyhanie (ľudskosť aj dôvera na dne, žiadna cesta)
        if (SoulBalance.Humanity < 12f && (ArakaniFavor.Instance == null || ArakaniFavor.Instance.Favor < 20f) && Story.Path != StoryPath.Demon)
        {
            Finish(UnityEngine.Random.value < 0.5f ? "C1" : "C2");
            return;
        }

        if (GoalsMet(Story.Path))
        {
            endingChoiceOpen = true;
            MangaDialog.Show("Finále", new[] { "Všetko, čo si začal, sa dokončilo. Zostáva posledné rozhodnutie." });
        }
    }

    void Finish(string ending)
    {
        Story.Ending = ending;
        endingChoiceOpen = false;
        crossroadsOpen = false;
        if (!MangaDialog.Active) MangaDialog.Show("Koniec", new[] { EndingText(ending) });
    }

    static string EndingTitle(string e)
    {
        switch (e)
        {
            case "A1": return "Koniec A1: Obeť pre Nádej";
            case "A2": return "Koniec A2: Technokratický Chlad";
            case "B1": return "Koniec B1: Verný Sluha";
            case "B2": return "Koniec B2: Trón Zrady";
            case "C1": return "Koniec C1: Útek do ničoty";
            case "C2": return "Koniec C2: Odzbrojený Sluha";
            default: return "Koniec D: Cesta Rovnováhy";
        }
    }

    static string EndingText(string e)
    {
        switch (e)
        {
            case "A1": return "Porazil si hlavné hniezdo, no zdroje sú vyčerpané a mestom prejde nekonečná zima. Dáš prežívajúcim posledné zásoby a teplo. Zomieraš sám v zasnežených uliciach. „Poraziť monštrum nestačí, ak sme predtým spálili všetko, čo nás držalo pri živote.“";
            case "A2": return "Využiješ zvyšky korporátnej energie a zakonzervuješ sa v kryo-komore. Mesto okolo zamrzne a vyhynie. Ostávaš ako žijúci pamätník v mŕtvom svete. „Technológia bez prírody je len pomalá smrť.“";
            case "B1": return "Stávaš sa pravou rukou Arakaniho. Hradby padnú, mesto patrí lesu. Kráčaš po ruinách neónových ulíc a démonické územie sa rozrastá. „Keď nahradíš tyrana iným tyranom, svet nezíska slobodu.“";
            case "B2": return "Využiješ moc Hybrida, zradíš a zabiješ Arakaniho na vrchole jeho moci. Stávaš sa Novým Hlavným Démonom. „Zmení sa len pán, ktorý drží bič.“";
            case "C1": return "Hráš na obe strany a zlyháš v oboch. Prežiješ, no stratíš schopnosti, peniaze aj materiál. Ostávaš osamelý štvanec bez domova. „Neutralita bez činov je zbabelosť.“";
            case "C2": return "Zúfalo sa pridáš k jednej strane. Zoberú ti väčšinu moci a spravia z teba podradného sluhu. O pár mesiacov zomieraš v zabudnutí na následky rozpadu Hybrida.";
            default: return "Zničíš skorumpované vedenie Megakorporácie aj Arakaniho. Zhromaždíš prežitých civilistov a osvietených démonov v skrytej osade žijúcej v symbióze s prírodou. Veľké mesto sa rozpadne, ale ľudia a príroda nájdu mier. Po rokoch pokojne zomieraš ako uctievaný starosta. „Skutočná sila leží v rovnováhe a vzájomnom rešpekte.“";
        }
    }

    // ---------- Menu rozcestia a koncov ----------
    void OnGUI()
    {
        if (PauseMenu.AtStartup || MangaDialog.Active || Inventory.MenuOpen) return;   // pri inventári sa žiadne okno neotvára
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            body = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            button = new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true };
        }

        if (Story.Ending != null) { if (Story.Ending != "pokracovanie") DrawEnding(); return; }
        if (crossroadsOpen) DrawCrossroads();
        else if (endingChoiceOpen) DrawEndingChoice();
    }

    void BeginModal()
    {
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void EndModal()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    Rect Panel(float w, float h)
    {
        Rect r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.color = new Color(0.03f, 0.03f, 0.05f, 0.97f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        return r;
    }

    void DrawCrossroads()
    {
        BeginModal();
        Rect r = Panel(760f, 480f);
        GUILayout.BeginArea(new Rect(r.x + 24f, r.y + 20f, r.width - 48f, r.height - 40f));
        GUILayout.Label("ROZCESTIE", title);
        GUILayout.Label("Vyber cestu. Nemusí byť konečná, ale ovplyvní ciele a koniec hry. Ciele vidíš v paneli F2.", body);
        GUILayout.Space(10f);
        Choice(StoryPath.Human, "Cesta Človeka", "Bojuj proti nákaze, postav mesto, zachráň ľudí. Hrozí kruté vytriezvenie: zdroje sa raz minú.");
        Choice(StoryPath.Demon, "Cesta Démona", "Prijmi Arakaniho, rituál Pekelnej Krvi a zničenie civilizácie. Cesta späť je zatvorená.");
        Choice(StoryPath.Balance, "Cesta Rovnováhy", "Zníž moc oboch vodcov a hľadaj tretí spôsob. Najťažšia a tajná cesta.");
        GUILayout.EndArea();
    }

    void Choice(StoryPath path, string name, string description)
    {
        GUILayout.Label($"<b>{name}</b>", new GUIStyle(body) { richText = true });
        GUILayout.Label(description, body);
        if (GUILayout.Button($"Zvoliť: {name}", button, GUILayout.Height(34f)))
        {
            Story.Path = path;
            crossroadsOpen = false;
            EndModal();
            GameHUD.Message($"Zvolená cesta: {name}. Ciele nájdeš v paneli F2.", 6f);
        }
        GUILayout.Space(8f);
    }

    void DrawEndingChoice()
    {
        BeginModal();
        Rect r = Panel(760f, 360f);
        GUILayout.BeginArea(new Rect(r.x + 24f, r.y + 20f, r.width - 48f, r.height - 40f));
        GUILayout.Label("POSLEDNÉ ROZHODNUTIE", title);
        GUILayout.Space(10f);

        switch (Story.Path)
        {
            case StoryPath.Human:
                if (GUILayout.Button("Obeť pre Nádej: dáš svoje posledné zásoby a teplo prežívajúcim", button, GUILayout.Height(44f))) Finish("A1");
                if (GUILayout.Button("Technokratický Chlad: zakonzervuješ sa v kryo-komore", button, GUILayout.Height(44f))) Finish("A2");
                break;
            case StoryPath.Demon:
                if (GUILayout.Button("Verný Sluha: staneš sa pravou rukou Arakaniho", button, GUILayout.Height(44f))) Finish("B1");
                if (GUILayout.Button("Trón Zrady: zradíš a zabiješ Arakaniho", button, GUILayout.Height(44f))) Finish("B2");
                break;
            default:
                if (GUILayout.Button("Vytvoriť osadu v symbióze s prírodou", button, GUILayout.Height(44f))) Finish("D");
                break;
        }
        GUILayout.EndArea();
    }

    void DrawEnding()
    {
        BeginModal();
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float w = Mathf.Min(900f, Screen.width - 80f);
        GUILayout.BeginArea(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.15f, w, Screen.height * 0.75f));
        GUILayout.Label(EndingTitle(Story.Ending), title);
        GUILayout.Space(18f);
        GUILayout.Label(EndingText(Story.Ending), body);
        GUILayout.Space(30f);
        if (GUILayout.Button("Hlavné menu", button, GUILayout.Height(40f)))
        {
            EndModal();
            PauseMenu.ReturnToMainMenu();
        }
        if (GUILayout.Button("Pokračovať v hre", button, GUILayout.Height(40f)))
        {
            EndModal();
            Story.Ending = "pokracovanie";
            Story.Act = 3;
        }
        GUILayout.EndArea();
    }
}
