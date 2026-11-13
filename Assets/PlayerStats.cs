using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PlayerForm { Human, Demon }
public enum DamageSource { Normal, Demon, Fall, Starvation }

/// <summary>
/// Potreby postavy + premena Človek <-> Démon (GDD kap. 4).
/// Človek: HP, výdrž, hlad. Démon: HP, výdrž, krv.
/// Levely sa pri dlhom hraní jednej formy znižujú tej druhej.
/// </summary>
public class PlayerStats : MonoBehaviour
{
    public static event Action<PlayerForm> OnFormChanged;
    public event Action OnDied;

    [Header("Forma (rozhoduje hra, nie hráč)")]
    public PlayerForm startForm = PlayerForm.Human;

    [Header("Peniaze")]
    public int money = 100;

    [Header("Zdravie a výdrž")]
    public float maxHealth = 100f;
    public float maxStamina = 100f;
    public float staminaRegenPerSecond = 15f;
    public float staminaRegenDelay = 1f;
    [Tooltip("Násobiteľ spotreby výdrže (šprint, skok, útok, dash). Menej = vydrží dlhšie. Platí aj nad hodnotami uloženými v scéne.")]
    public float staminaCostMultiplier = 0.65f;
    [Tooltip("Násobiteľ regenerácie výdrže")]
    public float staminaRegenMultiplier = 1.3f;
    [Tooltip("Hráč sa počas úhybu (dash) nedá zraniť do tohto času")]
    public float InvulnerableUntil;
    /// <summary>Krv titanov: do tohto času dvojnásobné poškodenie.</summary>
    [System.NonSerialized] public float RageUntil;
    [Tooltip("Koľko poškodenia prejde počas bloku (0.35 = len 35 %)")]
    public float blockDamageFactor = 0.35f;
    /// <summary>Hráč drží blok (nastavuje PlayerWeapons).</summary>
    [System.NonSerialized] public bool IsBlocking;
    public float healthRegenPerSecond = 0.5f;   // len keď je postava nasýtená

    [Header("Človek: hlad")]
    public float maxHunger = 100f;
    [Tooltip("Za koľko sekúnd klesne hlad zo 100 % na 25 %")]
    public float needSecondsToQuarter = 360f;
    [Tooltip("Úbytok za sekundu pod 25 % (pre hlad aj krv), o trochu rýchlejší")]
    public float needLossBelowQuarter = 0.3f;
    public float starvationDamagePerSecond = 2f;

    [Header("Démon: krv")]
    public float maxBlood = 100f;
    [Range(0f, 1f)] public float weakenedBelow = 0.25f;
    [Tooltip("Keď krv klesne pod túto hodnotu, démonické vnímanie (E) sa samo zapne. Postih za nízku krv ostáva.")]
    public float autoVisionBelowBlood = 15f;

    [Header("Bonusy foriem")]
    public float demonSpeedMultiplier = 1.25f;
    public float demonDamageMultiplier = 1.5f;
    [Tooltip("Postih, keď má démon málo krvi alebo človek hladuje")]
    public float weakenedFactor = 0.6f;

    [Header("Levely")]
    public int humanLevel = 1;
    public int demonLevel = 1;
    public int minLevel = 1;
    [Tooltip("Po koľkých sekundách v jednej forme klesne level tej druhej")]
    public float levelDecaySeconds = 900f;

    [Header("Boj (sem sa napoja frakcie a klany)")]
    [Tooltip("1 = normálne, 0.85 = brnenie znižuje prijaté poškodenie o 15 %")]
    public float damageTakenMultiplier = 1f;
    [Tooltip("Pripočíta sa k základnej šanci na kritický zásah (0.1 = +10 %)")]
    public float critChanceBonus = 0f;
    public float attackSpeedMultiplier = 1f;
    [Tooltip("Podiel spôsobeného poškodenia, ktorý sa vráti ako liečenie (0.1 = 10 %)")]
    public float lifestealFraction = 0f;

    [Header("Skúsenosti (démon)")]
    public int demonXp = 0;
    public int xpPerLevel = 100;
    [Header("Skill tree")]
    public int skillPoints;
    [Tooltip("Body duše (menu na klávese 0): dostávaš ich za každý nový level človeka alebo démona; platí sa nimi za perky Duše, nie za strom schopností")]
    public int soulPoints;
    public int skillLevel = 1;
    public int skillXp;
    public int skillXpPerLevel = 100;


    public float Health { get; private set; }
    public float Stamina { get; private set; }
    public float Hunger { get; private set; }
    public float Blood { get; private set; }
    public PlayerForm CurrentForm { get; private set; }
    public bool IsDemon => CurrentForm == PlayerForm.Demon;
    public bool IsDead { get; private set; }

    /// <summary>Max HP vrátane frakčných bonusov a postihov.</summary>
    public float MaxHealth => (maxHealth + SpecialGoods.MaxHpBonus) * (faction != null ? faction.MaxHpMult : 1f) * ArakaniFavor.MaxHpFactor * (1f + SkillTree.Sum(SkillStat.MaxHp));

    public float NeedRatio => IsDemon ? Blood / maxBlood : Hunger / maxHunger;

    float ConditionFactor => IsDemon
        ? (Blood / maxBlood < weakenedBelow ? weakenedFactor : 1f)
        : (Hunger <= 0f ? weakenedFactor : 1f);

    public float SpeedMultiplier => (IsDemon ? demonSpeedMultiplier * Lair.SpeedFactor : 1f) * ConditionFactor
                                    * (senses != null ? senses.SpeedMultiplier : 1f)
                                    * (faction != null ? faction.SpeedMult : 1f)
                                    * TempSpeed * SpecialGoods.SpeedFactor * (1f + SkillTree.Sum(SkillStat.MoveSpeed));
    public float DamageMultiplier => (IsDemon ? demonDamageMultiplier * Lair.DamageFactor : 1f) * ConditionFactor
                                     * (senses != null ? senses.StrengthMultiplier : 1f)
                                     * (faction != null ? faction.DamageMult : 1f)
                                     * (1f + SkillTree.Sum(SkillStat.Damage)) * SoulBalance.DamageFactor(IsDemon) * SpecialGoods.DamageFactor
                                     * ArakaniFavor.DamageFactor * (Time.time < RageUntil ? 2f : 1f);
    public float InvestigationTimeMultiplier => SkillTree.InvestigationFactor;
    public int EffectiveMaxJumps => 1 + (SkillTree.Sum(SkillStat.ExtraJump) > 0f ? 1 : 0);
    public float WeaponRangeMultiplier => 1f + SkillTree.Sum(SkillStat.WeaponReach);

    // Bonusy zo stromu schopností k hodnotám z frakcie / klanu
    public float CritBonusTotal => critChanceBonus + SkillTree.Sum(SkillStat.Crit);
    public float LifestealTotal => lifestealFraction + SkillTree.Sum(SkillStat.Lifesteal);
    public float AttackSpeedTotal => attackSpeedMultiplier * (1f + SkillTree.Sum(SkillStat.AttackSpeed));

    /// <summary>Efektivita výdrže: viac = pomalšie míňanie a rýchlejší návrat.</summary>
    public float StaminaEfficiency =>
        (senses != null ? Mathf.Max(0.1f, senses.StaminaMultiplier) : 1f)
        * (faction != null ? faction.StaminaMult : 1f)
        * ArakaniFavor.StaminaFactor * SpecialGoods.StaminaFactor * (1f + SkillTree.Sum(SkillStat.Stamina));

    private PerceptionToggle perception;
    private DemonSenses senses;
    private PlayerFaction faction;
    private float lastStaminaUse = -999f;
    private bool exhausted;
    private float formTimer;
    private bool autoVisionDone;

    void Start()
    {
        LoadSkillTree();
        Health = MaxHealth;
        Stamina = maxStamina;
        Hunger = maxHunger;
        Blood = maxBlood * 0.6f;

        perception = FindAnyObjectByType<PerceptionToggle>();
        if (GetComponent<DemonLeap>() == null) gameObject.AddComponent<DemonLeap>();
        senses = GetComponent<DemonSenses>();
        faction = GetComponent<PlayerFaction>();
        ApplyForm(startForm);
    }

    void Update()
    {
        if (IsDead)
        {
            if (GameInput.GetKeyDown(KeyCode.R))
                PauseMenu.ReturnToMainMenu();
            return;
        }

        float dt = Time.deltaTime;

        Health = Mathf.Min(Health, MaxHealth);   // postih z frakcie môže znížiť max HP
        UpdateStamina(dt);
        UpdateNeeds(dt);
        CheckAutoVision();
        UpdateLevelDecay(dt);

    }

    // ---------- Výdrž ----------
    void UpdateStamina(float dt)
    {
        if (Time.time - lastStaminaUse > staminaRegenDelay)
            Stamina = Mathf.Min(maxStamina, Stamina + staminaRegenPerSecond * staminaRegenMultiplier * StaminaEfficiency * dt);

        if (exhausted && Stamina >= maxStamina * 0.2f)
            exhausted = false;
    }

    /// <summary>Lektvar výdrže: doplní výdrž a zruší vyčerpanie.</summary>
    public void RestoreStamina(float amount)
    {
        Stamina = Mathf.Min(maxStamina, Stamina + amount);
        if (Stamina >= maxStamina * 0.2f) exhausted = false;
    }

    /// <summary>Spotrebuje výdrž. Vráti false, ak je postava vyčerpaná.</summary>
    public bool UseStamina(float amount)
    {
        if (exhausted || Stamina <= 0f) return false;

        Stamina = Mathf.Max(0f, Stamina - amount * staminaCostMultiplier * (IsDemon ? SoulBalance.DemonStaminaFactor : 1f));
        lastStaminaUse = Time.time;
        if (Stamina <= 0f) exhausted = true;
        return true;
    }

    // ---------- Hlad / krv ----------
    // Rovnaká krivka pre hlad aj krv: 100 -> 25 rovnomerne, potom rýchlejšie
    float NeedLoss(float value, float max) =>
        (value > max * 0.25f ? max * 0.75f / Mathf.Max(1f, needSecondsToQuarter) : needLossBelowQuarter) * Mathf.Clamp(1f - SkillTree.Sum(SkillStat.NeedsDrain), 0.3f, 1f);

    void UpdateNeeds(float dt)
    {
        if (IsDemon)
        {
            Blood = Mathf.Max(0f, Blood - NeedLoss(Blood, maxBlood) * (senses != null ? senses.BloodDrainMultiplier : 1f) * ArakaniFavor.BloodDrainFactor * Lair.BloodDrainFactor * SpecialGoods.NeedsDrainFactor * dt);
            if (Blood <= 0f)
                TakeDamage(starvationDamagePerSecond * dt, DamageSource.Starvation);
        }
        else
        {
            Hunger = Mathf.Max(0f, Hunger - NeedLoss(Hunger, maxHunger) * SpecialGoods.NeedsDrainFactor * dt);
            if (Hunger <= 0f)
                TakeDamage(starvationDamagePerSecond * dt, DamageSource.Starvation);
        }

        if (!IsDead && NeedRatio > 0.5f)
            Health = Mathf.Min(MaxHealth, Health + healthRegenPerSecond * (1f + SkillTree.Sum(SkillStat.HpRegen)) * dt);
    }

    // Pri veľmi nízkej krvi sa samo zapne démonické vnímanie (E). Zapne sa len raz po poklese,
    // hráč ho môže hneď vypnúť. Pri premene na démona sa nezapína (to je na hráčovi).
    void CheckAutoVision()
    {
        if (!IsDemon || perception == null) return;

        if (Blood >= autoVisionBelowBlood)
        {
            autoVisionDone = false;
            return;
        }
        if (autoVisionDone) return;

        autoVisionDone = true;
        if (!PerceptionToggle.IsDemonVision)
        {
            perception.SetDemonVision(true);
            GameHUD.Message("Hlad po krvi ti zaostril zmysly - démonické vnímanie sa zapne.", 3f);
        }
    }

    public void Eat(float amount)
    {
        if (IsDemon) return;    // démon sa živí krvou
        Hunger = Mathf.Min(maxHunger, Hunger + amount * (1f + SkillTree.Sum(SkillStat.Nutrition)));
    }

    public void DrinkBlood(float amount)
    {
        if (!IsDemon) return;
        Blood = Mathf.Min(maxBlood, Blood + amount * (1f + SkillTree.Sum(SkillStat.Nutrition)));
    }

    /// <summary>Scénar (prológ): nastaví zdravie bez smrti a premeny.</summary>
    public void SetHealthScripted(float value) { if (!IsDead) Health = Mathf.Clamp(value, 0f, MaxHealth); }

    // ---------- Zranenie ----------
    public void TakeDamage(float amount, DamageSource source = DamageSource.Normal,
                           FactionId killerClan = FactionId.None)
    {
        if (IsDead || amount <= 0f) return;
        if (source != DamageSource.Starvation && Time.time < InvulnerableUntil) return;   // úhyb (dash)

        if (source != DamageSource.Starvation)
            amount *= damageTakenMultiplier * (IsDemon ? Lair.TakenFactor : 1f) * SpecialGoods.TakenFactor * SkillTree.ArmorFactor;
            if (source == DamageSource.Demon) amount *= Mathf.Clamp(1f - SkillTree.Sum(SkillStat.DemonHitArmor), 0.3f, 1f);

        // Blok (pravé tlačidlo myši s vytasenou zbraňou) zníži prijaté poškodenie
        if (IsBlocking && source != DamageSource.Starvation)
            amount *= blockDamageFactor;

        Health -= amount;
        if (source == DamageSource.Normal || source == DamageSource.Demon) CombatFx.PlayerHurt(amount);
        if (Health > 0f) return;

        Health = 0f;

        // Hrateľný prológ: smrť počas prepadu spustí scénu smrti a prebudenia (hráč sa nezabije naozaj)
        if (Prologue.TryIntercept())
        {
            Health = 1f;
            InvulnerableUntil = Time.time + 180f;
            return;
        }

        // Smrť od démona = premena (pád ani hlad premenu nespôsobia)
        if (CurrentForm == PlayerForm.Human && source == DamageSource.Demon)
        {
            Debug.Log("SMRTEĽNÉ ZRANENIE OD DÉMONA: Telo sa mení...");
            ApplyForm(PlayerForm.Demon);
            if (faction != null) faction.SetFaction(killerClan);   // klan démona, ktorý ťa zabil
            Health = MaxHealth * 0.4f;
            return;
        }

        Die();
    }

    void Die()
    {
        IsDead = true;
        Debug.Log("POSTAVA ZOMREL/A. (R = návrat do hlavného menu)");

        var movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.enabled = false;

        var rb = GetComponent<Rigidbody>();
        if (rb != null) rb.linearVelocity = Vector3.zero;

        OnDied?.Invoke();
    }

    // ---------- Premena (rozhoduje hra) ----------
    /// <summary>Lekárka: vylieči démona na človeka a nastaví ľudskú frakciu (alebo ju len zmení človeku).</summary>
    public bool TryCureAtHealer(int cost, FactionId humanFaction)
    {
        if (IsDead) return false;

        // Človek môže len zmeniť frakciu na inú
        if (!IsDemon && faction != null && faction.Current == humanFaction) return false;

        // Po Rituáli Pekelnej Krvi sa už nikdy nevrátiš medzi ľudí
        if (IsDemon && ArakaniFavor.BloodSealed)
        {
            GameHUD.Message("Prastará Krv ťa viaže navždy. Lekárka ti nepomôže.");
            return false;
        }

        if (!TrySpend(cost))
        {
            Debug.Log($"Nemáš dosť peňazí (treba {cost}, máš {money}).");
            return false;
        }

        if (IsDemon)
        {
            ApplyForm(PlayerForm.Human);
            Hunger = maxHunger * 0.5f;
            Health = Mathf.Max(Health, MaxHealth * 0.6f);
            ArakaniFavor.NotifyCured();
        }

        if (faction != null) faction.SetFaction(humanFaction);
        return true;
    }

    public bool TrySpend(int amount)
    {
        if (money < amount) return false;
        money -= amount;
        return true;
    }

    public void AddMoney(int amount) => money += amount;

    public void ForceArakaniTransformation()
    {
        if (IsDead || IsDemon) return;
        ApplyForm(PlayerForm.Demon);
        if (faction != null) faction.SetFaction(FactionId.ShadowClan);
        Blood = Mathf.Max(Blood, maxBlood * 0.4f);
        Health = Mathf.Min(MaxHealth, Mathf.Max(Health, MaxHealth * 0.4f));
        GameHUD.Message("Arakani: Tvoje telo sa napokon poddalo krvi.", 5f);
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        Health = Mathf.Min(MaxHealth, Health + amount);
    }

    void ApplyForm(PlayerForm newForm)
    {
        CurrentForm = newForm;
        SkillTree.Demon = newForm == PlayerForm.Demon;   // bonusy „iba démon“ platia len v jeho podobe
        formTimer = 0f;

        // Ako človek sa démonické vnímanie vypne. Pri premene na démona sa samo nezapína (E je na hráčovi).
        if (perception != null && newForm == PlayerForm.Human) perception.SetDemonVision(false);

        // Ak je krv pri premene už nízko, automatika sa spustí až po novom poklese
        autoVisionDone = Blood < autoVisionBelowBlood;

        Debug.Log(newForm == PlayerForm.Demon
            ? "PREMENA: Si DÉMON. Krv je tvoj hlad."
            : "PREMENA: Si ČLOVEK. Démonické schopnosti slabnú.");

        OnFormChanged?.Invoke(newForm);
    }

    // ---------- Levely ----------
    /// <summary>Za koľko sekúnd klesne o 1 level opačnej formy (démon: ľudský level; človek: démonický). 0, ak už je na minime.</summary>
    public float LevelDecayLeft => (IsDemon ? humanLevel : demonLevel) > minLevel ? Mathf.Max(0f, levelDecaySeconds - formTimer) : 0f;

    void UpdateLevelDecay(float dt)
    {
        formTimer += dt;
        if (formTimer < levelDecaySeconds) return;
        formTimer = 0f;

        if (IsDemon && humanLevel > minLevel)
        {
            humanLevel--;
            GameHUD.Message($"Ľudský level klesol na {humanLevel}: príliš dlho si démon.", 4f);
            Debug.Log($"Ľudská úroveň klesla na {humanLevel}");
        }
        else if (!IsDemon && demonLevel > minLevel)
        {
            demonLevel--;
            GameHUD.Message($"Démonický level klesol na {demonLevel}: príliš dlho si človek.", 4f);
            Debug.Log($"Démonická úroveň klesla na {demonLevel}");
        }
    }

    // ---------- Levely: krivka stúpa, každý ďalší level potrebuje viac XP; strop MaxLevel ----------
    public const int MaxLevel = 60;

    /// <summary>XP potrebné na prechod z levelu na ďalší: ~114 (lv 1), ~430 (lv 10), ~925 (lv 20), ~1570 (lv 30), ~3330 (lv 50). `xpPerLevel` v Inspectore škáluje celú krivku (100 = predvolene).</summary>
    public static int XpForLevel(int level, int scale)
    {
        int l = Mathf.Max(1, level);
        float baseXp = (40f + 12f * l + 0.35f * l * l) * 2.2f;   // pomalšie levely: treba farmiť, stavať, pomáhať aj skúmať
        return Mathf.Max(1, Mathf.RoundToInt(baseXp * Mathf.Max(1, scale) / 100f));
    }

    /// <summary>XP do ďalšieho levelu schopností; 0, keď je dosiahnutý strop.</summary>
    public int SkillXpToNext => skillLevel >= MaxLevel ? 0 : XpForLevel(skillLevel, skillXpPerLevel);
    public int DemonXpToNext => demonLevel >= MaxLevel ? 0 : XpForLevel(demonLevel, xpPerLevel);

    /// <summary>XP podľa aktuálnej formy: démon dostane démonické (aj skill XP), človek skill XP.</summary>
    public void AddXp(int amount)
    {
        if (IsDemon) AddDemonXp(amount); else AddSkillXp(amount);
    }

    public void AddDemonXp(int amount)
    {
        if (amount > 0 && demonLevel < MaxLevel)
        {
            demonXp += amount;
            while (demonLevel < MaxLevel && demonXp >= XpForLevel(demonLevel, xpPerLevel))
            {
                demonXp -= XpForLevel(demonLevel, xpPerLevel);
                demonLevel++;
                soulPoints++;
                Debug.Log($"Démonická úroveň stúpla na {demonLevel}!");
            }
            if (demonLevel >= MaxLevel) demonXp = 0;
        }
        AddSkillXp(amount);
    }

    public void AddSkillXp(int amount)
    {
        if (amount <= 0 || skillLevel >= MaxLevel) return;
        skillXp += amount;
        while (skillLevel < MaxLevel && skillXp >= XpForLevel(skillLevel, skillXpPerLevel))
        {
            skillXp -= XpForLevel(skillLevel, skillXpPerLevel);
            skillLevel++;
            int gained = skillLevel % 10 == 0 ? 2 : 1;   // každý desiaty level je bonusový skill point
            skillPoints += gained;
            GameHUD.Message($"Level schopností {skillLevel}! Skill pointy +{gained} (spolu {skillPoints})", 3.5f);
        }
        if (skillLevel >= MaxLevel) skillXp = 0;
        SaveSkillTree();
    }

    /// <summary>Odomkne uzol stromu schopností (po poradí, level a body); vráti dôvod, ak sa nedá.</summary>
    public SkillTree.Reason TryUnlockSkill(string id)
    {
        SkillTree.Reason reason = SkillTree.CanUnlock(id, skillLevel, skillPoints);
        if (reason != SkillTree.Reason.Ok) return reason;

        skillPoints -= SkillTree.Get(id).cost;
        SkillTree.Unlock(id);
        SaveSkillTree();
        return reason;
    }

    /// <summary>Zruší všetky odomknuté schopnosti stromu a vráti ich body.</summary>
    public void RefundSkills()
    {
        skillPoints += SkillTree.SpentPoints();
        SkillTree.Clear();
        SaveSkillTree();
    }

    private float soulProgress;

    public float FormTimerValue { get => formTimer; set => formTimer = Mathf.Max(0f, value); }
    public float SoulProgressValue { get => soulProgress; set => soulProgress = Mathf.Clamp(value, 0f, 0.99f); }

    /// <summary>Priebežné body duše: starostlivosť o prírodu, pomoc ľuďom, dokončené stavby. Každý celý 1,0 je bod duše navyše k bodom za level.</summary>
    public void AddSoulProgress(float amount)
    {
        soulProgress += amount;
        while (soulProgress >= 1f)
        {
            soulProgress -= 1f;
            soulPoints++;
            GameHUD.Message($"Bod duše +1 (spolu {soulPoints})", 3f);
            SaveSkillTree();
        }
    }

    public bool TrySpendSoulPoint()
    {
        if (soulPoints <= 0) return false;
        soulPoints--;
        SaveSkillTree();
        return true;
    }

    /// <summary>Schopnosti, level schopností a body duše z uloženej hry (slot): nahradia hodnoty z posledného hrania.</summary>
    public void ImportSkills(int level, int xp, int points, int soul, string nodes)
    {
        skillLevel = Mathf.Clamp(level, 1, MaxLevel);
        skillXp = xp;
        skillPoints = points;
        soulPoints = soul;
        SkillTree.Import(nodes);
        SaveSkillTree();
    }

    public bool TrySpendSkillPoint()
    {
        if (skillPoints <= 0) return false;
        skillPoints--;
        SaveSkillTree();
        return true;
    }

    void SaveSkillTree()
    {
        PlayerPrefs.SetInt("POA.SkillLevel", skillLevel);
        PlayerPrefs.SetInt("POA.SkillXp", skillXp);
        PlayerPrefs.SetInt("POA.SkillPoints", skillPoints);
        PlayerPrefs.SetInt("POA.SoulPoints", soulPoints);
        SkillTree.Save();
        PlayerPrefs.Save();
    }

    void LoadSkillTree()
    {
        skillLevel = PlayerPrefs.GetInt("POA.SkillLevel", skillLevel);
        skillXp = PlayerPrefs.GetInt("POA.SkillXp", skillXp);
        skillPoints = PlayerPrefs.GetInt("POA.SkillPoints", skillPoints);
        soulPoints = PlayerPrefs.GetInt("POA.SoulPoints", soulPoints);
        SkillTree.Load();
    }

    public void GainLevel(PlayerForm form, int amount = 1)
    {
        if (form == PlayerForm.Human) humanLevel = Mathf.Min(MaxLevel, humanLevel + amount);
        else demonLevel = Mathf.Min(MaxLevel, demonLevel + amount);
        soulPoints += amount;
    }


    // ---------- Pomocné pre schopnosti a HUD ----------
    public bool IsWeakened => ConditionFactor < 1f;

    public bool TrySpendBlood(float amount)
    {
        if (Blood < amount) return false;
        Blood -= amount;
        return true;
    }

    private float guaranteedCritUntil;

    /// <summary>Najbližší zásah v danom čase bude kritický (Tieniaci skok).</summary>
    public void ArmGuaranteedCrit(float seconds) => guaranteedCritUntil = Time.time + seconds;

    public bool ConsumeGuaranteedCrit()
    {
        if (Time.time >= guaranteedCritUntil) return false;
        guaranteedCritUntil = 0f;
        return true;
    }

    // ---------- Neviditeľnosť (Klan Tieňa) ----------
    private float invisibleUntil;
    public bool IsInvisible => Time.time < invisibleUntil;
    public float InvisibleLeft => Mathf.Max(0f, invisibleUntil - Time.time);

    /// <summary>Nepriatelia ťa nevidia (prestanú ťa prenasledovať a nezačnú).</summary>
    public void MakeInvisible(float seconds) => invisibleUntil = Time.time + seconds;

    // ---------- Overclock (Rebelanti) ----------
    private float overclockUntil;
    private float overclockAttack;
    private float overclockSpeed;

    public bool OverclockActive => Time.time < overclockUntil;
    public float OverclockLeft => Mathf.Max(0f, overclockUntil - Time.time);
    public float TempAttackSpeed => OverclockActive ? 1f + overclockAttack : 1f;
    public float TempSpeed => OverclockActive ? 1f + overclockSpeed : 1f;

    public void StartOverclock(float seconds, float attackBonus, float speedBonus)
    {
        overclockUntil = Time.time + seconds;
        overclockAttack = attackBonus;
        overclockSpeed = speedBonus;
    }
}