using UnityEngine;

/// <summary>
/// Blikajúce zvýraznenie postavy po skene dronom: žiara okolo tela je vidieť aj cez budovy a steny (UI/Default s ZTest Always),
/// pulzuje a po uplynutí času sama zmizne. Opätovné použitie na tej istej postave len predĺži čas.
/// </summary>
public class ScanHighlight : MonoBehaviour
{
    private const float BlinkHz = 2.4f;

    private GameObject halo;
    private Material material;
    private Color color;
    private float until;
    private float phase;

    /// <summary>Zvýrazní postavu na daný čas (s). Farba je základ žiary.</summary>
    public static void Apply(Transform target, Color color, float seconds)
    {
        if (target == null) return;
        ScanHighlight h = target.GetComponent<ScanHighlight>();
        if (h == null) h = target.gameObject.AddComponent<ScanHighlight>();
        h.color = color;
        h.until = Time.time + seconds;
        h.Build();
    }

    void Build()
    {
        if (halo != null) return;

        halo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(halo.GetComponent<Collider>());
        halo.name = "ScanHalo";
        halo.transform.SetParent(transform, false);

        // Veľkosť podľa kolidéra postavy
        Vector3 size = new Vector3(1.4f, 2.4f, 1.4f);
        Vector3 center = transform.position + Vector3.up;
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            size = col.bounds.size + new Vector3(0.5f, 0.4f, 0.5f);
            center = col.bounds.center;
        }
        Vector3 ls = transform.lossyScale;
        float sx = Mathf.Approximately(ls.x, 0f) ? 1f : ls.x, sy = Mathf.Approximately(ls.y, 0f) ? 1f : ls.y, sz = Mathf.Approximately(ls.z, 0f) ? 1f : ls.z;
        halo.transform.position = center;
        halo.transform.localScale = new Vector3(size.x / sx, size.y / (2f * sy), size.z / sz);

        Shader sh = Shader.Find("UI/Default");
        bool seeThrough = sh != null;
        if (sh == null) sh = Shader.Find("Sprites/Default");
        material = new Material(sh);
        if (seeThrough) material.SetInt("unity_GUIZTestMode", 8);   // vždy viditeľné, aj za budovou
        material.renderQueue = 3600;
        halo.GetComponent<Renderer>().material = material;
        phase = Random.value;
    }

    void Update()
    {
        if (halo == null) { Destroy(this); return; }
        float left = until - Time.time;
        if (left <= 0f) { Cleanup(); return; }

        // Ostré blikanie (zapnuté/vypnuté so zjemnením), v poslednej sekunde sa stráca
        float wave = Mathf.Sin((Time.time + phase) * BlinkHz * Mathf.PI * 2f);
        float blink = Mathf.SmoothStep(0.08f, 1f, Mathf.Clamp01(wave * 1.6f + 0.5f));
        float alpha = (0.18f + 0.5f * blink) * Mathf.Clamp01(left);
        material.color = new Color(color.r, color.g, color.b, alpha);
    }

    void OnDisable() => Cleanup();

    void Cleanup()
    {
        if (halo != null) Destroy(halo);
        if (material != null) Destroy(material);
        halo = null;
        material = null;
        Destroy(this);
    }
}
