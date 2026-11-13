using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dialógy v štýle manga panelov: čierne pásy hore a dole, biely panel s čiernym rámom a menom hovoriaceho, text sa "píše".
/// Show(...) zastaví hru (timeScale 0) a posúva sa klávesom E / medzerník / Enter / kliknutím; Whisper(...) je nenásilný pruh hore
/// (hlas Arakaniho v noci), hra beží ďalej. Fronta: ďalší dialóg sa spustí po skončení predchádzajúceho.
/// </summary>
public class MangaDialog : MonoBehaviour
{
    public static bool Active { get; private set; }

    private class Entry
    {
        public string speaker;
        public string[] lines;
        public Action done;
    }

    private static readonly Queue<Entry> queue = new Queue<Entry>();
    private static Entry current;
    private static int lineIndex;
    private static float typed;

    private static string whisperSpeaker, whisperText;
    private static float whisperUntil;

    private static float savedScale = 1f;
    private static CursorLockMode savedLock;
    private static bool savedVisible;
    private GUIStyle speakerStyle, textStyle, hintStyle, whisperStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        queue.Clear();
        current = null;
        Active = false;
        whisperUntil = 0f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<MangaDialog>() != null) return;
        new GameObject("MangaDialog").AddComponent<MangaDialog>();
    }

    public static void Show(string speaker, string[] lines, Action onDone = null)
    {
        queue.Enqueue(new Entry { speaker = speaker, lines = lines, done = onDone });
    }

    public static void Whisper(string speaker, string text, float seconds = 7f)
    {
        whisperSpeaker = speaker;
        whisperText = text;
        whisperUntil = Time.unscaledTime + seconds;
    }

    void Update()
    {
        if (current == null)
        {
            if (queue.Count == 0 || PauseMenu.AtStartup || Inventory.MenuOpen) return;   // pri inventári dialóg počká
            Begin(queue.Dequeue());
            return;
        }

        if (PauseMenu.IsOpen) return;   // menu pauzy (Esc) má prednosť: dialóg sa nepíše ani neposúva

        typed += Time.unscaledDeltaTime * 45f;
        bool advance = GameInput.GetKeyDown(KeyCode.E) || GameInput.GetKeyDown(KeyCode.Space) || GameInput.GetKeyDown(KeyCode.Return) || GameInput.GetMouseButtonDown(0);
        if (!advance) return;

        string line = current.lines[lineIndex];
        if (typed < line.Length) { typed = line.Length; return; }   // prvé stlačenie dopíše riadok

        lineIndex++;
        typed = 0f;
        lineTime = Time.unscaledTime;
        if (lineIndex < current.lines.Length) return;

        Action done = current.done;
        current = null;
        End();
        done?.Invoke();
    }

    static void Begin(Entry entry)
    {
        if (entry.lines == null || entry.lines.Length == 0) { entry.done?.Invoke(); return; }

        current = entry;
        lineIndex = 0;
        typed = 0f;
        openTime = lineTime = Time.unscaledTime;
        cachedLine = -1;

        if (!Active)
        {
            Active = true;
            savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            savedLock = Cursor.lockState;
            savedVisible = Cursor.visible;
        }
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    static void End()
    {
        if (queue.Count > 0) return;   // ďalší dialóg v rade: hra ostáva zastavená

        Active = false;
        Time.timeScale = savedScale;
        Cursor.lockState = savedLock == CursorLockMode.None ? CursorLockMode.Locked : savedLock;
        Cursor.visible = savedLock == CursorLockMode.None ? false : savedVisible;
    }

    void OnDestroy()
    {
        if (Active) { Active = false; Time.timeScale = 1f; }
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;
        if (current == null && Inventory.MenuOpen) return;   // šepot počas inventára nevyskakuje

        if (speakerStyle == null)
        {
            speakerStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            speakerStyle.normal.textColor = Color.white;
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, wordWrap = true, richText = true };
            textStyle.normal.textColor = new Color(0.93f, 0.96f, 1f);
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            hintStyle.normal.textColor = new Color(0.55f, 0.58f, 0.65f);
            whisperStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Italic, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            whisperStyle.normal.textColor = new Color(0.95f, 0.8f, 0.9f);
        }

        // Šepot (Arakani v noci): pruh hore, bez zastavenia hry
        if (current == null && Time.unscaledTime < whisperUntil && !string.IsNullOrEmpty(whisperText))
        {
            float a = Mathf.Clamp01((whisperUntil - Time.unscaledTime) / 1.2f);
            Color prev = GUI.color;
            GUI.color = new Color(0.12f, 0f, 0.08f, 0.7f * a);
            GUI.DrawTexture(new Rect(Screen.width * 0.2f, 40f, Screen.width * 0.6f, 74f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(new Rect(Screen.width * 0.2f + 14f, 44f, Screen.width * 0.6f - 28f, 66f), $"{whisperSpeaker}: „{whisperText}“", whisperStyle);
            GUI.color = prev;
        }

        if (current == null || PauseMenu.IsOpen) return;

        float w = Screen.width, h = Screen.height;
        Color old = GUI.color;
        // Čierne pásy sa vysunú hore a dole (filmový záber)
        float barIn = Mathf.Clamp01((Time.unscaledTime - openTime) / 0.4f);
        float barH = h * 0.12f * (1f - (1f - barIn) * (1f - barIn));
        GUI.color = new Color(0f, 0f, 0f, 0.92f);
        GUI.DrawTexture(new Rect(0f, 0f, w, barH), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0f, h - barH, w, barH), Texture2D.whiteTexture);

        DrawPanel(w, h, old);
    }

    // ---------- Vzhľad panelu: tmavé sklo, žiariaci rám v farbe hovoriaceho, animovaný portrét, písanie s doznievaním ----------
    private static Texture2D circle;
    private static readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
    private static string cachedText = "", cachedShadow = "";
    private static int cachedShown = -1, cachedLine = -1;
    private static float openTime, lineTime;

    static Texture2D Circle()
    {
        if (circle != null) return circle;
        const int n = 64;
        circle = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                circle.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * n * 0.25f)));
            }
        circle.Apply();
        return circle;
    }

    static Color SpeakerColor(string speaker)
    {
        switch ((speaker ?? "").ToUpperInvariant())
        {
            case "ARAKANI": return new Color(0.7f, 0.3f, 0.85f);
            case "MESTO": return new Color(0.95f, 0.65f, 0.25f);
            case "LEKÁRKA": case "LEKÁR": case "DOKTORKA YUMI": return new Color(0.3f, 0.8f, 0.5f);
            case "VOJAK": return new Color(0.4f, 0.6f, 0.8f);
            case "ZISTENIE": case "SÚMRAK": case "PROLÓG": return new Color(0.75f, 0.75f, 0.8f);
            default: return new Color(0.9f, 0.3f, 0.3f);
        }
    }

    static void Fill(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
    }

    // Rohy rámu (komiksový "technický" detail)
    static void Corners(Rect r, float len, float thick, Color c)
    {
        Fill(new Rect(r.x, r.y, len, thick), c); Fill(new Rect(r.x, r.y, thick, len), c);
        Fill(new Rect(r.xMax - len, r.y, len, thick), c); Fill(new Rect(r.xMax - thick, r.y, thick, len), c);
        Fill(new Rect(r.x, r.yMax - thick, len, thick), c); Fill(new Rect(r.x, r.yMax - len, thick, len), c);
        Fill(new Rect(r.xMax - len, r.yMax - thick, len, thick), c); Fill(new Rect(r.xMax - thick, r.yMax - len, thick, len), c);
    }

    // Text sa objavuje po písmenách, posledných pár dozniva z priehľadnosti do plnej farby
    static string RevealText(string line, int shown, int lineIdx)
    {
        if (cachedShown == shown && cachedLine == lineIdx) return cachedText;
        cachedShown = shown;
        cachedLine = lineIdx;

        // Písmená sa objavujú rovno v plnej farbe (doznievanie nepôsobilo pekne)
        sb.Clear();
        sb.Append(line, 0, shown);
        cachedText = sb.ToString();
        cachedShadow = "<color=#00000099>" + StripTags(cachedText) + "</color>";
        return cachedText;
    }

    // Animovaný portrét: pulzujúca aura, kapucňa/hlava/ramená, mihajúce sa oči a ústa, ktoré sa hýbu, kým sa píše text
    void DrawPortrait(Rect p, Color accent, bool talking, float t, bool shout)
    {
        // aura
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 2.2f);
        float aura = p.width * (0.18f + 0.06f * pulse);
        Fill(new Rect(p.x - aura * 0.5f, p.y - aura * 0.5f, p.width + aura, p.height + aura), new Color(accent.r, accent.g, accent.b, 0.10f + 0.06f * pulse));
        Fill(new Rect(p.x - 4f, p.y - 4f, p.width + 8f, p.height + 8f), accent);
        Fill(new Rect(p.x - 1f, p.y - 1f, p.width + 2f, p.height + 2f), new Color(0.03f, 0.03f, 0.05f));

        GUI.BeginGroup(p);
        float w = p.width, h = p.height;
        // pozadie: odtieň farby postavy, tmavnúci smerom nadol
        Fill(new Rect(0f, 0f, w, h), new Color(accent.r * 0.35f, accent.g * 0.35f, accent.b * 0.35f));
        Fill(new Rect(0f, h * 0.55f, w, h * 0.45f), new Color(0f, 0f, 0f, 0.35f));
        // stúpajúce iskry
        for (int i = 0; i < 6; i++)
        {
            float u = Mathf.Repeat(t * (0.12f + i * 0.03f) + i * 0.37f, 1f);
            GUI.color = new Color(accent.r, accent.g, accent.b, (1f - u) * 0.7f);
            float s = 3f + (i % 3) * 2f;
            GUI.DrawTexture(new Rect(w * (0.12f + 0.15f * i) + Mathf.Sin(t + i) * 5f, h * (1f - u), s, s), Circle());
        }

        float bob = Mathf.Sin(t * 1.7f) * h * 0.012f + (talking ? Mathf.Abs(Mathf.Sin(t * 9f)) * h * 0.008f : 0f);
        // ramená + plášť
        GUI.color = new Color(0.08f, 0.08f, 0.1f);
        GUI.DrawTexture(new Rect(w * 0.06f, h * 0.68f + bob, w * 0.88f, h * 0.7f), Circle());
        GUI.color = new Color(accent.r * 0.6f, accent.g * 0.6f, accent.b * 0.6f);
        GUI.DrawTexture(new Rect(w * 0.42f, h * 0.7f + bob, w * 0.16f, h * 0.3f), Circle());   // golier
        // kapucňa a hlava
        GUI.color = new Color(0.06f, 0.06f, 0.08f);
        GUI.DrawTexture(new Rect(w * 0.22f, h * 0.1f + bob, w * 0.56f, h * 0.66f), Circle());
        GUI.color = new Color(0.78f, 0.62f, 0.52f);
        GUI.DrawTexture(new Rect(w * 0.31f, h * 0.2f + bob, w * 0.38f, h * 0.48f), Circle());
        GUI.color = new Color(0f, 0f, 0f, 0.28f);
        GUI.DrawTexture(new Rect(w * 0.27f, h * 0.14f + bob, w * 0.46f, h * 0.24f), Circle());   // tieň kapucne na čele

        // oči: žmurkanie každé ~3,4 s, pohľad sa jemne mení
        float blinkPhase = Mathf.Repeat(t, 3.4f);
        float open = blinkPhase > 3.25f ? Mathf.Abs(blinkPhase - 3.325f) / 0.075f : 1f;
        open = Mathf.Clamp01(open);
        if (shout) open = 1f;
        float eyeW = w * 0.09f, eyeH = h * 0.075f * Mathf.Max(0.08f, open);
        float eyeY = h * 0.42f + bob;
        float look = Mathf.Sin(t * 0.7f) * w * 0.012f;
        for (int side = -1; side <= 1; side += 2)
        {
            float ex = w * 0.5f + side * w * 0.095f;
            GUI.color = new Color(0.96f, 0.96f, 0.96f);
            GUI.DrawTexture(new Rect(ex - eyeW * 0.5f, eyeY - eyeH * 0.5f, eyeW, eyeH), Circle());
            if (open > 0.3f)
            {
                GUI.color = new Color(accent.r * 0.4f, accent.g * 0.7f, accent.b * 0.9f);
                GUI.DrawTexture(new Rect(ex - eyeW * 0.25f + look, eyeY - eyeH * 0.4f, eyeW * 0.5f, eyeH * 0.8f), Circle());
            }
            // obočie: pri kriku šikmé a zamračené
            GUI.color = new Color(0.05f, 0.04f, 0.04f);
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot((shout ? 18f : -4f) * side, new Vector2(ex, eyeY - h * 0.075f));
            GUI.DrawTexture(new Rect(ex - eyeW * 0.65f, eyeY - h * 0.085f, eyeW * 1.3f, h * 0.018f), Texture2D.whiteTexture);
            GUI.matrix = m;
        }

        // ústa: otvárajú sa v rytme písania
        float mouthOpen = talking ? 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(t * (shout ? 16f : 11f))) : 0.08f;
        if (shout && talking) mouthOpen = Mathf.Max(mouthOpen, 0.7f);
        float mw = w * 0.14f, mh = h * 0.07f * mouthOpen + 2f;
        GUI.color = new Color(0.25f, 0.05f, 0.07f);
        GUI.DrawTexture(new Rect(w * 0.5f - mw * 0.5f, h * 0.58f + bob, mw, mh), Circle());
        GUI.EndGroup();

        // rámik s rohmi
        Corners(new Rect(p.x - 6f, p.y - 6f, p.width + 12f, p.height + 12f), 14f, 2f, Color.Lerp(accent, Color.white, 0.5f));
    }

    void DrawPanel(float w, float h, Color old)
    {
        string line = current.lines[Mathf.Clamp(lineIndex, 0, current.lines.Length - 1)];
        bool shout = line.EndsWith("!");
        Color accent = SpeakerColor(current.speaker);
        float t = Time.unscaledTime;
        float intro = Mathf.Clamp01((t - openTime) / 0.32f);
        float ease = 1f - (1f - intro) * (1f - intro) * (1f - intro);
        float pop = Mathf.Clamp01((t - lineTime) / 0.18f);   // pri novom riadku panel krátko "poskočí"

        float ph = h * 0.26f;
        Rect panel = new Rect(w * 0.1f, h * 0.84f - ph + (1f - ease) * 60f - (1f - pop) * 3f, w * 0.8f, ph);
        float shake = shout && typed < line.Length ? 1.6f : 0f;
        if (shake > 0f) panel.x += Mathf.Sin(t * 70f) * shake;

        float alpha = ease;
        // tieň, sklenené telo, dvojitý žiariaci rám
        Fill(new Rect(panel.x + 6f, panel.y + 10f, panel.width, panel.height), new Color(0f, 0f, 0f, 0.5f * alpha));
        Fill(new Rect(panel.x - 3f, panel.y - 3f, panel.width + 6f, panel.height + 6f), new Color(accent.r, accent.g, accent.b, 0.35f * alpha));
        Fill(panel, new Color(0.02f, 0.02f, 0.03f, 0.98f * alpha));
        Fill(new Rect(panel.x + 2f, panel.y + 2f, panel.width - 4f, panel.height - 4f), new Color(0.07f, 0.075f, 0.1f, 0.96f * alpha));
        // jemný zvislý prechod a skenovacie čiary
        Fill(new Rect(panel.x + 2f, panel.y + 2f, panel.width - 4f, panel.height * 0.35f), new Color(accent.r, accent.g, accent.b, 0.07f * alpha));
        for (float ly = panel.y + 6f; ly < panel.yMax - 4f; ly += 5f)
            Fill(new Rect(panel.x + 2f, ly, panel.width - 4f, 1f), new Color(1f, 1f, 1f, 0.018f * alpha));
        // posúvajúci sa svetelný pás pozdĺž horného okraja
        float sweep = Mathf.Repeat(t * 0.35f, 1f);
        Fill(new Rect(panel.x + sweep * (panel.width - 140f), panel.y, 140f, 2f), new Color(accent.r, accent.g, accent.b, 0.9f * alpha));
        Corners(panel, 22f, 3f, new Color(accent.r, accent.g, accent.b, alpha));

        // typ menovky: skosený štítok s menom nad ľavým horným rohom
        if (!string.IsNullOrEmpty(current.speaker))
        {
            Vector2 sz = speakerStyle.CalcSize(new GUIContent(current.speaker));
            Rect tag = new Rect(panel.x + 20f, panel.y - 18f, sz.x + 48f, 38f);
            Fill(new Rect(tag.x - 2f, tag.y - 2f, tag.width + 4f, tag.height + 4f), new Color(0f, 0f, 0f, alpha));
            Fill(tag, new Color(accent.r * 0.55f, accent.g * 0.55f, accent.b * 0.55f, alpha));
            Fill(new Rect(tag.x, tag.y, 6f, tag.height), new Color(accent.r, accent.g, accent.b, alpha));
            speakerStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);
            GUI.color = Color.white;
            GUI.Label(new Rect(tag.x + 22f, tag.y + 4f, sz.x + 20f, 30f), current.speaker, speakerStyle);
        }

        // portrét (animovaný) vľavo
        float side = ph - 40f;
        Rect portrait = new Rect(panel.x + 26f, panel.y + 22f, side, side);
        bool talking = typed < line.Length;
        GUI.color = new Color(1f, 1f, 1f, alpha);
        DrawPortrait(portrait, accent, talking, t, shout);

        // text
        float textX = portrait.xMax + 32f;
        float textW = panel.xMax - textX - 30f;
        int shown = Mathf.Min(line.Length, Mathf.FloorToInt(typed));
        int prevSize = textStyle.fontSize;
        FontStyle prevFont = textStyle.fontStyle;
        if (shout) { textStyle.fontSize = prevSize + 4; textStyle.fontStyle = FontStyle.Bold; }
        textStyle.normal.textColor = new Color(0.93f, 0.96f, 1f, alpha);
        GUI.color = Color.white;
        Rect textRect = new Rect(textX, panel.y + 26f, textW, panel.height - 64f);
        string revealed = RevealText(line, shown, lineIndex);
        GUI.Label(new Rect(textRect.x + 1.5f, textRect.y + 1.5f, textRect.width, textRect.height), cachedShadow, textStyle);   // tieň textu
        GUI.Label(textRect, revealed, textStyle);
        textStyle.fontSize = prevSize;
        textStyle.fontStyle = prevFont;

        // nápoveda vpravo dole (vľavo prekrývala portrét): tmavý podklad, čitateľný text; šípka vedľa nej, keď je riadok hotový
        string hint = $"E / medzerník / klik – ďalej   ({lineIndex + 1}/{current.lines.Length})";
        float hintW = 330f;
        Rect hintRect = new Rect(panel.xMax - hintW - 22f, panel.yMax - 31f, hintW, 22f);
        Fill(new Rect(hintRect.x - 8f, hintRect.y - 1f, hintRect.width + 14f, hintRect.height + 2f), new Color(0f, 0f, 0f, 0.55f * alpha));
        hintStyle.alignment = TextAnchor.MiddleRight;
        hintStyle.fontSize = 14;
        hintStyle.normal.textColor = new Color(0.82f, 0.86f, 0.93f, alpha);
        GUI.color = Color.white;
        GUI.Label(hintRect, hint, hintStyle);
        if (!talking)
        {
            float bounce = Mathf.Abs(Mathf.Sin(t * 4f)) * 4f;
            hintStyle.alignment = TextAnchor.MiddleCenter;
            hintStyle.normal.textColor = new Color(accent.r, accent.g, accent.b, alpha);
            GUI.Label(new Rect(hintRect.x - 36f, hintRect.y - 2f + bounce - 2f, 26f, 26f), "▼", hintStyle);
        }

        // pásik postupu rozhovoru
        float prog = (lineIndex + Mathf.Clamp01(typed / Mathf.Max(1, line.Length))) / current.lines.Length;
        Fill(new Rect(panel.x + 4f, panel.yMax - 5f, (panel.width - 8f) * prog, 2f), new Color(accent.r, accent.g, accent.b, alpha));

        GUI.color = old;
    }

    // Tieň textu má byť čisto čierny: bez farebných značiek reveal efektu
    static string StripTags(string s)
    {
        if (s.IndexOf('<') < 0) return s;
        var o = new System.Text.StringBuilder(s.Length);
        bool inTag = false;
        foreach (char c in s)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) o.Append(c);
        }
        return o.ToString();
    }
}
