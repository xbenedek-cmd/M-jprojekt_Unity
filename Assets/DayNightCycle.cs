using System;
using UnityEngine;

/// <summary>
/// Cyklus dňa a noci pre Progeny of the Ashes.
/// DEŇ -> SÚMRAK -> NOC -> RÁNO, počíta dni a fázy mesiaca (Lunar Corruption).
/// Spolupracuje s Reversed Perception (kláves E) cez SetReversedPerception().
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    public enum DayPhase { Dawn, Day, Dusk, Night }
    public enum MoonPhase { NewMoon, Waxing, FullMoon, Waning }

    // Udalosti, na ktoré sa budú pripájať ďalšie systémy (démoni, NPC, UI...)
    public static event Action<DayPhase> OnPhaseChanged;
    public static event Action<int> OnNewDay;

    [Header("Odkazy")]
    [SerializeField] private Light sun;                 // Directional Light

    [Header("Čas")]
    [Tooltip("Dĺžka jedného celého dňa v reálnych sekundách")]
    [SerializeField] private float dayLengthSeconds = 240f;
    [Range(0f, 24f)][SerializeField] private float startHour = 8f;
    [SerializeField] private float timeMultiplier = 1f; // na testovanie (napr. 10)
    [Tooltip("2 = čas plynie dvakrát pomalšie (deň trvá dvakrát dlhšie)")]
    [SerializeField] private float timeSlowFactor = 2f;

    [Header("Hranice fáz (hodiny)")]
    [SerializeField] private float dawnStart = 5f;
    [SerializeField] private float dayStart = 7f;
    [SerializeField] private float duskStart = 18f;
    [SerializeField] private float nightStart = 20f;

    [Header("Slnko")]
    [SerializeField] private Gradient sunColor;
    [SerializeField]
    private AnimationCurve sunIntensity =
        new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.25f, 0.2f),
            new Keyframe(0.5f, 1.2f), new Keyframe(0.75f, 0.2f), new Keyframe(1f, 0f));
    [SerializeField] private Gradient ambientColor;

    [Header("Mesiac")]
    [Tooltip("Po koľkých dňoch sa fáza mesiaca posunie")]
    [SerializeField] private int daysPerMoonPhase = 2;

    [Header("Mesiac - vplyv na démonov")]
    [SerializeField] private float fullMoonDamage = 1.3f;     // úplnok: silnejšie údery
    [SerializeField] private float fullMoonSpeed = 1.15f;
    [SerializeField] private float fullMoonAggro = 1.4f;      // pach krvi je intenzívny - vidia ďalej
    [SerializeField] private float newMoonDamage = 0.75f;     // nov: slabší démoni
    [SerializeField] private float newMoonSpeed = 0.9f;

    [Header("Slnko a tieň (nižší a strední démoni)")]
    [Range(0f, 1f)][SerializeField] private float twilightSunStrength = 0.4f;   // úsvit a súmrak

    [Header("Reversed Perception")]
    [SerializeField] private Color perceptionTint = new Color(1f, 0.25f, 0.3f);
    [SerializeField] private float perceptionBlendSpeed = 3f;
    [SerializeField] private float perceptionSunBoost = 0.8f;   // min. intenzita slnka pri E
    [SerializeField] private float perceptionFogDensity = 0.06f;

    [Header("Krvavé videnie")]
    [SerializeField] private Color bloodVisionTint = new Color(0.7f, 0.02f, 0.08f);
    [SerializeField] private float bloodFogBoost = 1.5f;

    public float TimeOfDay { get; private set; }
    public int Day { get; private set; } = 1;
    public DayPhase CurrentPhase { get; private set; }
    public MoonPhase CurrentMoon => (MoonPhase)((Day / Mathf.Max(1, daysPerMoonPhase)) % 4);
    public bool IsNight => CurrentPhase == DayPhase.Night;

    // Hranice fáz pre HUD (graf dňa)
    public float DawnStartHour => dawnStart;
    public float DayStartHour => dayStart;
    public float DuskStartHour => duskStart;
    public float NightStartHour => nightStart;

    // ---------- Mesiac: vplyv na démonov ----------
    public float DemonDamageMultiplier =>
        CurrentMoon == MoonPhase.FullMoon ? fullMoonDamage : CurrentMoon == MoonPhase.NewMoon ? newMoonDamage : 1f;
    public float DemonSpeedMultiplier =>
        CurrentMoon == MoonPhase.FullMoon ? fullMoonSpeed : CurrentMoon == MoonPhase.NewMoon ? newMoonSpeed : 1f;
    public float DemonAggroMultiplier => CurrentMoon == MoonPhase.FullMoon ? fullMoonAggro : 1f;

    // ---------- Slnko a tieň ----------
    /// <summary>Sila slnka pre slnečné poškodenie: deň 1, úsvit/súmrak slabšie, noc 0.</summary>
    public float SunStrength
    {
        get
        {
            switch (CurrentPhase)
            {
                case DayPhase.Day: return 1f;
                case DayPhase.Dawn:
                case DayPhase.Dusk: return twilightSunStrength;
                default: return 0f;
            }
        }
    }

    private static readonly RaycastHit[] sunHits = new RaycastHit[16];

    /// <summary>Svieti na dané miesto slnko? (nič ho nezakrýva) Postavy tieň nerobia.</summary>
    public bool IsSunlit(Vector3 pos)
    {
        if (SunStrength <= 0f) return false;
        if (sun == null) return true;

        Vector3 toSun = -sun.transform.forward;
        if (toSun.y <= 0.02f) return false;      // slnko pod obzorom

        int n = Physics.RaycastNonAlloc(pos + Vector3.up, toSun, sunHits, 150f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = sunHits[i].collider;
            if (c.GetComponentInParent<Damageable>() != null || c.GetComponentInParent<PlayerStats>() != null)
                continue;
            return false;       // niečo zakrýva slnko = tieň
        }
        return true;
    }

    private bool reversedPerception;
    private float perceptionBlend; // 0 = normálne vnímanie, 1 = obrátené
    private Material skyInstance;
    private bool bloodVision;
    private float bloodBlend;

    private void Reset()
    {
        sun = FindAnyObjectByType<Light>();
        SetDefaultGradients();
    }

    private void Awake()
    {
        if (sun == null) sun = FindAnyObjectByType<Light>();
        if (sunColor == null || sunColor.colorKeys.Length == 0 ||
            ambientColor == null || ambientColor.colorKeys.Length == 0)
            SetDefaultGradients();

        TimeOfDay = startHour;
        CurrentPhase = EvaluatePhase(TimeOfDay);

        // Bez tohto sa RenderSettings.ambientLight ignoruje (zdroj = Skybox)
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

        // Kópia skyboxu, aby sme nemenili originálny asset
        if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_SkyTint"))
        {
            skyInstance = new Material(RenderSettings.skybox);
            RenderSettings.skybox = skyInstance;
        }
    }

    private void Start()
    {
        ApplyLighting();
        OnPhaseChanged?.Invoke(CurrentPhase);
    }

    private void Update()
    {
        AdvanceTime();
        UpdatePhase();

        perceptionBlend = Mathf.MoveTowards(
            perceptionBlend, reversedPerception ? 1f : 0f, perceptionBlendSpeed * Time.deltaTime);

        bloodBlend = Mathf.MoveTowards(
            bloodBlend, bloodVision ? 1f : 0f, perceptionBlendSpeed * Time.deltaTime);

        ApplyLighting();
    }

    /// <summary>Volaj z PerceptionToggle pri stlačení E.</summary>
    public void SetReversedPerception(bool value) => reversedPerception = value;

    /// <summary>Krvavé videnie: svet zhustne a zhrubne do tmavej červenej.</summary>
    public void SetBloodVision(bool value) => bloodVision = value;

    /// <summary>Nastaví deň a hodinu (načítanie uloženého stavu sveta); udalosť nového dňa sa nespúšťa.</summary>
    public void SetClock(int day, float hour)
    {
        Day = Mathf.Max(1, day);
        TimeOfDay = Mathf.Repeat(hour, 24f);
        CurrentPhase = EvaluatePhase(TimeOfDay);
    }

    private void AdvanceTime()
    {
        float hoursPerSecond = 24f / dayLengthSeconds;
        TimeOfDay += hoursPerSecond * timeMultiplier / Mathf.Max(0.1f, timeSlowFactor) * Time.deltaTime;

        if (TimeOfDay >= 24f)
        {
            TimeOfDay -= 24f;
            Day++;
            OnNewDay?.Invoke(Day);
        }
    }

    private void UpdatePhase()
    {
        DayPhase newPhase = EvaluatePhase(TimeOfDay);
        if (newPhase == CurrentPhase) return;

        CurrentPhase = newPhase;
        OnPhaseChanged?.Invoke(CurrentPhase);
    }

    private DayPhase EvaluatePhase(float h)
    {
        if (h >= nightStart || h < dawnStart) return DayPhase.Night;
        if (h >= duskStart) return DayPhase.Dusk;
        if (h >= dayStart) return DayPhase.Day;
        return DayPhase.Dawn;
    }

    /// <summary>
    /// Slnečný čas 0..1 (0,25 = východ, 0,5 = zenit, 0,75 = západ). Slnko vychádza na začiatku úsvitu a zapadá
    /// na konci súmraku, takže obe fázy majú nízke slnko a dlhé tiene (predtým zapadalo už na začiatku súmraku).
    /// </summary>
    public float SolarTime
    {
        get
        {
            float h = TimeOfDay;
            if (h >= dawnStart && h < nightStart)
                return 0.25f + 0.5f * (h - dawnStart) / Mathf.Max(0.01f, nightStart - dawnStart);

            float nightLength = 24f - nightStart + dawnStart;
            float sinceSunset = h >= nightStart ? h - nightStart : h + 24f - nightStart;
            return Mathf.Repeat(0.75f + 0.5f * sinceSunset / Mathf.Max(0.01f, nightLength), 1f);
        }
    }

    private void ApplyLighting()
    {
        float t = SolarTime;
        Color tint = Color.Lerp(perceptionTint, bloodVisionTint, bloodBlend);

        float angle = t * 360f - 90f;
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(angle, 170f, 0f);

            Color c = sunColor.Evaluate(t);
            sun.color = Color.Lerp(c, tint, perceptionBlend * 0.85f);

            // Pod obzorom slnko nesvieti (inak by osvetľovalo svet zospodu); výnimkou je démonické vnímanie
            float aboveHorizon = Mathf.Clamp01(-sun.transform.forward.y * 9f);
            float intensity = Mathf.Max(0f, sunIntensity.Evaluate(t)) * aboveHorizon;
            sun.intensity = Mathf.Max(intensity, perceptionBlend * perceptionSunBoost);
        }

        Color ambient = ambientColor.Evaluate(t);
        RenderSettings.ambientLight = Color.Lerp(ambient, tint * 0.8f, perceptionBlend * 0.8f);

        // Obloha
        if (skyInstance != null)
        {
            skyInstance.SetColor("_SkyTint",
                Color.Lerp(new Color(0.5f, 0.5f, 0.5f), tint, perceptionBlend));
        }

        // Krvavá hmla. Hmlu sveta riadi cyklus len počas vnímania; inak ju necháva iným systémom
        // (popolová búrka, svätyňa v menu), ktorým ju predtým každý snímok vypínal.
        if (perceptionBlend > 0.01f)
        {
            if (!fogOwned)
            {
                fogOwned = true;
                fogWasOn = RenderSettings.fog;
                fogMemoryMode = RenderSettings.fogMode;
                fogMemoryColor = RenderSettings.fogColor;
                fogMemoryDensity = RenderSettings.fogDensity;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(Color.black, tint * 0.6f, perceptionBlend);
            RenderSettings.fogDensity = perceptionFogDensity * (1f + bloodBlend * bloodFogBoost) * perceptionBlend;
        }
        else if (fogOwned)
        {
            fogOwned = false;
            RenderSettings.fog = fogWasOn;
            RenderSettings.fogMode = fogMemoryMode;
            RenderSettings.fogColor = fogMemoryColor;
            RenderSettings.fogDensity = fogMemoryDensity;
        }
    }

    /// <summary>Hmlu sveta práve riadi cyklus (démonické vnímanie).</summary>
    public bool OwnsFog => fogOwned;

    private bool fogOwned, fogWasOn;
    private FogMode fogMemoryMode;
    private Color fogMemoryColor;
    private float fogMemoryDensity;

    private void SetDefaultGradients()
    {
        sunColor = new Gradient();
        sunColor.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.25f, 0.3f, 0.6f), 0f),     // polnoc
                new GradientColorKey(new Color(1f, 0.55f, 0.35f), 0.27f),   // východ
                new GradientColorKey(new Color(1f, 0.96f, 0.88f), 0.5f),    // poludnie
                new GradientColorKey(new Color(1f, 0.45f, 0.3f), 0.75f),    // západ
                new GradientColorKey(new Color(0.25f, 0.3f, 0.6f), 1f)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

        ambientColor = new Gradient();
        ambientColor.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.05f, 0.06f, 0.14f), 0f),
                new GradientColorKey(new Color(0.55f, 0.4f, 0.4f), 0.27f),
                new GradientColorKey(new Color(0.6f, 0.65f, 0.7f), 0.5f),
                new GradientColorKey(new Color(0.5f, 0.3f, 0.35f), 0.75f),
                new GradientColorKey(new Color(0.05f, 0.06f, 0.14f), 1f)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
    }

    [ContextMenu("Preskočiť na súmrak")]
    private void SkipToDusk() => TimeOfDay = duskStart;

    [ContextMenu("Preskočiť na noc")]
    private void SkipToNight() => TimeOfDay = nightStart;
}