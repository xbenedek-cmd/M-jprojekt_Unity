using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Efekty boja a schopností bez externých assetov: častice (iskry, dym, oheň, krv, žiara), vetvený blesk,
/// stopy za čepeľou a postavou, bliknutie zasiahnutej postavy, odraz, hit stop, otras kamery, pád a rozptýlenie pri smrti.
/// Všetko beží z niekoľkých spoločných Particle Systemov (Emit), nič sa za behu neinštancuje.
/// Komponent stačí jeden v scéne; ak chýba, vytvorí sa sám s predvolenými hodnotami.
/// </summary>
[DisallowMultipleComponent]
public class CombatFx : MonoBehaviour
{
    private enum StatusKind : byte { Burn, Bleed, Dissolve }

    private struct Flash
    {
        public Renderer[] renderers;
        public int[] subMeshes;
        public Color color;
        public float start, duration, rim;
    }

    private struct Status
    {
        public Transform target;
        public Damageable owner;
        public StatusKind kind;
        public float until, nextEmit, height, bottomY, radius;
        public Color color;
    }

    private struct Push
    {
        public Transform target;
        public Vector3 direction;
        public float remaining;
    }

    private struct Orb
    {
        public Vector3 start, control;
        public Transform target;
        public float startTime, duration;
        public Color color;
        public bool active;
    }

    private class Bolt
    {
        public LineRenderer main;
        public LineRenderer[] branches;
        public Vector3 from, to;
        public Color color;
        public float until, nextJitter, width, duration;
    }

    private class BeamFx
    {
        public LineRenderer line;
        public Color color;
        public float start, duration, width;
        public bool strike;
    }

    private class FlashLight
    {
        public Light light;
        public float start, duration, intensity;
    }

    private class Follower
    {
        public Transform root, target;
        public TrailRenderer trail;
        public Vector3 offset;
        public float emitUntil;
    }

    private class Body
    {
        public Renderer[] renderers;
        public int[] subMeshes;
        public float height, centerY, bottomY, radius;
    }

    [Header("Zásah")]
    [Tooltip("Dĺžka zastavenia času pri kritickom zásahu (reálne sekundy)")]
    [SerializeField] private float hitStopSeconds = 0.05f;
    [Range(0f, 0.3f)] [SerializeField] private float hitStopTimeScale = 0.02f;
    [Tooltip("Otras kamery: najmenší a najväčší (m) podľa sily zásahu")]
    [SerializeField] private Vector2 shakeRange = new Vector2(0.04f, 0.32f);
    [Tooltip("Poškodenie, pri ktorom je otras najväčší")]
    [SerializeField] private float shakeFullDamage = 70f;
    [SerializeField] private float shakeDecay = 1.6f;
    [SerializeField] private float shakeFrequency = 28f;
    [ColorUsage(false, true)] [SerializeField] private Color flashColor = new Color(0.55f, 0.5f, 0.45f);
    [SerializeField] private float flashSeconds = 0.13f;
    [Tooltip("Odraz zasiahnutého pri zásahu hráčom: najmenší a najväčší (m)")]
    [SerializeField] private Vector2 knockback = new Vector2(0.18f, 0.6f);
    [SerializeField] private float knockbackSpeed = 7f;

    [Header("Čepeľ")]
    [SerializeField] private float trailSeconds = 0.22f;
    [ColorUsage(true, true)] [SerializeField] private Color trailColor = new Color(1.4f, 1.6f, 2f, 0.9f);
    [ColorUsage(true, true)] [SerializeField] private Color trailHeavyColor = new Color(2.2f, 1.5f, 0.8f, 1f);

    [Header("Blesk")]
    [SerializeField] private float boltSeconds = 0.24f;
    [Range(0f, 1f)] [SerializeField] private float boltJaggedness = 0.09f;
    [SerializeField] private int boltBranches = 3;

    [Header("Smrť")]
    [SerializeField] private float staggerSeconds = 0.34f;
    [SerializeField] private float fallSeconds = 0.42f;
    [SerializeField] private float dissolveSeconds = 0.9f;

    [Header("Kŕmenie")]
    [Tooltip("Ako rýchlo nabehne a odznie červený okraj a tlmenie zvuku")]
    [SerializeField] private float feedBlendSpeed = 3f;

    /// <summary>Posun kamery otrasom; pripočíta ho CameraFollow.</summary>
    public static Vector3 ShakeOffset { get; private set; }

    private static CombatFx instance;
    private static readonly int FlashColorId = Shader.PropertyToID("_FxFlashColor");
    private static readonly List<Renderer> rendererBuffer = new List<Renderer>(32);
    private static readonly RaycastHit[] rayBuffer = new RaycastHit[8];
    private static readonly Vector3[] boltPoints = new Vector3[40];

    private Material additive, alpha, flashMaterial, lineMaterial;
    private Texture2D softTexture, lineTexture;
    private ParticleSystem sparks, glow, smoke, fire, blood, motes;

    private readonly Flash[] flashes = new Flash[20];
    private readonly Status[] statuses = new Status[28];
    private readonly Push[] pushes = new Push[12];
    private readonly Orb[] orbs = new Orb[32];
    private readonly Bolt[] bolts = new Bolt[12];
    private readonly FlashLight[] lights = new FlashLight[5];
    private readonly Follower[] followers = new Follower[4];
    private readonly Dictionary<Damageable, Body> bodies = new Dictionary<Damageable, Body>();
    private int nextBolt, nextLight, nextFollower, nextOrb;

    // Lúče a vlny po zemi (nahrádzajú kocky a valce z Vfx)
    private readonly BeamFx[] beams = new BeamFx[64];
    private int nextBeam;
    private Material lineAlphaMaterial, ringAdditive, ringAlpha;
    private Texture2D ringTexture;
    private ParticleSystem rings, ringsDark, circles, circlesDark, wave;
    private const float RingEdge = 0.93f;   // kde v textúre leží hrana prstenca (podiel polomeru)

    private CommandBuffer flashBuffer;
    private Camera flashCamera;
    private bool flashBufferFilled;
    private float nextCameraSearch;

    private Transform swordTrailRoot;
    private TrailRenderer swordTrail;
    private float swordTrailUntil;

    private PlayerStats player;
    private float nextPlayerSearch;
    private float shake;
    private float hitStopUntil = -1f;
    private float feedUntil, feed, nextFeedEmit;
    private Transform feedTarget, feedDrinker;
    private float desaturateStart = -100f, desaturateSeconds = 0.5f;

    private static CombatFx I
    {
        get
        {
            if (instance == null) Create();
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;
        instance = FindAnyObjectByType<CombatFx>();
        if (instance == null) instance = new GameObject("CombatFx").AddComponent<CombatFx>();
    }

    // ---------- Stavba ----------
    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;

        softTexture = BuildSoftCircle(64);
        lineTexture = BuildLineTexture(32);

        Shader unlit = Resources.Load<Shader>("AtmosphereUnlit");
        additive = MakeMaterial(unlit, softTexture, BlendMode.SrcAlpha, BlendMode.One);
        alpha = MakeMaterial(unlit, softTexture, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
        lineMaterial = MakeMaterial(unlit, lineTexture, BlendMode.SrcAlpha, BlendMode.One);

        Shader flashShader = Resources.Load<Shader>("FxFlash");
        if (flashShader != null) flashMaterial = new Material(flashShader) { hideFlags = HideFlags.HideAndDontSave };

        sparks = MakeSystem("Iskry", additive, 500, true, 1.4f);
        glow = MakeSystem("Žiara", additive, 200, false, 0f);
        smoke = MakeSystem("Dym", alpha, 350, false, -0.06f);
        fire = MakeSystem("Oheň", additive, 600, false, -0.55f);
        blood = MakeSystem("Krv", alpha, 300, true, 2.6f);
        motes = MakeSystem("Čiastočky", additive, 400, false, -0.35f);

        Grow(smoke, 0.6f, 2.6f);
        Grow(fire, 1f, 0.25f);
        Grow(glow, 0.7f, 1.3f);
        FireColors(fire);
        Drift(motes, 0.6f);
        Drift(smoke, 0.25f);

        ringTexture = BuildRingTexture(128);
        lineAlphaMaterial = MakeMaterial(unlit, lineTexture, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);
        ringAdditive = MakeMaterial(unlit, ringTexture, BlendMode.SrcAlpha, BlendMode.One);
        ringAlpha = MakeMaterial(unlit, ringTexture, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha);

        // Prstence ležia na zemi; "rings" sa rozpínajú, "circles" držia veľkosť (výstraha pred úderom)
        rings = MakeFlatSystem("Prstence", ringAdditive, 120);
        ringsDark = MakeFlatSystem("PrstenceTmavé", ringAlpha, 60);
        circles = MakeFlatSystem("Kruhy", ringAdditive, 300);
        circlesDark = MakeFlatSystem("KruhyTmavé", ringAlpha, 150);
        Grow(rings, 0.12f, 1f);
        Grow(ringsDark, 0.12f, 1f);
        wave = MakeSystem("Vlna", additive, 700, false, 0f);
        Grow(wave, 0.8f, 1.5f);

        for (int i = 0; i < beams.Length; i++)
        {
            LineRenderer line = MakeLine("Lúč_" + i);
            line.positionCount = 2;
            beams[i] = new BeamFx { line = line };
        }

        for (int i = 0; i < bolts.Length; i++) bolts[i] = MakeBolt(i);
        for (int i = 0; i < lights.Length; i++) lights[i] = MakeLight(i);
        for (int i = 0; i < followers.Length; i++) followers[i] = MakeFollower(i);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (flashCamera != null && flashBuffer != null) flashCamera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, flashBuffer);
        flashBuffer?.Release();
        if (additive != null) Destroy(additive);
        if (alpha != null) Destroy(alpha);
        if (lineMaterial != null) Destroy(lineMaterial);
        if (flashMaterial != null) Destroy(flashMaterial);
        if (softTexture != null) Destroy(softTexture);
        if (lineTexture != null) Destroy(lineTexture);
        if (lineAlphaMaterial != null) Destroy(lineAlphaMaterial);
        if (ringAdditive != null) Destroy(ringAdditive);
        if (ringAlpha != null) Destroy(ringAlpha);
        if (ringTexture != null) Destroy(ringTexture);
        ShakeOffset = Vector3.zero;
        AtmosphereDirector.FeedAmount = 0f;
        AtmosphereDirector.Desaturate = 0f;
        UnderwaterEffect.ExtraMuffle = 0f;
    }

    static Material MakeMaterial(Shader shader, Texture texture, BlendMode src, BlendMode dst)
    {
        var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, mainTexture = texture };
        m.SetFloat("_SrcBlend", (float)src);
        m.SetFloat("_DstBlend", (float)dst);
        m.SetFloat("_ZWrite", 0f);
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }

    ParticleSystem MakeSystem(string systemName, Material material, int max, bool stretched, float gravity)
    {
        var go = new GameObject(systemName);
        go.transform.SetParent(transform, false);
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.gravityModifier = gravity;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        // Každá častica plynule zhasne; farbu a veľkosť dostane pri vyslaní
        ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.55f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (stretched)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 2.2f;
        }

        ps.Play();
        return ps;
    }

    ParticleSystem MakeFlatSystem(string systemName, Material material, int max)
    {
        ParticleSystem ps = MakeSystem(systemName, material, max, false, 0f);
        ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        return ps;
    }

    // Prstenec: jasná hrana pri okraji a slabá výplň vnútri (vidno celú zasiahnutú plochu)
    static Texture2D BuildRingTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[size * size];
        float half = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                float edge = Mathf.Clamp01(1f - Mathf.Abs(d - RingEdge) / 0.065f);
                float inner = d < RingEdge ? 0.1f + 0.22f * Mathf.Pow(d / RingEdge, 5f) : 0f;
                float a = Mathf.Clamp01(edge * edge + inner);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(true, true);
        return tex;
    }

    static void Grow(ParticleSystem ps, float from, float to)
    {
        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, from), new Keyframe(1f, to)));
    }

    static void Drift(ParticleSystem ps, float strength)
    {
        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = strength;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.6f;
    }

    static void FireColors(ParticleSystem ps)
    {
        ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.95f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.12f), 0.3f),
                new GradientColorKey(new Color(0.8f, 0.15f, 0.03f), 0.7f), new GradientColorKey(new Color(0.2f, 0.05f, 0.02f), 1f)
            },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
    }

    Bolt MakeBolt(int index)
    {
        var bolt = new Bolt { main = MakeLine("Blesk_" + index), branches = new LineRenderer[Mathf.Max(0, boltBranches)] };
        for (int i = 0; i < bolt.branches.Length; i++) bolt.branches[i] = MakeLine("Blesk_" + index + "_vetva" + i);
        return bolt;
    }

    LineRenderer MakeLine(string lineName)
    {
        var go = new GameObject(lineName);
        go.transform.SetParent(transform, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = lineMaterial;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCornerVertices = 0;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    FlashLight MakeLight(int index)
    {
        var go = new GameObject("Záblesk_" + index);
        go.transform.SetParent(transform, false);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.shadows = LightShadows.None;
        l.enabled = false;
        return new FlashLight { light = l };
    }

    Follower MakeFollower(int index)
    {
        var go = new GameObject("Stopa_" + index);
        go.transform.SetParent(transform, false);
        return new Follower { root = go.transform, trail = MakeTrail(go) };
    }

    TrailRenderer MakeTrail(GameObject go)
    {
        TrailRenderer trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = lineMaterial;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.minVertexDistance = 0.04f;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        trail.emitting = false;
        return trail;
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
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(true, true);
        return tex;
    }

    // Pás pre čiary a stopy: jasný stred, mäkké okraje naprieč
    static Texture2D BuildLineTexture(int size)
    {
        var tex = new Texture2D(4, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color32[4 * size];
        for (int y = 0; y < size; y++)
        {
            float v = 1f - Mathf.Abs((y / (float)(size - 1)) * 2f - 1f);
            byte a = (byte)(Mathf.Pow(v, 1.6f) * 255f);
            for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color32(255, 255, 255, a);
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return tex;
    }

    // ---------- Vysielanie častíc ----------
    static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float life, Color color)
    {
        var p = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startSize = size,
            startLifetime = life,
            startColor = color
        };
        ps.Emit(p, 1);
    }

    void Burst(ParticleSystem ps, Vector3 position, int count, float speed, float size, float life, Color color, Vector3 bias)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = (Random.onUnitSphere + bias).normalized;
            Emit(ps, position + dir * 0.05f, dir * speed * Random.Range(0.4f, 1f), size * Random.Range(0.6f, 1.2f), life * Random.Range(0.6f, 1f), color);
        }
    }

    void Flare(Vector3 position, Color color, float range, float intensity, float seconds)
    {
        FlashLight f = lights[nextLight];
        nextLight = (nextLight + 1) % lights.Length;
        f.light.transform.position = position;
        f.light.color = color;
        f.light.range = range;
        f.intensity = intensity;
        f.start = Time.time;
        f.duration = seconds;
        f.light.enabled = true;
    }

    // ---------- Verejné: zásahy ----------
    /// <summary>Zásah postavy (volá Damageable). amount 0 = blok alebo nezraniteľnosť.</summary>
    public static void Damaged(Damageable target, float amount, GameObject attacker, DamageKind kind)
    {
        if (target == null) return;
        CombatFx fx = I;
        Body body = fx.BodyOf(target);

        Vector3 center = target.transform.position + Vector3.up * body.centerY;
        Vector3 away = Vector3.zero;
        if (attacker != null)
        {
            away = target.transform.position - attacker.transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.zero;
        }
        Vector3 contact = center - away * 0.3f;

        if (amount <= 0f)
        {
            // Blok / nezraniteľný: studené iskry, bez bliknutia
            fx.Burst(fx.sparks, contact, 8, 5f, 0.07f, 0.3f, new Color(0.75f, 0.85f, 1f), -away);
            return;
        }

        Color color = KindColor(kind);
        float strength = Mathf.Clamp01(amount / Mathf.Max(1f, fx.shakeFullDamage));
        fx.Burst(fx.sparks, contact, 12 + Mathf.RoundToInt(strength * 18f), 5f + strength * 5f, 0.11f, 0.4f, color, -away * 0.6f + Vector3.up * 0.3f);
        Emit(fx.glow, contact, Vector3.zero, 0.7f + strength * 0.9f, 0.14f, new Color(color.r, color.g, color.b, 0.6f));
        if (target.team != CombatTeam.Demon && (kind == DamageKind.Normal || kind == DamageKind.Blood || kind == DamageKind.Earth))
            fx.Burst(fx.blood, contact, 4 + Mathf.RoundToInt(strength * 6f), 3.5f, 0.09f, 0.45f, new Color(0.55f, 0.02f, 0.03f, 0.9f), away + Vector3.up * 0.4f);

        fx.AddFlash(body, kind == DamageKind.Normal ? fx.flashColor : color * 0.6f, fx.flashSeconds, 0f);

        if (kind == DamageKind.Earth) fx.Slashes(center, body.height);
        else if (kind == DamageKind.Lightning) fx.BodyArcs(center, body.height, color);

        bool byPlayer = attacker != null && fx.PlayerObject() == attacker;
        if (byPlayer)
        {
            Shake(Mathf.Lerp(fx.shakeRange.x, fx.shakeRange.y, strength));
            if (away != Vector3.zero && target.GetComponent<BossCore>() == null)
                fx.AddPush(target.transform, away, Mathf.Lerp(fx.knockback.x, fx.knockback.y, strength));
        }
    }

    /// <summary>Kritický zásah hráča: krátke zastavenie času, silnejší otras a záblesk.</summary>
    public static void Crit(Damageable target)
    {
        CombatFx fx = I;
        HitStop(fx.hitStopSeconds);
        Shake(fx.shakeRange.y);
        if (target == null) return;

        Body body = fx.BodyOf(target);
        Vector3 center = target.transform.position + Vector3.up * body.centerY;
        fx.Burst(fx.sparks, center, 26, 11f, 0.09f, 0.4f, new Color(1f, 0.85f, 0.35f), Vector3.up * 0.2f);
        Emit(fx.glow, center, Vector3.zero, 2.6f, 0.16f, new Color(1f, 0.9f, 0.6f));
        fx.Flare(center, new Color(1f, 0.85f, 0.5f), 7f, 3.5f, 0.18f);
        fx.AddFlash(body, new Color(1.1f, 0.9f, 0.5f), fx.flashSeconds * 1.6f, 0f);
    }

    /// <summary>Zásah hráča: otras podľa poškodenia.</summary>
    public static void PlayerHurt(float amount)
    {
        CombatFx fx = I;
        Shake(Mathf.Lerp(fx.shakeRange.x, fx.shakeRange.y, Mathf.Clamp01(amount / 40f)));
    }

    /// <summary>Postupné poškodenie: horenie (oheň) alebo krvácanie (krvavé čiary).</summary>
    public static void Dot(Damageable target, float seconds, DamageKind kind)
    {
        if (target == null) return;
        if (kind == DamageKind.Fire) I.AddStatus(target, StatusKind.Burn, seconds, new Color(1f, 0.5f, 0.12f));
        else if (kind == DamageKind.Earth || kind == DamageKind.Blood) I.AddStatus(target, StatusKind.Bleed, seconds, new Color(0.6f, 0.02f, 0.04f));
    }

    /// <summary>Smrť: démon sa krátko rozptýli do dymu a čiastočiek vo farbe svojej slabiny.</summary>
    public static void Died(Damageable target)
    {
        if (target == null || target.team != CombatTeam.Demon) return;
        CombatFx fx = I;
        fx.AddStatus(target, StatusKind.Dissolve, fx.dissolveSeconds, KindColor(target.weakness));
        Body body = fx.BodyOf(target);
        Vector3 center = target.transform.position + Vector3.up * body.centerY;
        fx.Burst(fx.smoke, center, 14, 1.6f, 1.1f, 1.1f, new Color(0.03f, 0.02f, 0.05f, 0.75f), Vector3.up * 0.4f);
        fx.AddFlash(body, new Color(0.9f, 0.3f, 1.4f), fx.dissolveSeconds, 1f);
    }

    /// <summary>Dezorientovaný pád: postava sa zatacká, pretočí a zvalí smerom od útočníka (namiesto okamžitého prevrátenia).</summary>
    public static void Tumble(Transform body, Vector3 attackerPosition, bool hasAttacker)
    {
        if (body == null) return;
        Vector3 away = body.position - attackerPosition;
        away.y = 0f;
        if (!hasAttacker || away.sqrMagnitude < 0.0001f) away = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        I.StartCoroutine(I.TumbleRoutine(body, away.normalized));
    }

    public static void Shake(float amplitude)
    {
        CombatFx fx = I;
        if (amplitude > fx.shake) fx.shake = amplitude;
    }

    public static void HitStop(float seconds)
    {
        // Spomalenie súboja (QTE) a pauza majú prednosť
        if (seconds <= 0f || Time.timeScale < 0.5f) return;
        CombatFx fx = I;
        fx.hitStopUntil = Time.unscaledTime + seconds;
        Time.timeScale = fx.hitStopTimeScale;
    }

    public static void HitSpark(Vector3 position, Vector3 direction, Color color, float power)
    {
        CombatFx fx = I;
        fx.Burst(fx.sparks, position, 6 + Mathf.RoundToInt(power * 14f), 4f + power * 5f, 0.07f, 0.35f, color, direction);
        Emit(fx.glow, position, Vector3.zero, 0.6f + power, 0.12f, color);
    }

    static Color KindColor(DamageKind kind)
    {
        switch (kind)
        {
            case DamageKind.Fire: return new Color(1f, 0.5f, 0.12f);
            case DamageKind.Lightning: return new Color(0.55f, 0.85f, 1f);
            case DamageKind.Earth: return new Color(0.85f, 0.12f, 0.1f);
            case DamageKind.Blood: return new Color(0.9f, 0.05f, 0.15f);
            case DamageKind.Shadow: return new Color(0.6f, 0.2f, 1f);
            case DamageKind.Holy: return new Color(1f, 0.95f, 0.6f);
            default: return new Color(1f, 0.9f, 0.7f);
        }
    }

    // ---------- Verejné: čepeľ a stopy ----------
    /// <summary>Záblesk čepele: stopa z hrotu meča počas švihu.</summary>
    public static void SwordTrail(Transform holder, Vector3 localTip, float bladeLength, bool heavy, float seconds)
    {
        if (holder == null) return;
        CombatFx fx = I;

        if (fx.swordTrailRoot == null)
        {
            var go = new GameObject("StopaČepele");
            fx.swordTrailRoot = go.transform;
            fx.swordTrail = fx.MakeTrail(go);
        }
        if (fx.swordTrailRoot.parent != holder)
        {
            fx.swordTrailRoot.SetParent(holder, false);
            fx.swordTrail.Clear();
        }
        fx.swordTrailRoot.localPosition = localTip * 0.72f;   // stred ostria; šírka stopy pokryje zvyšok

        Color c = heavy ? fx.trailHeavyColor : fx.trailColor;
        fx.swordTrail.startColor = c;
        fx.swordTrail.endColor = new Color(c.r, c.g, c.b, 0f);
        fx.swordTrail.time = fx.trailSeconds * (heavy ? 1.4f : 1f);
        fx.swordTrail.widthMultiplier = bladeLength * (heavy ? 0.75f : 0.55f);
        fx.swordTrail.emitting = true;
        fx.swordTrailUntil = Time.time + Mathf.Max(0.25f, seconds);
    }

    /// <summary>Farebná stopa, ktorá chvíľu sleduje postavu (Krvavé šialenstvo).</summary>
    public static void FollowTrail(Transform target, Vector3 offset, Color color, float width, float seconds)
    {
        if (target == null) return;
        CombatFx fx = I;
        Follower f = fx.followers[fx.nextFollower];
        fx.nextFollower = (fx.nextFollower + 1) % fx.followers.Length;

        f.target = target;
        f.offset = offset;
        f.root.position = target.position + offset;
        f.trail.Clear();
        f.trail.startColor = color;
        f.trail.endColor = new Color(color.r, color.g, color.b, 0f);
        f.trail.time = 0.45f;
        f.trail.widthMultiplier = width;
        f.trail.emitting = true;
        f.emitUntil = Time.time + seconds;
    }

    // ---------- Verejné: schopnosti ----------
    /// <summary>EMP impulz: modrá elektrická vlna po zemi, výboje do strán a krátke zrušenie farieb obrazu.</summary>
    public static void Emp(Vector3 center, float radius)
    {
        CombatFx fx = I;
        Color blue = new Color(0.35f, 0.8f, 1f);
        Vector3 ground = center + Vector3.up * 0.15f;
        const float seconds = 0.42f;

        for (int i = 0; i < 160; i++)
        {
            float a = i / 160f * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Emit(fx.glow, ground + dir * 0.4f, dir * (radius / seconds), 0.8f, seconds, blue);
            if ((i & 3) == 0) Emit(fx.glow, ground + dir * 0.2f, dir * (radius / seconds) * 0.72f, 0.5f, seconds, Color.white);
            if ((i & 7) == 0) Emit(fx.sparks, ground + dir * 0.4f, dir * (radius / seconds) * 0.8f + Vector3.up * Random.Range(0.5f, 2.5f), 0.07f, seconds * 1.2f, Color.white);
        }
        for (int i = 0; i < 7; i++)
        {
            float a = (i + Random.value * 0.6f) / 7f * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Lightning(center + Vector3.up * 0.9f, ground + dir * radius * Random.Range(0.7f, 1f), blue, 0.07f);
        }

        Emit(fx.glow, center + Vector3.up * 0.9f, Vector3.zero, 4.5f, 0.2f, blue);
        fx.Flare(center + Vector3.up * 1.2f, blue, radius * 2.2f, 5f, 0.35f);
        fx.desaturateStart = Time.time;
        fx.desaturateSeconds = 0.55f;
        Shake(0.14f);
    }

    /// <summary>Tieniaci skok: čierny dym na mieste odchodu aj príchodu a fialové stopy medzi nimi.</summary>
    public static void ShadowStep(Vector3 from, Vector3 to)
    {
        CombatFx fx = I;
        Color black = new Color(0.01f, 0f, 0.02f, 1f);
        Color purple = new Color(0.6f, 0.15f, 1f);

        fx.ShadowPuff(from, black, purple);
        fx.ShadowPuff(to, black, purple);

        Vector3 a = from + Vector3.up * 0.9f, b = to + Vector3.up * 0.9f;
        int steps = Mathf.Clamp(Mathf.RoundToInt(Vector3.Distance(a, b) * 3f), 8, 48);
        Vector3 dir = (b - a).normalized;
        for (int i = 0; i < steps; i++)
        {
            Vector3 p = Vector3.Lerp(a, b, i / (float)(steps - 1)) + Random.insideUnitSphere * 0.25f;
            Emit(fx.sparks, p, dir * Random.Range(4f, 9f), 0.09f, Random.Range(0.25f, 0.5f), purple);
            if ((i & 1) == 0) Emit(fx.motes, p, Random.insideUnitSphere * 0.4f, 0.14f, Random.Range(0.5f, 0.9f), purple);
        }
        fx.Flare(b, purple, 6f, 2.5f, 0.3f);
    }

    void ShadowPuff(Vector3 position, Color black, Color purple)
    {
        for (int i = 0; i < 36; i++)
        {
            Vector3 offset = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0.1f, 1.7f), Random.Range(-0.4f, 0.4f));
            Vector3 velocity = new Vector3(offset.x, 0.2f, offset.z) * 2.2f;
            Emit(smoke, position + offset, velocity, Random.Range(1.1f, 1.9f), Random.Range(0.7f, 1.2f), black);
        }
        Burst(motes, position + Vector3.up * 0.9f, 14, 1.5f, 0.12f, 0.8f, purple, Vector3.up * 0.3f);
    }

    /// <summary>Krvavé šialenstvo: dve červené stopy za postavou počas výpadu.</summary>
    public static void FeralRush(Transform runner)
    {
        Color red = new Color(1.6f, 0.08f, 0.1f, 0.9f);
        FollowTrail(runner, Vector3.up * 0.45f, red, 0.5f, 0.35f);
        FollowTrail(runner, Vector3.down * 0.2f, red, 0.35f, 0.35f);
    }

    /// <summary>Jedna vlna ohnivého výbuchu: prstenec plameňov, žeravé uhlíky a teplý svit na okolí.</summary>
    public static void FireWave(Vector3 center, float radius, int wave)
    {
        CombatFx fx = I;
        Vector3 ground = center + Vector3.up * 0.2f;
        const float seconds = 0.4f;
        Color fireTint = new Color(1f, 1f, 1f, 0.45f);   // plamene sa prekrývajú; plné krytie by sa zlialo do bielej
        float reach = radius * (0.75f + 0.125f * wave);
        int count = 44 + wave * 8;

        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.value) / count * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 velocity = dir * (reach / seconds) * Random.Range(0.75f, 1.05f) + Vector3.up * Random.Range(0.6f, 2.2f);
            Emit(fx.fire, ground + dir * 0.3f, velocity, Random.Range(0.55f, 1.05f), seconds * Random.Range(1f, 1.6f), fireTint);
            if (i % 3 == 0) Emit(fx.sparks, ground + dir * 0.3f, velocity * 1.2f + Vector3.up * 3f, 0.07f, 0.7f, new Color(1f, 0.6f, 0.2f));
        }
        for (int i = 0; i < 6; i++)
            Emit(fx.fire, ground + Random.insideUnitSphere * 0.5f, Vector3.up * Random.Range(2f, 4.5f), Random.Range(0.7f, 1.2f), 0.5f, fireTint);

        fx.Flare(center + Vector3.up * 1.4f, new Color(1f, 0.55f, 0.2f), radius * 4f, 2.6f + 0.5f * wave, 0.5f);
        Shake(0.08f + 0.04f * wave);
    }

    /// <summary>Krvavý závan: krvavé orby vyletia z cieľa a vsajú sa do hráča.</summary>
    public static void BloodOrbs(Vector3 from, Transform to, int count)
    {
        if (to == null) return;
        CombatFx fx = I;
        Color red = new Color(1f, 0.04f, 0.1f);

        for (int i = 0; i < count; i++)
        {
            int slot = fx.nextOrb;
            fx.nextOrb = (fx.nextOrb + 1) % fx.orbs.Length;
            Vector3 start = from + Vector3.up * Random.Range(0f, 0.8f);
            fx.orbs[slot] = new Orb
            {
                start = start,
                // Oblúk: každý orb vyletí najprv nabok a nahor, až potom ho stiahne hráč
                control = start + Random.onUnitSphere * 1.6f + Vector3.up * Random.Range(1.2f, 2.4f),
                target = to,
                startTime = Time.time + i * 0.06f,
                duration = Random.Range(0.5f, 0.75f),
                color = red,
                active = true
            };
        }
        fx.Burst(fx.blood, from + Vector3.up, 8, 3f, 0.1f, 0.5f, new Color(0.5f, 0.01f, 0.03f, 0.9f), Vector3.up * 0.5f);
    }

    /// <summary>Blesk s lomenou dráhou a vetvami, ktorý sa počas trvania prekresľuje.</summary>
    public static void Lightning(Vector3 from, Vector3 to, Color color, float width = 0.11f)
    {
        CombatFx fx = I;
        Bolt bolt = fx.bolts[fx.nextBolt];
        fx.nextBolt = (fx.nextBolt + 1) % fx.bolts.Length;

        bolt.from = from;
        bolt.to = to;
        bolt.color = color;
        bolt.width = width;
        bolt.duration = fx.boltSeconds;
        bolt.until = Time.time + fx.boltSeconds;
        bolt.nextJitter = 0f;

        Emit(fx.glow, to, Vector3.zero, 1.5f, 0.15f, color);
        fx.Burst(fx.sparks, to, 8, 5f, 0.06f, 0.3f, Color.white, Vector3.up * 0.3f);
        fx.Flare(to, color, 7f, 3.2f, fx.boltSeconds);
    }

    /// <summary>Blesk s vlastnou dĺžkou trvania (náhrada rovného lúča pri bleskových útokoch); šírka je v mierke pôvodných lúčov.</summary>
    public static void Lightning(Vector3 from, Vector3 to, Color color, float seconds, float beamWidth)
    {
        CombatFx fx = I;
        Bolt bolt = fx.bolts[fx.nextBolt];
        Lightning(from, to, color, Mathf.Clamp(beamWidth * 0.6f, 0.08f, 0.32f));
        bolt.duration = Mathf.Max(seconds, fx.boltSeconds);
        bolt.until = Time.time + bolt.duration;
    }

    /// <summary>
    /// Energetický lúč medzi dvoma bodmi (strela, laser, mieridlo). Krátke lúče (pod 0,12 s) sú tenké mieriace čiary,
    /// ktoré sa volajú každý snímok; dlhšie sú výstrel so žiarou a iskrami v cieli.
    /// </summary>
    public static void Beam(Vector3 from, Vector3 to, Color color, float seconds, float width)
    {
        CombatFx fx = I;
        BeamFx beam = fx.beams[fx.nextBeam];
        fx.nextBeam = (fx.nextBeam + 1) % fx.beams.Length;

        bool dark = Mathf.Max(color.r, Mathf.Max(color.g, color.b)) < 0.35f;   // tieň: sčítaním farieb by ho nebolo vidno
        beam.strike = seconds >= 0.12f;
        beam.color = color;
        beam.start = Time.time;
        beam.duration = Mathf.Max(0.03f, seconds);
        beam.width = beam.strike ? Mathf.Clamp(width * 1.7f, 0.14f, 1.5f) : Mathf.Clamp(width * 1.3f, 0.05f, 0.5f);
        beam.line.sharedMaterial = dark ? fx.lineAlphaMaterial : fx.lineMaterial;
        beam.line.SetPosition(0, from);
        beam.line.SetPosition(1, to);
        fx.PaintBeam(beam, 0f);
        beam.line.enabled = true;

        if (!beam.strike || dark) return;
        Vector3 back = (from - to).normalized;
        Emit(fx.glow, to, Vector3.zero, 0.9f + beam.width * 3f, 0.2f, color);
        Emit(fx.glow, from, Vector3.zero, 0.5f + beam.width * 1.5f, 0.14f, color);
        fx.Burst(fx.sparks, to, 7, 5f, 0.06f, 0.32f, Color.Lerp(color, Color.white, 0.5f), back * 0.6f);
    }

    void PaintBeam(BeamFx beam, float u)
    {
        float fade = beam.strike ? (1f - u) * (1f - u) : 1f - u * 0.5f;
        Color c = beam.color * (beam.strike ? 1.9f : 1.2f);
        c.a = beam.color.a * fade;
        beam.line.startColor = c;
        beam.line.endColor = c;
        beam.line.widthMultiplier = beam.width * (beam.strike ? 1f - 0.6f * u : 1f);
    }

    /// <summary>
    /// Vlna po zemi do daného polomeru (výbuch, impulz, výstraha). Krátke volania (pod 0,12 s, každý snímok) kreslia
    /// stály kruh danej veľkosti; dlhšie rozpínajúci sa prstenec so žiarou na čele a obrysom cieľovej plochy.
    /// </summary>
    public static void Ring(Vector3 center, float radius, Color color, float seconds)
    {
        if (radius <= 0.01f) return;
        CombatFx fx = I;
        bool dark = Mathf.Max(color.r, Mathf.Max(color.g, color.b)) < 0.35f;
        float size = radius * 2f / RingEdge;
        if (color.a <= 0f) color.a = 1f;

        if (seconds < 0.12f)
        {
            // Volá sa každý snímok: kópie sa prekrývajú, preto slabšie krytie
            color.a *= 0.45f;
            Emit(dark ? fx.circlesDark : fx.circles, center, Vector3.zero, size, Mathf.Max(0.06f, seconds * 1.5f), color);
            return;
        }

        Emit(dark ? fx.ringsDark : fx.rings, center, Vector3.zero, size, seconds, color);
        Color outline = color;
        outline.a *= 0.4f;
        Emit(dark ? fx.circlesDark : fx.circles, center, Vector3.zero, size, seconds, outline);
        if (dark) return;

        // Žiara na čele vlny a iskry odletujúce nahor
        int count = Mathf.Clamp(Mathf.RoundToInt(radius * 6f), 16, 96);
        float speed = radius * 0.88f / seconds;
        Color front = color;
        front.a = Mathf.Min(1f, color.a * 0.9f);
        Vector3 ground = center + Vector3.up * 0.12f;
        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.value * 0.5f) / count * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Emit(fx.wave, ground + dir * (radius * 0.12f), dir * speed, 0.45f + radius * 0.035f, seconds * Random.Range(0.85f, 1f), front);
            if ((i & 3) == 0) Emit(fx.sparks, ground + dir * (radius * 0.12f), dir * speed * 0.8f + Vector3.up * Random.Range(1f, 3.5f), 0.06f, seconds, Color.Lerp(color, Color.white, 0.6f));
        }
        if (radius >= 2.5f && seconds <= 1.3f) fx.Flare(center + Vector3.up * 1.2f, color, Mathf.Min(radius * 2.2f, 40f), 2.4f, Mathf.Min(seconds, 0.45f));
    }

    /// <summary>Kŕmenie: volať každý snímok počas pitia. Krvavá hmla okolo cieľa, prúd krvi k hráčovi, červené okraje, tlmený zvuk.</summary>
    public static void Feeding(Transform target, Transform drinker)
    {
        CombatFx fx = I;
        fx.feedTarget = target;
        fx.feedDrinker = drinker;
        fx.feedUntil = Time.time + 0.15f;
    }

    // ---------- Vnútorné ----------
    GameObject PlayerObject()
    {
        if (player == null && Time.unscaledTime >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.unscaledTime + 2f;
            player = FindAnyObjectByType<PlayerStats>();
        }
        return player != null ? player.gameObject : null;
    }

    Body BodyOf(Damageable target)
    {
        if (bodies.TryGetValue(target, out Body body) && body.renderers.Length > 0 && body.renderers[0] != null) return body;
        if (bodies.Count > 150) bodies.Clear();   // mŕtve a zničené postavy sa inak hromadia

        target.GetComponentsInChildren(rendererBuffer);
        int count = 0;
        for (int i = 0; i < rendererBuffer.Count; i++)
            if (IsBodyRenderer(rendererBuffer[i])) count++;

        body = new Body { renderers = new Renderer[count], subMeshes = new int[count], height = 1.8f, centerY = 0.9f };
        float top = float.MinValue, bottom = float.MaxValue, radius = 0.3f;
        int k = 0;
        for (int i = 0; i < rendererBuffer.Count; i++)
        {
            Renderer r = rendererBuffer[i];
            if (!IsBodyRenderer(r)) continue;
            body.renderers[k] = r;
            int subs = 1;
            if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) subs = skinned.sharedMesh.subMeshCount;
            else if (r.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) subs = filter.sharedMesh.subMeshCount;
            body.subMeshes[k++] = subs;
            if (r.bounds.max.y > top) top = r.bounds.max.y;
            if (r.bounds.min.y < bottom) bottom = r.bounds.min.y;
            radius = Mathf.Max(radius, Mathf.Min(r.bounds.extents.x, r.bounds.extents.z));
        }
        rendererBuffer.Clear();
        if (count > 0)
        {
            body.height = Mathf.Clamp(top - bottom, 0.6f, 6f);
            body.bottomY = bottom - target.transform.position.y;
            body.centerY = body.bottomY + body.height * 0.55f;
            body.radius = Mathf.Clamp(radius, 0.25f, 2.5f);
        }

        bodies[target] = body;
        return body;
    }

    static bool IsBodyRenderer(Renderer r)
    {
        return r is SkinnedMeshRenderer || r is MeshRenderer;
    }

    void AddFlash(Body body, Color color, float seconds, float rim)
    {
        if (flashMaterial == null || body.renderers.Length == 0) return;

        int slot = 0;
        float oldest = float.MaxValue;
        for (int i = 0; i < flashes.Length; i++)
        {
            float end = flashes[i].start + flashes[i].duration;
            if (flashes[i].renderers == body.renderers && flashes[i].rim == rim) { slot = i; break; }
            if (end < oldest) { oldest = end; slot = i; }
        }
        flashes[slot] = new Flash { renderers = body.renderers, subMeshes = body.subMeshes, color = color, start = Time.time, duration = seconds, rim = rim };
    }

    void AddStatus(Damageable target, StatusKind kind, float seconds, Color color)
    {
        int slot = -1;
        float soonest = float.MaxValue;
        for (int i = 0; i < statuses.Length; i++)
        {
            if (statuses[i].owner == target && statuses[i].kind == kind) { slot = i; break; }
            if (statuses[i].until < soonest) { soonest = statuses[i].until; slot = i; }
        }
        Body body = BodyOf(target);
        statuses[slot] = new Status
        {
            target = target.transform, owner = target, kind = kind, until = Time.time + seconds,
            nextEmit = 0f, height = body.height, bottomY = body.bottomY, radius = Mathf.Max(0.25f, body.radius), color = color
        };
    }

    void AddPush(Transform target, Vector3 direction, float distance)
    {
        // Za postavou stena: odraz by ju do nej vtlačil
        int hits = Physics.RaycastNonAlloc(target.position + Vector3.up * 0.9f, direction, rayBuffer, distance + 0.5f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
            if (!rayBuffer[i].transform.IsChildOf(target)) return;

        int slot = 0;
        for (int i = 0; i < pushes.Length; i++)
        {
            if (pushes[i].target == target || pushes[i].remaining <= 0f) { slot = i; break; }
        }
        pushes[slot] = new Push { target = target, direction = direction, remaining = distance };
    }

    // Krvavé čiary: tri krátke šikmé seky cez trup
    void Slashes(Vector3 center, float height)
    {
        Color red = new Color(1f, 0.05f, 0.08f);
        Vector3 right = flashCamera != null ? flashCamera.transform.right : Vector3.right;
        for (int s = 0; s < 3; s++)
        {
            float tilt = Random.Range(-55f, 55f);
            Vector3 dir = Quaternion.AngleAxis(tilt, Vector3.Cross(right, Vector3.up)) * right * (Random.value < 0.5f ? 1f : -1f);
            Vector3 origin = center + Vector3.up * Random.Range(-0.3f, 0.3f) * height - dir * 0.45f;
            for (int i = 0; i < 7; i++)
                Emit(sparks, origin + dir * (i * 0.13f), dir * 9f, 0.1f, 0.16f + i * 0.012f, red);
        }
    }

    void BodyArcs(Vector3 center, float height, Color color)
    {
        for (int i = 0; i < 2; i++)
        {
            Vector3 a = center + Random.insideUnitSphere * 0.45f * height;
            Vector3 b = center + Random.insideUnitSphere * 0.45f * height;
            Bolt bolt = bolts[nextBolt];
            nextBolt = (nextBolt + 1) % bolts.Length;
            bolt.from = a; bolt.to = b; bolt.color = color; bolt.width = 0.05f;
            bolt.duration = 0.16f; bolt.until = Time.time + 0.16f; bolt.nextJitter = 0f;
        }
    }

    IEnumerator TumbleRoutine(Transform body, Vector3 away)
    {
        Vector3 startPosition = body.position;
        Quaternion startRotation = body.rotation;

        // Koncovú polohu na zemi spočíta pôvodné položenie tela; potom sa telo vráti a dopadne tam plynule
        Corpse.LayDown(body);
        Vector3 endPosition = body.position + away * 0.3f;
        // Položené telo má hlavu v smere svojho -X, preto sa natočí tak, aby padlo od útočníka
        float endYaw = Quaternion.LookRotation(Vector3.Cross(-away, Vector3.up)).eulerAngles.y + Random.Range(-30f, 30f);
        float startYaw = startRotation.eulerAngles.y;
        body.SetPositionAndRotation(startPosition, startRotation);

        float sway = Random.Range(7f, 12f) * (Random.value < 0.5f ? 1f : -1f);
        float t = 0f;
        float roll = 0f;
        while (t < staggerSeconds)
        {
            if (body == null) yield break;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / staggerSeconds);
            // Zatackanie: kolísanie do strán, pretočenie a krok vzad
            roll = Mathf.Sin(u * Mathf.PI * 2.4f) * sway;
            float pitch = Mathf.Sin(u * Mathf.PI * 1.6f + 0.8f) * 6f;
            float yaw = Mathf.LerpAngle(startYaw, endYaw, u * u * (3f - 2f * u));
            body.SetPositionAndRotation(startPosition + away * (0.3f * u), Quaternion.Euler(pitch, yaw, roll));
            yield return null;
        }

        Vector3 midPosition = startPosition + away * 0.3f;
        float fromRoll = roll;
        t = 0f;
        while (t < fallSeconds)
        {
            if (body == null) yield break;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / fallSeconds);
            float fall = u * u;                                   // pád zrýchľuje
            float bounce = u > 0.82f ? Mathf.Sin((u - 0.82f) / 0.18f * Mathf.PI) * 5f : 0f;
            body.SetPositionAndRotation(
                Vector3.Lerp(midPosition, endPosition, fall) + Vector3.up * (bounce * 0.012f),
                Quaternion.Euler(0f, endYaw, Mathf.Lerp(fromRoll, 90f, fall) - bounce));
            yield return null;
        }

        if (body == null) yield break;
        body.SetPositionAndRotation(endPosition, Quaternion.Euler(0f, endYaw, 90f));
        Burst(smoke, endPosition + Vector3.up * 0.1f, 6, 1.2f, 0.7f, 0.7f, new Color(0.35f, 0.32f, 0.3f, 0.35f), Vector3.up * 0.2f);
    }

    // ---------- Každý snímok ----------
    void Update()
    {
        float now = Time.time;

        // Hit stop beží v reálnom čase
        if (hitStopUntil > 0f && Time.unscaledTime >= hitStopUntil)
        {
            hitStopUntil = -1f;
            if (Mathf.Approximately(Time.timeScale, hitStopTimeScale)) Time.timeScale = 1f;
        }

        UpdateShake();
        UpdateStatuses(now);
        UpdatePushes();
        UpdateOrbs(now);
        UpdateBolts(now);
        UpdateBeams(now);
        UpdateLights(now);
        UpdateFeeding();

        float desaturate = 1f - Mathf.Clamp01((now - desaturateStart) / desaturateSeconds);
        AtmosphereDirector.Desaturate = Mathf.Sqrt(desaturate);   // drží sa takmer naplno a ku koncu rýchlo pustí

        if (swordTrail != null && swordTrail.emitting && now >= swordTrailUntil) swordTrail.emitting = false;
    }

    void LateUpdate()
    {
        float now = Time.time;
        for (int i = 0; i < followers.Length; i++)
        {
            Follower f = followers[i];
            if (!f.trail.emitting) continue;
            if (f.target == null || now >= f.emitUntil) { f.trail.emitting = false; continue; }
            f.root.position = f.target.position + f.offset;
        }
        UpdateFlashes(now);
    }

    void UpdateShake()
    {
        if (shake <= 0.0005f)
        {
            shake = 0f;
            ShakeOffset = Vector3.zero;
            return;
        }

        float t = Time.unscaledTime * shakeFrequency;
        Transform cam = flashCamera != null ? flashCamera.transform : null;
        Vector3 right = cam != null ? cam.right : Vector3.right;
        Vector3 up = cam != null ? cam.up : Vector3.up;
        ShakeOffset = (right * (Mathf.PerlinNoise(t, 0.3f) - 0.5f) + up * (Mathf.PerlinNoise(0.7f, t) - 0.5f)) * (2f * shake);
        shake = Mathf.MoveTowards(shake, 0f, shakeDecay * Mathf.Max(0.15f, shake) * 4f * Time.unscaledDeltaTime);
    }

    void UpdateStatuses(float now)
    {
        for (int i = 0; i < statuses.Length; i++)
        {
            if (statuses[i].until <= 0f) continue;
            Status s = statuses[i];
            bool gone = s.target == null || now >= s.until || (s.kind != StatusKind.Dissolve && (s.owner == null || s.owner.IsDead));
            if (gone) { statuses[i].until = 0f; continue; }
            if (now < s.nextEmit) continue;

            // Na plášti tela, nie vo vnútri: častice v modeli by zakryla jeho vlastná sieť
            float angle = Random.value * Mathf.PI * 2f;
            float reach = s.radius * Random.Range(0.95f, 1.25f);
            Vector3 p = s.target.position + new Vector3(Mathf.Cos(angle) * reach, s.bottomY + Random.Range(0.1f, 1f) * s.height, Mathf.Sin(angle) * reach);
            switch (s.kind)
            {
                case StatusKind.Burn:
                    statuses[i].nextEmit = now + 0.016f;
                    Emit(fire, p, Vector3.up * Random.Range(0.8f, 2f), Random.Range(0.45f, 0.9f), Random.Range(0.4f, 0.7f), new Color(1f, 1f, 1f, 0.7f));
                    if (Random.value < 0.25f) Emit(sparks, p, Vector3.up * Random.Range(2f, 4f) + Random.insideUnitSphere, 0.05f, 0.6f, new Color(1f, 0.6f, 0.2f));
                    AddFlashFor(s.owner, new Color(0.9f, 0.3f, 0.05f) * (0.35f + 0.25f * Mathf.Sin(now * 22f)), 0.08f, 0.6f);
                    break;

                case StatusKind.Bleed:
                    statuses[i].nextEmit = now + 0.09f;
                    Emit(blood, p, Vector3.down * Random.Range(0.5f, 1.5f) + Random.insideUnitSphere * 0.4f, 0.08f, 0.55f, new Color(0.5f, 0.01f, 0.03f, 0.9f));
                    break;

                default:
                    statuses[i].nextEmit = now + 0.008f;
                    Emit(motes, p, Vector3.up * Random.Range(0.8f, 2.2f) + Random.insideUnitSphere * 0.6f, Random.Range(0.14f, 0.3f), Random.Range(0.6f, 1.1f), s.color);
                    if (Random.value < 0.45f) Emit(smoke, p, Vector3.up * 0.8f, Random.Range(0.7f, 1.3f), 0.9f, new Color(0.03f, 0.02f, 0.05f, 0.7f));
                    break;
            }
        }
    }

    void AddFlashFor(Damageable target, Color color, float seconds, float rim)
    {
        if (target != null) AddFlash(BodyOf(target), color, seconds, rim);
    }

    void UpdatePushes()
    {
        float step = knockbackSpeed * Time.deltaTime;
        for (int i = 0; i < pushes.Length; i++)
        {
            if (pushes[i].remaining <= 0f) continue;
            if (pushes[i].target == null) { pushes[i].remaining = 0f; continue; }

            // Spomaľuje ku koncu, aby odraz pôsobil ako úder, nie posun
            float move = Mathf.Min(pushes[i].remaining, step * Mathf.Lerp(0.5f, 1.6f, Mathf.Clamp01(pushes[i].remaining / 0.4f)));
            pushes[i].target.position += pushes[i].direction * move;
            pushes[i].remaining -= move;
        }
    }

    void UpdateOrbs(float now)
    {
        for (int i = 0; i < orbs.Length; i++)
        {
            if (!orbs[i].active || now < orbs[i].startTime) continue;
            Orb o = orbs[i];
            if (o.target == null) { orbs[i].active = false; continue; }

            float u = (now - o.startTime) / o.duration;
            Vector3 end = o.target.position + Vector3.up * 0.4f;
            if (u >= 1f)
            {
                orbs[i].active = false;
                Emit(glow, end, Vector3.zero, 0.9f, 0.15f, o.color);
                Burst(sparks, end, 4, 2.5f, 0.06f, 0.25f, o.color, Vector3.zero);
                continue;
            }

            float e = u * u;   // zrýchľuje k hráčovi
            Vector3 p = Vector3.Lerp(Vector3.Lerp(o.start, o.control, e), Vector3.Lerp(o.control, end, e), e);
            Emit(glow, p, Vector3.zero, 0.42f, 0.09f, o.color);
            Emit(motes, p, Random.insideUnitSphere * 0.3f, 0.12f, 0.3f, o.color);
        }
    }

    void UpdateBeams(float now)
    {
        for (int i = 0; i < beams.Length; i++)
        {
            BeamFx beam = beams[i];
            if (!beam.line.enabled) continue;
            float u = (now - beam.start) / beam.duration;
            if (u >= 1f) { beam.line.enabled = false; continue; }
            PaintBeam(beam, u);
        }
    }

    void UpdateBolts(float now)
    {
        for (int i = 0; i < bolts.Length; i++)
        {
            Bolt b = bolts[i];
            if (!b.main.enabled && now >= b.until) continue;
            if (now >= b.until)
            {
                b.main.enabled = false;
                for (int k = 0; k < b.branches.Length; k++) b.branches[k].enabled = false;
                continue;
            }

            float life = Mathf.Clamp01((b.until - now) / Mathf.Max(0.01f, b.duration));
            Color c = b.color * (0.9f + 0.7f * life);
            c.a = life;
            float width = b.width * (0.5f + 0.5f * life);

            if (now >= b.nextJitter)
            {
                b.nextJitter = now + 0.045f;   // dráha sa prekresľuje, blesk "žije"
                Shape(b.main, b.from, b.to, boltJaggedness);
                Vector3 axis = b.to - b.from;
                float length = axis.magnitude;
                for (int k = 0; k < b.branches.Length; k++)
                {
                    LineRenderer branch = b.branches[k];
                    if (length < 1.5f) { branch.enabled = false; continue; }
                    Vector3 origin = b.main.GetPosition(Random.Range(1, Mathf.Max(2, b.main.positionCount - 1)));
                    Vector3 side = Vector3.Cross(axis, Random.onUnitSphere).normalized;
                    Vector3 tip = origin + (axis.normalized * Random.Range(0.3f, 0.8f) + side * Random.Range(0.5f, 1f)) * (length * Random.Range(0.12f, 0.3f));
                    Shape(branch, origin, tip, boltJaggedness * 1.3f);
                    branch.enabled = true;
                }
                b.main.enabled = true;
            }

            b.main.startColor = c; b.main.endColor = c;
            b.main.widthMultiplier = width;
            for (int k = 0; k < b.branches.Length; k++)
            {
                LineRenderer branch = b.branches[k];
                if (!branch.enabled) continue;
                branch.startColor = c; branch.endColor = new Color(c.r, c.g, c.b, 0f);
                branch.widthMultiplier = width * 0.55f;
            }
        }
    }

    // Lomená dráha: body rozložené po úsečke a vychýlené do strán, na koncoch menej
    static void Shape(LineRenderer line, Vector3 from, Vector3 to, float jaggedness)
    {
        Vector3 axis = to - from;
        float length = axis.magnitude;
        int count = Mathf.Clamp(Mathf.RoundToInt(length * 3.2f) + 3, 4, boltPoints.Length);
        Vector3 forward = length > 0.001f ? axis / length : Vector3.up;
        Vector3 side = Vector3.Cross(forward, Mathf.Abs(forward.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 up = Vector3.Cross(forward, side);
        float amplitude = Mathf.Min(length * jaggedness, 0.55f);

        for (int i = 0; i < count; i++)
        {
            float u = i / (float)(count - 1);
            float envelope = Mathf.Sin(u * Mathf.PI);
            boltPoints[i] = from + axis * u + (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * (amplitude * envelope);
        }
        line.positionCount = count;
        line.SetPositions(boltPoints);
    }

    void UpdateLights(float now)
    {
        for (int i = 0; i < lights.Length; i++)
        {
            FlashLight f = lights[i];
            if (!f.light.enabled) continue;
            float u = (now - f.start) / Mathf.Max(0.01f, f.duration);
            if (u >= 1f) { f.light.enabled = false; continue; }
            f.light.intensity = f.intensity * (1f - u) * (1f - u);
        }
    }

    void UpdateFeeding()
    {
        bool active = Time.time < feedUntil && feedTarget != null;
        feed = Mathf.MoveTowards(feed, active ? 1f : 0f, feedBlendSpeed * Time.deltaTime);
        AtmosphereDirector.FeedAmount = feed;
        UnderwaterEffect.ExtraMuffle = feed;
        if (!active || Time.time < nextFeedEmit) return;
        nextFeedEmit = Time.time + 0.025f;

        // Krvavá hmla okolo cieľa a tenký prúd krvi k ústam hráča
        Vector3 chest = feedTarget.position + Vector3.up * 0.4f;
        Emit(smoke, chest + Random.insideUnitSphere * 0.5f, Random.insideUnitSphere * 0.3f + Vector3.up * 0.2f,
            Random.Range(0.7f, 1.3f), Random.Range(0.7f, 1.2f), new Color(0.5f, 0.01f, 0.03f, 0.55f));
        if (feedDrinker == null) return;
        Vector3 mouth = feedDrinker.position + Vector3.up * 0.8f;
        Vector3 start = chest + Random.insideUnitSphere * 0.25f;
        const float travel = 0.35f;
        Emit(glow, start, (mouth - start) / travel, 0.24f, travel, new Color(1f, 0.05f, 0.1f));
    }

    void UpdateFlashes(float now)
    {
        if (flashCamera == null)
        {
            if (Time.unscaledTime < nextCameraSearch) return;
            nextCameraSearch = Time.unscaledTime + 1f;
            flashCamera = Camera.main;
            if (flashCamera == null) return;
            if (flashBuffer == null) flashBuffer = new CommandBuffer { name = "CombatFx: bliknutie postáv" };
            flashCamera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, flashBuffer);
        }

        bool any = false;
        for (int i = 0; i < flashes.Length; i++)
        {
            if (flashes[i].renderers == null) continue;
            if (now >= flashes[i].start + flashes[i].duration) { flashes[i].renderers = null; continue; }
            any = true;
        }

        if (!any)
        {
            if (flashBufferFilled) { flashBuffer.Clear(); flashBufferFilled = false; }
            return;
        }

        flashBuffer.Clear();
        flashBufferFilled = true;
        for (int i = 0; i < flashes.Length; i++)
        {
            Flash f = flashes[i];
            if (f.renderers == null) continue;

            float u = (now - f.start) / f.duration;
            Color c = f.color * (1f - u);
            c.a = f.rim;
            flashBuffer.SetGlobalColor(FlashColorId, c);
            for (int k = 0; k < f.renderers.Length; k++)
            {
                Renderer r = f.renderers[k];
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                for (int m = 0; m < f.subMeshes[k]; m++) flashBuffer.DrawRenderer(r, flashMaterial, m, 0);
            }
        }
    }
}
