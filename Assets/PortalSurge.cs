using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Portal Surge: pri nestabilnom portáli sa občas (po začiatku dňa, v náhodnom čase) cez portál do mesta prevalí vlna vojakov
/// a z divočiny sa na čas prilákajú démoni. Šanca rastie s klesajúcou stabilitou portálu (PortalStability).
/// </summary>
public class PortalSurge : MonoBehaviour
{
    private const string CorpZone = "Megakorporácia";

    [SerializeField] private int minSoldiers = 4;
    [SerializeField] private int maxSoldiers = 7;
    [SerializeField] private int maxDemons = 3;
    [SerializeField] private float demonStaySeconds = 180f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<PortalSurge>() != null) return;
        new GameObject("PortalSurge").AddComponent<PortalSurge>();
    }

    void OnEnable() => DayNightCycle.OnNewDay += HandleNewDay;
    void OnDisable() => DayNightCycle.OnNewDay -= HandleNewDay;

    void HandleNewDay(int day)
    {
        if (PortalGate.All.Count == 0) return;

        float chance = Mathf.Clamp01((100f - PortalStability.Stability) / 120f + 0.1f);
        if (Random.value < chance) Invoke(nameof(Trigger), Random.Range(25f, 100f));
    }

    public void Trigger()
    {
        PlayerStats player = FindAnyObjectByType<PlayerStats>();
        if (player == null || player.IsDead || PauseMenu.AtStartup) return;

        PortalGate gate = null;
        foreach (PortalGate g in PortalGate.All)
            if (g.ZoneName == CorpZone) { gate = g; break; }

        GameObject template = null;
        foreach (ZonePopulation zone in FindObjectsByType<ZonePopulation>())
            if (zone.SoldierTemplate != null) { template = zone.SoldierTemplate; break; }

        if (gate == null || template == null) return;

        int soldiers = Random.Range(minSoldiers, maxSoldiers + 1);
        for (int i = 0; i < soldiers; i++)
        {
            Vector3 position = gate.transform.position + gate.transform.forward * (4f + i) + new Vector3(Random.Range(-3f, 3f), 0.1f, Random.Range(-3f, 3f));
            GameObject soldier = Instantiate(template, position, template.transform.rotation, template.transform.parent);
            soldier.name = $"Vlna_{i + 1}";
            soldier.AddComponent<TemporaryVisitor>();
            soldier.SetActive(true);
        }

        int demons = PullDemons(gate.transform.position);
        Reputation.AddSuspicion(10f);
        GameHUD.Message($"PORTAL SURGE! Portál sa prelomil: {soldiers} vojakov" + (demons > 0 ? $" a {demons} divokých démonov" : "") + " v meste. Pomôž obrancom!", 6f);
        GameAudio.Play(GameAudio.Sfx.Portal, gate.transform.position);
    }

    // Niektorí voľne sa túlajúci démoni (nie bossovia) sa objavia pri portáli a po čase sa vrátia
    int PullDemons(Vector3 around)
    {
        var candidates = new List<DemonEnemy>();
        foreach (DemonEnemy demon in DemonEnemy.All)
            if (demon != null && demon.tier != DemonTier.Boss && demon.gameObject.activeInHierarchy && demon.Damage != null && !demon.Damage.IsDead
                && demon.GetComponent<SurgeGuest>() == null)
                candidates.Add(demon);

        int pulled = 0;
        while (pulled < maxDemons && candidates.Count > 0)
        {
            int index = Random.Range(0, candidates.Count);
            DemonEnemy demon = candidates[index];
            candidates.RemoveAt(index);

            demon.gameObject.AddComponent<SurgeGuest>().Begin(around + new Vector3(Random.Range(-6f, 6f), 0.5f, Random.Range(-6f, 6f)), demonStaySeconds);
            pulled++;
        }
        return pulled;
    }
}

/// <summary>Démon prilákaný portálom: po čase sa vráti na pôvodné miesto.</summary>
public class SurgeGuest : MonoBehaviour
{
    private Vector3 home;
    private float returnAt;

    public void Begin(Vector3 target, float seconds)
    {
        home = transform.position;
        returnAt = Time.time + seconds;
        MoveTo(target);
    }

    void Update()
    {
        if (Time.time < returnAt) return;

        MoveTo(home);
        Destroy(this);
    }

    void MoveTo(Vector3 position)
    {
        transform.position = position;
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) return;
        rb.position = position;
        rb.linearVelocity = Vector3.zero;
    }
}
