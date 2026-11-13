using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tabuľka "ťažká sieť -> odľahčená sieť". Modely z generátorov majú milióny trojuholníkov; namiesto úpravy
/// pôvodných súborov sa pri vložení modelu do hry jeho sieť vymení za odľahčenú kópiu (rovnaká kostra, UV aj materiály).
/// Tabuľku plní menu Progeny → Hráč → Odľahčiť siete; leží v Resources/LightMeshTable.asset.
/// Vypnutie: zmazať asset alebo záznam, model sa potom zobrazí v pôvodnej sieti.
/// </summary>
public class LightMeshTable : ScriptableObject
{
    public const string ResourceName = "LightMeshTable";

    [SerializeField] private List<Mesh> heavy = new List<Mesh>();
    [SerializeField] private List<Mesh> light = new List<Mesh>();

    private static LightMeshTable loaded;
    private static bool searched;
    private static readonly List<Renderer> rendererBuffer = new List<Renderer>(16);
    private static readonly List<MeshCollider> colliderBuffer = new List<MeshCollider>(8);

    public int Count => heavy.Count;

    public void Set(Mesh heavyMesh, Mesh lightMesh)
    {
        int i = heavy.IndexOf(heavyMesh);
        if (i >= 0) light[i] = lightMesh;
        else
        {
            heavy.Add(heavyMesh);
            light.Add(lightMesh);
        }
    }

    /// <summary>Odľahčená sieť zapísaná pre danú ťažkú (null = žiadna).</summary>
    public Mesh Get(Mesh heavyMesh) => Find(heavyMesh);

    Mesh Find(Mesh heavyMesh)
    {
        for (int i = 0; i < heavy.Count; i++)
            if (heavy[i] == heavyMesh) return light[i];
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        loaded = null;
        searched = false;
    }

    static bool Load()
    {
        if (!searched)
        {
            searched = true;
            loaded = Resources.Load<LightMeshTable>(ResourceName);
        }
        return loaded != null && loaded.heavy.Count > 0;
    }

    // Modely uložené priamo v scéne (domy, jedlo): siete sa vymenia raz po jej načítaní, scéna sa nemení
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        if (!Load()) return;
        int swapped = 0;
        foreach (MeshFilter filter in FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
        {
            Mesh replacement = loaded.Find(filter.sharedMesh);
            if (replacement == null) continue;
            filter.sharedMesh = replacement;
            swapped++;
        }
        foreach (SkinnedMeshRenderer skinned in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include))
        {
            Mesh replacement = loaded.Find(skinned.sharedMesh);
            if (replacement == null) continue;
            skinned.sharedMesh = replacement;
            swapped++;
        }
        // Kolízia z miliónov trojuholníkov zdržiava fyziku rovnako ako kreslenie
        foreach (MeshCollider collider in FindObjectsByType<MeshCollider>(FindObjectsInactive.Include))
        {
            Mesh replacement = loaded.Find(collider.sharedMesh);
            if (replacement == null) continue;
            collider.sharedMesh = replacement;
            swapped++;
        }
        if (swapped > 0) Debug.Log($"LightMeshTable: v scéne vymenených {swapped} ťažkých sietí za odľahčené.");
    }

    /// <summary>Vymení siete v práve vytvorenej kópii modelu za odľahčené. Vráti počet vymenených.</summary>
    public static int Apply(GameObject instance)
    {
        if (instance == null) return 0;
        if (!Load()) return 0;

        int swapped = 0;
        instance.GetComponentsInChildren(true, rendererBuffer);
        for (int i = 0; i < rendererBuffer.Count; i++)
        {
            Renderer r = rendererBuffer[i];
            if (r is SkinnedMeshRenderer skinned)
            {
                Mesh replacement = loaded.Find(skinned.sharedMesh);
                if (replacement == null) continue;
                skinned.sharedMesh = replacement;
                swapped++;
            }
            else if (r.TryGetComponent(out MeshFilter filter))
            {
                Mesh replacement = loaded.Find(filter.sharedMesh);
                if (replacement == null) continue;
                filter.sharedMesh = replacement;
                swapped++;
            }
        }
        rendererBuffer.Clear();

        instance.GetComponentsInChildren(true, colliderBuffer);
        for (int i = 0; i < colliderBuffer.Count; i++)
        {
            Mesh replacement = loaded.Find(colliderBuffer[i].sharedMesh);
            if (replacement == null) continue;
            colliderBuffer[i].sharedMesh = replacement;
            swapped++;
        }
        colliderBuffer.Clear();
        return swapped;
    }
}
