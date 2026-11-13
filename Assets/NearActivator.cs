using UnityEngine;

/// <summary>
/// Zapína model zdroja (jedlo) len v blízkosti hráča. Modely chleba a zeleniny majú státisíce trojuholníkov, takže desiatky kusov rozložených
/// po Divočine by sa inak vykresľovali aj z diaľky. Logika zberu (ResourceNode) beží na rodičovi ďalej, vypína sa len vizuál.
/// </summary>
public class NearActivator : MonoBehaviour
{
    [SerializeField] private GameObject target;
    [SerializeField] private float radius = 90f;

    private static Transform player;
    private float nextCheck;
    private bool shown = true;

    public void Init(GameObject visual, float activationRadius)
    {
        target = visual;
        radius = activationRadius;
    }

    void Start()
    {
        // Rozloženie kontrol v čase, aby sa všetky kusy nevyhodnocovali v rovnakom snímku
        nextCheck = Time.unscaledTime + Random.value * 0.5f;
    }

    void Update()
    {
        if (target == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 0.5f;

        if (player == null)
        {
            PlayerStats stats = FindAnyObjectByType<PlayerStats>();
            if (stats == null) return;
            player = stats.transform;
        }

        bool near = (player.position - transform.position).sqrMagnitude < radius * radius;
        if (near != shown)
        {
            shown = near;
            target.SetActive(near);
        }
    }
}
