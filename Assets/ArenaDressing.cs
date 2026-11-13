using UnityEngine;

/// <summary>
/// Vizuálne oblečenie arény bossa bez externých assetov: vlastná obloha (guľa okolo arény zakryje ostatné arény a prázdno),
/// masív pod plošinou, kruh kulís okolo (veže mesta, skaly, obelisky podľa témy), svietiace okraje, runové kruhy na zemi,
/// častice v ovzduší a búrka. Stavia sa samo pri priblížení hráča a pri odchode sa vypne; do scény sa nič neukladá
/// a hrateľnosť (kolízie, kryty, stanice) sa nemení.
/// </summary>
public class ArenaDressing : MonoBehaviour
{
    public enum Theme { Mech, Lightning, Earth, Fire, Shadow, Blood, Final }

    private struct Look
    {
        public Color glow, rock, skyBottom, skyHorizon, skyTop;
        public PrimitiveType shape;
        public Vector2 width, top;
        public float tilt;
        public bool floating, lake, storm;
    }

    [SerializeField] private Theme theme;
    [Tooltip("Vzdialenosť kamery od stredu, pri ktorej sa kulisy zapnú. Arény sú 150 m od seba: pod 75 m je vždy zapnutá len jedna obloha")]
    [SerializeField] private float showDistance = 72f;
    [SerializeField] private int backdropCount = 26;

    private const float SkyRadius = 100f;   // susedné arény sú 150 m od seba: guľa ich musí zakryť

    private BossCore boss;
    private Transform dressing;
    private Transform viewer;
    private readonly Renderer[] rings = new Renderer[3];
    private MaterialPropertyBlock block;
    private Look look;
    private float radius = 42f;
    private bool round;
    private float nextCheck, nextBolt;
    private bool near;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static ArenaDressing active;   // aréna, ktorá práve riadi hmlu

    void OnDisable()
    {
        if (active != this) return;
        active = null;
        AtmosphereDirector.ArenaFog = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => AttachAll();

    static void AttachAll()
    {
        foreach (BossCore b in FindObjectsByType<BossCore>(FindObjectsInactive.Include))
        {
            Transform root = b.transform.parent;
            if (root == null || root.GetComponent<ArenaDressing>() != null) continue;
            ArenaDressing dress = root.gameObject.AddComponent<ArenaDressing>();
            dress.boss = b;
            dress.theme = ThemeOf(b);
        }
    }

    public static Theme ThemeOf(BossCore b)
    {
        switch (b.GetType().Name)
        {
            case "RaizenBoss": return Theme.Lightning;
            case "GoraBoss": return Theme.Earth;
            case "IgnisBoss": return Theme.Fire;
            case "KuroBoss": return Theme.Shadow;
            case "CarmillaBoss": return Theme.Blood;
            case "ArakaniBoss": return Theme.Final;
            default: return Theme.Mech;
        }
    }

    /// <summary>Hlavná farba témy (používa ju aj vizuál bossa).</summary>
    public static Color GlowOf(Theme t) => LookOf(t).glow;

    static Look LookOf(Theme t)
    {
        switch (t)
        {
            case Theme.Lightning:
                return new Look
                {
                    glow = new Color(0.35f, 0.8f, 1f), rock = new Color(0.05f, 0.06f, 0.1f),
                    skyBottom = new Color(0.01f, 0.02f, 0.05f), skyHorizon = new Color(0.12f, 0.2f, 0.34f), skyTop = new Color(0.02f, 0.03f, 0.07f),
                    shape = PrimitiveType.Cube, width = new Vector2(10f, 19f), top = new Vector2(-14f, 40f), tilt = 0f, storm = true
                };
            case Theme.Earth:
                return new Look
                {
                    glow = new Color(0.45f, 1f, 0.4f), rock = new Color(0.2f, 0.19f, 0.15f),
                    skyBottom = new Color(0.03f, 0.05f, 0.03f), skyHorizon = new Color(0.42f, 0.5f, 0.36f), skyTop = new Color(0.12f, 0.2f, 0.2f),
                    shape = PrimitiveType.Sphere, width = new Vector2(26f, 44f), top = new Vector2(8f, 34f), tilt = 0f
                };
            case Theme.Fire:
                return new Look
                {
                    glow = new Color(1f, 0.4f, 0.08f), rock = new Color(0.07f, 0.04f, 0.04f),
                    skyBottom = new Color(0.5f, 0.12f, 0.02f), skyHorizon = new Color(0.3f, 0.07f, 0.03f), skyTop = new Color(0.03f, 0.01f, 0.02f),
                    shape = PrimitiveType.Cube, width = new Vector2(8f, 17f), top = new Vector2(6f, 44f), tilt = 13f, lake = true
                };
            case Theme.Shadow:
                return new Look
                {
                    glow = new Color(0.6f, 0.25f, 1f), rock = new Color(0.04f, 0.03f, 0.07f),
                    skyBottom = new Color(0.01f, 0f, 0.02f), skyHorizon = new Color(0.16f, 0.06f, 0.3f), skyTop = new Color(0.01f, 0.01f, 0.03f),
                    shape = PrimitiveType.Cube, width = new Vector2(4f, 8f), top = new Vector2(18f, 52f), tilt = 4f
                };
            case Theme.Blood:
                return new Look
                {
                    glow = new Color(1f, 0.08f, 0.18f), rock = new Color(0.1f, 0.03f, 0.04f),
                    skyBottom = new Color(0.25f, 0.01f, 0.03f), skyHorizon = new Color(0.5f, 0.06f, 0.1f), skyTop = new Color(0.06f, 0.01f, 0.03f),
                    shape = PrimitiveType.Cylinder, width = new Vector2(5f, 9f), top = new Vector2(20f, 46f), tilt = 0f, lake = true
                };
            case Theme.Final:
                return new Look
                {
                    glow = new Color(0.8f, 0.35f, 1f), rock = new Color(0.05f, 0.04f, 0.08f),
                    skyBottom = new Color(0.02f, 0f, 0.04f), skyHorizon = new Color(0.35f, 0.1f, 0.45f), skyTop = new Color(0.02f, 0.02f, 0.06f),
                    shape = PrimitiveType.Cube, width = new Vector2(7f, 14f), top = new Vector2(16f, 50f), tilt = 9f, floating = true, storm = true
                };
            default:
                return new Look
                {
                    glow = new Color(1f, 0.55f, 0.15f), rock = new Color(0.09f, 0.09f, 0.11f),
                    skyBottom = new Color(0.02f, 0.02f, 0.03f), skyHorizon = new Color(0.45f, 0.2f, 0.08f), skyTop = new Color(0.05f, 0.05f, 0.09f),
                    shape = PrimitiveType.Cube, width = new Vector2(12f, 22f), top = new Vector2(-20f, 30f), tilt = 0f
                };
        }
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (now >= nextCheck)
        {
            nextCheck = now + 0.5f;
            if (viewer == null)
            {
                Camera cam = Camera.main;
                viewer = cam != null ? cam.transform : null;
            }
            bool wanted = viewer != null && (viewer.position - transform.position).sqrMagnitude < showDistance * showDistance;
            if (wanted != near)
            {
                near = wanted;
                if (near && dressing == null) Build();
                if (dressing != null) dressing.gameObject.SetActive(near);
                if (!near && active == this) { active = null; AtmosphereDirector.ArenaFog = 0f; }
            }
        }
        if (!near || dressing == null) return;

        // Hmla sveta by oblohu a kulisy arény zaliala farbou dňa: v aréne sa stlmí a prefarbí na obzor témy
        active = this;
        AtmosphereDirector.ArenaFogColor = look.skyHorizon;
        AtmosphereDirector.ArenaFog = Mathf.MoveTowards(AtmosphereDirector.ArenaFog, 1f, Time.unscaledDeltaTime * 1.5f);

        // Runové kruhy sa pomaly otáčajú proti sebe a dýchajú
        float t = Time.time;
        for (int i = 0; i < rings.Length; i++)
        {
            if (rings[i] == null) continue;
            rings[i].transform.Rotate(0f, 0f, (i % 2 == 0 ? 4f : -6f) * Time.deltaTime, Space.Self);
            Color c = look.glow * (0.5f + 0.2f * Mathf.Sin(t * 1.3f + i * 2.1f));
            c.a = 0.55f;
            block.SetColor(ColorId, c);
            rings[i].SetPropertyBlock(block);
        }

        if (look.storm && t >= nextBolt)
        {
            nextBolt = t + Random.Range(2.2f, 6f);
            float a = Random.value * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 from = transform.position + dir * Random.Range(58f, 80f) + Vector3.up * 75f;
            Vector3 to = transform.position + dir * Random.Range(55f, 78f) + Vector3.up * Random.Range(4f, 32f);
            CombatFx.Lightning(from, to, Color.Lerp(look.glow, Color.white, 0.4f), 0.32f, 0.5f);
        }
    }

    // ---------- Stavba ----------
    void Build()
    {
        look = LookOf(theme);
        block = new MaterialPropertyBlock();
        MeasureFloor();

        var holder = new GameObject("Kulisy");
        dressing = holder.transform;
        dressing.SetParent(transform, false);

        Material rock = VisualKit.Lit(look.rock, theme == Theme.Lightning || theme == Theme.Mech ? 0.55f : 0.05f, theme == Theme.Lightning || theme == Theme.Mech ? 0.55f : 0.2f, Color.black);
        Material glow = VisualKit.Lit(look.glow * 0.25f, 0f, 0.6f, look.glow * 2.4f);
        var random = new System.Random(1009 * (1 + (int)theme));   // každá téma má stále rovnaké kulisy

        // Obloha: guľa zvnútra (materiál kreslí obe strany)
        GameObject sky = VisualKit.Prim(PrimitiveType.Sphere, "Obloha", dressing, Vector3.zero, Vector3.one * SkyRadius * 2f, VisualKit.Sky(look.skyBottom, look.skyHorizon, look.skyTop));
        sky.GetComponent<Renderer>().receiveShadows = false;

        // Masív, na ktorom plošina stojí (bez neho je to tenká doska vo vzduchu)
        if (!look.floating)
            VisualKit.Prim(round ? PrimitiveType.Cylinder : PrimitiveType.Cube, "Podstava", dressing, new Vector3(0f, -46.2f, 0f),
                round ? new Vector3(radius * 1.9f, 45f, radius * 1.9f) : new Vector3(radius * 1.96f, 90f, radius * 1.96f), rock);

        if (look.lake)
            VisualKit.Prim(PrimitiveType.Cylinder, "Jazero", dressing, new Vector3(0f, -34f, 0f), new Vector3(SkyRadius * 2f, 0.2f, SkyRadius * 2f),
                VisualKit.Lit(look.glow * 0.3f, 0f, 0.8f, look.glow * 1.3f));

        BuildBackdrop(rock, glow, random);
        BuildFloor(glow);
        BuildAir();
    }

    // Podlaha arény: najväčší povrch vo výške koreňa (štvorec 84 m alebo kruh 80 m)
    void MeasureFloor()
    {
        float best = 0f;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (boss != null && r.transform.IsChildOf(boss.transform)) continue;
            Bounds b = r.bounds;
            if (Mathf.Abs(b.max.y - transform.position.y) > 1.5f) continue;
            float area = b.size.x * b.size.z;
            if (area <= best) continue;
            best = area;
            radius = Mathf.Max(b.extents.x, b.extents.z);
            MeshFilter filter = r.GetComponent<MeshFilter>();
            round = filter != null && filter.sharedMesh != null && filter.sharedMesh.name.StartsWith("Cylinder");
        }
        radius = Mathf.Clamp(radius, 20f, 60f);
    }

    void BuildBackdrop(Material rock, Material glow, System.Random random)
    {
        Color[] elements = { new Color(0.6f, 0.25f, 1f), new Color(1f, 0.4f, 0.08f), new Color(0.35f, 0.8f, 1f), new Color(1f, 0.08f, 0.18f) };
        Material[] elementGlow = null;
        if (theme == Theme.Final)
        {
            elementGlow = new Material[elements.Length];
            for (int i = 0; i < elements.Length; i++) elementGlow[i] = VisualKit.Lit(elements[i] * 0.25f, 0f, 0.6f, elements[i] * 2.4f);
        }

        for (int i = 0; i < backdropCount; i++)
        {
            float angle = (i + Next(random, -0.3f, 0.3f)) / backdropCount * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float width = Next(random, look.width.x, look.width.y);
            float depth = width * Next(random, 0.7f, 1.2f);
            float top = Next(random, look.top.x, look.top.y);
            float bottom = look.floating ? top - Next(random, 18f, 42f) : -92f;
            float distance = radius * 1.42f + width * 0.5f + Next(random, 0f, 9f);
            if (look.shape == PrimitiveType.Sphere) { bottom = -top * 0.6f - 12f; distance += 4f; }

            float height = top - bottom;
            // Valec má v Unity výšku 2 pri mierke 1, kocka a guľa 1
            float scaleY = look.shape == PrimitiveType.Cylinder ? height * 0.5f : height;
            GameObject tower = VisualKit.Prim(look.shape, "Kulisa_" + i, dressing, dir * distance + Vector3.up * ((top + bottom) * 0.5f), new Vector3(width, scaleY, depth), rock, true);
            tower.transform.localRotation = Quaternion.LookRotation(-dir) * Quaternion.Euler(Next(random, -look.tilt, look.tilt), Next(random, -20f, 20f), Next(random, -look.tilt, look.tilt));

            if (look.shape == PrimitiveType.Sphere) continue;   // skalné masívy sú bez svetiel

            // Svietiace pásy na strane obrátenej k aréne: neóny veží, žeravé pukliny, runy obeliskov
            Material strip = elementGlow != null ? elementGlow[i % elementGlow.Length] : glow;
            int strips = 1 + random.Next(0, 3);
            for (int s = 0; s < strips; s++)
            {
                bool horizontal = (theme == Theme.Lightning || theme == Theme.Mech) && random.Next(0, 3) == 0;
                float visibleTop = 0.5f;
                float visibleBottom = look.floating ? -0.5f : Mathf.Clamp(0.5f - (top + 6f) / height, -0.5f, 0.4f);   // pod plošinou pás nevidno
                float y = Next(random, visibleBottom, visibleTop - 0.05f);
                Vector3 size = horizontal
                    ? new Vector3(Next(random, 0.5f, 0.9f), 0.5f / height, 0.02f)
                    : new Vector3(Next(random, 0.35f, 0.8f) / width, Mathf.Min(Next(random, 5f, 16f) / height, (visibleTop - y) * 1.8f + 0.02f), 0.02f);
                float z = look.shape == PrimitiveType.Cylinder ? 0.5f : 0.505f;
                VisualKit.Prim(PrimitiveType.Cube, "Pas", tower.transform, new Vector3(horizontal ? 0f : Next(random, -0.32f, 0.32f), y, z), size, strip);
            }
        }
    }

    void BuildFloor(Material glow)
    {
        // Svietiaci lem okraja štvorcovej plošiny
        if (!round)
        {
            float r = radius;
            VisualKit.Prim(PrimitiveType.Cube, "Lem_x+", dressing, new Vector3(r, 0.06f, 0f), new Vector3(0.35f, 0.14f, r * 2f), glow);
            VisualKit.Prim(PrimitiveType.Cube, "Lem_x-", dressing, new Vector3(-r, 0.06f, 0f), new Vector3(0.35f, 0.14f, r * 2f), glow);
            VisualKit.Prim(PrimitiveType.Cube, "Lem_z+", dressing, new Vector3(0f, 0.06f, r), new Vector3(r * 2f, 0.14f, 0.35f), glow);
            VisualKit.Prim(PrimitiveType.Cube, "Lem_z-", dressing, new Vector3(0f, 0.06f, -r), new Vector3(r * 2f, 0.14f, 0.35f), glow);
        }

        float[] sizes = { 1.88f, 1.2f, 0.56f };
        for (int i = 0; i < rings.Length; i++)
            rings[i] = VisualKit.FlatQuad("RunovyKruh_" + i, dressing, new Vector3(0f, 0.07f + i * 0.01f, 0f), radius * sizes[i], VisualKit.RuneRing);
    }

    void BuildAir()
    {
        float r2 = radius * 2f;
        switch (theme)
        {
            case Theme.Fire:
                VisualKit.Loop("Uhlíky", dressing, new Vector3(0f, 0.5f, 0f), new Vector3(r2 * 1.3f, 1f, r2 * 1.3f), VisualKit.Additive, new Color(1f, 0.55f, 0.15f, 0.9f),
                    80f, new Vector2(3f, 6f), new Vector2(0.08f, 0.22f), new Vector3(0f, 2.4f, 0f), 0.9f, -0.03f, false, 600);
                Mist(new Color(0.12f, 0.03f, 0.01f, 0.16f), 2f);
                break;
            case Theme.Lightning:
                VisualKit.Loop("Dážď", dressing, new Vector3(0f, 30f, 0f), new Vector3(r2 * 1.3f, 1f, r2 * 1.3f), VisualKit.Additive, new Color(0.6f, 0.78f, 1f, 0.3f),
                    520f, new Vector2(1.1f, 1.3f), new Vector2(0.03f, 0.05f), new Vector3(3f, -27f, 0f), 1f, 0f, true, 900);
                Mist(new Color(0.2f, 0.3f, 0.45f, 0.07f), 1.5f);
                break;
            case Theme.Earth:
                VisualKit.Loop("Peľ", dressing, new Vector3(0f, 6f, 0f), new Vector3(r2, 11f, r2), VisualKit.Additive, new Color(0.75f, 1f, 0.5f, 0.6f),
                    34f, new Vector2(5f, 9f), new Vector2(0.06f, 0.16f), new Vector3(0.6f, 0.15f, 0.3f), 0.5f, 0f, false, 400);
                Mist(new Color(0.6f, 0.75f, 0.6f, 0.07f), 1.5f);
                break;
            case Theme.Shadow:
                VisualKit.Loop("Čiastočky", dressing, new Vector3(0f, 4f, 0f), new Vector3(r2, 8f, r2), VisualKit.Additive, new Color(0.6f, 0.3f, 1f, 0.55f),
                    36f, new Vector2(4f, 8f), new Vector2(0.05f, 0.14f), new Vector3(0f, 0.45f, 0f), 0.4f, 0f, false, 400);
                Mist(new Color(0.02f, 0f, 0.05f, 0.24f), 2f);
                break;
            case Theme.Blood:
                VisualKit.Loop("Kvapky", dressing, new Vector3(0f, 4f, 0f), new Vector3(r2, 8f, r2), VisualKit.Additive, new Color(1f, 0.1f, 0.15f, 0.55f),
                    40f, new Vector2(4f, 7f), new Vector2(0.05f, 0.13f), new Vector3(0f, 0.7f, 0f), 0.3f, 0f, false, 400);
                Mist(new Color(0.45f, 0.02f, 0.05f, 0.13f), 2f);
                break;
            case Theme.Final:
                VisualKit.Loop("Čiastočky", dressing, new Vector3(0f, 8f, 0f), new Vector3(r2 * 1.2f, 16f, r2 * 1.2f), VisualKit.Additive, new Color(0.85f, 0.55f, 1f, 0.6f),
                    70f, new Vector2(4f, 8f), new Vector2(0.06f, 0.2f), new Vector3(0f, 1.2f, 0f), 0.6f, 0f, false, 700);
                Mist(new Color(0.2f, 0.04f, 0.3f, 0.14f), 2.5f);
                break;
            default:
                VisualKit.Loop("Popol", dressing, new Vector3(0f, 14f, 0f), new Vector3(r2 * 1.2f, 1f, r2 * 1.2f), VisualKit.Additive, new Color(1f, 0.7f, 0.4f, 0.5f),
                    28f, new Vector2(6f, 9f), new Vector2(0.05f, 0.12f), new Vector3(1f, -1.6f, 0f), 0.6f, 0f, false, 350);
                Mist(new Color(0.3f, 0.26f, 0.22f, 0.09f), 1.5f);
                break;
        }
    }

    // Nízka hmla, ktorá sa pomaly valí po podlahe
    void Mist(Color color, float height)
    {
        VisualKit.Loop("Hmla", dressing, new Vector3(0f, height, 0f), new Vector3(radius * 2.2f, height, radius * 2.2f), VisualKit.Smoke, color,
            5f, new Vector2(10f, 15f), new Vector2(9f, 16f), new Vector3(0.5f, 0f, 0.25f), 0.3f, 0f, false, 90);
    }

    static float Next(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
}
