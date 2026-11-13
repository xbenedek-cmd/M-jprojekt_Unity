using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pozadie hlavného menu: temná svätyňa ďaleko mimo mapy. Démon (model bossa alebo iného démona zo scény) stojí na stupňovitej obsidiánovej plošine
/// s otáčajúcimi sa runovými prstencami, za ním sa pomaly krúti žiarivé halo, okolo plávajú kryštály a stúpa prach. Vlastná kamera sa zapne len,
/// kým je hlavné menu otvorené (PauseMenu), potom sa všetko zničí a obnoví sa pôvodná hmla scény.
/// Plochy sa nikdy neprekrývajú v tej istej výške (predtým prstence blikali – z-fighting).
/// </summary>
public class MenuSanctuary : MonoBehaviour
{
    public static MenuSanctuary Instance { get; private set; }

    private static readonly Vector3 Origin = new Vector3(9000f, -600f, 9000f);
    private const float CenterX = 1.6f;   // stred plošiny = miesto, kde stojí démon (lokálne)

    private Camera cam;
    private Camera mainCamera;

    private class Spinner { public Transform t; public Vector3 axis; public float speed; }
    private class Floater { public Transform t; public Vector3 basePos; public float phase, bob, spin; }
    private class Pulse { public Material m; public Color color; public float phase, speed, low; }

    private readonly List<Spinner> spinners = new List<Spinner>();
    private readonly List<Floater> floaters = new List<Floater>();
    private readonly List<Pulse> pulses = new List<Pulse>();
    private Light flicker;

    private bool oldFog;
    private Color oldFogColor;
    private FogMode oldFogMode;
    private float oldFogDensity;
    private Texture2D softDot;

    public static void Show()
    {
        if (Instance != null) return;
        new GameObject("MenuSanctuary").AddComponent<MenuSanctuary>();
    }

    public static void Hide()
    {
        if (Instance != null) Destroy(Instance.gameObject);
    }

    void Awake()
    {
        Instance = this;
        transform.position = Origin;

        // Hmla len pre svätyňu; pôvodné nastavenie scény sa vráti v OnDestroy
        oldFog = RenderSettings.fog;
        oldFogColor = RenderSettings.fogColor;
        oldFogMode = RenderSettings.fogMode;
        oldFogDensity = RenderSettings.fogDensity;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.02f, 0.025f, 0.055f);
        RenderSettings.fogDensity = 0.028f;

        BuildRoom();
        BuildDemon();
        BuildCamera();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            RenderSettings.fog = oldFog;
            RenderSettings.fogColor = oldFogColor;
            RenderSettings.fogMode = oldFogMode;
            RenderSettings.fogDensity = oldFogDensity;
        }
        if (softDot != null) Destroy(softDot);
        if (flameTex != null) Destroy(flameTex);
    }

    // ---------- Kamera ----------
    void BuildCamera()
    {
        mainCamera = Camera.main;

        GameObject go = new GameObject("MenuCamera");
        go.transform.SetParent(transform, false);
        cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.015f, 0.018f, 0.04f);
        cam.fieldOfView = 38f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 90f;
        cam.depth = (mainCamera != null ? mainCamera.depth : 0f) + 10f;
        cam.allowHDR = false;
        go.transform.position = Origin + new Vector3(-2.2f, 1.7f, -8.5f);
        go.transform.LookAt(Origin + new Vector3(0.9f, 1.5f, 0f));
    }

    // Pomalý pohyb kamery a živá svätyňa (čas stojí, preto neškálovaný čas)
    void Update()
    {
        float t = Time.unscaledTime;
        float dt = Time.unscaledDeltaTime;

        if (cam != null)
        {
            cam.transform.position = Origin + new Vector3(-2.2f + Mathf.Sin(t * 0.15f) * 0.5f, 1.7f + Mathf.Sin(t * 0.21f) * 0.08f, -8.5f + Mathf.Sin(t * 0.1f) * 0.6f);
            cam.transform.LookAt(Origin + new Vector3(0.9f, 1.5f, 0f));
        }

        if (cam != null) foreach (FluidFire fire in fluidFires) if (fire != null) fire.Face(cam.transform.position);

        foreach (Spinner s in spinners) s.t.Rotate(s.axis, s.speed * dt, Space.Self);

        foreach (Floater f in floaters)
        {
            f.t.localPosition = f.basePos + Vector3.up * (Mathf.Sin(t * 0.7f + f.phase) * f.bob);
            f.t.Rotate(Vector3.up, f.spin * dt, Space.World);
        }

        foreach (Pulse p in pulses)
        {
            float k = Mathf.Lerp(p.low, 1f, 0.5f + 0.5f * Mathf.Sin(t * p.speed + p.phase));
            p.m.SetColor("_EmissionColor", p.color * k);
        }

        if (flicker != null) flicker.intensity = 2.2f + Mathf.Sin(t * 3.1f) * 0.25f + Mathf.Sin(t * 7.7f) * 0.12f;

        // Kolísavé svetlá ohnísk (Perlinov šum = nepravidelné, nie sínusovka)
        foreach (FireLight f in fireLights)
            if (f.light != null) f.light.intensity = f.baseIntensity * (0.65f + 0.7f * Mathf.PerlinNoise(t * 4.2f + f.seed, f.seed));
    }

    // ---------- Materiály ----------
    static Material Dark(Color c, float gloss = 0.35f, float metallic = 0f)
    {
        Material m = new Material(Shader.Find("Standard")) { color = c };
        m.SetFloat("_Glossiness", gloss);
        m.SetFloat("_Metallic", metallic);
        return m;
    }

    // Svietiaci materiál, ktorého jas jemne pulzuje (registruje sa do zoznamu pre Update)
    Material Glow(Color c, float intensity = 2.2f, float speed = 1.4f, float low = 0.55f)
    {
        var m = new Material(Shader.Find("Standard")) { color = c * 0.25f };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * intensity);
        pulses.Add(new Pulse { m = m, color = c * intensity, phase = Random.value * 6.28f, speed = speed, low = low });
        return m;
    }

    GameObject Part(PrimitiveType type, string name, Vector3 local, Vector3 scale, Material mat, Transform parent = null)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent != null ? parent : transform, false);
        g.transform.localPosition = local;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        Destroy(g.GetComponent<Collider>());
        return g;
    }

    // Prstenec zo segmentov (kocky pozdĺž dotyčnice): vodorovný alebo zvislý (v rovine XY)
    Transform Ring(string name, Vector3 center, float radius, int count, Vector3 segment, Material mat, bool vertical)
    {
        Transform root = new GameObject(name).transform;
        root.SetParent(transform, false);
        root.localPosition = center;

        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2f / count;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            Vector3 pos = vertical ? new Vector3(c * radius, s * radius, 0f) : new Vector3(c * radius, 0f, s * radius);
            Vector3 tangent = vertical ? new Vector3(-s, c, 0f) : new Vector3(-s, 0f, c);
            GameObject g = Part(PrimitiveType.Cube, "Segment", pos, segment, mat, root);
            g.transform.localRotation = Quaternion.LookRotation(tangent, vertical ? Vector3.forward : Vector3.up);
        }
        return root;
    }

    void Spin(Transform t, Vector3 axis, float degreesPerSecond) => spinners.Add(new Spinner { t = t, axis = axis, speed = degreesPerSecond });

    // ---------- Svätyňa ----------
    void BuildRoom()
    {
        Material stone = Dark(new Color(0.045f, 0.047f, 0.07f), 0.25f, 0.2f);
        Material obsidian = Dark(new Color(0.025f, 0.027f, 0.045f), 0.85f, 0.5f);
        Material trim = Dark(new Color(0.08f, 0.085f, 0.12f), 0.5f, 0.6f);
        Material blue = Glow(new Color(0.2f, 0.55f, 1f), 2.4f, 1.3f);
        Material violet = Glow(new Color(0.6f, 0.25f, 1f), 2.2f, 1.0f);

        // Podlaha a trojstupňová plošina: každý stupeň má vlastnú výšku horného povrchu (žiadne splývajúce plochy)
        Part(PrimitiveType.Cylinder, "Podlaha", new Vector3(CenterX, -0.5f, 0f), new Vector3(46f, 0.5f, 46f), obsidian);                 // horná plocha y = 0
        Part(PrimitiveType.Cylinder, "Stupen1", new Vector3(CenterX, 0.06f, 0f), new Vector3(14.5f, 0.06f, 14.5f), stone);               // y = 0,12
        Part(PrimitiveType.Cylinder, "Stupen2", new Vector3(CenterX, 0.16f, 0f), new Vector3(11.5f, 0.06f, 11.5f), trim);                // y = 0,22
        Part(PrimitiveType.Cylinder, "Stupen3", new Vector3(CenterX, 0.28f, 0f), new Vector3(8.6f, 0.07f, 8.6f), obsidian);              // y = 0,35

        // Rituálny kruh na plošine – celý v červenej. Spodok kociek je schovaný v plošine, vrch je o pár cm vyššie (žiadne splývajúce plochy).
        Material red = Glow(new Color(1f, 0.08f, 0.06f), 2.6f, 1.2f, 0.5f);
        Material crimson = Glow(new Color(0.85f, 0.04f, 0.08f), 2.2f, 0.9f, 0.45f);
        Material hot = Glow(new Color(1f, 0.3f, 0.08f), 2.8f, 2.0f, 0.4f);
        Material dimRed = Glow(new Color(0.7f, 0.05f, 0.05f), 1.6f, 0.7f, 0.5f);

        Transform ringOuter = Ring("RunyVonkajsie", new Vector3(CenterX, 0.38f, 0f), 4.0f, 96, new Vector3(0.1f, 0.06f, 0.30f), red, false);
        Transform ringStar = Ring("RunyKruhHviezdy", new Vector3(CenterX, 0.38f, 0f), 3.05f, 72, new Vector3(0.07f, 0.06f, 0.24f), crimson, false);
        Transform ringInner = Ring("RunyVnutorne", new Vector3(CenterX, 0.38f, 0f), 1.5f, 48, new Vector3(0.05f, 0.05f, 0.2f), hot, false);
        Spin(ringOuter, Vector3.up, 3f);
        Spin(ringInner, Vector3.up, -12f);

        // Pás glyfov medzi dvoma prstencami: malé „písmená“ z 2–3 čiarok rôznej dĺžky, pomaly sa otáča opačne ako vonkajší kruh
        Transform glyphs = new GameObject("Glyfy").transform;
        glyphs.SetParent(transform, false);
        glyphs.localPosition = new Vector3(CenterX, 0.38f, 0f);
        var glyphRng = new System.Random(11);
        const int glyphCount = 40;
        for (int i = 0; i < glyphCount; i++)
        {
            float a = i * Mathf.PI * 2f / glyphCount;
            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vector3 pos = radial * 3.55f;
            Quaternion rot = Quaternion.LookRotation(radial, Vector3.up);   // z podľa polomeru

            float len = 0.22f + (float)glyphRng.NextDouble() * 0.14f;
            Part(PrimitiveType.Cube, "GlyfZvisla", pos, new Vector3(0.035f, 0.05f, len), dimRed, glyphs).transform.rotation = rot;
            int style = glyphRng.Next(3);
            if (style >= 1) Part(PrimitiveType.Cube, "GlyfPriecka", pos + radial * ((float)glyphRng.NextDouble() - 0.5f) * len * 0.6f, new Vector3(0.2f, 0.05f, 0.035f), dimRed, glyphs).transform.rotation = rot;
            if (style == 2) Part(PrimitiveType.Cube, "GlyfBodka", pos + tangent * 0.09f + radial * len * 0.4f, new Vector3(0.05f, 0.05f, 0.05f), red, glyphs);
        }
        Spin(glyphs, Vector3.up, -2f);

        // Päťcípa hviezda (pentagram) nad prstencami, vrcholy sa dotýkajú kruhu; vo vrcholoch stoja ohniská
        Transform star = new GameObject("Hviezda").transform;
        star.SetParent(transform, false);
        star.localPosition = new Vector3(CenterX, 0.43f, 0f);
        const float starRadius = 3.0f;
        var starPoints = new Vector3[5];
        for (int i = 0; i < 5; i++)
        {
            float a = (i * 72f - 90f) * Mathf.Deg2Rad;
            starPoints[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * starRadius;
        }
        for (int i = 0; i < 5; i++)
        {
            Vector3 p0 = starPoints[i];
            Vector3 p1 = starPoints[(i + 2) % 5];
            Vector3 mid = (p0 + p1) * 0.5f;
            GameObject line = Part(PrimitiveType.Cube, "Luc", mid, new Vector3(0.07f, 0.025f, (p1 - p0).magnitude), red, star);
            line.transform.localRotation = Quaternion.LookRotation(p1 - p0, Vector3.up);
        }
        // Ohniská nie sú v rotujúcej skupine: stoja pevne na vrcholoch
        for (int i = 0; i < 5; i++)
            BuildBrazier(new Vector3(CenterX + starPoints[i].x, 0.35f, starPoints[i].z), i);


        // Zadná stena
        Part(PrimitiveType.Cube, "Stena", new Vector3(0f, 7f, 13f), new Vector3(40f, 18f, 1f), stone);

        // Halo za démonom: dva zvislé prstence rotujúce proti sebe, uprostred slabé svetlo
        Transform haloOuter = Ring("HaloVonkajsie", new Vector3(CenterX - 0.7f, 5.4f, 10.5f), 5.0f, 80, new Vector3(0.18f, 0.18f, 0.55f), blue, true);
        Transform haloInner = Ring("HaloVnutorne", new Vector3(CenterX - 0.7f, 5.4f, 10.4f), 3.7f, 56, new Vector3(0.12f, 0.12f, 0.4f), violet, true);
        Spin(haloOuter, Vector3.forward, 5f);
        Spin(haloInner, Vector3.forward, -9f);
        AddPointLight("HaloSvetlo", transform.position + new Vector3(CenterX - 0.7f, 5.4f, 8.5f), new Color(0.35f, 0.4f, 1f), 3.5f, 22f);

        // Osem pilierov v polkruhu: podstavec, driek, hlavica, svietiaci pásik a plávajúci kryštál; každý druhý má svetlo
        for (int i = 0; i < 8; i++)
        {
            float angle = (i / 7f) * Mathf.PI - Mathf.PI;
            Vector3 p = new Vector3(Mathf.Cos(angle) * 10f + CenterX, 0f, -Mathf.Sin(angle) * 8.5f + 2f);
            Material strip = i % 2 == 0 ? blue : violet;
            Vector3 inward = (new Vector3(CenterX, 0f, 2f) - p).normalized;

            Part(PrimitiveType.Cube, "PilierPodstavec", p + Vector3.up * 0.35f, new Vector3(2.0f, 0.7f, 2.0f), trim);
            Part(PrimitiveType.Cube, "PilierDriek", p + Vector3.up * 4.9f, new Vector3(1.1f, 8.4f, 1.1f), stone);
            Part(PrimitiveType.Cube, "PilierHlavica", p + Vector3.up * 9.3f, new Vector3(1.7f, 0.5f, 1.7f), trim);
            Part(PrimitiveType.Cube, "PilierZiara", p + Vector3.up * 4.9f + inward * 0.59f, new Vector3(0.08f, 6.6f, 0.08f), strip);


            if (i % 2 == 0)
            {
                AddPointLight("Svetlo", transform.position + p + Vector3.up * 2.6f + inward * 1.2f,
                              i % 4 == 0 ? new Color(0.3f, 0.6f, 1f) : new Color(0.65f, 0.3f, 1f), 1.6f, 10f);
            }
        }


        BuildParticles();
    }

    // ---------- Oheň ----------
    // Plameň nie je jeden sprite: jazyk (oranžovo-červený, zužuje sa nahor), jasné žlté jadro, tmavý dym a iskry; všetko s turbulenciou (noise)
    // a farbou, ktorá sa po živote mení z bielo-žltej cez oranžovú do tmavočervenej. Textúra jazyka je v tvare slzy s rozstrapkanými okrajmi.
    private Texture2D flameTex;
    private class FireLight { public Light light; public float seed, baseIntensity; }
    private readonly List<FireLight> fireLights = new List<FireLight>();
    private readonly List<FluidFire> fluidFires = new List<FluidFire>();

    Texture2D FlameTexture()
    {
        if (flameTex != null) return flameTex;
        const int n = 64;
        flameTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x / (n - 1f)) * 2f - 1f;          // -1..1 zľava doprava
                float v = y / (n - 1f);                       // 0 dole .. 1 hore
                float width = Mathf.Pow(Mathf.Clamp01(1f - v), 0.8f) * (0.25f + 0.75f * Mathf.Sin(Mathf.Clamp01(v * 3.2f) * Mathf.PI * 0.5f));
                float wobble = (Mathf.PerlinNoise(x * 0.18f, y * 0.12f) - 0.5f) * 0.35f;
                float d = Mathf.Abs(u + wobble * (1f - v)) / Mathf.Max(0.001f, width);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);                    // mäkký okraj
                a *= Mathf.Clamp01(v * 5f);                   // dole sa vytráca
                flameTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        flameTex.Apply();
        return flameTex;
    }

    static Material ParticleMaterial(Texture tex, bool additive)
    {
        Shader shader = Shader.Find(additive ? "Legacy Shaders/Particles/Additive" : "Legacy Shaders/Particles/Alpha Blended");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var m = new Material(shader);
        m.mainTexture = tex;
        return m;
    }

    static Gradient MakeGradient(Color[] colors, float[] colorTimes, float[] alphas, float[] alphaTimes)
    {
        var colorKeys = new GradientColorKey[colors.Length];
        for (int i = 0; i < colors.Length; i++) colorKeys[i] = new GradientColorKey(colors[i], colorTimes[i]);
        var alphaKeys = new GradientAlphaKey[alphas.Length];
        for (int i = 0; i < alphas.Length; i++) alphaKeys[i] = new GradientAlphaKey(alphas[i], alphaTimes[i]);
        var g = new Gradient();
        g.SetKeys(colorKeys, alphaKeys);
        return g;
    }

    enum FireKind { Flame, Core, Smoke, Sparks }

    ParticleSystem FireSystem(Transform parent, Vector3 localPos, FireKind kind, bool ring, float radius, float rate)
    {
        var go = new GameObject(kind.ToString());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // os kužeľa / rovina kruhu smeruje nahor

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        float lifeMin, lifeMax, sizeMin, sizeMax, rise;
        switch (kind)
        {
            case FireKind.Core: lifeMin = 0.45f; lifeMax = 0.8f; sizeMin = 0.22f; sizeMax = 0.38f; rise = 1.1f; break;
            case FireKind.Smoke: lifeMin = 3f; lifeMax = 4.5f; sizeMin = 0.5f; sizeMax = 1.0f; rise = 0.9f; break;
            case FireKind.Sparks: lifeMin = 1.5f; lifeMax = 3.2f; sizeMin = 0.025f; sizeMax = 0.055f; rise = 1.3f; break;
            default: lifeMin = 0.8f; lifeMax = 1.3f; sizeMin = ring ? 0.28f : 0.5f; sizeMax = ring ? 0.5f : 0.85f; rise = ring ? 0.7f : 1.7f; break;
        }

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.useUnscaledTime = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = Color.white;
        main.maxParticles = 600;

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        if (ring)
        {
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;   // len okraj kruhu
            shape.arc = 360f;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = kind == FireKind.Sparks ? 22f : 5f;
            shape.radius = radius;
            shape.radiusThickness = 1f;
        }

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // Všetky tri osi musia mať rovnaký režim krivky (konštanta vs. dve konštanty), inak Unity ohlási chybu
        bool randomRise = kind == FireKind.Sparks;
        velocity.x = randomRise ? new ParticleSystem.MinMaxCurve(0f, 0f) : new ParticleSystem.MinMaxCurve(0f);
        velocity.z = randomRise ? new ParticleSystem.MinMaxCurve(0f, 0f) : new ParticleSystem.MinMaxCurve(0f);
        velocity.y = randomRise ? new ParticleSystem.MinMaxCurve(rise * 0.5f, rise * 1.8f) : new ParticleSystem.MinMaxCurve(rise);

        // Veľkosť: plameň rýchlo narastie a pri vrchole sa stratí; dym sa rozplýva do šírky
        var sizeOver = ps.sizeOverLifetime;
        sizeOver.enabled = true;
        AnimationCurve curve = kind == FireKind.Smoke
            ? new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.9f))
            : new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.2f, 1f), new Keyframe(0.65f, 0.7f), new Keyframe(1f, 0f));
        sizeOver.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var colorOver = ps.colorOverLifetime;
        colorOver.enabled = true;
        switch (kind)
        {
            case FireKind.Core:
                colorOver.color = MakeGradient(new[] { new Color(1f, 1f, 0.85f), new Color(1f, 0.82f, 0.35f), new Color(1f, 0.5f, 0.1f) }, new[] { 0f, 0.5f, 1f },
                                               new[] { 0f, 1f, 0.6f, 0f }, new[] { 0f, 0.12f, 0.6f, 1f });
                break;
            case FireKind.Smoke:
                colorOver.color = MakeGradient(new[] { new Color(0.12f, 0.09f, 0.09f), new Color(0.07f, 0.07f, 0.08f) }, new[] { 0f, 1f },
                                               new[] { 0f, 0.28f, 0.2f, 0f }, new[] { 0f, 0.2f, 0.6f, 1f });
                break;
            case FireKind.Sparks:
                colorOver.color = MakeGradient(new[] { new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.08f), new Color(0.6f, 0.05f, 0.02f) }, new[] { 0f, 0.5f, 1f },
                                               new[] { 0f, 1f, 0.8f, 0f }, new[] { 0f, 0.08f, 0.7f, 1f });
                break;
            default:
                colorOver.color = MakeGradient(new[] { new Color(1f, 0.9f, 0.5f), new Color(1f, 0.6f, 0.12f), new Color(1f, 0.22f, 0.04f), new Color(0.45f, 0.04f, 0.02f), new Color(0.1f, 0f, 0f) },
                                               new[] { 0f, 0.2f, 0.5f, 0.82f, 1f },
                                               new[] { 0f, 0.9f, 0.7f, 0.25f, 0f }, new[] { 0f, 0.08f, 0.5f, 0.85f, 1f });
                break;
        }

        // Turbulencia: plameň sa vlní a tancuje, iskry blúdia
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = kind == FireKind.Smoke ? 0.9f : kind == FireKind.Sparks ? 1.1f : 0.45f;
        noise.frequency = kind == FireKind.Smoke ? 0.35f : 0.8f;
        noise.scrollSpeed = 0.8f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var rotation = ps.rotationOverLifetime;
        rotation.enabled = kind == FireKind.Smoke;
        if (kind == FireKind.Smoke) rotation.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        bool additive = kind != FireKind.Smoke;
        renderer.material = ParticleMaterial(kind == FireKind.Flame || kind == FireKind.Core ? FlameTexture() : SoftDot(), additive);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingFudge = kind == FireKind.Smoke ? 5f : 0f;   // dym sa kreslí za plameňom

        ps.Play();
        return ps;
    }

    // Ohnisko vo vrchole pentagramu: kamenný stĺpik, miska s uhlíkmi, plameň + jadro + dym + iskry a kolísavé svetlo
    void BuildBrazier(Vector3 basePos, int index)
    {
        Material metal = Dark(new Color(0.05f, 0.045f, 0.05f), 0.55f, 0.7f);
        Material coals = Glow(new Color(1f, 0.25f, 0.05f), 2.6f, 2.4f, 0.45f);

        Transform root = new GameObject("Ohnisko" + index).transform;
        root.SetParent(transform, false);
        root.localPosition = basePos;

        Part(PrimitiveType.Cylinder, "Stlpik", new Vector3(0f, 0.18f, 0f), new Vector3(0.2f, 0.18f, 0.2f), metal, root);                // y 0..0,36
        Part(PrimitiveType.Cylinder, "Miska", new Vector3(0f, 0.43f, 0f), new Vector3(0.62f, 0.07f, 0.62f), metal, root);               // y 0,36..0,5
        Part(PrimitiveType.Sphere, "Uhliky", new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 0.14f, 0.5f), coals, root);

        Vector3 top = new Vector3(0f, 0.55f, 0f);
        // Plameň je skutočná simulácia tekutiny (FluidFire); časticový plameň ostáva len ako záloha, ak by sa simulácia nedala vytvoriť
        FluidFire fluid = FluidFire.Create(root, top - Vector3.up * 0.05f, 1.0f, 1.75f, index * 13.7f);
        if (fluid != null) fluidFires.Add(fluid);
        else FireSystem(root, top, FireKind.Flame, false, 0.16f, 38f);
        FireSystem(root, top + Vector3.up * 1.3f, FireKind.Smoke, false, 0.2f, 5f);
        FireSystem(root, top + Vector3.up * 0.2f, FireKind.Sparks, false, 0.12f, 9f);

        Light l = AddPointLight("OhniskoSvetlo" + index, root.position + Vector3.up * 1.4f, new Color(1f, 0.42f, 0.12f), 1.9f, 8f);
        fireLights.Add(new FireLight { light = l, seed = index * 7.3f, baseIntensity = 1.9f });
    }

    // Nízky ohnivý kruh po obvode vonkajšieho prstenca
    void BuildFireRing(Vector3 center, float radius)
    {
        Transform root = new GameObject("OhnivyKruh").transform;
        root.SetParent(transform, false);
        root.localPosition = center;
        FireSystem(root, Vector3.zero, FireKind.Flame, true, radius, 90f);
        FireSystem(root, Vector3.up * 0.02f, FireKind.Core, true, radius, 40f);
    }

    // ---------- Častice: stúpajúci prach a nízka hmla ----------
    Texture2D SoftDot()
    {
        if (softDot != null) return softDot;
        const int n = 32;
        softDot = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n * 0.5f - 0.5f, n * 0.5f - 0.5f)) / (n * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                softDot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        softDot.Apply();
        return softDot;
    }

    ParticleSystem MakeParticles(string name, Vector3 pos, Vector3 box, float rate, float life, float speed, Vector2 size, Color color, float riseVelocity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.useUnscaledTime = true;   // menu beží pri zastavenom čase
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = life;
        main.startSpeed = speed;
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = color;
        main.maxParticles = 400;

        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.y = riseVelocity;
        velocity.x = 0f;
        velocity.z = 0f;

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        mat.mainTexture = SoftDot();
        renderer.material = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        ps.Play();
        return ps;
    }

    void BuildParticles()
    {
        // Svetlé prachové čiastočky stúpajúce nad plošinou
        MakeParticles("Prach", new Vector3(CenterX, 0.3f, 1f), new Vector3(18f, 0.5f, 14f), 28f, 7f, 0.05f, new Vector2(0.04f, 0.11f), new Color(0.55f, 0.75f, 1f, 0.9f), 0.45f);
        // Pár oranžových iskier od stredových run
        MakeParticles("Iskry", new Vector3(CenterX, 0.4f, 0f), new Vector3(4.5f, 0.2f, 4.5f), 7f, 4.5f, 0.1f, new Vector2(0.03f, 0.07f), new Color(1f, 0.25f, 0.1f, 1f), 0.9f);
        // Veľké, veľmi slabé kúdoly hmly pri zemi
        MakeParticles("Hmla", new Vector3(CenterX, 0.6f, 1f), new Vector3(24f, 0.4f, 16f), 2.4f, 12f, 0.1f, new Vector2(4f, 7f), new Color(0.25f, 0.3f, 0.6f, 0.07f), 0.05f);
    }

    // ---------- Démon v popredí svätyne ----------
    void BuildDemon()
    {
        NPCPerception source = null;
        foreach (NPCPerception npc in FindObjectsByType<NPCPerception>(FindObjectsInactive.Include))
        {
            if (npc.kind == NPCKind.Civilian || npc.higherDemonBodyOverride == null) continue;

            DemonEnemy enemy = npc.GetComponent<DemonEnemy>();
            if (source == null || (enemy != null && enemy.tier == DemonTier.Boss)) source = npc;
            if (enemy != null && enemy.tier == DemonTier.Boss) break;   // boss je najlepší model do svätyne
        }
        if (source == null)
        {
            Debug.LogWarning("MenuSanctuary: v scéne nie je žiadny NPC démon s modelom (NPCPerception.higherDemonBodyOverride), svätyňa ostane prázdna. Spusti Progeny → Aktualizovať všetko.");
            return;
        }

        GameObject demon = Instantiate(source.higherDemonBodyOverride, transform);
        demon.name = "DemonSvatyne";
        demon.transform.localRotation = Quaternion.Euler(0f, 170f, 0f);
        NPCPerception.StripNonVisual(demon);

        Renderer[] renderers = demon.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"MenuSanctuary: model démona '{source.higherDemonBodyOverride.name}' nemá žiadny Renderer.");
            Destroy(demon);
            return;
        }

        foreach (Renderer r in renderers)
        {
            r.enabled = true;
            if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            if (source.demonMaterial == null) continue;

            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = source.demonMaterial;
            r.sharedMaterials = mats;
        }

        // Mierka na výšku ~2,8 m a postavenie na hornú plochu plošiny (y = 0,35) v strede prstencov
        Bounds b = RendererBounds(renderers);
        float scale = 2.8f / Mathf.Max(0.1f, b.size.y);
        demon.transform.localScale = Vector3.one * scale;

        b = RendererBounds(renderers);
        demon.transform.position += new Vector3(Origin.x + CenterX - b.center.x, Origin.y + 0.36f - b.min.y, Origin.z - b.center.z);

        AddDemonLights(RendererBounds(renderers));

        // Idle animácia beží aj pri zastavenom čase
        if (source.demonAnimationSet != null)
        {
            DemonModelAnimator animator = demon.AddComponent<DemonModelAnimator>();
            animator.useUnscaledTime = true;
            animator.Initialize(source.demonAnimationSet);
        }
    }

    // Hranice z Rendererov (zohľadňujú aj mierku kostí skinned meshu; hranice zo samotnej siete by boli chybné)
    static Bounds RendererBounds(Renderer[] renderers)
    {
        Bounds result = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
        return result;
    }

    // Démon musí byť vidieť aj v noci a bez slnka: kľúčové, doplňujúce a protisvetlo, zhora jemný lúč a pod nohami žiara z run
    void AddDemonLights(Bounds b)
    {
        Vector3 c = b.center;
        flicker = AddPointLight("Kluc", c + new Vector3(-2.2f, 1.6f, -3.8f), new Color(0.85f, 0.9f, 1f), 2.4f, 14f);
        AddPointLight("Doplnenie", c + new Vector3(2.8f, 0.4f, -3f), new Color(0.6f, 0.35f, 1f), 2f, 12f);
        AddPointLight("Protisvetlo", c + new Vector3(0.6f, 2.4f, 3.2f), new Color(0.3f, 0.6f, 1f), 3f, 12f);
        AddPointLight("ZiaraRun", new Vector3(b.center.x, Origin.y + 0.9f, b.center.z), new Color(1f, 0.22f, 0.1f), 2.2f, 7f);

        var go = new GameObject("Lúč");
        go.transform.SetParent(transform, false);
        go.transform.position = c + new Vector3(0f, 7f, -0.5f);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var spot = go.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.spotAngle = 38f;
        spot.color = new Color(0.75f, 0.82f, 1f);
        spot.intensity = 5f;
        spot.range = 14f;
    }

    Light AddPointLight(string lightName, Vector3 position, Color color, float intensity, float range)
    {
        var go = new GameObject(lightName);
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        return l;
    }
}
