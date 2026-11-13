using UnityEngine;

/// <summary>Kto je pre hráča nepriateľ - používajú to schopnosti, ktoré si hľadajú cieľ samy.</summary>
public static class Targeting
{
    /// <summary>
    /// Človek: nepriateľ sú démoni.
    /// Démon: nepriateľ sú ľudia a zvieratá (korisť) a démoni, ktorí ho práve naháňajú.
    /// </summary>
    public static bool IsEnemy(PlayerStats player, Damageable d)
    {
        if (d == null || d.IsDead) return false;
        if (player == null) return true;

        if (!player.IsDemon) return d.team == CombatTeam.Demon;

        if (d.team == CombatTeam.Civilian) return true;
        if (d.team == CombatTeam.Demon)
        {
            DemonEnemy e = d.GetComponent<DemonEnemy>();
            return e != null && e.IsChasing;
        }
        return false;
    }
}
