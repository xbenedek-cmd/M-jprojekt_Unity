using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Vzhľad hráča: nahradí provizórnu kocku modelom postavy (Assets/Models/Player/Hrac1.1/Hrac1.1.fbx) a pripojí rovnaký animačný systém
/// ako majú démoni (Idle, Start Walking, Run, Punching, Elbow Punch, kop, smrť - DemonAnimationSet + DemonModelAnimator).
/// Model s kostrou Mixamo (mixamorig:*) sa animuje klipmi; statický mesh dostane len procedurálny pohyb.
/// Kolízie a pohyb ostávajú na hráčovom koreni (Rigidbody + kapsula, PlayerMovement), model je len vizuál.
/// Vďaka PlayerBootstrap sa komponent pridá a nastaví aj sám, takže hráč je vždy postava, nie kocka.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerVisual : MonoBehaviour
{
    [Header("Model")]
    [Tooltip("Model hráča. Prázdne = ostane provizórna kocka.")]
    public GameObject model;
    [Tooltip("Animačná sada bez zbrane (Idle / Run / Punching / Elbow / Kop / smrť)")]
    public DemonAnimationSet animationSet;
    [Tooltip("Voliteľný materiál (textúra z modelu). Prázdne = materiál z modelu.")]
    public Material material;
    [Tooltip("Natočenie statického meshu bez kostry (Tripo); model s kostrou Mixamo sa neotáča")]
    public float staticModelYaw = 90f;

    [Header("Kapsula hráča (nahradí pôvodný BoxCollider)")]
    public float capsuleRadius = 0.35f;
    public float capsuleHeight = 1.8f;

    /// <summary>Kosť pravej ruky (miesto pre zbraň), alebo null, ak model nemá kostru.</summary>
    public Transform RightHand { get; private set; }
    /// <summary>Miesto na chrbte (zasunutý meč), alebo null.</summary>
    public Transform BackMount { get; private set; }
    public DemonModelAnimator ModelAnimator { get; private set; }
    /// <summary>Hráč má model postavy (nie provizórnu kocku).</summary>
    public bool HasModel => visual != null;
    public Transform VisualRoot => visual != null ? visual.transform : null;
    /// <summary>Všetky renderery tela (farba postavy sa nanáša na ne).</summary>
    public Renderer[] BodyRenderers { get; private set; }
    public bool IsRigged { get; private set; }

    private PlayerStats stats;
    private GameObject visual;

#if UNITY_EDITOR
    public const string ModelPath = "Assets/Models/Player/Hrac1.1/Hrac1.1.fbx";
    public const string UnarmedSetPath = "Assets/Animations/PlayerUnarmedSet.asset";
    public const string MaterialPath = "Assets/Materials/PlayerMaterial.mat";

    void Reset() => AutoAssign();
    void OnValidate() => AutoAssign();

    // V editore sa model, sada a materiál nájdu sami (hráč je postava, aj keď scéna ešte nebola nastavená menu)
    void AutoAssign()
    {
        if (model == null) model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (animationSet == null) animationSet = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(UnarmedSetPath);
        if (material == null) material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
    }
#endif

    void Awake()
    {
#if UNITY_EDITOR
        AutoAssign();
#endif
        if (model == null) return;

        stats = GetComponent<PlayerStats>();
        EnsureCapsule();
        BuildVisual();
        if (visual != null) HideProvisional();
    }

    void OnDestroy()
    {
        if (stats != null) stats.OnDied -= HandleDied;
    }

    // Hráč bol kocka s BoxCollider 1x1x1; humanoidný model potrebuje kapsulu (plynulý pohyb pri hranách a schodoch)
    void EnsureCapsule()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule != null) return;

        // Okamžite (nie na konci frame-u), aby ostatné skripty v Start() už videli len novú kapsulu
        foreach (Collider old in GetComponents<Collider>())
            DestroyImmediate(old);

        capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = capsuleRadius;
        capsule.height = capsuleHeight;
        capsule.center = Vector3.zero;   // pivot hráča je v strede tela, spodok kapsuly = päty
    }

    void BuildVisual()
    {
        visual = Instantiate(model, transform);
        LightMeshTable.Apply(visual);   // sieť s miliónmi trojuholníkov sa vymení za odľahčenú kópiu
        visual.name = "PlayerBodyVisual";
        visual.transform.localPosition = Vector3.zero;

        IsRigged = NPCPerception.HasMixamoRig(visual.transform);
        visual.transform.localRotation = Quaternion.Euler(0f, IsRigged ? 0f : staticModelYaw, 0f);
        visual.transform.localScale = Vector3.one;

        NPCPerception.StripNonVisual(visual);

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Debug.LogWarning("PlayerVisual: model hráča nemá žiadny Renderer, ostáva provizórna postava.", this);
            Destroy(visual);
            visual = null;
            return;
        }

        foreach (Renderer r in renderers)
        {
            r.enabled = true;

            SkinnedMeshRenderer skinned = r as SkinnedMeshRenderer;
            if (skinned != null) skinned.updateWhenOffscreen = true;   // kamera ho neorezáva

            if (material == null) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = material;
            r.sharedMaterials = mats;
        }

        NPCPerception.FitModelToCapsule(transform, visual.transform, renderers);
        RigFixup.FlattenArmatureWrapper(visual.transform);   // až po prispôsobení veľkosti (kým je Armature na mieste, sedia hranice rendererov)
        BodyRenderers = renderers;
        if (IsRigged) FindMounts();

        ModelAnimator = visual.AddComponent<DemonModelAnimator>();
        ModelAnimator.BindPlayer(GetComponent<PlayerCombat>());
        ModelAnimator.Initialize(animationSet);

        stats.OnDied += HandleDied;
    }

    // Kosť pravej ruky (zbraň v ruke), chrbát (zasunutý meč) a "Hand_R" pre PlayerCombat
    void FindMounts()
    {
        Transform spine = null, spine2 = null;
        foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (RightHand == null && n.EndsWith("RightHand", StringComparison.Ordinal)) RightHand = t;
            else if (n.EndsWith("Spine2", StringComparison.Ordinal)) spine2 = t;
            else if (n.EndsWith("Spine1", StringComparison.Ordinal)) spine = t;
        }
        BackMount = spine2 != null ? spine2 : spine;

        if (RightHand != null)
        {
            GameObject handObject = new GameObject("Hand_R");
            handObject.transform.SetParent(RightHand, false);
        }
    }

    // Provizórna kocka zostáva (iné skripty ju tónujú), len sa nevykresľuje
    void HideProvisional()
    {
        Renderer own = GetComponent<Renderer>();
        if (own != null) own.enabled = false;
    }

    /// <summary>Základná sada klipov hráča (bez zbrane).</summary>
    public DemonAnimationSet AnimationSet => animationSet;

    /// <summary>Prehrá klip smrti. Ak aktuálna sada (napr. s mečom) žiadny nemá, skúsi sa základná sada hráča.</summary>
    public bool TryPlayDeath()
    {
        if (ModelAnimator == null) return false;
        if (ModelAnimator.PlayDeath()) return true;

        if (animationSet != null)
        {
            ModelAnimator.SetAnimationSet(animationSet);
            if (ModelAnimator.PlayDeath()) return true;
        }
        return false;
    }

    void HandleDied()
    {
        if (TryPlayDeath()) return;
        if (ModelAnimator != null) ModelAnimator.ForceIdle();   // bez klipu smrti aspoň nohy nechodia
        if (visual == null) return;

        // Model bez animácie smrti (statický mesh): jednoducho sa položí na zem
        visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * visual.transform.localRotation;
        visual.transform.localPosition += Vector3.down * (capsuleHeight * 0.5f - capsuleRadius);
    }
}
