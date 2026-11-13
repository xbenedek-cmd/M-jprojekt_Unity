using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Poistka pre kamene v divočinách: po štarte hry prejde všetky skaly, monolity a kamene z modelov a zaistí, že
///  - majú pevnú kolíziu tesne podľa modelu (nedá sa cez ne prejsť, ale ani sa k nim nedá priblížiť len na pár metrov),
///  - sú ťažiteľné krompáčom (BreakableNode), ak ich nejaký iný systém nespravuje,
///  - nie sú celé zapadnuté pod terénom.
/// Funguje nezávisle od toho, ako sa kamene vygenerovali v editore (opraví aj staré uložené scény a ručne posunuté zóny).
/// </summary>
public class RockGuard : MonoBehaviour
{
    // Kolízia je o niečo menšia než obdĺžnik okolo modelu (skaly sú oblé, rohy krabice by hráča zastavili priskoro)
    private const float HorizontalShrink = 0.86f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<RockGuard>() != null) return;
        new GameObject("RockGuard").AddComponent<RockGuard>();
    }

    IEnumerator Start()
    {
        yield return null;
        yield return new WaitForSeconds(1.5f);   // po načítaní uloženého sveta a usadení objektov
        Scan();
    }

    static bool LooksLikeRock(string n) =>
        n == "Skala" || n == "Monolit" || n.StartsWith("Military_Trenches_Scatter") || n.StartsWith("Mossy_Stone") || n.Contains("forest_stones");

    /// <summary>Spúšťa sa po štarte hry a z editora (Aktualizovať všetko), aby sa opravy uložili do scény.</summary>
    public static void Scan()
    {
        int rocks = 0, colliderAdded = 0, madeBreakable = 0, lifted = 0, realigned = 0;
        var done = new HashSet<GameObject>();

        foreach (Transform t in FindObjectsByType<Transform>())
        {
            if (t == null || !LooksLikeRock(t.name)) continue;
            if (t.GetComponentInChildren<Renderer>() == null && t.GetComponentInParent<BreakableNode>() == null) continue;

            // Skupina, ktorá kameň spravuje (skala / zdroj), inak samotný objekt
            BreakableNode owner = t.GetComponentInParent<BreakableNode>();
            ResourceNode pile = t.GetComponentInParent<ResourceNode>();
            GameObject target = owner != null ? owner.gameObject : pile != null ? pile.gameObject : t.gameObject;
            if (!done.Add(target)) continue;   // skupina, model aj jeho koreň sú jeden kameň
            rocks++;

            // Zapadnutý pod terén: zdvihne sa tak, aby spodok ležal na povrchu
            if (LiftOutOfTerrain(target.transform)) lifted++;

            if (Realign(target)) realigned++;
            else if (!HasSolidCollider(target)) { AddBox(target); colliderAdded++; }

            if (owner == null)
            {
                var node = target.AddComponent<BreakableNode>();
                node.Configure(ToolKit.Kind.Pickaxe, ItemCatalog.Stone, t.name == "Monolit" ? "Monolit" : pile != null ? "Kamene" : "Skala", t.name == "Monolit" ? 250f : pile != null ? 40f : 60f, false);
                if (pile != null) pile.LinkBreakable();
                madeBreakable++;
            }
            else if (owner != null) owner.RefreshColliders();
        }

        int trees = EnsureTreesChoppable();
        int repairedNodes = GuardNodes();
        if (rocks > 0 || repairedNodes > 0 || trees > 0) Debug.Log($"RockGuard: stromy doplnené na sekanie {trees}; skontrolovaných kameňov {rocks}, doplnená kolízia {colliderAdded}, doplnená ťažiteľnosť {madeBreakable}, zdvihnutých z terénu {lifted}, kolízia prestavaná podľa modelu {realigned}; stromy, bloky a zdroje opravené {repairedNodes}.");
    }

    /// <summary>
    /// Kolízia skaly musí ležať tam, kde je jej model, a mať jeho veľkosť. Ak sa model (alebo skupina) posunul ručne bez kolízie,
    /// alebo je kolízia zväčšená, zmaže sa a vytvorí sa jedna nová podľa modelu. Skaly s vlastnou kolíziou z modelu (MeshCollider) sa nemenia.
    /// Vráti true, ak sa kolízia prestavala.
    /// </summary>
    public static bool Realign(GameObject target)
    {
        if (!ComputeBox(target, out Vector3 center, out Vector3 size)) return false;

        var boxes = new List<BoxCollider>();
        foreach (Collider c in target.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger || c is TerrainCollider) continue;
            if (!(c is BoxCollider box)) return false;   // vlastná kolízia modelu (mesh / kapsula): nechať
            boxes.Add(box);
        }

        // Jediná správna krabica na koreni skupiny sa nemení
        if (boxes.Count == 1 && boxes[0].gameObject == target
            && (boxes[0].center - center).sqrMagnitude < 0.0025f && (boxes[0].size - size).sqrMagnitude < 0.0025f) return false;

        foreach (BoxCollider box in boxes)
        {
            if (Application.isPlaying) Destroy(box); else DestroyImmediate(box);
        }
        AddBox(target);
        return true;
    }


    /// <summary>
    /// Každý strom v Divočine sa dá zoťať sekerou: mŕtve stromy (dekorácia "MrtveStromy" a skupiny "Mŕtvy strom") aj skupiny stromov,
    /// ktorým sa pri generovaní nedoplnil BreakableNode. Kolíziu kmeňa doplní GuardNodes.
    /// </summary>
    static int EnsureTreesChoppable()
    {
        int added = 0;

        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t == null) continue;

            bool deadGroup = t.name == "Mŕtvy strom";
            bool deadDecor = t.parent != null && t.parent.name == "MrtveStromy";
            bool liveGroup = t.name == "Strom" || t.name == "Smrek";
            if (!deadGroup && !deadDecor && !liveGroup) continue;

            if (t.GetComponentInParent<BreakableNode>() != null || t.GetComponentInChildren<BreakableNode>(true) != null) continue;
            if (t.GetComponentInChildren<Renderer>(true) == null) continue;

            float height = 6f;
            if (VisualBounds(t.gameObject, out Bounds b)) height = Mathf.Max(2f, b.size.y);
            float hp = (deadGroup || deadDecor) ? Mathf.Clamp(30f + height * 6f, 35f, 110f) : Mathf.Clamp(50f + height * 12f, 40f, 300f);

            var node = t.gameObject.AddComponent<BreakableNode>();
            node.Configure(ToolKit.Kind.Axe, ItemCatalog.Wood, (deadGroup || deadDecor) ? "Mŕtvy strom" : "Strom", hp, true);
            added++;
        }
        return added;
    }

    // ======== Stromy, bloky a zdroje: model a jeho "kostra" (kolízia, bod zberu) musia byť na tom istom mieste ========
    static int GuardNodes()
    {
        int repaired = 0;

        foreach (BreakableNode n in FindObjectsByType<BreakableNode>())   // aj v editore (statický zoznam All sa plní len za behu)
        {
            if (n == null) continue;

            if (RecenterPivot(n.transform)) repaired++;
            if (n.IsTree) { if (EnsureTrunk(n.gameObject)) repaired++; }
            else if (n.Requires != ToolKit.Kind.Pickaxe && EnsureBoxMatches(n.gameObject)) repaired++;   // bloky a vraky (kladivo)
            n.RefreshColliders();
        }

        foreach (ResourceNode r in FindObjectsByType<ResourceNode>())
            if (r != null && RecenterPivot(r.transform)) repaired++;

        return repaired;
    }

    static bool IsUnderStump(Transform t, Transform root)
    {
        for (Transform p = t; p != null && p != root; p = p.parent)
            if (p.name == "Pen") return true;
        return false;
    }

    static bool VisualBounds(GameObject root, out Bounds visual)
    {
        visual = default;
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (IsUnderStump(r.transform, root.transform)) continue;
            if (!any) { visual = r.bounds; any = true; } else visual.Encapsulate(r.bounds);
        }
        return any;
    }

    // Skupina (bod zberu / ťažby) sa posunie k modelu, ak sa model ručne posunul ďalej než 1,5 m; model ostane na mieste
    static bool RecenterPivot(Transform group)
    {
        if (!VisualBounds(group.gameObject, out Bounds visual)) return false;

        Vector3 delta = new Vector3(visual.center.x - group.position.x, 0f, visual.center.z - group.position.z);
        if (delta.magnitude < 1.5f) return false;

        var children = new List<Transform>();
        foreach (Transform child in group) children.Add(child);

        group.position += delta;
        foreach (Transform child in children)
            if (child.name != "Pen") child.position -= delta;   // peň patrí k novému bodu stromu
        Physics.SyncTransforms();
        return true;
    }

    static bool EnsureTrunk(GameObject tree)
    {
        if (!VisualBounds(tree, out Bounds visual)) return false;

        var solid = new List<Collider>();
        foreach (Collider c in tree.GetComponentsInChildren<Collider>(true))
            if (c.enabled && !c.isTrigger && !(c is TerrainCollider) && !IsUnderStump(c.transform, tree.transform)) solid.Add(c);

        if (solid.Count > 0)
        {
            Bounds physical = solid[0].bounds;
            foreach (Collider c in solid) physical.Encapsulate(c.bounds);
            Vector2 offset = new Vector2(physical.center.x - visual.center.x, physical.center.z - visual.center.z);
            if (physical.Intersects(visual) && offset.magnitude < Mathf.Max(1.5f, visual.extents.x)) return false;   // kmeň sedí pod modelom
        }

        foreach (Collider c in solid)
        {
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }

        float height = Mathf.Clamp(visual.size.y, 1.5f, 6f);
        float scale = Mathf.Max(0.0001f, tree.transform.lossyScale.x);
        Vector3 basePoint = new Vector3(visual.center.x, visual.min.y, visual.center.z);
        Vector3 local = tree.transform.InverseTransformPoint(basePoint + Vector3.up * height * 0.5f);

        CapsuleCollider capsule = tree.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.radius = 0.4f / scale;
        capsule.height = height / scale;
        capsule.center = local;
        return true;
    }

    // Blok / vrak: kolízia musí byť pri modeli; ak nesedí, prestaví sa podľa modelu (inak sa necháva)
    static bool EnsureBoxMatches(GameObject block)
    {
        if (!VisualBounds(block, out Bounds visual)) return false;

        Bounds physical = default;
        bool any = false;
        foreach (Collider c in block.GetComponentsInChildren<Collider>(true))
        {
            if (!c.enabled || c.isTrigger || c is TerrainCollider) continue;
            if (!any) { physical = c.bounds; any = true; } else physical.Encapsulate(c.bounds);
        }

        if (any && physical.Intersects(visual) && (physical.center - visual.center).magnitude < 1.5f) return false;
        return Realign(block);
    }
    static bool HasSolidCollider(GameObject root)
    {
        foreach (Collider c in root.GetComponentsInChildren<Collider>(false))
            if (c.enabled && !c.isTrigger && !(c is TerrainCollider)) return true;
        return false;
    }

    // Krabica podľa lokálnych rozmerov vykreslení v priestore koreňa (nie podľa svetového AABB: ten by natočenú skalu nafúkol)
    static bool ComputeBox(GameObject root, out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.zero;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
        bool first = true;
        Bounds bounds = default;
        foreach (Renderer r in renderers)
        {
            Bounds lb = r.localBounds;
            Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                Vector3 local = m.MultiplyPoint3x4(corner);
                if (first) { bounds = new Bounds(local, Vector3.zero); first = false; } else bounds.Encapsulate(local);
            }
        }

        center = bounds.center;
        size = new Vector3(bounds.size.x * HorizontalShrink, bounds.size.y, bounds.size.z * HorizontalShrink);
        return true;
    }

    static void AddBox(GameObject root)
    {
        if (!ComputeBox(root, out Vector3 center, out Vector3 size)) return;

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = center;
        box.size = size;
    }

    static bool LiftOutOfTerrain(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        Vector3 p = root.position;
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 local = p - terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;

            float surface = terrain.SampleHeight(p) + terrain.transform.position.y;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            if (b.max.y >= surface + 0.2f) return false;   // vyčnieva dosť

            root.position += Vector3.up * (surface - b.min.y);
            Physics.SyncTransforms();
            return true;
        }
        return false;
    }
}
