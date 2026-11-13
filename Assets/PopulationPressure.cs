using UnityEngine;

/// <summary>
/// Tlak na civilistov: útlak korporácie (najvyššie podozrenie mesta za deň) a ničenie divočiny (zber surovín v podobe démona).
/// Pri vysokom tlaku civilisti utekajú (ZonePopulation.ApplyPressure); utečenci čakajú v divočine a vracajú sa, keď tlak opadne.
/// </summary>
public static class PopulationPressure
{
    /// <summary>Civilisti, ktorí utiekli do divočiny (nie sú v žiadnej zóne).</summary>
    public static int Refugees;
    public static float NatureDamage;

    private static float maxSuspicion;
    private static int lastDay = -1;
    private static float cachedPressure;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        Refugees = 0;
        NatureDamage = 0f;
        maxSuspicion = 0f;
        lastDay = -1;
        cachedPressure = 0f;
    }

    /// <summary>Zaznamená podozrenie (volajú zóny pravidelne); do dňa sa berie najvyššia hodnota.</summary>
    public static void Sample(float suspicion)
    {
        if (suspicion > maxSuspicion) maxSuspicion = suspicion;
    }

    public static void AddNatureDamage(float amount) => NatureDamage += Mathf.Max(0f, amount);

    /// <summary>Aktuálny tlak 0..1 (len na zobrazenie, nič nemení).</summary>
    public static float CurrentPressure => Mathf.Clamp01(Mathf.Max(Mathf.Max(maxSuspicion, Reputation.Suspicion) / 100f, Mathf.Max(NatureDamage / 10f, Dread)));

    /// <summary>Strach z hráča, ktorý stratil ľudskosť (SoulBalance.Rejected): od 0,5 civilisti utekajú.</summary>
    public static float Dread => SoulBalance.Rejected ? Mathf.Lerp(0.5f, 1f, (SoulBalance.RejectBelow - SoulBalance.Humanity) / SoulBalance.RejectBelow) : 0f;

    /// <summary>Tlak 0..1 za uplynulý deň (rovnaký pre všetky zóny); pri prvom volaní v dni sa počíta a začne nový deň.</summary>
    public static float PressureForDay(int day)
    {
        if (day == lastDay) return cachedPressure;

        lastDay = day;
        cachedPressure = Mathf.Clamp01(Mathf.Max(Mathf.Max(maxSuspicion / 100f, NatureDamage / 10f), Dread));
        maxSuspicion = Reputation.Suspicion;
        NatureDamage *= 0.7f;
        return cachedPressure;
    }
}
