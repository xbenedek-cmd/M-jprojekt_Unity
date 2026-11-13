using UnityEngine;

/// <summary>
/// Kamera z tretej osoby bez trasenia:
///  - sleduje interpolovaného hráča (Rigidbody Interpolate),
///  - kolízie ignorujú NPC a nepriateľov (predtým ich čiary kamerou "trhali"),
///  - priblíženie pri stene je okamžité, odďaľovanie plynulé.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Cieľ")]
    public Transform target;                    // Náš Hráč
    public float pivotHeight = 1.5f;            // výška bodu, na ktorý sa kamera pozerá

    [Header("Vzdialenosť")]
    public float distance = 8.5f;
    [Tooltip("Najmenšia a najväčšia vzdialenosť pri zoome PageUp/PageDown (m)")]
    public Vector2 zoomRange = new Vector2(4f, 16f);
    [Tooltip("Pri štarte sa vzdialenosť zvýši aspoň na túto hodnotu (prepíše starú uloženú 6 m zo scény); neskôr platí zvolený zoom")]
    public float minStartDistance = 8.5f;
    [Tooltip("0 = kamera je pevne prichytená na hráča. Malá hodnota (0.03-0.08) = jemné dobiehanie.")]
    public float positionSmoothTime = 0.04f;

    [Header("Otáčanie myšou")]
    public float sensitivity = 3f;
    public float minPitch = -20f;
    public float maxPitch = 60f;
    public bool lockCursor = true;
    [Tooltip("Najväčší delta myši za frame; chráni pred skokom kamery po zamknutí kurzora alebo pri záseku")]
    public float maxMouseDelta = 8f;
    [Tooltip("Pri väčšom skoku cieľa než táto vzdialenosť (m) sa kamera prichytí okamžite")]
    public float teleportSnapDistance = 6f;

    [Header("Kolízie")]
    public float collisionPadding = 0.6f;
    [Tooltip("Orezávanie hlavnej kamery (platí pri štarte, prepíše hodnoty z Inspectora); malý pomer Near/Far = menej blikania")]
    public float nearClip = 0.05f;
    public float farClip = 300f;
    public float zoomOutSpeed = 6f;

    private float yaw;
    private float pitch = 15f;
    private float currentDistance;
    private Vector3 smoothedPivot;
    private Vector3 pivotVelocity;
    private bool initialized;
    private bool wasLookActive;
    private int skipMouseFrames;
    private float nextTargetSearch;
    private float nextCameraCheck;
    private Transform checkedTarget;
    private bool targetIsPlayer;
    private readonly Camera[] cameraBuffer = new Camera[8];

    // Väčší buffer: RaycastNonAlloc nevracia zásahy zoradené, v hustej scéne by sa najbližšia stena mohla stratiť
    private static readonly RaycastHit[] hitBuffer = new RaycastHit[64];

    /// <summary>Otočí pohľad okolo hráča o daný uhol v stupňoch (portál: hráč vyjde tak, ako vošiel).</summary>
    public void Turn(float deltaYaw) => yaw += deltaYaw;

    void Start()
    {
        Camera own = GetComponent<Camera>();
        if (own == null) own = Camera.main;
        if (own != null) { own.nearClipPlane = nearClip; own.farClipPlane = farClip; }
        collisionPadding = Mathf.Max(collisionPadding, 0.6f);   // kamera ostane od steny ďalej ako je Near

        distance = Mathf.Clamp(PlayerPrefs.GetFloat("camera_distance", Mathf.Max(distance, minStartDistance)), zoomRange.x, zoomRange.y);

        yaw = transform.eulerAngles.y;
        currentDistance = distance;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void LateUpdate()
    {
        if (!ResolvePlayerTarget()) return;

        EnsureSingleCamera();
        HandleCursor();
        HandleZoom();

        bool lookActive = Cursor.lockState == CursorLockMode.Locked && !Healer.MenuOpen;
        if (lookActive && !wasLookActive) skipMouseFrames = 2;   // po zamknutí kurzora / zatvorení menu je prvý delta myši skok
        wasLookActive = lookActive;

        if (lookActive)
        {
            if (skipMouseFrames > 0) skipMouseFrames--;
            else
            {
                yaw += Mathf.Clamp(GameInput.GetAxis("Mouse X"), -maxMouseDelta, maxMouseDelta) * sensitivity;
                pitch -= Mathf.Clamp(GameInput.GetAxis("Mouse Y"), -maxMouseDelta, maxMouseDelta) * sensitivity;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
        }

        // Pivot (bod nad hráčom) - jemne vyhladený
        Vector3 rawPivot = target.position + Vector3.up * (pivotHeight - PlayerStealth.CameraDrop);   // pri čupnutí nižšie
        // Teleport (väzenie, prepustenie, Teleport hráča): kamera sa nemá "doťahovať" cez celú mapu
        bool teleported = (rawPivot - smoothedPivot).sqrMagnitude > teleportSnapDistance * teleportSnapDistance;
        if (!initialized || positionSmoothTime <= 0f || teleported)
        {
            pivotVelocity = Vector3.zero;
            smoothedPivot = rawPivot;
            initialized = true;
        }
        else
        {
            // Vodorovne je kamera pevne na hráčovi: dobiehanie (SmoothDamp) pri šprinte kolísalo s dĺžkou snímku a pôsobilo ako trasenie.
            // Jemne sa vyhladzuje len výška (skok, schody, čupnutie).
            float y = Mathf.SmoothDamp(smoothedPivot.y, rawPivot.y, ref pivotVelocity.y, positionSmoothTime * 2f);
            smoothedPivot = new Vector3(rawPivot.x, y, rawPivot.z);
        }

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 backDir = rotation * Vector3.back;

        // Kolízia len so statickým svetom (nie s NPC/nepriateľmi)
        float wanted = distance;
        float blocked = FindBlockingDistance(smoothedPivot, backDir, distance + collisionPadding);
        if (blocked >= 0f)
            wanted = Mathf.Max(0.6f, blocked - collisionPadding);

        currentDistance = wanted < currentDistance
            ? wanted                                                           // k stene okamžite
            : Mathf.MoveTowards(currentDistance, wanted, zoomOutSpeed * Time.deltaTime);   // od steny plynulo

        transform.position = smoothedPivot + backDir * currentDistance + CombatFx.ShakeOffset;
        transform.rotation = rotation;
    }

    // Kamera sleduje VÝHRADNE hráča (objekt s PlayerStats). Ak by target ukazoval inam (napr. na démona), prepojí sa späť.
    bool ResolvePlayerTarget()
    {
        if (target != checkedTarget)
        {
            checkedTarget = target;
            targetIsPlayer = target != null && target.GetComponentInParent<PlayerStats>() != null;
        }
        if (target != null && targetIsPlayer) return true;

        // Hráča hľadá zriedka, nie každý frame
        if (Time.unscaledTime < nextTargetSearch) return false;
        nextTargetSearch = Time.unscaledTime + 1f;

        PlayerStats player = FindAnyObjectByType<PlayerStats>();
        if (player == null) return false;

        if (target != null) Debug.LogWarning($"CameraFollow: cieľ '{target.name}' nie je hráč, kamera sa vracia na '{player.name}'.");
        target = player.transform;
        checkedTarget = target;
        targetIsPlayer = true;
        return true;
    }

    // Hlavná kamera je len táto. Iné kamery, ktoré kreslia na obrazovku (nie do textúry ako minimapa), sa vypnú.
    void EnsureSingleCamera()
    {
        if (Time.unscaledTime < nextCameraCheck) return;
        nextCameraCheck = Time.unscaledTime + 2f;

        int count = Camera.GetAllCameras(cameraBuffer);
        for (int i = 0; i < count; i++)
        {
            Camera other = cameraBuffer[i];
            if (other == null || other.gameObject == gameObject || other.targetTexture != null || other.GetComponentInParent<MenuSanctuary>() != null) continue;

            Debug.LogWarning($"CameraFollow: vypínam cudziu kameru '{other.name}' (hlavná kamera je len '{name}').");
            other.enabled = false;
        }
    }

    float FindBlockingDistance(Vector3 origin, Vector3 dir, float maxDist)
    {
        int count = Physics.RaycastNonAlloc(origin, dir, hitBuffer, maxDist, ~0, QueryTriggerInteraction.Ignore);
        float best = -1f;

        for (int i = 0; i < count; i++)
        {
            Collider c = hitBuffer[i].collider;

            // Ignoruj hráča, všetko s Rigidbody a všetkých NPC / nepriateľov / zdroje krvi
            if (c.transform == target || c.transform.IsChildOf(target)) continue;
            if (c.attachedRigidbody != null) continue;
            if (c.GetComponentInParent<NPCPerception>() != null) continue;
            if (c.GetComponentInParent<Damageable>() != null) continue;
            if (c.GetComponentInParent<BloodSource>() != null) continue;

            float d = hitBuffer[i].distance;
            if (best < 0f || d < best) best = d;
        }
        return best;
    }

    // PageUp / PageDown (alebo numerické + / -) oddialia a priblížia kameru; koliesko myši ostáva na prepínanie zbraní. Hodnota sa pamätá.
    void HandleZoom()
    {
        if (Healer.MenuOpen || GameHUD.MenuOpen || MangaDialog.Active) return;

        float direction = 0f;
        if (GameInput.GetKey(KeyCode.PageDown) || GameInput.GetKey(KeyCode.KeypadMinus)) direction += 1f;
        if (GameInput.GetKey(KeyCode.PageUp) || GameInput.GetKey(KeyCode.KeypadPlus)) direction -= 1f;
        if (direction == 0f) return;

        distance = Mathf.Clamp(distance + direction * 6f * Time.unscaledDeltaTime, zoomRange.x, zoomRange.y);
        PlayerPrefs.SetFloat("camera_distance", distance);
    }

    // Esc uvoľní kurzor, kliknutie ho znova zamkne
    void HandleCursor()
    {
        // Menu HUD, parcely a príroda démona si riadia kurzor samy (inak by kliknutie na tlačidlo hneď zamklo kurzor a tlačidlo by nezareagovalo)
        if (Healer.MenuOpen || Inventory.MenuOpen || GameHUD.MenuOpen || SoulMenu.MenuOpen || LairMenu.MenuOpen || BuyerNPC.MenuOpen || MerchantNPC.MenuOpen || PropagandaMenu.MenuOpen || StoryNpc.MenuOpen || MangaDialog.Active || WanderingMerchant.MenuOpen || PlotManager.MenuOpen || DemonNatureMenu.MenuOpen || WorkshopMenu.MenuOpen || PauseMenu.IsOpen) return;

        if (GameInput.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (lockCursor && GameInput.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}