using UnityEngine;

/// <summary>
/// Nástroj v pravej ruke hráča (sekera / krompáč / kladivo), farebne podľa typu: ukáže sa na chvíľu pri práci a potom zmizne.
/// Ak existuje prefab Resources/Tools/Tool_Axe, Tool_Pickaxe (vytvára ho Progeny → Nástroje: pripraviť modely), použije sa model;
/// inak jednoduchý tvar z primitív. Pridáva sa hráčovi automaticky pri prvom použití nástroja.
/// </summary>
public class HeldToolVisual : MonoBehaviour
{
    private Transform holder;
    private float hideAt;
    private ToolKit.Kind builtKind = (ToolKit.Kind)(-1);
    private int builtTier = -1;
    private MaterialPropertyBlock block;

    public static void Show(Transform player, ToolKit.Kind kind, int tier, float seconds)
    {
        HeldToolVisual v = player.GetComponent<HeldToolVisual>();
        if (v == null) v = player.gameObject.AddComponent<HeldToolVisual>();
        v.Display(kind, tier, seconds);
    }

    void Display(ToolKit.Kind kind, int tier, float seconds)
    {
        if (visual == null) visual = GetComponent<PlayerVisual>();
        Transform hand = visual != null ? visual.RightHand : null;
        if (hand == null) return;

        if (holder == null || kind != builtKind || tier != builtTier || holder.parent != hand) Build(hand, kind, tier);
        holder.gameObject.SetActive(true);
        hideAt = Time.time + seconds;

        // Záložné zamáchnutie (bez klipu útoku): nástroj zdvihnutý nad hlavu a sekne dolu; telo sa mierne nakloní
        if (ProceduralSwing && (swingStart < 0f || Time.time - swingStart > SwingSeconds))
        {
            swingStart = Time.time;
            if (visualRoot == null && visual != null) visualRoot = visual.VisualRoot;
            if (visualRoot != null) bodyBase = visualRoot.localRotation;
        }
    }

    /// <summary>true = nástroj sa máva procedurálne (nie je k dispozícii klip útoku)</summary>
    public static bool ProceduralSwing = true;

    /// <summary>
    /// Klip sekania: dlhý zvislý zdvih sekery z ťažkých útokov mečom (Great Sword Slash), nie rýchly ľahký výpad. Pre hráča aj drevorubačov.
    /// </summary>
    public static AnimationClip ChopClip(PlayerWeapons weapons)
    {
        if (weapons == null || weapons.swordSet == null) return null;
        AnimationClip[] heavy = weapons.swordSet.heavyAttacks;
        if (heavy != null)
            foreach (AnimationClip c in heavy)
                if (c != null && c.name.ToLowerInvariant().Contains("slash")) return c;
        if (heavy != null && heavy.Length > 0 && heavy[0] != null) return heavy[0];
        AnimationClip[] light = weapons.swordSet.lightAttacks;
        return light != null && light.Length > 0 ? light[0] : null;
    }
    private PlayerWeapons weapons;
    private const float SwingSeconds = 0.5f;
    private float swingStart = -1f;
    private Transform visualRoot;
    private Quaternion bodyBase;
    private PlayerVisual visual;

    // Fázy: nádych (0–0,3), seknutie (0,3–0,5 vo zlomku), návrat
    static float SwingAngle(float u, float windup, float strike)
    {
        if (u < 0.3f) return Mathf.Lerp(0f, windup, Mathf.SmoothStep(0f, 1f, u / 0.3f));
        if (u < 0.5f) return Mathf.Lerp(windup, strike, (u - 0.3f) / 0.2f);
        return Mathf.Lerp(strike, 0f, Mathf.SmoothStep(0f, 1f, (u - 0.5f) / 0.5f));
    }

    void LateUpdate()
    {
        if (swingStart < 0f) return;

        float u = (Time.time - swingStart) / SwingSeconds;
        bool done = u >= 1f;
        if (holder != null) holder.localRotation = Quaternion.Euler(done ? 0f : SwingAngle(u, -110f, 35f), 0f, 0f);
        if (visualRoot != null) visualRoot.localRotation = Quaternion.AngleAxis(done ? 0f : SwingAngle(u, -8f, 20f), Vector3.right) * bodyBase;
        if (done) swingStart = -1f;
    }

    void Build(Transform hand, ToolKit.Kind kind, int tier)
    {
        if (holder != null) Destroy(holder.gameObject);
        builtKind = kind;
        builtTier = tier;

        holder = new GameObject("PlayerTool").transform;
        holder.SetParent(hand, false);
        holder.localPosition = Vector3.zero;
        holder.localRotation = Quaternion.identity;
        holder.localScale = Vector3.one / Mathf.Max(0.0001f, hand.lossyScale.x);   // svetová veľkosť bez ohľadu na mierku kosti

        Color metal = ToolKit.ColorOf(tier);
        string modelName = kind == ToolKit.Kind.Axe ? "Tool_Axe" : kind == ToolKit.Kind.Pickaxe ? "Tool_Pickaxe" : null;
        GameObject model = modelName != null ? Resources.Load<GameObject>("Tools/" + modelName) : null;

        if (model != null)
        {
            // Model z balíka: farba typu sa primieša do textúry, najsilnejší typ navyše žiari
            GameObject instance = Instantiate(model, holder);
            instance.transform.localPosition = Vector3.zero;
            foreach (Collider c in instance.GetComponentsInChildren<Collider>()) Destroy(c);

            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor("_Color", Color.Lerp(Color.white, metal, tier == 0 ? 0.15f : 0.55f));
            if (tier == 3) block.SetColor("_EmissionColor", metal * 0.8f);
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>())
            {
                r.SetPropertyBlock(block);
                if (tier == 3) foreach (Material m in r.materials) m.EnableKeyword("_EMISSION");
            }
            return;
        }

        BuildPrimitive(kind, tier, metal);
    }

    void BuildPrimitive(ToolKit.Kind kind, int tier, Color metal)
    {
        Material wood = Make(new Color(0.42f, 0.28f, 0.14f), 0f);
        Material head = Make(tier == 0 ? new Color(0.5f, 0.5f, 0.52f) : metal, tier == 0 ? 0.1f : 0.6f);
        if (tier == 3)
        {
            head.EnableKeyword("_EMISSION");
            head.SetColor("_EmissionColor", metal * 1.4f);
        }

        // Rukoväť pozdĺž osi Y ruky, hlava na konci
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0.28f, 0f), new Vector3(0.05f, 0.34f, 0.05f), wood);
        if (kind == ToolKit.Kind.Axe)
        {
            Part(PrimitiveType.Cube, new Vector3(0.1f, 0.58f, 0f), new Vector3(0.22f, 0.2f, 0.04f), head);
            Part(PrimitiveType.Cube, new Vector3(-0.02f, 0.58f, 0f), new Vector3(0.07f, 0.12f, 0.06f), head);
        }
        else if (kind == ToolKit.Kind.Pickaxe)
        {
            Part(PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), new Vector3(0.38f, 0.05f, 0.05f), head);
            Part(PrimitiveType.Cube, new Vector3(0.2f, 0.57f, 0f), new Vector3(0.05f, 0.1f, 0.05f), head);
            Part(PrimitiveType.Cube, new Vector3(-0.2f, 0.57f, 0f), new Vector3(0.05f, 0.1f, 0.05f), head);
        }
        else
        {
            Part(PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), new Vector3(0.24f, 0.14f, 0.12f), head);   // masívna hlava kladiva
        }
    }

    static Material Make(Color color, float metallic)
    {
        Material m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", 0.4f);
        return m;
    }

    void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(holder, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = mat;
    }

    void Update()
    {
        if (holder == null || !holder.gameObject.activeSelf) return;
        if (weapons == null) weapons = GetComponent<PlayerWeapons>();
        // Sekera ostáva v ruke 5 s po poslednom použití; po vytasení meča hneď zmizne
        if (Time.time >= hideAt || (weapons != null && weapons.IsDrawn)) holder.gameObject.SetActive(false);
    }
}
