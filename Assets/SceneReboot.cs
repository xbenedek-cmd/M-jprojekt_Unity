using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Systémy hry (HUD, mapa, príbeh, uloženie, zóny...) vznikajú cez [RuntimeInitializeOnLoadMethod(AfterSceneLoad)], čo Unity spustí
/// len raz pri štarte hry. Po SceneManager.LoadScene (Nová hra, Načítať z pauzy, Hlavné menu) by preto v novej scéne chýbali.
/// Tento objekt prežije načítanie scény a po ňom (o snímok neskôr, kým sa zničia objekty starej scény) znova zavolá všetky tieto metódy.
/// Ich vlastné kontroly ("ak už existuje, nerob nič") zabránia duplicitám.
/// </summary>
public class SceneReboot : MonoBehaviour
{
    private static MethodInfo[] bootMethods;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        if (FindAnyObjectByType<SceneReboot>() != null) return;

        var go = new GameObject("SceneReboot");
        DontDestroyOnLoad(go);
        go.AddComponent<SceneReboot>();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;   // aditívne zóny nič nerestartujú
        StartCoroutine(Reboot());
    }

    IEnumerator Reboot()
    {
        yield return null;   // objekty starej scény sa zničia na konci snímku; až potom môžu metódy kontrolovať "už existuje"

        if (bootMethods == null) bootMethods = FindBootMethods();
        int ok = 0;
        foreach (MethodInfo m in bootMethods)
        {
            try { m.Invoke(null, null); ok++; }
            catch (System.Exception e) { Debug.LogWarning($"[SceneReboot] {m.DeclaringType?.Name}.{m.Name} zlyhalo: {(e.InnerException ?? e).Message}"); }
        }
        Debug.Log($"[SceneReboot] Scéna znova načítaná: spustených {ok} systémov.");
    }

    static MethodInfo[] FindBootMethods()
    {
        var list = new List<MethodInfo>();
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (System.Type type in typeof(SceneReboot).Assembly.GetTypes())
        {
            if (type == typeof(SceneReboot) || type.IsGenericTypeDefinition) continue;
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.GetParameters().Length != 0) continue;
                var attribute = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                if (attribute != null && attribute.loadType == RuntimeInitializeLoadType.AfterSceneLoad) list.Add(method);
            }
        }
        return list.ToArray();
    }
}
