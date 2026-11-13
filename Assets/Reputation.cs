using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dôvera jednotlivých frakcií (-100 až +100) a podozrenie mesta (0 až 100, samo klesá).
/// Zdieľaný stav hry; menu dôvery ho neskôr zobrazí.
/// </summary>
public static class Reputation
{
    public static event Action Changed;

    public const float SuspicionDecayPerSecond = 0.1f;

    private static readonly Dictionary<FactionId, float> trust = new Dictionary<FactionId, float>();
    private static float suspicion;
    private static float suspicionStamp;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        trust.Clear();
        suspicion = 0f;
        suspicionStamp = 0f;
        Changed = null;
    }

    public static float Get(FactionId id)
    {
        float v;
        return trust.TryGetValue(id, out v) ? v : 0f;
    }

    public static void Add(FactionId id, float delta)
    {
        if (id == FactionId.None) return;
        trust[id] = Mathf.Clamp(Get(id) + delta, -100f, 100f);
        if (Changed != null) Changed();
    }

    public static float Suspicion
    {
        get
        {
            Decay();
            return suspicion;
        }
    }

    public static void AddSuspicion(float amount)
    {
        Decay();
        suspicion = Mathf.Clamp(suspicion + amount, 0f, 100f);
        if (Changed != null) Changed();
    }

    static void Decay()
    {
        float now = Time.time;
        suspicion = Mathf.Max(0f, suspicion - (now - suspicionStamp) * SuspicionDecayPerSecond);
        suspicionStamp = now;
    }
}
