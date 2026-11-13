using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Vchod do bane v zóne Bane: dva múry z modelov ruín po stranách tmavého otvoru, kamenný preklad, drevený rám a vnútorné schody.
/// Vstup (trigger) vedie do tunelov, ktoré sa za behu vytvoria hlboko pod zónou (Mine.cs).
/// </summary>
public static partial class ProgenyTools
{
    [MenuItem("Progeny/Bane/Pridať vchod Hlbinná Baňa", false, 201)]
    static void AddMineEntranceMenu()
    {
        GameObject root = GameObject.Find("Divocina_Bane");
        GameObject gate = GameObject.Find("Portal_zBane");
        if (root == null || gate == null)
        {
            Debug.LogWarning("Najprv vytvor zónu Bane (Progeny → Bane → Vytvoriť zónu Bane).");
            return;
        }

        WildTheme theme = null;
        foreach (WildTheme t in WildThemes) if (t.key == "Bane") theme = t;
        if (theme == null) return;

        BuildMineEntrance(root.transform, gate.transform.position, MakeWildMats(theme), new System.Random(77));
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("Vchod do bane je pridaný. Ulož scénu.");
    }

    static void BuildMineEntrance(Transform parent, Vector3 gatePos, WildMats m, System.Random rnd)
    {
        Transform old = parent.Find("Vchod_do_bane");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // Vchod stojí pred portálom, otvorom (miestna +z) smerom k nemu
        Vector3 pos = gatePos + new Vector3(24f, 0f, 20f);
        if (Physics.Raycast(pos + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 200f)) pos = hit.point;

        var entrance = new GameObject("Vchod_do_bane");
        entrance.transform.SetParent(parent);
        entrance.transform.position = pos;
        Vector3 toGate = gatePos - pos;
        toGate.y = 0f;
        entrance.transform.rotation = Quaternion.LookRotation(toGate.sqrMagnitude > 0.01f ? toGate : Vector3.forward);

        GameObject[] ruinModels = FindModels("brickruin");
        const float openWidth = 4.2f, openHeight = 3.8f;

        // Dva ruinové múry po stranách: model sa zmenší/zväčší na výšku ~5 m, postaví na zem a otočí čelom k otvoru
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 anchor = new Vector3(side * (openWidth * 0.5f + 0.4f), 0f, 0f);
            if (ruinModels != null && ruinModels.Length > 0)
            {
                GameObject piece = Object.Instantiate(ruinModels[rnd.Next(ruinModels.Length)], entrance.transform);
                piece.name = side < 0 ? "RuinaL" : "RuinaP";
                piece.transform.localRotation = Quaternion.identity;
                piece.transform.localPosition = Vector3.zero;

                Bounds b = WorldBounds(piece);
                float scale = Mathf.Clamp(5f / Mathf.Max(0.1f, b.size.y), 0.3f, 4f);
                piece.transform.localScale = Vector3.one * scale;

                b = WorldBounds(piece);
                Vector3 local = entrance.transform.InverseTransformPoint(b.center);
                float bottom = entrance.transform.InverseTransformPoint(new Vector3(b.center.x, b.min.y, b.center.z)).y;
                // vonkajšia hrana múru sa zarovná k otvoru
                float halfWidth = Mathf.Max(b.extents.x, b.extents.z);
                piece.transform.localPosition = new Vector3(side * (openWidth * 0.5f + 0.4f + halfWidth) - local.x, -bottom, -local.z);

                ApplyRuinMaterial(piece, m);
                AddColliderFromBounds(piece);
            }
            else
            {
                GameObject wall = new GameObject(side < 0 ? "RuinaL" : "RuinaP");
                wall.transform.SetParent(entrance.transform, false);
                wall.transform.localPosition = anchor + new Vector3(side * 1.6f, 0f, 0f);
                Part(PrimitiveType.Cube, wall.transform, Vector3.up * 2.5f, new Vector3(3.2f, 5f, 1.6f), m.rock, true);
            }
        }

        // Kamenný preklad nad otvorom a tmavé pozadie otvoru
        GameObject lintel = new GameObject("Preklad");
        lintel.transform.SetParent(entrance.transform, false);
        Part(PrimitiveType.Cube, lintel.transform, new Vector3(0f, openHeight + 0.6f, 0f), new Vector3(openWidth + 2.6f, 1.2f, 2.4f), m.rock, true);

        Material black = Mat(new Color(0.01f, 0.01f, 0.012f));
        GameObject hole = new GameObject("Ustie");
        hole.transform.SetParent(entrance.transform, false);
        Part(PrimitiveType.Cube, hole.transform, new Vector3(0f, openHeight * 0.5f, -2.4f), new Vector3(openWidth + 0.6f, openHeight + 0.4f, 0.5f), black, true);
        Part(PrimitiveType.Cube, hole.transform, new Vector3(0f, openHeight + 0.1f, -1.2f), new Vector3(openWidth, 0.3f, 2.4f), black, false);

        // Drevený rám a schody dole
        Material wood = Mat(new Color(0.22f, 0.14f, 0.08f));
        GameObject frame = new GameObject("Ram");
        frame.transform.SetParent(entrance.transform, false);
        Part(PrimitiveType.Cube, frame.transform, new Vector3(-openWidth * 0.5f, openHeight * 0.5f, 0.4f), new Vector3(0.35f, openHeight, 0.35f), wood, false);
        Part(PrimitiveType.Cube, frame.transform, new Vector3(openWidth * 0.5f, openHeight * 0.5f, 0.4f), new Vector3(0.35f, openHeight, 0.35f), wood, false);
        Part(PrimitiveType.Cube, frame.transform, new Vector3(0f, openHeight, 0.4f), new Vector3(openWidth + 0.4f, 0.35f, 0.35f), wood, false);

        GameObject stairs = new GameObject("Schody");
        stairs.transform.SetParent(entrance.transform, false);
        for (int i = 0; i < 4; i++)
            Part(PrimitiveType.Cube, stairs.transform, new Vector3(0f, -0.15f * i - 0.075f, 1.2f - i * 0.55f), new Vector3(openWidth - 0.4f, 0.15f, 0.55f), m.rock, false);

        // Lampa pred vchodom
        GameObject lamp = new GameObject("Lampa");
        lamp.transform.SetParent(entrance.transform, false);
        lamp.transform.localPosition = new Vector3(0f, openHeight - 0.4f, 1.2f);
        Light light = lamp.AddComponent<Light>();
        light.color = new Color(1f, 0.7f, 0.4f);
        light.range = 12f;
        light.intensity = 1.4f;

        // Vstupný trigger (pred čiernou stenou)
        var trigger = entrance.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 1.6f, -1.4f);
        trigger.size = new Vector3(openWidth - 0.6f, 3.2f, 1.8f);
        entrance.AddComponent<MineEntrance>();
        FinishMineEntrance(entrance.transform, m);   // skala, koľajnice, vozík, debny, cedula, lampy

        // Hráčovi vzdialený smerník
        Debug.Log($"Vchod do bane postavený na {pos}.");
    }

    static Bounds WorldBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }
}
