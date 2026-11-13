using UnityEngine;

/// <summary>
/// Príbehová postava z Postavy.txt (Ren, Kazumi, Vance, Kage, Mei), ktorá stojí na pevnom mieste. Kláves G pri nej otvorí rozhovor
/// (manga panely pri prvom stretnutí) a ponuku činností: hlásenie veliteľovi, technická podpora, varovanie lovca, čierny trh, modlitba.
/// Vzhľad: jednoduchá postava z primitív s farbou a doplnkami podľa postavy (zatiaľ bez modelu).
/// </summary>
public class StoryNpc : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }

    [SerializeField] private CharId character = CharId.Kage;
    [SerializeField] private float range = 3.6f;

    private PlayerStats player;
    private DayNightCycle clock;
    private GUIStyle titleStyle, textStyle;
    private string status = "";
    private float statusUntil;
    private static StoryNpc current;

    public CharId Character { get => character; set => character = value; }

    void Awake()
    {
        BuildVisual();
        if (GetComponent<NPCGrounding>() == null) gameObject.AddComponent<NPCGrounding>();
    }

    void OnDisable() { if (current == this) Close(); }

    // ---------- Vzhľad ----------
    static Material Mat(Color c, bool glow = false)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", 0.4f);
        if (glow)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 2f);
        }
        return m;
    }

    static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        if (rot.HasValue) g.transform.localRotation = rot.Value;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g;
    }

    void BuildVisual()
    {
        CharDef def = StoryCast.Get(character);
        if (def == null) return;

        Renderer rootRenderer = GetComponent<Renderer>();
        if (rootRenderer != null) rootRenderer.enabled = false;
        if (transform.Find("Vzhlad") != null) return;

        var root = new GameObject("Vzhlad").transform;
        root.SetParent(transform, false);

        Material coat = Mat(def.color);
        Material dark = Mat(new Color(0.07f, 0.07f, 0.09f));
        Material skin = Mat(new Color(0.78f, 0.58f, 0.45f));
        Material glow = Mat(Color.Lerp(def.color, Color.white, 0.4f), true);

        Part(root, PrimitiveType.Capsule, "Telo", new Vector3(0f, 0.65f, 0f), new Vector3(0.72f, 0.9f, 0.45f), coat);
        Part(root, PrimitiveType.Sphere, "Hlava", new Vector3(0f, 1.55f, 0f), new Vector3(0.44f, 0.5f, 0.44f), skin);
        Part(root, PrimitiveType.Cube, "Opasok", new Vector3(0f, 0.55f, 0f), new Vector3(0.74f, 0.1f, 0.5f), dark);
        Part(root, PrimitiveType.Cube, "NohaL", new Vector3(-0.2f, -0.2f, 0f), new Vector3(0.25f, 0.4f, 0.3f), dark);
        Part(root, PrimitiveType.Cube, "NohaP", new Vector3(0.2f, -0.2f, 0f), new Vector3(0.25f, 0.4f, 0.3f), dark);

        switch (character)
        {
            case CharId.Ren:   // červený zorník a ramenné pancierové dosky
                Part(root, PrimitiveType.Cube, "Zornik", new Vector3(0f, 1.58f, 0.2f), new Vector3(0.42f, 0.09f, 0.08f), Mat(new Color(1f, 0.15f, 0.1f), true));
                Part(root, PrimitiveType.Cube, "RamenoL", new Vector3(-0.5f, 1.15f, 0f), new Vector3(0.3f, 0.12f, 0.45f), dark);
                Part(root, PrimitiveType.Cube, "RamenoP", new Vector3(0.5f, 1.15f, 0f), new Vector3(0.3f, 0.12f, 0.45f), dark);
                break;
            case CharId.Kazumi:   // prilba, okuliare a hrnček kávy
                Part(root, PrimitiveType.Sphere, "Prilba", new Vector3(0f, 1.75f, 0f), new Vector3(0.5f, 0.28f, 0.5f), Mat(Color.white));
                Part(root, PrimitiveType.Cube, "Okuliare", new Vector3(0f, 1.6f, 0.2f), new Vector3(0.38f, 0.08f, 0.06f), glow);
                Part(root, PrimitiveType.Cylinder, "Kava", new Vector3(0.5f, 0.85f, 0.2f), new Vector3(0.12f, 0.1f, 0.12f), Mat(new Color(0.4f, 0.25f, 0.1f)));
                break;
            case CharId.Vance:   // páska na oku a ťažká zbraň na chrbte
                Part(root, PrimitiveType.Cube, "Paska", new Vector3(-0.1f, 1.58f, 0.2f), new Vector3(0.18f, 0.14f, 0.06f), dark);
                Part(root, PrimitiveType.Cube, "Zbran", new Vector3(0.15f, 0.9f, -0.35f), new Vector3(0.18f, 0.18f, 1.2f), Mat(new Color(0.25f, 0.27f, 0.3f)), Quaternion.Euler(20f, 0f, 0f));
                Part(root, PrimitiveType.Cube, "RamenoL", new Vector3(-0.5f, 1.15f, 0f), new Vector3(0.3f, 0.14f, 0.45f), Mat(new Color(0.3f, 0.32f, 0.36f)));
                break;
            case CharId.Kage:   // kapucňa a neónový prúžok
                Part(root, PrimitiveType.Sphere, "Kapucna", new Vector3(0f, 1.62f, -0.03f), new Vector3(0.52f, 0.5f, 0.52f), dark);
                Part(root, PrimitiveType.Cube, "Prúžok", new Vector3(0f, 0.9f, 0.24f), new Vector3(0.06f, 0.7f, 0.04f), glow);
                break;
            case CharId.Mei:   // kužeľovitý klobúk a palica so svetlom
                Part(root, PrimitiveType.Cylinder, "Klobuk", new Vector3(0f, 1.82f, 0f), new Vector3(0.6f, 0.04f, 0.6f), Mat(new Color(0.9f, 0.9f, 0.8f)));
                Part(root, PrimitiveType.Cylinder, "Palica", new Vector3(0.55f, 0.9f, 0.1f), new Vector3(0.06f, 1f, 0.06f), Mat(new Color(0.35f, 0.22f, 0.1f)));
                Part(root, PrimitiveType.Sphere, "Gula", new Vector3(0.55f, 1.95f, 0.1f), new Vector3(0.2f, 0.2f, 0.2f), glow);
                break;
        }

        PortalNetwork.MakeLabel(transform.position + Vector3.up * 2.9f, transform, "Napis_" + character, def.name + "\n" + def.title, 0.045f);
    }

    // ---------- Interakcia ----------
    void Update()
    {
        if (PauseMenu.AtStartup) return;
        if (player == null) { player = FindAnyObjectByType<PlayerStats>(); return; }
        if (player.IsDead || JailManager.IsInJail || MangaDialog.Active) return;

        float distance = Vector3.Distance(transform.position, player.transform.position);
        if (!MenuOpen && distance <= range)
        {
            GameHUD.Prompt($"[{MerchantNPC.TradeKey}] {StoryCast.Get(character).name}");
            if (GameInput.GetKeyDown(MerchantNPC.TradeKey) && !Healer.MenuOpen)
            {
                current = this;
                StoryCast.Meet(character, true, OpenMenu);
            }
        }
        else if (MenuOpen && current == this && distance > range + 1.5f) Close();

        if (MenuOpen && current == this && GameInput.GetKeyDown(KeyCode.Escape)) Close();
    }

    void OpenMenu()
    {
        MenuOpen = true;
        status = "";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Close()
    {
        if (current == this) current = null;
        if (!MenuOpen) return;
        MenuOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    int Day
    {
        get
        {
            if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
            return clock != null ? clock.Day : 1;
        }
    }

    void Say(string text)
    {
        status = text;
        statusUntil = Time.unscaledTime + 5f;
    }

    // ---------- Ponuka ----------
    void OnGUI()
    {
        if (PauseMenu.AtStartup || !MenuOpen || current != this || player == null) return;
        CharDef def = StoryCast.Get(character);

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            textStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14, richText = true };
        }

        float w = 560f, h = character == CharId.Kazumi ? 620f : 470f;
        Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.96f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = def.color;
        GUI.DrawTexture(new Rect(box.x, box.y, w, 4f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(box.x + 20f, box.y + 14f, w - 40f, h - 28f));
        titleStyle.normal.textColor = Color.Lerp(def.color, Color.white, 0.4f);
        GUILayout.Label(def.name, titleStyle);
        GUILayout.Label($"<i>{def.title}</i>   Peniaze: <b>{player.money}</b>", textStyle);
        GUILayout.Space(6f);
        GUILayout.Label(def.bio, textStyle);
        GUILayout.Space(10f);

        switch (character)
        {
            case CharId.Ren: DrawRen(); break;
            case CharId.Kazumi: DrawKazumi(); break;
            case CharId.Vance: DrawVance(def); break;
            case CharId.Kage: DrawKage(); break;
            case CharId.Mei: DrawMei(); break;
        }

        GUILayout.FlexibleSpace();
        if (!string.IsNullOrEmpty(status) && Time.unscaledTime < statusUntil) GUILayout.Label(status, textStyle);
        if (GUILayout.Button("Zavrieť (Esc)", GUILayout.Height(28f))) Close();
        GUILayout.EndArea();
    }

    bool Daily(CharId id, string reason)
    {
        if (StoryCast.UsedToday(id, Day)) { Say(reason); return false; }
        StoryCast.MarkUsed(id, Day);
        return true;
    }

    void DrawRen()
    {
        GUILayout.Label("Denné hlásenie veliteľovi: stabilita mesta, tvoje správanie, podozrenie. Ren si všíma každú chybu.", textStyle);
        if (!GUILayout.Button("Podať hlásenie (raz denne)", GUILayout.Height(32f))) return;
        if (!Daily(CharId.Ren, "Dnes si už hlásil. Choď pracovať.")) return;

        float suspicion = Reputation.Suspicion;
        if (player.IsDemon || suspicion > 60f)
        {
            Reputation.Add(FactionId.Megacorp, -4f);
            Reputation.AddSuspicion(6f);
            Say("Ren ťa dlho pozoruje. „Niečo je v tebe iné.“ Dôvera Megakorporácie −4, podozrenie +6.");
        }
        else
        {
            Reputation.Add(FactionId.Megacorp, 3f);
            player.AddMoney(60);
            Say("Ren prikývne: „Splnené.“ Dôvera Megakorporácie +3, odmena 60 peňazí.");
        }
    }

    void DrawKazumi()
    {
        DrawQuest();
        GUILayout.Space(8f);
        bool power = Power.PlantBuilt && Power.Online;
        GUILayout.Label($"Mestská technika: elektráreň {(Power.PlantBuilt ? (Power.Online ? "beží" : "stojí bez paliva") : "nie je postavená")}. Konvoje do baní sa zadávajú v menu osady (kláves 9).", textStyle);

        if (GUILayout.Button("Technická podpora (raz denne; 40 peňazí pri behu elektrárne)", GUILayout.Height(32f)))
        {
            if (!power) Say("Bez bežiacej elektrárne ti nemám čo dať. Postav ju a nakŕm čiernymi kryštálmi.");
            else if (Daily(CharId.Kazumi, "Dnes už nemám voľné ruky."))
            {
                player.AddMoney(40);
                Reputation.Add(FactionId.Civilians, 2f);
                Say("Kazumi ti pošle kreditovú dotáciu: +40 peňazí, dôvera civilistov +2.");
            }
        }

        bool trusted = Reputation.Get(FactionId.Civilians) >= 40f;
        GUI.enabled = trusted;
        if (GUILayout.Button(trusted ? "Prototyp protidémonickej zbrane (jednorazovo)" : "Prototyp zbrane (treba dôveru civilistov 40)", GUILayout.Height(32f)))
        {
            WeaponUpgrades.Kind chosen = WeaponUpgrades.Kind.Sharpen;
            bool found = false;
            foreach (WeaponUpgrades.Kind k in System.Enum.GetValues(typeof(WeaponUpgrades.Kind)))
                if (WeaponUpgrades.CanUpgrade(k)) { chosen = k; found = true; break; }

            if (!found) Say("Všetky vylepšenia zbrane už máš.");
            else if (StoryCast.UsedToday((CharId)100, -1)) Say("Prototyp si už dostal.");
            else
            {
                WeaponUpgrades.Upgrade(chosen);
                StoryCast.MarkUsed((CharId)100, -1);   // kľúč 100, deň -1 = jednorazová odmena Kazumi
                Say($"Kazumi ti vrazí prototyp do ruky: vylepšenie zbrane ({chosen}).");
            }
        }
        GUI.enabled = true;
    }

    void DrawQuest()
    {
        switch (MainQuest.State)
        {
            case QuestState.None:
                if (!Story.PrologueDone) { GUILayout.Label("<color=#aaaaaa>Hlavná úloha sa odomkne po prológu.</color>", textStyle); break; }
                GUILayout.Label("<b>Hlavná úloha:</b> hradby praskajú, treba drevo a šrot z Bleskového kaňona.", textStyle);
                if (GUILayout.Button($"Prijať úlohu: {MainQuest.Title}", GUILayout.Height(34f))) { Close(); MainQuest.Accept(player); }
                break;

            case QuestState.Gather:
            case QuestState.Ambush:
            case QuestState.Fight:
                int w = MainQuestRunner.GatheredNow(player, ItemCatalog.Wood, MainQuest.BaseWood), sc = MainQuestRunner.GatheredNow(player, ItemCatalog.Scrap, MainQuest.BaseScrap);
                GUILayout.Label($"<b>Úloha prebieha:</b> {MainQuest.Title}. {MainQuest.Objective(w, sc)}", textStyle);
                if (MainQuest.State == QuestState.Gather && MainQuest.HasEnough(w, sc)
                    && GUILayout.Button($"Odovzdať {MainQuest.WoodNeeded} dreva a {MainQuest.ScrapNeeded} šrotu", GUILayout.Height(34f)))
                    Say(MainQuest.DeliverEarly(player));
                break;

            case QuestState.Return:
                GUILayout.Label("<b>Kazumi:</b> „Kde je náklad?! Kde sú muži?! Čo sa to tam vonku stalo?!“ Čo jej povieš?", textStyle);
                if (GUILayout.Button("Klamstvo: konvoj prepadla divoká svorka beštií, robotníci utiekli", GUILayout.Height(32f))) Say(MainQuest.ReportLie());
                if (GUILayout.Button("Pravda: v lese sú nové bytosti, ktoré pohltili ľudí a nereagujú na zbrane", GUILayout.Height(32f))) Say(MainQuest.ReportTruth());
                break;

            default:
                GUILayout.Label($"<color=#88cc88>Úloha {MainQuest.Title}: hotová.</color>" + (MainQuest.VoidKnown ? " Kazumi vie pravdu o Void-Spawns." : ""), textStyle);
                break;
        }
    }

    void DrawVance(CharDef def)
    {
        GUILayout.Label("„Rohy, svietiace oči, čudný pach – to je môj lov. Ak sa ma budeš báť, aspoň si žijúci.“", textStyle);
        if (GUILayout.Button("Pozdraviť (a nevzbudiť podozrenie)", GUILayout.Height(32f)))
        {
            if (!player.IsDemon && !Lair.Disguised && Reputation.Suspicion < 40f)
            {
                if (Daily(CharId.Vance, "Už sme sa dnes rozprávali.")) { Reputation.AddSuspicion(-4f); Say("Vance si ťa premeria a kývne. Podozrenie −4."); }
            }
            else
            {
                Reputation.AddSuspicion(8f);
                Say("Vance zúži oči. „Ty smrdíš niečím iným, než by si mal.“ Podozrenie +8.");
            }
        }
        GUILayout.Label("<color=#aaaaaa>Slabina: slepý uhol zľava; v berserk hneve nekryje chrbát.</color>", textStyle);
    }

    void DrawKage()
    {
        GUILayout.Label("Čierny trh: ceny platia pre teba, nová tvár.", textStyle);

        KageOffer("Parfum na zakrytie pachu (6 min)", 90, () => Lair.ActivatePerfume(), "Pach je zakrytý: civilisti sa ťa nebojú ani ako démona.");
        KageOffer("Falošné doklady (4 min)", 150, () => Lair.ActivateDocs(), "Doklady platia: vojaci ťa v zónach nepodozrievajú.");
        KageOffer("Detektor duší (3 min): ukazuje infiltrátorov", 220, () => StoryCastRunner.DetectorUntil = Time.time + 180f, "Detektor duší pípa. Sleduj správy.");
    }

    void KageOffer(string label, int price, System.Action apply, string done)
    {
        if (!GUILayout.Button($"{label}  –  {price}", GUILayout.Height(30f))) return;
        if (!player.TrySpend(price)) { Say("Nemáš dosť peňazí."); return; }
        apply();
        Say(done);
    }

    void DrawMei()
    {
        GUILayout.Label("„Ľudia a duchovia sa môžu naučiť žiť spolu. Pomôžem ti nájsť rovnováhu.“", textStyle);
        GUILayout.Label($"Ľudskosť: {SoulBalance.Humanity:0}/100", textStyle);

        if (GUILayout.Button("Očistná modlitba (raz denne, 40 peňazí): ľudskosť +5", GUILayout.Height(32f)))
        {
            if (StoryCast.UsedToday(CharId.Mei, Day)) Say("Dnes si sa už modlil.");
            else if (!player.TrySpend(40)) Say("Nemáš dosť peňazí.");
            else
            {
                StoryCast.MarkUsed(CharId.Mei, Day);
                SoulBalance.Add(5f);
                Say("Mei ti položí ruku na čelo. Ľudskosť +5.");
            }
        }
    }
}
