using UnityEditor;

/// <summary>
/// Pri štarte hry sa zruší výber v Hierarchy. Hra pri štarte zámerne ničí/nahrádza komponenty na vybraných objektoch
/// (PlayerVisual vymení BoxCollider hráča za kapsulu, NPCPerception odstráni kolízie z modelov, parcely a zóny vypínajú postavy).
/// Ak je taký objekt vybraný, Inspector hlási SerializedObjectNotCreatableException / MissingReferenceException (GameObjectInspector).
/// Chyby sú len v editore a hru neovplyvňujú; zrušením výberu pred štartom sa nevytvoria.
/// </summary>
[InitializeOnLoad]
static class PlayModeSelectionGuard
{
    static PlayModeSelectionGuard() => EditorApplication.playModeStateChanged += OnStateChanged;

    static void OnStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            Selection.objects = new UnityEngine.Object[0];
    }
}
