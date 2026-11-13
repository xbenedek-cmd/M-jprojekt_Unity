using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Špeciálne predmety obchodníkov v Centre mesta: šesť rôznych vecí, každú predáva iný obchodník a každá robí niečo iné.
/// Idú postupne od najlacnejšej: ďalšia sa odomkne, až keď kúpiš všetky lacnejšie (na najdrahšiu treba všetkých päť predchádzajúcich).
/// Stav sa ukladá s osadou.
/// </summary>
public static class SpecialGoods
{
    public class Def
    {
        public string seller, name, effect;
        public int price;
    }

    public static readonly Def[] Items =
    {
        new Def { seller = "Lekárnik", name = "Bylinný elixír", price = 120, effect = "+25 maximálneho zdravia." },
        new Def { seller = "Kováč", name = "Majstrov brúsny kameň", price = 220, effect = "+10 % poškodenia." },
        new Def { seller = "Zásobovač", name = "Cestovná torba", price = 380, effect = "+15 % efektivity výdrže." },
        new Def { seller = "Krvavý obchodník", name = "Fľaša zásob", price = 600, effect = "Hlad a krv ubúdajú o 25 % pomalšie." },
        new Def { seller = "Inžinier", name = "Prenosný kondenzátor", price = 900, effect = "Generátor spotrebuje o 15 % menej kryštálov." },
        new Def { seller = "Starý majster", name = "Talizman Prvého popola", price = 4500, effect = "+20 % poškodenia, −20 % prijatého poškodenia, +10 % rýchlosti." }
    };

    private static readonly bool[] owned = new bool[6];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { for (int i = 0; i < owned.Length; i++) owned[i] = false; }

    public static bool Owns(int index) => index >= 0 && index < owned.Length && owned[index];

    public static int OwnedCount
    {
        get { int n = 0; foreach (bool b in owned) if (b) n++; return n; }
    }

    /// <summary>Prečo sa predmet zatiaľ nedá kúpiť (null = dá sa).</summary>
    public static string LockReason(int index)
    {
        if (Owns(index)) return "už máš";
        for (int i = 0; i < index; i++)
            if (!owned[i]) return index == Items.Length - 1 ? $"najprv kúp všetkých {index} lacnejších predmetov (chýba: {Items[i].name})" : $"najprv kúp: {Items[i].name}";
        return null;
    }

    public static bool TryBuy(int index, PlayerStats player, int price, out string message)
    {
        message = LockReason(index);
        if (message != null) return false;
        if (!player.TrySpend(price)) { message = "Nemáš dosť peňazí."; return false; }

        owned[index] = true;
        message = $"Kúpil si: {Items[index].name}. {Items[index].effect}";
        return true;
    }

    // ---------- Efekty (volajú ich PlayerStats a Power) ----------
    public static float MaxHpBonus => owned[0] ? 25f : 0f;
    public static float DamageFactor => (owned[1] ? 1.1f : 1f) * (owned[5] ? 1.2f : 1f);
    public static float StaminaFactor => owned[2] ? 1.15f : 1f;
    public static float NeedsDrainFactor => owned[3] ? 0.75f : 1f;
    public static float PowerDemandFactor => owned[4] ? 0.85f : 1f;
    public static float TakenFactor => owned[5] ? 0.8f : 1f;
    public static float SpeedFactor => owned[5] ? 1.1f : 1f;

    // ---------- Ukladanie ----------
    public static int Export()
    {
        int mask = 0;
        for (int i = 0; i < owned.Length; i++) if (owned[i]) mask |= 1 << i;
        return mask;
    }

    public static void Import(int mask)
    {
        for (int i = 0; i < owned.Length; i++) owned[i] = (mask & (1 << i)) != 0;
    }
}

/// <summary>
/// Kupec: vykupuje od hráča suroviny za dynamické ceny. Každý predaný kus cenu zníži, cena sa potom po dňoch pomaly vracia hore
/// a môže presiahnuť základnú až po strop (zlato 200, čierny kryštál 400...). Zlato a čierny kryštál sa obnovujú veľmi pomaly, preto je ich strop vyšší.
/// </summary>
public static class Buyer
{
    public class Entry
    {
        public string id;
        public int basePrice, cap;
        public float recoverPerDay;   // podiel základnej ceny, o ktorý cena denne stúpne
        public float price;
    }

    private const float DropPerUnit = 0.965f;   // každý predaný kus zníži cenu o 3,5 %
    private const float FloorShare = 0.2f;      // cena neklesne pod 20 % základnej

    public static readonly Entry[] Entries =
    {
        new Entry { id = ItemCatalog.Gold, basePrice = 100, cap = 200, recoverPerDay = 0.06f },
        new Entry { id = ItemCatalog.Rifle, basePrice = 90, cap = 180, recoverPerDay = 0.05f },
        new Entry { id = ItemCatalog.BlackCrystal, basePrice = 150, cap = 400, recoverPerDay = 0.03f },
        new Entry { id = ItemCatalog.Crystal, basePrice = 40, cap = 90, recoverPerDay = 0.08f },
        new Entry { id = ItemCatalog.Herb, basePrice = 10, cap = 24, recoverPerDay = 0.12f },
        new Entry { id = ItemCatalog.Scrap, basePrice = 8, cap = 16, recoverPerDay = 0.12f },
        new Entry { id = ItemCatalog.Wood, basePrice = 6, cap = 12, recoverPerDay = 0.12f },
        new Entry { id = ItemCatalog.Stone, basePrice = 6, cap = 12, recoverPerDay = 0.12f },
        new Entry { id = ItemCatalog.HealthPotion, basePrice = 12, cap = 20, recoverPerDay = 0.1f },
        new Entry { id = ItemCatalog.StaminaPotion, basePrice = 10, cap = 18, recoverPerDay = 0.1f },
        new Entry { id = ItemCatalog.BloodVial, basePrice = 15, cap = 26, recoverPerDay = 0.1f },
        new Entry { id = ItemCatalog.Vegetables, basePrice = 6, cap = 12, recoverPerDay = 0.12f },
        new Entry { id = ItemCatalog.Bread, basePrice = 14, cap = 26, recoverPerDay = 0.1f }
    };

    static Buyer() { ResetState(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { foreach (Entry e in Entries) e.price = e.basePrice; }

    public static int Quote(Entry e) => Mathf.FloorToInt(e.price);

    /// <summary>Predá až count kusov; vráti utŕžené peniaze. Každý kus ide za aktuálnu cenu, potom cena klesne.</summary>
    public static int Sell(Entry e, int count, PlayerStats player, Inventory inventory)
    {
        if (inventory == null || player == null) return 0;
        count = Mathf.Min(count, inventory.CountOf(e.id));
        int total = 0, sold = 0;
        float floor = e.basePrice * FloorShare;

        for (int i = 0; i < count; i++)
        {
            total += Mathf.Max(1, Mathf.FloorToInt(e.price));
            e.price = Mathf.Max(floor, e.price * DropPerUnit);
            sold++;
        }
        if (sold > 0 && inventory.TryRemove(e.id, sold)) player.AddMoney(total);
        else total = 0;
        return total;
    }

    /// <summary>Denne: cena stúpne o podiel základnej ceny, najviac po strop.</summary>
    public static void OnNewDay()
    {
        foreach (Entry e in Entries) e.price = Mathf.Min(e.cap, e.price + e.basePrice * e.recoverPerDay);
    }

    public static List<float> Export()
    {
        var list = new List<float>();
        foreach (Entry e in Entries) list.Add(e.price);
        return list;
    }

    public static void Import(List<float> prices)
    {
        for (int i = 0; i < Entries.Length; i++)
            Entries[i].price = prices != null && i < prices.Count ? Mathf.Clamp(prices[i], Entries[i].basePrice * FloorShare, Entries[i].cap) : Entries[i].basePrice;
    }
}

/// <summary>Kupec v Centre mesta: E pri ňom otvorí výkup. Ceny sú dynamické (pozri Buyer).</summary>
public class BuyerNPC : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }
    public static int NearFrame { get; private set; } = -10;
    private static BuyerNPC current;   // zatvára ho len kupec, ktorý ho otvoril

    [SerializeField] private float interactionRange = 4f;

    private PlayerStats player;
    private Inventory inventory;
    private GUIStyle titleStyle, rich;
    private Vector2 scroll;
    private string message;

    void Awake()
    {
        if (GetComponent<NPCGrounding>() == null) gameObject.AddComponent<NPCGrounding>();
    }

    void Update()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null) inventory = player.GetComponent<Inventory>();
        }
        if (player == null || player.IsDead || JailManager.IsInJail) return;

        float distance = Vector3.Distance(transform.position, player.transform.position);
        if (distance <= interactionRange + 1f) NearFrame = Time.frameCount;
        if (!MenuOpen && distance <= interactionRange)
        {
            GameHUD.Prompt("[G] Kupec: výkup surovín");
            if (GameInput.GetKeyDown(MerchantNPC.TradeKey))
            {
                if (SoulBalance.Rejected) { GameHUD.Message("Kupec s tebou neobchoduje: ľudia v tebe už nevidia človeka.", 3f); return; }
                current = this;
                MenuOpen = true;
                message = null;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
        else if (MenuOpen && current == this && distance > interactionRange + 1f) Close();

        if (MenuOpen && current == this && GameInput.GetKeyDown(KeyCode.Escape)) Close();
    }

    void OnDisable() { if (current == this) Close(); }

    void Close()
    {
        if (!MenuOpen) return;
        current = null;
        MenuOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup || !MenuOpen || player == null) return;
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        }

        float w = 560f, h = Mathf.Min(560f, Screen.height - 20f);
        Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.96f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.BeginArea(new Rect(box.x + 18f, box.y + 12f, w - 36f, h - 24f));
        GUILayout.Label("KUPEC – výkup", titleStyle);
        GUILayout.Label($"Peniaze: <b>{player.money}</b>. Čím viac predáš, tým nižšia cena; po pár dňoch sa vráti hore (až po strop).", rich);
        GUILayout.Space(4f);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(h - 130f));
        foreach (Buyer.Entry e in Buyer.Entries)
        {
            int have = inventory != null ? inventory.CountOf(e.id) : 0;
            ItemDef def = ItemCatalog.Get(e.id);
            string trend = e.price >= e.basePrice ? "<color=#7CFC7C>vysoká</color>" : e.price >= e.basePrice * 0.6f ? "stredná" : "<color=#ff8a6a>nízka</color>";

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{def.displayName}</b>  máš {have}   cena <b>{Buyer.Quote(e)}</b> (základ {e.basePrice}, strop {e.cap}, {trend})", rich, GUILayout.Width(w - 230f));
            GUI.enabled = have > 0;
            if (GUILayout.Button("Predať 1", GUILayout.Width(80f), GUILayout.Height(24f))) Report(e, Buyer.Sell(e, 1, player, inventory));
            if (GUILayout.Button("Všetko", GUILayout.Width(80f), GUILayout.Height(24f))) Report(e, Buyer.Sell(e, have, player, inventory));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(message)) GUILayout.Label(message, rich);
        GUILayout.Label("Esc: zavrieť");
        GUILayout.EndArea();
    }

    void Report(Buyer.Entry e, int money)
    {
        message = money > 0 ? $"Predané za {money} peňazí. Nová cena: {Buyer.Quote(e)}." : "Nič sa nepredalo.";
    }
}
