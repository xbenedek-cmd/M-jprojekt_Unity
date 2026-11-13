using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public enum NPCKind { Civilian, LesserDemon, HigherDemon, MidDemon }

[RequireComponent(typeof(CapsuleCollider))]
public class NPCPerception : MonoBehaviour
{
    [Header("Skutočná identita")]
    public NPCKind kind = NPCKind.Civilian;
    public FactionId faction = FactionId.None;

    [Header("Vzhľad (voliteľné - ak je prázdne, vytvorí sa prototyp)")]
    public GameObject humanVisual;
    public GameObject monsterVisual;
    public GameObject higherDemonBodyOverride;
    [Tooltip("Natočenie modelu démona okolo osi Y (model z GLB je po načítaní otočený o 90° doľava)")]
    public float demonModelYawOffset = 90f;
    [Tooltip("PBR materiál démonov (textúra z Demon_rigged). Ak je prázdny, použije sa materiál z modelu.")]
    public Material demonMaterial;
    [Tooltip("Vlastný model (boss) si nechá svoje materiály a textúry: spoločný materiál démonov sa naň nepoužije")]
    public bool keepModelMaterials;
    [Tooltip("Veľkosť démonickej podoby voči ľudskej (boss je väčší ako človek, ale maska cez deň má normálnu výšku)")]
    public float demonVisualScale = 1f;
    [Tooltip("Sila podfarbenia podľa úrovne démona (0 = čistá textúra, 1 = plná farba úrovne)")]
    [Range(0f, 1f)] public float demonTint = 0.35f;
    private MaterialPropertyBlock tintBlock;
    [Tooltip("Sada animácií (Idle / Run / Attack / premena) pre všetkých démonov. V editore sa priradí sama.")]
    public DemonAnimationSet demonAnimationSet;
    [Tooltip("Dĺžka premeny vyššieho démona, ak sada nemá klip premeny (s)")]
    public float transformSeconds = 1.2f;
    public Color humanColor = new Color(0.35f, 0.55f, 0.9f);
    public Color monsterColor = new Color(0.45f, 0.05f, 0.25f);

    [Header("Debug")]
    public bool showTrueIdentity = false;
    /// <summary>Ladiaci popisky nad NPC ([Civilista], frakcia...). Zapnúť len na ladenie; predvolene vypnuté (zhoršovali čitateľnosť).</summary>
    public static bool DebugLabels;

    public static readonly List<NPCPerception> All = new List<NPCPerception>();

    public bool ShowsMonster { get; private set; }
    public bool Revealed { get; private set; }
    /// <summary>Hráč ju už raz skúmal (T): frakcia je odhalená a ďalšie skúmanie nie je možné.</summary>
    public bool IsScanned { get; private set; }

    public void MarkScanned() => IsScanned = true;

    /// <summary>Vyšší démon odhodil masku po úspešnom skúmaní (T). Vojaci ho tým ešte nevidia ako hrozbu (to rieši Reveal).</summary>
    public bool Unmasked { get; private set; }

    public void Unmask()
    {
        if (kind != NPCKind.HigherDemon || Unmasked) return;
        Unmasked = true;
        Refresh();

        // Premena: model sa plynulo rozbehne (klip premeny alebo nábeh mierky) a démon počas nej nebojuje ani sa nepotuluje
        float length = transformSeconds;
        DemonModelAnimator anim = monsterVisual != null ? monsterVisual.GetComponent<DemonModelAnimator>() : null;
        if (anim != null) length = anim.PlayTransformation(transformSeconds);

        DemonEnemy enemy = GetComponent<DemonEnemy>();
        if (enemy != null) enemy.Stun(length);

        NPCWander wander = GetComponent<NPCWander>();
        if (wander != null) wander.Pause(length);
    }

    // Aký vzhľad je práve zapnutý: -1 = nič, 0 = človek, 1 = démon (prepína sa len pri zmene)
    private int visualState = -1;

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);   // Contains chráni pred dvojitým pridaním po SetActive
    }

    void OnDisable() => All.Remove(this);
    public bool IsResistance => FactionData.IsResistance(faction);
    public bool IsNeutralCivilian => kind == NPCKind.Civilian && faction == FactionId.None;

    private BloodSource source;
    private Damageable damageable;

    // OPRAVA: Samostatné referencie pre ľudské a démonické končatiny
    private Transform[] humanLegs = new Transform[2];
    private Transform[] humanArms = new Transform[2];
    private Transform[] monsterLegs = new Transform[2];
    private Transform[] monsterArms = new Transform[2];

    private float walkCycle;
    private Vector3 lastPos;
    private Camera cachedCam;   // len pre ladiace popisky

    void Awake()
    {
        source = GetComponent<BloodSource>();
        damageable = GetComponent<Damageable>();

        if (humanVisual == null)
        {
            AlignCapsuleToFeet();
            humanVisual = CreateHumanVisual();
        }

        // Démoni používajú model output_mesh_pbr; provizórne kocky len ako záloha, keď model chýba
        bool isDemon = kind == NPCKind.LesserDemon || kind == NPCKind.MidDemon || kind == NPCKind.HigherDemon;
        if (isDemon && monsterVisual == null && higherDemonBodyOverride != null)
            monsterVisual = CreateDemonModelVisual();
        if (monsterVisual == null) monsterVisual = CreateMonsterVisual();
    }

    // Proceduálna postava má pivot na päte (vizuál je o 1 m vyššie). Kapsula so stredom v 0 ju pri zarovnaní
    // na zem nechala vznášať sa o výšku kapsuly - preto sa stred kapsuly dá na polovicu výšky ako u ostatných NPC.
    void AlignCapsuleToFeet()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule == null || capsule.center.y >= capsule.height * 0.25f) return;
        capsule.center = new Vector3(capsule.center.x, capsule.height * 0.5f, capsule.center.z);
    }

    GameObject CreateDemonModelVisual()
    {
        GameObject instance = Instantiate(higherDemonBodyOverride, transform);
        instance.name = "DemonBodyVisual";
        instance.transform.localPosition = Vector3.zero;
        // Mixamo model je otočený správne (+Z dopredu); 90° korekcia patrí len statickému Tripo meshu
        bool mixamo = HasMixamoRig(instance.transform) || HasHumanoidRig(instance.transform);
        instance.transform.localRotation = Quaternion.Euler(0f, mixamo ? 0f : demonModelYawOffset, 0f);
        instance.transform.localScale = Vector3.one;

        StripNonVisual(instance);

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"{name}: model démona nemá žiadny Renderer, použijem provizórny vzhľad.", this);
            Destroy(instance);
            return null;
        }

        FitModelToCapsule(transform, instance.transform, renderers, demonVisualScale);
        FixMaterials(renderers);
        instance.AddComponent<DemonModelAnimator>().Initialize(demonAnimationSet);
        return instance;
    }

    /// <summary>
    /// Zo spawnutého modelu odstráni všetko okrem vzhľadu: collidery (kolízie rieši kapsula postavy),
    /// kamery, listenery a svetlá (FBX ich nesie z importu).
    /// </summary>
    public static void StripNonVisual(GameObject instance)
    {
        foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
            DestroyImmediate(c);

        foreach (Camera cam in instance.GetComponentsInChildren<Camera>(true))
        {
            AudioListener listener = cam.GetComponent<AudioListener>();
            if (listener != null) DestroyImmediate(listener);
            DestroyImmediate(cam);
        }
        foreach (AudioListener listener in instance.GetComponentsInChildren<AudioListener>(true))
            DestroyImmediate(listener);
        foreach (Light light in instance.GetComponentsInChildren<Light>(true))
            DestroyImmediate(light);
    }

    public static bool HasMixamoRig(Transform root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.Length >= 9 && string.CompareOrdinal(n, 0, "mixamorig", 0, 9) == 0) return true;
        }
        return false;
    }

    /// <summary>Model s Humanoid avatarom (rig z Blendera a pod.): Mixamo klipy sa naň prenášajú retargetingom.</summary>
    public static bool HasHumanoidRig(Transform root)
    {
        Animator a = root.GetComponentInChildren<Animator>(true);
        return a != null && a.avatar != null && a.avatar.isHuman;
    }

    // AI model má neznámu mierku a pivot (často v strede): zmenší/zväčší sa na výšku kapsuly a postaví na päty
    public static void FitModelToCapsule(Transform host, Transform model, Renderer[] renderers, float heightMultiplier = 1f)
    {
        CapsuleCollider capsule = host.GetComponent<CapsuleCollider>();
        float capsuleHeight = capsule != null ? capsule.height * host.lossyScale.y : 2f;
        float height = capsuleHeight * Mathf.Max(0.1f, heightMultiplier);
        float feetY = capsule != null
            ? host.TransformPoint(capsule.center).y - capsuleHeight * 0.5f
            : host.position.y;

        Bounds b = CombinedBounds(renderers);
        if (b.size.y > 0.0001f)
        {
            model.localScale = Vector3.one * (height / b.size.y / Mathf.Max(0.0001f, host.lossyScale.y));
            b = CombinedBounds(renderers);
        }

        Vector3 shift = new Vector3(host.position.x - b.center.x, feetY - b.min.y, host.position.z - b.center.z);
        model.position += shift;
    }

    static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    // Chýbajúci materiál alebo shader (ružová/neviditeľná) nahradí zálohou; skinned mesh sa nesmie orezať kamerou
    void FixMaterials(Renderer[] renderers)
    {
        Material fallback = null;

        foreach (Renderer r in renderers)
        {
            r.enabled = true;

            SkinnedMeshRenderer skinned = r as SkinnedMeshRenderer;
            if (skinned != null) skinned.updateWhenOffscreen = true;

            // Jednotný PBR materiál démonov (textúra z modelu); farba úrovne ho len jemne podfarbí
            if (demonMaterial != null)
            {
                Material[] forced = r.sharedMaterials;
                for (int i = 0; i < forced.Length; i++) forced[i] = demonMaterial;
                r.sharedMaterials = forced;

                if (tintBlock == null) tintBlock = new MaterialPropertyBlock();
                tintBlock.SetColor("_Color", Color.Lerp(Color.white, monsterColor, demonTint));
                r.SetPropertyBlock(tintBlock);
                continue;
            }

            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].shader != null && mats[i].shader.isSupported) continue;

                if (fallback == null)
                {
                    fallback = new Material(Shader.Find("Standard"));
                    fallback.color = monsterColor;
                }
                mats[i] = fallback;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

#if UNITY_EDITOR
    // GLB (glTFast) je primárny; FBX output_mesh_pbr sa Unity-u nedarilo importovať
    public const string DemonModelPath = "Assets/Models/Demon_body.glb";
    public const string DemonModelFallbackPath = "Assets/Models/output_mesh_pbr.fbx";

    // Mixamo model s kostrou (mixamorig:*), ak existuje, má prednosť pred statickým Tripo GLB
    public const string DemonModelRiggedPath = "Assets/Models/Demon_rigged.fbx";

    public static GameObject LoadDemonModelAsset()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(DemonModelRiggedPath);
        if (model == null) model = AssetDatabase.LoadAssetAtPath<GameObject>(DemonModelPath);
        return model != null ? model : AssetDatabase.LoadAssetAtPath<GameObject>(DemonModelFallbackPath);
    }

    // V editore sa model priradí sám, takže referencia je uložená v scéne a funguje aj v builde
    void Reset() => AssignDemonModel();
    void OnValidate() => AssignDemonModel();

    public const string DemonAnimationSetPath = "Assets/Animations/DemonAnimationSet.asset";
    public const string DemonMaterialPath = "Assets/Materials/DemonMaterial.mat";

    void AssignDemonModel()
    {
        if (kind == NPCKind.Civilian) return;
        if (higherDemonBodyOverride == null) higherDemonBodyOverride = LoadDemonModelAsset();
        if (demonAnimationSet == null) demonAnimationSet = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(DemonAnimationSetPath);
        if (demonMaterial == null && !keepModelMaterials) demonMaterial = AssetDatabase.LoadAssetAtPath<Material>(DemonMaterialPath);
    }
#endif

    public void Reveal()
    {
        Revealed = true;

        // Vyšší démon sa vždy najprv premení (animácia), až potom bojuje - rovnaká cesta ako po skúmaní
        if (kind == NPCKind.HigherDemon && !Unmasked)
        {
            Unmask();
            return;
        }
        Refresh();
    }

    void Start()
    {
        Refresh();
        lastPos = transform.position;
        if (GetComponent<NPCGrounding>() == null) gameObject.AddComponent<NPCGrounding>();

        // Civilisti sa vedia brániť a utekať (CivilianAI sa pridá sám)
        // (vojaci majú vlastnú AI - SoldierAI)
        bool isSoldier = GetComponent<SoldierAI>() != null;

        if (kind == NPCKind.Civilian && !isSoldier && GetComponent<Damageable>() != null && GetComponent<CivilianAI>() == null)
            gameObject.AddComponent<CivilianAI>();

        // Civilisti si všímajú vyčnievajúceho hráča (nesprávne oblečenie, démon vo vojenskej zóne)
        if (kind == NPCKind.Civilian && !isSoldier && GetComponent<CivilianAI>() != null && GetComponent<SuspicionWatcher>() == null)
            gameObject.AddComponent<SuspicionWatcher>();
    }

    void Refresh()
    {
        // Nižší a strední démon je vždy démon; vyšší sa maskuje za človeka, kým ho hráč neskúma (T) alebo neodhalí
        switch (kind)
        {
            case NPCKind.LesserDemon:
            case NPCKind.MidDemon: ShowsMonster = true; break;
            case NPCKind.HigherDemon: ShowsMonster = Revealed || Unmasked; break;
            default: ShowsMonster = false; break;
        }

        // Vždy je zapnutý práve jeden vzhľad a prepne sa len pri zmene (žiadne dva modely naraz, žiadne blikanie)
        int state = ShowsMonster ? 1 : 0;
        if (state == visualState) return;
        visualState = state;

        if (humanVisual != null) humanVisual.SetActive(state == 0);
        if (monsterVisual != null) monsterVisual.SetActive(state == 1);
    }

    // ----------------------------------------------------------------------
    // GENERÁTOR ĽUDSKEJ POSTAVY (Hlava, Telo, Ruky, Nohy)
    // ----------------------------------------------------------------------
    GameObject CreateHumanVisual()
    {
        GameObject root = new GameObject("HumanVisual");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(0f, 1f, 0f);

        Material bodyMat = new Material(Shader.Find("Standard"));
        bodyMat.color = humanColor;

        Material skinMat = new Material(Shader.Find("Standard"));
        skinMat.color = new Color(0.95f, 0.8f, 0.7f); // Farba pokožky

        // 1. TELO (Torso)
        GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(torso.GetComponent<Collider>());
        torso.transform.SetParent(root.transform, false);
        torso.transform.localPosition = new Vector3(0, 0.05f, 0);
        torso.transform.localScale = new Vector3(0.6f, 0.9f, 0.3f);
        torso.GetComponent<Renderer>().material = bodyMat;

        // 2. HLAVA (Head)
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0, 0.75f, 0);
        head.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        head.GetComponent<Renderer>().material = skinMat;

        // 3. RUKY (Arms)
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject armPivot = new GameObject("ArmPivot");
            armPivot.transform.SetParent(root.transform, false);
            armPivot.transform.localPosition = new Vector3(0.45f * i, 0.45f, 0);

            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(arm.GetComponent<Collider>());
            arm.transform.SetParent(armPivot.transform, false);
            arm.transform.localPosition = new Vector3(0, -0.45f, 0);
            arm.transform.localScale = new Vector3(0.2f, 0.9f, 0.2f);
            arm.GetComponent<Renderer>().material = bodyMat;

            if (i == -1) humanArms[0] = armPivot.transform;
            else humanArms[1] = armPivot.transform;
        }

        // 4. NOHY (Legs)
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject legPivot = new GameObject("LegPivot");
            legPivot.transform.SetParent(root.transform, false);
            legPivot.transform.localPosition = new Vector3(0.18f * i, -0.4f, 0);

            GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(leg.GetComponent<Collider>());
            leg.transform.SetParent(legPivot.transform, false);
            leg.transform.localPosition = new Vector3(0, -0.3f, 0);
            leg.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);
            leg.GetComponent<Renderer>().material = bodyMat;

            if (i == -1) humanLegs[0] = legPivot.transform;
            else humanLegs[1] = legPivot.transform;
        }

        return root;
    }

    // ----------------------------------------------------------------------
    // GENERÁTOR DÉMONA (Väčší, s rohmi, pazúrmi a očami)
    // ----------------------------------------------------------------------
    GameObject CreateMonsterVisual()
    {
        GameObject root = new GameObject("MonsterVisual");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(0f, 1f, 0f);

        Material bodyMat = new Material(Shader.Find("Standard"));
        bodyMat.color = monsterColor;

        Material darkMat = new Material(Shader.Find("Standard"));
        darkMat.color = new Color(0.1f, 0.05f, 0.05f);

        // 1. TELO
        GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(torso.GetComponent<Collider>());
        torso.transform.SetParent(root.transform, false);
        torso.transform.localPosition = new Vector3(0, 0.1f, 0);
        torso.transform.localScale = new Vector3(0.8f, 1.0f, 0.4f);
        torso.GetComponent<Renderer>().material = bodyMat;

        // 2. HLAVA
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0, 0.8f, 0);
        head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        head.GetComponent<Renderer>().material = bodyMat;

        // 3. ROHY A OČI
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject horn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(horn.GetComponent<Collider>());
            horn.transform.SetParent(head.transform, false);
            horn.transform.localPosition = new Vector3(0.3f * i, 0.6f, 0f);
            horn.transform.localRotation = Quaternion.Euler(0f, 0f, -20f * i);
            horn.transform.localScale = new Vector3(0.15f, 0.5f, 0.15f);
            horn.GetComponent<Renderer>().material = darkMat;

            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(eye.GetComponent<Collider>());
            eye.transform.SetParent(head.transform, false);
            eye.transform.localPosition = new Vector3(0.2f * i, 0.1f, 0.26f);
            eye.transform.localScale = Vector3.one * 0.15f;
            eye.GetComponent<Renderer>().material.color = new Color(1f, 0.9f, 0.1f);
        }

        // 4. RUKY S PAZÚRMI
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject armPivot = new GameObject("ArmPivot");
            armPivot.transform.SetParent(root.transform, false);
            armPivot.transform.localPosition = new Vector3(0.55f * i, 0.5f, 0);

            GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(arm.GetComponent<Collider>());
            arm.transform.SetParent(armPivot.transform, false);
            arm.transform.localPosition = new Vector3(0, -0.5f, 0);
            arm.transform.localScale = new Vector3(0.25f, 1.0f, 0.25f);
            arm.GetComponent<Renderer>().material = bodyMat;

            GameObject claw = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(claw.GetComponent<Collider>());
            claw.transform.SetParent(arm.transform, false);
            claw.transform.localPosition = new Vector3(0, -0.55f, 0.1f);
            claw.transform.localScale = new Vector3(0.3f, 0.2f, 0.4f);
            claw.GetComponent<Renderer>().material = darkMat;

            if (i == -1) monsterArms[0] = armPivot.transform;
            else monsterArms[1] = armPivot.transform;
        }

        // 5. NOHY
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject legPivot = new GameObject("LegPivot");
            legPivot.transform.SetParent(root.transform, false);
            legPivot.transform.localPosition = new Vector3(0.25f * i, -0.4f, 0);

            GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(leg.GetComponent<Collider>());
            leg.transform.SetParent(legPivot.transform, false);
            leg.transform.localPosition = new Vector3(0, -0.3f, 0);
            leg.transform.localScale = new Vector3(0.3f, 0.6f, 0.3f);
            leg.GetComponent<Renderer>().material = bodyMat;

            if (i == -1) monsterLegs[0] = legPivot.transform;
            else monsterLegs[1] = legPivot.transform;
        }

        return root;
    }

    // ----------------------------------------------------------------------
    // ANIMÁCIA CHÔDZE
    // ----------------------------------------------------------------------
    void Update()
    {
        // Končatiny existujú len pri provizórnych kockových postavách; modely s animátorom ich nemajú
        if (!ShowsMonster ? humanLegs[0] == null : monsterLegs[0] == null) return;

        Transform[] currentLegs = ShowsMonster ? monsterLegs : humanLegs;
        Transform[] currentArms = ShowsMonster ? monsterArms : humanArms;

        Vector3 pos = transform.position;
        float movedSqr = (pos - lastPos).sqrMagnitude;
        if (movedSqr > 0.005f * 0.005f && movedSqr < 0.25f) // Ak sa NPC hýbe (nie teleportácia)
        {
            walkCycle += Time.deltaTime * 12f;
            float swing = Mathf.Sin(walkCycle) * 35f;

            if (currentLegs[0] != null) currentLegs[0].localRotation = Quaternion.Euler(swing, 0, 0);
            if (currentLegs[1] != null) currentLegs[1].localRotation = Quaternion.Euler(-swing, 0, 0);
            if (currentArms[0] != null) currentArms[0].localRotation = Quaternion.Euler(-swing, 0, 0);
            if (currentArms[1] != null) currentArms[1].localRotation = Quaternion.Euler(swing, 0, 0);
        }
        else
        {
            // Ak stojí, vráti sa do pokojovej pozície
            walkCycle = 0;
            for (int i = 0; i < 2; i++)
            {
                if (currentLegs[i] != null) currentLegs[i].localRotation = Quaternion.Slerp(currentLegs[i].localRotation, Quaternion.identity, Time.deltaTime * 5f);
                if (currentArms[i] != null) currentArms[i].localRotation = Quaternion.Slerp(currentArms[i].localRotation, Quaternion.identity, Time.deltaTime * 5f);
            }
        }
        lastPos = pos;
    }

    // ----------------------------------------------------------------------
    // ZOBRAZENIE MIEN A DEBUG
    // ----------------------------------------------------------------------
    void OnDrawGizmos()
    {
        Gizmos.color = kind == NPCKind.Civilian ? Color.cyan
                     : (kind == NPCKind.LesserDemon || kind == NPCKind.MidDemon) ? Color.red : Color.magenta;
        Gizmos.DrawWireCube(transform.position, new Vector3(1f, 2f, 1f));
    }

    void OnGUI()
    {
        if (!DebugLabels || !showTrueIdentity) return;   // najlacnejšia kontrola prvá: OnGUI beží na každom NPC
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!NpcOverlay.Visible(transform.position)) return;

        if (cachedCam == null) cachedCam = Camera.main;
        Camera cam = cachedCam;
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.2f);
        if (p.z <= 0f) return;

        string text = kind == NPCKind.Civilian ? "[Civilista]"
                    : kind == NPCKind.LesserDemon ? "[Nižší démon]"
                    : kind == NPCKind.MidDemon ? "[Stredný démon]" : "[Vyšší démon]";

        string factionText = IsNeutralCivilian ? "Civilista - neutrálna" : FactionData.NameOf(faction);
        text += $"\n({factionText})";

        if ((source != null && source.IsDead) || (damageable != null && damageable.IsDead))
            text += " (mŕtvy)";

        float scale = Mathf.Clamp(Screen.height / 720f, 0.85f, 1.6f);

        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(16 * scale),
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };

        Rect labelRect = new Rect(p.x - 200f * scale, Screen.height - p.y - 20f, 400f * scale, 60f * scale);

        style.normal.textColor = Color.black;
        GUI.Label(new Rect(labelRect.x + 1, labelRect.y + 1, labelRect.width, labelRect.height), text, style);

        style.normal.textColor = Color.white;
        GUI.Label(labelRect, text, style);
    }
}