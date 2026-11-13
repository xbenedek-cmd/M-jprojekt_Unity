using UnityEngine;

public class NPCGrounding : MonoBehaviour
{
    [Tooltip("Pri prvom zarovnaní (spawn) sa zem hľadá z tejto výšky nad postavou")]
    public float rayStartHeight = 5f;
    [Tooltip("Ďalej sa zem hľadá len z tejto výšky nad nohami: predmety vyššie (sklo, múr, strecha) nie sú zem, takže postava na ne nevyskočí")]
    public float stepHeight = 0.7f;
    public float rayDistance = 20f;
    public float verticalTolerance = 0.02f;
    [Tooltip("Ako často sa stojaci NPC znova zarovná na zem (s)")]
    public float recheckInterval = 0.5f;

    private Vector3 lastSnapPosition;
    private bool firstSnap = true;
    private float nextSnapTime;

    private static readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    private Rigidbody rb;
    private Collider bodyCollider;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<Collider>();
        if (bodyCollider == null) bodyCollider = GetComponentInChildren<Collider>();
    }

    void Start()
    {
        SnapToGround();   // hneď pri spawne, nie až po prvom pohybe
        lastSnapPosition = transform.position;
        nextSnapTime = Time.time + recheckInterval;
    }

    void LateUpdate()
    {
        // Stojaci NPC sa kontroluje len občas; pohyb vyvolá okamžitú kontrolu (menej lúčov a SyncTransforms)
        bool moved = (transform.position - lastSnapPosition).sqrMagnitude > 0.0001f;
        if (!moved && Time.time < nextSnapTime) return;

        SnapToGround();
        lastSnapPosition = transform.position;
        nextSnapTime = Time.time + recheckInterval;
    }

    public void SnapToGround()
    {
        Damageable dmg = GetComponent<Damageable>();
        if (dmg != null && dmg.IsDead)
        {
            enabled = false;
            return;
        }

        if (bodyCollider == null) return;

        Physics.SyncTransforms();

        float lift = firstSnap ? rayStartHeight : stepHeight;
        firstSnap = false;
        Vector3 origin = transform.position + Vector3.up * lift;
        RaycastHit closestHit = default;
        float closestDistance = float.MaxValue;
        bool found = false;

        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, rayDistance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (hit.collider == bodyCollider || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.normal.y < 0.5f) continue;   // zvislé steny a strmé plochy nie sú podlaha

            // Živé postavy (hráč, iní NPC) nie sú zem - inak sa NPC pri hráčovi "vyšplhá" na jeho hlavu a hráča vystrelí
            Rigidbody other = hit.collider.attachedRigidbody;
            if (other != null && !other.isKinematic) continue;
            if (hit.collider.GetComponentInParent<Damageable>() != null || hit.collider.GetComponentInParent<PlayerStats>() != null) continue;

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                found = true;
            }
        }

        if (!found) return;

        // Používame lokálny bottom offset, nie raw world-space min.y. To robí korekciu stabilnejšou
        // pre modely s odlišným pivotom/rotáciou, kde sa niektoré NPC zasaďujú do podlahy.
        float bottomOffset = transform.InverseTransformPoint(bodyCollider.bounds.min).y;
        float targetY = closestHit.point.y - bottomOffset;

        // Kinematické telo: vychádza sa z rb.position (nie z interpolovaného transformu, ktorý za ním zaostáva).
        // Inak by korekcia výšky prepísala krok AI späť do starého X/Z a postava by sa trhala.
        bool kinematic = rb != null && rb.isKinematic;
        Vector3 position = kinematic ? rb.position : transform.position;
        if (Mathf.Abs(targetY - position.y) <= verticalTolerance) return;

        position.y = targetY;
        if (kinematic) rb.MovePosition(position);
        else transform.position = position;
    }
}
