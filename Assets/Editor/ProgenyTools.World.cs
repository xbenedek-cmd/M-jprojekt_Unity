using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Svet: zóny a portály (plán sveta: odboj + civilisti ↔ Megakorporácia, okolo Divočina).</summary>
public static partial class ProgenyTools
{
    const float PortalZoneDistance = 330f;
    static readonly Vector3 CorpOffset = new Vector3(1240f, 0f, 630f);   // východ mapy od brány v meste (svetové osi), mimo divočín, kde už ostane

    [MenuItem("Progeny/Mesto/Portály: odboj ↔ Megakorporácia", false, 300)]
    static void CreateCityPortals()
    {
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        if (player == null) { Debug.LogError("Nenašiel sa hráč!"); return; }

        GameObject old = GameObject.Find("CityPortals");
        if (old != null) Undo.DestroyObjectImmediate(old);

        GameObject root = new GameObject("CityPortals");
        Undo.RegisterCreatedObjectUndo(root, "Create City Portals");

        // Civilná strana pred hráčom, korporácia ďaleko vedľa nej (zatiaľ jedna scéna; neskôr samostatná zóna)
        Vector3 civil = player.transform.position + player.transform.forward * 12f;
        Vector3 corp = civil + CorpOffset;

        // Zem pod Megakorporáciou: druhá polovica mesta je zatiaľ prázdne miesto, bez podlahy by hráč spadol do prázdna
        float groundY = civil.y;
        if (Physics.Raycast(civil + Vector3.up * 50f, Vector3.down, out RaycastHit civilHit, 100f, ~0, QueryTriggerInteraction.Ignore))
            groundY = civilHit.point.y;
        corp.y = groundY;
        BuildZoneGround(root.transform, "Zem_Megakorporacia", corp, new Vector2(150f, 150f), new Color(0.18f, 0.2f, 0.24f));

        PortalGate toCorp = BuildPortal(root.transform, "Portal_DoMegakorporacie", civil, new Color(0.3f, 0.7f, 1f), Quaternion.LookRotation(-player.transform.forward));
        PortalGate toCivil = BuildPortal(root.transform, "Portal_DoMesta", corp, new Color(1f, 0.55f, 0.2f), Quaternion.LookRotation(Vector3.left));

        toCorp.Link(toCivil, "Megakorporácia");
        toCivil.Link(toCorp, WorldNames.StartCity);
        EditorUtility.SetDirty(toCorp);
        EditorUtility.SetDirty(toCivil);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;
        Debug.Log("Portály a zem Megakorporácie (150×150 m) vytvorené. Druhý portál je 200 m od prvého. Ulož scénu.");
    }

    [MenuItem("Progeny/Megakorporácia/Starý komplex (zastaraný)", false, 329)]
    static void BuildCorpComplex()
    {
        GameObject ground = GameObject.Find("Zem_Megakorporacia");
        if (ground == null)
        {
            CreateCityPortals();
            ground = GameObject.Find("Zem_Megakorporacia");
            if (ground == null) return;
        }

        Vector3 c = ground.transform.position + Vector3.up * 0.5f;   // stred zeme, vrch plochy

        GameObject old = GameObject.Find("CorpComplex");
        if (old != null) Undo.DestroyObjectImmediate(old);
        GameObject root = new GameObject("CorpComplex");
        Undo.RegisterCreatedObjectUndo(root, "Build Corp Complex");
        root.transform.position = c;

        Color steel = new Color(0.22f, 0.24f, 0.28f);
        Color glass = new Color(0.2f, 0.36f, 0.48f);
        Color neon = new Color(0.2f, 0.9f, 1f);
        const float r = 60f;

        // Obvod: žiadny východ, dovnútra sa chodí portálom; zároveň hráč nespadne zo zeme
        Block(root.transform, "Obvod_S", c, new Vector3(0f, 0f, r), new Vector3(2 * r, 5f, 2f), steel);
        Block(root.transform, "Obvod_J", c, new Vector3(0f, 0f, -r), new Vector3(2 * r, 5f, 2f), steel);
        Block(root.transform, "Obvod_V", c, new Vector3(r, 0f, 0f), new Vector3(2f, 5f, 2 * r), steel);
        Block(root.transform, "Obvod_Z", c, new Vector3(-r, 0f, 0f), new Vector3(2f, 5f, 2 * r), steel);

        // Obranné veže v rohoch s neónovou korunou
        int n = 0;
        foreach (float sx in new[] { -r, r })
            foreach (float sz in new[] { -r, r })
            {
                n++;
                Block(root.transform, $"Veza_{n}", c, new Vector3(sx, 0f, sz), new Vector3(4f, 12f, 4f), steel);
                Block(root.transform, $"VezaNeon_{n}", c, new Vector3(sx, 12f, sz), new Vector3(4.4f, 0.5f, 4.4f), neon, true);
            }

        // Hlavná sklenená veža komplexu a dve kasárne
        Block(root.transform, "HlavnaVeza", c, new Vector3(35f, 0f, 0f), new Vector3(16f, 30f, 16f), glass);
        Block(root.transform, "HlavnaVezaNeon", c, new Vector3(35f, 22f, 0f), new Vector3(16.4f, 0.6f, 16.4f), neon, true);
        Block(root.transform, "Kasarne_1", c, new Vector3(0f, 0f, 35f), new Vector3(24f, 6f, 12f), steel);
        Block(root.transform, "Kasarne_2", c, new Vector3(0f, 0f, -35f), new Vector3(24f, 6f, 12f), steel);

        // Nízke prekážky: kryt pre zakrádanie
        Block(root.transform, "Kryt_1", c, new Vector3(-25f, 0f, 12f), new Vector3(6f, 2.5f, 2f), steel);
        Block(root.transform, "Kryt_2", c, new Vector3(-20f, 0f, -14f), new Vector3(2f, 2.5f, 7f), steel);
        Block(root.transform, "Kryt_3", c, new Vector3(15f, 0f, 18f), new Vector3(7f, 2.5f, 2f), steel);

        // Vojenská zóna na celý komplex (vojaci strieľajú na démonov, hliadkujú vnútri)
        GameObject zoneGo = new GameObject("MilitaryZone_Megakorporacia");
        zoneGo.transform.SetParent(root.transform);
        zoneGo.transform.position = c;
        MilitaryZone zone = zoneGo.AddComponent<MilitaryZone>();
        BoxCollider zoneBox = zone.GetComponent<BoxCollider>();
        zoneBox.size = new Vector3(2 * r - 6f, 8f, 2 * r - 6f);
        zoneBox.center = new Vector3(0f, 4f, 0f);

        Physics.SyncTransforms();   // nové kolízie musia byť v fyzike, kým vojakov položíme na zem
        Vector3[] posts =
        {
            new Vector3(-12f, 0f, 8f), new Vector3(10f, 0f, -10f), new Vector3(25f, 0f, 20f),
            new Vector3(-30f, 0f, -25f), new Vector3(20f, 0f, -28f), new Vector3(-8f, 0f, 28f)
        };
        for (int i = 0; i < posts.Length; i++)
            SpawnSoldier(root.transform, $"Soldier_{i + 1}", c + posts[i]);

        EnsurePlayerComponents(Object.FindAnyObjectByType<PlayerStats>());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;
        Debug.Log("Komplex Megakorporácie je postavený (obvod, 4 veže, hlavná veža, kasárne, kryty, vojenská zóna, 6 vojakov). Ulož scénu.");
    }

    // ---------- Divočina ----------


    static Material Mat(Color color, bool emissive = false)
    {
        Material m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Glossiness", 0.1f);
        if (emissive)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * 1.8f);
            // Bez tohto príznaku Unity pri uložení scény emisiu zahodí (materiál potom nesvieti)
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return m;
    }

    static Vector3 Around(System.Random rnd, Vector3 center, float radius)
    {
        double angle = rnd.NextDouble() * Mathf.PI * 2.0;
        double dist = Mathf.Sqrt((float)rnd.NextDouble()) * radius;
        return center + new Vector3((float)(System.Math.Cos(angle) * dist), 0f, (float)(System.Math.Sin(angle) * dist));
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 center, Vector3 scale, Material mat, bool collider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = center;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static void BuildTree(Transform parent, Vector3 ground, Vector3 pos, Material bark, Material leaves, System.Random rnd)
    {
        float h = 3.5f + (float)rnd.NextDouble() * 2.5f;
        GameObject tree = new GameObject("Strom");
        tree.transform.SetParent(parent);
        tree.transform.position = new Vector3(pos.x, ground.y, pos.z);
        Prim(PrimitiveType.Cylinder, "Kmen", tree.transform, tree.transform.position + Vector3.up * h * 0.5f, new Vector3(0.7f, h * 0.5f, 0.7f), bark, true);
        Prim(PrimitiveType.Sphere, "Koruna", tree.transform, tree.transform.position + Vector3.up * (h + 1.2f), new Vector3(4f, 3.4f, 4f), leaves, false);
    }

    static void BuildProp(Transform parent, Vector3 ground, Vector3 pos, PrimitiveType type, Vector3 size, Material mat, System.Random rnd)
    {
        GameObject prop = Prim(type, "Prekazka", parent, new Vector3(pos.x, ground.y + size.y * 0.5f, pos.z), size, mat, true);
        prop.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
    }

    static void BuildNode(Transform parent, Vector3 ground, Vector3 pos, string itemId, PrimitiveType type, Vector3 size, Material mat,
        int min, int max, bool collider, float tilt)
    {
        GameObject node = Prim(type, "Zdroj_" + itemId, parent, new Vector3(pos.x, ground.y + size.y * 0.5f, pos.z), size, mat, collider);
        node.transform.rotation = Quaternion.Euler(tilt, pos.x * 13f % 360f, 0f);
        node.AddComponent<ResourceNode>().Configure(itemId, min, max);
    }

    // Kváder stojaci na zemi (vrch zeme c.y), pozícia podľa stredu základne
    static void Block(Transform parent, string name, Vector3 origin, Vector3 offset, Vector3 size, Color color, bool emissive = false)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent);
        block.transform.position = origin + offset + Vector3.up * (size.y * 0.5f);
        block.transform.localScale = size;
        block.isStatic = true;

        Material mat = new Material(Shader.Find("Standard")) { color = color };
        mat.SetFloat("_Glossiness", emissive ? 0.2f : 0.35f);
        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
            Object.DestroyImmediate(block.GetComponent<BoxCollider>());   // neón je len ozdoba
        }
        block.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // Rovná plocha s kolíziou; vrch je na úrovni y daného bodu
    static void BuildZoneGround(Transform parent, string name, Vector3 center, Vector2 size, Color color)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = name;
        ground.transform.SetParent(parent);
        ground.transform.position = center + Vector3.down * 0.5f;
        ground.transform.localScale = new Vector3(size.x, 1f, size.y);
        ground.isStatic = true;

        Material mat = new Material(Shader.Find("Standard")) { color = color };
        mat.SetFloat("_Glossiness", 0.15f);
        ground.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static PortalGate BuildPortal(Transform parent, string name, Vector3 position, Color color, Quaternion rotation)
    {
        if (Physics.Raycast(position + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            position.y = hit.point.y;

        GameObject gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        gate.name = name;
        gate.transform.SetParent(parent);
        gate.transform.SetPositionAndRotation(position + Vector3.up * 1.5f, rotation);
        gate.transform.localScale = new Vector3(2.4f, 3f, 0.4f);

        // Svietiaci obdĺžnik portálu; prechod sa deje vstupom do spúšťača
        BoxCollider box = gate.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1f, 1f, 4f);

        Material mat = new Material(Shader.Find("Standard")) { color = new Color(color.r, color.g, color.b, 0.6f) };
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 1.35f);   // o 10 % slabšie než pôvodných 1,5
        gate.GetComponent<Renderer>().sharedMaterial = mat;

        Rigidbody rb = gate.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        PortalGate portal = gate.AddComponent<PortalGate>();
        DecoratePortal(portal, new Color(color.r, color.g, color.b, 1f));   // kamenný oblúk s runami namiesto holej dosky
        return portal;
    }
}
