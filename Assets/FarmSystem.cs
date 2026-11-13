using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pole (farma): postaví sa na parcele za drevo a 100 peňazí. Na jednom poli pracujú najviac 3 farmári (civilisti), ktorým sa platí denná mzda.
/// Úroda rastie podľa rýchlosti = súčet (farmári × faktor mzdy); po dozretí farmári uložia zeleninu a chlieb do truhly pri poli
/// (nie rovno do inventára; vezmeš ju klávesom G pri truhle). Vyššia mzda = rýchlejší rast aj väčšia úroda; nezaplatení pracujú len na štvrtinu.
/// Úrodu zhoršuje kontaminovaná pôda a démonické stromy v okolí; kuchár s dôverou ≥ 20 pridá 15 %. Farmári pracujú len cez deň, nie pri protestoch ani poplachu.
/// Jedlo je aj v truhliciach v divočine a v bani.
/// </summary>
public static class FarmSystem
{
    public const int MaxFarmers = 3;
    public const int HireCost = 40;
    public const int MinWage = 10, MaxWage = 80, DefaultWage = 30, WageStep = 5;
    public const float BaseGrowSeconds = 240f;     // jeden farmár s mzdou 30 = jeden herný deň
    public const float MaxSuspicion = 70f;
    public const float NightFactor = 0.8f;       // pod lampami sa pracuje o 20 % pomalšie

    /// <summary>Elektráreň beží (je prúd): polia sú v noci osvetlené a farmári môžu pracovať aj po zotmení.</summary>
    public static bool Lit => Power.PlantBuilt && Power.Online;

    public class Farm
    {
        public int farmers;
        public int wage = DefaultWage;
        public float progress;      // 0..1
        public bool unpaid;
        public int harvests;
    }

    private static readonly Dictionary<string, Farm> farms = new Dictionary<string, Farm>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() => farms.Clear();

    public static Farm Get(string plotId)
    {
        if (!farms.TryGetValue(plotId, out Farm f)) farms[plotId] = f = new Farm();
        return f;
    }

    public static bool TryGet(string plotId, out Farm farm) => farms.TryGetValue(plotId, out farm);

    public static IEnumerable<KeyValuePair<string, Farm>> All => farms;

    public static int TotalFarmers
    {
        get
        {
            int n = 0;
            foreach (Farm f in farms.Values) n += f.farmers;
            return n;
        }
    }

    /// <summary>Efektívny počet farmárov pre potrebu jedla mesta (nezaplatení sa počítajú ako polovica).</summary>
    public static float Staff(string plotId) => farms.TryGetValue(plotId, out Farm f) ? f.farmers * (f.unpaid ? 0.5f : 1f) : 0f;

    public static float WageFactor(int wage) => Mathf.Clamp(0.5f + wage / 60f, 0.5f, 1.5f);

    public static float Speed(Farm f) => f.farmers * WageFactor(f.wage) * (f.unpaid ? 0.25f : 1f);

    /// <summary>Podiel úrody, o ktorý prídeš pre zlú pôdu: kontaminácia do 45 m (−30 % každá, max 60 %) a démonické stromy do 30 m (−15 %, max 45 %).</summary>
    public static float Penalty(BuildPlot plot)
    {
        float loss = 0f;
        foreach (BuildPlot other in BuildPlot.All)
            if (other != plot && other.Contaminated && (other.transform.position - plot.transform.position).sqrMagnitude < 45f * 45f) loss += 0.3f;
        loss = Mathf.Min(0.6f, loss);

        float trees = 0f;
        foreach (DemonTree tree in DemonTree.All)
            if (tree != null && (tree.transform.position - plot.transform.position).sqrMagnitude < 30f * 30f) trees += 0.15f;
        return Mathf.Clamp01(loss + Mathf.Min(0.45f, trees));
    }

    /// <summary>Šanca na bohatú úrodu (2 – 30 %): rastie s dôverou civilistov a spokojnosťou mesta.</summary>
    public static float BountyChance()
    {
        float trust = Mathf.Clamp01(Reputation.Get(FactionId.Civilians) / 100f);
        float happy = Mathf.Clamp01(CityManagement.Satisfaction / 100f);
        return Mathf.Lerp(0.02f, 0.30f, trust * 0.6f + happy * 0.4f);
    }

    public static int Yield(Farm f, BuildPlot plot)
    {
        float wageShare = Mathf.InverseLerp(MinWage, MaxWage, f.wage);
        float amount = (3 + 2 * f.farmers) * Mathf.Lerp(0.85f, 1.3f, wageShare);
        amount *= 1f - Penalty(plot);
        if (ProfessionTrust.Get(Profession.Cook) >= 20f) amount *= 1.15f;
        return Mathf.Max(1, Mathf.RoundToInt(amount));
    }

    static int WorkersUsed()
    {
        int n = TotalFarmers;
        foreach (KeyValuePair<GatherMaterial, string> pair in SettlementGatherers.Assignments) n++;
        return n;
    }

    public static string Hire(BuildPlot plot, PlayerStats player)
    {
        Farm f = Get(plot.PlotId);
        if (f.farmers >= MaxFarmers) return $"Na jednom poli pracujú najviac {MaxFarmers} farmári.";
        if (WorkersUsed() + 1 > Mathf.Max(1, CityPopulation.AliveCivilians() / 2)) return "Mesto nemá dosť civilistov: pracovať môže najviac polovica obyvateľov.";
        if (!player.TrySpend(HireCost)) return $"Najatie farmára stojí {HireCost} peňazí.";
        f.farmers++;
        return $"Najatý farmár ({f.farmers}/{MaxFarmers}). Denná mzda: {f.farmers * f.wage}.";
    }

    public static string Fire(BuildPlot plot)
    {
        Farm f = Get(plot.PlotId);
        if (f.farmers <= 0) return "Na poli nikto nepracuje.";
        f.farmers--;
        return $"Farmár prepustený ({f.farmers}/{MaxFarmers}).";
    }

    public static void SetWage(BuildPlot plot, int delta)
    {
        Farm f = Get(plot.PlotId);
        f.wage = Mathf.Clamp(f.wage + delta, MinWage, MaxWage);
    }

    /// <summary>Dá do truhly pri poli zeleninu a niekedy chlieb.</summary>
    static void Harvest(BuildPlot plot, Farm f)
    {
        int amount = Yield(f, plot);
        amount = Mathf.Max(1, amount + UnityEngine.Random.Range(-1, 2));   // úroda nie je každý raz rovnaká
        int bread = 0;
        for (int i = 0; i < amount; i++) if (UnityEngine.Random.value < 0.25f) bread++;
        int vegetables = amount - bread;

        // Bohatá úroda: vďaka dôvere civilistov pribudne niekoľko kusov navyše, hlavne chleba
        int bonus = 0;
        if (UnityEngine.Random.value < BountyChance())
        {
            bonus = Mathf.Max(2, Mathf.RoundToInt(amount * UnityEngine.Random.Range(0.25f, 0.7f)));
            int extraBread = Mathf.CeilToInt(bonus * 0.6f);
            bread += extraBread;
            vegetables += bonus - extraBread;
        }

        if (vegetables > 0) SettlementGatherers.AddToChest(plot.PlotId, ItemCatalog.Vegetables, vegetables);
        if (bread > 0) SettlementGatherers.AddToChest(plot.PlotId, ItemCatalog.Bread, bread);
        f.harvests++;
        GameHUD.Message(bonus > 0 ? $"Bohatá úroda na poli {plot.PlotId}! Zelenina {vegetables}, chlieb {bread} (z toho {bonus} navyše vďaka dôvere ľudí)." : $"Žatva na poli {plot.PlotId}: zelenina {vegetables}, chlieb {bread} (truhla pri poli).", 4f);
    }

    static bool IsFarm(string plotId)
    {
        foreach (BuildPlot p in BuildPlot.All)
            if (p.PlotId == plotId) return p.State == PlotState.Built && p.Building != null && p.Building.id == BuildingCatalog.Farm;
        return false;
    }

    /// <summary>Denne: výplata mzdy. Kto nedostane peniaze, pracuje nasledujúci deň len na štvrtinu. Zničené pole farmárov prepustí.</summary>
    public static void OnNewDay(PlayerStats player)
    {
        int paid = 0, unpaidFarms = 0;
        foreach (KeyValuePair<string, Farm> pair in farms)
        {
            Farm f = pair.Value;
            if (!IsFarm(pair.Key)) { f.farmers = 0; f.progress = 0f; f.unpaid = false; continue; }
            if (f.farmers <= 0) { f.unpaid = false; continue; }

            int wages = f.farmers * f.wage;
            if (player != null && player.TrySpend(wages)) { f.unpaid = false; paid += wages; }
            else { f.unpaid = true; unpaidFarms++; }
        }
        if (unpaidFarms > 0) GameHUD.Message($"Farmári na {unpaidFarms} poliach nedostali mzdu a pracujú len na štvrtinu.", 5f);
        else if (paid > 0) GameHUD.Message($"Farmárom vyplatená mzda: {paid} peňazí.", 3f);
    }

    /// <summary>Môže sa dnes na poli pracovať?</summary>
    public static string WhyIdle(BuildPlot plot, Farm f)
    {
        if (f.farmers <= 0) return "nikto tu nepracuje";
        if (CityPopulation.AliveCivilians() < SettlementGatherers.MinCivilians) return "málo civilistov";
        if (CityManagement.Protesting) return "protesty";
        if (Reputation.Suspicion >= MaxSuspicion) return "mesto je v poplachu";
        DayNightCycle clock = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
        if (clock != null && clock.IsNight && !Lit) return "je noc (bez elektriny sa nesvieti)";
        if (SettlementGatherers.Stored(plot.PlotId, ItemCatalog.Vegetables) >= SettlementGatherers.ChestCapacityPerItem) return "truhla je plná";
        return null;
    }

    public static void Tick(BuildPlot plot, float dt)
    {
        if (!farms.TryGetValue(plot.PlotId, out Farm f)) return;
        if (WhyIdle(plot, f) != null) return;

        DayNightCycle clock = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
        float night = clock != null && clock.IsNight ? NightFactor : 1f;
        f.progress += dt * Speed(f) * night / BaseGrowSeconds;
        if (f.progress >= 1f)
        {
            f.progress = 0f;
            Harvest(plot, f);
        }
    }

    // ---------- Ponuka v menu parcely (P) ----------
    public static void DrawMenu(BuildPlot plot, PlayerStats player, GUIStyle text, ref string status)
    {
        Farm f = Get(plot.PlotId);

        GUILayout.Label($"<b>Farmári:</b> {f.farmers}/{MaxFarmers}   <b>Mzda každého:</b> {f.wage}/deň   (spolu {f.farmers * f.wage}/deň)", text);
        float speed = Speed(f);
        string idle = WhyIdle(plot, f);
        GUILayout.Label($"Rýchlosť rastu: ×{speed:0.0}  ·  úroda na žatvu: {(f.farmers > 0 ? Yield(f, plot).ToString() : "–")} ks  ·  postup: {f.progress * 100f:0} %" + (idle != null ? $"  <color=#e0a040>(stojí: {idle})</color>" : ""), text);

        float penalty = Penalty(plot);
        if (penalty > 0.01f) GUILayout.Label($"<color=#e07070>Zlá pôda v okolí (kontaminácia / démonické stromy): úroda −{penalty * 100f:0} %.</color>", text);
        if (f.unpaid) GUILayout.Label("<color=#e07070>Farmári nedostali mzdu: pracujú len na štvrtinu.</color>", text);
        GUILayout.Label($"Šanca na bohatú úrodu (viac chleba): {BountyChance() * 100f:0} %  (rastie s dôverou civilistov a spokojnosťou mesta)" + (Lit ? "  ·  Elektrina svieti: farmári pracujú aj v noci." : "  ·  V noci sa nepracuje (bez elektriny)."), text);
        GUILayout.Label($"Truhla pri poli: {SettlementGatherers.Summary(plot.PlotId)}", text);

        GUILayout.BeginHorizontal();
        GUI.enabled = f.farmers < MaxFarmers;
        if (GUILayout.Button($"Najať farmára ({HireCost})", GUILayout.Height(28f))) status = Hire(plot, player);
        GUI.enabled = f.farmers > 0;
        if (GUILayout.Button("Prepustiť farmára", GUILayout.Height(28f))) status = Fire(plot);
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUI.enabled = f.wage > MinWage;
        if (GUILayout.Button($"Mzda −{WageStep}", GUILayout.Height(26f))) SetWage(plot, -WageStep);
        GUI.enabled = f.wage < MaxWage;
        if (GUILayout.Button($"Mzda +{WageStep}", GUILayout.Height(26f))) SetWage(plot, WageStep);
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label("Čím vyššia mzda a viac farmárov, tým rýchlejšie rastie úroda a tým viac jedla dostaneš. Mzda sa platí každý deň.", text);
    }

    // ---------- Ukladanie ----------
    [Serializable]
    public class SaveData
    {
        public List<string> plots = new List<string>();
        public List<int> farmers = new List<int>();
        public List<int> wages = new List<int>();
        public List<float> progress = new List<float>();
        public List<int> unpaid = new List<int>();
    }

    public static SaveData Export()
    {
        var data = new SaveData();
        foreach (KeyValuePair<string, Farm> pair in farms)
        {
            data.plots.Add(pair.Key);
            data.farmers.Add(pair.Value.farmers);
            data.wages.Add(pair.Value.wage);
            data.progress.Add(pair.Value.progress);
            data.unpaid.Add(pair.Value.unpaid ? 1 : 0);
        }
        return data;
    }

    public static void Import(SaveData data)
    {
        farms.Clear();
        if (data == null) return;
        for (int i = 0; i < data.plots.Count && i < data.farmers.Count && i < data.wages.Count; i++)
        {
            farms[data.plots[i]] = new Farm
            {
                farmers = Mathf.Clamp(data.farmers[i], 0, MaxFarmers),
                wage = Mathf.Clamp(data.wages[i], MinWage, MaxWage),
                progress = i < data.progress.Count ? Mathf.Clamp01(data.progress[i]) : 0f,
                unpaid = i < data.unpaid.Count && data.unpaid[i] == 1
            };
        }
    }
}

/// <summary>Beží v scéne: rast úrody na všetkých poliach (aj keď je hráč ďaleko) a denná výplata mzdy.</summary>
public class FarmRunner : MonoBehaviour
{
    private PlayerStats player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<FarmRunner>() != null) return;
        new GameObject("FarmRunner").AddComponent<FarmRunner>();
    }

    void OnEnable() => DayNightCycle.OnNewDay += HandleNewDay;
    void OnDisable() => DayNightCycle.OnNewDay -= HandleNewDay;

    void HandleNewDay(int day)
    {
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        FarmSystem.OnNewDay(player);
    }

    void Update()
    {
        if (PauseMenu.AtStartup) return;
        float dt = Time.deltaTime;
        foreach (BuildPlot plot in BuildPlot.All)
            if (plot.State == PlotState.Built && plot.Building != null && plot.Building.id == BuildingCatalog.Farm) FarmSystem.Tick(plot, dt);
    }
}

/// <summary>Vzhľad poľa: úroda dorastá a mení farbu podľa postupu, pri poli pracujú figúrky farmárov (ohýbajú sa a prechádzajú po hriadkach).</summary>
public class FarmVisual : MonoBehaviour
{
    private string plotId;
    private Transform[] rows;
    private Renderer[] rowRenderers;
    private readonly List<Transform> figures = new List<Transform>();
    private float nextUpdate;
    private DayNightCycle clock;
    private bool working;
    private Light fieldLight;

    public void Init(string id, int rowCount)
    {
        plotId = id;
        rows = new Transform[rowCount];
        rowRenderers = new Renderer[rowCount];
        for (int i = 0; i < rowCount; i++)
        {
            rows[i] = transform.Find("Rad_" + i);
            rowRenderers[i] = rows[i] != null ? rows[i].GetComponent<Renderer>() : null;
        }
        Refresh();
    }

    void Refresh()
    {
        float growth = 0f;
        int farmers = 0;
        if (FarmSystem.TryGet(plotId, out FarmSystem.Farm farm)) { growth = farm.progress; farmers = farm.farmers; }

        // Úroda: od nízkych zelených výhonkov po vysoké zlatisté klasy
        float height = Mathf.Lerp(0.12f, 0.9f, Mathf.Pow(growth, 0.7f));
        Color color = Color.Lerp(new Color(0.25f, 0.55f, 0.2f), new Color(0.88f, 0.72f, 0.22f), Mathf.Clamp01((growth - 0.35f) / 0.65f));
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] == null) continue;
            Vector3 scale = rows[i].localScale;
            scale.y = height;
            rows[i].localScale = scale;
            Vector3 position = rows[i].localPosition;
            position.y = 0.45f + height * 0.5f;
            rows[i].localPosition = position;
            if (rowRenderers[i] != null) rowRenderers[i].material.color = color;
        }

        // Počet figúrok = počet farmárov
        while (figures.Count < farmers) figures.Add(CreateFigure(figures.Count));
        while (figures.Count > farmers) { Destroy(figures[figures.Count - 1].gameObject); figures.RemoveAt(figures.Count - 1); }

        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
        bool night = clock != null && clock.IsNight;
        working = farm != null && farmers > 0 && clock != null && (!night || FarmSystem.Lit);
        if (fieldLight == null)
        {
            var go = new GameObject("PoleSvetlo");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 4.5f, 0f);
            fieldLight = go.AddComponent<Light>();
            fieldLight.color = new Color(1f, 0.85f, 0.55f);
            fieldLight.range = 16f;
            fieldLight.intensity = 1.4f;
        }
        fieldLight.enabled = night && FarmSystem.Lit && farmers > 0;
    }

    Transform CreateFigure(int index)
    {
        var root = new GameObject("Farmar_" + index).transform;
        root.SetParent(transform, false);
        Color[] shirts = { new Color(0.3f, 0.4f, 0.6f), new Color(0.55f, 0.3f, 0.25f), new Color(0.35f, 0.5f, 0.3f) };

        void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
        {
            GameObject g = GameObject.CreatePrimitive(type);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(root, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().material.color = c;
        }
        Part(PrimitiveType.Capsule, new Vector3(0f, 0.85f, 0f), new Vector3(0.5f, 0.55f, 0.35f), shirts[index % shirts.Length]);
        Part(PrimitiveType.Sphere, new Vector3(0f, 1.55f, 0f), new Vector3(0.36f, 0.4f, 0.36f), new Color(0.75f, 0.55f, 0.42f));
        Part(PrimitiveType.Cylinder, new Vector3(0f, 1.78f, 0f), new Vector3(0.7f, 0.03f, 0.7f), new Color(0.85f, 0.75f, 0.4f));   // slamený klobúk
        root.localPosition = new Vector3(-4f + index * 3.6f, 0.45f, -3f + index * 3f);
        return root;
    }

    void Update()
    {
        if (Time.time >= nextUpdate)
        {
            nextUpdate = Time.time + 0.7f;
            Refresh();
        }

        float t = Time.time;
        for (int i = 0; i < figures.Count; i++)
        {
            Transform f = figures[i];
            if (f == null) continue;
            float phase = i * 2.1f;

            if (working)
            {
                // Pomaly pochodia pozdĺž hriadky a ohýbajú sa k úrode
                Vector3 p = f.localPosition;
                p.z = Mathf.Sin(t * 0.18f + phase) * 5f;
                f.localPosition = p;
                f.localRotation = Quaternion.Euler(28f + 14f * Mathf.Sin(t * 2.2f + phase), Mathf.Cos(t * 0.18f + phase) >= 0f ? 0f : 180f, 0f);
            }
            else f.localRotation = Quaternion.identity;
        }
    }
}
