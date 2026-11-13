using UnityEngine;

/// <summary>
/// Oheň zo skutočnej simulácie tekutiny (Stable Fluids, Jos Stam) na 2D mriežke: teplota sa pridáva pri spodku, vztlak ju ženie nahor,
/// tlaková projekcia drží prúdenie nestlačiteľné a „vorticity confinement“ udržiava vírenie, takže plameň sa krúti a láme ako pravý, nie ako sprite.
/// Výsledná teplota sa cez farebnú rampu (tmavočervená → oranžová → žltá → biela) zapíše do textúry zobrazenej na zvislom billboarde.
/// Simulácia beží na CPU (malá mriežka, ~2 600 buniek) v pevnom kroku 1/30 s a nezávisí od timeScale (hlavné menu stojí).
/// </summary>
public class FluidFire : MonoBehaviour
{
    private const int W = 40, H = 64;
    private const float Step = 1f / 30f;

    [Tooltip("Výkon zdroja tepla pri spodku")]
    public float heat = 2.4f;
    [Tooltip("Sila vztlaku (bunky/s² na jednotku teploty)")]
    public float buoyancy = 95f;
    [Tooltip("Rýchlosť chladnutia: vyššie = nižší plameň")]
    public float cooling = 0.9f;
    [Tooltip("Vírenie: vyššie = viac sa krúti")]
    public float vorticity = 14f;
    [Tooltip("Náhodné chvenie zdroja (plameň sa kolíše do strán)")]
    public float turbulence = 55f;
    [Range(0.3f, 2f)] public float brightness = 1.15f;

    private float[] u, v, u0, v0, d, d0, curl;
    private Texture2D texture;
    private Color32[] pixels;
    private Transform quad;
    private float accumulator, seed;

    static int IX(int i, int j) => i + (W + 2) * j;

    /// <summary>Vytvorí plameň: spodok štvorca je v localPos, výška/šírka v metroch.</summary>
    public static FluidFire Create(Transform parent, Vector3 localPos, float widthMeters, float heightMeters, float seed)
    {
        var go = new GameObject("TekutyOhen");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos + Vector3.up * (heightMeters * 0.5f);
        FluidFire fire = go.AddComponent<FluidFire>();
        fire.seed = seed;
        fire.BuildQuad(widthMeters, heightMeters);
        return fire;
    }

    void Awake()
    {
        int size = (W + 2) * (H + 2);
        u = new float[size]; v = new float[size]; u0 = new float[size]; v0 = new float[size];
        d = new float[size]; d0 = new float[size]; curl = new float[size];

        texture = new Texture2D(W, H, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        pixels = new Color32[W * H];

        // Predpočítaný rozbeh, aby plameň po zobrazení menu nenaskočil z nuly
        for (int i = 0; i < 45; i++) Simulate(Step, i * Step);
    }

    void BuildQuad(float width, float height)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "PlamenPlocha";
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(transform, false);
        q.transform.localScale = new Vector3(width, height, 1f);
        quad = q.transform;

        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var m = new Material(shader);
        // Awake nemusí byť zavolaný pred BuildQuad (AddComponent ho spustí hneď, takže textúra už existuje)
        m.mainTexture = texture;
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    void OnDestroy()
    {
        if (texture != null) Destroy(texture);
    }

    /// <summary>Plocha sa otáča len okolo zvislej osi, aby plameň vždy stál rovno a smeroval ku kamere.</summary>
    public void Face(Vector3 cameraPosition)
    {
        if (quad == null) return;
        Vector3 away = transform.position - cameraPosition;
        away.y = 0f;
        if (away.sqrMagnitude > 0.0001f) quad.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    void Update()
    {
        accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        int steps = 0;
        while (accumulator >= Step && steps < 2)
        {
            accumulator -= Step;
            steps++;
            Simulate(Step, Time.unscaledTime);
        }
        if (steps > 0) Render();
    }

    // ---------- Simulácia ----------
    void Simulate(float dt, float time)
    {
        // Zdroj: pásik uprostred spodku; jas kolíše podľa šumu, vodorovné trhnutia robia z plameňa živý jazyk
        float flicker = 0.7f + 0.5f * Mathf.PerlinNoise(time * 5.5f + seed, seed);
        for (int j = 1; j <= 3; j++)
            for (int i = W / 2 - 5; i <= W / 2 + 5; i++)
            {
                float edge = 1f - Mathf.Abs(i - W / 2) / 6f;
                d[IX(i, j)] = Mathf.Min(1.2f, d[IX(i, j)] + heat * flicker * edge * dt * 6f);
                v[IX(i, j)] += 18f * edge * dt;
            }
        float sway = (Mathf.PerlinNoise(time * 2.1f + seed * 3f, 7f) - 0.5f) * turbulence;
        for (int j = 2; j <= 18; j++)
            for (int i = 2; i <= W - 1; i++)
            {
                float n = Mathf.PerlinNoise(i * 0.21f + seed, j * 0.17f - time * 3.2f) - 0.5f;
                u[IX(i, j)] += (sway * (1f - j / 20f) + n * turbulence * 0.9f) * dt;
            }

        // Vztlak: horúca tekutina stúpa
        for (int j = 1; j <= H; j++)
            for (int i = 1; i <= W; i++)
                v[IX(i, j)] += buoyancy * d[IX(i, j)] * dt;

        Vorticity(dt);
        VelocityStep(dt);
        DensityStep(dt);

        // Chladnutie: exponenciálne aj lineárne, plameň sa pri vrchole vytratí
        float k = Mathf.Exp(-cooling * dt);
        for (int j = 1; j <= H; j++)
            for (int i = 1; i <= W; i++)
            {
                int c = IX(i, j);
                d[c] = Mathf.Max(0f, d[c] * k - 0.06f * dt * (1f + j / (float)H));
            }
    }

    void Vorticity(float dt)
    {
        for (int j = 1; j <= H; j++)
            for (int i = 1; i <= W; i++)
                curl[IX(i, j)] = 0.5f * ((v[IX(i + 1, j)] - v[IX(i - 1, j)]) - (u[IX(i, j + 1)] - u[IX(i, j - 1)]));

        for (int j = 2; j < H; j++)
            for (int i = 2; i < W; i++)
            {
                float nx = 0.5f * (Mathf.Abs(curl[IX(i + 1, j)]) - Mathf.Abs(curl[IX(i - 1, j)]));
                float ny = 0.5f * (Mathf.Abs(curl[IX(i, j + 1)]) - Mathf.Abs(curl[IX(i, j - 1)]));
                float len = Mathf.Sqrt(nx * nx + ny * ny) + 1e-5f;
                nx /= len; ny /= len;
                float w = curl[IX(i, j)];
                u[IX(i, j)] += ny * w * vorticity * dt;
                v[IX(i, j)] -= nx * w * vorticity * dt;
            }
    }

    void VelocityStep(float dt)
    {
        Project(u, v, u0, v0);
        System.Array.Copy(u, u0, u.Length);
        System.Array.Copy(v, v0, v.Length);
        Advect(1, u, u0, u0, v0, dt);
        Advect(2, v, v0, u0, v0, dt);
        Project(u, v, u0, v0);
    }

    void DensityStep(float dt)
    {
        System.Array.Copy(d, d0, d.Length);
        Advect(0, d, d0, u, v, dt);
    }

    void Advect(int b, float[] dst, float[] src, float[] uu, float[] vv, float dt)
    {
        for (int j = 1; j <= H; j++)
            for (int i = 1; i <= W; i++)
            {
                float x = Mathf.Clamp(i - dt * uu[IX(i, j)], 0.5f, W + 0.5f);
                float y = Mathf.Clamp(j - dt * vv[IX(i, j)], 0.5f, H + 0.5f);
                int i0 = (int)x, j0 = (int)y;
                float s1 = x - i0, s0 = 1f - s1, t1 = y - j0, t0 = 1f - t1;
                dst[IX(i, j)] = s0 * (t0 * src[IX(i0, j0)] + t1 * src[IX(i0, j0 + 1)])
                              + s1 * (t0 * src[IX(i0 + 1, j0)] + t1 * src[IX(i0 + 1, j0 + 1)]);
            }
        SetBoundary(b, dst);
    }

    // Tlaková projekcia: odstráni divergenciu, aby tekutina nevznikala ani nezanikala. Vrch je otvorený (tlak 0), tekutina tadiaľ vychádza.
    void Project(float[] uu, float[] vv, float[] p, float[] div)
    {
        for (int j = 1; j <= H; j++)
            for (int i = 1; i <= W; i++)
            {
                div[IX(i, j)] = -0.5f * (uu[IX(i + 1, j)] - uu[IX(i - 1, j)] + vv[IX(i, j + 1)] - vv[IX(i, j - 1)]);
                p[IX(i, j)] = 0f;
            }
        SetBoundary(0, div);
        SetBoundary(0, p);

        for (int iter = 0; iter < 14; iter++)
        {
            for (int j = 1; j <= H; j++)
                for (int i = 1; i <= W; i++)
                    p[IX(i, j)] = (div[IX(i, j)] + p[IX(i - 1, j)] + p[IX(i + 1, j)] + p[IX(i, j - 1)] + p[IX(i, j + 1)]) * 0.25f;
            SetBoundary(0, p);
            for (int i = 0; i <= W + 1; i++) p[IX(i, H + 1)] = 0f;   // otvorený vrch
        }

        for (int j = 1; j <= H; j++)
            for (int i = 1; i <= W; i++)
            {
                uu[IX(i, j)] -= 0.5f * (p[IX(i + 1, j)] - p[IX(i - 1, j)]);
                vv[IX(i, j)] -= 0.5f * (p[IX(i, j + 1)] - p[IX(i, j - 1)]);
            }
        SetBoundary(1, uu);
        SetBoundary(2, vv);
    }

    // b: 1 = vodorovná rýchlosť, 2 = zvislá rýchlosť, 0 = skalár. Spodok a boky odrážajú, vrch prepúšťa.
    void SetBoundary(int b, float[] x)
    {
        for (int j = 1; j <= H; j++)
        {
            x[IX(0, j)] = b == 1 ? -x[IX(1, j)] : x[IX(1, j)];
            x[IX(W + 1, j)] = b == 1 ? -x[IX(W, j)] : x[IX(W, j)];
        }
        for (int i = 1; i <= W; i++)
        {
            x[IX(i, 0)] = b == 2 ? -x[IX(i, 1)] : x[IX(i, 1)];
            x[IX(i, H + 1)] = x[IX(i, H)];   // výtok
        }
        x[IX(0, 0)] = 0.5f * (x[IX(1, 0)] + x[IX(0, 1)]);
        x[IX(0, H + 1)] = 0.5f * (x[IX(1, H + 1)] + x[IX(0, H)]);
        x[IX(W + 1, 0)] = 0.5f * (x[IX(W, 0)] + x[IX(W + 1, 1)]);
        x[IX(W + 1, H + 1)] = 0.5f * (x[IX(W, H + 1)] + x[IX(W + 1, H)]);
    }

    // ---------- Vykreslenie ----------
    // Teplota → farba čierneho telesa: tmavočervená, červenooranžová, žltá, takmer biela v jadre; okraje textúry sa vytrácajú (bez viditeľného štvorca)
    void Render()
    {
        for (int j = 1; j <= H; j++)
            for (int i = 1; i <= W; i++)
            {
                float t = Mathf.Clamp01(d[IX(i, j)]);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.4f, t));

                float r, g, bl;
                if (t < 0.35f) { float k = t / 0.35f; r = Mathf.Lerp(0.35f, 1f, k); g = Mathf.Lerp(0.02f, 0.22f, k); bl = Mathf.Lerp(0.01f, 0.03f, k); }
                else if (t < 0.7f) { float k = (t - 0.35f) / 0.35f; r = 1f; g = Mathf.Lerp(0.22f, 0.7f, k); bl = Mathf.Lerp(0.03f, 0.15f, k); }
                else { float k = (t - 0.7f) / 0.3f; r = 1f; g = Mathf.Lerp(0.7f, 0.97f, k); bl = Mathf.Lerp(0.15f, 0.75f, k); }

                float edge = Mathf.Min(Mathf.Min(i / 5f, (W + 1 - i) / 5f), Mathf.Min((H + 1 - j) / 8f, 1f));
                alpha *= Mathf.Clamp01(edge) * brightness;

                pixels[(j - 1) * W + (i - 1)] = new Color32(
                    (byte)(Mathf.Clamp01(r) * 255f), (byte)(Mathf.Clamp01(g) * 255f), (byte)(Mathf.Clamp01(bl) * 255f), (byte)(Mathf.Clamp01(alpha) * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply(false);
    }
}
