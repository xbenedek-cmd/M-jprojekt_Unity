using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Zvuky krokov z balíka Assets/Sounds/footsteps_pack_2: priradí priečinky k povrchom a uloží Resources/FootstepSet.asset
/// (z každého priečinka sa berie rovnomerne rozložených najviac MaxClipsPerSurface záznamov, aby sa nenačítali tisíce súborov).
/// Spúšťa sa samo pri prvom načítaní editora, ak asset chýba, a ručne cez menu Progeny → Zvuk.
/// </summary>
public static class ProgenyFootsteps
{
    const string SoundsFolder = "Assets/Sounds/footsteps_pack_2";
    const string AssetPath = "Assets/Resources/FootstepSet.asset";
    const int MaxClipsPerSurface = 14;

    // povrch → priečinok kráčania (a voliteľne behu)
    static readonly (StepSurface surface, string walk, string run)[] Mapping =
    {
        (StepSurface.Grass, "forest_cover", null),
        (StepSurface.Leaves, "leafs_dry", null),
        (StepSurface.Thicket, "thickets", null),
        (StepSurface.Dirt, "dirt_road", null),
        (StepSurface.Mud, "mud_dirt_walk", "mud_dirt_run"),
        (StepSurface.Stone, "stones", null),
        (StepSurface.Rubble, "debris_rubble", null),
        (StepSurface.RubbleMetal, "debris_rubble_clangorous", null),
        (StepSurface.Hay, "hay_straw", null),
        (StepSurface.WaterShallow, "puddle_shallow", null),
        (StepSurface.WaterDeep, "puddle_deeper", null)
    };

    [InitializeOnLoadMethod]
    static void EnsureOnLoad()
    {
        if (AssetDatabase.LoadAssetAtPath<FootstepSet>(AssetPath) != null) return;
        EditorApplication.delayCall += () => { if (AssetDatabase.IsValidFolder(SoundsFolder)) Build(false); };
    }

    [MenuItem("Progeny/Materiály a zvuk/Kroky z balíka (podľa povrchu)", false, 800)]
    static void BuildMenu() => Build(true);

    internal static void Build(bool log)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

        FootstepSet set = AssetDatabase.LoadAssetAtPath<FootstepSet>(AssetPath);
        bool isNew = set == null;
        if (isNew) set = ScriptableObject.CreateInstance<FootstepSet>();

        var entries = new List<FootstepSet.Entry>();
        int total = 0;
        foreach (var m in Mapping)
        {
            var entry = new FootstepSet.Entry { surface = m.surface, walk = LoadFolder(m.walk) };
            if (m.run != null) entry.run = LoadFolder(m.run);
            total += entry.walk.Length + (entry.run != null ? entry.run.Length : 0);
            entries.Add(entry);
        }
        set.entries = entries.ToArray();

        if (isNew) AssetDatabase.CreateAsset(set, AssetPath);
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();
        if (log) Debug.Log($"Kroky: {entries.Count} povrchov, {total} záznamov (max {MaxClipsPerSurface} z priečinka) uložených do {AssetPath}.");
    }

    static AudioClip[] LoadFolder(string folderName)
    {
        string folder = $"{SoundsFolder}/{folderName}";
        if (!AssetDatabase.IsValidFolder(folder)) return new AudioClip[0];

        var all = new List<AudioClip>();
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            if (clip != null) all.Add(clip);
        }
        all.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

        if (all.Count <= MaxClipsPerSurface) return all.ToArray();

        var picked = new AudioClip[MaxClipsPerSurface];
        for (int i = 0; i < MaxClipsPerSurface; i++) picked[i] = all[i * all.Count / MaxClipsPerSurface];
        return picked;
    }
}
