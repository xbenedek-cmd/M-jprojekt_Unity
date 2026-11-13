using UnityEngine;

/// <summary>
/// Jedlo v Divočine: chlieb a zelenina rozložené po zemi (zbiera sa klávesom F ako ostatné suroviny, po čase sa obnoví).
/// Vzniká raz, keď hráč prvýkrát vstúpi do Divočiny, ako potomok koreňa zóny (zóna ich vypína a zapína spolu s sebou).
/// Model sa berie z Resources/Items (Chlieb, zelenina), inak jednoduchý tvar z primitív.
/// </summary>
public class WildFood : MonoBehaviour
{
    private const string Zone = "Zem_Divocina";
    private const int BreadCount = 10, VegetableCount = 14;

    private PlayerStats player;
    private bool spawned;
    private float nextCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<WildFood>() != null) return;
        new GameObject("WildFood").AddComponent<WildFood>();
    }

    void Update()
    {
        if (spawned || PauseMenu.AtStartup || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 1f;

        if (player == null) { player = FindAnyObjectByType<PlayerStats>(); return; }
        if (!ZoneLoader.TryGetGround(Zone, out Bounds b)) return;

        Vector3 p = player.transform.position;
        if (Mathf.Abs(p.x - b.center.x) > b.extents.x || Mathf.Abs(p.z - b.center.z) > b.extents.z || p.y < -100f) return;

        spawned = true;
        GameObject root = ZoneLoader.GetRoot(Zone);
        Spawn(b, root != null ? root.transform : null, ItemCatalog.Bread, "Chlieb", "Items/Chlieb", BreadCount, 0.32f, 1, 2, new Color(0.85f, 0.62f, 0.3f));
        Spawn(b, root != null ? root.transform : null, ItemCatalog.Vegetables, "Zelenina", "Items/zelenina", VegetableCount, 0.38f, 1, 3, new Color(0.45f, 0.75f, 0.25f));
    }

    void Spawn(Bounds area, Transform parent, string itemId, string label, string modelPath, int count, float size, int min, int max, Color fallbackColor)
    {
        GameObject model = Resources.Load<GameObject>(modelPath);

        for (int i = 0; i < count; i++)
        {
            Vector3 at = new Vector3(
                Random.Range(area.min.x + 25f, area.max.x - 25f), area.max.y + 300f,
                Random.Range(area.min.z + 25f, area.max.z - 25f));
            if (!Physics.Raycast(at, Vector3.down, out RaycastHit hit, 700f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (hit.point.y < area.min.y - 5f) continue;

            var go = new GameObject("Jedlo_" + label + "_" + i);
            go.transform.SetParent(parent, true);
            go.transform.position = hit.point;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            if (model != null) AttachModel(go.transform, model, size);
            else AttachPrimitive(go.transform, size, fallbackColor);

            var node = go.AddComponent<ResourceNode>();
            node.Configure(itemId, min, max);
        }
    }

    // Model sa zmenší na pevnú veľkosť (zdrojové súbory majú ľubovoľné mierky) a postaví na zem
    static void AttachModel(Transform parent, GameObject model, float size)
    {
        GameObject instance = Instantiate(model, parent);
        LightMeshTable.Apply(instance);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        foreach (Collider c in instance.GetComponentsInChildren<Collider>()) Destroy(c);
        ModelCleanup.StripCamerasAndLights(instance);

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        float largest = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (largest < 0.0001f) return;
        float k = size / largest;
        instance.transform.localScale = Vector3.one * k;
        instance.transform.localPosition = new Vector3(-(b.center.x - parent.position.x) * k, -(b.min.y - parent.position.y) * k, -(b.center.z - parent.position.z) * k);
        parent.gameObject.AddComponent<NearActivator>().Init(instance, 90f);   // model má milióny trojuholníkov, vykresľuje sa len v blízkosti
    }

    static void AttachPrimitive(Transform parent, float size, Color color)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localScale = new Vector3(size * 0.6f, size * 0.45f, size * 0.6f);
        g.transform.localPosition = Vector3.up * size * 0.3f;
        g.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        g.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = color };
    }
}
