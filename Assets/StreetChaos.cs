using UnityEngine;

/// <summary>Chaos v uliciach po smrti Rena: príznak na pár dní (číta ho svet/príbeh), HUD správa a pokles dôvery Megakorporácie.</summary>
public static class StreetChaos
{
    public static int UntilDay { get; private set; }
    public static bool Active
    {
        get
        {
            DayNightCycle c = Object.FindAnyObjectByType<DayNightCycle>();
            return c != null && c.Day < UntilDay;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { UntilDay = 0; }

    public static void Start(int days)
    {
        DayNightCycle c = Object.FindAnyObjectByType<DayNightCycle>();
        UntilDay = (c != null ? c.Day : 0) + days;
    }
}
