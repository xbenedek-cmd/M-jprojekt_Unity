using UnityEditor;
using UnityEngine;

public static class ChopAnimationMenu
{
    [MenuItem("Progeny/Animácie/Pridať sekanie dreva na označený model")]
    static void AddChopAnimation()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogWarning("Najprv označ model drevorubača (koreň s kosťami) v Hierarchy.");
            return;
        }

        if (go.GetComponent<ChopAnimation>() != null)
        {
            Debug.Log("Tento model už má ChopAnimation.");
            return;
        }

        Undo.AddComponent<ChopAnimation>(go);
        Debug.Log("ChopAnimation pridaná. Spusti hru (Play) a postava začne sekať.");
    }
}