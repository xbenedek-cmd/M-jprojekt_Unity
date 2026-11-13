using System.Collections.Generic;
using UnityEngine;

public enum ItemType { Weapon, Potion, Misc, Tool }

public enum PotionEffect { None, Heal, Stamina, Blood, ScentMask, Food, Invulnerable, Rage, FullRestore }

/// <summary>Definícia predmetu (zbraň, lektvar, ostatné). Katalóg je v kóde, takže netreba žiadne assety.</summary>
public class ItemDef
{
    public string id;
    public string displayName;
    public string description;
    public ItemType type;
    public Color color = Color.white;
    public int maxStack = 1;
    public int price;

    // Zbraň: násobiče voči základu v PlayerCombat
    public float damageMultiplier = 1f;
    public float range = 2.2f;
    public float staminaCost = 12f;
    public float cooldownMultiplier = 1f;
    public DamageKind damageKind = DamageKind.Normal;

    // Lektvar
    public PotionEffect effect;
    public float amount;
    public float duration;

    /// <summary>Legendárny predmet (cena od 1000): v inventári aj u obchodníka má zlatý rám.</summary>
    public bool IsLegendary => price >= 1000;
    public bool IsWeapon => type == ItemType.Weapon;
    public bool IsPotion => type == ItemType.Potion;
    public bool IsTool => type == ItemType.Tool;

    // Nástroj (sekera / krompáč / kladivo): po zdvihnutí sa nepridá do inventára, ale do ToolKit
    public ToolKit.Kind toolKind;
    public int toolTier;

    // Oblečenie (4 časti): bonusy cez rovnaké štáty ako strom schopností; durability > 0 = opotrebúva sa (maska)
    public EquipSlot slot;
    public float durability;
    public SkillTree.Effect[] equipEffects;
}

/// <summary>Všetky predmety v hre. Nový predmet = nový záznam v Build().</summary>
public static class ItemCatalog
{
    public const string IronSword = "sword_iron";
    public const string SteelSword = "sword_steel";
    public const string YumiNotebook = "yumi_notebook";   // príbehový predmet zo skrytej odbočky v Divočine
    public const string BlessedBlade = "sword_blessed";
    public const string HealthPotion = "potion_health";
    public const string StaminaPotion = "potion_stamina";
    public const string BloodVial = "potion_blood";
    public const string ScentSerum = "potion_scent";
    public const string Herb = "herb";
    public const string Scrap = "scrap";
    public const string Wood = "wood";
    public const string Stone = "stone";
    public const string Crystal = "crystal";
    public const string Gold = "gold";
    public const string BlackCrystal = "crystal_black";
    public const string SoulDetector = "soul_detector";
    public const string KeyCard = "keycard";
    public const string Vegetables = "food_vegetables";
    public const string Bread = "food_bread";
    public const string Rifle = "rifle_military";
    public const string Mask = "mask_filter";
    public const string Hood = "gear_hood", LeatherJacket = "gear_jacket", MilitaryVest = "gear_vest", WorkPants = "gear_pants", TacticalPants = "gear_pants_tac", QuietBoots = "gear_boots_quiet", MilitaryBoots = "gear_boots_mil";

    // Legendárne predmety (1000+): každý obchodník predáva jeden
    public const string DawnKatana = "sword_dawn", StormFang = "sword_stormfang", ColossusBlade = "sword_colossus";
    public const string AshCrown = "gear_crown_ash", TitanPlate = "gear_plate_titan", ShadowCloak = "gear_cloak_shadow", StormGreaves = "gear_greaves_storm", WindBoots = "gear_boots_wind";
    public const string ImmortalElixir = "potion_immortal", TitanBlood = "potion_rage", PhoenixTear = "potion_phoenix";

    /// <summary>Legendárne predmety podľa sortimentu obchodníka.</summary>
    public static readonly string[] LegendaryWeapons = { DawnKatana, StormFang, ColossusBlade };
    public static readonly string[] LegendaryArmor = { TitanPlate, AshCrown, StormGreaves, WindBoots, ShadowCloak };
    public static readonly string[] LegendaryPotions = { ImmortalElixir, PhoenixTear, TitanBlood };

    /// <summary>Id predmetu nástroja, napr. tool_0_1 = sekera, železný typ.</summary>
    public static string ToolId(ToolKit.Kind kind, int tier) => $"tool_{(int)kind}_{tier}";

    private static readonly Dictionary<string, ItemDef> items = Build();

    /// <summary>Päste: nie je to predmet v inventári, ale vždy dostupný "útok bez zbrane".</summary>
    public static readonly ItemDef Fists = new ItemDef
    {
        id = "fists", displayName = "Päste", type = ItemType.Weapon, description = "Bez zbrane. Slabší, ale rýchly útok.",
        damageMultiplier = 0.45f, range = 1.5f, staminaCost = 8f, color = new Color(0.8f, 0.65f, 0.5f)
    };

    public static ItemDef Get(string id) => !string.IsNullOrEmpty(id) && items.TryGetValue(id, out ItemDef d) ? d : null;

    public static IEnumerable<ItemDef> All => items.Values;

    static Dictionary<string, ItemDef> Build()
    {
        var d = new Dictionary<string, ItemDef>();
        void Add(ItemDef item) { d[item.id] = item; }
        void AddGear(string id, string name, EquipSlot slot, Color color, string description, params SkillTree.Effect[] effects)
        {
            Add(new ItemDef { id = id, displayName = name, type = ItemType.Misc, color = color, maxStack = 1, price = 40, slot = slot, equipEffects = effects, description = description });
        }

        // ---------- Zbrane ----------
        Add(new ItemDef
        {
            id = IronSword, displayName = "Železný meč", type = ItemType.Weapon, color = new Color(0.6f, 0.65f, 0.72f), price = 60,
            description = "Obyčajný meč z mestskej zbrojárne. Spoľahlivý začiatok.",
            damageMultiplier = 1f, range = 2.2f, staminaCost = 12f, cooldownMultiplier = 1f
        });
        Add(new ItemDef
        {
            id = SteelSword, displayName = "Oceľový meč", type = ItemType.Weapon, color = new Color(0.45f, 0.75f, 1f), price = 120,
            description = "Kalená oceľ z čierneho trhu. Silnejší a s dlhším dosahom.",
            damageMultiplier = 1.5f, range = 2.4f, staminaCost = 14f, cooldownMultiplier = 0.95f
        });
        Add(new ItemDef
        {
            id = BlessedBlade, displayName = "Posvätená čepeľ", type = ItemType.Weapon, color = new Color(1f, 0.85f, 0.35f),
            description = "Čepeľ z očistného séra. Zasiahne aj nezraniteľných vyšších démonov.",
            damageMultiplier = 1.3f, range = 2.3f, staminaCost = 13f, cooldownMultiplier = 1f, damageKind = DamageKind.Holy
        });

        // ---------- Nástroje (zdvihnú sa do ToolKit, nezaberajú miesto v inventári) ----------
        int[] toolPrices = { 40, 100, 220, 400 };
        for (int k = 0; k < ToolKit.Kinds; k++)
            for (int t = 0; t < ToolKit.Tiers; t++)
                Add(new ItemDef
                {
                    id = ToolId((ToolKit.Kind)k, t), displayName = ToolKit.Name((ToolKit.Kind)k, t), type = ItemType.Tool, color = ToolKit.ColorOf(t),
                    price = toolPrices[t], toolKind = (ToolKit.Kind)k, toolTier = t,
                    description = $"{ToolKit.KindName((ToolKit.Kind)k)}: ničí {ToolKit.Targets((ToolKit.Kind)k).ToLowerInvariant()}."
                });

        // ---------- Lektvary (v inventári 5 miest, stoh do 5) ----------
        Add(new ItemDef
        {
            id = HealthPotion, displayName = "Liečivý lektvar", type = ItemType.Potion, color = new Color(0.9f, 0.2f, 0.25f), maxStack = 5, price = 25,
            description = "Okamžite vylieči 40 HP.", effect = PotionEffect.Heal, amount = 40f
        });
        Add(new ItemDef
        {
            id = StaminaPotion, displayName = "Lektvar výdrže", type = ItemType.Potion, color = new Color(1f, 0.85f, 0.2f), maxStack = 5, price = 20,
            description = "Úplne obnoví výdrž.", effect = PotionEffect.Stamina, amount = 100f
        });
        Add(new ItemDef
        {
            id = BloodVial, displayName = "Krvavá fľaša", type = ItemType.Potion, color = new Color(0.6f, 0.05f, 0.12f), maxStack = 5, price = 30,
            description = "Doplní 30 krvi. Použiť ju môže len démon.", effect = PotionEffect.Blood, amount = 30f
        });
        Add(new ItemDef
        {
            id = ScentSerum, displayName = "Sérum proti pachu", type = ItemType.Potion, color = new Color(0.35f, 0.85f, 0.5f), maxStack = 5, price = 40,
            description = "60 s ťa vojaci a civilisti spozornejú z menšej vzdialenosti (zakryje pach).",
            effect = PotionEffect.ScentMask, duration = 60f
        });

        // ---------- Jedlo (zaberá miesto v lektvaroch; hlad dopĺňa len človek) ----------
        Add(new ItemDef
        {
            id = Vegetables, displayName = "Zelenina", type = ItemType.Potion, color = new Color(0.45f, 0.75f, 0.25f), maxStack = 20, price = 6,
            description = "Čerstvá úroda z poľa. Zasýti o 20 hladu (len človek).", effect = PotionEffect.Food, amount = 20f
        });
        Add(new ItemDef
        {
            id = Bread, displayName = "Chlieb", type = ItemType.Potion, color = new Color(0.85f, 0.65f, 0.3f), maxStack = 20, price = 14,
            description = "Pečivo z farmy alebo z truhly. Zasýti o 40 hladu (len človek).", effect = PotionEffect.Food, amount = 40f
        });

        // ---------- Ostatné ----------
        Add(new ItemDef
        {
            id = Herb, displayName = "Liečivá bylina", type = ItemType.Misc, color = new Color(0.35f, 0.75f, 0.3f), maxStack = 20,
            description = "Surovina na lieky pre Doktorku Yumi."
        });
        Add(new ItemDef
        {
            id = Scrap, displayName = "Kovový šrot", type = ItemType.Misc, color = new Color(0.55f, 0.55f, 0.6f), maxStack = 20,
            description = "Materiál na opravy a vylepšenia."
        });
        Add(new ItemDef
        {
            id = Wood, displayName = "Drevo", type = ItemType.Misc, color = new Color(0.45f, 0.3f, 0.16f), maxStack = 20,
            description = "Surovina na stavby a nástroje z Divočiny."
        });
        Add(new ItemDef
        {
            id = Stone, displayName = "Kameň", type = ItemType.Misc, color = new Color(0.5f, 0.5f, 0.5f), maxStack = 20,
            description = "Pevný stavebný materiál z Divočiny."
        });
        Add(new ItemDef
        {
            id = Crystal, displayName = "Bleskový kryštál", type = ItemType.Misc, color = new Color(0.3f, 0.75f, 1f), maxStack = 20,
            description = "Kryštál nabitý bleskom. Zdroj energie pre pokročilé vylepšenia."
        });
        Add(new ItemDef
        {
            id = Gold, displayName = "Zlato", type = ItemType.Misc, color = new Color(1f, 0.82f, 0.2f), maxStack = 50, price = 70,
            description = "Cenná ruda z bane. Predáva sa za peniaze."
        });
        Add(new ItemDef
        {
            id = BlackCrystal, displayName = "Čierny kryštál", type = ItemType.Misc, color = new Color(0.4f, 0.15f, 0.65f), maxStack = 20,
            description = "Palivo pre generátor elektrárne. Ťaží sa v bani."
        });
        Add(new ItemDef
        {
            id = SoulDetector, displayName = "Detektor duší", type = ItemType.Misc, color = new Color(0.6f, 0.4f, 1f), maxStack = 1,
            description = "Okuliare z čierneho trhu na odhalenie démona."
        });
        Add(new ItemDef
        {
            id = Mask, displayName = "Filtračná maska", type = ItemType.Misc, color = new Color(0.45f, 0.5f, 0.45f), maxStack = 1, price = 40,
            slot = EquipSlot.Head, durability = 480f,
            description = "Nasaď ju na hlavu (Inventár → Oblečenie): chráni pred popolovou búrkou. V búrke sa opotrebúva (vydrží asi 4 búrky) a potom sa rozpadne."
        });
        AddGear(Hood, "Tmavá kapucňa", EquipSlot.Head, new Color(0.15f, 0.15f, 0.2f), "Zakrýva tvár: strážcovia ťa vidia z menšej vzdialenosti.", new SkillTree.Effect(SkillStat.StealthRange, 0.08f));
        AddGear(LeatherJacket, "Kožená bunda", EquipSlot.Top, new Color(0.35f, 0.22f, 0.14f), "Hrubá koža zmierňuje údery.", new SkillTree.Effect(SkillStat.Armor, 0.05f));
        AddGear(MilitaryVest, "Vojenská vesta", EquipSlot.Top, new Color(0.25f, 0.3f, 0.22f), "Pancierová vesta Megakorporácie.", new SkillTree.Effect(SkillStat.Armor, 0.09f));
        AddGear(WorkPants, "Pracovné nohavice", EquipSlot.Bottom, new Color(0.3f, 0.3f, 0.4f), "Pohodlné: výdrž sa míňa pomalšie.", new SkillTree.Effect(SkillStat.Stamina, 0.08f));
        AddGear(TacticalPants, "Taktické nohavice", EquipSlot.Bottom, new Color(0.2f, 0.22f, 0.24f), "Vreckovité a pevné: lepšia výdrž aj kratšie čakanie na úhyb.", new SkillTree.Effect(SkillStat.Stamina, 0.05f), new SkillTree.Effect(SkillStat.DashCooldown, 0.1f));
        AddGear(QuietBoots, "Tiché topánky", EquipSlot.Boots, new Color(0.1f, 0.1f, 0.12f), "Mäkká podrážka: rýchlejší pohyb v čupe a menší dosah odhalenia.", new SkillTree.Effect(SkillStat.CrouchSpeed, 0.2f), new SkillTree.Effect(SkillStat.StealthRange, 0.05f));
        AddGear(MilitaryBoots, "Vojenské čižmy", EquipSlot.Boots, new Color(0.18f, 0.14f, 0.1f), "Pevné čižmy: rýchlejšia chôdza a trochu ochrany.", new SkillTree.Effect(SkillStat.MoveSpeed, 0.05f), new SkillTree.Effect(SkillStat.Armor, 0.03f));
        Add(new ItemDef
        {
            id = Rifle, displayName = "Vojenská puška", type = ItemType.Misc, color = new Color(0.3f, 0.32f, 0.36f), maxStack = 5, price = 90,
            description = "Puška vojaka Megakorporácie. Strelné zbrane v hre ešte nie sú, zatiaľ ju možno len predať."
        });
        Add(new ItemDef
        {
            id = KeyCard, displayName = "Prístupová karta", type = ItemType.Misc, color = new Color(0.3f, 0.7f, 1f), maxStack = 1,
            description = "Otvorí uzamknuté bezpečnostné zóny."
        });

        // ---------- Legendárne predmety (1000+) ----------
        Add(new ItemDef
        {
            id = DawnKatana, displayName = "Katana Úsvitu", type = ItemType.Weapon, color = new Color(1f, 0.78f, 0.3f), price = 2400,
            description = "Čepeľ kovaná v posvätnom ohni. Trojnásobné poškodenie, dlhý dosah a zasiahne aj nezraniteľných vyšších démonov.",
            damageMultiplier = 3f, range = 2.9f, staminaCost = 10f, cooldownMultiplier = 0.85f, damageKind = DamageKind.Holy
        });
        Add(new ItemDef
        {
            id = StormFang, displayName = "Bleskový tesák", type = ItemType.Weapon, color = new Color(0.4f, 0.9f, 1f), price = 1800,
            description = "Ľahká čepeľ nabitá bleskom. Seká takmer dvakrát rýchlejšie a skoro neberie výdrž.",
            damageMultiplier = 2.1f, range = 2.4f, staminaCost = 6f, cooldownMultiplier = 0.55f, damageKind = DamageKind.Lightning
        });
        Add(new ItemDef
        {
            id = ColossusBlade, displayName = "Kolosov drvič", type = ItemType.Weapon, color = new Color(0.75f, 0.35f, 0.25f), price = 2200,
            description = "Obrovský meč z pancierov padnutého mecha. Pomalý, ale jeden úder zrazí takmer čokoľvek.",
            damageMultiplier = 4.6f, range = 3.3f, staminaCost = 24f, cooldownMultiplier = 1.4f
        });
        void AddLegendGear(string id, string name, EquipSlot slot, Color color, int price, string description, params SkillTree.Effect[] effects)
        {
            Add(new ItemDef { id = id, displayName = name, type = ItemType.Misc, color = color, maxStack = 1, price = price, slot = slot, equipEffects = effects, description = description });
        }
        AddLegendGear(TitanPlate, "Titánový pancier", EquipSlot.Top, new Color(0.7f, 0.75f, 0.85f), 2600, "Pancier z jadra vojnového stroja. Tretina poškodenia sa od neho odrazí.",
            new SkillTree.Effect(SkillStat.Armor, 0.3f), new SkillTree.Effect(SkillStat.MaxHp, 0.25f));
        AddLegendGear(AshCrown, "Koruna popola", EquipSlot.Head, new Color(0.85f, 0.3f, 0.9f), 1500, "Čelenka démonického pána. Vidíš slabiny nepriateľa a rany sa ti zatvárajú samy.",
            new SkillTree.Effect(SkillStat.Crit, 0.2f), new SkillTree.Effect(SkillStat.HpRegen, 1f));
        AddLegendGear(StormGreaves, "Búrkové nohavice", EquipSlot.Bottom, new Color(0.3f, 0.6f, 1f), 1400, "Tkanina pretkaná bleskovým vláknom. Výdrž skoro nemizne a úhyb je hneď pripravený.",
            new SkillTree.Effect(SkillStat.Stamina, 0.3f), new SkillTree.Effect(SkillStat.DashCooldown, 0.4f));
        AddLegendGear(WindBoots, "Čižmy vetra", EquipSlot.Boots, new Color(0.6f, 1f, 0.85f), 1700, "Ľahké ako dych. Bežíš rýchlejšie a vo vzduchu skočíš ešte raz.",
            new SkillTree.Effect(SkillStat.MoveSpeed, 0.2f), new SkillTree.Effect(SkillStat.ExtraJump, 1f));
        AddLegendGear(ShadowCloak, "Plášť tieňov", EquipSlot.Top, new Color(0.2f, 0.12f, 0.3f), 1900, "Plášť utkaný z tmy. Stráže ťa takmer nevidia a každý tvoj zásah ťa lieči.",
            new SkillTree.Effect(SkillStat.StealthRange, 0.3f), new SkillTree.Effect(SkillStat.Lifesteal, 0.1f));
        Add(new ItemDef
        {
            id = ImmortalElixir, displayName = "Elixír nesmrteľnosti", type = ItemType.Potion, color = new Color(1f, 0.9f, 0.45f), maxStack = 3, price = 1000,
            description = "15 sekúnd ťa nič nezraní. Hlad a smäd po krvi však nezastaví.", effect = PotionEffect.Invulnerable, duration = 15f
        });
        Add(new ItemDef
        {
            id = TitanBlood, displayName = "Krv titanov", type = ItemType.Potion, color = new Color(1f, 0.3f, 0.1f), maxStack = 3, price = 1100,
            description = "30 sekúnd dávaš dvojnásobné poškodenie.", effect = PotionEffect.Rage, duration = 30f
        });
        Add(new ItemDef
        {
            id = PhoenixTear, displayName = "Slza fénixa", type = ItemType.Potion, color = new Color(1f, 0.55f, 0.8f), maxStack = 3, price = 1200,
            description = "Okamžite doplní celé zdravie aj výdrž.", effect = PotionEffect.FullRestore
        });

        // ---------- Príbehové predmety ----------
        Add(new ItemDef
        {
            id = YumiNotebook, displayName = "Yumin zápisník", type = ItemType.Misc, color = new Color(0.4f, 1f, 0.65f), maxStack = 1, price = 0,
            description = "Poznámky doktorky Yumi o očistnom sére: zrážanie hybridnej krvi a recept na čepeľ namočenú v sére."
        });

        return d;
    }
}
