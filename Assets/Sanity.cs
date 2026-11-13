using UnityEngine;

/// <summary>
/// Psychika hráča (0–100). Ubúda v noci vonku, pri mŕtvolách, po zabití človeka, v popolovej búrke a pri nízkej ľudskosti; vo dne a v osvetlenom meste sa vracia.
/// Pod 50 prichádzajú halucinácie (šepot, tiene), pod 15: človek sa môže náhle premeniť, démon vpadne do Berserku (rýchlejší, ale po ňom sa psychika zrúti).
/// </summary>
public static class Sanity
{
    public static float Value { get; private set; } = 100f;
    public static bool Berserk { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { Value = 100f; Berserk = false; }

    public static void Add(float amount) => Value = Mathf.Clamp(Value + amount, 0f, 100f);
    public static void Set(float value) => Value = Mathf.Clamp(value, 0f, 100f);
    public static void SetBerserk(bool on) => Berserk = on;
}

/// <summary>Beh psychiky: úbytok/obnova, halucinácie, premena/berserk pri páde pod 15, okraj obrazovky.</summary>
public class SanityRunner : MonoBehaviour
{
    private static readonly string[] Whispers =
    {
        "Za tebou stojí niekto, kto tam nebol...", "Počuješ šepot mŕtvych: nepodarilo sa ti nás zachrániť.", "Tiene sa hýbu aj bez svetla.",
        "Niekto ťa volá menom, ktoré si zabudol.", "Krv si pamätá všetko, čo si urobil.", "Okolo teba je príliš ticho."
    };

    private PlayerStats player;
    private DayNightCycle clock;
    private Transform[] shadows;
    private float[] shadowUntil;
    private float nextHallucination, nextCheck, berserkUntil, berserkCooldown;
    private float corpseDrain;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<SanityRunner>() != null) return;
        new GameObject("SanityRunner").AddComponent<SanityRunner>();
    }

    void Start()
    {
        // Dve postavy-tiene sa vytvoria raz a znova používajú (pooling)
        shadows = new Transform[2];
        shadowUntil = new float[2];
        Material dark = new Material(Shader.Find("Standard")) { color = new Color(0.02f, 0.02f, 0.03f) };
        Material eye = new Material(Shader.Find("Standard")) { color = new Color(1f, 0.1f, 0.1f) };
        eye.EnableKeyword("_EMISSION");
        eye.SetColor("_EmissionColor", new Color(1f, 0.1f, 0.1f) * 3f);

        for (int i = 0; i < shadows.Length; i++)
        {
            var root = new GameObject("HalucinaciaTien");
            root.transform.SetParent(transform, false);
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.up * 0.95f;
            body.transform.localScale = new Vector3(0.7f, 0.95f, 0.5f);
            body.GetComponent<Renderer>().sharedMaterial = dark;
            foreach (float ex in new[] { -0.12f, 0.12f })
            {
                GameObject e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(e.GetComponent<Collider>());
                e.transform.SetParent(root.transform, false);
                e.transform.localPosition = new Vector3(ex, 1.65f, 0.2f);
                e.transform.localScale = Vector3.one * 0.08f;
                e.GetComponent<Renderer>().sharedMaterial = eye;
            }
            root.SetActive(false);
            shadows[i] = root.transform;
        }
    }

    void Update()
    {
        if (PauseMenu.AtStartup || Time.timeScale == 0f) return;
        if (player == null) { player = FindAnyObjectByType<PlayerStats>(); return; }
        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
        if (player.IsDead) return;

        float dt = Time.deltaTime;
        bool night = clock != null && clock.IsNight;
        bool inCity = CityPopulation.At(player.transform.position) != null;

        // Úbytok / obnova
        float rate = 0f;
        if (night && !MineSystem.Inside) rate -= 0.04f;
        if (night && inCity && Power.Online) rate += 0.03f;
        if (night && AshStorm.IsSheltered(player.transform.position)) rate += 0.08f;   // úkryt dá oddych
        if (!night) rate += 0.05f;
        if (SoulBalance.Humanity < 30f) rate -= 0.02f;
        if (AshStorm.ExposedNow) rate -= 0.08f;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.5f;
            corpseDrain = NearCorpseDrain();
        }
        rate -= corpseDrain;
        if (Sanity.Berserk) rate = 0f;
        Sanity.Add(rate * dt);

        UpdateHallucinations();
        UpdateBreakdown();
        UpdateShadows();
    }

    // Čerstvá viditeľná mŕtvola ľudí do 10 m zráža psychiku
    float NearCorpseDrain()
    {
        foreach (Damageable c in Damageable.AllCorpses)
        {
            if (c == null || !c.IsHumanBody || c.IsHidden || c.IsCarried) continue;
            if ((c.transform.position - player.transform.position).sqrMagnitude < 100f) return 0.25f;
        }
        return 0f;
    }

    void UpdateHallucinations()
    {
        if (Sanity.Value >= 50f || Time.time < nextHallucination) return;
        nextHallucination = Time.time + Random.Range(15f, 40f) * Mathf.Lerp(0.4f, 1f, Sanity.Value / 50f);

        GameHUD.Message(Whispers[Random.Range(0, Whispers.Length)], 4f);
        if (Sanity.Value >= 30f) return;

        // Tieň v zornom poli, ktorý zmizne, keď sa priblížiš
        for (int i = 0; i < shadows.Length; i++)
        {
            if (shadows[i].gameObject.activeSelf) continue;
            Camera cam = Camera.main;
            Vector3 forward = cam != null ? cam.transform.forward : player.transform.forward;
            forward.y = 0f;
            Vector3 dir = Quaternion.Euler(0f, Random.Range(-40f, 40f), 0f) * forward.normalized;
            Vector3 pos = player.transform.position + dir * Random.Range(9f, 14f);
            if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore)) pos.y = hit.point.y;
            shadows[i].position = pos;
            shadows[i].gameObject.SetActive(true);
            shadowUntil[i] = Time.time + 7f;
            break;
        }
    }

    void UpdateShadows()
    {
        for (int i = 0; i < shadows.Length; i++)
        {
            if (!shadows[i].gameObject.activeSelf) continue;
            Vector3 to = player.transform.position - shadows[i].position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) shadows[i].rotation = Quaternion.LookRotation(to);
            if (Time.time >= shadowUntil[i] || to.sqrMagnitude < 25f || Sanity.Value >= 50f) shadows[i].gameObject.SetActive(false);
        }
    }

    // Pod 15: človek sa môže náhle premeniť; démon vpadne do Berserku (po ňom psychika spadne naspäť na 25)
    void UpdateBreakdown()
    {
        if (Sanity.Berserk)
        {
            if (Time.time >= berserkUntil || !player.IsDemon)
            {
                Sanity.SetBerserk(false);
                Sanity.Set(25f);
                GameHUD.Message("Berserk pominul. Si vyčerpaný a roztrasený.", 4f);
            }
            return;
        }
        if (Sanity.Value >= 15f || Time.time < berserkCooldown) return;

        berserkCooldown = Time.time + 60f;
        if (player.IsDemon)
        {
            Sanity.SetBerserk(true);
            berserkUntil = Time.time + 20f;
            player.StartOverclock(20f, 0.3f, 0.15f);
            GameHUD.Message("BERSERK! Šialenstvo ťa ženie vpred (20 s).", 4f);
        }
        else if (Random.value < 0.35f)
        {
            player.ForceArakaniTransformation();
            Sanity.Set(30f);
            GameHUD.Message("Psychika sa zlomila: telo sa premenilo.", 5f);
        }
        else GameHUD.Message("Hranica šialenstva: ešte chvíľu a telo sa premení!", 4f);
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup || player == null || player.IsDead) return;
        float strength = Sanity.Berserk ? 0.35f : Mathf.Clamp01((60f - Sanity.Value) / 60f) * 0.4f;
        if (strength <= 0.01f) return;

        float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * (Sanity.Berserk ? 8f : 2.5f));
        Color prev = GUI.color;
        GUI.color = Sanity.Berserk ? new Color(0.7f, 0f, 0f, strength * pulse) : new Color(0.12f, 0f, 0.2f, strength * pulse);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }
}
