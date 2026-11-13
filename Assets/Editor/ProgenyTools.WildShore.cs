using UnityEngine;

/// <summary>
/// Dokončenie rybníkov: okolo brehu rákosie s gaštanmi, kamene pri vode a na hladine leknové listy s kvetmi.
/// Breh sa hľadá z výškovej mapy (kde dno prechádza nad hladinu), takže sedí na skutočný tvar jazera.
/// </summary>
public static partial class ProgenyTools
{
    const string ShoreGroupName = "Breh_Rybnika";

    static void BuildLakeShore(GameObject root, TerrainBuild b)
    {
        foreach (Transform old in root.GetComponentsInChildren<Transform>(true))
            if (old != null && old.name == ShoreGroupName && old.parent != null) Object.DestroyImmediate(old.gameObject);

        Transform decor = root.transform.Find("Dekoracie");
        Transform parent = decor != null ? decor : root.transform;

        Material reed = Mat(new Color(0.3f, 0.36f, 0.15f));
        Material cattail = Mat(new Color(0.24f, 0.13f, 0.07f));
        Material pad = Mat(new Color(0.1f, 0.36f, 0.14f));
        Material flower = Mat(new Color(0.95f, 0.6f, 0.75f));
        Material stone = SurfaceOrColor("castle_wall", new Color(0.36f, 0.37f, 0.38f), 1f);

        int index = 0;
        foreach (LakeSpec spec in b.lakes)
        {
            var rnd = new System.Random(b.theme.key.GetHashCode() ^ (index++ * 7919));
            var group = new GameObject(ShoreGroupName);
            group.transform.SetParent(parent, false);

            float waterY = b.groundY + WaterLevel;
            float maxReach = spec.lake.basinRadius * LakeShapeMaxWarp * 1.1f;
            float sum = 0f;
            int count = 0;

            // Obrys brehu: od stredu von, kým zem nevystúpi nad hladinu
            for (float angle = 0f; angle < 360f; angle += 8f)
            {
                Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
                float r = 2f;
                while (r < maxReach)
                {
                    float x = spec.pos.x + dir.x * r, z = spec.pos.y + dir.z * r;
                    if (b.Sample(b.rel, x, z) > WaterLevel + 0.12f) break;
                    r += 0.5f;
                }
                if (r >= maxReach) continue;
                sum += r;
                count++;

                float shoreSlope = Mathf.Abs(b.Sample(b.rel, spec.pos.x + dir.x * (r + 2f), spec.pos.y + dir.z * (r + 2f)) - b.Sample(b.rel, spec.pos.x + dir.x * r, spec.pos.y + dir.z * r));
                if (shoreSlope > 2.2f) continue;   // príkry breh bez rákosia

                if (rnd.NextDouble() < 0.6)
                {
                    float rr = r + (float)rnd.NextDouble() * 0.8f - 0.2f;
                    Vector3 center = new Vector3(spec.pos.x + dir.x * rr, 0f, spec.pos.y + dir.z * rr);
                    center.y = b.groundY + b.Sample(b.rel, center.x, center.z);
                    int stems = 4 + rnd.Next(3);
                    for (int s = 0; s < stems; s++)
                    {
                        Vector3 o = new Vector3((float)rnd.NextDouble() - 0.5f, 0f, (float)rnd.NextDouble() - 0.5f) * 0.9f;
                        float h = RandomRange(rnd, 1.1f, 2.3f);
                        GameObject stem = Prim(PrimitiveType.Cylinder, "Rakos", group.transform, center + o + Vector3.up * h * 0.5f, new Vector3(0.07f, h * 0.5f, 0.07f), reed, false);
                        stem.transform.rotation = Quaternion.Euler(RandomRange(rnd, -7f, 7f), 0f, RandomRange(rnd, -7f, 7f));
                        if (s % 3 == 0)
                            Prim(PrimitiveType.Cylinder, "Gastan", group.transform, center + o + Vector3.up * (h + 0.1f), new Vector3(0.14f, 0.18f, 0.14f), cattail, false);
                    }
                }
                if (rnd.NextDouble() < 0.28)
                {
                    float rr = r + (float)rnd.NextDouble() * 1.4f - 0.4f;
                    Vector3 p = new Vector3(spec.pos.x + dir.x * rr, 0f, spec.pos.y + dir.z * rr);
                    p.y = b.groundY + b.Sample(b.rel, p.x, p.z);
                    float s = RandomRange(rnd, 0.5f, 1.3f);
                    GameObject rock = Prim(PrimitiveType.Sphere, "Kamen", group.transform, p + Vector3.up * s * 0.12f, new Vector3(s, s * 0.55f, s * RandomRange(rnd, 0.7f, 1f)), stone, false);
                    rock.transform.rotation = Quaternion.Euler(0f, RandomRange(rnd, 0f, 360f), 0f);
                }
            }

            // Leknové listy v plytkej vode
            float avg = count > 0 ? sum / count : spec.lake.waterRadius;
            int pads = 0;
            for (int attempt = 0; attempt < 80 && pads < 14; attempt++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)rnd.NextDouble()) * avg * 0.85f;
                float x = spec.pos.x + Mathf.Cos(a) * d, z = spec.pos.y + Mathf.Sin(a) * d;
                float depth = WaterLevel - b.Sample(b.rel, x, z);
                if (depth < 0.3f || depth > 1.8f) continue;

                float size = RandomRange(rnd, 0.5f, 0.95f);
                Prim(PrimitiveType.Cylinder, "Leknovy_list", group.transform, new Vector3(x, waterY + 0.03f, z), new Vector3(size, 0.01f, size), pad, false);
                if (rnd.NextDouble() < 0.3)
                    Prim(PrimitiveType.Sphere, "Lekno_kvet", group.transform, new Vector3(x, waterY + 0.1f, z), new Vector3(0.22f, 0.14f, 0.22f), flower, false);
                pads++;
            }
        }
    }
}
