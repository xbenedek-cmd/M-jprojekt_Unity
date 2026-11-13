using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Elitné lovecké komandá. Posielajú sa, keď hráč zachádza príliš ďaleko:
///  - Tieňoví lovci (Arakani): hráč-ČLOVEK dlhodobo ignoruje jeho rozkazy (neposlušnosť o 1 nad prahom Popravcov). Tiší démoni pod vedením veliteľa tieňov ho prepadnú zozadu.
///  - Mecha jednotky (armáda): hráč-DÉMON terorizuje mesto (podozrenie ≥ 85 alebo 4 nedávno zničené budovy; počet denne klesá o 1) alebo je pozornosť armády po hackoch ≥ 65 (človek ≥ 80). Obrnené, ťažko ozbrojené jednotky vojaka.
/// Komando varuje 8 s pred príchodom, objaví sa 24–30 m za hráčom a ide po ňom 5 minút (alebo kým nie je zničené / hráč ďaleko). Najviac jedno komando naraz, pauza 2 dni.
/// </summary>
public class HunterSquads : MonoBehaviour
{
    private const int CooldownDays = 2;
    private const float LifeSeconds = 300f;

    private enum Kind { None, Shadows, Mecha }

    private PlayerStats player;
    private DayNightCycle clock;
    private float nextCheck, spawnAt = -1f, despawnAt, nextHint;
    private Kind pending = Kind.None;
    private int nextAllowedDay;
    private readonly List<GameObject> members = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<HunterSquads>() != null) return;
        new GameObject("HunterSquads").AddComponent<HunterSquads>();
    }

    void Update()
    {
        if (PauseMenu.AtStartup || Time.timeScale == 0f) return;
        if (player == null) { player = FindAnyObjectByType<PlayerStats>(); return; }
        if (clock == null) clock = FindAnyObjectByType<DayNightCycle>();
        if (player.IsDead) return;

        members.RemoveAll(m => m == null);
        TickActive();

        if (pending != Kind.None)
        {
            if (Time.time >= spawnAt) { Spawn(pending); pending = Kind.None; }
            return;
        }
        if (members.Count > 0 || Time.time < nextCheck) return;
        nextCheck = Time.time + 12f;
        if (clock == null || clock.Day < nextAllowedDay) return;
        if (Prologue.Active || MineSystem.Inside || JailManager.IsInJail || !Story.PrologueDone) return;

        Kind kind = Decide();
        if (kind == Kind.None) return;

        pending = kind;
        spawnAt = Time.time + 8f;
        nextAllowedDay = clock.Day + CooldownDays;
        GameHUD.Message(kind == Kind.Shadows
            ? "Cítiš, že ťa sleduje niečo tiché. Arakani posiela svojich lovcov..."
            : "Zvuk ťažkých motorov v diaľke. Armáda posiela elitné Mecha jednotky!", 6f);
    }

    Kind Decide()
    {
        ArakaniFavor af = ArakaniFavor.Instance;
        if (!player.IsDemon && af != null && af.VoiceHeard && af.disobedience > af.executionerThreshold) return Kind.Shadows;

        bool terror = player.IsDemon && (Reputation.Suspicion >= 85f || DemonRaid.Recent >= 4f);   // nedávne ničenie (denne klesá), nie celková štatistika
        bool attention = Propaganda.Heat >= (player.IsDemon ? 65f : 80f);
        if (terror || attention) return Kind.Mecha;
        return Kind.None;
    }

    // ---------- Živé komando ----------
    void TickActive()
    {
        if (members.Count == 0) return;

        bool timeUp = Time.time >= despawnAt;
        bool far = true;
        foreach (GameObject m in members)
            if (m != null && (m.transform.position - player.transform.position).sqrMagnitude < 160f * 160f) far = false;

        if (timeUp || far)
        {
            foreach (GameObject m in members) if (m != null) Destroy(m);
            members.Clear();
            return;
        }

        // Elita pozná hráčovu polohu: vojaci a strážnici ho naháňajú, démoni sú podráždení
        if (Time.time < nextHint) return;
        nextHint = Time.time + 1.5f;
        foreach (GameObject m in members)
        {
            if (m == null) continue;
            SoldierAI soldier = m.GetComponent<SoldierAI>();
            if (soldier != null && !soldier.IsDead) soldier.AlertTo(player.transform.position);
            DemonEnemy demon = m.GetComponent<DemonEnemy>();
            if (demon != null && demon.Damage != null && !demon.Damage.IsDead && !demon.IsChasing) demon.Provoke();
        }
    }

    // ---------- Vytvorenie ----------
    void Spawn(Kind kind)
    {
        int day = clock != null ? clock.Day : 1;
        Camera cam = Camera.main;
        Vector3 back = cam != null ? -cam.transform.forward : -player.transform.forward;
        back.y = 0f;
        if (back.sqrMagnitude < 0.01f) back = Vector3.back;
        back.Normalize();

        int count = kind == Kind.Shadows ? Mathf.Clamp(2 + (ArakaniFavor.Instance != null ? ArakaniFavor.Instance.disobedience - ArakaniFavor.Instance.executionerThreshold : 0), 2, 4)
                                         : Mathf.Clamp(2 + Mathf.FloorToInt(Propaganda.Heat / 50f) + (day > 10 ? 1 : 0), 2, 4);

        for (int i = 0; i < count; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, (i - (count - 1) * 0.5f) * 22f, 0f) * back;
            Vector3 pos = player.transform.position + dir * Random.Range(24f, 30f);
            if (Physics.Raycast(pos + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, ~0, QueryTriggerInteraction.Ignore)) pos = hit.point;

            GameObject unit = kind == Kind.Shadows ? SpawnShadow(pos, i == 0) : SpawnMecha(pos, i == 0);
            if (unit != null) members.Add(unit);
        }

        despawnAt = Time.time + LifeSeconds;
        if (members.Count == 0)
        {
            nextAllowedDay = 0;   // nič sa nevytvorilo (chýba šablóna): skúsi sa znova
            return;
        }
        GameHUD.Message(kind == Kind.Shadows ? "Tieňoví lovci ťa prepadli zozadu!" : "Mecha jednotky ťa obkľúčili!", 4f);
    }

    GameObject SpawnShadow(Vector3 pos, bool leader)
    {
        VoidSpawn vs = VoidSpawn.Spawn(pos, false);
        if (vs == null) return null;

        GameObject go = vs.gameObject;
        Destroy(vs);   // nie je to Void-Spawn z questu (nemá jeho slabiny ani zoznam)
        Damageable dmg = go.GetComponent<Damageable>();
        DemonEnemy enemy = go.GetComponent<DemonEnemy>();
        NPCPerception npc = go.GetComponent<NPCPerception>();

        dmg.displayName = leader ? "Veliteľ tieňov" : "Tieňový lovec";
        dmg.normalDamageFactor = 1f;
        dmg.weakness = DamageKind.Normal;
        dmg.secondaryWeakness = DamageKind.Normal;
        dmg.hybridVulnerable = false;
        dmg.moneyReward = leader ? 120 : 60;
        dmg.SetMaxHealth(leader ? 520f : 240f);
        if (enemy != null) { enemy.moveSpeed = leader ? 5.4f : 5.8f; enemy.attackDamage = leader ? 30f : 20f; enemy.Provoke(); }
        if (npc != null) npc.monsterColor = leader ? new Color(0.35f, 0.05f, 0.5f) : new Color(0.08f, 0.04f, 0.14f);
        go.name = leader ? "VelitelTienov" : "TienovyLovec";
        if (leader) go.transform.localScale *= 1.3f;
        return go;
    }

    GameObject SpawnMecha(Vector3 pos, bool leader)
    {
        SoldierAI source = null;
        foreach (SoldierAI s in FindObjectsByType<SoldierAI>(FindObjectsInactive.Include))
            if (s != null && s.GetComponent<Damageable>() != null && !s.GetComponent<Damageable>().IsDead) { source = s; break; }
        if (source == null) return null;

        GameObject go = Instantiate(source.gameObject, pos, source.transform.rotation);
        go.name = leader ? "MechaVelitel" : "MechaJednotka";
        go.SetActive(true);
        go.transform.localScale *= leader ? 1.55f : 1.4f;

        Damageable dmg = go.GetComponent<Damageable>();
        dmg.displayName = leader ? "Mecha veliteľ" : "Mecha jednotka";
        dmg.moneyReward = leader ? 160 : 80;
        dmg.SetMaxHealth(leader ? 700f : 380f);

        SoldierAI ai = go.GetComponent<SoldierAI>();
        ai.shootDamage *= leader ? 3f : 2.2f;
        ai.hitChance = 0.88f;
        ai.chaseSpeed *= 1.15f;
        ai.shootRange *= 1.3f;
        ai.huntSeconds = 90f;
        ai.AlertTo(player.transform.position);

        // Oranžovo-červená žiara pancierovania
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer) continue;
            foreach (Material m in r.materials)
            {
                if (m == null || !m.HasProperty("_Color")) continue;
                m.color = Color.Lerp(m.color, new Color(0.9f, 0.3f, 0.1f), 0.5f);
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.9f, 0.25f, 0.05f) * 0.6f);
            }
        }
        return go;
    }
}
