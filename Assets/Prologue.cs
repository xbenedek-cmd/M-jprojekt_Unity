using System.Collections.Generic;
using UnityEngine;

public enum PrologueStep { Inactive, Talk, GoToWilds, Chop, Ambush, Death, Done }

/// <summary>
/// Hrateľný prológ „Noc, kedy zhasli neóny“ – hlavný quest rozdelený na menšie úlohy:
///  1. Drevorubači: hovor (G) s dvojicou drevorubačov, ktorí ti dajú sekeru.
///  2. Cesta do Divočiny: prejdi s nimi portálom do Divočiny.
///  3. Drevo pre hradby: vyrúb 4 stromy sekerou (okolo havarovaný konvoj s toxickým odpadom).
///  4. Súmrak: prepad neznámych bytostí – nevyhnutný, drevorubači aj ty zomriete.
///  5. Prebudenie: ako Hybrid v masovom hrobe; až tu sa začína samotná hra (Story.PrologueDone).
/// </summary>
public static class Prologue
{
    public static PrologueStep Step;

    public static bool Active => Step != PrologueStep.Inactive && Step != PrologueStep.Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { Step = PrologueStep.Inactive; }

    /// <summary>Prológ sa dá hrať len ak je v scéne Divočina (zem a portál); inak sa použije textový úvod.</summary>
    public static bool CanRun() => ZoneLoader.TryGetGround("Zem_Divocina", out _);

    public static void Begin()
    {
        if (Step != PrologueStep.Inactive) return;
        Step = PrologueStep.Talk;
        PrologueRunner.StartTalkStep();
    }

    /// <summary>Hráč dosiahol nulové zdravie: počas prepadu to spustí scénu smrti (hráč sa nezabije "naozaj", inak by sa hra skončila).</summary>
    public static bool TryIntercept()
    {
        if (Step == PrologueStep.Ambush) { PrologueRunner.BeginDeath(); return true; }
        return Step == PrologueStep.Death;
    }

    /// <summary>Spustí prológ odznova (ladenie / test z editora).</summary>
    public static void Restart()
    {
        Step = PrologueStep.Inactive;
        Story.PrologueDone = false;
        Story.Act = 0;
    }
}

public class PrologueRunner : MonoBehaviour
{
    private static PrologueRunner instance;

    private PlayerStats player;
    private PlayerMovement movement;
    private Inventory inventory;
    private DayNightCycle clock;
    private readonly List<QuestWorker> woodcutters = new List<QuestWorker>();
    private readonly List<VoidSpawn> predators = new List<VoidSpawn>();
    private readonly List<GameObject> props = new List<GameObject>();

    private int baseWood;
    private float chopStartedAt, ambushStartedAt, killWorkersAt = -1f;
    private bool ambushDialogDone, wreckLineShown, silenceShown;
    private Vector3 wreckPosition;
    private bool hasWreck;

    // Scéna smrti
    private float deathStart;
    private int deathStage;
    private float fade;
    private bool dialogWaiting;
    private string overlayText = "";
    private bool tint;
    private GUIStyle style, shadow, bigStyle;

    private static readonly string[] questNames =
    {
        "Drevorubači", "Cesta do Divočiny", "Drevo pre hradby", "Súmrak", "Prebudenie"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<PrologueRunner>() != null) return;
        instance = new GameObject("PrologueRunner").AddComponent<PrologueRunner>();
    }

    void Awake() => instance = this;

    public static void StartTalkStep() { if (instance != null) instance.SpawnWoodcutters(); }
    public static void BeginDeath() { if (instance != null) instance.StartDeath(); }

    bool Find()
    {
        if (player != null) return true;
        player = FindAnyObjectByType<PlayerStats>();
        if (player == null) return false;
        movement = player.GetComponent<PlayerMovement>();
        inventory = player.GetComponent<Inventory>();
        return true;
    }

    // ---------- 1. Drevorubači ----------
    void SpawnWoodcutters()
    {
        if (!Find()) return;
        GiveStarterKit();
        Vector3 forward = player.transform.forward;
        Vector3 right = player.transform.right;
        string[] names = { "Drevorubač Marek", "Drevorubač Tomáš" };
        Color[] shirts = { new Color(0.55f, 0.15f, 0.12f), new Color(0.2f, 0.35f, 0.55f) };

        woodcutters.Clear();
        for (int i = 0; i < 2; i++)
        {
            Vector3 pos = player.transform.position + forward * 5f + right * (i == 0 ? -1.8f : 1.8f) + Vector3.up * 0.4f;
            QuestWorker w = QuestWorker.Create(pos, 10 + i, names[i], shirts[i]);
            w.follow = false;
            w.transform.rotation = Quaternion.LookRotation(-forward);
            woodcutters.Add(w);
        }
        GameHUD.Message("Prológ: pri tebe stoja dvaja drevorubači. Podíď k nim a stlač G.", 6f);
    }

    private bool kitGiven;

    /// <summary>Začiatočná výbava: prázdny inventár nestačí na prežitie prológu (jedlo, lektvar); sekera sa pridá, kým ju hráč nemá.</summary>
    void GiveStarterKit()
    {
        if (kitGiven || inventory == null) return;
        kitGiven = true;
        bool empty = inventory.CountOf(ItemCatalog.Bread) + inventory.CountOf(ItemCatalog.Vegetables) + inventory.CountOf(ItemCatalog.HealthPotion) == 0;
        if (!empty) return;
        inventory.TryAdd(ItemCatalog.Bread, 2);
        inventory.TryAdd(ItemCatalog.Vegetables, 2);
        inventory.TryAdd(ItemCatalog.HealthPotion, 1);
    }

    /// <summary>Hráč sa dostal do Divočiny bez rozhovoru s drevorubačmi: sekera sa nesmie stratiť, inak nepôjde sekať stromy.</summary>
    void EnsureAxe()
    {
        if (ToolKit.Has(ToolKit.Kind.Axe)) return;
        ToolKit.Give(ToolKit.Kind.Axe, 0);
        if (!crewRunning) StartCrew();
        GameHUD.Message("Marek ti hodil záložnú sekeru. Ľavým tlačidlom do stromu.", 5f);
    }

    QuestWorker NearestWoodcutter(float range)
    {
        QuestWorker best = null;
        float bestSq = range * range;
        foreach (QuestWorker w in woodcutters)
        {
            if (w == null) continue;
            float sq = (w.transform.position - player.transform.position).sqrMagnitude;
            if (sq < bestSq) { bestSq = sq; best = w; }
        }
        return best;
    }

    // ---------- Drevorubači idú sami zbierať drevo ----------
    private class CrewJob
    {
        public bool inWilds, chopping;
        public BreakableNode tree;
        public float chopUntil, nextSearch;
        public int collected;
    }

    private readonly Dictionary<QuestWorker, CrewJob> jobs = new Dictionary<QuestWorker, CrewJob>();
    private bool crewRunning;

    // Po rozhovore drevorubači odídu cez portál do Divočiny, tam chodia od stromu k stromu a rúbu ich (aj nejaké pozbierajú), kým ich nezabijú démoni
    void StartCrew()
    {
        crewRunning = true;
        foreach (QuestWorker w in woodcutters) if (w != null) w.follow = false;
    }

    PortalGate FindWildsGate()
    {
        return player != null ? PortalGate.NearestTo("Divočina", player.transform.position) : null;
    }

    static bool Walk(QuestWorker w, Vector3 target, float speed, float stopDistance)
    {
        Vector3 to = target - w.transform.position;
        to.y = 0f;
        float d = to.magnitude;
        if (d <= stopDistance) return true;

        Vector3 dir = to / d;
        w.transform.position += dir * Mathf.Min(speed * Time.deltaTime, d - stopDistance * 0.5f);
        w.transform.rotation = Quaternion.Slerp(w.transform.rotation, Quaternion.LookRotation(dir), 8f * Time.deltaTime);
        return false;
    }

    void SendToWilds(QuestWorker w, CrewJob job, PortalGate gate)
    {
        job.inWilds = true;
        int index = woodcutters.IndexOf(w);
        float side = index == 0 ? -1.6f : 1.6f;
        Vector3 p;
        if (gate != null && gate.Destination != null)
            p = gate.Destination.transform.position + gate.Destination.transform.forward * 5f + gate.Destination.transform.right * side + Vector3.up * 1f;
        else p = player.transform.position + player.transform.forward * 8f + player.transform.right * side;
        p.y = GroundY(p, w.transform, p.y);   // na zem, nie 2 m nad ňu
        w.transform.position = p;
    }

    void UpdateCrew()
    {
        if (!crewRunning) return;
        PortalGate gate = FindWildsGate();

        foreach (QuestWorker w in woodcutters)
        {
            if (w == null) continue;
            if (!jobs.TryGetValue(w, out CrewJob job)) { job = new CrewJob(); jobs[w] = job; }

            if (!job.inWilds)
            {
                if (gate == null || Walk(w, gate.transform.position, 4.6f, 1.8f)) SendToWilds(w, job, gate);
                continue;
            }
            Gather(w, job);
        }
    }

    void UpdateCrewFreeze()
    {
        crewRunning = false;
        foreach (QuestWorker w in woodcutters)
        {
            if (w == null) continue;
            if (!jobs.TryGetValue(w, out CrewJob job)) { job = new CrewJob(); jobs[w] = job; }
            w.SetChopping(false);
            if (!job.inWilds) SendToWilds(w, job, null);
        }
    }

    BreakableNode FindTreeFor(QuestWorker w, CrewJob self)
    {
        BreakableNode best = null;
        float bestSq = 70f * 70f;
        Vector3 from = w.transform.position;
        foreach (BreakableNode n in BreakableNode.All)
        {
            if (n == null || !n.IsTree || n.IsDown || !n.gameObject.activeInHierarchy) continue;
            float sq = (n.transform.position - from).sqrMagnitude;
            if (sq >= bestSq) continue;

            bool taken = false;
            foreach (KeyValuePair<QuestWorker, CrewJob> other in jobs)
                if (other.Value != self && other.Value.tree == n) { taken = true; break; }
            if (taken) continue;
            bestSq = sq;
            best = n;
        }
        return best;
    }

    void Gather(QuestWorker w, CrewJob job)
    {
        if (job.tree != null && (job.tree.IsDown || !job.tree.gameObject.activeInHierarchy)) { job.tree = null; job.chopping = false; }
        if (job.tree == null)
        {
            if (Time.time < job.nextSearch) return;
            job.nextSearch = Time.time + 1f;
            job.tree = FindTreeFor(w, job);
            job.chopping = false;
            return;
        }

        Vector3 treePos = job.tree.transform.position;
        if (!job.chopping)
        {
            if (Walk(w, treePos, 4.2f, 1.7f)) { job.chopping = true; job.chopUntil = Time.time + Random.Range(6f, 9f); w.SetChopping(true); }
            return;
        }

        Vector3 look = treePos - w.transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f) w.transform.rotation = Quaternion.Slerp(w.transform.rotation, Quaternion.LookRotation(look), 6f * Time.deltaTime);
        if (Time.time < job.chopUntil) return;

        w.SetChopping(false);
        job.tree.AutoHarvest();   // strom padne, drevo si vezmú drevorubači (hráčovi sa nepripíše)
        job.tree = null;
        job.chopping = false;
        job.collected++;
    }

    // ---------- Beh ----------
    void Update()
    {
        if (!Prologue.Active || PauseMenu.AtStartup || !Find()) return;
        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();

        if (Prologue.Step == PrologueStep.GoToWilds || Prologue.Step == PrologueStep.Chop) UpdateCrew();

        switch (Prologue.Step)
        {
            case PrologueStep.Talk: UpdateTalk(); break;
            case PrologueStep.GoToWilds: UpdateGoToWilds(); break;
            case PrologueStep.Chop: UpdateChop(); break;
            case PrologueStep.Ambush: UpdateAmbush(); break;
            case PrologueStep.Death: UpdateDeath(); break;
        }
    }

    void UpdateTalk()
    {
        woodcutters.RemoveAll(w => w == null);
        if (woodcutters.Count == 0) SpawnWoodcutters();
        if (InWilds()) { Prologue.Step = PrologueStep.GoToWilds; return; }   // preskočený rozhovor: pokračuje sa už v Divočine

        QuestWorker near = NearestWoodcutter(3.8f);
        if (near == null || MangaDialog.Active) return;

        GameHUD.Prompt($"[{MerchantNPC.TradeKey}] Hovoriť s drevorubačmi");
        if (!GameInput.GetKeyDown(MerchantNPC.TradeKey)) return;

        Prologue.Step = PrologueStep.GoToWilds;   // hneď, aby sa dialóg nespustil dvakrát
        MangaDialog.Show("Drevorubač Marek", new[]
        {
            "Hej, dôstojník! Konečne niekto, kto sa nebojí Divočiny. Hradby potrebujú drevo a z našej smeny neprišiel nikto.",
            "Pôjdeme len my dvaja a ty. Za hodinu máme naložený voz a večer sme späť pri ohni.",
            "Tu máš moju záložnú sekeru. Bez nej tam nič nespravíš – ľavým tlačidlom do stromu, kým nepadne.",
            "Stretneme sa na druhej strane portálu do Divočiny. Choď, my ťa dobehneme."
        }, GiveAxe);
    }

    void GiveAxe()
    {
        ToolKit.Give(ToolKit.Kind.Axe, 0);
        StartCrew();
        GameHUD.Message("Dostal si sekeru. Drevorubači už vyrazili zbierať drevo – choď za nimi cez portál do Divočiny.", 6f);
    }

    void UpdateGoToWilds()
    {
        if (!InWilds()) return;

        Prologue.Step = PrologueStep.Chop;
        EnsureAxe();
        chopStartedAt = Time.time;
        baseWood = inventory != null ? inventory.CountOf(ItemCatalog.Wood) : 0;
        if (!hasWreck) SpawnWreck();
        MangaDialog.Whisper("Drevorubač Tomáš", "Je tu nejako ticho... Pozri, tamto asi havaroval konvoj. A tá zelená brečka z barelov sa mi nepáči.", 8f);
    }

    bool InWilds()
    {
        if (!ZoneLoader.TryGetGround("Zem_Divocina", out Bounds b)) return false;
        Vector3 p = player.transform.position;
        return Mathf.Abs(p.x - b.center.x) <= b.extents.x && Mathf.Abs(p.z - b.center.z) <= b.extents.z && p.y > -100f;
    }

    // Havarovaný konvoj: nákladiak na boku a svietiace barely s toxickým odpadom (odhalenie pravdy o korporácii)
    void SpawnWreck()
    {
        hasWreck = true;
        Vector3 p = player.transform.position + player.transform.forward * 22f;
        if (Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 160f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
        wreckPosition = p;

        Material metal = new Material(Shader.Find("Standard")) { color = new Color(0.28f, 0.3f, 0.34f) };
        Material cab = new Material(Shader.Find("Standard")) { color = new Color(0.2f, 0.22f, 0.26f) };
        Material waste = new Material(Shader.Find("Standard")) { color = new Color(0.3f, 0.9f, 0.3f) };
        waste.EnableKeyword("_EMISSION");
        waste.SetColor("_EmissionColor", new Color(0.2f, 0.9f, 0.2f) * 1.6f);

        var root = new GameObject("HavarovanyKonvoj");
        root.transform.position = p;
        root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        props.Add(root);

        void Piece(PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Material m, bool collider)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Destroy(g.GetComponent<Collider>());
        }
        BuildTruck(root.transform, metal, cab, waste);
        for (int i = 0; i < 8; i++)
            Piece(PrimitiveType.Cylinder, new Vector3(Random.Range(-4f, 4f), 0.5f, Random.Range(-5f, 3f)), new Vector3(0.9f, 0.55f, 0.9f),
                  Quaternion.Euler(Random.Range(-20f, 20f), 0f, Random.Range(-70f, 70f)), waste, i < 2);

        var glow = new GameObject("ToxickeSvetlo");
        glow.transform.SetParent(root.transform, false);
        glow.transform.localPosition = new Vector3(0f, 1.6f, -1f);
        var light = glow.AddComponent<Light>();
        light.color = new Color(0.3f, 1f, 0.35f);
        light.range = 14f;
        light.intensity = 1.5f;
    }

    // Prevrátený cisternový kamión na boku: rám, kabína s oknami a svetlami, kapota, mriežka, nárazník, výfuk, cisterna s pásmi a poklopmi, kolesá s diskami
    static Material Mat(Color c, float metallic = 0.4f, float smooth = 0.35f, Color? emission = null)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smooth);
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); }
        return m;
    }

    void BuildTruck(Transform root, Material metal, Material cabMat, Material waste)
    {
        Material rubber = Mat(new Color(0.06f, 0.06f, 0.07f), 0f, 0.15f);
        Material rim = Mat(new Color(0.55f, 0.57f, 0.6f), 0.8f, 0.5f);
        Material glass = Mat(new Color(0.12f, 0.2f, 0.26f), 0.1f, 0.9f);
        Material frame = Mat(new Color(0.1f, 0.1f, 0.11f), 0.5f, 0.2f);
        Material hazard = Mat(new Color(0.95f, 0.75f, 0.05f), 0.1f, 0.3f);
        Material lamp = Mat(new Color(0.9f, 0.9f, 0.7f), 0f, 0.8f, new Color(1f, 0.95f, 0.6f) * 0.5f);
        Material rust = Mat(new Color(0.35f, 0.18f, 0.1f), 0.3f, 0.1f);
        Material tankMat = Mat(new Color(0.62f, 0.64f, 0.66f), 0.7f, 0.45f);

        // Kamión leží na pravom boku (os Z okolo pozdĺžnej osi), mierne naklonený na zosunutej kope
        var truck = new GameObject("Kamion").transform;
        truck.SetParent(root, false);
        truck.localPosition = new Vector3(-1.1f, 1.75f, 0f);
        truck.localRotation = Quaternion.Euler(0f, 0f, 84f) * Quaternion.Euler(0f, 4f, 0f);

        void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Material m, bool collider = false)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.transform.SetParent(truck, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Destroy(g.GetComponent<Collider>());
        }

        // Rám a podvozok
        Part(PrimitiveType.Cube, new Vector3(0f, 0.95f, 0.2f), new Vector3(1.7f, 0.28f, 9.4f), Vector3.zero, frame, true);
        Part(PrimitiveType.Cube, new Vector3(0f, 0.65f, -0.8f), new Vector3(1.2f, 0.3f, 4.2f), Vector3.zero, frame);   // hnacia sústava
        // Kabína
        Part(PrimitiveType.Cube, new Vector3(0f, 2.15f, 3.35f), new Vector3(2.4f, 1.9f, 2.1f), Vector3.zero, cabMat, true);
        Part(PrimitiveType.Cube, new Vector3(0f, 2.15f, 2.3f), new Vector3(2.46f, 1.5f, 0.1f), Vector3.zero, frame);      // zadná stena kabíny
        Part(PrimitiveType.Cube, new Vector3(0f, 2.45f, 4.43f), new Vector3(2.1f, 0.85f, 0.06f), new Vector3(-12f, 0f, 0f), glass);   // čelné sklo
        Part(PrimitiveType.Cube, new Vector3(1.22f, 2.4f, 3.5f), new Vector3(0.06f, 0.8f, 1.1f), Vector3.zero, glass);       // bočné okná
        Part(PrimitiveType.Cube, new Vector3(-1.22f, 2.4f, 3.5f), new Vector3(0.06f, 0.8f, 1.1f), Vector3.zero, glass);
        Part(PrimitiveType.Cube, new Vector3(0f, 3.18f, 3.4f), new Vector3(2.0f, 0.25f, 1.6f), Vector3.zero, metal);       // strešný spojler
        // Kapota, mriežka, nárazník, svetlá
        Part(PrimitiveType.Cube, new Vector3(0f, 1.65f, 4.9f), new Vector3(2.2f, 1.0f, 1.2f), Vector3.zero, cabMat, true);
        Part(PrimitiveType.Cube, new Vector3(0f, 1.6f, 5.53f), new Vector3(1.7f, 0.8f, 0.08f), Vector3.zero, frame);        // mriežka chladiča
        Part(PrimitiveType.Cube, new Vector3(0f, 1.0f, 5.62f), new Vector3(2.5f, 0.32f, 0.3f), Vector3.zero, rust);         // nárazník
        Part(PrimitiveType.Sphere, new Vector3(0.8f, 1.95f, 5.5f), new Vector3(0.42f, 0.34f, 0.2f), Vector3.zero, lamp);
        Part(PrimitiveType.Sphere, new Vector3(-0.8f, 1.95f, 5.5f), new Vector3(0.42f, 0.34f, 0.2f), Vector3.zero, lamp);
        // Zvislý výfuk a zrkadlá
        Part(PrimitiveType.Cylinder, new Vector3(1.35f, 2.9f, 2.5f), new Vector3(0.18f, 1.1f, 0.18f), Vector3.zero, metal);
        Part(PrimitiveType.Cube, new Vector3(1.45f, 2.6f, 4.3f), new Vector3(0.08f, 0.5f, 0.3f), Vector3.zero, frame);
        // Cisterna: valec naležato, hrdzavé pásy, poklopy, zadný dnový uzáver
        Part(PrimitiveType.Cylinder, new Vector3(0f, 2.15f, -1.9f), new Vector3(2.3f, 3.3f, 2.3f), new Vector3(90f, 0f, 0f), tankMat, true);
        for (int i = 0; i < 4; i++)
            Part(PrimitiveType.Cylinder, new Vector3(0f, 2.15f, -4.3f + i * 1.55f), new Vector3(2.36f, 0.07f, 2.36f), new Vector3(90f, 0f, 0f), i % 2 == 0 ? hazard : rust);
        Part(PrimitiveType.Cylinder, new Vector3(0f, 3.4f, -1.2f), new Vector3(0.7f, 0.12f, 0.7f), Vector3.zero, frame);     // poklop
        Part(PrimitiveType.Cylinder, new Vector3(0f, 3.4f, -3.1f), new Vector3(0.7f, 0.12f, 0.7f), Vector3.zero, frame);
        Part(PrimitiveType.Sphere, new Vector3(0f, 2.15f, -5.1f), new Vector3(2.3f, 1.1f, 2.3f), Vector3.zero, tankMat);
        Part(PrimitiveType.Cube, new Vector3(0f, 3.5f, -1.9f), new Vector3(0.15f, 0.1f, 4.6f), Vector3.zero, frame);        // zábradlie
        // Trhlina v cisterne: zelený odpad vytekajúci na bok
        Part(PrimitiveType.Sphere, new Vector3(1.05f, 1.9f, -2.2f), new Vector3(0.9f, 1.3f, 0.8f), Vector3.zero, waste);
        // Kolesá: 2 nápravy kabíny + 2 tandemové nápravy cisterny, každá s dvojicou kolies
        float[] axles = { 3.9f, -0.5f, -2.1f, -3.7f };
        foreach (float z in axles)
            foreach (float side in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Cylinder, new Vector3(side * 1.05f, 0.62f, z), new Vector3(1.25f, 0.2f, 1.25f), new Vector3(0f, 0f, 90f), rubber);
                Part(PrimitiveType.Cylinder, new Vector3(side * 1.18f, 0.62f, z), new Vector3(0.55f, 0.2f, 0.55f), new Vector3(0f, 0f, 90f), rim);
            }
        // Palivová nádrž a blatník
        Part(PrimitiveType.Cylinder, new Vector3(1.05f, 1.2f, 1.1f), new Vector3(0.55f, 0.8f, 0.55f), new Vector3(90f, 0f, 0f), metal);
        Part(PrimitiveType.Cube, new Vector3(1.0f, 1.2f, -0.5f), new Vector3(0.08f, 0.6f, 1.6f), Vector3.zero, rust);
    }

    // ---------- 3. Drevo ----------
    void UpdateChop()
    {
        int wood = inventory != null ? Mathf.Max(0, inventory.CountOf(ItemCatalog.Wood) - baseWood) : 0;

        if (!wreckLineShown && hasWreck && (player.transform.position - wreckPosition).sqrMagnitude < 14f * 14f)
        {
            wreckLineShown = true;
            MangaDialog.Show("Zistenie", new[]
            {
                "V troskách konvoja leží pod prevrátenými barelmi niečo, čo nevyzerá ako odpad. Sú to ľudské telá v korporátnych vakoch.",
                "Megakorporácia v lesoch neukladá len toxický odpad. Ukladá tam aj ľudí, ktorí jej prekážali."
            });
        }
        if (!silenceShown && wood >= 1)
        {
            silenceShown = true;
            MangaDialog.Whisper("Drevorubač Marek", "Počuješ to? Žiadne vtáky. Stromy sú tu akési... čierne zvnútra.", 7f);
        }

        // Hráč verí, že treba 4 kusy dreva; prepad príde skôr (po druhom) a nemá šancu ho zastaviť
        // Prvý zrúbaný strom / zobrané drevo hráčom privolá bytosti
        if (wood >= 1 || Time.time - chopStartedAt > 200f) BeginAmbush();
    }

    // ---------- 4. Prepad ----------
    void BeginAmbush()
    {
        Prologue.Step = PrologueStep.Ambush;
        ambushStartedAt = Time.time;
        ambushDialogDone = false;
        tint = true;

        // Drevorubači prestanú rúbať; tí, čo ešte neprešli portálom, sa objavia pri hráčovi, aby zomreli v scéne
        UpdateCrewFreeze();
        if (clock != null) clock.SetClock(clock.Day, 18.7f);
        GameAudio.Play(GameAudio.Sfx.Lightning, player.transform.position);
        MangaDialog.Show("Súmrak", new[]
        {
            "Nebo mení farbu. Z útesov sa ozve škrípanie, aké si nikdy nepočul – ako plač kovu zmiešaný s hlbokým chrapotom.",
            "Nie sú to vlci ani medvede. Z tieňa vybehnú obrovské bytosti z dymu a kostí. Prvá vlna beštií zrodená z toxického odpadu.",
            "Drevorubači kričia. Niet kam utiecť."
        }, () =>
        {
            ambushDialogDone = true;
            SpawnPredators();
            killWorkersAt = Time.time + 3f;
        });
    }

    void SpawnPredators()
    {
        Vector3 p = player.transform.position;
        for (int i = 0; i < 3; i++)
        {
            Vector2 ring = Random.insideUnitCircle.normalized * 26f;
            Vector3 spawnAt = p + new Vector3(ring.x, 1f, ring.y);
            VoidSpawn v = VoidSpawn.Spawn(spawnAt, true);
            if (v == null) continue;
            v.transform.position = new Vector3(spawnAt.x, GroundY(spawnAt, v.transform, p.y), spawnAt.z);

            // Scénar: bytosť sa nedá odhaliť ani zastaviť. Riadi ju prológ (nie bežná AI s maskovaním, úhybmi a QTE), aby vždy dobehla a zabila.
            NPCPerception perception = v.GetComponent<NPCPerception>();
            if (perception != null) perception.Reveal();
            // Všetko, čo by mohlo hýbať telom (AI, uzemnenie, vyhýbanie, túlanie), sa vypne: pohyb riadi výlučne prológ
            // DemonEnemy ostáva zapnutý (animátor z neho číta útok), len jeho AI sa preskakuje
            DemonEnemy ai = v.GetComponent<DemonEnemy>();
            if (ai != null) ai.Scripted = true;
            foreach (MonoBehaviour b in v.GetComponents<MonoBehaviour>())
                if (b is NPCGrounding || b is NPCWander || b is BossPost) b.enabled = false;
            Rigidbody body = v.GetComponent<Rigidbody>();
            if (body != null) { body.isKinematic = true; body.detectCollisions = false; body.interpolation = RigidbodyInterpolation.None; }   // interpolácia by prepisovala polohu z prológu a bytosť by sa chvela
            StartCoroutine(UseChaseClips(v));
            predators.Add(v);
        }
    }

    // Rútiaca sa bytosť nesmie bežať klipom chôdze (pri 12 m/s sa jej nohy trhali): dostane šprintový klip hráča (rovnaká kostra mixamorig)
    System.Collections.IEnumerator UseChaseClips(VoidSpawn v)
    {
        yield return null;
        yield return null;   // model a animátor sa postavia v Awake/Start klonu
        if (v == null || player == null) yield break;

        NPCPerception perception = v.GetComponent<NPCPerception>();
        DemonModelAnimator anim = perception != null && perception.monsterVisual != null ? perception.monsterVisual.GetComponent<DemonModelAnimator>() : v.GetComponentInChildren<DemonModelAnimator>(true);
        PlayerVisual pv = player.GetComponent<PlayerVisual>();
        DemonAnimationSet playerSet = pv != null ? pv.AnimationSet : null;
        if (anim == null || anim.CurrentSet == null || playerSet == null) yield break;

        AnimationClip fast = playerSet.sprint != null ? playerSet.sprint : playerSet.run;
        if (fast == null) yield break;

        DemonAnimationSet chase = Instantiate(anim.CurrentSet);
        chase.run = fast;
        chase.runReferenceSpeed = playerSet.sprint != null ? playerSet.sprintReferenceSpeed : playerSet.runReferenceSpeed;
        anim.SetAnimationSet(chase);
    }

    const float ChargeSpeed = 12f, KillDistance = 2.2f;
    private float strikeAt = -1f;

    // Predátori sa rútia na hráča; prvý, čo ho dobehne, ho zabije jedným úderom
    void ChargePredators()
    {
        Vector3 target = player.transform.position;
        foreach (VoidSpawn v in predators)
        {
            if (v == null) continue;
            Vector3 position = v.transform.position;
            Vector3 to = target - position;
            to.y = 0f;
            float distance = to.magnitude;

            if (distance <= KillDistance)
            {
                // Najprv sa prehrá animácia útoku, úder dopadne až po zamáchnutí
                DemonEnemy striker = v.GetComponent<DemonEnemy>();
                if (strikeAt < 0f)
                {
                    strikeAt = Time.time + 0.6f;
                    if (striker != null) striker.ScriptedAttack = true;
                }
                if (distance > 0.001f) v.transform.rotation = Quaternion.LookRotation(to / distance);
                if (Time.time < strikeAt) continue;

                if (striker != null) striker.ScriptedAttack = false;
                GameHUD.MangaPanel("ŠKRÁÁT!", 0.6f);
                StartDeath();
                return;
            }

            Vector3 dir = distance > 0.001f ? to / distance : v.transform.forward;
            position += dir * ChargeSpeed * Time.deltaTime;
            float targetY = GroundY(position, v.transform, v.transform.position.y, 2f, 1f);
            position.y = Mathf.Lerp(v.transform.position.y, targetY, 1f - Mathf.Exp(-7f * Time.deltaTime));   // vyhladená výška, bez cukania
            Quaternion turn = Quaternion.Slerp(v.transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-10f * Time.deltaTime));
            v.transform.SetPositionAndRotation(position, turn);
        }
    }

    private static readonly RaycastHit[] groundHits = new RaycastHit[16];

    // Zem pod bytosťou bez vlastného tela a hráča (lúč z výšky by inak trafil jej kolízie a bytosť by sa v každom snímku dvíhala vyššie)
    float GroundY(Vector3 at, Transform self, float fallback, float lift = 40f, float maxAbove = 3f)
    {
        int count = Physics.RaycastNonAlloc(at + Vector3.up * lift, Vector3.down, groundHits, lift + 50f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            Transform t = groundHits[i].transform;
            if (t.IsChildOf(self) || t.IsChildOf(player.transform) || groundHits[i].normal.y < 0.55f) continue;
            if (t.GetComponentInParent<BreakableNode>() != null || t.GetComponentInParent<Damageable>() != null) continue;   // kmene, kamene a postavy nie sú zem (výška by skákala)
            if (groundHits[i].point.y > best && groundHits[i].point.y < at.y + maxAbove) best = groundHits[i].point.y;
        }
        if (float.IsNegativeInfinity(best)) return fallback;

        // Koreň postavy nie je pri chodidlách: výška sa počíta tak, aby spodok kapsuly (kolízie) sedel na zemi, rovnako ako NPCGrounding
        Collider body = self.GetComponent<Collider>();
        if (body == null) body = self.GetComponentInChildren<Collider>();
        float bottomOffset = -0.05f;
        if (body is CapsuleCollider cap) bottomOffset = (cap.center.y - Mathf.Max(cap.height * 0.5f, cap.radius)) * body.transform.lossyScale.y + (body.transform.position.y - self.position.y);   // z lokálnych rozmerov: world bounds sa pri vypnutých kolíziách neaktualizujú
        else if (body != null) bottomOffset = self.InverseTransformPoint(body.bounds.min).y;
        return best - bottomOffset;
    }

    void UpdateAmbush()
    {
        if (!ambushDialogDone) return;

        ChargePredators();
        if (Prologue.Step != PrologueStep.Ambush) return;

        if (killWorkersAt > 0f && Time.time >= killWorkersAt)
        {
            killWorkersAt = -1f;
            foreach (QuestWorker w in woodcutters) if (w != null) w.Kill();
            GameHUD.MangaPanel("KRRR!", 0.6f);
            GameHUD.Message("Drevorubačov zabili. Bež... alebo bojuj. Ničomu to nepomôže.", 6f);
        }

        // Scenár vyžaduje smrť: ak hráč prežíva príliš dlho (uteká), prepad sa dokončí sám
        if (Time.time - ambushStartedAt > 45f) StartDeath();
    }

    // ---------- Smrť a prebudenie ----------
    void StartDeath()
    {
        if (Prologue.Step == PrologueStep.Death) return;
        Prologue.Step = PrologueStep.Death;
        deathStart = Time.unscaledTime;
        deathStage = 0;
        dialogWaiting = false;
        player.InvulnerableUntil = Time.time + 180f;
        player.SetHealthScripted(0f);
        strikeAt = -1f;
        if (movement != null) movement.LockMovement(30f);
        GameAudio.Play(GameAudio.Sfx.Hurt, player.transform.position);
        BeginFall();
    }

    // ---------- Pád pri smrti a vstávanie ----------
    // Smrť hráča: prehrá sa skutočný klip pádu (Falling Forward/Back Death). Ak by model klip nemal, padá procedurálne:
    // otočí sa naznak okolo svojej osi a spodok tela sa položí na zem.
    private bool clipDeath;
    private Transform deathVisual;
    private DemonModelAnimator deathAnimator;
    private Renderer[] deathRenderers;
    private Collider deathBodyCollider;
    private Quaternion deathBaseRot;
    private Vector3 deathBasePos;
    private float fallStart;
    private bool fallen, woke;

    void BeginFall()
    {
        PlayerVisual pv = player.GetComponent<PlayerVisual>();
        deathVisual = pv != null ? pv.VisualRoot : null;
        deathAnimator = pv != null ? pv.ModelAnimator : null;
        if (deathVisual == null) return;

        clipDeath = pv.TryPlayDeath();
        if (clipDeath)
        {
            fallStart = Time.unscaledTime;
            fallen = false;   // procedurálny pád sa nepoužije
            woke = false;
            return;
        }

        deathBaseRot = deathVisual.localRotation;
        deathBasePos = deathVisual.localPosition;
        deathRenderers = deathVisual.GetComponentsInChildren<Renderer>();
        deathBodyCollider = player.GetComponent<Collider>();
        fallStart = Time.unscaledTime;
        fallen = true;
        woke = false;
        if (deathAnimator != null) deathAnimator.ForceIdle();   // nohy nesmú pri páde chodiť
        if (deathAnimator != null) deathAnimator.enabled = false;   // jeho zarovnanie výšky by prepisovalo ležiacu polohu
    }

    void UpdateFall()
    {
        if (!fallen || deathVisual == null) return;

        float k = Mathf.Clamp01((Time.unscaledTime - fallStart) / 0.65f);
        float ease = 1f - (1f - k) * (1f - k) * (1f - k);
        deathVisual.localRotation = deathBaseRot * Quaternion.Euler(-88f * ease, 0f, 0f);
        deathVisual.localPosition = deathBasePos;

        // Spodok tela na zem (vrstva kapsuly hráča)
        if (deathRenderers != null && deathRenderers.Length > 0 && deathBodyCollider != null)
        {
            float lowest = float.MaxValue;
            foreach (Renderer r in deathRenderers) if (r != null) lowest = Mathf.Min(lowest, r.bounds.min.y);
            if (lowest < float.MaxValue) deathVisual.position += Vector3.up * (deathBodyCollider.bounds.min.y - lowest + 0.04f);
        }
    }

    void StandUp()
    {
        fallen = false;
        if (clipDeath)
        {
            clipDeath = false;
            if (deathAnimator != null) deathAnimator.Revive();   // zruší stav smrti, vstáva klip Standing Up
        }
        else if (deathVisual != null)
        {
            deathVisual.localRotation = deathBaseRot;
            deathVisual.localPosition = deathBasePos;
        }
        if (deathAnimator != null)
        {
            deathAnimator.enabled = true;
            float length = deathAnimator.PlayOneShot(DemonModelAnimator.OneShot.StandUp);
            if (length > 0f && movement != null) movement.LockMovement(Mathf.Min(length, 2.5f));   // vstáva z hrobu, kým sa nedohrá klip
        }
    }

    void UpdateDeath()
    {
        float t = Time.unscaledTime - deathStart;
        if (deathStage < 3) { player.SetHealthScripted(0f); UpdateFall(); }   // regenerácia by pruh zdravia vrátila späť; telo leží

        if (deathStage == 0)
        {
            fade = Mathf.Clamp01(t / 2.5f);
            overlayText = fade > 0.6f ? "ZOMREL SI" : "";
            if (t >= 3f) { deathStage = 1; MoveToGrave(); }
        }
        else if (deathStage == 1)
        {
            if (t >= 5.2f)
            {
                deathStage = 2;
                dialogWaiting = true;
                overlayText = "";
                MangaDialog.Show("Prológ", new[]
                {
                    "Vyšší Démon ťa zasiahne ako prvého. Posledné, čo vidíš, sú čierne pazúry a dym, ktorý pohlcuje svetlo.",
                    "Ale nezomrieš. Tvoje telo pohltí duchovný odpad a toxická mágia. Prebúdzaš sa v masovom hrobe medzi telami. Je noc.",
                    "Svet sa obrátil: ľudia sú pre teba ohavné monštrá a démoni sa ti zdajú ľudskí. Si jediný Hybrid.",
                    "Dnes sa to skončilo. Zajtra sa začína tvoj dvojitý život."
                }, () => { dialogWaiting = false; deathStage = 3; deathStart = Time.unscaledTime; });
            }
        }
        else if (deathStage == 3)
        {
            if (!woke)
            {
                woke = true;
                if (movement != null) movement.UnlockMovement();   // zámok pohybu z momentu smrti (30 s) by držal hráča v hrobe
                StandUp();
            }
            fade = 1f - Mathf.Clamp01(t / 2f);
            if (t >= 2f) FinishPrologue();
        }
    }

    void MoveToGrave()
    {
        foreach (VoidSpawn v in predators) if (v != null) Destroy(v.gameObject);
        predators.Clear();
        foreach (QuestWorker w in woodcutters) if (w != null) Destroy(w.gameObject);
        woodcutters.Clear();
        jobs.Clear();
        tint = false;
        if (clock != null) clock.SetClock(clock.Day, 22.5f);

        // Masový hrob: 55–75 m od miesta prepadu, v hraniciach Divočiny
        Vector3 origin = player.transform.position;
        Vector3 grave = origin;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(55f, 75f);
            Vector3 candidate = origin + new Vector3(ring.x, 0f, ring.y);
            if (ZoneLoader.TryGetGround("Zem_Divocina", out Bounds b)
                && (Mathf.Abs(candidate.x - b.center.x) > b.extents.x - 20f || Mathf.Abs(candidate.z - b.center.z) > b.extents.z - 20f)) continue;
            grave = candidate;
            break;
        }
        if (Physics.Raycast(grave + Vector3.up * 80f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)) grave = hit.point;
        BuildGrave(grave);

        if (movement != null) movement.Teleport(grave + new Vector3(2.2f, 1.2f, 0f), Vector3.forward);
        // Hráč ostáva človekom (inak by nemohol používať portály); premena príde neskôr v príbehu
        PerceptionToggle perception = FindAnyObjectByType<PerceptionToggle>();
        if (perception != null) perception.SetDemonVision(true);
    }

    // Mŕtvola sa nehýbe, preto sa skinovaná sieť zmrazí do obyčajnej: modely drevorubačov sú obrovské (desiatky MB) a GPU skinning ich počas celej scény
    // hlási "Thread group count is above the maximum allowed limit" (viac ako 65535 skupín vlákien)
    static void FreezeSkinned(Transform root)
    {
        foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr == null || smr.sharedMesh == null) continue;

            var baked = new Mesh { name = smr.sharedMesh.name + "_Zmrazena", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            smr.BakeMesh(baked, true);   // so zahrnutou mierkou

            var go = new GameObject(smr.name + "_Zmrazene");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = smr.sharedMaterials;
            var block = new MaterialPropertyBlock();
            smr.GetPropertyBlock(block);
            mr.SetPropertyBlock(block);
            Destroy(smr);
        }
    }

    void BuildGrave(Vector3 center)
    {
        var root = new GameObject("MasovyHrob");
        root.transform.position = center;
        props.Add(root);

        void Piece(PrimitiveType type, Vector3 pos, Vector3 scale, Quaternion rot, Color color, bool collider, bool glow = false)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            g.transform.SetParent(root.transform, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot;
            g.transform.localScale = scale;
            Material m = new Material(Shader.Find("Standard")) { color = color };
            if (glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * 1.5f); }
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Destroy(g.GetComponent<Collider>());
        }
        Piece(PrimitiveType.Sphere, new Vector3(0f, -0.5f, 0f), new Vector3(7f, 2f, 5f), Quaternion.identity, new Color(0.12f, 0.09f, 0.07f), false);
        for (int i = 0; i < 7; i++)
        {
            Vector3 at = new Vector3(Random.Range(-2.5f, 2.5f), 0.3f, Random.Range(-1.8f, 1.8f));
            float yaw = Random.Range(0f, 360f);

            // Mŕtvoly v hrobe sú drevorubači (modely z prológu), ležia v základnej póze; bez modelov kapsuly
            string path = "Characters/Drevorubac" + (i % 2 + 1);
            GameObject model = Resources.Load<GameObject>(path);
            if (model == null)
            {
                Piece(PrimitiveType.Capsule, at + Vector3.up * 0.15f, new Vector3(0.45f, 0.9f, 0.45f), Quaternion.Euler(90f, yaw, 0f), new Color(0.35f, 0.35f, 0.38f), false);
                continue;
            }

            var body = new GameObject("Mrtvola_" + i);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = at;
            QuestWorker.AttachModel(body.transform, model, path, new Color(0.45f, 0.4f, 0.38f), false);   // postaví sa vzpriamene, až potom ľahne
            // Všetky telá ležia na zemi (nikto nestojí ani sa neopiera o odpad); hlavou sa striedavo na obe strany
            body.transform.localPosition = at + Vector3.up * 0.2f;
            body.transform.localRotation = Quaternion.Euler(i % 2 == 0 ? 90f : -90f, yaw, 0f);
            FreezeSkinned(body.transform);
        }
        for (int i = 0; i < 4; i++)
            Piece(PrimitiveType.Cylinder, new Vector3(Random.Range(-4f, 4f), 0.4f, Random.Range(-3f, 3f)), new Vector3(0.7f, 0.45f, 0.7f),
                  Quaternion.Euler(Random.Range(-12f, 12f), 0f, Random.Range(-60f, 60f)), new Color(0.3f, 0.9f, 0.3f), i < 1, true);

        var glowObj = new GameObject("HrobSvetlo");
        glowObj.transform.SetParent(root.transform, false);
        glowObj.transform.localPosition = Vector3.up * 2f;
        var light = glowObj.AddComponent<Light>();
        light.color = new Color(0.35f, 1f, 0.45f);
        light.range = 16f;
        light.intensity = 1.2f;
    }

    void FinishPrologue()
    {
        fade = 0f;
        player.InvulnerableUntil = 0f;
        player.SetHealthScripted(player.MaxHealth * 0.5f);   // prebudenie s polovicou zdravia
        Prologue.Step = PrologueStep.Done;
        Story.PrologueDone = true;
        Story.Act = 1;
        GameHUD.Message("Prológ skončil. Si Hybrid. Vráť sa do mesta (portál späť), nájdi Inžinierku Kazumi a zisti, čo sa stalo. Cez deň buduj mesto, v noci počúvaj hlas lesa.", 10f);
    }

    // ---------- Zobrazenie ----------
    private readonly HudText goalText = new HudText();
    private static readonly string[] panelSteps = { "Drevorubači", "Cesta do Divočiny", "Drevo pre hradby", "???" };

    float DistanceToWildsGate()
    {
        PortalGate wilds = PortalGate.NearestTo("Divočina", player.transform.position);
        return wilds != null ? Vector3.Distance(player.transform.position, wilds.transform.position) : -1f;
    }

    string CurrentObjective()
    {
        switch (Prologue.Step)
        {
            case PrologueStep.Talk: return "Podíď k drevorubačom a stlač G.";
            case PrologueStep.GoToWilds:
            {
                float d = DistanceToWildsGate();
                return d >= 0f ? $"Choď cez portál do Divočiny ({Mathf.RoundToInt(d / 5f) * 5} m)." : "Choď cez portál do Divočiny.";
            }
            case PrologueStep.Chop:
            {
                int wood = inventory != null ? Mathf.Max(0, inventory.CountOf(ItemCatalog.Wood) - baseWood) : 0;
                return $"Vyrúb 4 stromy sekerou (ľavé tlačidlo): drevo {wood}/4.";
            }
            case PrologueStep.Ambush: return "Čo je to za zvuk?";
            default: return "";
        }
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup || player == null) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = true };
            style.normal.textColor = new Color(1f, 0.9f, 0.5f);
            shadow = new GUIStyle(style);
            shadow.normal.textColor = Color.black;
            bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }

        Color prev = GUI.color;
        if (tint)
        {
            GUI.color = new Color(0.55f, 0.08f, 0.38f, 0.2f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        if (Prologue.Active && Prologue.Step != PrologueStep.Death)
        {
            int step = Prologue.Step == PrologueStep.Talk ? 0 : Prologue.Step == PrologueStep.GoToWilds ? 1 : Prologue.Step == PrologueStep.Chop ? 2 : 3;
            int wood = Prologue.Step == PrologueStep.Chop && inventory != null ? Mathf.Max(0, inventory.CountOf(ItemCatalog.Wood) - baseWood) : 0;
            int dist = Prologue.Step == PrologueStep.GoToWilds ? Mathf.RoundToInt(DistanceToWildsGate() / 5f) : 0;   // text sa prepočíta len pri zmene
            if (goalText.Stale((int)Prologue.Step, wood, dist)) goalText.Text = CurrentObjective();
            GUI.color = Color.white;
            QuestPanel.Draw("Noc, kedy zhasli neóny", panelSteps, step, goalText.Text, Prologue.Step == PrologueStep.Chop ? Mathf.Clamp01(wood / 4f) : -1f);
        }

        if (fade > 0.001f)
        {
            int depth = GUI.depth;
            GUI.depth = 20;   // za dialógom a HUD (vyššie číslo = vzadu)
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            if (!string.IsNullOrEmpty(overlayText))
            {
                GUI.color = new Color(0.8f, 0.1f, 0.12f, fade);
                GUI.Label(new Rect(0f, Screen.height * 0.4f, Screen.width, 80f), overlayText, bigStyle);
            }
            GUI.depth = depth;
        }
        GUI.color = prev;
    }
}
