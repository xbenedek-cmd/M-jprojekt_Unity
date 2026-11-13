using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 3D tráva na teréne divočín z balíka Assets/Grass Pack (grass01–06, clover01–02). Pre každý model sa vytvorí jednoduchý prefab
/// (jedna sieť + materiály z balíka) a použije sa ako detail terénu, takže sa kreslí len v okolí hráča (vzdialenosť detailov).
/// Tráva rastie len na vrstve trávy: nie na ceste, skale, brehu a vo vode. Husté lúky a plešiny sa striedajú podľa šumu.
/// </summary>
public static partial class ProgenyTools
{
    // ======== NASTAVENIA TRÁVY ========
    const string GrassPackFolder = "Assets/Grass Pack";
    const string GrassDetailFolder = "Assets/Grass Pack/DetailPrefabs";
    const float GrassHeightMeters = 0.55f;      // priemerná výška trávy (m); zmenši pre nižšiu trávu
    const float CloverHeightMeters = 0.2f;      // najväčší rozmer ďateliny (m)
    const int GrassDetailResolution = 560;      // bunka ≈ 0,75 m aj na zóne 420 m (rovnaká hustota ako predtým)
    const int GrassMaxPerCell = 4;              // najviac kusov trávy v jednej bunke
    const float GrassMeadowScale = 0.045f;      // veľkosť lúk a plešín (menej = väčšie plochy)
    const float GrassMinGrassWeight = 0.55f;    // minimálny podiel vrstvy trávy v mieste

    // Koľko trávy je v ktorej zóne (0 = žiadna, 1 = plná lúka)
    static float GrassAmount(string themeKey)
    {
        switch (themeKey)
        {
            case "Les": return 1f;
            case "Mociare": return 0.95f;
            case "Divocina": return 0.9f;
            case "Pustatina": return 0.4f;
            case "Bane": return 0.05f;
            default: return 0.3f;   // kaňon
        }
    }

    static string[] GrassModelsFor(string themeKey)
    {
        switch (themeKey)
        {
            case "Les": return new[] { "grass02", "grass03", "grass05", "clover01" };
            case "Mociare": return new[] { "grass04", "grass06", "grass05", "clover02" };
            case "Pustatina": return new[] { "grass01", "grass03" };
            case "Kanon": return new[] { "grass01", "grass04" };
            default: return new[] { "grass01", "grass02", "grass04", "grass06", "clover01", "clover02" };
        }
    }

    // Jednoduchý prefab pre detail terénu: koreň musí mať MeshFilter a MeshRenderer (originál má LOD skupinu v potomkoch).
    // Sieť sa "upečie" so svojou polohou, natočením a mierkou v prefabe (FBX modely bývajú otočené Z-hore alebo v iných jednotkách),
    // takže detail terénu dostane správne orientovanú sieť v metroch so spodkom na y = 0.
    // referenceSize = výška siete (tráva) alebo najväčší rozmer (ďatelina je plochá a široká); podľa neho sa počíta veľkosť v metroch.
    static GameObject GrassDetailPrefab(string modelName, out float referenceSize, out float meshHeight)
    {
        referenceSize = 1f;
        meshHeight = 1f;
        string source = $"{GrassPackFolder}/Prefabs/{modelName}.prefab";
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(source);
        if (asset == null) return null;

        if (!AssetDatabase.IsValidFolder(GrassDetailFolder)) AssetDatabase.CreateFolder(GrassPackFolder, "DetailPrefabs");
        string path = $"{GrassDetailFolder}/{modelName}_detail.prefab";

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        try
        {
            // Z LOD skupiny sa vyberie stredná podrobnosť: sieť s druhým najväčším počtom vrcholov (alebo jediná)
            var sorted = new List<MeshFilter>(instance.GetComponentsInChildren<MeshFilter>(true));
            sorted.RemoveAll(f => f.sharedMesh == null);
            if (sorted.Count == 0) return null;
            sorted.Sort((a, c) => c.sharedMesh.vertexCount.CompareTo(a.sharedMesh.vertexCount));
            MeshFilter chosen = sorted[Mathf.Min(1, sorted.Count - 1)];

            Matrix4x4 toRoot = instance.transform.worldToLocalMatrix * chosen.transform.localToWorldMatrix;
            Mesh baked = BakeGrassMesh(chosen.sharedMesh, toRoot, $"{GrassDetailFolder}/{modelName}_detail.asset");

            Vector3 size = baked.bounds.size;
            meshHeight = Mathf.Max(0.001f, size.y);
            referenceSize = modelName.StartsWith("clover") ? Mathf.Max(0.001f, Mathf.Max(size.x, Mathf.Max(size.y, size.z))) : meshHeight;

            var go = new GameObject(modelName + "_detail");
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            MeshRenderer sourceRenderer = chosen.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GrassDetailMaterial(modelName, sourceRenderer != null && sourceRenderer.sharedMaterial != null ? sourceRenderer.sharedMaterial : null, meshHeight);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // tráva netieni (výkon)

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static Mesh BakeGrassMesh(Mesh src, Matrix4x4 m, string assetPath)
    {
        Matrix4x4 normalMatrix = m.inverse.transpose;
        Vector3[] vertices = src.vertices;
        Vector3[] normals = src.normals;
        Vector4[] tangents = src.tangents;

        float minY = float.MaxValue;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = m.MultiplyPoint3x4(vertices[i]);
            minY = Mathf.Min(minY, vertices[i].y);
        }
        for (int i = 0; i < vertices.Length; i++) vertices[i].y -= minY;   // spodok trávy na zemi

        for (int i = 0; i < normals.Length; i++) normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
        for (int i = 0; i < tangents.Length; i++)
        {
            Vector3 t = m.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
            tangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
        }

        var baked = new Mesh { name = System.IO.Path.GetFileNameWithoutExtension(assetPath), indexFormat = src.indexFormat };
        baked.vertices = vertices;
        if (normals.Length == vertices.Length) baked.normals = normals;
        if (tangents.Length == vertices.Length) baked.tangents = tangents;
        baked.uv = src.uv;
        baked.colors = src.colors;
        baked.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++) baked.SetTriangles(src.GetTriangles(s), s);
        baked.RecalculateBounds();

        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(baked, assetPath);
            return baked;
        }
        EditorUtility.CopySerialized(baked, existing);
        Object.DestroyImmediate(baked);
        return existing;
    }

    // Materiál s vlastným shaderom Progeny/GrassDetail (orezanie, vietor, GPU instancing); textúry a farba z materiálu balíka
    static Material GrassDetailMaterial(string modelName, Material source, float meshHeight)
    {
        Shader shader = Shader.Find("Progeny/GrassDetail");
        if (shader == null)
        {
            Debug.LogError("Shader Progeny/GrassDetail sa nenašiel (Assets/Shaders/GrassDetail.shader). Počkaj na import a spusti terén znova.");
            return source;
        }

        string path = $"{GrassDetailFolder}/{modelName}_detail.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        m.enableInstancing = true;

        if (source != null)
        {
            Texture albedo = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            Texture normal = source.HasProperty("_Normal") ? source.GetTexture("_Normal") : (source.HasProperty("_BumpMap") ? source.GetTexture("_BumpMap") : null);
            m.SetTexture("_MainTex", albedo);
            if (normal != null) m.SetTexture("_BumpMap", normal);
            Color tint = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            m.SetColor("_Color", new Color(Mathf.Min(1f, tint.r * 1.25f), Mathf.Min(1f, tint.g * 1.25f), Mathf.Min(1f, tint.b * 1.25f), 1f));
            m.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? Mathf.Clamp(source.GetFloat("_Cutoff"), 0.2f, 0.6f) : 0.4f);
        }
        m.SetFloat("_MeshHeight", meshHeight);
        EditorUtility.SetDirty(m);
        return m;
    }

    static void PaintGrassDetails(TerrainBuild b, TerrainData data, float[,,] alphamaps, int alphaRes)
    {
        float amount = GrassAmount(b.theme.key);
        var prototypes = new List<DetailPrototype>();
        foreach (string model in GrassModelsFor(b.theme.key))
        {
            GameObject prefab = GrassDetailPrefab(model, out float referenceSize, out float meshHeight);
            if (prefab == null) continue;

            // Veľkosť sa berie v metroch (modely z balíka majú rôzne jednotky): k = cieľ / rozmer siete
            bool clover = model.StartsWith("clover");
            float k = (clover ? CloverHeightMeters : GrassHeightMeters) / referenceSize;
            prototypes.Add(new DetailPrototype
            {
                prototype = prefab,
                usePrototypeMesh = true,
                useInstancing = true,                  // materiál používa Progeny/GrassDetail, ktorý instancing podporuje
                renderMode = DetailRenderMode.VertexLit,
                minWidth = k * 0.85f, maxWidth = k * 1.2f,
                minHeight = k * 0.7f, maxHeight = k * 1.3f,
                noiseSeed = 100 + prototypes.Count * 17,
                noiseSpread = 0.4f,
                healthyColor = Color.white,
                dryColor = Color.Lerp(Color.white, new Color(0.85f, 0.8f, 0.6f), 0.5f),
                density = 1f,
                alignToGround = 0.95f,                 // tráva sa nakláňa podľa sklonu kopca (nie vždy zvislo)
                positionJitter = 0.5f
            });
        }

        // Kapradiny, rastliny a tráva z balíka Forest Environment (majú správnu veľkosť a vlastný shader balíka)
        foreach (string natureName in NatureDetailsFor(b.theme.key))
        {
            GameObject naturePrefab = NatureDetailPrefab(natureName);
            if (naturePrefab == null) continue;

            prototypes.Add(new DetailPrototype
            {
                prototype = naturePrefab,
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minWidth = 0.85f, maxWidth = 1.3f,
                minHeight = 0.85f, maxHeight = 1.3f,
                noiseSeed = 300 + prototypes.Count * 13,
                noiseSpread = 0.4f,
                healthyColor = Color.white,
                dryColor = Color.white,
                density = 1f,
                alignToGround = 0.95f,                 // tráva sa nakláňa podľa sklonu kopca (nie vždy zvislo)
                positionJitter = 0.5f
            });
        }

        if (prototypes.Count == 0 || amount <= 0f)
        {
            data.detailPrototypes = new DetailPrototype[0];
            Debug.LogWarning($"Tráva ({b.theme.displayName}): nenašli sa modely v {GrassPackFolder}/Prefabs, tráva sa nepridala.");
            return;
        }

        data.SetDetailResolution(GrassDetailResolution, 16);
        data.detailPrototypes = prototypes.ToArray();
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

        var layers = new int[prototypes.Count][,];
        for (int i = 0; i < layers.Length; i++) layers[i] = new int[GrassDetailResolution, GrassDetailResolution];

        var rnd = new System.Random(b.theme.key.GetHashCode() ^ 0x77aa);
        float o1 = (float)rnd.NextDouble() * 400f, o2 = (float)rnd.NextDouble() * 400f, o3 = (float)rnd.NextDouble() * 400f;
        long placed = 0;

        for (int dz = 0; dz < GrassDetailResolution; dz++)
            for (int dx = 0; dx < GrassDetailResolution; dx++)
            {
                float u = dx / (float)(GrassDetailResolution - 1), v = dz / (float)(GrassDetailResolution - 1);
                float x = b.originX + u * TerrainSize, z = b.originZ + v * TerrainSize;

                int ax = Mathf.Clamp(Mathf.RoundToInt(u * (alphaRes - 1)), 0, alphaRes - 1);
                int az = Mathf.Clamp(Mathf.RoundToInt(v * (alphaRes - 1)), 0, alphaRes - 1);
                if (alphamaps[az, ax, 0] < GrassMinGrassWeight) continue;               // nie na ceste, skale a brehu
                if (b.Sample(b.lakeAmt, x, z) > 0.005f) continue;                       // nie vo vode ani v kotline jazera

                // Lúky: šum rozhodne, kde je tráva hustá a kde je plešina
                float meadow = Mathf.PerlinNoise(x * GrassMeadowScale + o1, z * GrassMeadowScale + o2);
                float chance = Mathf.Clamp01((meadow - 0.22f) * 2.4f) * amount * alphamaps[az, ax, 0];   // plešiny ostávajú, lúk je viac
                if (rnd.NextDouble() > chance) continue;

                int count = 1 + (int)(rnd.NextDouble() * GrassMaxPerCell);
                int kind = Mathf.Clamp((int)(Mathf.PerlinNoise(x * 0.03f + o3, z * 0.03f + o1) * layers.Length), 0, layers.Length - 1);   // druhy tvoria škvrny
                if (layers.Length > 1 && rnd.NextDouble() < 0.2) kind = rnd.Next(layers.Length);
                layers[kind][dz, dx] = Mathf.Min(count, GrassMaxPerCell);
                placed += count;
            }

        for (int i = 0; i < layers.Length; i++) data.SetDetailLayer(0, 0, i, layers[i]);
        Debug.Log($"Tráva ({b.theme.displayName}): {prototypes.Count} druhov, asi {placed} kusov.");
    }
}
