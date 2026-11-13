using UnityEngine;

/// <summary>
/// Pevné miesto bossa: boss žije na svojej lokácii po celú hru (túla sa v jej okolí, nezabieha za civilistami ani stavbami) a po naháňaní
/// hráča sa vráti späť, ak sa vzdialil za vodítko. Miesto sa dá presunúť podľa príbehu (SetHome); poloha sa ukladá cez WorldSave.
/// </summary>
[RequireComponent(typeof(DemonEnemy))]
public class BossPost : MonoBehaviour
{
    [SerializeField] private string postName = "Hniezdo";
    [SerializeField] private Vector3 home;
    [Tooltip("Ďalej od miesta sa boss (bez naháňania) vráti domov")]
    [SerializeField] private float leashRadius = 25f;
    [Tooltip("Polomer túlania okolo miesta, kým nikoho nehonia")]
    [SerializeField] private float idleRadius = 5f;
    [SerializeField] private float returnSpeed = 3.5f;

    private DemonEnemy enemy;
    private Rigidbody rb;
    private NPCWander wander;

    public string PostName => postName;
    public Vector3 Home => home;

    /// <summary>Presunie bossovo miesto (príbeh, nový úkryt); boss sa tam vráti sám, alebo ho presunie `teleport`.</summary>
    public void SetHome(Vector3 position, string name = null, bool teleport = false)
    {
        home = position;
        if (!string.IsNullOrEmpty(name)) postName = name;
        if (wander != null) wander.SetOrigin(position);

        if (!teleport) return;
        transform.position = position;
        if (rb != null) rb.position = position;
    }

    void Start()
    {
        enemy = GetComponent<DemonEnemy>();
        rb = GetComponent<Rigidbody>();
        wander = GetComponent<NPCWander>();

        if (home == Vector3.zero) home = transform.position;

        // Boss stráži svoje miesto: nelovi civilistov a nebúra stavby ďaleko od neho
        enemy.attacksCivilians = false;
        enemy.demolishesBuildings = false;
        if (wander != null) wander.wanderRadius = idleRadius;

        // Fázy súboja (plášť, šokové výboje, blesky)
        if (GetComponent<BossPhases>() == null) gameObject.AddComponent<BossPhases>();
    }

    void FixedUpdate()
    {
        if (enemy == null || rb == null || enemy.IsChasing || enemy.Damage == null || enemy.Damage.IsDead) return;

        Vector3 to = home - rb.position;
        to.y = 0f;
        float distance = to.magnitude;
        if (distance <= leashRadius) return;

        Vector3 direction = to / distance;
        rb.MovePosition(rb.position + direction * returnSpeed * Time.fixedDeltaTime);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(direction), 6f * Time.fixedDeltaTime));
    }
}
