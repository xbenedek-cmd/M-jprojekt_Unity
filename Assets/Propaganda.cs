using System.Collections.Generic;
using UnityEngine;

public enum BroadcastKind { Screen, Tower }
public enum BroadcastMsg { RebelCall, DemonWarning, Panic, FalseAlarm }

/// <summary>
/// Propaganda a informačná vojna. Mestské obrazovky (Centrum) a vysielacie veže (Megakorporácia) sa dajú hacknúť (kláves „použiť“, drž).
/// Čo hráč pošle, závisí od formy:
///  - ČLOVEK: „Rebelské posolstvo“ (rebeli ti dajú kódy: treba dôveru odboja 15) zvýši odboj a civilistov, zníži Megakorporáciu;
///    „Varovanie pred démonmi“ je proti-propaganda pre Megakorporáciu (treba jej dôveru 5 a nesmieš byť hľadaný): vplyv Megakorporácie sa zvýši, odboj ťa vníma horšie.
///  - DÉMON: „Šíriť paniku“ zníži dôveru civilistov aj Megakorporácie a spokojnosť mesta, nasýti ťa strachom (krv) a poteší Arakaniho;
///    „Falošný poplach“ odláka vojakov v okolí 160 m k obrazovke, aby si mohol konať inde.
/// Každý hack zvyšuje „Pozornosť armády“ (Heat 0–100, denne klesá o 8). Veža pôsobí 1,5× silnejšie, ale aj Heat rastie 1,5×. Hack pred svedkom je prezradený:
/// polovičný účinok, kriminálny čin a väčší Heat. Pri vysokej pozornosti pošle armáda elitné Mecha jednotky (HunterSquads).
/// </summary>
public static class Propaganda
{
    public static float Heat { get; private set; }
    public static int NextAllowedDay;
    public const int CooldownDays = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { Heat = 0f; NextAllowedDay = 0; }

    public static void Set(float heat, int nextDay) { Heat = Mathf.Clamp(heat, 0f, 100f); NextAllowedDay = nextDay; }
    public static void AddHeat(float amount) => Heat = Mathf.Clamp(Heat + amount, 0f, 100f);
    public static void DailyDecay() => Heat = Mathf.Max(0f, Heat - 8f);

    public static string Name(BroadcastMsg m)
    {
        switch (m)
        {
            case BroadcastMsg.RebelCall: return "Rebelské posolstvo";
            case BroadcastMsg.DemonWarning: return "Varovanie pred démonmi";
            case BroadcastMsg.Panic: return "Šíriť paniku";
            default: return "Falošný poplach";
        }
    }

    public static string Description(BroadcastMsg m)
    {
        switch (m)
        {
            case BroadcastMsg.RebelCall: return "Odboj +10, civilisti +4, Megakorporácia −10, spokojnosť mesta +3. Zvýši pozornosť armády (+25) a podozrenie. Treba dôveru odboja 15.";
            case BroadcastMsg.DemonWarning: return "Megakorporácia +8, odboj −6, civilisti −2, spokojnosť −3 (strach z démonov = disciplína). Zníži pozornosť armády (−15) a podozrenie. Treba dôveru Megakorporácie 5 a nesmieš byť hľadaný.";
            case BroadcastMsg.Panic: return "Civilisti −8, Megakorporácia −6, spokojnosť mesta −10. Strach ťa nasýti (+20 krvi), Arakani je spokojný. Pozornosť armády +20.";
            default: return "Vojaci a strážnici do 160 m sa rozbehnú k obrazovke. Čas konať inde. Megakorporácia −3, pozornosť armády +10.";
        }
    }

    public static bool ForDemon(BroadcastMsg m) => m == BroadcastMsg.Panic || m == BroadcastMsg.FalseAlarm;

    /// <summary>Dá sa správa teraz použiť? Ak nie, vráti dôvod.</summary>
    public static bool Available(BroadcastMsg m, PlayerStats player, out string reason)
    {
        reason = null;
        if (ForDemon(m) != player.IsDemon) { reason = player.IsDemon ? "Ako démon vysielaš iné správy." : "Ako človek vysielaš iné správy."; return false; }
        if (m == BroadcastMsg.RebelCall && Reputation.Get(FactionId.Rebels) < 15f) { reason = "Rebeli ti nedajú kódy: treba dôveru odboja 15."; return false; }
        if (m == BroadcastMsg.DemonWarning)
        {
            if (Reputation.Get(FactionId.Megacorp) < 5f) { reason = "Megakorporácia ti neverí: treba dôveru 5."; return false; }
            if (CivilianAI.LastPlayerCrimeTime >= 0f && Time.time - CivilianAI.LastPlayerCrimeTime <= 30f) { reason = "Si hľadaný, nikto ti neuverí."; return false; }
        }
        return true;
    }

    /// <summary>Vykoná hack. seen = pri hackovaní ťa niekto videl (prezradený: účinok ×0,5, zločin).</summary>
    public static void Execute(BroadcastMsg m, PlayerStats player, BroadcastTerminal terminal, bool seen, int day)
    {
        float s = (terminal != null && terminal.kind == BroadcastKind.Tower ? 1.5f : 1f) * (seen ? 0.5f : 1f);
        float heatScale = terminal != null && terminal.kind == BroadcastKind.Tower ? 1.5f : 1f;
        Vector3 at = terminal != null ? terminal.transform.position : player.transform.position;

        switch (m)
        {
            case BroadcastMsg.RebelCall:
                Reputation.Add(FactionId.Rebels, 10f * s);
                Reputation.Add(FactionId.Civilians, 4f * s);
                Reputation.Add(FactionId.Megacorp, -10f * s);
                CityManagement.Satisfaction = Mathf.Min(100f, CityManagement.Satisfaction + 3f * s);
                AddHeat(25f * heatScale);
                Reputation.AddSuspicion(8f);
                break;
            case BroadcastMsg.DemonWarning:
                Reputation.Add(FactionId.Megacorp, 8f * s);
                Reputation.Add(FactionId.Rebels, -6f * s);
                Reputation.Add(FactionId.Civilians, -2f * s);
                CityManagement.Satisfaction = Mathf.Max(0f, CityManagement.Satisfaction - 3f * s);
                AddHeat(-15f);
                Reputation.AddSuspicion(-6f);
                break;
            case BroadcastMsg.Panic:
                Reputation.Add(FactionId.Civilians, -8f * s);
                Reputation.Add(FactionId.Megacorp, -6f * s);
                CityManagement.Satisfaction = Mathf.Max(0f, CityManagement.Satisfaction - 10f * s);
                AddHeat(20f * heatScale);
                ArakaniFavor.AddFavor(4f);
                player.DrinkBlood(20f * s);
                Reputation.AddSuspicion(10f);
                break;
            default:
                SoldierAI.ReportCrimeAt(at, 160f, at);
                CityGuardNPC.ReportCrimeAt(at, 160f, at);
                Reputation.Add(FactionId.Megacorp, -3f * s);
                AddHeat(10f * heatScale);
                Reputation.AddSuspicion(4f);
                break;
        }

        if (seen)
        {
            CivilianAI.LastPlayerCrimeTime = Time.time;
            SoldierAI.ReportCrimeAt(player.transform.position, 30f, player.transform.position);
            Reputation.AddSuspicion(15f);
            AddHeat(20f);
        }
        NextAllowedDay = day + CooldownDays;
    }
}

/// <summary>Hacknuteľná obrazovka alebo veža. Vytvára ich PropagandaRunner pri zónach mesta.</summary>
public class BroadcastTerminal : MonoBehaviour
{
    public static readonly List<BroadcastTerminal> All = new List<BroadcastTerminal>();
    public BroadcastKind kind = BroadcastKind.Screen;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    static readonly Color Cyan = new Color(0.2f, 0.85f, 1f), Magenta = new Color(1f, 0.25f, 0.7f);

    /// <summary>Postaví obrazovku (na stĺpe) alebo vežu so zábleskom; bez svetelných zdrojov kvôli výkonu.</summary>
    public static BroadcastTerminal Create(Transform parent, Vector3 world, BroadcastKind kind, float yaw)
    {
        var root = new GameObject(kind == BroadcastKind.Tower ? "VysielaciaVeza" : "VysielaciaObrazovka");
        root.transform.SetParent(parent, true);
        root.transform.SetPositionAndRotation(world, Quaternion.Euler(0f, yaw, 0f));

        Material iron = new Material(Shader.Find("Standard")) { color = new Color(0.1f, 0.1f, 0.13f) };
        Material glow = new Material(Shader.Find("Standard")) { color = kind == BroadcastKind.Tower ? Magenta : Cyan };
        glow.EnableKeyword("_EMISSION");
        glow.SetColor("_EmissionColor", glow.color * 2.2f);

        void Part(string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, bool collider)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.name = name;
            if (!collider) Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
        }

        if (kind == BroadcastKind.Tower)
        {
            Part("Podstava", PrimitiveType.Cylinder, new Vector3(0f, 0.3f, 0f), new Vector3(2.4f, 0.3f, 2.4f), iron, true);
            Part("Stlp", PrimitiveType.Cylinder, new Vector3(0f, 7f, 0f), new Vector3(0.9f, 6.7f, 0.9f), iron, true);
            Part("Obrazovka", PrimitiveType.Cube, new Vector3(0f, 10.5f, 0.55f), new Vector3(6f, 3.2f, 0.25f), glow, false);
            Part("Anténa", PrimitiveType.Cylinder, new Vector3(0f, 15.2f, 0f), new Vector3(0.12f, 2f, 0.12f), iron, false);
            Part("Maják", PrimitiveType.Sphere, new Vector3(0f, 17.4f, 0f), new Vector3(0.45f, 0.45f, 0.45f), glow, false);
        }
        else
        {
            Part("Podstava", PrimitiveType.Cylinder, new Vector3(0f, 0.2f, 0f), new Vector3(1.4f, 0.2f, 1.4f), iron, true);
            Part("Stlp", PrimitiveType.Cylinder, new Vector3(0f, 2.4f, 0f), new Vector3(0.35f, 2.2f, 0.35f), iron, true);
            Part("Obrazovka", PrimitiveType.Cube, new Vector3(0f, 5.2f, 0.25f), new Vector3(4.2f, 2.3f, 0.2f), glow, false);
            Part("Rám", PrimitiveType.Cube, new Vector3(0f, 5.2f, 0.12f), new Vector3(4.5f, 2.6f, 0.15f), iron, false);
        }

        var tm = new GameObject("Nápis").AddComponent<TextMesh>();
        tm.transform.SetParent(root.transform, false);
        tm.transform.localPosition = new Vector3(0f, kind == BroadcastKind.Tower ? 10.5f : 5.2f, 0.7f);
        tm.text = kind == BroadcastKind.Tower ? "MEGAKORPORÁCIA\nVYSIELANIE" : "VYSIELANIE";
        tm.fontSize = 60;
        tm.characterSize = kind == BroadcastKind.Tower ? 0.12f : 0.08f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(0.02f, 0.02f, 0.05f);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.font = font;
        tm.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        tm.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        // Text patrí na prednú (+z) stranu obrazovky: otočenie o 180° ho robí čitateľným pre divákov pred ňou
        tm.transform.localPosition = new Vector3(0f, kind == BroadcastKind.Tower ? 10.5f : 5.2f, 0.7f);

        var terminal = root.AddComponent<BroadcastTerminal>();
        terminal.kind = kind;
        return terminal;
    }
}

/// <summary>Beh propagandy: tvorba terminálov v zónach, interakcia (drž „použiť“), menu správ, denný pokles pozornosti a jej HUD pásik.</summary>
public class PropagandaMenu : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }

    private const float Reach = 4f;
    private PlayerStats player;
    private DayNightCycle clock;
    private BroadcastTerminal near, active;
    private float hold, lastCheck;
    private bool created;
    private float nextScan, createAt = -1f;
    private string status;
    private float statusUntil;
    private bool seenAtStart;
    private GUIStyle title, text, hint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<PropagandaMenu>() != null) return;
        new GameObject("PropagandaMenu").AddComponent<PropagandaMenu>();
    }

    void OnEnable() => DayNightCycle.OnNewDay += OnNewDay;
    void OnDisable() { DayNightCycle.OnNewDay -= OnNewDay; MenuOpen = false; }
    void OnNewDay(int day) { Propaganda.DailyDecay(); DemonRaid.DecayRecent(); }

    // Terminály sa postavia raz, keď existujú zóny mesta
    void BuildTerminals()
    {
        created = true;
        PlaceGroup("Zem_Centrum", BroadcastKind.Screen, new[] { new Vector3(90f, 0f, 14f), new Vector3(-90f, 0f, -14f), new Vector3(12f, 0f, 92f), new Vector3(-14f, 0f, -92f) });
        PlaceGroup("Zem_Megakorporacia", BroadcastKind.Tower, new[] { new Vector3(34f, 0f, 20f), new Vector3(-36f, 0f, -22f) });
    }

    void PlaceGroup(string ground, BroadcastKind kind, Vector3[] offsets)
    {
        GameObject g = GameObject.Find(ground);
        if (g == null) return;   // zóna je vypnutá alebo neexistuje: skúsi sa znova po zapnutí (created sa nenastaví)
        Transform parent = g.transform.parent != null ? g.transform.parent : g.transform;
        Vector3 center = g.transform.position;
        Collider gc = g.GetComponent<Collider>();
        if (gc != null) center = gc.bounds.center;

        foreach (Vector3 off in offsets)
        {
            Vector3 p = center + off;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (!Physics.CheckSphere(p + Vector3.up * 2f, 1.6f, ~0, QueryTriggerInteraction.Ignore)) break;
                p += new Vector3(3f, 0f, 2f);
            }
            if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
            else p.y = center.y;

            Vector3 toCenter = center - p;
            toCenter.y = 0f;
            float yaw = toCenter.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toCenter).eulerAngles.y : 0f;
            BroadcastTerminal.Create(parent, p, kind, yaw);
        }
    }

    void Update()
    {
        if (PauseMenu.AtStartup) return;
        if (player == null) { player = FindAnyObjectByType<PlayerStats>(); return; }
        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();

        // Terminály: zóny sa zapínajú podľa polohy hráča, preto sa dopĺňa, kým chýbajú
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 4f;
            bool hasScreens = false, hasTowers = false;
            foreach (BroadcastTerminal t in BroadcastTerminal.All)
            {
                if (t == null) continue;
                if (t.kind == BroadcastKind.Screen) hasScreens = true; else hasTowers = true;
            }
            if (!hasScreens && GameObject.Find("Zem_Centrum") != null) PlaceGroup("Zem_Centrum", BroadcastKind.Screen, new[] { new Vector3(90f, 0f, 14f), new Vector3(-90f, 0f, -14f), new Vector3(12f, 0f, 92f), new Vector3(-14f, 0f, -92f) });
            if (!hasTowers && GameObject.Find("Zem_Megakorporacia") != null) PlaceGroup("Zem_Megakorporacia", BroadcastKind.Tower, new[] { new Vector3(34f, 0f, 20f), new Vector3(-36f, 0f, -22f) });
        }

        if (player.IsDead) { MenuOpen = false; return; }

        if (MenuOpen)
        {
            if (GameInput.GetKeyDown(KeyCode.Escape)) Close();
            else if (GameInput.GetKeyDown(KeyCode.Alpha1)) Pick(0);
            else if (GameInput.GetKeyDown(KeyCode.Alpha2)) Pick(1);
            return;
        }
        if (Healer.MenuOpen) { hold = 0f; return; }

        near = Nearest();
        KeyCode use = KeyBindings.Get("use", KeyCode.F);
        if (near == null) { hold = 0f; return; }

        int day = clock != null ? clock.Day : 0;
        if (day < Propaganda.NextAllowedDay)
        {
            GameHUD.Prompt($"Vysielanie je zablokované po poslednom hacku (zajtra {Propaganda.NextAllowedDay - day} d).");
            hold = 0f;
            return;
        }

        float need = HackSeconds();
        GameHUD.Prompt($"[{KeyBindings.Name(use)}] podrž: hacknúť vysielanie ({need:0} s)");
        if (GameInput.GetKey(use))
        {
            if (hold <= 0f) seenAtStart = WitnessesNow(near) > 0;
            hold += Time.deltaTime;
            if (hold >= need) { hold = 0f; Open(near); }
        }
        else hold = 0f;
    }

    BroadcastTerminal Nearest()
    {
        BroadcastTerminal best = null;
        float bestSqr = Reach * Reach;
        foreach (BroadcastTerminal t in BroadcastTerminal.All)
        {
            if (t == null) continue;
            Vector3 d = t.transform.position - player.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < bestSqr) { bestSqr = d.sqrMagnitude; best = t; }
        }
        return best;
    }

    // Inžinier s dôverou 25 zrýchli hack
    float HackSeconds() => ProfessionTrust.Get(Profession.Engineer) >= 25f ? 3f : 5f;

    int WitnessesNow(BroadcastTerminal t)
    {
        if (player.IsInvisible) return 0;
        return StealthWorld.CountWitnesses(player.transform.position, player.transform, SoldierAI.SightRadius, 140f);
    }

    void Open(BroadcastTerminal terminal)
    {
        active = terminal;
        MenuOpen = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Close()
    {
        MenuOpen = false;
        active = null;
    }

    void Pick(int slot)
    {
        BroadcastMsg[] options = player.IsDemon
            ? new[] { BroadcastMsg.Panic, BroadcastMsg.FalseAlarm }
            : new[] { BroadcastMsg.RebelCall, BroadcastMsg.DemonWarning };
        BroadcastMsg m = options[Mathf.Clamp(slot, 0, 1)];

        if (!Propaganda.Available(m, player, out string reason)) { Say(reason); return; }

        bool seen = seenAtStart || WitnessesNow(active) > 0;
        Propaganda.Execute(m, player, active, seen, clock != null ? clock.Day : 0);
        GameHUD.Message(seen ? $"Hack prezradený! {Propaganda.Name(m)} vyšlo, ale ťa videli." : $"{Propaganda.Name(m)} odvysielané.", 5f);
        GameAudio.Play(GameAudio.Sfx.Portal, player.transform.position);
        Close();
    }

    void Say(string message) { status = message; statusUntil = Time.unscaledTime + 3.5f; }

    void OnGUI()
    {
        if (PauseMenu.AtStartup || player == null) return;
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            text = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            hint = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            hint.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        }

        // Pásik hackovania
        if (!MenuOpen && hold > 0f && near != null)
        {
            float need = HackSeconds();
            Rect bar = new Rect(Screen.width * 0.5f - 130f, Screen.height * 0.68f, 260f, 16f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 0.9f, 1f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(hold / need), bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(bar.x, bar.y - 22f, bar.width, 20f), seenAtStart ? "Hackuješ... niekto ťa vidí!" : "Hackuješ vysielanie...", hint);
        }

        if (!MenuOpen) return;

        float w = Mathf.Min(660f, Screen.width - 40f), h = 420f;
        Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = new Color(0.04f, 0.05f, 0.09f, 0.97f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = active != null && active.kind == BroadcastKind.Tower ? new Color(1f, 0.25f, 0.7f) : new Color(0.2f, 0.85f, 1f);
        GUI.DrawTexture(new Rect(box.x, box.y, box.width, 4f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(box.x + 24f, box.y + 12f, box.width - 48f, 30f), active != null && active.kind == BroadcastKind.Tower ? "VYSIELACIA VEŽA MEGAKORPORÁCIE" : "MESTSKÁ OBRAZOVKA", title);
        GUI.Label(new Rect(box.x + 24f, box.y + 46f, box.width - 48f, 40f),
            player.IsDemon ? "Ako démon môžeš šíriť strach alebo odlákať stráže. Veža pôsobí 1,5× silnejšie." : "Ako človek môžeš vysielať rebelské posolstvo alebo naopak podporiť Megakorporáciu. Veža pôsobí 1,5× silnejšie.", text);
        GUI.Label(new Rect(box.x + 24f, box.y + 86f, box.width - 48f, 22f), $"Pozornosť armády: {Propaganda.Heat:0} / 100   |   Spokojnosť mesta: {CityManagement.Satisfaction:0}", hint);

        BroadcastMsg[] options = player.IsDemon
            ? new[] { BroadcastMsg.Panic, BroadcastMsg.FalseAlarm }
            : new[] { BroadcastMsg.RebelCall, BroadcastMsg.DemonWarning };
        float y = box.y + 118f;
        for (int i = 0; i < options.Length; i++)
        {
            bool ok = Propaganda.Available(options[i], player, out string reason);
            GUI.enabled = ok;
            if (GUI.Button(new Rect(box.x + 24f, y, box.width - 48f, 36f), $"[{i + 1}] {Propaganda.Name(options[i])}" + (ok ? "" : $"   ({reason})"))) Pick(i);
            GUI.enabled = true;
            GUI.Label(new Rect(box.x + 34f, y + 38f, box.width - 68f, 62f), Propaganda.Description(options[i]), hint);
            y += 106f;
        }

        GUI.Label(new Rect(box.x + 24f, box.yMax - 62f, box.width - 48f, 40f),
            seenAtStart ? "Pozor: niekto ťa pri hackovaní videl. Vysielanie bude prezradené (účinok ×0,5, trest)." : "Nikto ťa nevidí. Vysielanie vyjde čisto.", hint);
        if (statusUntil > Time.unscaledTime) GUI.Label(new Rect(box.x + 24f, box.yMax - 30f, box.width - 48f, 24f), status, text);
        GUI.Label(new Rect(box.xMax - 160f, box.yMax - 26f, 150f, 20f), "Esc: zrušiť", hint);
    }
}
