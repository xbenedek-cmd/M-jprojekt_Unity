using System;
using System.Collections.Generic;
using UnityEngine;

public enum CharId { Ren, Yumi, Kazumi, Vance, Kage, Mei, Arakani, Raizen, Kuro, Gora, Ignis, Carmilla }

/// <summary>Karta postavy podľa Postavy.txt: meno, rola, slabina v boji, vzhľad (farba) a texty rozhovoru.</summary>
public class CharDef
{
    public CharId id;
    public string name, title, side, bio, weakness, consequence;
    public Color color;
    public string[] firstMeet;
}

/// <summary>
/// Zoznam hlavných postáv hry a pamäť, ktoré si hráč už stretol (karta Postavy v menu na Tab). Ukladá sa s osadou.
/// Stretnutie sa zaznamená rozhovorom (StoryNpc), blízkosťou bossa (do 35 m), lekárky alebo hlasom Arakaniho.
/// </summary>
public static class StoryCast
{
    public static readonly CharDef[] Defs =
    {
        new CharDef
        {
            id = CharId.Ren, name = "Veliteľ Ren", title = "Megakorporácia – tvoj nadriadený", side = "Ľudia",
            bio = "Chladný pragmatik, ktorý vidí ľudí len ako čísla a zdroje. Dáva ti korporátne misie cez deň a ak zanedbáš povinnosti alebo prejavíš „ľudské“ emócie, začne ťa podozrievať. Bola s tebou, keď si zomrel – a nechala ťa na smetisku.",
            weakness = "V mecha-brnení ho paralyzujú elektromagnetické pulzy (Klan Blesku) a má odhalené chladiace ventily na chrbte.",
            consequence = "Porážka: Megakorporácia stratí velenie v meste, nastane chaos, no civilisti a odboj získajú slobodu.",
            color = new Color(0.55f, 0.1f, 0.12f),
            firstMeet = new[] { "Hlásenie, dôstojník. Prepad konvoja prežil len jeden z vás.", "Odteraz hlásiš priamo mne. Žiadne emócie, žiadne výhovorky." }
        },
        new CharDef
        {
            id = CharId.Yumi, name = "Doktorka Yumi", title = "Podzemná lekárka a alchymistka", side = "Ľudia",
            bio = "Tajomná, no súcitná. Jediná v meste, ktorá za peniaze a vzácny materiál vykoná očistný rituál (démon späť na človeka). Tuší viac, než hovorí.",
            weakness = "Nemá brnenie ani bojový výcvik. Rýchly výpad na blízko, kým stihne aplikovať séra, ju vyradí.",
            consequence = "Bez nej niet očistného rituálu ani posvätených čepelí.",
            color = new Color(0.2f, 0.65f, 0.4f),
            firstMeet = new[] { "Pst. Pozri sa na mňa – nemáš odraz v očiach, ale máš ľudské ruky.", "Ak ťa raz začne trápiť to, čo sa v tebe zobúdza, príď. Rituál nie je zadarmo." }
        },
        new CharDef
        {
            id = CharId.Kazumi, name = "Inžinierka Kazumi", title = "Hlavná technička mesta (civilisti)", side = "Ľudia",
            bio = "Génius pod obrovským stresom, večne so syntetickou kávou. Stará sa o hradby, elektrické štíty a konvoje do baní. Ak pre mesto staviaš a zaisťuješ materiál, dá ti prototypy protidémonických zbraní.",
            weakness = "Závisí od velína: zničené generátory veží jej obranu zrútia. Sama má málo HP a zbrane Klanu Tieňa obídu jej mínové polia.",
            consequence = "Porážka: mesto prestane generovať dane a materiál z baní, civilisti sa začnú meniť na démonov alebo utekať.",
            color = new Color(0.95f, 0.55f, 0.1f),
            firstMeet = new[] { "Dobre, že ťa vidím. Hradby vydržia, len ak mi niekto prinesie materiál.", "Stavaj, zásobuj mesto, a dostaneš odo mňa veci, ktoré démoni neradi vidia." }
        },
        new CharDef
        {
            id = CharId.Vance, name = "Kapitán Vance", title = "Šéf lovcov beštií", side = "Ľudia",
            bio = "Horkokrvný veterán, ktorý prišiel o polovicu tváre pri prepade Vyšším Démonom. Totálna vojna proti všetkému, čo má rohy alebo svietiace oči. Ak sa prezradíš, začne ťa loviť po uliciach.",
            weakness = "Slepý uhol na ľavej strane poškodenej tváre; v berserk hneve robí zbrklé chyby a nekryje si chrbát.",
            consequence = "Porážka: mesto prichádza o najnebezpečnejšieho lovca, no lovci stratia vodcu.",
            color = new Color(0.4f, 0.45f, 0.5f),
            firstMeet = new[] { "Čo sa na mňa pozeráš? Áno, tá tvár je od démona.", "Ak ti niečo na očiach svieti, nech to nevidím." }
        },
        new CharDef
        {
            id = CharId.Kage, name = "Kage", title = "Informátor a šéf čierneho trhu", side = "Odboj / podsvetie",
            bio = "Prešibaný hacker a cynik, ktorý hrá len za seba. Predáva detektory duší, parfumy na zakrytie pachu a falošné doklady. Obchod uzavrie aj s diablom, len keď zarobí.",
            weakness = "Plošné (AoE) výbuchy a sledovanie krvi odhalia jeho klony. Nízka výdrž; ak ho preplatíš, prezradí vlastné pasce.",
            consequence = "Porážka: čierny trh skolabuje, ceny vylepšení stúpnu, zmizne jediná organizovaná hrozba z podsvetia.",
            color = new Color(0.1f, 0.85f, 0.95f),
            firstMeet = new[] { "Hej, nový. Mám tovar, ktorý ti zakryje pach aj minulosť.", "Čo sa predáva, to sa kúpi. Čo sa nedá kúpiť, to sa dá ukradnúť." }
        },
        new CharDef
        {
            id = CharId.Mei, name = "Sestra Mei", title = "Strážkyňa starej mágie, vodkyňa odboja", side = "Odboj",
            bio = "Duchovne založená, pokojná, ale v boji nekompromisná. Chce dokázať, že ľudia a starodávni duchovia (Kami) môžu žiť v symbióze. Kľúčová spojenkyňa na Ceste Rovnováhy.",
            weakness = "Korporátny jed a toxický odpad oslabujú jej štíty a regeneráciu.",
            consequence = "Bez nej sa uzavrie cesta k tajnému koncu (Rovnováha).",
            color = new Color(0.55f, 0.85f, 0.6f),
            firstMeet = new[] { "Cítim v tebe oboje: ľudský tlkot aj hlas prastarej zeme.", "Nemusíš si vybrať jednu stranu. Možno ich dokážeš zmieriť." }
        },
        new CharDef
        {
            id = CharId.Arakani, name = "Arakani", title = "Vládca dymu a hnevu zeme", side = "Démoni",
            bio = "Prapôvodný kami prebudený toxickým odpadom, najvyšší vládca všetkých klanov. Filozofický a cieľavedomý – seba vidí ako prirodzenú reakciu prírody na korporátny parazitizmus. Hovorí v tvojej mysli v noci a ponúka Rituál Pekelnej Krvi.",
            weakness = "Imúnny voči bežným zbraniam aj čistým démonickým kúzlam. Ublíži mu len hybridná krv a posvätené čepele s očistným sérom od Yumi.",
            consequence = "Porážka: oslobodíš myseľ od jeho hlasu (ľudská cesta), alebo ho zradíš a staneš sa Novým Hlavným Démonom.",
            color = new Color(0.6f, 0.15f, 0.7f),
            firstMeet = null
        },
        new CharDef
        {
            id = CharId.Raizen, name = "Lord Raizen", title = "Klan Blesku – Arakaniho pravá ruka", side = "Démoni",
            bio = "Agresívny, hrdý kyber-démon s rohom z čistého blesku. Považuje ťa za nečistého polokrvníka a skúša tvoju silu.",
            weakness = "Uzemnenie a zemné útoky (Klan Divočiny): kamenné zábrany ho skratujú a znehybnia.",
            consequence = "Porážka: dokážeš svoju prevahu a získaš jeho kyber-rohy s bleskovou aurou.",
            color = new Color(0.3f, 0.6f, 1f),
            firstMeet = new[] { "Takže ty si ten hybrid. Smrdíš ľudskou slabosťou." }
        },
        new CharDef
        {
            id = CharId.Kuro, name = "Lady Kuro", title = "Klan Tieňa – mstiteľka a infiltrátorka", side = "Démoni",
            bio = "Chladná, zvodná a vypočítavá. Cez deň chodí mestom v maske šľachtičnej a zbiera informácie. Učí ťa invertované vnímanie a tiché zabíjanie zo tieňa.",
            weakness = "Svetlo, ohňostroje a plamene (Klan Popola) rozbijú jej ilúzie a neviditeľnosť.",
            consequence = "Porážka: získaš Čepeľ Tieňa a krátenie tieňov, no stratíš tiché splynutie s davom.",
            color = new Color(0.45f, 0.15f, 0.65f),
            firstMeet = new[] { "Tichšie, hybrid. Tieň počuje všetko." }
        },
        new CharDef
        {
            id = CharId.Gora, name = "General Gora", title = "Klan Divočiny – skamenený tank", side = "Démoni",
            bio = "Obrovský démon obrastený skalami, kôrou a machom, hlas ako praskanie starých stromov. Nenávidí korporátne bane a stroje. Pozná misie na ničenie konvojov a baní.",
            weakness = "Oheň: kôra a mach vzplanú a obchádzajú jeho 50 % brnenie. Je extrémne pomalý, dá sa obehnúť zozadu.",
            consequence = "Porážka: získaš toxický klanový totem na vyznačenie teritória, lesné beštie sa stanú agresívnejšie.",
            color = new Color(0.35f, 0.5f, 0.25f),
            firstMeet = new[] { "Zem... krváca. Ty... voniaš po bani." }
        },
        new CharDef
        {
            id = CharId.Ignis, name = "Lord Ignis", title = "Klan Popola – Arakaniho kat", side = "Démoni",
            bio = "Fanatický, posadnutý očistným ohňom. Chce vidieť neónové mesto v popole bez ohľadu na Arakaniho či tvoje plány.",
            weakness = "Chlad a voda – roztavená láva stuhne na krehký bazalt; zároveň ho sráža blesk (ako ostatných z jeho klanu).",
            consequence = "Porážka: uhasíš hrozbu nad mestom a získaš Popolavé Srdce (ohnivý enchant, zapaľuje).",
            color = new Color(1f, 0.4f, 0.1f),
            firstMeet = new[] { "Chladný odpad. Zhorím ťa prvého." }
        },
        new CharDef
        {
            id = CharId.Carmilla, name = "Kňažka Carmilla", title = "Klan Pekelnej Krvi – kňažka rituálov", side = "Démoni",
            bio = "Zvodná, aristokratická, posadnutá čistotou pekelnej krvi. Ľudia sú pre ňu nádoby životnej energie. Fascinuje ju tvoja hybridná krv.",
            weakness = "Očistné séra, jed a anti-heal efekty: jej lifesteal sa obráti proti nej.",
            consequence = "Porážka: odomkneš najsilnejšie mágie krvi a lifesteal, ale stratíš dôveru ortodoxných uctievateľov Arakaniho.",
            color = new Color(0.8f, 0.05f, 0.15f),
            firstMeet = new[] { "Ach, ta vzácna krv. Neplytvaj ňou na ulici." }
        }
    };

    private static readonly HashSet<CharId> met = new HashSet<CharId>();
    private static readonly Dictionary<CharId, int> usedDay = new Dictionary<CharId, int>();

    public static CharDef Get(CharId id)
    {
        foreach (CharDef d in Defs) if (d.id == id) return d;
        return null;
    }

    public static bool Met(CharId id) => met.Contains(id);
    public static int MetCount => met.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { met.Clear(); usedDay.Clear(); }

    /// <summary>Zaznamená stretnutie; pri prvom sa spustí úvodný rozhovor a potom onDone. Ďalšie stretnutia len zavolajú onDone.</summary>
    public static void Meet(CharId id, bool withDialog = true, Action onDone = null)
    {
        if (!met.Add(id))
        {
            onDone?.Invoke();
            return;
        }

        CharDef def = Get(id);
        if (def != null) GameHUD.Message($"Nová postava v zozname (Tab → Postavy): {def.name}", 4f);
        if (withDialog && def != null && def.firstMeet != null && def.firstMeet.Length > 0) MangaDialog.Show(def.name, def.firstMeet, onDone);
        else onDone?.Invoke();
    }

    // Denné obmedzenie činností postáv (hlásenie, modlitba, podpora)
    public static bool UsedToday(CharId id, int day) => usedDay.TryGetValue(id, out int d) && d == day;
    public static void MarkUsed(CharId id, int day) => usedDay[id] = day;

    // ---------- Ukladanie ----------
    [Serializable]
    public class SaveData
    {
        public List<int> met = new List<int>();
        public List<int> usedIds = new List<int>();
        public List<int> usedDays = new List<int>();
        public int quest;
        public bool tracks, voidKnown, voidUnlocked;
        public int baseWood, baseScrap;
    }

    public static SaveData Export()
    {
        var data = new SaveData();
        foreach (CharId id in met) data.met.Add((int)id);
        foreach (KeyValuePair<CharId, int> pair in usedDay) { data.usedIds.Add((int)pair.Key); data.usedDays.Add(pair.Value); }
        data.quest = (int)MainQuest.State; data.tracks = MainQuest.TracksFound; data.voidKnown = MainQuest.VoidKnown; data.voidUnlocked = MainQuest.VoidUnlocked;
        data.baseWood = MainQuest.BaseWood; data.baseScrap = MainQuest.BaseScrap;
        return data;
    }

    public static void Import(SaveData data)
    {
        met.Clear();
        usedDay.Clear();
        if (data == null) { MainQuest.State = QuestState.None; MainQuest.TracksFound = MainQuest.VoidKnown = MainQuest.VoidUnlocked = false; return; }
        MainQuest.State = (QuestState)data.quest; MainQuest.TracksFound = data.tracks; MainQuest.VoidKnown = data.voidKnown; MainQuest.VoidUnlocked = data.voidUnlocked;
        MainQuest.BaseWood = data.baseWood; MainQuest.BaseScrap = data.baseScrap;
        foreach (int id in data.met) met.Add((CharId)id);
        for (int i = 0; i < data.usedIds.Count && i < data.usedDays.Count; i++) usedDay[(CharId)data.usedIds[i]] = data.usedDays[i];
    }
}

/// <summary>Sleduje stretnutia: boss do 35 m, lekárka do 5 m, hlas Arakaniho; a Detektor duší (ukazuje najbližšieho infiltrátora).</summary>
public class StoryCastRunner : MonoBehaviour
{
    public static float DetectorUntil;

    private PlayerStats player;
    private float nextScan, nextDetector;
    private Healer healer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { DetectorUntil = 0f; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<StoryCastRunner>() != null) return;
        new GameObject("StoryCastRunner").AddComponent<StoryCastRunner>();
    }

    static CharId? BossId(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return null;
        if (displayName.Contains("Raizen")) return CharId.Raizen;
        if (displayName.Contains("Kuro") && displayName.Contains("Lady")) return CharId.Kuro;
        if (displayName.Contains("Gora")) return CharId.Gora;
        if (displayName.Contains("Ignis") && displayName.Contains("Lord")) return CharId.Ignis;
        if (displayName.Contains("Carmilla") && displayName.Contains("Kňažka")) return CharId.Carmilla;
        return null;
    }

    void Update()
    {
        if (PauseMenu.AtStartup) return;
        if (player == null) { player = FindAnyObjectByType<PlayerStats>(); return; }
        if (player.IsDead) return;

        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 1f;
            Vector3 p = player.transform.position;

            foreach (DemonEnemy demon in DemonEnemy.All)
            {
                if (demon == null || demon.tier != DemonTier.Boss || demon.Damage == null || demon.Damage.IsDead || !demon.gameObject.activeInHierarchy) continue;
                CharId? id = BossId(demon.Damage.displayName);
                if (id.HasValue && !StoryCast.Met(id.Value) && (demon.transform.position - p).sqrMagnitude < 35f * 35f)
                    StoryCast.Meet(id.Value);
            }

            if (healer == null) healer = FindAnyObjectByType<Healer>();
            if (healer != null && !StoryCast.Met(CharId.Yumi) && (healer.transform.position - p).sqrMagnitude < 5f * 5f) StoryCast.Meet(CharId.Yumi);
            if (ArakaniFavor.Instance != null && ArakaniFavor.Instance.VoiceHeard && !StoryCast.Met(CharId.Arakani)) StoryCast.Meet(CharId.Arakani, false);
        }

        // Detektor duší: raz za 4 s ukáže vzdialenosť a smer k najbližšiemu živému neodhalenému infiltrátorovi
        if (Time.time < DetectorUntil && Time.time >= nextDetector)
        {
            nextDetector = Time.time + 4f;
            Infiltrator best = null;
            float bestSq = float.MaxValue;
            foreach (Infiltrator i in Infiltrator.All)
            {
                if (i == null || !i.Alive || i.Revealed) continue;
                float sq = (i.transform.position - player.transform.position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            if (best == null) GameHUD.Message("Detektor duší: ticho. Žiadny infiltrátor v dosahu.", 2.5f);
            else
            {
                Vector3 d = best.transform.position - player.transform.position;
                string dir = Mathf.Abs(d.x) > Mathf.Abs(d.z) ? (d.x > 0f ? "východ" : "západ") : (d.z > 0f ? "sever" : "juh");
                GameHUD.Message($"Detektor duší: infiltrátor {Mathf.Sqrt(bestSq):0} m na {dir}.", 3f);
            }
        }
    }
}
