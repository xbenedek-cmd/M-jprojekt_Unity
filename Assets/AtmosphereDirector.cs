using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Celá vizuálna atmosféra hry na jednom mieste (built-in RP, bez externých balíkov):
///  - nálada podľa dennej doby (hmla, bloom, expozícia, farby, zrno, slnečné lúče, sila slnka, okolité svetlo, mesiac),
///  - démonická forma / obrátené vnímanie a krvavé videnie (červený posun, vinetácia, aberácia, pulz podľa krvi, zvýraznené ciele),
///  - neóny na cyberpunk budovách (zapnú sa po zotmení, pár bliká alebo je pokazených),
///  - dym nad skladmi a nastavenie kaskádových tieňov.
/// Patrí na hlavnú kameru. Hodnoty sa dajú meniť v Inspectore aj počas hry.
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class AtmosphereDirector : MonoBehaviour
{
    [Serializable]
    public class Mood
    {
        [Header("Hmla")]
        [ColorUsage(false, true)] public Color fogColor = new Color(0.6f, 0.7f, 0.8f);
        [Tooltip("Hustota do diaľky (viditeľnosť). 0,002 = čistý vzduch, 0,015 = hustá hmla")]
        [Range(0f, 0.05f)] public float fogDistance = 0.0015f;
        [Tooltip("Hustota vrstvy pri zemi")]
        [Range(0f, 0.15f)] public float groundFog;
        [Tooltip("Výška vrstvy pri zemi v metroch")]
        [Range(0.5f, 40f)] public float groundFogHeight = 4f;
        [Range(0f, 1f)] public float fogMaxOpacity = 0.9f;
        [Tooltip("Opar na obzore oblohy")]
        [Range(0f, 1f)] public float skyHaze = 0.3f;

        [Header("Bloom")]
        [Range(0f, 3f)] public float bloom = 0.25f;
        [Range(0.2f, 3f)] public float bloomThreshold = 1.1f;

        [Header("Obraz")]
        [Range(0.2f, 3f)] public float exposure = 1f;
        [Range(0f, 2f)] public float saturation = 1f;
        [Range(0.5f, 1.6f)] public float contrast = 1f;
        [ColorUsage(false, true)] public Color colorFilter = Color.white;
        [Range(0f, 1f)] public float vignette = 0.15f;
        [Range(0f, 0.4f)] public float grain;

        [Header("Slnečné lúče")]
        [Range(0f, 3f)] public float shafts;
        [ColorUsage(false, true)] public Color shaftColor = new Color(1f, 0.8f, 0.6f);

        [Header("Svetlo")]
        [Tooltip("Násobok sily slnka z DayNightCycle")]
        [Range(0f, 6f)] public float sunIntensity = 1f;
        [ColorUsage(false, true)] public Color sunTint = Color.white;
        [Tooltip("Násobky okolitého svetla z DayNightCycle: zhora / z bokov / zdola")]
        [ColorUsage(false, true)] public Color ambientSky = Color.white;
        [ColorUsage(false, true)] public Color ambientEquator = new Color(0.8f, 0.8f, 0.8f);
        [ColorUsage(false, true)] public Color ambientGround = new Color(0.5f, 0.5f, 0.5f);

        public void Blend(Mood a, Mood b, float t)
        {
            fogColor = Color.LerpUnclamped(a.fogColor, b.fogColor, t);
            fogDistance = Mathf.LerpUnclamped(a.fogDistance, b.fogDistance, t);
            groundFog = Mathf.LerpUnclamped(a.groundFog, b.groundFog, t);
            groundFogHeight = Mathf.LerpUnclamped(a.groundFogHeight, b.groundFogHeight, t);
            fogMaxOpacity = Mathf.LerpUnclamped(a.fogMaxOpacity, b.fogMaxOpacity, t);
            skyHaze = Mathf.LerpUnclamped(a.skyHaze, b.skyHaze, t);
            bloom = Mathf.LerpUnclamped(a.bloom, b.bloom, t);
            bloomThreshold = Mathf.LerpUnclamped(a.bloomThreshold, b.bloomThreshold, t);
            exposure = Mathf.LerpUnclamped(a.exposure, b.exposure, t);
            saturation = Mathf.LerpUnclamped(a.saturation, b.saturation, t);
            contrast = Mathf.LerpUnclamped(a.contrast, b.contrast, t);
            colorFilter = Color.LerpUnclamped(a.colorFilter, b.colorFilter, t);
            vignette = Mathf.LerpUnclamped(a.vignette, b.vignette, t);
            grain = Mathf.LerpUnclamped(a.grain, b.grain, t);
            shafts = Mathf.LerpUnclamped(a.shafts, b.shafts, t);
            shaftColor = Color.LerpUnclamped(a.shaftColor, b.shaftColor, t);
            sunIntensity = Mathf.LerpUnclamped(a.sunIntensity, b.sunIntensity, t);
            sunTint = Color.LerpUnclamped(a.sunTint, b.sunTint, t);
            ambientSky = Color.LerpUnclamped(a.ambientSky, b.ambientSky, t);
            ambientEquator = Color.LerpUnclamped(a.ambientEquator, b.ambientEquator, t);
            ambientGround = Color.LerpUnclamped(a.ambientGround, b.ambientGround, t);
        }

        public static Mood MakeDay() => new Mood
        {
            fogColor = new Color(0.62f, 0.74f, 0.88f), fogDistance = 0.0012f, groundFog = 0f, groundFogHeight = 6f,
            fogMaxOpacity = 0.85f, skyHaze = 0.25f,
            bloom = 0.22f, bloomThreshold = 1.15f,
            exposure = 1.25f, saturation = 1.08f, contrast = 1.04f, colorFilter = new Color(1.03f, 0.99f, 0.92f),
            vignette = 0.14f, grain = 0f,
            shafts = 0.15f, shaftColor = new Color(1f, 0.92f, 0.78f),
            sunIntensity = 1.1f, sunTint = new Color(1f, 0.96f, 0.88f),
            ambientSky = new Color(1.05f, 1.15f, 1.35f), ambientEquator = new Color(0.9f, 0.88f, 0.85f),
            ambientGround = new Color(0.5f, 0.44f, 0.38f)
        };

        public static Mood MakeDusk() => new Mood
        {
            fogColor = new Color(0.62f, 0.3f, 0.14f), fogDistance = 0.0022f, groundFog = 0.009f, groundFogHeight = 2.2f,
            fogMaxOpacity = 0.9f, skyHaze = 0.75f,
            bloom = 0.45f, bloomThreshold = 1.25f,
            exposure = 1.05f, saturation = 1.18f, contrast = 1.08f, colorFilter = new Color(1.05f, 0.93f, 0.82f),
            vignette = 0.24f, grain = 0.02f,
            shafts = 0.55f, shaftColor = new Color(1f, 0.58f, 0.26f),
            sunIntensity = 3.2f, sunTint = new Color(1f, 0.8f, 0.6f),
            ambientSky = new Color(1.1f, 0.8f, 0.9f), ambientEquator = new Color(1f, 0.75f, 0.62f),
            ambientGround = new Color(0.45f, 0.3f, 0.3f)
        };

        public static Mood MakeDawn() => new Mood
        {
            fogColor = new Color(0.6f, 0.42f, 0.38f), fogDistance = 0.003f, groundFog = 0.018f, groundFogHeight = 3.5f,
            fogMaxOpacity = 0.9f, skyHaze = 0.6f,
            bloom = 0.45f, bloomThreshold = 1.2f,
            exposure = 1.15f, saturation = 1.02f, contrast = 1.04f, colorFilter = new Color(1.04f, 0.94f, 0.9f),
            vignette = 0.2f, grain = 0.02f,
            shafts = 0.5f, shaftColor = new Color(1f, 0.7f, 0.5f),
            sunIntensity = 2.6f, sunTint = new Color(1f, 0.85f, 0.75f),
            ambientSky = new Color(1f, 0.9f, 1.05f), ambientEquator = new Color(1f, 0.78f, 0.7f),
            ambientGround = new Color(0.45f, 0.36f, 0.36f)
        };

        public static Mood MakeNight() => new Mood
        {
            fogColor = new Color(0.035f, 0.07f, 0.16f), fogDistance = 0.0095f, groundFog = 0.012f, groundFogHeight = 5f,
            fogMaxOpacity = 0.93f, skyHaze = 0.7f,
            bloom = 0.75f, bloomThreshold = 0.85f,
            exposure = 1.3f, saturation = 0.86f, contrast = 1.1f, colorFilter = new Color(0.72f, 0.86f, 1.2f),
            vignette = 0.36f, grain = 0.1f,
            shafts = 0f, shaftColor = new Color(0.5f, 0.6f, 1f),
            sunIntensity = 0f, sunTint = Color.white,
            ambientSky = new Color(1.6f, 2.1f, 3.2f), ambientEquator = new Color(1.1f, 1.4f, 2.3f),
            ambientGround = new Color(0.6f, 0.7f, 1.1f)
        };
    }

    public enum PreviewLook { Podľa_hry, Ľudské, Démonické, Krvavé_videnie }

    private enum NeonMode : byte { Steady, Flicker, Broken }

    private class NeonSign
    {
        public Renderer renderer;
        public Renderer[] extra;   // ďalšie trubice toho istého nápisu (rám)
        public Light light;
        public Transform anchor;
        public Color color;
        public float threshold;
        public float seed;
        public float shown = -1f;
        public NeonMode mode;
        public bool existing;      // už existujúci objekt mesta (Standard materiál s emisiou)
    }

    [Header("Odkazy (nájdu sa samy)")]
    [SerializeField] private DayNightCycle dayNight;
    [SerializeField] private Light sun;

    [Header("Náhľad (ladenie v Play móde)")]
    [Tooltip("-1 = čas beží podľa hry; inak sa hodiny držia na tejto hodnote")]
    [Range(-1f, 24f)] [SerializeField] private float previewHour = -1f;
    [SerializeField] private PreviewLook previewLook = PreviewLook.Podľa_hry;
    [Tooltip("-1 = krv podľa hráča; inak 0..1 pre skúšku pulzu")]
    [Range(-1f, 1f)] [SerializeField] private float previewBlood = -1f;

    [Header("Hodiny prechodov")]
    [SerializeField] private float nightUntil = 4.4f;
    [SerializeField] private float dawnPeak = 5.7f;
    [SerializeField] private float dayFrom = 7.5f;
    [SerializeField] private float dayUntil = 17f;
    [SerializeField] private float duskPeak = 19.2f;
    [SerializeField] private float nightFrom = 20.7f;

    [Header("Nálady")]
    [SerializeField] private Mood day = Mood.MakeDay();
    [SerializeField] private Mood dusk = Mood.MakeDusk();
    [SerializeField] private Mood night = Mood.MakeNight();
    [SerializeField] private Mood dawn = Mood.MakeDawn();

    [Header("Hmla - spoločné")]
    [Tooltip("Do tejto vzdialenosti od kamery hmla do diaľky nepôsobí (hráč ostane čistý)")]
    [SerializeField] private float fogStartDistance = 10f;
    [Tooltip("Zem hmly = výška hráča + tento posun")]
    [SerializeField] private float fogGroundOffset = -1.2f;
    [Range(0f, 1f)] [SerializeField] private float fogNoise = 0.55f;
    [SerializeField] private float fogNoiseScale = 0.045f;
    [Tooltip("Ako veľmi hmla smerom k slnku preberá farbu slnka")]
    [Range(0f, 1f)] [SerializeField] private float fogSunScatter = 0.3f;

    [Header("Bloom a lúče - spoločné")]
    [Range(1, 7)] [SerializeField] private int bloomIterations = 5;
    [Range(0.05f, 1f)] [SerializeField] private float bloomSoftKnee = 0.5f;
    [Range(0.2f, 1.5f)] [SerializeField] private float shaftRadius = 0.85f;
    [Range(0f, 1f)] [SerializeField] private float tonemapping = 1f;

    [Header("Mesiac")]
    [SerializeField] private bool moonLight = true;
    [ColorUsage(false, true)] [SerializeField] private Color moonColor = new Color(0.45f, 0.62f, 1f);
    [Range(0f, 2f)] [SerializeField] private float moonIntensity = 0.6f;
    [SerializeField] private Vector2 moonAngles = new Vector2(48f, 215f);
    [SerializeField] private bool moonShadows = true;

    [Header("Démonická forma a obrátené vnímanie")]
    [Tooltip("Sila efektu v podobe démona bez zapnutého vnímania")]
    [Range(0f, 1f)] [SerializeField] private float demonFormWeight = 0.5f;
    [ColorUsage(false, true)] [SerializeField] private Color demonColor = new Color(1.25f, 0.42f, 0.4f);
    [Range(0f, 1f)] [SerializeField] private float demonRedShift = 0.7f;
    [Range(0f, 1f)] [SerializeField] private float demonVignette = 0.7f;
    [SerializeField] private Color demonVignetteColor = new Color(0.16f, 0f, 0.015f);
    [Range(0f, 3f)] [SerializeField] private float demonAberration = 1.5f;
    [Range(0f, 0.3f)] [SerializeField] private float demonGrain = 0.12f;
    [Tooltip("Tep za sekundu pri plnej a pri prázdnej krvi")]
    [SerializeField] private Vector2 pulseRate = new Vector2(0.9f, 2.4f);
    [Tooltip("Sila pulzu pri plnej a pri prázdnej krvi")]
    [SerializeField] private Vector2 pulseStrength = new Vector2(0.12f, 1f);
    [SerializeField] private float lookBlendSpeed = 2.5f;
    [Tooltip("Farba okrajov obrazu počas kŕmenia (násobí obraz)")]
    [SerializeField] private Color feedVignetteColor = new Color(0.75f, 0.02f, 0.03f);
    [ColorUsage(false, true)] [SerializeField] private Color feedEdgeColor = new Color(0.9f, 0.02f, 0.04f);
    [Range(0f, 2f)] [SerializeField] private float feedEdgeGlow = 0.22f;
    [Tooltip("Násobok hustoty červenej hmly, ktorú pri vnímaní zapína DayNightCycle (1 = pôvodná, veľmi hustá)")]
    [Range(0.05f, 1f)] [SerializeField] private float perceptionFogScale = 0.3f;
    [Tooltip("To isté pri krvavom videní (cyklus tam hmlu ešte zahusťuje)")]
    [Range(0.02f, 1f)] [SerializeField] private float bloodFogScale = 0.1f;

    [Header("Krvavé videnie (krv pod 10 %)")]
    [ColorUsage(false, true)] [SerializeField] private Color bloodColor = new Color(1f, 0.05f, 0.07f);
    [Range(0f, 1f)] [SerializeField] private float bloodStrength = 0.88f;
    [Range(0f, 1f)] [SerializeField] private float bloodVignette = 0.92f;
    [Range(0f, 4f)] [SerializeField] private float bloodAberration = 3f;
    [ColorUsage(false, true)] [SerializeField] private Color targetColor = new Color(2f, 0.45f, 0.25f);
    [Range(0f, 3f)] [SerializeField] private float targetGlow = 1f;

    [Header("Neóny")]
    [SerializeField] private bool neons = true;
    [Tooltip("Budova = objekt, ktorého meno obsahuje niektorý z týchto textov")]
    [SerializeField] private string[] neonBuildingNames = { "cyberpunk", "Mrakodrap", "HlavnaVeza", "Sklad" };
    [Tooltip("Existujúce svietiace časti mesta s týmto textom v mene sa cez deň stlmia")]
    [SerializeField] private string existingNeonName = "Neon";
    [Tooltip("Svietiace okná samotných budov sa cez deň stlmia a po zotmení rozsvietia")]
    [SerializeField] private bool dimBuildingWindows = true;
    [Tooltip("Jas existujúcich svietiacich častí: cez deň / v noci (násobok ich pôvodnej emisie)")]
    [SerializeField] private Vector2 existingGlow = new Vector2(0.05f, 1f);
    [ColorUsage(false, true)] [SerializeField] private Color[] neonPalette =
    {
        new Color(0.1f, 0.9f, 1f), new Color(1f, 0.15f, 0.65f), new Color(1f, 0.45f, 0.08f),
        new Color(0.55f, 0.25f, 1f), new Color(0.2f, 1f, 0.45f)
    };
    [Range(1, 6)] [SerializeField] private int accentsPerBuilding = 3;
    [Range(1f, 12f)] [SerializeField] private float neonBrightness = 2.4f;
    [Range(0f, 0.3f)] [SerializeField] private float neonOffBrightness = 0.06f;
    [SerializeField] private float neonLightRange = 16f;
    [SerializeField] private float neonLightIntensity = 1.7f;
    [Tooltip("Bodové svetlá neónov svietia len do tejto vzdialenosti od kamery")]
    [SerializeField] private float neonLightCullDistance = 75f;
    [Range(0f, 1f)] [SerializeField] private float flickerChance = 0.18f;
    [Range(0f, 1f)] [SerializeField] private float brokenChance = 0.1f;
    [SerializeField] private int neonSeed = 7;

    [Header("Dym")]
    [SerializeField] private bool smoke = true;
    [SerializeField] private string smokeAnchorName = "Sklad";
    [Range(0, 12)] [SerializeField] private int smokeStacks = 6;
    [SerializeField] private Transform[] extraSmokePoints = new Transform[0];
    [SerializeField] private Color smokeColor = new Color(0.33f, 0.31f, 0.3f, 0.55f);
    [SerializeField] private float smokeSize = 7f;
    [SerializeField] private float smokeRise = 2.6f;
    [SerializeField] private Vector3 smokeWind = new Vector3(1.4f, 0f, 0.5f);

    [Header("Tiene")]
    [SerializeField] private bool applyShadowSettings = true;
    [SerializeField] private float shadowDistance = 85f;
    [Tooltip("Hranice 4 kaskád (podiel vzdialenosti tieňov): prvé dve tesné pri hráčovi")]
    [SerializeField] private Vector3 cascadeSplits = new Vector3(0.09f, 0.22f, 0.48f);
    [SerializeField] private ShadowResolution shadowResolution = ShadowResolution.VeryHigh;
    [SerializeField] private ShadowProjection shadowProjection = ShadowProjection.StableFit;

    // ---------- Stav ----------
    private Camera cam;
    private Material material;
    private Material neonMaterial;
    private Material smokeMaterial;
    private Material maskMaterial;
    private Texture2D smokeTexture;
    private readonly Mood current = new Mood();
    private Light moon;
    private PlayerStats stats;
    private DemonSenses senses;
    private float nextPlayerSearch;
    private float demonBlend;
    private float bloodBlend;
    private float pulsePhase;
    private float pulse;
    private float darkness;
    private float sunHeight = 1f;

    private readonly RenderTexture[] bloomDown = new RenderTexture[8];
    private readonly Vector3[] frustumCorners = new Vector3[4];
    private CommandBuffer targetBuffer;
    private readonly List<Renderer> targetRenderers = new List<Renderer>(64);
    private readonly List<int> targetSubMeshes = new List<int>(64);
    private float nextTargetRefresh;

    private readonly List<Transform> buildingCandidates = new List<Transform>(128);
    private readonly List<bool> buildingDone = new List<bool>(128);
    private readonly List<NeonSign> signs = new List<NeonSign>(256);
    private readonly List<ParticleSystem> smokeSystems = new List<ParticleSystem>(12);
    private MaterialPropertyBlock block;
    private Mesh cubeMesh;
    private float nextBuildingScan;
    private float nextSmokeTint;
    private bool scanned;

    private float savedShadowDistance;
    private int savedCascades;
    private Vector3 savedSplits;
    private ShadowResolution savedResolution;
    private ShadowProjection savedProjection;
    private bool shadowsSaved;

    private static readonly RaycastHit[] rayHits = new RaycastHit[8];
    private static readonly List<GameObject> rootBuffer = new List<GameObject>(64);
    private static readonly List<Renderer> rendererBuffer = new List<Renderer>(64);

    private static readonly int IdHighTex = Shader.PropertyToID("_HighTex");
    private static readonly int IdBloomTex = Shader.PropertyToID("_BloomTex");
    private static readonly int IdShaftTex = Shader.PropertyToID("_ShaftTex");
    private static readonly int IdTargetTex = Shader.PropertyToID("_TargetTex");
    private static readonly int IdTargetBlurTex = Shader.PropertyToID("_TargetBlurTex");
    private static readonly int IdBloomParams = Shader.PropertyToID("_BloomParams");
    private static readonly int IdSunScreen = Shader.PropertyToID("_SunScreen");
    private static readonly int IdFrustumRays = Shader.PropertyToID("_FrustumRays");
    private static readonly int IdCamPos = Shader.PropertyToID("_CamPos");
    private static readonly int IdFogColor = Shader.PropertyToID("_FogColor");
    private static readonly int IdFogSunColor = Shader.PropertyToID("_FogSunColor");
    private static readonly int IdFogParams = Shader.PropertyToID("_FogParams");
    private static readonly int IdFogParams2 = Shader.PropertyToID("_FogParams2");
    private static readonly int IdSunDir = Shader.PropertyToID("_SunDir");
    private static readonly int IdAtmoTime = Shader.PropertyToID("_AtmoTime");
    private static readonly int IdColorFilter = Shader.PropertyToID("_ColorFilter");
    private static readonly int IdGrade = Shader.PropertyToID("_Grade");
    private static readonly int IdVignette = Shader.PropertyToID("_Vignette");
    private static readonly int IdVignetteColor = Shader.PropertyToID("_VignetteColor");
    private static readonly int IdFx = Shader.PropertyToID("_Fx");
    private static readonly int IdShaftColor = Shader.PropertyToID("_ShaftColor");
    private static readonly int IdDemon = Shader.PropertyToID("_Demon");
    private static readonly int IdDemonColor = Shader.PropertyToID("_DemonColor");
    private static readonly int IdBloodColor = Shader.PropertyToID("_BloodColor");
    private static readonly int IdTargetColor = Shader.PropertyToID("_TargetColor");
    private static readonly int IdEdgeGlow = Shader.PropertyToID("_EdgeGlow");
    private static readonly int IdColor = Shader.PropertyToID("_Color");
    private static readonly int IdEmissionColor = Shader.PropertyToID("_EmissionColor");

    /// <summary>Kŕmenie 0..1: pulzujúce červené okraje obrazu (nastavuje CombatFx).</summary>
    public static float FeedAmount;
    /// <summary>Krátke zrušenie farieb 0..1 (EMP impulz; nastavuje CombatFx).</summary>
    public static float Desaturate;
    /// <summary>Aréna bossa 0..1: hmla sveta sa stlmí a prefarbí na farbu arény (nastavuje ArenaDressing).</summary>
    public static float ArenaFog;
    public static Color ArenaFogColor;
    private const float ArenaFogScale = 0.35f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetArenaFog() { ArenaFog = 0f; }

    /// <summary>0 = deň, 1 = úplná tma (riadi neóny).</summary>
    public float Darkness => darkness;

    // ---------- Životný cyklus ----------
    void Awake()
    {
        cam = GetComponent<Camera>();
        block = new MaterialPropertyBlock();

        Shader post = Resources.Load<Shader>("Atmosphere");
        if (post != null && post.isSupported)
            material = new Material(post) { hideFlags = HideFlags.HideAndDontSave };
        else
            Debug.LogWarning("AtmosphereDirector: shader Resources/Atmosphere chýba alebo nie je podporovaný, obrazové efekty sú vypnuté.");

        Shader unlit = Resources.Load<Shader>("AtmosphereUnlit");
        if (unlit != null)
        {
            neonMaterial = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };

            maskMaterial = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave };
            maskMaterial.SetFloat("_ZTest", (float)CompareFunction.Always);   // ciele je vidno aj cez steny
            maskMaterial.SetFloat("_ZWrite", 0f);

            smokeTexture = BuildSoftCircle(64);
            smokeMaterial = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave, mainTexture = smokeTexture };
            smokeMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            smokeMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            smokeMaterial.SetFloat("_ZWrite", 0f);
            smokeMaterial.renderQueue = (int)RenderQueue.Transparent;
        }

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubeMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
    }

    void Start()
    {
        if (dayNight == null) dayNight = FindAnyObjectByType<DayNightCycle>();
        if (sun == null) sun = FindSun();
        BuildMoon();
        current.Blend(day, day, 0f);
    }

    void OnEnable()
    {
        if (cam == null) cam = GetComponent<Camera>();
        cam.depthTextureMode |= DepthTextureMode.Depth;
        ApplyShadows();
    }

    void OnDisable()
    {
        RestoreShadows();
        RenderSettings.ambientMode = AmbientMode.Flat;
        if (moon != null) moon.enabled = false;
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
        if (neonMaterial != null) Destroy(neonMaterial);
        if (smokeMaterial != null) Destroy(smokeMaterial);
        if (maskMaterial != null) Destroy(maskMaterial);
        if (smokeTexture != null) Destroy(smokeTexture);
        targetBuffer?.Release();
    }

    void OnValidate()
    {
        if (Application.isPlaying && isActiveAndEnabled) ApplyShadows();
    }

    void Reset()
    {
        dayNight = FindAnyObjectByType<DayNightCycle>();
        sun = FindSun();
    }

    [ContextMenu("Obnoviť predvolené nálady")]
    private void ResetMoods()
    {
        day = Mood.MakeDay();
        dusk = Mood.MakeDusk();
        night = Mood.MakeNight();
        dawn = Mood.MakeDawn();
    }

    [ContextMenu("Neóny: postaviť znova")]
    public void RebuildNeons()
    {
        for (int i = 0; i < signs.Count; i++)
        {
            NeonSign s = signs[i];
            if (s.existing) continue;
            if (s.light != null) Destroy(s.light.gameObject);
            if (s.renderer != null) Destroy(s.renderer.gameObject);
            if (s.extra != null)
                for (int k = 0; k < s.extra.Length; k++)
                    if (s.extra[k] != null) Destroy(s.extra[k].gameObject);
        }
        signs.Clear();
        for (int i = 0; i < smokeSystems.Count; i++)
            if (smokeSystems[i] != null) Destroy(smokeSystems[i].gameObject);
        smokeSystems.Clear();
        buildingCandidates.Clear();
        buildingDone.Clear();
        scanned = false;
    }

    static Light FindSun()
    {
        if (RenderSettings.sun != null) return RenderSettings.sun;
        Light[] lights = FindObjectsByType<Light>();
        for (int i = 0; i < lights.Length; i++)
            if (lights[i].type == LightType.Directional) return lights[i];
        return null;
    }

    // ---------- Každý snímok ----------
    void Update()
    {
        if (previewHour >= 0f && dayNight != null)
            dayNight.SetClock(dayNight.Day, previewHour);

        FindPlayer();
        UpdateLook();

        if (!scanned) ScanBuildings();
        if (Time.unscaledTime >= nextBuildingScan)
        {
            nextBuildingScan = Time.unscaledTime + 1.5f;
            BuildPending();
        }
    }

    // Po DayNightCycle.Update: ten každý snímok nastaví slnko a okolité svetlo, tu sa doladia podľa nálady
    void LateUpdate()
    {
        EvaluateMood();
        ApplyLights();
        UpdateNeons();
        UpdateSmokeTint();
    }

    void FindPlayer()
    {
        if (stats != null || Time.unscaledTime < nextPlayerSearch) return;
        nextPlayerSearch = Time.unscaledTime + 2f;
        stats = FindAnyObjectByType<PlayerStats>();
        if (stats != null) senses = stats.GetComponent<DemonSenses>();
    }

    void UpdateLook()
    {
        float demonTarget = 0f;
        bool blood = false;
        float blood01 = 1f;

        if (stats != null && !stats.IsDead)
        {
            if (PerceptionToggle.IsDemonVision) demonTarget = 1f;
            else if (stats.IsDemon) demonTarget = demonFormWeight;
            blood = senses != null && senses.BloodVisionActive;
            if (stats.IsDemon && stats.maxBlood > 0f) blood01 = Mathf.Clamp01(stats.Blood / stats.maxBlood);
        }

        switch (previewLook)
        {
            case PreviewLook.Ľudské: demonTarget = 0f; blood = false; break;
            case PreviewLook.Démonické: demonTarget = 1f; blood = false; break;
            case PreviewLook.Krvavé_videnie: demonTarget = 1f; blood = true; blood01 = 0.05f; break;
        }
        if (previewBlood >= 0f) blood01 = previewBlood;

        float dt = Time.unscaledDeltaTime;
        demonBlend = Mathf.MoveTowards(demonBlend, demonTarget, lookBlendSpeed * dt);
        bloodBlend = Mathf.MoveTowards(bloodBlend, blood ? 1f : 0f, lookBlendSpeed * dt);

        // Tep: čím menej krvi, tým rýchlejší a silnejší (dva údery za sebou ako srdce)
        float hunger = 1f - blood01;
        pulsePhase = Mathf.Repeat(pulsePhase + Mathf.Lerp(pulseRate.x, pulseRate.y, hunger) * dt, 1f);
        float beat = Mathf.Exp(-pulsePhase * 9f) + 0.6f * Mathf.Exp(-Mathf.Abs(pulsePhase - 0.28f) * 14f);
        pulse = Mathf.Clamp01(beat) * Mathf.Lerp(pulseStrength.x, pulseStrength.y, hunger) * Mathf.Max(demonBlend, bloodBlend);
    }

    void EvaluateMood()
    {
        float h = dayNight != null ? dayNight.TimeOfDay : 12f;

        if (h < nightUntil || h >= nightFrom) current.Blend(night, night, 0f);
        else if (h < dawnPeak) current.Blend(night, dawn, Smooth(nightUntil, dawnPeak, h));
        else if (h < dayFrom) current.Blend(dawn, day, Smooth(dawnPeak, dayFrom, h));
        else if (h < dayUntil) current.Blend(day, day, 0f);
        else if (h < duskPeak) current.Blend(day, dusk, Smooth(dayUntil, duskPeak, h));
        else current.Blend(dusk, night, Smooth(duskPeak, nightFrom, h));

        sunHeight = sun != null ? -sun.transform.forward.y : 1f;
        darkness = Mathf.Clamp01((0.12f - sunHeight) / 0.3f);
    }

    static float Smooth(float from, float to, float value)
    {
        float t = Mathf.Clamp01((value - from) / Mathf.Max(0.0001f, to - from));
        return t * t * (3f - 2f * t);
    }

    void ApplyLights()
    {
        // Slnko pod obzorom nesmie svietiť zospodu; pri démonickom vnímaní ostáva pôvodné správanie cyklu
        float horizon = Mathf.Clamp01(sunHeight * 9f);
        if (sun != null)
        {
            float keep = Mathf.Max(horizon, demonBlend);
            sun.intensity *= Mathf.Lerp(current.sunIntensity, 1f, demonBlend) * keep;
            sun.color *= Color.Lerp(current.sunTint, Color.white, demonBlend);
        }

        if (moon != null)
        {
            float amount = moonLight ? (1f - horizon) * darkness * (1f - demonBlend) : 0f;
            moon.enabled = amount > 0.01f;
            if (moon.enabled)
            {
                moon.intensity = moonIntensity * amount;
                moon.color = moonColor;
                moon.shadows = moonShadows ? LightShadows.Soft : LightShadows.None;
                moon.transform.rotation = Quaternion.Euler(moonAngles.x, moonAngles.y, 0f);
            }
        }

        // Červená hmla vnímania z cyklu je taká hustá, že by z obrazu ostala jednoliata plocha
        if (dayNight != null && dayNight.OwnsFog) RenderSettings.fogDensity *= Mathf.Lerp(perceptionFogScale, bloodFogScale, bloodBlend);

        // Tri farby okolitého svetla namiesto jednej plochej: tiene dostanú smer a farbu oblohy
        Color baseAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = baseAmbient * current.ambientSky;
        RenderSettings.ambientEquatorColor = baseAmbient * current.ambientEquator;
        RenderSettings.ambientGroundColor = baseAmbient * current.ambientGround;
    }

    void BuildMoon()
    {
        GameObject go = new GameObject("Mesiac (AtmosphereDirector)");
        moon = go.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.shadowStrength = 0.75f;
        moon.enabled = false;
    }

    // ---------- Tiene ----------
    void ApplyShadows()
    {
        if (!applyShadowSettings) return;
        if (!shadowsSaved)
        {
            savedShadowDistance = QualitySettings.shadowDistance;
            savedCascades = QualitySettings.shadowCascades;
            savedSplits = QualitySettings.shadowCascade4Split;
            savedResolution = QualitySettings.shadowResolution;
            savedProjection = QualitySettings.shadowProjection;
            shadowsSaved = true;
        }
        QualitySettings.shadowDistance = shadowDistance;
        QualitySettings.shadowCascades = 4;
        QualitySettings.shadowCascade4Split = cascadeSplits;
        QualitySettings.shadowResolution = shadowResolution;
        QualitySettings.shadowProjection = shadowProjection;
    }

    void RestoreShadows()
    {
        if (!shadowsSaved) return;
        QualitySettings.shadowDistance = savedShadowDistance;
        QualitySettings.shadowCascades = savedCascades;
        QualitySettings.shadowCascade4Split = savedSplits;
        QualitySettings.shadowResolution = savedResolution;
        QualitySettings.shadowProjection = savedProjection;
        shadowsSaved = false;
    }

    // ---------- Neóny a dym ----------
    void ScanBuildings()
    {
        scanned = true;
        if (!neons && !smoke) return;

        Scene scene = gameObject.scene;
        scene.GetRootGameObjects(rootBuffer);
        for (int r = 0; r < rootBuffer.Count; r++)
        {
            Transform root = rootBuffer[r].transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (Consider(child)) continue;
                for (int k = 0; k < child.childCount; k++) Consider(child.GetChild(k));
            }
        }
        rootBuffer.Clear();
    }

    bool Consider(Transform t)
    {
        string n = t.name;
        bool isExistingNeon = existingNeonName.Length > 0 && n.IndexOf(existingNeonName, StringComparison.OrdinalIgnoreCase) >= 0;
        if (isExistingNeon)
        {
            if (neons) RegisterExistingNeon(t);
            return true;
        }

        for (int i = 0; i < neonBuildingNames.Length; i++)
        {
            string key = neonBuildingNames[i];
            if (string.IsNullOrEmpty(key) || n.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
            buildingCandidates.Add(t);
            buildingDone.Add(false);
            return true;
        }
        return false;
    }

    void RegisterExistingNeon(Transform t)
    {
        Renderer r = t.GetComponent<Renderer>();
        if (r == null || !PrepareEmissive(r.sharedMaterial)) return;

        signs.Add(new NeonSign
        {
            renderer = r,
            anchor = t,
            color = EmissionOf(r.sharedMaterial),
            threshold = 0.2f + Hash01(t.position, 3) * 0.4f,
            seed = Hash01(t.position, 5) * 50f,
            mode = NeonMode.Steady,
            existing = true
        });
    }

    // Zóny sa zapínajú až keď v nich hráč stojí; neóny sa stavajú až na zapnutej budove (lúče potrebujú aktívne kolízie)
    void BuildPending()
    {
        int builtNow = 0;
        for (int i = 0; i < buildingCandidates.Count && builtNow < 6; i++)
        {
            if (buildingDone[i]) continue;
            Transform b = buildingCandidates[i];
            if (b == null) { buildingDone[i] = true; continue; }
            if (!b.gameObject.activeInHierarchy) continue;

            buildingDone[i] = true;
            builtNow++;
            if (neons) BuildNeons(b, i);
            if (smoke && smokeSystems.Count < smokeStacks && smokeAnchorName.Length > 0 && i % 4 == 0 &&
                b.name.IndexOf(smokeAnchorName, StringComparison.OrdinalIgnoreCase) >= 0)
                BuildSmoke(b, b.position, true);
        }

        if (smoke && extraSmokePoints != null)
        {
            for (int i = 0; i < extraSmokePoints.Length; i++)
            {
                Transform p = extraSmokePoints[i];
                if (p == null || p.Find("Dym") != null) continue;
                BuildSmoke(p, p.position, false);
            }
        }
    }

    static bool TryBounds(Transform building, out Bounds bounds)
    {
        building.GetComponentsInChildren(rendererBuffer);
        bounds = new Bounds(building.position, Vector3.zero);
        bool any = false;
        for (int i = 0; i < rendererBuffer.Count; i++)
        {
            Renderer r = rendererBuffer[i];
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        rendererBuffer.Clear();
        return any;
    }

    void BuildNeons(Transform building, int index)
    {
        RegisterEmissiveParts(building);
        if (neonMaterial == null || neonPalette.Length == 0) return;
        if (!TryBounds(building, out Bounds bounds)) return;

        float height = bounds.size.y;
        if (height < 2f) return;

        var random = new System.Random(neonSeed * 7919 + index * 104729 + Mathf.RoundToInt(building.position.x * 3f + building.position.z * 7f));
        float scale = Mathf.Clamp(height / 9f, 1f, 3.5f);
        int count = height < 5f ? 1 : Mathf.RoundToInt(accentsPerBuilding * Mathf.Clamp(height / 14f, 1f, 2.2f));

        Transform holder = new GameObject("Neony").transform;
        holder.SetParent(building.parent, true);
        holder.position = building.position;

        Color buildingColor = neonPalette[random.Next(neonPalette.Length)];
        Vector3 right = Flat(building.right), forward = Flat(building.forward);
        bool lightPlaced = false;

        for (int a = 0; a < count; a++)
        {
            // Väčšina akcentov budovy má jednu farbu, občas druhá pre kontrast
            Color color = random.NextDouble() < 0.7 ? buildingColor : neonPalette[random.Next(neonPalette.Length)];

            for (int attempt = 0; attempt < 6; attempt++)
            {
                int side = random.Next(4);
                Vector3 dir = side == 0 ? right : side == 1 ? -right : side == 2 ? forward : -forward;
                Vector3 lateral = Vector3.Cross(Vector3.up, dir);
                float depthExtent = Mathf.Abs(dir.x) * bounds.extents.x + Mathf.Abs(dir.z) * bounds.extents.z;
                float sideExtent = Mathf.Abs(lateral.x) * bounds.extents.x + Mathf.Abs(lateral.z) * bounds.extents.z;

                float y = bounds.min.y + height * Mathf.Lerp(0.3f, 0.85f, (float)random.NextDouble());
                Vector3 origin = new Vector3(bounds.center.x, y, bounds.center.z)
                    + dir * (depthExtent + 3f)
                    + lateral * (sideExtent * Mathf.Lerp(-0.7f, 0.7f, (float)random.NextDouble()));

                if (!HitBuilding(building, origin, -dir, depthExtent * 2f + 6f, out RaycastHit hit)) continue;
                if (Mathf.Abs(hit.normal.y) > 0.45f) continue;

                Vector3 normal = Flat(hit.normal);
                int kind = random.Next(100);
                Vector3 size;
                if (kind < 40) size = new Vector3(0.16f * scale, Mathf.Min(height * 0.45f, 2.6f * scale * Mathf.Lerp(0.8f, 1.6f, (float)random.NextDouble())), 0.07f);
                else if (kind < 75) size = new Vector3(Mathf.Min(sideExtent * 1.2f, 3.2f * scale * Mathf.Lerp(0.7f, 1.4f, (float)random.NextDouble())), 0.16f * scale, 0.07f);
                else size = new Vector3(1.5f * scale, 0.7f * scale, 0.07f);
                bool frame = kind >= 75;

                Vector3 pos = hit.point + normal * 0.08f;
                pos.y = Mathf.Clamp(pos.y, bounds.min.y + size.y * 0.5f + 1.8f, Mathf.Max(bounds.min.y + size.y * 0.5f + 1.8f, bounds.max.y - size.y * 0.5f));
                Quaternion rot = Quaternion.LookRotation(normal);

                MeshRenderer mr;
                Renderer[] extra = null;
                if (frame)
                {
                    // Tabuľa = rám zo štyroch trubíc (plná svietiaca plocha by v bloome zaliala celú stenu)
                    float tube = 0.07f * scale;
                    Vector3 side3 = rot * Vector3.right * (size.x * 0.5f);
                    Vector3 up3 = Vector3.up * (size.y * 0.5f);
                    mr = MakeTube(holder, pos + up3, rot, new Vector3(size.x + tube, tube, 0.07f));
                    extra = new Renderer[]
                    {
                        MakeTube(holder, pos - up3, rot, new Vector3(size.x + tube, tube, 0.07f)),
                        MakeTube(holder, pos - side3, rot, new Vector3(tube, size.y, 0.07f)),
                        MakeTube(holder, pos + side3, rot, new Vector3(tube, size.y, 0.07f))
                    };
                }
                else mr = MakeTube(holder, pos, rot, size);

                double roll = random.NextDouble();
                var sign = new NeonSign
                {
                    renderer = mr,
                    extra = extra,
                    anchor = mr.transform,
                    color = color,
                    threshold = Mathf.Lerp(0.15f, 0.75f, (float)random.NextDouble()),
                    seed = (float)random.NextDouble() * 100f,
                    mode = roll < brokenChance ? NeonMode.Broken : roll < brokenChance + flickerChance ? NeonMode.Flicker : NeonMode.Steady
                };

                // Jedno bodové svetlo na budovu (viac by v doprednom vykresľovaní zbytočne pridávalo prechody)
                if (!lightPlaced && sign.mode != NeonMode.Broken)
                {
                    GameObject lightGo = new GameObject("NeonSvetlo");
                    lightGo.transform.SetParent(holder, true);
                    lightGo.transform.position = pos + normal * 1.6f;   // kúsok od steny, inak by svietilo len na ňu
                    Light l = lightGo.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = color;
                    l.range = neonLightRange * Mathf.Sqrt(scale);
                    l.intensity = 0f;
                    l.shadows = LightShadows.None;
                    l.enabled = false;
                    sign.light = l;
                    lightPlaced = true;
                }

                signs.Add(sign);
                break;
            }
        }
    }

    // Okná a svietiace časti samotnej budovy (Standard s emisiou): cez deň stlmené, po zotmení sa rozsvietia
    void RegisterEmissiveParts(Transform building)
    {
        if (!dimBuildingWindows) return;

        building.GetComponentsInChildren(rendererBuffer);
        for (int i = 0; i < rendererBuffer.Count; i++)
        {
            Renderer r = rendererBuffer[i];
            Material m = r.sharedMaterial;
            if (!PrepareEmissive(m)) continue;

            signs.Add(new NeonSign
            {
                renderer = r,
                anchor = r.transform,
                color = EmissionOf(m),
                threshold = 0.25f + Hash01(r.bounds.center, 7) * 0.45f,
                seed = Hash01(r.bounds.center, 9) * 50f,
                mode = NeonMode.Steady,
                existing = true
            });
        }
        rendererBuffer.Clear();
    }

    // Materiály mesta majú farbu emisie, ale v scéne im chýba kľúčové slovo _EMISSION (nesvietili vôbec)
    static bool PrepareEmissive(Material m)
    {
        if (m == null || !m.HasProperty(IdEmissionColor)) return false;
        if (m.GetColor(IdEmissionColor).maxColorComponent < 0.02f) return false;
        if (!m.IsKeywordEnabled("_EMISSION")) m.EnableKeyword("_EMISSION");
        return true;
    }

    // Presvetlené (HDR) farby emisie sa zrovnajú na 1, aby jeden násobok jasu sedel na okná domov aj mrakodrapov
    static Color EmissionOf(Material m)
    {
        Color c = m.GetColor(IdEmissionColor);
        float max = c.maxColorComponent;
        return max > 1f ? c / max : c;
    }

    MeshRenderer MakeTube(Transform holder, Vector3 pos, Quaternion rot, Vector3 size)
    {
        GameObject go = new GameObject("Neon");
        go.transform.SetParent(holder, true);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = size;
        go.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = neonMaterial;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        return mr;
    }

    static bool HitBuilding(Transform building, Vector3 origin, Vector3 dir, float distance, out RaycastHit result)
    {
        int n = Physics.RaycastNonAlloc(origin, dir, rayHits, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        result = default;
        for (int i = 0; i < n; i++)
        {
            if (rayHits[i].distance >= best || !rayHits[i].transform.IsChildOf(building)) continue;
            best = rayHits[i].distance;
            result = rayHits[i];
        }
        return best < float.MaxValue;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward;
    }

    static float Hash01(Vector3 p, int salt)
    {
        return Mathf.Repeat(Mathf.Sin(p.x * 12.9898f + p.z * 78.233f + p.y * 37.719f + salt * 11.3f) * 43758.5453f, 1f);
    }

    void UpdateNeons()
    {
        if (signs.Count == 0) return;

        float time = Time.unscaledTime;
        Vector3 camPos = cam.transform.position;
        float cullSqr = neonLightCullDistance * neonLightCullDistance;

        for (int i = 0; i < signs.Count; i++)
        {
            NeonSign s = signs[i];
            if (s.renderer == null) continue;

            float on = neons && darkness > s.threshold ? 1f : 0f;
            if (on > 0f)
            {
                if (s.mode == NeonMode.Flicker)
                {
                    // Krátke výpadky a jemné chvenie jasu
                    float noise = Mathf.PerlinNoise(time * 7f, s.seed);
                    on = noise < 0.3f ? 0.12f : 0.85f + 0.15f * Mathf.PerlinNoise(time * 23f, s.seed + 9f);
                }
                else if (s.mode == NeonMode.Broken)
                {
                    // Pokazený: väčšinou tma, občas preblikne
                    on = Mathf.PerlinNoise(time * 2.3f, s.seed) > 0.72f ? Mathf.PerlinNoise(time * 31f, s.seed + 4f) : 0f;
                }
            }

            if (Mathf.Abs(on - s.shown) > 0.01f)
            {
                s.shown = on;
                block.Clear();   // blok je spoločný; bez vyčistenia by si niesol farbu predošlého neónu
                if (s.existing)
                    block.SetColor(IdEmissionColor, s.color * Mathf.Lerp(existingGlow.x, existingGlow.y, on));
                else
                    block.SetColor(IdColor, s.color * Mathf.Lerp(neonOffBrightness, neonBrightness, on));
                s.renderer.SetPropertyBlock(block);
                if (s.extra != null)
                    for (int k = 0; k < s.extra.Length; k++) s.extra[k].SetPropertyBlock(block);
            }

            if (s.light != null)
            {
                bool lit = on > 0.03f && (s.anchor.position - camPos).sqrMagnitude < cullSqr;
                if (s.light.enabled != lit) s.light.enabled = lit;
                if (lit) s.light.intensity = neonLightIntensity * on;
            }
        }
    }

    void BuildSmoke(Transform anchor, Vector3 fallback, bool onRoof)
    {
        if (smokeMaterial == null) return;

        Vector3 pos = fallback;
        if (onRoof && TryBounds(anchor, out Bounds bounds))
            pos = new Vector3(bounds.center.x, bounds.max.y + 0.5f, bounds.center.z);

        GameObject go = new GameObject("Dym");
        go.transform.SetParent(onRoof ? anchor.parent : anchor, true);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(-90f, 0f, 0f));

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 10f;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(13f, 18f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(smokeRise * 0.8f, smokeRise * 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(smokeSize * 0.6f, smokeSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = Color.white;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 3.2f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 7f;
        shape.radius = 1.2f;

        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(smokeWind.x * 0.6f, smokeWind.x * 1.3f);
        velocity.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.z = new ParticleSystem.MinMaxCurve(smokeWind.z * 0.6f, smokeWind.z * 1.3f);

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 3.4f)));

        ParticleSystem.ColorOverLifetimeModule colorOver = ps.colorOverLifetime;
        colorOver.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.55f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colorOver.color = gradient;

        ParticleSystem.RotationOverLifetimeModule rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = smokeMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        ps.Play();
        smokeSystems.Add(ps);
    }

    void UpdateSmokeTint()
    {
        if (smokeMaterial == null || Time.unscaledTime < nextSmokeTint) return;
        nextSmokeTint = Time.unscaledTime + 0.5f;

        // Dym nie je osvetlený, preto si jas a farbu berie z okolitého svetla a hmly
        Color lit = RenderSettings.ambientSkyColor + current.fogColor * 0.35f;
        Color c = new Color(smokeColor.r * lit.r, smokeColor.g * lit.g, smokeColor.b * lit.b, smokeColor.a);
        smokeMaterial.SetColor(IdColor, c);
    }

    static Texture2D BuildSoftCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);
                a *= 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.19f, y * 0.19f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(true, true);
        return tex;
    }

    // ---------- Obrazové efekty ----------
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (material == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        SetFogAndGrade();

        RenderTexture bloomTex = RenderBloom(source);
        RenderTexture shaftTex = RenderShafts(source);
        RenderTargets(source, out RenderTexture targetTex, out RenderTexture targetBlurTex);

        material.SetTexture(IdBloomTex, bloomTex);
        material.SetTexture(IdShaftTex, shaftTex != null ? shaftTex : (Texture)Texture2D.blackTexture);
        material.SetTexture(IdTargetTex, targetTex != null ? targetTex : (Texture)Texture2D.blackTexture);
        material.SetTexture(IdTargetBlurTex, targetBlurTex != null ? targetBlurTex : (Texture)Texture2D.blackTexture);

        Graphics.Blit(source, destination, material, 5);

        RenderTexture.ReleaseTemporary(bloomTex);
        if (shaftTex != null) RenderTexture.ReleaseTemporary(shaftTex);
        if (targetTex != null) RenderTexture.ReleaseTemporary(targetTex);
        if (targetBlurTex != null) RenderTexture.ReleaseTemporary(targetBlurTex);
    }

    void SetFogAndGrade()
    {
        Mood m = current;
        Transform t = cam.transform;

        cam.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), cam.farClipPlane, Camera.MonoOrStereoscopicEye.Mono, frustumCorners);
        Matrix4x4 rays = Matrix4x4.zero;
        rays.SetRow(0, t.TransformVector(frustumCorners[0]));   // ľavý dolný
        rays.SetRow(1, t.TransformVector(frustumCorners[3]));   // pravý dolný
        rays.SetRow(2, t.TransformVector(frustumCorners[1]));   // ľavý horný
        rays.SetRow(3, t.TransformVector(frustumCorners[2]));   // pravý horný
        material.SetMatrix(IdFrustumRays, rays);
        material.SetVector(IdCamPos, t.position);
        material.SetFloat(IdAtmoTime, Time.unscaledTime);

        // Pri démonickom vnímaní hmlu sveta rieši DayNightCycle (červená hmla), táto vrstva sa stlmí
        float fogFade = 1f - 0.6f * demonBlend;
        float arena = Mathf.Clamp01(ArenaFog);
        fogFade *= Mathf.Lerp(1f, ArenaFogScale, arena);
        float groundY = (stats != null ? stats.transform.position.y : t.position.y - 3f) + fogGroundOffset;

        Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
        Color sunScatter = sun != null ? sun.color * Mathf.Min(1.5f, sun.intensity) : Color.black;
        sunScatter.a = fogSunScatter * Mathf.Clamp01(sunHeight * 9f);

        Color fog = Color.Lerp(m.fogColor, ArenaFogColor, arena);
        fog.a = m.fogMaxOpacity;
        material.SetColor(IdFogColor, fog);
        material.SetColor(IdFogSunColor, sunScatter);
        material.SetVector(IdFogParams, new Vector4(m.fogDistance * fogFade, m.groundFog * fogFade, groundY, 1f / Mathf.Max(0.5f, m.groundFogHeight)));
        material.SetVector(IdFogParams2, new Vector4(fogNoise, fogNoiseScale, m.skyHaze * fogFade, fogStartDistance));
        material.SetVector(IdSunDir, toSun);

        float look = Mathf.Max(demonBlend, bloodBlend);
        float vignette = Mathf.Max(m.vignette, Mathf.Max(demonVignette * demonBlend, bloodVignette * bloodBlend));
        Color vignetteColor = Color.Lerp(Color.black, demonVignetteColor, look);
        float aberration = Mathf.Lerp(demonAberration * demonBlend, bloodAberration, bloodBlend) * (1f + pulse * 0.6f);
        float grain = Mathf.Max(m.grain, demonGrain * look);

        // Studený nočný filter by červenú vnímania zmenil na ružovú
        material.SetColor(IdColorFilter, Color.Lerp(m.colorFilter, Color.white, look * 0.8f));
        float edgeGlow = 0f;
        // Kŕmenie: okraje pulzujú do červena v rytme tepu
        if (FeedAmount > 0.001f)
        {
            float beat = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 7.5f);
            vignette = Mathf.Max(vignette, FeedAmount * 0.9f * beat);
            vignetteColor = Color.Lerp(vignetteColor, feedVignetteColor, FeedAmount);
            edgeGlow = FeedAmount * beat * feedEdgeGlow;
        }

        material.SetVector(IdGrade, new Vector4(m.exposure * (1f + 0.5f * Desaturate), m.saturation * (1f - Desaturate), m.contrast, tonemapping));
        material.SetVector(IdVignette, new Vector4(vignette, 0.35f, 0f, 0f));
        material.SetColor(IdEdgeGlow, new Color(feedEdgeColor.r, feedEdgeColor.g, feedEdgeColor.b, edgeGlow));
        material.SetColor(IdVignetteColor, vignetteColor);
        material.SetVector(IdFx, new Vector4(aberration, grain, 0f, 0f));   // w (lúče) dopĺňa RenderShafts
        material.SetVector(IdBloomParams, new Vector4(m.bloomThreshold, m.bloomThreshold * bloomSoftKnee, m.bloom, 0f));
        material.SetColor(IdShaftColor, m.shaftColor);
        material.SetVector(IdDemon, new Vector4(demonRedShift * demonBlend, pulse, bloodStrength * bloodBlend, targetGlow * bloodBlend));
        material.SetColor(IdDemonColor, demonColor);
        material.SetColor(IdBloodColor, bloodColor);
        material.SetColor(IdTargetColor, targetColor);
    }

    RenderTexture RenderBloom(RenderTexture source)
    {
        int w = Mathf.Max(2, source.width / 2), h = Mathf.Max(2, source.height / 2);
        RenderTextureFormat format = source.format;

        RenderTexture first = RenderTexture.GetTemporary(w, h, 0, format);
        first.filterMode = FilterMode.Bilinear;
        Graphics.Blit(source, first, material, 0);
        bloomDown[0] = first;

        int levels = 1;
        for (int i = 1; i < bloomIterations && i < bloomDown.Length; i++)
        {
            w /= 2; h /= 2;
            if (w < 4 || h < 4) break;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, format);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(bloomDown[i - 1], rt, material, 1);
            bloomDown[i] = rt;
            levels++;
        }

        RenderTexture last = bloomDown[levels - 1];
        for (int i = levels - 2; i >= 0; i--)
        {
            RenderTexture high = bloomDown[i];
            RenderTexture up = RenderTexture.GetTemporary(high.width, high.height, 0, format);
            up.filterMode = FilterMode.Bilinear;
            material.SetTexture(IdHighTex, high);
            Graphics.Blit(last, up, material, 2);
            RenderTexture.ReleaseTemporary(last);
            RenderTexture.ReleaseTemporary(high);
            last = up;
        }

        for (int i = 0; i < bloomDown.Length; i++) bloomDown[i] = null;
        return last;
    }

    RenderTexture RenderShafts(RenderTexture source)
    {
        if (sun == null || current.shafts <= 0.01f || sunHeight <= 0f || demonBlend > 0.95f) return null;

        Vector3 toSun = -sun.transform.forward;
        Vector3 vp = cam.WorldToViewportPoint(cam.transform.position + toSun * 1000f);
        if (vp.z <= 0f) return null;

        // Slnko ďaleko mimo obrazu lúče nerobí
        float edge = Mathf.Max(Mathf.Abs(vp.x - 0.5f), Mathf.Abs(vp.y - 0.5f));
        float fade = 1f - Mathf.Clamp01((edge - 0.5f) / 0.6f);
        float strength = current.shafts * fade * (1f - demonBlend);
        if (strength <= 0.01f) return null;

        int w = Mathf.Max(2, source.width / 4), h = Mathf.Max(2, source.height / 4);
        RenderTexture a = RenderTexture.GetTemporary(w, h, 0, source.format);
        RenderTexture b = RenderTexture.GetTemporary(w, h, 0, source.format);
        a.filterMode = FilterMode.Bilinear;
        b.filterMode = FilterMode.Bilinear;

        material.SetVector(IdSunScreen, new Vector4(vp.x, vp.y, shaftRadius, 0f));
        Graphics.Blit(source, a, material, 3);

        // Tri prechody s rastúcim krokom = dlhé hladké lúče za cenu 18 vzoriek v štvrtinovom rozlíšení
        float step = 0.035f;
        for (int i = 0; i < 3; i++)
        {
            material.SetVector(IdSunScreen, new Vector4(vp.x, vp.y, shaftRadius, step));
            Graphics.Blit(a, b, material, 4);
            RenderTexture swap = a; a = b; b = swap;
            step *= 2.2f;
        }
        RenderTexture.ReleaseTemporary(b);

        Vector4 fx = material.GetVector(IdFx);
        fx.w = strength;
        material.SetVector(IdFx, fx);
        return a;
    }

    void RenderTargets(RenderTexture source, out RenderTexture sharp, out RenderTexture blurred)
    {
        sharp = null;
        blurred = null;
        if (bloodBlend <= 0.01f || maskMaterial == null || stats == null) return;

        if (Time.unscaledTime >= nextTargetRefresh)
        {
            nextTargetRefresh = Time.unscaledTime + 0.5f;
            RefreshTargets();
        }
        if (targetRenderers.Count == 0) return;

        if (targetBuffer == null) targetBuffer = new CommandBuffer { name = "Krvavé videnie: ciele" };
        targetBuffer.Clear();

        int w = Mathf.Max(2, source.width / 2), h = Mathf.Max(2, source.height / 2);
        sharp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.R8);
        sharp.filterMode = FilterMode.Bilinear;

        targetBuffer.SetRenderTarget(sharp);
        targetBuffer.ClearRenderTarget(false, true, Color.clear);
        targetBuffer.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
        for (int i = 0; i < targetRenderers.Count; i++)
        {
            Renderer r = targetRenderers[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            targetBuffer.DrawRenderer(r, maskMaterial, targetSubMeshes[i], 0);
        }
        Graphics.ExecuteCommandBuffer(targetBuffer);

        RenderTexture half = RenderTexture.GetTemporary(w / 2, h / 2, 0, RenderTextureFormat.R8);
        RenderTexture quarter = RenderTexture.GetTemporary(w / 4, h / 4, 0, RenderTextureFormat.R8);
        half.filterMode = FilterMode.Bilinear;
        quarter.filterMode = FilterMode.Bilinear;
        Graphics.Blit(sharp, half, material, 1);
        Graphics.Blit(half, quarter, material, 1);
        RenderTexture.ReleaseTemporary(half);
        blurred = quarter;
    }

    void RefreshTargets()
    {
        targetRenderers.Clear();
        targetSubMeshes.Clear();

        float range = senses != null ? senses.bloodVisionRange : 60f;
        float rangeSqr = range * range;
        Vector3 origin = stats.transform.position;

        List<BloodSource> all = BloodSource.All;
        for (int i = 0; i < all.Count; i++)
        {
            BloodSource s = all[i];
            if (s == null || s.IsDead || !s.gameObject.activeInHierarchy) continue;
            if ((s.transform.position - origin).sqrMagnitude > rangeSqr) continue;

            s.GetComponentsInChildren(rendererBuffer);
            for (int k = 0; k < rendererBuffer.Count; k++)
            {
                Renderer r = rendererBuffer[k];
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

                int subMeshes = 1;
                if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) subMeshes = skinned.sharedMesh.subMeshCount;
                else if (r.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) subMeshes = filter.sharedMesh.subMeshCount;

                for (int m = 0; m < subMeshes; m++)
                {
                    targetRenderers.Add(r);
                    targetSubMeshes.Add(m);
                }
            }
            rendererBuffer.Clear();
        }
    }
}
