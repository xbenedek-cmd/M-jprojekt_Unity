using UnityEngine;

/// <summary>Táborový oheň: svetlo jemne kmitá a vypína sa, keď je hráč ďaleko (výkon).</summary>
public class CampFire : MonoBehaviour
{
    [SerializeField] private Light fireLight;
    [SerializeField] private float baseIntensity = 1.6f;
    [SerializeField] private float activeDistance = 35f;
    [SerializeField] private float maxRange = 8f;

    private Transform player;
    private float nextCheck;
    private float seed;

    public void Configure(Light light) => fireLight = light;

    private DayNightCycle clock;

    void Awake()
    {
        seed = Random.value * 100f;
        if (fireLight != null) fireLight.range = Mathf.Min(fireLight.range, maxRange);   // menšie svetlo = menej pixelov v ďalšom prechode
    }

    void Update()
    {
        if (fireLight == null) return;

        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            if (player == null)
            {
                PlayerStats stats = FindAnyObjectByType<PlayerStats>();
                if (stats != null) player = stats.transform;
            }
            if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
            bool dark = clock == null || clock.CurrentPhase == DayNightCycle.DayPhase.Dusk || clock.CurrentPhase == DayNightCycle.DayPhase.Night || clock.CurrentPhase == DayNightCycle.DayPhase.Dawn;
            if (player != null)
            {
                Vector3 d = player.position - transform.position;
                d.y = 0f;
                fireLight.enabled = dark && d.sqrMagnitude < activeDistance * activeDistance;   // cez deň sa reálne svetlo nepočíta
            }
        }

        if (fireLight.enabled)
            fireLight.intensity = baseIntensity * (0.8f + 0.4f * Mathf.PerlinNoise(Time.time * 6f, seed));
    }
}
