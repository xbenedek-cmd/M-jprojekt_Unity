using System.Collections.Generic;
using UnityEngine;

public enum VictimKind { Civilian, Rebel, Soldier }

/// <summary>
/// Následky zabitia človeka hráčom. Kým nikto na zabitie nepríde (žiadny svedok, nikto nenájde telo), hráča nikto netrestá.
/// Po objavení sa mení dôvera frakcií:
///  - civilista: klesne u všetkých (civilisti, odboj aj Megakorporácia),
///  - vojak: klesne Megakorporácia aj civilisti, odboj mierne stúpne,
///  - odbojár: klesne odboj, civilisti mierne, Megakorporácia výrazne stúpne.
/// Hneď (aj neobjavené) sa nedeje nič okrem tichého uznania: odboj sa teší zo zabitého vojaka, vojaci z odstráneného odbojára.
/// Objavenie = svedok pri zabití, alebo vojak/strážnik/civilista nájde telo.
/// </summary>
public static class KillConsequences
{
    private static readonly Dictionary<Damageable, VictimKind> pending = new Dictionary<Damageable, VictimKind>();
    private static readonly HashSet<Damageable> settled = new HashSet<Damageable>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        pending.Clear();
        settled.Clear();
    }

    public static bool TryGetKind(Damageable victim, out VictimKind kind)
    {
        kind = VictimKind.Civilian;
        if (victim == null) return false;

        if (victim.GetComponent<SoldierAI>() != null || victim.GetComponent<CityGuardNPC>() != null) { kind = VictimKind.Soldier; return true; }
        CivilianAI civ = victim.GetComponent<CivilianAI>();
        if (civ == null) return false;
        kind = civ.IsResistance ? VictimKind.Rebel : VictimKind.Civilian;
        return true;
    }

    /// <summary>Hráč práve zabil človeka. Hneď sa vyhodnotí len svedok; inak sa čaká, či niekto nájde telo.</summary>
    public static void OnPlayerKill(Damageable victim)
    {
        if (!TryGetKind(victim, out VictimKind kind)) return;

        // Tichý zisk bez objavenia: odboj sa teší z mŕtveho vojaka, Megakorporácia z mŕtveho odbojára
        if (kind == VictimKind.Soldier) Reputation.Add(FactionId.Rebels, 2f);
        else if (kind == VictimKind.Rebel) Reputation.Add(FactionId.Megacorp, 2f);

        pending[victim] = kind;

        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        bool seen = player != null && !player.IsInvisible
            && StealthWorld.CountWitnesses(victim.transform.position, victim.transform, SoldierAI.SightRadius, 140f) > 0;
        if (seen) Discovered(victim);
    }

    /// <summary>Niekto (vojak, strážnik, civilista) našiel telo zabitého človeka.</summary>
    public static void OnBodyDiscovered(Damageable victim)
    {
        if (victim != null && pending.ContainsKey(victim)) Discovered(victim);
    }

    static void Discovered(Damageable victim)
    {
        if (!pending.TryGetValue(victim, out VictimKind kind) || !settled.Add(victim)) return;
        pending.Remove(victim);

        switch (kind)
        {
            case VictimKind.Civilian:
                Reputation.Add(FactionId.Civilians, -10f);
                Reputation.Add(FactionId.Rebels, -8f);
                Reputation.Add(FactionId.Megacorp, -8f);
                Reputation.AddSuspicion(10f);
                GameHUD.Message("Zabitie civilistu sa prezradilo: dôvera klesla u všetkých frakcií.", 4f);
                break;
            case VictimKind.Soldier:
                Reputation.Add(FactionId.Megacorp, -12f);
                Reputation.Add(FactionId.Civilians, -6f);
                Reputation.Add(FactionId.Rebels, 3f);
                GameHUD.Message("Zabitie vojaka sa prezradilo: Megakorporácia a civilisti ti dôverujú menej, odboj o niečo viac.", 4f);
                break;
            default:
                Reputation.Add(FactionId.Rebels, -10f);
                Reputation.Add(FactionId.Civilians, -3f);
                Reputation.Add(FactionId.Megacorp, 10f);
                Reputation.AddSuspicion(6f);
                GameHUD.Message("Zabitie odbojára sa prezradilo: odboj ti dôveruje menej, Megakorporácia výrazne viac.", 4f);
                break;
        }
    }
}
