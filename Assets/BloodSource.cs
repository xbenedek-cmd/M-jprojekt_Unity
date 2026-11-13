using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zdroj krvi pre hráča-démona: civilista alebo zviera.
/// Démoni tento komponent NEMAJÚ, preto sa z nich krv piť nedá.
/// Krv je vždy konečná: zdroj sa dá vypiť len do dna, mŕtvola (zabitá zbraňou) má len polovicu krvi
/// a po čase sa krv zrazí. Zvieratá dávajú hráčovi menej krvi než ľudia.
/// </summary>
public class BloodSource : MonoBehaviour
{
    public string displayName = "Civilista";
    [Tooltip("Koľko krvi sa dá zo zdroja vypiť, ak je živý")]
    public float bloodValue = 40f;
    [Tooltip("Skúsenosti za úplné vypitie")]
    public int xpValue = 25;

    [Header("Zvieratá a mŕtvoly")]
    [Tooltip("Zviera dá hráčovi len tento podiel vypitej krvi (1 = ako človek)")]
    [Range(0.1f, 1f)] public float animalBloodYield = 0.5f;
    [Tooltip("Koľko krvi zostane v tele, ak zdroj zabila zbraň (nie vypitie)")]
    [Range(0f, 1f)] public float corpseBloodFraction = 0.5f;
    [Tooltip("Po koľkých sekundách od smrti sa krv v mŕtvole zrazí a už sa nedá piť")]
    public float corpseBloodSeconds = 45f;

    // Zoznam všetkých zdrojov krvi (využíva krvavé videnie)
    public static readonly List<BloodSource> All = new List<BloodSource>();

    private Damageable damageable;
    private bool isAnimal;
    private float diedAt = -1f;

    /// <summary>Koľko krvi v zdroji ešte zostalo.</summary>
    public float Remaining { get; private set; }
    /// <summary>Koľko krvi mal zdroj na začiatku pitia (živý = bloodValue, mŕtvola = menej).</summary>
    public float Capacity { get; private set; }
    public bool IsDead { get; private set; }
    public bool Coagulated { get; private set; }
    public bool BeingDrunk { get; set; }

    public bool IsAnimal => isAnimal;
    /// <summary>Podiel vypitej krvi, ktorý sa naozaj dostane k hráčovi.</summary>
    public float GainFactor => isAnimal ? animalBloodYield : 1f;
    /// <summary>Zdroj zabila zbraň (nie vypitie).</summary>
    public bool IsCorpse => damageable != null && damageable.IsDead;

    void Awake()
    {
        Remaining = bloodValue;
        Capacity = bloodValue;
        damageable = GetComponent<Damageable>();
        isAnimal = GetComponent<NPCPerception>() == null;
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        if (damageable != null) damageable.OnDied += HandleKilled;
    }

    void OnDisable()
    {
        All.Remove(this);
        if (damageable != null) damageable.OnDied -= HandleKilled;
    }

    // Zabitie zbraňou: v tele zostane len časť krvi a čoskoro sa zrazí
    void HandleKilled()
    {
        if (IsDead) return;

        diedAt = Time.time;
        Remaining *= corpseBloodFraction;
        Capacity = Remaining;

        if (Remaining <= 0.01f) Coagulate();
    }

    void Update()
    {
        if (IsDead || diedAt < 0f) return;
        if (Time.time - diedAt > corpseBloodSeconds) Coagulate();
    }

    void Coagulate()
    {
        Remaining = 0f;
        IsDead = true;
        Coagulated = true;
        BeingDrunk = false;
    }

    /// <summary>Odoberie krv. Keď je zdroj prázdny, zomrie.</summary>
    public void Drain(float amount)
    {
        if (IsDead) return;

        Remaining = Mathf.Max(0f, Remaining - amount);
        if (Remaining <= 0f) Die();
    }

    void Die()
    {
        IsDead = true;
        BeingDrunk = false;

        NPCWander wander = GetComponent<NPCWander>();
        if (wander != null) wander.enabled = false;

        // Vysatý zomrie naozaj (nie je to len ležiaca kulisa, ktorú treba ešte udrieť)
        if (damageable != null)
        {
            if (!damageable.IsDead) damageable.ForceKill(true);
        }
        else
        {
            LieDown();
        }
        Debug.Log($"{displayName} je vysatý/á a mŕtvy/a.");
    }

    void LieDown() => Corpse.LayDown(transform);

    // Zvieratá nemajú NPCPerception (ten kreslí popisok), tak označíme mŕtve telo tu
    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!NpcOverlay.Visible(transform.position)) return;
        bool drained = IsDead;
        bool killed = damageable != null && damageable.IsDead;
        if ((!drained && !killed) || GetComponent<NPCPerception>() != null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.4f);
        if (p.z <= 0f) return;

        string state = Coagulated ? "(krv zrazená)" : (drained ? "(vysaté)" : "(mŕtve)");

        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(p.x - 80f, Screen.height - p.y - 10f, 160f, 20f),
            $"[{displayName}] {state}", style);
    }
}