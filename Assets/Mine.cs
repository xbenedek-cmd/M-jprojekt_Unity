using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vchod do bane (postavený z ruín v zóne Bane). Baňa je rozvetvený systém tunelov hlboko pod zónou, vytvorí sa za behu pri prvom vstupe.
/// V tuneloch sa ťaží zlato a čierny kryštál (palivo pre generátor). Ložiská po niekoľkých dňoch dorastú.
/// </summary>
public class MineEntrance : MonoBehaviour
{
    public static readonly List<MineEntrance> All = new List<MineEntrance>();

    public Vector3 ReturnPosition => transform.position + transform.forward * 7f;
    void Start()
    {
        // Vchod je portál „Hlbinná Baňa“: svietiaca plocha v otvore a čitateľný nápis (dopĺňa sa za behu, netreba prerábať scénu)
        if (transform.Find("PortalSvetlo") == null)
        {
            var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            glow.name = "PortalSvetlo";
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.8f, -1.3f);
            glow.transform.localScale = new Vector3(3.4f, 3.5f, 0.08f);
            var mat = new Material(Shader.Find("Standard")) { color = new Color(0.45f, 0.2f, 0.8f, 1f) };
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.55f, 0.25f, 1f) * 2.2f);
            glow.GetComponent<Renderer>().sharedMaterial = mat;

            var light = glow.AddComponent<Light>();
            light.color = new Color(0.6f, 0.35f, 1f);
            light.range = 12f;
            light.intensity = 1.6f;
        }
        PortalNetwork.MakeLabel(transform.position + Vector3.up * 6.2f, transform.parent, "Napis_HlbinnaBana", "Hlbinná Baňa");
    }


    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    void OnTriggerEnter(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body == null) return;
        PlayerMovement movement = body.GetComponent<PlayerMovement>();
        PlayerStats stats = body.GetComponent<PlayerStats>();
        if (movement == null || stats == null || stats.IsDead) return;
        MineSystem.Enter(movement, this);
    }
}

/// <summary>Východ z bane: svietiaca plocha pri začiatku tunela.</summary>
public class MineExit : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body == null) return;
        PlayerMovement movement = body.GetComponent<PlayerMovement>();
        if (movement != null && body.GetComponent<PlayerStats>() != null) MineSystem.Exit(movement);
    }
}

public enum OreKind { Gold, BlackCrystal }

public static class MineSystem
{
    public const float DepthY = -400f;
    const int Layer = 31;           // vrstva baní: slnko do nej nesvieti
    const float Cell = 4f, Height = 3.6f;
    const int Width = 33, Depth = 44, Mid = 16;

    public static bool Inside { get; private set; }
    static MineEntrance current;
    static GameObject root;
    static Vector3 origin;
    static float lockUntil;
    static GameObject headLamp;
    static readonly List<(Light light, int mask)> sunMasks = new List<(Light, int)>();
    static float savedAmbient = -1f;
    static readonly List<Vector3> deadEnds = new List<Vector3>();
    static readonly List<GameObject> blocks = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        Inside = false;
        root = null;
        current = null;
        lockUntil = 0f;
        headLamp = null;
        sunMasks.Clear();
        savedAmbient = -1f;
        deadEnds.Clear();
        blocks.Clear();
    }

    public static Vector3 SpawnPoint => origin + new Vector3(0f, 1.2f, 5f);

    public static void Enter(PlayerMovement movement, MineEntrance entrance)
    {
        if (Time.time < lockUntil || Inside) return;
        lockUntil = Time.time + 2.5f;
        current = entrance;
        EnsureBuilt(entrance);

        movement.Teleport(SpawnPoint, Vector3.forward);
        SetInside(true, movement);
        GameHUD.Message("Hlbinná Baňa: rudu ťaž krompáčom (ľavé tlačidlo myši), treba aspoň železný krompáč. Hľadaj truhly v slepých chodbách. Portál na povrch je pri začiatku tunela.", 8f);

        // Démoni sa skrývajú v slepých tunelách
        int pulled = 0;
        foreach (DemonEnemy demon in DemonEnemy.All)
        {
            if (pulled >= 3 || deadEnds.Count == 0) break;
            if (demon == null || demon.tier != DemonTier.Lesser || demon.Damage == null || demon.Damage.IsDead || demon.GetComponent<SurgeGuest>() != null) continue;
            demon.gameObject.AddComponent<SurgeGuest>().Begin(deadEnds[Random.Range(0, deadEnds.Count)] + Vector3.up * 0.4f, 600f);
            pulled++;
        }
    }

    public static void Exit(PlayerMovement movement)
    {
        if (!Inside || Time.time < lockUntil) return;
        lockUntil = Time.time + 2.5f;

        Vector3 back = current != null ? current.ReturnPosition : movement.transform.position + Vector3.up * 450f;
        if (Physics.Raycast(back + Vector3.up * 40f, Vector3.down, out RaycastHit hit, 120f, ~(1 << Layer), QueryTriggerInteraction.Ignore)) back = hit.point;
        movement.Teleport(back + Vector3.up * 1.2f, current != null ? current.transform.forward : Vector3.forward);
        SetInside(false, movement);
    }

    /// <summary>Hráč uložený v bani sa po načítaní hry vráti do bane (ak vchod existuje).</summary>
    public static void RecoverIfStuckInMine(PlayerMovement movement)
    {
        if (Inside || movement.transform.position.y > DepthY + 100f || MineEntrance.All.Count == 0) return;
        current = MineEntrance.All[0];
        EnsureBuilt(current);
        movement.Teleport(SpawnPoint, Vector3.forward);
        SetInside(true, movement);
    }

    static void SetInside(bool value, PlayerMovement movement)
    {
        Inside = value;
        if (value)
        {
            foreach (Light l in Object.FindObjectsByType<Light>())
                if (l.type == LightType.Directional) { sunMasks.Add((l, l.cullingMask)); l.cullingMask &= ~(1 << Layer); }
            if (savedAmbient < 0f) savedAmbient = RenderSettings.ambientIntensity;
            RenderSettings.ambientIntensity = 0.25f;

            headLamp = new GameObject("Celovka");
            headLamp.transform.SetParent(movement.transform, false);
            headLamp.transform.localPosition = new Vector3(0f, 1.8f, 0.2f);
            var light = headLamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 13f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.85f, 0.65f);
        }
        else
        {
            foreach ((Light light, int mask) in sunMasks) if (light != null) light.cullingMask = mask;
            sunMasks.Clear();
            if (savedAmbient >= 0f) RenderSettings.ambientIntensity = savedAmbient;
            savedAmbient = -1f;
            if (headLamp != null) Object.Destroy(headLamp);
        }
    }

    // ---------- Stavba tunelov ----------
    static void EnsureBuilt(MineEntrance entrance)
    {
        origin = new Vector3(entrance.transform.position.x, DepthY, entrance.transform.position.z);
        if (root != null) return;
        Build();
    }

    private struct Spot
    {
        public Vector2Int cell;
        public bool deep;
    }

    static void Build()
    {
        root = new GameObject("Bana_Tunely");
        root.transform.position = origin;

        var rnd = new System.Random(1337);
        bool[,] open = new bool[Width, Depth];
        var trunk = new List<Vector2Int>();
        var spots = new List<Spot>();
        deadEnds.Clear();
        blocks.Clear();

        void Carve(int x, int z) { if (x > 0 && x < Width - 1 && z >= 0 && z < Depth - 1) open[x, z] = true; }
        void Room(int cx, int cz, int rx, int rz)
        {
            for (int dx = -rx; dx <= rx; dx++) for (int dz = -rz; dz <= rz; dz++) Carve(cx + dx, cz + dz);
        }

        // Odbočka: vodorovná chodba s driftom, z jej stredu môže odbočiť zvislá a z jej konca ďalšia; slepé konce často končia komorou
        void Branch(int bx, int bz, int dir, int length, int level)
        {
            int x = bx, z = bz;
            for (int step = 0; step < length; step++)
            {
                x += dir;
                if (rnd.Next(100) < 20) z = Mathf.Clamp(z + (rnd.Next(2) == 0 ? -1 : 1), 3, Depth - 8);
                Carve(x, z);

                if (level == 0 && step == length / 2 && rnd.Next(100) < 65)
                {
                    int sdir = rnd.Next(2) == 0 ? -1 : 1, sz = z, sub = 4 + rnd.Next(5);
                    for (int s = 0; s < sub; s++) { sz = Mathf.Clamp(sz + sdir, 3, Depth - 8); Carve(x, sz); }
                    if (rnd.Next(100) < 55) Room(x, sz, 1, 1);
                    spots.Add(new Spot { cell = new Vector2Int(x, sz), deep = sz > Depth * 0.55f });
                    if (rnd.Next(100) < 55) Branch(x, sz, rnd.Next(2) == 0 ? -1 : 1, 3 + rnd.Next(5), 1);
                }
            }
            if (rnd.Next(100) < 60) Room(x, z, 1, 1);
            spots.Add(new Spot { cell = new Vector2Int(x, z), deep = z > Depth * 0.55f });
        }

        // Vstupná sieň a hlavná štôlňa
        for (int x = Mid - 2; x <= Mid + 2; x++) for (int z = 0; z <= 1; z++) Carve(x, z);
        int tx = Mid;
        for (int z = 0; z < Depth - 2; z++)
        {
            if (z > 3 && z % 6 == 0) tx = Mathf.Clamp(tx + (rnd.Next(2) == 0 ? -1 : 1), Mid - 2, Mid + 2);
            Carve(tx, z);
            Carve(tx, z + 1);   // dvojitý krok, aby sa posun nerozpojil
            trunk.Add(new Vector2Int(tx, z));
        }
        // Záverečná komora
        Room(tx, Depth - 5, 2, 2);
        spots.Add(new Spot { cell = new Vector2Int(tx - 1, Depth - 4), deep = true });
        spots.Add(new Spot { cell = new Vector2Int(tx + 1, Depth - 6), deep = true });

        // Odbočky po celej dĺžke hlavnej štôlne
        foreach (Vector2Int t in trunk)
        {
            if (t.y < 4 || t.y % 4 != 2 || t.y > Depth - 8) continue;
            foreach (int dir in new[] { -1, 1 })
                if (rnd.Next(100) >= 20) Branch(t.x, t.y, dir, 5 + rnd.Next(8), 0);
        }

        Material[] walls = { Mat(new Color(0.16f, 0.15f, 0.15f)), Mat(new Color(0.2f, 0.18f, 0.17f)), Mat(new Color(0.12f, 0.12f, 0.13f)) };
        Material[] floors = { Mat(new Color(0.11f, 0.1f, 0.09f)), Mat(new Color(0.14f, 0.12f, 0.1f)), Mat(new Color(0.09f, 0.09f, 0.09f)) };
        Material wood = Mat(new Color(0.22f, 0.14f, 0.08f));
        Material woodDark = Mat(new Color(0.14f, 0.09f, 0.05f));
        Material metal = Mat(new Color(0.32f, 0.33f, 0.36f));
        Material lantern = Mat(new Color(1f, 0.65f, 0.25f), true);

        var trunkSet = new HashSet<Vector2Int>(trunk);
        int lightCount = 0;
        for (int x = 0; x < Width; x++)
            for (int z = 0; z < Depth; z++)
            {
                if (!open[x, z]) continue;
                Vector3 c = CellCenter(x, z);
                Material wallMat = walls[(x * 5 + z * 3) % walls.Length];

                Block(c + new Vector3(0f, -0.25f, 0f), new Vector3(Cell, 0.5f, Cell), floors[(x * 7 + z * 13) % floors.Length]);
                Block(c + new Vector3(0f, Height + 0.25f, 0f), new Vector3(Cell, 0.5f, Cell), wallMat);

                // Steny + nerovnosti (balvany vyčnievajúce zo steny)
                if (!IsOpen(open, x - 1, z)) { Block(c + new Vector3(-Cell * 0.5f - 0.2f, Height * 0.5f, 0f), new Vector3(0.4f, Height, Cell + 0.8f), wallMat); Chunks(c, -1, 0, wallMat, rnd); }
                if (!IsOpen(open, x + 1, z)) { Block(c + new Vector3(Cell * 0.5f + 0.2f, Height * 0.5f, 0f), new Vector3(0.4f, Height, Cell + 0.8f), wallMat); Chunks(c, 1, 0, wallMat, rnd); }
                if (!IsOpen(open, x, z - 1)) { Block(c + new Vector3(0f, Height * 0.5f, -Cell * 0.5f - 0.2f), new Vector3(Cell + 0.8f, Height, 0.4f), wallMat); Chunks(c, 0, -1, wallMat, rnd); }
                if (!IsOpen(open, x, z + 1)) { Block(c + new Vector3(0f, Height * 0.5f, Cell * 0.5f + 0.2f), new Vector3(Cell + 0.8f, Height, 0.4f), wallMat); Chunks(c, 0, 1, wallMat, rnd); }

                // Stalaktity a suť
                if (rnd.Next(100) < 25)
                    Block(c + new Vector3(Rand(rnd, 1.4f), Height - 0.45f, Rand(rnd, 1.4f)), new Vector3(0.28f, 0.95f, 0.28f), wallMat,
                          Quaternion.Euler(Rand(rnd, 10f), rnd.Next(90), Rand(rnd, 10f)), false);
                if (rnd.Next(100) < 30)
                    Block(c + new Vector3(Rand(rnd, 1.5f), 0.12f, Rand(rnd, 1.5f)), new Vector3(0.35f + (float)rnd.NextDouble() * 0.3f, 0.25f, 0.35f), floors[0],
                          Quaternion.Euler(0f, rnd.Next(360), 0f), false);

                // Koľajnice na hlavnej štôlni
                if (trunkSet.Contains(new Vector2Int(x, z)))
                {
                    Block(c + new Vector3(-0.55f, 0.06f, 0f), new Vector3(0.1f, 0.08f, Cell), metal, null, false);
                    Block(c + new Vector3(0.55f, 0.06f, 0f), new Vector3(0.1f, 0.08f, Cell), metal, null, false);
                    for (int i = 0; i < 4; i++)
                        Block(c + new Vector3(0f, 0.03f, -1.5f + i), new Vector3(1.6f, 0.06f, 0.22f), woodDark, null, false);
                }

                // Debny a sudy pri stene
                if (!IsOpen(open, x - 1, z) && !trunkSet.Contains(new Vector2Int(x, z)) && rnd.Next(100) < 10)
                {
                    if (rnd.Next(2) == 0) Block(c + new Vector3(-1.4f, 0.4f, Rand(rnd, 1.2f)), new Vector3(0.9f, 0.8f, 0.9f), wood, Quaternion.Euler(0f, rnd.Next(60), 0f));
                    else Block(c + new Vector3(-1.45f, 0.5f, Rand(rnd, 1.2f)), new Vector3(0.75f, 0.5f, 0.75f), woodDark, null, true, PrimitiveType.Cylinder);
                }

                // Drevené výstuhy a lampy
                if ((x + z) % 3 == 0 && lightCount < 70 && !IsOpen(open, x - 1, z) && !IsOpen(open, x + 1, z))
                {
                    Block(c + new Vector3(-1.7f, Height * 0.5f, 0f), new Vector3(0.25f, Height, 0.25f), wood);
                    Block(c + new Vector3(1.7f, Height * 0.5f, 0f), new Vector3(0.25f, Height, 0.25f), wood);
                    Block(c + new Vector3(0f, Height - 0.1f, 0f), new Vector3(3.8f, 0.25f, 0.25f), wood);
                    Block(c + new Vector3(0f, Height - 0.55f, 0f), new Vector3(0.22f, 0.3f, 0.22f), lantern, null, false);

                    var lamp = new GameObject("Lampa");
                    lamp.transform.SetParent(root.transform, false);
                    lamp.transform.position = c + new Vector3(0f, Height - 0.6f, 0f);
                    lamp.layer = Layer;
                    var l = lamp.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.range = 11f;
                    l.intensity = 1.3f;
                    l.color = new Color(1f, 0.7f, 0.4f);
                    lightCount++;
                }
            }

        // Východ
        Vector3 exitPos = CellCenter(Mid, 0);
        var exit = GameObject.CreatePrimitive(PrimitiveType.Cube);
        exit.name = "Vychod";
        exit.transform.SetParent(root.transform, false);
        exit.transform.position = exitPos + new Vector3(0f, 0.05f, -0.6f);
        exit.transform.localScale = new Vector3(3.4f, 0.1f, 2.4f);
        exit.layer = Layer;
        Renderer er = exit.GetComponent<Renderer>();
        er.material = Mat(new Color(0.2f, 0.8f, 0.35f), true);
        var box = exit.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1f, 25f, 1f);
        box.center = new Vector3(0f, 12f, 0f);
        exit.AddComponent<MineExit>();
        PortalNetwork.MakeLabel(exitPos + new Vector3(0f, 3.2f, -0.6f), root.transform, "Napis_PovrchBana", "Povrch");
        var exitLight = exit.AddComponent<Light>();
        exitLight.color = new Color(0.4f, 1f, 0.5f);
        exitLight.range = 9f;

        // Rudy a truhly: zlato všade, čierny kryštál len hlboko a vzácne; truhly v slepých chodbách a v poslednej komore
        var used = new HashSet<Vector2Int>();
        int chests = 0;
        foreach (Spot spot in spots)
        {
            if (!used.Add(spot.cell)) continue;
            Vector3 p = CellCenter(spot.cell.x, spot.cell.y);
            deadEnds.Add(p);

            SpawnOre(p + new Vector3(0.9f, 0f, 0.5f), OreKind.Gold, rnd);
            if (spot.deep && rnd.Next(100) < 55) SpawnOre(p + new Vector3(-0.9f, 0f, -0.5f), OreKind.BlackCrystal, rnd);
            else if (rnd.Next(100) < 60) SpawnOre(p + new Vector3(-0.9f, 0f, -0.5f), OreKind.Gold, rnd);

            if (chests < 14 && (spot.cell.y >= Depth - 8 || rnd.Next(100) < 45))
            {
                SpawnChest(p + new Vector3(Rand(rnd, 0.4f), 0f, 1.3f), rnd.Next(360));
                chests++;
            }
            if (rnd.Next(100) < 25) SpawnCart(p + new Vector3(-0.2f, 0f, -1.4f), rnd.Next(2) * 180f, metal, woodDark, rnd);
        }
        // Pozdĺž hlavnej štôlne vzácny čierny kryštál hlboko pri stene
        for (int i = 18; i < trunk.Count; i += 9)
            SpawnOre(CellCenter(trunk[i].x, trunk[i].y) + new Vector3(1.4f, 0f, 0f), OreKind.BlackCrystal, rnd);

        // Steny, podlahy a výstuhy sa zlúčia do niekoľkých batchov (menej draw callov)
        StaticBatchingUtility.Combine(blocks.ToArray(), root);
        blocks.Clear();
    }

    static float Rand(System.Random rnd, float range) => ((float)rnd.NextDouble() * 2f - 1f) * range;

    // Balvany vyčnievajúce zo steny (nerovný povrch)
    static void Chunks(Vector3 cellCenter, int dx, int dz, Material mat, System.Random rnd)
    {
        int count = rnd.Next(100) < 45 ? 1 + rnd.Next(2) : 0;
        for (int i = 0; i < count; i++)
        {
            float size = 0.55f + (float)rnd.NextDouble() * 0.6f;
            Vector3 along = dx != 0 ? new Vector3(0f, 0f, Rand(rnd, 1.5f)) : new Vector3(Rand(rnd, 1.5f), 0f, 0f);
            Vector3 pos = cellCenter + new Vector3(dx * (Cell * 0.5f - size * 0.2f), 0.2f + (float)rnd.NextDouble() * (Height - 0.6f), dz * (Cell * 0.5f - size * 0.2f)) + along;
            Block(pos, new Vector3(size, size * (0.8f + (float)rnd.NextDouble() * 0.6f), size), mat, Quaternion.Euler(rnd.Next(360), rnd.Next(360), rnd.Next(360)));
        }
    }

    static bool IsOpen(bool[,] open, int x, int z) => x >= 0 && x < Width && z >= 0 && z < Depth && open[x, z];
    static Vector3 CellCenter(int x, int z) => origin + new Vector3((x - Mid) * Cell, 0f, z * Cell);

    static Material Mat(Color color, bool glow = false)
    {
        var m = new Material(Shader.Find("Standard")) { color = color };
        if (glow)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color * 1.4f);
        }
        return m;
    }

    static GameObject Block(Vector3 worldPos, Vector3 size, Material mat, Quaternion? rotation = null, bool collider = true, PrimitiveType type = PrimitiveType.Cube)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(root.transform, false);
        go.transform.position = worldPos;
        if (rotation.HasValue) go.transform.rotation = rotation.Value;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.layer = Layer;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        blocks.Add(go);
        return go;
    }

    // Truhla s korisťou (LootCrate, kláves F): drevený korpus, veko, kovové pásy a zlatá západka
    static void SpawnChest(Vector3 position, float yaw)
    {
        var chest = new GameObject("Truhla");
        chest.transform.SetParent(root.transform, false);
        chest.transform.position = position;
        chest.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        chest.layer = Layer;

        Material wood = Mat(new Color(0.34f, 0.2f, 0.09f));
        Material lid = Mat(new Color(0.28f, 0.16f, 0.07f));
        Material band = Mat(new Color(0.3f, 0.3f, 0.33f));
        Material gold = Mat(new Color(1f, 0.8f, 0.2f), true);

        void Part(Vector3 local, Vector3 scale, Material m)
        {
            GameObject g = Child(chest, PrimitiveType.Cube, local, scale, m, Quaternion.identity);
            g.layer = Layer;
            Object.DestroyImmediate(g.GetComponent<Collider>());
        }
        Part(new Vector3(0f, 0.3f, 0f), new Vector3(1.1f, 0.6f, 0.7f), wood);
        Part(new Vector3(0f, 0.68f, 0f), new Vector3(1.14f, 0.22f, 0.74f), lid);
        Part(new Vector3(-0.35f, 0.4f, 0f), new Vector3(0.1f, 0.85f, 0.76f), band);
        Part(new Vector3(0.35f, 0.4f, 0f), new Vector3(0.1f, 0.85f, 0.76f), band);
        Part(new Vector3(0f, 0.55f, 0.38f), new Vector3(0.16f, 0.2f, 0.06f), gold);

        var bc = chest.AddComponent<BoxCollider>();
        bc.center = new Vector3(0f, 0.42f, 0f);
        bc.size = new Vector3(1.15f, 0.85f, 0.75f);

        var glow = new GameObject("Svetlo");
        glow.transform.SetParent(chest.transform, false);
        glow.transform.localPosition = new Vector3(0f, 1.1f, 0f);
        glow.layer = Layer;
        var light = glow.AddComponent<Light>();
        light.color = new Color(1f, 0.75f, 0.35f);
        light.range = 4f;
        light.intensity = 0.8f;

        chest.AddComponent<LootCrate>().MakeMineChest();
    }

    // Vozík s hromadou rudy (len výzdoba, pevný)
    static void SpawnCart(Vector3 position, float yaw, Material metal, Material wood, System.Random rnd)
    {
        var cart = new GameObject("Vozik");
        cart.transform.SetParent(root.transform, false);
        cart.transform.position = position;
        cart.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cart.layer = Layer;

        Child(cart, PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(1.3f, 0.7f, 2f), metal, Quaternion.identity).layer = Layer;
        Material ore = Mat(new Color(0.95f, 0.75f, 0.2f), true);
        for (int i = 0; i < 4; i++)
        {
            GameObject heap = Child(cart, PrimitiveType.Cube, new Vector3(Rand(rnd, 0.4f), 0.95f, Rand(rnd, 0.7f)), new Vector3(0.35f, 0.3f, 0.35f), ore, Quaternion.Euler(rnd.Next(360), rnd.Next(360), 0f));
            heap.layer = Layer;
            Object.DestroyImmediate(heap.GetComponent<Collider>());
        }
        foreach (Vector2 w in new[] { new Vector2(-0.7f, -0.7f), new Vector2(0.7f, -0.7f), new Vector2(-0.7f, 0.7f), new Vector2(0.7f, 0.7f) })
        {
            GameObject wheel = Child(cart, PrimitiveType.Cylinder, new Vector3(w.x, 0.22f, w.y), new Vector3(0.44f, 0.05f, 0.44f), wood, Quaternion.Euler(0f, 0f, 90f));
            wheel.layer = Layer;
            Object.DestroyImmediate(wheel.GetComponent<Collider>());
        }
    }

    // Ruda: ničí sa ľavým tlačidlom krompáčom ako kameň alebo drevo (BreakableNode), potrebuje aspoň železný krompáč
    static void SpawnOre(Vector3 position, OreKind kind, System.Random rnd)
    {
        var ore = new GameObject(kind == OreKind.Gold ? "Zlato" : "CiernyKrystal");
        ore.transform.SetParent(root.transform, false);
        ore.transform.position = position;
        ore.layer = Layer;

        Material rock = Mat(new Color(0.2f, 0.19f, 0.19f));
        Material veins = kind == OreKind.Gold ? Mat(new Color(1f, 0.78f, 0.15f), true) : Mat(new Color(0.35f, 0.1f, 0.55f), true);

        GameObject core = Child(ore, PrimitiveType.Cube, new Vector3(0f, 0.5f, 0f), new Vector3(1.1f, 1f, 1.1f), rock, Quaternion.Euler(0f, rnd.Next(360), 0f));
        core.layer = Layer;
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f + rnd.Next(40);
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            bool crystal = kind == OreKind.BlackCrystal;
            Vector3 size = crystal ? new Vector3(0.22f, 0.9f + (float)rnd.NextDouble() * 0.5f, 0.22f) : new Vector3(0.3f, 0.25f, 0.3f);
            Vector3 pos = crystal ? new Vector3(dir.x * 0.25f, 1.0f, dir.z * 0.25f) : new Vector3(dir.x * 0.5f, 0.35f + i * 0.12f, dir.z * 0.5f);
            Quaternion rot = crystal ? Quaternion.Euler((float)rnd.NextDouble() * 24f - 12f, a, (float)rnd.NextDouble() * 24f - 12f) : Quaternion.Euler(0f, a, 20f);
            GameObject v = Child(ore, PrimitiveType.Cube, pos, size, veins, rot);
            v.layer = Layer;
            Object.DestroyImmediate(v.GetComponent<Collider>());
        }

        var glow = new GameObject("Svetlo");
        glow.transform.SetParent(ore.transform, false);
        glow.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        glow.layer = Layer;
        var light = glow.AddComponent<Light>();
        light.color = kind == OreKind.Gold ? new Color(1f, 0.8f, 0.3f) : new Color(0.6f, 0.25f, 1f);
        light.range = kind == OreKind.Gold ? 4.5f : 7f;
        light.intensity = 1.1f;

        // Pevnosť: zlato ~5 úderov železným krompáčom, čierny kryštál ~8
        bool gold = kind == OreKind.Gold;
        float hp = (gold ? 90f : 140f) + (float)rnd.NextDouble() * 30f;
        ore.AddComponent<BreakableNode>().Configure(ToolKit.Kind.Pickaxe, gold ? ItemCatalog.Gold : ItemCatalog.BlackCrystal, gold ? "Zlatá žila" : "Čierny kryštál", hp, false, 1);
    }

    static GameObject Child(GameObject parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat, Quaternion rot)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }
}

/// <summary>Návrat hráča do bane, ak bol uložený pod zemou (ťažba sa robí krompáčom ako pri kameni – ToolKit / BreakableNode).</summary>
public class MineRunner : MonoBehaviour
{
    private PlayerStats player;
    private PlayerMovement movement;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<MineRunner>() != null) return;
        new GameObject("MineRunner").AddComponent<MineRunner>();
    }

    void Update()
    {
        if (PauseMenu.AtStartup) return;
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player == null) return;
            movement = player.GetComponent<PlayerMovement>();
        }
        if (player.IsDead || movement == null) return;

        MineSystem.RecoverIfStuckInMine(movement);
    }
}
