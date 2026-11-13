using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Nová zóna Centrum mesta: rovná plocha bez kopcov, v strede námestie so štvrťami civilistov (západ) a odboja (východ),
/// okolo parcely na výstavbu a stromy z modelov. Vlastná brána z mesta odboja a obyvatelia zóny. Plus parcely v Megakorporácii.
/// </summary>
public static partial class ProgenyTools
{
    static void CorpPlot(Transform parent, Vector3 local, float groundY, int number)
    {
        Vector3 pos = parent.TransformPoint(local);
        pos.y = groundY;

        var plotGo = new GameObject($"Plot_K_{number:00}");
        plotGo.transform.SetParent(parent);
        plotGo.transform.position = pos;
        Slab(plotGo.transform, "Podklad", pos + Vector3.up * 0.07f, new Vector2(PlotSide, PlotSide), new Color(0.2f, 0.22f, 0.26f));

        BuildPlot plot = plotGo.AddComponent<BuildPlot>();
        plot.Configure($"K_{number:00}", FactionId.Megacorp, 120 + number * 15, number <= 4 ? 0f : 4f + number);
        EditorUtility.SetDirty(plot);
    }

    static readonly Vector3 CentrumOffset = new Vector3(-380f, 0f, -80f);   // západ od brány v meste, pevné miesto

    static GameObject SpawnShopkeeper(Transform parent, string name, Vector3 world, string kind, bool civil, int special = -1)
    {
        GameObject m = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        m.name = name;
        m.transform.SetParent(parent);
        m.transform.position = world + Vector3.up * 1f;
        Undo.RegisterCreatedObjectUndo(m, "Obchodník");
        var merchant = m.AddComponent<MerchantNPC>();
        merchant.faction = civil ? FactionId.Civilians : FactionId.Rebels;
        merchant.specialIndex = special;
        merchant.betterWeaponStock = kind == "Zbrane" ? 3 : kind == "Zásoby" ? 1 : 0;
        merchant.herbStock = kind == "Lekáreň" ? 20 : kind == "Zásoby" ? 10 : 0;
        merchant.bloodJarStock = kind == "Krv" ? 20 : kind == "Zásoby" ? 10 : 0;
        m.GetComponent<Renderer>().sharedMaterial = Mat(civil ? new Color(0.75f, 0.55f, 0.2f) : new Color(0.55f, 0.2f, 0.2f));
        return m;
    }

    static int signCounter;

    static void StallSign(Transform parent, string text, Vector3 world, float characterSize = 0.055f)
    {
        PortalNetwork.MakeLabel(world, parent, "Napis_" + text + "_" + (++signCounter), text, characterSize);   // otáča sa ku kamere
    }

    [MenuItem("Progeny/Mesto/Zóna Centrum (ulice, trhy, parcely, stromy)", false, 311)]
    static void BuildCityZone()
    {
        GameObject civilGate = GameObject.Find("Portal_DoMegakorporacie");
        if (civilGate == null)
        {
            CreateCityPortals();
            civilGate = GameObject.Find("Portal_DoMegakorporacie");
            if (civilGate == null) return;
        }

        const float half = 150f;
        Vector3 civilGround = civilGate.transform.position + Vector3.down * 1.5f;
        Vector3 c = civilGround + CentrumOffset;
        c.y = civilGround.y;

        foreach (string stale in new[] { "Mesto_Centrum", "CityHub" })
        {
            GameObject old = GameObject.Find(stale);
            if (old != null) Undo.DestroyObjectImmediate(old);
        }
        var root = new GameObject("Mesto_Centrum");
        Undo.RegisterCreatedObjectUndo(root, "Mesto Centrum");
        root.transform.position = c;
        cityRoot = root.transform;
        MakeCityMaterials();
        float baseY = c.y;

        GameObject B(string name, Vector3 local, Vector3 size, Material mat, bool collider = true, float yaw = 0f) =>
            CB(cityRoot, name, local, size, mat, collider, yaw, baseY);

        // Zem (rovina) s trávou
        BuildZoneGround(root.transform, "Zem_Centrum", c, new Vector2(2f * half + 10f, 2f * half + 10f), new Color(0.22f, 0.3f, 0.17f));
        Transform ground = root.transform.Find("Zem_Centrum");
        Material grass = LoadSurface("mossy_grass") ?? LoadSurface("grass_and_rubble");
        if (ground != null && grass != null)
        {
            var gm = new Material(grass);
            gm.mainTextureScale = new Vector2((2f * half + 10f) / 4f, (2f * half + 10f) / 4f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;
        }

        // Obvodový múr
        B("Hranica_S", new Vector3(0f, 0f, half), new Vector3(2f * half, 5f, 3f), mConcrete);
        B("Hranica_J", new Vector3(0f, 0f, -half), new Vector3(2f * half, 5f, 3f), mConcrete);
        B("Hranica_V", new Vector3(half, 0f, 0f), new Vector3(3f, 5f, 2f * half), mConcrete);
        B("Hranica_Z", new Vector3(-half, 0f, 0f), new Vector3(3f, 5f, 2f * half), mConcrete);

        // Ulice (asfalt): dve avenue a dve priečne ulice na každú stranu
        B("AvenueV", Vector3.zero, new Vector3(2f * half - 6f, 0.1f, 14f), mAsphalt);
        B("AvenueS", Vector3.zero, new Vector3(14f, 0.1f, 2f * half - 6f), mAsphalt);
        foreach (float o in new[] { -75f, 75f })
        {
            B("UlicaV", new Vector3(0f, 0f, o), new Vector3(2f * half - 6f, 0.1f, 8f), mAsphalt);
            B("UlicaS", new Vector3(o, 0f, 0f), new Vector3(8f, 0.1f, 2f * half - 6f), mAsphalt);
        }
        // Chodníky pozdĺž avenue len od kruhového objazdu von (nepretínajú ho)
        float walkFrom = RoundaboutRadius + 2f, walkLength = half - 3f - walkFrom;
        foreach (int side in new[] { -1, 1 })
            foreach (int dir in new[] { -1, 1 })
            {
                float mid = dir * (walkFrom + walkLength * 0.5f);
                B("ChodnikV", new Vector3(mid, 0f, side * 9f), new Vector3(walkLength, 0.16f, 3f), mCurb);
                B("ChodnikS", new Vector3(side * 9f, 0f, mid), new Vector3(3f, 0.16f, walkLength), mCurb);
            }
        float dashFrom = RoundaboutRadius + 2f;
        DashedLine(cityRoot, new Vector3(-half + 4f, 0f, 0f), new Vector3(-dashFrom, 0f, 0f), 7f);
        DashedLine(cityRoot, new Vector3(dashFrom, 0f, 0f), new Vector3(half - 4f, 0f, 0f), 7f);
        DashedLine(cityRoot, new Vector3(0f, 0f, -half + 4f), new Vector3(0f, 0f, -dashFrom), 7f);
        DashedLine(cityRoot, new Vector3(0f, 0f, dashFrom), new Vector3(0f, 0f, half - 4f), 7f);

        // Kruhový objazd v strede: pásy dlažby, asfaltový okruh, obrubník a trávnatý kopček so stromčekom
        BuildRoundabout(root.transform, baseY, grass);
        Sign(cityRoot, "CENTRUM MESTA", new Vector3(0f, 0.2f, -42f), 0f);
        Sign(cityRoot, "CIVILNÁ ŠTVRŤ", new Vector3(-46f, 0.2f, 12f), 90f);
        Sign(cityRoot, "ŠTVRŤ ODBOJA", new Vector3(46f, 0.2f, -12f), 90f);

        // Bloky: vnútorné = trhoviská, zmiešané = domy, vonkajšie = parcely
        var parcels = new GameObject("Parcely_Centrum").transform;
        parcels.SetParent(root.transform, false);
        var shops = new GameObject("Obchody").transform;
        shops.SetParent(root.transform, false);
        float[][] ranges = { new[] { 11f, 70f }, new[] { 80f, 142f } };
        string[] civilStalls = { "Zbrane", "Lekáreň", "Zásoby", "Lekáreň", "Zbrane", "Zásoby" };
        string[] rebelStalls = { "Zbrane", "Krv", "Zásoby", "Krv", "Zbrane", "Lekáreň" };
        int plotNo = 0, merchants = 0, houses = 0;

        foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
                foreach (float[] rx in ranges)
                    foreach (float[] rz in ranges)
                    {
                        float cx = sx * (rx[0] + rx[1]) * 0.5f, cz = sz * (rz[0] + rz[1]) * 0.5f;
                        bool innerX = rx[0] < 20f, innerZ = rz[0] < 20f;
                        bool civil = sx < 0;

                        if (innerX && innerZ)
                        {
                            // Trhovisko: dláždené nádvorie a 6 stánkov s obchodníkmi
                            // Trhovisko sa odsunie mimo kruhového objazdu (r = 34): spadá do pásu 27–70 m od stredu
                            float tx = sx * 48.5f, tz = sz * 48.5f;
                            B("Trhovisko", new Vector3(tx, 0f, tz), new Vector3(43f, 0.14f, 43f), mPaving);
                            string[] types = civil ? civilStalls : rebelStalls;
                            int k = 0;
                            foreach (float dx in new[] { -15f, 15f })
                                foreach (float dz in new[] { -18f, 0f, 18f })
                                {
                                    int stallIdx = k;
                                    string type = types[k++ % types.Length];
                                    int special = sz < 0 && stallIdx < 3 ? stallIdx + (civil ? 0 : 3) : -1;   // šesť obchodníkov so špeciálnym predmetom
                                    Vector3 p = new Vector3(tx + dx, 0f, tz + dz);
                                    float inward = -Mathf.Sign(dx);
                                    B("StanokStol", p, new Vector3(1.8f, 1.1f, 4.5f), mWoodDark);
                                    B("StanokStrecha", p + Vector3.up * 2.7f, new Vector3(3.4f, 0.2f, 5.2f), civil ? mWhite : mCloth, false);
                                    B("StanokStlp", p + new Vector3(inward * 1.4f, 0f, -2.2f), new Vector3(0.15f, 2.7f, 0.15f), mWoodDark, false);
                                    B("StanokStlp", p + new Vector3(inward * 1.4f, 0f, 2.2f), new Vector3(0.15f, 2.7f, 0.15f), mWoodDark, false);
                                    B("Debna", p + new Vector3(-inward * 2.4f, 0f, 1.2f), new Vector3(0.9f, 0.9f, 0.9f), mWoodDark);
                                    Vector3 world = cityRoot.TransformPoint(p + new Vector3(inward * 1.7f, 0f, 0f));
                                    StallSign(shops, special >= 0 ? SpecialGoods.Items[special].seller + "\n" + SpecialGoods.Items[special].name : type, new Vector3(world.x, baseY + 3.8f, world.z));
                                    SpawnShopkeeper(shops, $"Obchodnik_{type}_{++merchants}", cityRoot.TransformPoint(p + new Vector3(inward * 1.5f, 0f, 0f)), type, civil, special);
                                }
                        }
                        else if (!innerX && !innerZ)
                        {
                            // Parcely 3 x 3
                            for (int ix = -1; ix <= 1; ix++)
                                for (int iz = -1; iz <= 1; iz++)
                                {
                                    plotNo++;
                                    Vector3 pos = new Vector3(c.x + cx + ix * 21f, baseY, c.z + cz + iz * 21f);
                                    var plotGo = new GameObject($"Plot_C_{plotNo:00}");
                                    plotGo.transform.SetParent(parcels);
                                    plotGo.transform.position = pos;
                                    Slab(plotGo.transform, "Podklad", pos + Vector3.up * 0.07f, new Vector2(PlotSide, PlotSide), new Color(0.24f, 0.22f, 0.2f));

                                    BuildPlot plot = plotGo.AddComponent<BuildPlot>();
                                    plot.Configure($"C_{plotNo:00}", civil ? FactionId.Civilians : FactionId.Rebels, 60 + plotNo * 6, Mathf.Min(20f, (civil ? 0f : 3f) + plotNo * 0.5f));
                                    EditorUtility.SetDirty(plot);
                                }
                        }
                        else
                        {
                            // Štvrť: 4 domy (civilné murované, odbojové z plechu) so stromom pri každom
                            foreach (Vector2 o in new[] { new Vector2(-16f, -16f), new Vector2(16f, -16f), new Vector2(-16f, 16f), new Vector2(16f, 16f) })
                            {
                                Vector3 p = new Vector3(cx + o.x, 0f, cz + o.y);
                                houses++;
                                // Dom sa otáča čelom k blízkej avenue (vnútorná os bloku)
                                Vector3 toStreet = innerX ? new Vector3(-Mathf.Sign(cx), 0f, 0f) : new Vector3(0f, 0f, -Mathf.Sign(cz));
                                if (PlaceHouseModel(cityRoot, civil, houses, cityRoot.TransformPoint(p), toStreet, baseY, civil ? 12f : 19f))
                                {
                                    Vector3 twm = cityRoot.TransformPoint(p + new Vector3(o.x > 0 ? 9.5f : -9.5f, 0f, 0f));
                                    HouseTree(cityRoot, twm, baseY);
                                    continue;
                                }
                                if (civil)
                                {
                                    B("Dom", p, new Vector3(11f, 5.5f, 9f), houses % 2 == 0 ? mBrick : mPlaster);
                                    B("Strecha", p + Vector3.up * 5.5f, new Vector3(12.4f, 0.6f, 10.4f), mRoofRed, false);
                                    B("Dvere", p + new Vector3(0f, 0f, -Mathf.Sign(o.y) * 4.6f), new Vector3(1.4f, 2.4f, 0.2f), mWoodDark, false);
                                    B("Okno", p + new Vector3(3f, 1.6f, -Mathf.Sign(o.y) * 4.55f), new Vector3(1.3f, 1.2f, 0.15f), mWindow, false);
                                    B("Okno", p + new Vector3(-3f, 1.6f, -Mathf.Sign(o.y) * 4.55f), new Vector3(1.3f, 1.2f, 0.15f), mWindow, false);
                                    B("Komin", p + new Vector3(3.5f, 6f, 2f), new Vector3(1f, 2.2f, 1f), mBrick, false);
                                }
                                else
                                {
                                    B("Chatrc", p, new Vector3(10f, 3.8f, 8f), mMetalRust);
                                    B("StrechaPlech", p + Vector3.up * 3.8f, new Vector3(11f, 0.25f, 9f), Mat(new Color(0.3f, 0.34f, 0.36f)), false);
                                    B("Dvere", p + new Vector3(0f, 0f, -Mathf.Sign(o.y) * 4.05f), new Vector3(1.3f, 2.2f, 0.2f), mWoodDark, false);
                                    CLight(cityRoot, p + new Vector3(6.5f, 1.4f, 0f), new Color(1f, 0.5f, 0.15f), 9f, 1.5f);
                                    B("Sud", p + new Vector3(6.5f, 0f, 0f), new Vector3(0.9f, 1.1f, 0.9f), mMetalRust);
                                }
                                Vector3 tw = cityRoot.TransformPoint(p + new Vector3(o.x > 0 ? 9.5f : -9.5f, 0f, 0f));
                                HouseTree(cityRoot, tw, baseY);
                            }
                        }
                    }

        // Kupec: výkup surovín za dynamické ceny (pri námestí)
        {
            Vector3 kp = new Vector3(16f, 0f, 38f);   // pri kruhovom objazde, mimo okruhu
            B("KupecStol", kp, new Vector3(4f, 1.1f, 1.5f), mWoodDark);
            B("KupecStrecha", kp + new Vector3(0f, 2.8f, 0f), new Vector3(4.8f, 0.2f, 2.8f), mCloth, false);
            B("KupecStlp", kp + new Vector3(-2.1f, 0f, -1.2f), new Vector3(0.15f, 2.8f, 0.15f), mWoodDark, false);
            B("KupecStlp", kp + new Vector3(2.1f, 0f, -1.2f), new Vector3(0.15f, 2.8f, 0.15f), mWoodDark, false);
            B("KupecDebna", kp + new Vector3(2.8f, 0f, 1.6f), new Vector3(1f, 1f, 1f), mWoodDark);
            B("KupecDebna", kp + new Vector3(-2.8f, 0f, 1.6f), new Vector3(0.9f, 0.9f, 0.9f), mWoodDark);
            Vector3 kw = cityRoot.TransformPoint(kp);
            StallSign(shops, "KUPEC\nvýkup surovín", new Vector3(kw.x, baseY + 4.2f, kw.z), 0.07f);

            GameObject kupec = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            kupec.name = "Kupec";
            kupec.transform.SetParent(shops);
            kupec.transform.position = cityRoot.TransformPoint(kp + new Vector3(0f, 0f, 1.3f)) + Vector3.up;
            Undo.RegisterCreatedObjectUndo(kupec, "Kupec");
            kupec.AddComponent<BuyerNPC>();
            kupec.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.8f, 0.7f, 0.2f));
        }

        // Stromy z modelov pozdĺž avenue a lampy
        int lampIndex = 0;
        for (float s = -half + 14f; s <= half - 14f; s += 24f)
        {
            if (Mathf.Abs(s) < 28f) continue;
            lampIndex++;
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 e = cityRoot.TransformPoint(new Vector3(s, 0f, side * 12.5f));
                Vector3 n = cityRoot.TransformPoint(new Vector3(side * 12.5f, 0f, s));
                HouseTree(cityRoot, e, baseY);
                HouseTree(cityRoot, n, baseY);
                B("StlpAvenue", new Vector3(s + 12f, 0.16f, side * 10.5f), new Vector3(0.25f, 6f, 0.25f), mWoodDark);
                B("LampaAvenue", new Vector3(s + 12f, 6f, side * 10.5f), new Vector3(0.8f, 0.2f, 0.5f), mWindow, false);
                if (lampIndex % 2 == 0) CLight(cityRoot, new Vector3(s + 12f, 5.8f, side * 10.5f), new Color(1f, 0.8f, 0.55f), 22f, 1.2f);
            }
        }

        // Koreňový uzol (cesta démona) pri námestí
        var marker = new GameObject("KorenovyUzol_Mesto");
        marker.transform.SetParent(cityRoot, false);
        Vector3 markerWorld = cityRoot.TransformPoint(new Vector3(-24f, 0f, -30f));
        marker.transform.position = new Vector3(markerWorld.x, baseY, markerWorld.z);

        // Brány: zo štartu (vľavo od ostatných) a späť na južnom konci avenue
        Vector3 gateLocal = new Vector3(0f, 0f, -(half - 18f));
        PortalGate toZone = BuildPortal(root.transform, "Portal_DoCentra", civilGround + civilGate.transform.right * -12f, new Color(0.4f, 1f, 0.5f), civilGate.transform.rotation);
        PortalGate fromZone = BuildPortal(root.transform, "Portal_zCentra", c + gateLocal, new Color(0.35f, 0.7f, 1f), Quaternion.LookRotation(Vector3.forward));
        toZone.Link(fromZone, "Centrum mesta");
        fromZone.Link(toZone, WorldNames.StartCity);
        EditorUtility.SetDirty(toZone);
        EditorUtility.SetDirty(fromZone);

        Physics.SyncTransforms();
        EnsurePlotManager();
        BuildCityPopulationAt("Obyvatelia_Centrum", "Centrum mesta", c, new Vector3(2f * half, 40f, 2f * half), 14);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;
        Debug.Log($"Zóna Centrum mesta: rovina 300x300 m, asfaltové ulice, námestie, 4 trhoviská ({merchants} obchodníkov), {houses} domov, {plotNo} parciel, stromy z modelov Fir. Ulož scénu.");
    }
}

public static partial class ProgenyTools
{
    static int houseTreeCounter;

    // Predná strana modelov domov (smer dverí v modeli); ak sú dvere na opačnej strane, zmeň na 180
    const float HouseModelYawOffset = 0f;
    static readonly string[] CivilHouseModels = { "Medieval House 003", "Medieval House 005 - Blacksmith", "Medieval House 009 - Tavern" };
    static readonly string[] CivilHousePrefabs = { "House_Medieval_003", "House_Medieval_005_Blacksmith", "House_Medieval_009_Tavern" };   // v poradí ako CivilHouseModels
    static readonly string[] RebelHouseModels = { "dom cyberpunk 1", "dom cyberpunk 2" };

    static readonly Color[] CyberTints =
    {
        new Color(0.35f, 0.85f, 0.9f), new Color(0.95f, 0.45f, 0.75f), new Color(0.95f, 0.7f, 0.3f), new Color(0.55f, 0.6f, 1f)
    };

    // Farba domov odboja: odtieň sa násobí s textúrou modelu (ak ju má), inak je to priama farba
    static void TintHouse(Renderer[] renderers, int index)
    {
        foreach (Renderer r in renderers)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Color tint = CyberTints[(index + i) % CyberTints.Length];   // každý materiál modelu iná farba
                if (mats[i] == null || !mats[i].HasProperty("_Color")) continue;
                if (mats[i].HasProperty("_MainTex") && mats[i].GetTexture("_MainTex") != null) continue;   // texturované materiály (Cyber_A/B) sa neprefarbujú, tint by prekryl detail
                mats[i] = new Material(mats[i]) { color = Color.Lerp(Color.white, tint, 0.75f) };
            }
            r.sharedMaterials = mats;
        }
    }

    // Hranice samotného domu bez drobných rekvizít (vedrá, tehly, čiastky pod zemou), ktoré by dom zdvihli nad zem alebo ho zmenšili
    static Bounds MainBounds(Renderer[] renderers)
    {
        Bounds all = renderers[0].bounds;
        foreach (Renderer r in renderers) all.Encapsulate(r.bounds);

        float minSize = 0.15f * Mathf.Max(all.size.x, all.size.z);
        bool any = false;
        Bounds main = new Bounds();
        foreach (Renderer r in renderers)
        {
            Vector3 s = r.bounds.size;
            if (Mathf.Max(s.x, s.y, s.z) < minSize) continue;
            if (!any) { main = r.bounds; any = true; }
            else main.Encapsulate(r.bounds);
        }
        return any ? main : all;
    }

    // Výška podlahy domu: najnižší renderer s veľkým pôdorysom (>= 15 % z najväčšieho). Odľahlé časti (zvislý panel, blok pod
    // schodmi, ktoré siahajú pod podlahu) sa nepočítajú, inak by sa dom posadil na ne a podlaha by levitovala (Blacksmith, Tavern).
    // Pôdorys sa meria v lokálnych súradniciach domu, takže nezávisí od jeho otočenia (zvislý panel otočený o 72° by inak vyzeral ako podlaha).
    static float FloorY(Transform root, Renderer[] renderers)
    {
        var areas = new float[renderers.Length];
        float maxArea = 0f;
        for (int i = 0; i < renderers.Length; i++)
        {
            // Rohy skutočného (otočeného) kvádra renderera, nie svetového AABB, ktorý by pri otočení nafúkol pôdorys
            Bounds lb = renderers[i].localBounds;
            Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue), max = new Vector3(float.MinValue, 0f, float.MinValue);
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = renderers[i].transform.TransformPoint(lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                Vector3 l = root.InverseTransformPoint(corner);
                min.x = Mathf.Min(min.x, l.x); max.x = Mathf.Max(max.x, l.x);
                min.z = Mathf.Min(min.z, l.z); max.z = Mathf.Max(max.z, l.z);
            }
            areas[i] = (max.x - min.x) * (max.z - min.z);
            maxArea = Mathf.Max(maxArea, areas[i]);
        }

        float floor = float.MaxValue;
        for (int i = 0; i < renderers.Length; i++)
            if (areas[i] >= 0.15f * maxArea) floor = Mathf.Min(floor, renderers[i].bounds.min.y);
        return floor == float.MaxValue ? renderers[0].bounds.min.y : floor;
    }

    // Zmaže celý objekt, ak je to len nosič kamery/svetla (bez vykresľovania), inak len komponent
    static void RemoveHelper(GameObject go, Component c)
    {
        if (go == null) return;
        bool carrier = go.GetComponent<Renderer>() == null && go.transform.childCount == 0;
        if (carrier) Object.DestroyImmediate(go);
        else Object.DestroyImmediate(c);
    }

    /// <summary>Postaví dom z modelu v Assets/Models/House: zmenší ho na šírku maxWidth, posadí na zem a pridá kolíziu. Bez modelu vráti false.</summary>
    static bool PlaceHouseModel(Transform parent, bool civil, int index, Vector3 world, Vector3 facing, float groundY, float maxWidth)
    {
        string[] names = civil ? CivilHouseModels : RebelHouseModels;

        // Domy majú hotový prefab (s kolíziou, pevnou mierkou a pivotom v podlahe) – nerozbaľuje sa, takže zmena prefabu sa prenesie do scény
        string prefabName = civil ? CivilHousePrefabs[index % names.Length] : "House_Cyberpunk_" + (index % names.Length + 1);
        GameObject housePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Houses/" + prefabName + ".prefab");
        if (housePrefab != null) return PlaceHousePrefab(parent, housePrefab, civil, index, world, facing, groundY);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/House/" + names[index % names.Length] + ".fbx");
        if (prefab == null) return false;

        GameObject house = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        house.name = "Dom_" + names[index % names.Length];
        house.transform.position = Vector3.zero;
        house.transform.rotation = Quaternion.Euler(0f, Quaternion.LookRotation(facing).eulerAngles.y + HouseModelYawOffset, 0f);
        house.transform.localScale = Vector3.one;

        // FBX nesie aj kamery a svetlá z modelovacieho programu; rozbaliť z prefabu (inak sa nedajú odstrániť) a zmazať
        PrefabUtility.UnpackPrefabInstance(house, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (Camera cam in house.GetComponentsInChildren<Camera>(true)) RemoveHelper(cam.gameObject, cam);
        foreach (Light lt in house.GetComponentsInChildren<Light>(true)) RemoveHelper(lt.gameObject, lt);
        foreach (AudioListener al in house.GetComponentsInChildren<AudioListener>(true)) Object.DestroyImmediate(al);

        Renderer[] renderers = house.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { Object.DestroyImmediate(house); return false; }
        Bounds b = MainBounds(renderers);

        float scale = maxWidth / Mathf.Max(b.size.x, b.size.z, 0.01f);
        house.transform.localScale = Vector3.one * scale;
        Physics.SyncTransforms();

        b = MainBounds(renderers);
        house.transform.position += new Vector3(world.x - b.center.x, groundY - FloorY(house.transform, renderers), world.z - b.center.z);

        if (!civil) TintHouse(renderers, index);

        foreach (MeshFilter mf in house.GetComponentsInChildren<MeshFilter>())
            if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        return true;
    }

    /// <summary>Postaví dom z prefabu: mierka je v prefabe, tu sa nastaví len otočenie, pozícia (podlaha na zem) a farba rebelských domov.</summary>
    static bool PlaceHousePrefab(Transform parent, GameObject housePrefab, bool civil, int index, Vector3 world, Vector3 facing, float groundY)
    {
        string[] names = civil ? CivilHouseModels : RebelHouseModels;
        GameObject house = (GameObject)PrefabUtility.InstantiatePrefab(housePrefab, parent);
        house.name = "Dom_" + names[index % names.Length];
        house.transform.position = Vector3.zero;
        house.transform.rotation = Quaternion.Euler(0f, Quaternion.LookRotation(facing).eulerAngles.y + HouseModelYawOffset, 0f);
        Physics.SyncTransforms();

        Renderer[] renderers = house.GetComponentsInChildren<Renderer>();
        Bounds b = MainBounds(renderers);
        house.transform.position += new Vector3(world.x - b.center.x, groundY - FloorY(house.transform, renderers), world.z - b.center.z);

        if (!civil) TintHouse(renderers, index);
        return true;
    }

    static GameObject FindTreeAsset(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FirFolder + name + ".prefab");
        if (prefab != null) return prefab;
        foreach (string filter in new[] { name + " t:Prefab", name + " t:Model" })
            foreach (string guid in AssetDatabase.FindAssets(filter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null) return asset;
            }
        return null;
    }

    /// <summary>Strom pri dome: striedavo Fir_01_Plant a Fir_03_Medium (Dynamic Nature). Ak prefab chýba, vráti false.</summary>
    static bool HouseTree(Transform parent, Vector3 world, float groundY)
    {
        string[] names = { "Fir_01_Plant", "Fir_03_Medium" };
        GameObject prefab = FindTreeAsset(names[houseTreeCounter % 2]);
        houseTreeCounter++;
        if (prefab == null) return false;

        GameObject tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        tree.name = "StromPriDome";
        tree.transform.position = new Vector3(world.x, groundY, world.z);
        tree.transform.rotation = Quaternion.Euler(0f, houseTreeCounter * 57f % 360f, 0f);
        tree.transform.localScale = Vector3.one * (0.9f + (houseTreeCounter % 4) * 0.08f);
        return true;
    }
}

public static partial class ProgenyTools
{
    [MenuItem("Progeny/Megakorporácia/Premiestniť na pevné miesto (východ mapy)", false, 321)]
    static void MoveCorpToFixedPlace()
    {
        GameObject ground = GameObject.Find("Zem_Megakorporacia");
        GameObject civilGate = GameObject.Find("Portal_DoMegakorporacie");
        if (ground == null || civilGate == null)
        {
            Debug.LogWarning("Megakorporácia alebo portál v meste neexistuje.");
            return;
        }

        // Pevné miesto: východ mapy, ďaleko od divočín (sever), Centra (západ) aj mesta; mesto sa prispôsobí neskôr
        Vector3 target = new Vector3(civilGate.transform.position.x + CorpOffset.x, ground.transform.position.y, civilGate.transform.position.z + CorpOffset.z);
        Vector3 delta = target - ground.transform.position;
        if (delta.sqrMagnitude < 1f) { Debug.Log("Megakorporácia už stojí na správnom mieste."); return; }

        foreach (string name in new[] { "Zem_Megakorporacia", "Portal_DoMesta", "CorpCity", "CorpComplex", "Obyvatelia_Megakorporacia" })
        {
            GameObject go = GameObject.Find(name);
            if (go == null) continue;
            Undo.RecordObject(go.transform, "Premiestnenie Megakorporácie");
            go.transform.position += delta;
            EditorUtility.SetDirty(go);
        }
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Megakorporácia presunutá o {delta} na východ mapy (pevné miesto, mimo divočín). Ulož scénu.");
    }
}

public static partial class ProgenyTools
{
    /// <summary>
    /// Opravuje prepadávanie v Megakorporácii: zem (Zem_Megakorporacia) chýba, je vypnutá, malá alebo stojí inde než komplex.
    /// Zem je samostatný objekt s kolíziou, takže ostáva aj pri vypnutej zóne.
    /// </summary>
    [MenuItem("Progeny/Megakorporácia/Opraviť zem (prepadávanie)", false, 322)]
    static void FixCorpGroundMenu()
    {
        Debug.Log(EnsureCorpGround() ? "Zem Megakorporácie opravená. Ulož scénu." : "Zem Megakorporácie je v poriadku (alebo komplex ešte nestojí).");
    }

    static bool EnsureCorpGround()
    {
        GameObject city = GameObject.Find("CorpCity") ?? GameObject.Find("CorpComplex");
        if (city == null) return false;

        const float size = 230f;
        Vector3 top = city.transform.position;   // koreň komplexu leží na vrchu zeme
        GameObject ground = GameObject.Find("Zem_Megakorporacia");
        bool changed = false;

        if (ground == null)
        {
            BuildZoneGround(null, "Zem_Megakorporacia", top, new Vector2(size, size), new Color(0.18f, 0.2f, 0.24f));
            changed = true;
        }
        else
        {
            Vector3 wanted = top + Vector3.down * 0.5f;
            if ((ground.transform.position - wanted).sqrMagnitude > 0.25f) { ground.transform.position = wanted; changed = true; }
            if (ground.transform.localScale.x < size || ground.transform.localScale.z < size || ground.transform.localScale.y < 1f)
            {
                ground.transform.localScale = new Vector3(Mathf.Max(size, ground.transform.localScale.x), 1f, Mathf.Max(size, ground.transform.localScale.z));
                changed = true;
            }
            if (!ground.activeSelf) { ground.SetActive(true); changed = true; }

            Collider col = ground.GetComponent<Collider>();
            if (col == null) { ground.AddComponent<BoxCollider>(); changed = true; }
            else if (!col.enabled) { col.enabled = true; changed = true; }
            Renderer r = ground.GetComponent<Renderer>();
            if (r != null && !r.enabled) { r.enabled = true; changed = true; }
        }

        if (changed)
        {
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
        return changed;
    }
}
