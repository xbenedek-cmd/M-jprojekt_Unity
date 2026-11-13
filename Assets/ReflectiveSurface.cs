using System.Collections.Generic;
using UnityEngine;

public enum ReflectiveKind { Window, Mirror, Water }

public class ReflectiveSurface : MonoBehaviour
{
    public const float MaxCheckRadius = 5f;

    public ReflectiveKind kind = ReflectiveKind.Window;
    public float radius = 5f;
    public Vector2 size = new Vector2(3f, 2f);
    public bool showVisual = true;

    [Header("Pevná stena (Sklo / Zrkadlo)")]
    public float mirrorThickness = 0.5f; // Zväčšené z 0.25 na 0.5

    static readonly List<ReflectiveSurface> all = new List<ReflectiveSurface>();
    private GameObject visual;

    public float EffectiveRadius => Mathf.Min(radius, MaxCheckRadius);

    public string Label
    {
        get
        {
            if (kind == ReflectiveKind.Mirror) return "zrkadle";
            if (kind == ReflectiveKind.Water) return "kaluži";
            return "výklade";
        }
    }

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    void Start()
    {
        SetupPhysics();
        if (showVisual) BuildVisual();
    }

    void OnDestroy()
    {
        if (visual != null) Destroy(visual);
    }

    public static ReflectiveSurface Find(Vector3 npcPos, Vector3 playerPos, float viewDistance = 15f)
    {
        ReflectiveSurface best = null;
        float bestDist = float.MaxValue;

        foreach (ReflectiveSurface s in all)
        {
            if (s == null) continue;
            float d = Vector3.Distance(s.transform.position, npcPos);
            if (d > s.EffectiveRadius || d >= bestDist) continue;
            if (Vector3.Distance(s.transform.position, playerPos) > viewDistance) continue;

            bestDist = d;
            best = s;
        }
        return best;
    }

    public static bool IsOnWater(Vector3 pos)
    {
        foreach (ReflectiveSurface s in all)
        {
            if (s == null || s.kind != ReflectiveKind.Water) continue;
            if (Mathf.Abs(pos.y - s.transform.position.y) > 2f) continue;

            Vector3 local = s.transform.InverseTransformPoint(pos);
            if (Mathf.Abs(local.x) <= s.size.x * 0.5f && Mathf.Abs(local.z) <= s.size.y * 0.5f)
                return true;
        }
        return false;
    }

    void SetupPhysics()
    {
        Collider[] oldColliders = GetComponents<Collider>();
        foreach (var c in oldColliders)
        {
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }

        if (kind == ReflectiveKind.Mirror || kind == ReflectiveKind.Window)
        {
            Vector3 ls = transform.lossyScale;
            BoxCollider box = gameObject.AddComponent<BoxCollider>();

            box.size = new Vector3(
                size.x / Mathf.Max(0.0001f, Mathf.Abs(ls.x)),
                size.y / Mathf.Max(0.0001f, Mathf.Abs(ls.y)),
                mirrorThickness / Mathf.Max(0.0001f, Mathf.Abs(ls.z)));

            box.center = Vector3.zero;
            box.isTrigger = false;
            gameObject.layer = 0;
        }
    }

    void BuildVisual()
    {
        visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(visual.GetComponent<Collider>());
        visual.name = "ReflectiveVisual";

        Color color;
        Quaternion rot;
        Vector3 pos = transform.position;

        if (kind == ReflectiveKind.Water)
        {
            color = new Color(0.2f, 0.45f, 0.85f, 0.5f);
            rot = Quaternion.Euler(90f, transform.eulerAngles.y, 0f);
            pos.y += 0.03f;
        }
        else
        {
            color = kind == ReflectiveKind.Mirror ? new Color(0.85f, 0.92f, 1f, 0.55f)
                                                  : new Color(0.55f, 0.85f, 1f, 0.35f);
            rot = transform.rotation;
        }

        visual.transform.SetPositionAndRotation(pos, rot);
        visual.transform.localScale = new Vector3(size.x, size.y, 1f);

        Renderer r = visual.GetComponent<Renderer>();
        Shader sh = Shader.Find("Sprites/Default");
        if (sh != null) r.material = new Material(sh);
        r.material.color = color;
    }
}