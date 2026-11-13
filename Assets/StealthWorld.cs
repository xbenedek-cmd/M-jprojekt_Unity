using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spoločné pomôcky pre zakrádanie: výhľad (čo stojí v ceste), svedkovia vraždy a nájdené telá.
/// </summary>
public static class StealthWorld
{
    public class Body
    {
        public Vector3 pos;
        public float time;
        public bool found;
        public bool humanVictim;
        public string victimName;
        public Damageable corpse;      // telo, ktoré sa dá odviezť
        public bool claimed;           // nejaký vojak ho už ide skontrolovať
        public bool examined;          // skontrolované: telo zmizlo
    }

    public static readonly List<Body> Bodies = new List<Body>();

    /// <summary>Frame, v ktorom prebehlo tiché zabitie (ostatné skripty vtedy netrestajú podruhé).</summary>
    public static int SilentKillFrame = -1;
    public static bool IsSilentKillNow => SilentKillFrame == Time.frameCount;

    private static readonly RaycastHit[] hitBuffer = new RaycastHit[24];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        Bodies.Clear();
        SilentKillFrame = -1;
    }

    public static Body RegisterBody(Vector3 pos, bool humanVictim, string victimName, Damageable corpse = null)
    {
        // Odstránia sa telá, ktoré už zmizli (skontrolované, skryté, po čase); nenájdené tela ostávajú, kým existuje mŕtvola
        Bodies.RemoveAll(b => b.examined || (b.corpse != null ? b.corpse.IsHidden : b.found && Time.time - b.time > 120f));

        // Ten istý zabitý sa nezapisuje dvakrát (Damageable, tiché zabitie aj vojak ho hlásia)
        foreach (Body existing in Bodies)
        {
            bool same = corpse != null ? existing.corpse == corpse : (existing.pos - pos).sqrMagnitude < 2.25f && Time.time - existing.time < 2f;
            if (!same) continue;
            if (existing.corpse == null) existing.corpse = corpse;
            return existing;
        }

        var body = new Body { pos = pos, time = Time.time, humanVictim = humanVictim, victimName = victimName, corpse = corpse };
        Bodies.Add(body);
        return body;
    }

    /// <summary>Vojak telo skontroloval: nahlási sa a odvezie (mŕtvola zmizne).</summary>
    public static void ExamineBody(Body body)
    {
        if (body == null) return;
        body.examined = true;
        if (body.corpse != null)
        {
            KillConsequences.OnBodyDiscovered(body.corpse);
            body.corpse.MarkReported();
            body.corpse.RemoveCorpse();
        }
        Bodies.Remove(body);
    }

    /// <summary>
    /// Vidí sa z bodu "from" do bodu "to"? Výhľad blokuje len statický svet (steny, budovy),
    /// nie postavy (majú Rigidbody) ani priehľadné sklo výkladov.
    /// </summary>
    public static bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float dist = d.magnitude;
        if (dist < 0.05f) return true;

        int n = Physics.RaycastNonAlloc(from, d / dist, hitBuffer, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = hitBuffer[i].collider;
            if (c.attachedRigidbody != null) continue;                         // postavy a NPC
            if (c.GetComponentInParent<Damageable>() != null) continue;        // zvieratá a NPC bez Rigidbody
            if (c.GetComponentInParent<ReflectiveSurface>() != null) continue; // sklo
            if (c.GetComponentInParent<PlayerStats>() != null) continue;
            return false;
        }
        return true;
    }

    /// <summary>Vidí pozorovateľ daný bod? (dosah, zorný uhol, výhľad)</summary>
    public static bool CanSeePoint(Transform watcher, Vector3 point, float range, float fovDegrees)
    {
        Vector3 to = point - watcher.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist > range) return false;
        if (dist > 0.01f && Vector3.Angle(watcher.forward, to) > fovDegrees * 0.5f) return false;

        return HasLineOfSight(watcher.position + Vector3.up * 0.8f, point + Vector3.up * 0.8f);
    }

    /// <summary>
    /// Koľko ľudí (civilisti, vojaci) videlo, čo sa stalo na danom mieste. Neviditeľný hráč nemá svedkov.
    /// </summary>
    public static int CountWitnesses(Vector3 pos, Transform exclude, float radius, float fov)
    {
        int count = 0;

        foreach (CivilianAI c in CivilianAI.All)
        {
            if (c == null || c.IsDead || c.transform == exclude) continue;
            if (CanSeePoint(c.transform, pos, Mathf.Min(radius, CivilianAI.SightRadius), fov)) count++;
        }
        foreach (SoldierAI s in SoldierAI.All)
        {
            if (s == null || s.IsDead || s.transform == exclude) continue;
            if (CanSeePoint(s.transform, pos, Mathf.Min(radius, SoldierAI.SightRadius), fov)) count++;
        }
        foreach (CityGuardNPC g in CityGuardNPC.All)
        {
            if (g == null || g.IsDead || g.transform == exclude) continue;
            if (CanSeePoint(g.transform, pos, Mathf.Min(radius, SoldierAI.SightRadius), fov)) count++;
        }
        return count;
    }

    /// <summary>Svedkovia sa zľaknú (civilisti utekajú, vojaci poplach).</summary>
    public static void AlarmWitnesses(Vector3 pos, Transform exclude, Vector3 playerPos, float radius, float fov)
    {
        foreach (CivilianAI c in CivilianAI.All)
        {
            if (c == null || c.IsDead || c.transform == exclude) continue;
            if (CanSeePoint(c.transform, pos, Mathf.Min(radius, CivilianAI.SightRadius), fov)) c.Alarm(playerPos);
        }
        foreach (SoldierAI s in SoldierAI.All)
        {
            if (s == null || s.IsDead || s.transform == exclude) continue;
            if (CanSeePoint(s.transform, pos, Mathf.Min(radius, SoldierAI.SightRadius), fov)) s.AlertTo(playerPos);
        }
        foreach (CityGuardNPC g in CityGuardNPC.All)
        {
            if (g == null || g.IsDead || g.transform == exclude) continue;
            if (CanSeePoint(g.transform, pos, Mathf.Min(radius, SoldierAI.SightRadius), fov)) g.AlertTo(playerPos);
        }
    }
}
