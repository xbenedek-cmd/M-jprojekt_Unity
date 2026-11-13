using System.Text;
using UnityEngine;

/// <summary>
/// Diagnostika kameňov (kláves F8): vypíše, čo je okolo hráča – kolízie do 4 m, ťažiteľné objekty do 8 m (najbližší: vzdialenosť, nástroj,
/// počet kolízií, či je zničený), nástroje hráča a výšku terénu pod najbližším kameňom. Výpis ide do Console aj na obrazovku.
/// </summary>
public class RockDebug : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<RockDebug>() != null) return;
        new GameObject("RockDebug").AddComponent<RockDebug>();
    }

    void Update()
    {
        if (!GameInput.GetKeyDown(KeyCode.F8)) return;

        PlayerStats player = FindAnyObjectByType<PlayerStats>();
        if (player == null) return;
        Vector3 p = player.transform.position;
        var sb = new StringBuilder();

        sb.AppendLine($"[F8] Hráč {p.x:0.0}, {p.y:0.0}, {p.z:0.0}. Krompáč: {(ToolKit.Has(ToolKit.Kind.Pickaxe) ? ToolKit.Name(ToolKit.Kind.Pickaxe) : "NEMÁŠ")}.");

        Collider[] colliders = Physics.OverlapSphere(p, 4f, ~0, QueryTriggerInteraction.Collide);
        sb.AppendLine($"Kolízie do 4 m: {colliders.Length}");
        for (int i = 0; i < colliders.Length && i < 10; i++)
        {
            Collider c = colliders[i];
            sb.AppendLine($"  {c.GetType().Name} '{c.name}' (rodič '{(c.transform.parent != null ? c.transform.parent.name : "-")}') povolená={c.enabled} trigger={c.isTrigger} vrstva={c.gameObject.layer}");
        }

        BreakableNode best = null;
        float bestDistance = 8f;
        int inRange = 0;
        foreach (BreakableNode n in BreakableNode.All)
        {
            if (n == null) continue;
            float d = n.DistanceTo(p);
            if (d > 8f) continue;
            inRange++;
            if (d < bestDistance) { bestDistance = d; best = n; }
        }
        sb.AppendLine($"Ťažiteľné objekty do 8 m: {inRange}");
        if (best != null)
        {
            Vector3 np = best.transform.position;
            string terrainInfo = "bez terénu";
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 local = np - terrain.transform.position;
                if (local.x < 0f || local.z < 0f || local.x > terrain.terrainData.size.x || local.z > terrain.terrainData.size.z) continue;
                terrainInfo = $"terén tu {terrain.SampleHeight(np) + terrain.transform.position.y:0.00}";
                break;
            }
            sb.AppendLine($"Najbližší: '{best.name}' ({best.Label}, vyžaduje {best.Requires}), vzdialenosť {bestDistance:0.0} m, kolízií {best.ColliderCount}, zničený={best.IsDown}, pozícia y={np.y:0.00}, {terrainInfo}");
        }


        // Čo sa v skutočnosti zobrazuje pri hráčovi: vykresľovače do 1,5 m (aj bez kolízie), s cestou v hierarchii a stavom kolízie / ťažiteľnosti
        sb.AppendLine("Vykresľované objekty do 1,5 m od hráča:");
        int shown = 0;
        foreach (Renderer r in FindObjectsByType<Renderer>())
        {
            if (r == null || !r.enabled || r.bounds.SqrDistance(p) > 1.5f * 1.5f) continue;
            if (r.GetComponentInParent<PlayerStats>() != null || r.GetComponentInParent<DemonEnemy>() != null) continue;


            string path = r.name;
            for (Transform up = r.transform.parent; up != null; up = up.parent) path = up.name + "/" + path;
            bool anyCollider = r.GetComponentInParent<Collider>() != null || r.GetComponentInChildren<Collider>() != null;
            BreakableNode owner = r.GetComponentInParent<BreakableNode>();
            sb.AppendLine($"  {path}  kolízia={(anyCollider ? "áno" : "NIE")}  ťažiteľné={(owner != null ? "áno" : "NIE")}  stred={r.bounds.center.x:0.0},{r.bounds.center.y:0.0},{r.bounds.center.z:0.0}");
            if (++shown >= 8) break;
        }
        if (shown == 0) sb.AppendLine("  (nič)");

        // Najbližšia ťažiteľná skala kdekoľvek (ak je ďaleko, kamene pri hráčovi nie sú registrované)
        BreakableNode global = null;
        float globalDistance = float.MaxValue;
        foreach (BreakableNode n in BreakableNode.All)
        {
            if (n == null || n.Requires != ToolKit.Kind.Pickaxe) continue;
            float d = (n.transform.position - p).magnitude;
            if (d < globalDistance) { globalDistance = d; global = n; }
        }
        if (global != null) sb.AppendLine($"Najbližšia skala (krompáč) kdekoľvek: '{global.name}' {globalDistance:0.0} m, pozícia {global.transform.position.x:0.0},{global.transform.position.y:0.0},{global.transform.position.z:0.0}");
        Debug.Log(sb.ToString());
        GameHUD.Message(best != null
            ? $"F8: najbližšia skala '{best.name}' {bestDistance:0.0} m, kolízií {best.ColliderCount}, krompáč {(ToolKit.Has(ToolKit.Kind.Pickaxe) ? "áno" : "NIE")}; kolízie do 4 m: {colliders.Length}"
            : $"F8: žiadny ťažiteľný objekt do 8 m; kolízie do 4 m: {colliders.Length}; krompáč {(ToolKit.Has(ToolKit.Kind.Pickaxe) ? "áno" : "NIE")}", 8f);
    }
}
