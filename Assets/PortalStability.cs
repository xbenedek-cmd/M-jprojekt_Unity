using UnityEngine;

/// <summary>
/// Stabilita portálov. Každý deň klesá podľa podozrenia mesta (poplach) a energie, ktorú čerpá korporácia (jej postavené stavby),
/// a sama sa trochu obnovuje. Pri nízkej stabilite portál na 1–2 dni vypadne (prechod zatvorený) a vojaci z Megakorporácie sa
/// môžu preliať do mesta, čo zvýši podozrenie (a tým aj tlak na civilistov). Hráč portál stabilizuje kryštálmi pri portáli (kláves 7).
/// </summary>
public class PortalStability : MonoBehaviour
{
    public static PortalStability Instance { get; private set; }
    /// <summary>0..100 (100 = stabilný).</summary>
    public static float Stability => Instance != null ? Instance.stability : 100f;
    public static bool Outage => Instance != null && Instance.outageUntilDay >= 0;

    [SerializeField] private float dailyRecovery = 10f;
    [SerializeField] private float baseDrain = 4f;
    [SerializeField] private float corpBuildingDrain = 4f;
    [SerializeField] private KeyCode stabilizeKey = KeyCode.Alpha7;
    [SerializeField] private int stabilizeCrystals = 2;
    [SerializeField] private float stabilizeAmount = 40f;
    [SerializeField] private float stabilizeRange = 6f;

    private float stability = 100f;
    private int outageUntilDay = -1;
    private PlayerStats player;
    private Inventory inventory;
    private PortalGate nearGate;
    private GUIStyle hint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<PortalStability>() != null) return;
        new GameObject("PortalStability").AddComponent<PortalStability>();
    }

    void Awake() => Instance = this;

    void OnEnable() => DayNightCycle.OnNewDay += HandleNewDay;
    void OnDisable() => DayNightCycle.OnNewDay -= HandleNewDay;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---------- Denný vývoj ----------
    void HandleNewDay(int day)
    {
        int corpBuildings = 0;
        foreach (BuildPlot plot in BuildPlot.All)
            if (plot.State == PlotState.Built && plot.Building != null && plot.Building.faction == FactionId.Megacorp) corpBuildings++;

        float drain = baseDrain + Reputation.Suspicion / 10f + corpBuildings * corpBuildingDrain;
        stability = Mathf.Clamp(stability - drain + dailyRecovery, 0f, 100f);

        if (outageUntilDay >= 0 && day >= outageUntilDay && stability > 25f) EndOutage();

        if (outageUntilDay < 0 && stability < 40f && Random.value < (40f - stability) / 60f + 0.15f) StartOutage(day);
        if (stability < 30f && Random.value < 0.5f) Spillover();
    }

    void StartOutage(int day)
    {
        outageUntilDay = day + Random.Range(1, 3);
        SetPortalsOpen(false);
        GameHUD.Message("Portál vypadol: prechod medzi zónami je zatvorený. Stabilizuj ho kryštálmi (kláves 7 pri portáli).", 5f);
    }

    void EndOutage()
    {
        outageUntilDay = -1;
        SetPortalsOpen(true);
        GameHUD.Message("Portál sa znova rozbehol.", 3f);
    }

    static void SetPortalsOpen(bool open)
    {
        foreach (PortalGate gate in PortalGate.All) gate.SetOpen(open);
    }

    // Vojaci z korporácie prejdú cez nestabilný portál do mesta: pátrajú po porušovateľoch (podozrenie mesta rastie)
    void Spillover()
    {
        PortalGate cityGate = null;
        foreach (PortalGate gate in PortalGate.All)
            if (gate.ZoneName == "Megakorporácia") { cityGate = gate; break; }

        GameObject template = null;
        foreach (ZonePopulation zone in FindObjectsByType<ZonePopulation>())
            if (zone.SoldierTemplate != null) { template = zone.SoldierTemplate; break; }

        if (cityGate == null || template == null) return;

        int count = Random.Range(2, 5);
        for (int i = 0; i < count; i++)
        {
            Vector3 position = cityGate.transform.position + cityGate.transform.forward * (4f + i * 1.5f) + Vector3.up * 0.1f;
            position += new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));

            GameObject soldier = Instantiate(template, position, template.transform.rotation, template.transform.parent);
            soldier.name = $"Prebehlík_{i + 1}";
            soldier.AddComponent<TemporaryVisitor>();
            soldier.SetActive(true);
        }

        Reputation.AddSuspicion(15f);
        GameHUD.Message($"Cez nestabilný portál sa do mesta prelialo {count} vojakov. Podozrenie mesta stúpa.", 5f);
    }

    // ---------- Stabilizácia ----------
    void Update()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null) inventory = player.GetComponent<Inventory>();
            return;
        }

        nearGate = player.IsDead || Healer.MenuOpen || stability >= 80f && outageUntilDay < 0 ? null : FindNearGate();
        if (nearGate != null && GameInput.GetKeyDown(stabilizeKey)) Stabilize();
    }

    PortalGate FindNearGate()
    {
        PortalGate best = null;
        float bestDistance = stabilizeRange * stabilizeRange;
        foreach (PortalGate gate in PortalGate.All)
        {
            float d = (gate.transform.position - player.transform.position).sqrMagnitude;
            if (d < bestDistance) { best = gate; bestDistance = d; }
        }
        return best;
    }

    void Stabilize()
    {
        if (inventory == null || inventory.CountOf(ItemCatalog.Crystal) < stabilizeCrystals)
        {
            GameHUD.Message($"Na stabilizáciu treba {stabilizeCrystals}× Bleskový kryštál.", 3f);
            return;
        }

        inventory.TryRemove(ItemCatalog.Crystal, stabilizeCrystals);
        stability = Mathf.Min(100f, stability + stabilizeAmount);
        if (outageUntilDay >= 0 && stability > 25f) EndOutage();
        GameHUD.Message($"Portál stabilizovaný ({stability:0} %).", 3f);
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (nearGate == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(nearGate.transform.position + Vector3.up * 3f);
        if (p.z <= 0f) return;

        if (hint == null)
        {
            hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            hint.normal.textColor = new Color(1f, 0.85f, 0.3f);
        }
        GUI.Label(new Rect(p.x - 200f, Screen.height - p.y - 14f, 400f, 28f),
            $"Portál {stability:0} % – [{stabilizeKey}] stabilizovať ({stabilizeCrystals}× kryštál)", hint);
    }

    // ---------- Ukladanie ----------
    public void Export(out float stabilityValue, out int outageDay)
    {
        stabilityValue = stability;
        outageDay = outageUntilDay;
    }

    public void Import(float stabilityValue, int outageDay)
    {
        stability = Mathf.Clamp(stabilityValue, 0f, 100f);
        outageUntilDay = outageDay;
        SetPortalsOpen(outageUntilDay < 0);
    }
}

/// <summary>Postava, ktorá prišla cez portál a po čase zase zmizne (nie je súčasťou poolu obyvateľov).</summary>
public class TemporaryVisitor : MonoBehaviour
{
    [SerializeField] private float lifetimeSeconds = 240f;

    void Start() => Destroy(gameObject, lifetimeSeconds);
}
