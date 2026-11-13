using UnityEngine;

/// <summary>
/// Vzhľad postavy hráča: farba pokožky, farba oblečenia a tvar (výška, šírka, veľkosť hlavy). Menu sa otvára klávesom U,
/// nastavenie sa ukladá do PlayerPrefs. Farby sa nanášajú cez MaterialPropertyBlock na shader Progeny/PlayerBody
/// (pokožka a oblečenie sa v textúre rozlíšia podľa farby), tvar mení mierku modelu okolo päty, takže postava ostáva na zemi.
/// </summary>
[RequireComponent(typeof(PlayerVisual))]
[DefaultExecutionOrder(100)]   // LateUpdate po animátore (veľkosť hlavy sa nesmie prepísať animáciou)
public class PlayerAppearance : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }

    private const string SaveKey = "POA.Appearance2";

    public KeyCode menuKey => KeyBindings.Get("appearance", KeyCode.U);
    [Header("Ovládanie")]

    [Header("Farby")]
    public Color skinColor = new Color(0.86f, 0.69f, 0.57f);
    public Color clothesColor = new Color(0.22f, 0.26f, 0.40f);
    [Tooltip("Vypnuté = pôvodná textúra modelu bez prefarbenia")]
    public bool recolor = true;

    [Header("Tvar postavy")]
    [Range(0.9f, 1.15f)] public float height = 1f;
    [Range(0.85f, 1.25f)] public float width = 1f;
    [Range(0.85f, 1.3f)] public float headSize = 1f;

    private struct Preset
    {
        public string name;
        public Color color;
    }

    private static readonly Preset[] SkinPresets =
    {
        new Preset { name = "Svetlá", color = new Color(0.90f, 0.75f, 0.66f) },
        new Preset { name = "Stredná", color = new Color(0.86f, 0.69f, 0.57f) },
        new Preset { name = "Opálená", color = new Color(0.72f, 0.52f, 0.38f) },
        new Preset { name = "Tmavá", color = new Color(0.45f, 0.30f, 0.22f) },
        new Preset { name = "Bledá", color = new Color(0.95f, 0.88f, 0.84f) },
        new Preset { name = "Popolová", color = new Color(0.62f, 0.64f, 0.70f) },
        new Preset { name = "Zelená", color = new Color(0.45f, 0.65f, 0.45f) },
        new Preset { name = "Fialová", color = new Color(0.62f, 0.50f, 0.78f) },
    };

    private static readonly Preset[] ClothesPresets =
    {
        new Preset { name = "Noc", color = new Color(0.22f, 0.26f, 0.40f) },
        new Preset { name = "Karmín", color = new Color(0.70f, 0.15f, 0.20f) },
        new Preset { name = "Les", color = new Color(0.25f, 0.45f, 0.30f) },
        new Preset { name = "Oceľ", color = new Color(0.55f, 0.60f, 0.70f) },
        new Preset { name = "Zlato", color = new Color(0.85f, 0.70f, 0.25f) },
        new Preset { name = "Čierna", color = new Color(0.10f, 0.10f, 0.12f) },
        new Preset { name = "Biela", color = new Color(0.90f, 0.90f, 0.90f) },
        new Preset { name = "Fialová", color = new Color(0.50f, 0.30f, 0.70f) },
    };

    [System.Serializable]
    private class SaveData
    {
        public float sr, sg, sb, cr, cg, cb, height, width, headSize;
        public bool recolor;
    }

    private PlayerVisual visual;
    private PlayerStats stats;
    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private Vector3 fitScale = Vector3.one;
    private Transform head;
    private bool editSkin = true;
    private GUIStyle titleStyle, textStyle;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        visual = GetComponent<PlayerVisual>();
        Load();
    }

    void Start()
    {
        if (visual == null || !visual.HasModel) return;

        renderers = visual.BodyRenderers;
        fitScale = visual.VisualRoot.localScale;

        foreach (Transform t in visual.VisualRoot.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.EndsWith("Head", System.StringComparison.Ordinal)) continue;
            head = t;
            break;
        }

        Apply();
    }

    void OnDestroy()
    {
        if (MenuOpen) SetMenu(false);
    }

    // ---------- Použitie nastavenia ----------
    void Apply()
    {
        if (renderers == null) return;

        if (block == null) block = new MaterialPropertyBlock();
        block.SetColor("_SkinColor", skinColor);
        block.SetColor("_ClothesColor", clothesColor);
        block.SetFloat("_Recolor", recolor ? 1f : 0f);
        block.SetColor("_Color", clothesColor);   // záloha, ak má materiál obyčajný Standard shader
        foreach (Renderer r in renderers)
            if (r != null) r.SetPropertyBlock(block);

        // Tvar: mierka okolo päty, animátor si ju prevezme ako základnú a prepočíta výšku chodidiel
        Vector3 scale = new Vector3(fitScale.x * width, fitScale.y * height, fitScale.z * width);
        if (visual.ModelAnimator != null)
        {
            visual.ModelAnimator.SetBaseScale(scale);
            visual.ModelAnimator.RecalibrateFloors();
        }
        else visual.VisualRoot.localScale = scale;
    }

    // Veľkosť hlavy sa nastavuje po animácii, aby ju klip neprepísal
    void LateUpdate()
    {
        if (head != null) head.localScale = Vector3.one * headSize;
    }

    // ---------- Ukladanie ----------
    void Save()
    {
        var data = new SaveData
        {
            sr = skinColor.r, sg = skinColor.g, sb = skinColor.b,
            cr = clothesColor.r, cg = clothesColor.g, cb = clothesColor.b,
            height = height, width = width, headSize = headSize, recolor = recolor
        };
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
    }

    void Load()
    {
        if (!PlayerPrefs.HasKey(SaveKey)) return;

        SaveData data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(SaveKey));
        if (data == null) return;

        skinColor = new Color(Mathf.Clamp01(data.sr), Mathf.Clamp01(data.sg), Mathf.Clamp01(data.sb));
        clothesColor = new Color(Mathf.Clamp01(data.cr), Mathf.Clamp01(data.cg), Mathf.Clamp01(data.cb));
        height = Mathf.Clamp(data.height, 0.9f, 1.15f);
        width = Mathf.Clamp(data.width, 0.85f, 1.25f);
        headSize = Mathf.Clamp(data.headSize, 0.85f, 1.3f);
        recolor = data.recolor;
    }

    // ---------- Menu ----------
    void Update()
    {
        if (stats == null || stats.IsDead)
        {
            if (MenuOpen) SetMenu(false);
            return;
        }

        if (GameInput.GetKeyDown(menuKey) || (MenuOpen && GameInput.GetKeyDown(KeyCode.Escape)))
        {
            if (MenuOpen) SetMenu(false);
            else if (!Healer.MenuOpen && visual != null && visual.HasModel) SetMenu(true);
        }
    }

    void SetMenu(bool open)
    {
        if (MenuOpen == open) return;
        MenuOpen = open;
        if (!open) Save();

        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!MenuOpen) return;

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        }

        float s = Mathf.Clamp(Screen.height / 720f, 0.85f, 1.6f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        float vh = Screen.height / s;

        Rect box = new Rect((vw - 470f) * 0.5f, (vh - 590f) * 0.5f, 470f, 590f);
        Color prev = GUI.color;
        GUI.color = new Color(0.04f, 0.04f, 0.07f, 0.95f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = prev;

        float x = box.x + 24f;
        float y = box.y + 14f;
        GUI.Label(new Rect(x, y, 400f, 30f), "VZHĽAD POSTAVY", titleStyle);
        y += 42f;

        bool changed = false;

        // Čo sa mení: pokožka alebo oblečenie
        GUI.color = editSkin ? Color.white : new Color(1f, 1f, 1f, 0.5f);
        if (GUI.Button(new Rect(x, y, 200f, 30f), "Pokožka")) editSkin = true;
        GUI.color = editSkin ? new Color(1f, 1f, 1f, 0.5f) : Color.white;
        if (GUI.Button(new Rect(x + 210f, y, 200f, 30f), "Oblečenie")) editSkin = false;
        GUI.color = Color.white;
        y += 40f;

        Preset[] presets = editSkin ? SkinPresets : ClothesPresets;
        for (int i = 0; i < presets.Length; i++)
        {
            Rect r = new Rect(x + (i % 4) * 104f, y + (i / 4) * 34f, 98f, 28f);
            GUI.color = presets[i].color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                if (editSkin) skinColor = presets[i].color; else clothesColor = presets[i].color;
                recolor = true;
                changed = true;
            }
            GUI.Label(new Rect(r.x + 4f, r.y + 3f, r.width, r.height), presets[i].name, textStyle);
        }
        y += 76f;

        // Vlastná farba (RGB) pre vybranú časť
        Color edit = editSkin ? skinColor : clothesColor;
        bool colorChanged = false;
        colorChanged |= Slider(ref y, x, "Červená", ref edit.r, 0f, 1f);
        colorChanged |= Slider(ref y, x, "Zelená", ref edit.g, 0f, 1f);
        colorChanged |= Slider(ref y, x, "Modrá", ref edit.b, 0f, 1f);
        if (colorChanged)
        {
            if (editSkin) skinColor = edit; else clothesColor = edit;
            recolor = true;
            changed = true;
        }
        y += 8f;

        GUI.Label(new Rect(x, y, 400f, 22f), "Tvar", textStyle);
        y += 26f;
        changed |= Slider(ref y, x, "Výška", ref height, 0.9f, 1.15f);
        changed |= Slider(ref y, x, "Šírka", ref width, 0.85f, 1.25f);
        changed |= Slider(ref y, x, "Hlava", ref headSize, 0.85f, 1.3f);
        y += 10f;

        if (GUI.Button(new Rect(x, y, 130f, 32f), "Pôvodné"))
        {
            skinColor = SkinPresets[1].color;
            clothesColor = ClothesPresets[0].color;
            height = 1f; width = 1f; headSize = 1f;
            recolor = true;
            changed = true;
        }
        if (GUI.Button(new Rect(x + 140f, y, 160f, 32f), recolor ? "Pôvodná textúra" : "Vlastné farby"))
        {
            recolor = !recolor;
            changed = true;
        }
        if (GUI.Button(new Rect(x + 310f, y, 100f, 32f), "Hotovo")) SetMenu(false);

        if (changed) Apply();
        GUI.matrix = previous;
    }

    bool Slider(ref float y, float x, string label, ref float value, float min, float max)
    {
        GUI.Label(new Rect(x, y, 90f, 22f), label, textStyle);
        float next = GUI.HorizontalSlider(new Rect(x + 100f, y + 6f, 260f, 20f), value, min, max);
        GUI.Label(new Rect(x + 370f, y, 60f, 22f), next.ToString("0.00"), textStyle);
        y += 28f;

        bool changed = !Mathf.Approximately(next, value);
        value = next;
        return changed;
    }
}
