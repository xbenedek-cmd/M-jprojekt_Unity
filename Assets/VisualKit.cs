using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Spoločné stavebné diely pre vizuál arén a bossov bez externých assetov: textúry a materiály vyrobené kódom,
/// primitívy bez kolízie a slučkové časticové systémy. Všetko sa vyrába raz a zdieľa.
/// </summary>
public static class VisualKit
{
    private static Texture2D soft, runeRing;
    private static Material additive, smoke, ringMaterial;
    private static Mesh quad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        soft = runeRing = null;
        additive = smoke = ringMaterial = null;
        quad = null;
    }

    static Shader Unlit => Resources.Load<Shader>("AtmosphereUnlit");

    static Material Blend(Texture texture, BlendMode src, BlendMode dst, bool zWrite, int queue)
    {
        var m = new Material(Unlit) { hideFlags = HideFlags.HideAndDontSave, mainTexture = texture };
        m.SetFloat("_SrcBlend", (float)src);
        m.SetFloat("_DstBlend", (float)dst);
        m.SetFloat("_ZWrite", zWrite ? 1f : 0f);
        m.renderQueue = queue;
        return m;
    }

    /// <summary>Žiarivé častice (farby sa sčítajú).</summary>
    public static Material Additive
    {
        get
        {
            if (additive == null) additive = Blend(Soft, BlendMode.SrcAlpha, BlendMode.One, false, (int)RenderQueue.Transparent);
            return additive;
        }
    }

    /// <summary>Dym a hmla (bežná priehľadnosť).</summary>
    public static Material Smoke
    {
        get
        {
            if (smoke == null) smoke = Blend(Soft, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false, (int)RenderQueue.Transparent);
            return smoke;
        }
    }

    /// <summary>Runový kruh na zem; farba sa dáva cez MaterialPropertyBlock (_Color).</summary>
    public static Material RuneRing
    {
        get
        {
            if (ringMaterial == null) ringMaterial = Blend(RuneRingTexture, BlendMode.SrcAlpha, BlendMode.One, false, (int)RenderQueue.Transparent - 10);
            return ringMaterial;
        }
    }

    static Texture2D Soft
    {
        get
        {
            if (soft != null) return soft;
            const int size = 64;
            soft = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255f));
                }
            soft.SetPixels32(pixels);
            soft.Apply(true, true);
            return soft;
        }
    }

    // Dva tenké kruhy a medzi nimi pás "rún" (nepravidelné čiarky po obvode)
    static Texture2D RuneRingTexture
    {
        get
        {
            if (runeRing != null) return runeRing;
            const int size = 512;
            runeRing = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - half, dy = y - half;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / half;
                    float angle = Mathf.Atan2(dy, dx);
                    float a = Line(d, 0.97f, 0.012f) + Line(d, 0.86f, 0.008f) + 0.6f * Line(d, 0.62f, 0.006f);
                    if (d > 0.875f && d < 0.955f)
                    {
                        // 36 znakov: každý je inak široký a niektoré majú priečku
                        float cell = (angle + Mathf.PI) / (Mathf.PI * 2f) * 36f;
                        int index = Mathf.FloorToInt(cell);
                        float inCell = cell - index;
                        float seed = Mathf.Repeat(Mathf.Sin(index * 12.9898f) * 43758.5453f, 1f);
                        float width = 0.18f + 0.3f * seed;
                        bool stroke = Mathf.Abs(inCell - 0.5f) < width * 0.5f && (seed > 0.3f || Mathf.Abs(d - 0.915f) < 0.012f);
                        bool bar = seed > 0.55f && Mathf.Abs(d - (0.895f + 0.04f * seed)) < 0.006f && Mathf.Abs(inCell - 0.5f) < 0.42f;
                        if (stroke && Mathf.Abs(inCell - 0.5f) > width * 0.5f - 0.07f || bar) a += 0.9f;
                    }
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            runeRing.SetPixels32(pixels);
            runeRing.Apply(true, true);
            return runeRing;
        }
    }

    static float Line(float d, float at, float halfWidth)
    {
        float v = Mathf.Clamp01(1f - Mathf.Abs(d - at) / halfWidth);
        return v * v;
    }

    /// <summary>Nesvietená obloha s prechodom farieb zdola nahor (na vnútro veľkej gule okolo arény).</summary>
    public static Material Sky(Color bottom, Color horizon, Color top)
    {
        const int size = 128;
        var tex = new Texture2D(1, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var pixels = new Color[size];
        for (int y = 0; y < size; y++)
        {
            float v = y / (float)(size - 1);
            // Obzor leží v strede gule; nad ním rýchly prechod do farby neba
            pixels[y] = v < 0.5f ? Color.Lerp(bottom, horizon, Mathf.Pow(v / 0.5f, 2.2f)) : Color.Lerp(horizon, top, Mathf.Pow((v - 0.5f) / 0.5f, 0.45f));
            pixels[y].a = 1f;
        }
        tex.SetPixels(pixels);
        tex.Apply(false, true);
        Shader skyShader = Resources.Load<Shader>("ArenaSky");
        if (skyShader == null) return Blend(tex, BlendMode.One, BlendMode.Zero, true, (int)RenderQueue.Geometry + 100);
        return new Material(skyShader) { hideFlags = HideFlags.HideAndDontSave, mainTexture = tex };
    }

    /// <summary>Svietený materiál (Standard) s voliteľnou žiarou.</summary>
    public static Material Lit(Color color, float metallic, float smoothness, Color emission)
    {
        var m = new Material(Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave, color = color };
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        if (emission.maxColorComponent > 0.001f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return m;
    }

    static Mesh Quad
    {
        get
        {
            if (quad == null) quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            return quad;
        }
    }

    /// <summary>Primitív bez kolízie (kulisa); tiene vrhá len keď o to volajúci požiada.</summary>
    public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool shadows = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return go;
    }

    /// <summary>Plochý štvorec ležiaci na zemi (runové kruhy); veľkosť = priemer.</summary>
    public static Renderer FlatQuad(string name, Transform parent, Vector3 localPosition, float diameter, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = new Vector3(diameter, diameter, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = Quad;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    /// <summary>Slučkový časticový systém v kvádri daných rozmerov (prach, uhlíky, dážď, hmla).</summary>
    public static ParticleSystem Loop(string name, Transform parent, Vector3 localPosition, Vector3 box, Material material, Color color,
        float rate, Vector2 life, Vector2 size, Vector3 velocity, float spread, float gravity, bool stretched, int max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = max;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startSpeed = 0f;
        main.startColor = color;
        main.gravityModifier = gravity;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = rate;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;

        ParticleSystem.VelocityOverLifetimeModule move = ps.velocityOverLifetime;
        move.enabled = true;
        move.space = ParticleSystemSimulationSpace.World;
        move.x = new ParticleSystem.MinMaxCurve(velocity.x - spread, velocity.x + spread);
        move.y = new ParticleSystem.MinMaxCurve(velocity.y - spread * 0.5f, velocity.y + spread * 0.5f);
        move.z = new ParticleSystem.MinMaxCurve(velocity.z - spread, velocity.z + spread);

        ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (stretched)
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            r.lengthScale = 1.5f;
        }

        ps.Play();
        return ps;
    }
}
