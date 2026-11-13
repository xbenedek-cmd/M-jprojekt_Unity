using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Uhýbanie a odrazenie útoku démona (quick-time event). Démon bojuje normálne 2–3 údery; potom jeho úder spomalí hru (3 s reálneho času)
/// a hráč musí stlačiť zobrazenú klávesu (písmeno alebo číslo). Úspech: úder sa nezasiahne, démon je omráčený a ak má hráč aspoň
/// 25 života, nasleduje silný protiútok (pri nižšom živote sa hráč len vyhne). Neúspech alebo zlá klávesa: úder zasiahne.
/// Ďalšie uhýbanie príde po ďalších 3 útokoch a od druhého vyžaduje dve klávesy za sebou (aj písmeno + číslo).
/// Bossov špeciálny úder funguje ako parry: úspech ho omráči na dlhšie a otvorí ho silnejšiemu protiútoku.
/// </summary>
public class CombatQte : MonoBehaviour
{
    public static CombatQte Instance { get; private set; }
    /// <summary>Práve prebieha uhýbanie (menu, pohyb a ďalšie vstupy sú zablokované cez Healer.MenuOpen).</summary>
    public static bool Active => Instance != null && Instance.active;

    [SerializeField] private float slowScale = 0.04f;
    [SerializeField] private float windowSeconds = 3f;
    [SerializeField] private float bossWindowSeconds = 1.6f;
    [SerializeField] private float cooldownSeconds = 7f;
    [Tooltip("Pod týmto životom sa po uhnutí nespraví protiútok")]
    [SerializeField] private float counterMinHealth = 25f;
    [SerializeField] private float stunSeconds = 2f;
    [SerializeField] private float bossStunSeconds = 3.5f;

    // Klávesy uhýbania: písmená a čísla, ktoré nie sú ladiace (G, H, J, K)
    private static readonly KeyCode[] Pool =
    {
        KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4
    };

    private class Duel
    {
        public int attacksSince;
        public int threshold = -1;   // po koľkých údery príde ďalšie uhýbanie (prvé 2–3, ďalšie 3)
        public int qteCount;
    }

    private readonly Dictionary<DemonEnemy, Duel> duels = new Dictionary<DemonEnemy, Duel>();
    private bool active;
    private bool boss;
    private DemonEnemy demon;
    private float damage;
    private KeyCode[] sequence;
    private int progress;
    private float deadline;
    private float windowLength;
    private float nextQte;
    private float restoreScale = 1f;
    private PlayerStats player;
    private PlayerCombat combat;
    private GUIStyle big, small;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<CombatQte>() != null) return;
        new GameObject("CombatQte").AddComponent<CombatQte>();
    }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (active) Time.timeScale = restoreScale;
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Volá DemonEnemy pri údere na hráča. Vráti true, ak uhýbanie úder prebralo (poškodenie aplikuje samo pri neúspechu);
    /// false = úder prebehne normálne.
    /// </summary>
    public static bool Intercept(DemonEnemy attacker, float hitDamage)
    {
        CombatQte q = Instance;
        if (q == null || q.active || attacker == null || PauseMenu.IsOpen) return false;

        if (q.player == null)
        {
            q.player = FindAnyObjectByType<PlayerStats>();
            q.combat = q.player != null ? q.player.GetComponent<PlayerCombat>() : null;
        }
        if (q.player == null || q.player.IsDead) return false;

        if (!q.duels.TryGetValue(attacker, out Duel duel))
        {
            duel = new Duel();
            q.duels[attacker] = duel;
        }

        bool isBoss = attacker.tier >= DemonTier.Boss;
        if (duel.threshold < 0) duel.threshold = isBoss ? 3 : Random.Range(2, 4);

        duel.attacksSince++;
        if (duel.attacksSince < duel.threshold || Time.unscaledTime < q.nextQte) return false;

        duel.attacksSince = 0;
        duel.threshold = 3;
        duel.qteCount++;
        q.Begin(attacker, hitDamage, isBoss, duel.qteCount);
        return true;
    }

    void Begin(DemonEnemy attacker, float hitDamage, bool isBoss, int round)
    {
        active = true;
        boss = isBoss;
        demon = attacker;
        damage = hitDamage;

        // 1. uhýbanie: jedna klávesa, od 2.: dve (aj písmeno + číslo); bossov parry je vždy jedna a kratšia
        int length = isBoss ? 1 : Mathf.Min(2, round);
        sequence = new KeyCode[length];
        for (int i = 0; i < length; i++)
        {
            KeyCode key;
            do key = Pool[Random.Range(0, Pool.Length)];
            while (i > 0 && key == sequence[i - 1]);
            sequence[i] = key;
        }
        if (length == 2 && Random.value < 0.5f)   // občas presne písmeno + číslo
        {
            sequence[0] = Pool[Random.Range(0, 4)];
            sequence[1] = Pool[Random.Range(4, 8)];
        }

        progress = 0;
        windowLength = isBoss ? bossWindowSeconds : windowSeconds;
        deadline = Time.unscaledTime + windowLength;

        restoreScale = Time.timeScale;
        Time.timeScale = slowScale;
    }

    void Update()
    {
        if (!active) return;

        if (player == null || player.IsDead || demon == null || demon.Damage == null || demon.Damage.IsDead) { End(false, false); return; }

        if (Time.unscaledTime >= deadline) { End(false, true); return; }

        // Správna klávesa posunie sekvenciu, akákoľvek iná zo zoznamu uhýbanie zruší
        for (int i = 0; i < Pool.Length; i++)
        {
            if (!GameInput.GetKeyDown(Pool[i])) continue;

            if (Pool[i] == sequence[progress])
            {
                progress++;
                GameAudio.Play(GameAudio.Sfx.Swing, player.transform.position);
                if (progress >= sequence.Length) End(true, true);
            }
            else End(false, true);
            return;
        }
    }

    void End(bool success, bool applyResult)
    {
        active = false;
        Time.timeScale = restoreScale;
        nextQte = Time.unscaledTime + cooldownSeconds;
        if (!applyResult || demon == null || player == null) return;

        Vector3 textPos = player.transform.position + Vector3.up * 2.2f;

        if (!success)
        {
            GameHUD.WorldText(textPos, "Nestihol si sa uhnúť!", new Color(1f, 0.4f, 0.3f));
            player.TakeDamage(damage, DamageSource.Demon, demon.clan);
            return;
        }

        GameHUD.WorldText(textPos, boss ? "ODRAZENÉ!" : "Uhol si sa!", new Color(0.5f, 1f, 0.6f));
        player.InvulnerableUntil = Mathf.Max(player.InvulnerableUntil, Time.time + 0.6f);
        demon.Parried(boss ? bossStunSeconds : stunSeconds);

        if (combat != null && player.Health >= counterMinHealth)
            combat.CounterAttack(demon.Damage, boss ? 3f : 2f);
        else
            GameHUD.WorldText(textPos + Vector3.up * 0.4f, "Si príliš zranený na protiútok", Color.gray);
    }

    // ---------- Zobrazenie ----------
    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (!active) return;

        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
        }

        // Tmavý okraj a text uprostred
        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        big.normal.textColor = boss ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.4f, 0.35f);
        GUI.Label(new Rect(cx - 300f, cy - 150f, 600f, 60f), boss ? "ODRAZ ÚDER!" : "UHNI SA!", big);

        // Klávesy: už stlačené svietia zeleno
        float boxSize = 78f, gap = 14f;
        float total = sequence.Length * boxSize + (sequence.Length - 1) * gap;
        for (int i = 0; i < sequence.Length; i++)
        {
            Rect r = new Rect(cx - total * 0.5f + i * (boxSize + gap), cy - 60f, boxSize, boxSize);
            GUI.color = i < progress ? new Color(0.3f, 0.9f, 0.4f, 0.9f) : new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;

            big.normal.textColor = Color.black;
            GUI.Label(r, KeyName(sequence[i]), big);
        }

        // Zostávajúci čas
        float left = Mathf.Clamp01((deadline - Time.unscaledTime) / windowLength);
        Rect bar = new Rect(cx - 200f, cy + 40f, 400f, 10f);
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = Color.Lerp(new Color(1f, 0.3f, 0.2f), new Color(0.4f, 0.9f, 1f), left);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * left, bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        small.normal.textColor = Color.white;
        GUI.Label(new Rect(cx - 300f, cy + 56f, 600f, 30f), sequence.Length > 1 ? "Stlač klávesy za sebou" : "Stlač klávesu", small);
    }

    static string KeyName(KeyCode key) => key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9 ? ((int)(key - KeyCode.Alpha0)).ToString() : key.ToString();
}
