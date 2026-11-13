using UnityEngine;

/// <summary>
/// Krytie pri stene: kláves coverKey pri blízkej stene hráča pritiahne k nej chrbtom (animácia "Standing Cover Turn").
/// V kryte sa hráč pohybuje len pozdĺž steny a nepriatelia ho vidia menej (PlayerStealth.RangeFactor), okrem tých,
/// čo stoja priamo naproti (na voľnej strane steny). Za stenou ho nevidia vôbec (zakrýva ho geometria).
/// Z krytu: coverKey znova, Medzerník, útok, alebo odstúpenie od steny.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerCover : MonoBehaviour
{
    public static PlayerCover Instance { get; private set; }
    public static bool InCover => Instance != null && Instance.active;

    private KeyCode coverKey => KeyBindings.Get("cover", KeyCode.Y);
    [Tooltip("Najďalej, odkiaľ hráč k stene pribehne")]
    [SerializeField] private float searchRange = 2.2f;
    [SerializeField] private float enterSeconds = 0.25f;
    [SerializeField] private float coverSpeed = 2f;
    [Tooltip("Dotyk so stenou: vzdialenosť stredu hráča od steny navyše k polomeru kapsuly")]
    [SerializeField] private float wallGap = 0.08f;
    [Tooltip("Ak model v kryte stojí čelom k stene namiesto chrbtom, nastav 180")]
    [SerializeField] private float facingYawOffset;

    [Header("Viditeľnosť")]
    [Tooltip("Takto veľký dosah zrakov zostane pozorovateľom, ktorí nie sú oproti")]
    [Range(0.1f, 1f)] [SerializeField] private float coverVisibility = 0.3f;
    [Tooltip("Pozorovateľ do tohto uhla od normály steny (naproti hráčovi) sa krytom nenechá oklamať")]
    [SerializeField] private float openArc = 65f;

    private PlayerMovement movement;
    private PlayerStats stats;
    private PlayerVisual visual;
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private Transform cam;

    private bool active;
    private Vector3 normal;           // smer od steny k hráčovi (voľná strana)
    private Vector3 enterFrom;
    private Vector3 enterTo;
    private float enterT;
    private float awayTime;
    private readonly RaycastHit[] hits = new RaycastHit[8];

    void Awake()
    {
        Instance = this;
        movement = GetComponent<PlayerMovement>();
        stats = GetComponent<PlayerStats>();
        visual = GetComponent<PlayerVisual>();
        rb = GetComponent<Rigidbody>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Ako dobre vidí pozorovateľ krytého hráča (1 = bez zmeny). Volá PlayerStealth.RangeFactor.</summary>
    public static float VisibilityFrom(Vector3 watcherPosition)
    {
        PlayerCover c = Instance;
        if (c == null || !c.active) return 1f;

        Vector3 to = watcherPosition - c.transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return 1f;

        return Vector3.Angle(to, c.normal) <= c.openArc ? 1f : c.coverVisibility;
    }

    void Update()
    {
        if (stats != null && stats.IsDead) { if (active) Leave(); return; }
        if (JailManager.MenuOpen || JailManager.IsInJail || Wardrobe.MenuOpen || Inventory.MenuOpen || PlayerAppearance.MenuOpen) return;
        if (movement.ExternalControl && !active) return;   // práve šplhá

        if (GameInput.GetKeyDown(coverKey))
        {
            if (active) Leave();
            else TryEnter();
            return;
        }

        if (active && (GameInput.GetKeyDown(KeyCode.Space) || GameInput.GetMouseButtonDown(0))) Leave();
    }

    void FixedUpdate()
    {
        if (!active) return;

        if (enterT < 1f)
        {
            enterT = Mathf.Min(1f, enterT + Time.fixedDeltaTime / Mathf.Max(0.05f, enterSeconds));
            rb.MovePosition(Vector3.Lerp(enterFrom, enterTo, Mathf.SmoothStep(0f, 1f, enterT)));
            return;
        }

        MoveAlongWall();
    }

    void TryEnter()
    {
        if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        if (capsule == null || rb == null) return;

        // Najbližšia vysoká zvislá stena v okolí (8 smerov v pásovej a v hlavovej výške)
        Vector3 pivot = rb.position;
        float feetY = capsule.bounds.min.y;
        float bestDist = float.MaxValue;
        RaycastHit bestHit = default;

        for (int i = 0; i < 8; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            if (!Raycast(new Vector3(pivot.x, feetY + 1.0f, pivot.z), dir, out RaycastHit low)) continue;
            if (!Raycast(new Vector3(pivot.x, feetY + 1.6f, pivot.z), dir, out RaycastHit high)) continue;   // stena musí byť vysoká
            if (Mathf.Abs(high.distance - low.distance) > 0.4f) continue;

            if (low.distance < bestDist) { bestDist = low.distance; bestHit = low; }
        }

        if (bestDist == float.MaxValue)
        {
            GameHUD.Message("V blízkosti nie je stena na krytie.", 2f);
            return;
        }

        normal = bestHit.normal;
        normal.y = 0f;
        normal.Normalize();

        enterFrom = rb.position;
        enterTo = new Vector3(bestHit.point.x, rb.position.y, bestHit.point.z) + normal * (capsule.radius + wallGap);
        enterT = 0f;
        awayTime = 0f;
        active = true;

        movement.ExternalControl = true;
        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.MoveRotation(Quaternion.LookRotation(normal, Vector3.up) * Quaternion.Euler(0f, facingYawOffset, 0f));

        if (visual != null && visual.ModelAnimator != null) visual.ModelAnimator.SetCover(true);
        GameHUD.Message($"Krytie [{coverKey}]: pohyb pozdĺž steny. Pozor na tých, čo stoja naproti.", 3f);
    }

    bool Raycast(Vector3 origin, Vector3 dir, out RaycastHit result)
    {
        result = default;
        int count = Physics.RaycastNonAlloc(origin, dir, hits, searchRange, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i].collider;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
            if (c.GetComponentInParent<NPCPerception>() != null || c.GetComponentInParent<Damageable>() != null) continue;
            if (Mathf.Abs(hits[i].normal.y) > 0.3f) continue;

            if (hits[i].distance < nearest) { nearest = hits[i].distance; result = hits[i]; found = true; }
        }
        return found;
    }

    void MoveAlongWall()
    {
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        Vector3 moveDirection = Vector3.zero;

        Vector2 input = new Vector2(GameInput.GetAxisRaw("Horizontal"), GameInput.GetAxisRaw("Vertical"));
        if (cam != null && input.sqrMagnitude > 0.01f)
        {
            Vector3 f = cam.forward; f.y = 0f; f.Normalize();
            Vector3 r = cam.right; r.y = 0f; r.Normalize();
            Vector3 wanted = (r * input.x + f * input.y).normalized;

            // Odstúpenie od steny drží dlhšie = odchod z krytu
            awayTime = Vector3.Dot(wanted, normal) > 0.7f ? awayTime + Time.fixedDeltaTime : 0f;
            if (awayTime > 0.35f) { Leave(); return; }

            Vector3 tangent = Vector3.Cross(Vector3.up, normal);
            float along = Vector3.Dot(wanted, tangent);
            if (Mathf.Abs(along) > 0.2f)
            {
                Vector3 direction = tangent * Mathf.Sign(along);
                if (TryStep(direction * coverSpeed * Time.fixedDeltaTime)) moveDirection = direction;
            }
        }
        else awayTime = 0f;

        // Pri pohybe sa postava pozerá na stranu, kam ide (animácia chôdze); v státí je chrbtom k stene v póze krytia
        Quaternion target = moveDirection != Vector3.zero
            ? Quaternion.LookRotation(moveDirection, Vector3.up)
            : Quaternion.LookRotation(normal, Vector3.up) * Quaternion.Euler(0f, facingYawOffset, 0f);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, 14f * Time.fixedDeltaTime));
    }

    // Krok pozdĺž steny: stena musí pokračovať a v ceste nesmie byť prekážka (na rohu hráč zastane = "vyzeranie spoza rohu")
    bool TryStep(Vector3 step)
    {
        Vector3 next = rb.position + step;
        float feetY = capsule.bounds.min.y;
        Vector3 probe = new Vector3(next.x, feetY + 1.0f, next.z);

        if (!Raycast(probe + normal * 0.5f, -normal, out RaycastHit wall)) return false;
        if (Physics.Raycast(new Vector3(rb.position.x, feetY + 1.0f, rb.position.z), step.normalized,
                capsule.radius + step.magnitude + 0.05f, ~0, QueryTriggerInteraction.Ignore)) return false;

        Vector3 snapped = new Vector3(wall.point.x, rb.position.y, wall.point.z) + normal * (capsule.radius + wallGap);
        rb.MovePosition(snapped);
        return true;
    }

    void Leave()
    {
        active = false;
        movement.ExternalControl = false;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        if (visual != null && visual.ModelAnimator != null) visual.ModelAnimator.SetCover(false);
    }

    void OnDisable()
    {
        if (active) Leave();
    }
}
