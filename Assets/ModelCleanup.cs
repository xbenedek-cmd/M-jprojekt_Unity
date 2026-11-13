using UnityEngine;

/// <summary>Modely z FBX / GLB nesú vložené kamery, svetlá a počúvače zvuku: po vytvorení v hre sa vypnú a zmažú (inak by rušili hlavnú kameru a osvetlenie).</summary>
public static class ModelCleanup
{
    public static int StripCamerasAndLights(GameObject instance)
    {
        int removed = 0;
        foreach (AudioListener a in instance.GetComponentsInChildren<AudioListener>(true)) { a.enabled = false; Object.Destroy(a); removed++; }
        foreach (Camera c in instance.GetComponentsInChildren<Camera>(true)) { c.enabled = false; Object.Destroy(c); removed++; }
        foreach (Light l in instance.GetComponentsInChildren<Light>(true)) { l.enabled = false; Object.Destroy(l); removed++; }
        return removed;
    }
}
