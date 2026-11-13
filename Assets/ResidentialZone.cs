using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(ZoneBoundary))]
public class ResidentialZone : MonoBehaviour
{
    public Color fillColor = new Color(0.25f, 0.55f, 1f, 0.18f);
    public Color borderColor = new Color(0.35f, 0.7f, 1f, 0.85f);
    public float borderWidth = 0.4f;

    private static readonly List<ResidentialZone> zones = new List<ResidentialZone>();
    private BoxCollider box;
    private readonly List<GameObject> visuals = new List<GameObject>();

    void Reset()
    {
        BoxCollider collider = GetComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(20f, 6f, 20f);
        collider.center = new Vector3(0f, 3f, 0f);
    }

    void OnEnable()
    {
        box = GetComponent<BoxCollider>();
        if (!zones.Contains(this)) zones.Add(this);
    }

    void OnDisable() => zones.Remove(this);

    void Start() => BuildVisual();

    void OnDestroy()
    {
        foreach (GameObject visual in visuals)
            if (visual != null) Destroy(visual);
    }

    public static ResidentialZone At(Vector3 position)
    {
        foreach (ResidentialZone zone in zones)
            if (zone != null && zone.Contains(position)) return zone;
        return null;
    }

    public bool Contains(Vector3 position)
    {
        if (box == null) return false;
        Bounds bounds = box.bounds;
        return position.x >= bounds.min.x && position.x <= bounds.max.x
            && position.z >= bounds.min.z && position.z <= bounds.max.z;
    }

    public Vector3 ClampInside(Vector3 position, float margin = 0.65f)
    {
        if (box == null) return position;
        Bounds bounds = box.bounds;
        position.x = Mathf.Clamp(position.x, bounds.min.x + margin, bounds.max.x - margin);
        position.z = Mathf.Clamp(position.z, bounds.min.z + margin, bounds.max.z - margin);
        return position;
    }

    void BuildVisual()
    {
        if (box == null) box = GetComponent<BoxCollider>();
        Bounds bounds = box.bounds;
        float y = bounds.min.y + 0.05f;
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        Quaternion rotation = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
        Quaternion flat = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        MakeQuad(new Vector3(center.x, y, center.z), rotation, new Vector3(size.x, size.z, 1f), fillColor);
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;
        MakeQuad(new Vector3(center.x, y + 0.01f, center.z) + flat * new Vector3(0f, 0f, halfZ), rotation, new Vector3(size.x, borderWidth, 1f), borderColor);
        MakeQuad(new Vector3(center.x, y + 0.01f, center.z) + flat * new Vector3(0f, 0f, -halfZ), rotation, new Vector3(size.x, borderWidth, 1f), borderColor);
        MakeQuad(new Vector3(center.x, y + 0.01f, center.z) + flat * new Vector3(halfX, 0f, 0f), rotation, new Vector3(borderWidth, size.z, 1f), borderColor);
        MakeQuad(new Vector3(center.x, y + 0.01f, center.z) + flat * new Vector3(-halfX, 0f, 0f), rotation, new Vector3(borderWidth, size.z, 1f), borderColor);
    }

    void MakeQuad(Vector3 position, Quaternion rotation, Vector3 scale, Color color)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "ResidentialZoneVisual";
        quad.transform.SetPositionAndRotation(position, rotation);
        quad.transform.localScale = scale;

        Renderer renderer = quad.GetComponent<Renderer>();
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null) renderer.material = new Material(shader);
        renderer.material.color = color;
        visuals.Add(quad);
    }
}
