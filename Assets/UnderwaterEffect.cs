using UnityEngine;

/// <summary>
/// Kamera pod hladinou jazera: obraz sa jemne vlní, zafarbí do modrozelena (hmla, tmavšie okraje) a zvuk sa tlmí (dolnopriepustný filter).
/// Detekcia: kamera je vodorovne v jazere (WildLake), pod jeho hladinou a nad dnom. Efekt sa plynule zapína a vypína.
/// Pridáva sa samo na hlavnú kameru pri štarte scény.
/// </summary>
[RequireComponent(typeof(Camera))]
public class UnderwaterEffect : MonoBehaviour
{
    [SerializeField] private Color tint = new Color(0.35f, 0.65f, 0.7f);
    [SerializeField] private float fadeSpeed = 4f;
    [SerializeField] private float muffledCutoff = 900f;

    /// <summary>Tlmenie zvuku z iného dôvodu než voda (kŕmenie), 0..1.</summary>
    public static float ExtraMuffle;

    private Material material;
    private AudioLowPassFilter lowPass;
    private WildLake[] lakes = new WildLake[0];
    private float nextLakeRefresh;
    private float intensity;
    private Camera cam;
    private UnderwaterRender render;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        Camera main = Camera.main;
        if (main == null || main.GetComponent<UnderwaterEffect>() != null) return;
        main.gameObject.AddComponent<UnderwaterEffect>();
    }

    void Awake()
    {
        cam = GetComponent<Camera>();
        Shader shader = Resources.Load<Shader>("Underwater");
        if (shader != null && shader.isSupported)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            render = gameObject.AddComponent<UnderwaterRender>();
            render.Setup(material, tint);
            render.enabled = false;
        }

        if (GetComponent<AudioListener>() != null)
        {
            lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            lowPass.enabled = false;
        }
    }

    void OnDestroy()
    {
        if (material != null) Destroy(material);
    }

    void Update()
    {
        if (Time.unscaledTime >= nextLakeRefresh)
        {
            nextLakeRefresh = Time.unscaledTime + 5f;
            lakes = FindObjectsByType<WildLake>();
        }

        intensity = Mathf.MoveTowards(intensity, IsUnderwater(cam.transform.position) ? 1f : 0f, fadeSpeed * Time.unscaledDeltaTime);

        // Vykresľovanie efektu beží len pod vodou (inak by každý snímok platil zbytočný Blit)
        if (render != null)
        {
            render.enabled = intensity > 0.01f;
            render.Intensity = intensity;
        }

        if (lowPass != null)
        {
            float muffle = Mathf.Max(intensity, ExtraMuffle * 0.85f);
            lowPass.enabled = muffle > 0.05f;
            lowPass.cutoffFrequency = Mathf.Lerp(22000f, muffledCutoff, muffle);
        }
    }

    bool IsUnderwater(Vector3 p)
    {
        foreach (WildLake lake in lakes)
        {
            if (lake == null || !lake.Covers(p, 0f)) continue;

            float surface = lake.transform.position.y;
            if (p.y >= surface) continue;

            // Pod hladinou a nad dnom; pri brehu je terén vyššie ako hladina, tam kamera pod vodou nie je
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 local = p - terrain.transform.position;
                if (local.x < 0f || local.z < 0f || local.x > terrain.terrainData.size.x || local.z > terrain.terrainData.size.z) continue;

                float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
                if (ground < surface && p.y > ground + 0.02f) return true;
                break;
            }
        }
        return false;
    }
}

/// <summary>Obrazovkový prechod efektu pod vodou; zapína ho UnderwaterEffect len keď je kamera pod hladinou.</summary>
public class UnderwaterRender : MonoBehaviour
{
    private Material material;
    private Color tint;

    public float Intensity { get; set; }

    public void Setup(Material m, Color color)
    {
        material = m;
        tint = color;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (material == null)
        {
            Graphics.Blit(source, destination);
            return;
        }

        material.SetColor("_Tint", tint);
        material.SetFloat("_Intensity", Intensity);
        Graphics.Blit(source, destination, material);
    }
}
