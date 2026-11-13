using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Krvavé stopy: zranení démoni (nie bossovia) za sebou nechávajú stopu krvi. Démon-hráč ich vidí s démonickým vnímaním (E)
/// a môže ich sledovať. Silnejší démon (Mid+) = alfa: jeho stopa je fialová a po jeho zabití čaká korisť z brlohu.
/// Značky sú v poole (žiadne vytváranie za behu).
/// </summary>
public class BloodTrail : MonoBehaviour
{
    private const int MaxMarks = 90;
    private const float MarkLifetime = 200f;
    private const float StepDistance = 2.5f;
    private const float TrackRange = 110f;

    private struct Mark { public Vector3 position; public float time; public bool alpha, digital; }

    private readonly Mark[] marks = new Mark[MaxMarks];
    private GameObject[] objects;
    private Renderer[] renderers;
    private int next;
    private bool visible;
    private float nextScan;
    private bool hinted;

    private readonly Dictionary<DemonEnemy, Vector3> lastDrop = new Dictionary<DemonEnemy, Vector3>();
    private readonly HashSet<DemonEnemy> alphas = new HashSet<DemonEnemy>();

    private PlayerStats player;
    private DemonSenses senses;
    private Material normalMat, alphaMat, digitalMat;
    private Inventory inventory;
    private bool allowBlood, allowDigital;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<BloodTrail>() != null) return;
        new GameObject("BloodTrail").AddComponent<BloodTrail>();
    }

    void Start()
    {
        normalMat = MakeMaterial(new Color(0.8f, 0.05f, 0.1f));
        alphaMat = MakeMaterial(new Color(0.75f, 0.2f, 1f));
        digitalMat = MakeMaterial(new Color(0.1f, 0.9f, 1f));   // digitálna stopa infiltrátora

        objects = new GameObject[MaxMarks];
        renderers = new Renderer[MaxMarks];
        for (int i = 0; i < MaxMarks; i++)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = "Stopa";
            g.transform.SetParent(transform, false);
            g.transform.localScale = new Vector3(0.45f, 0.12f, 0.45f);
            g.SetActive(false);
            objects[i] = g;
            renderers[i] = g.GetComponent<Renderer>();
        }
        for (int i = 0; i < MaxMarks; i++) marks[i].time = -9999f;
    }

    static Material MakeMaterial(Color c)
    {
        var m = new Material(Shader.Find("Standard")) { color = c };
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 1.6f);
        return m;
    }

    void Update()
    {
        if (objects == null) return;
        if (player == null)
        {
            player = FindAnyObjectByType<PlayerStats>();
            senses = player != null ? player.GetComponent<DemonSenses>() : null;
            inventory = player != null ? player.GetComponent<Inventory>() : null;
            return;
        }

        // Krvavé stopy vidí len démon; digitálne stopy infiltrátorov aj človek s Detektorom duší (okuliare z čierneho trhu)
        allowBlood = player.IsDemon;
        allowDigital = player.IsDemon || (inventory != null && inventory.CountOf(ItemCatalog.SoulDetector) > 0);
        bool shouldShow = (allowBlood || allowDigital) && PerceptionToggle.IsDemonVision && !player.IsDead;

        if (Time.time >= nextScan)
        {
            nextScan = Time.time + 1f;
            Drop();
            Refresh(shouldShow);
        }
        else if (shouldShow != visible) Refresh(shouldShow);
    }

    void Drop()
    {
        Vector3 p = player.transform.position;
        foreach (DemonEnemy demon in DemonEnemy.All)
        {
            if (demon == null || demon.tier == DemonTier.Boss || demon.Damage == null || demon.Damage.IsDead) continue;
            if ((demon.transform.position - p).sqrMagnitude > TrackRange * TrackRange) continue;

            // Infiltrátor v ľudskej podobe zanecháva digitálnu stopu z implantátov (aj nezranený); ostatní démoni len zranení
            bool digital = demon.GetComponent<Infiltrator>() != null;
            if (!digital && demon.Damage.Health >= demon.Damage.maxHealth * 0.95f) continue;

            Vector3 pos = demon.transform.position;
            if (lastDrop.TryGetValue(demon, out Vector3 last) && (pos - last).sqrMagnitude < StepDistance * StepDistance) continue;
            lastDrop[demon] = pos;

            bool alpha = !digital && demon.Tier >= DemonTier.Mid;
            if (alpha && alphas.Add(demon)) demon.Damage.OnDied += () => OnAlphaDied(demon);

            marks[next] = new Mark { position = pos, time = Time.time, alpha = alpha, digital = digital };
            next = (next + 1) % MaxMarks;
        }
    }

    void Refresh(bool show)
    {
        visible = show;
        if (show && !hinted && HasAny())
        {
            hinted = true;
            GameHUD.Message("Démonické vnímanie (E) odhaľuje krvavé stopy zranených démonov. Fialová stopa vedie k alfe.", 4f);
        }

        for (int i = 0; i < MaxMarks; i++)
        {
            bool typeOk = marks[i].digital ? allowDigital : allowBlood;
            bool on = show && typeOk && Time.time - marks[i].time < MarkLifetime;
            if (objects[i].activeSelf != on) objects[i].SetActive(on);
            if (!on) continue;

            objects[i].transform.position = marks[i].position + Vector3.up * 0.08f;
            renderers[i].sharedMaterial = marks[i].digital ? digitalMat : marks[i].alpha ? alphaMat : normalMat;
        }
    }

    bool HasAny()
    {
        for (int i = 0; i < MaxMarks; i++)
            if (Time.time - marks[i].time < MarkLifetime) return true;
        return false;
    }

    // Brloh alfy: korisť za jeho zabitie, ak je hráč pri tom
    void OnAlphaDied(DemonEnemy demon)
    {
        alphas.Remove(demon);
        if (player == null || demon == null) return;
        if ((demon.transform.position - player.transform.position).sqrMagnitude > 60f * 60f) return;

        player.AddMoney(60);
        Inventory inventory = player.GetComponent<Inventory>();
        if (inventory != null) inventory.TryAdd(ItemCatalog.Crystal);
        GameHUD.Message("Brloh alfy: korisť +60 peňazí a Bleskový kryštál.", 4f);
    }
}
