using UnityEditor;

/// <summary>
/// Ak nie je v Build Settings žiadna scéna, doplní sa SampleScene. Bez toho by sa scéna nedala znova načítať
/// (Nová hra a Hlavné menu v PauseMenu používajú SceneManager.LoadScene).
/// </summary>
[InitializeOnLoad]
static class BuildSettingsGuard
{
    const string ScenePath = "Assets/Scenes/SampleScene.unity";

    static BuildSettingsGuard()
    {
        if (EditorBuildSettings.scenes.Length > 0) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return;

        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}
