using UnityEngine;

/// <summary>
/// Vzhľad bossa: démonickí bossovia dostanú namiesto dočasných kapsúl a kociek skutočný animovaný model démona
/// (rovnaký, aký majú démoni vo svete), zväčšený na výšku bossa a zafarbený podľa klanu. Model stojí, chodí a pri
/// telegrafovaní úderu útočí; bliká spolu s pôvodným modelom. Herné diely (jadro, kamene, orby, čepele, štít, ventily)
/// ostávajú viditeľné. K tomu aura z častíc a svetlo vo farbe klanu. Pridáva sa sám ku každému BossCore.
/// </summary>
[DisallowMultipleComponent]
public class BossVisuals : MonoBehaviour
{
    private const string BodyName = "TeloDemona";
    private static readonly string[] KeptParts = { "Jadro", "Ramenny_kamen", "Orb_", "Cepel", "Stit", "Ventil", "Laser" };

    [Tooltip("Démonickí bossovia použijú model démona zo sveta; vypnuté = ostane dočasný model z primitív")]
    [SerializeField] private bool useDemonModel = true;
    [SerializeField] private float buildDistance = 220f;

    private BossCore boss;
    private ArenaDressing.Theme theme;
    private Color glow;
    private float height = 3f;

    private DemonModelAnimator animator;
    private Renderer[] bodyRenderers;
    private Renderer reference;        // dočasný diel, podľa ktorého sa riadi viditeľnosť (neviditeľnosť Kuro, zmiznutie po porážke)
    private ParticleSystem aura;
    private Light auraLight;
    private Transform viewer;
    private bool built, shown = true;
    private float nextCheck, lightBase, surgeUntil, auraRate;
    private Renderer ventProbe;
    private bool auraHot = true;
    private MaterialPropertyBlock probeBlock;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    // Zdroj modelu sa hľadá raz za scénu
    private static GameObject demonPrefab, bossPrefab;
    private static DemonAnimationSet demonSet, bossSet;
    private static Material demonMaterial, bossMaterial;
    private static bool searched;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        demonPrefab = bossPrefab = null;
        demonSet = bossSet = null;
        demonMaterial = bossMaterial = null;
        searched = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll();
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        searched = false;
        AttachAll();
    }

    static void AttachAll()
    {
        foreach (BossCore b in FindObjectsByType<BossCore>(FindObjectsInactive.Include))
            if (b.GetComponent<BossVisuals>() == null) b.gameObject.AddComponent<BossVisuals>();
    }

    static void FindSources()
    {
        if (searched) return;
        searched = true;
        foreach (NPCPerception npc in FindObjectsByType<NPCPerception>(FindObjectsInactive.Include))
        {
            if (npc.higherDemonBodyOverride == null || npc.demonAnimationSet == null) continue;
            DemonEnemy enemy = npc.GetComponent<DemonEnemy>();
            if (enemy != null && enemy.tier == DemonTier.Boss)
            {
                if (bossPrefab == null) { bossPrefab = npc.higherDemonBodyOverride; bossSet = npc.demonAnimationSet; bossMaterial = npc.demonMaterial; }
            }
            else if (demonPrefab == null) { demonPrefab = npc.higherDemonBodyOverride; demonSet = npc.demonAnimationSet; demonMaterial = npc.demonMaterial; }
            if (bossPrefab != null && demonPrefab != null) break;
        }
    }

    /// <summary>Kópia bossa (ilúzie Kuro) príde o skripty: model démona v nej treba znova rozhýbať.</summary>
    public static void Reanimate(GameObject copy)
    {
        if (copy == null) return;
        Transform body = copy.transform.Find(BodyName);
        if (body == null) return;
        FindSources();
        DemonAnimationSet set = demonSet != null ? demonSet : bossSet;
        if (set == null) return;
        body.gameObject.AddComponent<DemonModelAnimator>().Initialize(set);
    }

    void OnDestroy()
    {
        if (boss != null) boss.Telegraphed -= OnTelegraph;
    }

    void Update()
    {
        if (!built)
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            if (viewer == null)
            {
                Camera cam = Camera.main;
                viewer = cam != null ? cam.transform : null;
            }
            if (viewer == null || (viewer.position - transform.position).sqrMagnitude > buildDistance * buildDistance) return;
            Build();
            return;
        }

        // Viditeľnosť podľa pôvodného modelu: boss, ktorý sa zneviditeľní alebo po porážke zmizne, nesmie nechať stáť model ani auru
        bool visible = reference == null || reference.enabled;
        if (visible != shown)
        {
            shown = visible;
            if (bodyRenderers != null)
                for (int i = 0; i < bodyRenderers.Length; i++)
                    if (bodyRenderers[i] != null) bodyRenderers[i].enabled = visible;
            if (aura != null)
            {
                ParticleSystem.EmissionModule emission = aura.emission;
                emission.enabled = visible;
            }
            if (auraLight != null) auraLight.enabled = visible;
        }

        if (auraLight != null && visible)
        {
            float surge = Time.time < surgeUntil ? 1.8f : 1f;
            auraLight.intensity = lightBase * surge * VentHeat() * (0.85f + 0.15f * Mathf.Sin(Time.time * 3.1f));
        }
    }

    // Skryté žily bossa hovoria, či je rozžeravený (zraniteľný) alebo vychladnutý: podľa toho svieti aura
    float VentHeat()
    {
        if (ventProbe == null) return 1f;
        if (probeBlock == null) probeBlock = new MaterialPropertyBlock();
        ventProbe.GetPropertyBlock(probeBlock);
        if (probeBlock.isEmpty) return 1f;
        float heat = Mathf.Clamp01(probeBlock.GetColor(EmissionId).maxColorComponent);
        bool hot = heat > 0.05f;
        if (hot != auraHot && aura != null)
        {
            auraHot = hot;
            ParticleSystem.EmissionModule emission = aura.emission;
            emission.rateOverTimeMultiplier = hot ? auraRate : auraRate * 0.25f;
        }
        return Mathf.Lerp(0.35f, 1.4f, heat);
    }

    // ---------- Stavba ----------
    void Build()
    {
        built = true;
        boss = GetComponent<BossCore>();
        if (boss == null) return;
        theme = ArenaDressing.ThemeOf(boss);
        glow = ArenaDressing.GlowOf(theme);

        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null) height = Mathf.Max(1.5f, (box.center.y + box.size.y * 0.5f) * transform.lossyScale.y);

        if (useDemonModel && theme != ArenaDressing.Theme.Mech) BuildDemon();
        BuildAura();
        boss.Telegraphed += OnTelegraph;
    }

    void BuildDemon()
    {
        FindSources();
        bool lord = theme == ArenaDressing.Theme.Lightning || theme == ArenaDressing.Theme.Final;   // páni bleskov majú vlastný model bossa
        GameObject prefab = lord && bossPrefab != null ? bossPrefab : demonPrefab != null ? demonPrefab : bossPrefab;
        DemonAnimationSet set = prefab == bossPrefab ? bossSet : demonSet;
        Material source = prefab == bossPrefab ? bossMaterial : demonMaterial;
        if (prefab == null || set == null) return;   // vo svete nie je žiadny démon s modelom: ostáva dočasný model

        // Kópia bossa (ilúzia) si mohla priniesť starý model
        Transform old = transform.Find(BodyName);
        if (old != null) Destroy(old.gameObject);

        GameObject body = Instantiate(prefab, transform);
        body.name = BodyName;
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        NPCPerception.StripNonVisual(body);
        LightMeshTable.Apply(body);

        bodyRenderers = body.GetComponentsInChildren<Renderer>(true);
        if (bodyRenderers.Length == 0)
        {
            Destroy(body);
            bodyRenderers = null;
            return;
        }

        Color tint = Tint(theme);
        Material shared = null;
        foreach (Renderer r in bodyRenderers)
        {
            r.enabled = true;
            if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            Material basis = source != null ? source : r.sharedMaterial;
            if (basis == null) continue;
            if (shared == null)
            {
                shared = new Material(basis) { hideFlags = HideFlags.HideAndDontSave };
                if (shared.HasProperty("_Color")) shared.color = tint;
                // Žiara je vypnutá, ale kľúčové slovo zapnuté: boss cez ňu bliká pri telegrafovaní
                if (shared.HasProperty("_EmissionColor"))
                {
                    shared.EnableKeyword("_EMISSION");
                    shared.SetColor("_EmissionColor", Color.black);
                }
            }
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = shared;
            r.sharedMaterials = mats;
        }

        // Mierka na výšku bossa a postavenie chodidlami na jeho bod
        Bounds b = BoundsOf(bodyRenderers);
        body.transform.localScale *= height / Mathf.Max(0.1f, b.size.y);
        b = BoundsOf(bodyRenderers);
        body.transform.position += new Vector3(transform.position.x - b.center.x, transform.position.y - b.min.y, transform.position.z - b.center.z);

        animator = body.AddComponent<DemonModelAnimator>();
        animator.Initialize(set);

        HidePlaceholder();
        boss.AddFlashRenderers(bodyRenderers);
    }

    // Dočasné telo z primitív sa prestane kresliť (kolízie a zásahové zóny ostávajú); herné diely ostanú viditeľné
    void HidePlaceholder()
    {
        BossModelRig rig = GetComponentInChildren<BossModelRig>(true);
        if (rig == null) return;
        if (rig.ventRenderers != null && rig.ventRenderers.Length > 0) ventProbe = rig.ventRenderers[0];
        foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(true))
        {
            if (Kept(r, rig)) continue;
            r.forceRenderingOff = true;   // enabled nechávame bossovi: prepína ním neviditeľnosť
            if (reference == null) reference = r;
        }
    }

    static bool Kept(Renderer r, BossModelRig rig)
    {
        string partName = r.name;
        for (int i = 0; i < KeptParts.Length; i++)
            if (partName.StartsWith(KeptParts[i])) return true;
        if (r.GetComponentInParent<BossHitZone>() != null) return true;
        // Žily a pazúry dočasného modelu sedeli na kvádroch, pred modelom démona by viseli vo vzduchu:
        // ich stav (žeravý / vychladnutý) preberá aura, viditeľné ostáva len jadro
        return rig.shieldVisual != null && r.transform.IsChildOf(rig.shieldVisual.transform);
    }

    void BuildAura()
    {
        float h = height;
        bool mech = theme == ArenaDressing.Theme.Mech;
        Color c = glow;
        c.a = 0.75f;
        auraRate = 10f + h * 2.5f;
        aura = VisualKit.Loop("Aura", transform, new Vector3(0f, h * 0.45f, 0f), new Vector3(h * 0.45f, h * 0.9f, h * 0.45f), VisualKit.Additive, c,
            auraRate, new Vector2(1.2f, 2.6f), new Vector2(0.04f + h * 0.008f, 0.1f + h * 0.02f),
            new Vector3(0f, mech ? 0.4f : 0.5f + h * 0.12f, 0f), 0.25f + h * 0.03f, 0f, false, 300);

        // Aréna Kuro stojí na tme (svetlo je herný prvok): tam boss nesvieti
        if (theme == ArenaDressing.Theme.Shadow) return;
        var holder = new GameObject("SvetloAury");
        holder.transform.SetParent(transform, false);
        holder.transform.localPosition = new Vector3(0f, h * 0.6f, 0f);
        auraLight = holder.AddComponent<Light>();
        auraLight.type = LightType.Point;
        auraLight.color = glow;
        auraLight.range = Mathf.Clamp(h * 3.2f, 9f, 60f);
        auraLight.shadows = LightShadows.None;
        lightBase = 1.5f;
        auraLight.intensity = lightBase;
    }

    void OnTelegraph(float seconds, Color color)
    {
        if (!shown) return;
        surgeUntil = Time.time + seconds;
        if (animator != null) animator.PlayAttackFor(Mathf.Clamp(seconds + 0.35f, 0.5f, 2.5f));

        // Vzopnutie aury: častice vyletia z tela vo farbe úderu
        if (aura == null) return;
        var emit = new ParticleSystem.EmitParams { startColor = new Color(color.r, color.g, color.b, 0.9f) };
        int count = Mathf.Clamp(Mathf.RoundToInt(14f + height * 3f), 14, 60);
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Abs(dir.y);
            emit.position = transform.position + Vector3.up * (height * Random.Range(0.2f, 0.9f)) + dir * (height * 0.15f);
            emit.velocity = dir * (1.5f + height * 0.35f);
            emit.startSize = 0.1f + height * 0.03f;
            emit.startLifetime = Random.Range(0.5f, 1.1f);
            aura.Emit(emit, 1);
        }
    }

    static Color Tint(ArenaDressing.Theme t)
    {
        switch (t)
        {
            case ArenaDressing.Theme.Lightning: return new Color(0.6f, 0.75f, 1f);
            case ArenaDressing.Theme.Earth: return new Color(0.62f, 0.78f, 0.5f);
            case ArenaDressing.Theme.Fire: return new Color(1f, 0.55f, 0.4f);
            case ArenaDressing.Theme.Shadow: return new Color(0.45f, 0.36f, 0.62f);
            case ArenaDressing.Theme.Blood: return new Color(0.95f, 0.42f, 0.46f);
            default: return new Color(0.7f, 0.5f, 0.85f);
        }
    }

    // Hranice z Rendererov (zohľadňujú mierku kostí skinned meshu)
    static Bounds BoundsOf(Renderer[] renderers)
    {
        Bounds result = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) result.Encapsulate(renderers[i].bounds);
        return result;
    }
}
