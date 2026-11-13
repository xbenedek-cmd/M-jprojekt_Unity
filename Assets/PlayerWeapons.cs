using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Zbraň v ruke: model meča (Meč.glb) sa pripevní na kosť pravej ruky, zasunutý meč visí na chrbte.
/// Q = vytasiť / zasunúť (hrá sa animácia Draw / Sheath), koliesko myši = ďalšia zbraň z inventára,
/// pravé tlačidlo myši = blok s vytasenou zbraňou. Pri vytasení sa vymení animačná sada (s mečom / bez zbrane).
/// Zbrane a lektvary spravuje Inventory.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
[DefaultExecutionOrder(100)]   // LateUpdate po animátore, aby kolísanie meča sedelo na aktuálnu pózu ruky
public class PlayerWeapons : MonoBehaviour
{
    [Header("Model meča")]
    public GameObject swordModel;
    [Tooltip("Voliteľný materiál (textúra z modelu). Prázdne = materiál z modelu.")]
    public Material swordMaterial;
    [Tooltip("Dĺžka meča v postave (m); model sa na ňu zmenší/zväčší")]
    public float swordLength = 1.0f;
    [Tooltip("Kde je čepeľ na najdlhšej osi modelu: -1 = v zápornom konci, +1 = v kladnom. Meč.glb má čepeľ v zápornom konci; ak je v ruke opačne, zmeň na +1.")]
    public float bladeSign = -1f;
    [Tooltip("Vzdialenosť rukoväte od stredu meča ako zlomok dĺžky")]
    [Range(0f, 0.5f)] public float gripFromCenter = 0.31f;
    [Tooltip("Sila podfarbenia podľa zbrane (0 = bez zmeny)")]
    [Range(0f, 1f)] public float weaponTint = 0.3f;

    [Header("Meč v ruke (priestor kosti pravej ruky)")]
    [Tooltip("Smer čepele voči kosti ruky. Mixamo: +Y je pozdĺž prstov.")]
    public Vector3 handBladeDirection = Vector3.up;
    [Tooltip("Natočenie meča okolo čepele (plocha čepele), stupne")]
    public float handRoll = 0f;
    public Vector3 handOffset = Vector3.zero;
    [Tooltip("Veľkosť meča v ruke voči Dĺžke meča (1 = celá). Kosť ruky môže model zväčšovať, preto sa mierka kompenzuje.")]
    [Range(0.2f, 1.5f)] public float handSwordScale = 0.6f;

    [Header("Meč na chrbte (priestor postavy)")]
    [Tooltip("Poloha rukoväti v priestore postavy: za pravým ramenom (x vpravo, y hore od stredu tela, z dozadu). Meč sa pripevní na kosť chrbtice a ide s ňou.")]
    public Vector3 backHilt = new Vector3(0.13f, 0.5f, -0.28f);
    [Tooltip("Smer čepele z rukoväte: šikmo dole a doľava popri chrbte. Kladné z vedie čepeľ smerom dole k chrbtu (chrbát sa dole zužuje); so záporným z bola čepeľ od tela odsadená až 25 cm.")]
    public Vector3 backDirection = new Vector3(-0.35f, -0.9f, 0.10f);
    public float backRollDegrees = 0f;

    [Header("Pohyb meča (dýchanie, kolísanie pri pohybe)")]
    [Tooltip("Celková sila efektu (0 = meč úplne statický)")]
    public float swayStrength = 1f;
    public float breathDegrees = 1.2f;
    public float breathSpeed = 1.6f;
    [Tooltip("Hojdanie meča v rytme krokov pri behu (stupne)")]
    public float walkSwayDegrees = 5f;
    [Tooltip("Stupne vychýlenia na 1 m/s² zrýchlenia postavy")]
    public float accelSwayDegrees = 0.35f;
    public float maxSwayDegrees = 14f;
    public float swayStiffness = 90f;
    public float swayDamping = 9f;

    [Header("Animácie")]
    [Tooltip("Prázdne = sada z PlayerVisual")]
    public DemonAnimationSet unarmedSet;
    public DemonAnimationSet swordSet;
    [Range(0f, 1f)] public float drawAttachFraction = 0.35f;
    [Range(0f, 1f)] public float sheathDetachFraction = 0.6f;

    [Header("Blok (pravé tlačidlo myši)")]
    public float blockStaminaPerSecond = 6f;

    private enum Pending { None, AttachHand, AttachBack }

    private PlayerStats stats;
    private Inventory inventory;
    private PlayerVisual visual;
    private DemonModelAnimator animator;

    private Transform holder;
    private Renderer[] swordRenderers;
    private MaterialPropertyBlock tintBlock;
    private Quaternion handRotation, backRotation;
    private Vector3 backBoneOffset;        // poloha meča voči chrbtici, vo svetových dĺžkach v natočení kosti
    private Vector3 lastBackPosition;
    private Transform backFollowBone;      // meč na chrbte nie je dieťa kosti: kosť mu dáva len polohu a natočenie (mierka kosti ho nedeformuje)
    private Quaternion backLocalRotation;
    private bool backCached;

    // Pohyb meča
    private Quaternion baseLocalRotation = Quaternion.identity;
    private bool onBack;
    private Vector3 lastPosition, lastVelocity, smoothedAcceleration;
    private Vector2 swayAngle, swayVelocity;
    private float walkPhase;

    private bool drawn;
    private bool blocking;
    private float busyUntil;
    private Pending pending;
    private float pendingAt;
    private float switchSetAt = -1f;

    /// <summary>Zbraň z inventára je vytasená (v ruke).</summary>
    public bool IsDrawn => drawn;
    /// <summary>Práve sa vyťahuje / zasúva zbraň: útok nie je možný.</summary>
    public bool IsBusy => Time.time < busyUntil;
    public bool IsBlocking => blocking;
    /// <summary>Zbraň, ktorou sa práve bojuje (päste, ak nič nie je v ruke).</summary>
    public ItemDef Current => drawn && inventory != null && inventory.EquippedWeapon != null ? inventory.EquippedWeapon : ItemCatalog.Fists;
    public bool IsArmed => drawn && inventory != null && inventory.EquippedWeapon != null;

    private Vector3 bladeTipLocal;

    /// <summary>Držiak meča v ruke a poloha hrotu v jeho priestore (pre stopu čepele).</summary>
    public bool TryGetBlade(out Transform bladeHolder, out Vector3 localTip, out float worldLength)
    {
        bladeHolder = holder;
        localTip = bladeTipLocal;
        worldLength = holder != null ? bladeTipLocal.magnitude * holder.lossyScale.x : 0f;
        return holder != null && !onBack && holder.gameObject.activeInHierarchy;
    }

#if UNITY_EDITOR
    public const string SwordModelPath = "Assets/Models/Player/Sword/Meč.glb";
    public const string SwordMaterialPath = "Assets/Materials/SwordMaterial.mat";
    public const string SwordSetPath = "Assets/Animations/PlayerSwordSet.asset";

    void Reset() => AutoAssign();
    void OnValidate() => AutoAssign();

    void AutoAssign()
    {
        if (swordModel == null) swordModel = AssetDatabase.LoadAssetAtPath<GameObject>(SwordModelPath);
        if (swordMaterial == null) swordMaterial = AssetDatabase.LoadAssetAtPath<Material>(SwordMaterialPath);
        if (swordSet == null) swordSet = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(SwordSetPath);
        if (unarmedSet == null) unarmedSet = AssetDatabase.LoadAssetAtPath<DemonAnimationSet>(PlayerVisual.UnarmedSetPath);
    }
#endif

    void Awake()
    {
#if UNITY_EDITOR
        AutoAssign();
#endif
    }

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        inventory = GetComponent<Inventory>();
        if (inventory == null) inventory = gameObject.AddComponent<Inventory>();
        visual = GetComponent<PlayerVisual>();
        animator = visual != null ? visual.ModelAnimator : null;
        if (unarmedSet == null && visual != null) unarmedSet = visual.animationSet;

        BuildSword();
        inventory.Changed += OnInventoryChanged;
        PlayerStats.OnFormChanged += OnFormChanged;

        // Na začiatku je vybraná zbraň rovno v ruke
        drawn = inventory.EquippedWeapon != null;
        ApplySet(drawn ? swordSet : unarmedSet);
        if (drawn) MountHand(); else MountBack();
        ApplyTint();
    }

    void OnDestroy()
    {
        if (inventory != null) inventory.Changed -= OnInventoryChanged;
        PlayerStats.OnFormChanged -= OnFormChanged;
        if (stats != null) stats.IsBlocking = false;
    }

    // ---------- Model meča ----------
    void BuildSword()
    {
        if (swordModel == null) return;

        // Meranie na čistom mieste so identitou, aby osi bounds sedeli s osami modelu
        GameObject instance = Instantiate(swordModel);
        LightMeshTable.Apply(instance);
        instance.name = "PlayerSwordModel";
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        NPCPerception.StripNonVisual(instance);

        swordRenderers = instance.GetComponentsInChildren<Renderer>(true);
        if (swordRenderers.Length == 0)
        {
            Debug.LogWarning("PlayerWeapons: model meča nemá žiadny Renderer.", this);
            Destroy(instance);
            swordRenderers = null;
            return;
        }

        Bounds bounds = swordRenderers[0].bounds;
        for (int i = 1; i < swordRenderers.Length; i++) bounds.Encapsulate(swordRenderers[i].bounds);

        foreach (Renderer r in swordRenderers)
        {
            r.enabled = true;
            if (swordMaterial == null) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = swordMaterial;
            r.sharedMaterials = mats;
        }

        // Najdlhšia os = os meča; čepeľ je na jej konci podľa bladeSign
        Vector3 size = bounds.size;
        int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        Vector3 axisVector = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        float length = Mathf.Max(0.001f, size[axis]);
        float scale = swordLength / length;
        Vector3 bladeModel = axisVector * Mathf.Sign(bladeSign == 0f ? -1f : bladeSign);

        // Rukoväť je opačne než čepeľ, kúsok od stredu. Držiak má pivot v rukoväti, takže sa meč otáča okolo ruky.
        Vector3 gripPoint = bounds.center - bladeModel * (length * gripFromCenter);

        holder = new GameObject("PlayerSword").transform;
        instance.transform.SetParent(holder, false);
        instance.transform.localScale = Vector3.one * scale;
        instance.transform.localPosition = -gripPoint * scale;
        bladeTipLocal = bladeModel * (length * (0.5f + gripFromCenter) * scale);

        handRotation = Quaternion.AngleAxis(handRoll, handBladeDirection.normalized)
                       * Quaternion.FromToRotation(bladeModel, handBladeDirection.normalized);
        backRotation = Quaternion.AngleAxis(backRollDegrees, backDirection.normalized)
                       * Quaternion.FromToRotation(bladeModel, backDirection.normalized);

        Debug.Log($"PlayerWeapons: meč dlhý {length:0.##} (os {(axis == 0 ? "X" : axis == 1 ? "Y" : "Z")}), mierka {scale:0.###}. " +
                  "Ak je čepeľ v ruke otočená naopak, zmeň 'bladeSign'.");
    }

    void MountHand()
    {
        if (holder == null) return;
        Transform hand = visual != null ? visual.RightHand : null;

        if (hand != null)
        {
            holder.SetParent(hand, false);
            holder.localPosition = handOffset;
            holder.localRotation = handRotation;
            // Svetová veľkosť meča = Dĺžka × handSwordScale bez ohľadu na mierku kosti ruky (model je zväčšený/zmenšený na výšku kapsuly)
            holder.localScale = Vector3.one * (handSwordScale / Mathf.Max(0.0001f, hand.lossyScale.x));
        }
        else
        {
            // Model bez kostry: meč pri pravom boku postavy
            holder.SetParent(transform, false);
            holder.localPosition = new Vector3(0.45f, 0.1f, 0.35f);
            holder.localRotation = handRotation;
            holder.localScale = Vector3.one * (handSwordScale / Mathf.Max(0.0001f, transform.lossyScale.x));
        }
        baseLocalRotation = handRotation;
        backFollowBone = null;
        onBack = false;
        holder.gameObject.SetActive(true);
    }

    void MountBack()
    {
        if (holder == null) return;

        // Meč na chrbte sa nezobrazí, ak hráč nemá žiadnu zbraň v inventári
        Transform spine = visual != null ? visual.BackMount : null;
        if (spine != null)
        {
            // Poloha a natočenie sa raz prepočítajú z priestoru postavy do priestoru kosti (Spine2); potom meč ide s chrbticou aj pri čupnutí
            if (!backCached)
            {
                backBoneOffset = Quaternion.Inverse(spine.rotation) * (transform.TransformPoint(backHilt) - spine.position);
                backLocalRotation = Quaternion.Inverse(spine.rotation) * (transform.rotation * backRotation);
                backCached = true;
            }

            // Meč je pod koreňom postavy a v LateUpdate sleduje chrbticu (poloha + natočenie). Ako dieťa kosti by ho pri niektorých klipoch
            // (napr. otočný kop) rozhodila mierka / poloha kosti a na zlomok sekundy by odletel.
            holder.SetParent(transform, false);
            holder.localScale = Vector3.one / Mathf.Max(0.0001f, transform.lossyScale.x);   // na chrbte plná veľkosť
            backFollowBone = spine;
            lastBackPosition = spine.position + spine.rotation * backBoneOffset;
            holder.position = lastBackPosition;
            holder.rotation = spine.rotation * backLocalRotation;
            baseLocalRotation = backLocalRotation;
        }
        else
        {
            // Model bez kostry: meč na koreni postavy
            holder.SetParent(transform, false);
            holder.localPosition = backHilt;
            holder.localRotation = backRotation;
            backFollowBone = null;
            holder.localScale = Vector3.one / Mathf.Max(0.0001f, transform.lossyScale.x);
            baseLocalRotation = backRotation;
        }

        onBack = true;
        holder.gameObject.SetActive(inventory != null && HasAnyWeapon() && !DemonForm);
    }

    // ---------- Živý pohyb meča ----------
    // Meč nie je prilepený: kýva sa za zrýchlením postavy (pružina), dýcha a pri chôdzi sa v rytme krokov hojdá.
    // Počíta sa vo svetových osiach postavy, takže to funguje rovnako v ruke (kosť) aj na chrbte.
    void LateUpdate()
    {
        if (holder == null || !holder.gameObject.activeSelf || stats == null || holder.parent == null) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 position = transform.position;
        Vector3 velocity = (position - lastPosition) / dt;
        lastPosition = position;
        if (velocity.sqrMagnitude > 900f) velocity = Vector3.zero;   // teleport (väzenie...) nie je rýchlosť

        Vector3 acceleration = Vector3.ClampMagnitude((velocity - lastVelocity) / dt, 40f);
        lastVelocity = velocity;
        smoothedAcceleration = Vector3.Lerp(smoothedAcceleration, acceleration, 12f * dt);

        Vector3 local = transform.InverseTransformDirection(smoothedAcceleration);
        float speed01 = Mathf.Clamp01(new Vector2(velocity.x, velocity.z).magnitude / 6f);

        // Pružina: zrýchlenie dopredu skloní čepeľ dozadu, zrýchlenie do strany ju nakloní
        float targetPitch = Mathf.Clamp(-local.z * accelSwayDegrees, -maxSwayDegrees, maxSwayDegrees);
        float targetRoll = Mathf.Clamp(local.x * accelSwayDegrees, -maxSwayDegrees, maxSwayDegrees);
        swayVelocity.x += ((targetPitch - swayAngle.x) * swayStiffness - swayVelocity.x * swayDamping) * dt;
        swayVelocity.y += ((targetRoll - swayAngle.y) * swayStiffness - swayVelocity.y * swayDamping) * dt;
        swayAngle += swayVelocity * dt;

        // Dýchanie a kroky
        walkPhase += dt * (3f + 5f * speed01);
        float time = Time.time;
        float breathPitch = Mathf.Sin(time * breathSpeed) * breathDegrees;
        float breathRoll = Mathf.Cos(time * breathSpeed * 0.8f) * breathDegrees * 0.6f;
        float stepPitch = Mathf.Sin(walkPhase * 2f) * walkSwayDegrees * speed01;
        float stepRoll = Mathf.Sin(walkPhase) * walkSwayDegrees * 1.2f * speed01;

        float pitch = (swayAngle.x + breathPitch + stepPitch) * swayStrength;
        float roll = (swayAngle.y + breathRoll + stepRoll) * swayStrength;

        Quaternion sway = Quaternion.AngleAxis(pitch, transform.right) * Quaternion.AngleAxis(roll, transform.forward);
        bool followBone = onBack && backFollowBone != null;
        Quaternion parentRotation = followBone ? backFollowBone.rotation : holder.parent.rotation;
        holder.rotation = sway * (parentRotation * baseLocalRotation);

        // Meč na chrbte sa navyše jemne nadskakuje v rytme krokov
        if (onBack)
        {
            if (followBone)
            {
                // Ochrana: ak kosť na jeden snímok skočí ďaleko od postavy (chyba klipu), meč ostane na poslednej dobrej polohe
                Vector3 wanted = backFollowBone.position + backFollowBone.rotation * backBoneOffset;
                if ((wanted - transform.position).sqrMagnitude < 2.2f * 2.2f) lastBackPosition = wanted;
                holder.position = lastBackPosition;
            }
            else holder.localPosition = backHilt;
            float bob = Mathf.Sin(walkPhase * 2f) * 0.012f * speed01 * swayStrength + Mathf.Sin(time * breathSpeed) * 0.004f;
            holder.position += transform.up * bob;
        }
    }

    bool HasAnyWeapon()
    {
        for (int i = 0; i < Inventory.WeaponSlots; i++)
            if (!inventory.GetWeapon(i).IsEmpty) return true;
        return false;
    }

    void ApplyTint()
    {
        ItemDef w = inventory != null ? inventory.EquippedWeapon : null;
        if (swordRenderers == null || w == null || weaponTint <= 0f) return;

        if (tintBlock == null) tintBlock = new MaterialPropertyBlock();
        tintBlock.SetColor("_Color", Color.Lerp(Color.white, w.color, weaponTint));
        foreach (Renderer r in swordRenderers) r.SetPropertyBlock(tintBlock);
    }

    void ApplySet(DemonAnimationSet set)
    {
        if (animator != null && set != null) animator.SetAnimationSet(set);
    }

    // ---------- Vytasenie / zasunutie ----------
    public void ToggleDrawn()
    {
        if (IsBusy || stats.IsDead) return;
        if (DemonForm) { NoWeaponsMessage(); return; }
        if (drawn) Sheath(); else Draw();
    }

    void Draw()
    {
        // Žiadna vybraná zbraň: vezme sa prvá z inventára
        if (inventory.EquippedWeapon == null)
        {
            int first = -1;
            for (int i = 0; i < Inventory.WeaponSlots && first < 0; i++)
                if (!inventory.GetWeapon(i).IsEmpty) first = i;

            if (first < 0) { GameHUD.Message("Nemáš žiadnu zbraň.", 1.8f); return; }
            inventory.SetEquippedWeapon(first);
        }

        drawn = true;
        ApplySet(swordSet);
        ApplyTint();

        float length = animator != null ? animator.PlayOneShot(DemonModelAnimator.OneShot.Draw) : 0f;
        if (length <= 0f)
        {
            MountHand();
            return;
        }

        busyUntil = Time.time + length;
        pending = Pending.AttachHand;
        pendingAt = Time.time + length * drawAttachFraction;
        switchSetAt = -1f;
    }

    void Sheath()
    {
        drawn = false;
        SetBlocking(false);

        float length = animator != null ? animator.PlayOneShot(DemonModelAnimator.OneShot.Sheath) : 0f;
        if (length <= 0f)
        {
            MountBack();
            ApplySet(unarmedSet);
            return;
        }

        busyUntil = Time.time + length;
        pending = Pending.AttachBack;
        pendingAt = Time.time + length * sheathDetachFraction;
        switchSetAt = Time.time + length;   // klip zasunutia je v sade s mečom, preto sa sada vymení až po ňom
    }

    /// <summary>Volá inventár: klik na zbraň. Vybranú vytasí / zasunie, inú vyberie a vytasí; -1 = päste.</summary>
    public void EquipFromInventory(int slot)
    {
        if (stats.IsDead || IsBusy) return;
        if (DemonForm && slot >= 0) { NoWeaponsMessage(); return; }

        if (slot < 0)
        {
            if (drawn) Sheath();
            inventory.SetEquippedWeapon(-1);
            return;
        }

        if (slot == inventory.EquippedWeaponIndex)
        {
            ToggleDrawn();
            return;
        }

        inventory.SetEquippedWeapon(slot);
        if (!drawn) Draw();
    }

    // Démon zbrane nedrží: meč sa zasunie a skryje, údery päsťami sú silnejšie (PlayerCombat)
    bool DemonForm => stats != null && stats.IsDemon;

    static void NoWeaponsMessage() => GameHUD.Message("Démon nemôže držať zbrane – bije päsťami.", 2f);

    void OnFormChanged(PlayerForm form)
    {
        if (this == null || inventory == null) return;

        if (form == PlayerForm.Demon)
        {
            drawn = false;
            pending = Pending.None;
            switchSetAt = -1f;
            busyUntil = 0f;
            SetBlocking(false);
            ApplySet(unarmedSet);
            MountBack();   // skryje sa, lebo DemonForm
        }
        else if (!drawn)
        {
            MountBack();
        }
    }

    void OnInventoryChanged()
    {
        ItemDef w = inventory.EquippedWeapon;

        // Vybraná zbraň zmizla (zahodená): meč sa zasunie
        if (w == null && drawn)
        {
            drawn = false;
            SetBlocking(false);
            MountBack();
            ApplySet(unarmedSet);
            return;
        }

        // Viditeľnosť meča na chrbte závisí od toho, či nejaký meč vlastníš (nie počas zasúvania: to dokončí časovač)
        if (!drawn && pending == Pending.None) MountBack();
        ApplyTint();
    }

    // ---------- Blok ----------
    void SetBlocking(bool value)
    {
        if (blocking == value) return;
        blocking = value;
        stats.IsBlocking = value;
        if (animator != null) animator.SetBlocking(value);
    }

    // ---------- Update ----------
    void Update()
    {
        if (stats == null || inventory == null) return;

        if (stats.IsDead)
        {
            SetBlocking(false);
            return;
        }

        if (pending != Pending.None && Time.time >= pendingAt)
        {
            if (pending == Pending.AttachHand) MountHand(); else MountBack();
            pending = Pending.None;
        }
        if (switchSetAt >= 0f && Time.time >= switchSetAt)
        {
            switchSetAt = -1f;
            if (!drawn) ApplySet(unarmedSet);
        }

        bool inputFree = !Healer.MenuOpen && Cursor.lockState == CursorLockMode.Locked;

        // Koliesko myši: ďalšia / predchádzajúca zbraň
        if (inputFree && !IsBusy)
        {
            float scroll = GameInput.mouseScrollDelta.y;
            if (scroll > 0.5f) inventory.CycleWeapon(1);
            else if (scroll < -0.5f) inventory.CycleWeapon(-1);
        }

        // Blok: pravé tlačidlo myši, len s vytasenou zbraňou a kým je výdrž
        bool wantBlock = inputFree && drawn && !IsBusy && GameInput.GetMouseButton(1);
        if (wantBlock && !stats.UseStamina(blockStaminaPerSecond * Time.deltaTime)) wantBlock = false;
        SetBlocking(wantBlock);
    }
}
