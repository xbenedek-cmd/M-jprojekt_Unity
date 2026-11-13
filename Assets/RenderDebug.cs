using UnityEngine;

/// <summary>Dočasná diagnostika miznutia objektov: skripty, ktoré vypnú Renderer alebo celú zónu, to tu zapíšu do Console (len v editore).</summary>
public static class RenderDebug
{
    public static bool Enabled = true;

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Hidden(Object who, string reason)
    {
        if (Enabled) Debug.Log($"[Zmizne] {(who != null ? who.name : "?")}: {reason}", who);
    }
}
