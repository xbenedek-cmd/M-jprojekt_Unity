using UnityEngine;

/// <summary>
/// Kŕmenie: démon pije krv z civilistov a zvierat (drž kláves).
/// Z démonov sa krv piť nedá. Ľudská forma nepije.
/// Krv je konečná (zdroj sa vypije do dna), zvieratá dávajú menej krvi než ľudia.
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public class PlayerFeeding : MonoBehaviour
{
    public KeyCode feedKey => KeyBindings.Get("feed", KeyCode.Q);
    [Header("Kŕmenie (len v démonickej forme)")]
    public float feedRange = 2.2f;
    [Tooltip("Za koľko sekúnd vypije zdroj úplne")]
    public float feedDuration = 2.5f;
    [Tooltip("Rýchlosť pohybu počas kŕmenia (1 = normálna)")]
    public float moveSpeedWhileFeeding = 0.15f;

    public bool IsFeeding { get; private set; }

    private PlayerStats stats;
    private BloodSource current;
    private BloodSource candidate;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
    }

    void Update()
    {
        IsFeeding = false;

        if (stats.IsDead || !stats.IsDemon)
        {
            StopFeeding();
            candidate = null;
            return;
        }

        candidate = FindClosestSource();

        if (candidate != null && !GameInput.GetKey(feedKey))
        {
            string extra = candidate.IsAnimal ? " - zviera, menej krvi"
                         : (candidate.IsCorpse ? " - mŕtvola, krv sa kazí" : "");
            GameHUD.Prompt($"[{feedKey}] Piť krv (drž){extra}");
        }

        if (GameInput.GetKeyDown(feedKey) && candidate == null)
            ExplainWhyNot();

        bool full = stats.Blood >= stats.maxBlood - 0.01f;

        if (GameInput.GetKey(feedKey) && candidate != null)
        {
            if (full)
            {
                Say("Nie si hladný po krvi.");
                StopFeeding();
                return;
            }
            Feed(candidate, Time.deltaTime);
        }
        else
        {
            StopFeeding();
        }
    }

    void Feed(BloodSource source, float dt)
    {
        if (current != source)
        {
            StopFeeding();
            current = source;
        }

        current.BeingDrunk = true;
        CombatFx.Feeding(current.transform, transform);
        IsFeeding = true;

        // Krv je konečná: odoberie sa najviac to, čo v zdroji ešte zostalo
        float amount = Mathf.Min(current.bloodValue / feedDuration * dt, current.Remaining);
        current.Drain(amount);

        // Zviera dá hráčovi len časť vypitej krvi
        stats.DrinkBlood(amount * current.GainFactor);

        if (current.IsDead)
        {
            if (!current.Coagulated) stats.AddDemonXp(current.xpValue);
            Debug.Log($"Vypil si {current.displayName}. +{current.xpValue} XP");
            current = null;
            IsFeeding = false;
        }
    }

    void StopFeeding()
    {
        if (current != null) current.BeingDrunk = false;
        current = null;
    }

    BloodSource FindClosestSource()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, feedRange, ~0,
            QueryTriggerInteraction.Ignore);

        BloodSource best = null;
        float bestDist = float.MaxValue;

        foreach (Collider h in hits)
        {
            BloodSource s = h.GetComponentInParent<BloodSource>();
            if (s == null || s.IsDead || s.Remaining <= 0.01f) continue;

            float d = (s.transform.position - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = s;
            }
        }
        return best;
    }

    // Hráč-démon vidí démonov ako ľudí, tak ho upozorníme, že z nich krv nepotečie
    void ExplainWhyNot()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, feedRange, ~0,
            QueryTriggerInteraction.Ignore);

        foreach (Collider h in hits)
        {
            NPCPerception npc = h.GetComponentInParent<NPCPerception>();
            if (npc != null && npc.kind != NPCKind.Civilian && npc.GetComponent<BloodSource>() == null)
            {
                Say("Z démona sa krv piť nedá.");
                return;
            }

            BloodSource bs = h.GetComponentInParent<BloodSource>();
            if (bs != null && bs.Coagulated)
            {
                Say("Krv sa už zrazila.");
                return;
            }
        }
    }

    void Say(string text) => GameHUD.Message(text);

    // Priebeh pitia (správy a výzvy kreslí GameHUD)
    void OnGUI()
    {
        if (PauseMenu.AtStartup) return;   // za hlavným menu sa HUD nekreslí
        if (stats == null || stats.IsDead || !stats.IsDemon) return;
        if (!IsFeeding || current == null) return;

        Rect bar = new Rect(Screen.width / 2f - 100f, Screen.height * 0.62f, 200f, 18f);
        float fill = 1f - current.Remaining / Mathf.Max(0.01f, current.Capacity);

        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = new Color(0.55f, 0.05f, 0.25f);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(fill), bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(bar, current.IsAnimal ? " Piješ krv zvieraťa..." : " Piješ krv...");
        GUI.color = prev;
    }
}