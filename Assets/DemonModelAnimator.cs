using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Animuje model démona (rovnaký systém pre nižších, stredných aj vyšších). Režim sa vyberie pri Initialize:
///  - Playable: model má kostru (Mixamo mixamorig:*) a je priradená DemonAnimationSet. Idle / Run / Attack / premena
///    sa miešajú cez PlayableGraph (bez AnimatorControllera), takže nevznikne T-pose,
///  - Legacy: glTFast Animation s klipmi pomenovanými idle/run/attack,
///  - Controller: Animator s controllerom (parametre Speed, Moving/Run, Attack),
///  - Procedural: statický mesh bez kostry (napr. Tripo model) - dýchanie, kolísanie, výpad pri útoku.
/// Pridáva sa automaticky z NPCPerception.
/// </summary>
public class DemonModelAnimator : MonoBehaviour
{
    private enum Mode { None, Playable, Legacy, Controller, Procedural }

    private const int IdleSlot = 0, RunSlot = 1, AttackSlot = 2, TransformSlot = 3, StartSlot = 4,
        AttackElbowSlot = 5, DeathFwdSlot = 6, DeathBackSlot = 7,
        AttackKickSlot = 8, BlockSlot = 9, DrawSlot = 10, SheathSlot = 11,
        CrouchIdleSlot = 12, CrouchWalkSlot = 13, JumpSlot = 14, SprintSlot = 15, StandUpSlot = 16, ClimbSlot = 17, CoverSlot = 18, LandSlot = 19, LightBase = 20, HeavyBase = 26, SlotCount = 32;
    private const int ComboCount = 6;   // koľko klipov ľahkého / ťažkého útoku sa nahrá

    [Tooltip("Animácia beží aj pri zastavenom čase (hlavné menu so svätyňou)")]
    public bool useUnscaledTime;

    [Tooltip("Pod touto rýchlosťou (m/s) sa démon považuje za stojaceho")]
    public float moveThreshold = 0.3f;
    public float crossFade = 0.15f;

    private Mode mode = Mode.None;
    private DemonEnemy demon;
    private Animation legacy;
    private Animator animator;

    // Legacy / Controller
    private string idleClip, runClip, attackClip, current;
    private int speedHash, moveHash, attackHash;
    private bool hasSpeed, hasMove, hasAttack, attackIsTrigger;

    // Playable
    private DemonAnimationSet set;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private readonly AnimationClipPlayable[] playables = new AnimationClipPlayable[SlotCount];
    private readonly bool[] slotUsed = new bool[SlotCount];
    private readonly float[] weights = new float[SlotCount];
    private readonly float[] targets = new float[SlotCount];
    private bool graphCreated;

    // Stav
    private Vector3 lastPosition;
    private float speed;
    private bool wasAttacking;
    private bool movingState, wasMoving;
    private float startWalkUntil;
    private int attackSlotInUse = -1;
    private float attackUntil;
    private int deathSlot = -1;
    [Tooltip("Najdlhšie, ako sa dohráva útok po konci nabíjania (s)")]
    public float maxAttackClipSeconds = 1.6f;
    [Tooltip("Hrúbka tela nad zemou, keď mŕtvy démon leží (m)")]
    public float deathBodyThickness = 0.1f;
    private float stillTime = 10f;
    [Tooltip("Ako dlho musí démon stáť, aby sa pri ďalšom pohybe znova prehral rozbeh (Start Walking)")]
    public float startWalkMinIdle = 0.35f;
    [Tooltip("Zruší posun bokov dopredu z klipov, ktoré nie sú 'In Place' (model by inak kĺzal pred svojou kapsulou, prešiel cez hráča a skočil späť)")]
    public bool lockHipsHorizontal = true;
    private Transform hips;
    private Vector3 hipsRest;
    // Humanoid avatar (napr. boss z Blendera, nie Mixamo): kosti sa berú z avataru a lokálne osi bokov nie sú známe,
    // preto sa poloha bokov zamyká v priestore modelu
    private bool humanoidRig;
    private Vector3 hipsRootRest;

    // Korekcia výšky: chodidlá sa držia na zemi bez ohľadu na výšku bokov v klipe
    [Header("Zem")]
    [Tooltip("Priebežne dorovnáva model tak, aby najnižšie chodidlo stálo na zemi (klip z iného rigu môže model nadvihnúť)")]
    public bool calibrateGround = true;
    public float toeSoleOffset = 0.03f;
    public float footSoleOffset = 0.09f;
    public float maxGroundCorrection = 1.5f;
    public float groundCorrectionSpeed = 6f;
    private Transform[] footBones;
    private Transform[] toeBones;
    private Quaternion[] toeRest;
    private float soleOffset;
    private CapsuleCollider capsule;
    private float groundCorrection;
    private bool groundCalibrated;
    private Vector3 basePosition;
    private Quaternion baseRotation;
    private Vector3 baseScale;
    private float phase;
    private float transformUntil;
    private float transformLength;

    public bool IsTransforming => Time.time < transformUntil;
    public bool IsRiggedMode => mode == Mode.Playable;

    void Awake()
    {
        demon = GetComponentInParent<DemonEnemy>();
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;
        baseScale = transform.localScale;
        lastPosition = transform.position;
    }

    private PlayerCombat playerCombat;
    private bool isPlayer;

    // Jednorazové akcie hráča (vytasenie / zasunutie zbrane) a držaný blok
    public enum OneShot { Draw, Sheath, StandUp, Climb, Land }
    private int oneShotSlot = -1;
    private float oneShotUntil;
    private bool blocking;

    public void SetBlocking(bool value) => blocking = value;

    private bool forceIdle;

    /// <summary>Postava je mŕtva bez klipu smrti: prehráva sa len státie, aby nohy nechodili.</summary>
    public void ForceIdle() => forceIdle = true;

    private float externalAttackUntil;
    /// <summary>Útok riadený zvonka (bossovia nemajú DemonEnemy ani PlayerCombat): po dané sekundy sa prehráva útočný klip.</summary>
    public void PlayAttackFor(float seconds) => externalAttackUntil = Time.time + seconds;

    /// <summary>Prehrá jednorazový klip (Draw / Sheath) a vráti jeho dĺžku v sekundách; 0, ak klip chýba.</summary>
    public float PlayOneShot(OneShot kind)
    {
        if (mode != Mode.Playable || deathSlot >= 0) return 0f;

        int slot = kind == OneShot.Draw ? DrawSlot : kind == OneShot.Sheath ? SheathSlot : kind == OneShot.StandUp ? StandUpSlot
                 : kind == OneShot.Climb ? ClimbSlot : LandSlot;
        if (!slotUsed[slot]) return 0f;

        // Za pohybu (nie v čupnutí) sa vytasenie/zasunutie hrá len na hornej polovici tela, hráč sa nezastaví
        if ((kind == OneShot.Draw || kind == OneShot.Sheath) && overlayReady && this.speed > 0.5f && crouchAmount < 0.5f)
        {
            int which = kind == OneShot.Draw ? 0 : 1;
            overlayMixer.SetInputWeight(0, which == 0 ? 1f : 0f);
            overlayMixer.SetInputWeight(1, which == 1 ? 1f : 0f);
            overlayMixer.SetInputWeight(2, 0f);
            overlayClips[which].SetTime(0.0);
            float overlayLength = overlayClips[which].GetAnimationClip().length;
            overlayUntil = Time.time + overlayLength;
            return overlayLength;
        }

        // Dopad má byť svižný: dlhší klip sa zrýchli, aby hráča nezdržal
        AnimationClip clip = ClipOfSlot(slot);
        float speed = kind == OneShot.Land ? Mathf.Max(1f, clip.length / landMaxSeconds) : 1f;
        float length = clip.length / speed;

        oneShotSlot = slot;
        oneShotUntil = Time.time + length;
        attackUntil = 0f;
        playables[slot].SetSpeed(speed);
        playables[slot].SetTime(0.0);
        return length;
    }

    [Tooltip("Najdlhšie trvanie animácie dopadu (s)")]
    public float landMaxSeconds = 0.6f;

    // Dopad po skoku/páde: preruší klip skoku a prehrá dopadnutie na zem
    void OnPlayerLanded(float impactSpeed)
    {
        jumpUntil = 0f;
        if (mode != Mode.Playable || !slotUsed[LandSlot] || deathSlot >= 0 || (oneShotSlot == ClimbSlot && Time.time < oneShotUntil)) return;

        // Kým dopad neskončí, postava stojí na mieste (inak by sa pri animácii dopadu kĺzala)
        float length = PlayOneShot(OneShot.Land);
        if (length > 0f && playerMovement != null) playerMovement.LockMovement(length);
    }

    /// <summary>
    /// Vymení animačnú sadu za behu (hráč: bez zbrane / s mečom). Graf sa postaví nanovo, takže sa aj stav vynuluje.
    /// </summary>
    public void SetAnimationSet(DemonAnimationSet newSet)
    {
        if (mode != Mode.Playable || newSet == null || newSet == set || deathSlot >= 0) return;

        if (graphCreated && graph.IsValid()) graph.Destroy();
        graphCreated = false;

        Array.Clear(slotUsed, 0, slotUsed.Length);
        Array.Clear(weights, 0, weights.Length);
        Array.Clear(targets, 0, targets.Length);
        attackSlotInUse = -1;
        attackUntil = 0f;
        oneShotSlot = -1;
        oneShotUntil = 0f;
        overlayUntil = 0f;
        overlayWeight = 0f;
        startWalkUntil = 0f;

        set = newSet;
        SetupPlayable();
        CalibrateClipFloors();
    }

    // ---------- Výška chodidiel podľa klipu ----------
    // Každý klip má inú výšku bokov (iný rig, iná póza), takže chodidlá by boli raz nad zemou a raz v nej. Namiesto dobiehania
    // za aktuálnou pózou (pri prechode klipov to nadskočí a pri zastavení sa postava na chvíľu prepadne) sa pre každý klip
    // raz zmeria, ako vysoko je jeho najnižšie chodidlo, a za behu sa výška vyrovná podľa AKTUÁLNYCH váh klipov.
    private readonly float[] clipFloor = new float[SlotCount];
    // Pri chôdzi/behu/státí sa podlaha berie z času klipu (nie len minimum), inak model pri prechode nadskočí a pri zastavení sa prepadne
    private const int CurveSamples = 33;
    [Tooltip("O koľko (m) môže krivka podlahy vystúpiť nad najnižší bod klipu; menej = pokojnejší model, viac = presnejšie chodidlá na zemi")]
    public float floorCurveMaxRise = 0.04f;
    private readonly float[][] floorCurve = new float[SlotCount][];
    private bool floorsReady;
    private Transform[] allBones;

    [Tooltip("Výška modelu sa vyrovnáva podľa váh klipov (bez nadskakovania a prepadávania pri prechodoch)")]
    public bool useClipFloors = true;

    AnimationClip ClipOfSlot(int slot)
    {
        switch (slot)
        {
            case IdleSlot: return set.idle != null ? set.idle : set.run;
            case RunSlot: return set.run != null ? set.run : set.idle;
            case AttackSlot: return set.attack;
            case TransformSlot: return set.transformation;
            case StartSlot: return set.startWalk;
            case AttackElbowSlot: return set.attackElbow;
            case DeathFwdSlot: return set.deathForward;
            case DeathBackSlot: return set.deathBack;
            case AttackKickSlot: return set.attackKick;
            case BlockSlot: return set.block;
            case DrawSlot: return set.drawWeapon;
            case SheathSlot: return set.sheathWeapon;
            case CrouchIdleSlot: return set.crouchIdle;
            case CrouchWalkSlot: return set.crouchWalk;
            case JumpSlot: return set.jump;
            case SprintSlot: return set.sprint;
            case StandUpSlot: return set.standUp;
            case ClimbSlot: return set.climb;
            case CoverSlot: return set.coverTurn;
            case LandSlot: return set.land;
            default:
                // Útoky s mečom: ľahké (kratší klik) a ťažké (dlhší klik) klipy
                if (slot >= LightBase && slot < HeavyBase) return PlayerOnly(ArrayClip(set.lightAttacks, slot - LightBase));
                if (slot >= HeavyBase && slot < SlotCount) return PlayerOnly(ArrayClip(set.heavyAttacks, slot - HeavyBase));
                return null;
        }
    }

    static AnimationClip ArrayClip(AnimationClip[] clips, int index) => clips != null && index < clips.Length ? clips[index] : null;

    // Skutočná rýchlosť (m/s), ktorú klip chôdze/behu "prejde" (posun bokov za jeden cyklus); 0 = klip je In Place, použije sa runReferenceSpeed.
    // Bez toho sa tempo nôh riadi odhadom 3,2 m/s a pri inej rýchlosti démona nohy kĺzu.
    private float runClipSpeed, sprintClipSpeed;

    float MeasureClipSpeed(AnimationClip clip)
    {
        if (clip == null || hips == null || clip.length < 0.1f) return 0f;

        clip.SampleAnimation(gameObject, 0f);
        Vector3 start = hips.position;
        clip.SampleAnimation(gameObject, clip.length);
        Vector3 travel = hips.position - start;
        travel.y = 0f;

        float measured = travel.magnitude / clip.length;
        return measured > 0.3f ? measured : 0f;
    }

    void CalibrateClipFloors()
    {
        runClipSpeed = slotUsed[RunSlot] && mode == Mode.Playable ? MeasureClipSpeed(ClipOfSlot(RunSlot)) : 0f;
        sprintClipSpeed = slotUsed[SprintSlot] && mode == Mode.Playable ? MeasureClipSpeed(ClipOfSlot(SprintSlot)) : 0f;

        // Začiatočná poloha bokov v klipe šplhania (v priestore modelu), od nej sa meria zrušený posun
        climbHipsReady = false;
        System.Array.Clear(attackHipsReady, 0, attackHipsReady.Length);
        if (mode == Mode.Playable && slotUsed[ClimbSlot] && hips != null)
        {
            set.climb.SampleAnimation(gameObject, 0f);
            climbHipsStart = transform.InverseTransformPoint(hips.position);
            climbHipsReady = true;
        }

        floorsReady = false;
        if (!useClipFloors || mode != Mode.Playable || footBones == null || capsule == null) return;

        if (allBones == null) allBones = CollectRigBones();

        float idleHipsY = float.NaN;
        if (hips != null && slotUsed[IdleSlot] && ClipOfSlot(IdleSlot) != null)
        {
            ClipOfSlot(IdleSlot).SampleAnimation(gameObject, 0f);
            idleHipsY = hips.localPosition.y;
        }

        const int samples = 9;
        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (!slotUsed[slot]) continue;
            AnimationClip clip = ClipOfSlot(slot);
            if (clip == null) continue;

            // Ležiace telo (smrť) nestojí na chodidlách: počíta sa najnižšia kosť celého tela
            bool lying = slot == DeathFwdSlot || slot == DeathBackSlot;
            Transform[] bones = lying ? allBones : footBones;

            // Skok: výška bokov sa z klipu neberie (vzlet obstaráva kapsula), takže sa meria s boky na úrovni začiatku klipu
            bool isJump = slot == JumpSlot && hips != null;
            if (isJump)
            {
                clip.SampleAnimation(gameObject, 0f);
                jumpHipsY0 = hips.localPosition.y;
                jumpHipsReady = true;
            }
            // Údery päsťami (úder, elbow, kop): klip niekedy nadskočí celým telom; výška bokov sa z neho neberie (ako pri skoku)
            bool isAttackLift = isPlayer && hips != null && (slot == AttackSlot || slot == AttackElbowSlot || slot == AttackKickSlot);
            if (isAttackLift)
            {
                clip.SampleAnimation(gameObject, 0f);
                // Cieľová výška bokov = výška v klipe státia (nie v klipe útoku: klipy démona majú inú výšku bokov než model hráča)
                attackHipsY0[slot] = float.IsNaN(idleHipsY) ? hips.localPosition.y : idleHipsY;
                attackHipsReady[slot] = true;
            }
            float liftBase = isJump ? jumpHipsY0 : attackHipsY0[slot];
            float hipsScale = (isJump || isAttackLift) && hips.parent != null ? hips.parent.lossyScale.y : 1f;

            float offset = lying ? deathBodyThickness : soleOffset;
            float lowest = float.MaxValue, maxLift = 0f;
            for (int k = 0; k < samples; k++)
            {
                clip.SampleAnimation(gameObject, clip.length * k / (samples - 1));
                float lift = isJump || isAttackLift ? (hips.localPosition.y - liftBase) * hipsScale : 0f;
                maxLift = Mathf.Max(maxLift, lift);
                for (int b = 0; b < bones.Length; b++)
                    lowest = Mathf.Min(lowest, bones[b].position.y - lift);
            }
            clipFloor[slot] = (lowest - transform.position.y) - offset;
            // Útok nemení výšku modelu voči státiu: podlaha klipu = podlaha státia (inak by sa pri začiatku útoku celý model nadvihol / zhodil)
            if (isAttackLift && slotUsed[IdleSlot] && slot != IdleSlot) clipFloor[slot] = clipFloor[IdleSlot];
            if (isAttackLift) Debug.Log($"Útok bez zbrane ({clip.name}): klip zdvíha boky až o {maxLift:0.00} m, výška sa ruší.");

            floorCurve[slot] = null;
            if (!UsesFloorCurve(slot)) continue;

            float[] curve = new float[CurveSamples];
            for (int k = 0; k < CurveSamples; k++)
            {
                clip.SampleAnimation(gameObject, clip.length * k / (CurveSamples - 1));
                float low = float.MaxValue;
                for (int b = 0; b < bones.Length; b++)
                    low = Mathf.Min(low, bones[b].position.y);
                // Fáza behu vo vzduchu (obe chodidlá hore) sa nesleduje: model by sa kolísal voči kamere
                curve[k] = Mathf.Min((low - transform.position.y) - offset, clipFloor[slot] + floorCurveMaxRise);
            }
            floorCurve[slot] = curve;
        }

        floorsReady = true;
        floorSnapPending = true;   // po (pre)kalibrácii sa výška nastaví hneď, bez nábehu
    }

    private readonly float[] attackHipsY0 = new float[32];
    private readonly bool[] attackHipsReady = new bool[32];
    private float jumpHipsY0;
    private bool jumpHipsReady;

    bool HasClipWeights()
    {
        float sum = 0f;
        for (int i = 0; i < SlotCount; i++)
            if (slotUsed[i]) sum += weights[i];
        return sum > 0.001f;
    }

    // Žiadny klip nemá takmer celú váhu = práve prebieha prechod medzi klipmi
    bool IsBlendingClips()
    {
        float sum = 0f, max = 0f;
        for (int i = 0; i < SlotCount; i++)
        {
            if (!slotUsed[i]) continue;
            sum += weights[i];
            if (weights[i] > max) max = weights[i];
        }
        return sum > 0.001f && max / sum < 0.97f;
    }

    private Vector3 climbHipsStart;
    private bool climbHipsReady;
    private bool coverActive;

    // Podiel klipu šplhania v aktuálnom mixe (0..1)
    float ClimbShare()
    {
        if (!slotUsed[ClimbSlot]) return 0f;

        float sum = 0f;
        for (int i = 0; i < SlotCount; i++)
            if (slotUsed[i]) sum += weights[i];
        return sum > 0.001f ? weights[ClimbSlot] / sum : 0f;
    }

    /// <summary>Krytie pri stene: pózu z klipu "cover turn" prehrá raz a drží ju, kým hráč v kryte stojí.</summary>
    public void SetCover(bool active)
    {
        if (active == coverActive) return;
        coverActive = active;
        if (active && mode == Mode.Playable && slotUsed[CoverSlot]) playables[CoverSlot].SetTime(0.0);
    }

    // Podiel klipu skoku v aktuálnom mixe (0..1)
    private static readonly int[] AttackLiftSlots = { AttackSlot, AttackElbowSlot, AttackKickSlot };

    float AttackLiftShareTotal()
    {
        float total = 0f;
        foreach (int s in AttackLiftSlots) total += SlotShare(s);
        return total;
    }

    float SlotShare(int slot)
    {
        float sum = 0f;
        for (int i = 0; i < SlotCount; i++)
            if (slotUsed[i]) sum += weights[i];
        return sum > 0.001f ? weights[slot] / sum : 0f;
    }

    float JumpShare()
    {
        if (!slotUsed[JumpSlot]) return 0f;

        float sum = 0f;
        for (int i = 0; i < SlotCount; i++)
            if (slotUsed[i]) sum += weights[i];
        return sum > 0.001f ? weights[JumpSlot] / sum : 0f;
    }

    static bool UsesFloorCurve(int slot)
    {
        return slot == IdleSlot || slot == RunSlot || slot == StartSlot || slot == CrouchIdleSlot || slot == CrouchWalkSlot || slot == SprintSlot;
    }

    // Podlaha klipu v jeho aktuálnom čase (lineárna interpolácia nameranej krivky)
    float FloorAtPlayTime(int slot)
    {
        float[] curve = floorCurve[slot];
        if (curve == null) return clipFloor[slot];

        AnimationClip clip = ClipOfSlot(slot);
        float length = Mathf.Max(0.0001f, clip.length);
        float t = (float)playables[slot].GetTime();
        float u = clip.isLooping || slot == IdleSlot || slot == RunSlot || slot == SprintSlot ? Mathf.Repeat(t, length) / length : Mathf.Clamp01(t / length);

        float f = u * (CurveSamples - 1);
        int a = Mathf.Min((int)f, CurveSamples - 2);
        return Mathf.Lerp(curve[a], curve[a + 1], f - a);
    }

    // Aktuálna výška podložky pod modelom: priemer podlaží klipov podľa ich váh
    float BlendedFloor()
    {
        float sum = 0f, floor = 0f;
        for (int i = 0; i < SlotCount; i++)
        {
            if (!slotUsed[i]) continue;
            sum += weights[i];
            if (weights[i] > 0f) floor += weights[i] * FloorAtPlayTime(i);
        }
        return sum > 0.001f ? floor / sum : 0f;
    }

    /// <summary>Zmení základnú mierku modelu (vzhľad hráča: výška/šírka). Výšky chodidiel treba potom prepočítať.</summary>
    public void SetBaseScale(Vector3 scale)
    {
        baseScale = scale;
        transform.localScale = scale;
    }

    public void RecalibrateFloors() => CalibrateClipFloors();

    private bool floorSnapPending = true;

    float CapsuleBottomFromTransform()
    {
        Transform t = capsule.transform;
        float centerY = t.TransformPoint(capsule.center).y;
        return centerY - Mathf.Max(capsule.height * 0.5f, capsule.radius) * t.lossyScale.y;
    }

    // ---------- Čupnutie ----------
    private float crouchAmount;
    [Tooltip("Bez klipov čupnutia sa postava pri čupnutí len nakloní dopredu o toľko stupňov")]
    public float crouchLeanDegrees = 22f;

    /// <summary>Hĺbka čupnutia 0..1 (PlayerStealth). S klipmi Crouch Idle / Crouch Walk sa prehrajú, inak sa postava nakloní.</summary>
    public void SetCrouch(float amount) => crouchAmount = Mathf.Clamp01(amount);

    bool UsesCrouchClips => slotUsed[CrouchIdleSlot] || slotUsed[CrouchWalkSlot];

    // ---------- Dĺžka útoku hráča ----------
    // Klip útoku hráča trvá najviac attackAnimSeconds (dlhšie sa zrýchlia, kratšie idú v pôvodnej rýchlosti)
    [Tooltip("Kop bez zbrane trvá toľkokrát dlhšie než ostatné údery (väčšie číslo = pomalší kop)")]
    public float unarmedKickSlowdown = 1.8f;
    [Tooltip("Tempo animácie šprintu hráča (1 = podľa rýchlosti pohybu; menej = pomalšie nohy)")]
    [Range(0.3f, 1f)] public float sprintAnimScale = 0.6f;

    float PlayerAttackSpeed(AnimationClip clip, bool heavy = false, int slot = -1)
    {
        float target = playerCombat != null ? playerCombat.attackAnimSeconds * (heavy ? playerCombat.heavyAnimFactor : 1f) : 0.9f;
        bool unarmedSet = set != null && (set.lightAttacks == null || set.lightAttacks.Length == 0);
        if (slot == AttackKickSlot && unarmedSet) target *= unarmedKickSlowdown;
        return Mathf.Max(1f, clip.length / Mathf.Max(0.2f, target));
    }

    private int preparedSlot = -1;

    /// <summary>
    /// Hráč: vyberie klip útoku (s mečom náhodný ľahký alebo ťažký, inak variant 0/1/2), zapamätá si ho a vráti jeho trvanie (s).
    /// Klip sa prehrá, keď PlayerCombat spustí signál útoku.
    /// </summary>
    public float PrepareAttack(int variant, bool heavy)
    {
        if (mode != Mode.Playable) return 0f;

        int slot = PickComboSlot(heavy);
        if (slot < 0) slot = ResolveAttackSlot(variant);
        preparedSlot = slot;
        if (slot < 0) return 0f;

        AnimationClip clip = ClipOfSlot(slot);
        return Mathf.Min(clip.length / PlayerAttackSpeed(clip, heavy, slot), maxAttackClipSeconds * (heavy ? 1.5f : 1f) * (slot == AttackKickSlot ? unarmedKickSlowdown : 1f));
    }

    // Náhodný použitý slot ľahkého / ťažkého útoku; ťažký bez klipov použije ľahký; -1 = žiadny
    int PickComboSlot(bool heavy)
    {
        int slot = RandomComboSlot(heavy ? HeavyBase : LightBase);
        return slot >= 0 || !heavy ? slot : RandomComboSlot(LightBase);
    }

    int RandomComboSlot(int baseSlot)
    {
        int count = 0;
        for (int i = 0; i < ComboCount; i++)
            if (slotUsed[baseSlot + i]) count++;
        if (count == 0) return -1;

        int pick = UnityEngine.Random.Range(0, count);
        for (int i = 0; i < ComboCount; i++)
            if (slotUsed[baseSlot + i] && pick-- == 0) return baseSlot + i;
        return -1;
    }

    int ResolveAttackSlot(int variant)
    {
        if (variant == 2 && slotUsed[AttackKickSlot]) return AttackKickSlot;
        if (variant == 1 && slotUsed[AttackElbowSlot]) return AttackElbowSlot;
        if (slotUsed[AttackSlot]) return AttackSlot;
        if (slotUsed[AttackElbowSlot]) return AttackElbowSlot;
        if (slotUsed[AttackKickSlot]) return AttackKickSlot;
        return -1;
    }

    /// <summary>Koľko sekúnd bude trvať útok daného variantu (s aktuálnou sadou); 0, ak nie je klip.</summary>
    public float AttackDuration(int variant)
    {
        if (mode != Mode.Playable) return 0f;
        int slot = ResolveAttackSlot(variant);
        if (slot < 0) return 0f;

        AnimationClip clip = ClipOfSlot(slot);
        float clipSpeed = isPlayer ? PlayerAttackSpeed(clip, false, slot) : Mathf.Max(0.1f, (float)playables[slot].GetSpeed());
        return Mathf.Min(clip.length / clipSpeed, maxAttackClipSeconds * (slot == AttackKickSlot ? unarmedKickSlowdown : 1f));
    }

    /// <summary>Hráč používa rovnaký animátor ako démoni: útoky (úder / elbow) sa čítajú z PlayerCombat. Volať pred Initialize.</summary>
    public void BindPlayer(PlayerCombat combat)
    {
        playerCombat = combat;
        isPlayer = true;

        // Skok má len hráč: démoni a NPC nemajú ani klip skoku, ani túto väzbu
        playerMovement = GetComponentInParent<PlayerMovement>();
        if (playerMovement != null)
        {
            playerMovement.Jumped += OnPlayerJumped;
            playerMovement.Landed += OnPlayerLanded;
        }
    }

    private PlayerMovement playerMovement;
    private float jumpUntil;

    // Nábeh a dojazd skoku sú mäkšie ako bežný prechod klipov
    private const float JumpFadeIn = 0.1f;
    private const float JumpFadeOut = 0.25f;

    // Odraz zo zeme: klip skoku sa spustí od začiatku a natiahne na dobu, ktorú skok reálne trvá, aby pristátie sedelo na dopad
    void OnPlayerJumped()
    {
        if (mode != Mode.Playable || !slotUsed[JumpSlot] || deathSlot >= 0) return;

        float airTime = Mathf.Max(0.3f, playerMovement != null ? playerMovement.ExpectedAirTime : 1f);

        // Dvojitý skok vo vzduchu: klip sa nereštartuje, len sa drží pózu, kým hráč nepristane
        if (Time.time < jumpUntil && playerMovement != null && !playerMovement.IsGrounded)
        {
            jumpUntil = Time.time + airTime;
            return;
        }

        playables[JumpSlot].SetTime(0.0);
        playables[JumpSlot].SetSpeed(Mathf.Clamp(set.jump.length / airTime, 0.6f, 2f));
        jumpUntil = Time.time + airTime;
    }

    /// <summary>Vyberie režim animácie podľa toho, čo model obsahuje. Volá NPCPerception (alebo PlayerVisual) hneď po vytvorení modelu.</summary>
    public void Initialize(DemonAnimationSet animationSet)
    {
        set = animationSet;
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;
        baseScale = transform.localScale;

        legacy = GetComponentInChildren<Animation>(true);
        animator = GetComponentInChildren<Animator>(true);

        bool rigged = GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

        if (rigged && set != null && set.HasAnyClip && SetupPlayable()) mode = Mode.Playable;
        else if (legacy != null && SetupLegacy()) mode = Mode.Legacy;
        else if (animator != null && animator.runtimeAnimatorController != null && SetupController()) mode = Mode.Controller;
        else mode = Mode.Procedural;   // statický mesh bez kostry: aspoň základný pohyb namiesto T-pose

        if (mode == Mode.Playable)
        {
            ResolveFeet();
            CalibrateClipFloors();
        }

        if (animator != null && mode != Mode.Playable)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (mode != Mode.Controller) animator.enabled = false;
        }
    }

    // Čupnutie, skok, šprint, vstávanie, šplhanie a krytie patria len hráčovi: NPC a démoni tieto klipy nikdy neprehrajú, ani keby boli v ich sade
    AnimationClip PlayerOnly(AnimationClip clip) => isPlayer ? clip : null;

    // ---------- Playable (Mixamo kostra) ----------
    bool SetupPlayable()
    {
        if (animator == null) animator = gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Klipy sú Generic (podľa ciest kostí). Humanoid alebo neplatný avatar na modeli (napr. rig importovaný ako Humanoid bez mapovania)
        // by ich nepustil na kosti a postava by ostala v bind póze; bez avataru sa klipy aplikujú podľa názvov kostí.
        if (animator.avatar != null && (!animator.avatar.isValid || animator.avatar.isHuman)) animator.avatar = null;

        AnimationClip idle = set.idle != null ? set.idle : set.run;   // bez Idle sa použije zmrazená póza z Run
        AnimationClip[] clips =
        {
            idle, set.run != null ? set.run : set.idle, set.attack, set.transformation, set.startWalk,
            set.attackElbow, set.deathForward, set.deathBack,
            set.attackKick, set.block, set.drawWeapon, set.sheathWeapon,
            PlayerOnly(set.crouchIdle), PlayerOnly(set.crouchWalk), PlayerOnly(set.jump), PlayerOnly(set.sprint),
            PlayerOnly(set.standUp), PlayerOnly(set.climb), PlayerOnly(set.coverTurn), PlayerOnly(set.land)
        };
        Array.Resize(ref clips, SlotCount);
        for (int i = LightBase; i < SlotCount; i++) clips[i] = ClipOfSlot(i);

        graph = PlayableGraph.Create("DemonAnimation");
        graph.SetTimeUpdateMode(useUnscaledTime ? DirectorUpdateMode.UnscaledGameTime : DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, SlotCount);

        for (int i = 0; i < SlotCount; i++)
        {
            if (clips[i] == null) continue;

            AnimationClipPlayable p = AnimationClipPlayable.Create(graph, clips[i]);
            graph.Connect(p, 0, mixer, i);
            mixer.SetInputWeight(i, 0f);
            playables[i] = p;
            slotUsed[i] = true;
        }

        if (set.idle == null && slotUsed[IdleSlot]) playables[IdleSlot].SetSpeed(0.0);   // zmrazená póza
        // Rýchlosť úderov podľa vlastníka (démon alebo hráč), aby animácia sedela na frekvenciu útokov
        // Hráč neurýchľuje klipy paušálne: dĺžku úderu určuje PlayerCombat.attackAnimSeconds (pozri PlayerAttackSpeed)
        float attackBoost = demon != null ? demon.attackSpeedBoost : 1f;
        double boost = Mathf.Max(0.1f, attackBoost);
        if (slotUsed[AttackSlot]) playables[AttackSlot].SetSpeed(boost);
        if (slotUsed[AttackElbowSlot]) playables[AttackElbowSlot].SetSpeed(boost);
        if (slotUsed[AttackKickSlot]) playables[AttackKickSlot].SetSpeed(boost);

        Playable root = mixer;
        overlayReady = false;
        AnimationClip overlayFallback = set.drawWeapon != null ? set.drawWeapon : set.sheathWeapon != null ? set.sheathWeapon : set.idle != null ? set.idle : set.run;
        if (isPlayer && overlayFallback != null && BuildUpperBodyMask())
        {
            // Vytasenie/zasunutie a útok za pohybu: klip ovláda len hornú polovicu tela, nohy ďalej idú podľa chôdze/behu
            layerMixer = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(mixer, 0, layerMixer, 0);
            layerMixer.SetInputWeight(0, 1f);

            overlayMixer = AnimationMixerPlayable.Create(graph, 3);   // 0 vytasenie, 1 zasunutie, 2 útok (vytvára sa za behu)
            overlayClips[0] = AnimationClipPlayable.Create(graph, set.drawWeapon != null ? set.drawWeapon : overlayFallback);
            overlayClips[1] = AnimationClipPlayable.Create(graph, set.sheathWeapon != null ? set.sheathWeapon : overlayFallback);
            graph.Connect(overlayClips[0], 0, overlayMixer, 0);
            graph.Connect(overlayClips[1], 0, overlayMixer, 1);
            overlayMixer.SetInputWeight(0, 1f);
            overlayMixer.SetInputWeight(1, 0f);

            graph.Connect(overlayMixer, 0, layerMixer, 1);
            layerMixer.SetInputWeight(1, 0f);
            layerMixer.SetLayerMaskFromAvatarMask(1, upperMask);
            root = layerMixer;
            overlayReady = true;
        }

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(graph, "Demon", animator);
        output.SetSourcePlayable(root);
        graph.Play();
        graphCreated = true;

        weights[IdleSlot] = 1f;
        mixer.SetInputWeight(IdleSlot, 1f);
        return true;
    }

    // Maska hornej polovice tela: kosti pod "Spine" (chrbtica, ruky, hlava); boky a nohy ostávajú pri chôdzi
    bool BuildUpperBodyMask()
    {
        if (animator == null) return false;
        Transform rootT = animator.transform;

        var paths = new System.Collections.Generic.List<string>();
        var active = new System.Collections.Generic.List<bool>();
        foreach (Transform t in rootT.GetComponentsInChildren<Transform>(true))
        {
            if (t == rootT) continue;

            var sb = new System.Text.StringBuilder(t.name);
            bool upper = false;
            for (Transform p = t; p != rootT && p != null; p = p.parent)
            {
                if (p != t) sb.Insert(0, p.name + "/");
                if (p.name.EndsWith("Spine", StringComparison.Ordinal)) upper = true;
            }
            paths.Add(sb.ToString());
            active.Add(upper);
        }
        if (paths.Count == 0) return false;

        upperMask = new AvatarMask();
        upperMask.transformCount = paths.Count;
        for (int i = 0; i < paths.Count; i++)
        {
            upperMask.SetTransformPath(i, paths[i]);
            upperMask.SetTransformActive(i, active[i]);
        }
        return true;
    }

    private AnimationLayerMixerPlayable layerMixer;
    private AnimationMixerPlayable overlayMixer;
    private AnimationClipPlayable attackOverlay;
    private readonly AnimationClipPlayable[] overlayClips = new AnimationClipPlayable[2];
    private AvatarMask upperMask;
    private bool overlayReady;
    private float overlayUntil, overlayWeight;

    void OnDestroy()
    {
        if (upperMask != null) Destroy(upperMask);
        if (playerMovement != null)
        {
            playerMovement.Jumped -= OnPlayerJumped;
            playerMovement.Landed -= OnPlayerLanded;
        }
        if (graphCreated && graph.IsValid()) graph.Destroy();
    }

    // ---------- Legacy ----------
    bool SetupLegacy()
    {
        legacy.playAutomatically = false;
        legacy.cullingType = AnimationCullingType.AlwaysAnimate;

        string first = null;
        foreach (AnimationState state in legacy)
        {
            string n = state.name;
            if (first == null) first = n;
            if (idleClip == null && Has(n, "idle", "stand", "breath", "wait")) idleClip = n;
            else if (runClip == null && Has(n, "run", "walk", "move", "sprint")) runClip = n;
            else if (attackClip == null && Has(n, "attack", "hit", "slash", "strike", "claw", "bite")) attackClip = n;
        }

        if (first == null) return false;                       // žiadne klipy
        if (idleClip == null) idleClip = first;                // jediný klip sa berie ako Idle
        if (runClip == null) runClip = idleClip;

        legacy[idleClip].wrapMode = WrapMode.Loop;
        legacy[runClip].wrapMode = WrapMode.Loop;
        if (attackClip != null)
        {
            legacy[attackClip].wrapMode = WrapMode.Once;
            if (demon != null) legacy[attackClip].speed = Mathf.Max(0.1f, demon.attackSpeedBoost);
        }

        legacy.Play(idleClip);
        current = idleClip;
        return true;
    }

    // ---------- Controller ----------
    bool SetupController()
    {
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            string n = p.name;
            if (p.type == AnimatorControllerParameterType.Float && Has(n, "speed", "velocity", "move"))
            {
                speedHash = p.nameHash; hasSpeed = true;
            }
            else if (p.type == AnimatorControllerParameterType.Bool && Has(n, "move", "run", "walk"))
            {
                moveHash = p.nameHash; hasMove = true;
            }
            else if ((p.type == AnimatorControllerParameterType.Trigger || p.type == AnimatorControllerParameterType.Bool) && Has(n, "attack"))
            {
                attackHash = p.nameHash; hasAttack = true;
                attackIsTrigger = p.type == AnimatorControllerParameterType.Trigger;
            }
        }
        return true;
    }

    static bool Has(string text, string a, string b = null, string c = null, string d = null, string e = null, string f = null)
    {
        return Contains(text, a) || Contains(text, b) || Contains(text, c) || Contains(text, d) || Contains(text, e) || Contains(text, f);
    }

    static bool Contains(string text, string part)
    {
        return part != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ---------- Premena vyššieho démona ----------
    /// <summary>Spustí animáciu premeny a vráti jej dĺžku (s), počas ktorej démon nebojuje.</summary>
    public float PlayTransformation(float fallbackSeconds)
    {
        bool hasClip = mode == Mode.Playable && slotUsed[TransformSlot];
        transformLength = hasClip ? set.transformation.length : fallbackSeconds;
        transformUntil = Time.time + transformLength;

        if (hasClip) playables[TransformSlot].SetTime(0.0);
        return transformLength;
    }

    void OnEnable()
    {
        lastPosition = transform.position;
        speed = 0f;
        wasAttacking = false;
        movingState = false;
        wasMoving = false;
        stillTime = 10f;
        groundCalibrated = false;
    }

    // Nájde chodidlá / prsty nôh kostry (Mixamo: ...LeftToeBase, ...LeftFoot)
    void ResolveFeet()
    {
        capsule = GetComponentInParent<CapsuleCollider>();

        Transform leftToe = null, rightToe = null, leftFoot = null, rightFoot = null, leftKnee = null, rightKnee = null, spineLow = null, spineMid = null, spineHigh = null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n.EndsWith("LeftToeBase", StringComparison.Ordinal)) leftToe = t;
            else if (n.EndsWith("RightToeBase", StringComparison.Ordinal)) rightToe = t;
            else if (n.EndsWith("LeftFoot", StringComparison.Ordinal)) leftFoot = t;
            else if (n.EndsWith("RightFoot", StringComparison.Ordinal)) rightFoot = t;
            else if (n.EndsWith("LeftLeg", StringComparison.Ordinal)) leftKnee = t;
            else if (n.EndsWith("Spine", StringComparison.Ordinal)) spineLow = t;
            else if (n.EndsWith("Spine1", StringComparison.Ordinal)) spineMid = t;
            else if (n.EndsWith("Spine2", StringComparison.Ordinal)) spineHigh = t;
            else if (n.EndsWith("RightLeg", StringComparison.Ordinal)) rightKnee = t;
            else if (hips == null && n.EndsWith("Hips", StringComparison.Ordinal))
            {
                hips = t;
                hipsRest = t.localPosition;   // pokojová póza pred prvou animáciou
            }
        }

        if (leftToe != null && rightToe != null)
        {
            footBones = new[] { leftToe, rightToe };
            soleOffset = toeSoleOffset;
            toeBones = footBones;
            toeRest = new[] { leftToe.localRotation, rightToe.localRotation };
        }
        else if (leftFoot != null && rightFoot != null) { footBones = new[] { leftFoot, rightFoot }; soleOffset = footSoleOffset; }

        if (spineLow != null && spineMid != null && spineHigh != null) spineBones = new[] { spineLow, spineMid, spineHigh };

        // Kolená (pokojová póza kosti sa zapamätá na hinge obmedzenie pri čupnutí)
        if (leftKnee != null && rightKnee != null)
        {
            kneeBones = new[] { leftKnee, rightKnee };
            kneeRest = new[] { leftKnee.localRotation, rightKnee.localRotation };
        }

        // Humanoid avatar bez Mixamo názvov kostí: boky a chodidlá podľa avataru
        if (footBones == null && animator != null && animator.isHuman)
        {
            humanoidRig = true;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null)
            {
                hipsRest = hips.localPosition;
                hipsRootRest = transform.InverseTransformPoint(hips.position);
            }

            Transform lt = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rt = animator.GetBoneTransform(HumanBodyBones.RightToes);
            Transform lf = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rf = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (lt != null && rt != null) { footBones = new[] { lt, rt }; soleOffset = toeSoleOffset; }
            else if (lf != null && rf != null) { footBones = new[] { lf, rf }; soleOffset = footSoleOffset; }
        }
    }

    // Po vyhodnotení animácie: najnižšie chodidlo sa dorovná na spodok kapsuly (zem), model sa posúva ako celok
    void LateUpdate()
    {
        if (mode != Mode.Playable) return;

        // Klipy z iných kostier ohýbajú prsty nôh (iná pokojová póza); prsty hráča držíme rovno v pokojovej póze
        if (isPlayer && toeBones != null)
            for (int i = 0; i < toeBones.Length; i++) toeBones[i].localRotation = toeRest[i];

        if (isPlayer && kneeBones != null && crouchKneeHinge > 0f)
        {
            float crouchShare = CrouchShare();
            if (crouchShare > 0.01f) ConstrainKnees(crouchShare);
        }

        if (isPlayer && spineBones != null && idleForwardLean > 0f)
        {
            float idleShare = IdleShare();
            if (idleShare > 0.01f) LeanForward(idleForwardLean * idleShare);
        }

        // Šplhanie: posun bokov z klipu sa celý zruší, hore a dopredu hráča vedie PlayerClimb (inak by sa výška pripočítala dvakrát)
        if (climbHipsReady && hips != null)
        {
            float share = ClimbShare();
            if (share > 0.001f)
            {
                Vector3 travel = transform.InverseTransformPoint(hips.position) - climbHipsStart;
                hips.position -= transform.TransformVector(travel) * share;
            }
        }

        // Boky zostanú nad nohami: vodorovný posun z klipu sa zruší, výška (Y) ostáva
        if (lockHipsHorizontal && hips != null)
        {
            if (humanoidRig)
            {
                Vector3 rp = transform.InverseTransformPoint(hips.position);
                rp.x = hipsRootRest.x;
                rp.z = hipsRootRest.z;
                hips.position = transform.TransformPoint(rp);
            }
            else
            {
                Vector3 lp = hips.localPosition;
                lp.x = hipsRest.x;
                lp.z = hipsRest.z;
                hips.localPosition = lp;
            }
        }

        // Klip skoku zdvíha boky, ale kapsula už letí: bez tohto by postava vyskočila dvakrát a pri páde sa "sekala"
        float jumpShare = JumpShare();
        if (jumpHipsReady && jumpShare > 0.001f && hips != null)
        {
            Vector3 lp = hips.localPosition;
            lp.y = Mathf.Lerp(lp.y, jumpHipsY0, jumpShare);
            hips.localPosition = lp;
        }

        // Údery päsťami: výška bokov zostáva na úrovni začiatku klipu (postava nenadskakuje)
        if (isPlayer && hips != null)
            foreach (int attackSlot in AttackLiftSlots)
            {
                if (!attackHipsReady[attackSlot] || !slotUsed[attackSlot]) continue;
                float attackShare = SlotShare(attackSlot);
                if (attackShare < 0.001f) continue;

                Vector3 alp = hips.localPosition;
                alp.y = Mathf.Lerp(alp.y, attackHipsY0[attackSlot], attackShare);
                hips.localPosition = alp;
            }

        // Bez klipov čupnutia sa postava pri čupnutí nakloní dopredu okolo päty (nie škálovaním)
        if (!UsesCrouchClips)
            transform.localRotation = Quaternion.Euler(crouchLeanDegrees * crouchAmount, 0f, 0f) * baseRotation;

        // Výška podľa váh klipov: bez nadskakovania pri prechodoch a bez prepadnutia pri zastavení
        if (floorsReady && capsule != null)
        {
            // Prológový démon sa rúti vysokou rýchlosťou: výška modelu sa dorovnáva plynulo ako u hráča (okamžité dorovnanie podľa nôh by sekalo)
            bool playerLike = isPlayer || (demon != null && demon.Scripted);
            // Základná výška koreňa bez skutočne aplikovaného posunu modelu (ten je vyhladený a zaostáva za groundCorrection;
            // odpočítanie cieľovej hodnoty by tvorilo spätnú väzbu a model by chvel)
            float parentScaleY = Mathf.Max(0.01f, transform.parent != null ? transform.parent.lossyScale.y : 1f);
            float appliedOffset = (transform.localPosition.y - basePosition.y) * parentScaleY;
            float rootBaseY = playerLike ? transform.position.y - appliedOffset : transform.position.y - groundCorrection;
            // Hráč: spodok kapsuly z (interpolovaného) transformu, nie z collidera: bounds sa menia len vo fyzikálnom kroku a pri skoku,
            // šprinte alebo teleporte by sa výška modelu rátala z inej polohy než tá, ktorú vidíš
            // Prológový démon (Scripted) má vypnuté kolízie: bounds kapsuly sa vtedy neaktualizujú, výška sa preto berie z transformu
            float capsuleBottom = playerLike ? CapsuleBottomFromTransform() : capsule.bounds.min.y;
            float wanted = (capsuleBottom - (rootBaseY + BlendedFloor())) / (playerLike ? parentScaleY : 1f);   // hráč: lokálne jednotky

            // Výška sa mení len plynule: dorovnávanie podľa chodidiel v každom snímku (pri páde) spôsobovalo sekanie
            // Po výmene sady (zasunutie meča) sú váhy klipov na chvíľu nulové: natvrdo nastavená výška by bola zlá a model by sa potom 'dotiahol' (nadskok/prepad)
            bool snapNow = floorSnapPending && playerLike && HasClipWeights();
            if (snapNow || !playerLike) floorSnapPending = false;
            groundCorrection = Mathf.Clamp(snapNow ? wanted : Mathf.MoveTowards(groundCorrection, wanted, (playerLike ? PlayerFloorFollowSpeed : 3f) * Time.deltaTime),
                -maxGroundCorrection, maxGroundCorrection);
            if (snapNow) visualYVelocity = 0f;

            Vector3 q = transform.localPosition;
            float newY = basePosition.y + groundCorrection;

            // Poistka: ak by sa podľa skutočnej pózy (napr. uprostred prechodu klipov) chodidlo ocitlo pod zemou, model sa zdvihne hneď
            if (footBones != null && !(isPlayer && AttackLiftShareTotal() > 0.05f))   // počas úderu bez zbrane poistka model nezdvíha
            {
                float parentScale = transform.parent != null ? transform.parent.lossyScale.y : 1f;
                float lowestFoot = float.MaxValue;
                for (int i = 0; i < footBones.Length; i++)
                    lowestFoot = Mathf.Min(lowestFoot, footBones[i].position.y);

                // Kladné = chodidlo je pod zemou. Počas prechodu klipov (rozbeh, zastavenie) sa dorovnáva aj nadvihnutie,
                // inak by model pri rozbehu nadskočil; v ustálenom behu/skoku vzduchové fázy zostávajú.
                float below = capsuleBottom - (lowestFoot + (newY - q.y) * parentScale - soleOffset);
                bool correctAbove = deathSlot < 0 && JumpShare() < 0.01f && IsBlendingClips();
                float dead = playerLike ? FootDeadZone : 0.005f;
                if (below > dead || (correctAbove && below < -dead))
                {
                    float fix = below / Mathf.Max(0.01f, parentScale);
                    // Spúšťanie modelu k zemi (chodidlá nad zemou) je pomalé: rýchle dorovnanie podľa aktuálnej pózy pri rozbehu / zastavení vyzeralo ako skok
                    float speed = fix < 0f ? PlayerLowerSpeed : FootFixSpeed;
                    float step = playerLike ? Mathf.Min(Mathf.Abs(fix), speed * Time.deltaTime) * Mathf.Sign(fix) : fix;
                    groundCorrection = Mathf.Clamp(groundCorrection + step, -maxGroundCorrection, maxGroundCorrection);
                    newY = basePosition.y + groundCorrection;
                }
            }

            // Výstup sa ešte jemne vyhladí: aj malé skoky výšky (zmena chodidla pri behu, prechody klipov) by pôsobili ako zasekávanie postavy
            float smoothY = Mathf.SmoothDamp(q.y, newY, ref visualYVelocity, playerLike ? 0.12f : 0.05f);
            q.y = !playerLike || snapNow || Mathf.Abs(newY - q.y) > 0.5f ? newY : smoothY;
            transform.localPosition = q;
            return;
        }

        // Záloha (bez nameraných klipov): doťahovanie podľa najnižšieho chodidla
        if (!calibrateGround || footBones == null || capsule == null) return;

        float lowest = float.MaxValue;
        for (int i = 0; i < footBones.Length; i++)
            lowest = Mathf.Min(lowest, footBones[i].position.y);

        float error = capsule.bounds.min.y - (lowest - soleOffset);   // záporné = model sa vznáša
        float k = groundCalibrated ? Mathf.Min(1f, groundCorrectionSpeed * Time.deltaTime) : 1f;   // prvý frame natvrdo
        groundCalibrated = true;

        groundCorrection = Mathf.Clamp(groundCorrection + error * k, -maxGroundCorrection, maxGroundCorrection);
        Vector3 p = transform.localPosition;
        p.y = basePosition.y + groundCorrection;
        transform.localPosition = p;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;

        float raw = delta.magnitude / dt;
        if (raw > 30f) raw = 0f;                                   // teleport / grounding skok
        speed = Mathf.Lerp(speed, raw, 10f * dt);

        // Hysteréza: rozbeh pri prekročení prahu, zastavenie až pri výrazne nižšej rýchlosti (bez blikania Start/Idle)
        if (!movingState && speed > moveThreshold) movingState = true;
        else if (movingState && speed < moveThreshold * 0.6f) movingState = false;

        bool moving = movingState && !forceIdle;
        // Rozbeh sa prehrá len po dlhšom státí; krátke zastavenie počas naháňania ho nespúšťa znova (bez cukania)
        bool startedMoving = moving && !wasMoving && stillTime >= startWalkMinIdle;
        wasMoving = moving;
        stillTime = moving ? 0f : stillTime + dt;
        bool attacking = Time.time < externalAttackUntil || (demon != null ? demon.IsAttackWindup : (playerCombat != null && playerCombat.IsAttackAnimating));
        bool transforming = IsTransforming;

        switch (mode)
        {
            case Mode.Playable: UpdatePlayable(dt, moving, startedMoving, attacking, transforming); break;
            case Mode.Legacy: UpdateLegacy(moving, attacking); break;
            case Mode.Controller: UpdateController(moving, attacking); break;
            case Mode.Procedural: UpdateProcedural(dt, moving, attacking); break;
        }

        // Plynulý nábeh modelu pri premene (bez poskoku z ľudskej podoby)
        if (transforming && mode != Mode.Playable)
        {
            float t = 1f - Mathf.Clamp01((transformUntil - Time.time) / Mathf.Max(0.01f, transformLength));
            transform.localScale = baseScale * Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0f, 1f, t));
        }
        else if (transform.localScale != baseScale && !transforming)
        {
            transform.localScale = baseScale;
        }

        wasAttacking = attacking;
    }

    void UpdatePlayable(float dt, bool moving, bool startedMoving, bool attacking, bool transforming)
    {
        for (int i = 0; i < SlotCount; i++) targets[i] = 0f;

        bool dead = deathSlot >= 0;

        // Rozbeh (Start Walking) sa prehrá len raz pri pohybe z miesta; potom Run/Walking až do zastavenia
        if (startedMoving && slotUsed[StartSlot] && !attacking && !transforming && !dead)
        {
            startWalkUntil = Time.time + set.startWalk.length;
            playables[StartSlot].SetTime(0.0);
        }
        if (!moving) startWalkUntil = 0f;
        bool starting = moving && Time.time < startWalkUntil;

        // Nový útok: vyberie sa bežný úder alebo silnejší výpad (podľa DemonEnemy) a prehrá sa od začiatku
        if (attacking && !wasAttacking && !dead) BeginAttackClip();

        if (dead) targets[deathSlot] = 1f;
        else if (transforming && slotUsed[TransformSlot]) targets[TransformSlot] = 1f;
        else if (oneShotSlot >= 0 && Time.time < oneShotUntil) targets[oneShotSlot] = 1f;   // vytasenie / zasunutie zbrane
        else if (coverActive && !moving && slotUsed[CoverSlot]) targets[CoverSlot] = 1f;   // pri pohybe po stene ide chôdza
        else if (blocking && slotUsed[BlockSlot]) targets[BlockSlot] = 1f;
        else if (attackSlotInUse >= 0 && Time.time < attackUntil) targets[attackSlotInUse] = 1f;
        else if (isPlayer && slotUsed[JumpSlot] && Time.time < jumpUntil && playerMovement != null && !playerMovement.IsGrounded)
            targets[JumpSlot] = 1f;
        else if (crouchAmount > 0.5f && moving && slotUsed[CrouchWalkSlot]) targets[CrouchWalkSlot] = 1f;
        else if (crouchAmount > 0.5f && slotUsed[CrouchIdleSlot]) targets[CrouchIdleSlot] = 1f;
        else if (starting && slotUsed[StartSlot]) targets[StartSlot] = 1f;
        else if (moving && isPlayer && slotUsed[SprintSlot] && playerMovement != null && playerMovement.IsSprinting) targets[SprintSlot] = 1f;
        else if (moving && slotUsed[RunSlot]) targets[RunSlot] = 1f;
        else if (slotUsed[IdleSlot]) targets[IdleSlot] = 1f;

        if (isPlayer)
        {
            TickIdleHold(IdleSlot);
            TickIdleHold(CrouchIdleSlot);
        }

        // Tempo behu podľa skutočnej rýchlosti, aby nohy neklzali
        float runRef = Mathf.Max(0.1f, runClipSpeed > 0f ? runClipSpeed : set.runReferenceSpeed);
        float stride = Mathf.Clamp(speed / runRef, 0.6f, demon != null && demon.Scripted ? 2.2f : 2.5f);   // rútiaca sa bytosť z prológu má šprintový klip, ten netreba zrýchľovať
        if (slotUsed[RunSlot]) playables[RunSlot].SetSpeed(stride);
        if (slotUsed[CrouchWalkSlot]) playables[CrouchWalkSlot].SetSpeed(Mathf.Clamp(speed / Mathf.Max(0.1f, set.runReferenceSpeed), 0.6f, 1.6f));
        if (slotUsed[SprintSlot])
        {
            float sprintRef = Mathf.Max(0.1f, sprintClipSpeed > 0f ? sprintClipSpeed : set.sprintReferenceSpeed);
            playables[SprintSlot].SetSpeed(Mathf.Clamp(speed / sprintRef, 0.7f, 2f) * sprintAnimScale);
        }

        if (overlayReady)
        {
            // Horná vrstva sa zapne len kým beží vytasenie/zasunutie; ak hráč medzitým stojí, ostane mu celé telo z hlavných slotov
            float target = Time.time < overlayUntil && !dead ? 1f : 0f;
            overlayWeight = Mathf.MoveTowards(overlayWeight, target, dt / 0.12f);
            layerMixer.SetInputWeight(1, overlayWeight);
        }

        float step = dt / Mathf.Max(0.01f, set.crossFadeTime);
        float sum = 0f;
        for (int i = 0; i < SlotCount; i++)
        {
            if (!slotUsed[i]) continue;
            float slotStep = i == JumpSlot ? dt / (targets[i] > weights[i] ? JumpFadeIn : JumpFadeOut) : step;
            weights[i] = Mathf.MoveTowards(weights[i], targets[i], slotStep);
            sum += weights[i];
        }

        // Súčet váh musí byť 1, inak by sa kostra "stiahla" do bind pózy (T-pose)
        if (sum < 0.0001f)
        {
            int fallback = slotUsed[IdleSlot] ? IdleSlot : RunSlot;
            weights[fallback] = 1f;
            sum = 1f;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            if (!slotUsed[i]) continue;
            mixer.SetInputWeight(i, weights[i] / sum);

            // Cyklické klipy (Idle / Run) zacyklí aj vtedy, keď import nemá zapnuté Loop Time
            if (i == IdleSlot || i == RunSlot || i == SprintSlot)
            {
                AnimationClip clip = ClipOfSlot(i);
                if (clip != null && !clip.isLooping && playables[i].GetTime() >= clip.length)
                    playables[i].SetTime(0.0);
            }
        }
    }

    private float visualYVelocity;
    private const float PlayerLowerSpeed = 0.6f;       // hráč: najrýchlejšie spúšťanie modelu poistkou (m/s)
    private const float PlayerFloorFollowSpeed = 0.35f;   // hráč: výška modelu sa mení pomaly, aby rozdiel klipov (státie/beh) nevyzeral ako skok
    private const float FootDeadZone = 0.03f;    // chodidlo pod/nad zemou o menej než toto (m) sa nekoriguje
    private const float FootFixSpeed = 10f;      // najrýchlejšie dorovnanie poistkou (m/s)
    private Transform[] kneeBones;
    private Quaternion[] kneeRest;
    private Transform[] spineBones;

    [Tooltip("Hráč v státí: o koľko stupňov sa chrbtica predkloní (rozdelené na tri kosti), aby nebol zaklonený dozadu. 0 = vypnuté")]
    [Range(0f, 20f)] public float idleForwardLean = 6f;

    // Podiel klipu státia (Idle) v aktuálnom mixe (0..1)
    float IdleShare()
    {
        if (!slotUsed[IdleSlot]) return 0f;

        float sum = 0f;
        for (int i = 0; i < SlotCount; i++)
            if (slotUsed[i]) sum += weights[i];
        return sum > 0.001f ? weights[IdleSlot] / sum : 0f;
    }

    // Predklon chrbtice okolo osi "doprava" postavy (svetový priestor, takže nezáleží na orientácii kostí)
    void LeanForward(float degrees)
    {
        Quaternion tilt = Quaternion.AngleAxis(degrees / spineBones.Length, transform.right);
        for (int i = 0; i < spineBones.Length; i++)
            spineBones[i].rotation = tilt * spineBones[i].rotation;
    }

    [Tooltip("Pri čupnutí sa koleno ohýba len jednou osou (ako záves); skrútenie lýtka z klipu sa odstráni. 0 = vypnuté, 1 = plne")]
    [Range(0f, 1f)] public float crouchKneeHinge = 1f;
    [Tooltip("Ak nie je samostatný klip Crouch Idle, čupnutie v státí je zmrazený záber z chôdze v čupe: kde v klipe (0–1)")]
    [Range(0f, 1f)] public float crouchFreezeTime = 0f;

    // Koleno je záves: z rotácie voči pokojovej póze ostane len ohyb okolo osi X; ostatné osi (skrútenie lýtka) sa tlmia podľa podielu čupnutia
    void ConstrainKnees(float share)
    {
        for (int i = 0; i < kneeBones.Length; i++)
        {
            Quaternion delta = Quaternion.Inverse(kneeRest[i]) * kneeBones[i].localRotation;
            float flex = Mathf.DeltaAngle(0f, delta.eulerAngles.x);
            Quaternion hinge = kneeRest[i] * Quaternion.Euler(flex, 0f, 0f);
            kneeBones[i].localRotation = Quaternion.Slerp(kneeBones[i].localRotation, hinge, share * crouchKneeHinge);
        }
    }

    // Podiel klipov čupnutia v aktuálnom mixe (0..1)
    float CrouchShare()
    {
        if (!slotUsed[CrouchIdleSlot] && !slotUsed[CrouchWalkSlot]) return 0f;

        float sum = 0f;
        for (int i = 0; i < SlotCount; i++)
            if (slotUsed[i]) sum += weights[i];
        float crouch = (slotUsed[CrouchIdleSlot] ? weights[CrouchIdleSlot] : 0f) + (slotUsed[CrouchWalkSlot] ? weights[CrouchWalkSlot] : 0f);
        return sum > 0.001f ? crouch / sum : 0f;
    }

    [Tooltip("Hráč v státí / čupnutí: pauza (s) v základnej póze, kým sa idle animácia prehrá raz (náhodne od-do). Potom sa znova vráti do pauzy.")]
    public Vector2 idleHoldSeconds = new Vector2(4f, 9f);
    private readonly bool[] holdPlaying = new bool[SlotCount];
    private readonly float[] holdNext = new float[SlotCount];

    // Idle sa neprehráva v slučke ihneď za sebou: postava stojí v základnej póze (prvé okno klipu), po dlhšej nečinnosti sa klip prehrá raz a vráti sa do pauzy
    void TickIdleHold(int slot)
    {
        if (!slotUsed[slot] || (slot == IdleSlot && set.idle == null)) return;

        AnimationClip clip = ClipOfSlot(slot);
        if (clip == null) return;

        AnimationClipPlayable p = playables[slot];

        // Bez samostatného Crouch Idle je čupnutie v státí zmrazený záber z chôdze v čupe (nič sa neprehráva)
        if (slot == CrouchIdleSlot && set.crouchIdle == set.crouchWalk)
        {
            p.SetSpeed(0.0);
            p.SetTime(clip.length * crouchFreezeTime);
            return;
        }

        // Iný stav (beh, útok...): odpočet sa začína odznova
        if (targets[slot] < 0.5f)
        {
            holdPlaying[slot] = false;
            holdNext[slot] = Time.time + UnityEngine.Random.Range(idleHoldSeconds.x, idleHoldSeconds.y);
            p.SetSpeed(0.0);
            p.SetTime(0.0);
            return;
        }

        if (!holdPlaying[slot])
        {
            bool play = Time.time >= holdNext[slot];
            holdPlaying[slot] = play;
            p.SetSpeed(play ? 1.0 : 0.0);
            p.SetTime(0.0);
        }
        else if (p.GetTime() >= clip.length - 0.02)
        {
            holdPlaying[slot] = false;
            holdNext[slot] = Time.time + UnityEngine.Random.Range(idleHoldSeconds.x, idleHoldSeconds.y);
            p.SetSpeed(0.0);
            p.SetTime(0.0);
        }
    }

    // Klip na hornej polovici tela (útok za pohybu, zamáchnutie nástrojom); nohy idú ďalej podľa chôdze / státia
    void PlayOverlayClip(AnimationClip clip, float clipSpeed, float maxSeconds)
    {
        if (attackOverlay.IsValid()) { graph.Disconnect(overlayMixer, 2); attackOverlay.Destroy(); }
        attackOverlay = AnimationClipPlayable.Create(graph, clip);
        attackOverlay.SetSpeed(clipSpeed);
        graph.Connect(attackOverlay, 0, overlayMixer, 2);
        overlayMixer.SetInputWeight(0, 0f);
        overlayMixer.SetInputWeight(1, 0f);
        overlayMixer.SetInputWeight(2, 1f);
        overlayUntil = Time.time + Mathf.Min(clip.length / Mathf.Max(0.1f, clipSpeed), maxSeconds);
    }

    /// <summary>Zamáchnutie nástrojom (sekera) klipom útoku mečom; vráti false, ak graf nemá hornú vrstvu.</summary>
    public bool PlayToolSwing(AnimationClip clip, float seconds)
    {
        if (!overlayReady || clip == null || deathSlot >= 0 || mode != Mode.Playable) return false;
        PlayOverlayClip(clip, clip.length / Mathf.Max(0.2f, seconds), seconds + 0.1f);
        return true;
    }

    // Variant útoku: 0 = úder (attack), 1 = elbow, 2 = kop. Ak chýba klip variantu, použije sa prvý dostupný.
    void BeginAttackClip()
    {
        int variant = demon != null ? demon.AttackVariant : (playerCombat != null ? playerCombat.AttackVariant : 0);
        int slot = preparedSlot >= 0 ? preparedSlot : ResolveAttackSlot(variant);
        preparedSlot = -1;
        if (slot < 0) return;

        AnimationClip clip = ClipOfSlot(slot);
        float clipSpeed;
        if (isPlayer)
        {
            clipSpeed = PlayerAttackSpeed(clip, playerCombat != null && playerCombat.AttackIsHeavy, slot);
            playables[slot].SetSpeed(clipSpeed);
        }
        else clipSpeed = Mathf.Max(0.1f, (float)playables[slot].GetSpeed());

        // Útok za pohybu: horná polovica tela šermuje, nohy dobiehajú ďalej (postava sa nezastaví)
        if (isPlayer && overlayReady && speed > 0.5f && crouchAmount < 0.5f && !blocking && !coverActive && clip != null)
        {
            PlayOverlayClip(clip, clipSpeed, maxAttackClipSeconds);
            return;
        }

        attackSlotInUse = slot;
        playables[slot].SetTime(0.0);
        // Útok sa dohrá celý (aj po konci nabíjania), najviac maxAttackClipSeconds
        attackUntil = Time.time + Mathf.Min(clip.length / clipSpeed, maxAttackClipSeconds * (slot == AttackKickSlot ? unarmedKickSlowdown : 1f));
    }

    // ---------- Smrť ----------
    /// <summary>
    /// Spustí animáciu smrti (50 % pád dopredu, 50 % dozadu; ak je len jedna, tá). Vráti false, ak model nemá klip smrti
    /// (Damageable vtedy telo položí ako doteraz). Telo prestane blokovať hráča.
    /// </summary>
    private Transform[] preDeathFootBones;
    private float preDeathSoleOffset;
    private bool preDeathLockHips = true;

    /// <summary>Aktuálna sada klipov (na odvodenie upravenej sady, napr. rýchlejší beh).</summary>
    public DemonAnimationSet CurrentSet => set;

    /// <summary>Postava vstáva po scénar smrti (prológ): zruší stav smrti a vráti pôvodné zarovnanie výšky.</summary>
    public void Revive()
    {
        forceIdle = false;
        if (deathSlot < 0) return;
        deathSlot = -1;
        lockHipsHorizontal = preDeathLockHips;
        footBones = preDeathFootBones;
        soleOffset = preDeathSoleOffset;
        forceIdle = false;
        floorSnapPending = true;
        visualYVelocity = 0f;
    }

    public bool PlayDeath()
    {
        if (deathSlot >= 0) return true;
        if (mode != Mode.Playable) return false;

        bool forward = slotUsed[DeathFwdSlot], back = slotUsed[DeathBackSlot];
        if (!forward && !back) return false;

        if (forward && back) deathSlot = UnityEngine.Random.value < 0.5f ? DeathFwdSlot : DeathBackSlot;
        else deathSlot = forward ? DeathFwdSlot : DeathBackSlot;

        playables[deathSlot].SetTime(0.0);
        attackUntil = 0f;
        preDeathFootBones = footBones;
        preDeathSoleOffset = soleOffset;
        preDeathLockHips = lockHipsHorizontal;

        // Pád nie je "in place": boky sa nezamykajú. Výška sa dorovnáva podľa najnižšej kosti celého tela.
        lockHipsHorizontal = false;
        SwitchToBodyGrounding();
        if (!isPlayer) IgnorePlayerCollisions();   // hráčovo telo vlastnú kapsulu neignoruje
        return true;
    }

    void SwitchToBodyGrounding()
    {
        Transform[] bones = CollectRigBones();
        if (bones.Length == 0) return;

        footBones = bones;
        soleOffset = deathBodyThickness;
    }

    // Kosti kostry: Mixamo podľa názvu, inak (Humanoid z iného riggeru) kosti skinned meshov
    Transform[] CollectRigBones()
    {
        int count = 0;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name.IndexOf("mixamorig", StringComparison.Ordinal) >= 0) count++;

        if (count > 0)
        {
            Transform[] bones = new Transform[count];
            int i = 0;
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf("mixamorig", StringComparison.Ordinal) >= 0) bones[i++] = t;
            return bones;
        }

        var unique = new System.Collections.Generic.HashSet<Transform>();
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (Transform bone in smr.bones)
                if (bone != null) unique.Add(bone);

        Transform[] result = new Transform[unique.Count];
        unique.CopyTo(result);
        return result;
    }

    // Mŕtve telo nesmie blokovať hráča: kapsula ignoruje jeho kolízie (zostáva pre lúče, napr. nájdenie tela strážou)
    void IgnorePlayerCollisions()
    {
        if (capsule == null) return;
        PlayerStats player = FindAnyObjectByType<PlayerStats>();
        if (player == null) return;

        foreach (Collider c in player.GetComponentsInChildren<Collider>())
            Physics.IgnoreCollision(capsule, c, true);
    }

    void UpdateLegacy(bool moving, bool attacking)
    {
        string desired = attacking && attackClip != null ? attackClip : (moving ? runClip : idleClip);
        if (desired == current) return;

        if (desired == attackClip) legacy.Play(desired, PlayMode.StopAll);
        else legacy.CrossFade(desired, crossFade);
        current = desired;
    }

    void UpdateController(bool moving, bool attacking)
    {
        if (hasSpeed) animator.SetFloat(speedHash, speed);
        if (hasMove) animator.SetBool(moveHash, moving);
        if (!hasAttack) return;

        if (attackIsTrigger)
        {
            if (attacking && !wasAttacking) animator.SetTrigger(attackHash);
        }
        else animator.SetBool(attackHash, attacking);
    }

    // Bez klipov: dýchanie vo voľnoväzbe, kolísanie a náklon pri chôdzi, výpad dopredu pri nabíjaní úderu
    void UpdateProcedural(float dt, bool moving, bool attacking)
    {
        phase += dt * (moving ? 9f : 1.8f);

        float bob = moving ? Mathf.Abs(Mathf.Sin(phase)) * 0.08f : Mathf.Sin(phase) * 0.02f;
        float lean = moving ? 8f : 0f;
        float roll = moving ? Mathf.Sin(phase) * 4f : 0f;
        float lunge = attacking ? 0.25f : 0f;

        Vector3 targetPos = basePosition + new Vector3(0f, bob, lunge);
        Quaternion targetRot = Quaternion.Euler(attacking ? 18f : lean, 0f, roll) * baseRotation;

        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, 14f * dt);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot, 14f * dt);
    }
}
