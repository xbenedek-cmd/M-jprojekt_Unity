using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Pohyb")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 12f;

    public KeyCode sprintKey => KeyBindings.Get("sprint", KeyCode.LeftShift);
    [Header("Šprint")]
    public float sprintMultiplier = 1.6f;
    public float sprintStaminaPerSecond = 20f;

    [Header("Skok")]
    public float jumpForce = 7f;
    public int maxJumps = 1;
    public float jumpStaminaCost = 10f;
    [Tooltip("Núdzová hodnota dosahu lúča, ak hráč nemá collider")]
    public float groundCheckDistance = 0.6f;
    [Tooltip("Rezerva pod spodkom kolidera, v ktorej sa ešte hráč považuje za stojaceho na zemi")]
    public float groundSkin = 0.1f;
    private Collider bodyCollider;
    // Trenie tela: na zemi bežné, vo vzduchu nulové (inak hráč pri skoku vedľa steny alebo na hrane ostal visieť)
    private PhysicsMaterial bodyMaterial;
    private bool airFriction;

    public KeyCode dashKey => KeyBindings.Get("dash", KeyCode.LeftAlt);
    [Header("Bojový úhyb (dash)")]
    public float dashSpeed = 14f;
    public float dashDuration = 0.18f;
    public float dashCooldown = 0.8f;
    public float dashStaminaCost = 15f;
    [Tooltip("Ako dlho po začatí úhybu sa hráč nedá zraniť")]
    public float dashInvulnerableTime = 0.22f;

    [Header("Voda (šmyk)")]
    [Range(0.01f, 1f)] public float waterFriction = 0.05f;

    [Header("Odkazy")]
    public Transform cameraTransform;

    private Rigidbody rb;
    private PlayerStats stats;
    private PlayerFeeding feeding;
    private int jumpsLeft;
    private bool isGrounded;
    private bool isOnWater;

    private Vector2 input;
    private bool sprintHeld;
    private bool jumpPressed;
    private bool dashPressed;
    private float nextDashTime;

    private Vector3 dashVelocity;
    private float dashUntil;
    private Vector3 facingDir;
    private float facingUntil;

    private Vector3 currentVelocity;

    public bool IsGrounded => isGrounded;
    /// <summary>Hráč sa práve pohybuje šprintom (Shift s dostatkom výdrže); animátor vtedy prehráva rýchly beh.</summary>
    public bool IsSprinting { get; private set; }

    /// <summary>Pohyb hráča práve vedie iný skript (šplhanie, krytie): PlayerMovement nehýbe telom.</summary>
    public bool ExternalControl { get; set; }

    private PlayerClimb climb;
    private float lockUntil;
    /// <summary>Hráč stojí (bez vstupu) daný čas, napr. počas animácie vstávania.</summary>
    public void LockMovement(float seconds) => lockUntil = Mathf.Max(lockUntil, Time.time + seconds);
    public void UnlockMovement() => lockUntil = 0f;

    /// <summary>Hráč sa práve odrazil (animátor spustí klip skoku).</summary>
    public event System.Action Jumped;

    /// <summary>Hráč dopadol na zem po skoku alebo páde; parameter = rýchlosť pádu (m/s) tesne pred dopadom.</summary>
    public event System.Action<float> Landed;

    [Header("Dopad")]
    [Tooltip("Od akej rýchlosti pádu (m/s) sa prehrá animácia dopadu; krátky skok ju nespúšťa")]
    [SerializeField] private float landMinSpeed = 4f;
    private bool wasGrounded = true;
    private float airDownSpeed;

    /// <summary>Ako dlho trvá skok po rovine (s): hore a späť dole pri aktuálnej sile skoku a gravitácii.</summary>
    public float ExpectedAirTime => 2f * Mathf.Max(0.1f, jumpForce) / Mathf.Max(0.1f, Mathf.Abs(Physics.gravity.y));

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        stats = GetComponent<PlayerStats>();
        feeding = GetComponent<PlayerFeeding>();
        climb = GetComponent<PlayerClimb>();

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        jumpsLeft = EffectiveMaxJumps;
    }

    int EffectiveMaxJumps => stats != null ? Mathf.Max(maxJumps, stats.EffectiveMaxJumps) : maxJumps;

    void Update()
    {
        // OPRAVA: Ak sme zatknutí, ignorujeme vstup
        if (JailManager.MenuOpen || JailManager.IsInJail)
        {
            input = Vector2.zero;
            sprintHeld = false;
            jumpPressed = false;
            dashPressed = false;
            return;
        }

        // Pri šatníku (a počas animácie vstávania) postava stojí
        bool uiOpen = StoryNpc.MenuOpen || SoulMenu.MenuOpen || LairMenu.MenuOpen || BuyerNPC.MenuOpen || MangaDialog.Active || WanderingMerchant.MenuOpen || PropagandaMenu.MenuOpen || Wardrobe.MenuOpen || PlotManager.MenuOpen || DemonNatureMenu.MenuOpen || WorkshopMenu.MenuOpen || PauseMenu.IsOpen || CombatQte.Active || Inventory.MenuOpen || PlayerAppearance.MenuOpen || MangaPanels.Active || Time.time < lockUntil;
        input = uiOpen ? Vector2.zero : new Vector2(GameInput.GetAxisRaw("Horizontal"), GameInput.GetAxisRaw("Vertical"));
        sprintHeld = GameInput.GetKey(sprintKey);

        if (GameInput.GetKeyDown(KeyBindings.Get("jump", KeyCode.Space)) && !uiOpen && !BodyCarry.Carrying)
            jumpPressed = true;

        if (GameInput.GetKeyDown(dashKey) && !Wardrobe.MenuOpen && !Healer.MenuOpen && !BodyCarry.Carrying)
            dashPressed = true;
    }

    void TryDash()
    {
        if (Time.time < nextDashTime || Time.time < dashUntil) return;
        if (stats != null && !stats.UseStamina(dashStaminaCost * Mathf.Clamp(1f - SkillTree.Sum(SkillStat.DashStamina), 0.3f, 1f) / stats.StaminaEfficiency))
        {
            GameHUD.WorldText(transform.position + Vector3.up * 1.8f, "Vyčerpaný!", Color.gray);
            return;
        }

        // Smer podľa vstupu (voči kamere); bez vstupu uskočí dozadu
        Vector3 dir;
        if (input.sqrMagnitude > 0.01f && cameraTransform != null)
        {
            Vector3 f = cameraTransform.forward; f.y = 0f; f.Normalize();
            Vector3 r = cameraTransform.right; r.y = 0f; r.Normalize();
            dir = r * input.x + f * input.y;
        }
        else dir = -transform.forward;

        Dash(dir, dashSpeed, dashDuration);
        nextDashTime = Time.time + dashCooldown * Mathf.Clamp(1f - SkillTree.Sum(SkillStat.DashCooldown), 0.2f, 1f);
        if (stats != null) stats.InvulnerableUntil = Mathf.Max(stats.InvulnerableUntil, Time.time + dashInvulnerableTime);   // nesmie skrátiť elixír nesmrteľnosti
    }

    void FixedUpdate()
    {
        // OPRAVA: Úplné zastavenie pohybu počas zatknutia
        if (JailManager.MenuOpen || JailManager.IsInJail)
        {
            if (rb != null)
            {
                rb.linearVelocity = new Vector3(0, rb.linearVelocity.y, 0); // Padanie necháme, aby nezamrzol vo vzduchu
                rb.angularVelocity = Vector3.zero;
            }
            return;
        }

        // Šplhanie / krytie vedie pohyb samo (rb je kinematické)
        if (ExternalControl) return;

        CheckGround();
        if (dashPressed)
        {
            dashPressed = false;
            TryDash();
        }
        Move();
        HandleJump();
    }

    public void FaceDirection(Vector3 dir)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        facingDir = dir.normalized;
        facingUntil = Time.time + 0.25f;
    }

    public void Dash(Vector3 dir, float speed, float duration)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        dashVelocity = dir.normalized * speed;
        dashUntil = Time.time + duration;
        FaceDirection(dir);
    }

    public void Teleport(Vector3 position, Vector3 facing)
    {
        ZoneLoader.EnsureLoadedAt(position);   // mapa cieľovej zóny sa načíta až teraz
        rb.position = position;
        transform.position = position;
        rb.linearVelocity = Vector3.zero;
        dashUntil = 0f;
        FaceDirection(facing);
    }

    private static readonly RaycastHit[] groundBuffer = new RaycastHit[24];

    /// <summary>
    /// Teleport, ktorý hráča postaví na zem pod cieľovým bodom (portál mal pivot vo výške oblúka, takže hráč vznikol 2 – 3 m nad zemou a padal).
    /// Zem sa hľadá z výšky nad cieľom; kmene, kamene a postavy sa nepočítajú. Bez zásahu sa použije pôvodná poloha.
    /// </summary>
    public void TeleportGrounded(Vector3 position, Vector3 facing)
    {
        ZoneLoader.EnsureLoadedAt(position);
        Physics.SyncTransforms();

        if (bodyCollider == null) bodyCollider = GetComponent<Collider>();
        int count = Physics.RaycastNonAlloc(position + Vector3.up * 30f, Vector3.down, groundBuffer, 80f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = groundBuffer[i];
            if (h.transform.IsChildOf(transform) || h.normal.y < 0.55f) continue;
            if (h.collider.GetComponentInParent<Damageable>() != null || h.collider.GetComponentInParent<BreakableNode>() != null) continue;
            if (h.point.y > position.y + 3f) continue;   // oblúk / strecha nad cieľom nie je zem
            if (h.point.y > best) best = h.point.y;
        }

        if (!float.IsNegativeInfinity(best))
        {
            float feet = bodyCollider != null ? rb.position.y - bodyCollider.bounds.min.y : 0.9f;
            position.y = best + feet + 0.05f;
        }
        Teleport(position, facing);
    }

    void CheckGround()
    {
        // Dosah lúča sa odvodí od spodku kolidera (kocka 1 m, kapsula 1,8 m...), nie od pevnej hodnoty
        if (bodyCollider == null) bodyCollider = GetComponent<Collider>();
        float reach = bodyCollider != null ? rb.position.y - bodyCollider.bounds.min.y + groundSkin : groundCheckDistance;
        isGrounded = Physics.Raycast(rb.position, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore);

        // Dopad: rýchlosť pádu sa zapamätá za letu a pri návrate na zem sa oznámi animátoru
        if (!isGrounded) airDownSpeed = Mathf.Max(airDownSpeed, -rb.linearVelocity.y);
        else
        {
            if (!wasGrounded && airDownSpeed >= landMinSpeed) Landed?.Invoke(airDownSpeed);
            airDownSpeed = 0f;
        }
        wasGrounded = isGrounded;

        if (isGrounded && rb.linearVelocity.y <= 0.1f)
            jumpsLeft = EffectiveMaxJumps;

        isOnWater = ReflectiveSurface.IsOnWater(rb.position);
        SetAirFriction(!isGrounded);
    }

    void SetAirFriction(bool inAir)
    {
        if (bodyCollider == null) return;
        if (bodyMaterial == null)
        {
            bodyMaterial = new PhysicsMaterial("TeloHraca") { bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum };
            bodyCollider.sharedMaterial = bodyMaterial;
            airFriction = !inAir;   // vynúti prvé nastavenie
        }
        if (airFriction == inAir) return;
        airFriction = inAir;
        bodyMaterial.dynamicFriction = inAir ? 0f : 0.6f;
        bodyMaterial.staticFriction = inAir ? 0f : 0.6f;
        bodyMaterial.frictionCombine = inAir ? PhysicsMaterialCombine.Minimum : PhysicsMaterialCombine.Average;
    }

    void Move()
    {
        if (Time.time < dashUntil)
        {
            rb.linearVelocity = new Vector3(dashVelocity.x, rb.linearVelocity.y, dashVelocity.z);
            rb.MoveRotation(Quaternion.LookRotation(dashVelocity));
            return;
        }

        Vector3 camForward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
        Vector3 camRight = cameraTransform != null ? cameraTransform.right : Vector3.right;
        camForward.y = 0f; camRight.y = 0f;
        camForward.Normalize(); camRight.Normalize();

        Vector3 moveDirection = (camRight * input.x + camForward * input.y).normalized;
        bool isMoving = moveDirection.sqrMagnitude > 0.01f;

        IsSprinting = false;
        float speed = moveSpeed;
        if (stats != null) speed *= stats.SpeedMultiplier;
        speed *= GameSpeed.Player;

        // Zakrádanie: čupnutý hráč je pomalší a nešprintuje
        PlayerStealth stealth = PlayerStealth.Instance;
        if (stealth != null) speed *= stealth.SpeedFactor;
        if (BodyCarry.Carrying) speed *= BodyCarry.SpeedFactor;   // s telom len pomaly kráča
        bool crouched = stealth != null && stealth.IsCrouched;

        if (feeding != null && feeding.IsFeeding)
        {
            speed *= feeding.moveSpeedWhileFeeding;
        }
        else if (isMoving && sprintHeld && !crouched && !BodyCarry.Carrying)
        {
            if (stats == null || stats.UseStamina(sprintStaminaPerSecond * Time.fixedDeltaTime / stats.StaminaEfficiency))
            {
                speed *= sprintMultiplier;
                IsSprinting = true;
            }
        }

        if (isMoving)
        {
            Vector3 nextPosition = rb.position + moveDirection * speed * Time.fixedDeltaTime;
            bool enteringResidential = ResidentialZone.At(nextPosition) != null && ResidentialZone.At(rb.position) == null;
            if (enteringResidential)
            {
                moveDirection = Vector3.zero;
                isMoving = false;
            }

            if (!isMoving)
            {
                currentVelocity = Vector3.zero;
                rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
                return;
            }

            float checkDist = 0.6f;
            if (Physics.SphereCast(rb.position + Vector3.up * 1f, 0.4f, moveDirection, out RaycastHit hit, checkDist, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponent<CityGuardNPC>() != null ||
                    hit.collider.GetComponent<ReflectiveSurface>() != null)
                {
                    moveDirection = Vector3.ProjectOnPlane(moveDirection, hit.normal).normalized;
                }
            }
        }

        if (isOnWater)
        {
            Vector3 targetVel = isMoving ? moveDirection * speed : Vector3.zero;
            currentVelocity = Vector3.Lerp(currentVelocity, targetVel, waterFriction);
            rb.linearVelocity = new Vector3(currentVelocity.x, rb.linearVelocity.y, currentVelocity.z);
        }
        else
        {
            currentVelocity = moveDirection * speed;
            rb.linearVelocity = new Vector3(moveDirection.x * speed, rb.linearVelocity.y, moveDirection.z * speed);
        }

        if (Time.time < facingUntil)
        {
            rb.MoveRotation(Quaternion.LookRotation(facingDir));
        }
        else if (isMoving)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime));
        }
        else if (isOnWater && currentVelocity.sqrMagnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(currentVelocity.normalized);
            rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRotation, rotationSpeed * 0.5f * Time.fixedDeltaTime));
        }
    }

    void HandleJump()
    {
        if (!jumpPressed) return;
        jumpPressed = false;

        // Pri rímse sa namiesto skoku vyšplhá
        if (climb != null && climb.TryClimb()) return;

        if (jumpsLeft > 0)
        {
            if (stats != null && !stats.UseStamina(jumpStaminaCost / stats.StaminaEfficiency))
            {
                GameHUD.WorldText(transform.position + Vector3.up * 1.8f, "Vyčerpaný!", Color.gray);
                return;
            }

            Vector3 v = rb.linearVelocity;
            rb.linearVelocity = new Vector3(v.x, jumpForce, v.z);
            jumpsLeft--;
            Jumped?.Invoke();
        }
    }
}