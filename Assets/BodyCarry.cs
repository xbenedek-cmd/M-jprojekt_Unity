using UnityEngine;

/// <summary>
/// Nosenie tiel: hráč zdvihne mŕtveho človeka (civilista, odbojár, vojak, strážnik) klávesou „použiť“. S telom nešprintuje, nešplhá
/// ani nehýbe sa úhybom, len pomaly kráča. Položiť ho môže kdekoľvek; telo zmizne, keď ho vojak nájde a skontroluje,
/// alebo keď ho nikto nenájde do 3 minút (Damageable.HumanCorpseSeconds, čas neplynie, kým ho nesie).
/// Vojak, ktorý vidí nesené telo, hráča hneď odhalí.
/// </summary>
public class BodyCarry : MonoBehaviour
{
    public static bool Carrying { get; private set; }
    /// <summary>Rýchlosť chôdze s telom oproti normálnej.</summary>
    public const float SpeedFactor = 0.4f;

    private const float PickupRange = 2.5f;

    private PlayerStats stats;
    private Damageable body;
    private Collider[] bodyColliders;
    private StealthWorld.Body entry;
    private float nextSync;

    void OnDisable()
    {
        if (body != null) Drop();
        Carrying = false;
    }

    /// <summary>Vráti true, ak ukázal výzvu (potom sa výzva na tiché zabitie nezobrazuje).</summary>
    public bool Tick()
    {
        if (stats == null) stats = GetComponent<PlayerStats>();
        if (stats == null) return false;

        if (body != null && (stats.IsDead || body.IsHidden || !body.gameObject.activeInHierarchy))
        {
            Drop();
            return false;
        }
        if (Healer.MenuOpen || PauseMenu.IsOpen || stats.IsDead) return false;

        KeyCode key = KeyBindings.Get("use", KeyCode.F);

        if (body != null)
        {
            GameHUD.Prompt($"[{key}] Položiť telo (nesieš ho pomaly)");
            if (GameInput.GetKeyDown(key)) Drop();
            return true;
        }

        Damageable nearest = FindNearestBody();
        if (nearest == null) return false;

        GameHUD.Prompt($"[{key}] Zobrať telo: {nearest.displayName}");
        if (GameInput.GetKeyDown(key)) Pick(nearest);
        return true;
    }

    void LateUpdate()
    {
        if (body == null || Time.time < nextSync) return;
        nextSync = Time.time + 0.2f;

        // Vojaci hľadajú telo podľa zapísanej polohy: nesené telo ide s hráčom
        if (entry == null)
            foreach (StealthWorld.Body b in StealthWorld.Bodies)
                if (b.corpse == body) { entry = b; break; }
        if (entry != null) entry.pos = body.transform.position;
    }

    Damageable FindNearestBody()
    {
        Damageable nearest = null;
        float best = PickupRange;

        foreach (Damageable corpse in Damageable.AllCorpses)
        {
            if (corpse == null || corpse.IsHidden || corpse.IsCarried || !corpse.IsHumanBody) continue;

            float d = Vector3.Distance(transform.position, corpse.transform.position);
            if (d < best) { best = d; nearest = corpse; }
        }
        return nearest;
    }

    void Pick(Damageable corpse)
    {
        body = corpse;
        entry = null;
        body.IsCarried = true;
        Carrying = true;

        // Kolízia tela by hráča zasekávala
        bodyColliders = body.GetComponentsInChildren<Collider>();
        foreach (Collider c in bodyColliders) c.enabled = false;

        body.transform.SetParent(transform);
        body.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        body.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);   // prehodené cez plecia
        GameHUD.Message("Zdvihol si telo. Nešprintuješ ani neskáčeš.", 2.5f);
    }

    void Drop()
    {
        Damageable corpse = body;
        body = null;
        entry = null;
        Carrying = false;
        if (corpse == null) return;

        corpse.IsCarried = false;
        if (corpse.transform.parent == transform) corpse.transform.SetParent(null);

        Vector3 at = transform.position + transform.forward * 1.2f;
        corpse.transform.position = at;
        corpse.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (bodyColliders != null)
            foreach (Collider c in bodyColliders)
                if (c != null) c.enabled = true;
        bodyColliders = null;

        // Uloží telo na zem ako po smrti
        if (!corpse.IsHidden) Corpse.LayDown(corpse.transform);
    }
}
