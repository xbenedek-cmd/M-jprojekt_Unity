using UnityEngine;

/// <summary>
/// Civilisti si všímajú, keď hráč vyčnieva: nesprávne oblečenie v štvrti alebo démonická forma
/// vo vojenskej zóne (viď Wardrobe.SuspicionMultiplier). Keď si ťa dosť dlho všímajú, nahlásia ťa:
/// zvýši sa podozrenie mesta a oni utekajú. Pridáva sa automaticky z NPCPerception.
/// </summary>
[RequireComponent(typeof(CivilianAI))]
public class SuspicionWatcher : MonoBehaviour
{
    [Header("Zrak")]
    public float viewRange = 10f;
    [Range(40f, 360f)] public float viewAngle = 140f;
    public float closeAwareness = 2f;
    public float detectRate = 0.9f;
    public float loseRate = 0.3f;

    [Header("Kedy si ťa všímajú")]
    [Tooltip("Civilisti si všímajú len, ak je násobič podozrenia aspoň takýto (správne oblečenie ich neznepokojuje)")]
    public float onlyAboveMultiplier = 1.3f;

    [Header("Nahlásenie")]
    public float suspicionOnReport = 8f;
    public float cooldown = 25f;

    private CivilianAI civilian;
    private PlayerStats player;
    private float detection;
    private float nextTick;
    private float nextReport;
    private float nextText;

    public float Detection => detection;

    void Start()
    {
        civilian = GetComponent<CivilianAI>();
        player = FindAnyObjectByType<PlayerStats>();
    }

    void Update()
    {
        if (civilian == null || civilian.IsDead || civilian.IsHealer) return;
        if (Time.time < nextTick) return;
        nextTick = Time.time + 0.2f;

        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player == null) return;
        }

        float dt = 0.2f;
        float mult = Wardrobe.SuspicionMultiplier();

        if (mult < onlyAboveMultiplier || Time.time < nextReport || player.IsDead || player.IsInvisible)
        {
            detection = Mathf.Max(0f, detection - loseRate * dt);
            return;
        }

        float see = SeeStrength();
        if (see > 0f) detection += see * detectRate * mult * dt;
        else detection = Mathf.Max(0f, detection - loseRate * dt);

        if (detection >= 0.5f) Say("?", new Color(1f, 0.85f, 0.3f));

        if (detection >= 1f)
        {
            detection = 0f;
            nextReport = Time.time + cooldown;

            Reputation.AddSuspicion(suspicionOnReport);
            civilian.Alarm(player.transform.position);
            Say(player.IsDemon ? "Démon! Pomoc!" : "Ten sem nepatrí!", new Color(1f, 0.5f, 0.3f));
            GameHUD.Message($"Civilista ťa nahlásil! Podozrenie mesta +{suspicionOnReport:0}", 3.5f);
        }
    }

    float SeeStrength()
    {
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        float range = viewRange * PlayerStealth.RangeFactor(transform);
        if (dist > range) return 0f;

        if (dist > closeAwareness && Vector3.Angle(transform.forward, to) > viewAngle * 0.5f) return 0f;

        if (!StealthWorld.HasLineOfSight(transform.position + Vector3.up * 0.8f, player.transform.position + Vector3.up * 0.3f))
            return 0f;

        return Mathf.Clamp01(1f - dist / Mathf.Max(0.1f, range)) * 0.8f + 0.2f;
    }

    void Say(string text, Color color)
    {
        if (Time.time < nextText) return;
        nextText = Time.time + 2f;
        GameHUD.WorldText(transform.position + Vector3.up * 2.9f, text, color);
    }
}
