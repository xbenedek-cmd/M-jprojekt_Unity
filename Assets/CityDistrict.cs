using System.Collections.Generic;
using UnityEngine;

public enum DistrictType { None, Residential, Military, Underworld, Industrial, Ruins }

/// <summary>
/// Časť mesta s vlastnými pravidlami oblečenia (viď Wardrobe). Veľkosť určuje BoxCollider (trigger).
/// Vojenská zóna (MilitaryZone) sa počíta ako vojenská štvrť automaticky.
/// Objekt polož na zem, box sa tiahne nahor.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class CityDistrict : MonoBehaviour
{
    public DistrictType type = DistrictType.Residential;

    [Header("Zobrazenie")]
    public bool showOnGround = true;
    public float borderWidth = 0.3f;

    static readonly List<CityDistrict> all = new List<CityDistrict>();

    private BoxCollider box;
    private readonly List<GameObject> visuals = new List<GameObject>();

    // ---------- Názvy a farby ----------
    public static string NameOf(DistrictType t)
    {
        switch (t)
        {
            case DistrictType.Residential: return "Obytná štvrť";
            case DistrictType.Military:    return "Vojenská zóna";
            case DistrictType.Underworld:  return "Podsvetie";
            case DistrictType.Industrial:  return "Priemyselná štvrť";
            case DistrictType.Ruins:       return "Zničená stará časť";
            default:                       return "Mimo mesta";
        }
    }

    public static Color ColorOf(DistrictType t)
    {
        switch (t)
        {
            case DistrictType.Residential: return new Color(0.35f, 0.7f, 1f);
            case DistrictType.Military:    return new Color(0.3f, 1f, 0.4f);
            case DistrictType.Underworld:  return new Color(0.75f, 0.35f, 1f);
            case DistrictType.Industrial:  return new Color(1f, 0.65f, 0.25f);
            case DistrictType.Ruins:       return new Color(0.7f, 0.7f, 0.7f);
            default:                       return Color.white;
        }
    }

    void Reset()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        b.isTrigger = true;
        b.size = new Vector3(20f, 6f, 20f);
        b.center = new Vector3(0f, 3f, 0f);
    }

    void OnEnable()
    {
        box = GetComponent<BoxCollider>();
        all.Add(this);
    }

    void OnDisable() => all.Remove(this);

    void Start()
    {
        if (showOnGround) BuildVisual();
    }

    void OnDestroy()
    {
        foreach (GameObject g in visuals)
            if (g != null) Destroy(g);
    }

    // ---------- Dotazy ----------
    /// <summary>Typ štvrte na danom mieste (podľa X a Z). Vojenská zóna má prednosť, inak vyhrá najmenšia štvrť.</summary>
    public static DistrictType TypeAt(Vector3 p)
    {
        if (MilitaryZone.At(p) != null) return DistrictType.Military;
        if (ResidentialZone.At(p) != null) return DistrictType.Residential;

        CityDistrict best = null;
        float bestArea = float.MaxValue;

        foreach (CityDistrict d in all)
        {
            if (d == null || d.box == null) continue;

            Bounds b = d.box.bounds;
            if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;

            float area = b.size.x * b.size.z;
            if (area < bestArea)
            {
                bestArea = area;
                best = d;
            }
        }
        return best != null ? best.type : DistrictType.None;
    }

    // ---------- Vzhľad: priesvitný obdĺžnik na zemi ----------
    void BuildVisual()
    {
        if (box == null) box = GetComponent<BoxCollider>();

        Bounds b = box.bounds;
        float y = b.min.y + 0.04f;
        if (Physics.Raycast(b.center, Vector3.down, out RaycastHit hit, 60f, ~0, QueryTriggerInteraction.Ignore))
            y = hit.point.y + 0.04f;

        Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
        Vector3 c = transform.TransformPoint(box.center);
        float yaw = transform.eulerAngles.y;
        Quaternion rot = Quaternion.Euler(90f, yaw, 0f);
        Quaternion flat = Quaternion.Euler(0f, yaw, 0f);

        Color baseColor = ColorOf(type);
        Color fill = new Color(baseColor.r, baseColor.g, baseColor.b, 0.14f);
        Color border = new Color(baseColor.r, baseColor.g, baseColor.b, 0.7f);

        MakeQuad(new Vector3(c.x, y, c.z), rot, new Vector3(size.x, size.z, 1f), fill);

        float hx = size.x * 0.5f, hz = size.z * 0.5f, w = borderWidth;
        MakeQuad(new Vector3(c.x, y + 0.01f, c.z) + flat * new Vector3(0f, 0f, hz), rot, new Vector3(size.x, w, 1f), border);
        MakeQuad(new Vector3(c.x, y + 0.01f, c.z) + flat * new Vector3(0f, 0f, -hz), rot, new Vector3(size.x, w, 1f), border);
        MakeQuad(new Vector3(c.x, y + 0.01f, c.z) + flat * new Vector3(hx, 0f, 0f), rot, new Vector3(w, size.z, 1f), border);
        MakeQuad(new Vector3(c.x, y + 0.01f, c.z) + flat * new Vector3(-hx, 0f, 0f), rot, new Vector3(w, size.z, 1f), border);
    }

    void MakeQuad(Vector3 pos, Quaternion rot, Vector3 scale, Color color)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = "DistrictVisual";
        q.transform.SetPositionAndRotation(pos, rot);
        q.transform.localScale = scale;

        Renderer r = q.GetComponent<Renderer>();
        Shader sh = Shader.Find("Sprites/Default");
        if (sh != null) r.material = new Material(sh);
        r.material.color = color;

        visuals.Add(q);
    }

    void OnDrawGizmos()
    {
        BoxCollider b = GetComponent<BoxCollider>();
        if (b == null) return;

        Color c = ColorOf(type);
        Gizmos.color = new Color(c.r, c.g, c.b, 0.9f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
