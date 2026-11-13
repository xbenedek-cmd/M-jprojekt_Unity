using UnityEditor;
using UnityEngine;

/// <summary>
/// Kruhový objazd v strede Centra mesta: dlážděný lem, asfaltový okruh s čiarou, dlažba a obrubník okolo trávnatého kopčeka
/// s malým stromčekom, svietiace stĺpiky, lampy a lavičky. Nahrádza pôvodné námestie s obeliskom.
/// </summary>
public static partial class ProgenyTools
{
    const float RoundaboutRadius = 34f;   // vonkajší okraj dláždeného lemu

    // Valec s presnou kolíziou (primitívny valec má kapsulový collider, ten by nesedel)
    static GameObject Disc(Transform parent, string name, float radius, float baseY, float height, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        Vector3 world = cityRoot.position;
        go.transform.position = new Vector3(world.x, baseY + height * 0.5f, world.z);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;

        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        return go;
    }

    static void BuildRoundabout(Transform parent, float baseY, Material grassMaterial)
    {
        Vector3 center = cityRoot.position;
        GameObject Ring(string n, float r, float h, Material m) => Disc(parent, n, r, baseY, h, m);

        // Pásy zvonka dovnútra, každý o niečo vyšší (bez blikania plôch)
        Ring("ObjazdLem", RoundaboutRadius, 0.09f, mPaving);
        Ring("ObjazdAsfalt", 29f, 0.12f, mAsphalt);
        Ring("ObjazdDlazba", 21.5f, 0.15f, mPaving);
        Ring("ObjazdObrubnik", 19f, 0.32f, mCurb);

        // Kopček: splošťená guľa, spodná polovica schovaná pod obrubníkom
        GameObject mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mound.name = "ObjazdKopcek";
        mound.transform.SetParent(parent, false);
        mound.transform.position = new Vector3(center.x, baseY + 0.32f, center.z);
        mound.transform.localScale = new Vector3(33f, 2.6f, 33f);
        mound.isStatic = true;
        Material turf = grassMaterial != null ? new Material(grassMaterial) { mainTextureScale = new Vector2(8f, 8f) } : Mat(new Color(0.22f, 0.4f, 0.16f));
        mound.GetComponent<Renderer>().sharedMaterial = turf;
        Object.DestroyImmediate(mound.GetComponent<Collider>());
        mound.AddComponent<MeshCollider>().sharedMesh = mound.GetComponent<MeshFilter>().sharedMesh;

        float top = baseY + 0.32f + 1.3f;   // vrchol kopčeka

        // Malý stromček v strede a svetlo naň
        houseTreeCounter = 0;   // Fir_01_Plant (menší)
        HouseTree(parent, new Vector3(center.x, top, center.z), top);
        CLight(cityRoot, new Vector3(0f, 4f, 0f), new Color(1f, 0.85f, 0.6f), 16f, 1.1f);

        ScatterMoundGrass(parent, center, top, baseY + 0.32f);

        // Prerušovaná čiara na okruhu
        for (int i = 0; i < 40; i++)
        {
            float a = i * 9f;
            float rad = a * Mathf.Deg2Rad;
            Vector3 local = new Vector3(Mathf.Cos(rad) * 25.2f, 0.12f, Mathf.Sin(rad) * 25.2f);
            CB(cityRoot, "CiaraObjazd", local, new Vector3(0.28f, 0.015f, 2.4f), mWhite, false, -a, baseY);
        }

        // Svietiace stĺpiky na dlažbe okolo obrubníka
        for (int i = 0; i < 24; i++)
        {
            float rad = i * 15f * Mathf.Deg2Rad;
            Vector3 local = new Vector3(Mathf.Cos(rad) * 20.3f, 0.15f, Mathf.Sin(rad) * 20.3f);
            CB(cityRoot, "StlpikObjazd", local, new Vector3(0.3f, 0.7f, 0.3f), mNeon, false, 0f, baseY);
        }

        // Lampy a lavičky na leme mimo vjazdov (uhly 45° + k·90°)
        for (int k = 0; k < 4; k++)
        {
            float a = 45f + 90f * k;
            float rad = a * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

            Vector3 lamp = dir * 31f;
            CB(cityRoot, "StlpLampy", lamp + Vector3.up * 0.09f, new Vector3(0.25f, 5f, 0.25f), mWoodDark, true, 0f, baseY);
            CB(cityRoot, "Lampa", lamp + Vector3.up * 5.2f, new Vector3(0.7f, 0.5f, 0.7f), mWindow, false, 0f, baseY);
            CLight(cityRoot, lamp + Vector3.up * 5f, new Color(1f, 0.8f, 0.55f), 20f, 1.3f);

            foreach (float shift in new[] { -5.5f, 5.5f })
            {
                Vector3 tangent = new Vector3(-dir.z, 0f, dir.x);
                Vector3 bench = dir * 31.5f + tangent * shift;
                CB(cityRoot, "Lavicka", bench + Vector3.up * 0.09f, new Vector3(3f, 0.5f, 0.9f), mWoodDark, true, -a - 90f, baseY);
            }
        }
    }

    // Tráva na kopčeku: detailové prefaby z Grass Pack (veľkosť a materiál už nastavené), výška podľa tvaru kopčeka
    static void ScatterMoundGrass(Transform parent, Vector3 center, float top, float rimY)
    {
        var prefabs = new System.Collections.Generic.List<GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("_detail t:Prefab", new[] { "Assets/Grass Pack/DetailPrefabs" }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab != null) prefabs.Add(prefab);
        }
        if (prefabs.Count == 0) return;

        var group = new GameObject("ObjazdTrava").transform;
        group.SetParent(parent, false);

        var rng = new System.Random(4711);
        const float moundRadius = 16.5f;
        float height = top - rimY;
        for (int i = 0; i < 220; i++)
        {
            double angle = rng.NextDouble() * Mathf.PI * 2.0;
            float r = Mathf.Sqrt((float)rng.NextDouble()) * (moundRadius - 1.8f);
            if (r < 1.2f) continue;   // stred patrí stromčeku

            float y = rimY + height * Mathf.Sqrt(Mathf.Max(0f, 1f - (r * r) / (moundRadius * moundRadius)));
            var pos = new Vector3(center.x + Mathf.Cos((float)angle) * r, y - 0.03f, center.z + Mathf.Sin((float)angle) * r);

            var tuft = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[rng.Next(prefabs.Count)], group);
            tuft.transform.position = pos;
            tuft.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            tuft.transform.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 0.7f);
            foreach (Collider c in tuft.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        }
    }
}
