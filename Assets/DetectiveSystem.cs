using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DetectiveSystem : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }

    public KeyCode examineMenuKey => KeyBindings.Get("examine", KeyCode.T);
    [Header("Skúmanie")]
    public float examineRange = 7f;
    public float viewAngle = 35f;

    [Header("Vzdialenosti (v metroch)")]
    public float closeCheckDistance = 1.5f;
    public float animalCheckDistance = 7f;

    [Header("Skúmanie (klávesa T)")]
    [Tooltip("Základný čas skúmania (s). V démonickej forme trvá skúmanie VŽDY presne toľko, bez modifikátorov.")]
    public float scanDuration = 2f;
    [Tooltip("Skrátenie času (násobiteľ), ak je pri cieli sklo / zrkadlo / voda")]
    [Range(0.2f, 1f)] public float reflectionFactor = 0.7f;
    [Tooltip("Skrátenie času (násobiteľ), ak je pri cieli zviera")]
    [Range(0.2f, 1f)] public float animalFactor = 0.7f;
    [Tooltip("Násobiteľ času pri skúmaní zblízka; do examineRange rastie späť na 1 (z diaľky je skúmanie pomalšie)")]
    [Range(0.2f, 1f)] public float closeRangeFactor = 0.6f;
    [Tooltip("Čas skúmania v ľudskej forme nikdy neklesne pod túto hodnotu (s)")]
    public float minScanDuration = 0.5f;
    [Tooltip("XP za prvé skúmanie postavy")]
    public int scanXp = 10;

    [Header("Posily (len v démonickej forme)")]
    [Tooltip("Voliteľný vzor posily. Ak je prázdny, použije sa živý nižší/stredný démon zo scény.")]
    public GameObject reinforcementPrefab;
    public int reinforcementCount = 2;
    public int maxReinforcements = 4;
    public float reinforcementCooldown = 30f;
    public float reinforcementSpawnRadius = 3f;

    private PlayerStats stats;
    private PlayerFaction playerFaction;
    private NPCPerception focus;
    private DemonMarks focusMarks;
    private float nextFocusScan;
    private float hold;

    private readonly HashSet<NPCPerception> reported = new HashSet<NPCPerception>();
    private readonly List<DemonEnemy> reinforcements = new List<DemonEnemy>();
    private float nextReinforcement;

    private const int SrcClose = 1, SrcReflection = 2, SrcAnimal = 4;
    private bool examActive;
    private float examRequired = 1f;
    private string scanPrompt, scannedPrompt, identifiedPrompt;
    private KeyCode promptKey;

    private NPCPerception menuNpc;
    private DemonMarks menuMarks;
    private GUIStyle titleStyle, textStyle;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        playerFaction = GetComponent<PlayerFaction>();

        BuildPrompts();
    }

    // Texty výziev sa skladajú len pri zmene klávesy (Nastavenia → Ovládanie), nie každý frame
    void BuildPrompts()
    {
        promptKey = examineMenuKey;
        scanPrompt = $"[{promptKey}] Skúmať (drž) - sklo, zviera a blízkosť skracujú čas";
        scannedPrompt = $"Už si ho skúmal. [{promptKey}] Rozhodnúť, čo s ním";
        identifiedPrompt = $"Démon je odhalený! [{promptKey}] Rozhodnúť, čo s ním";
    }

    void OnDisable() => MenuOpen = false;
    void OnDestroy() => MenuOpen = false;

    void Update()
    {
        examActive = false;
        if (promptKey != examineMenuKey) BuildPrompts();

        if (stats == null || stats.IsDead)
        {
            MenuOpen = false;
            return;
        }

        if (MenuOpen)
        {
            MenuInput();
            return;
        }
        if (Healer.MenuOpen) return;

        if (Time.time >= nextFocusScan)
        {
            nextFocusScan = Time.time + 0.1f;
            FindFocus();
        }

        if (focus == null)
        {
            hold = 0f;
            return;
        }

        Damageable dmg = focus.GetComponent<Damageable>();
        BloodSource bs = focus.GetComponent<BloodSource>();
        if ((dmg != null && dmg.IsDead) || (bs != null && bs.IsDead)) return;
        if (focusMarks != null && focusMarks.Resolved) return;

        // Jednorazovosť: skúmanú postavu už nejde skúmať znova, T otvorí rozhodnutie
        if (focus.IsScanned || (focusMarks != null && focusMarks.Identified))
        {
            GameHUD.Prompt(focusMarks != null && focusMarks.Identified ? identifiedPrompt : scannedPrompt);

            if (GameInput.GetKeyDown(examineMenuKey))
                OpenMenu(focus, focusMarks);

            return;
        }

        GameHUD.Prompt(scanPrompt);

        if (GameInput.GetKey(examineMenuKey))
        {
            int avail = AvailableSources();
            if (hold <= 0f) examRequired = ComputeScanDuration(avail);   // pri začiatku držania, počas neho sa nemení
            ProcessHold(avail);
        }
        else
        {
            hold = 0f;
        }
    }

    // Démon: vždy fixný čas. Človek: základ skrátený sklom/zrkadlom, zvieraťom a malou vzdialenosťou.
    float ComputeScanDuration(int availableSources)
    {
        if (stats != null && stats.IsDemon) return scanDuration;

        float t = scanDuration;
        if ((availableSources & SrcReflection) != 0) t *= reflectionFactor;
        if ((availableSources & SrcAnimal) != 0) t *= animalFactor;

        float dist = Vector3.Distance(transform.position, focus.transform.position);
        float far = Mathf.InverseLerp(closeCheckDistance, examineRange, dist);   // 0 = zblízka, 1 = na hranici dosahu
        t *= Mathf.Lerp(closeRangeFactor, 1f, far);

        return Mathf.Max(minScanDuration, t);
    }

    void ProcessHold(int availableSources)
    {
        examActive = true;
        hold += Time.deltaTime;
        FreezeTarget();

        if (hold >= examRequired)
        {
            CompleteExam(availableSources);
            hold = 0f;
        }
    }

    void FreezeTarget()
    {
        if (focus == null) return;
        DemonEnemy demon = focus.GetComponent<DemonEnemy>();
        if (demon != null) demon.Stun(0.2f);

        CivilianAI civ = focus.GetComponent<CivilianAI>();
        if (civ != null) civ.Stun(0.2f);

        NPCWander wander = focus.GetComponent<NPCWander>();
        if (wander != null) wander.Pause(0.2f);
    }

    void FindFocus()
    {
        focus = null;
        focusMarks = null;

        Camera cam = Camera.main;
        if (cam == null) return;

        float bestAngle = viewAngle;
        foreach (NPCPerception n in NPCPerception.All)
        {
            if (n == null) continue;
            if (n.GetComponent<CityGuardNPC>() != null) continue;
            if (n.Revealed || reported.Contains(n)) continue;

            Damageable d = n.GetComponent<Damageable>();
            if (d != null && d.IsDead) continue;

            Vector3 to = n.transform.position - cam.transform.position;
            // Kamera je 8,5 m za hráčom, preto sa dosah meria len od hráča (nie od kamery)
            if (Vector3.Distance(transform.position, n.transform.position) > examineRange) continue;

            float angle = Vector3.Angle(cam.transform.forward, to);
            if (angle < bestAngle) { bestAngle = angle; focus = n; }
        }

        if (focus != null) focusMarks = focus.GetComponent<DemonMarks>();
    }

    int AvailableSources()
    {
        int src = 0;
        Vector3 npcPos = focus.transform.position;

        if (Vector3.Distance(transform.position, npcPos) <= closeCheckDistance) src |= SrcClose;
        if (ReflectiveSurface.Find(npcPos, transform.position) != null) src |= SrcReflection;
        if (DemonMarks.AnimalNear(npcPos, animalCheckDistance)) src |= SrcAnimal;

        return src;
    }

    void CompleteExam(int src)
    {
        focus.MarkScanned();
        focus.Unmask();   // vyšší démon odhodí masku až po úspešnom skúmaní

        if ((src & SrcClose) != 0) ExamineClose();
        if ((src & SrcReflection) != 0) ExamineReflection();
        if ((src & SrcAnimal) != 0) ExamineAnimals();

        // Civilista s povolaním: po skúmaní sa dá pomôcť (L), čo zvyšuje dôveru povolania
        CivilianProfile profile = focus.GetComponent<CivilianProfile>();
        if (profile != null && profile.Profession != Profession.None)
            GameHUD.Message($"Povolanie: {ProfessionTrust.Name(profile.Profession)}. Stlač [L], ak mu chceš pomôcť (surovina alebo peniaze) – zvýši sa jeho dôvera.", 6f);

        // Každé prvé skúmanie odhalí frakciu (človeka aj démona) a dá XP
        string factionName = FactionData.NameOf(focus.faction);
        if (stats != null)
        {
            // Človek dostane len skill XP; démonická úroveň rastie iba démonovi
            if (stats.IsDemon) stats.AddDemonXp(scanXp); else stats.AddSkillXp(scanXp);
            GameHUD.WorldText(focus.transform.position + Vector3.up * 2.5f, $"+{scanXp} XP", new Color(0.7f, 0.35f, 0.95f));
        }

        if (focusMarks != null && focusMarks.Identified && focus.faction != FactionId.None)
        {
            ArakaniFavor.AddFavor(2);
            GameHUD.Message($"Démon odhalený! Frakcia: {factionName} (+{scanXp} XP, +2 Dôvera)", 5f);
        }
        else
        {
            GameHUD.Message($"Frakcia: {factionName} (+{scanXp} XP)", 4f);
            if (focusMarks != null && focusMarks.DiscoveredCount == 1)
                GameHUD.Message("Našiel si len náznak - môže to byť choroba.", 4f);
        }
    }

    void ExamineClose()
    {
        if (focusMarks == null) return;
        bool any = false;
        if (focusMarks.pupilFlash) { any = true; Found(DemonSign.Pupil, "Zmenená zrenička!"); }
        if (focusMarks.teeth) { any = true; Found(DemonSign.Teeth, "Ostré tesáky!"); }
        if (focusMarks.veins) { any = true; Found(DemonSign.Veins, "Neprirodzené žily!"); }
        if (focusMarks.bodyAnomaly) { any = true; Found(DemonSign.Body, "Anomália postavy!"); }
        if (!any) GameHUD.Message("Zblízka vyzerá normálne.");
    }

    void ExamineReflection()
    {
        if (focusMarks != null && focusMarks.noReflection) Found(DemonSign.Reflection, "V odraze sa NEODRÁŽA!");
        else { if (focusMarks != null) focusMarks.MarkReflectionOk(); GameHUD.Message("V odraze sa odráža normálne."); }
    }

    void ExamineAnimals()
    {
        if (focusMarks != null && focusMarks.animalsFear) Found(DemonSign.Animals, "Zvieratá na neho vrčia!");
        else GameHUD.Message("Zvieratá sa k nemu správajú normálne.");
    }

    void Found(DemonSign sign, string text)
    {
        if (focusMarks == null || !focusMarks.Discover(sign)) return;
        GameHUD.Message(text, 4f);
        GameHUD.WorldText(focus.transform.position + Vector3.up * 2.4f, "ZNAK!", new Color(1f, 0.4f, 0.3f));
    }

    void OpenMenu(NPCPerception npc, DemonMarks marks)
    {
        menuNpc = npc; menuMarks = marks; MenuOpen = true;
    }

    void CloseMenu()
    {
        MenuOpen = false; menuNpc = null; menuMarks = null;
    }

    void MenuInput()
    {
        if (menuNpc == null || (menuNpc.GetComponent<Damageable>()?.IsDead ?? false)) { CloseMenu(); return; }

        if (GameInput.GetKeyDown(KeyCode.Escape)) CloseMenu();
        else if (GameInput.GetKeyDown(KeyCode.Alpha1)) QuietFight();
        else if (GameInput.GetKeyDown(KeyCode.Alpha2)) Report();
        else if (GameInput.GetKeyDown(KeyCode.Alpha3)) StartFight(false);
        else if (GameInput.GetKeyDown(KeyCode.Alpha4)) Contact();
        else if (GameInput.GetKeyDown(KeyCode.Alpha5)) CallReinforcements();
    }

    // ---------- Posily (len démon) ----------
    void CallReinforcements()
    {
        if (!stats.IsDemon) { GameHUD.Message("Posily môže privolať len démon.", 3f); return; }
        if (Time.time < nextReinforcement)
        {
            GameHUD.Message($"Posily ešte nie sú pripravené ({nextReinforcement - Time.time:0} s).", 3f);
            return;
        }

        reinforcements.RemoveAll(IsGoneOrDead);
        int free = maxReinforcements - reinforcements.Count;
        if (free <= 0) { GameHUD.Message("Už máš dosť posíl na bojisku.", 3f); return; }

        GameObject template = reinforcementPrefab != null ? reinforcementPrefab : FindReinforcementTemplate();
        if (template == null) { GameHUD.Message("Nie je k dispozícii vzor nižšieho démona na privolanie.", 3f); return; }

        int count = Mathf.Min(reinforcementCount, free);
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            float angle = (i + UnityEngine.Random.value) * Mathf.PI * 2f / Mathf.Max(1, count);
            Vector3 pos = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * reinforcementSpawnRadius;
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                pos.y = hit.point.y;

            GameObject go = Instantiate(template, pos, Quaternion.identity, template.transform.parent);
            DemonEnemy demon = go.GetComponent<DemonEnemy>();

            // Iba stredné a nižšie úrovne; vyšší démon sa nikdy nespawnuje
            if (demon == null || demon.Tier > DemonTier.Mid)
            {
                Destroy(go);
                continue;
            }

            demon.allied = true;                // na hráča-démona neútočí
            demon.attacksCivilians = false;     // cieľ skúmania nezabije sám: to je rozhodnutie hráča
            reinforcements.Add(demon);
            spawned++;
        }

        if (spawned == 0) { GameHUD.Message("Posily sa nepodarilo privolať.", 3f); return; }

        nextReinforcement = Time.time + reinforcementCooldown;
        GameHUD.Message($"Privolal si posily: {spawned} démon(i).", 3f);
    }

    static bool IsGoneOrDead(DemonEnemy d) => d == null || d.Damage == null || d.Damage.IsDead;

    // Živý nižší/stredný démon v scéne (nie nezraniteľný), z ktorého sa posila naklonuje
    static GameObject FindReinforcementTemplate()
    {
        foreach (DemonEnemy d in DemonEnemy.All)
        {
            if (IsGoneOrDead(d) || d.Tier > DemonTier.Mid) continue;
            return d.gameObject;
        }
        return null;
    }

    static bool IsDemonTarget(NPCPerception npc)
    {
        if (npc == null) return false;
        if (npc.GetComponent<DemonEnemy>() != null) return true;
        Damageable d = npc.GetComponent<Damageable>();
        return d != null && d.team == CombatTeam.Demon;
    }

    // Svedok = civilista, odbojár alebo vojak, ktorý na miesto vidí (dosah, zorný kužeľ a výhľad), nie len ktokoľvek v 12 m
    int Witnesses(NPCPerception self) => StealthWorld.CountWitnesses(self.transform.position, self.transform, 14f, 140f);

    void QuietFight()
    {
        if (menuNpc == null) return;
        if (Witnesses(menuNpc) > 0) { GameHUD.Message("Príliš veľa svedkov!", 3f); return; }
        StartFight(true);
    }

    void StartFight(bool ambush)
    {
        NPCPerception npc = menuNpc; DemonMarks m = menuMarks;
        if (npc == null) { CloseMenu(); return; }

        DemonEnemy e = npc.GetComponent<DemonEnemy>();
        Damageable d = npc.GetComponent<Damageable>();

        if (!IsDemonTarget(npc))
        {
            if (ambush)
            {
                if (d != null && !d.IsDead) d.TakeDamage(d.maxHealth * 10f, gameObject);
                // Nikto to nevidel (QuietFight to kontroluje), ale telo sa nájde: menší trest než pred svedkami
                StealthWorld.RegisterBody(npc.transform.position, true, d != null ? d.displayName : "civilista");
                GameHUD.Message("Zabil si nevinného. Nikto ťa nevidel, ale telo sa nájde.", 4f);
            }
            else
            {
                CivilianAI ci = npc.GetComponent<CivilianAI>(); if (ci != null) ci.Provoke(true);
                Reputation.Add(FactionId.Civilians, -6f); GameHUD.Message("Napadol si nevinného!", 4f);
            }
            CloseMenu(); return;
        }

        if (m != null) m.Resolved = true;
        npc.Reveal();

        if (e != null)
        {
            e.Provoke();
            if (ambush) e.Stun(3f);
        }
        CloseMenu();
    }

    void Report()
    {
        NPCPerception npc = menuNpc;
        if (npc == null) return;

        // Démon nemôže privolať ani nahlásiť vojakov systému
        if (stats.IsDemon) { GameHUD.Message("Ako démon nemôžeš nahlásiť nikoho vojakom.", 3f); return; }

        if (menuMarks != null) menuMarks.Resolved = true;
        reported.Add(npc);

        if (IsDemonTarget(npc))
        {
            npc.Reveal();
            DemonEnemy e = npc.GetComponent<DemonEnemy>();
            if (e != null)
            {
                e.MarkOfficiallyReported();
                SoldierAI.ReportDemonToSoldiers(e);
            }

            // Nahlásenie démona je dobrý čin: frakcie ti veria viac (zločiny to ale nevymaže).
            Reputation.Add(FactionId.Civilians, 12f);
            Reputation.Add(FactionId.Megacorp, 10f);
            Reputation.AddSuspicion(-10f);   // staré zločiny sa tým nemažú

            GameHUD.Message("Nahlásené! Vojaci idú po démone. (+12 Civilisti, +10 Megakorporácia)", 4f);
        }
        else
        {
            Reputation.Add(FactionId.Civilians, -20f);
            Reputation.Add(FactionId.Megacorp, -15f);
            Reputation.AddSuspicion(25f);

            // Falošné hlásenie je trestným činom hráča; vojaci nesmú meniť identitu civilistu.
            CivilianAI.LastPlayerCrimeTime = Time.time;
            SoldierAI.ReportHumanToSoldiers();

            GameHUD.Message("Nahlásil si nevinného! Vojaci sa obrátia proti tebe. (-20 Civilisti, -15 Megakorporácia)", 5f);

            npc.Reveal();

            // Nahlásený civilista sa len zľakne; AI mu nevypíname natrvalo
            CivilianAI civ = npc.GetComponent<CivilianAI>();
            if (civ != null) civ.Alarm(transform.position);
        }

        CloseMenu();
    }

    void Contact()
    {
        if (!stats.IsDemon) { GameHUD.Message("Len ako démon.", 3f); return; }
        NPCPerception npc = menuNpc; if (npc == null) return;
        DemonEnemy e = npc.GetComponent<DemonEnemy>(); if (e == null) { GameHUD.Message("Nie je to démon.", 3f); return; }

        if (menuMarks != null) menuMarks.Resolved = true;
        e.allied = true; Reputation.Add(e.clan, 10f); GameHUD.Message("Je teraz tvoj spojenec!", 4f);
        CloseMenu();
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (examActive && !MenuOpen) DrawProgress();
        if (!MenuOpen || menuNpc == null) return;

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
        }

        float s = Mathf.Clamp(Screen.height / 720f, 0.85f, 1.6f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, vh = Screen.height / s;

        float w = 580f, h = 410f;
        Rect box = new Rect((vw - w) / 2f, (vh - h) / 2f, w, h);

        GUI.color = new Color(0f, 0f, 0f, 0.88f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = box.x + 20f, y = box.y + 14f, tw = w - 40f;
        bool identified = menuMarks != null && menuMarks.Identified;
        bool demonTarget = IsDemonTarget(menuNpc);

        GUI.contentColor = identified ? new Color(1f, 0.4f, 0.35f) : new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(x, y, tw, 28f), identified ? "ODHALENÝ INFILTRÁTOR" : "NEODHALENÝ ČLOVEK", titleStyle);
        y += 34f;

        GUI.contentColor = Color.white;
        GUI.Label(new Rect(x, y, tw, 44f), $"Frakcia: {FactionData.NameOf(menuNpc.faction)}", textStyle);
        y += 30f;

        bool demon = stats.IsDemon;
        Color ok = Color.white, no = new Color(1f, 1f, 1f, 0.35f);

        GUI.contentColor = ok;
        GUI.Label(new Rect(x, y, tw, 24f), "[1] Poraziť potichu", textStyle); y += 30f;

        GUI.contentColor = demon ? no : ok;
        GUI.Label(new Rect(x, y, tw, 24f), "[2] Nahlásiť vojakom (nie ako démon)", textStyle); y += 30f;

        GUI.contentColor = ok;
        GUI.Label(new Rect(x, y, tw, 24f), "[3] Zničiť sám (verejne)", textStyle); y += 30f;

        GUI.contentColor = (demon && demonTarget) ? ok : no;
        GUI.Label(new Rect(x, y, tw, 24f), "[4] Kontaktovať (len démon)", textStyle); y += 30f;

        GUI.contentColor = demon ? ok : no;
        GUI.Label(new Rect(x, y, tw, 24f), "[5] Privolať posily (len démon; nižší/strední)", textStyle); y += 36f;

        GUI.contentColor = new Color(1f, 1f, 1f, 0.5f);
        GUI.Label(new Rect(x, y, tw, 24f), "[Esc] Zavrieť", textStyle);

        GUI.contentColor = Color.white;
        GUI.matrix = Matrix4x4.identity;
    }

    void DrawProgress()
    {
        float w = 260f, h = 18f;
        Rect bar = new Rect(Screen.width / 2f - w / 2f, Screen.height * 0.68f, w, h);
        float t = Mathf.Clamp01(hold / Mathf.Max(0.01f, examRequired));

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);

        GUI.color = new Color(0.95f, 0.8f, 0.3f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * t, bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(bar, $" Skúmam... {hold:0.0} / {examRequired:0.0} s");
        GUI.color = prev;
    }
}