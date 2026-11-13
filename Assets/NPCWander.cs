using UnityEngine;

/// <summary>
/// Chôdza postavy. Obyvatelia mesta (civilista, odbojár, vojak, strážnik) chodia po svojej trase (PatrolPlanner) a nevyjdú
/// z hraníc zóny (mesta). Ostatné postavy sa túlajú v malom kruhu okolo miesta.
/// </summary>
public class NPCWander : MonoBehaviour
{
    public float wanderRadius = 3f;
    public float speed = 1.5f;
    public Vector2 pauseRange = new Vector2(1f, 4f);

    private Vector3 origin;
    private Vector3 target;
    private float waitUntil;
    private Rigidbody rb;
    private BloodSource source;
    private ResidentialZone residentialZone;

    private PatrolRoute route;
    private ZonePopulation cityZone;
    private bool usesRoute;
    private float routeRetryAt;
    private Vector3 stuckPos;
    private float nextStuckCheck;

    private const float RouteRefreshSeconds = 180f;   // stavby sa menia: trasa sa občas zostaví znova

    void Start()
    {
        origin = transform.position;
        source = GetComponent<BloodSource>();
        residentialZone = ResidentialZone.At(origin);
        usesRoute = GetComponent<CivilianAI>() != null || GetComponent<SoldierAI>() != null || GetComponent<CityGuardNPC>() != null;

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        PickNewTarget();
    }

    void OnEnable()
    {
        // Zóna sa zapína a postava sa vracia domov: zisti mesto a zastaranú trasu zahoď
        cityZone = CityPopulation.At(transform.position);
        if (rb != null && usesRoute && route != null && Time.time - route.BuiltAt > RouteRefreshSeconds)
        {
            route = null;
            routeRetryAt = 0f;
            PickNewTarget();
        }
    }

    /// <summary>Nové stredisko túlania (napr. boss po presune na iné miesto).</summary>
    public void SetOrigin(Vector3 position)
    {
        origin = position;
        if (rb != null) PickNewTarget();
    }

    public void Pause(float seconds)
    {
        waitUntil = Time.time + seconds;
    }

    void FixedUpdate()
    {
        if (source != null && (source.BeingDrunk || source.IsDead)) return;
        if (Time.time < waitUntil)
        {
            stuckPos = rb.position;
            nextStuckCheck = Time.time + 2.5f;
            return;
        }

        bool onRoute = usesRoute && route != null && route.Valid;
        Vector3 toTarget = target - rb.position;
        toTarget.y = 0f;

        if (toTarget.magnitude < (onRoute ? 0.8f : 0.2f))
        {
            ArriveAtTarget(onRoute);
            return;
        }

        // Zaseknutá postava preskočí bod trasy
        if (onRoute && Time.time >= nextStuckCheck)
        {
            nextStuckCheck = Time.time + 2.5f;
            if ((rb.position - stuckPos).sqrMagnitude < 0.16f)
            {
                route.Advance();
                PickNewTarget();
                return;
            }
            stuckPos = rb.position;
        }

        Vector3 dir = toTarget.normalized;
        float step = speed * GameSpeed.Npc * Time.fixedDeltaTime;

        if (Physics.SphereCast(rb.position + Vector3.up * 0.5f, 0.4f, dir, out RaycastHit hit, step + 0.2f, ~0, QueryTriggerInteraction.Ignore))
        {
            bool isObstacle = hit.collider.GetComponentInParent<PlayerStats>() == null &&
                              hit.collider.GetComponentInParent<NPCPerception>() == null &&
                              hit.collider.GetComponentInParent<BloodSource>() == null &&
                              !hit.collider.isTrigger;

            if (isObstacle)
            {
                waitUntil = Time.time + Random.Range(pauseRange.x, pauseRange.y);
                if (onRoute) route.Advance();
                PickNewTarget();
                return;
            }
        }

        Vector3 nextPosition = rb.position + dir * step;
        if (!onRoute && residentialZone != null) nextPosition = residentialZone.ClampInside(nextPosition);
        if (cityZone != null) nextPosition = cityZone.ClampInside(nextPosition, 1f);   // z mesta nevyjde
        rb.MovePosition(nextPosition);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(dir), 8f * Time.fixedDeltaTime));
    }

    void ArriveAtTarget(bool onRoute)
    {
        if (onRoute)
        {
            Vector2 d = route.CurrentDwell;
            waitUntil = Time.time + (d.y > 0.01f ? Random.Range(d.x, d.y) : 0f);
            route.Advance();
        }
        else
        {
            waitUntil = Time.time + Random.Range(pauseRange.x, pauseRange.y);
        }
        PickNewTarget();
    }

    void PickNewTarget()
    {
        if (usesRoute)
        {
            if (route == null && Time.time >= routeRetryAt)
            {
                cityZone = CityPopulation.At(transform.position);
                route = PatrolPlanner.Build(transform, cityZone, null, PatrolPlanner.RoleOf(gameObject));
                routeRetryAt = Time.time + 60f;
            }
            if (route != null && route.Valid)
            {
                target = route.Current;
                return;
            }
        }

        Vector2 c = Random.insideUnitCircle * wanderRadius;
        target = origin + new Vector3(c.x, 0f, c.y);
        if (residentialZone != null) target = residentialZone.ClampInside(target);
        if (cityZone != null) target = cityZone.ClampInside(target, 1f);
    }
}
