using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Okraj divočiny bez múru a bez prázdna: kamenné múry po obvode (Utes_*) sa prestanú kresliť a ostanú len ako neviditeľná
/// zábrana, a terén za okrajom pokračuje vygenerovaným prstencom kopcov a hôr (svah hneď za hranicou, hory v diaľke).
/// Prstenec je len kulisa bez kolízie, stavia sa pri prvom zapnutí zóny a kreslí sa iba keď je hráč v nej
/// (inak by hory jednej zóny zakryli susednú). Pridáva sa sám ku každému koreňu „Divocina*“.
/// </summary>
public class WildEdge : MonoBehaviour
{
    private const float Cell = 10f;          // veľkosť políčka siete (m); hranica terénu musí ležať na čiare siete
    private const float Outer = 340f;        // ako ďaleko za hranicu prstenec siaha
    private const float WallHeight = 110f;   // neviditeľná zábrana: nedá sa preskočiť ani preletieť

    private GameObject ring;
    private Transform viewer;
    private Vector3 center;
    private float half;
    private float nextCheck;
    private bool built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        AttachAll(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => AttachAll(scene);

    static void AttachAll(UnityEngine.SceneManagement.Scene scene)
    {
        if (!scene.IsValid()) return;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name.StartsWith("Divocina") && root.GetComponent<WildEdge>() == null) root.AddComponent<WildEdge>();
    }

    void OnEnable()
    {
        if (!built) Build();
    }

    void Update()
    {
        if (ring == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.5f;
        if (viewer == null)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            viewer = cam.transform;
        }
        Vector3 p = viewer.position;
        bool inside = Mathf.Abs(p.x - center.x) < half + 40f && Mathf.Abs(p.z - center.z) < half + 40f;
        if (ring.activeSelf != inside) ring.SetActive(inside);
    }

    void Build()
    {
        built = true;
        Terrain terrain = GetComponentInChildren<Terrain>(true);
        if (terrain == null || terrain.terrainData == null) return;

        TerrainData data = terrain.terrainData;
        Vector3 size = data.size;
        half = Mathf.Round(Mathf.Min(size.x, size.z) * 0.5f / Cell) * Cell;
        center = terrain.transform.position + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
        float edgeY = terrain.transform.position.y + data.GetInterpolatedHeight(0.5f, 0f);

        // Múry: viditeľná časť preč, kolízia ostáva a je vyššia
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Utes_")) continue;
            Renderer wall = child.GetComponent<Renderer>();
            if (wall != null) wall.enabled = false;
            BoxCollider box = child.GetComponent<BoxCollider>();
            if (box != null)
            {
                Vector3 s = box.size;
                s.y = WallHeight * 2f / Mathf.Max(0.01f, Mathf.Abs(child.lossyScale.y));
                box.size = s;
            }
        }

        // Povrch: prvá vrstva terénu (tráva / piesok / bahno podľa zóny) a kamenistá vrstva terénu ako skala
        TerrainLayer[] layers = data.terrainLayers;
        TerrainLayer layer = layers != null && layers.Length > 0 ? layers[0] : null;
        TerrainLayer stones = layer;
        if (layers != null)
            foreach (TerrainLayer l in layers)
                if (l != null && (l.name.Contains("Stone") || l.name.Contains("Rock"))) { stones = l; break; }

        Material surface;
        Shader shader = Resources.Load<Shader>("WildEdge");
        if (shader != null && layer != null && layer.diffuseTexture != null)
        {
            surface = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            surface.SetTexture("_MainTex", layer.diffuseTexture);
            surface.SetTexture("_RockTex", stones.diffuseTexture != null ? stones.diffuseTexture : layer.diffuseTexture);
            surface.SetFloat("_GroundTile", Mathf.Max(1f, layer.tileSize.x));
            surface.SetFloat("_RockTile", 14f);
        }
        else
        {
            surface = new Material(Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave, color = new Color(0.3f, 0.36f, 0.24f) };
            surface.SetFloat("_Glossiness", 0.05f);
        }

        ring = new GameObject("Okraj_Hory");
        ring.transform.SetParent(transform, false);
        ring.transform.position = new Vector3(center.x, edgeY, center.z);
        ring.transform.rotation = Quaternion.identity;
        ring.AddComponent<MeshFilter>().sharedMesh = BuildMesh(name.GetHashCode());
        MeshRenderer renderer = ring.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = surface;
        renderer.shadowCastingMode = ShadowCastingMode.Off;   // tiene hôr by padali cez celú zónu
        renderer.receiveShadows = false;
    }

    // Štvorcová sieť okolo terénu; vnútro (kde je skutočný terén) sa vynechá. Výška rastie so vzdialenosťou od hranice.
    Mesh BuildMesh(int seed)
    {
        int inner = Mathf.RoundToInt(half / Cell);
        int outer = Mathf.RoundToInt(Outer / Cell);
        int cells = (inner + outer) * 2;
        int side = cells + 1;
        float extent = (inner + outer) * Cell;
        float ox = (seed & 0xff) * 1.37f, oz = ((seed >> 8) & 0xff) * 2.11f;

        var vertices = new Vector3[side * side];
        for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                float px = -extent + x * Cell, pz = -extent + z * Cell;
                float dx = Mathf.Max(0f, Mathf.Abs(px) - half), dz = Mathf.Max(0f, Mathf.Abs(pz) - half);
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float wx = px + center.x, wz = pz + center.z;

                float broad = Mathf.PerlinNoise(wx * 0.009f + ox, wz * 0.009f + oz);
                float mid = Mathf.PerlinNoise(wx * 0.031f + oz, wz * 0.031f + ox);
                float fine = Mathf.PerlinNoise(wx * 0.11f, wz * 0.11f);

                // Hneď za hranicou svah (aby neviditeľná zábrana dávala zmysel), ďalej kopce a v diaľke hory
                float bank = Mathf.SmoothStep(0f, 1f, d / 16f) * (5f + 8f * mid);
                float rise = Mathf.SmoothStep(0f, 1f, d / 85f);   // hory musia stáť bližšie než koniec dohľadu kamery
                float h = bank + rise * (26f + 95f * broad * broad + 16f * mid) + Mathf.SmoothStep(0f, 1f, (d - 10f) / 50f) * 5f * fine;
                if (d <= 0f) h = 0f;

                vertices[z * side + x] = new Vector3(px, h, pz);
            }

        var triangles = new List<int>(cells * cells * 6);
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                // Políčko celé vnútri terénu sa nekreslí
                bool insideX = x >= outer && x < outer + inner * 2;
                bool insideZ = z >= outer && z < outer + inner * 2;
                if (insideX && insideZ) continue;

                int a = z * side + x, b = a + 1, c = a + side, e = c + 1;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(e);
            }

        var mesh = new Mesh { name = "Okraj_Hory", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = vertices;
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();

        // Podiel skaly: strmé svahy a vysoké polohy (plynulý prechod, nie po políčkach)
        Vector3[] normals = mesh.normals;
        var colors = new Color32[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            float steep = Mathf.InverseLerp(0.93f, 0.72f, normals[i].y);
            float high = Mathf.InverseLerp(34f, 70f, vertices[i].y);
            float patch = Mathf.PerlinNoise(vertices[i].x * 0.05f + ox, vertices[i].z * 0.05f + oz) * 0.35f - 0.15f;
            byte rock = (byte)(Mathf.Clamp01(Mathf.Max(steep, high) + patch * Mathf.Clamp01(vertices[i].y / 12f)) * 255f);
            colors[i] = new Color32(rock, 0, 0, 255);
        }
        mesh.colors32 = colors;
        mesh.RecalculateBounds();
        return mesh;
    }
}
