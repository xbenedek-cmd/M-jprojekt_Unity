using UnityEngine;

/// <summary>
/// Krátke volania efektov pre hrateľnostné skripty (bossovia, veže, drony, schopnosti).
/// Lúče a vlny kreslí CombatFx (žiarivé čiary a prstence z častíc); tu ostáva len značka nad cieľom.
/// </summary>
public static class Vfx
{
    private static readonly SimpleObjectPool markers = new SimpleObjectPool(() => MakePrimitive(PrimitiveType.Sphere, "VfxMarker"));

    // Farba cez MaterialPropertyBlock: .material.color by pri každom efekte vytvorilo nový Material (únik pamäte)
    private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private static GameObject MakePrimitive(PrimitiveType type, string name)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = name;
        return go;
    }

    private static void Tint(GameObject go, Color color)
    {
        Renderer r = go.GetComponent<Renderer>();
        block.SetColor(ColorId, color);
        r.SetPropertyBlock(block);
    }

    /// <summary>Lúč medzi dvoma bodmi (strela, laser, mieridlo).</summary>
    public static void Beam(Vector3 a, Vector3 b, Color color, float time = 0.18f, float width = 0.12f)
    {
        if ((b - a).sqrMagnitude < 0.0001f) return;
        CombatFx.Beam(a, b, color, time, width);
    }

    /// <summary>Značka nad hlavou cieľa, ktorá ho sleduje (označenie dronom).</summary>
    public static void Marker(Transform target, Color color, float time)
    {
        if (target == null) return;

        GameObject go = markers.Get();
        go.transform.SetParent(target, true);
        go.transform.position = target.position + Vector3.up * 3.2f;

        Vector3 ls = target.lossyScale;
        float sx = Mathf.Approximately(ls.x, 0f) ? 1f : ls.x;
        float sy = Mathf.Approximately(ls.y, 0f) ? 1f : ls.y;
        float sz = Mathf.Approximately(ls.z, 0f) ? 1f : ls.z;
        go.transform.localScale = new Vector3(0.4f / sx, 0.4f / sy, 0.4f / sz);

        Tint(go, color);
        markers.ReleaseAfter(go, time);
    }

    /// <summary>Vlna po zemi, ktorá sa rozšíri do daného polomeru (výbuch, impulz, výstraha pred úderom).</summary>
    public static void Disc(Vector3 center, float radius, Color color, float time = 0.4f)
    {
        CombatFx.Ring(center, radius, color, time);
    }
}
