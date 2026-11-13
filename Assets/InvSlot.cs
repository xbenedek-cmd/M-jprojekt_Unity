using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Jedno miesto v okne inventára (UI Toolkit): rámček so zrezanými rohmi v štýle HUD-u a vektorová ikona predmetu
/// (meč, fľaša, jedlo, prilba, vesta, nohavice, čižma, surovina) vo farbe predmetu. Nič sa nenačítava zo súborov.
/// </summary>
public class InvSlot : VisualElement
{
    private enum Icon { None, Sword, Flask, Food, Head, Top, Bottom, Boots, Gem, Log, Leaf, Box }

    public int kind, index;
    public ItemDef def;
    public Color border = Color.white, back = new Color(0f, 0f, 0f, 0.5f), tint = Color.clear;
    public bool hovered, faded;
    public float cut = 7f;
    /// <summary>Výška pásu s názvom dole (0 = miesto bez názvu).</summary>
    public float nameHeight;

    public readonly Label count, star, title, key;

    public InvSlot(int kind, int index, bool withName)
    {
        this.kind = kind;
        this.index = index;
        nameHeight = withName ? 20f : 0f;
        generateVisualContent += Draw;

        count = Text(11, TextAnchor.LowerRight);
        count.style.right = 4; count.style.bottom = nameHeight + 1f;
        star = Text(12, TextAnchor.UpperLeft);
        star.style.left = 4; star.style.top = 1;
        star.text = "★";
        star.style.color = new Color(1f, 0.82f, 0.3f);
        key = Text(11, TextAnchor.UpperRight);
        key.style.right = 5; key.style.top = 2;
        title = Text(10, TextAnchor.LowerCenter);
        title.style.left = 2; title.style.right = 2; title.style.bottom = 2;
        title.style.overflow = Overflow.Hidden;
        title.style.whiteSpace = WhiteSpace.NoWrap;
        title.style.textOverflow = TextOverflow.Ellipsis;
        if (!withName) title.style.display = DisplayStyle.None;
    }

    Label Text(int size, TextAnchor anchor)
    {
        var label = new Label { pickingMode = PickingMode.Ignore };
        label.style.position = Position.Absolute;
        label.style.fontSize = size;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.unityTextAlign = anchor;
        label.style.marginLeft = 0; label.style.marginRight = 0; label.style.marginTop = 0; label.style.marginBottom = 0;
        label.style.paddingLeft = 0; label.style.paddingRight = 0; label.style.paddingTop = 0; label.style.paddingBottom = 0;
        Add(label);
        return label;
    }

    /// <summary>Nastaví obsah miesta; prekreslí sa len pri zmene.</summary>
    public void Set(ItemDef newDef, int amount, Color newBorder, Color newTint, bool newFaded)
    {
        bool changed = newDef != def || newBorder != border || newTint != tint || newFaded != faded;
        def = newDef;
        border = newBorder;
        tint = newTint;
        faded = newFaded;

        string wanted = def != null && amount > 1 ? amount.ToString() : string.Empty;
        if (count.text != wanted) count.text = wanted;
        bool legendary = def != null && def.IsLegendary;
        star.style.display = legendary ? DisplayStyle.Flex : DisplayStyle.None;
        if (nameHeight > 0f)
        {
            string label = def != null ? def.displayName : string.Empty;
            if (title.text != label) title.text = label;
        }
        if (changed) MarkDirtyRepaint();
    }

    public void SetHover(bool over)
    {
        if (hovered == over) return;
        hovered = over;
        MarkDirtyRepaint();
    }

    void Draw(MeshGenerationContext context)
    {
        Rect r = contentRect;
        if (r.width < cut * 2f || r.height < cut * 2f) return;
        Painter2D p = context.painter2D;

        p.fillColor = back;
        Outline(p, r);
        p.Fill();
        if (tint.a > 0.001f || hovered)
        {
            p.fillColor = hovered ? new Color(border.r, border.g, border.b, 0.06f) : tint;
            Outline(p, r);
            p.Fill();
        }

        if (def != null)
        {
            float pad = Mathf.Min(9f, r.width * 0.16f);
            Rect area = new Rect(r.x + pad, r.y + pad, r.width - pad * 2f, r.height - pad * 2f - nameHeight);
            Color c = def.color;
            if (faded) c.a = 0.3f;
            DrawIcon(p, area, IconOf(def), c);
        }

        p.strokeColor = hovered ? Color.Lerp(border, Color.white, 0.5f) : border;
        p.lineWidth = hovered ? 2f : 1.3f;
        p.lineJoin = LineJoin.Miter;
        Outline(p, r);
        p.Stroke();
    }

    // Zrezaný pravý horný a ľavý dolný roh, ako rámy HUD-u
    void Outline(Painter2D p, Rect r)
    {
        p.BeginPath();
        p.MoveTo(new Vector2(r.x + 1f, r.y + 1f));
        p.LineTo(new Vector2(r.xMax - cut, r.y + 1f));
        p.LineTo(new Vector2(r.xMax - 1f, r.y + cut));
        p.LineTo(new Vector2(r.xMax - 1f, r.yMax - 1f));
        p.LineTo(new Vector2(r.x + cut, r.yMax - 1f));
        p.LineTo(new Vector2(r.x + 1f, r.yMax - cut));
        p.ClosePath();
    }

    static Icon IconOf(ItemDef def)
    {
        if (def.IsWeapon) return Icon.Sword;
        if (def.IsPotion) return def.effect == PotionEffect.Food ? Icon.Food : Icon.Flask;
        switch (def.slot)
        {
            case EquipSlot.Head: return Icon.Head;
            case EquipSlot.Top: return Icon.Top;
            case EquipSlot.Bottom: return Icon.Bottom;
            case EquipSlot.Boots: return Icon.Boots;
        }
        string id = def.id;
        if (id.Contains("crystal") || id == ItemCatalog.Gold) return Icon.Gem;
        if (id == ItemCatalog.Wood) return Icon.Log;
        if (id == ItemCatalog.Herb) return Icon.Leaf;
        return Icon.Box;
    }

    /// <summary>Vektorová ikona predmetu do daného obdĺžnika (používa ju aj ikona ťahaného predmetu a popis).</summary>
    public static void DrawIcon(Painter2D p, Rect area, ItemDef def, float alpha = 1f)
    {
        if (def == null) return;
        Color c = def.color;
        c.a = alpha;
        DrawIcon(p, area, IconOf(def), c);
    }

    static void DrawIcon(Painter2D p, Rect area, Icon icon, Color color)
    {
        if (icon == Icon.None) return;
        float s = Mathf.Min(area.width, area.height) * 0.5f;
        Vector2 c = area.center;
        Color fill = new Color(color.r, color.g, color.b, color.a * 0.35f);
        Color line = Color.Lerp(color, Color.white, 0.25f);
        line.a = color.a;

        p.strokeColor = line;
        p.fillColor = fill;
        p.lineWidth = Mathf.Max(1.4f, s * 0.09f);
        p.lineJoin = LineJoin.Round;
        p.lineCap = LineCap.Round;

        switch (icon)
        {
            case Icon.Sword:
                // Čepeľ šikmo hore, záštita a rukoväť
                p.lineWidth = Mathf.Max(2.2f, s * 0.2f);
                Segment(p, c + new Vector2(-0.3f, 0.3f) * s, c + new Vector2(0.7f, -0.7f) * s);
                p.lineWidth = Mathf.Max(1.6f, s * 0.12f);
                Segment(p, c + new Vector2(-0.58f, 0.02f) * s, c + new Vector2(-0.02f, 0.58f) * s);
                Segment(p, c + new Vector2(-0.3f, 0.3f) * s, c + new Vector2(-0.68f, 0.68f) * s);
                break;

            case Icon.Flask:
                Poly(p, c, s, true, -0.18f, -0.8f, 0.18f, -0.8f, 0.18f, -0.3f, 0.62f, 0.5f, 0.5f, 0.78f, -0.5f, 0.78f, -0.62f, 0.5f, -0.18f, -0.3f);
                // Hladina
                p.fillColor = new Color(color.r, color.g, color.b, color.a * 0.85f);
                Poly(p, c, s, false, -0.42f, 0.1f, 0.42f, 0.1f, 0.6f, 0.5f, 0.48f, 0.74f, -0.48f, 0.74f, -0.6f, 0.5f);
                Segment(p, c + new Vector2(-0.26f, -0.8f) * s, c + new Vector2(0.26f, -0.8f) * s);
                break;

            case Icon.Food:
                p.fillColor = new Color(color.r, color.g, color.b, color.a * 0.75f);
                p.BeginPath();
                p.Arc(c + new Vector2(0f, 0.08f * s), s * 0.62f, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Fill();
                p.Stroke();
                Segment(p, c + new Vector2(0.05f, -0.52f) * s, c + new Vector2(0.28f, -0.82f) * s);
                break;

            case Icon.Head:
                p.BeginPath();
                p.Arc(c + new Vector2(0f, 0.1f * s), s * 0.66f, Angle.Degrees(180f), Angle.Degrees(360f));
                p.LineTo(c + new Vector2(0.66f, 0.5f) * s);
                p.LineTo(c + new Vector2(0.2f, 0.5f) * s);
                p.LineTo(c + new Vector2(0.2f, 0.2f) * s);
                p.LineTo(c + new Vector2(-0.66f, 0.2f) * s);
                p.ClosePath();
                p.Fill();
                p.Stroke();
                break;

            case Icon.Top:
                Poly(p, c, s, true, -0.28f, -0.7f, 0.28f, -0.7f, 0.8f, -0.42f, 0.62f, -0.02f, 0.42f, -0.14f, 0.42f, 0.74f, -0.42f, 0.74f, -0.42f, -0.14f, -0.62f, -0.02f, -0.8f, -0.42f);
                break;

            case Icon.Bottom:
                Poly(p, c, s, true, -0.5f, -0.74f, 0.5f, -0.74f, 0.58f, 0.76f, 0.14f, 0.76f, 0f, -0.1f, -0.14f, 0.76f, -0.58f, 0.76f);
                break;

            case Icon.Boots:
                Poly(p, c, s, true, -0.4f, -0.76f, 0.1f, -0.76f, 0.1f, 0.1f, 0.7f, 0.34f, 0.7f, 0.72f, -0.4f, 0.72f);
                Segment(p, c + new Vector2(-0.4f, 0.48f) * s, c + new Vector2(0.7f, 0.48f) * s);
                break;

            case Icon.Gem:
                Poly(p, c, s, true, 0f, -0.8f, 0.6f, -0.2f, 0f, 0.8f, -0.6f, -0.2f);
                Segment(p, c + new Vector2(-0.6f, -0.2f) * s, c + new Vector2(0.6f, -0.2f) * s);
                break;

            case Icon.Log:
                Poly(p, c, s, true, -0.78f, -0.26f, 0.5f, -0.26f, 0.78f, 0f, 0.5f, 0.3f, -0.78f, 0.3f);
                Segment(p, c + new Vector2(-0.4f, 0.02f) * s, c + new Vector2(0.2f, 0.02f) * s);
                break;

            case Icon.Leaf:
                Poly(p, c, s, true, 0f, -0.8f, 0.5f, -0.1f, 0f, 0.7f, -0.5f, -0.1f);
                Segment(p, c + new Vector2(0f, -0.6f) * s, c + new Vector2(0f, 0.86f) * s);
                break;

            default:
                Poly(p, c, s, true, -0.34f, -0.68f, 0.34f, -0.68f, 0.7f, 0f, 0.34f, 0.68f, -0.34f, 0.68f, -0.7f, 0f);
                p.fillColor = new Color(color.r, color.g, color.b, color.a * 0.8f);
                Poly(p, c, s, false, -0.14f, -0.28f, 0.14f, -0.28f, 0.3f, 0f, 0.14f, 0.28f, -0.14f, 0.28f, -0.3f, 0f);
                break;
        }
    }

    static void Segment(Painter2D p, Vector2 a, Vector2 b)
    {
        p.BeginPath();
        p.MoveTo(a);
        p.LineTo(b);
        p.Stroke();
    }

    // Mnohouholník zo súradníc v jednotkách polovice ikony (x, y striedavo)
    static void Poly(Painter2D p, Vector2 c, float s, bool stroke, params float[] points)
    {
        p.BeginPath();
        p.MoveTo(c + new Vector2(points[0], points[1]) * s);
        for (int i = 2; i + 1 < points.Length; i += 2) p.LineTo(c + new Vector2(points[i], points[i + 1]) * s);
        p.ClosePath();
        p.Fill();
        if (stroke) p.Stroke();
    }
}
