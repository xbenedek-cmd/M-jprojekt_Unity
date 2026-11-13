using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimapa a mapa: ortografická kamera zhora, ktorá sleduje hráča a kreslí do RenderTexture.
/// Vytvorí ju GameHUD automaticky, nič nemusíš pridávať do scény.
/// Sever je vždy hore. Značky (hráč, démoni, lekárka...) kreslí GameHUD.
/// </summary>
public class MiniMap : MonoBehaviour
{
    public static MiniMap Instance { get; private set; }

    [Header("Minimapa (v rohu obrazovky)")]
    [Tooltip("Polovica výšky záberu v metroch")]
    public float miniSize = 30f;
    public int miniResolution = 256;

    [Header("Mapa (v menu)")]
    public float mapSize = 90f;
    public int mapResolution = 512;

    [Header("Kamera")]
    public float cameraHeight = 120f;
    public Color background = new Color(0.03f, 0.04f, 0.06f);

    /// <summary>GameHUD zapne, keď je otvorená karta Mapa (väčší záber a väčšia textúra).</summary>
    public bool MapMode { get; set; }
    /// <summary>Priblíženie mapy: menej = bližšie.</summary>
    public float MapZoom { get; set; } = 1f;

    public Texture Texture => MapMode ? (activeSnap != null ? activeSnap : mapRT) : miniRT;
    /// <summary>Výrez textúry; pri snímku nenačítanej zóny sa podľa posunu a priblíženia vyberá jeho časť.</summary>
    public Rect TexCoords { get; private set; } = new Rect(0f, 0f, 1f, 1f);

    // Snímky zón, ktoré nie sú načítané: zóna sa na okamih zapne, nasníma a hneď vypne
    private readonly Dictionary<string, Texture> snapshots = new Dictionary<string, Texture>();
    private readonly List<GameObject> unloadedBuffer = new List<GameObject>();
    private Texture activeSnap;
    private Vector3 snapCenter;
    private float snapHalf = 1f;
    public float CurrentSize => MapMode ? (HasTerritory ? viewSize : AroundSize) : miniSize;

    // "Okolie hráča" je zamknuté na územie, v ktorom hráč stojí: pohľad ani priblíženie z neho nevyjdú (žiadne prázdno okolo)
    private bool hasAroundLock;
    private Bounds aroundBounds;
    private float AroundSize => hasAroundLock
        ? Mathf.Min(mapSize * MapZoom, Mathf.Max(5f, Mathf.Min(aroundBounds.extents.x, aroundBounds.extents.z)))
        : mapSize * MapZoom;
    public Vector3 Center { get; private set; }

    // ---------- Územia (mapa jednej zóny bez prázdna okolo) ----------
    public struct MapTerritory
    {
        public string name;
        public string ground;   // meno objektu zeme (kľúč pre uložený obrázok mapy)
        public Bounds bounds;
    }

    /// <summary>Územia, ktoré sa dajú zvoliť v mape (mesto, Megakorporácia, divočiny). Obnovuje RefreshTerritories.</summary>
    public readonly List<MapTerritory> Territories = new List<MapTerritory>();
    /// <summary>Zvolené územie (-1 = mapa okolo hráča). Kamera sa vtedy pohybuje len v hraniciach územia, takže prázdno nie je vidieť.</summary>
    public int SelectedTerritory { get; private set; } = -1;
    public bool HasTerritory => SelectedTerritory >= 0 && SelectedTerritory < Territories.Count;

    private Vector3 pan;
    private float viewSize = 30f;

    static readonly (string ground, string name)[] WildZones =
    {
        ("Zem_Divocina", "Divočina"), ("Zem_Kanon", "Bleskový kaňon"), ("Zem_Les", "Temný les"),
        ("Zem_Pustatina", "Zrezavená pustatina"), ("Zem_Mociare", "Močiare"), ("Zem_Bane", "Bane (povrch)")
    };

    public void RefreshTerritories()
    {
        Territories.Clear();

        // Mesto odboja: zóna obyvateľov, ak je; inak štvorec okolo portálu do Megakorporácie
        GameObject cityZone = GameObject.Find("Obyvatelia_MestoOdboja");
        BoxCollider cityBox = cityZone != null ? cityZone.GetComponent<BoxCollider>() : null;
        GameObject gate = GameObject.Find("Portal_DoMegakorporacie");
        if (cityBox != null) Territories.Add(new MapTerritory { name = WorldNames.StartCity, bounds = cityBox.bounds });
        else if (gate != null) Territories.Add(new MapTerritory { name = WorldNames.StartCity, bounds = new Bounds(gate.transform.position, new Vector3(180f, 40f, 180f)) });

        AddGround("Zem_Megakorporacia", "Megakorporácia");
        AddGround("Zem_Centrum", "Centrum mesta");
        foreach (var zone in WildZones) AddGround(zone.ground, zone.name);
    }

    void AddGround(string objectName, string territoryName)
    {
        if (ZoneLoader.TryGetGround(objectName, out Bounds cached)) { Territories.Add(new MapTerritory { name = territoryName, ground = objectName, bounds = cached }); return; }
        GameObject ground = GameObject.Find(objectName);
        Renderer r = ground != null ? ground.GetComponent<Renderer>() : null;
        if (r != null) Territories.Add(new MapTerritory { name = territoryName, ground = objectName, bounds = ZoneLoader.GroundBounds(r) });
    }

    /// <summary>Index územia, v ktorom je bod (vodorovne), alebo -1.</summary>
    public int TerritoryAt(Vector3 position)
    {
        for (int i = 0; i < Territories.Count; i++)
        {
            Bounds b = Territories[i].bounds;
            if (Mathf.Abs(position.x - b.center.x) <= b.extents.x && Mathf.Abs(position.z - b.center.z) <= b.extents.z) return i;
        }
        return -1;
    }

    /// <summary>Zvolí územie (-1 = okolie hráča); pohľad sa vycentruje na jeho stred.</summary>
    public void SelectTerritory(int index)
    {
        SelectedTerritory = index >= 0 && index < Territories.Count ? index : -1;
        activeSnap = null;
        freeOffset = Vector3.zero;
        TexCoords = new Rect(0f, 0f, 1f, 1f);
        if (!HasTerritory) return;

        pan = Territories[SelectedTerritory].bounds.center;
        pendingLoad = true;   // obrázok sa načíta v LateUpdate (kamera sa nesmie renderovať z OnGUI, odkiaľ sa výber volá)
        infoAt = -10f;
    }

    private bool pendingLoad;
    private Vector3 freeOffset;   // posun mapy okolo hráča

    void LoadTerritoryPicture()
    {
        pendingLoad = false;
        if (!HasTerritory) return;

        MapTerritory t = Territories[SelectedTerritory];
        Vector3 playerPos = target != null ? target.position : Vector3.zero;
        bool unloaded = ZoneLoader.GetUnloadedRoots(t.bounds.center, unloadedBuffer);
        // Ladiaci výpis pre hlásenie čiernej mapy (skopíruj z Console)
        Debug.Log($"[MapDebug] územie='{t.name}' ground='{t.ground}' bounds center={t.bounds.center} size={t.bounds.size} | hráč={playerPos} | " +
                  $"zóna vypnutá={unloaded} (vypnuté koreňe: {unloadedBuffer.Count}) | obrázok v Resources/Maps/{t.ground}={(Resources.Load<Texture2D>("Maps/" + t.ground) != null)} | " +
                  $"cam.cullingMask={cam.cullingMask} cameraHeight={cameraHeight}");
        if (!unloaded) return;   // zóna je načítaná: živý záber

        // 1) obrázok zóny uložený z editora (Progeny → Mesto → Mapy zón), 2) záložne jednorazový snímok za behu
        Texture picture = Resources.Load<Texture2D>("Maps/" + t.ground);
        if (picture == null && (!snapshots.TryGetValue(t.name, out picture) || picture == null))
        {
            picture = Capture(t, unloadedBuffer);
            snapshots[t.name] = picture;
            Debug.Log($"Mapa: pre zónu {t.name} nie je uložený obrázok (Resources/Maps/{t.ground}), urobil sa jednorazový snímok.");
        }
        activeSnap = picture;
        snapCenter = t.bounds.center;
        snapHalf = Mathf.Max(5f, t.bounds.extents.x, t.bounds.extents.z);
        Debug.Log($"[MapDebug] zobrazuje sa obrázok '{(picture != null ? picture.name : "null")}' snapHalf={snapHalf}");
    }

    RenderTexture Capture(MapTerritory t, List<GameObject> roots)
    {
        float half = Mathf.Max(5f, t.bounds.extents.x, t.bounds.extents.z);
        var rt = new RenderTexture(1024, 1024, 16, RenderTextureFormat.ARGB32) { name = "MapSnap_" + t.name };

        RenderTexture prevRT = cam.targetTexture;
        float prevSize = cam.orthographicSize, prevFar = cam.farClipPlane;
        bool wasEnabled = cam.enabled;

        foreach (GameObject root in roots) root.SetActive(true);
        cam.enabled = true;
        cam.targetTexture = rt;
        cam.orthographicSize = half;
        cam.farClipPlane = 1400f;
        cam.transform.position = new Vector3(t.bounds.center.x, t.bounds.center.y + 600f, t.bounds.center.z);
        cam.Render();
        foreach (GameObject root in roots) root.SetActive(false);   // Start/Update zóny sa nestihli spustiť

        cam.targetTexture = prevRT;
        cam.orthographicSize = prevSize;
        cam.farClipPlane = prevFar;
        cam.enabled = wasEnabled;
        return rt;
    }

    // Zapamätaný počet obyvateľov územia (aj keď zóna nie je načítaná): ľudia zo ZonePopulation, démoni z DemonStreamer
    private string[] infoCache = new string[0];
    private float infoAt = -10f;

    public string[] PopulationLines
    {
        get
        {
            if (!HasTerritory) return infoCache;
            if (Time.unscaledTime - infoAt < 2f) return infoCache;
            infoAt = Time.unscaledTime;

            MapTerritory t = Territories[SelectedTerritory];
            int civilians = 0, rebels = 0, soldiers = 0;
            bool known = false;
            foreach (ZonePopulation z in FindObjectsByType<ZonePopulation>(FindObjectsInactive.Include))
            {
                if (z == null || z.ZoneName != t.name) continue;
                known = true;
                civilians += z.Stock(PopulationGroup.Civilians);
                rebels += z.Stock(PopulationGroup.Rebels);
                soldiers += z.Stock(PopulationGroup.Soldiers);
            }
            DemonStreamer.CountIn(t.bounds, out int demonsAlive, out int demonsTotal);

            string text = known ? $"Obyvatelia: civilisti {civilians}, odboj {rebels}, vojaci {soldiers}" : "Obyvatelia: bez zaznamenaných ľudí";
            if (demonsTotal > 0) text += $"{(char)10}Démoni: {demonsAlive} živých (z {demonsTotal})";
            infoCache = text.Split((char)10);   // len raz za 2 s, nie každý snímok
            return infoCache;
        }
    }

    /// <summary>Priblíženie (MapZoom), pri ktorom sa celé územie práve zmestí do záberu.</summary>
    public float FitZoom(int index)
    {
        if (index < 0 || index >= Territories.Count) return 1f;
        Bounds b = Territories[index].bounds;
        return Mathf.Min(b.extents.x, b.extents.z) / Mathf.Max(1f, mapSize);
    }

    /// <summary>Posun pohľadu o svetové metre (x = východ, y = sever); kamera nikdy nevyjde z územia.</summary>
    public void Pan(Vector2 worldDelta)
    {
        if (!HasTerritory)
        {
            // okolie hráča: voľný posun (max 600 m od hráča)
            freeOffset += new Vector3(worldDelta.x, 0f, worldDelta.y);
            freeOffset = Vector3.ClampMagnitude(freeOffset, 600f);
            return;
        }
        pan.x += worldDelta.x;
        pan.z += worldDelta.y;
    }

    private Camera cam;
    private RenderTexture miniRT, mapRT;
    private Transform target;

    public static MiniMap Create()
    {
        if (Instance != null) return Instance;
        return new GameObject("MiniMap").AddComponent<MiniMap>();
    }

    void Awake()
    {
        Instance = this;

        miniRT = new RenderTexture(miniResolution, miniResolution, 16, RenderTextureFormat.ARGB32) { name = "MiniMapRT" };
        mapRT = new RenderTexture(mapResolution, mapResolution, 16, RenderTextureFormat.ARGB32) { name = "MapRT" };

        GameObject go = new GameObject("MiniMapCamera");
        go.transform.SetParent(transform, false);

        cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = miniSize;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = background;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = cameraHeight * 2f;
        cam.depth = -50f;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.useOcclusionCulling = false;
        cam.targetTexture = miniRT;
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (miniRT != null) miniRT.Release();
        if (mapRT != null) mapRT.Release();
        foreach (RenderTexture s in snapshots.Values) if (s != null) s.Release();
    }

    void LateUpdate()
    {
        if (target == null)
        {
            PlayerStats p = FindAnyObjectByType<PlayerStats>();
            if (p != null) target = p.transform;
            return;
        }

        if (pendingLoad) LoadTerritoryPicture();
        if (!MapMode) freeOffset = Vector3.zero;

        if (MapMode && HasTerritory)
        {
            // Pohľad ostáva celý v hraniciach územia (priblíženie aj posun sú obmedzené), takže prázdno okolo nie je vidieť
            Bounds b = Territories[SelectedTerritory].bounds;
            viewSize = Mathf.Clamp(mapSize * MapZoom, 5f, Mathf.Max(5f, Mathf.Min(b.extents.x, b.extents.z)));
            pan.x = Mathf.Clamp(pan.x, b.min.x + viewSize, Mathf.Max(b.min.x + viewSize, b.max.x - viewSize));
            pan.z = Mathf.Clamp(pan.z, b.min.z + viewSize, Mathf.Max(b.min.z + viewSize, b.max.z - viewSize));
            Center = new Vector3(pan.x, b.center.y, pan.z);
        }
        else
        {
            hasAroundLock = false;
            if (MapMode)
            {
                int at = TerritoryAt(target.position);
                if (at >= 0) { hasAroundLock = true; aroundBounds = Territories[at].bounds; }
            }

            Vector3 c = target.position + (MapMode ? freeOffset : Vector3.zero);

            if (hasAroundLock)
            {
                Bounds b = aroundBounds;
                float size = AroundSize;
                c.x = Mathf.Clamp(c.x, b.min.x + size, Mathf.Max(b.min.x + size, b.max.x - size));
                c.z = Mathf.Clamp(c.z, b.min.z + size, Mathf.Max(b.min.z + size, b.max.z - size));
                freeOffset = new Vector3(c.x - target.position.x, 0f, c.z - target.position.z);   // posun sa nehromadí za hranicou
            }
            Center = c;
        }

        bool showSnap = MapMode && HasTerritory && activeSnap != null;
        cam.enabled = !showSnap;   // pri snímku nenačítanej zóny živý záber netreba
        if (showSnap)
        {
            float w = viewSize / snapHalf;
            TexCoords = new Rect((Center.x - snapCenter.x) / (2f * snapHalf) + 0.5f - w * 0.5f,
                                 (Center.z - snapCenter.z) / (2f * snapHalf) + 0.5f - w * 0.5f, w, w);
            return;
        }
        TexCoords = new Rect(0f, 0f, 1f, 1f);

        cam.orthographicSize = Mathf.Max(5f, CurrentSize);
        cam.targetTexture = MapMode ? mapRT : miniRT;
        cam.transform.position = Center + Vector3.up * cameraHeight;
    }

    /// <summary>Svetová pozícia -> bod v obdĺžniku r (v súradniciach GUI).</summary>
    public Vector2 WorldToMap(Vector3 world, Rect r)
    {
        float s = Mathf.Max(0.01f, CurrentSize);
        float nx = 0.5f + (world.x - Center.x) / (2f * s);
        float ny = 0.5f - (world.z - Center.z) / (2f * s);
        return new Vector2(r.x + nx * r.width, r.y + ny * r.height);
    }
}