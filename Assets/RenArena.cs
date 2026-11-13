using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Strecha prekladiska: vstup (výťah) a priebeh stretnutia: dialóg pred bojom, potom BeginFight.</summary>
public class RenArena : MonoBehaviour
{
    public static RenArena Instance { get; private set; }

    [SerializeField] private RenBoss boss;
    [SerializeField] private Transform playerSpawn;
    [SerializeField] private Transform exitPoint;
    [SerializeField] private float radius = 30f;

    private PlayerMovement movement;
    private bool started;

    public Transform ExitPoint => exitPoint;

    void Awake()
    {
        Instance = this;
        if (boss != null) boss.ArenaCenter = transform.position;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void StartEncounter()
    {
        if (BossRegistry.IsDefeated("ren") || (boss != null && boss.InFight) || started) return;
        if (movement == null) movement = FindAnyObjectByType<PlayerMovement>();
        if (movement == null || boss == null) return;

        movement.Teleport(playerSpawn.position, playerSpawn.forward);
        started = true;
        MangaDialog.Show("Veliteľ Ren", new[]
        {
            "Hlásili mi, že niekto prežil prepad. Že niekto prežil aj smetisko.",
            "Bola som pri tom, keď si zomrel. Nechala som ťa tam, kde sa vyhadzuje odpad. Odpad sa nevracia.",
            "A predsa stojíš predo mnou, s démonom pod kožou. Dokončím to osobne."
        }, () => { started = false; boss.BeginFight(); });
    }

    /// <summary>Ladenie: preskočí dialóg a spustí boj.</summary>
    public void DebugStartImmediately()
    {
        if (movement == null) movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null) movement.Teleport(playerSpawn.position, playerSpawn.forward);
        boss.BeginFight();
    }

    public RenBoss Boss => boss;
}
