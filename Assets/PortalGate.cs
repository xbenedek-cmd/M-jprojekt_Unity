using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Portál medzi zónami (napr. civilná polovica mesta ↔ Megakorporácia). Hráč-človek doňho vojde a objaví sa pri protiľahlom portáli.
/// Démon portály nepoužíva (jeho cesty sú korene stromov). Portál sa dá zatvoriť (nestabilita, eventy) cez SetOpen.
/// Zatiaľ ide o prechod v rámci jednej scény; neskôr sa môže načítať iná zóna.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PortalGate : MonoBehaviour
{
    [SerializeField] private PortalGate destination;
    [SerializeField] private string zoneName = "Zóna";
    [Tooltip("Pauza (s) po použití, aby hráča hneď nevrátil späť")]
    [SerializeField] private float cooldownSeconds = 1.5f;
    [SerializeField] private bool humansOnly = true;
    [SerializeField] private bool startsOpen = true;

    public static readonly List<PortalGate> All = new List<PortalGate>();

    private static float blockedUntil;
    private Renderer gateRenderer;
    private Color baseColor;

    public bool IsOpen { get; private set; }
    public string ZoneName => zoneName;
    public bool HasDestination => destination != null;
    public PortalGate Destination => destination;

    public void Link(PortalGate other, string name)
    {
        destination = other;
        zoneName = name;
    }

    /// <summary>Zmení len názov cieľa (nápis, hlášky); cieľ ostáva.</summary>
    public void Rename(string name) { zoneName = name; }

    /// <summary>Brána vedúca do danej zóny, najbližšia k bodu (zóna môže mať viac brán: Hradba aj Centrum majú vstup do Divočiny).</summary>
    public static PortalGate NearestTo(string zone, Vector3 from)
    {
        PortalGate best = null;
        float bestSqr = float.MaxValue;
        foreach (PortalGate g in All)
        {
            if (g == null || g.zoneName != zone) continue;
            float d = (g.transform.position - from).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = g; }
        }
        return best;
    }

    void Awake()
    {
        IsOpen = startsOpen;
        if (zoneName == WorldNames.LegacyStartCity) zoneName = WorldNames.StartCity;   // uložené brány ešte nesú starý názov
        gateRenderer = GetComponent<Renderer>();
        if (gateRenderer != null) baseColor = gateRenderer.material.color;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    void Reset()
    {
        Collider c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    /// <summary>Zatvorí / otvorí portál (výpadok, nestabilita, udalosť).</summary>
    public void SetOpen(bool open)
    {
        IsOpen = open;
        // Zatvorený portál je tmavý a bez žiary
        if (gateRenderer != null) gateRenderer.material.color = open ? baseColor : new Color(0.12f, 0.12f, 0.14f, 0.5f);
    }

    void OnTriggerEnter(Collider other)
    {
        if (destination == null || Time.time < blockedUntil) return;

        Rigidbody body = other.attachedRigidbody;
        if (body == null) return;

        PlayerStats stats = body.GetComponent<PlayerStats>();
        PlayerMovement movement = body.GetComponent<PlayerMovement>();
        if (stats == null || movement == null || stats.IsDead) return;

        if (humansOnly && stats.IsDemon)
        {
            blockedUntil = Time.time + 3f;
            GameHUD.Message("Démon portály nepoužíva. Stlač [5]: cesta vedie cez korene.", 3f);
            return;
        }

        // Nová hra: kým hráč nedokončí prológ (prvý hlavný quest), pustí ho len portál do Divočiny
        if (!Story.PrologueDone && !string.Equals(zoneName, "Divočina", System.StringComparison.Ordinal))
        {
            blockedUntil = Time.time + 3f;
            GameHUD.Message("Najprv dokonči úvodnú úlohu. Portál do Divočiny je jediný, ktorý je teraz otvorený.", 4f);
            return;
        }

        if (!IsOpen || !destination.IsOpen)
        {
            blockedUntil = Time.time + 3f;
            GameHUD.Message("Portál je nestabilný, prechod je zatvorený.", 3f);
            return;
        }

        blockedUntil = Time.time + cooldownSeconds;
        Vector3 arrival = destination.transform.position + destination.transform.forward * 3f + Vector3.up * 1f;

        // Hráč vyjde tak, ako vošiel: smer, ktorým do brány kráčal, sa stane smerom von z cieľovej brány
        // a pohľad kamery sa otočí o rovnaký uhol (inak by kamera ostala mieriť späť na portál)
        Vector3 inDir = body.linearVelocity;
        inDir.y = 0f;
        if (inDir.sqrMagnitude < 0.25f) { inDir = body.transform.forward; inDir.y = 0f; }
        Vector3 outDir = destination.transform.forward;
        outDir.y = 0f;
        float turn = inDir.sqrMagnitude > 0.0001f && outDir.sqrMagnitude > 0.0001f ? Vector3.SignedAngle(inDir, outDir, Vector3.up) : 0f;

        movement.TeleportGrounded(arrival, destination.transform.forward);
        Camera view = Camera.main;
        CameraFollow follow = view != null ? view.GetComponent<CameraFollow>() : null;
        if (follow != null) follow.Turn(turn);
        GameAudio.Play(GameAudio.Sfx.Portal, arrival);
        GameHUD.Message($"Portál: {zoneName}", 2.5f);
        PortalFollowers.OnPlayerUsedPortal(this, arrival);   // vojaci v poplachu idú za hráčom
    }
}
