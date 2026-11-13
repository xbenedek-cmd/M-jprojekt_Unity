using UnityEditor;
using UnityEngine;

/// <summary>
/// Boss Klanu Blesku (Lord Raizen): model boss_pre_mixamo.fbx už má Mixamo kostru (mixamorig:*), takže hrá rovnaké
/// Generic klipy ako ostatní démoni. Menej detailný Novy_Demon_blesku2.fbx je model bežných démonov Klanu Blesku (nie boss).
/// </summary>
public static partial class ProgenyTools
{
    const string BossModelPath = "Assets/Models/boss_pre_mixamo.fbx";
    const string NewDemonModelPath = "Assets/Models/Novy_Demon_blesku2.fbx";
    const string BossClipFolder = "Assets/Models/Boss";
    const string BossSetPath = "Assets/Animations/BossRaizenSet.asset";

    static DemonEntry FindBossEntry(DemonRoster roster)
    {
        foreach (DemonEntry e in roster.entries)
            if (e.tier == DemonTier.Boss && e.clan == FactionId.LightningClan) return e;
        return null;
    }

    [MenuItem("Progeny/Démoni/Pripraviť bossa Raizena (model + animácie)", false, 500)]
    static void PrepareRaizenBossMenu()
    {
        if (PrepareRaizenBoss(true))
            Debug.Log("Boss Raizen je pripravený. Spusti Progeny → Démoni → Vytvoriť 10 rôznych démonov, boss sa objaví medzi nimi. Ulož scénu.");
    }

    /// <summary>Nastaví rig modelu bossa, vytvorí jeho sadu animácií a priradí oboje záznamu v zozname démonov.</summary>
    static bool PrepareRaizenBoss(bool force)
    {
        PrepareLightningDemonModel(force);

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(BossModelPath);
        if (model == null)
        {
            if (force) Debug.LogError($"Model bossa sa nenašiel ({BossModelPath}).");
            return false;
        }

        // Rig vždy: klipy sú Generic, Humanoid avatar na modeli by ich na kostry nepustil (boss by stál v bind póze)
        if (!PrepareMixamoModel(BossModelPath)) return false;

        DemonRoster roster = LoadOrCreateRoster(false);
        DemonEntry entry = FindBossEntry(roster);
        if (entry == null) return false;

        DemonAnimationSet existing = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(BossSetPath);
        if (!force && existing != null && existing.idle != null && entry.model == model && entry.animationSet == existing && entry.material != null) return true;

        // 1. Model má Mixamo kostru: Generic rig, rovnaké klipy ako ostatní démoni
        if (!PrepareMixamoModel(BossModelPath)) return false;

        // 2. Sada animácií bossa = kópia spoločnej sady démonov (vlastná, aby sa dala neskôr upraviť samostatne)
        DemonAnimationSet shared = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(NPCPerception.DemonAnimationSetPath);
        if (shared == null || shared.idle == null || shared.run == null)
        {
            Debug.LogError($"Boss: chýba spoločná sada démonov ({NPCPerception.DemonAnimationSetPath}). Spusti menu Progeny → Animácie démona → Vytvoriť sadu z Mixamo klipov.");
            return false;
        }

        DemonAnimationSet set = LoadOrCreateSet(BossSetPath);
        string setName = set.name;
        EditorUtility.CopySerialized(shared, set);
        set.name = setName;   // CopySerialized prepíše aj názov: bez toho Unity hlási "Main Object Name ... does not match filename"
        EditorUtility.SetDirty(set);

        // 3. Záznam v zozname démonov
        entry.model = AssetDatabase.LoadAssetAtPath<GameObject>(BossModelPath);
        entry.animationSet = set;
        entry.material = BuildBossMaterial();
        entry.keepModelMaterials = true;
        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        return true;
    }

    // Len boss Klanu Blesku má vlastný model (boss_pre_mixamo). Všetci ostatní démoni, aj bežní Klanu Blesku, používajú spoločný model démona:
    // záznamy v zozname, ktoré mali Novy_Demon_blesku2 a jeho materiál, sa vrátia na spoločný model, sadu a materiál.
    static void PrepareLightningDemonModel(bool force)
    {
        GameObject oldModel = AssetDatabase.LoadAssetAtPath<GameObject>(NewDemonModelPath);
        DemonRoster roster = LoadOrCreateRoster(false);
        bool changed = false;

        foreach (DemonEntry e in roster.entries)
        {
            if (e.tier == DemonTier.Boss || oldModel == null || e.model != oldModel) continue;

            e.model = null;
            e.keepModelMaterials = false;
            e.material = null;
            changed = true;
        }

        if (!changed) return;
        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
    }

    // Dočasný materiál bežných démonov Klanu Blesku (kým nepríde textúra): tmavomodrý kov so slabým bleskovým žiarením
    static Material BuildLightningDemonMaterial()
    {
        const string matPath = "Assets/Materials/NovyDemonMaterial.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        bool isNew = mat == null;
        if (isNew)
        {
            EnsureFolder("Assets/Materials");
            mat = new Material(Shader.Find("Standard"));
        }

        // Zvlnený kov (balík corrugated_metal) podfarbený modrou Klanu Blesku; bez balíka len modrý kov
        Material metal = LoadSurface("corrugated_metal");
        mat.SetTexture("_MainTex", metal != null ? metal.GetTexture("_MainTex") : null);
        mat.SetTexture("_BumpMap", metal != null ? metal.GetTexture("_BumpMap") : null);
        if (metal != null) mat.EnableKeyword("_NORMALMAP");
        mat.mainTextureScale = new Vector2(2f, 2f);
        mat.color = new Color(0.45f, 0.65f, 1f);
        mat.SetFloat("_Metallic", 0.55f);
        mat.SetFloat("_Glossiness", 0.5f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.04f, 0.15f, 0.4f));

        if (isNew) AssetDatabase.CreateAsset(mat, matPath);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>Model s Mixamo kostrou: Generic rig s vlastným avatarom (rovnako ako Demon_rigged), bez kamery, svetla a kolízie z Blendera.</summary>
    static bool PrepareMixamoModel(string path)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return false;

        if (importer.animationType != ModelImporterAnimationType.Generic
            || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
            || importer.importCameras || importer.importLights || importer.addCollider)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.SaveAndReimport();

            ModelImporter check = AssetImporter.GetAtPath(path) as ModelImporter;
            if (check != null && check.animationType != ModelImporterAnimationType.Generic)
                Debug.LogWarning($"Rig {path} sa nepodarilo prepnúť na Generic. Nastav ručne: Inspector → Rig → Animation Type = Generic, Avatar Definition = Create From This Model → Apply.");
        }
        return true;
    }

    // Textúry sú v FBX len vložené (Tripo: Color / NormalGL) a Unity ich nenamapuje na materiál, preto sa vyextrahujú
    // a poskladá sa z nich vlastný Standard materiál bossa
    static Material BuildBossMaterial()
    {
        const string dir = BossClipFolder + "/Textures";
        const string matPath = "Assets/Materials/BossRaizenMaterial.mat";

        EnsureFolder(dir);
        ModelImporter importer = AssetImporter.GetAtPath(BossModelPath) as ModelImporter;

        Texture2D albedo = FindTextureIn(dir, "color", "base", "albedo", "diffuse");
        if (albedo == null && importer != null)
        {
            importer.ExtractTextures(dir);
            AssetDatabase.Refresh();
            AddMissingTextureExtensions(dir);   // FBX vkladá textúry bez prípony, Unity ich potom neimportuje
            AssetDatabase.Refresh();
            albedo = FindTextureIn(dir, "color", "base", "albedo", "diffuse");
        }

        Texture2D normal = FindTextureIn(dir, "normal");
        if (normal != null)
        {
            string normalPath = AssetDatabase.GetAssetPath(normal);
            TextureImporter ti = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (ti != null && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            }
        }

        EnsureFolder("Assets/Materials");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        bool isNew = mat == null;
        if (isNew) mat = new Material(Shader.Find("Standard"));

        // Pôvodná textúra je veľmi tmavá a pri lesku vyzerá sivo: spraví sa z nej chladná, svetlejšia verzia + žiara z modrých bleskových miest
        Texture2D tinted = null, glow = null;
        if (albedo != null) BuildBossLightningTextures(albedo, dir, out tinted, out glow);

        mat.SetTexture("_MainTex", tinted != null ? tinted : albedo);
        mat.color = albedo != null ? Color.white : new Color(0.25f, 0.45f, 0.9f);
        mat.SetFloat("_Metallic", 0.05f);
        mat.SetFloat("_Glossiness", 0.15f);
        if (glow != null)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", glow);
            mat.SetColor("_EmissionColor", Color.white * 2.2f);
        }
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }

        if (isNew) AssetDatabase.CreateAsset(mat, matPath);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Debug.Log($"Materiál bossa: albedo = {(albedo != null ? albedo.name : "NENÁJDENÉ (použitá modrá farba)")}, normálová mapa = {(normal != null ? normal.name : "nie")}.");
        return mat;
    }

    // Z textúry bossa: svetlejšia chladná verzia (modrasté tiene) a maska žiary z modrých pixelov (blesky, oči, zdobenie)
    static void BuildBossLightningTextures(Texture2D source, string dir, out Texture2D tinted, out Texture2D glow)
    {
        string tintedPath = dir + "/Raizen_Tinted.jpg";
        string glowPath = dir + "/Raizen_Glow.png";
        tinted = AssetDatabase.LoadAssetAtPath<Texture2D>(tintedPath);
        glow = AssetDatabase.LoadAssetAtPath<Texture2D>(glowPath);
        if (tinted != null && glow != null) return;

        // Čítať zo súboru (originálna textúra nemusí byť čitateľná)
        string sourceFile = System.IO.Path.GetFullPath(AssetDatabase.GetAssetPath(source));
        var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!image.LoadImage(System.IO.File.ReadAllBytes(sourceFile))) { Object.DestroyImmediate(image); return; }

        Color32[] pixels = image.GetPixels32();
        var glowPixels = new Color32[pixels.Length];

        for (int i = 0; i < pixels.Length; i++)
        {
            float r = pixels[i].r / 255f, g = pixels[i].g / 255f, b = pixels[i].b / 255f;
            float max = Mathf.Max(r, Mathf.Max(g, b));

            // Žiara: výrazne modré pixely
            bool electric = b > 0.35f && b > 1.35f * Mathf.Max(r, g * 0.9f);
            glowPixels[i] = electric ? new Color32(pixels[i].r, pixels[i].g, pixels[i].b, 255) : new Color32(0, 0, 0, 255);

            // Svetlejšie a chladnejšie: gamma nahor, tmavé miesta posunuté do modra, hnedá koža (oranžová) skôr do oceľovej
            float lift = Mathf.Pow(max, 0.7f) / Mathf.Max(0.001f, max);
            float nr = r * lift * 0.85f, ng = g * lift * 0.95f, nb = b * lift * 1.1f + 0.03f;
            float dark = 1f - Mathf.Clamp01(max * 3f);
            nb += 0.05f * dark;
            pixels[i] = new Color32((byte)(Mathf.Clamp01(nr) * 255f), (byte)(Mathf.Clamp01(ng) * 255f), (byte)(Mathf.Clamp01(nb) * 255f), 255);
        }

        var tintedImage = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);
        tintedImage.SetPixels32(pixels);
        var glowImage = new Texture2D(image.width, image.height, TextureFormat.RGBA32, false);
        glowImage.SetPixels32(glowPixels);

        System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(tintedPath), tintedImage.EncodeToJPG(90));
        System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(glowPath), glowImage.EncodeToPNG());
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(tintedImage);
        Object.DestroyImmediate(glowImage);
        AssetDatabase.Refresh();

        tinted = ConfigureTexture(tintedPath, false, true);
        glow = ConfigureTexture(glowPath, false, true);
    }

    // Súbory bez prípony v priečinku dostanú .png / .jpg podľa obsahu
    static void AddMissingTextureExtensions(string assetDir)
    {
        string full = System.IO.Path.GetFullPath(assetDir);
        foreach (string file in System.IO.Directory.GetFiles(full))
        {
            if (System.IO.Path.HasExtension(file)) continue;

            byte[] head = new byte[4];
            using (System.IO.FileStream s = System.IO.File.OpenRead(file)) s.Read(head, 0, 4);
            string ext = head[0] == 0x89 && head[1] == 0x50 ? ".png" : head[0] == 0xFF && head[1] == 0xD8 ? ".jpg" : null;
            if (ext == null) continue;

            System.IO.File.Move(file, file + ext);
            if (System.IO.File.Exists(file + ".meta")) System.IO.File.Delete(file + ".meta");
        }
        AssetDatabase.Refresh();
    }

    static Texture2D FindTextureIn(string dir, params string[] keys)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (NameHas(System.IO.Path.GetFileNameWithoutExtension(path), keys)) return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        return null;
    }
}
