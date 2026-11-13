using UnityEngine;

/// <summary>
/// Sieť portálov: po štarte prepojí páry podľa mien (Portal_DoMegakorporacie ↔ Portal_DoMesta, Portal_DoX ↔ Portal_zX),
/// aj keď sa odkaz v scéne stratil pri prerábaní zón, otvorí zatvorené brány a nad každú dá nápis s cieľom.
/// </summary>
public class PortalNetwork : MonoBehaviour
{
    private bool done;
    private float nextTry;


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<PortalNetwork>() != null) return;
        new GameObject("PortalNetwork").AddComponent<PortalNetwork>();
    }

    void Update()
    {
        if (done || PauseMenu.AtStartup || Time.time < nextTry) return;
        nextTry = Time.time + 1f;
        if (PortalGate.All.Count == 0) return;
        done = true;
        RemoveOldMirroredSigns();
        Connect();
    }

    static PortalGate Find(string name)
    {
        foreach (PortalGate g in PortalGate.All) if (g != null && g.gameObject.name == name) return g;
        return null;
    }

    // Staré nápisy (dvojice textov chrbtom k sebe) sa zrkadlovo prekrývali; zostáva len jeden nápis otočený ku kamere
    static void RemoveOldMirroredSigns()
    {
        foreach (TextMesh tm in FindObjectsByType<TextMesh>(FindObjectsInactive.Include))
        {
            string n = tm.gameObject.name;
            bool old = n == "NapisZozadu" || n.EndsWith("_zozadu") || (n == "Napis" && tm.GetComponentInParent<PortalLabel>() == null);
            if (old) Destroy(tm.gameObject);
        }
    }

    static void Connect()
    {
        foreach (PortalGate gate in PortalGate.All.ToArray())
        {
            if (gate == null) continue;
            string n = gate.gameObject.name;
            string partnerName = null;
            string zone = null;

            if (n == "Portal_DoMegakorporacie") { partnerName = "Portal_DoMesta"; zone = "Megakorporácia"; }
            else if (n == "Portal_DoMesta") { partnerName = "Portal_DoMegakorporacie"; zone = WorldNames.StartCity; }
            else if (n.StartsWith("Portal_Do")) { partnerName = "Portal_z" + n.Substring("Portal_Do".Length); zone = Pretty(n.Substring("Portal_Do".Length)); }
            else if (n.StartsWith("Portal_z")) { partnerName = "Portal_Do" + n.Substring("Portal_z".Length); zone = WorldNames.StartCity; }
            if (partnerName == null)
            {
                Label(gate);   // aj brána s neštandardným menom dostane nápis
                continue;
            }

            PortalGate partner = gate.Destination != null ? gate.Destination : Find(partnerName);
            if (partner == null) { Debug.LogWarning($"PortalNetwork: '{n}' nemá protiľahlý portál '{partnerName}'. Vytvor zónu znova."); Label(gate); continue; }

            if (!gate.HasDestination) gate.Link(partner, zone);
            else if (!string.Equals(gate.ZoneName, "Divočina", System.StringComparison.Ordinal)) gate.Rename(ZoneOf(partner, zone));   // návraty vedú do Centra, ak tam stojí protiľahlá brána
            if (!gate.IsOpen) gate.SetOpen(true);
            Label(gate);
        }
    }

    static string Pretty(string key)
    {
        switch (key)
        {
            case "Divocina": case "Divociny": return "Divočina";
            case "Centra": return "Centrum mesta";
            case "Kanon": return "Bleskový kaňon";
            case "Les": return "Temný les";
            case "Pustatina": return "Zrezavená pustatina";
            case "Mociare": return "Močiare";
            case "Bane": return "Bane";
            default: return key;
        }
    }

    // Nápis portálu je samostatný objekt bez rodiča: brána sa za behu presúva z koreňa zóny do "PortalyAParcely", takže nápis pod vypnutou zónou by zmizol
    static void Label(PortalGate gate) => MakeLabel(gate.transform.position + Vector3.up * 7.0f, null, "Napis_" + gate.gameObject.name, SignText(gate));

    // Zóna, do ktorej vedie návrat: ak protiľahlá brána stojí v Centre mesta (portálové nádvorie), je to Centrum
    static string ZoneOf(PortalGate partner, string fallback)
    {
        for (Transform t = partner.transform; t != null; t = t.parent)
            if (t.name == "Mesto_Centrum") return "Centrum mesta";
        return fallback;
    }

    // Prirodzený názov brány: "Brána do Divočiny" / "Späť do Popolovej Hradby"
    static string SignText(PortalGate gate)
    {
        string n = gate.gameObject.name;
        bool back = n.StartsWith("Portal_z") || n == "Portal_DoMesta";
        if (!gate.HasDestination) return "Portál bez cieľa";
        return ShortName(gate.ZoneName);
    }

    // Nápis brány je len krátky názov cieľa
    static string ShortName(string zone)
    {
        switch (zone)
        {
            case "Bleskový kaňon": return "Kaňon";
            case "Temný les": return "Les";
            case "Zrezavená pustatina": return "Pustatina";
            case "Centrum mesta": return "Centrum";
            case WorldNames.StartCity: return "Hradba";
            default: return zone;
        }
    }

    /// <summary>Čitateľný nápis otočený ku kamere (aj pre vchod do bane).</summary>
    public static void MakeLabel(Vector3 position, Transform parent, string labelName, string textValue, float characterSize = 0.1f)
    {
        if (GameObject.Find(labelName) != null) return;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var root = new GameObject(labelName);
        root.transform.SetParent(parent, false);
        root.transform.position = position;
        root.AddComponent<PortalLabel>();

        // Čierny tieň za textom zlepší čitateľnosť na každom pozadí
        for (int layer = 0; layer < 2; layer++)
        {
            var go = new GameObject(layer == 0 ? "Tien" : "Text");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = layer == 0 ? new Vector3(0.04f, -0.04f, 0.02f) : Vector3.zero;
            var text = go.AddComponent<TextMesh>();
            text.text = textValue;
            text.fontSize = 80;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.color = layer == 0 ? new Color(0f, 0f, 0f, 0.9f) : new Color(0.85f, 0.97f, 1f);
            text.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }
}

/// <summary>Nápis (nad portálom, stánkom, značka): otočený ku kamere a viditeľný len, keď je hráč bližšie ako ShowDistance (inak by sa nápisy prekrývali).</summary>
public class PortalLabel : MonoBehaviour
{
    public const float ShowDistance = 10f;

    private static Transform player;
    private static float nextPlayerSearch;

    private Transform cam;
    private Renderer[] renderers;
    private bool shown = true;

    void LateUpdate()
    {
        if (player == null && Time.unscaledTime >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.unscaledTime + 1f;
            PlayerStats stats = FindAnyObjectByType<PlayerStats>();
            if (stats != null) player = stats.transform;
        }
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);

        if (player != null)
        {
            float d = Vector3.Distance(transform.position, player.position);
            bool want = shown ? d <= ShowDistance + 1f : d <= ShowDistance;   // malá hysterézia, aby nápis neblikal na hranici
            if (want != shown)
            {
                shown = want;
                foreach (Renderer r in renderers) if (r != null) r.enabled = shown;
            }
            if (!shown) return;
        }

        if (cam == null)
        {
            Camera main = Camera.main;
            if (main == null) return;
            cam = main.transform;
        }

        Vector3 away = transform.position - cam.position;
        if (away.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.LookRotation(away);   // text sa číta z miesta kamery, nie zrkadlovo

        float distance = away.magnitude;
        transform.localScale = Vector3.one * Mathf.Clamp(distance * 0.07f, 1f, 2.5f);
    }
}
