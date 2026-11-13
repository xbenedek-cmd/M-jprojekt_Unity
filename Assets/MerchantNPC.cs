using UnityEngine;

public class MerchantNPC : MonoBehaviour
{
    public static bool MenuOpen { get; private set; }
    // E je démonické vnímanie, preto sa obchoduje na G (ignoruje sa aj stará uložená hodnota zo scény)
    public static KeyCode TradeKey => KeyBindings.Get("trade", KeyCode.G);
    public static int NearFrame { get; private set; } = -10;   // G je zároveň ladiaci kláves jedla; pri obchodníkovi ho PlayerStats preskočí
    private static MerchantNPC current;   // menu zatvára len obchodník, ktorý ho otvoril (inak ho zavrel každý vzdialený obchodník v meste)

    [Header("Dosah")]
    public float interactionRange = 4f;
    private KeyCode interactKey => TradeKey;

    [Header("Sklad")]
    public int betterWeaponPrice = 120;
    public float betterWeaponDamage = 10f;
    public float betterWeaponRange = 0.35f;
    public int herbPrice = 25;
    public float herbHealing = 35f;
    public int bloodJarPrice = 30;
    public float bloodJarAmount = 30f;
    public int betterWeaponStock = 1;
    public int herbStock = 8;
    public int bloodJarStock = 8;
    [Header("Frakcia")]
    public FactionId faction = FactionId.Rebels;
    [Tooltip("Index špeciálneho predmetu v SpecialGoods (-1 = žiadny). Predmety sa odomykajú postupne.")]
    public int specialIndex = -1;

    private PlayerStats player;
    private PlayerCombat combat;
    private GUIStyle titleStyle, legendStyle, legendText;

    // Legendárny predmet (1000+): každý obchodník má jeden, podľa sortimentu a poradia stánku
    private string legendaryId;
    private int legendaryStock = 1;

    void Start()
    {
        if (legendaryId == null) AssignLegendaries();
    }

    static string[] PoolOf(MerchantNPC m) =>
        m.name.Contains("Zbrane") ? ItemCatalog.LegendaryWeapons
        : m.name.Contains("Lek") || m.name.Contains("Krv") ? ItemCatalog.LegendaryPotions
        : ItemCatalog.LegendaryArmor;

    static int NumberOf(MerchantNPC m)
    {
        int number = 0;
        foreach (char c in m.name)
            if (char.IsDigit(c)) number = number * 10 + (c - '0');
        return number;
    }

    // Rozdelenie raz za scénu: obchodníci toho istého sortimentu sa zoradia podľa čísla stánku a predmety sa striedajú,
    // takže každý legendárny predmet sa dá niekde kúpiť
    static void AssignLegendaries()
    {
        MerchantNPC[] all = FindObjectsByType<MerchantNPC>(FindObjectsInactive.Include);
        System.Array.Sort(all, (a, b) => NumberOf(a).CompareTo(NumberOf(b)));
        int weapons = 0, potions = 0, armor = 0;
        foreach (MerchantNPC m in all)
        {
            string[] pool = PoolOf(m);
            int index = pool == ItemCatalog.LegendaryWeapons ? weapons++ : pool == ItemCatalog.LegendaryPotions ? potions++ : armor++;
            m.legendaryId = pool[index % pool.Length];
            ItemDef def = ItemCatalog.Get(m.legendaryId);
            m.legendaryStock = def != null && def.IsPotion ? 3 : 1;
        }
    }

    void BuyLegendary()
    {
        ItemDef def = ItemCatalog.Get(legendaryId);
        if (def == null) return;
        if (legendaryStock <= 0) { GameHUD.Message($"{def.displayName}: vypredané."); return; }
        if (!HasRoomFor(legendaryId)) return;
        if (!Buy(def.price)) { GameHUD.Message($"Nemáš dosť peňazí ({P(def.price)})."); return; }
        legendaryStock--;
        player.GetComponent<Inventory>().TryAdd(legendaryId);
        GameAudio.Play(GameAudio.Sfx.Craft, transform.position);
        GameHUD.Message($"Získal si legendárny predmet: {def.displayName}! Nájdeš ho v inventári (I).", 5f);
    }

    void Awake()
    {
        BuildVisual();
        if (GetComponent<NPCGrounding>() == null) gameObject.AddComponent<NPCGrounding>();   // vždy pevne na zemi
    }

    void BuildVisual()
    {
        Transform visual = transform.Find("MerchantVisual");
        if (visual != null) return;

        Renderer rootRenderer = GetComponent<Renderer>();
        if (rootRenderer != null) rootRenderer.enabled = false;

        GameObject root = new GameObject("MerchantVisual");
        root.transform.SetParent(transform, false);

        Material coat = Material("Merchant coat", new Color(0.08f, 0.18f, 0.24f), 0.15f);
        Material leather = Material("Leather", new Color(0.24f, 0.1f, 0.045f), 0.05f);
        Material skin = Material("Skin", new Color(0.72f, 0.42f, 0.28f), 0f);
        Material metal = Material("Brass", new Color(0.85f, 0.48f, 0.12f), 0.7f);
        Material accent = Material("Resistance accent", new Color(0.12f, 0.85f, 0.55f), 0.45f);

        Part(root.transform, PrimitiveType.Capsule, "Coat", new Vector3(0f, 0.65f, 0f), new Vector3(0.72f, 0.9f, 0.42f), coat);
        Part(root.transform, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.55f, 0f), new Vector3(0.46f, 0.5f, 0.46f), skin);
        Part(root.transform, PrimitiveType.Cylinder, "Hat", new Vector3(0f, 1.86f, 0f), new Vector3(0.58f, 0.12f, 0.58f), leather);
        Part(root.transform, PrimitiveType.Cylinder, "HatBand", new Vector3(0f, 1.77f, 0f), new Vector3(0.5f, 0.05f, 0.5f), accent);
        Part(root.transform, PrimitiveType.Cube, "Scarf", new Vector3(0f, 1.2f, -0.02f), new Vector3(0.58f, 0.12f, 0.48f), accent);
        Part(root.transform, PrimitiveType.Cube, "Belt", new Vector3(0f, 0.58f, -0.23f), new Vector3(0.68f, 0.1f, 0.08f), leather);
        Part(root.transform, PrimitiveType.Cube, "Buckle", new Vector3(0f, 0.58f, -0.29f), new Vector3(0.14f, 0.14f, 0.04f), metal);
        Part(root.transform, PrimitiveType.Cube, "Satchel", new Vector3(0.48f, 0.65f, 0.05f), new Vector3(0.28f, 0.42f, 0.18f), leather);
        Part(root.transform, PrimitiveType.Cube, "TradeSign", new Vector3(0f, 1.1f, 0.5f), new Vector3(0.46f, 0.08f, 0.04f), metal);
        Part(root.transform, PrimitiveType.Cube, "LeftBoot", new Vector3(-0.2f, -0.2f, 0.04f), new Vector3(0.25f, 0.4f, 0.3f), leather);
        Part(root.transform, PrimitiveType.Cube, "RightBoot", new Vector3(0.2f, -0.2f, 0.04f), new Vector3(0.25f, 0.4f, 0.3f), leather);
    }

    static Material Material(string name, Color color, float metallic)
    {
        Material material = new Material(Shader.Find("Standard"));
        material.name = name;
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", 0.55f);
        return material;
    }

    static void Part(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().material = material;
    }

    void Update()
    {
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            if (player != null) combat = player.GetComponent<PlayerCombat>();
        }
        if (player == null || player.IsDead || JailManager.IsInJail) return;

        float distance = Vector3.Distance(transform.position, player.transform.position);
        if (distance <= interactionRange + 1f) NearFrame = Time.frameCount;
        if (!MenuOpen && distance <= interactionRange)
        {
            GameHUD.Prompt($"[{interactKey}] Obchodník");
            if (GameInput.GetKeyDown(interactKey) && SoulBalance.Rejected)
            {
                GameHUD.Message("Obchodník ti nič nepredá: ľudia v tebe už nevidia človeka.", 3f);
            }
            else if (GameInput.GetKeyDown(interactKey))
            {
                MenuOpen = true;
                current = this;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
        else if (MenuOpen && current == this && distance > interactionRange + 1f)
        {
            Close();
        }

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

    static int P(int price) => Mathf.CeilToInt(price * SoulBalance.BuyFactor);

    bool Buy(int price)
    {
        return player != null && player.TrySpend(P(price));
    }

    // Nákupy idú do inventára. Najprv sa overí miesto, až potom sa platí (inak by hráč prišiel o peniaze).
    bool HasRoomFor(string itemId)
    {
        Inventory inventory = player != null ? player.GetComponent<Inventory>() : null;
        if (inventory == null)
        {
            GameHUD.Message("Hráč nemá inventár.");
            return false;
        }
        if (!inventory.CanAdd(itemId))
        {
            GameHUD.Message($"Inventár je plný: {ItemCatalog.Get(itemId).displayName}.");
            return false;
        }
        return true;
    }

    void BuyWeapon()
    {
        if (betterWeaponStock <= 0) { GameHUD.Message("Táto zbraň je vypredaná."); return; }
        if (!HasRoomFor(ItemCatalog.SteelSword)) return;
        if (!Buy(betterWeaponPrice)) { GameHUD.Message("Nemáš dosť peňazí."); return; }
        betterWeaponStock--;
        player.GetComponent<Inventory>().TryAdd(ItemCatalog.SteelSword);
        GameHUD.Message("Kúpil si Oceľový meč. Nájdeš ho v inventári (I).");
    }

    void BuyHerb()
    {
        if (herbStock <= 0) { GameHUD.Message("Liečivé byliny sú vypredané."); return; }
        if (!HasRoomFor(ItemCatalog.HealthPotion)) return;
        if (!Buy(herbPrice)) { GameHUD.Message("Nemáš dosť peňazí."); return; }
        herbStock--;
        player.GetComponent<Inventory>().TryAdd(ItemCatalog.HealthPotion);
        GameHUD.Message("Kúpil si Liečivý lektvar.");
    }

    void BuyBloodJar()
    {
        if (!player.IsDemon) { GameHUD.Message("Krv v nádobe môže použiť iba démon."); return; }
        if (bloodJarStock <= 0) { GameHUD.Message("Krv v nádobách je vypredaná."); return; }
        if (!HasRoomFor(ItemCatalog.BloodVial)) return;
        if (!Buy(bloodJarPrice)) { GameHUD.Message("Nemáš dosť peňazí."); return; }
        bloodJarStock--;
        player.GetComponent<Inventory>().TryAdd(ItemCatalog.BloodVial);
        GameHUD.Message("Kúpil si Krvavú fľašu.");
    }

    // Nástroje: obchodník predáva dva najslabšie typy sekery a krompáča a základné kladivo (lepšie sa vyrábajú v dielni). Kúpa nahradí slabší nástroj.
    private struct ToolOffer { public ToolKit.Kind kind; public int tier, price; }
    static readonly ToolOffer[] toolOffers =
    {
        new ToolOffer { kind = ToolKit.Kind.Axe, tier = 0, price = 40 }, new ToolOffer { kind = ToolKit.Kind.Axe, tier = 1, price = 100 },
        new ToolOffer { kind = ToolKit.Kind.Pickaxe, tier = 0, price = 50 }, new ToolOffer { kind = ToolKit.Kind.Pickaxe, tier = 1, price = 120 },
        new ToolOffer { kind = ToolKit.Kind.Hammer, tier = 0, price = 60 }
    };

    void BuyTool(int index)
    {
        ToolOffer o = toolOffers[index];
        if (ToolKit.Has(o.kind) && ToolKit.Tier(o.kind) >= o.tier)
        {
            GameHUD.Message("Už máš rovnaký alebo lepší nástroj.");
            return;
        }
        if (!Buy(o.price)) { GameHUD.Message("Nemáš dosť peňazí."); return; }

        ToolKit.Give(o.kind, o.tier);
        GameHUD.Message($"Kúpil si: {ToolKit.Name(o.kind)}. {ToolKit.Targets(o.kind)} ničíš ľavým tlačidlom.");
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!MenuOpen || player == null || current != this) return;   // menu kreslí len obchodník, ktorý ho otvoril
        if (titleStyle == null)
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        ItemDef legend = ItemCatalog.Get(legendaryId);
        float extra = legend != null ? 104f : 0f;
        float width = 460f, height = (specialIndex >= 0 ? 580f : 440f) + extra;
        Rect box = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.96f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(box.x, box.y + 14f, width, 32f), "OBCHODNÍK", titleStyle);
        GUI.Label(new Rect(box.x + 20f, box.y + 52f, width - 40f, 24f), $"Peniaze: {player.money}");

        float top = box.y + 86f;
        if (legend != null)
        {
            if (legendStyle == null)
            {
                legendStyle = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
                legendStyle.normal.textColor = legendStyle.hover.textColor = new Color(1f, 0.85f, 0.35f);
                legendText = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                legendText.normal.textColor = new Color(1f, 0.92f, 0.7f);
            }
            Rect gold = new Rect(box.x + 18f, top - 4f, width - 36f, 96f);
            GUI.color = new Color(1f, 0.8f, 0.25f, 0.16f);
            GUI.DrawTexture(gold, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.8f, 0.25f, 0.9f);
            GUI.DrawTexture(new Rect(gold.x, gold.y, 3f, gold.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.enabled = legendaryStock > 0;
            if (GUI.Button(new Rect(box.x + 25f, top, width - 50f, 36f),
                    legendaryStock > 0 ? $"[0] ★ {legend.displayName}  {P(legend.price)}  ({legendaryStock})" : $"★ {legend.displayName}: vypredané", legendStyle)) BuyLegendary();
            GUI.enabled = true;
            GUI.Label(new Rect(box.x + 28f, top + 40f, width - 56f, 50f), legend.description, legendText);
            top += extra;
        }

        if (GUI.Button(new Rect(box.x + 25f, top, width - 50f, 34f), $"[1] Oceľový meč  {P(betterWeaponPrice)}  ({betterWeaponStock})")) BuyWeapon();
        if (GUI.Button(new Rect(box.x + 25f, top + 42f, width - 50f, 34f), $"[2] Liečivý lektvar  {P(herbPrice)}  ({herbStock})")) BuyHerb();
        if (GUI.Button(new Rect(box.x + 25f, top + 84f, width - 50f, 34f), $"[3] Krvavá fľaša  {P(bloodJarPrice)}  ({bloodJarStock})")) BuyBloodJar();
        for (int i = 0; i < toolOffers.Length; i++)
        {
            ToolOffer o = toolOffers[i];
            if (GUI.Button(new Rect(box.x + 25f, top + 126f + i * 38f, width - 50f, 32f), $"[{4 + i}] {ToolKit.Name(o.kind, o.tier)}  {P(o.price)}")) BuyTool(i);
        }
        GUI.Label(new Rect(box.x + 25f, top + 126f + toolOffers.Length * 38f + 2f, width - 50f, 44f),
            $"Máš: {ToolKit.Name(ToolKit.Kind.Axe)}, {ToolKit.Name(ToolKit.Kind.Pickaxe)}, {ToolKit.Name(ToolKit.Kind.Hammer)}. Esc: zavrieť");

        if (specialIndex >= 0 && specialIndex < SpecialGoods.Items.Length)
        {
            SpecialGoods.Def item = SpecialGoods.Items[specialIndex];
            float y = top + 126f + toolOffers.Length * 38f + 50f;
            string reason = SpecialGoods.LockReason(specialIndex);
            GUI.Label(new Rect(box.x + 25f, y, width - 50f, 22f), $"Špeciálny predmet ({SpecialGoods.OwnedCount}/{SpecialGoods.Items.Length}): {item.name}");
            GUI.Label(new Rect(box.x + 25f, y + 22f, width - 50f, 40f), item.effect + (reason != null ? $"  [{reason}]" : ""));
            GUI.enabled = reason == null;
            if (GUI.Button(new Rect(box.x + 25f, y + 64f, width - 50f, 34f), reason == null ? $"Kúpiť {item.name}  {P(item.price)}" : (SpecialGoods.Owns(specialIndex) ? "Už vlastníš" : "Zatiaľ zamknuté")))
            {
                if (SpecialGoods.TryBuy(specialIndex, player, P(item.price), out string msg)) GameHUD.Message(msg, 5f);
                else GameHUD.Message(msg, 3f);
            }
            GUI.enabled = true;
        }

        if (Event.current.type == EventType.KeyDown)
        {
            for (int i = 0; i < toolOffers.Length; i++)
                if (Event.current.keyCode == KeyCode.Alpha4 + i) BuyTool(i);
            if (Event.current.keyCode == KeyCode.Alpha0) BuyLegendary();
            else if (Event.current.keyCode == KeyCode.Alpha1) BuyWeapon();
            else if (Event.current.keyCode == KeyCode.Alpha2) BuyHerb();
            else if (Event.current.keyCode == KeyCode.Alpha3) BuyBloodJar();
        }
    }
}
