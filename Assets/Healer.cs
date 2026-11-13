using UnityEngine;

/// <summary>
/// Doktorka Yumi: za poplatok vylieči démona späť na človeka a opýta sa, do ktorej ľudskej
/// frakcie chceš patriť. Človeku vie za poplatok zmeniť frakciu.
/// (Neskôr sa poplatok rozšíri o vzácny materiál.)
/// </summary>
[RequireComponent(typeof(NPCPerception))]
public class Healer : MonoBehaviour
{
    private static bool healerMenuOpen;
    /// <summary>Otvorené akékoľvek menu (lekárka, rozhodnutie detektívky, šatník, zatknutie) - blokuje boj a schopnosti.</summary>
    public static bool MenuOpen => healerMenuOpen || PropagandaMenu.MenuOpen || MerchantNPC.MenuOpen || StoryNpc.MenuOpen || DetectiveSystem.MenuOpen || ArakaniFavor.MenuOpen || GameHUD.MenuOpen || Wardrobe.MenuOpen || PlotManager.MenuOpen || DemonNatureMenu.MenuOpen || WorkshopMenu.MenuOpen || PauseMenu.IsOpen || CombatQte.Active || JailManager.MenuOpen || JailManager.IsInJail || Inventory.MenuOpen || PlayerAppearance.MenuOpen || MangaPanels.Active;

    [Header("Ceny")]
    public int cost = 50;                 // očistný rituál (démon -> človek)
    public int factionChangeCost = 30;    // zmena frakcie ako človek

    [Header("Interakcia")]
    public float interactRange = 3f;
    public KeyCode interactKey => KeyBindings.Get("use", KeyCode.F);

    private PlayerStats player;
    private bool inRange;
    private bool isOpen;
    private string message;
    private float messageUntil;

    void Start()
    {
        player = FindAnyObjectByType<PlayerStats>();
    }

    void OnDisable() => Close();
    void OnDestroy() => Close();

    // Civilisti majú -20 % u lekárky (Master Trader)
    int Price
    {
        get
        {
            int price = player != null && player.IsDemon ? cost : factionChangeCost;
            PlayerFaction f = player != null ? player.GetComponent<PlayerFaction>() : null;
            if (f != null && f.Current == FactionId.Civilians) price = Mathf.RoundToInt(price * 0.8f);
            return price;
        }
    }

    void Update()
    {
        if (player == null || player.IsDead)
        {
            inRange = false;
            Close();
            return;
        }

        inRange = Vector3.Distance(transform.position, player.transform.position) <= interactRange;
        if (!inRange)
        {
            Close();
            return;
        }

        if (GameInput.GetKeyDown(interactKey))
        {
            if (isOpen) Close(); else Open();
            return;
        }

        if (!isOpen)
        {
            string what = player.IsDemon ? "očistný rituál a výber frakcie" : "zmena frakcie";
            GameHUD.Prompt($"[{interactKey}] Doktorka Yumi: {what} ({Price} peňazí)");
            return;
        }

        if (GameInput.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (GameInput.GetKeyDown(KeyCode.Alpha1)) Choose(FactionId.Megacorp);
        else if (GameInput.GetKeyDown(KeyCode.Alpha2)) Choose(FactionId.Rebels);
        else if (GameInput.GetKeyDown(KeyCode.Alpha3)) Choose(FactionId.Civilians);
    }

    void Open()
    {
        isOpen = true;
        healerMenuOpen = true;
    }

    void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        healerMenuOpen = false;
    }

    void Choose(FactionId faction)
    {
        if (player.TryCureAtHealer(Price, faction))
        {
            Debug.Log($"Lekárka ťa prijala: {FactionData.NameOf(faction)}.");
            Close();
        }
        else
        {
            message = player.money < Price ? $"Nemáš dosť peňazí ({Price})." : "Do tejto frakcie už patríš.";
            messageUntil = Time.time + 2.5f;
        }
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!inRange || player == null) return;

        float cx = Screen.width / 2f;

        if (!isOpen) return;

        Rect box = new Rect(cx - 190f, Screen.height / 2f - 100f, 380f, 200f);
        GUI.Box(box, "Doktorka Yumi");

        float y = box.y + 28f;
        GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 22f),
            player.IsDemon ? $"Očistný rituál ťa vráti k ľuďom. Cena: {Price} (máš {player.money})"
                           : $"Zmena frakcie. Cena: {Price} (máš {player.money})");
        y += 26f;
        GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 22f), "Kam chceš patriť?");
        y += 24f;
        GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 22f), $"[1] {FactionData.NameOf(FactionId.Megacorp)}");
        y += 22f;
        GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 22f), $"[2] {FactionData.NameOf(FactionId.Rebels)}");
        y += 22f;
        GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 22f), $"[3] {FactionData.NameOf(FactionId.Civilians)}");
        y += 26f;
        GUI.Label(new Rect(box.x + 12f, y, box.width - 24f, 22f), $"[{interactKey} / Esc] Zrušiť");

        if (Time.time < messageUntil)
            GUI.Label(new Rect(box.x + 12f, box.yMax - 26f, box.width - 24f, 22f), message);
    }
}