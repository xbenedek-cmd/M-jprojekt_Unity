using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene kamera so zapnutým Dynamic Clipping si pri vzdialenom pivote zväčší Near plane a blízke objekty miznú skôr, než k nim dôjdeš.
/// Vypne dynamic clip a nastaví pevné Near/Far; platí pre všetky otvorené okná Scene a znova pri každom otvorení projektu.
/// </summary>
[InitializeOnLoad]
public static class SceneCameraFix
{
    const float Near = 0.01f;
    const float Far = 2000f;

    private static readonly HashSet<SceneView> handled = new HashSet<SceneView>();

    static SceneCameraFix()
    {
        EditorApplication.delayCall += () => Apply();
        SceneView.duringSceneGui += view =>   // aj okná Scene otvorené až neskôr
        {
            if (handled.Add(view)) ApplyTo(view);
        };
    }

    static void ApplyTo(SceneView view)
    {
        SceneView.CameraSettings settings = view.cameraSettings;
        settings.dynamicClip = false;
        settings.nearClip = Near;
        settings.farClip = Far;
        view.cameraSettings = settings;
    }

    [MenuItem("Progeny/Scene kamera/Vypnúť dynamic clipping")]
    static void ApplyMenu()
    {
        int count = Apply();
        Debug.Log($"Scene kamera: dynamic clipping vypnutý, Near {Near}, Far {Far} ({count} okien Scene).");
    }

    static int Apply()
    {
        int count = 0;
        foreach (SceneView view in SceneView.sceneViews)
        {
            ApplyTo(view);
            handled.Add(view);
            view.Repaint();
            count++;
        }
        return count;
    }
}
