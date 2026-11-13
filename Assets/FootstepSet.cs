using System;
using UnityEngine;

/// <summary>Zvuky krokov podľa povrchu (vytvára ich Editor menu z Assets/Sounds/footsteps_pack_2; uložené v Resources/FootstepSet).</summary>
// Musí byť v súbore s rovnakým názvom ako trieda, inak Unity asset nespojí so skriptom.
public class FootstepSet : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public StepSurface surface;
        public AudioClip[] walk;
        public AudioClip[] run;   // prázdne = použije sa walk
    }

    public Entry[] entries;

    public AudioClip[] Get(StepSurface surface, bool run)
    {
        if (entries == null) return null;
        foreach (Entry e in entries)
        {
            if (e.surface != surface) continue;
            return run && e.run != null && e.run.Length > 0 ? e.run : e.walk;
        }
        return null;
    }
}
