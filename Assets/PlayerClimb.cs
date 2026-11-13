using System.Collections;
using UnityEngine;

/// <summary>
/// Šplhanie na rímsu: Medzerník pri múre alebo rímse (výška minHeight–maxHeight nad nohami) hráča vytiahne hore namiesto skoku.
/// Pohyb hore a dopredu vedie tento skript, animácia "Climbing To Top" sa len prehráva (jej posun bokov sa v DemonModelAnimator ruší).
/// Volá ho PlayerMovement.HandleJump.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerClimb : MonoBehaviour
{
    [SerializeField] private float minHeight = 1f;
    [SerializeField] private float maxHeight = 2.5f;
    [Tooltip("Ako ďaleko pred sebou hráč hľadá múr")]
    [SerializeField] private float reach = 0.9f;
    [Tooltip("Dĺžka šplhania (s), ak model nemá klip")]
    [SerializeField] private float fallbackSeconds = 1f;

    public bool IsClimbing { get; private set; }

    private PlayerMovement movement;
    private Rigidbody rb;
    private CapsuleCollider capsule;
    private PlayerVisual visual;
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private readonly Collider[] overlaps = new Collider[8];

    void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        visual = GetComponent<PlayerVisual>();
    }

    /// <summary>Ak je pred hráčom použiteľná rímsa, spustí šplhanie a vráti true (skok sa vtedy nevykoná).</summary>
    public bool TryClimb()
    {
        if (IsClimbing || rb == null) return false;
        if (capsule == null) capsule = GetComponent<CapsuleCollider>();
        if (capsule == null) return false;

        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) return false;
        forward.Normalize();

        float feetY = capsule.bounds.min.y;
        float halfHeight = rb.position.y - feetY;

        if (!FindWall(feetY, forward, out RaycastHit wall)) return false;

        // Najvyšší vodorovný povrch tesne za hranou múru, v dosahu šplhania
        Vector3 inside = wall.point - wall.normal * 0.5f;
        Vector3 top = new Vector3(inside.x, feetY + maxHeight + 0.4f, inside.z);
        float topY = float.NaN;
        int count = Physics.RaycastNonAlloc(top, Vector3.down, hits, maxHeight - minHeight + 0.6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        for (int i = 0; i < count; i++)
        {
            if (!IsStatic(hits[i].collider) || hits[i].normal.y < 0.6f) continue;
            if (hits[i].point.y > best) best = hits[i].point.y;
        }
        if (best > float.MinValue) topY = best;

        float height = topY - feetY;
        if (float.IsNaN(topY) || height < minHeight || height > maxHeight) return false;

        Vector3 end = new Vector3(inside.x, topY + halfHeight + 0.03f, inside.z);
        if (!HasRoom(end, halfHeight)) return false;

        StartCoroutine(Climb(end, -wall.normal));
        return true;
    }

    // Múr pred hráčom: lúč z výšky pásu (zvislá, nepohyblivá plocha)
    bool FindWall(float feetY, Vector3 forward, out RaycastHit wall)
    {
        wall = default;
        Vector3 origin = new Vector3(rb.position.x, feetY + 0.6f, rb.position.z);
        int count = Physics.RaycastNonAlloc(origin, forward, hits, reach + capsule.radius, ~0, QueryTriggerInteraction.Ignore);

        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            if (!IsStatic(hits[i].collider) || Mathf.Abs(hits[i].normal.y) > 0.3f) continue;
            if (hits[i].distance < nearest) { nearest = hits[i].distance; wall = hits[i]; found = true; }
        }
        return found;
    }

    // Svet (nie hráč, NPC ani pohyblivé telesá)
    bool IsStatic(Collider c)
    {
        if (c.transform == transform || c.transform.IsChildOf(transform)) return false;
        if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) return false;
        return c.GetComponentInParent<NPCPerception>() == null && c.GetComponentInParent<Damageable>() == null;
    }

    // Hore musí byť miesto pre kapsulu
    bool HasRoom(Vector3 center, float halfHeight)
    {
        float r = capsule.radius * 0.9f;
        Vector3 a = center + Vector3.up * (r + 0.05f - halfHeight);
        Vector3 b = center + Vector3.up * (halfHeight - r);
        int count = Physics.OverlapCapsuleNonAlloc(a, b, r, overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (IsStatic(overlaps[i])) return false;
        return true;
    }

    IEnumerator Climb(Vector3 end, Vector3 facing)
    {
        IsClimbing = true;
        movement.ExternalControl = true;

        float length = visual != null && visual.ModelAnimator != null ? visual.ModelAnimator.PlayOneShot(DemonModelAnimator.OneShot.Climb) : 0f;
        float duration = length > 0.1f ? length : fallbackSeconds;
        movement.LockMovement(duration);

        rb.linearVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.MoveRotation(Quaternion.LookRotation(facing, Vector3.up));

        Vector3 start = rb.position;
        Vector3 up = new Vector3(start.x, end.y, start.z);
        float t = 0f;
        while (t < duration)
        {
            yield return new WaitForFixedUpdate();
            t += Time.fixedDeltaTime;

            // Najprv hore po stene (70 % času), potom nad rímsu
            float p = Mathf.Clamp01(t / duration);
            Vector3 pos = p < 0.7f
                ? Vector3.Lerp(start, up, Mathf.SmoothStep(0f, 1f, p / 0.7f))
                : Vector3.Lerp(up, end, Mathf.SmoothStep(0f, 1f, (p - 0.7f) / 0.3f));
            rb.MovePosition(pos);
        }

        rb.position = end;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        movement.ExternalControl = false;
        IsClimbing = false;
    }

    void OnDisable()
    {
        if (!IsClimbing) return;
        IsClimbing = false;
        movement.ExternalControl = false;
        if (rb != null) rb.isKinematic = false;
    }
}
