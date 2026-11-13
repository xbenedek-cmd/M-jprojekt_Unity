using System.Collections.Generic;
using UnityEngine;

public enum QuestState { None, Gather, Ambush, Fight, Return, Resolved, Done }

/// <summary>
/// Prvý hlavný quest „Tiene v Hlbokom lese“ (Akt I). Zadáva ho Inžinierka Kazumi (StoryNpc). Fázy:
/// Gather (výprava do Bleskového kaňona, 50 dreva a 10 šrotu, stopy) → Ambush (súmrak, Void-Spawns pohltia robotníkov a náklad) →
/// Fight (boj; smrteľný úder od démona = premena = invertované vnímanie, hybridná krv zraní Void-Spawns) → Return (návrat a rozhodnutie
/// klamstvo / pravda) → Resolved (nočný šepot Arakaniho) → Done (Void-Spawns sa občas objavujú v Kaňone a Temnom lese).
/// </summary>
public static class MainQuest
{
    public const string Title = "Tiene v Hlbokom lese";
    public const int WoodNeeded = 50, ScrapNeeded = 10;

    public static QuestState State;
    public static bool TracksFound, VoidKnown, VoidUnlocked;
    /// <summary>Materiál odovzdaný Kazumi bez výpravy do kaňona (prepad sa preskočil).</summary>
    public static bool DeliveredEarly;

    public static bool HasEnough(int wood, int scrap) => wood >= WoodNeeded && scrap >= ScrapNeeded;

    /// <summary>Odovzdanie materiálu priamo Kazumi: nie je treba ísť do kaňona ani hľadať stopy.</summary>
    public static string DeliverEarly(PlayerStats player)
    {
        Inventory inv = player != null ? player.GetComponent<Inventory>() : null;
        if (State != QuestState.Gather || inv == null) return "";
        if (!HasEnough(MainQuestRunner.GatheredNow(player, ItemCatalog.Wood, BaseWood), MainQuestRunner.GatheredNow(player, ItemCatalog.Scrap, BaseScrap))) return "";
        inv.TryRemove(ItemCatalog.Wood, WoodNeeded);
        inv.TryRemove(ItemCatalog.Scrap, ScrapNeeded);
        DeliveredEarly = true;
        State = QuestState.Resolved;
        return $"Odovzdal si {WoodNeeded} dreva a {ScrapNeeded} šrotu. Kazumi kývne: „Štíty to zatiaľ udržia. Dobrá práca.“";
    }
    public static int BaseWood, BaseScrap;
    /// <summary>Čierne stopy v kaňone (marker na ne ukáže, keď je všetko nazbierané).</summary>
    public static readonly System.Collections.Generic.List<GameObject> TrackObjects = new System.Collections.Generic.List<GameObject>();
    public static bool QuietWoods => State == QuestState.Gather || State == QuestState.Ambush;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        State = QuestState.None;
        TracksFound = VoidKnown = VoidUnlocked = DeliveredEarly = false;
        BaseWood = BaseScrap = 0;
    }

    public static bool Active => State == QuestState.Gather || State == QuestState.Ambush || State == QuestState.Fight || State == QuestState.Return;

    /// <summary>Kazumi zadá úlohu: štandardná výbava, dvaja robotníci, ciele.</summary>
    public static void Accept(PlayerStats player)
    {
        if (State != QuestState.None || player == null) return;

        Inventory inv = player.GetComponent<Inventory>();
        BaseWood = inv != null ? inv.CountOf(ItemCatalog.Wood) : 0;
        BaseScrap = inv != null ? inv.CountOf(ItemCatalog.Scrap) : 0;
        TracksFound = false;
        State = QuestState.Gather;

        if (!ToolKit.Has(ToolKit.Kind.Axe)) ToolKit.Give(ToolKit.Kind.Axe, 0);
        if (!ToolKit.Has(ToolKit.Kind.Pickaxe)) ToolKit.Give(ToolKit.Kind.Pickaxe, 0);
        if (!ToolKit.Has(ToolKit.Kind.Hammer)) ToolKit.Give(ToolKit.Kind.Hammer, 0);   // železný odpad sa láme kladivom

        MangaDialog.Show("Inžinierka Kazumi", new[]
        {
            "Počúvaj ma, dôstojník. Hradby na južnej strane praskajú.",
            $"Ak do západu slnka nedonesieme aspoň {WoodNeeded} jednotiek dreva a {ScrapNeeded} železného šrotu zo zrúcanín, obranné štíty skolabujú.",
            "Vezmi dvoch robotníkov a choďte do Bleskového kaňona. A pohni sa – včera tam hliadka hlásila divné zvuky.",
            "Sekeru, krompáč a kladivo máš. Stromy na drevo, skaly krompáčom, kovové debny kladivom."
        });
        GameHUD.Message($"Úloha: {Title}. Choď do Bleskového kaňona (portál z mesta).", 6f);
    }

    public static string Objective(int wood, int scrap)
    {
        switch (State)
        {
            case QuestState.Gather:
                if (HasEnough(wood, scrap)) return "Všetko nazbierané. Odovzdaj materiál Inžinierke Kazumi (rozhovor G).";
                if (!TracksFound && wood >= 25) return $"Nazbieraj {WoodNeeded} dreva ({wood}) a {ScrapNeeded} šrotu ({scrap}). Nájdi čierne stopy v blízkosti.";
                return $"Bleskový kaňon: nazbieraj {WoodNeeded} dreva ({wood}) a {ScrapNeeded} šrotu ({scrap}), drevo sekerou, šrot z kovových debien.";
            case QuestState.Ambush: return "Čo je to za zvuk...?";
            case QuestState.Fight: return "Prežiť. Toxín Void-Spawns reaguje na hybridnú krv.";
            case QuestState.Return: return "Vráť sa do mesta a podaj hlásenie Inžinierke Kazumi.";
            case QuestState.Resolved: return "Choď spať: noc ťa čaká.";
            default: return "";
        }
    }

    // Kazumi: rozhodnutie po návrate
    public static string ReportLie()
    {
        if (State != QuestState.Return) return "";
        Reputation.AddSuspicion(6f);
        Reputation.Add(FactionId.Megacorp, -3f);
        State = QuestState.Resolved;
        return "Povedal si, že konvoj prepadla svorka beštií a robotníci utiekli. Ren ťa začne podozrievať zo slabosti (podozrenie +6, Megakorporácia −3).";
    }

    public static string ReportTruth()
    {
        if (State != QuestState.Return) return "";
        VoidKnown = true;
        CityManagement.Satisfaction = Mathf.Max(0f, CityManagement.Satisfaction - 10f);
        Reputation.Add(FactionId.Civilians, 2f);
        State = QuestState.Resolved;
        return "Povedal si pravdu o nových monštrách. Kazumi panikári, hradby ostanú neopravené (spokojnosť mesta −10, civilisti +2).";
    }
}

/// <summary>Anomálny démon z toxického odpadu: veľmi rýchly, bežné zbrane ho takmer nezraňujú, oheň a blesk áno, hybridná krv tiež.</summary>
public class VoidSpawn : MonoBehaviour
{
    public static readonly List<VoidSpawn> All = new List<VoidSpawn>();
    public Damageable Damage { get; private set; }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);
    void Awake() => Damage = GetComponent<Damageable>();

    private static Transform holder;

    /// <param name="prologue">Predátor z prológu: takmer nezničiteľný vyšší démon (scénarizovaná smrť hráča), nie Void-Spawn.</param>
    public static VoidSpawn Spawn(Vector3 position, bool prologue = false)
    {
        DemonEnemy source = null;
        foreach (DemonEnemy d in Object.FindObjectsByType<DemonEnemy>(FindObjectsInactive.Include))
        {
            if (d == null || d.tier >= DemonTier.Higher || d.allied || d.Damage == null || d.Damage.IsDead) continue;
            if (d.GetComponent<VoidSpawn>() != null || d.GetComponent<Infiltrator>() != null || d.GetComponent<TamedBeast>() != null) continue;
            if (source == null || (d.tier == DemonTier.Lesser && source.tier != DemonTier.Lesser)) source = d;
        }
        if (source == null)
        {
            Debug.LogWarning("VoidSpawn: v scéne nie je žiadny nižší démon, z ktorého by sa dal vytvoriť.");
            return null;
        }

        // Klon vznikne pod vypnutým rodičom, aby sa Awake spustil až po úprave štatistík
        if (holder == null)
        {
            var go = new GameObject("VoidHolder");
            go.SetActive(false);
            holder = go.transform;
        }
        GameObject copy = Object.Instantiate(source.gameObject, holder);
        copy.name = "VoidSpawn";

        // Klon dostane kópie modelov zo zdroja, ale ich animátory sú nenainicializované (graf sa nekopíruje): démon by levitoval v T-póze.
        // Modely sa zmažú a NPCPerception.Awake postaví čerstvé.
        NPCPerception clonedPerception = copy.GetComponent<NPCPerception>();
        if (clonedPerception != null)
        {
            if (clonedPerception.monsterVisual != null) Object.DestroyImmediate(clonedPerception.monsterVisual);
            if (clonedPerception.humanVisual != null) Object.DestroyImmediate(clonedPerception.humanVisual);
            clonedPerception.monsterVisual = null;
            clonedPerception.humanVisual = null;
        }

        Damageable dmg = copy.GetComponent<Damageable>();
        dmg.team = CombatTeam.Demon;
        DemonEnemy enemy = copy.GetComponent<DemonEnemy>();
        NPCPerception npc = copy.GetComponent<NPCPerception>();

        if (prologue)
        {
            copy.name = "NeznamaBytost";
            dmg.displayName = "Neznáma bytosť";
            dmg.maxHealth = 4000f;
            dmg.normalDamageFactor = 0.02f;
            enemy.moveSpeed = 6.2f;
            enemy.attackDamage = 9999f;   // nikto nevie, čo to je: jediný úder zabije (scénar prológu)
            if (npc != null) npc.monsterColor = new Color(0.28f, 0.03f, 0.05f);
        }
        else
        {
            dmg.displayName = "Prázdna schránka (Void-Spawn)";
            dmg.maxHealth = 130f;
            dmg.moneyReward = 80;
            dmg.weakness = DamageKind.Fire;
            dmg.secondaryWeakness = DamageKind.Lightning;
            dmg.hybridVulnerable = true;
            dmg.normalDamageFactor = 0.12f;
            enemy.moveSpeed = 4.6f;
            enemy.attackDamage = 24f;
            if (npc != null) npc.monsterColor = new Color(0.03f, 0.02f, 0.05f);
        }

        var glow = new GameObject("BielaZila");
        glow.transform.SetParent(copy.transform, false);
        glow.transform.localPosition = Vector3.up * 1.2f;
        var light = glow.AddComponent<Light>();
        light.color = prologue ? new Color(1f, 0.3f, 0.2f) : new Color(0.85f, 0.9f, 1f);
        light.range = 6f;
        light.intensity = 1.4f;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForceVertex;   // pohyblivé pixelové svetlo pri postave blikalo a ťahalo osvetlenie okolia

        copy.AddComponent<VoidSpawn>();
        copy.transform.SetParent(null);
        copy.transform.position = position;
        copy.SetActive(true);
        return copy.GetComponent<VoidSpawn>();
    }
}

/// <summary>Robotník, ktorý ťa sprevádza na výprave (jednoduchý nasledovník s labelom).</summary>
public class QuestWorker : MonoBehaviour
{
    public bool absorbing;
    public bool follow = true;
    private bool dying;
    private float dyingStart;
    private float absorbStart;
    private Transform player;
    private int index;

    public static QuestWorker Create(Vector3 position, int number, string label = "Robotník", Color? shirt = null)
    {
        var go = new GameObject("Robotnik_" + number);
        go.transform.position = position;
        var cap = go.AddComponent<CapsuleCollider>();
        cap.height = 1.8f;
        cap.radius = 0.35f;
        cap.center = new Vector3(0f, 0.9f, 0f);
        cap.isTrigger = false;

        Material vest = new Material(Shader.Find("Standard")) { color = shirt ?? new Color(0.95f, 0.55f, 0.1f) };
        Material helmet = new Material(Shader.Find("Standard")) { color = new Color(0.95f, 0.9f, 0.2f) };
        Material skin = new Material(Shader.Find("Standard")) { color = new Color(0.75f, 0.55f, 0.42f) };

        void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Material m)
        {
            GameObject p = GameObject.CreatePrimitive(type);
            Object.Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(go.transform, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<Renderer>().sharedMaterial = m;
        }
        // Drevorubači majú vlastný model (Resources/Characters/Drevorubac1, Drevorubac2); ostatní robotníci zostávajú z primitív
        bool lumberjack = label.StartsWith("Drevorubač");
        string modelPath = "Characters/Drevorubac" + (number % 2 + 1);
        GameObject model = lumberjack ? Resources.Load<GameObject>(modelPath) : null;
        if (model == null && lumberjack) { modelPath = "Characters/Drevorubac1"; model = Resources.Load<GameObject>(modelPath); }   // druhý model ešte nemusí existovať

        if (model != null) AttachModel(go.transform, model, modelPath, shirt ?? new Color(0.55f, 0.35f, 0.2f));
        else
        {
            Part(PrimitiveType.Capsule, new Vector3(0f, 0.9f, 0f), new Vector3(0.6f, 0.8f, 0.4f), vest);
            Part(PrimitiveType.Sphere, new Vector3(0f, 1.62f, 0f), new Vector3(0.42f, 0.46f, 0.42f), skin);
            Part(PrimitiveType.Sphere, new Vector3(0f, 1.76f, 0f), new Vector3(0.5f, 0.26f, 0.5f), helmet);
        }

        go.AddComponent<NPCGrounding>();
        QuestWorker w = go.AddComponent<QuestWorker>();
        w.index = number;
        PortalNetwork.MakeLabel(go.transform.position + Vector3.up * 2.5f, go.transform, "Napis_" + label.Replace(' ', '_') + "_" + number, label, 0.045f);
        return w;
    }

    // Model sa zmenší na výšku postavy. Animator sa vypne a postava stojí v základnej póze: pri generickom rigu
    // by deformácia kostí naťahovala trup k rukám, tu ju netreba (robotník len chodí / padá ako celok).
    public static void AttachModel(Transform parent, GameObject model, string modelPath, Color tint, bool animate = true)
    {
        GameObject instance = Object.Instantiate(model, parent);
        LightMeshTable.Apply(instance);   // drevorubači majú v zdroji 1,5 – 2 milióny trojuholníkov
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        RigFixup.FlattenArmatureWrapper(instance.transform);   // modely z Blendera: kostra pod "Armature", klipy ju čakajú priamo pod koreňom
        foreach (Collider c in instance.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        ModelCleanup.StripCamerasAndLights(instance);

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        // Materiál bez textúry (import ju nenašiel) by bol biely / sivý: dostane farbu postavy
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in renderers)
        {
            Material m = r.sharedMaterial;
            if (m == null || m.mainTexture != null) continue;
            block.Clear();
            block.SetColor("_Color", tint);
            block.SetColor("_BaseColor", tint);
            r.SetPropertyBlock(block);
        }
        if (animate) ClipLoopAnimator.Attach(instance, modelPath, parent);
        else foreach (Animator a in instance.GetComponentsInChildren<Animator>(true)) a.enabled = false;   // mŕtvola leží v základnej póze
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        if (b.size.y < 0.0001f) return;

        float k = 1.8f / b.size.y;
        instance.transform.localScale = Vector3.one * k;
        instance.transform.localPosition = new Vector3(-(b.center.x - parent.position.x) * k, -(b.min.y - parent.position.y) * k, -(b.center.z - parent.position.z) * k);
    }

    private ClipLoopAnimator clipAnimator;
    private ChopAnimation chop;

    /// <summary>Drevorubač sekeru máva (skriptová animácia ChopAnimation + sekera v ruke); vypnuté = bežná chôdza / státie.</summary>
    public void SetChopping(bool on)
    {
        if (clipAnimator == null) clipAnimator = GetComponentInChildren<ClipLoopAnimator>();
        if (chop == null)
        {
            if (!on || clipAnimator == null) return;
            chop = ChopAnimation.Attach(clipAnimator.gameObject, true, 1.4f, 0.3f);   // na modeli s kosťami
            chop.loop = true;
        }
        if (on) { if (!chop.IsChopping) chop.StartChopping(); }
        else if (chop.IsChopping) chop.StopChopping();
    }

    public void Absorb() { absorbing = true; absorbStart = Time.time; }

    /// <summary>Zabitie démonmi: postava padne na zem a po chvíli zmizne.</summary>
    public void Kill() { dying = true; dyingStart = Time.time; }

    void Update()
    {
        if (player == null)
        {
            PlayerStats p = FindAnyObjectByType<PlayerStats>();
            if (p != null) player = p.transform;
            return;
        }

        if (dying)
        {
            float k = Mathf.Clamp01((Time.time - dyingStart) / 0.45f);
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 90f * k);
            if (Time.time - dyingStart > 6f) Destroy(gameObject);
            return;
        }

        if (absorbing)
        {
            float u = (Time.time - absorbStart) / 0.9f;
            transform.localScale = Vector3.one * Mathf.Max(0f, 1f - u);
            transform.position += Vector3.up * Time.deltaTime * 1.2f;
            if (u >= 1f)
            {
                Vfx.Disc(transform.position, 1.6f, new Color(0.05f, 0.03f, 0.08f), 1.2f);
                Destroy(gameObject);
            }
            return;
        }

        if (!follow) return;

        // Drží sa pár metrov pri hráčovi; po teleporte (portál) sa priblíži k nemu
        Vector3 behind = player.position - player.forward * (2.5f + index * 1.4f) + player.right * (index == 1 ? -1.2f : 1.2f);
        Vector3 to = behind - transform.position;
        to.y = 0f;
        float d = to.magnitude;
        if (d > 35f) { transform.position = behind + Vector3.up * 0.5f; return; }
        if (d > 1.2f)
        {
            transform.position += to.normalized * Mathf.Min(d, 5.2f * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), 8f * Time.deltaTime);
        }
    }
}

/// <summary>Riadi priebeh questu a nepriateľov Void-Spawn po jeho skončení; zobrazuje cieľ úlohy a fialovo-červený súmrak.</summary>
public class MainQuestRunner : MonoBehaviour
{
    private PlayerStats player;
    private Inventory inventory;
    private DayNightCycle clock;
    private readonly List<QuestWorker> workers = new List<QuestWorker>();
    private List<GameObject> tracks => MainQuest.TrackObjects;
    private readonly List<VoidSpawn> spawns = new List<VoidSpawn>();
    private bool trackLineShown, treeLineShown, silenceShown, transformShown, fled;
    private float ambushAt = -1f, absorbAt = -1f, fleeAt = -1f, nextDirector, nextObjective;
    private GUIStyle style, shadow;
    private bool nightLineDone;
    private readonly HudText goalText = new HudText();
    private static readonly string[] panelSteps = { "Výprava do Bleskového kaňona", "Prežiť prepad", "Hlásenie Inžinierke Kazumi" };
    private float emptySince = -1f;
    private bool inited;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<MainQuestRunner>() != null) return;
        new GameObject("MainQuestRunner").AddComponent<MainQuestRunner>();
    }

    bool InZone(string ground, Vector3 p)
    {
        if (!ZoneLoader.TryGetGround(ground, out Bounds b)) return false;
        return Mathf.Abs(p.x - b.center.x) <= b.extents.x && Mathf.Abs(p.z - b.center.z) <= b.extents.z;
    }

    public static int GatheredNow(PlayerStats player, string id, int baseline)
    {
        var inv = player != null ? player.GetComponent<Inventory>() : null;
        return inv != null ? Mathf.Max(0, inv.CountOf(id) - baseline) : 0;
    }

    int Gathered(string id, int baseline) => inventory != null ? Mathf.Max(0, inventory.CountOf(id) - baseline) : 0;

    void Update()
    {
        if (PauseMenu.AtStartup) return;
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null) inventory = player.GetComponent<Inventory>();
            return;
        }
        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
        if (player.IsDead) return;
        if (!inited)
        {
            inited = true;
            if (MainQuest.State == QuestState.Ambush) MainQuest.State = QuestState.Fight;   // načítaná hra uprostred prepadu
        }

        Vector3 p = player.transform.position;

        switch (MainQuest.State)
        {
            case QuestState.Gather: UpdateGather(p); break;
            case QuestState.Ambush: UpdateAmbush(); break;
            case QuestState.Fight: UpdateFight(); break;
            case QuestState.Resolved: UpdateResolved(); break;
            case QuestState.Done: UpdateDirector(p); break;
        }
    }

    // ---------- Výprava ----------
    void UpdateGather(Vector3 p)
    {
        workers.RemoveAll(w => w == null);
        if (workers.Count == 0)
            for (int i = 0; i < 2; i++) workers.Add(QuestWorker.Create(p - player.transform.forward * (3f + i) + Vector3.up * 0.4f, i));

        int wood = Gathered(ItemCatalog.Wood, MainQuest.BaseWood), scrap = Gathered(ItemCatalog.Scrap, MainQuest.BaseScrap);
        bool inCanyon = InZone("Zem_Kanon", p);

        if (inCanyon && !silenceShown)
        {
            silenceShown = true;
            MangaDialog.Whisper("Robotník", "Pán dôstojník... je tu ticho. Zvieratá utiekli a vtáky nespievajú.", 7f);
        }
        if (inCanyon && wood >= 10 && !treeLineShown)
        {
            treeLineShown = true;
            MangaDialog.Whisper("Robotník", "Pán dôstojník... pozrite na tie stromy. Prečo sú celé čierne, akoby ich niečo vycucalo zvnútra?", 8f);
        }
        if (inCanyon && wood >= 25 && tracks.Count == 0) SpawnTracks(p);

        if (!MainQuest.TracksFound)
            foreach (GameObject t in tracks)
                if (t != null && (t.transform.position - p).sqrMagnitude < 3.2f * 3.2f)
                {
                    MainQuest.TracksFound = true;
                    if (!trackLineShown)
                    {
                        trackLineShown = true;
                        MangaDialog.Show("Zistenie", new[]
                        {
                            "Žiadne odtlačky vlkov ani známych klanov. Stopy sú čierne, zuhoľnatené a slizké.",
                            "Zdá sa, že pohlcujú svetlo okolo seba. Nič také si ešte nevidel."
                        });
                    }
                    break;
                }

        if (inCanyon && wood >= MainQuest.WoodNeeded && scrap >= MainQuest.ScrapNeeded && MainQuest.TracksFound) BeginAmbush();
    }

    void SpawnTracks(Vector3 around)
    {
        for (int i = 0; i < 4; i++)
        {
            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(10f, 18f);
            Vector3 pos = around + new Vector3(ring.x, 0f, ring.y);
            if (Physics.Raycast(pos + Vector3.up * 40f, Vector3.down, out RaycastHit hit, 120f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "CiernaStopa";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = pos + Vector3.up * 0.03f;
            go.transform.localScale = new Vector3(1.6f, 0.02f, 1.1f);
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.01f, 0.01f, 0.015f) };
            tracks.Add(go);
        }
        GameHUD.Message("Pri stromoch si všimol čierne stopy na zemi. Skúmaj ich zblízka.", 5f);
    }

    // ---------- Prepad ----------
    void BeginAmbush()
    {
        MainQuest.State = QuestState.Ambush;
        if (clock != null) clock.SetClock(clock.Day, 18.4f);   // súmrak
        GameAudio.Play(GameAudio.Sfx.Lightning, player.transform.position);
        MangaDialog.Show("Súmrak", new[]
        {
            "Nákladný voz je takmer plný. Slnko zapadá a nebo sfarbilo do neprirodzene fialovo-červenej.",
            "Z útesov sa ozve zvuk – skratujúci prúd zmiešaný s hlbokým mechanickým chrapotom. Znie to ako plač kovových súčiastok.",
            "Z tieňa vybehnú bytosti, aké si nikdy nevidel: telá z polámaných plátov Megakorporácie, obrastené pulzujúcim čiernym dymom."
        }, StartAbsorb);
    }

    void StartAbsorb()
    {
        Vector3 p = player.transform.position;
        for (int i = 0; i < 2; i++)
        {
            Vector2 ring = Random.insideUnitCircle.normalized * 12f;
            VoidSpawn v = VoidSpawn.Spawn(p + new Vector3(ring.x, 1f, ring.y));
            if (v != null) spawns.Add(v);
        }
        absorbAt = Time.time + 2.5f;
    }

    void UpdateAmbush()
    {
        if (absorbAt < 0f || Time.time < absorbAt) return;
        absorbAt = -1f;

        foreach (QuestWorker w in workers) if (w != null) w.Absorb();
        workers.Clear();

        // Náklad je zničený
        if (inventory != null)
        {
            inventory.TryRemove(ItemCatalog.Wood, Mathf.Min(inventory.CountOf(ItemCatalog.Wood), Gathered(ItemCatalog.Wood, MainQuest.BaseWood)));
            inventory.TryRemove(ItemCatalog.Scrap, Mathf.Min(inventory.CountOf(ItemCatalog.Scrap), Gathered(ItemCatalog.Scrap, MainQuest.BaseScrap)));
        }
        GameHUD.MangaPanel("ŠKRÁÁT!", 0.6f);
        GameHUD.Message("Dva prízraky robotníkov doslova vysali. Konvoj je zničený a náklad stratený.", 6f);

        Vector3 p = player.transform.position;
        Vector2 ring2 = Random.insideUnitCircle.normalized * 14f;
        VoidSpawn third = VoidSpawn.Spawn(p + new Vector3(ring2.x, 1f, ring2.y));
        if (third != null) spawns.Add(third);

        MainQuest.State = QuestState.Fight;
        transformShown = fled = false;
        fleeAt = -1f;
    }

    // ---------- Boj ----------
    void UpdateFight()
    {
        spawns.RemoveAll(s => s == null);

        if (player.IsDemon && !transformShown)
        {
            transformShown = true;
            GameHUD.MangaPanel("BLESK!", 0.8f);
            MangaDialog.Show("Premena", new[]
            {
                "Smrteľný úder ťa neusmrtil. Duchovný odpad a neznáma anomália v tebe zareagovali – silný záblesk.",
                "Obrazovka sa rozostrí. Keď otvoríš oči, nové démony vidíš ako zúfalé, plačúce ľudské siluety roztrhané na kusy.",
                "Tvoja hybridná krv reaguje na ich toxín. Päste ich zrazu zraňujú."
            });
            PerceptionToggle perception = FindAnyObjectByType<PerceptionToggle>();
            if (perception != null) perception.SetDemonVision(true);
        }

        // Jeden padol: zvyšné dve zdesené stiahnu chvosty a zmiznú hlbšie v kaňone
        bool anyDead = false;
        foreach (VoidSpawn s in spawns) if (s != null && s.Damage != null && s.Damage.IsDead) { anyDead = true; break; }
        if (anyDead && !fled)
        {
            fled = true;
            fleeAt = Time.time + 2f;
            GameHUD.Message("Zvyšné prízraky sú zaskočené tvojou silou...", 4f);
        }
        if (fled && fleeAt > 0f && Time.time >= fleeAt)
        {
            foreach (VoidSpawn s in spawns)
                if (s != null && s.Damage != null && !s.Damage.IsDead)
                {
                    Vfx.Disc(s.transform.position, 2f, new Color(0.05f, 0.03f, 0.08f), 1.2f);
                    Destroy(s.gameObject);
                }
            spawns.Clear();
            fleeAt = -1f;
            MainQuest.State = QuestState.Return;
            GameHUD.Message("Prízraky utiekli hlbšie do kaňona. Vráť sa do mesta a podaj hlásenie Kazumi.", 7f);
            foreach (GameObject t in tracks) if (t != null) Destroy(t);
            tracks.Clear();
            return;
        }

        // Načítaná hra uprostred boja bez nepriateľov: po chvíli pokračuje návratom
        if (spawns.Count == 0 && VoidSpawn.All.Count == 0 && !fled)
        {
            if (emptySince < 0f) emptySince = Time.time;
            else if (Time.time - emptySince > 4f) { MainQuest.State = QuestState.Return; emptySince = -1f; }
        }
        else emptySince = -1f;
    }

    // ---------- Šepot ----------
    void UpdateResolved()
    {
        // Šepot príde hneď po hlásení Kazumi (druhý quest nadväzuje bez čakania na noc)
        if (clock == null || nightLineDone) return;
        nightLineDone = true;
        foreach (QuestWorker w in workers) if (w != null) Destroy(w.gameObject);
        workers.Clear();
        foreach (GameObject t in tracks) if (t != null) Destroy(t);
        tracks.Clear();
        GameHUD.MangaPanel("ARAKANI", 0.5f);
        if (MainQuest.DeliveredEarly)
            MangaDialog.Whisper("ARAKANI", "Mesto si kúpilo ďalší deň, hybrid môj. Ale tma v lesoch sa nehýbe bez dôvodu... Čoskoro ťa zavolám.", 12f);
        else
        MangaDialog.Whisper("ARAKANI", "Cítil si to, hybrid môj? Toto nie sú moje deti... Toto stvorila tvoja drahá Megakorporácia. Sú to prízraky vašej vlastnej chamtivosti. A čoskoro si po teba prídu znova.", 14f);
        MainQuest.VoidUnlocked = true;
        MainQuest.State = QuestState.Done;
        GameHUD.Message("Úloha splnená: Tiene v Hlbokom lese. Void-Spawns sa teraz občas objavia v Bleskovom kaňone a Temnom lese.", 8f);
    }

    // ---------- Void-Spawns v otvorenom svete ----------
    void UpdateDirector(Vector3 p)
    {
        if (!MainQuest.VoidUnlocked || Time.time < nextDirector) return;
        nextDirector = Time.time + Random.Range(120f, 240f);

        bool inWoods = InZone("Zem_Kanon", p) || InZone("Zem_Les", p);
        spawns.RemoveAll(s => s == null);

        // Vzdialené alebo denné prízraky zmiznú
        for (int i = spawns.Count - 1; i >= 0; i--)
            if (!inWoods || (clock != null && !clock.IsNight && Random.value < 0.5f))
            {
                if (spawns[i] != null) Destroy(spawns[i].gameObject);
                spawns.RemoveAt(i);
            }

        if (!inWoods || spawns.Count >= 2 || Random.value > 0.4f) return;
        Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(40f, 60f);
        VoidSpawn v = VoidSpawn.Spawn(p + new Vector3(ring.x, 1f, ring.y));
        if (v != null)
        {
            spawns.Add(v);
            GameHUD.Message("V lese sa ozve plač kovových súčiastok. Niečo sa blíži...", 4f);
        }
    }

    // ---------- Zobrazenie cieľa a súmraku ----------
    void OnGUI()
    {
        if (PauseMenu.AtStartup || player == null) return;

        QuestState s = MainQuest.State;
        if (s == QuestState.Ambush || s == QuestState.Fight)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0.55f, 0.08f, 0.38f, 0.2f);   // fialovo-červený súmrak (anomália)
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        if (!MainQuest.Active && s != QuestState.Resolved) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = true };
            style.normal.textColor = new Color(1f, 0.9f, 0.5f);
            shadow = new GUIStyle(style);
            shadow.normal.textColor = Color.black;
        }

        int wood = Gathered(ItemCatalog.Wood, MainQuest.BaseWood), scrap = Gathered(ItemCatalog.Scrap, MainQuest.BaseScrap);
        if (goalText.Stale((int)s, wood, scrap + (MainQuest.TracksFound ? 1000 : 0))) goalText.Text = MainQuest.Objective(wood, scrap);
        int step = s == QuestState.Gather || s == QuestState.Ambush ? 0 : s == QuestState.Fight ? 1 : s == QuestState.Return ? 2 : 3;
        float progress = s == QuestState.Gather ? Mathf.Clamp01((Mathf.Min(wood, MainQuest.WoodNeeded) + Mathf.Min(scrap, MainQuest.ScrapNeeded)) / (float)(MainQuest.WoodNeeded + MainQuest.ScrapNeeded)) : -1f;
        GUI.color = Color.white;
        QuestPanel.Draw(MainQuest.Title, panelSteps, step, goalText.Text, progress);
    }
}
