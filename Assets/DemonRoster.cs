using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Jeden démon v zozname: trieda, klan, štatistiky a vzhľad.</summary>
[Serializable]
public class DemonEntry
{
    public string displayName = "Démon";
    public DemonTier tier = DemonTier.Lesser;
    public FactionId clan = FactionId.WildClan;

    [Header("Štatistiky")]
    public float maxHealth = 60f;
    public float attackDamage = 15f;
    public float moveSpeed = 3.2f;
    public int moneyReward = 20;
    [Tooltip("Slabina. Vyšší démon je nezraniteľný voči bežným zbraniam, preto tu nesmie byť Normal.")]
    public DamageKind weakness = DamageKind.Fire;

    [Header("Vzhľad")]
    [Tooltip("Podfarbenie démonického modelu (klanová farba)")]
    public Color tint = Color.red;
    [Tooltip("Vyšší démon: farba ľudskej masky, v ktorej sa ukrýva medzi ľuďmi (kým ho hráč neskúma - T)")]
    public Color disguiseColor = new Color(0.55f, 0.55f, 0.6f);
    [Tooltip("Mierka postavy (zvieratá menšie, tanky väčšie)")]
    public float scale = 1f;
    [Tooltip("Voliteľný vlastný model (napr. vlk, krkavec). Prázdne = spoločný model démona.")]
    public GameObject model;
    [Tooltip("Vlastná sada animácií (napr. boss s Humanoid kostrou). Prázdne = spoločná sada démonov.")]
    public DemonAnimationSet animationSet;
    [Tooltip("Vlastný materiál modelu (boss). Prázdne = spoločný materiál démonov.")]
    public Material material;
    [Tooltip("Model má vlastný vzhľad (boss): hromadné menu spoločný model, sadu a materiál naň nepoužijú")]
    public bool keepModelMaterials;

    [Header("Poznámky")]
    [Tooltip("Schopnosť / správanie podľa GDD, ktoré sa dá neskôr doprogramovať")]
    public string trait;
    [TextArea] public string note;

    public NPCKind Kind => tier == DemonTier.Lesser ? NPCKind.LesserDemon
                         : tier == DemonTier.Mid ? NPCKind.MidDemon
                         : NPCKind.HigherDemon;
}

/// <summary>
/// Zoznam démonov pre generátor (menu Progeny → Démoni), postavený podľa GDD (Hra.txt, Postavy.txt):
///  - nižší = Divoká svorka (zmutované zvieratá z odpadu a prírodnej mágie),
///  - strední = bojovníci klanov,
///  - vyšší = infiltrátori klanov, cez deň maskovaní za ľudí, nezraniteľní voči bežným zbraniam.
/// Predvolene 10 démonov: všetkých 5 klanov a všetky 3 triedy. Upraviť sa dá v Assets/Data/DemonRoster.asset.
/// </summary>
[CreateAssetMenu(fileName = "DemonRoster", menuName = "Progeny/Demon Roster")]
public class DemonRoster : ScriptableObject
{
    public List<DemonEntry> entries = new List<DemonEntry>();

    public const string AssetPath = "Assets/Data/DemonRoster.asset";

    public static List<DemonEntry> BuildDefaults()
    {
        var list = new List<DemonEntry>
        {
            // ================= NIŽŠÍ: Divoká svorka (zmutované zvieratá, rýchli, krehkí, útočia vo svorkách) =================
            new DemonEntry
            {
                displayName = "Okami (zmutovaný vlk)", tier = DemonTier.Lesser, clan = FactionId.WildClan,
                maxHealth = 50f, attackDamage = 12f, moveSpeed = 3.9f, moneyReward = 15, weakness = DamageKind.Fire,
                tint = new Color(0.35f, 0.7f, 0.25f), scale = 0.85f,
                trait = "Feral Rush, Pack Tactics",
                note = "Divoká svorka: zmutovaný lesný vlk z toxického odpadu. Rýchly, dravý, silnejší vo svorke."
            },
            new DemonEntry
            {
                displayName = "Karasu (zmutovaný krkavec)", tier = DemonTier.Lesser, clan = FactionId.ShadowClan,
                maxHealth = 35f, attackDamage = 10f, moveSpeed = 4.2f, moneyReward = 12, weakness = DamageKind.Fire,
                tint = new Color(0.5f, 0.2f, 0.8f), scale = 0.75f,
                trait = "Špeh klanu Tieňa (Beast Taming)",
                note = "Krkavec ako špeh Klanu Tieňa: krehký, najrýchlejší, fialové oči."
            },
            new DemonEntry
            {
                displayName = "Ketsu-inu (krvavý pes)", tier = DemonTier.Lesser, clan = FactionId.BloodClan,
                maxHealth = 55f, attackDamage = 13f, moveSpeed = 3.8f, moneyReward = 18, weakness = DamageKind.Holy,
                tint = new Color(0.9f, 0.1f, 0.15f), scale = 0.9f,
                trait = "Pach krvi (Blood Lust)",
                note = "Lovec zrodený z pekelnej krvi, cíti ranených a ľudí s krvou."
            },

            // ================= STREDNÝ: bojovníci klanov =================
            new DemonEntry
            {
                displayName = "Enjin (popolový pazúrnik)", tier = DemonTier.Mid, clan = FactionId.AshClan,
                maxHealth = 90f, attackDamage = 20f, moveSpeed = 3.1f, moneyReward = 40, weakness = DamageKind.Lightning,
                tint = new Color(1f, 0.45f, 0.1f), scale = 1.05f,
                trait = "Zapaľuje (DoT), agresívny na blízko",
                note = "Popolavé telo, roztavená láva a plamenné pazúry. Slabina podľa GDD: voda / chladenie (zatiaľ blesk)."
            },
            new DemonEntry
            {
                displayName = "Raijin-gari (kyber-strážca)", tier = DemonTier.Mid, clan = FactionId.LightningClan,
                maxHealth = 80f, attackDamage = 18f, moveSpeed = 4.0f, moneyReward = 40, weakness = DamageKind.Earth,
                tint = new Color(0.2f, 0.6f, 1f), scale = 1.05f,
                trait = "Reťazové výboje, najrýchlejší bojovník",
                note = "Mechanické rohy a neónovo-modrá aura. Uzemnenie (zem) ho spomalí."
            },
            new DemonEntry
            {
                displayName = "Jukai (skamenený strážca)", tier = DemonTier.Mid, clan = FactionId.WildClan,
                maxHealth = 140f, attackDamage = 24f, moveSpeed = 2.2f, moneyReward = 45, weakness = DamageKind.Fire,
                tint = new Color(0.3f, 0.55f, 0.2f), scale = 1.2f,
                trait = "Tank: veľa HP, pomalý",
                note = "Rohatá beštia obrastená kôrou a mochom. Oheň zapáli kôru; dá sa obehnúť zozadu."
            },

            // ================= VYŠŠÍ: infiltrátori (cez deň v ľudskej maske, nezraniteľní voči bežným zbraniam) =================
            new DemonEntry
            {
                displayName = "Kage (šľachtic Lady Kuro)", tier = DemonTier.Higher, clan = FactionId.ShadowClan,
                maxHealth = 120f, attackDamage = 28f, moveSpeed = 3.6f, moneyReward = 70, weakness = DamageKind.Fire,
                tint = new Color(0.4f, 0.15f, 0.6f), disguiseColor = new Color(0.25f, 0.18f, 0.35f), scale = 1.1f,
                trait = "Illusion Cloak, Shadow Step",
                note = "Elegantný šľachtic v tmavom, sluha Lady Kuro. Plamene prelomia jeho ilúzie."
            },
            new DemonEntry
            {
                displayName = "Ketsu (akolyta Kňažky Carmilly)", tier = DemonTier.Higher, clan = FactionId.BloodClan,
                maxHealth = 140f, attackDamage = 26f, moveSpeed = 3.2f, moneyReward = 70, weakness = DamageKind.Holy,
                tint = new Color(0.8f, 0.05f, 0.2f), disguiseColor = new Color(0.55f, 0.1f, 0.15f), scale = 1.1f,
                trait = "Lifesteal, Illusion Cloak",
                note = "Maskovaný v červenom rúchu, lieči sa na úkor nepriateľa. Posvätené/očistné zbrane ho zasiahnu."
            },
            new DemonEntry
            {
                displayName = "Enjin (kat Lorda Ignisa)", tier = DemonTier.Higher, clan = FactionId.AshClan,
                maxHealth = 160f, attackDamage = 30f, moveSpeed = 3.0f, moneyReward = 75, weakness = DamageKind.Lightning,
                tint = new Color(1f, 0.3f, 0.05f), disguiseColor = new Color(0.7f, 0.45f, 0.2f), scale = 1.2f,
                trait = "Ohnivé výbuchy, Illusion Cloak",
                note = "Za robotníka maskovaný kat posadnutý očistným ohňom. GDD: slabý voči vode (zatiaľ blesk do jadra)."
            },
            new DemonEntry
            {
                displayName = "Raijin (technik Lorda Raizena)", tier = DemonTier.Higher, clan = FactionId.LightningClan,
                maxHealth = 130f, attackDamage = 25f, moveSpeed = 4.0f, moneyReward = 70, weakness = DamageKind.Earth,
                tint = new Color(0.2f, 0.5f, 1f), disguiseColor = new Color(0.5f, 0.6f, 0.75f), scale = 1.1f,
                trait = "Reťazový blesk, Illusion Cloak",
                note = "Maskovaný za technika. Zemné útoky ho uzemnia a na chvíľu znehybnia."
            },

            // ================= BOSS: Klan Blesku =================
            BuildLightningBoss(),
        };
        list.AddRange(BuildClanBosses());
        return list;
    }

    public const string ShadowBossName = "Lady Kuro (boss Klanu Tieňa)";
    public const string WildBossName = "General Gora (boss Klanu Divočiny)";
    public const string AshBossName = "Lord Ignis (boss Klanu Popola)";
    public const string BloodBossName = "Kňažka Carmilla (boss Krvavého klanu)";

    /// <summary>Ďalší bossovia klanov podľa Postavy.txt (body 8, 10, 11, 12). Zatiaľ so spoločným modelom démona, farbou klanu a mierkou.</summary>
    public static List<DemonEntry> BuildClanBosses()
    {
        return new List<DemonEntry>
        {
            new DemonEntry
            {
                displayName = ShadowBossName, tier = DemonTier.Boss, clan = FactionId.ShadowClan,
                maxHealth = 450f, attackDamage = 34f, moveSpeed = 4.6f, moneyReward = 280, weakness = DamageKind.Fire,
                tint = new Color(0.35f, 0.1f, 0.6f), disguiseColor = new Color(0.12f, 0.1f, 0.16f), scale = 1.3f,
                trait = "Ilúzie (klony), neviditeľnosť, rezné rany zo strán",
                note = "Mstiteľka, majsterka infiltrácie. Svetlo, ohňostroje a plamene (Klan Popola) rozbijú jej ilúzie."
            },
            new DemonEntry
            {
                displayName = WildBossName, tier = DemonTier.Boss, clan = FactionId.WildClan,
                maxHealth = 650f, attackDamage = 48f, moveSpeed = 1.9f, moneyReward = 320, weakness = DamageKind.Fire,
                tint = new Color(0.3f, 0.45f, 0.2f), disguiseColor = new Color(0.3f, 0.3f, 0.25f), scale = 1.9f,
                trait = "Zemetrasenia, balvany, 50 % brnenie, extrémne pomalý",
                note = "Skamenený tank. Oheň obchádza jeho brnenie a zapaľuje kôru; dá sa obehnúť zozadu."
            },
            new DemonEntry
            {
                displayName = AshBossName, tier = DemonTier.Boss, clan = FactionId.AshClan,
                maxHealth = 520f, attackDamage = 46f, moveSpeed = 3.3f, moneyReward = 320, weakness = DamageKind.Lightning,
                tint = new Color(1f, 0.35f, 0.05f), disguiseColor = new Color(0.4f, 0.3f, 0.25f), scale = 1.5f,
                trait = "Ohnivé výbuchy (AoE), tlakové vlny, spaľuje bojisko",
                note = "Kat Popola. Voda a chlad ho spomalia (zatiaľ ako slabina Blesk, ako ostatní z jeho klanu)."
            },
            new DemonEntry
            {
                displayName = BloodBossName, tier = DemonTier.Boss, clan = FactionId.BloodClan,
                maxHealth = 480f, attackDamage = 36f, moveSpeed = 3.8f, moneyReward = 300, weakness = DamageKind.Holy,
                tint = new Color(0.75f, 0.03f, 0.12f), disguiseColor = new Color(0.4f, 0.1f, 0.12f), scale = 1.3f,
                trait = "Lifesteal, krvavé kaluže, vysáva na diaľku",
                note = "Kňažka rituálov. Očistné séra a jed obrátia jej lifesteal proti nej."
            }
        };
    }

    public const string LightningBossName = "Lord Raizen (boss Klanu Blesku)";

    /// <summary>Boss Klanu Blesku podľa GDD (Postavy.txt, bod 7). Model a animácie mu priradí menu Progeny → Démoni → Pripraviť bossa Raizena.</summary>
    public static DemonEntry BuildLightningBoss()
    {
        return new DemonEntry
        {
            displayName = LightningBossName, tier = DemonTier.Boss, clan = FactionId.LightningClan,
            maxHealth = 400f, attackDamage = 40f, moveSpeed = 4.8f, moneyReward = 300, weakness = DamageKind.Earth,
            tint = Color.white, keepModelMaterials = true, disguiseColor = new Color(0.45f, 0.55f, 0.8f), scale = 1.35f,
            trait = "Reťazové výboje, extrémna rýchlosť, Illusion Cloak",
            note = "Líder Klanu Blesku, Arakaniho pravá ruka. Kyber-démon s rohom z čistého blesku. Uzemnenie (zem / Divočina) ho skratuje a na pár sekúnd znehybní."
        };
    }
}
