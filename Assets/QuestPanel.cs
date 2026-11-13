using UnityEngine;

/// <summary>
/// Spoločný panel aktuálnej úlohy (prológ aj hlavný quest): tmavý panel so zlatým pruhom, názov úlohy, zoznam krokov s odškrtávaním
/// a zvýraznený aktuálny cieľ s pásikom postupu. Volá sa z OnGUI; texty cachuje volajúci (žiadne alokácie na snímok).
/// </summary>
public static class QuestPanel
{
    private const float Width = 330f, Pad = 12f, StepHeight = 22f;
    private const float Scale = 0.8f;   // celkové zmenšenie panelu
    private static GUIStyle headStyle, stepStyle, goalStyle, tagStyle;
    private static readonly GUIContent goalContent = new GUIContent();
    private static readonly Color Gold = new Color(1f, 0.82f, 0.35f);
    private static readonly Color Done = new Color(0.45f, 0.8f, 0.5f);
    private static readonly Color Dim = new Color(1f, 1f, 1f, 0.38f);

    public static string JournalTitle, JournalGoal;
    public static string[] JournalSteps;
    public static int JournalCurrent;
    public static float JournalProgress, JournalTime = -100f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetJournal() { JournalTitle = JournalGoal = null; JournalSteps = null; JournalTime = -100f; }

    static void Init()
    {
        headStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = false };
        headStyle.normal.textColor = Gold;
        tagStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
        tagStyle.normal.textColor = new Color(1f, 1f, 1f, 0.5f);
        stepStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false };
        goalStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
        goalStyle.normal.textColor = Color.white;
    }

    static void Fill(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
    }

    /// <param name="steps">Názvy krokov (môže byť prázdne)</param>
    /// <param name="current">Index aktuálneho kroku; kroky pred ním sú splnené</param>
    /// <param name="progress">0–1 pásik postupu aktuálneho cieľa, záporné = bez pásika</param>
    public static void Draw(string title, string[] steps, int current, string goal, float progress, float x = 14f, float y = 150f)
    {
        // Denník (menu → Úlohy) ukazuje to isté, čo panel naposledy kreslil
        JournalTitle = title; JournalSteps = steps; JournalCurrent = current; JournalGoal = goal; JournalProgress = progress;
        JournalTime = Time.unscaledTime;
        if (Inventory.MenuOpen) return;   // okno inventára je cez celú obrazovku
        if (headStyle == null) Init();
        if (HudUi.LeftBottomPixels > 0f) y = Mathf.Max(y, HudUi.LeftBottomPixels + 8f);   // nový ľavý panel HUD-u je vyšší než starý

        goalContent.text = goal;
        float goalH = goalStyle.CalcHeight(goalContent, Width - Pad * 2f - 4f);
        float stepsH = steps != null ? steps.Length * StepHeight : 0f;
        float barH = progress >= 0f ? 14f : 0f;
        float height = Pad + 18f + 24f + stepsH + (stepsH > 0f ? 8f : 0f) + goalH + barH + Pad + 4f;

        Color prev = GUI.color;
        Matrix4x4 prevMatrix = GUI.matrix;
        GUI.matrix = prevMatrix * Matrix4x4.TRS(new Vector3(x * (1f - Scale), y * (1f - Scale), 0f), Quaternion.identity, new Vector3(Scale, Scale, 1f));   // zmenšenie okolo ľavého horného rohu panelu
        Rect panel = new Rect(x, y, Width, height);
        Fill(new Rect(panel.x + 2f, panel.y + 3f, panel.width, panel.height), new Color(0f, 0f, 0f, 0.35f));   // tieň
        Fill(panel, new Color(0.05f, 0.05f, 0.08f, 0.82f));
        Fill(new Rect(panel.x, panel.y, 4f, panel.height), Gold);

        float cx = panel.x + Pad + 4f, cy = panel.y + Pad;
        GUI.color = Color.white;
        GUI.Label(new Rect(cx, cy, Width - Pad * 2f, 16f), "ÚLOHA", tagStyle);
        cy += 14f;
        GUI.Label(new Rect(cx, cy, Width - Pad * 2f, 26f), title, headStyle);
        cy += 28f;

        if (steps != null)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                bool done = i < current, now = i == current;
                Rect box = new Rect(cx, cy + 5f, 11f, 11f);
                Fill(box, done ? Done : now ? Gold : Dim);
                if (!done && !now) Fill(new Rect(box.x + 2f, box.y + 2f, 7f, 7f), new Color(0.05f, 0.05f, 0.08f, 1f));
                stepStyle.normal.textColor = done ? new Color(Done.r, Done.g, Done.b, 0.75f) : now ? Color.white : Dim;
                GUI.color = Color.white;
                GUI.Label(new Rect(cx + 19f, cy, Width - Pad * 2f - 19f, StepHeight), steps[i], stepStyle);
                cy += StepHeight;
            }
            cy += 8f;
        }

        // Aktuálny cieľ vo vlastnom svetlejšom poli
        Fill(new Rect(cx - 2f, cy - 2f, Width - Pad * 2f + 2f, goalH + barH + 6f), new Color(1f, 1f, 1f, 0.07f));
        GUI.color = Color.white;
        GUI.Label(new Rect(cx, cy, Width - Pad * 2f - 4f, goalH), goalContent, goalStyle);
        cy += goalH;
        if (progress >= 0f)
        {
            Fill(new Rect(cx, cy + 3f, Width - Pad * 2f - 4f, 6f), new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(cx, cy + 3f, (Width - Pad * 2f - 4f) * Mathf.Clamp01(progress), 6f), Gold);
        }
        GUI.color = prev;
        GUI.matrix = prevMatrix;
    }
}
