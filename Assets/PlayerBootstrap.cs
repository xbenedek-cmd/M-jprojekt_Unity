using UnityEngine;

/// <summary>
/// Po načítaní scény doplní hráčovi chýbajúce komponenty vzhľadu a výbavy (model postavy, zbrane, inventár).
/// Vďaka tomu sa pri každom spustení hrá za postavu, nie za provizórnu kocku, aj keď scéna ešte nebola nastavená menu
/// Progeny → Hráč. Komponenty, ktoré už na hráčovi sú, sa nemenia.
/// </summary>
public static class PlayerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsurePlayerComponents()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        if (player == null) return;

        JailManager.EnsureExists(player.transform.position);

        // Každý boss má fázy súboja (aj bez pevného miesta)
        foreach (DemonEnemy demon in Object.FindObjectsByType<DemonEnemy>(FindObjectsInactive.Include))
            if (demon.tier == DemonTier.Boss && demon.GetComponent<BossPhases>() == null) demon.gameObject.AddComponent<BossPhases>();

        GameObject go = player.gameObject;
        if (go.GetComponent<Inventory>() == null) go.AddComponent<Inventory>();
        if (go.GetComponent<PlayerVisual>() == null) go.AddComponent<PlayerVisual>();
        if (go.GetComponent<PlayerAppearance>() == null) go.AddComponent<PlayerAppearance>();
        if (go.GetComponent<PlayerWeapons>() == null) go.AddComponent<PlayerWeapons>();
        if (go.GetComponent<PlayerClimb>() == null) go.AddComponent<PlayerClimb>();
        if (go.GetComponent<PlayerCover>() == null) go.AddComponent<PlayerCover>();
        if (go.GetComponent<ProfessionInteraction>() == null) go.AddComponent<ProfessionInteraction>();
        if (go.GetComponent<DemonNatureMenu>() == null) go.AddComponent<DemonNatureMenu>();
        if (go.GetComponent<PopulationHud>() == null) go.AddComponent<PopulationHud>();
        if (go.GetComponent<WorkshopMenu>() == null) go.AddComponent<WorkshopMenu>();
        if (go.GetComponent<SoulMenu>() == null) go.AddComponent<SoulMenu>();
        if (go.GetComponent<SoulAbilities>() == null) go.AddComponent<SoulAbilities>();
    }
}
