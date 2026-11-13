using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Kopce v Divočinách (nie v meste): 6 hladkých pahorkov na zónu z procedurálnej siete (4 varianty v Assets/Models/Hills), s kolíziou a trávnatým
/// povrchom zóny. Sklon do ~27°, takže sa na ne dá vyjsť. Už stojace objekty pod kopcom sa vyzdvihnú na jeho povrch, jazierka sa zmažú.
/// </summary>
public static partial class ProgenyTools
{
    const string HillFolder = "Assets/Models/Hills";
    const float HillMeshRadius = 24f;    // polomer kopca pri mierke 1 (m)
    const float HillMeshHeight = 7f;     // výška kopca pri mierke 1 (m)
    const float HillMeshHalfSize = 26f;  // polovica strany siete

    // Stredy kopcov voči stredu zóny (x, z); mimo lokalít a portálov, v dosahu útesov nie
    // x, z, najmenšia a najväčšia mierka; dva veľké a vysoké kopce (mierka ~1,5–1,7 = polomer ~38 m, výška ~11 m), zvyšok menší
    static readonly Vector4[] hillSpots =
    {
        new Vector4(-64f, -28f, 1.5f, 1.7f), new Vector4(68f, -34f, 1.4f, 1.6f),
        new Vector4(-36f, -80f, 0.8f, 1f), new Vector4(40f, -82f, 0.75f, 0.95f), new Vector4(-84f, 86f, 0.95f, 1.15f), new Vector4(84f, 86f, 1f, 1.2f)
    };

    // Kopce práve vytvorenej zóny (generátor sa im pri rozmiestňovaní vyhýba)
    static readonly List<Vector3> activeHills = new List<Vector3>();   // x, z, polomer

    static bool InHill(Vector3 p)
    {
        foreach (Vector3 h in activeHills)
        {
            float dx = p.x - h.x, dz = p.z - h.y;
            if (dx * dx + dz * dz < (h.z + 3f) * (h.z + 3f)) return true;
        }
        return false;
    }

    static Mesh HillMesh(int variant)
    {
        string path = $"{HillFolder}/Hill_{variant}.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(HillFolder)) AssetDatabase.CreateFolder("Assets/Models", "Hills");

        const int n = 40;
        var rnd = new System.Random(1000 + variant * 77);
        float p1 = (float)rnd.NextDouble() * 6.28f, p2 = (float)rnd.NextDouble() * 6.28f, p3 = (float)rnd.NextDouble() * 6.28f, p4 = (float)rnd.NextDouble() * 6.28f;

        var verts = new Vector3[(n + 1) * (n + 1)];
        var uvs = new Vector2[verts.Length];
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
            {
                float x = (i / (float)n * 2f - 1f) * HillMeshHalfSize;
                float z = (j / (float)n * 2f - 1f) * HillMeshHalfSize;
                float angle = Mathf.Atan2(z, x);
                float d = Mathf.Sqrt(x * x + z * z) / (HillMeshRadius * (1f + 0.18f * Mathf.Sin(angle * 2f + p4)));   // nepravidelný pôdorys

                float baseHeight = d < 1f ? Mathf.Pow(1f - d * d, 2f) : 0f;   // hladký okraj s nulovým sklonom
                float noise = 1f + 0.22f * (Mathf.Sin(x * 0.17f + p1) + Mathf.Sin(z * 0.13f + p2) + 0.6f * Mathf.Sin((x + z) * 0.11f + p3));

                verts[j * (n + 1) + i] = new Vector3(x, baseHeight * Mathf.Max(0.3f, noise) * HillMeshHeight, z);
                uvs[j * (n + 1) + i] = new Vector2(x / 5f, z / 5f);
            }

        var tris = new List<int>();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = (j + 1) * (n + 1) + i, c = (j + 1) * (n + 1) + i + 1, e = j * (n + 1) + i + 1;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(e);
            }

        Mesh mesh = new Mesh { name = $"Hill_{variant}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>Vytvorí kopce zóny (ak ešte nie sú) a zaznamená ich pôdorysy. Vráti počet kopcov.</summary>
    static int BuildHills(GameObject root, WildTheme theme, Vector3 center, float groundY)
    {
        activeHills.Clear();

        // Ak má zóna skutočný terén, kopce sú v ňom (mesh kopce sa nevytvárajú); generátor sa im vyhýba podľa ich pôdorysov
        if (FindWildTerrain(root) != null)
        {
            foreach (Vector4 h in WildHillList(theme, center)) activeHills.Add(new Vector3(h.x, h.y, h.z * 1.05f));
            return 0;
        }

        Transform oldHills = root.transform.Find("Kopce");   // prvá verzia (menšie kopce): zmaže sa a nahradí novými
        if (oldHills != null) Object.DestroyImmediate(oldHills.gameObject);
        Transform holder = root.transform.Find("Kopce_v2");
        bool existed = holder != null;
        if (!existed)
        {
            holder = new GameObject("Kopce_v2").transform;
            holder.SetParent(root.transform);
        }

        Material ground = HillMaterial(theme);
        var rnd = new System.Random(theme.key.GetHashCode() ^ 0x7a31);

        int index = 0;
        foreach (Vector4 spot in hillSpots)
        {
            float sx = Mathf.Lerp(spot.z, spot.w, (float)rnd.NextDouble());
            float sy = sx * (0.85f + (float)rnd.NextDouble() * 0.3f);
            Vector2 jitter = new Vector2((float)rnd.NextDouble() * 8f - 4f, (float)rnd.NextDouble() * 8f - 4f);
            Vector3 pos = new Vector3(center.x + spot.x + jitter.x, groundY - 0.05f, center.z + spot.y + jitter.y);   // y = spot.y je z

            if (!existed)
            {
                GameObject hill = new GameObject("Kopec");
                hill.transform.SetParent(holder);
                hill.transform.position = pos;
                hill.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                hill.transform.localScale = new Vector3(sx, sy, sx);

                Mesh mesh = HillMesh(index % 4);
                hill.AddComponent<MeshFilter>().sharedMesh = mesh;
                hill.AddComponent<MeshRenderer>().sharedMaterial = ground;
                hill.AddComponent<MeshCollider>().sharedMesh = mesh;
                hill.isStatic = true;
            }

            index++;
        }

        // Kopce vytvorené skôr dostanú správny trávnatý materiál (predtým mali dlaždice zeme zóny, ktoré sa na sieti s vlastným UV zlievali do jednej farby)
        foreach (Transform hill in holder)
        {
            MeshRenderer hr = hill.GetComponent<MeshRenderer>();
            if (hr != null) hr.sharedMaterial = ground;
        }

        // Pôdorysy z reálnych kopcov (aj pri opakovanom spustení)
        foreach (Transform hill in holder)
            activeHills.Add(new Vector3(hill.position.x, hill.position.z, HillMeshRadius * hill.localScale.x * 1.15f));

        Physics.SyncTransforms();
        return holder.childCount;
    }

    /// <summary>Objekty, ktoré stoja v pôdoryse kopca, sa vyzdvihnú na jeho povrch (jazierka sa zmažú).</summary>
    static int LiftOntoHills(GameObject root, float groundY)
    {
        Physics.SyncTransforms();
        int lifted = 0;
        var names = new HashSet<string>
        {
            "Strom", "Smrek", "Mŕtvy strom", "Skala", "Vrak", "Huba", "Ruina", "Tabor", "Monolit", "Kryštály",
            "Zdroj_bylina", "Zdroj_drevo", "Zdroj_kameň", "Zdroj_kryštál", "Zdroj_šrot"
        };

        var targets = new List<Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root.transform && (names.Contains(t.name) || t.name == "Rybník")) targets.Add(t);

        foreach (Transform t in targets)
        {
            if (t == null) continue;

            // Najprv späť na rovnú zem (objekt mohol byť vyzdvihnutý na starší kopec, ktorý sa zmazal)
            if (t.name != "Rybník" && Mathf.Abs(t.position.y - groundY) > 0.01f)
                t.position = new Vector3(t.position.x, groundY, t.position.z);

            if (!InHill(t.position)) continue;
            if (t.name == "Rybník") { Object.DestroyImmediate(t.gameObject); continue; }

            Vector3 origin = new Vector3(t.position.x, groundY + 60f, t.position.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.collider.name != "Kopec") continue;
                t.position = new Vector3(t.position.x, h.point.y, t.position.z);
                lifted++;
                break;
            }
        }
        return lifted;
    }
}

public static partial class ProgenyTools
{
    // Materiál kopcov: tráva zóny, ale s mierkou 1 (UV siete kopca je už v metroch / 5, na rozdiel od veľkej plochej zeme, ktorej mierka je 48×)
    static Material HillMaterial(WildTheme theme)
    {
        EnsureSurfaceMaterials(false);
        Material surface = LoadSurface(GroundSurfaceFor(theme.key));
        string path = $"{SurfacesFolder}/Kopec_{theme.key}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (surface == null)
        {
            if (m != null) return m;
            m = new Material(Shader.Find("Standard")) { color = theme.ground };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        bool isNew = m == null;
        if (isNew) m = new Material(surface);
        else EditorUtility.CopySerialized(surface, m);

        m.color = Color.Lerp(Color.white, theme.ground * 2.2f, 0.45f);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", 0.08f);
        m.mainTextureScale = Vector2.one;
        if (isNew) AssetDatabase.CreateAsset(m, path);
        EditorUtility.SetDirty(m);
        return m;
    }
}
