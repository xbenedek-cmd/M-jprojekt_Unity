using System.Collections.Generic;
using UnityEngine;

/// <summary>Značka démona, ktorý prišiel s nočným útokom; ráno zmizne.</summary>
public class RaidDemon : MonoBehaviour { }

/// <summary>
/// Nočné útoky démonov na mesto: po zotmení si démoni vyberú nechránené postavené parcely (bez obrannej stavby do 60 m).
/// Hráč v blízkosti uvidí skutočných démonov (klony voľných nižších/stredných); vzdialené útoky sa počítajú abstraktne
/// (každých 5 s škoda stavbe). Ráno sa vyhodnotí: zničená stavba po opakovaných útokoch (podľa obtiažnosti) kontaminuje pôdu
/// parcely, očistiť ju dá len drahý rituál (BuildPlot.Cleanse).
/// </summary>
public class NightRaids : MonoBehaviour
{
    private class Raid
    {
        public BuildPlot plot;
        public int strength;
        public readonly List<GameObject> demons = new List<GameObject>();
        public bool hadReal, damaged, destroyed, repelled;
    }

    private const float TickSeconds = 5f;
    private const float RealRange = 170f;
    private const int MaxRaiders = 8;

    private static readonly List<Raid> raids = new List<Raid>();
    private static bool introShown;
    private static bool contaminationShown;
    private static readonly string[] whispers =
    {
        "Moje deti zostúpili do mesta. Pozeraj, ako horí.",
        "Nechránené múry padajú ako suché lístie, Hybrid.",
        "Ľudia sa schovávajú za dverami. Nech sa schovávajú.",
        "Dnes v noci mesto zaplatí za každý strom, ktorý zoťalo."
    };
    private DayNightCycle clock;
    private float nextTick;

    public static bool Active => raids.Count > 0;
    public static int ActiveTargets { get { int n = 0; foreach (Raid r in raids) if (!r.repelled && !r.destroyed) n++; return n; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { raids.Clear(); introShown = false; contaminationShown = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<NightRaids>() != null) return;
        new GameObject("NightRaids").AddComponent<NightRaids>();
    }

    void OnEnable() => DayNightCycle.OnPhaseChanged += OnPhase;
    void OnDisable() => DayNightCycle.OnPhaseChanged -= OnPhase;

    void OnPhase(DayNightCycle.DayPhase phase)
    {
        if (phase == DayNightCycle.DayPhase.Night) BeginRaids();
        else if (phase == DayNightCycle.DayPhase.Dawn) EndRaids();
    }

    // ---------- Začiatok noci ----------
    void BeginRaids()
    {
        raids.Clear();
        if (PauseMenu.AtStartup || !Story.PrologueDone) return;
        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
        int day = clock != null ? clock.Day : 1;
        if (day < Difficulty.FirstRaidDay) return;

        var candidates = new List<BuildPlot>();
        foreach (BuildPlot plot in BuildPlot.All)
            if (plot.State == PlotState.Built && plot.Building != null && plot.Owner != FactionId.Megacorp && !plot.IsDefended())
                candidates.Add(plot);
        if (candidates.Count == 0) return;

        int since = day - Difficulty.FirstRaidDay;
        int targets = Mathf.Clamp(Mathf.RoundToInt((1f + since / 5f) * Difficulty.RaidScale), 1, 4);
        int strength = Mathf.Clamp(Mathf.RoundToInt((2f + since / 3f) * Difficulty.RaidScale), 1, 10);

        PlayerStats player = FindAnyObjectByType<PlayerStats>();
        for (int i = 0; i < targets && candidates.Count > 0; i++)
        {
            int index = Random.Range(0, candidates.Count);
            BuildPlot plot = candidates[index];
            candidates.RemoveAt(index);

            var raid = new Raid { plot = plot, strength = strength };
            raids.Add(raid);
            if (player != null && (plot.transform.position - player.transform.position).sqrMagnitude < RealRange * RealRange)
                SpawnRaiders(raid, Mathf.Min(strength, 4));
        }

        if (raids.Count == 0) return;
        nextTick = Time.time + TickSeconds;

        string names = "";
        foreach (Raid r in raids) names += (names.Length > 0 ? ", " : "") + r.plot.PlotId;
        GameHUD.Message($"NOČNÝ ÚTOK! Démoni útočia na nechránené parcely: {names}. Postav pri nich vežu alebo ich bráň!", 8f);
        GameHUD.MangaPanel("GRAAAH!", 0.7f);
        GameAudio.Play(GameAudio.Sfx.Alert, player != null ? player.transform.position : Vector3.zero);
        if (player != null && player.IsDemon) MangaDialog.Whisper("ARAKANI", whispers[Random.Range(0, whispers.Length)], 8f);

        if (!introShown)
        {
            introShown = true;
            MangaDialog.Show("MESTO", new[]
            {
                "Slnko zapadlo. Zo zeme sa ozýva škrabot a z lesa prichádzajú tiene.",
                "Démoni útočia na parcely, ktoré nikto nehlieda. Veža, barikáda alebo kasárne do 60 metrov ich ochránia.",
                "Stavba, ktorú v noci zrovnajú so zemou, môže zanechať kontaminovanú pôdu. Tú očistí len drahý rituál."
            });
        }
    }

    void SpawnRaiders(Raid raid, int count)
    {
        // zdroj klonov: voľný nižší alebo stredný démon (aj vypnutý, napr. zo vzdialenej zóny)
        var sources = new List<DemonEnemy>();
        foreach (DemonEnemy d in FindObjectsByType<DemonEnemy>(FindObjectsInactive.Include))
        {
            if (d == null || d.allied || d.tier >= DemonTier.Higher || d.Damage == null || d.Damage.IsDead) continue;
            if (d.GetComponent<SurgeGuest>() != null || d.GetComponent<TamedBeast>() != null || d.GetComponent<Infiltrator>() != null) continue;
            sources.Add(d);
        }
        if (sources.Count == 0) return;

        for (int i = 0; i < count; i++)
        {
            if (CountRaiders() >= MaxRaiders) break;

            // radšej stredný démon (búra budovy); zvyšok nižší
            DemonEnemy source = sources[Random.Range(0, sources.Count)];
            foreach (DemonEnemy d in sources) if (d.tier == DemonTier.Mid && Random.value < 0.4f) { source = d; break; }

            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(14f, 22f);
            Vector3 position = raid.plot.transform.position + new Vector3(ring.x, 0.6f, ring.y);

            GameObject copy = Instantiate(source.gameObject, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            copy.name = $"Nocny_utok_{raid.plot.PlotId}_{i + 1}";
            copy.SetActive(true);
            copy.AddComponent<RaidDemon>();
            raid.demons.Add(copy);
            raid.hadReal = true;
        }
    }

    static int CountRaiders()
    {
        int n = 0;
        foreach (Raid r in raids) foreach (GameObject g in r.demons) if (g != null) n++;
        return n;
    }

    // ---------- Priebeh noci ----------
    void Update()
    {
        if (raids.Count == 0 || Time.time < nextTick || PauseMenu.AtStartup) return;
        nextTick = Time.time + TickSeconds;

        foreach (Raid raid in raids)
        {
            if (raid.repelled || raid.destroyed || raid.plot == null) continue;
            if (raid.plot.State != PlotState.Built) { raid.destroyed = true; continue; }

            int alive = 0;
            foreach (GameObject g in raid.demons)
            {
                if (g == null) continue;
                Damageable dm = g.GetComponent<Damageable>();
                if (dm != null && !dm.IsDead) alive++;
            }

            // Hráč (alebo obrana) pozabíjal skutočných útočníkov
            if (raid.hadReal && alive == 0)
            {
                raid.repelled = true;
                Reputation.Add(raid.plot.Owner, 3f);
                CityManagement.Satisfaction = Mathf.Min(100f, CityManagement.Satisfaction + 3f);
                GameHUD.Message($"Útok na parcelu {raid.plot.PlotId} je odrazený! Mesto ti dôveruje viac.", 5f);
                continue;
            }

            float damage = raid.strength * 5f * Difficulty.RaidDamage;
            if (raid.plot.IsDefended()) damage *= 0.35f;
            if (alive > 0) damage *= 0.5f;   // skutočných démonov rieši aj ich vlastné búranie

            raid.damaged = true;
            raid.plot.TakeDamage(damage, null);
            if (raid.plot.State != PlotState.Built)
            {
                raid.destroyed = true;
                CityManagement.Satisfaction = Mathf.Max(0f, CityManagement.Satisfaction - 6f);
            }
        }
    }

    // ---------- Ráno ----------
    void EndRaids()
    {
        if (raids.Count == 0)
        {
            DecayUnraided();
            return;
        }

        int destroyed = 0, damaged = 0, repelled = 0, contaminatedBefore = BuildPlot.ContaminatedCount;
        foreach (Raid raid in raids)
        {
            foreach (GameObject g in raid.demons) if (g != null) Destroy(g);

            if (raid.plot == null) continue;
            bool lost = raid.destroyed || raid.plot.State == PlotState.Ruined;
            if (lost) destroyed++;
            else if (raid.repelled) repelled++;
            else if (raid.damaged) damaged++;

            raid.plot.ApplyRaidOutcome(raid.damaged && !raid.repelled, lost);
        }
        DecayUnraided();

        int fresh = BuildPlot.ContaminatedCount - contaminatedBefore;
        raids.Clear();
        GameHUD.Message($"Ráno po útoku: zničených {destroyed}, poškodených {damaged}, odrazených {repelled}" + (fresh > 0 ? $", nová kontaminovaná pôda: {fresh}." : "."), 8f);

        if (fresh > 0 && !contaminationShown)
        {
            contaminationShown = true;
            MangaDialog.Show("MESTO", new[]
            {
                "Tam, kde stála stavba, ostala čierna pôda a ostré fialové kryštály. Nič tam nevyrastie.",
                "Pôdu očistí len drahý rituál: peniaze, bleskové kryštály a zlato. Pri parcele otvor ponuku (P).",
                "Stavaj obranné veže, kým sa to nezopakuje!"
            });
        }
    }

    // Parcely, ktoré tejto noci neboli terčom, sa po čase vrátia k pokoju
    static void DecayUnraided()
    {
        foreach (BuildPlot plot in BuildPlot.All)
        {
            bool targeted = false;
            foreach (Raid r in raids) if (r.plot == plot) { targeted = true; break; }
            if (!targeted && plot.RaidedNights > 0) plot.ApplyRaidOutcome(false, false);
        }
    }
}
