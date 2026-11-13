using System;
using System.Collections.Generic;
using UnityEngine;

public enum Profession { None, Engineer, Cook, Doctor, Electrician }

/// <summary>
/// Dôvera povolaní civilistov (0–100 za povolanie). Získava sa pomocou civilistom (ProfessionInteraction) a odomyká:
///  - rýchlejšie stavanie (Inžinier až -35 % času, Elektrikár až -15 %),
///  - pokročilé stavby (BuildingDef.requiresProfession),
///  - vylepšenie zbrane (Inžinier 50: +5 poškodenie, Elektrikár 50: +3 poškodenie a dlhší dosah; jednorazovo).
/// </summary>
public static class ProfessionTrust
{
    public static event Action Changed;

    private static readonly float[] trust = new float[5];
    private static bool engineerBonus, electricianBonus;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        Array.Clear(trust, 0, trust.Length);
        engineerBonus = electricianBonus = false;
        Changed = null;
    }

    public static float Get(Profession p) => trust[(int)p];

    public static void Add(Profession p, float delta)
    {
        if (p == Profession.None) return;
        if (delta > 0f) delta *= SoulBalance.TrustGainFactor;
        trust[(int)p] = Mathf.Clamp(trust[(int)p] + delta, 0f, 100f);
        ApplyBonuses(false);
        Changed?.Invoke();
    }

    public static string Name(Profession p)
    {
        switch (p)
        {
            case Profession.Engineer: return "Inžinier";
            case Profession.Cook: return "Kuchár";
            case Profession.Doctor: return "Lekár";
            case Profession.Electrician: return "Elektrikár";
            default: return "Civilista";
        }
    }

    /// <summary>Násobok času stavby (1 = bez zrýchlenia).</summary>
    public static float BuildSpeedFactor => SoulBalance.BuildTimeFactor * (1f - 0.35f * Get(Profession.Engineer) / 100f) * (1f - 0.15f * Get(Profession.Electrician) / 100f);

    static void ApplyBonuses(bool silent)
    {
        if (!engineerBonus && Get(Profession.Engineer) >= 50f)
        {
            engineerBonus = true;
            Upgrade(5f, 0.1f, "Inžinieri ti vylepšili zbraň (+5 poškodenie, +0,1 dosah).", silent);
        }
        if (!electricianBonus && Get(Profession.Electrician) >= 50f)
        {
            electricianBonus = true;
            Upgrade(3f, 0.2f, "Elektrikári ti nabili zbraň (+3 poškodenie, +0,2 dosah).", silent);
        }
    }

    static void Upgrade(float damage, float range, string message, bool silent)
    {
        PlayerCombat combat = UnityEngine.Object.FindAnyObjectByType<PlayerCombat>();
        if (combat != null) combat.UpgradeWeapon(damage, range);
        if (!silent) GameHUD.Message(message, 4f);
    }

    // ---------- Ukladanie ----------
    public static float[] Snapshot() => (float[])trust.Clone();

    public static void Restore(float[] values)
    {
        Array.Clear(trust, 0, trust.Length);
        engineerBonus = electricianBonus = false;
        if (values != null)
            for (int i = 0; i < values.Length && i < trust.Length; i++) trust[i] = Mathf.Clamp(values[i], 0f, 100f);

        ApplyBonuses(true);   // upgrade zbrane sa po načítaní uplatní znova (zbraň si ho v scéne neukladá)
        Changed?.Invoke();
    }
}

/// <summary>Povolanie civilistu. Pridáva sa postavám civilistov a odboja (ZonePopulation); vybrať sa dá len pomoc civilistovi s povolaním.</summary>
public class CivilianProfile : MonoBehaviour
{
    public static readonly List<CivilianProfile> All = new List<CivilianProfile>();

    [SerializeField] private Profession profession;
    private Damageable damage;

    public Profession Profession => profession;
    public float NextHelpTime { get; set; }
    public bool IsAlive => damage == null || !damage.IsDead;

    public void Assign(Profession value) => profession = value;

    void Awake() => damage = GetComponent<Damageable>();
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    /// <summary>Povolanie podľa mena postavy (stále pri každom načítaní): ~40 % bez povolania; odboj má viac inžinierov a elektrikárov.</summary>
    public static Profession Pick(string seed, bool rebel)
    {
        int hash = 17;
        foreach (char c in seed) hash = hash * 31 + c;
        int roll = Mathf.Abs(hash % 100);

        if (roll < 40) return Profession.None;
        roll = (roll - 40) * 100 / 60;   // 0..99 medzi povolaniami

        if (rebel) return roll < 35 ? Profession.Engineer : roll < 70 ? Profession.Electrician : roll < 85 ? Profession.Doctor : Profession.Cook;
        return roll < 25 ? Profession.Engineer : roll < 50 ? Profession.Cook : roll < 75 ? Profession.Doctor : Profession.Electrician;
    }
}

/// <summary>
/// Pomoc civilistovi s povolaním: kláves pri ňom, stojí 1 vhodnú surovinu (bylina pre kuchára/lekára, šrot pre inžiniera/elektrikára)
/// alebo peniaze; zvýši dôveru povolania. Civilista môže byť požiadaný znova až po pauze.
/// </summary>
public class ProfessionInteraction : MonoBehaviour
{
    private KeyCode helpKey => KeyBindings.Get("help", KeyCode.L);
    [SerializeField] private float range = 3.5f;
    [SerializeField] private int moneyCost = 20;
    [SerializeField] private float trustGain = 8f;
    [SerializeField] private float cooldownSeconds = 90f;

    private PlayerStats player;
    private Inventory inventory;
    private CivilianProfile near;
    private GUIStyle hint;

    void Update()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            inventory = player != null ? player.GetComponent<Inventory>() : null;
            return;
        }

        near = player.IsDead || Blocked() ? null : FindNear();
        if (near != null && GameInput.GetKeyDown(helpKey)) Help(near);
    }

    static bool Blocked() => Healer.MenuOpen || PlotManager.MenuOpen;

    CivilianProfile FindNear()
    {
        CivilianProfile best = null;
        float bestDistance = range * range;
        Vector3 p = player.transform.position;

        // Civilisti, ktorí nevznikli cez ZonePopulation (ručne v scéne), dostanú povolanie až keď sú pri hráčovi
        foreach (CivilianAI ai in CivilianAI.All)
        {
            if (ai == null) continue;
            Vector3 d = ai.transform.position - p;
            d.y = 0f;
            if (d.sqrMagnitude >= bestDistance || ai.GetComponent<CivilianProfile>() != null) continue;

            NPCPerception perception = ai.GetComponent<NPCPerception>();
            bool rebel = perception != null && perception.faction == FactionId.Rebels;
            ai.gameObject.AddComponent<CivilianProfile>().Assign(CivilianProfile.Pick(ai.name, rebel));
        }

        foreach (CivilianProfile c in CivilianProfile.All)
        {
            if (c.Profession == Profession.None || !c.IsAlive) continue;

            // Najprv sa postava skúma (T): až potom sa pozná jej povolanie a dá sa jej pomôcť
            NPCPerception seen = c.GetComponent<NPCPerception>();
            if (seen != null && !seen.IsScanned) continue;

            Vector3 d = c.transform.position - p;
            d.y = 0f;
            if (d.sqrMagnitude < bestDistance) { best = c; bestDistance = d.sqrMagnitude; }
        }
        return best;
    }

    static string ItemFor(Profession p) =>
        p == Profession.Cook || p == Profession.Doctor ? ItemCatalog.Herb : ItemCatalog.Scrap;

    void Help(CivilianProfile civilian)
    {
        if (Time.time < civilian.NextHelpTime)
        {
            GameHUD.Message($"{ProfessionTrust.Name(civilian.Profession)} ti teraz nič nepotrebuje.", 2f);
            return;
        }

        string item = ItemFor(civilian.Profession);
        string paid;
        if (inventory != null && inventory.TryRemove(item, 1)) paid = ItemCatalog.Get(item).displayName;
        else if (player.TrySpend(moneyCost)) paid = $"{moneyCost} peňazí";
        else
        {
            GameHUD.Message($"Nemáš čím pomôcť: treba {ItemCatalog.Get(item).displayName} alebo {moneyCost} peňazí.", 3f);
            return;
        }

        civilian.NextHelpTime = Time.time + cooldownSeconds;
        ProfessionTrust.Add(civilian.Profession, trustGain);
        player.AddSoulProgress(0.5f);   // pomoc ľuďom je ľudský skutok
        player.AddXp(15);   // pomoc civilistovi dáva XP
        Reputation.Add(FactionId.Civilians, 1f);
        GameHUD.Message($"Pomohol si ({paid}). {ProfessionTrust.Name(civilian.Profession)}: dôvera {ProfessionTrust.Get(civilian.Profession):0}.", 3.5f);
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (near == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(near.transform.position + Vector3.up * 2.3f);
        if (p.z <= 0f) return;

        if (hint == null)
        {
            hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
            hint.normal.textColor = new Color(0.6f, 0.95f, 1f);
        }
        string item = ItemCatalog.Get(ItemFor(near.Profession)).displayName;
        GUI.Label(new Rect(p.x - 170f, Screen.height - p.y - 14f, 340f, 28f),
            $"[{helpKey}] {ProfessionTrust.Name(near.Profession)} (dôvera {ProfessionTrust.Get(near.Profession):0}) – pomôcť: {item}", hint);
    }
}
