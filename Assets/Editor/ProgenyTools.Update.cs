using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Jedno menu, ktoré obnoví všetky assety a nastavenia (animačné sady, materiály, modely a materiály démonov, boss, ruiny parciel)
/// bez toho, aby vytváralo alebo mazalo postavy v scéne (obnoví aj Divočiny: terén s kopcami, cestami a organickými jazerami s vodným shaderom, 3D trávu, stromy s pňami, mŕtve stromy, byliny, kryštály z modelu, železný odpad, kamene, zvuky krokov). Aj domy z modelov (zmaže vložené kamery a svetlá, zmenší textúry) a uloží obrázky zón pre mapu (M). Používa sa po každej zmene klipov, modelov či materiálov.
/// </summary>
public static partial class ProgenyTools
{
    [MenuItem("Progeny/★ Aktualizovať všetko (bez nových NPC)", false, 1)]
    static void UpdateEverything()
    {
        EnsureSurfaceMaterials(true);                     // PBR povrchy (cesty, kov)
        bool player = EnsurePlayerAssets(true);           // sady hráča, materiály hráča a meča
        BuildDemonAnimationSet();                         // spoločná sada démonov (+ priradí existujúcim v scéne)
        bool boss = PrepareRaizenBoss(true);              // boss + model a materiál bežných démonov Blesku
        int demons = ApplyRosterToSceneDemons();          // model / sada / materiál z DemonRoster na démonov, čo už v scéne sú

        if (Object.FindAnyObjectByType<BuildPlot>() != null) EnsurePlotManager();   // modely ruín parciel

        // Divočiny: tráva na zemi, potom kopce, stromy (smreky + sekera), skaly / bloky, zdroje, kryštály, ruiny, voda, tábory, modely nástrojov
        ApplyWildGrassMenu();
        UpgradeExistingWilds();

        // Terén divočín (kopce, cesty, jazerá, 3D tráva, objekty v hraniciach mapy) a zvuky krokov z balíka
        int terrains = 0;
        foreach (WildTheme theme in WildThemes)
            if (SafeBuildWildTerrain(theme)) terrains++;
        ProgenyFootsteps.Build(false);

        // Kamene, stromy, bloky a zdroje: kolízia a bod ťažby/zberu pri modeli (aj po ručnom posune), bez zapadnutia pod terén
        RockGuard.Scan();

        // Vchod do bane (len ak chýba) a kontrola mesta, portálov a zón
        int oldSigns = RemoveOldSigns();
        bool corpGround = EnsureCorpGround();
        bool mineEntrance = EnsureMineEntrance();
        string hints = WorldHints() + StoryAssetHints();

        EnsurePlayerComponents(Object.FindAnyObjectByType<PlayerStats>());

        // Portály (kamenné oblúky s lampami), nádvorie Centra, lampy pozdĺž ulíc a dokončený vchod do bane
        RefreshPortalLooks();
        EnsureCityGates();   // nádvorie Centra, brány Hradby a chýbajúce brány (aj Divočina v Centre)
        UpgradeAvenueLamps();
        Transform mineRoot = HbFind("Vchod_do_bane");
        if (mineRoot != null) FinishMineEntrance(mineRoot, MakeWildMatsForMine());

        // Domy z modelov: zmazať vložené kamery a svetlá a zmenšiť textúry (FBX ich nesie vložené)
        int houseHelpers = CleanHouseHelpers();
        bool houseTextures = TryShrinkHouseTextures();

        // Obrázky zón pre mapu (M): až na konci, aby zachytili aktuálny stav sveta
        bool zoneMaps = TryBakeZoneMaps();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Aktualizované: assety hráča {(player ? "OK" : "chýba model")}, boss {(boss ? "OK" : "chýba")}, obnovených démonov v scéne: {demons}, terén divočín: {terrains} zón, zvuky krokov obnovené, kamene a zdroje skontrolované (RockGuard). " +
                  $"staré nápisy odstránené: {oldSigns}, zem Megakorporácie {(corpGround ? "opravená" : "OK")}, " + $"vchod do bane {(mineEntrance ? "doplnený" : "OK/nie je zóna Bane")}. " + hints +
                  $" Domy: odstránených kamier a svetiel {houseHelpers}, textúry {(houseTextures ? "zmenšené" : "bez zmeny alebo chyba")}; obrázky mapy {(zoneMaps ? "uložené" : "chyba")}. Nové systémy (úvodná animácia, dialógové okno, kamión v prológu, cestovanie koreňmi, útok za pohybu, strom schopností, trasy NPC, nosenie tiel, značka cieľa úlohy) sú v kóde a v scéne netreba nič dopĺňať; portály, lampy, vchod do bane a cesty v divočinách sa aktualizovali. Žiadne NPC sa nepridali ani nezmazali. Ulož scénu.");
    }

    // Domy v scéne (objekty "Dom_...") môžu mať z FBX kamery a svetlá; prefab sa rozbalí, inak sa komponenty nedajú zmazať
    static int CleanHouseHelpers()
    {
        int removed = 0;
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null || !t.name.StartsWith("Dom_") || EditorUtility.IsPersistent(t)) continue;

            Camera[] cameras = t.GetComponentsInChildren<Camera>(true);
            Light[] lights = t.GetComponentsInChildren<Light>(true);
            AudioListener[] listeners = t.GetComponentsInChildren<AudioListener>(true);
            if (cameras.Length + lights.Length + listeners.Length == 0) continue;

            if (PrefabUtility.IsPartOfPrefabInstance(t.gameObject))
            {
                GameObject outer = PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject);
                if (outer != null) PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            foreach (AudioListener a in listeners) if (a != null) { Object.DestroyImmediate(a); removed++; }
            foreach (Camera c in cameras) if (c != null) { RemoveHelper(c.gameObject, c); removed++; }
            foreach (Light l in lights) if (l != null) { RemoveHelper(l.gameObject, l); removed++; }
        }
        return removed;
    }

    static bool TryShrinkHouseTextures()
    {
        if (!AssetDatabase.IsValidFolder(HouseFolder)) return false;
        try { ShrinkHouseTextures(); return true; }
        catch (System.Exception e) { Debug.LogWarning("Domy: zmenšenie textúr zlyhalo: " + e.Message); return false; }
    }

    static bool TryBakeZoneMaps()
    {
        try { BakeZoneMaps(); return true; }
        catch (System.Exception e) { Debug.LogWarning("Mapy zón: uloženie obrázkov zlyhalo: " + e.Message); return false; }
    }

    // Démon v scéne sa spáruje so záznamom zoznamu podľa mena (Damageable.displayName = DemonEntry.displayName)
    static int ApplyRosterToSceneDemons()
    {
        DemonRoster roster = LoadOrCreateRoster(false);
        int count = 0;

        foreach (NPCPerception npc in Object.FindObjectsByType<NPCPerception>(FindObjectsInactive.Include))
        {
            Damageable damage = npc.GetComponent<Damageable>();
            if (damage == null || npc.GetComponent<DemonEnemy>() == null) continue;

            DemonEntry entry = null;
            foreach (DemonEntry e in roster.entries)
                if (e.displayName == damage.displayName) { entry = e; break; }
            if (entry == null) continue;

            Undo.RecordObject(npc, "Obnoviť démona");
            // Vlastný model/sada/materiál zo záznamu (boss), inak spoločný model, sada a materiál démonov
            npc.higherDemonBodyOverride = entry.model != null ? entry.model : NPCPerception.LoadDemonModelAsset();
            npc.demonAnimationSet = entry.animationSet != null ? entry.animationSet
                : AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(NPCPerception.DemonAnimationSetPath);
            npc.keepModelMaterials = entry.keepModelMaterials;
            npc.demonMaterial = entry.keepModelMaterials || entry.material != null ? entry.material
                : AssetDatabase.LoadAssetAtPath<Material>(NPCPerception.DemonMaterialPath);
            EditorUtility.SetDirty(npc);
            count++;
        }
        return count;
    }

    // Vchod do bane sa dopĺňa len ak zóna Bane existuje a vchod ešte nestojí (nič sa nemaže)
    static bool EnsureMineEntrance()
    {
        GameObject root = GameObject.Find("Divocina_Bane");
        GameObject gate = GameObject.Find("Portal_zBane");
        if (root == null || gate == null || root.transform.Find("Vchod_do_bane") != null) return false;

        WildTheme theme = null;
        foreach (WildTheme t in WildThemes) if (t.key == "Bane") theme = t;
        if (theme == null) return false;

        BuildMineEntrance(root.transform, gate.transform.position, MakeWildMats(theme), new System.Random(77));
        return true;
    }

    // Modely, ktoré hra načítava za behu z Resources (drevorubači v prológu, jedlo v Divočine): chýbajúci súbor sa len ohlási, hra má náhradné tvary
    static string StoryAssetHints()
    {
        string[] required =
        {
            "Assets/Resources/Characters/Drevorubac1.fbx", "Assets/Resources/Characters/Drevorubac2.fbx",
            "Assets/Resources/Items/Chlieb.glb", "Assets/Resources/Items/zelenina.glb"
        };
        var missing = new System.Text.StringBuilder();
        foreach (string path in required)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) missing.Append(path).Append(", ");
        string text = missing.Length == 0 ? "" : "Chýbajú alebo sa nenaimportovali modely: " + missing + "hra použije náhradné tvary. ";

        // Sekanie stromov máva sekeru klipom ľahkého útoku z mečovej sady; bez nej sa použije záložné procedurálne zamáchnutie
        DemonAnimationSet sword = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(PlayerWeapons.SwordSetPath);
        if (sword == null || sword.lightAttacks == null || sword.lightAttacks.Length == 0)
            text += "Mečová sada hráča nemá klipy ľahkých útokov: sekera sa bude mávať záložnou animáciou (spusti Progeny → Hráč). ";
        if (AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Tools/Tool_Axe.prefab") == null)
            text += "Chýba model sekery (Resources/Tools/Tool_Axe): použije sa tvar z primitív (menu Nástroje: pripraviť modely). ";

        // Nová hra a Načítať znova načítajú scénu podľa čísla v Build Settings; bez nej by nefungovali
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex < 0)
            text += "Aktívna scéna nie je v File → Build Profiles / Build Settings: Nová hra a Načítať z pauzy nebudú fungovať (pridaj ju cez Add Open Scenes). ";
        return text;
    }

    // Upozornenia: čo ešte treba vytvoriť / opraviť ručne cez menu (aktualizácia tieto veci nevytvára, lebo by mazala objekty)
    static string WorldHints()
    {
        var hints = new System.Text.StringBuilder();
        if (GameObject.Find("Mesto_Centrum") == null) hints.Append("Chýba zóna Centrum (menu Mesto: Zóna Centrum). ");
        if (GameObject.Find("CorpCity") == null) hints.Append("Chýba nová Megakorporácia (menu Megakorporácia: mrakodrapy...). ");

        // Staré Centrum (obelisk namiesto kruhového objazdu, trhoviská v ceste): treba ho vytvoriť znova
        GameObject centre = GameObject.Find("Mesto_Centrum");
        if (centre != null)
        {
            bool hasRoundabout = false, hasOldSquare = false;
            foreach (Transform t in centre.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "ObjazdKopcek") hasRoundabout = true;
                else if (t.name == "Obelisk") hasOldSquare = true;
            }
            if (!hasRoundabout || hasOldSquare) hints.Append("Centrum je staré (bez kruhového objazdu / s obeliskom): vytvor zónu Centrum znova (menu Mesto: Zóna Centrum). ");
        }

        GameObject corpGround = GameObject.Find("Zem_Megakorporacia");
        GameObject civilGate = GameObject.Find("Portal_DoMegakorporacie");
        if (corpGround != null && civilGate != null)
        {
            Vector3 d = corpGround.transform.position - civilGate.transform.position;
            if ((d - CorpOffset).sqrMagnitude > 25f) hints.Append("Megakorporácia nestojí na pevnom mieste (menu Megakorporácia: premiestniť na východ mapy). ");
        }

        // Prológ: bytosti prepadu sa klonujú z nižšieho démona v scéne (VoidSpawn.Spawn); bez neho sa neobjavia a hráča nič nezabije
        bool hasSource = false;
        foreach (DemonEnemy d in Object.FindObjectsByType<DemonEnemy>(FindObjectsInactive.Include))
            if (d != null && d.tier < DemonTier.Higher && !d.allied && d.GetComponent<VoidSpawn>() == null) { hasSource = true; break; }
        if (!hasSource) hints.Append("Prológ: v scéne chýba nižší démon (Divoká svorka), z ktorého sa klonujú bytosti prepadu – prológ ich nevytvorí (menu Divočina: Divoká svorka). ");
        if (!ZoneLoader.TryGetGround("Zem_Divocina", out _)) hints.Append("Prológ: chýba zem Divočiny (Zem_Divocina), prológ sa preskočí na textový úvod. ");

        foreach (WildTheme wt in WildThemes)
            if (GameObject.Find(wt.RootName) != null && GameObject.Find("Portal_Do" + wt.GateKey) == null) hints.Append($"Chýba portál do zóny {wt.displayName} (menu Mesto: Portály doplniť). ");
        foreach (PortalGate g in Object.FindObjectsByType<PortalGate>(FindObjectsInactive.Include))
            if (!g.HasDestination) hints.Append($"Portál '{g.name}' nemá cieľ. ");
        return hints.ToString();
    }
}

public static partial class ProgenyTools
{
    // Odstráni staré zrkadlové nápisy (dva texty chrbtom k sebe); nové sú jeden nápis otočený ku kamere
    static int RemoveOldSigns()
    {
        int removed = 0;
        foreach (TextMesh tm in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include))
        {
            string n = tm.gameObject.name;
            bool old = n == "NapisZozadu" || n.EndsWith("_zozadu") || (n == "Napis" && tm.GetComponentInParent<PortalLabel>() == null);
            if (!old) continue;
            Object.DestroyImmediate(tm.gameObject);
            removed++;
        }
        foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include))
            if (go != null && go.name == "TabulaZnacky") { Object.DestroyImmediate(go); removed++; }
        return removed;
    }
}
