using System.Collections.Generic;
using UnityEngine;

public enum CivilianMode { Calm, Flee, Fight }

public class CivilianAI : MonoBehaviour
{
    public static readonly List<CivilianAI> All = new List<CivilianAI>();
    public static float LastPlayerCrimeTime = -999f;

    [Header("Povaha")]
    public float bravery = -1f;

    [Header("Pohyb")]
    public float fleeSpeed = 4.4f;
    public float chaseSpeed = 3.8f;
    public float turnSpeed = 10f;

    [Header("Útok na hráča")]
    public float attackRange = 1.7f;
    public float attackDamage = 6f;
    public float attackCooldown = 1.6f;
    public float windUp = 0.5f;
    [Tooltip("Zrýchlenie útokov (1.25 = o 25 % častejšie)")]
    public float attackSpeedBoost = 1.25f;

    float AttackCooldown => attackCooldown / Mathf.Max(0.1f, attackSpeedBoost);
    float WindUp => windUp / Mathf.Max(0.1f, attackSpeedBoost);
    public float fightLoseRange = 22f;
    public float fightSeconds = 25f;

    [Header("Strach")]
    // Civilista vidí okolo seba do 10 m (démoni, mŕtvoly, hráč pri nízkej dôvere); mŕtvola potrebuje priamy výhľad
    public const float SightRadius = 10f;
    public float corpsePanicRadius = 12f; // Do akej vzdialenosti sa panika rozšíri na ostatných civilistov
    [Tooltip("Ak hráč stojí pri tele, ktoré zabil, takto blízko, civilisti ho nahlásia ako zločinca")]
    public float corpseWitnessPlayerRange = 15f;
    public float fleeSeconds = 8f;

    [Header("Dôvera")]
    public float hostileTrustBelow = -50f;
    public float trustRollInterval = 4f;

    private Damageable dmg;
    private NPCWander wander;
    private BloodSource source;
    private PlayerStats player;
    private Rigidbody rb;
    private bool isHealer;
    private NPCPerception perception;
    private ZonePopulation cityZone;   // z mesta nevyjde

    private CivilianMode mode = CivilianMode.Calm;
    private float modeUntil;
    private Vector3 fleeFrom;
    private bool attacking;
    private float hitTime;
    private float nextAttackTime;
    private float nextTrustRoll;
    private float nextText;
    private bool provokedByPlayer;

    private const float ScanInterval = 0.2f;
    private float nextScan;
    private static GUIStyle alertStyle;
    private static Camera guiCam;
    private float stunUntil;
    private readonly HashSet<Damageable> seenCorpses = new HashSet<Damageable>();

    public Damageable Damage => dmg;
    public CivilianMode Mode => mode;
    public bool IsHealer => isHealer;
    public bool IsFighting => mode == CivilianMode.Fight;
    public bool IsAlarmed => mode != CivilianMode.Calm;
    public bool IsDead => dmg == null || dmg.IsDead || (source != null && source.IsDead);
    public bool IsResistance => perception != null && FactionData.IsResistance(perception.faction);

    Vector3 Pos => rb != null ? rb.position : transform.position;

    void Awake()
    {
        dmg = GetComponent<Damageable>();
        if (dmg == null)
        {
            enabled = false;
            return;
        }
        wander = GetComponent<NPCWander>();
        source = GetComponent<BloodSource>();
        perception = GetComponent<NPCPerception>();
        if (GetComponent<NPCGrounding>() == null) gameObject.AddComponent<NPCGrounding>();
        isHealer = GetComponent<Healer>() != null;
        dmg.OnDamaged += OnHit;
    }

    void Start()
    {
        player = FindAnyObjectByType<PlayerStats>();
        rb = GetComponent<Rigidbody>();
        if (bravery < 0f) bravery = Random.Range(0.1f, 0.6f);
    }

    void OnEnable()
    {
        if (!All.Contains(this)) All.Add(this);
        cityZone = CityPopulation.At(transform.position);
    }

    void OnDisable() => All.Remove(this);

    void OnDestroy()
    {
        if (dmg != null) dmg.OnDamaged -= OnHit;
    }

    public void Stun(float seconds)
    {
        stunUntil = Time.time + seconds;
        attacking = false;
    }

    public static CivilianAI FindNearest(Vector3 pos, float range)
    {
        CivilianAI best = null;
        float bestSqr = range * range;

        foreach (CivilianAI c in All)
        {
            if (c == null || c.IsDead || c.isHealer) continue;
            if (c.source != null && c.source.BeingDrunk) continue;

            float d = (c.transform.position - pos).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                best = c;
            }
        }
        return best;
    }

    void OnHit(float amount, GameObject attacker)
    {
        if (attacker == null || dmg == null || dmg.Health <= 0f) return;

        if (attacker.GetComponent<PlayerStats>() != null)
        {
            Provoke(false);
        }
        else if (attacker.GetComponent<DemonEnemy>() != null)
        {
            BeginFlee(attacker.transform.position);
        }
    }

    public void Provoke(bool forceFight)
    {
        if (IsDead) return;
        if (player == null) player = FindAnyObjectByType<PlayerStats>();

        if (!provokedByPlayer)
        {
            provokedByPlayer = true;
            LastPlayerCrimeTime = Time.time;
            Reputation.Add(FactionId.Civilians, -3f);
            Reputation.AddSuspicion(4f);
        }

        bool fight = !isHealer && (forceFight || mode == CivilianMode.Fight || Random.value < bravery);

        if (fight) BeginFight();
        else BeginFlee(player != null ? player.transform.position : transform.position);
    }

    void BeginFight()
    {
        if (mode != CivilianMode.Fight) Say("Útok!", new Color(1f, 0.4f, 0.3f));
        mode = CivilianMode.Fight;
        modeUntil = Time.time + fightSeconds;
    }

    /// <summary>Svedok vraždy alebo nahlásenie: civilista sa zľakne a uteká od daného miesta.</summary>
    public void Alarm(Vector3 from)
    {
        if (IsDead || isHealer) return;
        BeginFlee(from);
    }

    void BeginFlee(Vector3 from)
    {
        if (mode != CivilianMode.Flee) Say("Pomoc!", new Color(1f, 0.9f, 0.4f));
        mode = CivilianMode.Flee;
        fleeFrom = from;
        modeUntil = Time.time + fleeSeconds;
        attacking = false;
    }

    void FixedUpdate()
    {
        if (IsDead || JailManager.IsInJail || (source != null && source.BeingDrunk) || Time.time < stunUntil)
        {
            attacking = false;
            return;
        }

        if (player == null) player = FindAnyObjectByType<PlayerStats>();   // rb sa berie v Start

        // Vnímanie nie je treba každý fyzikálny krok; náhodný posun rozloží NPC medzi snímky
        if (Time.time >= nextScan)
        {
            nextScan = Time.time + ScanInterval + Random.value * 0.05f;
            ScanThreats();
        }

        switch (mode)
        {
            case CivilianMode.Flee: DoFlee(); break;
            case CivilianMode.Fight: DoFight(); break;
        }

        if (wander != null)
        {
            bool calm = mode == CivilianMode.Calm;
            if (wander.enabled != calm) wander.enabled = calm;
        }
    }

    void ScanThreats()
    {
        // 1. Skenovanie démonov
        DemonEnemy nearest = null;
        float best = SightRadius * SightRadius;
        Vector3 myPos = transform.position;

        foreach (DemonEnemy e in DemonEnemy.All)
        {
            if (e == null || e.Damage == null || e.Damage.IsDead || !e.IsVisibleThreat) continue;

            float d = (e.transform.position - myPos).sqrMagnitude;
            if (d < best)
            {
                best = d;
                nearest = e;
            }
        }

        if (nearest != null && mode != CivilianMode.Fight)
            BeginFlee(nearest.transform.position);

        // 2. Mŕtvoly: civilista musí telo vidieť; každé objaví len raz (potom už nepanikári donekonečna)
        if (mode == CivilianMode.Calm)
        {
            Vector3 eye = Pos + Vector3.up * 1.6f;

            foreach (Damageable corpse in Damageable.AllCorpses)
            {
                if (corpse == null || corpse == dmg || !corpse.AttractsAttention) continue;
                if (seenCorpses.Contains(corpse)) continue;
                if (!Corpse.IsVisibleFrom(eye, corpse, SightRadius)) continue;

                seenCorpses.RemoveWhere(c => c == null);
                seenCorpses.Add(corpse);
                OnCorpseDiscovered(corpse);
                break;
            }
        }

        // 3. Nízka dôvera
        bool playerIsNonCriminalDemon = player != null && player.IsDemon
            && (CivilianAI.LastPlayerCrimeTime < 0f || Time.time - CivilianAI.LastPlayerCrimeTime > 30f);

        if (mode == CivilianMode.Calm && !isHealer && !IsResistance && player != null && !player.IsDead && !player.IsInvisible
            && !playerIsNonCriminalDemon && Time.time >= nextTrustRoll && Reputation.Get(FactionId.Civilians) <= hostileTrustBelow)
        {
            if ((player.transform.position - myPos).sqrMagnitude <= SightRadius * SightRadius)
            {
                nextTrustRoll = Time.time + trustRollInterval;

                if (Random.value < bravery) BeginFight();
                else if (Random.value < 0.5f) BeginFlee(player.transform.position);
            }
        }
    }

    void OnCorpseDiscovered(Damageable corpse)
    {
        Vector3 at = corpse.transform.position;

        Say("Mŕtvola!", new Color(1f, 0.9f, 0.4f));
        BeginFlee(at);
        Panic(at, corpsePanicRadius, this);

        // Videl vraha? Ak hráč stojí pri tele, ktoré zabil, civilista ho nahlási.
        // Mŕtvola démona je ale dobrý skutok: hráč nie je zločinec, ale „čistiteľ“.
        if (corpse.KilledByPlayer)
        {
            DemonEnemy demon = corpse.GetComponent<DemonEnemy>();
            if (demon == null) KillConsequences.OnBodyDiscovered(corpse);
            if (demon != null)
            {
                Reputation.Add(FactionId.Civilians, 6f);
                Reputation.Add(FactionId.Megacorp, 4f);
                Reputation.AddSuspicion(-3f);   // zločiny sa tým nemažú
                return;
            }

            if (player == null) player = FindAnyObjectByType<PlayerStats>();

            if (player != null && !player.IsDead && !player.IsInvisible
                && (player.transform.position - at).sqrMagnitude <= corpseWitnessPlayerRange * corpseWitnessPlayerRange)
            {
                LastPlayerCrimeTime = Time.time;
                Reputation.AddSuspicion(4f);
                SoldierAI.ReportCrimeAt(Pos, 30f, at);   // civilista volá o pomoc: vojaci v okolí idú k telu
                Reputation.Add(FactionId.Civilians, -2f);
            }
            else
            {
                Reputation.AddSuspicion(1.5f); // Telo našli, vraha nie
            }
        }
    }

    /// <summary>Výkrik: pokojní civilisti v okolí sa tiež vyľakajú a utekajú od zdroja.</summary>
    public static void Panic(Vector3 from, float radius, CivilianAI except = null)
    {
        float radiusSqr = radius * radius;
        foreach (CivilianAI c in All)
        {
            if (c == null || c == except || c.IsDead || c.mode != CivilianMode.Calm) continue;
            if (c.source != null && c.source.BeingDrunk) continue;
            if ((c.transform.position - from).sqrMagnitude > radiusSqr) continue;

            c.BeginFlee(from);
        }
    }

    void DoFlee()
    {
        if (Time.time >= modeUntil)
        {
            mode = CivilianMode.Calm;
            return;
        }

        Vector3 away = Pos - fleeFrom;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = transform.forward;
        Vector3 dir = away.normalized;

        if (Physics.Raycast(Pos + Vector3.up * 0.5f, dir, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            dir = Quaternion.Euler(0f, 90f, 0f) * dir;

        Face(dir);
        MoveBy(dir * fleeSpeed * Time.fixedDeltaTime);
    }

    void DoFight()
    {
        if (player == null || player.IsDead || player.IsInvisible)
        {
            mode = CivilianMode.Calm;
            attacking = false;
            return;
        }

        Vector3 to = player.transform.position - Pos;
        to.y = 0f;
        float dist = to.magnitude;

        if (dist > fightLoseRange || Time.time >= modeUntil)
        {
            mode = CivilianMode.Calm;
            attacking = false;
            return;
        }
        if (dist < 12f) modeUntil = Mathf.Max(modeUntil, Time.time + 3f);

        Vector3 dir = dist > 0.01f ? to / dist : transform.forward;
        Face(dir);

        if (attacking)
        {
            if (Time.time >= hitTime)
            {
                attacking = false;
                nextAttackTime = Time.time + AttackCooldown;
                if (dist <= attackRange * 1.4f)
                    player.TakeDamage(attackDamage);
            }
            return;
        }

        if (dist > attackRange)
            MoveBy(dir * chaseSpeed * Time.fixedDeltaTime);
        else if (Time.time >= nextAttackTime)
        {
            attacking = true;
            hitTime = Time.time + WindUp;
        }
    }

    void MoveBy(Vector3 delta)
    {
        Vector3 from = rb != null ? rb.position : transform.position;
        delta *= GameSpeed.Npc;
        delta = NpcSteering.Adjust(from, delta, transform);   // vyhýba sa pevným predmetom
        if (cityZone != null)
        {
            Vector3 clamped = cityZone.ClampInside(from + delta, 1f);
            delta = clamped - from;
        }
        if (rb != null) rb.MovePosition(rb.position + delta);
        else transform.position += delta;
    }

    void Face(Vector3 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(dir);
        if (rb != null) rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, turnSpeed * Time.fixedDeltaTime));
        else transform.rotation = Quaternion.Slerp(transform.rotation, target, turnSpeed * Time.fixedDeltaTime);
    }

    void Say(string text, Color color)
    {
        if (Time.time < nextText) return;
        nextText = Time.time + 1.5f;
        GameHUD.WorldText(transform.position + Vector3.up * 2.6f, text, color);
    }

    void OnGUI()
    {
        if (!attacking) return;   // OnGUI beží na každom civilistovi: najlacnejšia kontrola prvá
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!NpcOverlay.Visible(transform.position)) return;
        if (IsDead) return;
        if (guiCam == null) guiCam = Camera.main;
        Camera cam = guiCam;
        if (cam == null) return;

        Vector3 p = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
        if (p.z <= 0f) return;

        if (alertStyle == null)
            alertStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.5f, 0.2f) } };
        GUI.Label(new Rect(p.x - 20f, Screen.height - p.y - 15f, 40f, 30f), "!", alertStyle);
    }
}