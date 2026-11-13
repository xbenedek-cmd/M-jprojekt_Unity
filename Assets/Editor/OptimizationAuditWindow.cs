using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools → Optimization Audit. Len číta a hlási (nič nemení): scénové objekty bez static, ťažké siete bez LODGroup a textúry bez kompresie.
/// Pozor pri oprave podľa zoznamu: objekty, ktoré hra za behu ničí/hýbe (stromy, skaly, ruiny, parcely, NPC), NESMÚ byť static.
/// </summary>
public class OptimizationAuditWindow : EditorWindow
{
    private const int MaxListShown = 300;

    private sealed class SceneItem { public GameObject go; public string info; }
    private sealed class TexItem { public string path; public string info; }

    private readonly List<SceneItem> notStatic = new List<SceneItem>();
    private readonly List<SceneItem> heavyNoLod = new List<SceneItem>();
    private readonly List<TexItem> texProblems = new List<TexItem>();

    private int polyLimit = 5000;
    private int texSizeLimit = 2048;
    private bool includeInactive = true;
    private bool skipThirdParty = true;

    private bool sceneScanned, texScanned;
    private bool foldStatic = true, foldLod = true, foldTex = true;
    private Vector2 scroll;

    [MenuItem("Tools/Optimization Audit")]
    static void Open() => GetWindow<OptimizationAuditWindow>("Optimization Audit");

    void OnGUI()
    {
        EditorGUILayout.HelpBox("Okno len hlási. Objekty, ktoré hra za behu ničí alebo hýbe (stromy, skaly, ruiny, parcely, NPC), nemeň na static.", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            polyLimit = EditorGUILayout.IntField("Limit trojuholníkov", polyLimit);
            texSizeLimit = EditorGUILayout.IntField("Limit textúry (px)", texSizeLimit);
        }
        includeInactive = EditorGUILayout.Toggle("Zahrnúť vypnuté objekty (zóny)", includeInactive);
        skipThirdParty = EditorGUILayout.Toggle("Preskočiť balíky tretích strán (NatureManufacture…)", skipThirdParty);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Skenovať scénu", GUILayout.Height(26))) ScanScene();
            if (GUILayout.Button("Skenovať textúry", GUILayout.Height(26))) ScanTextures();
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (sceneScanned)
        {
            DrawSceneList("Bez Occluder/Occludee Static", notStatic, ref foldStatic);
            DrawSceneList($"Viac než {polyLimit} trojuholníkov bez LODGroup", heavyNoLod, ref foldLod);
        }
        if (texScanned) DrawTextures();
        EditorGUILayout.EndScrollView();
    }

    // ---------- Scéna ----------
    void ScanScene()
    {
        notStatic.Clear();
        heavyNoLod.Clear();
        FindObjectsInactive inactive = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;

        foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(inactive))
        {
            GameObject go = r.gameObject;
            if (!IsSceneObject(go)) continue;

            StaticEditorFlags f = GameObjectUtility.GetStaticEditorFlags(go);
            bool occluder = (f & StaticEditorFlags.OccluderStatic) != 0, occludee = (f & StaticEditorFlags.OccludeeStatic) != 0;
            if ((!occluder || !occludee) && !IsDynamic(go))
                notStatic.Add(new SceneItem { go = go, info = $"MeshRenderer: occluder={(occluder ? "áno" : "NIE")}, occludee={(occludee ? "áno" : "NIE")}" });
        }

        foreach (Terrain t in FindObjectsByType<Terrain>(inactive))
        {
            GameObject go = t.gameObject;
            if (!IsSceneObject(go)) continue;

            StaticEditorFlags f = GameObjectUtility.GetStaticEditorFlags(go);
            bool occluder = (f & StaticEditorFlags.OccluderStatic) != 0, occludee = (f & StaticEditorFlags.OccludeeStatic) != 0;
            if (!occluder || !occludee)
                notStatic.Add(new SceneItem { go = go, info = $"Terrain: occluder={(occluder ? "áno" : "NIE")}, occludee={(occludee ? "áno" : "NIE")}" });
        }

        foreach (MeshFilter mf in FindObjectsByType<MeshFilter>(inactive))
            CheckHeavy(mf.gameObject, mf.sharedMesh);
        foreach (SkinnedMeshRenderer sk in FindObjectsByType<SkinnedMeshRenderer>(inactive))
            CheckHeavy(sk.gameObject, sk.sharedMesh);

        heavyNoLod.Sort((a, b) => string.CompareOrdinal(b.info, a.info));
        sceneScanned = true;
        Debug.Log($"Optimization Audit (scéna): {notStatic.Count} bez static, {heavyNoLod.Count} ťažkých sietí bez LODGroup.");
    }

    void CheckHeavy(GameObject go, Mesh mesh)
    {
        if (mesh == null || !IsSceneObject(go)) return;
        if (go.GetComponentInParent<LODGroup>(true) != null) return;

        int tris = TriangleCount(mesh);
        if (tris > polyLimit) heavyNoLod.Add(new SceneItem { go = go, info = $"{tris:D8} trojuholníkov  ({mesh.name})" });
    }

    static int TriangleCount(Mesh mesh)
    {
        int tris = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            var sm = mesh.GetSubMesh(i);
            if (sm.topology == MeshTopology.Triangles) tris += (int)sm.indexCount / 3;
        }
        return tris;
    }

    bool IsSceneObject(GameObject go)
    {
        if (!go.scene.IsValid() || EditorUtility.IsPersistent(go)) return false;
        if (!skipThirdParty) return true;
        // Prefab z balíka tretej strany (NatureManufacture a pod.) nechávame na jeho autorovi
        GameObject src = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (src == null) return true;
        string path = AssetDatabase.GetAssetPath(src);
        return !path.Contains("NatureManufacture") && !path.StartsWith("Packages/");
    }

    // Pohyblivé/ničené objekty sa na static nedávajú
    static bool IsDynamic(GameObject go)
    {
        return go.GetComponentInParent<Rigidbody>(true) != null
            || go.GetComponentInParent<Animator>(true) != null
            || go.GetComponentInParent<BreakableNode>(true) != null
            || go.GetComponentInParent<NPCPerception>(true) != null
            || go.GetComponentInParent<BuildPlot>(true) != null
            || go.GetComponentInParent<PlayerStats>(true) != null;
    }

    void DrawSceneList(string title, List<SceneItem> items, ref bool fold)
    {
        fold = EditorGUILayout.Foldout(fold, $"{title}  ({items.Count})", true);
        if (!fold) return;

        int shown = Mathf.Min(items.Count, MaxListShown);
        for (int i = 0; i < shown; i++)
        {
            SceneItem it = items[i];
            if (it.go == null) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Vybrať", GUILayout.Width(55))) { Selection.activeGameObject = it.go; EditorGUIUtility.PingObject(it.go); }
                EditorGUILayout.LabelField(it.go.name, GUILayout.MinWidth(120));
                EditorGUILayout.LabelField(it.info);
            }
        }
        if (items.Count > shown) EditorGUILayout.LabelField($"… a ďalších {items.Count - shown} (skrátené)");
    }

    // ---------- Textúry ----------
    void ScanTextures()
    {
        texProblems.Clear();
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Textúry", path, i / (float)guids.Length)) break;
                if (skipThirdParty && path.Contains("NatureManufacture")) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType == TextureImporterType.Sprite || importer.textureType == TextureImporterType.GUI
                    || importer.textureType == TextureImporterType.Cursor) continue;

                var problems = new List<string>();
                if (importer.textureCompression == TextureImporterCompression.Uncompressed) problems.Add("bez kompresie");
                if (importer.isReadable) problems.Add("Read/Write (2× pamäť)");

                // Veľkosť sa načíta len keď ju nastavenie nezastrešuje: maxTextureSize pod limitom ju už znižuje
                if (importer.maxTextureSize > texSizeLimit)
                {
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (tex != null && (tex.width > texSizeLimit || tex.height > texSizeLimit))
                        problems.Add($"{tex.width}×{tex.height} (max {importer.maxTextureSize}px)");
                }

                if (problems.Count > 0) texProblems.Add(new TexItem { path = path, info = string.Join(", ", problems) });
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        texScanned = true;
        Debug.Log($"Optimization Audit (textúry): {texProblems.Count} problematických z {guids.Length}.");
    }

    void DrawTextures()
    {
        foldTex = EditorGUILayout.Foldout(foldTex, $"Textúry: bez kompresie / nad {texSizeLimit}px / Read-Write  ({texProblems.Count})", true);
        if (!foldTex) return;

        int shown = Mathf.Min(texProblems.Count, MaxListShown);
        for (int i = 0; i < shown; i++)
        {
            TexItem it = texProblems[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Vybrať", GUILayout.Width(55)))
                {
                    Object o = AssetDatabase.LoadAssetAtPath<Object>(it.path);
                    Selection.activeObject = o;
                    EditorGUIUtility.PingObject(o);
                }
                EditorGUILayout.LabelField(it.path, GUILayout.MinWidth(200));
                EditorGUILayout.LabelField(it.info);
            }
        }
        if (texProblems.Count > shown) EditorGUILayout.LabelField($"… a ďalších {texProblems.Count - shown} (skrátené)");
    }
}
