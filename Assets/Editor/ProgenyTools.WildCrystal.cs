using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Zbierateľné kryštály (Zdroj_kryštál) používajú model Assets/Models/Crystal.fbx (zmenšený na ~1 m, pri veľkom počte trojuholníkov sa vytvorí
/// ľahká verzia v Assets/Models/CrystalLow) a okolo neho malé úlomky BleskovyKrystal2. Kryštály sa len zbierajú (F), neničia sa.
/// Veľké dekoratívne kryštály sa zrušili.
/// </summary>
public static partial class ProgenyTools
{
    const string CrystalModelPath = "Assets/Models/Crystal.fbx";
    const string CrystalLowFolder = "Assets/Models/CrystalLow";
    const int CrystalMaxTriangles = 6000;      // model s viac trojuholníkmi sa zjednoduší (v zóne je ich desiatky)
    const float CrystalMinMeters = 0.9f, CrystalMaxMeters = 1.3f;   // najdlhší rozmer zbierateľného kryštálu

    static bool FillCrystalModel(GameObject cluster, WildMats m, System.Random rnd)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(CrystalModelPath);
        if (asset == null) return false;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, cluster.transform);
        instance.name = "Kristal";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
        instance.transform.localScale = Vector3.one;

        foreach (MeshFilter mf in instance.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) mf.sharedMesh = LowPolyCrystalMesh(mf.sharedMesh);
        foreach (Collider c in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) { Object.DestroyImmediate(instance); return false; }

        // Model bez textúry (biely štandardný materiál) dostane sklenený materiál kryštálov
        foreach (Renderer r in renderers)
        {
            bool bare = false;
            foreach (Material current in r.sharedMaterials)
                if (current == null || (current.mainTexture == null && current.color.maxColorComponent > 0.85f)) { bare = true; break; }   // FBX z Blendera dáva bielu 0,906 (nie 1,0)
            if (!bare) continue;

            var mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = m.crystal;
            r.sharedMaterials = mats;
        }

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        float target = RandomRange(rnd, CrystalMinMeters, CrystalMaxMeters);
        instance.transform.localScale = Vector3.one * (target / Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z))));

        b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        Vector3 origin = cluster.transform.position;
        instance.transform.position += new Vector3(origin.x - b.center.x, origin.y - b.min.y, origin.z - b.center.z);
        AddColliderFromBounds(instance);

        // Malé úlomky bleskového kryštálu okolo
        Mesh shard = CrystalShardMesh();
        for (int i = 0; i < 3; i++)
        {
            float angle = i * Mathf.PI * 2f / 3f + RandomRange(rnd, -0.4f, 0.4f);
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * RandomRange(rnd, 0.4f, 0.6f);
            float height = RandomRange(rnd, 0.28f, 0.5f);
            Part(PrimitiveType.Cube, cluster.transform, offset + Vector3.up * height * 0.5f, new Vector3(0.14f, height, 0.14f), m.crystal, false,
                Quaternion.Euler(RandomRange(rnd, -16f, 16f), RandomRange(rnd, 0f, 90f), RandomRange(rnd, -16f, 16f)));
            cluster.transform.GetChild(cluster.transform.childCount - 1).GetComponent<MeshFilter>().sharedMesh = shard;
        }

        // Žiara na zemi, aby bolo zbierateľné kryštály vidieť z diaľky
        Part(PrimitiveType.Cylinder, cluster.transform, new Vector3(0f, 0.02f, 0f), new Vector3(1.0f, 0.01f, 1.0f), m.crystalGlow, false);
        return true;
    }

    // ---------- Zjednodušenie siete ----------
    static Mesh LowPolyCrystalMesh(Mesh source)
    {
        if (source.triangles.Length / 3 <= CrystalMaxTriangles) return source;

        if (!AssetDatabase.IsValidFolder(CrystalLowFolder)) AssetDatabase.CreateFolder("Assets/Models", "CrystalLow");
        string path = $"{CrystalLowFolder}/{source.name}_low.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        Mesh low = source;
        foreach (int resolution in new[] { 64, 48, 36, 28, 22, 16 })
        {
            low = ClusterSimplify(source, resolution);
            if (low.triangles.Length / 3 <= CrystalMaxTriangles) break;
        }
        low.name = source.name + "_low";
        AssetDatabase.CreateAsset(low, path);
        Debug.Log($"Kryštál: sieť {source.name} zjednodušená z {source.triangles.Length / 3} na {low.triangles.Length / 3} trojuholníkov.");
        return low;
    }

    // Zlučovanie vrcholov v mriežke: vrcholy v jednej bunke sa spoja (priemer polohy, normály a UV), zdegenerované trojuholníky zmiznú
    static Mesh ClusterSimplify(Mesh src, int resolution)
    {
        Vector3[] vertices = src.vertices, normals = src.normals;
        Vector2[] uvs = src.uv;
        Bounds b = src.bounds;
        float cell = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) / resolution;

        var map = new Dictionary<Vector3Int, int>();
        var remap = new int[vertices.Length];
        var pos = new List<Vector3>();
        var nor = new List<Vector3>();
        var uv = new List<Vector2>();
        var count = new List<int>();

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 rel = vertices[i] - b.min;
            var key = new Vector3Int(Mathf.FloorToInt(rel.x / cell), Mathf.FloorToInt(rel.y / cell), Mathf.FloorToInt(rel.z / cell));
            if (!map.TryGetValue(key, out int index))
            {
                index = pos.Count;
                map[key] = index;
                pos.Add(Vector3.zero);
                nor.Add(Vector3.zero);
                uv.Add(Vector2.zero);
                count.Add(0);
            }
            pos[index] += vertices[i];
            if (normals.Length > 0) nor[index] += normals[i];
            if (uvs.Length > 0) uv[index] += uvs[i];
            count[index]++;
            remap[i] = index;
        }

        for (int i = 0; i < pos.Count; i++)
        {
            pos[i] /= count[i];
            nor[i] = nor[i].sqrMagnitude > 0.0001f ? nor[i].normalized : Vector3.up;
            uv[i] /= count[i];
        }

        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(pos);
        mesh.SetNormals(nor);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            int[] tris = src.GetTriangles(s);
            var kept = new List<int>(tris.Length / 4);
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = remap[tris[t]], c1 = remap[tris[t + 1]], c2 = remap[tris[t + 2]];
                if (a == c1 || c1 == c2 || a == c2) continue;
                kept.Add(a);
                kept.Add(c1);
                kept.Add(c2);
            }
            mesh.SetTriangles(kept, s);
        }
        mesh.RecalculateBounds();
        return mesh;
    }
}
