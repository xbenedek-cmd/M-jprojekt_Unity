using UnityEngine;

/// <summary>
/// Úvodná animácia pred hlavným menu: tma, úder blesku, názov hry sa po písmenách rozsvieti, stúpajúci popol. Kreslí sa v OnGUI cez celú obrazovku
/// a pohlcuje vstup, kým beží. Preskočí sa ľubovoľným klávesom / kliknutím. Beží raz za spustenie hry.
/// </summary>
public class IntroSplash : MonoBehaviour
{
    private const float Hold = 8f, FadeOut = 3f, Duration = Hold + FadeOut;
    private const float LetterFade = 1.2f;   // trvanie zmiznutia jedného písmena; začiatky sú rozložené po celom FadeOut
    private const string Title = "PROGENY OF THE ASHES";
    private const string Subtitle = "POTOMOK POPOLA";

    private static bool shown;
    private float startTime;
    private bool done;
    private float savedScale = 1f;
    private bool pausedByUs;
    private GUIStyle titleStyle, subStyle, hintStyle;
    private float[] letterWidth;
    private float titleWidth;
    private Texture2D dot;

    private const int Embers = 46;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() => shown = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (shown) return;
        shown = true;
        new GameObject("IntroSplash").AddComponent<IntroSplash>();
    }

    void Start()
    {
        startTime = Time.unscaledTime;
        if (Time.timeScale > 0f)
        {
            savedScale = Time.timeScale;
            Time.timeScale = 0f;   // hra pod animáciou nebeží
            pausedByUs = true;
        }
    }

    void Update()
    {
        if (done) return;
        float t = Time.unscaledTime - startTime;
        if (t > 0.4f && (GameInput.anyKeyDown || GameInput.GetMouseButtonDown(0)) || t >= Duration) Finish();
    }

    void Finish()
    {
        done = true;
        // Čas sa vráti len ak ho medzitým nezmenilo menu (hlavné menu si pri štarte nastavuje vlastné)
        if (pausedByUs && Time.timeScale == 0f && !PauseMenu.AtStartup && !PauseMenu.IsOpen) Time.timeScale = savedScale;
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (!done && pausedByUs && Time.timeScale == 0f && !PauseMenu.AtStartup && !PauseMenu.IsOpen) Time.timeScale = savedScale;
    }

    static float Hash(int n)
    {
        unchecked
        {
            uint x = (uint)n * 747796405u + 2891336453u;
            x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
            x = (x >> 22) ^ x;
            return (x & 0xFFFFu) / 65535f;
        }
    }

    void Prepare()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Overflow };
        subStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter };
        hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter };

        dot = new Texture2D(32, 32, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f;
                dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
            }
        dot.Apply();
        letterWidth = new float[Title.Length];
    }

    void OnGUI()
    {
        if (done) return;
        GUI.depth = -10000;   // nad všetkým ostatným

        // Počas animácie sa vstup pre GUI pod ňou zahodí
        Event e = Event.current;
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.KeyDown || e.type == EventType.ScrollWheel) e.Use();
        if (e.type != EventType.Repaint) return;

        Prepare();
        float t = Time.unscaledTime - startTime;
        float w = Screen.width, h = Screen.height;
        float fadeOut = Mathf.Clamp01((Duration - t) / FadeOut);   // celé zostupne do čierna -> menu
        Color prev = GUI.color;

        // Pozadie: tmavá, jemne pulzujúca modrá
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 1.6f);
        GUI.color = new Color(0.04f, 0.08f, 0.18f, Mathf.Clamp01(t / 2f) * (0.5f + 0.3f * pulse) * fadeOut);
        GUI.DrawTexture(new Rect(0f, h * 0.25f, w, h * 0.5f), dot);

        // Popol stúpajúci zospodu
        for (int i = 0; i < Embers; i++)
        {
            float speed = 24f + Hash(i * 3 + 1) * 60f;
            float size = 3f + Hash(i * 3 + 2) * 7f;
            float x = Hash(i * 3) * w + Mathf.Sin(t * 0.8f + i) * 18f;
            float y = h + 20f - Mathf.Repeat(t * speed + Hash(i * 7) * h * 1.3f, h * 1.3f);
            float life = Mathf.Clamp01(1f - Mathf.Abs(y / h - 0.5f) * 1.6f);
            GUI.color = new Color(1f, 0.45f + Hash(i) * 0.3f, 0.12f, 0.8f * life * Mathf.Clamp01(t / 1.5f) * fadeOut);
            GUI.DrawTexture(new Rect(x, y, size, size), dot);
        }

        // Blesk (t 0,9–1,5): zubatá čiara zhora do stredu a biely záblesk
        float cx = w * 0.5f, cy = h * 0.46f;
        if (t > 0.9f && t < 1.7f)
        {
            float k = (t - 0.9f) / 0.8f;
            float a = (1f - k) * (1f - k);
            float x0 = cx + (Hash(9) - 0.5f) * 80f, y0 = 0f;
            int segments = 9;
            for (int i = 1; i <= segments; i++)
            {
                float u = i / (float)segments;
                float x1 = Mathf.Lerp(cx, x0, 1f - u) + (Hash(i + 20) - 0.5f) * 70f * (1f - u * 0.4f);
                float y1 = Mathf.Lerp(0f, cy, u);
                DrawLine(x0, y0, x1, y1, 7f * a + 1f, new Color(0.65f, 0.85f, 1f, a * fadeOut));
                DrawLine(x0, y0, x1, y1, 2f, new Color(1f, 1f, 1f, a * fadeOut));
                x0 = x1; y0 = y1;
            }
            GUI.color = new Color(0.8f, 0.9f, 1f, 0.55f * a * fadeOut);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
        }

        // Názov po písmenách
        int fontSize = Mathf.RoundToInt(Mathf.Clamp(h * 0.085f, 28f, 90f));
        titleStyle.fontSize = fontSize;
        if (titleWidth <= 0f)
        {
            titleWidth = 0f;
            for (int i = 0; i < Title.Length; i++)
            {
                letterWidth[i] = titleStyle.CalcSize(new GUIContent(Title[i].ToString())).x;
                titleWidth += letterWidth[i];
            }
        }
        float spacing = 4f + Mathf.Min(t, 6f) * 1.4f;   // písmená sa pomaly rozťahujú
        float total = titleWidth + spacing * (Title.Length - 1);
        float scale = Mathf.Min(1f, (w * 0.92f) / total);
        float px = cx - total * scale * 0.5f;
        for (int i = 0; i < Title.Length; i++)
        {
            float start = 1.2f + i * 0.09f;
            float a = Mathf.Clamp01((t - start) / 0.55f);
            // Písmená miznú postupne zľava doprava
            float outStart = Hold + i * (FadeOut - LetterFade) / (Title.Length - 1);
            float letterOut = 1f - Mathf.SmoothStep(0f, 1f, (t - outStart) / LetterFade);
            if (a > 0f && letterOut > 0f && Title[i] != ' ')
            {
                float glow = Mathf.Clamp01(1f - (t - start) / 1.4f);   // čerstvé písmeno žiari modro, potom vychladne do popola
                Color c = Color.Lerp(new Color(0.88f, 0.84f, 0.8f), new Color(0.55f, 0.85f, 1f), glow);
                c.a = a * letterOut;
                float ly = cy - fontSize * 0.5f + (1f - a) * 22f;
                titleStyle.normal.textColor = new Color(0.1f, 0.5f, 1f, 0.35f * glow * a * letterOut);
                GUI.Label(new Rect(px - 2f, ly, 200f, fontSize * 1.3f), Title[i].ToString(), titleStyle);
                GUI.Label(new Rect(px + 2f, ly, 200f, fontSize * 1.3f), Title[i].ToString(), titleStyle);
                titleStyle.normal.textColor = c;
                GUI.Label(new Rect(px, ly, 200f, fontSize * 1.3f), Title[i].ToString(), titleStyle);
            }
            px += (letterWidth[i] + spacing) * scale;
        }

        // Podnázov a čiara
        float subA = Mathf.Clamp01((t - 4f) / 1.2f) * fadeOut;
        if (subA > 0f)
        {
            float lineW = Mathf.Lerp(0f, w * 0.34f, Mathf.SmoothStep(0f, 1f, (t - 4f) / 1.4f));
            GUI.color = new Color(0.55f, 0.85f, 1f, subA * 0.8f);
            GUI.DrawTexture(new Rect(cx - lineW * 0.5f, cy + fontSize * 0.95f, lineW, 2f), Texture2D.whiteTexture);
            subStyle.fontSize = Mathf.RoundToInt(fontSize * 0.34f);
            subStyle.normal.textColor = new Color(0.75f, 0.8f, 0.85f, subA);
            GUI.Label(new Rect(0f, cy + fontSize * 1.05f, w, fontSize * 0.6f), Subtitle, subStyle);
        }

        if (t > 1.5f)
        {
            hintStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 0.5f * fadeOut * (0.6f + 0.4f * pulse));
            GUI.Label(new Rect(0f, h - 40f, w, 24f), "Ľubovoľný kláves – preskočiť", hintStyle);
        }
        GUI.color = prev;
    }

    // Čiara medzi dvoma bodmi: otočený obdĺžnik
    static void DrawLine(float x0, float y0, float x1, float y1, float thickness, Color color)
    {
        Vector2 d = new Vector2(x1 - x0, y1 - y0);
        float length = d.magnitude;
        if (length < 0.5f) return;
        Matrix4x4 saved = GUI.matrix;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, new Vector2(x0, y0));
        GUI.DrawTexture(new Rect(x0, y0 - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
        GUI.matrix = saved;
    }
}
