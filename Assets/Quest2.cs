using System;
using System.Globalization;
using UnityEngine;

public enum Quest2Step { Inactive, Night, Sneak, Gate, Forest, Meeting, Return, Morning, Done }

/// <summary>
/// Druhý hlavný quest „Šepot v Dymových Horách“ (Akt I). Nadväzuje na „Tiene v Hlbokom lese“ (MainQuest.State == Done): v prvú noc po ňom ťa Arakani
/// zavolá k Hranici popola. Fázy: Night (šepot) → Sneak (plížiť sa k južnej bráne = portál do Divočiny) → Gate (dvaja strážcovia: omráčiť / nakŕmiť sa) →
/// Forest (prejsť do Divočiny k dymovej silueta) → Meeting (obeta: uctiť / vzdorovať) → Return (domov pred úsvitom) → Morning (Veliteľ Ren: klamstvo / drzosť) → Done.
/// Stav je statický a ukladá sa cez Story (SaveData.q2).
/// </summary>
public static class Quest2
{
    public const string Title = "Šepot v Dymových Horách";

    public static Quest2Step Step;
    public static bool Spotted;            // v meste ťa zbadali
    public static bool Late;               // domov si sa vrátil až po úsvite
    public static bool HasModule;          // ukradnutý korporátny komunikačný modul
    public static bool PathDemon, PathHuman;   // rozhodnutie v lese: A = obeta (démon), B = vzdor (človek)
    public static bool ShadowStepUnlocked;     // odmena A: pasívny Tieniaci skok (kláves Z)
    public static bool KageContact;            // odmena: kontakt s informátorom Kagem
    public static bool VancSuspicious;         // následok: kapitán Vanc ťa podozrieva
    public static bool DebuffActive;           // vzdor: dočasné oslabenie do úsvitu nasledujúceho dňa
    public static int DebuffUntilDay;
    public static bool HasHome;
    public static Vector3 HomePos;

    public static bool Active => Step != Quest2Step.Inactive && Step != Quest2Step.Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        Step = Quest2Step.Inactive;
        Spotted = Late = HasModule = PathDemon = PathHuman = ShadowStepUnlocked = KageContact = VancSuspicious = DebuffActive = HasHome = false;
        DebuffUntilDay = 0;
        HomePos = Vector3.zero;
    }

    // ---------- Ukladanie (jeden reťazec, aby sa nemusela meniť štruktúra savu) ----------
    public static string Export()
    {
        int flags = (Spotted ? 1 : 0) | (Late ? 2 : 0) | (HasModule ? 4 : 0) | (PathDemon ? 8 : 0) | (PathHuman ? 16 : 0) | (ShadowStepUnlocked ? 32 : 0)
                  | (KageContact ? 64 : 0) | (VancSuspicious ? 128 : 0) | (DebuffActive ? 256 : 0) | (HasHome ? 512 : 0);
        return string.Join("|", new[]
        {
            ((int)Step).ToString(CultureInfo.InvariantCulture), flags.ToString(CultureInfo.InvariantCulture), DebuffUntilDay.ToString(CultureInfo.InvariantCulture),
            HomePos.x.ToString("R", CultureInfo.InvariantCulture), HomePos.y.ToString("R", CultureInfo.InvariantCulture), HomePos.z.ToString("R", CultureInfo.InvariantCulture)
        });
    }

    public static void Import(string text)
    {
        ResetState();
        if (string.IsNullOrEmpty(text)) return;

        string[] p = text.Split('|');
        if (p.Length < 6) return;
        try
        {
            Step = (Quest2Step)int.Parse(p[0], CultureInfo.InvariantCulture);
            int flags = int.Parse(p[1], CultureInfo.InvariantCulture);
            DebuffUntilDay = int.Parse(p[2], CultureInfo.InvariantCulture);
            HomePos = new Vector3(float.Parse(p[3], CultureInfo.InvariantCulture), float.Parse(p[4], CultureInfo.InvariantCulture), float.Parse(p[5], CultureInfo.InvariantCulture));
            Spotted = (flags & 1) != 0; Late = (flags & 2) != 0; HasModule = (flags & 4) != 0; PathDemon = (flags & 8) != 0; PathHuman = (flags & 16) != 0;
            ShadowStepUnlocked = (flags & 32) != 0; KageContact = (flags & 64) != 0; VancSuspicious = (flags & 128) != 0; DebuffActive = (flags & 256) != 0; HasHome = (flags & 512) != 0;
        }
        catch (FormatException) { ResetState(); }
    }

    public static string Objective(Quest2Runner runner)
    {
        switch (Step)
        {
            case Quest2Step.Inactive: return runner.InCityNow ? "Arakani ťa volá. Zostaň v meste, čoskoro sa ozve." : "Arakani ťa volá: vráť sa do mesta.";
            case Quest2Step.Night: return "Počúvaj Arakaniho...";
            case Quest2Step.Sneak: return runner.DistanceText("Plíž sa tmavými uličkami k južnej bráne (portál do Divočiny)", runner.GateDistance) + " Vyhni sa dronom a svetlu reflektorov.";
            case Quest2Step.Gate: return "Dvaja strážcovia pri bráne. Rozhodni sa.";
            case Quest2Step.Forest: return runner.InWildsNow ? runner.DistanceText("Choď k dymovej silueta na hranici lesa", runner.ProjectionDistance) : "Prejdi bránou do Divočiny.";
            case Quest2Step.Meeting: return "Arakani čaká na tvoju obetu.";
            case Quest2Step.Return: return runner.DistanceText(runner.TimeLeftText() + " Vráť sa do svojej izby", runner.HomeDistance);
            case Quest2Step.Morning: return "Veliteľ Ren ťa čaká.";
            default: return "";
        }
    }
}

/// <summary>Riadi priebeh Quest2 (prehliadač kroku, strážcovia, silueta, voľby s časovým tlakom).</summary>
public class Quest2Runner : MonoBehaviour
{
    private PlayerStats player;
    private DayNightCycle clock;
    private Inventory inventory;

    private PortalGate gate;
    private readonly QuestWorker[] guards = new QuestWorker[2];
    private GameObject projection, beacon;
    private Vector3 projectionPos;
    private float startSuspicion, stepEnteredAt;
    private bool debuffApplied;
    private readonly HudText goalText = new HudText();
    private static readonly string[] panelSteps = { "Nočný nepokoj", "Plíženie k bráne", "Hranica lesa", "Návrat pred úsvitom", "Ranná prehliadka" };

    // ---------- Voľba ----------
    private bool choiceOpen;
    private string choiceTitle, choiceText;
    private string[] choiceOptions;
    private Action<int> choicePicked;
    private float choiceEnds;                 // 0 = bez limitu (neškálovaný čas)
    private float choiceTotal, savedScale = 1f;
    private CursorLockMode savedLock;
    private bool savedVisible;
    private GUIStyle titleStyle, textStyle, buttonStyle, hintStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<Quest2Runner>() != null) return;
        new GameObject("Quest2Runner").AddComponent<Quest2Runner>();
    }

    // ---------- Pomocné čítania pre text cieľa ----------
    public bool InWildsNow => player != null && InWilds(player.transform.position);
    public Vector3 ProjectionPosition => projectionPos;
    public bool InCityNow
    {
        get
        {
            if (player == null) return false;
            PortalGate g = FindGate();
            bool outside = (g != null && Vector3.Distance(player.transform.position, g.transform.position) > 160f) || InWilds(player.transform.position);
            return !outside;
        }
    }
    public float GateDistance => gate != null && player != null ? Vector3.Distance(player.transform.position, gate.transform.position) : -1f;
    public float ProjectionDistance => projection != null && player != null ? Vector3.Distance(player.transform.position, projectionPos) : -1f;
    public float HomeDistance => player != null ? Vector3.Distance(player.transform.position, Quest2.HomePos) : -1f;

    public string DistanceText(string text, float meters) => meters >= 0f ? $"{text} ({Mathf.RoundToInt(meters / 5f) * 5} m)." : text + ".";

    public string TimeLeftText()
    {
        if (clock == null || !clock.IsNight) return "Úsvit nastal!";
        return "Skôr než vyjde slnko.";
    }

    static bool InWilds(Vector3 p)
    {
        if (!ZoneLoader.TryGetGround("Zem_Divocina", out Bounds b)) return false;
        return Mathf.Abs(p.x - b.center.x) <= b.extents.x && Mathf.Abs(p.z - b.center.z) <= b.extents.z && p.y > -100f;
    }

    bool Find()
    {
        if (player != null) return true;
        player = FindAnyObjectByType<PlayerStats>();
        if (player == null) return false;
        inventory = player.GetComponent<Inventory>();
        return true;
    }

    PortalGate FindGate()
    {
        if (gate != null) return gate;
        if (player != null) gate = PortalGate.NearestTo("Divočina", player.transform.position);
        return gate;
    }

    static Vector3 GroundAt(Vector3 p, Transform ignore)
    {
        RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * 40f, Vector3.down, 90f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (RaycastHit h in hits)
        {
            if (ignore != null && h.transform.IsChildOf(ignore)) continue;
            if (h.normal.y < 0.5f || h.point.y > p.y + 3f) continue;
            if (h.point.y > best) best = h.point.y;
        }
        if (!float.IsNegativeInfinity(best)) p.y = best;
        return p;
    }

    void SetStep(Quest2Step step)
    {
        Quest2.Step = step;
        stepEnteredAt = Time.time;
    }

    // ---------- Hlavná slučka ----------
    void Update()
    {
        if (PauseMenu.AtStartup || !Find()) return;
        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
        if (clock == null || player.IsDead) return;

#if UNITY_EDITOR
        // Test v editore: Shift+F10 spustí quest 2 hneď (nastaví podmienky: quest 1 hotový, noc)
        if (GameInput.GetKey(KeyCode.LeftShift) && GameInput.GetKeyDown(KeyCode.F10) && Quest2.Step == Quest2Step.Inactive)
        {
            Story.PrologueDone = true;
            MainQuest.State = QuestState.Done;
            if (!clock.IsNight) clock.SetClock(clock.Day, 23f);
            GameHUD.Message("[test] Quest 2 sa spustí.", 2f);
        }
#endif

        UpdateDebuff();
        if (choiceOpen) { UpdateChoice(); return; }
        if (MangaDialog.Active || Prologue.Active) return;

        switch (Quest2.Step)
        {
            case Quest2Step.Inactive: UpdateInactive(); break;
            case Quest2Step.Sneak: UpdateSneak(); break;
            case Quest2Step.Gate: UpdateGate(); break;
            case Quest2Step.Forest: UpdateForest(); break;
            case Quest2Step.Return: UpdateReturn(); break;
        }
        if (Quest2.Step == Quest2Step.Sneak || Quest2.Step == Quest2Step.Gate) CheckSpotted();
    }

    // ---------- 1. Nočný nepokoj ----------
    void UpdateInactive()
    {
        // Začína hneď po prvom queste (nečaká sa na noc: BeginQuest ju sám privolá)
        if (MainQuest.State != QuestState.Done || !Story.PrologueDone) return;
        PortalGate g = FindGate();
        bool outsideCity = (g != null && Vector3.Distance(player.transform.position, g.transform.position) > 160f) || InWilds(player.transform.position);
        if (outsideCity)
        {
            if (Time.time >= nextCityHint)
            {
                nextCityHint = Time.time + 45f;
                GameHUD.Message("Arakani ťa volá: vráť sa do mesta (nová úloha: " + Quest2.Title + ").", 5f);
            }
            return;
        }

        BeginQuest();
    }

    private float nextCityHint;

    void BeginQuest()
    {
        if (!clock.IsNight) clock.SetClock(clock.Day, 22.5f);   // misia je nočná: plíženie a návrat pred úsvitom
        SetStep(Quest2Step.Night);
        Quest2.HasHome = true;
        Quest2.HomePos = player.transform.position;   // „izba“: sem sa treba vrátiť pred úsvitom
        BuildBeacon();

        GameHUD.MangaPanel("BZZZ...", 0.5f);
        MangaDialog.Show("Arakani", new[]
        {
            "Hladina krvi ti klesá, dôstojník. Cítiš ten chlad v žilách? Človek, ktorý slúži strojom, je len mŕtvolou, ktorá o tom ešte nevie...",
            "Príď za mnou k Hranici popola. Dnes mi prinesieš dôkaz svojej lojality.",
            "Mesto spí. Hradby sa trasú po včerajšku a dróny počúvajú. Choď tieňmi k južnej bráne – a vráť sa skôr, než ťa Ren začne hľadať."
        }, () =>
        {
            startSuspicion = Reputation.Suspicion;
            SetStep(Quest2Step.Sneak);
            GameHUD.Message($"Úloha: {Quest2.Title}. Plíž sa k južnej bráne (portál do Divočiny). Zapamätaj si, kde je tvoja izba (modrý stĺp).", 8f);
        });
    }

    // Modrý stĺp svetla na mieste „izby“, aby bolo jasné, kam sa vrátiť
    void BuildBeacon()
    {
        if (beacon != null) Destroy(beacon);
        beacon = new GameObject("Quest2_Izba");
        beacon.transform.position = GroundAt(Quest2.HomePos, player.transform);

        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(pillar.GetComponent<Collider>());
        pillar.transform.SetParent(beacon.transform, false);
        pillar.transform.localPosition = Vector3.up * 4f;
        pillar.transform.localScale = new Vector3(0.14f, 4f, 0.14f);
        var m = new Material(Shader.Find("Standard")) { color = new Color(0.1f, 0.3f, 0.5f) };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(0.2f, 0.6f, 1f) * 1.6f);
        pillar.GetComponent<Renderer>().sharedMaterial = m;

        var lightGo = new GameObject("Svetlo");
        lightGo.transform.SetParent(beacon.transform, false);
        lightGo.transform.localPosition = Vector3.up * 2f;
        var l = lightGo.AddComponent<Light>();
        l.color = new Color(0.3f, 0.6f, 1f);
        l.range = 8f;
        l.intensity = 1.4f;
    }

    // ---------- 2. Plíženie ----------
    void UpdateSneak()
    {
        PortalGate g = FindGate();
        if (g == null)
        {
            // Bez brány by quest uviazol: preskočí sa rovno do lesa
            SetStep(Quest2Step.Forest);
            return;
        }
        if (Vector3.Distance(player.transform.position, g.transform.position) < 14f) EnterGate(g);
    }

    void CheckSpotted()
    {
        if (Quest2.Spotted || Reputation.Suspicion - startSuspicion < 10f) return;
        Quest2.Spotted = true;
        GameHUD.Message("Zbadali ťa! Mesto začne tušiť, že sa niečo deje. Ren sa ťa ráno bude pýtať ostrejšie.", 5f);
    }

    // ---------- 3. Brána a strážcovia ----------
    void EnterGate(PortalGate g)
    {
        SetStep(Quest2Step.Gate);
        for (int i = 0; i < guards.Length; i++)
        {
            Vector3 p = g.transform.position + g.transform.right * (i == 0 ? -2.4f : 2.4f) + g.transform.forward * -2f;
            p = GroundAt(p, null);
            QuestWorker w = QuestWorker.Create(p + Vector3.up * 0.1f, 20 + i, "Strážca", new Color(0.25f, 0.3f, 0.4f));
            w.follow = false;
            Vector3 look = player.transform.position - p;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) w.transform.rotation = Quaternion.LookRotation(look.normalized);
            guards[i] = w;
        }
        GameHUD.MangaPanel("SHHH!", 0.7f);
        ShowChoice("Strážcovia pri bráne",
                   "Dvaja strážcovia stoja pri bráne a ešte ťa nezbadali. Dron nad nimi práve odleteli ďalej. Máš pár sekúnd.",
                   new[]
                   {
                       "[1] Potichu ich omráčiť (zostaneš človekom)",
                       "[2] Nakŕmiť sa z nich (riskantné: bližšie k démonom, +podozrenie)"
                   }, PickGuards, 0f);
    }

    void PickGuards(int option)
    {
        foreach (QuestWorker w in guards) if (w != null) w.Kill();

        Quest2.HasModule = true;   // strážcovi vezmeš korporátny komunikačný modul (obeta pre Arakaniho)
        if (option == 0)
        {
            SoulBalance.Add(2f);
            GameHUD.Message("Strážcovia padli bez zvuku. Zobral si korporátny komunikačný modul. Zostávaš človekom.", 5f);
        }
        else
        {
            SoulBalance.Add(-4f);
            Reputation.AddSuspicion(8f);
            ArakaniFavor.AddFavor(3f);
            if (player.IsDemon) player.DrinkBlood(40f); else player.Eat(25f);
            GameHUD.Message("Nakŕmil si sa. Telo ožilo, ale zanechal si stopy. Zobral si aj korporátny komunikačný modul.", 5f);
        }
        SetStep(Quest2Step.Forest);
    }

    void UpdateGate()
    {
        // Voľba sa otvára hneď po vstupe do fázy; ak sa stav načítal uprostred, otvorí sa znova
        PortalGate g = FindGate();
        if (g == null) { SetStep(Quest2Step.Forest); return; }
        if (!choiceOpen && Time.time - stepEnteredAt > 0.3f && guards[0] == null && guards[1] == null) EnterGate(g);
    }

    // ---------- 4. Les a Arakani ----------
    void UpdateForest()
    {
        bool inWilds = InWilds(player.transform.position);
        if (inWilds && projection == null)
        {
            Vector3 fwd = player.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            projectionPos = GroundAt(player.transform.position + fwd.normalized * 30f, player.transform);
            BuildProjection(projectionPos);
            GameHUD.Message("Les stíchol. Pred tebou sa v tme vlní dymová silueta.", 5f);
        }
        if (projection == null) return;

        if (Vector3.Distance(player.transform.position, projectionPos) < 7f) BeginMeeting();
    }

    void BeginMeeting()
    {
        SetStep(Quest2Step.Meeting);
        GameHUD.MangaPanel("VOOOM...", 0.6f);
        MangaDialog.Show("Arakani", new[]
        {
            "Vidím ťa, hybrid... Okolité stromy sú čierne a mŕtve. To je darček tvojej Megakorporácie.",
            "Priniesol si to, čo som chcel?"
        }, () => ShowChoice("Obeta Arakanimu",
                            "Dymová silueta čaká. V ruke držíš ukradnutý korporátny komunikačný modul.",
                            new[]
                            {
                                "[1] Obetovať modul a uctiť Arakaniho (cesta démona, odmena: Tieniaci skok)",
                                "[2] Odmietnuť a vzdorovať (cesta človeka, riziko: oslabenie do úsvitu)"
                            }, PickOffering, 0f));
    }

    void PickOffering(int option)
    {
        if (option == 0)
        {
            Quest2.PathDemon = true;
            Quest2.HasModule = false;
            Quest2.ShadowStepUnlocked = true;
            SoulBalance.Add(-6f);
            ArakaniFavor.AddFavor(15f);
            MangaDialog.Show("Arakani", new[]
            {
                "Múdro, hybrid. Lojalita sa platí darmi. Prijmi moju odmenu: tieň ťa bude niesť.",
                "Naučil si sa Tieniaci skok. Stlač Z blízko cieľa a prenesieš sa mu za chrbát."
            }, FinishMeeting);
            GameHUD.Message("Odomkol si pasívnu schopnosť: Tieniaci skok (kláves Z).", 6f);
        }
        else
        {
            Quest2.PathHuman = true;
            SoulBalance.Add(6f);
            Reputation.Add(FactionId.Civilians, 1f);
            player.SetHealthScripted(player.Health * 0.7f);
            Quest2.DebuffActive = true;
            Quest2.DebuffUntilDay = clock.Day + 1;
            MangaDialog.Show("Arakani", new[]
            {
                "Ha! Ha ha ha... Taký malý, taký tvrdohlavý. Zaplatíš za svoju hrdosť.",
                "Na tele ti zanechávam svoju stopu. Pamätaj: raz sa vrátiš na kolenách."
            }, FinishMeeting);
            GameHUD.Message("Arakani zmizol v dyme a zanechal na tebe stopu: máš menej zdravia a prijímaš viac poškodenia do úsvitu nasledujúceho dňa.", 7f);
        }
    }

    void FinishMeeting()
    {
        if (projection != null) { Destroy(projection, 1.5f); projection = null; }
        SetStep(Quest2Step.Return);
        GameHUD.Message("Vráť sa do mesta a do izby (modrý stĺp) skôr, než vyjde slnko.", 7f);
    }

    // Dymová silueta: tmavé častice stúpajúce v stĺpe, dve žiariace oči, fialové svetlo
    void BuildProjection(Vector3 at)
    {
        projection = new GameObject("Quest2_Arakani");
        projection.transform.position = at;

        var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f;
                float a = Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();

        var smokeGo = new GameObject("Dym");
        smokeGo.transform.SetParent(projection.transform, false);
        smokeGo.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = smokeGo.AddComponent<ParticleSystem>();
        ps.Stop();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startColor = new Color(0.07f, 0.04f, 0.1f, 0.6f);
        main.maxParticles = 200;
        var emission = ps.emission;
        emission.rateOverTime = 28f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 10f;
        shape.radius = 0.55f;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = 0f; vel.z = 0f; vel.y = 0.85f;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.4f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.6f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var renderer = smokeGo.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader) { mainTexture = tex };
        renderer.material = mat;
        ps.Play();

        var emissive = new Material(Shader.Find("Standard")) { color = Color.black };
        emissive.EnableKeyword("_EMISSION");
        emissive.SetColor("_EmissionColor", new Color(1f, 0.1f, 0.12f) * 3f);
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(eye.GetComponent<Collider>());
            eye.transform.SetParent(projection.transform, false);
            eye.transform.localPosition = new Vector3(i * 0.22f, 3.0f, 0.15f);
            eye.transform.localScale = new Vector3(0.18f, 0.1f, 0.1f);
            eye.GetComponent<Renderer>().sharedMaterial = emissive;
        }

        var lightGo = new GameObject("Svetlo");
        lightGo.transform.SetParent(projection.transform, false);
        lightGo.transform.localPosition = Vector3.up * 2.4f;
        var l = lightGo.AddComponent<Light>();
        l.color = new Color(0.6f, 0.25f, 1f);
        l.range = 12f;
        l.intensity = 2.2f;
    }

    // ---------- 5. Návrat ----------
    void UpdateReturn()
    {
        if (!Quest2.Late && !clock.IsNight)
        {
            Quest2.Late = true;
            GameHUD.Message("Úsvit! Meškáš. Ren bude podozrievavý.", 5f);
        }

        if (InWilds(player.transform.position)) return;
        if (Vector3.Distance(player.transform.position, Quest2.HomePos) < 5f) BeginMorning();
    }

    // ---------- 6. Ranná prehliadka ----------
    void BeginMorning()
    {
        SetStep(Quest2Step.Morning);
        if (beacon != null) { Destroy(beacon); beacon = null; }

        string opener = Quest2.Late
            ? "Meškáš, dôstojník. Slnko už vyšlo. "
            : "";
        string extra = Quest2.Spotted
            ? "Hliadka hlási podozrivý pohyb v uličkách. "
            : "";

        MangaDialog.Show("Veliteľ Ren", new[]
        {
            opener + "Prehliadka. Stoj rovno.",
            extra + "Hovoria, že v noci boli pri južnej bráne zaznamenané anomálne výkyvy energie a strážcovia našli vybité batérie na dronoch.",
            "Kde si bol, dôstojník?"
        }, () => ShowChoice("Veliteľ Ren čaká na odpoveď",
                            "Ren sa pozerá priamo na teba. Máš pár sekúnd, kým stratí trpezlivosť.",
                            new[]
                            {
                                "[1] Klamstvo: robil som nočnú obchôdzku v odľahlom sektore kvôli bezpečnosti",
                                "[2] Drzosť: tá brána je v zlom stave a vaše dróny sú nespoľahlivé"
                            }, PickMorning, 10f));
    }

    void PickMorning(int option)
    {
        string[] reply;
        if (option == 0)
        {
            Reputation.Add(FactionId.Megacorp, 1f);
            Reputation.AddSuspicion(3f);
            reply = new[] { "Obchôdzka. Hm. Zapíšem to. Ale pamätaj, dôstojník: sledujem ťa." };
        }
        else if (option == 1)
        {
            Reputation.Add(FactionId.Megacorp, -2f);
            Reputation.AddSuspicion(-4f);
            reply = new[] { "Neschopnosť technikov nie je tvoja starosť... ale máš pravdu, že dróny zlyhávajú. Odchod." };
        }
        else
        {
            Reputation.AddSuspicion(8f);   // mlčanie = podozrenie
            reply = new[] { "Mlčíš? To je tiež odpoveď. Budem ťa sledovať, dôstojník." };
        }

        if (Quest2.Spotted) { Reputation.AddSuspicion(6f); Quest2.VancSuspicious = true; }
        if (Quest2.Late) Reputation.AddSuspicion(4f);
        Quest2.KageContact = true;   // informátor z podsvetia sa ti ozve
        CityManagement.Satisfaction = Mathf.Max(0f, CityManagement.Satisfaction - 5f);   // hradby sú stále poškodené, strach rastie

        MangaDialog.Show("Veliteľ Ren", reply, FinishQuest);
    }

    void FinishQuest()
    {
        SetStep(Quest2Step.Done);
        string path = Quest2.PathDemon ? "Posunul si sa na cestu démona (priazeň Arakaniho)." : Quest2.PathHuman ? "Zostal si verný ľudskej strane (chatrná dôvera Megakorporácie)." : "";
        string contact = Quest2.VancSuspicious ? " Kapitán Vanc ťa podozrieva." : "";
        GameHUD.Message($"Úloha splnená: {Quest2.Title}. {path}{contact} Informátor Kage ti pošle tajné správy. Mesto je čoraz nestabilnejšie.", 10f);
    }

    // ---------- Debuff (vzdor) ----------
    void UpdateDebuff()
    {
        if (Quest2.DebuffActive && !debuffApplied)
        {
            debuffApplied = true;
            player.damageTakenMultiplier *= 1.25f;
        }
        if (Quest2.DebuffActive && clock.Day >= Quest2.DebuffUntilDay && !clock.IsNight)
        {
            Quest2.DebuffActive = false;
            if (debuffApplied) { debuffApplied = false; player.damageTakenMultiplier /= 1.25f; }
            GameHUD.Message("Stopa po Arakanim sa vytratila.", 4f);
        }
    }

    // ---------- Voľba (zastaví hru, tlačidlá + klávesy 1/2, voliteľný časový limit) ----------
    void ShowChoice(string title, string text, string[] options, Action<int> picked, float seconds)
    {
        if (choiceOpen) return;
        choiceOpen = true;
        choiceTitle = title;
        choiceText = text;
        choiceOptions = options;
        choicePicked = picked;
        choiceTotal = seconds;
        choiceEnds = seconds > 0f ? Time.unscaledTime + seconds : 0f;

        savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        savedLock = Cursor.lockState;
        savedVisible = Cursor.visible;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void UpdateChoice()
    {
        if (PauseMenu.IsOpen) return;
        for (int i = 0; i < choiceOptions.Length; i++)
            if (GameInput.GetKeyDown(KeyCode.Alpha1 + i) || GameInput.GetKeyDown(KeyCode.Keypad1 + i)) { Pick(i); return; }
        if (choiceEnds > 0f && Time.unscaledTime >= choiceEnds) Pick(-1);   // vypršal čas
    }

    void Pick(int option)
    {
        Action<int> callback = choicePicked;
        choiceOpen = false;
        choicePicked = null;
        Time.timeScale = savedScale;
        Cursor.lockState = savedLock == CursorLockMode.None ? CursorLockMode.Locked : savedLock;
        Cursor.visible = savedLock == CursorLockMode.None ? false : savedVisible;
        callback?.Invoke(option);
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup || player == null) return;

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(16, 16, 8, 8) };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        }

        Color prev = GUI.color;

        // Nízka hladina krvi v noci: obmedzené vnímanie, tmavo-červený okraj obrazovky
        if (Quest2.Active && Quest2.Step != Quest2Step.Morning && player.IsDemon && player.Blood < 25f)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
            GUI.color = new Color(0.3f, 0f, 0.02f, 0.18f + 0.1f * pulse);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        // Tabuľka úloh sa ukáže hneď po prvom queste (aj kým čaká začiatok a počas Arakaniho hlasu), rovnako ako pri quest 1
        bool waiting = Quest2.Step == Quest2Step.Inactive && MainQuest.State == QuestState.Done && Story.PrologueDone;
        if ((Quest2.Active || waiting) && !choiceOpen && !MangaDialog.Active && !PauseMenu.IsOpen)
        {
            int step = Quest2.Step == Quest2Step.Inactive || Quest2.Step == Quest2Step.Night ? 0 : Quest2.Step == Quest2Step.Sneak || Quest2.Step == Quest2Step.Gate ? 1 : Quest2.Step == Quest2Step.Forest || Quest2.Step == Quest2Step.Meeting ? 2
                     : Quest2.Step == Quest2Step.Return ? 3 : 4;
            int distance = Mathf.RoundToInt(Mathf.Max(GateDistance, ProjectionDistance, HomeDistance) / 5f);
            if (goalText.Stale((int)Quest2.Step, distance, clock != null && clock.IsNight ? 1 : 0)) goalText.Text = Quest2.Objective(this);
            GUI.color = Color.white;
            QuestPanel.Draw(Quest2.Title, panelSteps, step, goalText.Text, -1f);
        }

        if (choiceOpen && !PauseMenu.IsOpen) DrawChoice();
        GUI.color = prev;
    }

    void DrawChoice()
    {
        float w = Mathf.Min(680f, Screen.width - 40f);
        float h = 150f + choiceOptions.Length * 62f + (choiceEnds > 0f ? 22f : 0f);
        Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = new Color(0.04f, 0.04f, 0.07f, 0.97f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = new Color(0.75f, 0.25f, 0.35f);
        GUI.DrawTexture(new Rect(box.x, box.y, box.width, 4f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(box.x + 20f, box.y + 14f, box.width - 40f, 36f), choiceTitle, titleStyle);
        GUI.Label(new Rect(box.x + 30f, box.y + 54f, box.width - 60f, 60f), choiceText, textStyle);

        float y = box.y + 122f;
        for (int i = 0; i < choiceOptions.Length; i++)
        {
            if (GUI.Button(new Rect(box.x + 30f, y, box.width - 60f, 52f), choiceOptions[i], buttonStyle)) { Pick(i); return; }
            y += 62f;
        }

        if (choiceEnds > 0f)
        {
            float left = Mathf.Max(0f, choiceEnds - Time.unscaledTime);
            float k = choiceTotal > 0f ? left / choiceTotal : 0f;
            GUI.color = new Color(0.9f, 0.3f, 0.25f);
            GUI.DrawTexture(new Rect(box.x + 30f, box.yMax - 24f, (box.width - 60f) * k, 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            hintStyle.normal.textColor = new Color(0.8f, 0.7f, 0.7f);
            GUI.Label(new Rect(box.x, box.yMax - 22f, box.width, 20f), $"Rozhodni sa: {left:0.0} s", hintStyle);
        }
    }

    void OnDestroy()
    {
        if (choiceOpen) { choiceOpen = false; Time.timeScale = 1f; }
        if (debuffApplied && player != null) player.damageTakenMultiplier /= 1.25f;
    }
}
