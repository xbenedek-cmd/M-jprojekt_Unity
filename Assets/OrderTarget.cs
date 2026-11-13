using System.Collections.Generic;
using UnityEngine;

public enum OrderAction
{
    Destroy,    // cieľ musí zomrieť (konvoj, generátor, brána...) - potrebuje Damageable
    Interact,   // stoj pri cieli a drž kláves (otvorenie brány, sabotáž)
    Reach,      // stoj v dosahu cieľa niekoľko sekúnd (obeta na hranici lesa)
    KillCivilians,
    KillSoldiers,       // zabi N vojakov (requiredKills)
    DestroyBuildings    // zniči N ľudských stavieb (requiredKills)
}

/// <summary>
/// Cieľ nočného rozkazu Arakaniho. Pridaj ho na objekt v scéne (konvoj, brána, baňa, svätyňa)
/// a v Inspectore nastav text, typ akcie a odmenu. ArakaniFavor si z nich každú noc vyberie jeden.
/// </summary>
public class OrderTarget : MonoBehaviour
{
    public static readonly List<OrderTarget> All = new List<OrderTarget>();

    [Header("Rozkaz")]
    [Tooltip("Text, ktorý hráč uvidí, napr. 'Znič konvoj pri južnej bráne'")]
    public string orderText = "Zničiť konvoj";
    public OrderAction action = OrderAction.Destroy;
    [Tooltip("Od ktorého dňa sa môže rozkaz objaviť")]
    public int minDay = 1;
    [Tooltip("Po splnení sa môže objaviť znova (o deň neskôr). Pri Destroy nemá zmysel.")]
    public bool repeatable = false;

    [Header("Odmena")]
    public float favorReward = 10f;
    public int xpReward = 30;
    public int moneyReward = 0;
    public int requiredKills = 3;

    [Header("Dôsledky u ľudí")]
    [Tooltip("Ktorej ľudskej frakcii klesne dôvera po splnení")]
    public FactionId humanFaction = FactionId.Megacorp;
    public float humanTrustPenalty = 5f;
    [Tooltip("O koľko vzrastie podozrenie mesta")]
    public float suspicion = 10f;

    [Header("Interact / Reach")]
    public float radius = 3f;
    [Tooltip("Ako dlho treba držať / stáť pri cieli")]
    public float holdSeconds = 3f;
    public KeyCode interactKey => KeyBindings.Get("use", KeyCode.F);

    public bool Done { get; private set; }
    /// <summary>Postup 0..1 (pri Destroy podľa zdravia cieľa).</summary>
    public float Progress { get; private set; }

    private Damageable dmg;
    private int nextDay;
    private int civilianKillsAtIssue, soldierKillsAtIssue, destroyedAtIssue;

    void Reset()
    {
        if (action == OrderAction.Destroy && GetComponent<Damageable>() == null)
            gameObject.AddComponent<Damageable>();
    }

    public void BeginTracking()
    {
        civilianKillsAtIssue = Damageable.PlayerCivilianKillCount;
        soldierKillsAtIssue = Damageable.PlayerSoldierKillCount;
        destroyedAtIssue = DemonRaid.Destroyed;
        Progress = 0f;
    }

    void Awake()
    {
        if (action == OrderAction.Destroy)
        {
            dmg = GetComponent<Damageable>();
            if (dmg == null)
            {
                Debug.LogWarning($"OrderTarget '{name}': akcia Destroy potrebuje Damageable, pridávam predvolený.");
                dmg = gameObject.AddComponent<Damageable>();
            }
        }
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    void OnDisable() => All.Remove(this);

    public bool IsAvailable(int day)
    {
        if (Done || !isActiveAndEnabled) return false;
        if (dmg != null && dmg.IsDead) return false;
        return day >= minDay && day >= nextDay;
    }

    public float DistanceTo(Vector3 p)
    {
        Vector3 d = transform.position - p;
        d.y = 0f;
        return d.magnitude;
    }

    /// <summary>Volá ArakaniFavor každý frame, kým je rozkaz aktívny. Vráti true, keď je splnený.</summary>
    public bool Tick(Transform player, float dt)
    {
        switch (action)
        {
            case OrderAction.Destroy:
                if (dmg == null) return false;
                Progress = dmg.maxHealth > 0f ? 1f - Mathf.Clamp01(dmg.Health / dmg.maxHealth) : 0f;
                return dmg.IsDead;

            case OrderAction.KillSoldiers:
                int sk = Damageable.PlayerSoldierKillCount - soldierKillsAtIssue;
                Progress = Mathf.Clamp01((float)sk / Mathf.Max(1, requiredKills));
                return sk >= Mathf.Max(1, requiredKills);

            case OrderAction.DestroyBuildings:
                int db = DemonRaid.Destroyed - destroyedAtIssue;
                Progress = Mathf.Clamp01((float)db / Mathf.Max(1, requiredKills));
                return db >= Mathf.Max(1, requiredKills);

            case OrderAction.KillCivilians:
                int kills = Damageable.PlayerCivilianKillCount - civilianKillsAtIssue;
                Progress = Mathf.Clamp01((float)kills / Mathf.Max(1, requiredKills));
                return kills >= Mathf.Max(1, requiredKills);

            default:
                bool inRange = DistanceTo(player.position) <= radius;
                bool active = inRange && (action == OrderAction.Reach || GameInput.GetKey(interactKey));
                float step = dt / Mathf.Max(0.1f, holdSeconds);

                Progress = Mathf.Clamp01(Progress + (active ? step : -step * 0.5f));
                return Progress >= 1f;
        }
    }

    /// <summary>Rozkaz bol splnený: jednorazový cieľ sa vyradí, opakovateľný sa vráti o deň neskôr.</summary>
    public void MarkDone(int day)
    {
        Progress = 0f;
        if (repeatable && action != OrderAction.Destroy) nextDay = day + 1;
        else Done = true;
    }

    /// <summary>Rozkaz sa skončil bez splnenia - postup sa vynuluje.</summary>
    public void ResetProgress()
    {
        if (action != OrderAction.Destroy) Progress = 0f;
    }

    void OnDrawGizmosSelected()
    {
        if (action == OrderAction.Destroy) return;
        Gizmos.color = new Color(0.8f, 0.3f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}