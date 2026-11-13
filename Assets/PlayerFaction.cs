using UnityEngine;

public enum FactionState { None, Stable, Desperate }

/// <summary>
/// Frakcia hráča a jej bonusy. Hráč začína v Megakorporácii. Po smrti od démona sa stane
/// hybridom v klane toho démona (alebo bez klanu). Ďalšie frakcie získa u lekárky za poplatok
/// alebo ďalšou smrťou od démonov iného klanu.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerFaction : MonoBehaviour
{
    [Header("Frakcia")]
    public FactionId current = FactionId.Megacorp;

    [Header("Výsledné násobiče (len na čítanie)")]
    public FactionState state;
    public float SpeedMult { get; private set; } = 1f;
    public float DamageMult { get; private set; } = 1f;
    public float StaminaMult { get; private set; } = 1f;
    public float MaxHpMult { get; private set; } = 1f;

    public FactionId Current => current;
    public bool HasDemonClan { get { var p = FactionData.Get(current); return p != null && p.isDemonClan; } }

    private PlayerStats stats;
    private DemonSenses senses;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        senses = GetComponent<DemonSenses>();
    }

    public void SetFaction(FactionId id)
    {
        if (current == id) return;
        current = id;
        Debug.Log($"FRAKCIA: Patríš do: {FactionData.NameOf(id)}");
    }

    void Update()
    {
        if (stats == null) return;


        FactionMods m = default;
        FactionState newState = FactionState.None;
        FactionProfile p = FactionData.Get(current);

        // Bonusy platia len ak typ frakcie sedí s formou (človek = ľudská frakcia, démon = klan)
        if (p != null && !stats.IsDead && p.isDemonClan == stats.IsDemon)
        {
            if (!stats.IsDemon)
            {
                float hunger = stats.Hunger / stats.maxHunger;
                if (hunger > 0.5f) { newState = FactionState.Stable; m = p.stable; }
                else if (hunger < 0.25f) { newState = FactionState.Desperate; m = p.desperate; }
            }
            else
            {
                float blood = stats.Blood / stats.maxBlood;
                bool clanFocus = senses != null && senses.State == SensesState.Focused
                                 && senses.focus == SenseStat.Faction;

                if (blood < 0.25f && clanFocus) { newState = FactionState.Desperate; m = p.desperate; }
                else if (blood > 0.25f) { newState = FactionState.Stable; m = p.stable; }
            }
        }

        if (newState != state)
        {
            state = newState;
            if (state == FactionState.Desperate)
                Debug.Log($"FRAKČNÝ NÚDZOVÝ STAV: {p.desperateText}");
        }

        SpeedMult = 1f + m.moveSpeed;
        DamageMult = 1f + m.damage;
        StaminaMult = Mathf.Max(0.1f, 1f + m.stamina);
        MaxHpMult = Mathf.Max(0.2f, 1f + m.maxHp);

        // Napojenie na boj (háčiky v PlayerStats)
        stats.damageTakenMultiplier = Mathf.Clamp(1f - m.armor, 0.1f, 2f);
        stats.critChanceBonus = m.crit;
        stats.attackSpeedMultiplier = Mathf.Max(0.2f, 1f + m.attackSpeed);
        stats.lifestealFraction = Mathf.Max(0f, m.lifesteal);
    }
}
