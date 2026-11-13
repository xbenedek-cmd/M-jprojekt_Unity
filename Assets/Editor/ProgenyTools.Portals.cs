using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Obnova portálov: doplní iba tie, ktoré chýbajú (nič nemaže ani nepresúva): štart ↔ Megakorporácia, štart ↔ každá divočina vrátane Bane
/// a štart ↔ Centrum. Brány zo štartu majú vlastný koreň CityPortals, takže ostanú aktívne aj pri vypnutej zóne.
/// </summary>
public static partial class ProgenyTools
{
    [MenuItem("Progeny/Mesto/Portály: doplniť všetky chýbajúce (divočiny, Bane, Megakorporácia)", false, 299)]
    static void RestoreAllPortals()
    {
        var existing = new Dictionary<string, PortalGate>();
        foreach (PortalGate g in Object.FindObjectsByType<PortalGate>(FindObjectsInactive.Include)) existing[g.gameObject.name] = g;

        GameObject portalRoot = GameObject.Find("CityPortals");
        if (portalRoot == null)
        {
            portalRoot = new GameObject("CityPortals");
            Undo.RegisterCreatedObjectUndo(portalRoot, "Portály");
        }

        // Štartová brána do Megakorporácie (podľa nej sa počítajú ostatné)
        PlayerStats player = Object.FindAnyObjectByType<PlayerStats>();
        PortalGate toCorp;
        if (!existing.TryGetValue("Portal_DoMegakorporacie", out toCorp))
        {
            if (player == null) { Debug.LogError("Nenašiel sa hráč, nemám podľa čoho umiestniť štartovú bránu."); return; }
            Vector3 civil = player.transform.position + player.transform.forward * 12f;
            toCorp = BuildPortal(portalRoot.transform, "Portal_DoMegakorporacie", civil, new Color(0.3f, 0.7f, 1f), Quaternion.LookRotation(-player.transform.forward));
            existing[toCorp.gameObject.name] = toCorp;
        }
        Transform civilGate = toCorp.transform;
        Vector3 civilGround = civilGate.position + Vector3.down * 1.5f;
        int created = 0;

        // Megakorporácia: zem a spätný portál na pevnom mieste
        if (!existing.ContainsKey("Portal_DoMesta"))
        {
            GameObject ground = GameObject.Find("Zem_Megakorporacia");
            Vector3 corp;
            if (ground != null) corp = ground.transform.position + Vector3.up * 0.5f;
            else
            {
                corp = new Vector3(civilGate.position.x + CorpOffset.x, civilGround.y, civilGate.position.z + CorpOffset.z);
                BuildZoneGround(portalRoot.transform, "Zem_Megakorporacia", corp, new Vector2(230f, 230f), new Color(0.18f, 0.2f, 0.24f));
            }
            PortalGate back = BuildPortal(portalRoot.transform, "Portal_DoMesta", corp, new Color(1f, 0.55f, 0.2f), Quaternion.LookRotation(Vector3.left));
            existing[back.gameObject.name] = back;
            created++;
        }
        toCorp.Link(existing["Portal_DoMesta"], "Megakorporácia");
        existing["Portal_DoMesta"].Link(toCorp, WorldNames.StartCity);
        EditorUtility.SetDirty(toCorp);
        EditorUtility.SetDirty(existing["Portal_DoMesta"]);

        // Divočiny a Bane
        foreach (WildTheme t in WildThemes)
        {
            GameObject zoneRoot = GameObject.Find(t.RootName);
            if (zoneRoot == null) continue;   // zóna ešte neexistuje

            string toName = "Portal_Do" + t.GateKey, fromName = "Portal_z" + t.GateKey;
            PortalGate entryPortal, retPortal;
            existing.TryGetValue(toName, out entryPortal);
            existing.TryGetValue(fromName, out retPortal);

            if (entryPortal == null)
            {
                entryPortal = BuildPortal(portalRoot.transform, toName, civilGround + civilGate.right * t.cityGateOffset, t.gate, civilGate.rotation);
                created++;
            }
            if (retPortal == null)
            {
                Transform terrain = zoneRoot.transform.Find("Teren");
                Vector3 center = terrain != null ? terrain.position + new Vector3(TerrainSize * 0.5f, TerrainBaseDepth, TerrainSize * 0.5f) : zoneRoot.transform.position;
                Vector3 gatePos = center + new Vector3(0f, 0f, -(TerrainSize * 0.5f - 25f));
                retPortal = BuildPortal(zoneRoot.transform, fromName, gatePos, new Color(0.35f, 0.7f, 1f), Quaternion.LookRotation(Vector3.forward));
                created++;
            }
            entryPortal.Link(retPortal, t.displayName);
            retPortal.Link(entryPortal, WorldNames.StartCity);
            EditorUtility.SetDirty(entryPortal);
            EditorUtility.SetDirty(retPortal);
        }

        // Centrum mesta
        GameObject centrum = GameObject.Find("Mesto_Centrum");
        if (centrum != null)
        {
            PortalGate entryPortal, retPortal;
            existing.TryGetValue("Portal_DoCentra", out entryPortal);
            existing.TryGetValue("Portal_zCentra", out retPortal);
            if (entryPortal == null) { entryPortal = BuildPortal(portalRoot.transform, "Portal_DoCentra", civilGround + civilGate.right * -12f, new Color(0.4f, 1f, 0.5f), civilGate.rotation); created++; }
            if (retPortal == null)
            {
                Transform ground = centrum.transform.Find("Zem_Centrum");
                Vector3 c = ground != null ? ground.position + Vector3.up * 0.5f : centrum.transform.position;
                retPortal = BuildPortal(centrum.transform, "Portal_zCentra", c + new Vector3(0f, 0f, -132f), new Color(0.35f, 0.7f, 1f), Quaternion.LookRotation(Vector3.forward));
                created++;
            }
            entryPortal.Link(retPortal, "Centrum mesta");
            retPortal.Link(entryPortal, WorldNames.StartCity);
            EditorUtility.SetDirty(entryPortal);
            EditorUtility.SetDirty(retPortal);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Portály: doplnených {created}, všetky páry prepojené (Megakorporácia, divočiny vrátane Bane" + (centrum != null ? ", Centrum" : "") + "). Ulož scénu. Vchod do bane (Hlbinná Baňa) je v zóne Bane: Progeny → Bane → Pridať vchod Hlbinná Baňa.");
    }
}
