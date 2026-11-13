using System;
using System.Collections.Generic;
using UnityEngine;

public enum PanelType { Narrative, Choice, Qte, Result }

/// <summary>Kde sa smie sekvencia spustiť. Bežný boj tu zámerne nie je: panely sú len pre bossov, dialógy a rozhodnutia.</summary>
public enum PanelContext { Boss, Dialogue, Decision }

/// <summary>Placeholder obraz generovaný kódom (použije sa, ak neexistuje Resources/Panels/&lt;image&gt;).</summary>
public enum PanelArt { Figure, Eye, Blade, Skyline, Fist, Burst }

public enum QteOutcome { None, Perfect, Hit, Missed }
public enum ResultWord { Auto, Bam, Krak, Mimo }

[Serializable]
public class PanelSpec
{
    public PanelType type;
    [Tooltip("Meno PNG v Resources/Panels (bez prípony); prázdne = generovaný placeholder")]
    public string image;
    public PanelArt art = PanelArt.Figure;
    [Tooltip("Stred akcie 0..1; rázové čiary smerujú sem")]
    public Vector2 focus = new Vector2(0.5f, 0.6f);
    public string text;
    public KeyCode key = KeyCode.F;
    public float seconds = 2f;
    public float missDamage = 12f;
    public ResultWord word = ResultWord.Auto;

    public static PanelSpec Narrative(string text, PanelArt art = PanelArt.Figure, string image = null, Vector2? focus = null)
        => new PanelSpec { type = PanelType.Narrative, text = text, art = art, image = image, focus = focus ?? new Vector2(0.5f, 0.6f) };

    public static PanelSpec Choice(string text, PanelArt art = PanelArt.Figure, string image = null)
        => new PanelSpec { type = PanelType.Choice, text = text, art = art, image = image };

    public static PanelSpec Qte(KeyCode key, float seconds = 2f, float missDamage = 12f, string text = null, PanelArt art = PanelArt.Fist)
        => new PanelSpec { type = PanelType.Qte, key = key, seconds = Mathf.Max(0.6f, seconds), missDamage = missDamage, text = text, art = art };

    public static PanelSpec Result(ResultWord word = ResultWord.Auto)
        => new PanelSpec { type = PanelType.Result, word = word, art = PanelArt.Burst };
}

/// <summary>Jedna obrazovka: 2 až 4 panely, ktoré sa objavujú postupne zľava doprava.</summary>
[Serializable]
public class PanelPage
{
    public PanelSpec[] panels;
    public PanelPage(params PanelSpec[] panels) { this.panels = panels; }
}

public class PanelResult
{
    public bool Completed;
    /// <summary>Sekvencia sa neprehrala (panel už v tomto súboji bol, beží bežný boj alebo zlé vstupy).</summary>
    public bool Refused;
    /// <summary>Index zvolenej možnosti (0..2) pre každú stránku s voľbou v poradí.</summary>
    public readonly List<int> Choices = new List<int>();
    public int LastChoice = -1;
    public readonly List<QteOutcome> QteOutcomes = new List<QteOutcome>();
    /// <summary>Posledné QTE: Perfect = dokonalý úder, Hit = bežný zásah, Missed = nestihnuté (hráč dostal zásah, hra pokračuje).</summary>
    public QteOutcome Qte = QteOutcome.None;
    /// <summary>0..1; 1 = stlačené presne na konci kruhu, 0 = hneď na začiatku. Pri Missed je 0.</summary>
    public float Precision;
    /// <summary>Násobok poškodenia hráčovho úderu: Perfect 2, Hit 1, Missed 0.</summary>
    public float DamageMultiplier = 1f;
}

/// <summary>
/// Samostatný systém manga panelov (bossovia, dialógy, rozhodnutia). Volajúci podá stránky panelov cez Play(...) a dostane
/// PanelResult v callbacku. Počas prehrávania je Active = true (hráč sa nehýbe ani neútočí cez Healer.MenuOpen / PlayerMovement),
/// čas sa nezastavuje a hráč je nezraniteľný, aby ho v tej chvíli nikto nezabil. Obraz sa kreslí cez IMGUI z procedurálnych textúr.
/// Pravidlá: jedno fightId = najviac jedna sekvencia (ďalšie volanie je Refused) a nikdy nie počas bežného uhýbania v boji.
/// </summary>
public class MangaPanels : MonoBehaviour
{
    public static bool Active { get; private set; }

    private class Request
    {
        public PanelPage[] pages;
        public Action<PanelResult> done;
        public PanelResult result = new PanelResult();
    }

    private enum Kind { Narrative, Choice, Qte, Result }

    private static readonly HashSet<string> usedFights = new HashSet<string>();
    private static readonly Queue<Request> queue = new Queue<Request>();
    private static MangaPanels instance;

    private const float RevealStep = 0.4f;     // odstup objavenia panelov
    private const float RevealFade = 0.3f;
    private const float QteReadyDelay = 0.35f;
    private const float PerfectBand = 0.2f;     // posledný podiel času QTE = zelené pásmo
    private const float QteFeedback = 0.8f;
    private const float PickFeedback = 0.8f;

    private Request cur;
    private int pageIndex;
    private Kind kind;
    private PanelSpec[] panels;
    private Texture2D[] tex;
    private bool[] ownsTex;
    private Rect[] rects;
    private int choiceCount;
    private int qteIndex;

    private float clock;           // beží len mimo pauzy
    private float pageStart;
    private float revealEnd;
    private int picked;
    private float pickTime;
    private float qteStart;
    private bool qteDone;
    private float qteDoneAt;
    private QteOutcome pageQte;
    private float resultStartAt = -1f;

    private PlayerStats player;
    private bool debugFreeze;      // len na snímky/ladenie: zastaví hodiny panelov
    private GUIStyle caption, badge, keyGlyph, word, hint, tag;
    private Texture2D white, black, disc, ring, halftone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        usedFights.Clear();
        queue.Clear();
        Active = false;
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<MangaPanels>() != null) return;
        new GameObject("MangaPanels").AddComponent<MangaPanels>();
    }

    void Awake()
    {
        instance = this;
        player = FindAnyObjectByType<PlayerStats>();
    }

    // ---------- API ----------

    /// <summary>
    /// Spustí sekvenciu. fightId identifikuje súboj/udalosť: pre rovnaké id sa panely prehrajú najviac raz.
    /// Vráti false a zavolá onDone s Refused = true, ak sa nesmie (už použité id, bežné uhýbanie v boji, zlé vstupy).
    /// </summary>
    public static bool Play(string fightId, PanelContext context, PanelPage[] pages, Action<PanelResult> onDone)
    {
        var refused = new PanelResult { Refused = true };
        if (string.IsNullOrEmpty(fightId) || pages == null || pages.Length == 0 || instance == null || CombatQte.Active)
        {
            Debug.LogWarning("MangaPanels: sekvencia odmietnutá (prázdne vstupy alebo prebieha bežné uhýbanie v boji).");
            onDone?.Invoke(refused);
            return false;
        }
        if (!usedFights.Add(fightId))
        {
            Debug.LogWarning("MangaPanels: panely pre '" + fightId + "' (" + context + ") už v tomto súboji boli.");
            onDone?.Invoke(refused);
            return false;
        }
        queue.Enqueue(new Request { pages = pages, done = onDone });
        return true;
    }

    /// <summary>Pre nový súboj s rovnakým id (napr. opakovanie bossa po načítaní).</summary>
    public static void ForgetFight(string fightId) { usedFights.Remove(fightId); }

    // ---------- Beh ----------

    void Update()
    {
        if (cur == null)
        {
            if (queue.Count == 0 || PauseMenu.AtStartup || PauseMenu.IsOpen || MangaDialog.Active) return;
            cur = queue.Dequeue();
            pageIndex = -1;
            Active = true;
            NextPage();
            return;
        }
        if (PauseMenu.IsOpen || debugFreeze) return;

        clock += Time.unscaledDeltaTime;
        if (player == null) player = FindAnyObjectByType<PlayerStats>();
        if (player != null) player.InvulnerableUntil = Mathf.Max(player.InvulnerableUntil, Time.time + 0.3f);

        float t = clock - pageStart;
        bool revealed = t >= revealEnd;
        bool advance = GameInput.GetKeyDown(KeyCode.E) || GameInput.GetKeyDown(KeyCode.Space) || GameInput.GetKeyDown(KeyCode.Return) || GameInput.GetMouseButtonDown(0);

        switch (kind)
        {
            case Kind.Narrative:
            case Kind.Result:
                if (advance)
                {
                    if (!revealed) pageStart = clock - revealEnd;       // prvé stlačenie ukáže všetko
                    else NextPage();
                }
                else if (kind == Kind.Result && t > revealEnd + 4f) NextPage();
                break;

            case Kind.Choice:
                if (!revealed) break;
                if (picked < 0)
                {
                    for (int i = 0; i < choiceCount; i++)
                        if (GameInput.GetKeyDown(KeyCode.Alpha1 + i) || GameInput.GetKeyDown(KeyCode.Keypad1 + i))
                        {
                            picked = i;
                            pickTime = clock;
                            cur.result.Choices.Add(i);
                            cur.result.LastChoice = i;
                            break;
                        }
                }
                else if (clock - pickTime > PickFeedback) NextPage();
                break;

            case Kind.Qte:
                UpdateQte(t, revealed);
                break;
        }
    }

    void UpdateQte(float t, bool revealed)
    {
        PanelSpec q = panels[qteIndex];
        if (qteDone)
        {
            if (clock - qteDoneAt > QteFeedback) NextPage();
            return;
        }
        if (!revealed) return;
        if (qteStart < 0f) qteStart = clock + QteReadyDelay;
        float el = clock - qteStart;
        if (el < 0f) return;

        if (GameInput.GetKeyDown(q.key))
        {
            float p = Mathf.Clamp01(el / q.seconds);
            ResolveQte(p >= 1f - PerfectBand ? QteOutcome.Perfect : QteOutcome.Hit, p);
        }
        else if (el >= q.seconds) ResolveQte(QteOutcome.Missed, 0f);
    }

    void ResolveQte(QteOutcome o, float precision)
    {
        qteDone = true;
        qteDoneAt = clock;
        pageQte = o;
        PanelResult r = cur.result;
        r.Qte = o;
        r.Precision = precision;
        r.DamageMultiplier = o == QteOutcome.Perfect ? 2f : o == QteOutcome.Hit ? 1f : 0f;
        r.QteOutcomes.Add(o);
        // Nestihnutie = zásah do hráča, nikdy nie smrť (zdravie neklesne pod 1) a hra pokračuje
        if (o == QteOutcome.Missed && player != null && !player.IsDead)
            player.SetHealthScripted(Mathf.Max(1f, player.Health - panels[qteIndex].missDamage));
    }

    void NextPage()
    {
        ReleasePage();
        pageIndex++;
        if (pageIndex >= cur.pages.Length) { Finish(); return; }
        BeginPage(cur.pages[pageIndex]);
    }

    void Finish()
    {
        Request r = cur;
        cur = null;
        Active = false;
        r.result.Completed = true;
        r.done?.Invoke(r.result);
    }

    void BeginPage(PanelPage page)
    {
        var list = new List<PanelSpec>(page.panels);
        if (list.Count > 4) { Debug.LogWarning("MangaPanels: stránka má viac ako 4 panely, zvyšok sa vynechá."); list.RemoveRange(4, list.Count - 4); }
        while (list.Count < 2) list.Add(new PanelSpec { type = PanelType.Narrative, art = PanelArt.Skyline });   // doplnok do 2 panelov
        panels = list.ToArray();

        kind = Kind.Narrative;
        choiceCount = 0;
        qteIndex = -1;
        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i].type == PanelType.Choice) choiceCount++;
            else if (panels[i].type == PanelType.Qte && qteIndex < 0) qteIndex = i;
            else if (panels[i].type == PanelType.Result && kind == Kind.Narrative) kind = Kind.Result;
        }
        if (choiceCount > 3) choiceCount = 3;
        if (qteIndex >= 0) kind = Kind.Qte;
        else if (choiceCount >= 2) kind = Kind.Choice;
        else if (choiceCount == 1) Debug.LogWarning("MangaPanels: voľba potrebuje aspoň 2 panely Choice.");

        EnsureTextures();
        rects = new Rect[panels.Length];
        Rect area = PageArea();
        for (int i = 0; i < panels.Length; i++) rects[i] = PanelRect(area, panels.Length, i);

        tex = new Texture2D[panels.Length];
        ownsTex = new bool[panels.Length];
        for (int i = 0; i < panels.Length; i++)
        {
            PanelSpec s = panels[i];
            Texture2D png = string.IsNullOrEmpty(s.image) ? null : Resources.Load<Texture2D>("Panels/" + s.image);
            if (png != null) { tex[i] = png; continue; }
            float aspect = rects[i].width / Mathf.Max(1f, rects[i].height);
            int h = 240, w = Mathf.Clamp(Mathf.RoundToInt(h * aspect), 120, 600);
            tex[i] = MakePanelTexture(s, w, h, pageIndex * 31 + i * 7 + 3);
            ownsTex[i] = true;
        }

        pageStart = clock;
        revealEnd = (panels.Length - 1) * RevealStep + RevealFade;
        picked = -1;
        qteStart = -1f;
        qteDone = false;
        pageQte = QteOutcome.None;
    }

    void ReleasePage()
    {
        if (tex == null) return;
        for (int i = 0; i < tex.Length; i++) if (ownsTex[i] && tex[i] != null) Destroy(tex[i]);
        tex = null;
    }

    void OnDestroy()
    {
        ReleasePage();
        if (instance == this) { instance = null; Active = false; }
    }

    // ---------- Rozloženie ----------

    static Rect PageArea()
    {
        float sc = Screen.height / 1080f;
        float m = 14f * sc;       // biela medzera okolo
        return new Rect(Screen.width * 0.05f + m, Screen.height * 0.05f + m, Screen.width * 0.9f - 2f * m, Screen.height * 0.84f - 2f * m);
    }

    static Rect PanelRect(Rect area, int n, int i)
    {
        float gap = 16f * Screen.height / 1080f;
        int cols = n <= 3 ? n : 2, rows = n <= 3 ? 1 : 2;
        float cw = (area.width - gap * (cols - 1)) / cols;
        float ch = (area.height - gap * (rows - 1)) / rows;
        return new Rect(area.x + (i % cols) * (cw + gap), area.y + (i / cols) * (ch + gap), cw, ch);
    }

    // ---------- Kreslenie ----------

    void OnGUI()
    {
        GUI.depth = -1000;   // nad HUD a ostatnými IMGUI (platí pre každú udalosť, nielen Repaint)
        if (cur == null || panels == null || Event.current.type != EventType.Repaint) return;
        float sc = Screen.height / 1080f;
        EnsureStyles(sc);

        GUI.color = new Color(0f, 0f, 0f, 0.82f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
        GUI.color = Color.white;

        // Biela stránka s rastrom
        Rect page = new Rect(Screen.width * 0.05f, Screen.height * 0.05f, Screen.width * 0.9f, Screen.height * 0.84f);
        GUI.DrawTexture(page, white);
        GUI.DrawTextureWithTexCoords(page, halftone, new Rect(0, 0, page.width / 24f, page.height / 24f));

        // Rozloženie sa počíta pri každom vykreslení: veľkosť okna sa môže zmeniť počas stránky
        Rect area = PageArea();
        for (int i = 0; i < panels.Length; i++) rects[i] = PanelRect(area, panels.Length, i);

        float t = clock - pageStart;
        for (int i = 0; i < panels.Length; i++) DrawPanel(i, t, sc);

        string h = kind == Kind.Choice ? "Stlač " + ChoiceKeys() + " pre výber"
                 : kind == Kind.Qte ? "Stlač svietiacu klávesu v zelenom pásme" : "E / Medzerník / klik – pokračovať";
        if (kind != Kind.Qte || !qteDone) GUI.Label(new Rect(0, Screen.height * 0.905f, Screen.width, 40f * sc), h, hint);
    }

    string ChoiceKeys() { return choiceCount == 2 ? "1 / 2" : "1 / 2 / 3"; }

    void DrawPanel(int i, float t, float sc)
    {
        float appear = Mathf.Clamp01((t - i * RevealStep) / RevealFade);
        if (appear <= 0f) return;
        float ease = 1f - (1f - appear) * (1f - appear);
        Rect r = rects[i];
        r.x -= (1f - ease) * 70f * sc;
        PanelSpec s = panels[i];

        float dim = 1f;
        bool isPicked = false;
        if (kind == Kind.Choice && picked >= 0)
        {
            int ci = ChoiceOrdinal(i);
            isPicked = ci == picked;
            dim = (ci < 0 || isPicked) ? 1f : Mathf.Lerp(1f, 0.25f, Mathf.Clamp01((clock - pickTime) / 0.25f));
        }

        float frame = 7f * sc;
        GUI.color = new Color(1f, 1f, 1f, ease * dim);
        GUI.DrawTexture(r, isPicked ? white : black);
        if (isPicked) { GUI.color = new Color(1f, 0.85f, 0.1f, 1f); GUI.DrawTexture(r, white); }
        GUI.color = new Color(1f, 1f, 1f, ease * dim);
        Rect inner = new Rect(r.x + frame, r.y + frame, r.width - 2f * frame, r.height - 2f * frame);
        GUI.DrawTexture(inner, tex[i], ScaleMode.StretchToFill);

        // Hover/náznak voľby: číslo
        if (s.type == PanelType.Choice)
        {
            int n = ChoiceOrdinal(i);
            Rect b = new Rect(inner.x, inner.y, 90f * sc, 90f * sc);
            GUI.color = new Color(1f, 1f, 1f, ease * dim);
            GUI.DrawTexture(b, black);
            GUI.Label(b, (n + 1).ToString(), badge);
        }

        if (s.type == PanelType.Qte) DrawQte(inner, s, ease, sc);
        else if (s.type == PanelType.Result) DrawResult(inner, s, t - i * RevealStep, sc);

        // Titulok: biely box s čiernym okrajom dole v paneli
        string text = s.type == PanelType.Qte ? (s.text ?? "Stlač " + KeyName(s.key) + "!") : s.text;
        if (!string.IsNullOrEmpty(text) && s.type != PanelType.Result)
        {
            float bh = Mathf.Min(inner.height * 0.34f, 150f * sc);
            Rect cb = new Rect(inner.x + 12f * sc, inner.yMax - bh - 12f * sc, inner.width - 24f * sc, bh);
            GUI.color = new Color(0f, 0f, 0f, ease * dim);
            GUI.DrawTexture(cb, white);
            GUI.color = new Color(1f, 1f, 1f, ease * dim);
            GUI.DrawTexture(new Rect(cb.x + 3f * sc, cb.y + 3f * sc, cb.width - 6f * sc, cb.height - 6f * sc), white);
            GUI.color = new Color(1f, 1f, 1f, ease * dim);
            GUI.Label(new Rect(cb.x + 10f * sc, cb.y, cb.width - 20f * sc, cb.height), text, caption);
        }

        // Biely záblesk pri objavení
        if (appear < 1f)
        {
            GUI.color = new Color(1f, 1f, 1f, (1f - appear) * 0.9f);
            GUI.DrawTexture(r, white);
        }
        GUI.color = Color.white;
    }

    int ChoiceOrdinal(int panelIndex)
    {
        int n = 0;
        for (int i = 0; i <= panelIndex; i++)
        {
            if (panels[i].type != PanelType.Choice) continue;
            if (i == panelIndex) return n < 3 ? n : -1;
            n++;
        }
        return -1;
    }

    void DrawQte(Rect inner, PanelSpec s, float alpha, float sc)
    {
        Vector2 c = inner.center;
        float minDim = Mathf.Min(inner.width, inner.height);
        float r1 = 0.17f * minDim, r0 = 0.47f * minDim;
        float rb = r1 + (r0 - r1) * PerfectBand;

        float el = qteStart < 0f ? -1f : clock - qteStart;
        float p = qteDone ? 1f : Mathf.Clamp01(el / s.seconds);
        float pulse = 0.5f + 0.5f * Mathf.Sin(clock * 9f);

        // Zelené pásmo = dokonalý úder; pod ním tmavý terč s kľúčovou klávesou
        GUI.color = new Color(0.2f, 0.95f, 0.35f, 0.9f * alpha);
        DrawDisc(c, rb);
        GUI.color = new Color(1f, 0.9f, 0.2f, (0.35f + 0.4f * pulse) * alpha);
        DrawDisc(c, r1 * (1.35f + 0.1f * pulse));
        GUI.color = new Color(0.04f, 0.04f, 0.06f, alpha);
        DrawDisc(c, r1);
        GUI.color = new Color(1f, 1f, 1f, alpha);
        DrawRing(c, r1);

        // Zmenšujúci sa kruh
        if (el >= 0f && !qteDone)
        {
            float rad = Mathf.Lerp(r0, r1, p);
            bool inBand = p >= 1f - PerfectBand;
            GUI.color = inBand ? new Color(0.5f, 1f, 0.6f, alpha) : new Color(1f, 1f, 1f, alpha);
            DrawRing(c, rad);
        }
        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(c.x - r1, c.y - r1, 2f * r1, 2f * r1), KeyName(s.key), keyGlyph);

        if (qteDone)
        {
            string msg = pageQte == QteOutcome.Perfect ? "DOKONALÉ!" : pageQte == QteOutcome.Hit ? "ZÁSAH" : "NESTIHOL SI – zásah";
            Color col = pageQte == QteOutcome.Perfect ? new Color(0.3f, 1f, 0.4f) : pageQte == QteOutcome.Hit ? Color.white : new Color(1f, 0.4f, 0.3f);
            Rect tr = new Rect(inner.x, inner.y + 10f * sc, inner.width, 80f * sc);
            DrawOutlined(tr, msg, tag, col, sc);
        }
    }

    void DrawResult(Rect inner, PanelSpec s, float since, float sc)
    {
        ResultWord w = s.word;
        if (w == ResultWord.Auto)
        {
            QteOutcome q = cur.result.Qte;
            w = q == QteOutcome.Perfect ? ResultWord.Bam : q == QteOutcome.Missed ? ResultWord.Mimo : q == QteOutcome.None ? ResultWord.Bam : ResultWord.Krak;
        }
        string txt = w == ResultWord.Bam ? "BAM!" : w == ResultWord.Krak ? "KRAK!" : "MIMO!";
        Color col = w == ResultWord.Bam ? new Color(1f, 0.25f, 0.15f) : w == ResultWord.Krak ? new Color(1f, 0.85f, 0.1f) : new Color(0.7f, 0.85f, 1f);

        float k = Mathf.Clamp01(since / 0.3f);
        float scale = Mathf.Lerp(2.6f, 1f, 1f - (1f - k) * (1f - k));
        float shake = since < 0.6f ? Mathf.Sin(since * 90f) * 6f * sc * (1f - since / 0.6f) : 0f;
        Matrix4x4 m = GUI.matrix;
        Vector2 c = inner.center + new Vector2(shake, 0f);
        GUIUtility.RotateAroundPivot(-8f, c);
        GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), c);
        DrawOutlined(new Rect(c.x - inner.width, c.y - 120f * sc, inner.width * 2f, 240f * sc), txt, word, col, sc);
        GUI.matrix = m;
    }

    void DrawOutlined(Rect r, string text, GUIStyle st, Color fill, float sc)
    {
        float o = Mathf.Max(2f, st.fontSize * 0.06f);
        Color prev = GUI.color;
        GUI.color = Color.black;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            Rect rr = r;
            rr.x += Mathf.Cos(a) * o;
            rr.y += Mathf.Sin(a) * o;
            GUI.Label(rr, text, st);
        }
        GUI.color = fill;
        GUI.Label(r, text, st);
        GUI.color = prev;
    }

    void DrawDisc(Vector2 c, float rad) { GUI.DrawTexture(new Rect(c.x - rad, c.y - rad, 2f * rad, 2f * rad), disc); }
    void DrawRing(Vector2 c, float rad) { GUI.DrawTexture(new Rect(c.x - rad, c.y - rad, 2f * rad, 2f * rad), ring); }

    static string KeyName(KeyCode k)
    {
        if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
        if (k == KeyCode.Space) return "␣";
        return k.ToString();
    }

    void EnsureStyles(float sc)
    {
        if (caption == null)
        {
            caption = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
            caption.normal.textColor = Color.black;
            badge = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            badge.normal.textColor = Color.white;
            keyGlyph = new GUIStyle(badge);
            word = new GUIStyle(badge) { wordWrap = false };
            tag = new GUIStyle(badge);
            hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            hint.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
        }
        caption.fontSize = Mathf.RoundToInt(30f * sc);
        badge.fontSize = Mathf.RoundToInt(64f * sc);
        keyGlyph.fontSize = Mathf.RoundToInt(70f * sc);
        word.fontSize = Mathf.RoundToInt(190f * sc);
        tag.fontSize = Mathf.RoundToInt(54f * sc);
        hint.fontSize = Mathf.RoundToInt(26f * sc);
    }

    // ---------- Procedurálne textúry ----------

    void EnsureTextures()
    {
        if (white != null) return;
        white = Solid(new Color32(255, 255, 255, 255));
        black = Solid(new Color32(10, 10, 12, 255));

        const int n = 128;
        var d = new Color32[n * n];
        var rg = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float r = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f)) / 64f;
                byte a = (byte)(255f * Mathf.Clamp01((0.98f - r) * 40f));
                d[y * n + x] = new Color32(255, 255, 255, a);
                // čierno-biely-čierny pás, aby kruh bolo vidieť na rastri aj lúčoch
                Color32 c;
                if (r < 0.78f || r > 1f) c = new Color32(0, 0, 0, 0);
                else if (r < 0.84f || r > 0.94f) c = new Color32(0, 0, 0, 255);
                else c = new Color32(255, 255, 255, 255);
                rg[y * n + x] = c;
            }
        disc = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        disc.SetPixels32(d); disc.Apply(false, true);
        ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        ring.SetPixels32(rg); ring.Apply(false, true);

        // Dlaždica rastra (bodky), kreslí sa opakovane
        const int tn = 24;
        var h = new Color32[tn * tn];
        for (int y = 0; y < tn; y++)
            for (int x = 0; x < tn; x++)
            {
                float fx = (x % 12) - 5.5f, fy = (y % 12) - 5.5f;
                bool on = fx * fx + fy * fy < 6f;
                h[y * tn + x] = on ? new Color32(205, 205, 210, 255) : new Color32(255, 255, 255, 255);
            }
        halftone = new Texture2D(tn, tn, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        halftone.SetPixels32(h); halftone.Apply(false, true);
    }

    static Texture2D Solid(Color32 c)
    {
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c); t.Apply(false, true);
        return t;
    }

    static float Hash01(int n)
    {
        unchecked
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    static float SdCircle(float px, float py, float cx, float cy, float r) { return Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - r; }

    static float SdBox(float px, float py, float cx, float cy, float hx, float hy)
    {
        float qx = Mathf.Abs(px - cx) - hx, qy = Mathf.Abs(py - cy) - hy;
        float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
        return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f);
    }

    /// <summary>Raster, rázové čiary smerujúce do stredu akcie a obrázok z primitív; vstupom je len PanelSpec.</summary>
    static Texture2D MakePanelTexture(PanelSpec s, int w, int h, int seed)
    {
        var buf = new Color32[w * h];
        float fx = s.focus.x * w, fy = s.focus.y * h;
        float rmin = 0.26f * h, rspan = 0.8f * Mathf.Max(w, h);
        const int rays = 84;
        const float cell = 7f;
        const float cs = 0.7071f;
        Color32 paper = new Color32(247, 244, 236, 255);
        Color32 dot = new Color32(80, 80, 90, 255);
        Color32 ink = new Color32(10, 10, 12, 255);
        Color32 pure = new Color32(252, 252, 252, 255);
        float maxDim = Mathf.Max(w, h);

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = x - fx, dy = y - fy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                Color32 c = paper;

                // raster: bodky rastú smerom od stredu
                float gx = (x * cs + y * cs) / cell, gy = (-x * cs + y * cs) / cell;
                float ux = gx - Mathf.Floor(gx) - 0.5f, uy = gy - Mathf.Floor(gy) - 0.5f;
                if (Mathf.Sqrt(ux * ux + uy * uy) < Mathf.Clamp01(dist / (0.9f * maxDim)) * 0.6f) c = dot;

                // rázové čiary
                if (dist > rmin)
                {
                    float rf = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f) * rays;
                    int idx = (int)rf;
                    float hs = Hash01(idx * 7 + seed);
                    if (hs > 0.4f && Mathf.Abs(rf - idx - 0.5f) < Mathf.Clamp01((dist - rmin) / rspan) * (0.12f + 0.4f * hs)) c = ink;
                }

                // obrázok z primitív
                float px = dx / h, py = dy / h;
                float sd = 1f;
                switch (s.art)
                {
                    case PanelArt.Figure:
                        sd = Mathf.Min(SdCircle(px, py, 0f, 0.14f, 0.085f), Mathf.Min(SdBox(px, py, 0f, -0.02f, 0.15f, 0.06f), SdBox(px, py, 0f, -0.2f, 0.1f, 0.16f)));
                        break;
                    case PanelArt.Fist:
                        sd = Mathf.Min(SdBox(px, py, 0f, -0.04f, 0.1f, 0.09f), SdCircle(px, py, -0.11f, -0.02f, 0.04f));
                        for (int k = 0; k < 4; k++) sd = Mathf.Min(sd, SdCircle(px, py, -0.075f + 0.05f * k, 0.07f, 0.035f));
                        break;
                    case PanelArt.Blade:
                    {
                        const float cr = 0.819f, sr = 0.574f;   // 35°
                        float qx = px * cr + py * sr, qy = -px * sr + py * cr;
                        sd = Mathf.Min(SdBox(qx, qy, 0f, 0f, 0.4f, 0.022f), Mathf.Min(SdBox(qx, qy, -0.4f, 0f, 0.02f, 0.07f), SdBox(qx, qy, -0.5f, 0f, 0.1f, 0.02f)));
                        break;
                    }
                    case PanelArt.Skyline:
                        for (int k = 0; k < 7; k++)
                        {
                            float bh = 0.12f + 0.26f * Hash01(k * 13 + seed);
                            sd = Mathf.Min(sd, SdBox(px, py, -0.42f + 0.14f * k, -0.45f + bh * 0.5f, 0.06f, bh * 0.5f));
                        }
                        break;
                    case PanelArt.Eye:
                    {
                        float a = Mathf.Max(SdCircle(px, py, 0f, -0.22f, 0.36f), SdCircle(px, py, 0f, 0.22f, 0.36f));
                        if (Mathf.Abs(a) < 0.012f) c = ink;
                        else if (a < 0f)
                        {
                            c = pure;
                            if (SdCircle(px, py, 0f, 0f, 0.1f) < 0f) c = SdCircle(px, py, 0.03f, 0.03f, 0.025f) < 0f ? pure : ink;
                        }
                        break;
                    }
                    case PanelArt.Burst:
                    {
                        float k = Mathf.Repeat((Mathf.Atan2(py, px) / (2f * Mathf.PI) + 0.5f) * 14f, 1f);
                        float r = 0.2f + 0.14f * Mathf.Abs(k - 0.5f) * 2f;
                        float d = Mathf.Sqrt(px * px + py * py);
                        if (d < r) c = new Color32(255, 232, 70, 255);
                        else if (d < r + 0.014f) c = ink;
                        break;
                    }
                }
                if (s.art != PanelArt.Eye && s.art != PanelArt.Burst)
                {
                    if (sd < 0f) c = ink;
                    else if (sd < 0.014f) c = pure;
                }
                buf[y * w + x] = c;
            }

        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        t.SetPixels32(buf);
        t.Apply(false, true);
        return t;
    }
}
