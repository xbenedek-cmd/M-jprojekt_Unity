using System;
using System.Collections.Generic;
using UnityEngine;

public enum CombatTeam { Player, Civilian, Demon }
public enum HitResult { Hit, Killed, Immune, AlreadyDead, Blocked }
public enum DamageKind { Normal, Fire, Lightning, Earth, Blood, Shadow, Holy }

public class Damageable : MonoBehaviour
{
    public event Action<float, GameObject> OnDamaged;
    public event Action OnDied;

    // Zoznam všetkých mŕtvol v scéne
    public static readonly List<Damageable> AllCorpses = new List<Damageable>();
    public static int PlayerCivilianKillCount { get; private set; }
    public static int PlayerSoldierKillCount { get; private set; }

    [Header("Zdravie")]
    public string displayName = "NPC";
    public CombatTeam team = CombatTeam.Civilian;
    public float maxHealth = 50f;

    [Tooltip("Vyšší démon: bežné zbrane mu neublížia")]
    public bool immuneToNormalWeapons = false;
    [Tooltip("Čo ublíži nezraniteľnému démonovi")]
    public DamageKind weakness = DamageKind.Normal;

    [Header("Odmena za zabitie")]
    public int moneyReward = 0;

    [Header("Zobrazenie")]
    public bool showHealthBar = true;

    [Header("Mŕtvola")]
    [Tooltip("Ako dlho (s) mŕtvola púta pozornosť civilistov a vojakov")]
    public float corpseAttentionSeconds = 90f;

    private bool lastHitByPlayer;
    private bool killedByPlayer;
    private float dotDps;
    private float dotUntil;
    private GameObject dotSource;
    private float markUntil;
    private DamageKind dotKind;
    private float markMultiplier = 1f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public float DeathTime { get; private set; } // Kedy zomrel
    public bool IsHidden { get; private set; } // Či je telo skryté
    public bool IsReported { get; private set; } // Vojaci už telo našli a vybavili
    public bool OfficiallyReportedDemon { get; private set; }
    public bool KilledByPlayer => killedByPlayer; // Vrahom je hráč (zbraň, vysatie, tiché zabitie)

    /// <summary>Mŕtvy človek (civilista, odbojár, vojak, strážnik): telo sa dá nosiť a vojaci ho odvážajú.</summary>
    public bool IsHumanBody { get; private set; }
    /// <summary>Hráč ho práve nesie (nezaniká, kým ho nesie).</summary>
    public bool IsCarried { get; set; }
    private float lifeBonus;

    /// <summary>Ako dlho (s) leží nenájdené telo človeka, kým zmizne samo.</summary>
    public const float HumanCorpseSeconds = 180f;

    /// <summary>Mŕtvola, ktorá ešte púta pozornosť (čerstvá, neskrytá, nenahlásená).</summary>
    public bool AttractsAttention =>
        IsDead && !IsHidden && !IsReported && Time.time - DeathTime < corpseAttentionSeconds;

    public void MarkReported() => IsReported = true;
    public void MarkOfficiallyReportedDemon() => OfficiallyReportedDemon = true;

    void Awake()
    {
        Health = maxHealth;
    }

    void OnEnable()
    {
        AllCorpses.Remove(this); // Pre istotu
    }

    void OnDisable()
    {
        AllCorpses.Remove(this);
    }

    [Tooltip("Druhá slabina (napr. Void-Spawn: oheň aj blesk)")]
    public DamageKind secondaryWeakness = DamageKind.Normal;
    [Tooltip("Zraní ho aj hráč v podobe démona bez ohľadu na slabinu (hybridná krv reaguje na toxín)")]
    public bool hybridVulnerable;
    [Tooltip("Násobok bežného (Normal) poškodenia; pod 1 = bežné zbrane takmer nezaberajú (Void-Spawn)")]
    public float normalDamageFactor = 1f;

    public bool IsWeakTo(DamageKind kind) => (weakness != DamageKind.Normal && kind == weakness) || (secondaryWeakness != DamageKind.Normal && kind == secondaryWeakness);

    bool IsHybridHit(GameObject attacker)
    {
        if (!hybridVulnerable || attacker == null) return false;
        PlayerStats p = attacker.GetComponent<PlayerStats>();
        return p != null && p.IsDemon;
    }

    /// <summary>Bossovia: upraví prichádzajúce poškodenie (štít, zásahové zóny, strop zdravia pred finišerom). Vráti 0 = zásah zablokovaný.</summary>
    public Func<float, GameObject, DamageKind, float> DamageFilter;

    private Vector3 lastAttackerPosition;
    private bool hasLastAttacker;
    private bool inDotTick;

    public HitResult TakeDamage(float amount, GameObject attacker = null, DamageKind kind = DamageKind.Normal)
    {
        if (IsDead) return HitResult.AlreadyDead;

        if (attacker != null && attacker.GetComponent<PlayerStats>() != null)
            lastHitByPlayer = true;

        if (DamageFilter != null)
        {
            amount = DamageFilter(amount, attacker, kind);
            if (amount <= 0f)
            {
                OnDamaged?.Invoke(0f, attacker);
                CombatFx.Damaged(this, 0f, attacker, kind);
                return HitResult.Blocked;
            }
        }

        if (immuneToNormalWeapons && !IsWeakTo(kind) && !IsHybridHit(attacker))
        {
            OnDamaged?.Invoke(0f, attacker);
            CombatFx.Damaged(this, 0f, attacker, kind);
            return HitResult.Immune;
        }

        if (Time.time < markUntil) amount *= markMultiplier;
        if (kind == DamageKind.Normal && normalDamageFactor < 1f && !IsHybridHit(attacker)) amount *= normalDamageFactor;

        Health = Mathf.Max(0f, Health - amount);
        lastAttackerPosition = attacker != null ? attacker.transform.position : transform.position;
        hasLastAttacker = attacker != null;
        if (!inDotTick) CombatFx.Damaged(this, amount, attacker, kind);   // tiky horenia/krvácania majú vlastný efekt
        OnDamaged?.Invoke(amount, attacker);

        if (Health <= 0f)
        {
            Die();
            return HitResult.Killed;
        }
        return HitResult.Hit;
    }

    /// <summary>Lifesteal bossov (Carmilla): doplní zdravie bez prekročenia maxima.</summary>
    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Health = Mathf.Min(maxHealth, Health + amount);
    }

    /// <summary>Nastaví maximálne zdravie aj aktuálne (elitné komandá po vytvorení klonu).</summary>
    public void SetMaxHealth(float hp)
    {
        maxHealth = hp;
        Health = hp;
    }

    public void Mark(float seconds, float damageMultiplier)
    {
        markUntil = Time.time + seconds;
        markMultiplier = damageMultiplier;
    }

    /// <param name="byPlayer">Zabil to hráč (vysatie, tiché zabitie): telo bude usvedčujúce, ale bez odmeny.</param>
    public void ForceKill(bool byPlayer = false)
    {
        if (IsDead) return;
        if (byPlayer) killedByPlayer = true;
        Health = 0f;
        Die();
    }

    public void ApplyDot(float dps, float seconds, GameObject source, DamageKind kind = DamageKind.Normal)
    {
        if (IsDead || (immuneToNormalWeapons && !IsWeakTo(kind))) return;

        dotKind = kind;
        dotDps = Time.time < dotUntil ? Mathf.Max(dotDps, dps) : dps;
        dotUntil = Mathf.Max(dotUntil, Time.time + seconds);
        dotSource = source;
        CombatFx.Dot(this, seconds, kind);
    }

    void Update()
    {
        if (IsDead)
        {
            if (IsCarried) lifeBonus += Time.deltaTime;   // kým ho hráč nesie, čas neplynie
            float limit = IsHumanBody ? HumanCorpseSeconds : 15f;
            if (!IsHidden && !IsCarried && Time.time - DeathTime - lifeBonus >= limit)
                RemoveCorpse();
        }
        else if (Time.time >= dotUntil)
        {
            return;
        }

        if (!IsDead && Time.time < dotUntil)
        {
            inDotTick = true;
            TakeDamage(dotDps * Time.deltaTime, dotSource, dotKind);
            inDotTick = false;
        }
    }

    void Die()
    {
        IsDead = true;
        DeathTime = Time.time;
        if (lastHitByPlayer) killedByPlayer = true;
        if (killedByPlayer && team == CombatTeam.Civilian
            && GetComponent<CivilianAI>() != null && GetComponent<SoldierAI>() == null)
            PlayerCivilianKillCount++;
        if (killedByPlayer && GetComponent<SoldierAI>() != null) PlayerSoldierKillCount++;
        AllCorpses.Add(this); // Pridanie do zoznamu mŕtvol
        IsHumanBody = GetComponent<CivilianAI>() != null || GetComponent<SoldierAI>() != null || GetComponent<CityGuardNPC>() != null;
        if (IsHumanBody && killedByPlayer) StealthWorld.RegisterBody(transform.position, true, displayName, this);

        if (lastHitByPlayer)
        {
            PlayerStats p = FindAnyObjectByType<PlayerStats>();
            if (p != null)
            {
                if (IsHumanBody) GiveHumanKillReward(p);   // človek dá buď peniaze, alebo XP, nikdy oboje
                else
                {
                    if (moneyReward > 0)
                    {
                        p.AddMoney(moneyReward);
                        GameHUD.Message($"+{moneyReward} peňazí ({displayName})");
                    }
                    p.AddSkillXp(team == CombatTeam.Demon ? 20 : 10);
                }
            }
        }

        if (lastHitByPlayer && team == CombatTeam.Demon)
        {
            Reputation.Add(FactionId.Civilians, 12f);
            Reputation.Add(FactionId.Megacorp, 10f);
            Reputation.Add(FactionId.Rebels, 6f);
            Reputation.AddSuspicion(-12f);
        }
        else if ((lastHitByPlayer || killedByPlayer) && (team == CombatTeam.Civilian || team == CombatTeam.Player))
        {
            KillConsequences.OnPlayerKill(this);   // trest až keď na zabitie niekto príde
            Sanity.Add(-5f);   // zabitie človeka zaťažuje psychiku
        }

        NPCWander wander = GetComponent<NPCWander>();
        if (wander != null) wander.enabled = false;

        DemonEnemy enemy = GetComponent<DemonEnemy>();
        if (enemy != null) enemy.enabled = false;

        // OPRAVA: Manuálne volanie HandleKilled() je odstránené, 
        // pretože BloodSource.cs má už pripravené: damageable.OnDied += HandleKilled;

        // Démon s animáciou smrti sa nepoloží natvrdo na bok: animácia (pád dopredu/dozadu) ho zloží sama
        DemonModelAnimator deathAnimator = GetComponentInChildren<DemonModelAnimator>();
        if (deathAnimator == null || !deathAnimator.PlayDeath()) CombatFx.Tumble(transform, lastAttackerPosition, hasLastAttacker);
        CombatFx.Died(this);
        NPCGrounding grounding = GetComponent<NPCGrounding>();
        if (grounding != null) grounding.enabled = false;

        Debug.Log($"{displayName} zomrel/a.");
        OnDied?.Invoke();
    }

    /// <summary>Telo zmizne (odviezli ho, nikto ho nenašiel, zakopané) bez hlášky.</summary>
    public void RemoveCorpse()
    {
        if (!IsDead || IsHidden) return;
        IsCarried = false;
        if (transform.parent != null && transform.parent.GetComponent<PlayerStats>() != null) transform.SetParent(null);
        StealthWorld.Bodies.RemoveAll(b => b.corpse == this);
        IsHidden = true;
        AllCorpses.Remove(this);
        transform.position -= new Vector3(0, 2f, 0);
        gameObject.SetActive(false);
    }

    // Zabitý človek dá buď peniaze, alebo XP (nie oboje); vojak dvojnásobok a občas z neho vypadne puška
    void GiveHumanKillReward(PlayerStats p)
    {
        KillConsequences.TryGetKind(this, out VictimKind kind);
        // Civilista dá minimum (zabíjanie nevinných sa nemá oplatiť), odbojár viac, vojak najviac
        if (UnityEngine.Random.value < 0.5f)
        {
            int basePay = kind == VictimKind.Civilian ? 5 : moneyReward > 0 ? moneyReward : kind == VictimKind.Rebel ? 14 : 28;
            int money = Mathf.Max(1, basePay);
            p.AddMoney(money);
            GameHUD.Message($"+{money} peňazí ({displayName})");
        }
        else
        {
            int xp = kind == VictimKind.Civilian ? 4 : kind == VictimKind.Rebel ? 12 : 24;
            if (p.IsDemon) p.AddDemonXp(xp); else p.AddSkillXp(xp);
            GameHUD.Message($"+{xp} XP ({displayName})");
        }

        if (kind == VictimKind.Soldier && UnityEngine.Random.value < 0.25f)
        {
            Inventory inventory = p.GetComponent<Inventory>();
            if (inventory != null && inventory.TryAdd(ItemCatalog.Rifle))
                GameHUD.Message("Z vojaka vypadla puška. Strieľať z nej zatiaľ nevieš, dá sa predať kupcovi.", 4f);
        }
    }

    // Skrytie mŕtvoly (potopí sa do zeme a zmizne zo zoznamu)
    public void HideCorpse()
    {
        if (!IsDead || IsHidden) return;
        StealthWorld.Bodies.RemoveAll(b => b.corpse == this);
        IsHidden = true;
        AllCorpses.Remove(this);

        // Vizuálne potopenie
        transform.position -= new Vector3(0, 2f, 0);
        gameObject.SetActive(false); // Úplne vypnutie, aby ho nič nenašlo
        GameHUD.Message("Telo si skryl.", 2f);
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!NpcOverlay.Visible(transform.position)) return;
        if (!showHealthBar || IsDead || Health >= maxHealth) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
        if (p.z <= 0f) return;

        Rect r = new Rect(p.x - 25f, Screen.height - p.y, 50f, 6f);
        Color prev = GUI.color;

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.85f, 0.15f, 0.15f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * (Health / maxHealth), r.height), Texture2D.whiteTexture);

        GUI.color = prev;
    }
}

public static class Corpse
{
    /// <summary>Vidí pozorovateľ z výšky očí telo mŕtvoly (dosah + žiadna prekážka medzi nimi)?</summary>
    public static bool IsVisibleFrom(Vector3 eye, Damageable corpse, float range)
    {
        if (corpse == null) return false;

        Vector3 target = corpse.transform.position + Vector3.up * 0.3f;
        if ((target - eye).sqrMagnitude > range * range) return false;

        if (Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<Damageable>() == corpse;

        return true;
    }

    static readonly HashSet<Transform> darkened = new HashSet<Transform>();

    public static void LayDown(Transform t)
    {
        if (t == null) return;
        Darken(t);

        Collider col = t.GetComponent<Collider>();
        float bottomOffset = 0.8f;
        if (col != null)
        {
            Physics.SyncTransforms();
            bottomOffset = col.bounds.min.y - t.position.y;
        }

        t.rotation = Quaternion.Euler(0f, t.eulerAngles.y, 90f);

        RaycastHit groundHit = default;
        bool foundGround = false;
        float closestDistance = float.MaxValue;
        // Lúč začína tesne nad telom: z 10 m by pri smrti pod strechou alebo prevísom trafil strechu a telo by skončilo na nej
        foreach (RaycastHit hit in Physics.RaycastAll(t.position + Vector3.up * 1.2f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == col || (t != null && hit.collider.transform.IsChildOf(t))) continue;
            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                groundHit = hit;
                foundGround = true;
            }
        }

        if (foundGround)
        {
            float targetY = groundHit.point.y - bottomOffset;
            t.position = new Vector3(t.position.x, targetY, t.position.z);
        }

        Rigidbody rb = t.GetComponent<Rigidbody>();
        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        Physics.SyncTransforms();
    }

    static void Darken(Transform t)
    {
        if (!darkened.Add(t)) return;

        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
        {
            Material m = r.material;
            string prop = m.HasProperty("_BaseColor") ? "_BaseColor" : (m.HasProperty("_Color") ? "_Color" : null);
            if (prop == null) continue;

            m.SetColor(prop, Color.Lerp(m.GetColor(prop), Color.black, 0.6f));
        }
    }
}
