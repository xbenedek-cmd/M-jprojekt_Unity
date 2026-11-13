using UnityEditor;
using UnityEngine;

/// <summary>Ladenie príbehu za behu hry (Play Mode).</summary>
public static partial class ProgenyTools
{
    [MenuItem("Progeny/Príbeh/Spustiť hrateľný prológ znova (za behu)", false, 700)]
    static void RestartPrologue()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Prológ sa dá spustiť znova len počas hry (Play)."); return; }
        if (!Prologue.CanRun()) { Debug.LogWarning("Prológ potrebuje zónu Divočina (Zem_Divocina)."); return; }
        Prologue.Restart();
        Debug.Log("Prológ sa spustí odznova o pár sekúnd (drevorubači sa objavia pri tebe).");
    }
}
