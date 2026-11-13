using UnityEngine;

/// <summary>
/// Predmet na zemi (zahodený z inventára, nájdený vo svete alebo korisť). Zdvihne sa po priblížení;
/// ak sa nezmestí do inventára, ostane ležať. Vzhľad sa postaví sám podľa typu a farby predmetu.
/// </summary>
public class ItemPickup : MonoBehaviour
{
    public string itemId = ItemCatalog.HealthPotion;
    public int count = 1;
    [Tooltip("Vodorovná vzdialenosť, z ktorej hráč predmet zdvihne")]
    public float pickupRange = 1.6f;

    private static PlayerStats player;
    private static Inventory inventory;
    private static float nextPlayerSearch;

    private Transform visual;
    private float ignoreUntil;
    private float nextFullMessage;
    private string label;

    /// <summary>Vytvorí predmet na zemi pri danej pozícii. Prvých 1,5 s sa nedá zdvihnúť (aby sa nevrátil hneď po zahodení).</summary>
    public static ItemPickup Spawn(string id, int count, Vector3 position)
    {
        ItemDef def = ItemCatalog.Get(id);
        if (def == null) return null;

        if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore))
            position.y = hit.point.y;

        GameObject go = new GameObject("Pickup_" + def.displayName);
        go.transform.position = position;

        ItemPickup pickup = go.AddComponent<ItemPickup>();
        pickup.itemId = id;
        pickup.count = Mathf.Max(1, count);
        pickup.ignoreUntil = Time.time + 1.5f;
        return pickup;
    }

    void Start()
    {
        ItemDef def = ItemCatalog.Get(itemId);
        if (def == null)
        {
            Debug.LogWarning($"ItemPickup: neznámy predmet '{itemId}'.", this);
            enabled = false;
            return;
        }

        label = count > 1 ? $"{def.displayName} x{count}" : def.displayName;
        BuildVisual(def);
    }

    void BuildVisual(ItemDef def)
    {
        PrimitiveType shape = def.type == ItemType.Weapon || def.type == ItemType.Tool ? PrimitiveType.Cube
                            : def.type == ItemType.Potion ? PrimitiveType.Sphere
                            : PrimitiveType.Cube;

        GameObject v = GameObject.CreatePrimitive(shape);
        Destroy(v.GetComponent<Collider>());
        v.name = "PickupVisual";
        v.transform.SetParent(transform, false);
        v.transform.localScale = def.type == ItemType.Weapon || def.type == ItemType.Tool ? new Vector3(0.09f, 0.09f, 0.9f)
                               : def.type == ItemType.Potion ? new Vector3(0.28f, 0.28f, 0.28f)
                               : new Vector3(0.26f, 0.26f, 0.26f);

        Renderer r = v.GetComponent<Renderer>();
        Material m = new Material(Shader.Find("Standard")) { color = def.color };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", def.color * 0.6f);
        r.material = m;

        visual = v.transform;
        visual.localPosition = Vector3.up * 0.5f;   // meč (dlhá os Z) leží vodorovne
    }

    void Update()
    {
        if (visual != null)
        {
            Vector3 p = visual.localPosition;
            p.y = 0.5f + 0.08f * Mathf.Sin(Time.time * 2f);
            visual.localPosition = p;
            visual.Rotate(0f, 70f * Time.deltaTime, 0f, Space.World);
        }

        if (Time.time < ignoreUntil) return;

        if (player == null || inventory == null)
        {
            if (Time.unscaledTime < nextPlayerSearch) return;
            nextPlayerSearch = Time.unscaledTime + 1f;

            player = FindAnyObjectByType<PlayerStats>();
            inventory = player != null ? player.GetComponent<Inventory>() : null;
            return;
        }
        if (player.IsDead) return;

        Vector3 to = player.transform.position - transform.position;
        float vertical = Mathf.Abs(to.y);
        to.y = 0f;
        if (to.sqrMagnitude > pickupRange * pickupRange || vertical > 2.5f) return;

        ItemDef def = ItemCatalog.Get(itemId);
        if (def != null && def.IsTool)
        {
            // Nástroj: lepší nahradí slabší, rovnaký alebo horší sa len nechá ležať
            if (ToolKit.Has(def.toolKind) && ToolKit.Tier(def.toolKind) >= def.toolTier)
            {
                if (Time.time >= nextFullMessage) { nextFullMessage = Time.time + 2f; GameHUD.Message("Už máš rovnaký alebo lepší nástroj: " + label, 2f); }
                return;
            }
            ToolKit.Give(def.toolKind, def.toolTier);
            GameHUD.Message("Zobrané: " + label, 2f);
            Destroy(gameObject);
            return;
        }

        if (inventory.TryAdd(itemId, count))
        {
            GameHUD.Message("Zobrané: " + label, 2f);
            Destroy(gameObject);
        }
        else if (Time.time >= nextFullMessage)
        {
            nextFullMessage = Time.time + 2f;
            GameHUD.Message("Inventár je plný: " + label, 2f);
        }
    }
}
