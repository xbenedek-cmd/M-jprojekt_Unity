using System;
using UnityEngine;

public enum StepSurface { Grass, Leaves, Thicket, Dirt, Mud, Stone, Rubble, RubbleMetal, Hay, WaterShallow, WaterDeep }

/// <summary>
/// Zistí povrch pod hráčom (vrstva terénu divočiny, voda v jazere, názov materiálu / objektu) a vyberie zvuk kroku.
/// Divočina: tráva podľa zóny (Divočina = lesná pôda, Temný les = suché lístie, Kaňon = kamene, Pustatina = sutina, Močiare = krovie),
/// cesta = prašná cesta, svah = kamene, breh jazera = blato, plytká voda = kaluž, hlboká voda = hlboká kaluž.
/// </summary>
public static class FootstepSurfaces
{
    private static FootstepSet set;
    private static bool loaded;
    private static WildLake[] lakes = new WildLake[0];
    private static float nextLakeRefresh;
    private static AudioClip lastClip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { set = null; loaded = false; lakes = new WildLake[0]; nextLakeRefresh = 0f; lastClip = null; }

    public static AudioClip Pick(Vector3 feet, bool run)
    {
        if (!loaded)
        {
            loaded = true;
            set = Resources.Load<FootstepSet>("FootstepSet");
        }
        if (set == null) return null;

        AudioClip[] clips = set.Get(Detect(feet), run);
        if (clips == null || clips.Length == 0) return null;

        // Dva kroky po sebe nikdy rovnaký záznam
        AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
        if (clip == lastClip && clips.Length > 1) clip = clips[(Array.IndexOf(clips, clip) + 1) % clips.Length];
        lastClip = clip;
        return clip;
    }

    static StepSurface Detect(Vector3 feet)
    {
        if (!Physics.Raycast(feet + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore))
            return StepSurface.Dirt;

        // Voda v jazere
        if (Time.unscaledTime >= nextLakeRefresh)
        {
            nextLakeRefresh = Time.unscaledTime + 10f;
            lakes = UnityEngine.Object.FindObjectsByType<WildLake>();
        }
        foreach (WildLake lake in lakes)
        {
            if (lake == null || !lake.Covers(hit.point, 0f)) continue;
            float depth = lake.transform.position.y - hit.point.y;
            if (depth > 1f) return StepSurface.WaterDeep;
            if (depth > 0.05f) return StepSurface.WaterShallow;
        }

        Terrain terrain = hit.collider.GetComponent<Terrain>();
        if (terrain != null) return TerrainSurface(terrain, hit.point);

        string name = hit.collider.name.ToLowerInvariant();
        Renderer r = hit.collider.GetComponent<Renderer>();
        if (r != null && r.sharedMaterial != null) name += " " + r.sharedMaterial.name.ToLowerInvariant();

        if (name.Contains("hay") || name.Contains("podlaha")) return StepSurface.Hay;
        if (name.Contains("ruin") || name.Contains("rubble") || name.Contains("troska")) return StepSurface.Rubble;
        if (name.Contains("vrak") || name.Contains("metal") || name.Contains("plech")) return StepSurface.RubbleMetal;
        if (name.Contains("castle") || name.Contains("stone") || name.Contains("kopec") || name.Contains("skala") || name.Contains("utes") || name.Contains("brick")) return StepSurface.Stone;
        if (name.Contains("mud") || name.Contains("blato")) return StepSurface.Mud;
        if (name.Contains("grass") || name.Contains("trav")) return StepSurface.Grass;
        return StepSurface.Dirt;
    }

    static StepSurface TerrainSurface(Terrain terrain, Vector3 point)
    {
        TerrainData data = terrain.terrainData;
        Vector3 local = point - terrain.transform.position;
        int ax = Mathf.Clamp(Mathf.RoundToInt(local.x / data.size.x * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
        int az = Mathf.Clamp(Mathf.RoundToInt(local.z / data.size.z * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
        float[,,] weights = data.GetAlphamaps(ax, az, 1, 1);

        int best = 0;
        for (int i = 1; i < weights.GetLength(2); i++)
            if (weights[0, 0, i] > weights[0, 0, best]) best = i;

        switch (best)
        {
            case 1: return StepSurface.Dirt;     // cesta
            case 2: return StepSurface.Stone;    // strmý svah
            case 3: return StepSurface.Mud;      // breh jazera
            default: return ZoneGrass(terrain);
        }
    }

    static StepSurface ZoneGrass(Terrain terrain)
    {
        string zone = terrain.transform.parent != null ? terrain.transform.parent.name : "";
        if (zone.EndsWith("Les")) return StepSurface.Leaves;
        if (zone.EndsWith("Kanon")) return StepSurface.Stone;
        if (zone.EndsWith("Pustatina")) return StepSurface.Rubble;
        if (zone.EndsWith("Mociare")) return StepSurface.Thicket;
        return StepSurface.Grass;
    }
}
