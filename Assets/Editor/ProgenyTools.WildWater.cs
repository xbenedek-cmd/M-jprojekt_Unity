using UnityEditor;
using UnityEngine;

/// <summary>
/// Voda jazier: hladina sa nekreslí ako kruh, ale ako sieť, ktorá presne kopíruje brehy kotliny z výškovej mapy terénu
/// (organický tvar). Pri okraji je vertexová alfa 0, takže voda mäkko vyblednú do brehu; hĺbka sa ukladá do vertexovej farby
/// (plytká / hlboká voda). Materiál používa shader Progeny/Water (odrazy, vlnky vetra, kruhy od dažďa).
/// </summary>
public static partial class ProgenyTools
{
    const string WaterMeshFolder = "Assets/Models/Water";
    const int WaterGridStride = 2;              // každý druhý bod výškovej mapy (≈ 0,94 m)
    const float WaterShoreFadeMeters = 0.35f;   // hĺbka, v ktorej voda dosiahne plnú nepriehľadnosť
    const float WaterDeepMeters = 4f;           // hĺbka, od ktorej je voda "hlboká" (tmavá)

    static Material WaterMaterial(WildTheme theme)
    {
        Shader shader = Shader.Find("Progeny/Water");
        if (shader == null) return null;

        string path = $"{SurfacesFolder}/Voda_{theme.key}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;

        // Močiare: zelenohnedá voda, ostatné: modrozelená
        bool swamp = theme.key == "Mociare";
        m.SetColor("_ShallowColor", swamp ? new Color(0.22f, 0.3f, 0.14f) : new Color(0.25f, 0.45f, 0.45f));
        m.SetColor("_DeepColor", swamp ? new Color(0.05f, 0.09f, 0.06f) : new Color(0.03f, 0.13f, 0.18f));
        m.SetFloat("_Smoothness", swamp ? 0.85f : 0.96f);
        m.SetFloat("_MinAlpha", swamp ? 0.7f : 0.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh BuildWaterMesh(TerrainBuild b, LakeSpec spec, int index)
    {
        float reach = spec.lake.basinRadius * 1.5f;
        int ix0 = Mathf.Max(0, Mathf.FloorToInt((spec.pos.x - reach - b.originX) / b.cell));
        int ix1 = Mathf.Min(b.n - 1, Mathf.CeilToInt((spec.pos.x + reach - b.originX) / b.cell));
        int iz0 = Mathf.Max(0, Mathf.FloorToInt((spec.pos.y - reach - b.originZ) / b.cell));
        int iz1 = Mathf.Min(b.n - 1, Mathf.CeilToInt((spec.pos.y + reach - b.originZ) / b.cell));

        int nx = (ix1 - ix0) / WaterGridStride + 1, nz = (iz1 - iz0) / WaterGridStride + 1;
        var vertices = new Vector3[nx * nz];
        var colors = new Color[nx * nz];
        var uvs = new Vector2[nx * nz];
        var tangents = new Vector4[nx * nz];
        var normals = new Vector3[nx * nz];
        var depthAt = new float[nx * nz];

        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int ix = Mathf.Min(b.n - 1, ix0 + i * WaterGridStride), iz = Mathf.Min(b.n - 1, iz0 + j * WaterGridStride);
                float x = b.WorldX(ix), z = b.WorldZ(iz);
                // > 0 = pod hladinou; len v kotline jazera (jemné zvlnenie zeme inde nesmie vytvoriť mláky)
                float depth = b.lakeAmt[iz, ix] > 0.002f ? WaterLevel - b.rel[iz, ix] : 0f;
                int k = j * nx + i;

                depthAt[k] = depth;
                vertices[k] = new Vector3(x - spec.pos.x, 0f, z - spec.pos.y);
                colors[k] = new Color(Smooth01(depth / WaterDeepMeters), 0f, 0f, Smooth01(depth / WaterShoreFadeMeters));
                uvs[k] = new Vector2(x, z) * 0.1f;
                tangents[k] = new Vector4(1f, 0f, 0f, 1f);
                normals[k] = Vector3.up;
            }

        var triangles = new System.Collections.Generic.List<int>();
        for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                int a = j * nx + i, c = a + 1, d = a + nx, e = d + 1;   // a (x, z), c (x+1, z), d (x, z+1), e (x+1, z+1)
                if (depthAt[a] <= 0f && depthAt[c] <= 0f && depthAt[d] <= 0f && depthAt[e] <= 0f) continue;   // štvorec úplne na brehu
                triangles.Add(a); triangles.Add(d); triangles.Add(c);
                triangles.Add(c); triangles.Add(d); triangles.Add(e);
            }

        var mesh = new Mesh { name = $"{b.theme.key}_{index}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.tangents = tangents;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        if (!AssetDatabase.IsValidFolder(WaterMeshFolder)) AssetDatabase.CreateFolder("Assets/Models", "Water");
        string path = $"{WaterMeshFolder}/{mesh.name}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        return existing;
    }
}
