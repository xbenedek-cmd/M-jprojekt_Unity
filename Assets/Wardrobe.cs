using UnityEngine;

public enum OutfitId { Civilian, Officer, Underworld, Worker }
public enum OutfitRule { Welcome, Tolerated, Forbidden }

/// <summary>
/// Oblečenie hráča (kláves O). Každá časť mesta (CityDistrict, vojenská zóna) má pravidlá:
/// správne oblečenie je vítané (ľudia si ťa všímajú menej), nesprávne je zakázané
/// (vojaci a civilisti ťa odhalia rýchlejšie). Ako démon je vo vojenskej zóne podozrenie ešte väčšie.
/// Kocka hráča mení farbu podľa oblečenia (a mierne sa zafarbí, keď si démon).
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class Wardrobe : MonoBehaviour
{
    public static Wardrobe Instance { get; private set; }
    public static bool MenuOpen { get; private set; }

    public KeyCode menuKey => KeyBindings.Get("wardrobe", KeyCode.O);
    [Header("Ovládanie")]

    [Header("Oblečenie")]
    public OutfitId current = OutfitId.Civilian;
    [Tooltip("Uniforma sa odomkne aj sama, ak si v Megakorporácii. Inak ju treba zohnať (čierny trh, dielňa).")]
    public bool unlockedOfficer;
    [Tooltip("Ulično-podsvetný odev sa odomkne aj sám, ak si medzi Rebelantmi.")]
    public bool unlockedUnderworld;

    [Header("Podozrenie (násobiče rýchlosti odhalenia)")]
    public float welcomeMultiplier = 0.5f;
    public float toleratedMultiplier = 1f;
    public float forbiddenMultiplier = 2.2f;
    [Tooltip("Ako démon vo vojenskej zóne ťa skenery a vojaci odhaľujú rýchlejšie")]
    public float demonInMilitaryMultiplier = 2.2f;
    [Tooltip("Pri tomto podozrení mesta ťa vojaci a civilisti sledujú všade (hľadaný)")]
    public float wantedSuspicion = 50f;

    [Header("Vzhľad kocky")]
    public bool tintPlayer = true;
    public Color demonTint = new Color(0.6f, 0.1f, 0.25f);
    [Range(0f, 1f)] public float demonTintAmount = 0.35f;

    [Header("Debug")]

    public DistrictType District { get; private set; }

    public OutfitRule CurrentRule => RuleFor(District, current);

    private PlayerStats stats;
    private PlayerFaction faction;
    private Renderer[] renderers;
    private float nextDistrictCheck;
    private PlayerForm lastForm;
    private OutfitId lastOutfit = (OutfitId)(-1);
    private OutfitId colorOutfit = (OutfitId)(-1);
    private DistrictType lastDistrict = (DistrictType)(-1);
    private OutfitRule lastRule = (OutfitRule)(-1);
    private bool lastDemonWarn;

    private GUIStyle label, bold;

    // ---------- Údaje o oblečení ----------
    public static string OutfitName(OutfitId o)
    {
        switch (o)
        {
            case OutfitId.Officer:     return "Uniforma Megakorporácie";
            case OutfitId.Underworld:  return "Odev z podsvetia";
            case OutfitId.Worker:      return "Pracovný overal";
            default:                   return "Civilné oblečenie";
        }
    }

    public static Color OutfitColor(OutfitId o)
    {
        switch (o)
        {
            case OutfitId.Officer:     return new Color(0.2f, 0.45f, 0.25f);   // olivová
            case OutfitId.Underworld:  return new Color(0.4f, 0.2f, 0.55f);    // fialovo-čierna
            case OutfitId.Worker:      return new Color(0.95f, 0.6f, 0.15f);   // oranžový overal
            default:                   return new Color(0.55f, 0.65f, 0.8f);   // civil
        }
    }

    public static string RuleText(OutfitRule r)
    {
        switch (r)
        {
            case OutfitRule.Welcome:   return "vítané";
            case OutfitRule.Forbidden: return "ZAKÁZANÉ";
            default:                   return "tolerované";
        }
    }

    /// <summary>Pravidlá oblečenia v jednotlivých častiach mesta.</summary>
    public static OutfitRule RuleFor(DistrictType d, OutfitId o)
    {
        switch (d)
        {
            case DistrictType.Residential:
                return (o == OutfitId.Civilian || o == OutfitId.Worker) ? OutfitRule.Welcome : OutfitRule.Tolerated;

            case DistrictType.Military:
                return o == OutfitId.Officer ? OutfitRule.Welcome : OutfitRule.Forbidden;

            case DistrictType.Underworld:
                if (o == OutfitId.Underworld) return OutfitRule.Welcome;
                if (o == OutfitId.Officer) return OutfitRule.Forbidden;     // vojak v rebelskej štvrti
                return OutfitRule.Tolerated;

            case DistrictType.Industrial:
                return o == OutfitId.Worker ? OutfitRule.Welcome : OutfitRule.Tolerated;

            default:    // Ruins a mimo mesta: nikto nesleduje
                return OutfitRule.Welcome;
        }
    }

    /// <summary>
    /// Koľkokrát rýchlejšie ťa pozorovatelia (vojaci, civilisti) odhalia vďaka oblečeniu a forme.
    /// 1 = normálne, menej = splývaš, viac = vyčnievaš.
    /// </summary>
    /// <summary>
    /// Vyčnieva hráč práve teraz? (zakázané oblečenie, démon vo vojenskej zóne alebo ho mesto hľadá).
    /// Vojaci a civilisti ho len vtedy sledujú; obyčajného občana si nevšímajú.
    /// </summary>
    public static bool IsSuspicious
    {
        get
        {
            Wardrobe w = Instance;
            if (w == null) return false;

            if (w.CurrentRule == OutfitRule.Forbidden) return true;
            if (w.stats != null && w.stats.IsDemon && w.District == DistrictType.Military) return true;
            return Reputation.Suspicion >= w.wantedSuspicion;
        }
    }

    public static float SuspicionMultiplier()
    {
        Wardrobe w = Instance;
        if (w == null) return 1f;

        float m;
        switch (w.CurrentRule)
        {
            case OutfitRule.Welcome:   m = w.welcomeMultiplier; break;
            case OutfitRule.Forbidden: m = w.forbiddenMultiplier; break;
            default:                   m = w.toleratedMultiplier; break;
        }

        if (w.stats != null && w.stats.IsDemon && w.District == DistrictType.Military)
            m *= w.demonInMilitaryMultiplier;

        // Hľadaného hráča si všímajú aj v správnom oblečení
        if (Reputation.Suspicion >= w.wantedSuspicion) m = Mathf.Max(m, 1.5f);

        return m * Lair.SuspicionFactor;
    }

    public bool IsUnlocked(OutfitId o)
    {
        switch (o)
        {
            case OutfitId.Officer:
                return unlockedOfficer || (faction != null && faction.Current == FactionId.Megacorp);
            case OutfitId.Underworld:
                return unlockedUnderworld || (faction != null && faction.Current == FactionId.Rebels);
            default:
                return true;
        }
    }

    /// <summary>Odomkne oblečenie (obchod, dielňa v úkryte, čierny trh).</summary>
    public void Unlock(OutfitId o)
    {
        if (o == OutfitId.Officer) unlockedOfficer = true;
        else if (o == OutfitId.Underworld) unlockedUnderworld = true;
    }

    // ---------- Životný cyklus ----------
    void OnEnable() => Instance = this;

    void OnDisable()
    {
        if (Instance == this) Instance = null;
        MenuOpen = false;
    }

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        faction = GetComponent<PlayerFaction>();

        renderers = GetComponents<Renderer>();
        if (renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();

        lastForm = stats.CurrentForm;
        ApplyColor();
    }

    void Update()
    {
        if (stats == null) return;

        if (stats.IsDead)
        {
            MenuOpen = false;
            return;
        }


        if (MenuOpen)
        {
            MenuInput();
        }
        else if (GameInput.GetKeyDown(menuKey) && !Healer.MenuOpen)
        {
            MenuOpen = true;
        }

        if (Time.time >= nextDistrictCheck)
        {
            nextDistrictCheck = Time.time + 0.25f;
            District = CityDistrict.TypeAt(transform.position);
            AnnounceChanges();
        }

        if (stats.CurrentForm != lastForm || current != colorOutfit)
        {
            lastForm = stats.CurrentForm;
            ApplyColor();
        }
    }

    void MenuInput()
    {
        if (GameInput.GetKeyDown(KeyCode.Escape) || GameInput.GetKeyDown(menuKey))
        {
            MenuOpen = false;
            return;
        }

        if (GameInput.GetKeyDown(KeyCode.Alpha1)) TrySet(OutfitId.Civilian);
        else if (GameInput.GetKeyDown(KeyCode.Alpha2)) TrySet(OutfitId.Officer);
        else if (GameInput.GetKeyDown(KeyCode.Alpha3)) TrySet(OutfitId.Underworld);
        else if (GameInput.GetKeyDown(KeyCode.Alpha4)) TrySet(OutfitId.Worker);
    }

    void TrySet(OutfitId o)
    {
        if (!IsUnlocked(o))
        {
            GameHUD.Message("Toto oblečenie nemáš. Zožeň ho (čierny trh, dielňa v úkryte).", 3.5f);
            return;
        }

        current = o;
        GameHUD.Message($"Oblečenie: {OutfitName(o)}", 2.5f);
        MenuOpen = false;
    }

    // Hlásenia pri zmene štvrte, oblečenia alebo formy
    void AnnounceChanges()
    {
        OutfitRule rule = CurrentRule;
        bool demonWarn = stats.IsDemon && District == DistrictType.Military;

        bool districtChanged = District != lastDistrict;
        bool ruleChanged = rule != lastRule || current != lastOutfit;

        if (districtChanged && lastDistrict != (DistrictType)(-1))
            GameHUD.Message(CityDistrict.NameOf(District), 2.5f);

        if ((districtChanged || ruleChanged) && rule == OutfitRule.Forbidden && District != DistrictType.None)
            GameHUD.Message("Tu v tomto oblečení nepatríš! Prezleč sa (O) alebo sa zakrádaj.", 4f);

        if (demonWarn && (!lastDemonWarn || districtChanged))
            GameHUD.Message("Vojenská zóna: ako démon si tu veľmi podozrivý!", 4f);

        lastDemonWarn = demonWarn;
        lastDistrict = District;
        lastRule = rule;
        lastOutfit = current;
    }

    void ApplyColor()
    {
        colorOutfit = current;
        if (!tintPlayer || renderers == null) return;

        Color c = OutfitColor(current);
        if (stats != null && stats.IsDemon) c = Color.Lerp(c, demonTint, demonTintAmount);

        foreach (Renderer r in renderers)
            if (r != null) r.material.color = c;
    }

    // ---------- Menu ----------
    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!MenuOpen) return;

        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            bold = new GUIStyle(label) { fontStyle = FontStyle.Bold };
        }

        float s = Mathf.Clamp(Screen.height / 720f, 0.85f, 1.6f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        float vh = Screen.height / s;

        Rect r = new Rect(vw / 2f - 270f, vh / 2f - 140f, 540f, 280f);

        GUI.color = new Color(0.9f, 0.9f, 0.9f, 0.95f);
        GUI.DrawTexture(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), Texture2D.whiteTexture);
        GUI.color = new Color(0.07f, 0.07f, 0.1f, 0.96f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(r.x + 14f, r.y + 8f, r.width - 28f, 24f),
            $"Šatník - teraz si v: {CityDistrict.NameOf(District)}", bold);

        float y = r.y + 40f;
        OutfitId[] all = { OutfitId.Civilian, OutfitId.Officer, OutfitId.Underworld, OutfitId.Worker };

        for (int i = 0; i < all.Length; i++)
        {
            OutfitId o = all[i];
            bool unlocked = IsUnlocked(o);
            OutfitRule rule = RuleFor(District, o);

            string line = $"[{i + 1}] {(o == current ? "> " : "")}{OutfitName(o)}";
            line += unlocked ? $"   -  tu: {RuleText(rule)}" : "   -  (nemáš)";

            Color col = !unlocked ? new Color(1f, 1f, 1f, 0.35f)
                      : rule == OutfitRule.Forbidden ? new Color(1f, 0.5f, 0.4f)
                      : rule == OutfitRule.Welcome ? new Color(0.55f, 1f, 0.6f) : Color.white;

            // farebný štvorček s farbou oblečenia
            GUI.color = OutfitColor(o);
            GUI.DrawTexture(new Rect(r.x + 14f, y + 3f, 14f, 14f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.contentColor = col;
            GUI.Label(new Rect(r.x + 36f, y, r.width - 50f, 24f), line, label);
            GUI.contentColor = Color.white;
            y += 30f;
        }

        y += 6f;
        GUI.Label(new Rect(r.x + 14f, y, r.width - 28f, 40f),
            "Vojenská zóna chce uniformu, podsvetie ich odev, továrne overal. Správne oblečenie ťa chráni pred odhalením.",
            label);

        GUI.Label(new Rect(r.x + 14f, r.yMax - 28f, r.width - 28f, 22f), $"[{menuKey} / Esc] Zavrieť", label);

        GUI.matrix = Matrix4x4.identity;
        GUI.color = Color.white;
    }
}
