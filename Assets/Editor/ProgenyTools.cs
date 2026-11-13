using UnityEditor;
using UnityEngine;

public static partial class ProgenyTools
{
    static readonly RaycastHit[] groundHits = new RaycastHit[16];

    /// <summary>
    /// Výška povrchu pod bodom. Lúč začína tesne nad bodom (nie 20 m vysoko, aby nezasiahol strechu)
    /// a ignoruje hráča, NPC a triggery. Vráti false, ak pod bodom nič nie je.
    /// </summary>
    static bool TryGetGroundY(Vector3 position, out float y)
    {
        y = position.y;
        Vector3 origin = position + Vector3.up * 2f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 100f, ~0, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider.GetComponentInParent<PlayerStats>() != null || hit.collider.GetComponentInParent<Damageable>() != null) continue;
            if (hit.distance >= best) continue;

            best = hit.distance;
            y = hit.point.y;
            found = true;
        }
        return found;
    }

    [MenuItem("Progeny/Testy a pomocné/Vytvoriť obchodníka pri hráčovi", false, 900)]
    static void CreateMerchant()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        Vector3 position = player != null ? player.transform.position + player.transform.forward * 3f : Vector3.zero;
        if (TryGetGroundY(position, out float groundY)) position.y = groundY + 1f;    // kapsula má pivot v strede
        GameObject merchant = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        merchant.name = "Merchant";
        merchant.transform.position = position;
        Undo.RegisterCreatedObjectUndo(merchant, "Create Merchant");
        merchant.AddComponent<MerchantNPC>();
        merchant.GetComponent<Renderer>().material.color = new Color(0.75f, 0.55f, 0.2f);
        Selection.activeGameObject = merchant;
    }

    [MenuItem("Progeny/Testy a pomocné/Vytvoriť testovacie NPC", false, 901)]
    static void CreateTestNPCs()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        Vector3 center = player != null ? player.transform.position : Vector3.zero;

        GameObject root = new GameObject("TestNPCs");
        Undo.RegisterCreatedObjectUndo(root, "Create Test NPCs");

        Spawn(root.transform, "Civilian_1", NPCKind.Civilian, FactionId.Civilians, center + new Vector3(3f, 0f, 3f), false);
        Spawn(root.transform, "Civilian_2", NPCKind.Civilian, FactionId.Civilians, center + new Vector3(-3f, 0f, 3f), false);
        Spawn(root.transform, "Civilian_3", NPCKind.Civilian, FactionId.Megacorp, center + new Vector3(0f, 0f, 5f), false);
        Spawn(root.transform, "LesserDemon", NPCKind.LesserDemon, FactionId.WildClan, center + new Vector3(-4f, 0f, -2f), false);
        Spawn(root.transform, "MidDemon", NPCKind.MidDemon, FactionId.AshClan, center + new Vector3(-7f, 0f, 2f), false);
        Spawn(root.transform, "HigherDemon_Disguised", NPCKind.HigherDemon, FactionId.ShadowClan, center + new Vector3(4f, 0f, -2f), false);
        Spawn(root.transform, "Healer", NPCKind.Civilian, FactionId.Civilians, center + new Vector3(0f, 0f, -4f), true);
        SpawnAnimal(root.transform, "Animal_1", center + new Vector3(-2f, 0f, 6f));
        SpawnAnimal(root.transform, "Animal_2", center + new Vector3(2f, 0f, 6.5f));

        Spawn(root.transform, "Civilian_Sick", NPCKind.Civilian, FactionId.Civilians, center + new Vector3(7f, 0f, -3f), false);

        SpawnReflective(root.transform, "Okno", ReflectiveKind.Window, center + new Vector3(6f, 1.3f, -5f));
        SpawnReflective(root.transform, "Kaluz", ReflectiveKind.Water, center + new Vector3(2f, 0.02f, -6f));

        SpawnGuards(root.transform, center);

        EnsurePlayerComponents(player);
        AddCombatToSceneNPCs();

        // Chorý civilista: tmavé žily, ale žiadny iný znak (falošný poplach)
        Transform sick = root.transform.Find("Civilian_Sick");
        if (sick != null)
        {
            DemonMarks m = sick.GetComponent<DemonMarks>();
            if (m == null) m = Undo.AddComponent<DemonMarks>(sick.gameObject);
            m.noReflection = false;
            m.pupilFlash = false;
            m.veins = true;
            m.animalsFear = false;
            m.teeth = false;
            m.bodyAnomaly = false;
        }

        Selection.activeGameObject = root;
    }

    [MenuItem("Progeny/Testy a pomocné/Vytvoriť JailManager (Väzenie)", false, 903)]
    static void CreateJailManager()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        if (player == null) { Debug.LogError("Nenašiel sa hráč!"); return; }

        // 1. Nájdeme alebo vytvoríme JailManager
        JailManager existingJM = Object.FindAnyObjectByType<JailManager>();
        GameObject jmGo;
        if (existingJM != null) jmGo = existingJM.gameObject;
        else
        {
            jmGo = new GameObject("JailManager");
            Undo.RegisterCreatedObjectUndo(jmGo, "Create JailManager");
            existingJM = jmGo.AddComponent<JailManager>();
        }
        JailManager jm = jmGo.GetComponent<JailManager>();

        // 2. Vymažeme starú klietku, ak existuje, aby sme predišli duplicitám
        GameObject oldJail = GameObject.Find("Jail_Platform");
        if (oldJail != null) Undo.DestroyObjectImmediate(oldJail);

        // 3. Vytvoríme novú klietku presne pred hráčom
        Vector3 jailPos = player.transform.position + player.transform.forward * 15f;

        // Opravíme výšku, aby bola na zemi (raycast)
        if (Physics.Raycast(jailPos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            jailPos.y = hit.point.y;

        GameObject jailPlatform = new GameObject("Jail_Platform");
        Undo.RegisterCreatedObjectUndo(jailPlatform, "Create Jail Platform");
        jailPlatform.transform.position = jailPos;

        // PRIDANIE KINEMATIC RIGIDBODY: Aby sa klietka nedala zatlačiť hráčom
        Rigidbody jailRb = jailPlatform.AddComponent<Rigidbody>();
        jailRb.isKinematic = true;
        jailRb.useGravity = false;

        // Podlaha
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(floor, "Create Floor");
        floor.transform.SetParent(jailPlatform.transform);
        floor.transform.localPosition = new Vector3(0, -0.25f, 0); // Položíme na úroveň nôh
        floor.transform.localScale = new Vector3(6f, 0.5f, 6f);
        floor.GetComponent<Renderer>().material.color = new Color(0.2f, 0.2f, 0.2f);

        // Steny
        for (int i = 0; i < 4; i++)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(wall, "Create Wall");
            wall.transform.SetParent(jailPlatform.transform);
            wall.transform.localPosition = Quaternion.Euler(0, i * 90, 0) * new Vector3(0, 1.5f, 3f);
            wall.transform.localRotation = Quaternion.Euler(0, i * 90, 0);
            wall.transform.localScale = new Vector3(6f, 3f, 0.2f);
            wall.GetComponent<Renderer>().material.color = new Color(0.1f, 0.1f, 0.1f);
        }

        // Strecha
        GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(roof, "Create Roof");
        roof.transform.SetParent(jailPlatform.transform);
        roof.transform.localPosition = new Vector3(0, 3f, 0);
        roof.transform.localScale = new Vector3(6f, 0.2f, 6f);
        roof.GetComponent<Renderer>().material.color = new Color(0.1f, 0.1f, 0.1f);

        // 4. Nastavíme súradnice do JailManageru
        jm.jailPosition = jailPos + new Vector3(0, 1f, 0); // Hráč bude v strede klietky
        jm.releasePosition = jailPos + new Vector3(0, 0f, 6f); // Hráč sa objaví pred klietkou

        Selection.activeGameObject = jailPlatform;
        Debug.Log("JailManager a nová Jail_Platform úspešne vytvorené a nastavené!");
    }

    static void SpawnGuards(Transform parent, Vector3 center)
    {
        GameObject guardRoot = new GameObject("CityGuards");
        Undo.RegisterCreatedObjectUndo(guardRoot, "Create City Guards");
        guardRoot.transform.SetParent(parent);

        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 2f / 4;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 10f;
            Vector3 spawnPos = center + offset;

            if (TryGetGroundY(spawnPos, out float groundY)) spawnPos.y = groundY;   // kapsula má stred 1 m nad pivotom (päta)

            GameObject guard = new GameObject($"Vojak_{i}");
            Undo.RegisterCreatedObjectUndo(guard, "Create Guard");
            guard.transform.SetParent(guardRoot.transform);
            guard.transform.position = spawnPos;

            NPCPerception npc = Undo.AddComponent<NPCPerception>(guard);
            npc.kind = NPCKind.Civilian;
            npc.faction = FactionId.Megacorp;
            npc.humanColor = new Color(0.31f, 0.38f, 0.45f);

            CapsuleCollider col = guard.AddComponent<CapsuleCollider>();
            col.height = 2f;
            col.center = new Vector3(0, 1f, 0);
            col.isTrigger = false;

            Rigidbody rb = guard.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            CityGuardNPC cityGuard = guard.AddComponent<CityGuardNPC>();
            cityGuard.district = DistrictWard.Military;

            DistrictOutfit outfit = guard.AddComponent<DistrictOutfit>();
            outfit.district = DistrictWard.Military;
            outfit.ApplyDistrictColors();

            Damageable dmg = guard.AddComponent<Damageable>();
            dmg.displayName = "Vojak";
            dmg.team = CombatTeam.Civilian;
            dmg.maxHealth = 100f;
        }
    }

    [MenuItem("Progeny/Testy a pomocné/Vytvoriť okno (odraz)", false, 910)]
    static void CreateWindow() => CreateReflectiveInFrontOfPlayer(ReflectiveKind.Window);

    [MenuItem("Progeny/Testy a pomocné/Vytvoriť kaluž (odraz)", false, 911)]
    static void CreatePuddle() => CreateReflectiveInFrontOfPlayer(ReflectiveKind.Water);

    static void CreateReflectiveInFrontOfPlayer(ReflectiveKind kind)
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        Vector3 pos = player != null ? player.transform.position + player.transform.forward * 6f : Vector3.zero;
        pos.y = kind == ReflectiveKind.Water ? 0.02f : 1.3f;

        GameObject go = new GameObject(kind == ReflectiveKind.Water ? "Kaluz" : "Okno");
        Undo.RegisterCreatedObjectUndo(go, "Create Reflective");
        go.transform.position = pos;
        go.AddComponent<ReflectiveSurface>().kind = kind;
        Selection.activeGameObject = go;
    }

    [MenuItem("Progeny/Testy a pomocné/Vojenská zóna/Vytvoriť obytnú zónu vedľa vojenskej", false, 921)]
    static void CreateResidentialZone()
    {
        MilitaryZone military = Object.FindAnyObjectByType<MilitaryZone>();
        Vector3 pos;
        if (military != null)
        {
            Bounds bounds = military.GetComponent<BoxCollider>().bounds;
            pos = military.transform.position + Vector3.right * (bounds.size.x + 5f);
        }
        else
        {
            PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
            pos = player != null ? player.transform.position + player.transform.forward * 20f : Vector3.zero;
        }

        if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y;

        GameObject go = new GameObject("ResidentialZone");
        Undo.RegisterCreatedObjectUndo(go, "Create Residential Zone");
        go.transform.position = pos;
        go.AddComponent<ResidentialZone>();
        Selection.activeGameObject = go;
    }

    static void SpawnReflective(Transform parent, string name, ReflectiveKind kind, Vector3 position)
    {
        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Reflective");
        go.transform.SetParent(parent);
        go.transform.position = position;
        go.AddComponent<ReflectiveSurface>().kind = kind;
    }

    [MenuItem("Progeny/Testy a pomocné/Vojenská zóna/Vytvoriť vojenskú zónu", false, 920)]
    static void CreateMilitaryZone()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        Vector3 pos = player != null ? player.transform.position + player.transform.forward * 20f : Vector3.zero;

        if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y;

        GameObject go = new GameObject("MilitaryZone");
        Undo.RegisterCreatedObjectUndo(go, "Create Military Zone");
        go.transform.position = pos;
        go.AddComponent<MilitaryZone>();     // Reset() nastaví trigger a veľkosť
        Selection.activeGameObject = go;
    }


    // ---------- Zakrádanie, vojaci a štvrte mesta ----------
    [MenuItem("Progeny/Testy a pomocné/Vojenská zóna/Vytvoriť vojakov (hliadka)", false, 922)]
    static void CreateSoldiers()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        MilitaryZone zone = Object.FindAnyObjectByType<MilitaryZone>();

        Vector3 center = zone != null ? zone.transform.position
            : (player != null ? player.transform.position + player.transform.forward * 14f : Vector3.zero);

        GameObject root = new GameObject("Soldiers");
        Undo.RegisterCreatedObjectUndo(root, "Create Soldiers");

        SpawnSoldier(root.transform, "Soldier_1", center + new Vector3(-5f, 0f, 2f));
        SpawnSoldier(root.transform, "Soldier_2", center + new Vector3(5f, 0f, -2f));
        SpawnSoldier(root.transform, "Soldier_3", center + new Vector3(0f, 0f, 7f));

        EnsurePlayerComponents(player);
        Selection.activeGameObject = root;
    }

    static void SpawnSoldier(Transform parent, string name, Vector3 position)
    {
        // Pivot je na päte (rovnako ako u modrých civilistov), preto bez výškového posunu
        if (TryGetGroundY(position, out float groundY)) position.y = groundY;

        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create Soldier");
        go.transform.SetParent(parent);
        ConfigureGroundedCapsule(go);
        go.transform.position = position;

        NPCPerception npc = Undo.AddComponent<NPCPerception>(go);
        npc.kind = NPCKind.Civilian;                              // človek (vzhľad a vnímanie)
        npc.faction = FactionId.Megacorp;                          // vojenská megakorporácia
        npc.humanColor = new Color(0.22f, 0.38f, 0.2f);           // olivová uniforma
        npc.monsterColor = new Color(0.45f, 0.05f, 0.25f);

        Damageable d = Undo.AddComponent<Damageable>(go);
        d.displayName = "Vojak";
        d.team = CombatTeam.Civilian;
        d.maxHealth = 70f;
        d.moneyReward = 15;

        NPCWander wander = Undo.AddComponent<NPCWander>(go);
        wander.wanderRadius = 5f;
        wander.speed = 1.6f;

        Undo.AddComponent<SoldierAI>(go);
    }

    [MenuItem("Progeny/Testy a pomocné/Štvrte mesta/Obytná štvrť", false, 930)]
    static void MakeResidential() => CreateDistrict(DistrictType.Residential);

    [MenuItem("Progeny/Testy a pomocné/Štvrte mesta/Podsvetie", false, 931)]
    static void MakeUnderworld() => CreateDistrict(DistrictType.Underworld);

    [MenuItem("Progeny/Testy a pomocné/Štvrte mesta/Priemyselná štvrť", false, 932)]
    static void MakeIndustrial() => CreateDistrict(DistrictType.Industrial);

    [MenuItem("Progeny/Testy a pomocné/Štvrte mesta/Zničená stará časť", false, 933)]
    static void MakeRuins() => CreateDistrict(DistrictType.Ruins);

    static GameObject CreateDistrict(DistrictType type)
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        Vector3 pos = player != null ? player.transform.position + player.transform.forward * 20f : Vector3.zero;

        if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            pos.y = hit.point.y;

        GameObject go = new GameObject("District_" + type);
        Undo.RegisterCreatedObjectUndo(go, "Create District");
        go.transform.position = pos;

        CityDistrict district = go.AddComponent<CityDistrict>();   // Reset() nastaví trigger a veľkosť
        district.type = type;

        Selection.activeGameObject = go;
        return go;
    }

    [MenuItem("Progeny/Testy a pomocné/Vojenská zóna/Vytvoriť testovaciu zakrádaciu oblasť", false, 923)]
    static void CreateStealthTestArea()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();

        // Vojenská zóna s vojakmi a stenami, za ktorými sa dá skryť
        MilitaryZone zone = Object.FindAnyObjectByType<MilitaryZone>();
        if (zone == null)
        {
            CreateMilitaryZone();
            zone = Object.FindAnyObjectByType<MilitaryZone>();
        }

        Vector3 c = zone != null ? zone.transform.position : Vector3.zero;

        GameObject root = new GameObject("StealthTestArea");
        Undo.RegisterCreatedObjectUndo(root, "Create Stealth Test Area");

        SpawnWall(root.transform, "Wall_1", c + new Vector3(-6f, 0f, -4f), new Vector3(8f, 3f, 0.6f));
        SpawnWall(root.transform, "Wall_2", c + new Vector3(6f, 0f, 4f), new Vector3(8f, 3f, 0.6f));
        SpawnWall(root.transform, "Wall_3", c + new Vector3(0f, 0f, -9f), new Vector3(0.6f, 3f, 8f));

        SpawnSoldier(root.transform, "Soldier_1", c + new Vector3(-5f, 0f, 2f));
        SpawnSoldier(root.transform, "Soldier_2", c + new Vector3(5f, 0f, -2f));

        EnsurePlayerComponents(player);
        Selection.activeGameObject = root;
    }

    static void SpawnWall(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        if (Physics.Raycast(position + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            position.y = hit.point.y + scale.y * 0.5f;

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(wall, "Create Wall");
        wall.name = name;
        wall.transform.SetParent(parent);
        wall.transform.position = position;
        wall.transform.localScale = scale;

        // Vlastný materiál, aby sa nemenil zdieľaný predvolený
        Renderer r = wall.GetComponent<Renderer>();
        Material m = new Material(r.sharedMaterial);
        m.color = new Color(0.35f, 0.37f, 0.4f);
        r.sharedMaterial = m;
    }

    [MenuItem("Progeny/Hráč/Pridať komponenty hráčovi", false, 601)]
    static void AddPlayerComponents()
    {
        EnsurePlayerComponents(Object.FindAnyObjectByType<PlayerStats>());
    }

    [MenuItem("Progeny/Testy a pomocné/Pridať boj k NPC v scéne", false, 902)]
    static void AddCombatToSceneNPCs()
    {
        int count = 0;

        // NPC s identitou (civilisti, démoni)
        foreach (NPCPerception npc in Object.FindObjectsByType<NPCPerception>())
        {
            SetupCombat(npc.gameObject);
            count++;
        }

        // Zvieratá (majú zdroj krvi, ale nemajú NPCPerception)
        foreach (BloodSource bs in Object.FindObjectsByType<BloodSource>())
        {
            if (bs.GetComponent<NPCPerception>() == null)
            {
                SetupCombat(bs.gameObject);
                count++;
            }
        }

        EnsurePlayerComponents(Object.FindAnyObjectByType<PlayerStats>());
        Debug.Log($"Boj nastavený pre {count} NPC.");
    }

    // Pridá chýbajúce komponenty (kŕmenie, zmysly, boj) na hráča
    static void EnsurePlayerComponents(PlayerStats player)
    {
        if (player == null)
        {
            Debug.LogWarning("Hráč s komponentom PlayerStats sa v scéne nenašiel.");
            return;
        }

        if (player.GetComponent<PlayerFeeding>() == null)
            Undo.AddComponent<PlayerFeeding>(player.gameObject);

        if (player.GetComponent<DemonSenses>() == null)
            Undo.AddComponent<DemonSenses>(player.gameObject);

        if (player.GetComponent<PlayerCombat>() == null)
            Undo.AddComponent<PlayerCombat>(player.gameObject);

        if (player.GetComponent<PlayerFaction>() == null)
            Undo.AddComponent<PlayerFaction>(player.gameObject);

        if (player.GetComponent<FactionAbility>() == null)
            Undo.AddComponent<FactionAbility>(player.gameObject);

        if (player.GetComponent<ClanOnHit>() == null)
            Undo.AddComponent<ClanOnHit>(player.gameObject);

        if (player.GetComponent<DetectiveSystem>() == null)
            Undo.AddComponent<DetectiveSystem>(player.gameObject);

        if (player.GetComponent<PlayerStealth>() == null)
            Undo.AddComponent<PlayerStealth>(player.gameObject);

        if (player.GetComponent<Wardrobe>() == null)
            Undo.AddComponent<Wardrobe>(player.gameObject);
    }

    // Zdravie a správanie podľa typu NPC
    static void SetupCombat(GameObject go)
    {
        if (go.GetComponent<Healer>() != null) return;   // lekár sa zatiaľ nedá zabiť

        Damageable d = go.GetComponent<Damageable>();
        if (d == null) d = Undo.AddComponent<Damageable>(go);

        NPCPerception npc = go.GetComponent<NPCPerception>();

        // Zviera
        if (npc == null)
        {
            d.displayName = "Zviera";
            d.team = CombatTeam.Civilian;
            d.maxHealth = 20f;
            return;
        }

        if (npc.kind == NPCKind.Civilian)
        {
            DemonMarks m = go.GetComponent<DemonMarks>();
            if (m == null) m = Undo.AddComponent<DemonMarks>(go);

            m.noReflection = false; m.pupilFlash = false; m.veins = false; m.animalsFear = false; m.teeth = false; m.bodyAnomaly = false;
            m.signsNeeded = 2;

            if (Random.value < 0.4f)
            {
                int r = Random.Range(0, 3);
                if (r == 0) m.veins = true;
                else if (r == 1) m.noReflection = true;
                else m.animalsFear = true;
            }
        }

        switch (npc.kind)
        {
            case NPCKind.Civilian:
                d.displayName = "Civilista";
                d.team = CombatTeam.Civilian;
                d.maxHealth = 30f;
                break;

            case NPCKind.LesserDemon:
                d.displayName = "Nižší démon";
                d.team = CombatTeam.Demon;
                d.maxHealth = 60f;
                d.moneyReward = 20;
                AddEnemy(go, 15f, FactionId.WildClan, DemonTier.Lesser);
                break;

            case NPCKind.MidDemon:
                d.displayName = "Stredný démon";
                d.team = CombatTeam.Demon;
                d.maxHealth = 90f;
                d.moneyReward = 40;
                AddEnemy(go, 20f, FactionId.AshClan, DemonTier.Mid);
                break;

            case NPCKind.HigherDemon:
                d.displayName = "Vyšší démon";
                d.team = CombatTeam.Demon;
                d.maxHealth = 120f;
                d.immuneToNormalWeapons = true;
                d.moneyReward = 60;
                AddEnemy(go, 25f, FactionId.ShadowClan, DemonTier.Higher);

                // Detektívka: znaky a slabina (oheň, blesk alebo zem)
                if (go.GetComponent<DemonMarks>() == null) Undo.AddComponent<DemonMarks>(go);
                if (d.weakness == DamageKind.Normal) d.weakness = (DamageKind)Random.Range(1, 4);
                break;
        }
    }

    static void AddEnemy(GameObject go, float damage, FactionId clan, DemonTier tier)
    {
        DemonEnemy e = go.GetComponent<DemonEnemy>();
        if (e == null) e = Undo.AddComponent<DemonEnemy>(go);
        e.tier = tier;
        e.attackDamage = damage;
        if (e.clan == FactionId.None) e.clan = clan;
    }

    [MenuItem("Progeny/Démoni/Priradiť model démona (Demon_body.glb)", false, 520)]
    static void AssignDemonModels()
    {
        GameObject model = NPCPerception.LoadDemonModelAsset();
        if (model == null)
        {
            Debug.LogWarning($"Model démona sa nenašiel ({NPCPerception.DemonModelPath}). Je nainštalovaný glTFast a import prebehol?");
            return;
        }

        int count = 0;
        foreach (NPCPerception npc in Object.FindObjectsByType<NPCPerception>())
        {
            if (npc.kind == NPCKind.Civilian || npc.keepModelMaterials) continue;   // boss má vlastný model

            Undo.RecordObject(npc, "Model démona");
            npc.higherDemonBodyOverride = model;
            npc.demonMaterial = AssetDatabase.LoadAssetAtPath<Material>(NPCPerception.DemonMaterialPath);
            EditorUtility.SetDirty(npc);
            count++;
        }
        Debug.Log($"Model démona priradený {count} démonom. Ulož scénu.");
    }

    // ---------- Animácie démonov (Mixamo) ----------
    static bool NameHas(string text, params string[] keys)
    {
        foreach (string k in keys)
            if (text.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static AnimationClip FirstClip(string path)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            AnimationClip clip = o as AnimationClip;
            if (clip != null && !clip.name.StartsWith("__preview__")) return clip;
        }
        return null;
    }

    static void DisableLoop(string path)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) return;   // predvolené klipy nie sú cyklické

        bool changed = false;
        foreach (ModelImporterClipAnimation c in clips)
        {
            if (!c.loopTime) continue;
            c.loopTime = false;
            changed = true;
        }
        if (!changed) return;

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static void EnableLoop(string path)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) return;

        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

        bool changed = false;
        foreach (ModelImporterClipAnimation c in clips)
        {
            if (c.loopTime) continue;
            c.loopTime = true;
            changed = true;
        }
        if (!changed) return;

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// Prejde FBX súbory s "@" v názve (Mixamo: Postava@Akcia.fbx), podľa akcie v názve vyberie Idle / Run / Attack / premenu,
    /// zapne cyklenie pre Idle a Run, uloží DemonAnimationSet a priradí ho všetkým démonom v scéne.
    /// </summary>
    [MenuItem("Progeny/Démoni/Animácie/Vytvoriť sadu z Mixamo klipov", false, 530)]
    static void BuildDemonAnimationSet()
    {
        string idlePath = null, runPath = null, walkPath = null, attackPath = null, transformPath = null;
        int idleLen = int.MaxValue, runLen = int.MaxValue, walkLen = int.MaxValue, attackLen = int.MaxValue, transformLen = int.MaxValue;
        string startWalkPath = null, elbowPath = null, deathFwdPath = null, deathBackPath = null;
        int startLen = int.MaxValue, elbowLen = int.MaxValue, deathFwdLen = int.MaxValue, deathBackLen = int.MaxValue;

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith("Assets/Models/Player/")) continue;   // klipy hráča (napr. player@Fast Run) nepatria démonom

            string file = System.IO.Path.GetFileNameWithoutExtension(path);
            int at = file.IndexOf('@');
            if (at < 0) continue;

            string action = file.Substring(at + 1);
            // Smrť: "back" = pád dozadu, ostatné (Forward / Front) = pád dopredu
            if (NameHas(action, "death", "dying"))
            {
                if (NameHas(action, "back"))
                {
                    if (action.Length < deathBackLen) { deathBackLen = action.Length; deathBackPath = path; }
                }
                else if (action.Length < deathFwdLen) { deathFwdLen = action.Length; deathFwdPath = path; }
                continue;
            }

            // Druhý, silnejší útok (Illegal Elbow Punch) - musí pred bežným útokom, "punch" by ho zachytil
            if (NameHas(action, "elbow"))
            {
                if (action.Length < elbowLen) { elbowLen = action.Length; elbowPath = path; }
                continue;
            }

            // Rozbeh (Start Walking) má vlastný slot: prehrá sa len pri rozbehu z miesta
            if (NameHas(action, "start") && NameHas(action, "walk", "run"))
            {
                if (action.Length < startLen) { startLen = action.Length; startWalkPath = path; }
                continue;
            }
            // Ostatné prechodové klipy (Stop, Turn) do cyklu nepatria
            if (NameHas(action, "start", "stop", "turn")) continue;

            // Z viacerých kandidátov vyhrá najkratší názov (Walking pred "Walking 1")
            int len = action.Length;
            if (NameHas(action, "idle", "stand", "breath")) { if (len < idleLen) { idleLen = len; idlePath = path; } }
            else if (NameHas(action, "run", "jog", "sprint")) { if (len < runLen) { runLen = len; runPath = path; } }
            else if (NameHas(action, "walk")) { if (len < walkLen) { walkLen = len; walkPath = path; } }
            else if (NameHas(action, "attack", "punch", "kick", "slash", "strike", "bite", "swing", "hit")) { if (len < attackLen) { attackLen = len; attackPath = path; } }
            else if (NameHas(action, "transform", "roar", "scream", "rage", "power")) { if (len < transformLen) { transformLen = len; transformPath = path; } }
        }

        if (runPath == null) runPath = walkPath;   // chôdza ako náhrada behu

        if (idlePath != null) EnableLoop(idlePath);
        if (runPath != null) EnableLoop(runPath);

        // Jednorazové klipy sa nesmú zacyklovať (úder sa neopakuje, mŕtvy démon nevstane)
        foreach (string once in new[] { attackPath, elbowPath, deathFwdPath, deathBackPath, startWalkPath })
            if (once != null) DisableLoop(once);

        if (!AssetDatabase.IsValidFolder("Assets/Animations")) AssetDatabase.CreateFolder("Assets", "Animations");
        DemonAnimationSet set = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(NPCPerception.DemonAnimationSetPath);
        bool isNew = set == null;
        if (isNew) set = ScriptableObject.CreateInstance<DemonAnimationSet>();

        set.idle = idlePath != null ? FirstClip(idlePath) : null;
        set.run = runPath != null ? FirstClip(runPath) : null;
        set.startWalk = startWalkPath != null ? FirstClip(startWalkPath) : null;
        set.attack = attackPath != null ? FirstClip(attackPath) : null;
        set.attackElbow = elbowPath != null ? FirstClip(elbowPath) : null;
        set.deathForward = deathFwdPath != null ? FirstClip(deathFwdPath) : null;
        set.deathBack = deathBackPath != null ? FirstClip(deathBackPath) : null;
        set.transformation = transformPath != null ? FirstClip(transformPath) : null;

        if (isNew) AssetDatabase.CreateAsset(set, NPCPerception.DemonAnimationSetPath);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        int assigned = 0;
        foreach (NPCPerception npc in Object.FindObjectsByType<NPCPerception>())
        {
            if (npc.kind == NPCKind.Civilian || npc.keepModelMaterials) continue;   // boss má vlastnú sadu
            Undo.RecordObject(npc, "Animácie démona");
            npc.demonAnimationSet = set;
            EditorUtility.SetDirty(npc);
            assigned++;
        }

        string missing = "";
        if (set.idle == null) missing += " Idle";
        if (set.run == null) missing += " Run/Walk";
        if (set.attack == null) missing += " Attack";
        if (set.attackElbow == null) Debug.LogWarning("Chýba Elbow Punch (súbor Demon@Illegal Elbow Punch.fbx): démon použije len bežný úder.");
        if (set.deathForward == null && set.deathBack == null)
            Debug.LogWarning("Chýbajú animácie smrti (Demon@Falling Forward Death.fbx / Demon@Falling Back Death.fbx): démon sa pri smrti položí na bok ako doteraz.");
        else if (set.deathForward == null || set.deathBack == null)
            Debug.LogWarning($"Je len jedna animácia smrti ({(set.deathForward != null ? "dopredu" : "dozadu")}), použije sa vždy ona. Pridaj chýbajúci súbor pre výber 50/50.");
        Debug.Log($"Sada animácií démona: idle={(set.idle != null ? set.idle.name : "-")}, run={(set.run != null ? set.run.name : "-")}, " +
                  $"attack={(set.attack != null ? set.attack.name : "-")}. Priradená {assigned} démonom. Chýba:{(missing.Length > 0 ? missing : " nič")}. Ulož scénu.");

        // Kostra v modeli démona je nutná, inak sa Mixamo klipy nemajú na čo aplikovať
        GameObject model = NPCPerception.LoadDemonModelAsset();
        bool rigged = false;
        if (model != null)
        {
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.rootBone != null) { rigged = true; break; }
        }
        if (!rigged)
            Debug.LogWarning("Model démona nemá kostru (statický mesh). Mixamo animácie sa naň nedajú aplikovať - vyrigguj ho cez Mixamo (Auto-Rigger) a pridaj do Assets/Models. Zatiaľ beží len procedurálny pohyb.");
    }

    /// <summary>
    /// Všetko naraz: avatar rigu, sada animácií, model + sada pre všetkých démonov v scéne, kontrola a uloženie scény.
    /// </summary>
    [MenuItem("Progeny/Démoni/Animácie/Nastaviť všetko a uložiť scénu", false, 531)]
    static void SetupDemonsAndSave()
    {
        // 1. Rig: Generic avatar z modelu, aby mal Animator čo animovať
        ModelImporter rig = AssetImporter.GetAtPath(NPCPerception.DemonModelRiggedPath) as ModelImporter;
        if (rig == null)
        {
            Debug.LogError($"Chýba {NPCPerception.DemonModelRiggedPath}.");
            return;
        }
        if (rig.animationType == ModelImporterAnimationType.Generic && rig.avatarSetup == ModelImporterAvatarSetup.NoAvatar)
        {
            rig.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            rig.SaveAndReimport();
        }

        // FBX z Mixama nesú kamery a svetlá (Camera / Light uzly) - na démonoch nemajú byť
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" }))
            DisableCameraLightImport(AssetDatabase.GUIDToAssetPath(guid));

        // 2. Sada animácií, materiál a priradenie modelu + sady
        BuildDemonAnimationSet();
        BuildDemonMaterial();
        AssignDemonModels();

        // 3. Kontrola
        VerifyDemonSetup();

        // 4. Uloženie scén
        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        Debug.Log("Scéna uložená.");
    }

    static void DisableCameraLightImport(string path)
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null || (!importer.importCameras && !importer.importLights)) return;

        importer.importCameras = false;
        importer.importLights = false;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// Textúra z modelu (embedded Color / NormalGL z Tripo) sa nemusí namapovať na materiál z FBX, preto sa z nej poskladá
    /// vlastný Standard materiál Assets/Materials/DemonMaterial.mat. Bez textúry dostane tmavočervenú farbu.
    /// </summary>
    [MenuItem("Progeny/Démoni/Animácie/Vytvoriť materiál démona", false, 532)]
    static void BuildDemonMaterial()
    {
        Texture2D albedo = null, normal = null;
        bool normalIsNormalMap = false;

        string[] sources = { NPCPerception.DemonModelRiggedPath, NPCPerception.DemonModelPath };
        foreach (string src in sources)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(src))
            {
                Texture2D t = o as Texture2D;
                if (t == null) continue;
                if (albedo == null && NameHas(t.name, "color", "base", "albedo", "diffuse")) albedo = t;
                else if (normal == null && NameHas(t.name, "normal")) normal = t;
            }
        }

        // Statický GLB démona (rovnaké UV ako vyrigovaný model) drží textúru v materiáli, nie pod názvom "Color"
        if (albedo == null) albedo = FindAlbedoInModel(NPCPerception.DemonModelPath);

        // Textúry sú v FBX len vložené: vyextrahujú sa do súborov (aby šla normálová mapa správne nastaviť)
        if (albedo == null)
        {
            ModelImporter rig = AssetImporter.GetAtPath(NPCPerception.DemonModelRiggedPath) as ModelImporter;
            const string dir = "Assets/Models/Textures";
            if (rig != null)
            {
                if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder("Assets/Models", "Textures");
                rig.ExtractTextures(dir);
                AssetDatabase.Refresh();

                foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (t == null) continue;

                    if (albedo == null && NameHas(t.name, "color", "base", "albedo", "diffuse")) albedo = t;
                    else if (normal == null && NameHas(t.name, "normal"))
                    {
                        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                        if (ti != null)
                        {
                            ti.textureType = TextureImporterType.NormalMap;
                            ti.SaveAndReimport();
                            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                            normalIsNormalMap = true;
                        }
                    }
                }
            }
        }

        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(NPCPerception.DemonMaterialPath);
        bool isNew = mat == null;
        if (isNew) mat = new Material(Shader.Find("Standard"));

        mat.SetTexture("_MainTex", albedo);
        mat.color = albedo != null ? Color.white : new Color(0.55f, 0.1f, 0.12f);
        mat.SetFloat("_Metallic", 0.15f);
        mat.SetFloat("_Glossiness", 0.35f);
        if (normal != null && normalIsNormalMap)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }

        if (isNew) AssetDatabase.CreateAsset(mat, NPCPerception.DemonMaterialPath);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Debug.Log($"Materiál démona: albedo = {(albedo != null ? albedo.name : "NENÁJDENÉ (použitá farba)")}, " +
                  $"normálová mapa = {(normalIsNormalMap ? normal.name : "nie")}.");
    }

    static void VerifyDemonSetup()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(NPCPerception.DemonModelRiggedPath);
        DemonAnimationSet set = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(NPCPerception.DemonAnimationSetPath);
        if (model == null || set == null) { Debug.LogError("Kontrola: chýba model alebo sada animácií."); return; }

        SkinnedMeshRenderer skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Debug.Log($"Kontrola modelu: skinned mesh = {(skin != null ? skin.name : "NIE")}, kostí = {(skin != null ? skin.bones.Length : 0)}, " +
                  $"koreň kostry = {(skin != null && skin.rootBone != null ? skin.rootBone.name : "-")}.");

        CheckClip("Idle", set.idle, model);
        CheckClip("Run/Walk", set.run, model);
        CheckClip("Attack", set.attack, model);
        if (set.attackElbow != null) CheckClip("Elbow Punch", set.attackElbow, model);
        if (set.deathForward != null) CheckClip("Smrť dopredu", set.deathForward, model);
        if (set.deathBack != null) CheckClip("Smrť dozadu", set.deathBack, model);
        if (set.transformation != null) CheckClip("Transform", set.transformation, model);

        int demons = 0, ok = 0;
        foreach (NPCPerception npc in Object.FindObjectsByType<NPCPerception>())
        {
            if (npc.kind == NPCKind.Civilian) continue;
            demons++;
            if (npc.higherDemonBodyOverride == model && npc.demonAnimationSet == set) ok++;
        }
        Debug.Log($"Kontrola scény: démonov {demons}, správne nastavených {ok}.");
    }

    // Overí, že cesty kostí v klipe existujú v modeli (inak by sa klip na model nenaviazal)
    static void CheckClip(string label, AnimationClip clip, GameObject model)
    {
        if (clip == null) { Debug.LogWarning($"Kontrola {label}: klip chýba."); return; }

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        int total = 0, found = 0;
        string lastMissing = null;
        foreach (EditorCurveBinding b in bindings)
        {
            if (string.IsNullOrEmpty(b.path)) continue;
            total++;
            if (model.transform.Find(b.path) != null) found++;
            else lastMissing = b.path;
        }

        string loop = clip.isLooping ? "cyklí" : "necykli";
        string msg = $"Kontrola {label}: '{clip.name}' {clip.length:0.00} s, {loop}, kosti sedia {found}/{total}" +
                     (found < total ? $" (napr. chýba '{lastMissing}')" : "");
        if (total == 0 || found < total) Debug.LogWarning(msg);
        else Debug.Log(msg);
    }

    // ---------- Farby a klany ----------
    [MenuItem("Progeny/Démoni/Obnoviť farby a klany NPC", false, 540)]
    static void RefreshNPCs()
    {
        foreach (NPCPerception npc in Object.FindObjectsByType<NPCPerception>())
        {
            ApplyColors(npc, npc.GetComponent<Healer>() != null);

            DemonEnemy e = npc.GetComponent<DemonEnemy>();
            if (e != null && e.clan == FactionId.None)
            {
                Undo.RecordObject(e, "NPC clan");
                e.clan = npc.kind == NPCKind.LesserDemon ? FactionId.WildClan
                       : npc.kind == NPCKind.MidDemon ? FactionId.AshClan : FactionId.ShadowClan;
                EditorUtility.SetDirty(e);
            }
        }
        Debug.Log("Farby a klany NPC obnovené.");
    }

    static void ApplyColors(NPCPerception npc, bool healer)
    {
        Undo.RecordObject(npc, "NPC colors");

        if (healer)
        {
            npc.humanColor = new Color(0.1f, 0.75f, 0.75f);          // lekárka: tyrkysová
        }
        else
        {
            switch (npc.kind)
            {
                case NPCKind.Civilian:
                    npc.humanColor = new Color(0.35f, 0.55f, 0.9f);   // modrá
                    npc.monsterColor = new Color(0.45f, 0.05f, 0.25f);
                    break;
                case NPCKind.LesserDemon:                             // jasná červená
                    npc.humanColor = new Color(0.9f, 0.35f, 0.3f);
                    npc.monsterColor = new Color(0.85f, 0.08f, 0.08f);
                    break;
                case NPCKind.MidDemon:                                // oranžovo-červená
                    npc.humanColor = new Color(0.8f, 0.4f, 0.15f);
                    npc.monsterColor = new Color(0.75f, 0.25f, 0.05f);
                    break;
                case NPCKind.HigherDemon:                             // tmavá karmínová
                    npc.humanColor = new Color(0.55f, 0.05f, 0.12f);
                    npc.monsterColor = new Color(0.35f, 0f, 0.05f);
                    break;
            }
        }
        EditorUtility.SetDirty(npc);
    }

    static void ConfigureGroundedCapsule(GameObject go)
    {
        CapsuleCollider col = go.GetComponent<CapsuleCollider>();
        if (col == null) col = go.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 1f, 0f);
        col.height = 2f;
        col.radius = 0.35f;
    }

    static void Spawn(Transform parent, string name, NPCKind kind, FactionId faction, Vector3 position, bool healer)
    {
        // Postav NPC na zem tak, aby päta bola na povrchu platformy a nie v nej.
        if (TryGetGroundY(position, out float groundY)) position.y = groundY;

        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create NPC");
        go.transform.SetParent(parent);
        ConfigureGroundedCapsule(go);
        go.transform.position = position;

        NPCPerception npc = Undo.AddComponent<NPCPerception>(go);
        npc.kind = kind;
        npc.faction = faction;
        ApplyColors(npc, healer);

        if (healer)
        {
            // Lekárka zatiaľ nie je zdroj krvi ani cieľ útoku
            Undo.AddComponent<Healer>(go);
            return;
        }

        Undo.AddComponent<NPCWander>(go);

        // Len civilisti sú zdroj krvi. Démoni NIE.
        if (kind == NPCKind.Civilian)
        {
            BloodSource bs = Undo.AddComponent<BloodSource>(go);
            bs.displayName = "Civilista";
            bs.bloodValue = 40f;
            bs.xpValue = 25;
        }
    }

    static void SpawnAnimal(Transform parent, string name, Vector3 position)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Undo.RegisterCreatedObjectUndo(go, "Create Animal");
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.localScale = new Vector3(0.7f, 0.6f, 1.2f);

        if (TryGetGroundY(position, out float groundY)) position.y = groundY + 0.3f;
        go.transform.position = position;

        // Vlastný materiál, aby sa nemenil zdieľaný predvolený
        Renderer r = go.GetComponent<Renderer>();
        Material m = new Material(r.sharedMaterial);
        m.color = new Color(0.5f, 0.38f, 0.25f);
        r.sharedMaterial = m;

        NPCWander wander = Undo.AddComponent<NPCWander>(go);
        wander.speed = 2.5f;

        BloodSource bs = Undo.AddComponent<BloodSource>(go);
        bs.displayName = "Zviera";
        bs.bloodValue = 20f;
        bs.xpValue = 10;
    }
}