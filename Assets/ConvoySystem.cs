using System;
using System.Collections;
using UnityEngine;

public enum ConvoyRoute { FastDangerous, SafeSlow }
public enum ConvoyType { Cheap, Escort }

/// <summary>
/// Konvoje z baní (zóna Bane): každé 3 dni je dostupný nový konvoj. Hráč volí trasu (rýchla a nebezpečná, alebo bezpečná a pomalá)
/// a typ: lacný konvoj sa vyrieši sám po 1–2 dňoch s rizikom prepadu, osobná eskorta je akcia v zóne Bane: vozík ide k portálu
/// a hráč ho chráni pred zálohami démonov. Náklad (šrot, kameň, kryštály, peniaze) príde do inventára.
/// </summary>
public static class ConvoySystem
{
    public const int IntervalDays = 3;
    public const float EscortDeliveryBonus = 1.6f;

    public static ConvoyRoute SelectedRoute = ConvoyRoute.SafeSlow;
    public static ConvoyType SelectedType = ConvoyType.Cheap;

    public static bool CheapActive { get; private set; }
    public static bool EscortActive { get; private set; }
    public static int NextOfferDay = 3;
    private static int arriveDay;
    private static ConvoyRoute activeRoute;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetState()
    {
        CheapActive = EscortActive = false;
        NextOfferDay = 3;
        arriveDay = 0;
        SelectedRoute = ConvoyRoute.SafeSlow;
        SelectedType = ConvoyType.Cheap;
    }

    public static string RouteName(ConvoyRoute r) => r == ConvoyRoute.FastDangerous ? "Rýchla a nebezpečná (1 deň)" : "Bezpečná a pomalá (2 dni)";
    public static string TypeName(ConvoyType t) => t == ConvoyType.Cheap ? "Lacný konvoj (riziko prepadu)" : "Osobná eskorta (akcia v Baniach)";

    /// <summary>Šanca prepadu lacného konvoja: trasa, bezpečnosť mesta (veže), občianska vojna.</summary>
    public static float RaidChance(ConvoyRoute route)
    {
        float chance = route == ConvoyRoute.FastDangerous ? 0.5f : 0.2f;
        foreach (BuildPlot plot in BuildPlot.All)
            if (plot.State == PlotState.Built && plot.Building != null && plot.Building.defenseRange > 0f) chance -= 0.03f;
        if (CityManagement.CivilWarDays > 0) chance += 0.15f;
        chance += Reputation.Suspicion / 400f;
        return Mathf.Clamp(chance, 0.05f, 0.85f);
    }

    public static bool Offered(int day) => !CheapActive && !EscortActive && day >= NextOfferDay;

    // ---------- Odoslanie ----------
    public static bool Dispatch(PlayerStats player, int day, out string message)
    {
        if (!Offered(day)) { message = CheapActive ? "Konvoj je na ceste." : EscortActive ? "Eskorta práve prebieha." : $"Ďalší konvoj bude dostupný v deň {NextOfferDay}."; return false; }

        if (SelectedType == ConvoyType.Cheap)
        {
            CheapActive = true;
            activeRoute = SelectedRoute;
            arriveDay = day + (SelectedRoute == ConvoyRoute.FastDangerous ? 1 : 2);
            message = $"Lacný konvoj vyrazil, dorazí v deň {arriveDay}. Riziko prepadu {RaidChance(SelectedRoute) * 100f:0} %.";
            return true;
        }

        return ConvoyRunner.StartEscort(player, out message);
    }

    public static void OnNewDay(int day, PlayerStats player)
    {
        if (!CheapActive || day < arriveDay) return;
        CheapActive = false;
        NextOfferDay = day + IntervalDays;

        if (UnityEngine.Random.value < RaidChance(activeRoute))
        {
            Reputation.AddSuspicion(5f);
            ArakaniFavor.AddFavor(2f);   // prepadnutie konvoja poteší démonov
            GameHUD.Message("Konvoj z baní bol prepadnutý a náklad je stratený!", 6f);
            return;
        }
        Deliver(player, 1f, "Konvoj z baní dorazil");
    }

    // ---------- Doručenie ----------
    public static void Deliver(PlayerStats player, float factor, string title)
    {
        NextOfferDay = Mathf.Max(NextOfferDay, 0);
        if (player == null) return;

        Inventory inventory = player.GetComponent<Inventory>();
        int scrap = Mathf.RoundToInt(UnityEngine.Random.Range(18, 29) * factor);
        int stone = Mathf.RoundToInt(UnityEngine.Random.Range(18, 27) * factor);
        int crystal = Mathf.RoundToInt(UnityEngine.Random.Range(2, 5) * factor);
        int money = Mathf.RoundToInt(UnityEngine.Random.Range(80, 161) * factor);

        int lost = Add(inventory, ItemCatalog.Scrap, scrap) + Add(inventory, ItemCatalog.Stone, stone) + Add(inventory, ItemCatalog.Crystal, crystal);
        player.AddMoney(money + lost * 4);   // čo sa nezmestí do inventára, sa vyplatí v peniazoch
        GameHUD.Message($"{title}: šrot {scrap}, kameň {stone}, kryštály {crystal}, +{money + lost * 4} peňazí.", 7f);
    }

    static int Add(Inventory inventory, string item, int count)
    {
        if (inventory == null) return count;
        int n = count;
        while (n > 0 && !inventory.TryAdd(item, n)) n--;
        return count - n;   // nezmestené kusy
    }

    // ---------- Eskorta skončila ----------
    public static void FinishEscort(bool success, PlayerStats player)
    {
        EscortActive = false;
        NextOfferDay = (UnityEngine.Object.FindAnyObjectByType<DayNightCycle>()?.Day ?? 0) + IntervalDays;
        if (success)
        {
            Deliver(player, EscortDeliveryBonus, "Eskorta splnená, náklad dorazil");
            Reputation.Add(FactionId.Rebels, 3f);
        }
        else
        {
            GameHUD.Message("Eskorta zlyhala: vozík je zničený a náklad stratený.", 6f);
            Reputation.AddSuspicion(4f);
        }
    }

    public static void SetEscortActive(bool value) => EscortActive = value;

    // ---------- Ukladanie ----------
    [Serializable]
    public class SaveData
    {
        public int nextOfferDay = 3, arriveDay;
        public bool cheapActive;
        public int route;
    }

    public static SaveData Export() => new SaveData { nextOfferDay = NextOfferDay, arriveDay = arriveDay, cheapActive = CheapActive, route = (int)activeRoute };

    public static void Import(SaveData data)
    {
        ResetState();
        if (data == null) return;
        NextOfferDay = data.nextOfferDay;
        arriveDay = data.arriveDay;
        CheapActive = data.cheapActive;
        activeRoute = (ConvoyRoute)data.route;
    }

    // ---------- Karta v menu ----------
    public static void DrawMenu(GUIStyle rich, PlayerStats player, int day, ref string status)
    {
        GUILayout.Label("<b>KONVOJE Z BANÍ</b> – nová zóna Bane (vytvor ju v menu Svet), nový konvoj každé 3 dni.", rich);

        if (CheapActive) GUILayout.Label($"Lacný konvoj je na ceste, príde v deň {arriveDay}.", rich);
        else if (EscortActive) GUILayout.Label("Eskorta práve prebieha v zóne Bane: dovez vozík k portálu.", rich);
        else if (!Offered(day)) GUILayout.Label($"Ďalší konvoj bude dostupný v deň {NextOfferDay}.", rich);

        GUILayout.Space(6f);
        GUILayout.Label("<b>Trasa</b>", rich);
        foreach (ConvoyRoute r in new[] { ConvoyRoute.FastDangerous, ConvoyRoute.SafeSlow })
        {
            bool on = SelectedRoute == r;
            if (GUILayout.Toggle(on, " " + RouteName(r) + $"  (riziko prepadu {RaidChance(r) * 100f:0} %)") && !on) SelectedRoute = r;
        }
        GUILayout.Space(4f);
        GUILayout.Label("<b>Typ</b>", rich);
        foreach (ConvoyType t in new[] { ConvoyType.Cheap, ConvoyType.Escort })
        {
            bool on = SelectedType == t;
            if (GUILayout.Toggle(on, " " + TypeName(t) + (t == ConvoyType.Escort ? "  (náklad ×1,6)" : "")) && !on) SelectedType = t;
        }

        GUILayout.Space(8f);
        GUI.enabled = Offered(day);
        if (GUILayout.Button("Vyslať konvoj", GUILayout.Height(34f)))
        {
            Dispatch(player, day, out string message);
            status = message;
        }
        GUI.enabled = true;
    }
}

/// <summary>Osobná eskorta: vozík ide v zóne Bane k portálu; zálohy démonov ho zdržia a poškodzujú. Hráč ho musí chrániť.</summary>
public class ConvoyRunner : MonoBehaviour
{
    private const float CartSpeed = 2.2f;
    private const float CartHealth = 320f;
    private const float AbandonSeconds = 25f;

    private static ConvoyRunner instance;

    private Transform cart;
    private Vector3 target;
    private float health, travelled, totalLength, abandonTimer;
    private Vector3 start;
    private PlayerStats player;
    private readonly float[] ambushAt = { 0.28f, 0.55f, 0.8f };
    private int nextAmbush;

    public static bool StartEscort(PlayerStats player, out string message)
    {
        GameObject root = ZoneLoader.GetRoot("Divocina_Bane");
        Transform gate = root != null ? root.transform.Find("Portal_zBane") : null;
        if (root == null || gate == null) { message = "Zóna Bane ešte nie je vytvorená (menu Progeny → Bane → Vytvoriť zónu Bane)."; return false; }

        if (instance != null) Destroy(instance.gameObject);
        instance = new GameObject("ConvoyRunner").AddComponent<ConvoyRunner>();
        instance.Begin(player, root.transform.position, gate.position);
        ConvoySystem.SetEscortActive(true);
        message = "Eskorta začala: vozík ide k portálu v Baniach. Chráň ho pred zálohami!";
        return true;
    }

    void Begin(PlayerStats p, Vector3 zoneCenter, Vector3 gatePosition)
    {
        player = p;
        start = zoneCenter + new Vector3(0f, 0f, 70f);
        target = gatePosition + new Vector3(0f, 0f, 4f);
        totalLength = Vector3.Distance(Flat(start), Flat(target));
        health = CartHealth;

        cart = BuildCart();
        cart.position = Grounded(start);
        cart.rotation = Quaternion.LookRotation(Flat(target) - Flat(start));

        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        if (movement != null) movement.Teleport(Grounded(start + new Vector3(2.5f, 0f, 3f)) + Vector3.up, Vector3.back);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static Vector3 Grounded(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 60f, Vector3.down, out RaycastHit hit, 140f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
        return p;
    }

    static Transform BuildCart()
    {
        var root = new GameObject("KonvojovyVozik").transform;
        Material wood = Mat(new Color(0.4f, 0.26f, 0.13f));
        Material metal = Mat(new Color(0.35f, 0.36f, 0.4f));
        Piece(root, new Vector3(0f, 0.7f, 0f), new Vector3(1.6f, 0.8f, 3.2f), wood);
        Piece(root, new Vector3(0f, 1.2f, -0.2f), new Vector3(1.3f, 0.4f, 2.4f), metal);
        foreach (Vector3 w in new[] { new Vector3(-0.9f, 0.35f, 1f), new Vector3(0.9f, 0.35f, 1f), new Vector3(-0.9f, 0.35f, -1f), new Vector3(0.9f, 0.35f, -1f) })
        {
            Transform wheel = Piece(root, w, new Vector3(0.7f, 0.18f, 0.7f), metal, PrimitiveType.Cylinder);
            wheel.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
        return root;
    }

    static Material Mat(Color c) => new Material(Shader.Find("Standard")) { color = c };

    static Transform Piece(Transform parent, Vector3 pos, Vector3 scale, Material m, PrimitiveType type = PrimitiveType.Cube)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g.transform;
    }

    void Update()
    {
        if (cart == null || player == null) { End(false); return; }
        if (player.IsDead) { End(false); return; }

        // Démoni pri vozíku ho zdržia a poškodzujú
        int attackers = 0;
        foreach (DemonEnemy demon in DemonEnemy.All)
            if (demon != null && demon.Damage != null && !demon.Damage.IsDead && (demon.transform.position - cart.position).sqrMagnitude < 9f * 9f) attackers++;

        health -= attackers * 7f * Time.deltaTime;
        if (health <= 0f) { End(false); return; }

        // Hráč nesmie vozík opustiť
        if ((player.transform.position - cart.position).sqrMagnitude > 70f * 70f) abandonTimer += Time.deltaTime; else abandonTimer = 0f;
        if (abandonTimer > AbandonSeconds) { End(false); return; }

        float progress = totalLength > 0f ? travelled / totalLength : 1f;
        if (nextAmbush < ambushAt.Length && progress >= ambushAt[nextAmbush]) { Ambush(); nextAmbush++; }

        if (attackers == 0)
        {
            Vector3 direction = (Flat(target) - Flat(cart.position)).normalized;
            Vector3 next = cart.position + direction * CartSpeed * Time.deltaTime;
            travelled += CartSpeed * Time.deltaTime;
            cart.position = Grounded(next);
            if (direction.sqrMagnitude > 0.01f) cart.rotation = Quaternion.Slerp(cart.rotation, Quaternion.LookRotation(direction), 4f * Time.deltaTime);
        }

        if (Vector3.Distance(Flat(cart.position), Flat(target)) < 3f) End(true);
    }

    // Záloha: až 3 voľné nižšie démoni sa objavia pred vozíkom
    void Ambush()
    {
        Vector3 ahead = cart.position + (Flat(target) - Flat(cart.position)).normalized * 26f;
        int pulled = 0;
        foreach (DemonEnemy demon in DemonEnemy.All)
        {
            if (pulled >= 3) break;
            if (demon == null || demon.tier == DemonTier.Boss || demon.Damage == null || demon.Damage.IsDead || demon.GetComponent<SurgeGuest>() != null) continue;

            demon.gameObject.AddComponent<SurgeGuest>().Begin(Grounded(ahead + new Vector3(UnityEngine.Random.Range(-6f, 6f), 0f, UnityEngine.Random.Range(-6f, 6f))) + Vector3.up * 0.4f, 150f);
            pulled++;
        }
        GameHUD.Message(pulled > 0 ? "Záloha! Démoni vyrazili na konvoj." : "Cesta je pokojná.", 3.5f);
    }

    void End(bool success)
    {
        if (cart != null) Destroy(cart.gameObject);
        ConvoySystem.FinishEscort(success, player != null ? player : FindAnyObjectByType<PlayerStats>());
        instance = null;
        Destroy(gameObject);
    }

    void OnGUI()
    {
        if (PauseMenu.AtStartup || cart == null) return;
        GUI.Label(new Rect(Screen.width * 0.5f - 160f, 12f, 320f, 24f), $"Eskorta: vozík {Mathf.CeilToInt(health)}/{CartHealth:0}  |  zostáva {Mathf.Max(0f, totalLength - travelled):0} m");
    }
}
