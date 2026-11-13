using System.Collections.Generic;
using UnityEngine;

public enum PatrolRole { Civilian, Rebel, Soldier, Guard }

/// <summary>Trasa postavy: zoznam bodov s časom státia. Cyklus, alebo tam a späť, ak sa slučka nedá uzavrieť.</summary>
public class PatrolRoute
{
    public readonly List<Vector3> Points = new List<Vector3>();
    private readonly List<Vector2> dwell = new List<Vector2>();
    private int index;
    private int step = 1;

    public bool PingPong;
    public float BuiltAt;

    public bool Valid => Points.Count >= 2;
    public Vector3 Current => Points[index];
    public Vector2 CurrentDwell => dwell[index];

    public void Add(Vector3 point, float dwellMin, float dwellMax)
    {
        Points.Add(point);
        dwell.Add(new Vector2(dwellMin, dwellMax));
    }

    public void Advance()
    {
        if (!PingPong) { index = (index + 1) % Points.Count; return; }

        if (index + step < 0 || index + step >= Points.Count) step = -step;
        index += step;
    }

    /// <summary>Začne od bodu s daným poradím.</summary>
    public void StartAt(int i)
    {
        if (Points.Count == 0) return;
        index = Mathf.Clamp(i, 0, Points.Count - 1);
        step = 1;
    }

    /// <summary>Začne od najbližšieho bodu, aby postava po zapnutí nešla cez pol mesta.</summary>
    public void StartNearest(Vector3 position)
    {
        float best = float.MaxValue;
        for (int i = 0; i < Points.Count; i++)
        {
            float d = (Points[i] - position).sqrMagnitude;
            if (d < best) { best = d; index = i; }
        }
    }
}

/// <summary>
/// Plánuje trasy obyvateľov mesta podľa roly a povolania (civilista: domov, trh, jedlo; lekár: ambulancia; inžinier: dielňa;
/// odboj: obranné stavby a brány; vojak/strážnik: obvod, kríž ulíc alebo hliadka brán). Body sa overujú fyzikou
/// (zem, žiadna prekážka) a spoje obchádzajú budovy cez roh v tvare L. Body ležia vždy v hraniciach zóny (mesta).
/// </summary>
public static class PatrolPlanner
{
    private class Poi
    {
        public readonly List<Vector3> market = new List<Vector3>();
        public readonly List<Vector3> medical = new List<Vector3>();
        public readonly List<Vector3> workshop = new List<Vector3>();
        public readonly List<Vector3> power = new List<Vector3>();
        public readonly List<Vector3> food = new List<Vector3>();
        public readonly List<Vector3> housing = new List<Vector3>();
        public readonly List<Vector3> defense = new List<Vector3>();
        public readonly List<Vector3> gates = new List<Vector3>();
        public float builtAt;
    }

    private const float PoiCacheSeconds = 60f;
    private static readonly Dictionary<ZonePopulation, Poi> cache = new Dictionary<ZonePopulation, Poi>();
    private static readonly Collider[] overlap = new Collider[24];
    private static readonly RaycastHit[] castHits = new RaycastHit[24];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() => cache.Clear();

    public static PatrolRole RoleOf(GameObject npc)
    {
        if (npc.GetComponent<SoldierAI>() != null) return PatrolRole.Soldier;
        if (npc.GetComponent<CityGuardNPC>() != null) return PatrolRole.Guard;
        NPCPerception p = npc.GetComponent<NPCPerception>();
        return p != null && p.faction == FactionId.Rebels ? PatrolRole.Rebel : PatrolRole.Civilian;
    }

    /// <summary>Trasa pre postavu; null, ak sa nedá zostaviť (postava potom chodí ako doteraz).</summary>
    public static PatrolRoute Build(Transform npc, ZonePopulation zone, Bounds? areaOverride, PatrolRole role)
    {
        if (role == PatrolRole.Soldier || role == PatrolRole.Guard)
        {
            PatrolRoute fixedRoute = BuildFixed(npc);
            if (fixedRoute != null) return fixedRoute;
        }
        if (zone == null) return null;

        Bounds area = areaOverride ?? zone.Area;
        area.Expand(new Vector3(-12f, 0f, -12f));   // 6 m od hranice, nech postavy nechodia po okraji
        Poi poi = GetPoi(zone);
        var rng = new System.Random(StableHash(npc.name));
        Vector3 home = npc.position;
        refY = home.y + 0.5f;
        Vector3 center = area.center;

        var targets = new List<Vector3>();
        float dMin = 3f, dMax = 8f;

        switch (role)
        {
            case PatrolRole.Civilian:
            {
                Profession prof = CivilianProfile.Pick(npc.name, false);
                switch (prof)
                {
                    case Profession.Cook: Pick(targets, poi.food, rng); Pick(targets, poi.market, rng); Pick(targets, poi.housing, rng); break;
                    case Profession.Doctor: Pick(targets, poi.medical, rng); Pick(targets, poi.housing, rng); Pick(targets, poi.market, rng); break;
                    case Profession.Engineer: Pick(targets, poi.workshop, rng); Pick(targets, poi.power, rng); Pick(targets, poi.housing, rng); break;
                    case Profession.Electrician: Pick(targets, poi.power, rng); Pick(targets, poi.workshop, rng); Pick(targets, poi.housing, rng); break;
                    default: Pick(targets, poi.housing, rng); Pick(targets, poi.market, rng); Pick(targets, poi.food, rng); break;
                }
                targets.Add(RandomIn(area, rng, 0.5f));   // prechádzka po meste
                dMin = 4f; dMax = 10f;
                break;
            }
            case PatrolRole.Rebel:
            {
                Profession prof = CivilianProfile.Pick(npc.name, true);
                if (prof == Profession.Engineer || prof == Profession.Electrician) { Pick(targets, poi.workshop, rng); Pick(targets, poi.power, rng); }
                else if (prof == Profession.Doctor) { Pick(targets, poi.medical, rng); Pick(targets, poi.housing, rng); }
                Pick(targets, poi.defense, rng);
                Pick(targets, poi.gates, rng);
                targets.Add(RandomIn(area, rng, 0.9f));
                dMin = 3f; dMax = 8f;
                break;
            }
            default:   // vojak, strážnik
            {
                int style = rng.Next(3);
                if (role == PatrolRole.Guard && style == 1) style = 2;
                if (style == 1 && poi.gates.Count == 0) style = 2;

                if (style == 0) Perimeter(targets, area, home, rng);
                else if (style == 1) { foreach (Vector3 g in poi.gates) targets.Add(Toward(g, center, 4f)); targets.Add(center); dMin = 5f; dMax = 10f; }
                else Cross(targets, area, rng);

                if (style != 1) { dMin = 1f; dMax = 3f; }
                break;
            }
        }

        return Assemble(home, targets, area, dMin, dMax, rng);
    }

    /// <summary>Pevná hliadková trasa po uliciach (z RoadGraph); null, ak zóna žiadnu nemá.</summary>
    public static PatrolRoute BuildFixed(Transform npc)
    {
        RoadGraph graph = RoadGraph.At(npc.position);
        if (graph == null) return null;
        RoadGraph.Patrol def = graph.FindPatrol(npc.name, StableHash(npc.name));
        if (def == null || def.points.Length < 2) return null;

        var route = new PatrolRoute { BuiltAt = Time.time, PingPong = def.pingPong };
        for (int i = 0; i < def.points.Length; i++)
        {
            Vector2 d = i < def.dwell.Length ? def.dwell[i] : new Vector2(0f, 0.1f);
            route.Add(def.points[i], d.x, d.y);
        }
        return route;
    }

    /// <summary>Postavu postaví na bod hliadkovej trasy (podľa mena, mimo dohľadu hráča, ak sa dá) a trasu začne od neho.</summary>
    public static void PlaceOnRoute(Transform npc, PatrolRoute route, Transform player)
    {
        if (route == null || !route.Valid) return;
        int count = route.Points.Count;
        int start = (StableHash(npc.name) & 0x7fffffff) % count;
        if (npc.name.EndsWith("_b")) start = (start + count / 2) % count;   // druhý vojak hliadky začína v inej polovici trasy
        if (player != null)
        {
            // od svojho bodu sa posúva po trase, kým nie je aspoň 20 m od hráča (nevyskakuje mu pred nosom)
            int stepSize = Mathf.Max(1, count / 5);
            for (int k = 0; k < 5; k++)
            {
                int i = (start + k * stepSize) % count;
                if ((route.Points[i] - player.position).sqrMagnitude > 20f * 20f) { start = i; break; }
            }
        }
        npc.position = route.Points[start];
        route.StartAt((start + 1) % count);
    }

    // ---------- Tvary tras ----------
    static void Perimeter(List<Vector3> t, Bounds a, Vector3 home, System.Random rng)
    {
        Vector3 mn = a.min, mx = a.max, c = a.center;
        var ring = new List<Vector3>
        {
            new Vector3(mn.x, 0f, mn.z), new Vector3(c.x, 0f, mn.z), new Vector3(mx.x, 0f, mn.z),
            new Vector3(mx.x, 0f, c.z), new Vector3(mx.x, 0f, mx.z), new Vector3(c.x, 0f, mx.z),
            new Vector3(mn.x, 0f, mx.z), new Vector3(mn.x, 0f, c.z),
        };

        int start = 0;
        float best = float.MaxValue;
        for (int i = 0; i < ring.Count; i++)
        {
            float d = (ring[i] - home).sqrMagnitude;
            if (d < best) { best = d; start = i; }
        }
        int dir = rng.Next(2) == 0 ? 1 : -1;
        for (int i = 0; i < ring.Count; i++) t.Add(ring[((start + dir * i) % ring.Count + ring.Count) % ring.Count]);
    }

    static void Cross(List<Vector3> t, Bounds a, System.Random rng)
    {
        Vector3 c = a.center, mn = a.min, mx = a.max;
        Vector3[] ends = { new Vector3(c.x, 0f, mn.z), new Vector3(mx.x, 0f, c.z), new Vector3(c.x, 0f, mx.z), new Vector3(mn.x, 0f, c.z) };
        int first = rng.Next(4);
        t.Add(ends[first]);
        t.Add(c);
        t.Add(ends[(first + 2) % 4]);
        t.Add(c);
        t.Add(ends[(first + 1 + 2 * rng.Next(2)) % 4]);
        t.Add(c);
    }

    // ---------- Skladanie trasy s overením ----------
    static PatrolRoute Assemble(Vector3 home, List<Vector3> targets, Bounds area, float dMin, float dMax, System.Random rng)
    {
        var route = new PatrolRoute { BuiltAt = Time.time };

        Vector3 prev = home;
        if (!Snap(ref prev, area, rng, false)) prev = home;   // domov je vždy platný bod (tam stojí)
        route.Add(prev, dMin, dMax);
        Vector3 first = prev;
        RoadGraph graph = RoadGraph.At(home);
        float lane = (float)(rng.NextDouble() * 2.0 - 1.0);   // vlastný pruh na ceste

        foreach (Vector3 raw in targets)
        {
            Vector3 p = raw;
            if (!Snap(ref p, area, rng, true)) continue;
            if ((p - prev).sqrMagnitude < 9f) continue;

            if (graph != null && StreetLeg(graph, prev, p, lane, route, dMin, dMax)) { prev = p; continue; }
            if (!Link(prev, p, out Vector3 via)) continue;
            if (via != Vector3.zero) route.Add(via, 0f, 0.1f);
            route.Add(p, dMin, dMax);
            prev = p;
        }

        if (route.Points.Count < 2)
        {
            // Zálohový okruh okolo domova
            for (int i = 0; i < 4; i++)
            {
                float ang = (i * 90f + rng.Next(30)) * Mathf.Deg2Rad;
                Vector3 p = home + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (6f + rng.Next(6));
                if (!Snap(ref p, area, rng, true) || !Link(prev, p, out Vector3 via)) continue;
                if (via != Vector3.zero) route.Add(via, 0f, 0.1f);
                route.Add(p, dMin, dMax);
                prev = p;
            }
        }
        if (!route.Valid) return null;

        // Slučka sa uzavrie len ak sa dá prejsť späť; inak tam a späť po tej istej trase
        route.PingPong = !Link(prev, first, out _);
        route.StartNearest(home);
        return route;
    }

    private static readonly List<Vector3> legBuffer = new List<Vector3>();

    /// <summary>Úsek A→B po uliciach (RoadGraph); medzibody dostanú pruh lane. false, keď sa po uliciach nedá alebo napojenie na ulicu nie je voľné.</summary>
    static bool StreetLeg(RoadGraph graph, Vector3 a, Vector3 b, float lane, PatrolRoute route, float dMin, float dMax)
    {
        legBuffer.Clear();
        if (!graph.TryPath(a, b, legBuffer, lane, 40f) || legBuffer.Count == 0) return false;
        if (!Clear(a, legBuffer[0]) || !Clear(legBuffer[legBuffer.Count - 1], b)) return false;
        for (int i = 0; i < legBuffer.Count; i++) route.Add(legBuffer[i], 0f, 0.1f);
        route.Add(b, dMin, dMax);
        return true;
    }

    // Spoj A→B: priamo, inak cez roh v tvare L (ulice sú rovnobežné s osami). via = Vector3.zero, keď netreba medzibod.
    static bool Link(Vector3 a, Vector3 b, out Vector3 via)
    {
        via = Vector3.zero;
        if (Clear(a, b)) return true;

        Vector3 c1 = new Vector3(b.x, a.y, a.z);
        if (GroundAt(ref c1) && Clear(a, c1) && Clear(c1, b)) { via = c1; return true; }

        Vector3 c2 = new Vector3(a.x, a.y, b.z);
        if (GroundAt(ref c2) && Clear(a, c2) && Clear(c2, b)) { via = c2; return true; }
        return false;
    }

    static bool Clear(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist < 0.5f) return true;

        int n = Physics.SphereCastNonAlloc(a + Vector3.up * 0.7f, 0.35f, d / dist, castHits, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (IsSolidObstacle(castHits[i].collider) && castHits[i].distance > 0.01f) return false;
        return true;
    }

    // Prekážka = pevný objekt sveta; postavy, zvieratá a terén (svah) sa nepočítajú
    static bool IsSolidObstacle(Collider c)
    {
        if (c == null || c.isTrigger || c is TerrainCollider) return false;
        if (c.attachedRigidbody != null) return false;
        if (c.GetComponentInParent<Damageable>() != null || c.GetComponentInParent<PlayerStats>() != null) return false;
        return true;
    }

    // Zem hľadáme od výšky postavy (nie z veľkej výšky), aby sa bod neocitol na streche
    static float refY;

    static bool GroundAt(ref Vector3 p)
    {
        Vector3 origin = new Vector3(p.x, refY + 3f, p.z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore)) return false;
        p.y = hit.point.y;
        return true;
    }

    // Bod vtlačí do oblasti, nájde zem a (ak treba) overí, že na ňom nestojí budova ani strom
    static bool Snap(ref Vector3 p, Bounds area, System.Random rng, bool checkFree)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector3 q = p;
            if (attempt > 0)
            {
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
                q += new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (2f + attempt * 1.5f);
            }
            q.x = Mathf.Clamp(q.x, area.min.x, area.max.x);
            q.z = Mathf.Clamp(q.z, area.min.z, area.max.z);

            if (!GroundAt(ref q)) continue;
            if (checkFree && !IsFree(q)) continue;
            p = q;
            return true;
        }
        return false;
    }

    static bool IsFree(Vector3 ground)
    {
        int n = Physics.OverlapCapsuleNonAlloc(ground + Vector3.up * 0.5f, ground + Vector3.up * 1.5f, 0.5f, overlap, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (IsSolidObstacle(overlap[i])) return false;
        return true;
    }

    // ---------- Body záujmu ----------
    static Poi GetPoi(ZonePopulation zone)
    {
        if (cache.TryGetValue(zone, out Poi poi) && Time.time - poi.builtAt < PoiCacheSeconds) return poi;

        poi = new Poi { builtAt = Time.time };
        Vector3 center = zone.Area.center;

        foreach (MerchantNPC m in Object.FindObjectsByType<MerchantNPC>())
            if (m != null && zone.Contains(m.transform.position)) poi.market.Add(m.transform.position);
        foreach (BuyerNPC b in Object.FindObjectsByType<BuyerNPC>())
            if (b != null && zone.Contains(b.transform.position)) poi.market.Add(b.transform.position);
        foreach (Healer h in Object.FindObjectsByType<Healer>())
            if (h != null && zone.Contains(h.transform.position)) poi.medical.Add(h.transform.position);
        foreach (PortalGate g in PortalGate.All)
            if (g != null && zone.Contains(g.transform.position)) poi.gates.Add(g.transform.position);

        foreach (BuildPlot plot in BuildPlot.All)
        {
            if (plot == null || plot.State != PlotState.Built || plot.Building == null || !zone.Contains(plot.transform.position)) continue;

            // Cieľ je pri budove (vstup smerom k stredu mesta), nie v nej
            Vector3 at = Toward(plot.transform.position, center, plot.Side * 0.5f + 1.5f);
            switch (plot.Building.id)
            {
                case BuildingCatalog.House: poi.housing.Add(at); break;
                case BuildingCatalog.CivilianClinic: poi.medical.Add(at); break;
                case BuildingCatalog.RebelWorkshop: poi.workshop.Add(at); break;
                case BuildingCatalog.PowerPlant:
                case BuildingCatalog.RebelSolar: poi.power.Add(at); break;
                case BuildingCatalog.CivilianShop:
                case BuildingCatalog.CivilianKitchen:
                case BuildingCatalog.Farm: poi.food.Add(at); poi.market.Add(at); break;
                case BuildingCatalog.RebelBarricade:
                case BuildingCatalog.CorpTower:
                case BuildingCatalog.CorpBarracks:
                case BuildingCatalog.CorpSecurity:
                case BuildingCatalog.CorpDepot: poi.defense.Add(at); break;
            }
        }

        cache[zone] = poi;
        return poi;
    }

    static void Pick(List<Vector3> into, List<Vector3> from, System.Random rng)
    {
        if (from.Count > 0) into.Add(from[rng.Next(from.Count)]);
    }

    static Vector3 Toward(Vector3 from, Vector3 to, float meters)
    {
        Vector3 d = to - from;
        d.y = 0f;
        return d.sqrMagnitude < 0.01f ? from : from + d.normalized * meters;
    }

    static Vector3 RandomIn(Bounds a, System.Random rng, float spread)
    {
        float x = Mathf.Lerp(a.center.x, rng.Next(2) == 0 ? a.min.x : a.max.x, (float)rng.NextDouble() * spread);
        float z = Mathf.Lerp(a.center.z, rng.Next(2) == 0 ? a.min.z : a.max.z, (float)rng.NextDouble() * spread);
        return new Vector3(x, a.center.y, z);
    }

    static int StableHash(string s)
    {
        int h = 17;
        foreach (char c in s) h = h * 31 + c;
        return h;
    }
}
