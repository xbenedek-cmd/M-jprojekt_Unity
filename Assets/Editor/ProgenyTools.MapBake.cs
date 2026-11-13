using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Obrázky zón pre mapu (M): každá zóna sa odfotí zhora a uloží do Assets/Resources/Maps/&lt;Zem_...&gt;.png. Hra ich za behu načíta len
/// pri pozeraní nenačítanej zóny, takže zóna sa kvôli mape nemusí zapínať. Spusti po zmene zón alebo mesta.
/// </summary>
public static partial class ProgenyTools
{
    const int MapBakeSize = 1536;
    const string MapBakeFolder = "Assets/Resources/Maps";

    static readonly string[] MapBakeGrounds =
    {
        "Zem_Megakorporacia", "Zem_Centrum", "Zem_Divocina", "Zem_Kanon", "Zem_Les", "Zem_Pustatina", "Zem_Mociare", "Zem_Bane"
    };

    static Transform FindAnyTransform(string name)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t.name == name && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t)) return t;
        return null;
    }

    [MenuItem("Progeny/Mesto/Mapy zón: uložiť obrázky pre mapu (M)", false, 330)]
    static void BakeZoneMaps()
    {
        Directory.CreateDirectory(MapBakeFolder);

        var camGo = new GameObject("MapBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
        Camera cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.03f, 0.04f, 0.06f);
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 1400f;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.useOcclusionCulling = false;
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        int saved = 0;
        var paths = new List<string>();
        try
        {
            foreach (string name in MapBakeGrounds)
            {
                Transform ground = FindAnyTransform(name);
                Renderer groundRenderer = ground != null ? ground.GetComponent<Renderer>() : null;
                if (groundRenderer == null) { Debug.LogWarning($"Mapy zón: {name} v scéne nie je, preskakujem."); continue; }

                Bounds b = ZoneLoader.GroundBounds(groundRenderer);

                // Zapnúť koreň zóny (aj nadradené), ak je vypnutý; Megakorporácia má dva koreňe
                var toggled = new List<GameObject>();
                for (Transform t = ground; t != null; t = t.parent)
                    if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); toggled.Add(t.gameObject); }
                if (name == "Zem_Megakorporacia")
                    foreach (string rootName in new[] { "CorpCity", "CorpComplex" })
                    {
                        Transform extra = FindAnyTransform(rootName);
                        if (extra != null && !extra.gameObject.activeSelf) { extra.gameObject.SetActive(true); toggled.Add(extra.gameObject); }
                    }

                float half = Mathf.Max(5f, b.extents.x, b.extents.z);
                var rt = new RenderTexture(MapBakeSize, MapBakeSize, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.orthographicSize = half;
                cam.transform.position = new Vector3(b.center.x, b.center.y + 600f, b.center.z);
                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var picture = new Texture2D(MapBakeSize, MapBakeSize, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0f, 0f, MapBakeSize, MapBakeSize), 0, 0);
                picture.Apply();
                RenderTexture.active = previous;
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);

                string path = $"{MapBakeFolder}/{name}.png";
                File.WriteAllBytes(path, picture.EncodeToPNG());
                Object.DestroyImmediate(picture);
                paths.Add(path);
                saved++;

                foreach (GameObject g in toggled) if (g != null) g.SetActive(false);
            }
        }
        finally
        {
            Object.DestroyImmediate(camGo);
        }

        AssetDatabase.Refresh();
        foreach (string path in paths)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
        Debug.Log($"Mapy zón: uložených obrázkov {saved} do {MapBakeFolder}. Mapa (M) ich použije pre nenačítané zóny.");
    }
}
