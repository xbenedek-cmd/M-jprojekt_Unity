using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Segmentovaný pruh HUD-u (HP, výdrž, hlad/krv): šikmé dieliky, doznievajúca strata a voliteľná značka prahu.</summary>
public class HudSegmentBar : VisualElement
{
    public int segments = 20;
    public float skew = 5f;
    public float gap = 2f;
    public Color fill = Color.white;
    public Color back = new Color(1f, 1f, 1f, 0.1f);
    public Color ghostColor = new Color(1f, 1f, 1f, 0.75f);
    /// <summary>Značka prahu 0..1 (napr. hranica slabnutia); záporná = bez značky.</summary>
    public float tick = -1f;

    private float value = 1f, ghost = 1f, glow;

    public HudSegmentBar()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    /// <summary>Nastaví hodnoty; prekreslí sa len pri viditeľnej zmene.</summary>
    public void Set(float newValue, float newGhost, float newGlow)
    {
        newValue = Mathf.Clamp01(newValue);
        newGhost = Mathf.Clamp01(newGhost);
        if (Mathf.Abs(newValue - value) < 0.002f && Mathf.Abs(newGhost - ghost) < 0.002f && Mathf.Abs(newGlow - glow) < 0.02f) return;
        value = newValue;
        ghost = newGhost;
        glow = newGlow;
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        Rect r = contentRect;
        if (r.width < 4f || r.height < 1f || segments < 1) return;

        Painter2D p = context.painter2D;
        float usable = r.width - skew - gap * (segments - 1);
        float w = usable / segments;
        if (w <= 0.5f) return;

        Color lit = Color.Lerp(fill, Color.white, glow * 0.55f);
        float filled = value * segments;
        float ghosted = Mathf.Max(ghost, value) * segments;

        for (int i = 0; i < segments; i++)
        {
            float x = r.x + i * (w + gap);
            Quad(p, x, r.y, w, r.height, 1f, back);

            float ghostPart = Mathf.Clamp01(ghosted - i);
            float part = Mathf.Clamp01(filled - i);
            if (ghostPart > part + 0.01f) Quad(p, x, r.y, w, r.height, ghostPart, ghostColor);
            if (part > 0.01f) Quad(p, x, r.y, w, r.height, part, lit);
        }

        if (tick > 0f && tick < 1f)
        {
            float x = r.x + tick * (r.width - skew) + skew * 0.5f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.8f);
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(x + skew * 0.5f, r.y - 2f));
            p.LineTo(new Vector2(x - skew * 0.5f, r.yMax + 2f));
            p.Stroke();
        }
    }

    // Šikmý dielik; part < 1 ho skráti sprava (posledný, čiastočne plný)
    void Quad(Painter2D p, float x, float y, float w, float h, float part, Color color)
    {
        float ww = w * part;
        p.fillColor = color;
        p.BeginPath();
        p.MoveTo(new Vector2(x + skew, y));
        p.LineTo(new Vector2(x + skew + ww, y));
        p.LineTo(new Vector2(x + ww, y + h));
        p.LineTo(new Vector2(x, y + h));
        p.ClosePath();
        p.Fill();
    }
}

/// <summary>Rám panelu so zrezanými rohmi, neónovou linkou a akcentom; v démonickej téme s rozpadnutým okrajom.</summary>
public class HudFrame : VisualElement
{
    public Color background = new Color(0.02f, 0.05f, 0.08f, 0.72f);
    public Color line = new Color(0.35f, 0.85f, 1f, 0.9f);
    public float cut = 14f;
    /// <summary>0 = čisté rovné hrany (človek), 1 = roztrhaný okraj (démon).</summary>
    public float torn;
    public int seed = 3;

    public HudFrame()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    public void Apply(Color newBackground, Color newLine, float newTorn)
    {
        background = newBackground;
        line = newLine;
        torn = newTorn;
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        Rect r = contentRect;
        if (r.width < cut * 2f || r.height < cut * 2f) return;
        Painter2D p = context.painter2D;

        p.fillColor = background;
        Outline(p, r, true);
        p.Fill();

        p.strokeColor = line;
        p.lineWidth = 1.4f;
        p.lineJoin = LineJoin.Miter;
        Outline(p, r, true);
        p.Stroke();

        // Akcent: hrubší pásik vľavo hore a dve značky pri zrezanom rohu
        p.lineWidth = 3f;
        p.BeginPath();
        p.MoveTo(new Vector2(r.x, r.y + 6f));
        p.LineTo(new Vector2(r.x, r.y + Mathf.Min(46f, r.height * 0.4f)));
        p.Stroke();

        if (torn <= 0.01f)
        {
            p.lineWidth = 1.4f;
            for (int i = 0; i < 3; i++)
            {
                float x = r.xMax - cut - 10f - i * 7f;
                p.BeginPath();
                p.MoveTo(new Vector2(x, r.y + 4f));
                p.LineTo(new Vector2(x - 4f, r.y + 9f));
                p.Stroke();
            }
            return;
        }

        // Démon: žiara okolo okraja, stopy pazúrov a krv stekajúca zo spodnej hrany
        p.strokeColor = new Color(line.r, line.g, line.b, 0.16f * torn);
        p.lineWidth = 6f;
        Outline(p, r, true);
        p.Stroke();

        p.strokeColor = line;
        p.lineCap = LineCap.Round;
        for (int i = 0; i < 3; i++)
        {
            float x = r.xMax - cut - 8f - i * 8f;
            p.lineWidth = 2.2f - i * 0.4f;
            p.BeginPath();
            p.MoveTo(new Vector2(x, r.y + 3f));
            p.LineTo(new Vector2(x - 9f, r.y + 15f - i * 2f));
            p.Stroke();
        }
        p.lineCap = LineCap.Butt;

        if (r.width < 120f) return;
        p.fillColor = new Color(line.r * 0.8f, line.g * 0.6f, line.b * 0.6f, 0.9f);
        int drips = Mathf.Clamp(Mathf.RoundToInt(r.width / 70f), 2, 6);
        for (int i = 0; i < drips; i++)
        {
            float x = r.x + cut + 10f + Hash(i * 31 + seed * 17) * (r.width - cut - 30f);
            float length = 5f + Hash(i * 13 + seed * 5) * 11f;
            float half = 1.6f + Hash(i * 3 + seed) * 1.6f;
            p.BeginPath();
            p.MoveTo(new Vector2(x - half, r.yMax - 1f));
            p.LineTo(new Vector2(x + half, r.yMax - 1f));
            p.LineTo(new Vector2(x + half * 0.5f, r.yMax + length * 0.7f));
            p.LineTo(new Vector2(x, r.yMax + length));
            p.LineTo(new Vector2(x - half * 0.5f, r.yMax + length * 0.7f));
            p.ClosePath();
            p.Fill();
        }
    }

    /// <summary>Zmení len farbu linky (pulz pri nedostatku krvi); prekreslí sa iba pri viditeľnej zmene.</summary>
    public void SetLine(Color newLine)
    {
        if (Mathf.Abs(newLine.r - line.r) + Mathf.Abs(newLine.g - line.g) + Mathf.Abs(newLine.b - line.b) < 0.04f) return;
        line = newLine;
        MarkDirtyRepaint();
    }

    // Obrys v smere hodinových ručičiek: zrezaný pravý horný a ľavý dolný roh; pri torn > 0 sú dlhé hrany zubaté
    void Outline(Painter2D p, Rect r, bool close)
    {
        p.BeginPath();
        p.MoveTo(new Vector2(r.x, r.y));
        Edge(p, new Vector2(r.x, r.y), new Vector2(r.xMax - cut, r.y), 1);
        p.LineTo(new Vector2(r.xMax, r.y + cut));
        Edge(p, new Vector2(r.xMax, r.y + cut), new Vector2(r.xMax, r.yMax), 2);
        Edge(p, new Vector2(r.xMax, r.yMax), new Vector2(r.x + cut, r.yMax), 3);
        p.LineTo(new Vector2(r.x, r.yMax - cut));
        Edge(p, new Vector2(r.x, r.yMax - cut), new Vector2(r.x, r.y), 4);
        if (close) p.ClosePath();
    }

    void Edge(Painter2D p, Vector2 from, Vector2 to, int salt)
    {
        if (torn > 0.01f)
        {
            Vector2 dir = to - from;
            float length = dir.magnitude;
            Vector2 normal = new Vector2(-dir.y, dir.x) / Mathf.Max(0.001f, length);
            int steps = Mathf.Max(2, Mathf.RoundToInt(length / 9f));
            for (int i = 1; i < steps; i++)
            {
                float u = i / (float)steps;
                // Zuby smerujú dovnútra panelu, aby okraj vyzeral odhryznutý
                float bite = Mathf.Abs(Hash(i * 7 + salt * 131 + seed)) * 5.5f * torn * ((i & 1) == 0 ? 1f : 0.25f);
                p.LineTo(from + dir * u + normal * bite);
            }
        }
        p.LineTo(to);
    }

    static float Hash(int n)
    {
        return Mathf.Repeat(Mathf.Sin(n * 12.9898f) * 43758.5453f, 1f);
    }
}

/// <summary>Kruhový ukazovateľ cooldownu schopnosti: oblúk sa dopĺňa v smere hodín, po nabití krátko zažiari.</summary>
public class HudCooldownRing : VisualElement
{
    private float progress = 1f, flash;
    private bool usable = true;
    private Color line = Color.white, dimColor = new Color(1f, 1f, 1f, 0.2f), ready = new Color(0.5f, 1f, 0.6f);

    public HudCooldownRing()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    public void Colors(Color lineColor, Color readyColor)
    {
        line = lineColor;
        ready = readyColor;
        dimColor = new Color(lineColor.r, lineColor.g, lineColor.b, 0.22f);
        MarkDirtyRepaint();
    }

    /// <summary>progress 0..1 (1 = nabitá), usable = schopnosť patrí k forme hráča, flash 0..1 = záblesk po nabití.</summary>
    public void Set(float newProgress, bool newUsable, float newFlash)
    {
        newProgress = Mathf.Clamp01(newProgress);
        if (Mathf.Abs(newProgress - progress) < 0.004f && newUsable == usable && Mathf.Abs(newFlash - flash) < 0.02f) return;
        progress = newProgress;
        usable = newUsable;
        flash = newFlash;
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        Rect r = contentRect;
        float size = Mathf.Min(r.width, r.height);
        if (size < 10f) return;
        Painter2D p = context.painter2D;
        Vector2 c = r.center;
        float radius = size * 0.5f - 3f;
        bool charged = usable && progress >= 0.999f;

        // Tmavý stred, aby bol kláves čitateľný na každom pozadí
        p.fillColor = new Color(0f, 0f, 0f, 0.45f);
        p.BeginPath();
        p.Arc(c, radius, 0f, 360f);
        p.Fill();

        p.lineCap = LineCap.Butt;
        p.strokeColor = dimColor;
        p.lineWidth = 3.5f;
        p.BeginPath();
        p.Arc(c, radius, 0f, 360f);
        p.Stroke();

        if (!usable) return;

        p.strokeColor = charged ? Color.Lerp(ready, Color.white, flash) : line;
        p.lineWidth = charged ? 3.5f + flash * 2.5f : 3.5f;
        p.BeginPath();
        if (charged) p.Arc(c, radius, 0f, 360f);
        else if (progress > 0.004f) p.Arc(c, radius, -90f, -90f + 360f * progress);
        p.Stroke();

        // Štyri značky po obvode (štvrtiny cooldownu)
        p.strokeColor = new Color(0f, 0f, 0f, 0.7f);
        p.lineWidth = 1.5f;
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            p.BeginPath();
            p.MoveTo(c + d * (radius - 2.5f));
            p.LineTo(c + d * (radius + 2.5f));
            p.Stroke();
        }
    }
}

/// <summary>Ikona fázy dňa pri hodinách: slnko, mesiac, alebo slnko na obzore (úsvit, súmrak).</summary>
public class HudClockIcon : VisualElement
{
    private int phase = -1;
    private Color color = Color.white;

    public HudClockIcon()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    public void Set(DayNightCycle.DayPhase newPhase, Color newColor)
    {
        if ((int)newPhase == phase && newColor == color) return;
        phase = (int)newPhase;
        color = newColor;
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        Rect r = contentRect;
        float size = Mathf.Min(r.width, r.height);
        if (size < 6f || phase < 0) return;
        Painter2D p = context.painter2D;
        Vector2 c = r.center;
        float h = size * 0.5f;
        p.fillColor = color;
        p.strokeColor = color;
        p.lineWidth = 1.4f;

        if (phase == (int)DayNightCycle.DayPhase.Night)
        {
            // Kosáčik: vonkajší oblúk a plytší vnútorný
            p.BeginPath();
            p.Arc(c, h * 0.8f, 50f, 310f);
            p.Arc(c + new Vector2(h * 0.42f, 0f), h * 0.62f, 262f, 98f, ArcDirection.CounterClockwise);
            p.ClosePath();
            p.Fill();
            return;
        }

        bool horizon = phase != (int)DayNightCycle.DayPhase.Day;
        Vector2 sun = horizon ? c + new Vector2(0f, h * 0.35f) : c;
        float radius = h * 0.38f;
        p.BeginPath();
        if (horizon) { p.Arc(sun, radius, 180f, 360f); p.ClosePath(); }
        else p.Arc(sun, radius, 0f, 360f);
        p.Fill();

        int rays = horizon ? 5 : 8;
        for (int i = 0; i < rays; i++)
        {
            float a = (horizon ? 180f + i * 45f : i * 45f) * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            p.BeginPath();
            p.MoveTo(sun + d * (radius + 2f));
            p.LineTo(sun + d * (h * 0.92f));
            p.Stroke();
        }
        if (horizon)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(r.x, sun.y + 2f));
            p.LineTo(new Vector2(r.xMax, sun.y + 2f));
            p.Stroke();
        }
    }
}

/// <summary>Pásik dňa pod hodinami: noc, úsvit, deň, súmrak a značka aktuálneho času.</summary>
public class HudDayBar : VisualElement
{
    private float dawn = 5f, day = 7f, dusk = 18f, night = 20f, time = -1f;
    private Color marker = Color.white;

    static readonly Color NightColor = new Color(0.16f, 0.2f, 0.5f, 0.9f), DawnColor = new Color(0.9f, 0.55f, 0.3f, 0.9f),
                          DayColor = new Color(0.95f, 0.85f, 0.4f, 0.9f), DuskColor = new Color(0.8f, 0.4f, 0.35f, 0.9f);

    public HudDayBar()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    public void Set(float hour, float dawnHour, float dayHour, float duskHour, float nightHour, Color markerColor)
    {
        if (Mathf.Abs(hour - time) < 0.05f && markerColor == marker && dawnHour == dawn && nightHour == night) return;
        time = hour; dawn = dawnHour; day = dayHour; dusk = duskHour; night = nightHour; marker = markerColor;
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        Rect r = contentRect;
        if (r.width < 8f || r.height < 1f || time < 0f) return;
        Painter2D p = context.painter2D;
        Part(p, r, 0f, dawn, NightColor);
        Part(p, r, dawn, day, DawnColor);
        Part(p, r, day, dusk, DayColor);
        Part(p, r, dusk, night, DuskColor);
        Part(p, r, night, 24f, NightColor);

        float x = r.x + r.width * Mathf.Clamp01(time / 24f);
        p.fillColor = marker;
        p.BeginPath();
        p.MoveTo(new Vector2(x - 3.5f, r.y - 4f));
        p.LineTo(new Vector2(x + 3.5f, r.y - 4f));
        p.LineTo(new Vector2(x, r.y + 1f));
        p.ClosePath();
        p.Fill();
        p.BeginPath();
        p.MoveTo(new Vector2(x - 1f, r.y));
        p.LineTo(new Vector2(x + 1f, r.y));
        p.LineTo(new Vector2(x + 1f, r.yMax + 1f));
        p.LineTo(new Vector2(x - 1f, r.yMax + 1f));
        p.ClosePath();
        p.Fill();
    }

    static void Part(Painter2D p, Rect r, float from, float to, Color color)
    {
        if (to <= from) return;
        float a = r.x + r.width * (from / 24f), b = r.x + r.width * (to / 24f);
        p.fillColor = color;
        p.BeginPath();
        p.MoveTo(new Vector2(a, r.y));
        p.LineTo(new Vector2(b, r.y));
        p.LineTo(new Vector2(b, r.yMax));
        p.LineTo(new Vector2(a, r.yMax));
        p.ClosePath();
        p.Fill();
    }
}

/// <summary>Ikona formy hráča: človek = čistá silueta v šesťuholníku, démon = rohatá lebka v ostrom ráme.</summary>
public class HudFormIcon : VisualElement
{
    private bool demon;
    private Color line = Color.white, fillColor = new Color(1f, 1f, 1f, 0.15f);
    private float pulse;

    public HudFormIcon()
    {
        pickingMode = PickingMode.Ignore;
        generateVisualContent += Draw;
    }

    public void Set(bool isDemon, Color lineColor, Color bodyColor, float newPulse)
    {
        if (isDemon == demon && lineColor == line && bodyColor == fillColor && Mathf.Abs(newPulse - pulse) < 0.02f) return;
        demon = isDemon;
        line = lineColor;
        fillColor = bodyColor;
        pulse = newPulse;
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        Rect r = contentRect;
        float size = Mathf.Min(r.width, r.height);
        if (size < 8f) return;
        Painter2D p = context.painter2D;
        Vector2 c = r.center;
        float h = size * 0.5f;

        Color glow = Color.Lerp(line, Color.white, pulse * 0.5f);
        p.strokeColor = glow;
        p.lineWidth = 1.6f;
        p.lineJoin = LineJoin.Miter;
        p.fillColor = new Color(fillColor.r, fillColor.g, fillColor.b, fillColor.a * 0.35f);

        if (!demon)
        {
            // Šesťuholník
            p.BeginPath();
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 60f - 90f) * Mathf.Deg2Rad;
                Vector2 v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (h - 1f);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();
            p.Stroke();

            // Hlava a plecia
            p.fillColor = glow;
            p.BeginPath();
            p.Arc(c + new Vector2(0f, -h * 0.2f), h * 0.2f, 0f, 360f);
            p.Fill();
            p.BeginPath();
            p.MoveTo(c + new Vector2(-h * 0.42f, h * 0.5f));
            p.LineTo(c + new Vector2(-h * 0.26f, h * 0.1f));
            p.LineTo(c + new Vector2(h * 0.26f, h * 0.1f));
            p.LineTo(c + new Vector2(h * 0.42f, h * 0.5f));
            p.ClosePath();
            p.Fill();
        }
        else
        {
            // Ostrý kosoštvorec s výbežkami
            p.BeginPath();
            p.MoveTo(c + new Vector2(0f, -h + 1f));
            p.LineTo(c + new Vector2(h * 0.45f, -h * 0.4f));
            p.LineTo(c + new Vector2(h - 1f, 0f));
            p.LineTo(c + new Vector2(h * 0.4f, h * 0.45f));
            p.LineTo(c + new Vector2(0f, h - 1f));
            p.LineTo(c + new Vector2(-h * 0.4f, h * 0.45f));
            p.LineTo(c + new Vector2(-h + 1f, 0f));
            p.LineTo(c + new Vector2(-h * 0.45f, -h * 0.4f));
            p.ClosePath();
            p.Fill();
            p.Stroke();

            // Lebka s rohmi
            p.fillColor = glow;
            p.BeginPath();
            p.MoveTo(c + new Vector2(-h * 0.26f, -h * 0.18f));
            p.LineTo(c + new Vector2(h * 0.26f, -h * 0.18f));
            p.LineTo(c + new Vector2(h * 0.3f, h * 0.12f));
            p.LineTo(c + new Vector2(h * 0.12f, h * 0.42f));
            p.LineTo(c + new Vector2(-h * 0.12f, h * 0.42f));
            p.LineTo(c + new Vector2(-h * 0.3f, h * 0.12f));
            p.ClosePath();
            p.Fill();
            Horn(p, c, h, -1f);
            Horn(p, c, h, 1f);

            // Oči: tmavé štrbiny
            p.fillColor = new Color(0.05f, 0f, 0f, 0.95f);
            Eye(p, c, h, -1f);
            Eye(p, c, h, 1f);
        }
    }

    static void Horn(Painter2D p, Vector2 c, float h, float side)
    {
        p.BeginPath();
        p.MoveTo(c + new Vector2(side * h * 0.16f, -h * 0.18f));
        p.LineTo(c + new Vector2(side * h * 0.3f, -h * 0.18f));
        p.LineTo(c + new Vector2(side * h * 0.44f, -h * 0.62f));
        p.ClosePath();
        p.Fill();
    }

    static void Eye(Painter2D p, Vector2 c, float h, float side)
    {
        p.BeginPath();
        p.MoveTo(c + new Vector2(side * h * 0.06f, h * 0.06f));
        p.LineTo(c + new Vector2(side * h * 0.22f, -h * 0.04f));
        p.LineTo(c + new Vector2(side * h * 0.2f, h * 0.1f));
        p.ClosePath();
        p.Fill();
    }
}
