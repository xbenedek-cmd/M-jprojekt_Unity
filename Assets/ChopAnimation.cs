using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Animácia sekania dreva riešená skriptom (bez nahrávania klipu v okne Animation).
/// Skript hýbe kosťami postavy (Mixamo mená alebo Humanoid avatar): zdvih sekery, úder, náraz, návrat.
/// Pózu na začiatku berie z toho, ako máš kosti nastavené v scéne (tvoja počiatočná póza rúk).
///
/// Pohyby sa robia okolo osi "vpravo" modelu, takže fungujú bez ohľadu na lokálne osi kostí.
/// Ak sa postava nakláňa opačne (dozadu namiesto dopredu), zaškrtni "Flip Lean".
/// Ruky musia byť na začiatku predpažené dopredu (nie v T-póze), inak sa nebudú mať kam zdvihnúť.
/// </summary>
[DisallowMultipleComponent]
public class ChopAnimation : MonoBehaviour
{
    [Header("Ovládanie")]
    public bool autoStart = true;
    [Tooltip("Opakuje sekanie stále dokola")]
    public bool loop = true;
    [Tooltip("Dĺžka jedného zásahu v sekundách")]
    public float cycleSeconds = 1.6f;
    [Tooltip("Oddych medzi zásahmi")]
    public float pauseBetween = 0.4f;

    [Header("Model")]
    [Tooltip("Koreň modelu (určuje, kde je dopredu, hore a vpravo). Prázdne = tento objekt.")]
    public Transform modelRoot;
    [Tooltip("Zaškrtni, ak sa postava nakláňa dozadu namiesto dopredu")]
    public bool flipLean = false;
    [Tooltip("Vypne Animator na modeli, aby neprepisoval pózu")]
    public bool disableAnimator = true;

    [Header("Uhly pohybu (stupne)")]
    public float armAngle = 100f;
    public float forearmAngle = 45f;
    public float leanAngle = 38f;
    [Range(0f, 1f)] public float headCompensation = 0.4f;
    public float kneeAngle = 25f;
    [Tooltip("Ako hlboko klesne panva pri údere (v metroch)")]
    public float dropMeters = 0.12f;

    [Header("Priebeh (os X = čas 0 až 1)")]
    [Tooltip("-1 = ruky zdvihnuté nad hlavu, 0 = základná póza, +1 = ruky ďaleko dolu")]
    public AnimationCurve armsCurve = Curve(0f, 0f, 0.10f, 0f, 0.46f, -1f, 0.52f, -1f, 0.60f, 0.7f, 0.68f, 0.7f, 0.85f, 0.2f, 1f, 0f);
    [Tooltip("-1 = trup dozadu, +1 = trup dopredu")]
    public AnimationCurve leanCurve = Curve(0f, 0f, 0.10f, 0f, 0.46f, -0.6f, 0.52f, -0.6f, 0.60f, 1f, 0.68f, 1f, 0.85f, 0.3f, 1f, 0f);
    [Tooltip("0 = stojí, 1 = podrepnutý")]
    public AnimationCurve dropCurve = Curve(0f, 0f, 0.46f, 0f, 0.60f, 1f, 0.68f, 1f, 0.85f, 0.2f, 1f, 0f);

    [Header("Náraz do stromu")]
    [Range(0.1f, 0.95f)] public float hitTime = 0.60f;
    public UnityEvent onHit;
    public AudioClip hitSound;
    [Range(0f, 1f)] public float hitVolume = 1f;
    public bool spawnImpactEffect = true;
    [Tooltip("Kde je strom. Prázdne = kúsok pred postavou.")]
    public Transform impactPoint;
    public float impactDistance = 1.3f;
    public Color impactColor = new Color(0.55f, 0.4f, 0.25f);

    [Header("Sekera")]
    [Tooltip("Vytvorí jednoduchú sekeru v pravej ruke, ak nemáš vlastnú")]
    public bool createAxe = true;
    [Tooltip("Vlastný model sekery. Ak je potomkom pravej ruky, nechá sa tak; inak ho skript drží v ruke.")]
    public GameObject axeOverride;
    public float axeLength = 0.9f;
    [Tooltip("Sklon násady dopredu v základnej póze (stupne)")]
    public float handleTilt = 25f;
    [Range(0f, 1f)] public float gripBlend = 0.6f;

    [Header("Jednoručné sekanie")]
    [Tooltip("Sekanie jednou (pravou) rukou: zdvih spoza hlavy a úder šikmo dole; ľavá ruka sa nehýbe")]
    public bool oneHanded = true;
    [Tooltip("Pokrčenie lakťa, keď je ruka spoza hlavy")]
    public float behindHeadElbow = 75f;
    [Tooltip("Mierny ohyb lakťa pri údere (stupne)")]
    public float strikeElbow = 28f;
    [Tooltip("Stály jemný ohyb lakťa (stupne)")]
    public float restElbow = 8f;
    [Tooltip("Ako vysoko ide ruka nad predpaženie (stupne); viac = viac dozadu k hlave")]
    public float oneHandRaise = 55f;
    [Tooltip("Ako hlboko ide ruka pri údere pod predpaženie (stupne)")]
    public float oneHandDown = 95f;
    [Tooltip("Rameno von do strany pri zdvihu (stupne) – ruka ide vedľa hlavy, nie k nej")]
    public float tiltOut = 40f;
    [Tooltip("Ruka naprieč telom k ľavému boku pri údere (stupne) – šikmý úder")]
    public float tiltAcross = 75f;
    [Tooltip("Natočenie ramien okolo zvislej osi pri údere")]
    public float twistAngle = 20f;

    [Header("Použitie z kódu (hráč / drevorubači)")]
    [Tooltip("Pri každom štarte sekania vezme aktuálnu pózu ako základ a predpaží ruky (pre postavy s animátorom, ktorých ruky visia dolu)")]
    public bool captureOnStart = false;
    public float armsForwardDegrees = 85f;
    [Tooltip("Nevypne panvu a nohy: chôdza / státie ide ďalej z animátora")]
    public bool trackLowerBody = true;

    /// <summary>Zavolá sa pri každom údere do stromu.</summary>
    public event Action ChopHit;

    public bool IsChopping => playing;

    private class Bone
    {
        public Transform t;
        public Quaternion baseRot;
        public Vector3 basePos;
    }

    private readonly List<Bone> bones = new List<Bone>();
    private Bone hips, spine, spine1, spine2, neck, head;
    private Bone lShoulder, lArm, lFore, lHand, rShoulder, rArm, rFore, rHand;
    private Bone lUpLeg, lLeg, lFoot, rUpLeg, rLeg, rFoot;

    private Animator anim;
    private Transform axe;
    private bool axeFollow;
    private bool axeCreated;
    private Vector3 axeLocalPos;
    private Quaternion axeLocalRot = Quaternion.identity;

    private bool playing;
    private float time;
    private float prevT;
    private int remaining = -1;

    // ---------- Pomocné ----------
    static AnimationCurve Curve(params float[] timeValuePairs)
    {
        var c = new AnimationCurve();
        for (int i = 0; i + 1 < timeValuePairs.Length; i += 2)
            c.AddKey(timeValuePairs[i], timeValuePairs[i + 1]);
        for (int i = 0; i < c.keys.Length; i++)
            c.SmoothTangents(i, 0f);
        return c;
    }

    float Sign => flipLean ? -1f : 1f;

    // ---------- Životný cyklus ----------
    private static bool deferInit;   // Attach(): kosti sa nájdu až po nastavení polí, nie hneď v Awake
    private bool inited;

    /// <summary>
    /// Pridá sekanie na model hráča / drevorubača bez Animator Controllera a bez vypnutia animátora: animátor ďalej hýbe nohami a panvou,
    /// skript len pri sekaní prepíše trup a ruky. Pózu berie z aktuálneho záberu pri každom štarte.
    /// </summary>
    public static ChopAnimation Attach(GameObject model, bool createAxe, float cycleSeconds, float pause)
    {
        ChopAnimation existing = model.GetComponent<ChopAnimation>();
        if (existing != null) return existing;

        deferInit = true;
        ChopAnimation c = model.AddComponent<ChopAnimation>();
        deferInit = false;

        c.autoStart = false;
        c.loop = false;
        c.disableAnimator = false;
        c.captureOnStart = true;
        c.trackLowerBody = false;
        c.dropMeters = 0f;
        c.spawnImpactEffect = false;
        c.createAxe = createAxe;
        c.cycleSeconds = cycleSeconds;
        c.pauseBetween = pause;
        return c;
    }

    void Awake()
    {
        if (modelRoot == null) modelRoot = transform;
        anim = GetComponentInChildren<Animator>();
        if (!deferInit) EnsureInit();
    }

    void EnsureInit()
    {
        if (inited) return;
        inited = true;

        // Základná póza sa zapamätá ešte pred vypnutím Animatora
        hips = trackLowerBody ? Track("Hips", HumanBodyBones.Hips) : null;
        spine = Track("Spine", HumanBodyBones.Spine);
        spine1 = Track("Spine1", HumanBodyBones.Chest);
        spine2 = Track("Spine2", HumanBodyBones.UpperChest);
        neck = Track("Neck", HumanBodyBones.Neck);
        head = Track("Head", HumanBodyBones.Head);

        lShoulder = Track("LeftShoulder", HumanBodyBones.LeftShoulder);
        lArm = Track("LeftArm", HumanBodyBones.LeftUpperArm);
        lFore = Track("LeftForeArm", HumanBodyBones.LeftLowerArm);
        lHand = Track("LeftHand", HumanBodyBones.LeftHand);
        rShoulder = Track("RightShoulder", HumanBodyBones.RightShoulder);
        rArm = Track("RightArm", HumanBodyBones.RightUpperArm);
        rFore = Track("RightForeArm", HumanBodyBones.RightLowerArm);
        rHand = Track("RightHand", HumanBodyBones.RightHand);

        if (trackLowerBody)
        {
            lUpLeg = Track("LeftUpLeg", HumanBodyBones.LeftUpperLeg);
            lLeg = Track("LeftLeg", HumanBodyBones.LeftLowerLeg);
            lFoot = Track("LeftFoot", HumanBodyBones.LeftFoot);
            rUpLeg = Track("RightUpLeg", HumanBodyBones.RightUpperLeg);
            rLeg = Track("RightLeg", HumanBodyBones.RightLowerLeg);
            rFoot = Track("RightFoot", HumanBodyBones.RightFoot);
        }

        if (rArm == null || lArm == null)
            Debug.LogWarning("ChopAnimation: nenašiel som kosti (LeftArm, RightArm...). Skontroluj, či je skript na modeli s Mixamo kosťami.");

        if (!captureOnStart) SetupAxe();

        if (disableAnimator && anim != null) anim.enabled = false;
    }

    // Aktuálna póza (z animátora) sa stane základom sekania; ruky sa predpažia, lebo krivky počítajú s rukami dopredu
    void CaptureBaseline()
    {
        foreach (Bone b in bones)
        {
            b.baseRot = b.t.localRotation;
            b.basePos = b.t.localPosition;
        }

        Vector3 axis = modelRoot.right * Sign;
        if (!oneHanded) Rot(lArm, -armsForwardDegrees, axis);
        Rot(rArm, -armsForwardDegrees, axis);
        if (lArm != null && !oneHanded) lArm.baseRot = lArm.t.localRotation;
        if (rArm != null) rArm.baseRot = rArm.t.localRotation;

        // Sekera sa k ruke priradí v predpaženej póze (násada nahor)
        if (axe != null && axeCreated) { Destroy(axe.gameObject); axe = null; axeCreated = false; axeFollow = false; }
        SetupAxe();
    }

    void Start()
    {
        if (autoStart) StartChopping();
    }

    void OnDestroy()
    {
        if (axeCreated && axe != null) Destroy(axe.gameObject);
    }

    // ---------- Ovládanie z iných skriptov ----------
    /// <summary>Spustí sekanie. swings = 0 znamená podľa nastavenia Loop (nekonečne alebo raz).</summary>
    public void StartChopping(int swings = 0)
    {
        EnsureInit();
        if (captureOnStart && !playing) CaptureBaseline();

        playing = true;
        time = 0f;
        prevT = 0f;
        remaining = swings > 0 ? swings : (loop ? -1 : 1);
        if (axe != null) axe.gameObject.SetActive(true);
    }

    public void ChopOnce() => StartChopping(1);

    public void StopChopping()
    {
        playing = false;
        if (!captureOnStart) RestoreBaseline();   // pri snímanej póze preberá kosti animátor sám
        if (captureOnStart && axe != null) axe.gameObject.SetActive(false);
    }

    // ---------- Kosti ----------
    Bone Track(string token, HumanBodyBones humanBone)
    {
        Transform t = FindBone(token);
        if (t == null && anim != null && anim.isHuman) t = anim.GetBoneTransform(humanBone);
        if (t == null) return null;

        foreach (Bone existing in bones)
            if (existing.t == t) return existing;

        var b = new Bone { t = t, baseRot = t.localRotation, basePos = t.localPosition };
        bones.Add(b);
        return b;
    }

    // Hľadá kosť podľa mena bez prefixu ("mixamorig:Hips" -> "Hips")
    Transform FindBone(string token)
    {
        foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int i = n.LastIndexOf(':');
            if (i >= 0) n = n.Substring(i + 1);

            if (string.Equals(n, token, StringComparison.OrdinalIgnoreCase)) return t;
        }
        return null;
    }

    void RestoreBaseline()
    {
        foreach (Bone b in bones)
        {
            b.t.localRotation = b.baseRot;
            b.t.localPosition = b.basePos;
        }
    }

    // ---------- Sekera ----------
    void SetupAxe()
    {
        if (rHand == null) return;

        if (axeOverride != null)
        {
            axe = axeOverride.transform;
            if (axe.IsChildOf(rHand.t)) return;   // už je v ruke, nechaj ju tak
        }
        else if (createAxe)
        {
            axe = BuildAxe();
            axeCreated = true;
        }

        if (axe == null) return;

        // Miesto úchopu: medzi zápästím a prstami
        Vector3 grip = rHand.t.position;
        if (rHand.t.childCount > 0)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < rHand.t.childCount; i++) sum += rHand.t.GetChild(i).position;
            grip = Vector3.Lerp(grip, sum / rHand.t.childCount, gripBlend);
        }

        // Základné natočenie: násada nahor a trochu dopredu, ostrie dopredu
        Vector3 rightDir = modelRoot.right * Sign;
        Vector3 handleDir = Quaternion.AngleAxis(handleTilt, rightDir) * modelRoot.up;
        Vector3 bladeDir = Vector3.Cross(rightDir, handleDir);
        Quaternion desired = Quaternion.LookRotation(bladeDir, handleDir);

        axeLocalRot = Quaternion.Inverse(rHand.t.rotation) * desired;
        axeLocalPos = rHand.t.InverseTransformPoint(grip);
        axeFollow = true;
    }

    Transform BuildAxe()
    {
        float L = Mathf.Max(0.3f, axeLength);
        var root = new GameObject("ChopAxe");

        // Násada (počiatok objektu je v mieste úchopu)
        GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        handle.name = "Handle";
        Destroy(handle.GetComponent<Collider>());
        handle.transform.SetParent(root.transform, false);
        handle.transform.localPosition = new Vector3(0f, 0.25f * L, 0f);
        handle.transform.localScale = new Vector3(0.035f, L * 0.5f, 0.035f);
        handle.GetComponent<Renderer>().material.color = new Color(0.45f, 0.3f, 0.16f);

        // Hlava sekery
        float h = L * 0.22f;
        GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blade.name = "Blade";
        Destroy(blade.GetComponent<Collider>());
        blade.transform.SetParent(root.transform, false);
        blade.transform.localPosition = new Vector3(0f, 0.62f * L, h * 0.6f);
        blade.transform.localScale = new Vector3(0.02f, h, h * 1.2f);
        blade.GetComponent<Renderer>().material.color = new Color(0.62f, 0.64f, 0.68f);

        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "HeadBlock";
        Destroy(block.GetComponent<Collider>());
        block.transform.SetParent(root.transform, false);
        block.transform.localPosition = new Vector3(0f, 0.62f * L, -0.02f);
        block.transform.localScale = new Vector3(0.05f, h, 0.07f);
        block.GetComponent<Renderer>().material.color = new Color(0.35f, 0.37f, 0.4f);

        return root.transform;
    }

    // ---------- Slučka animácie ----------
    void LateUpdate()
    {
        if (playing) Tick();
        FollowAxe();
    }

    void Tick()
    {
        float total = Mathf.Max(0.2f, cycleSeconds) + Mathf.Max(0f, pauseBetween);
        time += Time.deltaTime;

        if (time >= total)
        {
            time -= total;
            prevT = 0f;

            if (remaining > 0)
            {
                remaining--;
                if (remaining == 0)
                {
                    StopChopping();
                    return;
                }
            }
        }

        // Počas oddychu stojí v základnej póze
        float t = time <= cycleSeconds ? time / Mathf.Max(0.2f, cycleSeconds) : 1f;

        if (prevT < hitTime && t >= hitTime) DoHit();
        prevT = t;

        Pose(t);
    }

    void Pose(float t)
    {
        float arms = armsCurve.Evaluate(t);
        float lean = leanCurve.Evaluate(t);
        float drop = dropCurve.Evaluate(t);
        Vector3 axis = modelRoot.right * Sign;

        // Panva: klesne
        if (hips != null)
        {
            Vector3 worldOffset = -modelRoot.up * (drop * dropMeters);
            Transform p = hips.t.parent;
            Vector3 local = p != null ? p.InverseTransformVector(worldOffset) : worldOffset;
            hips.t.localPosition = hips.basePos + local;
            hips.t.localRotation = hips.baseRot;
        }

        // Trup: rozdelený medzi tri stavce, hlava sa vyrovnáva
        float leanDeg = lean * leanAngle;
        Rot(spine, leanDeg * 0.30f, axis);
        Rot(spine1, leanDeg * 0.35f, axis);
        Rot(spine2, leanDeg * 0.35f, axis);
        Rot(neck, -leanDeg * headCompensation * 0.5f, axis);
        Rot(head, -leanDeg * headCompensation * 0.5f, axis);

        // Trup: jednoručný zásah sa podporí natočením ramien (navíjanie doprava, úder naprieč doľava)
        if (oneHanded)
        {
            Quaternion twist = Quaternion.AngleAxis(-lean * twistAngle, modelRoot.up);
            RotQ(spine1, twist * Quaternion.AngleAxis(leanDeg * 0.35f, axis));   // zahŕňa aj predklon
            RotQ(spine2, twist * Quaternion.AngleAxis(leanDeg * 0.35f, axis));
        }

        // Ruky: záporný uhol zdvíha dopredu predpažené ruky nad hlavu
        float raised = Mathf.Clamp01(-arms);
        float armDeg = oneHanded ? (arms < 0f ? arms * oneHandRaise : arms * oneHandDown) : arms * armAngle;
        float foreDeg = -raised * (oneHanded ? behindHeadElbow : forearmAngle);
        if (oneHanded) foreDeg -= strikeElbow * Mathf.Clamp01(arms) + restElbow;   // mierny ohyb v lakti aj pri údere a v pokoji

        Rot(rShoulder, 0f, axis);
        if (oneHanded)
        {
            // Pravá ruka: zdvih spoza hlavy (rameno von, lakeť pokrčený), úder šikmo dole naprieč telom; ľavá ruka ostáva v pokoji z animátora
            float tilt = tiltAcross * Mathf.Clamp01(arms) - tiltOut * raised;
            RotQ(rArm, Quaternion.AngleAxis(tilt * Sign, modelRoot.forward) * Quaternion.AngleAxis(armDeg, axis));
            Rot(rFore, foreDeg, axis);
            Rot(rHand, 0f, axis);
        }
        else
        {
            Rot(lShoulder, 0f, axis);
            Rot(lArm, armDeg, axis);
            Rot(rArm, armDeg, axis);
            Rot(lFore, foreDeg, axis);
            Rot(rFore, foreDeg, axis);
            Rot(lHand, 0f, axis);
            Rot(rHand, 0f, axis);
        }

        // Nohy: podrepnutie pri údere
        float k = drop * kneeAngle;
        Rot(lUpLeg, -k * 0.6f, axis);
        Rot(rUpLeg, -k * 0.6f, axis);
        Rot(lLeg, k * 1.2f, axis);
        Rot(rLeg, k * 1.2f, axis);
        Rot(lFoot, -k * 0.6f, axis);
        Rot(rFoot, -k * 0.6f, axis);
    }

    // Otočí kosť o uhol okolo osi vpravo (vo svete), na vrch jej základnej pózy.
    // Rodič už má nový pohyb, takže sa pohyby správne sčítavajú pozdĺž reťaze kostí.
    void Rot(Bone b, float degrees, Vector3 worldAxis)
    {
        if (b == null) return;

        Quaternion delta = Quaternion.AngleAxis(degrees, worldAxis);
        Transform parent = b.t.parent;
        Quaternion pr = parent != null ? parent.rotation : Quaternion.identity;

        b.t.localRotation = (Quaternion.Inverse(pr) * delta * pr) * b.baseRot;
    }

    // Rovnaké ako Rot, ale s hotovou svetovou rotáciou (viac osí naraz)
    void RotQ(Bone b, Quaternion deltaWorld)
    {
        if (b == null) return;

        Transform parent = b.t.parent;
        Quaternion pr = parent != null ? parent.rotation : Quaternion.identity;
        b.t.localRotation = (Quaternion.Inverse(pr) * deltaWorld * pr) * b.baseRot;
    }

    void FollowAxe()
    {
        if (!axeFollow || axe == null || rHand == null) return;
        axe.SetPositionAndRotation(rHand.t.TransformPoint(axeLocalPos), rHand.t.rotation * axeLocalRot);
    }

    // ---------- Náraz ----------
    void DoHit()
    {
        Vector3 point = impactPoint != null
            ? impactPoint.position
            : modelRoot.position + modelRoot.forward * (impactDistance * Sign);

        if (spawnImpactEffect)
            Vfx.Disc(point + Vector3.up * 0.05f, 0.8f, impactColor, 0.3f);

        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, point, hitVolume);

        onHit?.Invoke();
        ChopHit?.Invoke();
    }

    // ---------- Pohodlie v editore ----------
    [ContextMenu("Spustiť sekanie (len počas hry)")]
    void MenuStart()
    {
        if (Application.isPlaying) StartChopping();
        else Debug.Log("Sekanie sa dá vyskúšať až po spustení hry (Play).");
    }

    [ContextMenu("Zastaviť sekanie (len počas hry)")]
    void MenuStop()
    {
        if (Application.isPlaying) StopChopping();
    }
}