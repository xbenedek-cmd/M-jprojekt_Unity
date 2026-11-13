using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Mesto: centrum (námestie, civilná štvrť na západe, štvrť odboja na východe, asfaltové ulice) a nová Megakorporácia
/// (asfaltové avenue, mrakodrapy s osvetlenými oknami, obvodový múr a detailné strážne veže s reflektormi).
/// </summary>
public static partial class ProgenyTools
{
    static Transform cityRoot;
    static Material mAsphalt, mPaving, mConcrete, mGlass, mWindow, mWhite, mWoodDark, mMetalRust, mCloth, mRoofRed, mBrick, mNeon, mCurb, mPlaster;

    static void MakeCityMaterials()
    {
        mAsphalt = SurfaceOrColor("asphalt", new Color(0.09f, 0.09f, 0.1f), 6f);
        mPaving = SurfaceOrColor("old_road", new Color(0.45f, 0.43f, 0.4f), 4f);
        mConcrete = Mat(new Color(0.4f, 0.41f, 0.43f));
        mGlass = Mat(new Color(0.14f, 0.26f, 0.36f));
        mGlass.SetFloat("_Glossiness", 0.85f);
        mGlass.SetFloat("_Metallic", 0.5f);
        mWindow = Mat(new Color(0.55f, 0.85f, 1f), true);
        mWhite = Mat(new Color(0.92f, 0.92f, 0.9f));
        mWoodDark = Mat(new Color(0.2f, 0.13f, 0.08f));
        mMetalRust = SurfaceOrColor("corrugated_metal", new Color(0.4f, 0.28f, 0.2f), 2f);
        mCloth = Mat(new Color(0.7f, 0.12f, 0.1f));
        mRoofRed = Mat(new Color(0.42f, 0.17f, 0.13f));
        mBrick = Mat(new Color(0.55f, 0.45f, 0.36f));
        mNeon = Mat(new Color(0.2f, 0.9f, 1f), true);
        mCurb = Mat(new Color(0.55f, 0.55f, 0.55f));
        mPlaster = Mat(new Color(0.72f, 0.66f, 0.55f));
    }

    // Zem pod bodom bez objektov, ktoré už patria mestu (aby sa stavby nestavali na cestách)
    static float CityGroundY(Vector3 p, float fallback)
    {
        float best = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(p + Vector3.up * 80f, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (cityRoot != null && hit.collider.transform.IsChildOf(cityRoot)) continue;
            if (hit.point.y > best) best = hit.point.y;
        }
        return float.IsNegativeInfinity(best) ? fallback : best;
    }

    // Kváder: stred základne v lokálnych súradniciach koreňa, rozmery, voliteľné natočenie a kolízia
    static GameObject CB(Transform parent, string name, Vector3 baseCenter, Vector3 size, Material mat, bool collider = true, float yaw = 0f, float groundY = float.NaN)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        Vector3 world = cityRoot.TransformPoint(baseCenter);
        float y = float.IsNaN(groundY) ? CityGroundY(world, cityRoot.position.y) : groundY;
        go.transform.position = new Vector3(world.x, y + baseCenter.y + size.y * 0.5f, world.z);
        go.transform.rotation = cityRoot.rotation * Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static GameObject CPart(Transform parent, PrimitiveType type, string name, Vector3 baseCenter, Vector3 size, Material mat, bool collider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        Vector3 world = cityRoot.TransformPoint(baseCenter);
        go.transform.position = new Vector3(world.x, CityGroundY(world, cityRoot.position.y) + baseCenter.y + size.y * 0.5f, world.z);
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.isStatic = true;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static Light CLight(Transform parent, Vector3 baseCenter, Color color, float range, float intensity)
    {
        var go = new GameObject("Svetlo");
        go.transform.SetParent(parent, false);
        Vector3 world = cityRoot.TransformPoint(baseCenter);
        go.transform.position = new Vector3(world.x, CityGroundY(world, cityRoot.position.y) + baseCenter.y, world.z);
        Light l = go.AddComponent<Light>();
        l.color = color;
        l.range = range;
        l.intensity = intensity;
        return l;
    }

    static void Sign(Transform parent, string text, Vector3 baseCenter, float yaw)
    {
        CB(parent, "StlpZnacky", baseCenter, new Vector3(0.3f, 5.5f, 0.3f), mWoodDark);
        // Nápis sa vždy pozerá na hráča (rovnako ako nápisy nad portálmi)
        Vector3 world = cityRoot.TransformPoint(baseCenter);
        StallSign(parent, text, new Vector3(world.x, CityGroundY(world, cityRoot.position.y) + baseCenter.y + 6.4f, world.z), 0.09f);
    }

    static void Strip(Transform parent, Vector3 from, Vector3 to, float width, float height, Material mat, string name)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.1f) return;
        Vector3 mid = (from + to) * 0.5f;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        CB(parent, name, mid, new Vector3(width, height, len), mat, true, yaw);
    }

    static void DashedLine(Transform parent, Vector3 from, Vector3 to, float step)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        for (float s = 0f; s < len - 1f; s += step)
            CB(parent, "Ciara", from + d / len * (s + 1f) + Vector3.up * 0.13f, new Vector3(0.3f, 0.02f, 2.2f), mWhite, false, yaw);
    }

    // ---------- Centrum mesta ----------
    [MenuItem("Progeny/Mesto/Centrum: postaviť na označenom mieste", false, 310)]
    static void BuildCityHub()
    {
        GameObject gate = GameObject.Find("Portal_DoMegakorporacie");
        Vector3 center;
        if (Selection.activeTransform != null) center = Selection.activeTransform.position;
        else if (gate != null) center = gate.transform.position - gate.transform.forward * 60f;
        else { Debug.LogWarning("Označ v scéne objekt, kde má stáť stred mesta, alebo najprv vytvor portály."); return; }
        BuildCityHubAt(center, null);
    }

    static void BuildCityHubAt(Vector3 center, Transform parent)
    {
        GameObject old = GameObject.Find("CityHub");
        if (old != null) Undo.DestroyObjectImmediate(old);
        var rootGo = new GameObject("CityHub");
        Undo.RegisterCreatedObjectUndo(rootGo, "City Hub");
        if (parent != null) rootGo.transform.SetParent(parent, true);
        cityRoot = rootGo.transform;
        if (Physics.Raycast(center + Vector3.up * 80f, Vector3.down, out RaycastHit hit, 200f, ~0, QueryTriggerInteraction.Ignore)) center.y = hit.point.y;
        cityRoot.position = center;
        MakeCityMaterials();

        BuildPlot[] plots = Object.FindObjectsByType<BuildPlot>();
        bool NearPlot(Vector3 local)
        {
            Vector3 w = cityRoot.TransformPoint(local);
            foreach (BuildPlot p in plots) if ((p.transform.position - w).sqrMagnitude < 18f * 18f) return true;
            return false;
        }

        float baseY = center.y;
        // Ulice (asfalt) a námestie
        CB(cityRoot, "Namestie", new Vector3(0f, 0f, 0f), new Vector3(46f, 0.2f, 46f), mPaving, true, 0f, baseY);
        CB(cityRoot, "UlicaV", new Vector3(0f, 0f, 0f), new Vector3(230f, 0.12f, 12f), mAsphalt, true, 0f, baseY);
        CB(cityRoot, "UlicaS", new Vector3(0f, 0f, 0f), new Vector3(10f, 0.12f, 190f), mAsphalt, true, 0f, baseY);
        for (int side = -1; side <= 1; side += 2)
        {
            CB(cityRoot, "ChodnikV", new Vector3(0f, 0f, side * 8f), new Vector3(230f, 0.18f, 3f), mCurb, true, 0f, baseY);
            CB(cityRoot, "ChodnikS", new Vector3(side * 7f, 0f, 0f), new Vector3(2.5f, 0.18f, 190f), mCurb, true, 0f, baseY);
        }
        DashedLine(cityRoot, new Vector3(-113f, 0f, 0f), new Vector3(-24f, 0f, 0f), 6f);
        DashedLine(cityRoot, new Vector3(24f, 0f, 0f), new Vector3(113f, 0f, 0f), 6f);
        DashedLine(cityRoot, new Vector3(0f, 0f, -93f), new Vector3(0f, 0f, -24f), 6f);
        DashedLine(cityRoot, new Vector3(0f, 0f, 24f), new Vector3(0f, 0f, 93f), 6f);

        // Pamätník v strede
        CB(cityRoot, "Podstavec1", new Vector3(0f, 0.2f, 0f), new Vector3(9f, 0.5f, 9f), mConcrete, true, 0f, baseY);
        CB(cityRoot, "Podstavec2", new Vector3(0f, 0.7f, 0f), new Vector3(6.5f, 0.5f, 6.5f), mConcrete, true, 0f, baseY);
        CB(cityRoot, "Obelisk", new Vector3(0f, 1.2f, 0f), new Vector3(1.8f, 10f, 1.8f), mWhite, true, 0f, baseY);
        CB(cityRoot, "ObeliskZiara", new Vector3(0f, 7.5f, 0f), new Vector3(1.95f, 0.4f, 1.95f), mNeon, false, 0f, baseY);
        CLight(cityRoot, new Vector3(0f, 8f, 0f), new Color(0.5f, 0.85f, 1f), 24f, 1.5f);

        // Lampy a lavičky na námestí
        foreach (Vector2 c in new[] { new Vector2(-20f, -20f), new Vector2(20f, -20f), new Vector2(-20f, 20f), new Vector2(20f, 20f) })
        {
            CB(cityRoot, "StlpLampy", new Vector3(c.x, 0.2f, c.y), new Vector3(0.25f, 5f, 0.25f), mWoodDark, true, 0f, baseY);
            CB(cityRoot, "Lampa", new Vector3(c.x, 5.2f, c.y), new Vector3(0.7f, 0.5f, 0.7f), mWindow, false, 0f, baseY);
            CLight(cityRoot, new Vector3(c.x, 5f, c.y), new Color(1f, 0.8f, 0.55f), 20f, 1.3f);
            CB(cityRoot, "Lavicka", new Vector3(c.x * 0.6f, 0.2f, c.y * 0.6f), new Vector3(3f, 0.5f, 0.9f), mWoodDark, true, c.x * c.y > 0 ? 45f : -45f, baseY);
        }

        Sign(cityRoot, "CENTRUM MESTA", new Vector3(0f, 0.2f, -26f), 0f);

        // Civilná štvrť (západ): domy, trh
        Sign(cityRoot, "CIVILNÁ ŠTVRŤ", new Vector3(-36f, 0.1f, 8f), 90f);
        float[] houseX = { -50f, -70f, -90f };
        foreach (float x in houseX)
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 p = new Vector3(x, 0f, side * 22f);
                if (NearPlot(p)) continue;
                CB(cityRoot, "Dom", p, new Vector3(11f, 5.5f, 9f), x % 40f == -10f ? mBrick : mPlaster);
                CB(cityRoot, "Strecha", p + Vector3.up * 5.5f, new Vector3(12.4f, 0.6f, 10.4f), mRoofRed, false);
                CB(cityRoot, "Dvere", p + new Vector3(0f, 0f, -side * 4.6f), new Vector3(1.4f, 2.4f, 0.2f), mWoodDark, false);
                CB(cityRoot, "Okno", p + new Vector3(3f, 1.6f, -side * 4.55f), new Vector3(1.3f, 1.2f, 0.15f), mWindow, false);
                CB(cityRoot, "Okno", p + new Vector3(-3f, 1.6f, -side * 4.55f), new Vector3(1.3f, 1.2f, 0.15f), mWindow, false);
                CB(cityRoot, "Komin", p + new Vector3(3.5f, 6f, 2f), new Vector3(1f, 2.2f, 1f), mBrick, false);
                Vector3 treeWorld = cityRoot.TransformPoint(new Vector3(x + 7f, 0f, side * 31f));
                if (!HouseTree(cityRoot, treeWorld, CityGroundY(treeWorld, cityRoot.position.y)))
                {
                    CPart(cityRoot, PrimitiveType.Cylinder, "Strom", new Vector3(x + 7f, 0f, side * 31f), new Vector3(0.6f, 2f, 0.6f), mWoodDark, true);
                    CPart(cityRoot, PrimitiveType.Sphere, "Koruna", new Vector3(x + 7f, 3.2f, side * 31f), new Vector3(4.5f, 3.6f, 4.5f), Mat(new Color(0.18f, 0.4f, 0.16f)), false);
                }
            }
        for (int i = 0; i < 3; i++)   // trh
        {
            Vector3 p = new Vector3(-36f - i * 7f, 0f, -14f);
            if (NearPlot(p)) continue;
            CB(cityRoot, "StanokStol", p, new Vector3(4f, 1f, 1.8f), mWoodDark);
            CB(cityRoot, "StanokStrecha", p + Vector3.up * 2.6f, new Vector3(4.6f, 0.2f, 2.6f), i % 2 == 0 ? mCloth : mWhite, false);
            CB(cityRoot, "StanokStlp", p + new Vector3(-2f, 0f, -1.1f), new Vector3(0.15f, 2.6f, 0.15f), mWoodDark, false);
            CB(cityRoot, "StanokStlp", p + new Vector3(2f, 0f, -1.1f), new Vector3(0.15f, 2.6f, 0.15f), mWoodDark, false);
        }

        // Štvrť odboja (východ): barikády, chatrče, stany, sudy s ohňom, strážnica
        Sign(cityRoot, "ŠTVRŤ ODBOJA", new Vector3(36f, 0.1f, -8f), 90f);
        for (int i = 0; i < 4; i++)
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 tw = cityRoot.TransformPoint(new Vector3(56f + i * 16f, 0f, side * 33f));
                HouseTree(cityRoot, tw, CityGroundY(tw, cityRoot.position.y));
            }
        for (int i = 0; i < 4; i++)
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 p = new Vector3(48f + i * 16f, 0f, side * 22f);
                if (NearPlot(p)) continue;
                if (i % 2 == 0)
                {
                    CB(cityRoot, "Chatrc", p, new Vector3(9f, 3.6f, 7f), mMetalRust);
                    CB(cityRoot, "StrechaPlech", p + Vector3.up * 3.6f, new Vector3(10f, 0.25f, 8f), Mat(new Color(0.3f, 0.34f, 0.36f)), false, 0f);
                }
                else
                {
                    CB(cityRoot, "StanL", p + new Vector3(-1.6f, 0f, 0f), new Vector3(0.2f, 3.2f, 6.5f), mCloth, false, 0f).transform.Rotate(0f, 0f, -35f);
                    CB(cityRoot, "StanP", p + new Vector3(1.6f, 0f, 0f), new Vector3(0.2f, 3.2f, 6.5f), mCloth, false, 0f).transform.Rotate(0f, 0f, 35f);
                    CB(cityRoot, "StanKolizia", p, new Vector3(4f, 2.5f, 6.5f), mCloth, true).GetComponent<Renderer>().enabled = false;
                }
            }
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = new Vector3(40f + i * 24f, 0f, 8f);
            CB(cityRoot, "Vrece", p, new Vector3(5f, 1.1f, 1.2f), Mat(new Color(0.55f, 0.5f, 0.38f)));
            Vector3 barrel = new Vector3(44f + i * 24f, 0f, -9f);
            CPart(cityRoot, PrimitiveType.Cylinder, "Sud", barrel, new Vector3(0.9f, 0.55f, 0.9f), mMetalRust, true);
            CLight(cityRoot, barrel + Vector3.up * 1.4f, new Color(1f, 0.5f, 0.15f), 11f, 1.8f);
            CB(cityRoot, "Ohen", barrel + Vector3.up * 1.1f, new Vector3(0.5f, 0.4f, 0.5f), Mat(new Color(1f, 0.45f, 0.1f), true), false);
        }
        Vector3 watch = new Vector3(96f, 0f, 0f);
        foreach (Vector2 o in new[] { new Vector2(-1.8f, -1.8f), new Vector2(1.8f, -1.8f), new Vector2(-1.8f, 1.8f), new Vector2(1.8f, 1.8f) })
            CB(cityRoot, "StrazStlp", watch + new Vector3(o.x, 0f, o.y), new Vector3(0.35f, 8f, 0.35f), mWoodDark);
        CB(cityRoot, "StrazPlosina", watch + Vector3.up * 8f, new Vector3(5.2f, 0.3f, 5.2f), mWoodDark);
        CB(cityRoot, "StrazStrecha", watch + Vector3.up * 11f, new Vector3(5.8f, 0.25f, 5.8f), mMetalRust, false);
        CB(cityRoot, "Vlajka", watch + new Vector3(0f, 11.2f, 0f), new Vector3(0.1f, 2.5f, 0.1f), mWoodDark, false);
        CB(cityRoot, "Zastava", watch + new Vector3(0.9f, 12.6f, 0f), new Vector3(1.6f, 1f, 0.05f), mCloth, false);

        // Koreňový uzol: démon sa cez korene dostane do mesta (strom temnoty tu zasadí hra)
        var marker = new GameObject("KorenovyUzol_Mesto");
        marker.transform.SetParent(cityRoot, false);
        marker.transform.position = cityRoot.TransformPoint(new Vector3(0f, 0f, -42f));
        marker.transform.position = new Vector3(marker.transform.position.x, CityGroundY(marker.transform.position, center.y), marker.transform.position.z);

        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = rootGo;
        Debug.Log("Centrum mesta postavené: námestie, civilná štvrť (západ), štvrť odboja (východ), asfaltové ulice. Presuň koreň 'CityHub' podľa potreby (označ objekt a spusti znova, aby sa postavilo na ňom). Ulož scénu.");
    }

    // ---------- Megakorporácia: nové mesto ----------
    [MenuItem("Progeny/Megakorporácia/Postaviť mesto (mrakodrapy, asfalt, veže)", false, 320)]
    static void BuildCorpCity()
    {
        GameObject ground = GameObject.Find("Zem_Megakorporacia");
        if (ground == null)
        {
            GameObject civilGate = GameObject.Find("Portal_DoMegakorporacie");
            if (civilGate != null)
            {
                // Zem chýba (napr. zmazaný koreň CityPortals): vytvorí sa na pevnom mieste bez zásahu do portálov
                Vector3 pos = new Vector3(civilGate.transform.position.x + CorpOffset.x, civilGate.transform.position.y - 1.5f, civilGate.transform.position.z + CorpOffset.z);
                BuildZoneGround(null, "Zem_Megakorporacia", pos, new Vector2(230f, 230f), new Color(0.18f, 0.2f, 0.24f));
            }
            else CreateCityPortals();
            ground = GameObject.Find("Zem_Megakorporacia");
            if (ground == null) return;
        }

        const float r = 110f;
        ground.transform.localScale = new Vector3(2f * r + 10f, 1f, 2f * r + 10f);
        Vector3 c = ground.transform.position + Vector3.up * 0.5f;

        foreach (string stale in new[] { "CorpComplex", "CorpCity" })
        {
            GameObject old = GameObject.Find(stale);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }
        var rootGo = new GameObject("CorpCity");
        Undo.RegisterCreatedObjectUndo(rootGo, "Corp City");
        rootGo.transform.position = c;
        cityRoot = rootGo.transform;
        MakeCityMaterials();
        float baseY = c.y;

        Material steel = Mat(new Color(0.2f, 0.22f, 0.26f));
        Material pave = Mat(new Color(0.3f, 0.31f, 0.33f));

        // Asfalt, chodníky, ciary
        CB(cityRoot, "AvenueV", Vector3.zero, new Vector3(2f * r - 6f, 0.1f, 14f), mAsphalt, true, 0f, baseY);
        CB(cityRoot, "AvenueS", Vector3.zero, new Vector3(14f, 0.1f, 2f * r - 6f), mAsphalt, true, 0f, baseY);
        foreach (float o in new[] { -58f, 58f })
        {
            CB(cityRoot, "UlicaV", new Vector3(0f, 0f, o), new Vector3(2f * r - 6f, 0.1f, 8f), mAsphalt, true, 0f, baseY);
            CB(cityRoot, "UlicaS", new Vector3(o, 0f, 0f), new Vector3(8f, 0.1f, 2f * r - 6f), mAsphalt, true, 0f, baseY);
        }
        CB(cityRoot, "NamestiePortal", Vector3.zero, new Vector3(36f, 0.16f, 36f), pave, true, 0f, baseY);
        CB(cityRoot, "NeonPrstenec", new Vector3(0f, 0.16f, 0f), new Vector3(30f, 0.05f, 30f), mNeon, false, 0f, baseY);
        CB(cityRoot, "NamestieVnutro", new Vector3(0f, 0.2f, 0f), new Vector3(28f, 0.05f, 28f), pave, false, 0f, baseY);
        foreach (int side in new[] { -1, 1 })
        {
            CB(cityRoot, "ChodnikV", new Vector3(0f, 0f, side * 9f), new Vector3(2f * r - 6f, 0.16f, 3f), pave, true, 0f, baseY);
            CB(cityRoot, "ChodnikS", new Vector3(side * 9f, 0f, 0f), new Vector3(3f, 0.16f, 2f * r - 6f), pave, true, 0f, baseY);
        }
        DashedLine(cityRoot, new Vector3(-r + 4f, 0f, 0f), new Vector3(-20f, 0f, 0f), 7f);
        DashedLine(cityRoot, new Vector3(20f, 0f, 0f), new Vector3(r - 4f, 0f, 0f), 7f);
        DashedLine(cityRoot, new Vector3(0f, 0f, -r + 4f), new Vector3(0f, 0f, -20f), 7f);
        DashedLine(cityRoot, new Vector3(0f, 0f, 20f), new Vector3(0f, 0f, r - 4f), 7f);

        // Obvodový múr s neónovým pásom
        Material wallMat = Mat(new Color(0.18f, 0.2f, 0.23f));
        CB(cityRoot, "Obvod_S", new Vector3(0f, 0f, r), new Vector3(2f * r, 7f, 3f), wallMat, true, 0f, baseY);
        CB(cityRoot, "Obvod_J", new Vector3(0f, 0f, -r), new Vector3(2f * r, 7f, 3f), wallMat, true, 0f, baseY);
        CB(cityRoot, "Obvod_V", new Vector3(r, 0f, 0f), new Vector3(3f, 7f, 2f * r), wallMat, true, 0f, baseY);
        CB(cityRoot, "Obvod_Z", new Vector3(-r, 0f, 0f), new Vector3(3f, 7f, 2f * r), wallMat, true, 0f, baseY);
        CB(cityRoot, "NeonS", new Vector3(0f, 5.5f, r - 1.6f), new Vector3(2f * r, 0.25f, 0.1f), mNeon, false, 0f, baseY);
        CB(cityRoot, "NeonJ", new Vector3(0f, 5.5f, -r + 1.6f), new Vector3(2f * r, 0.25f, 0.1f), mNeon, false, 0f, baseY);
        CB(cityRoot, "NeonV", new Vector3(r - 1.6f, 5.5f, 0f), new Vector3(0.1f, 0.25f, 2f * r), mNeon, false, 0f, baseY);
        CB(cityRoot, "NeonZ", new Vector3(-r + 1.6f, 5.5f, 0f), new Vector3(0.1f, 0.25f, 2f * r), mNeon, false, 0f, baseY);

        // Strážne veže: 4 v rohoch a 4 v strede strán
        int n = 0;
        foreach (Vector2 p in new[] { new Vector2(-r, -r), new Vector2(r, -r), new Vector2(-r, r), new Vector2(r, r), new Vector2(0f, -r), new Vector2(0f, r), new Vector2(-r, 0f), new Vector2(r, 0f) })
            GuardTower(cityRoot, $"Veza_{++n}", new Vector3(p.x, 0f, p.y), baseY, steel, new Vector3(-p.x, 0f, -p.y));

        // Mrakodrapy: štyri kvadranty po štyri bloky (dva rady medzi avenue a ulicami)
        var rnd = new System.Random(5);
        int corpPlotNo = 0;
        float[][] ranges = { new[] { 11f, 54f }, new[] { 62f, 103f } };
        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                foreach (float[] rx in ranges)
                    foreach (float[] rz in ranges)
                    {
                        float cx = sx * (rx[0] + rx[1]) * 0.5f, cz = sz * (rz[0] + rz[1]) * 0.5f;
                        if (rx[0] == 62f && rz[0] == 62f)
                        {
                            // Vzdialené rohové bloky: parcely na stavbu (obytné domy, kasárne, bezpečnostné centrum)
                            foreach (Vector2 o in new[] { new Vector2(-10.5f, -10.5f), new Vector2(10.5f, -10.5f), new Vector2(-10.5f, 10.5f), new Vector2(10.5f, 10.5f) })
                                CorpPlot(cityRoot, new Vector3(cx + o.x, 0f, cz + o.y), baseY, ++corpPlotNo);
                            continue;
                        }
                        float bw = rx[1] - rx[0] - 14f, bd = rz[1] - rz[0] - 14f;
                        bool hq = sx == 1 && sz == 1 && rx[0] == 11f && rz[0] == 11f;
                        float height = hq ? 100f : 32f + (float)rnd.NextDouble() * 46f;
                        Skyscraper(cityRoot, new Vector3(cx, 0f, cz), Mathf.Min(bw, hq ? 26f : 20f + (float)rnd.NextDouble() * 6f), Mathf.Min(bd, hq ? 26f : 20f + (float)rnd.NextDouble() * 6f), height, baseY, hq);

                        // Nízke budovy pri rohoch bloku
                        for (int k = 0; k < 2; k++)
                        {
                            float lx = cx + (k == 0 ? -1f : 1f) * (rx[1] - rx[0]) * 0.34f * (sx);
                            float lz = cz + (k == 0 ? 1f : -1f) * (rz[1] - rz[0]) * 0.34f;
                            if (Mathf.Abs(lx - cx) < 12f && Mathf.Abs(lz - cz) < 12f) continue;
                            CB(cityRoot, "Sklad", new Vector3(lx, 0f, lz), new Vector3(9f, 5f + (float)rnd.NextDouble() * 4f, 8f), steel, true, 0f, baseY);
                            CB(cityRoot, "SkladNeon", new Vector3(lx, 3.4f, lz), new Vector3(9.1f, 0.2f, 8.1f), mNeon, false, 0f, baseY);
                        }
                    }

        // Lampy pozdĺž avenue
        for (float s = -r + 14f; s <= r - 14f; s += 22f)
        {
            if (Mathf.Abs(s) < 20f) continue;
            foreach (int side in new[] { -1, 1 })
            {
                CB(cityRoot, "StlpAvenue", new Vector3(s, 0.16f, side * 10.5f), new Vector3(0.25f, 6f, 0.25f), steel, true, 0f, baseY);
                CB(cityRoot, "LampaAvenue", new Vector3(s, 6f, side * 10.5f), new Vector3(0.8f, 0.2f, 0.5f), mNeon, false, 0f, baseY);
                CB(cityRoot, "StlpAvenue", new Vector3(side * 10.5f, 0.16f, s), new Vector3(0.25f, 6f, 0.25f), steel, true, 0f, baseY);
            }
        }

        // Vojenská zóna na celý komplex
        GameObject zoneGo = new GameObject("MilitaryZone_Megakorporacia");
        zoneGo.transform.SetParent(cityRoot);
        zoneGo.transform.position = c;
        MilitaryZone zone = zoneGo.AddComponent<MilitaryZone>();
        BoxCollider zoneBox = zone.GetComponent<BoxCollider>();
        zoneBox.size = new Vector3(2f * r - 8f, 12f, 2f * r - 8f);
        zoneBox.center = new Vector3(0f, 6f, 0f);

        Physics.SyncTransforms();
        Vector3[] posts =
        {
            new Vector3(-14f, 0f, 14f), new Vector3(14f, 0f, -14f), new Vector3(-40f, 0f, 3f), new Vector3(40f, 0f, -3f),
            new Vector3(3f, 0f, 40f), new Vector3(-3f, 0f, -40f), new Vector3(-80f, 0f, 3f), new Vector3(80f, 0f, -3f),
            new Vector3(3f, 0f, 80f), new Vector3(-3f, 0f, -80f), new Vector3(-58f, 0f, 60f), new Vector3(58f, 0f, -60f)
        };
        for (int i = 0; i < posts.Length; i++) SpawnSoldier(cityRoot, $"Soldier_{i + 1}", c + posts[i]);

        EnsurePlayerComponents(Object.FindAnyObjectByType<PlayerStats>());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = rootGo;
        EnsurePlotManager();
        Debug.Log("Megakorporácia prerobená: zem 230×230 m, asfaltové avenue, 12 mrakodrapov, 8 strážnych veží, múr, 12 vojakov a 16 parciel (obytný dom, kasárne, bezpečnostné centrum). Ulož scénu.");
    }

    static void Skyscraper(Transform parent, Vector3 pos, float w, float d, float h, float baseY, bool hq)
    {
        var tower = new GameObject(hq ? "HlavnaVeza" : "Mrakodrap");
        tower.transform.SetParent(parent, false);
        CB(tower.transform, "Podstava", pos, new Vector3(w + 4f, 4f, d + 4f), mConcrete, true, 0f, baseY);
        CB(tower.transform, "Telo", pos, new Vector3(w, h, d), mGlass, true, 0f, baseY);

        // Osvetlené okná: zvislé pásy na všetkých štyroch stranách
        int bands = Mathf.Max(3, Mathf.RoundToInt(w / 5f));
        for (int i = 0; i < bands; i++)
        {
            float off = (i + 0.5f) / bands * w - w * 0.5f;
            float offD = (i + 0.5f) / bands * d - d * 0.5f;
            float wh = h - 10f - (i % 3) * 5f;
            CB(tower.transform, "OknaS", pos + new Vector3(off, 6f, d * 0.5f + 0.05f), new Vector3(1.4f, wh, 0.1f), mWindow, false, 0f, baseY);
            CB(tower.transform, "OknaJ", pos + new Vector3(off, 6f, -d * 0.5f - 0.05f), new Vector3(1.4f, wh, 0.1f), mWindow, false, 0f, baseY);
            CB(tower.transform, "OknaV", pos + new Vector3(w * 0.5f + 0.05f, 6f, offD), new Vector3(0.1f, wh, 1.4f), mWindow, false, 0f, baseY);
            CB(tower.transform, "OknaZ", pos + new Vector3(-w * 0.5f - 0.05f, 6f, offD), new Vector3(0.1f, wh, 1.4f), mWindow, false, 0f, baseY);
        }
        CB(tower.transform, "Koruna", pos + new Vector3(0f, h, 0f), new Vector3(w + 0.6f, 1.2f, d + 0.6f), mConcrete, false, 0f, baseY);
        CB(tower.transform, "KorunaNeon", pos + new Vector3(0f, h + 1.2f, 0f), new Vector3(w + 0.8f, 0.4f, d + 0.8f), mNeon, false, 0f, baseY);
        CB(tower.transform, "Anténa", pos + new Vector3(0f, h + 1.6f, 0f), new Vector3(0.4f, hq ? 22f : 10f, 0.4f), mConcrete, false, 0f, baseY);
        if (hq) CLight(tower.transform, pos + new Vector3(0f, h + 24f, 0f), new Color(1f, 0.2f, 0.2f), 40f, 2f);
    }

    static void GuardTower(Transform parent, string name, Vector3 pos, float baseY, Material steel, Vector3 lookAt)
    {
        var tower = new GameObject(name);
        tower.transform.SetParent(parent, false);
        Material dark = Mat(new Color(0.12f, 0.13f, 0.15f));
        CB(tower.transform, "Zaklad", pos, new Vector3(8f, 3f, 8f), steel, true, 0f, baseY);
        foreach (Vector2 o in new[] { new Vector2(-2f, -2f), new Vector2(2f, -2f), new Vector2(-2f, 2f), new Vector2(2f, 2f) })
            CB(tower.transform, "Noha", pos + new Vector3(o.x, 3f, o.y), new Vector3(0.7f, 14f, 0.7f), steel, true, 0f, baseY);
        for (int k = 0; k < 3; k++)
            CB(tower.transform, "Vystuha", pos + new Vector3(0f, 6f + k * 4f, 0f), new Vector3(4.6f, 0.3f, 4.6f), dark, false, 0f, baseY);
        CB(tower.transform, "Plosina", pos + new Vector3(0f, 17f, 0f), new Vector3(8f, 0.6f, 8f), steel, true, 0f, baseY);
        CB(tower.transform, "Kabina", pos + new Vector3(0f, 17.6f, 0f), new Vector3(6.2f, 3.2f, 6.2f), mGlass, true, 0f, baseY);
        CB(tower.transform, "KabinaPas", pos + new Vector3(0f, 19f, 0f), new Vector3(6.4f, 0.9f, 6.4f), mWindow, false, 0f, baseY);
        CB(tower.transform, "Strecha", pos + new Vector3(0f, 20.8f, 0f), new Vector3(8.4f, 0.6f, 8.4f), dark, false, 0f, baseY);
        CB(tower.transform, "Neon", pos + new Vector3(0f, 21.4f, 0f), new Vector3(8.6f, 0.25f, 8.6f), mNeon, false, 0f, baseY);
        CB(tower.transform, "Antena", pos + new Vector3(2.5f, 21.4f, 2.5f), new Vector3(0.2f, 6f, 0.2f), dark, false, 0f, baseY);

        // Reflektor mieri do komplexu
        var lampGo = new GameObject("Reflektor");
        lampGo.transform.SetParent(tower.transform, false);
        Vector3 world = parent.TransformPoint(pos);
        lampGo.transform.position = new Vector3(world.x, baseY + 19f, world.z);
        Vector3 dir = parent.TransformDirection(lookAt - pos);
        dir.y = -0.35f * dir.magnitude;
        lampGo.transform.rotation = Quaternion.LookRotation(dir);
        Light spot = lampGo.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.range = 90f;
        spot.spotAngle = 38f;
        spot.intensity = 3.2f;
        spot.color = new Color(0.85f, 0.95f, 1f);
    }
}
