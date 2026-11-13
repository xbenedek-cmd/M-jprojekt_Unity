using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Základný boj hráča: ľavé tlačidlo myši = útok pred sebou.
/// Zbraň (poškodenie, dosah, výdrž, typ poškodenia) sa berie z inventára cez PlayerWeapons; bez zbrane útočia päste.
/// Poškodenie = základ x násobiteľ zbrane/variantu x násobiteľ formy/zmyslov/frakcie. Šanca na kritický zásah, lifesteal
/// a rýchlosť útoku sa berú z PlayerStats (nastavuje ich frakcia).
/// Variant útoku (0 úder, 1 elbow, 2 kop) určuje animáciu aj silu úderu.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerCombat : MonoBehaviour
{
    [Header("Útok")]
    public int attackMouseButton = 0;           // 0 = ľavé tlačidlo
    public float baseDamage = 20f;
    public float attackRange = 2.2f;
    [Range(10f, 180f)] public float attackAngle = 70f;
    public float attackCooldown = 0.55f;
    public float staminaCost = 12f;
    [Tooltip("Zrýchlenie útokov (1.25 = o 25 % častejšie). Platí nad hodnotou cooldownu uloženou v scéne.")]
    public float attackSpeedBoost = 1.25f;
    public KeyCode weaponToggleKey => KeyBindings.Get("weapon", KeyCode.Q);

    [Header("Dĺžka a reťazenie útokov")]
    [Tooltip("Najdlhšie trvá animácia útoku (s); dlhšie klipy sa zrýchlia, kratšie idú normálne. Ďalší útok nasleduje až po jej konci.")]
    public float attackAnimSeconds = 0.9f;
    [Tooltip("Klik počas útoku sa zaradí, ak do konca animácie zostáva najviac toľko sekúnd; ďalší útok potom začne hneď po jej skončení.")]
    public float comboBuffer = 0.5f;

    [Header("Varianty útoku")]
    [Tooltip("Bez zbrane: šanca na elbow (silnejší výpad)")]
    [Range(0f, 1f)] public float elbowChance = 0.3f;
    [Tooltip("Bez zbrane: šanca na kop")]
    [Range(0f, 1f)] public float kickChance = 0.25f;
    public float elbowDamageMultiplier = 1.35f;
    [Tooltip("Silnejší výpad sa po údere \"dobíja\" dlhšie")]
    public float elbowCooldownMultiplier = 1.2f;
    public float kickDamageMultiplier = 1.2f;
    public float kickCooldownMultiplier = 1.1f;

    [Header("Ľahký a ťažký útok (len s mečom)")]
    [Tooltip("Ako dlho treba držať ľavé tlačidlo na ťažký útok (s); kratší klik = ľahký")]
    public float heavyHoldSeconds = 0.3f;
    public float heavyDamageMultiplier = 1.8f;
    public float heavyStaminaMultiplier = 1.7f;
    public float heavyCooldownMultiplier = 1.3f;
    [Tooltip("Ťažký útok trvá toľkokrát dlhšie než ľahký (attackAnimSeconds)")]
    public float heavyAnimFactor = 1.4f;

    [Header("Neozbrojený útok (len ak nie je PlayerWeapons)")]
    [Tooltip("Poškodenie pri útoku rukami, relatívne k bežnému meču.")]
    public float unarmedDamageMultiplier = 0.45f;
    [Tooltip("Násobok poškodenia úderov v podobe démona (bez zbrane); 1 = ako meč")]
    public float demonPunchMultiplier = 1.8f;
    [Tooltip("Násobok poškodenia, ktorým démon búra stavby")]
    public float demonBuildingFactor = 2.5f;
    public float unarmedRange = 1.5f;

    [Header("Kritický zásah")]
    [Range(0f, 1f)] public float baseCritChance = 0.1f;
    public float critMultiplier = 2f;

    [Header("Odkazy")]
    public Transform cameraTransform;           // prázdne = Main Camera

    private struct WeaponStats
    {
        public bool armed;
        public float damage, range, stamina, cooldown;
        public DamageKind kind;
    }

    private PlayerStats stats;
    private PlayerMovement movement;
    private ClanOnHit clanOnHit;
    private PlayerWeapons weapons;
    private DemonModelAnimator modelAnimator;
    private ChopAnimation chopAnimation;
    private bool attackQueued;
    private float nextAttackTime;
    private readonly Collider[] hitBuffer = new Collider[32];
    private readonly HashSet<Damageable> done = new HashSet<Damageable>();
    private GameObject sword;
    private bool weaponEquipped = true;

    private int attackVariant;
    private bool attackIsHeavy;
    private bool queuedHeavy;
    private bool holding, heavyFired;
    private float holdStart;
    private float attackAnimUntil;

    /// <summary>0 = bežný úder, 1 = silnejší výpad (Elbow Punch), 2 = kop. Číta ho animátor postavy.</summary>
    public int AttackVariant => attackVariant;
    /// <summary>Posledný útok bol ťažký (dlhší klik s mečom). Číta ho animátor postavy.</summary>
    public bool AttackIsHeavy => attackIsHeavy;
    /// <summary>Krátky signál, že hráč práve zaútočil (číta ho animátor postavy).</summary>
    public bool IsAttackAnimating => Time.time < attackAnimUntil;

    public void UpgradeWeapon(float damageBonus, float rangeBonus)
    {
        baseDamage += Mathf.Max(0f, damageBonus);
        attackRange += Mathf.Max(0f, rangeBonus);
    }

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        movement = GetComponent<PlayerMovement>();
        clanOnHit = GetComponent<ClanOnHit>();
        weapons = GetComponent<PlayerWeapons>();
        PlayerVisual visual = GetComponent<PlayerVisual>();
        modelAnimator = visual != null ? visual.ModelAnimator : null;
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        // Bez systému zbraní ostáva provizórny meč z primitív
        if (weapons == null) BuildSword();
    }

    void Update()
    {
        if (stats.IsDead || Healer.MenuOpen || SoulMenu.MenuOpen || LairMenu.MenuOpen || BuyerNPC.MenuOpen || MangaDialog.Active || WanderingMerchant.MenuOpen) return;

        if (GameInput.GetKeyDown(weaponToggleKey))
        {
            if (weapons != null) weapons.ToggleDrawn();
            else
            {
                weaponEquipped = !weaponEquipped;
                if (sword != null) sword.SetActive(weaponEquipped);
            }
        }
        if (Cursor.lockState != CursorLockMode.Locked) return;   // klik len zamkne kurzor
        if (weapons != null && (weapons.IsBusy || weapons.IsBlocking))   // vyťahuje sa zbraň / drží sa blok (pravé tlačidlo)
        {
            holding = false;
            return;
        }

        if (GameInput.GetMouseButtonDown(attackMouseButton))
        {
            // Čupnutý hráč zozadu na nič netušiaci cieľ: tiché zabitie namiesto bežného útoku
            PlayerStealth stealth = PlayerStealth.Instance;
            if (stealth != null && stealth.TryTakedown()) return;

            // S mečom rozhoduje dĺžka kliku: krátky = ľahký, dlhší = ťažký; bez zbrane útok hneď
            if (weapons != null && weapons.IsArmed)
            {
                holding = true;
                heavyFired = false;
                holdStart = Time.time;
            }
            else RequestAttack(false);
        }

        if (holding)
        {
            if (!GameInput.GetMouseButton(attackMouseButton))
            {
                holding = false;
                if (!heavyFired) RequestAttack(false);   // klik skončil pred limitom: ľahký útok
            }
            else if (!heavyFired && Time.time - holdStart >= heavyHoldSeconds)
            {
                heavyFired = true;
                RequestAttack(true);                     // tlačidlo držané dosť dlho: ťažký útok
            }
        }

        if (attackQueued && Time.time >= nextAttackTime)
        {
            attackQueued = false;
            TryAttack(queuedHeavy);
        }
    }

    void RequestAttack(bool heavy)
    {
        if (Time.time >= nextAttackTime) TryAttack(heavy);
        else if (nextAttackTime - Time.time <= comboBuffer)   // zaradí sa hneď po skončení animácie
        {
            attackQueued = true;
            queuedHeavy = heavy;
        }
    }

    void BuildSword()
    {
        // Ruka môže byť aj hlboko v hierarchii (PlayerVisual ju pripevní na kosť pravej ruky modelu)
        Transform hand = null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "Hand_R") continue;
            hand = t;
            break;
        }
        if (hand == null)
        {
            GameObject handObject = new GameObject("Hand_R");
            handObject.transform.SetParent(transform, false);
            handObject.transform.localPosition = new Vector3(0.45f, 0.9f, 0.35f);
            hand = handObject.transform;
        }

        sword = new GameObject("PlayerSword");
        sword.transform.SetParent(hand, false);
        sword.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        sword.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);

        GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(blade.GetComponent<Collider>());
        blade.name = "Blade";
        blade.transform.SetParent(sword.transform, false);
        blade.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        blade.transform.localScale = new Vector3(0.08f, 0.7f, 0.12f);
        blade.GetComponent<Renderer>().material.color = new Color(0.75f, 0.8f, 0.88f);

        GameObject grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(grip.GetComponent<Collider>());
        grip.name = "Grip";
        grip.transform.SetParent(sword.transform, false);
        grip.transform.localPosition = new Vector3(0f, -0.15f, 0f);
        grip.transform.localScale = new Vector3(0.09f, 0.22f, 0.09f);
        grip.GetComponent<Renderer>().material.color = new Color(0.18f, 0.1f, 0.06f);

        GameObject guard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(guard.GetComponent<Collider>());
        guard.name = "Guard";
        guard.transform.SetParent(sword.transform, false);
        guard.transform.localScale = new Vector3(0.35f, 0.06f, 0.1f);
        guard.GetComponent<Renderer>().material.color = new Color(0.8f, 0.55f, 0.15f);
    }

    // Zbraň v ruke z inventára; bez systému zbraní sa použijú polia tohto komponentu
    WeaponStats CurrentWeapon()
    {
        if (weapons != null)
        {
            ItemDef w = weapons.Current;
            // Démon bez zbrane: výrazne silnejšie a dlhšie údery
            if (!weapons.IsArmed && stats != null && stats.IsDemon)
                return new WeaponStats
                {
                    armed = false, damage = Mathf.Max(w.damageMultiplier, 1f) * demonPunchMultiplier, range = w.range * 1.2f,
                    stamina = w.staminaCost, cooldown = w.cooldownMultiplier, kind = w.damageKind
                };

            return new WeaponStats
            {
                armed = weapons.IsArmed, damage = w.damageMultiplier, range = w.range, stamina = w.staminaCost,
                cooldown = w.cooldownMultiplier, kind = w.damageKind
            };
        }

        return new WeaponStats
        {
            armed = weaponEquipped, damage = weaponEquipped ? 1f : unarmedDamageMultiplier,
            range = weaponEquipped ? attackRange : unarmedRange, stamina = staminaCost, cooldown = 1f, kind = DamageKind.Normal
        };
    }

    // S mečom sa varianty striedajú rovnomerne (3 útoky meča); bez zbrane úder / elbow / kop podľa šancí
    int RollVariant(bool armed)
    {
        if (armed) return Random.Range(0, 3);

        // Za pohybu (chôdza, beh) sa bez zbrane bije len rukami: kop sa spustí iba zo státia
        bool moving = Mathf.Abs(GameInput.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(GameInput.GetAxisRaw("Vertical")) > 0.1f;
        if (moving) return Random.value < elbowChance / Mathf.Max(0.01f, 1f - kickChance) ? 1 : 0;

        float r = Random.value;
        if (r < kickChance) return 2;
        if (r < kickChance + elbowChance) return 1;
        return 0;
    }

    void TryAttack(bool heavy)
    {
        WeaponStats weapon = CurrentWeapon();
        heavy &= weapon.armed;

        if (!stats.UseStamina(weapon.stamina * (heavy ? heavyStaminaMultiplier : 1f)))
        {
            GameHUD.WorldText(transform.position + Vector3.up * 1.8f, "Vyčerpaný!", Color.gray);
            return;
        }

        // S mečom: ľahký/ťažký útok (varianta 0-1 / 2 platí len ako záloha, ak sada nemá klipy ľahkých a ťažkých útokov)
        attackIsHeavy = heavy;
        GameAudio.Play(heavy ? GameAudio.Sfx.SwingHeavy : GameAudio.Sfx.Swing, transform.position);
        attackVariant = weapon.armed ? (heavy ? 2 : Random.Range(0, 2)) : RollVariant(false);
        // Sekanie stromov / ťažba: bez úderu päsťou, nástroj sa v ruke zamáchne sám (HeldToolVisual)
        bool working = ToolKit.WouldUse(transform, AimForward());
        if (!working) attackAnimUntil = Time.time + 0.15f;   // len krátky signál pre animátor; klip sa dohrá sám

        float variantDamage, variantCooldown;
        if (weapon.armed)
        {
            variantDamage = heavy ? heavyDamageMultiplier : 1f;
            variantCooldown = heavy ? heavyCooldownMultiplier : 1f;
        }
        else
        {
            variantDamage = attackVariant == 1 ? elbowDamageMultiplier : attackVariant == 2 ? kickDamageMultiplier : 1f;
            variantCooldown = attackVariant == 1 ? elbowCooldownMultiplier : attackVariant == 2 ? kickCooldownMultiplier : 1f;
        }

        float damageMultiplier = weapon.damage * variantDamage;
        float effectiveRange = weapon.armed ? weapon.range * stats.WeaponRangeMultiplier : weapon.range;

        // Ďalší útok najskôr po dokončení animácie tohto (inak sa útok prerušuje v polovici a pôsobí to sekane)
        float cooldown = attackCooldown * weapon.cooldown * variantCooldown
                         / Mathf.Max(0.1f, stats.AttackSpeedTotal * stats.TempAttackSpeed * attackSpeedBoost);
        float animSeconds = !working && modelAnimator != null ? modelAnimator.PrepareAttack(attackVariant, heavy) : 0f;
        if (!working && weapon.armed && weapons != null && weapons.TryGetBlade(out Transform bladeHolder, out Vector3 bladeTip, out float bladeLength))
            CombatFx.SwordTrail(bladeHolder, bladeTip, bladeLength, heavy, animSeconds > 0f ? animSeconds : 0.45f);
        nextAttackTime = Time.time + (working ? Mathf.Max(1.0f, cooldown * 0.6f) : Mathf.Max(cooldown, animSeconds));

        // Útok je hlučný: na chvíľu ťa vidno a počuť viac
        if (PlayerStealth.Instance != null) PlayerStealth.Instance.MakeNoise();

        Vector3 aim = AimForward();
        if (movement != null) movement.FaceDirection(aim);
        else transform.rotation = Quaternion.LookRotation(aim);

        // Sekanie: skriptová animácia ChopAnimation (kosti trupu a rúk) na modeli hráča; nohy ďalej ide animátor, takže sa dá sekať aj za pohybu
        bool swung = false;
        if (working && modelAnimator != null)
        {
            if (chopAnimation == null)
            {
                chopAnimation = ChopAnimation.Attach(modelAnimator.gameObject, false, 1.0f, 0.1f);
                chopAnimation.hitTime = 0.5f;
            }
            chopAnimation.ChopOnce();
            swung = true;
        }
        HeldToolVisual.ProceduralSwing = !swung;
        ToolKit.TryUse(transform, aim);   // strom / skala / železný blok pred hráčom + správny nástroj

        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, effectiveRange, hitBuffer, ~0,
            QueryTriggerInteraction.Ignore);
        done.Clear();

        for (int i = 0; i < hitCount; i++)
        {
            Damageable d = hitBuffer[i].GetComponentInParent<Damageable>();
            if (d == null || d.IsDead || !done.Add(d)) continue;

            Vector3 to = d.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f && Vector3.Angle(aim, to) > attackAngle)
                continue;

            Hit(d, damageMultiplier, weapon.kind);
        }

        if (stats.IsDemon) HitBuildings(aim, effectiveRange, damageMultiplier);
    }

    // Démon búra ľudské stavby päsťami
    void HitBuildings(Vector3 aim, float range, float damageMultiplier)
    {
        float damage = baseDamage * stats.DamageMultiplier * damageMultiplier * demonBuildingFactor;

        foreach (BuildPlot plot in BuildPlot.All)
        {
            if (plot.State != PlotState.Built) continue;

            Vector3 d = plot.transform.position - transform.position;
            d.y = 0f;
            float edge = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z)) - plot.Side * 0.5f;
            if (edge > range) continue;
            if (edge > 1f && Vector3.Dot(aim, d.normalized) < 0f) continue;

            plot.TakeDamageFromPlayer(damage, stats);
            GameAudio.Play(GameAudio.Sfx.SwordHit, plot.transform.position);
            break;   // jeden úder poškodí jednu stavbu
        }
    }

    /// <summary>
    /// Protiútok po úspešnom uhnutí (CombatQte): silný zásah bez výdrže, ktorý sa pri nezraniteľnom démonovi použije jeho slabinou.
    /// </summary>
    public void CounterAttack(Damageable target, float multiplier)
    {
        if (target == null || target.IsDead) return;

        WeaponStats weapon = CurrentWeapon();
        attackIsHeavy = true;
        attackVariant = 2;
        attackAnimUntil = Time.time + 0.15f;
        if (modelAnimator != null) modelAnimator.PrepareAttack(attackVariant, true);
        nextAttackTime = Time.time + 0.6f;
        GameAudio.Play(GameAudio.Sfx.SwingHeavy, transform.position);

        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.01f && movement != null) movement.FaceDirection(toTarget);

        DamageKind kind = weapon.kind;   // nezraniteľných rieši Hit (len najlepší meč)
        Hit(target, weapon.damage * heavyDamageMultiplier * multiplier, kind);
    }

    // Vyšší démoni (nezraniteľní) sa dajú zraniť len Posvätenou čepeľou vylepšenou na úroveň 3 (ostrenie) alebo vyššie
    public const int BestSwordMinLevel = 3;

    bool HasBestSword()
    {
        if (weapons == null || !weapons.IsArmed || weapons.Current == null) return false;
        return weapons.Current.id == ItemCatalog.BlessedBlade && WeaponUpgrades.Level(WeaponUpgrades.Kind.Sharpen) >= BestSwordMinLevel;
    }

    void Hit(Damageable target, float damageMultiplier, DamageKind kind)
    {
        if (target.immuneToNormalWeapons) kind = HasBestSword() ? target.weakness : DamageKind.Normal;
        float damage = baseDamage * stats.DamageMultiplier * damageMultiplier;

        // Tieniaci skok zaručí kritický zásah, inak rozhoduje šanca
        bool crit = stats.ConsumeGuaranteedCrit()
                    || Random.value < Mathf.Clamp01(baseCritChance + stats.CritBonusTotal + SoulBalance.CritBonus);
        if (crit) damage *= critMultiplier * SoulBalance.CritDamageFactor;

        Vector3 popupPos = target.transform.position + Vector3.up * 2.2f;
        HitResult result = target.TakeDamage(damage, gameObject, kind);

        switch (result)
        {
            case HitResult.Blocked:
                GameHUD.WorldText(popupPos, "Blok!", Color.gray);
                break;

            case HitResult.Immune:
                GameHUD.WorldText(popupPos, "Nezraniteľný!", Color.gray);
                GameHUD.Message($"Vyšší démon: zraní ho len Posvätená čepeľ vylepšená na úroveň {BestSwordMinLevel} (ostrenie v dielni).", 3f);
                Debug.Log($"{target.displayName} je voči bežným zbraniam nezraniteľný.");
                break;

            case HitResult.Hit:
            case HitResult.Killed:
                GameHUD.WorldText(popupPos,
                    crit ? $"BAM! KRITICKÝ {damage:0}" : $"{damage:0}",
                    crit ? new Color(1f, 0.85f, 0.1f) : Color.white);

                if (crit)
                {
                    GameHUD.MangaPanel();
                    CombatFx.Crit(target);
                }

                if (stats.LifestealTotal > 0f)
                    stats.Heal(damage * stats.LifestealTotal);

                // Brutálna sila: zásah môže prerušiť útok nepriateľa
                if (result == HitResult.Hit)
                {
                    float stagger = SkillTree.Sum(SkillStat.Stagger);
                    if (stagger > 0f && Random.value < stagger)
                    {
                        DemonEnemy staggered = target.GetComponent<DemonEnemy>();
                        if (staggered != null) staggered.Stun(0.6f);
                        CivilianAI civ = target.GetComponent<CivilianAI>();
                        if (civ != null) civ.Stun(0.6f);
                    }
                }

                if (clanOnHit != null) clanOnHit.OnHit(target, damage);
                WeaponUpgrades.OnHit(target, damage, gameObject);
                SoulAbilities.AfterHit(stats, target, damage, result);
                GameAudio.Play(GameAudio.Sfx.SwordHit, target.transform.position);
                break;
        }
    }

    // Útok vždy smeruje tam, kam sa pozerá kamera
    Vector3 AimForward()
    {
        if (cameraTransform == null) return transform.forward;

        Vector3 f = cameraTransform.forward;
        f.y = 0f;
        return f.sqrMagnitude < 0.01f ? transform.forward : f.normalized;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
