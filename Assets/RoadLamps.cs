using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pouličné lampy ciest: v súmraku a v noci svietia (DayNightCycle). Skutočné svetlá sa zapínajú len v blízkosti hráča
/// (každé svetlo navyše stojí výkon), žiara hlavice lampy je spoločný materiál bez ďalších drawcallov.
/// </summary>
public class RoadLamps : MonoBehaviour
{
    [SerializeField] private List<Light> lights = new List<Light>();
    [SerializeField] private List<Renderer> heads = new List<Renderer>();
    [SerializeField] private Material onMaterial;
    [SerializeField] private Material offMaterial;
    [Tooltip("Do akej vzdialenosti od hráča svietia reálne svetlá (m)")]
    [SerializeField] private float lightDistance = 55f;
    [Tooltip("Dosah svetla lampy (m)")]
    [SerializeField] private float lightRange = 14f;
    [Tooltip("Jas svetla lampy")]
    [SerializeField] private float lightIntensity = 2.1f;

    private Transform player;
    private bool night;
    private float nextCheck;

    public void Configure(Material on, Material off)
    {
        onMaterial = on;
        offMaterial = off;
    }

    public void Add(Renderer head, Light light)
    {
        heads.Add(head);
        if (light != null) { Tune(light); lights.Add(light); }
    }

    void OnEnable() => DayNightCycle.OnPhaseChanged += OnPhase;
    void OnDisable() => DayNightCycle.OnPhaseChanged -= OnPhase;

    private DayNightCycle clock;

    void Start()
    {
        foreach (Light l in lights) if (l != null) Tune(l);   // aj lampy postavené predtým
        clock = FindAnyObjectByType<DayNightCycle>();
        if (clock != null) night = IsDark(clock.CurrentPhase);
        PlayerStats stats = FindAnyObjectByType<PlayerStats>();
        if (stats != null) player = stats.transform;
        ApplyHeads();
    }

    void Tune(Light l)
    {
        l.range = lightRange;
        l.intensity = lightIntensity;
    }

    static bool IsDark(DayNightCycle.DayPhase phase) => phase == DayNightCycle.DayPhase.Dusk || phase == DayNightCycle.DayPhase.Night;

    void OnPhase(DayNightCycle.DayPhase phase)
    {
        night = IsDark(phase);
        ApplyHeads();
        nextCheck = 0f;
    }

    void ApplyHeads()
    {
        Material m = night ? onMaterial : offMaterial;
        if (m == null) return;
        for (int i = 0; i < heads.Count; i++)
            if (heads[i] != null) heads[i].sharedMaterial = m;
        if (!night)
            for (int i = 0; i < lights.Count; i++)
                if (lights[i] != null) lights[i].enabled = false;
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;

        // Fáza sa číta aj priamo: čas sa môže nastaviť bez udalosti (načítanie uloženej hry, SetClock)
        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
        if (clock != null)
        {
            bool dark = IsDark(clock.CurrentPhase);
            if (dark != night) { night = dark; ApplyHeads(); }
        }
        if (!night) return;

        if (player == null)
        {
            PlayerStats stats = FindAnyObjectByType<PlayerStats>();
            if (stats == null) return;
            player = stats.transform;
        }

        Vector3 p = player.position;
        float max = lightDistance * lightDistance;
        for (int i = 0; i < lights.Count; i++)
        {
            Light l = lights[i];
            if (l == null) continue;
            Vector3 d = l.transform.position - p;
            d.y = 0f;
            l.enabled = d.sqrMagnitude < max;
        }
    }
}
