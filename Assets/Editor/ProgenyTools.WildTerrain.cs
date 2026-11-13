using System.Collections.Generic;

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Terén divočín: z rovnej dosky zóny urobí skutočný Unity Terrain.
///  - 6 kopcov na zónu (2 dominantné), hladké prechody, vyhladenie a obmedzenie sklonu,
///  - cesty od portálu k lokalitám hľadané po vrstevniciach (A*) a zarovnávajúce terén pod sebou,
///  - jazerá s reálnou kotlinou (dno 3–6 m pod okolím, plynulý breh) a hladinou swamp_water,
///  - všetky objekty (stromy, skaly, zdroje, tábory, ruiny, démoni) sa dostanú na povrch terénu a musia byť v hraniciach
///    mapy s odsadením BoundsMargin, mimo vody, ciest a príkrych svahov; inak sa presunú, alebo zmažú.
/// Menu: Progeny → Divočiny → Terén, cesty, jazerá a hranice. Spúšťa sa aj z "Aktualizovať všetko" a po vytvorení Divočín. Je opakovateľné.
/// </summary>
public static partial class ProgenyTools
{
    // ======== NASTAVENIA (tu sa dá ladiť) ========
    const int TerrainResolution = 1025;         // rozlíšenie výškovej mapy (2^n + 1), bunka ≈ 0,41 m
    const float TerrainSize = 420f;             // strana zóny (m)
    const float TerrainHeightRange = 40f;       // celkový rozsah výšky terénu (m)
    const float TerrainBaseDepth = 8f;          // o koľko metrov leží dno terénu pod úrovňou rovnej zeme zóny

    // Kopce
    const float MaxSlopeDeg = 27f;              // najstrmší povolený sklon po vyhladení (stupne)
    const float RollingNoiseMeters = 1.2f;      // amplitúda jemného zvlnenia zeme

    // Jazerá a močiare
    const float LakeDepthMin = 3f;              // hĺbka dna pod okolitou zemou: minimum (m)
    const float LakeDepthMax = 6f;              // maximum (m)
    const float LakeRadiusScale = 1.5f;         // násobok pôvodného polomeru rybníka (kotlina je väčšia než plochý kruh)
    const float LakeShoreFactor = 3.2f;         // dĺžka brehu = hĺbka × faktor (plynulé klesanie)
    const float WaterLevel = -0.4f;             // hladina voči okolitej zemi (m)
    const int MinLakesPerZone = 2;              // každá zóna má aspoň toľko jazier
    const float LakeShapeMaxWarp = 1.35f;       // najväčšie predĺženie brehu oproti kruhu (organický tvar jazera)

    // Cesty
    const float RoadWidth = 4f;                 // šírka cesty (m)
    const float RoadBlend = 5f;                 // šírka plynulého prechodu do okolitého terénu (m)
    const float RoadMaxGrade = 0.10f;           // najväčší pozdĺžny sklon cesty (0,10 = 10 %)
    const float RoadPathCell = 3f;              // bunka siete na hľadanie trasy (m)

    // Hranice mapy a rozmiestňovanie objektov
    const float BoundsMargin = 15f;             // odsadenie od okraja terénu, kde sa nesmie nič objaviť (m)
    const float PropMaxSlopeDeg = 24f;          // najstrmší svah, na ktorom smie objekt stáť
    const float LakeKeepOut = 2f;               // objekty ďalej od hladiny než toto (m)
    const float GateKeepOut = 7f;               // voľné miesto okolo portálu (m)

    // ======== Dáta jedného behu ========
    class LakeSpec { public Transform obj; public WildLake lake; public Vector2 pos; }

    class TerrainBuild
    {
        public WildTheme theme;
        public Vector3 center;
        public float groundY, originX, originZ, cell;
        public int n;
        public float[,] rel, lakeAmt, roadDist, roadH, roadMask;
        public List<LakeSpec> lakes = new List<LakeSpec>();
        public List<Vector3> flats = new List<Vector3>();   // x, z, polomer úplne rovnej plochy
        public Vector3 gate, anchorA, anchorB, anchorC;

        public float HalfSize => TerrainSize * 0.5f;
        public float WorldX(int ix) => originX + ix * cell;
        public float WorldZ(int iz) => originZ + iz * cell;

        public float Sample(float[,] map, float x, float z)
        {
            float gx = Mathf.Clamp((x - originX) / cell, 0f, n - 1.001f);
            float gz = Mathf.Clamp((z - originZ) / cell, 0f, n - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            float a = Mathf.Lerp(map[z0, x0], map[z0, x0 + 1], fx);
            float b = Mathf.Lerp(map[z0 + 1, x0], map[z0 + 1, x0 + 1], fx);
            return Mathf.Lerp(a, b, fz);
        }
    }

    static float Smooth01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // ======== Menu ========
    [MenuItem("Progeny/Divočiny/Terén, cesty, jazerá a hranice", false, 110)]
    static void BuildAllWildTerrainsMenu()
    {
        AssetDatabase.Refresh();
        EnsureSurfaceMaterials(false);

        int zones = 0;
        foreach (WildTheme theme in WildThemes)
            if (SafeBuildWildTerrain(theme)) zones++;

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log(zones == 0
            ? "Terén: nenašla sa žiadna divočina. Najprv ju vytvor (Svet → Vytvoriť divočiny)."
            : $"Terén divočín hotový ({zones} zón). Ulož scénu. Ak chceš Divokú svorku znova na povrchu, spusti jej menu alebo Aktualizovať všetko.");
    }

    static Transform FindWildTerrain(GameObject root) => root != null ? root.transform.Find("Teren") : null;

    // ======== Hlavný postup pre jednu zónu ========
    static bool BuildWildTerrain(WildTheme theme)
    {
        GameObject root = GameObject.Find(theme.RootName);
        if (root == null) return false;

        // Stred zóny sa berie zo skutočnej polohy terénu (zóny sa dajú ručne posúvať; stred koreňa objektu Divočina nemusí sedieť)
        Vector3 zoneCenter = root.transform.position;
        float zoneGround = zoneCenter.y;
        Transform existingTerrain = FindWildTerrain(root);
        if (existingTerrain != null)
        {
            zoneCenter = existingTerrain.position + new Vector3(TerrainSize * 0.5f, 0f, TerrainSize * 0.5f);
            zoneGround = existingTerrain.position.y + TerrainBaseDepth;
        }

        var b = new TerrainBuild { theme = theme, center = zoneCenter, n = TerrainResolution };
        b.groundY = zoneGround;
        b.cell = TerrainSize / (b.n - 1);
        b.originX = b.center.x - b.HalfSize;
        b.originZ = b.center.z - b.HalfSize;
        b.gate = b.center + new Vector3(0f, 0f, -(TerrainSize * 0.5f - 25f));
        WildAnchors(theme.key, out Vector3 offA, out Vector3 offB, out Vector3 offC);
        b.anchorA = b.center + offA;
        b.anchorB = b.center + offB;
        b.anchorC = b.center + offC;

        b.flats.Add(new Vector3(b.gate.x, b.gate.z, 12f));
        b.flats.Add(new Vector3(b.anchorA.x, b.anchorA.z, 18f));
        b.flats.Add(new Vector3(b.anchorB.x, b.anchorB.z, 18f));
        b.flats.Add(new Vector3(b.anchorC.x, b.anchorC.z, 18f));
        b.flats.Add(new Vector3(b.center.x + 25f, b.center.z - 10f, 8f));   // monolit

        // Staré vrstvy: mesh kopce, ploché cesty a rovná doska zóny
        foreach (string old in new[] { "Kopce", "Kopce_v2" })
        {
            Transform t = root.transform.Find(old);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != null && t.name == "Cesta" && t.parent != null) Object.DestroyImmediate(t.gameObject);
        Transform flatGround = root.transform.Find("Zem_" + theme.key);
        if (flatGround != null)
        {
            Collider gc = flatGround.GetComponent<Collider>();
            Renderer gr = flatGround.GetComponent<Renderer>();
            if (gc != null) gc.enabled = false;
            if (gr != null) gr.enabled = false;   // objekt ostáva (hľadá sa podľa mena), len nevidno ani nekoliduje
        }

        PrepareLakes(root, b);
        BuildHeights(b);
        CarveRoads(b);

        TerrainData data = CreateTerrainData(b);
        Terrain terrain = EnsureTerrainObject(root, b, data);
        PaintTerrain(b, data);
        ApplyLakeWater(b);
        BuildLakeShore(root, b);
        EditorUtility.SetDirty(data);

        Physics.SyncTransforms();
        try { ConformToTerrain(root, theme); }
        catch (System.Exception e) { Debug.LogError("Usadenie objektov (" + theme.displayName + ") zlyhalo: " + e.Message + " " + e.StackTrace); }
        DressForest(root, theme, terrain);   // lesný dekor z balíka Forest Environment (kroviny, kamene, pne, kmene, huby)
        return true;
    }

    // ======== Jazerá: výber miest a parametrov ========
    static void PrepareLakes(GameObject root, TerrainBuild b)
    {
        var rnd = new System.Random(b.theme.key.GetHashCode() ^ 0x2f1b);
        Transform decor = root.transform.Find("Dekoracie");

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == "Rybník") b.lakes.Add(new LakeSpec { obj = t });

        // Zóny bez rybníka dostanú aspoň jedno jazero
        WildMats mats = null;
        while (b.lakes.Count < MinLakesPerZone && decor != null)
        {
            if (mats == null) mats = MakeWildMats(b.theme);
            WildPond(decor, b.center, mats, rnd);
            Transform created = decor.GetChild(decor.childCount - 1);
            if (created.name != "Rybník") break;
            b.lakes.Add(new LakeSpec { obj = created });
        }

        var placed = new List<LakeSpec>();
        foreach (LakeSpec spec in b.lakes)
        {
            WildLake lake = spec.obj.GetComponent<WildLake>();
            if (lake == null)
            {
                lake = spec.obj.gameObject.AddComponent<WildLake>();
                lake.baseRadius = Mathf.Clamp(Mathf.Max(spec.obj.localScale.x, spec.obj.localScale.z) * 0.5f, 6f, 14f);
                float depthBias = b.theme.key == "Mociare" ? 0.35f : 1f;   // močiare sú plytšie
                lake.depth = Mathf.Lerp(LakeDepthMin, LakeDepthMax, (float)rnd.NextDouble() * depthBias);
            }
            spec.lake = lake;

            float rb = lake.baseRadius * LakeRadiusScale;
            float shore = lake.depth * LakeShoreFactor;
            lake.basinRadius = rb + shore;

            // Jazero musí ležať celé v hraniciach, mimo rovných plôch (portál, lokality), mimo kopcov a iných jazier
            Vector2 p = new Vector2(spec.obj.position.x, spec.obj.position.z);
            if (!LakePlaceOk(b, p, lake.basinRadius * LakeShapeMaxWarp, placed))
            {
                for (int attempt = 0; attempt < 120; attempt++)
                {
                    float span = b.HalfSize - BoundsMargin - lake.basinRadius;
                    if (span <= 0f) break;
                    Vector2 q = new Vector2(b.center.x + ((float)rnd.NextDouble() * 2f - 1f) * span, b.center.z + ((float)rnd.NextDouble() * 2f - 1f) * span);
                    if (LakePlaceOk(b, q, lake.basinRadius * LakeShapeMaxWarp, placed)) { p = q; break; }
                }
            }
            spec.pos = p;
            placed.Add(spec);
        }
    }

    static bool LakePlaceOk(TerrainBuild b, Vector2 p, float basinRadius, List<LakeSpec> others)
    {
        float lim = b.HalfSize - BoundsMargin - basinRadius;
        if (Mathf.Abs(p.x - b.center.x) > lim || Mathf.Abs(p.y - b.center.z) > lim) return false;

        foreach (Vector3 f in b.flats)
            if ((new Vector2(f.x, f.y) - p).magnitude < f.z + basinRadius + 10f) return false;

        foreach (Vector4 h in WildHillList(b.theme, b.center))
            if ((new Vector2(h.x, h.y) - p).magnitude < h.z * 0.8f + basinRadius) return false;

        foreach (LakeSpec o in others)
            if (o.lake != null && (o.pos - p).magnitude < o.lake.basinRadius + basinRadius + 6f) return false;
        return true;
    }

    // Kopce zóny: 6 kusov (2 dominantné, 4 menšie); x, z, polomer, výška
    static List<Vector4> WildHillList(WildTheme t, Vector3 c)
    {
        var rnd = new System.Random(t.key.GetHashCode() ^ 0x1d2b);
        var list = new List<Vector4>();
        for (int i = 0; i < hillSpots.Length; i++)
        {
            bool dominant = i < 2;
            float radius = dominant ? Mathf.Lerp(46f, 52f, (float)rnd.NextDouble()) : Mathf.Lerp(26f, 34f, (float)rnd.NextDouble());
            float height = dominant ? Mathf.Lerp(12f, 14.5f, (float)rnd.NextDouble()) : Mathf.Lerp(5f, 7.5f, (float)rnd.NextDouble());
            float jx = ((float)rnd.NextDouble() * 2f - 1f) * 6f, jz = ((float)rnd.NextDouble() * 2f - 1f) * 6f;
            list.Add(new Vector4(c.x + hillSpots[i].x + jx, c.z + hillSpots[i].y + jz, radius, height));
        }
        return list;
    }

    // ======== Výšková mapa ========
    static void BuildHeights(TerrainBuild b)
    {
        int n = b.n;
        b.rel = new float[n, n];
        b.lakeAmt = new float[n, n];

        var rnd = new System.Random(b.theme.key.GetHashCode() ^ 0x4c77);
        float o1 = (float)rnd.NextDouble() * 500f, o2 = (float)rnd.NextDouble() * 500f, o3 = (float)rnd.NextDouble() * 500f, o4 = (float)rnd.NextDouble() * 500f;
        List<Vector4> hills = WildHillList(b.theme, b.center);
        var phases = new List<Vector2>();
        foreach (Vector4 h in hills) phases.Add(new Vector2((float)rnd.NextDouble() * 6.28f, (float)rnd.NextDouble() * 6.28f));

        float minX = b.center.x - b.HalfSize, maxX = b.center.x + b.HalfSize, minZ = b.center.z - b.HalfSize, maxZ = b.center.z + b.HalfSize;

        for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                float x = b.WorldX(ix), z = b.WorldZ(iz);

                float relief = (Mathf.PerlinNoise(x * 0.018f + o1, z * 0.018f + o2) - 0.5f) * 2f * RollingNoiseMeters
                             + (Mathf.PerlinNoise(x * 0.06f + o3, z * 0.06f + o4) - 0.5f) * 2f * RollingNoiseMeters * 0.35f;

                for (int i = 0; i < hills.Count; i++)
                {
                    Vector4 h = hills[i];
                    float dx = x - h.x, dz = z - h.y;
                    float angle = Mathf.Atan2(dz, dx);
                    float radius = h.z * (1f + 0.16f * Mathf.Sin(angle * 2f + phases[i].x) + 0.09f * Mathf.Sin(angle * 3f + phases[i].y));   // nepravidelný pôdorys
                    float d = Mathf.Sqrt(dx * dx + dz * dz) / radius;
                    if (d >= 1f) continue;

                    float profile = Smooth01(1f - d);                                                                 // hladký okraj, zaoblený vrchol
                    float lumps = 0.88f + 0.24f * Mathf.PerlinNoise(x * 0.045f + o2, z * 0.045f + o3);               // vrcholy nie sú dokonalé kužele
                    relief += h.w * profile * lumps;
                }

                // Okraj mapy a rovné plochy (portál, lokality, monolit) bez reliéfu
                float edge = Mathf.Min(Mathf.Min(x - minX, maxX - x), Mathf.Min(z - minZ, maxZ - z));
                float factor = Smooth01((edge - 6f) / (BoundsMargin + 12f));
                foreach (Vector3 f in b.flats)
                {
                    float dist = Mathf.Sqrt((x - f.x) * (x - f.x) + (z - f.y) * (z - f.y));
                    factor *= Smooth01((dist - f.z) / (f.z + 6f));
                }
                b.rel[iz, ix] = relief * factor;
            }

        // Jazerá: kotlina so zaobleným dnom a plynulým brehom (minimum s okolitým terénom)
        foreach (LakeSpec spec in b.lakes)
        {
            WildLake lake = spec.lake;
            float rb = lake.baseRadius * LakeRadiusScale;
            float flatBottom = rb * 0.5f;
            float span = lake.basinRadius - flatBottom;
            float shapeA = Mathf.Abs(spec.pos.x * 0.37f + spec.pos.y * 0.11f) % 6.28f, shapeB = Mathf.Abs(spec.pos.x * 0.21f - spec.pos.y * 0.29f) % 6.28f;   // tvar jazera je stály pre dané miesto

            for (int iz = 0; iz < n; iz++)
                for (int ix = 0; ix < n; ix++)
                {
                    float dx = b.WorldX(ix) - spec.pos.x, dz = b.WorldZ(iz) - spec.pos.y;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    if (distance >= lake.basinRadius * LakeShapeMaxWarp) continue;

                    // Nepravidelný breh: polomer sa mení podľa smeru a šumu, takže jazero nie je kruh
                    float angle = Mathf.Atan2(dz, dx);
                    float warp = 1f + 0.2f * Mathf.Sin(angle * 2f + shapeA) + 0.12f * Mathf.Sin(angle * 3f + shapeB)
                               + (Mathf.PerlinNoise(b.WorldX(ix) * 0.08f + shapeA, b.WorldZ(iz) * 0.08f + shapeB) - 0.5f) * 0.3f;
                    float d = distance / Mathf.Clamp(warp, 0.65f, LakeShapeMaxWarp);
                    if (d >= lake.basinRadius) continue;

                    float profile = -lake.depth * (1f - Smooth01((d - flatBottom) / span));
                    if (profile < b.rel[iz, ix]) b.rel[iz, ix] = profile;
                    b.lakeAmt[iz, ix] = Mathf.Max(b.lakeAmt[iz, ix], -profile / lake.depth);
                }

            // Polomer hladiny = tam, kde dno klesne pod hladinu
            float water = flatBottom;
            for (float d = lake.basinRadius; d > flatBottom; d -= 0.25f)
            {
                float profile = -lake.depth * (1f - Smooth01((d - flatBottom) / span));
                if (profile <= WaterLevel) { water = d; break; }
            }
            lake.waterRadius = water * LakeShapeMaxWarp;   // poistka pre kontroly polohy objektov; skutočný tvar hladiny je v sieti vody
            EditorUtility.SetDirty(lake);
        }

        // Prirodzené vyhladenie: dva prechody rozmazaním a potom obmedzenie sklonu (žiadne strmé neprírodné steny)
        Blur(b.rel, n, 2);
        Thermal(b.rel, n, Mathf.Tan(MaxSlopeDeg * Mathf.Deg2Rad) * b.cell, 30);
    }

    static void Blur(float[,] h, int n, int passes)
    {
        var tmp = new float[n, n];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    tmp[z, x] = 0.25f * h[z, Mathf.Max(0, x - 1)] + 0.5f * h[z, x] + 0.25f * h[z, Mathf.Min(n - 1, x + 1)];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                    h[z, x] = 0.25f * tmp[Mathf.Max(0, z - 1), x] + 0.5f * tmp[z, x] + 0.25f * tmp[Mathf.Min(n - 1, z + 1), x];
        }
    }

    // Tepelná erózia: rozdiel susedov nad limitom sa presúva (strmé miesta sa zosunú)
    static void Thermal(float[,] h, int n, float maxDelta, int iterations)
    {
        for (int it = 0; it < iterations; it++)
            for (int z = 0; z < n - 1; z++)
                for (int x = 0; x < n - 1; x++)
                {
                    Relax(h, z, x, z, x + 1, maxDelta);
                    Relax(h, z, x, z + 1, x, maxDelta);
                }
    }

    static void Relax(float[,] h, int z1, int x1, int z2, int x2, float maxDelta)
    {
        float d = h[z1, x1] - h[z2, x2];
        float excess = Mathf.Abs(d) - maxDelta;
        if (excess <= 0f) return;
        float move = excess * 0.3f * Mathf.Sign(d);
        h[z1, x1] -= move;
        h[z2, x2] += move;
    }

    // ======== Cesty ========
    static void CarveRoads(TerrainBuild b)
    {
        int n = b.n;
        b.roadDist = new float[n, n];
        b.roadH = new float[n, n];
        b.roadMask = new float[n, n];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++) b.roadDist[z, x] = float.MaxValue;

        // Portál → tri lokality, a spojky medzi lokalitami
        var links = new[] { (b.gate, b.anchorA), (b.gate, b.anchorB), (b.anchorA, b.anchorC), (b.anchorB, b.anchorC) };
        float half = RoadWidth * 0.5f, reach = half + RoadBlend;

        foreach (var link in links)
        {
            List<Vector2> path = FindRoute(b, new Vector2(link.Item1.x, link.Item1.z), new Vector2(link.Item2.x, link.Item2.z));
            if (path == null || path.Count < 2) continue;

            List<Vector2> pts = Resample(Chaikin(Chaikin(path)), 1f);
            var heights = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++) heights[i] = b.Sample(b.rel, pts[i].x, pts[i].y);

            // Pozdĺžny profil: vyhladenie a obmedzenie sklonu cesty
            for (int pass = 0; pass < 6; pass++) SmoothProfile(heights, 5);
            float maxStep = RoadMaxGrade * 1f;
            for (int i = 1; i < heights.Length; i++) heights[i] = Mathf.Clamp(heights[i], heights[i - 1] - maxStep, heights[i - 1] + maxStep);
            for (int i = heights.Length - 2; i >= 0; i--) heights[i] = Mathf.Clamp(heights[i], heights[i + 1] - maxStep, heights[i + 1] + maxStep);

            int radiusCells = Mathf.CeilToInt(reach / b.cell) + 1;
            for (int i = 0; i < pts.Count; i++)
            {
                int cx = Mathf.RoundToInt((pts[i].x - b.originX) / b.cell), cz = Mathf.RoundToInt((pts[i].y - b.originZ) / b.cell);
                for (int dz = -radiusCells; dz <= radiusCells; dz++)
                    for (int dx = -radiusCells; dx <= radiusCells; dx++)
                    {
                        int ix = cx + dx, iz = cz + dz;
                        if (ix < 0 || iz < 0 || ix >= n || iz >= n) continue;

                        float ddx = b.WorldX(ix) - pts[i].x, ddz = b.WorldZ(iz) - pts[i].y;
                        float dist = Mathf.Sqrt(ddx * ddx + ddz * ddz);
                        if (dist < reach && dist < b.roadDist[iz, ix])
                        {
                            b.roadDist[iz, ix] = dist;
                            b.roadH[iz, ix] = heights[i];
                        }
                    }
            }
        }

        // Terén sa pod cestou zarovná na jej profil, okolo plynulo prejde späť do kopcov
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float d = b.roadDist[z, x];
                if (d >= reach) continue;

                float w = d <= half ? 1f : 1f - Smooth01((d - half) / RoadBlend);
                b.rel[z, x] = Mathf.Lerp(b.rel[z, x], b.roadH[z, x], w);
                b.roadMask[z, x] = d <= half ? 1f : 1f - Smooth01((d - half) / 1.5f);
            }
    }

    static void SmoothProfile(float[] h, int window)
    {
        var copy = (float[])h.Clone();
        for (int i = 0; i < h.Length; i++)
        {
            float sum = 0f;
            int count = 0;
            for (int k = -window; k <= window; k++)
            {
                int j = i + k;
                if (j < 0 || j >= h.Length) continue;
                sum += copy[j];
                count++;
            }
            h[i] = sum / count;
        }
    }

    // A* po sieti: trasa sa vyhýba strmým svahom a vode, takže prirodzene sleduje vrstevnice
    static List<Vector2> FindRoute(TerrainBuild b, Vector2 from, Vector2 to)
    {
        int cells = Mathf.RoundToInt(TerrainSize / RoadPathCell);
        float g = RoadPathCell;
        float margin = BoundsMargin;

        Vector2Int ToCell(Vector2 p) => new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((p.x - b.originX) / g), 0, cells - 1), Mathf.Clamp(Mathf.FloorToInt((p.y - b.originZ) / g), 0, cells - 1));
        Vector2 CellCenter(int ix, int iz) => new Vector2(b.originX + (ix + 0.5f) * g, b.originZ + (iz + 0.5f) * g);
        float HeightAt(int ix, int iz) { Vector2 c = CellCenter(ix, iz); return b.Sample(b.rel, c.x, c.y); }
        bool Allowed(int ix, int iz)
        {
            Vector2 c = CellCenter(ix, iz);
            if (Mathf.Abs(c.x - b.center.x) > b.HalfSize - margin || Mathf.Abs(c.y - b.center.z) > b.HalfSize - margin) return false;
            if (b.Sample(b.lakeAmt, c.x, c.y) > 0.02f) return false;       // žiadne cesty cez jazerá
            return true;
        }

        int keyHash = 0;
        foreach (char ch in b.theme.key) keyHash = keyHash * 31 + ch;
        float seedX = (keyHash & 0xff) * 0.53f, seedZ = ((keyHash >> 8) & 0xff) * 0.47f;

        Vector2Int start = ToCell(from), goal = ToCell(to);
        var cost = new float[cells, cells];
        var parent = new Vector2Int[cells, cells];
        for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++) cost[z, x] = float.MaxValue;

        var open = new SortedSet<(float f, int id)>();
        int Id(Vector2Int c) => c.y * cells + c.x;
        cost[start.y, start.x] = 0f;
        parent[start.y, start.x] = start;
        open.Add((0f, Id(start)));

        while (open.Count > 0)
        {
            (float f, int id) top = open.Min;
            open.Remove(top);
            var cur = new Vector2Int(top.id % cells, top.id / cells);
            if (cur == goal) break;

            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cur.x + dx, nz = cur.y + dz;
                    if (nx < 0 || nz < 0 || nx >= cells || nz >= cells) continue;
                    var next = new Vector2Int(nx, nz);
                    if (next != goal && next != start && !Allowed(nx, nz)) continue;

                    float step = g * ((dx != 0 && dz != 0) ? 1.4142f : 1f);
                    float slope = Mathf.Abs(HeightAt(nx, nz) - HeightAt(cur.x, cur.y)) / step;
                    float stepCost = step * (1f + 30f * slope * slope + (slope > 0.3f ? 40f : 0f));
                    // Každá zóna má iný tvar ciest (šum podľa jej kľúča) a nové cesty sa držia ďalej od už vyrezaných (okrem koncov)
                    Vector2 cc = CellCenter(nx, nz);
                    stepCost *= 1f + 0.9f * Mathf.PerlinNoise(cc.x * 0.012f + seedX, cc.y * 0.012f + seedZ);
                    int rix = Mathf.Clamp(Mathf.RoundToInt((cc.x - b.originX) / b.cell), 0, b.n - 1), riz = Mathf.Clamp(Mathf.RoundToInt((cc.y - b.originZ) / b.cell), 0, b.n - 1);
                    if (b.roadDist[riz, rix] < 22f && Vector2.Distance(cc, from) > 45f && Vector2.Distance(cc, to) > 45f) stepCost *= 4f;
                    float total = cost[cur.y, cur.x] + stepCost;
                    if (total >= cost[nz, nx]) continue;

                    cost[nz, nx] = total;
                    parent[nz, nx] = cur;
                    float heuristic = Vector2.Distance(CellCenter(nx, nz), CellCenter(goal.x, goal.y));
                    open.Add((total + heuristic, Id(next)));
                }
        }

        if (cost[goal.y, goal.x] == float.MaxValue) return null;

        var result = new List<Vector2> { from };
        var stack = new List<Vector2>();
        Vector2Int c2 = goal;
        while (c2 != start)
        {
            stack.Add(CellCenter(c2.x, c2.y));
            c2 = parent[c2.y, c2.x];
        }
        stack.Reverse();
        result.AddRange(stack);
        result.Add(to);
        return result;
    }

    static List<Vector2> Chaikin(List<Vector2> pts)
    {
        var r = new List<Vector2> { pts[0] };
        for (int i = 0; i < pts.Count - 1; i++)
        {
            r.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.25f));
            r.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.75f));
        }
        r.Add(pts[pts.Count - 1]);
        return r;
    }

    static List<Vector2> Resample(List<Vector2> pts, float spacing)
    {
        var r = new List<Vector2> { pts[0] };
        float carry = 0f;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector2 a = pts[i], bpt = pts[i + 1];
            float len = Vector2.Distance(a, bpt);
            float pos = spacing - carry;
            while (pos <= len)
            {
                r.Add(Vector2.Lerp(a, bpt, pos / len));
                pos += spacing;
            }
            carry = len - (pos - spacing);
        }
        r.Add(pts[pts.Count - 1]);
        return r;
    }

    // ======== Terrain objekt, dáta a vrstvy ========
    static TerrainData CreateTerrainData(TerrainBuild b)
    {
        const string folder = "Assets/Models/Terrain";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Models", "Terrain");
        string path = $"{folder}/Teren_{b.theme.key}.asset";

        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, path);
        }

        data.heightmapResolution = b.n;
        data.size = new Vector3(TerrainSize, TerrainHeightRange, TerrainSize);

        var normalized = new float[b.n, b.n];
        for (int z = 0; z < b.n; z++)
            for (int x = 0; x < b.n; x++)
                normalized[z, x] = Mathf.Clamp01((b.rel[z, x] + TerrainBaseDepth) / TerrainHeightRange);
        data.SetHeights(0, 0, normalized);
        return data;
    }

    static Terrain EnsureTerrainObject(GameObject root, TerrainBuild b, TerrainData data)
    {
        Transform existing = FindWildTerrain(root);
        GameObject go = existing != null ? existing.gameObject : Terrain.CreateTerrainGameObject(data);
        go.name = "Teren";
        go.transform.SetParent(root.transform, true);
        go.transform.position = new Vector3(b.originX, b.groundY - TerrainBaseDepth, b.originZ);

        Terrain terrain = go.GetComponent<Terrain>();
        terrain.terrainData = data;
        TerrainCollider collider = go.GetComponent<TerrainCollider>();
        if (collider != null) collider.terrainData = data;

        terrain.drawTreesAndFoliage = true;      // 3D tráva (detaily terénu)
        terrain.detailObjectDistance = 55f;
        terrain.detailObjectDensity = 0.85f;
        terrain.heightmapPixelError = 6f;        // menej trojuholníkov do diaľky, vzhľad rovnaký
        terrain.drawInstanced = true;            // instancovaný terén
        terrain.basemapDistance = 140f;          // vzdialený terén s lacnejším základným textúrovaním
        return terrain;
    }

    static TerrainLayer MakeTerrainLayer(string surfaceKey, string fallbackKey)
    {
        string path = $"{SurfacesFolder}/TL_{surfaceKey}.terrainlayer";
        TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, path);
        }

        Material surface = LoadSurface(surfaceKey) ?? LoadSurface(fallbackKey);
        Texture diffuse = surface != null ? surface.mainTexture : null;
        Texture normal = surface != null && surface.HasProperty("_BumpMap") ? surface.GetTexture("_BumpMap") : null;

        layer.diffuseTexture = diffuse != null ? (Texture2D)diffuse : Texture2D.grayTexture;
        layer.normalMapTexture = normal as Texture2D;
        layer.tileSize = new Vector2(GroundTileMeters * 1.6f, GroundTileMeters * 1.6f);
        layer.smoothness = 0.05f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    // Vrstvy: 0 tráva zóny, 1 cesta, 2 skala (strmé svahy), 3 breh jazera
    static void PaintTerrain(TerrainBuild b, TerrainData data)
    {
        string grass = GroundSurfaceFor(b.theme.key);
        string shore = grass == "mossy_grass" ? "grass_and_rubble" : "mossy_grass";
        data.terrainLayers = BuildTerrainLayers(b.theme.key, grass, shore);

        const int res = 1024;   // bunka ≈ 0,41 m, rovnaká ostrosť ako na pôvodnej zóne
        data.alphamapResolution = res;
        var maps = new float[res, res, 4];
        for (int az = 0; az < res; az++)
            for (int ax = 0; ax < res; ax++)
            {
                float u = ax / (float)(res - 1), v = az / (float)(res - 1);
                float x = b.originX + u * TerrainSize, z = b.originZ + v * TerrainSize;

                float steep = data.GetSteepness(u, v);
                float rock = Smooth01((steep - 22f) / 10f);
                float road = Mathf.Clamp01(b.Sample(b.roadMask, x, z));
                float mud = Smooth01(b.Sample(b.lakeAmt, x, z) * 8f);

                road *= 1f - rock * 0.5f;
                mud *= 1f - road;
                rock *= 1f - road;
                float grassW = Mathf.Clamp01(1f - road - rock - mud);

                float sum = grassW + road + rock + mud;
                maps[az, ax, 0] = grassW / sum;
                maps[az, ax, 1] = road / sum;
                maps[az, ax, 2] = rock / sum;
                maps[az, ax, 3] = mud / sum;
            }
        data.SetAlphamaps(0, 0, maps);
        PaintGrassDetails(b, data, maps, res);
    }

    // Hladina: sieť podľa skutočných brehov kotliny (organický tvar, mäkký okraj) s vodným shaderom
    static void ApplyLakeWater(TerrainBuild b)
    {
        Material material = WaterMaterial(b.theme);
        int index = 0;
        foreach (LakeSpec spec in b.lakes)
        {
            Mesh mesh = BuildWaterMesh(b, spec, index++);

            MeshFilter filter = spec.obj.GetComponent<MeshFilter>();
            if (filter == null) filter = spec.obj.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = spec.obj.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = spec.obj.gameObject.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            spec.obj.SetPositionAndRotation(new Vector3(spec.pos.x, b.groundY + WaterLevel, spec.pos.y), Quaternion.identity);
            spec.obj.localScale = Vector3.one;
            Collider c = spec.obj.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
            EditorUtility.SetDirty(spec.obj);
        }
    }

    // ======== Objekty na povrch a do hraníc mapy ========
    static readonly HashSet<string> TerrainPropNames = new HashSet<string>
    {
        "Strom", "Smrek", "Mŕtvy strom", "Skala", "Vrak", "Huba", "Ruina", "Tabor", "Monolit", "Kryštály",
        "Zdroj_bylina", "Zdroj_drevo", "Zdroj_kameň", "Zdroj_kryštál", "Zdroj_šrot"
    };

    static void ConformAllWilds()
    {
        foreach (WildTheme theme in WildThemes)
        {
            GameObject root = GameObject.Find(theme.RootName);
            if (root != null && FindWildTerrain(root) != null) ConformToTerrain(root, theme);
        }
    }

    static bool PropPositionOk(Terrain terrain, Vector3 p, GameObject root, WildTheme theme, WildLake[] lakes, bool isDemon)
    {
        Vector3 origin = terrain.transform.position;
        float half = TerrainSize * 0.5f, margin = isDemon ? BoundsMargin * 0.5f : BoundsMargin;
        Vector3 c = origin + new Vector3(half, 0f, half);   // stred zóny podľa skutočnej polohy terénu (zóny sa dajú ručne posúvať)
        if (Mathf.Abs(p.x - c.x) > half - margin || Mathf.Abs(p.z - c.z) > half - margin) return false;

        TerrainData data = terrain.terrainData;
        float u = (p.x - origin.x) / TerrainSize, v = (p.z - origin.z) / TerrainSize;
        if (data.GetSteepness(u, v) > PropMaxSlopeDeg) return false;

        foreach (WildLake lake in lakes)
            if (lake != null && lake.Covers(p, LakeKeepOut)) return false;

        // Nie na ceste (vrstva 1) a nie okolo portálu
        int ax = Mathf.Clamp(Mathf.RoundToInt(u * (data.alphamapWidth - 1)), 0, data.alphamapWidth - 1);
        int az = Mathf.Clamp(Mathf.RoundToInt(v * (data.alphamapHeight - 1)), 0, data.alphamapHeight - 1);
        if (data.GetAlphamaps(ax, az, 1, 1)[0, 0, 1] > 0.35f) return false;

        Vector3 gate = c + new Vector3(0f, 0f, -95f);
        gate.y = p.y;
        if ((p - gate).sqrMagnitude < GateKeepOut * GateKeepOut) return false;
        return true;
    }

    static void ConformToTerrain(GameObject root, WildTheme theme)
    {
        Transform terrainT = FindWildTerrain(root);
        if (terrainT == null) return;
        Terrain terrain = terrainT.GetComponent<Terrain>();
        if (terrain == null) return;
        Physics.SyncTransforms();

        WildLake[] lakes = root.GetComponentsInChildren<WildLake>(true);

        // Fialové svietiace huby sa zrušili úplne
        foreach (Transform old in root.GetComponentsInChildren<Transform>(true))
            if (old != null && (old.name == "Huba" || old.name == "Kryštály")) Object.DestroyImmediate(old.gameObject);   // fialové huby a veľké dekoratívne kryštály sa zrušili
        var candidates = new List<Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == root.transform || t == terrainT || t.IsChildOf(terrainT)) continue;
            if (TerrainPropNames.Contains(t.name) || t.GetComponent<ResourceNode>() != null || t.GetComponent<BreakableNode>() != null
                || t.GetComponent<LootCrate>() != null || t.GetComponent<DemonEnemy>() != null)
                candidates.Add(t);
        }

        // Do úvahy ide len najvyšší objekt skupiny (napr. celý tábor, nie jeho debny zvlášť)
        var set = new HashSet<Transform>(candidates);
        var tops = new List<Transform>();
        foreach (Transform t in candidates)
        {
            bool nested = false;
            for (Transform p = t.parent; p != null && p != root.transform; p = p.parent)
                if (set.Contains(p)) { nested = true; break; }
            if (!nested) tops.Add(t);
        }

        var rnd = new System.Random(theme.key.GetHashCode() ^ 0x6a09);
        float half = TerrainSize * 0.5f;
        Vector3 c = terrain.transform.position + new Vector3(half, 0f, half);
        int snapped = 0, moved = 0, removed = 0;

        foreach (Transform t in tops)
        {
            bool demon = t.GetComponent<DemonEnemy>() != null;
            Vector3 p = t.position;

            if (!PropPositionOk(terrain, p, root, theme, lakes, demon))
            {
                bool found = false;
                for (int attempt = 0; attempt < 90 && !found; attempt++)
                {
                    Vector3 q;
                    if (attempt < 45)
                    {
                        float radius = 4f + attempt * 0.8f;   // najprv blízko, potom ďalej
                        double angle = rnd.NextDouble() * Mathf.PI * 2.0;
                        q = new Vector3(p.x + (float)System.Math.Cos(angle) * radius, 0f, p.z + (float)System.Math.Sin(angle) * radius);
                    }
                    else
                    {
                        float span = half - BoundsMargin - 1f;
                        q = new Vector3(c.x + ((float)rnd.NextDouble() * 2f - 1f) * span, 0f, c.z + ((float)rnd.NextDouble() * 2f - 1f) * span);
                    }

                    if (!PropPositionOk(terrain, q, root, theme, lakes, demon)) continue;
                    p = q;
                    found = true;
                }

                // Zdroje (skaly, kryštály, drevo, šrot, byliny) sa nikdy nemažú: ak nevyšla prísna kontrola, hľadá sa miesto len v hraniciach a mimo vody
                bool resource = t.GetComponent<ResourceNode>() != null || t.GetComponent<BreakableNode>() != null || t.GetComponent<LootCrate>() != null;
                for (int attempt = 0; attempt < 200 && !found && resource; attempt++)
                {
                    float span = half - BoundsMargin - 1f;
                    var q = new Vector3(c.x + ((float)rnd.NextDouble() * 2f - 1f) * span, 0f, c.z + ((float)rnd.NextDouble() * 2f - 1f) * span);
                    bool inLake = false;
                    foreach (WildLake lake in lakes)
                        if (lake != null && lake.Covers(q, LakeKeepOut)) { inLake = true; break; }
                    if (inLake) continue;

                    Vector3 local = q - terrain.transform.position;
                    if (terrain.terrainData.GetSteepness(local.x / TerrainSize, local.z / TerrainSize) > 38f) continue;
                    p = q;
                    found = true;
                }

                if (!found)
                {
                    if (demon || resource) { /* nemažeme, len ho pustíme na povrch na pôvodnom mieste */ }
                    else { Object.DestroyImmediate(t.gameObject); removed++; continue; }
                }
                else moved++;
            }

            float surface = terrain.SampleHeight(p) + terrain.transform.position.y;
            t.position = new Vector3(p.x, surface + (demon ? 0.15f : 0f), p.z);
            if (demon)
            {
                Rigidbody rb = t.GetComponent<Rigidbody>();
                if (rb != null) { rb.position = t.position; rb.linearVelocity = Vector3.zero; }
            }
            else
            {
                // Objekty s posunutými potomkami: spodok tvaru musí ležať na teréne (nie zapadnutý pod ním ani vo vzduchu)
                Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                    if (bounds.max.y < surface + 0.25f || bounds.min.y > surface + 0.4f)
                        t.position += Vector3.up * (surface - bounds.min.y);
                }
            }
            snapped++;
        }

        // Portál zo zóny a ostatné pevné body stoja na rovnej ploche (úroveň zeme zóny), preto len kontrola
        Physics.SyncTransforms();
        int rocksFixed = EnsureRocksHarvestable(root);   // všetky kamene sú ťažiteľné a pevné
        Debug.Log($"{theme.displayName}: kamene opravené {rocksFixed}, objekty na teréne {snapped} (presunuté {moved}, zmazané mimo hraníc/vo vode {removed}).");
    }
}

public static partial class ProgenyTools
{
    // Chyba v jednej zóne nezastaví ostatné a vypíše sa s názvom zóny a krokom (inak by zostal rozpracovaný svet bez vysvetlenia)
    static bool SafeBuildWildTerrain(WildTheme theme)
    {
        try
        {
            return BuildWildTerrain(theme);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Terén ({theme.displayName}) zlyhal: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            return false;
        }
    }
}
