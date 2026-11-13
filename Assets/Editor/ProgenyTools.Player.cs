using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Hráč: model (Assets/Models/Player/Hrac.fbx), animačné sady (bez zbrane / s mečom), materiály, inventár a zbrane.
/// Sady a materiály sa vytvoria aj automaticky pri stlačení Play, ak ešte neexistujú.
/// </summary>
public static partial class ProgenyTools
{
    const string PlayerFolder = "Assets/Models/Player";
    const string SwordFolder = "Assets/Models/Player/Sword";

    // Zvýš, keď sa zmení spôsob skladania sád: staré sady sa pri ďalšom Play prestavajú
    const int PlayerSetVersion = 19;

    [InitializeOnLoadMethod]
    static void HookPlayerAssets()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            EnsurePlayerAssets(false);
            RefreshMaterialTextures(false);
        };
    }

    // Materiály démona a meča boli vytvorené bez textúry (glTFast textúru nepomenúva "Color"). Doplní sa z materiálu v GLB;
    // bez force len raz za spustenie editora, aby sa veľké GLB nenačítavali pri každom Play.
    static void RefreshMaterialTextures(bool force)
    {
        const string key = "POA.TextureRefresh";
        if (!force && SessionState.GetBool(key, false)) return;
        SessionState.SetBool(key, true);

        ApplyTextureFromModel(NPCPerception.DemonMaterialPath, NPCPerception.DemonModelPath, "démona");
        ApplyTextureFromModel(PlayerWeapons.SwordMaterialPath, PlayerWeapons.SwordModelPath, "meča");
    }

    static void ApplyTextureFromModel(string materialPath, string modelPath, string label)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (mat == null || mat.GetTexture("_MainTex") != null) return;

        Texture2D albedo = FindAlbedoInModel(modelPath);
        if (albedo == null) return;

        mat.SetTexture("_MainTex", albedo);
        mat.color = Color.white;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log($"Materiál {label}: doplnená textúra '{albedo.name}' z {modelPath}.");
    }

    // ---------- Menu ----------
    [MenuItem("Progeny/Hráč/Nastaviť hráča (model, zbrane, inventár) a uložiť scénu", false, 600)]
    static void SetupPlayerModel()
    {
        if (!EnsurePlayerAssets(true)) return;
        RefreshMaterialTextures(true);

        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        if (player == null)
        {
            Debug.LogError("Hráč (PlayerStats) sa v scéne nenašiel.");
            return;
        }

        GameObject go = player.gameObject;
        Inventory inventory = AddIfMissing<Inventory>(go);
        PlayerVisual visual = AddIfMissing<PlayerVisual>(go);
        AddIfMissing<PlayerAppearance>(go);
        PlayerWeapons weapons = AddIfMissing<PlayerWeapons>(go);

        Undo.RecordObject(visual, "Model hráča");
        visual.model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVisual.ModelPath);
        visual.animationSet = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(PlayerVisual.UnarmedSetPath);
        visual.material = AssetDatabase.LoadAssetAtPath<Material>(PlayerVisual.MaterialPath);
        EditorUtility.SetDirty(visual);

        Undo.RecordObject(weapons, "Zbrane hráča");
        weapons.swordModel = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerWeapons.SwordModelPath);
        weapons.swordMaterial = AssetDatabase.LoadAssetAtPath<Material>(PlayerWeapons.SwordMaterialPath);
        weapons.unarmedSet = visual.animationSet;
        weapons.swordSet = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(PlayerWeapons.SwordSetPath);
        EditorUtility.SetDirty(weapons);

        // Kolízia: kocka 1x1x1 -> kapsula (pivot ostáva v strede tela)
        if (go.GetComponent<CapsuleCollider>() == null)
        {
            BoxCollider box = go.GetComponent<BoxCollider>();
            if (box != null) Undo.DestroyObjectImmediate(box);

            CapsuleCollider capsule = Undo.AddComponent<CapsuleCollider>(go);
            capsule.direction = 1;
            capsule.radius = visual.capsuleRadius;
            capsule.height = visual.capsuleHeight;
            capsule.center = Vector3.zero;
        }

        // Provizórna kocka sa nevykresľuje (komponent ostáva, iné skripty ju tónujú)
        Renderer own = go.GetComponent<Renderer>();
        if (own != null)
        {
            Undo.RecordObject(own, "Skryť provizórnu postavu");
            own.enabled = false;
        }

        VerifyPlayerSetup();

        EditorSceneManager.MarkAllScenesDirty();
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("Hráč nastavený (model, zbrane, inventár). Scéna uložená. Inventár: I, vytasiť meč: Q, lektvar: B.");
    }

    [MenuItem("Progeny/Hráč/Položiť ukážkové predmety pred hráča", false, 602)]
    static void SpawnSampleItems()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        Vector3 center = player != null ? player.transform.position : Vector3.zero;
        Vector3 forward = player != null ? player.transform.forward : Vector3.forward;

        GameObject root = new GameObject("SampleItems");
        Undo.RegisterCreatedObjectUndo(root, "Sample items");

        string[] ids =
        {
            ItemCatalog.SteelSword, ItemCatalog.BlessedBlade, ItemCatalog.HealthPotion, ItemCatalog.StaminaPotion,
            ItemCatalog.BloodVial, ItemCatalog.ScentSerum, ItemCatalog.Herb, ItemCatalog.Scrap, ItemCatalog.SoulDetector, ItemCatalog.KeyCard,
            ItemCatalog.ToolId(ToolKit.Kind.Axe, 1), ItemCatalog.ToolId(ToolKit.Kind.Pickaxe, 1), ItemCatalog.ToolId(ToolKit.Kind.Hammer, 1)
        };

        for (int i = 0; i < ids.Length; i++)
        {
            float angle = (i - (ids.Length - 1) * 0.5f) * 0.4f;   // oblúk pred hráčom
            Vector3 dir = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.up) * forward;
            Vector3 position = center + dir * 5f;
            if (TryGetGroundY(position, out float groundY)) position.y = groundY;

            GameObject go = new GameObject("Pickup_" + ids[i]);
            Undo.RegisterCreatedObjectUndo(go, "Sample item");
            go.transform.SetParent(root.transform);
            go.transform.position = position;

            ItemPickup pickup = go.AddComponent<ItemPickup>();
            pickup.itemId = ids[i];
            pickup.count = ItemCatalog.Get(ids[i]).maxStack > 1 ? 2 : 1;
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;
        Debug.Log($"Položených {ids.Length} ukážkových predmetov. Pozbieraš ich priblížením.");
    }

    static T AddIfMissing<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    // ---------- Assety hráča ----------
    /// <summary>
    /// Pripraví assety hráča: import modelu, materiály a obe animačné sady. Bez force sa nerobí nič, ak už všetko existuje.
    /// Vráti false, ak chýba model.
    /// </summary>
    static bool EnsurePlayerAssets(bool force)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVisual.ModelPath) == null)
        {
            if (force) Debug.LogError($"Model hráča sa nenašiel ({PlayerVisual.ModelPath}).");
            return false;
        }

        DemonAnimationSet existingUnarmed = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(PlayerVisual.UnarmedSetPath);
        DemonAnimationSet existingSword = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(PlayerWeapons.SwordSetPath);
        Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>(PlayerVisual.MaterialPath);
        bool bodyOk = bodyMat != null && bodyMat.shader != null && bodyMat.shader.name == "Progeny/PlayerBody"
                      && bodyMat.GetTexture("_MainTex") != null;
        bool complete = existingUnarmed != null && existingUnarmed.builderVersion >= PlayerSetVersion
                        && existingSword != null && existingSword.builderVersion >= PlayerSetVersion
                        && bodyOk
                        && AssetDatabase.LoadAssetAtPath<Material>(PlayerWeapons.SwordMaterialPath) != null;
        if (complete && !force) return true;

        // Import modelu: bez kamier a svetiel, Generic avatar (Animator má čo animovať)
        DisableCameraLightImport(PlayerVisual.ModelPath);
        ModelImporter importer = AssetImporter.GetAtPath(PlayerVisual.ModelPath) as ModelImporter;
        if (importer != null && importer.animationType == ModelImporterAnimationType.Generic
            && importer.avatarSetup == ModelImporterAvatarSetup.NoAvatar)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }

        BuildPlayerBodyMaterial();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerWeapons.SwordModelPath) != null)
            BuildModelMaterial(PlayerWeapons.SwordModelPath, PlayerWeapons.SwordMaterialPath, null, new Color(0.7f, 0.74f, 0.82f), "meča");

        BuildPlayerAnimationSets();
        return true;
    }

    // ---------- Materiál hráča (shader Progeny/PlayerBody) ----------
    // Textúry hráča sú v Assets/Models/Player/Textures (vytiahnuté zo zdrojového GLB; UV sedia s Hrac.fbx).
    static Texture2D ConfigureTexture(string path, bool normalMap, bool srgb)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(path);
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
        }
        if (importer == null) return null;

        TextureImporterType type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        bool changed = false;
        if (importer.textureType != type) { importer.textureType = type; changed = true; }
        if (!normalMap && importer.sRGBTexture != srgb) { importer.sRGBTexture = srgb; changed = true; }   // ORM musí byť lineárna
        if (importer.maxTextureSize != 4096) { importer.maxTextureSize = 4096; changed = true; }
        if (changed) importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void BuildPlayerBodyMaterial()
    {
        const string dir = PlayerFolder + "/Textures";
        Texture2D color = ConfigureTexture(dir + "/Hrac_color.jpg", false, true);
        Texture2D orm = ConfigureTexture(dir + "/Hrac_orm.jpg", false, false);
        Texture2D normal = ConfigureTexture(dir + "/Hrac_normal.png", true, false);

        Shader shader = Shader.Find("Progeny/PlayerBody");
        EnsureFolder("Assets/Materials");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(PlayerVisual.MaterialPath);
        bool isNew = mat == null;
        if (isNew) mat = new Material(shader != null ? shader : Shader.Find("Standard"));
        else if (shader != null && mat.shader != shader) mat.shader = shader;

        mat.SetTexture("_MainTex", color);
        mat.SetTexture("_BumpMap", normal);
        if (shader != null)
        {
            mat.SetTexture("_OrmMap", orm);
            mat.SetColor("_SkinColor", new Color(0.86f, 0.69f, 0.57f));
            mat.SetColor("_ClothesColor", new Color(0.22f, 0.26f, 0.40f));
            mat.SetFloat("_Recolor", 1f);
        }
        else mat.color = Color.white;   // záloha: Standard shader s textúrou, bez prefarbenia

        if (isNew) AssetDatabase.CreateAsset(mat, PlayerVisual.MaterialPath);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Debug.Log($"Materiál hráča: shader = {(shader != null ? "Progeny/PlayerBody" : "Standard (PlayerBody sa ešte nenaimportoval)")}, " +
                  $"albedo = {(color != null ? color.name : "CHÝBA")}, ORM = {(orm != null ? orm.name : "CHÝBA")}, normála = {(normal != null ? normal.name : "CHÝBA")}.");
    }

    // Textúra z modelu (embedded Color) -> Standard materiál; bez textúry neutrálna farba.
    // extractDir == null: textúry sa nevyťahujú (glTFast ich už má ako podassety).
    static void BuildModelMaterial(string modelPath, string materialPath, string extractDir, Color fallback, string label)
    {
        Texture2D albedo = FindAlbedo(AssetDatabase.LoadAllAssetsAtPath(modelPath));

        if (albedo == null && extractDir != null)
        {
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer != null)
            {
                EnsureFolder(extractDir);
                importer.ExtractTextures(extractDir);
                AssetDatabase.Refresh();

                var found = new List<Object>();
                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { extractDir }))
                    found.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid)));
                albedo = FindAlbedo(found.ToArray());
            }
        }

        EnsureFolder("Assets/Materials");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        bool isNew = mat == null;
        if (isNew) mat = new Material(Shader.Find("Standard"));

        mat.SetTexture("_MainTex", albedo);
        mat.color = albedo != null ? Color.white : fallback;
        mat.SetFloat("_Metallic", 0.1f);
        mat.SetFloat("_Glossiness", 0.3f);

        if (isNew) AssetDatabase.CreateAsset(mat, materialPath);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Debug.Log($"Materiál {label}: albedo = {(albedo != null ? albedo.name : "NENÁJDENÉ (neutrálna farba)")}.");
    }

    // 1) textúra pomenovaná Color/Base/Albedo, 2) základná textúra z materiálu modelu (glTFast ich nepomenúva podľa obrázku,
    // ale materiál ich drží: baseColorTexture / _MainTex / _BaseMap)
    static Texture2D FindAlbedo(Object[] assets)
    {
        foreach (Object o in assets)
        {
            Texture2D t = o as Texture2D;
            if (t != null && NameHas(t.name, "color", "base", "albedo", "diffuse")) return t;
        }

        foreach (Object o in assets)
        {
            Material m = o as Material;
            if (m == null) continue;

            Texture2D fallback = null;
            foreach (string prop in m.GetTexturePropertyNames())
            {
                Texture2D tex = m.GetTexture(prop) as Texture2D;
                if (tex == null || NameHas(prop, "normal", "bump", "occlusion", "orm", "metal", "rough", "emission")) continue;
                if (NameHas(prop, "base", "main", "color", "albedo", "diffuse")) return tex;
                if (fallback == null) fallback = tex;
            }
            if (fallback != null) return fallback;
        }
        return null;
    }

    /// <summary>Základná textúra z podassetov modelu (vrátane materiálov); null, ak tam nie je.</summary>
    static Texture2D FindAlbedoInModel(string modelPath) => FindAlbedo(AssetDatabase.LoadAllAssetsAtPath(modelPath));

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    // ---------- Animačné sady hráča ----------
    // Najkratší názov súboru v priečinku, ktorý obsahuje všetky "must" a žiadne "none" (Mixamo: Meno@Akcia.fbx)
    static string FindClipFile(string folder, string[] must, string[] none = null, bool recursive = true)
    {
        string best = null;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!recursive && System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != folder) continue;

            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            bool ok = true;
            foreach (string m in must) if (!NameHas(file, m)) { ok = false; break; }
            if (!ok) continue;
            if (none != null)
            {
                bool excluded = false;
                foreach (string n in none) if (NameHas(file, n)) { excluded = true; break; }
                if (excluded) continue;
            }

            if (best == null || file.Length < System.IO.Path.GetFileNameWithoutExtension(best).Length) best = path;
        }
        return best;
    }

    // Všetky súbory s daným slovom v priečinku, zoradené podľa názvu (attack, attack (2), attack (3) ...)
    static List<string> FindClipFiles(string folder, string must)
    {
        var list = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (NameHas(System.IO.Path.GetFileNameWithoutExtension(path), must)) list.Add(path);
        }
        list.Sort((a, b) => string.CompareOrdinal(System.IO.Path.GetFileNameWithoutExtension(a), System.IO.Path.GetFileNameWithoutExtension(b)));
        return list;
    }

    static AnimationClip ClipFrom(string path, bool loop)
    {
        if (path == null) return null;
        if (loop) EnableLoop(path); else DisableLoop(path);
        return FirstClip(path);
    }

    static DemonAnimationSet LoadOrCreateSet(string path)
    {
        DemonAnimationSet set = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(path);
        if (set != null) return set;

        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        set = ScriptableObject.CreateInstance<DemonAnimationSet>();
        AssetDatabase.CreateAsset(set, path);
        return set;
    }

    static void BuildPlayerAnimationSets()
    {
        // --- Bez zbrane: klipy démonov (zdieľaný štýl) + kop z priečinka hráča ---
        DemonAnimationSet unarmed = LoadOrCreateSet(PlayerVisual.UnarmedSetPath);
        // Bez zbrane nesmú byť klipy z balíka s mečom: pravá ruka by zostala v póze držania meča. Samostatný "Start Walking" sa
        // nepoužíva (bol príliš dlhý a pri prechode do chôdze nadskakoval).
        unarmed.idle = ClipFrom(FindClipFile("Assets/Models", new[] { "Demon@", "idle" }), true);
        unarmed.run = ClipFrom(FindClipFile("Assets/Models", new[] { "Demon@", "walk" }, new[] { "start" }), true);
        unarmed.startWalk = null;
        unarmed.attack = ClipFrom(FindClipFile("Assets/Models", new[] { "Demon@", "punching" }), false);
        unarmed.attackElbow = ClipFrom(FindClipFile("Assets/Models", new[] { "Demon@", "elbow" }), false);
        unarmed.deathForward = ClipFrom(FindClipFile("Assets/Models", new[] { "Demon@", "death", "forward" }), false);
        unarmed.deathBack = ClipFrom(FindClipFile("Assets/Models", new[] { "Demon@", "death", "back" }), false);
        string kick = FindClipFile(PlayerFolder, new[] { "chapa" }, null, false) ?? FindClipFile(PlayerFolder, new[] { "kick" }, null, false);
        unarmed.attackKick = ClipFrom(kick, false);

        // Čupnutie (ak sú v priečinku hráča klipy Crouch Idle / Crouch Walk); inak sa postava pri čupnutí nakloní
        const string NewPlayerFolder = PlayerFolder + "/Hrac1.1";   // klipy z balíka nového modelu majú prednosť pred starými z Maximo
        AnimationClip crouchIdle = ClipFrom(FindClipFile(NewPlayerFolder, new[] { "crouch", "idle" }, new[] { "torch" }, false), true)
                                   ?? ClipFrom(FindClipFile(PlayerFolder, new[] { "crouch", "idle" }, new[] { "torch" }, false), true);   // klip s fakľou drží ruku natiahnutú dopredu
        AnimationClip crouchWalk = ClipFrom(FindClipFile(NewPlayerFolder, new[] { "crouch" }, new[] { "idle" }, false), true)
                                   ?? ClipFrom(FindClipFile(PlayerFolder, new[] { "crouch", "walk" }, null, false), true);
        if (crouchIdle == null) crouchIdle = crouchWalk;   // bez čistého Crouch Idle: zmrazený záber z chôdze v čupe
        unarmed.crouchIdle = crouchIdle;
        unarmed.crouchWalk = crouchWalk;

        // Skok: len hráč (NPC a démoni majú vlastné sady bez klipu skoku)
        AnimationClip jump = ClipFrom(FindClipFile(PlayerFolder, new[] { "jump" }, null, false), false);
        unarmed.jump = jump;
        // Šprint (Shift): rýchly beh z priečinka hráča (player@Fast Run.fbx); s mečom ostáva beh meča
        unarmed.sprint = ClipFrom(FindClipFile(PlayerFolder, new[] { "fast", "run" }, null, false), true);
        // Vlastný Idle hráča (Models/Player/Idle.fbx) má prednosť pred Idle démona
        AnimationClip playerIdle = ClipFrom(FindClipFile(PlayerFolder, new[] { "idle" }, new[] { "crouch", "_old" }, false), true);
        if (playerIdle != null) unarmed.idle = playerIdle;
        // Vlastná chôdza/beh hráča (Models/Player/Walking.fbx alebo Running.fbx) má prednosť pred Walking démona; šprint (fast run) a čupnutie sa preskakujú
        AnimationClip playerWalk = ClipFrom(FindClipFile(PlayerFolder, new[] { "run" }, new[] { "fast", "_old", "sprint" }, false), true)   // rýchlosť hráča je 5 m/s, preto Running; Walking len ako záloha
                                   ?? ClipFrom(FindClipFile(PlayerFolder, new[] { "walk" }, new[] { "crouch", "_old", "start" }, false), true);
        if (playerWalk != null) unarmed.run = playerWalk;
        AnimationClip standUp = ClipFrom(FindClipFile(PlayerFolder, new[] { "standing", "up" }, null, false), false);
        unarmed.standUp = standUp;
        AnimationClip climb = ClipFrom(FindClipFile(PlayerFolder, new[] { "climbing" }, null, false), false);
        AnimationClip coverTurn = ClipFrom(FindClipFile(PlayerFolder, new[] { "cover" }, null, false), false);
        AnimationClip land = ClipFrom(FindClipFile(PlayerFolder, new[] { "jumping", "down" }, null, false), false);
        unarmed.land = land;
        unarmed.climb = climb;
        unarmed.coverTurn = coverTurn;
        unarmed.builderVersion = PlayerSetVersion;
        EditorUtility.SetDirty(unarmed);

        // --- S mečom: balík Sword and Shield. Tri útoky meča = tri varianty útoku (0, 1, 2). ---
        DemonAnimationSet sword = LoadOrCreateSet(PlayerWeapons.SwordSetPath);
        sword.idle = ClipFrom(FindClipFile(SwordFolder, new[] { "idle" }, new[] { "block" }), true);
        sword.run = ClipFrom(FindClipFile(SwordFolder, new[] { "run" }), true);

        List<string> attacks = FindClipFiles(SwordFolder, "attack");
        // Dvojručné "Great Sword" klipy sú len ťažké útoky (pridajú sa nižšie), do pôvodných útokov nepatria
        attacks.RemoveAll(p => NameHas(System.IO.Path.GetFileNameWithoutExtension(p), "great"));
        sword.attack = ClipFrom(attacks.Count > 0 ? attacks[0] : null, false);
        sword.attackElbow = ClipFrom(attacks.Count > 1 ? attacks[1] : null, false);
        sword.attackKick = ClipFrom(attacks.Count > 2 ? attacks[2] : null, false);

        // Ľahké a ťažké útoky: všetky klipy útoku sa zoradia podľa dĺžky, kratšia polovica je ľahká, dlhšia ťažká (najviac 4 + 4, k ťažkým pribudnú Great Sword klipy)
        var attackClips = new List<AnimationClip>();
        foreach (string path in attacks)
        {
            AnimationClip c = ClipFrom(path, false);
            if (c != null) attackClips.Add(c);
        }
        attackClips.Sort((a, b) => a.length.CompareTo(b.length));

        int lightCount = Mathf.Min(4, (attackClips.Count + 1) / 2);
        int heavyCount = Mathf.Min(4, attackClips.Count - lightCount);

        // Ľahké útoky sú pôvodné tri útoky z čias pred rozdelením (prvé tri podľa názvu súboru); ťažké ostávajú najdlhšie klipy
        var lights = new List<AnimationClip>();
        foreach (AnimationClip original in new[] { sword.attack, sword.attackElbow, sword.attackKick })
            if (original != null) lights.Add(original);
        // Nové klipy: "Stabbing" = ľahký útok (bodnutie), "Great Sword ..." = ťažké útoky
        AnimationClip stab = ClipFrom(FindClipFile(SwordFolder, new[] { "stab" }), false);
        if (stab != null) lights.Add(stab);
        sword.lightAttacks = lights.ToArray();

        var heavies = attackClips.GetRange(attackClips.Count - heavyCount, heavyCount);
        foreach (string greatPath in FindClipFiles(SwordFolder, "great"))
        {
            AnimationClip great = ClipFrom(greatPath, false);
            if (great != null) heavies.Add(great);
        }
        sword.heavyAttacks = heavies.ToArray();
        Debug.Log($"Útoky s mečom: ľahké = {string.Join(", ", System.Array.ConvertAll(sword.lightAttacks, c => $"{c.name} {c.length:0.0}s"))}; " +
                  $"ťažké = {string.Join(", ", System.Array.ConvertAll(sword.heavyAttacks, c => $"{c.name} {c.length:0.0}s"))}.");

        string blockIdle = FindClipFile(SwordFolder, new[] { "block", "idle" });
        sword.block = ClipFrom(blockIdle, true);
        sword.drawWeapon = ClipFrom(FindClipFile(SwordFolder, new[] { "draw" }), false);
        sword.sheathWeapon = ClipFrom(FindClipFile(SwordFolder, new[] { "sheath" }), false);
        AnimationClip death = ClipFrom(FindClipFile(SwordFolder, new[] { "death" }), false);
        // Priečinok s mečom nemusí mať vlastný klip smrti: použije sa pád z klipov démonov (rovnako ako bez zbrane)
        sword.deathForward = death != null ? death : ClipFrom(FindClipFile("Assets/Models", new[] { "Demon@", "death", "forward" }), false);
        sword.deathBack = death != null ? death : ClipFrom(FindClipFile("Assets/Models", new[] { "Demon@", "death", "back" }), false);
        sword.startWalk = null;
        sword.crouchIdle = crouchIdle;
        sword.crouchWalk = crouchWalk;
        sword.jump = jump;
        sword.standUp = standUp;
        sword.land = land;
        sword.climb = climb;
        sword.coverTurn = coverTurn;
        sword.builderVersion = PlayerSetVersion;
        EditorUtility.SetDirty(sword);

        AssetDatabase.SaveAssets();
        Debug.Log($"Sada hráča bez zbrane: idle={Name(unarmed.idle)}, run={Name(unarmed.run)}, úder={Name(unarmed.attack)}, elbow={Name(unarmed.attackElbow)}, " +
                  $"kop={Name(unarmed.attackKick)}, smrť={Name(unarmed.deathForward)}/{Name(unarmed.deathBack)}.");
        Debug.Log($"Sada hráča s mečom: idle={Name(sword.idle)}, run={Name(sword.run)}, útoky={Name(sword.attack)}/{Name(sword.attackElbow)}/{Name(sword.attackKick)}, " +
                  $"blok={Name(sword.block)}, vytasiť={Name(sword.drawWeapon)}, zasunúť={Name(sword.sheathWeapon)}, smrť={Name(sword.deathForward)}.");
    }

    static string Name(AnimationClip c) => c != null ? c.name : "-";

    // ---------- Kontrola ----------
    static void VerifyPlayerSetup()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVisual.ModelPath);
        bool rigged = false;
        int bones = 0;
        foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.rootBone != null) { rigged = true; bones = smr.bones.Length; break; }

        if (!rigged)
        {
            Debug.LogWarning($"'{PlayerVisual.ModelPath}' nemá kostru: animácie sa neprehrajú, hráč sa bude len procedurálne hýbať.");
            return;
        }

        Debug.Log($"Kontrola modelu hráča: skinned mesh, {bones} kostí.");
        foreach (string path in new[] { PlayerVisual.UnarmedSetPath, PlayerWeapons.SwordSetPath })
        {
            DemonAnimationSet set = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(path);
            if (set == null) continue;

            string tag = path == PlayerVisual.UnarmedSetPath ? "bez zbrane" : "s mečom";
            CheckPlayerClip(tag + ": idle", set.idle, model);
            CheckPlayerClip(tag + ": run", set.run, model);
            CheckPlayerClip(tag + ": úder", set.attack, model);
            CheckPlayerClip(tag + ": elbow/2", set.attackElbow, model);
            CheckPlayerClip(tag + ": kop/3", set.attackKick, model);
            CheckPlayerClip(tag + ": smrť", set.deathForward, model);
            if (set.block != null) CheckPlayerClip(tag + ": blok", set.block, model);
            if (set.drawWeapon != null) CheckPlayerClip(tag + ": vytasiť", set.drawWeapon, model);
            if (set.sheathWeapon != null) CheckPlayerClip(tag + ": zasunúť", set.sheathWeapon, model);
        }
    }

    // Klipy démonov majú aj prsty, ktoré hráč nemá, preto stačí ~70 % ciest; chýbajúce krivky sa pri prehrávaní ignorujú
    static void CheckPlayerClip(string label, AnimationClip clip, GameObject model)
    {
        if (clip == null) { Debug.LogWarning($"Kontrola hráča ({label}): klip chýba."); return; }

        int total = 0, found = 0;
        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
        {
            if (string.IsNullOrEmpty(b.path)) continue;
            total++;
            if (model.transform.Find(b.path) != null) found++;
        }

        float ratio = total > 0 ? found / (float)total : 0f;
        string msg = $"Kontrola hráča ({label}): '{clip.name}' {clip.length:0.00} s, {(clip.isLooping ? "cyklí" : "necykli")}, kosti sedia {found}/{total}.";
        if (ratio < 0.7f) Debug.LogWarning(msg);
        else Debug.Log(msg);
    }
}
