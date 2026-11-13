using System;
using UnityEngine;

public enum SenseStat { Strength, Speed, Stamina, Faction }   // Faction = pripravené pre budúce frakcie démonov
public enum SensesState { Off, Full, Focused }

/// <summary>
/// Vylepšené zmysly démona + krvavé videnie.
///  - Krv nad hranicou: zmysly dajú bonus na rýchlosť, silu aj výdrž, krv však ubúda rýchlejšie.
///  - Krv pod hranicou: zmysly fungujú len na JEDNU zvolenú schopnosť, ostatné staty dostanú väčší nerf.
///  - Krv pod ~10 %: automaticky sa zapne krvavé videnie, ktoré zvýrazní zdroje krvi.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class DemonSenses : MonoBehaviour
{
    public static event Action<bool> OnBloodVisionChanged;

    public KeyCode toggleKey => KeyBindings.Get("senses", KeyCode.V);   // 1 / 2 / 3 = voľba zamerania
    [Header("Ovládanie")]

    [Header("Hranice krvi (v % maxima)")]
    [Range(0f, 100f)] public float fullSensesAbove = 25f;
    [Range(0f, 100f)] public float bloodVisionBelow = 10f;
    [Range(0f, 100f)] public float bloodVisionOffAbove = 15f;

    [Header("Plné zmysly (krv nad hranicou)")]
    public float fullSpeed = 1.3f;
    public float fullStrength = 1.4f;
    public float fullStamina = 1.5f;
    public float fullBloodDrain = 2.5f;

    [Header("Zameranie (krv pod hranicou)")]
    public SenseStat focus = SenseStat.Strength;
    public float focusedBoost = 1.6f;
    public float focusedPenalty = 0.5f;
    public float focusedBloodDrain = 1.5f;

    [Header("Krvavé videnie")]
    public float bloodVisionRange = 60f;

    public SensesState State { get; private set; }
    public bool BloodVisionActive { get; private set; }

    public float SpeedMultiplier => Get(SenseStat.Speed, fullSpeed);
    public float StrengthMultiplier => Get(SenseStat.Strength, fullStrength);
    public float StaminaMultiplier => Get(SenseStat.Stamina, fullStamina);
    public float BloodDrainMultiplier =>
        State == SensesState.Off ? 1f : (State == SensesState.Full ? fullBloodDrain : focusedBloodDrain);

    private PlayerStats stats;
    private PlayerFaction faction;
    private DayNightCycle dayNight;
    private bool sensesOn;

    /// <summary>Zmysly sú zapnuté (1–4 vtedy vyberajú zameranie, nie lektvary).</summary>
    public bool SensesOn => sensesOn;
    private float nearestBlood = -1f;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        faction = GetComponent<PlayerFaction>();
        dayNight = FindAnyObjectByType<DayNightCycle>();
    }

    void OnEnable() => PlayerStats.OnFormChanged += HandleFormChanged;
    void OnDisable() => PlayerStats.OnFormChanged -= HandleFormChanged;

    void HandleFormChanged(PlayerForm form)
    {
        if (form == PlayerForm.Demon) return;

        // Ľudia nemajú démonické zmysly
        sensesOn = false;
        State = SensesState.Off;
        SetBloodVision(false);
    }

    float Get(SenseStat stat, float fullValue)
    {
        switch (State)
        {
            case SensesState.Off: return 1f;
            case SensesState.Full: return fullValue;
            default:
                // Zameranie na klan: nerfy určuje klan (FactionData), nie všeobecný postih
                if (focus == SenseStat.Faction) return 1f;
                return focus == stat ? focusedBoost : focusedPenalty;
        }
    }

    void Update()
    {
        if (stats == null) return;

        if (!stats.IsDemon || stats.IsDead)
        {
            sensesOn = false;
            State = SensesState.Off;
            SetBloodVision(false);
            return;
        }

        float pct = stats.Blood / stats.maxBlood * 100f;

        // Krvavé videnie (s hysteréziou, aby neblikalo)
        if (!BloodVisionActive && pct < bloodVisionBelow) SetBloodVision(true);
        else if (BloodVisionActive && pct > bloodVisionOffAbove) SetBloodVision(false);

        // Zapnutie / vypnutie zmyslov
        if (GameInput.GetKeyDown(toggleKey) && stats.Blood > 0f)
            sensesOn = !sensesOn;
        if (stats.Blood <= 0f)
            sensesOn = false;

        // Voľba zamerania (nie keď je otvorené menu lekárky - tam 1/2/3 vyberajú frakciu)
        if (!Healer.MenuOpen)
        {
            if (GameInput.GetKeyDown(KeyCode.Alpha1)) SetFocus(SenseStat.Strength);
            if (GameInput.GetKeyDown(KeyCode.Alpha2)) SetFocus(SenseStat.Speed);
            if (GameInput.GetKeyDown(KeyCode.Alpha3)) SetFocus(SenseStat.Stamina);

            if (GameInput.GetKeyDown(KeyCode.Alpha4))
            {
                if (faction != null && faction.HasDemonClan) SetFocus(SenseStat.Faction);
                else Debug.Log("Nepatríš do žiadneho démonického klanu.");
            }
        }

        // Ak klan stratíš, zameranie sa vráti na silu
        if (focus == SenseStat.Faction && (faction == null || !faction.HasDemonClan))
            focus = SenseStat.Strength;

        SensesState newState = !sensesOn ? SensesState.Off
            : (pct > fullSensesAbove ? SensesState.Full : SensesState.Focused);

        if (newState != State)
        {
            State = newState;
            switch (State)
            {
                case SensesState.Off:
                    Debug.Log("ZMYSLY: vypnuté."); break;
                case SensesState.Full:
                    Debug.Log("ZMYSLY: plné (bonus na všetko, krv ubúda rýchlo)."); break;
                case SensesState.Focused:
                    Debug.Log($"ZMYSLY: málo krvi - len zameranie na {StatName(focus)}, ostatné slabnú."); break;
            }
        }

        UpdateNearestBlood();
    }

    void SetFocus(SenseStat stat)
    {
        if (focus == stat) return;
        focus = stat;
        Debug.Log($"Zameranie zmyslov: {StatName(stat)}");
    }

    void SetBloodVision(bool value)
    {
        if (BloodVisionActive == value) return;
        BloodVisionActive = value;

        if (dayNight != null) dayNight.SetBloodVision(value);

        Debug.Log(value
            ? "KRVAVÉ VIDENIE: Zmysly sa zameriavajú na krv..."
            : "Krvavé videnie sa vypína.");

        OnBloodVisionChanged?.Invoke(value);
    }

    void UpdateNearestBlood()
    {
        nearestBlood = -1f;
        if (!BloodVisionActive) return;

        float best = float.MaxValue;
        foreach (BloodSource s in BloodSource.All)
        {
            if (s == null || s.IsDead) continue;
            float d = Vector3.Distance(transform.position, s.transform.position);
            if (d < best) best = d;
        }
        if (best <= bloodVisionRange) nearestBlood = best;
    }

    public static string StatName(SenseStat s)
    {
        switch (s)
        {
            case SenseStat.Strength: return "SILU";
            case SenseStat.Speed: return "RÝCHLOSŤ";
            case SenseStat.Stamina: return "VÝDRŽ";
            default: return "FRAKČNÚ SCHOPNOSŤ";
        }
    }

    // ---------- Značky krvi vo svete (panel kreslí GameHUD) ----------
    public float NearestBloodDistance => nearestBlood;

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (stats == null || !stats.IsDemon || stats.IsDead) return;
        if (BloodVisionActive) DrawBloodMarkers();
    }

    void DrawBloodMarkers()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        var centered = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        Color prev = GUI.color;

        foreach (BloodSource s in BloodSource.All)
        {
            if (s == null || s.IsDead) continue;

            float d = Vector3.Distance(transform.position, s.transform.position);
            if (d > bloodVisionRange) continue;

            Vector3 p = cam.WorldToScreenPoint(s.transform.position + Vector3.up * 3.1f);
            if (p.z <= 0f) continue;

            float sx = p.x;
            float sy = Screen.height - p.y;

            GUI.color = new Color(0.9f, 0.1f, 0.15f);
            GUI.DrawTexture(new Rect(sx - 6f, sy - 6f, 12f, 12f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(sx - 60f, sy + 6f, 120f, 20f), $"{s.displayName} {d:0} m", centered);
        }

        GUI.color = prev;
    }
}
