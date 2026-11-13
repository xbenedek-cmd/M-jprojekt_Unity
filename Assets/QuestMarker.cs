using UnityEngine;

/// <summary>
/// Cieľ aktuálnej úlohy: značka na mape (M aj minimapa) a šípka smeru na obrazovke. Cieľ sa vyberá centrálne podľa stavu úloh
/// (prológ, Tiene v Hlbokom lese, Šepot v Dymových Horách), takže runnery úloh sa nemenia.
/// </summary>
public static class QuestMarker
{
    public static bool Active { get; private set; }
    public static Vector3 Position { get; private set; }
    public static string Label { get; private set; } = "";

    public static void Set(Vector3 position, string label)
    {
        Active = true;
        Position = position;
        Label = label;
    }

    public static void Clear() => Active = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() => Active = false;
}

/// <summary>Raz za pol sekundy zistí, kam má hráč ísť, a nastaví QuestMarker.</summary>
public class QuestMarkerDirector : MonoBehaviour
{
    private PlayerStats player;
    private float next;
    private QuestWorker[] workers = new QuestWorker[0];
    private StoryNpc[] npcs = new StoryNpc[0];
    private float nextScan;
    private Quest2Runner q2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<QuestMarkerDirector>() != null) return;
        new GameObject("QuestMarkerDirector").AddComponent<QuestMarkerDirector>();
    }

    void Update()
    {
        if (Time.time < next || PauseMenu.AtStartup) return;
        next = Time.time + 0.5f;

        if (player == null) { player = FindAnyObjectByType<PlayerStats>(); QuestMarker.Clear(); return; }
        if (player.IsDead) { QuestMarker.Clear(); return; }

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 3f;
            workers = FindObjectsByType<QuestWorker>();
            npcs = FindObjectsByType<StoryNpc>();
            if (q2 == null) q2 = FindAnyObjectByType<Quest2Runner>();
        }

        Vector3 p = player.transform.position;

        if (global::Prologue.Active) { PrologueGoal(p); return; }
        if (Quest2.Active) { Quest2Goal(p); return; }
        if (MainQuest.State != QuestState.None && MainQuest.State != QuestState.Done && MainQuest.State != QuestState.Resolved) { MainGoal(p); return; }
        if (Story.PrologueDone && MainQuest.State == QuestState.None) { Npc(CharId.Kazumi, "Kazumi (zadá úlohu)"); return; }
        QuestMarker.Clear();
    }

    // ---------- Prológ ----------
    void PrologueGoal(Vector3 p)
    {
        switch (global::Prologue.Step)
        {
            case PrologueStep.Talk:
                QuestWorker w = NearestWorker(p);
                if (w != null) QuestMarker.Set(w.transform.position, "Drevorubač"); else QuestMarker.Clear();
                break;
            case PrologueStep.GoToWilds:
                GoToWilds(p, "Brána do Divočiny");
                break;
            case PrologueStep.Chop:
                BreakableNode t = NearestTree(p);
                if (t != null) QuestMarker.Set(t.transform.position, "Strom na výrub"); else QuestMarker.Clear();
                break;
            default:
                QuestMarker.Clear();
                break;
        }
    }

    // ---------- Tiene v Hlbokom lese ----------
    void MainGoal(Vector3 p)
    {
        switch (MainQuest.State)
        {
            case QuestState.Gather:
                if (MainQuest.HasEnough(MainQuestRunner.GatheredNow(player, ItemCatalog.Wood, MainQuest.BaseWood), MainQuestRunner.GatheredNow(player, ItemCatalog.Scrap, MainQuest.BaseScrap)))
                {
                    Npc(CharId.Kazumi, "Kazumi (odovzdať materiál)");
                    break;
                }
                if (InZone("Zem_Kanon", p))
                {
                    if (!MainQuest.TracksFound && MainQuest.TrackObjects.Count > 0)
                    {
                        // Drevo aj šrot hotové: ďalší krok sú stopy, nie stromy
                        GameObject nearest = null;
                        float best = float.MaxValue;
                        foreach (GameObject tr in MainQuest.TrackObjects)
                        {
                            if (tr == null) continue;
                            float d = (tr.transform.position - p).sqrMagnitude;
                            if (d < best) { best = d; nearest = tr; }
                        }
                        if (nearest != null) { QuestMarker.Set(nearest.transform.position, "Čierne stopy"); break; }
                    }
                    BreakableNode t = NearestTree(p);
                    if (t != null) QuestMarker.Set(t.transform.position, "Strom na výrub"); else QuestMarker.Clear();
                }
                else GoToZone("Zem_Kanon", "Bleskový kaňon", "Brána do Bleskového kaňonu", p);
                break;
            case QuestState.Return:
                Npc(CharId.Kazumi, "Kazumi (hlásenie)");
                break;
            default:
                QuestMarker.Clear();
                break;
        }
    }

    // ---------- Šepot v Dymových Horách ----------
    void Quest2Goal(Vector3 p)
    {
        switch (Quest2.Step)
        {
            case Quest2Step.Sneak:
            case Quest2Step.Gate:
                GoToWilds(p, "Južná brána");
                break;
            case Quest2Step.Forest:
                if (q2 != null && q2.InWildsNow) QuestMarker.Set(q2.ProjectionPosition, "Dymová silueta");
                else GoToWilds(p, "Brána do Divočiny");
                break;
            case Quest2Step.Return:
                if (Quest2.HasHome) QuestMarker.Set(Quest2.HomePos, "Tvoja izba");
                else QuestMarker.Clear();
                break;
            case Quest2Step.Morning:
                Npc(CharId.Ren, "Veliteľ Ren");
                break;
            default:
                QuestMarker.Clear();
                break;
        }
    }

    // ---------- Pomocné ----------
    void GoToWilds(Vector3 p, string label)
    {
        if (InZone("Zem_Divocina", p))
        {
            BreakableNode t = NearestTree(p);
            if (t != null) QuestMarker.Set(t.transform.position, "Strom na výrub"); else QuestMarker.Clear();
            return;
        }
        PortalGate g = PortalGate.NearestTo("Divočina", p);
        if (g != null) QuestMarker.Set(g.transform.position, label); else QuestMarker.Clear();
    }

    void GoToZone(string ground, string zoneName, string label, Vector3 p)
    {
        PortalGate g = PortalGate.NearestTo(zoneName, p);
        if (g != null) { QuestMarker.Set(g.transform.position, label); return; }
        if (ZoneLoader.TryGetGround(ground, out Bounds b)) QuestMarker.Set(b.center, zoneName); else QuestMarker.Clear();
    }

    void Npc(CharId id, string label)
    {
        foreach (StoryNpc n in npcs)
            if (n != null && n.Character == id) { QuestMarker.Set(n.transform.position, label); return; }
        QuestMarker.Clear();
    }

    QuestWorker NearestWorker(Vector3 p)
    {
        QuestWorker best = null;
        float bestSqr = float.MaxValue;
        foreach (QuestWorker w in workers)
        {
            if (w == null) continue;
            float d = (w.transform.position - p).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = w; }
        }
        return best;
    }

    static BreakableNode NearestTree(Vector3 p)
    {
        BreakableNode best = null;
        float bestSqr = float.MaxValue;
        foreach (BreakableNode n in BreakableNode.All)
        {
            if (n == null || !n.IsTree || n.IsDown || !n.gameObject.activeInHierarchy) continue;
            float d = (n.transform.position - p).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = n; }
        }
        return best;
    }

    static bool InZone(string ground, Vector3 p)
    {
        if (!ZoneLoader.TryGetGround(ground, out Bounds b)) return false;
        return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z;
    }
}
