using UnityEngine;

/// <summary>
/// Popolová búrka: občas (každých ~6–12 min) padne hustý popol. Zníži viditeľnosť (hmla), bez Filtračnej masky v inventári ubúda zdravie
/// a psychika. Chráni vnútro bane. Masku vyrobíš v dielni odboja (kláves 8) a nasadíš ju v Inventári na hlavu.
/// </summary>
public class AshStorm : MonoBehaviour
{
    public static bool Active { get; private set; }
    /// <summary>Hráč je teraz vonku a nechránený (žiadna maska).</summary>
    public static bool ExposedNow { get; private set; }

    private PlayerStats player;
    private Inventory inventory;
    private float nextStorm, endsAt, nextHurt, density, fogMemoryDensity;
    private bool hadFog, fogRemembered;
    private Color fogMemoryColor;
    private FogMode fogMemoryMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { Active = false; ExposedNow = false; }

    /// <summary>Hráč stojí pri postavenom Podzemnom úkryte (10 m): chráni pred búrkou a obnovuje psychiku.</summary>
    public static bool IsSheltered(Vector3 position)
    {
        BuildPlot plot = BuildPlot.NearestBuilt(position, 10f);
        return plot != null && plot.Building != null && plot.Building.id == BuildingCatalog.Shelter;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<AshStorm>() != null) return;
        new GameObject("AshStorm").AddComponent<AshStorm>();
    }

    void Start() => nextStorm = Time.time + Random.Range(240f, 420f);

    void OnDestroy() { if (fogRemembered) RestoreFog(); Active = false; ExposedNow = false; }

    void Update()
    {
        if (PauseMenu.AtStartup || Time.timeScale == 0f) return;
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            inventory = player != null ? player.GetComponent<Inventory>() : null;
            return;
        }

        if (!Active && Time.time >= nextStorm && !player.IsDead && !MineSystem.Inside && !Prologue.Active) Begin();
        if (Active && Time.time >= endsAt) End();

        // Hmla pomaly nabieha a ustupuje
        float target = Active ? 0.022f : 0f;
        density = Mathf.MoveTowards(density, target, Time.deltaTime * 0.003f);
        if (fogRemembered)
        {
            RenderSettings.fogDensity = density;
            // Cyklus dňa si počas démonického vnímania hmlu požičiava; po ňom sa búrka znova prihlási
            if (Active && !PerceptionToggle.IsDemonVision && !RenderSettings.fog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = new Color(0.34f, 0.31f, 0.29f);
            }
            if (!Active && density <= 0.0005f) RestoreFog();
        }

        bool sheltered = MineSystem.Inside || IsSheltered(player.transform.position);
        bool masked = Equipment.IsWearing(ItemCatalog.Mask);
        ExposedNow = Active && !player.IsDead && !sheltered && !masked;
        if (Active && masked && !sheltered && !player.IsDead) Equipment.Wear(EquipSlot.Head, Time.deltaTime);   // filter sa opotrebúva
        if (ExposedNow)
        {
            GameHUD.Prompt("Popolová búrka: nasaď Filtračnú masku (Inventár → Oblečenie), inak ti ubúda zdravie. Maska sa vyrába v dielni odboja.");
            if (Time.time >= nextHurt)
            {
                nextHurt = Time.time + 2f;
                player.TakeDamage(1.5f, DamageSource.Starvation);   // obchádza úhyb aj brnenie: jed vo vzduchu
            }
        }
    }

    void Begin()
    {
        Active = true;
        endsAt = Time.time + Random.Range(90f, 150f);
        if (!fogRemembered)
        {
            fogRemembered = true;
            hadFog = RenderSettings.fog;
            fogMemoryMode = RenderSettings.fogMode;
            fogMemoryColor = RenderSettings.fogColor;
            fogMemoryDensity = RenderSettings.fogDensity;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.34f, 0.31f, 0.29f);
        GameHUD.Message("Popolová búrka! Zníženie viditeľnosti. Nasaď Filtračnú masku alebo sa ukry.", 6f);
    }

    void End()
    {
        Active = false;
        nextStorm = Time.time + Random.Range(360f, 720f);
        GameHUD.Message("Popolová búrka ustupuje.", 4f);
    }

    void RestoreFog()
    {
        RenderSettings.fog = hadFog;
        RenderSettings.fogMode = fogMemoryMode;
        RenderSettings.fogColor = fogMemoryColor;
        RenderSettings.fogDensity = fogMemoryDensity;
        fogRemembered = false;
    }
}
