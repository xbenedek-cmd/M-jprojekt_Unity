using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vojaci prechádzajú cez portály: keď hráč prejde portálom a v jeho blízkosti ho prenasledujú vojaci v poplachu (SoldierState.Alert),
/// o chvíľu sa pri protiľahlom portáli objavia ich dočasné kópie (ako pri Portal Surge), ktoré hráča hľadajú a po čase zmiznú.
/// Pôvodní vojaci ostávajú v svojej zóne (nedotknutá populácia zón).
/// </summary>
public static class PortalFollowers
{
    private const float PursuitRadius = 30f;
    private const int MaxFollowers = 4;
    private const float ArrivalDelay = 1.8f;

    private static PortalFollowerRunner runner;

    public static void OnPlayerUsedPortal(PortalGate from, Vector3 arrival)
    {
        int count = 0;
        foreach (SoldierAI soldier in SoldierAI.All)
        {
            if (soldier == null || soldier.IsDead || soldier.State != SoldierState.Alert || !soldier.gameObject.activeInHierarchy) continue;
            if (soldier.GetComponent<TemporaryVisitor>() != null) continue;   // kópie sa nekopírujú ďalej
            if ((soldier.transform.position - from.transform.position).sqrMagnitude > PursuitRadius * PursuitRadius) continue;
            if (++count >= MaxFollowers) break;
        }
        if (count == 0) return;

        GameObject template = null;
        foreach (ZonePopulation zone in Object.FindObjectsByType<ZonePopulation>())
            if (zone.SoldierTemplate != null) { template = zone.SoldierTemplate; break; }
        if (template == null) return;

        if (runner == null) runner = new GameObject("PortalFollowers").AddComponent<PortalFollowerRunner>();
        runner.StartCoroutine(runner.Arrive(template, arrival, count));
    }
}

public class PortalFollowerRunner : MonoBehaviour
{
    public IEnumerator Arrive(GameObject template, Vector3 arrival, int count)
    {
        yield return new WaitForSeconds(1.8f);

        var spawned = new List<SoldierAI>();
        for (int i = 0; i < count; i++)
        {
            Vector3 position = arrival + new Vector3(Random.Range(-2.5f, 2.5f), 0.1f, Random.Range(-2.5f, 2.5f));
            GameObject clone = Instantiate(template, position, template.transform.rotation, template.transform.parent);
            clone.name = $"Nasledovnik_{i + 1}";
            clone.AddComponent<TemporaryVisitor>();
            clone.SetActive(true);

            SoldierAI ai = clone.GetComponent<SoldierAI>();
            if (ai != null) spawned.Add(ai);
        }
        GameAudio.Play(GameAudio.Sfx.Portal, arrival);
        GameHUD.Message("Vojaci prešli za tebou cez portál!", 3.5f);

        yield return null;   // po Start, aby sa poplach nezrušil
        foreach (SoldierAI ai in spawned)
            if (ai != null) ai.ReceiveFalseReport();
    }
}
