using UnityEngine;

public enum WeatherKind { Clear, Rain, Storm }

/// <summary>
/// Počasie: mení sa pri každej zmene fázy dňa (úsvit, deň, súmrak, noc). Búrka zosilní blesky o 10 %,
/// v Bleskovom kaňone o 20 % (blesky bossa Raizena, reťazový blesk klanu Blesku). Dážď a búrka sa kreslia časticami okolo kamery.
/// </summary>
public class WeatherSystem : MonoBehaviour
{
    public static WeatherKind Current { get; private set; }

    private static readonly Vector3 CanyonCenter = new Vector3(-320f, 0f, 200f);
    private const float CanyonRadius = 170f;

    private static Transform playerTransform;

    public static string Label => Current == WeatherKind.Storm ? "Búrka" : Current == WeatherKind.Rain ? "Dážď" : "Jasno";

    /// <summary>Násobok poškodenia bleskov v mieste hráča.</summary>
    public static float LightningFactor
    {
        get
        {
            if (Current != WeatherKind.Storm) return 1f;
            return playerTransform != null && InCanyon(playerTransform.position) ? 1.2f : 1.1f;
        }
    }

    /// <summary>Blesky hráča: počasie × Búrkové srdce (búrka alebo Bleskový kaňon).</summary>
    public static float PlayerLightningFactor
    {
        get
        {
            bool canyon = playerTransform != null && InCanyon(playerTransform.position);
            return LightningFactor * SoulBalance.StormHeartFactor(Current == WeatherKind.Storm || canyon);
        }
    }

    static bool InCanyon(Vector3 p)
    {
        Vector3 d = p - CanyonCenter;
        d.y = 0f;
        return d.sqrMagnitude < CanyonRadius * CanyonRadius;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { Current = WeatherKind.Clear; playerTransform = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<WeatherSystem>() != null) return;
        new GameObject("WeatherSystem").AddComponent<WeatherSystem>();
    }

    private ParticleSystem rain;
    private ParticleSystem.EmissionModule emission;
    private Light flash;
    private float nextFlash, flashLevel, rainWetness;

    /// <summary>Dážď a búrky sú dočasne vypnuté. Pre zapnutie daj false.</summary>
    public const bool RainDisabled = true;

    void OnEnable() => DayNightCycle.OnPhaseChanged += HandlePhase;
    void OnDisable() => DayNightCycle.OnPhaseChanged -= HandlePhase;

    void HandlePhase(DayNightCycle.DayPhase phase)
    {
        if (playerTransform == null)
        {
            PlayerStats p = FindAnyObjectByType<PlayerStats>();
            if (p != null) playerTransform = p.transform;
        }

        // V okolí Bleskového kaňonu sú búrky oveľa častejšie
        float storm = playerTransform != null && (playerTransform.position - CanyonCenter).sqrMagnitude < 280f * 280f ? 0.5f : 0.15f;
        float roll = Random.value;
        WeatherKind next = RainDisabled ? WeatherKind.Clear : roll < storm ? WeatherKind.Storm : roll < storm + 0.25f ? WeatherKind.Rain : WeatherKind.Clear;
        if (next == Current) return;

        Current = next;
        if (next == WeatherKind.Storm) GameHUD.Message("Blíži sa búrka: blesky sú silnejšie.", 3.5f);
        else if (next == WeatherKind.Rain) GameHUD.Message("Začalo pršať.", 2.5f);
        else GameHUD.Message("Obloha sa vyjasnila.", 2.5f);
    }

    void Start()
    {
        PlayerStats p = FindAnyObjectByType<PlayerStats>();
        if (p != null) playerTransform = p.transform;
        BuildRain();
        BuildFlash();
    }

    void BuildRain()
    {
        var go = new GameObject("Dazd");
        go.transform.SetParent(transform, false);
        rain = go.AddComponent<ParticleSystem>();
        rain.Stop();

        var main = rain.main;
        main.loop = true;
        main.startLifetime = 1.1f;
        main.startSpeed = 0f;
        main.startSize = 0.05f;
        main.gravityModifier = 0f;
        main.maxParticles = 2500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new Color(0.7f, 0.8f, 1f, 0.35f);

        emission = rain.emission;
        emission.rateOverTime = 0f;

        var shape = rain.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(34f, 0.5f, 34f);

        var velocity = rain.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.y = -24f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 6f;
        renderer.velocityScale = 0.04f;
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        rain.Play();
    }

    void BuildFlash()
    {
        var go = new GameObject("BleskObloha");
        go.transform.SetParent(transform, false);
        go.transform.rotation = Quaternion.Euler(60f, 30f, 0f);
        flash = go.AddComponent<Light>();
        flash.type = LightType.Directional;
        flash.color = new Color(0.75f, 0.85f, 1f);
        flash.shadows = LightShadows.None;
        flash.intensity = 0f;
    }

    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null && rain != null) rain.transform.position = cam.transform.position + Vector3.up * 14f;

        // Dážď a búrka rozvlnia vodu v jazerách (shader Progeny/Water číta globálnu _PG_Rain)
        rainWetness = Mathf.MoveTowards(rainWetness, Current == WeatherKind.Storm ? 1f : Current == WeatherKind.Rain ? 0.6f : 0f, Time.deltaTime * 0.25f);
        Shader.SetGlobalFloat("_PG_Rain", rainWetness);

        if (rain != null)
            emission.rateOverTime = Current == WeatherKind.Storm ? 1500f : Current == WeatherKind.Rain ? 600f : 0f;

        // Záblesky pri búrke
        if (Current == WeatherKind.Storm && Time.time >= nextFlash)
        {
            nextFlash = Time.time + Random.Range(7f, 18f);
            flashLevel = 2.2f;
            if (cam != null) GameAudio.Play(GameAudio.Sfx.Lightning, cam.transform.position + Random.onUnitSphere * 40f);
        }
        if (flash != null)
        {
            flashLevel = Mathf.MoveTowards(flashLevel, 0f, Time.deltaTime * 9f);
            flash.intensity = flashLevel;
        }
    }
}
