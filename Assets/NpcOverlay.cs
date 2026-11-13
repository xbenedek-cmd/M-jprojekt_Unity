using UnityEngine;

/// <summary>
/// Kedy sa zobrazujú prvky nad postavami (lišty zdravia, "!", "?", texty, čo postavy hovoria): len do 15 m od hráča
/// a nikdy, keď je otvorené nejaké menu (menu hry, inventár, pauza, obchod, dielňa...).
/// </summary>
public static class NpcOverlay
{
    public const float Range = 15f;

    private static Transform player;
    private static float nextSearch;

    public static bool Visible(Vector3 worldPosition)
    {
        if (Healer.MenuOpen) return false;   // zahŕňa menu hry (Tab), inventár, pauzu a ostatné ponuky

        if (player == null)
        {
            if (Time.unscaledTime < nextSearch) return false;
            nextSearch = Time.unscaledTime + 1f;

            PlayerStats stats = Object.FindAnyObjectByType<PlayerStats>();
            if (stats == null) return false;
            player = stats.transform;
        }

        Vector3 d = worldPosition - player.position;
        d.y = 0f;
        return d.sqrMagnitude <= Range * Range;
    }
}
