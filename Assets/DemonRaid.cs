using UnityEngine;

/// <summary>
/// Démon (hráč) ničí budovy: za zničenie dostane peniaze, XP a dôveru démonov a Arakaniho. Ak ho pritom niekto uvidí,
/// nahlási ho vojakom a ti ho začnú prenasledovať a strieľať. Zatknutie neexistuje: treba ujsť z dohľadu alebo zabiť svedkov.
/// </summary>
public static class DemonRaid
{
    private const float WantedSeconds = 60f;
    private const float SoldierWitnessRange = 50f;
    private const float CivilianWitnessRange = 30f;
    private const float SoldierAlarmRadius = 140f;

    private static float wantedUntil;

    public static bool Wanted => Time.time < wantedUntil;
    /// <summary>Koľko budov hráč-démon zničil (cieľ cesty démona).</summary>
    public static int Destroyed;
    /// <summary>Nedávne ničenie budov: každé zničenie +1, denne klesne o 1. Z neho sa rozhoduje, či armáda pošle komandá (Destroyed je celková štatistika pre ciele a rozkazy).</summary>
    public static float Recent;
    public static void DecayRecent() => Recent = Mathf.Max(0f, Recent - 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState() { wantedUntil = 0f; Destroyed = 0; Recent = 0f; }

    /// <summary>Predĺži hľadanie, kým vojak démona vidí.</summary>
    public static void Extend(float seconds = 45f) => wantedUntil = Mathf.Max(wantedUntil, Time.time + seconds);

    /// <summary>Hráč poškodil budovu; po zničení príde odmena. Svedkovia sa zisťujú pri každom zásahu.</summary>
    public static void OnPlayerHitBuilding(BuildPlot plot, PlayerStats player, bool destroyed)
    {
        if (player == null) return;

        if (!player.IsInvisible && HasWitness(player.transform.position + Vector3.up * 1.2f))
            RaiseAlarm(player.transform.position);

        if (!destroyed) return;

        BuildingDef def = plot.Building;
        int price = def != null ? def.cost : 100;
        int money = Mathf.RoundToInt(Mathf.Max(30, price / 2) * SoulBalance.RaidRewardFactor);
        int xp = Mathf.RoundToInt((15 + price / 12) * SoulBalance.RaidRewardFactor);

        player.AddMoney(money);
        player.AddDemonXp(xp);

        // Démonické klany a Arakani oceňujú ničenie ľudských stavieb; ľudská frakcia parcely sa hnevá
        for (FactionId id = FactionId.ShadowClan; id <= FactionId.LightningClan; id++) Reputation.Add(id, 3f);
        Reputation.Add(plot.Owner, -8f);
        ArakaniFavor.AddFavor(4f);
        SoulBalance.Add(-2f);
        Destroyed++;
        Recent += 1f;
        player.Heal(player.MaxHealth * SoulBalance.KillHealFraction);

        GameHUD.Message($"Budova zničená: +{money} peňazí, +{xp} XP, dôvera démonov a Arakaniho rastie.", 4f);
    }

    static bool HasWitness(Vector3 eye)
    {
        foreach (SoldierAI s in SoldierAI.All)
        {
            if (s == null || s.IsDead || !s.gameObject.activeInHierarchy) continue;
            if ((s.transform.position - eye).sqrMagnitude > SoldierWitnessRange * SoldierWitnessRange) continue;
            if (StealthWorld.HasLineOfSight(s.transform.position + Vector3.up * 1.4f, eye)) return true;
        }

        foreach (CivilianAI c in CivilianAI.All)
        {
            if (c == null || c.IsDead || !c.gameObject.activeInHierarchy) continue;
            if ((c.transform.position - eye).sqrMagnitude > CivilianWitnessRange * CivilianWitnessRange) continue;
            if (StealthWorld.HasLineOfSight(c.transform.position + Vector3.up * 1.4f, eye)) return true;
        }
        return false;
    }

    static void RaiseAlarm(Vector3 where)
    {
        bool fresh = !Wanted;
        wantedUntil = Time.time + WantedSeconds;

        SoldierAI.ReportPlayerDemon(where, SoldierAlarmRadius);
        CivilianAI.Panic(where, 25f);

        if (fresh) GameHUD.Message("Videli ťa ničiť budovu! Vojaci ťa hľadajú a strieľajú. Uteč z dohľadu, alebo ich zabi.", 5f);
    }
}
