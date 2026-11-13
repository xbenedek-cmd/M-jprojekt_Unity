using System.Collections.Generic;
using UnityEngine;

/// <summary>Aréna klanovej elity: dialóg pred bojom a štart súboja. Spoločná pre všetky elity (Raizen, Gora, Ignis, Kuro).</summary>
public class EliteArena : MonoBehaviour
{
    private static readonly List<EliteArena> all = new List<EliteArena>();

    [SerializeField] private string bossId = "raizen";
    [SerializeField] private string speaker = "Lord Raizen";
    [SerializeField, TextArea] private string[] introLines;
    [SerializeField] private BossCore boss;
    [SerializeField] private Transform playerSpawn;

    private PlayerMovement movement;
    private bool started;

    public string BossId => bossId;
    public BossCore Boss => boss;

    void OnEnable() { all.Add(this); if (boss != null) boss.ArenaCenter = transform.position; }
    void OnDisable() { all.Remove(this); }

    public static EliteArena Find(string id)
    {
        for (int i = 0; i < all.Count; i++) if (all[i].bossId == id) return all[i];
        return null;
    }

    public void StartEncounter()
    {
        if (BossRegistry.IsDefeated(bossId) || boss == null || boss.InFight || started) return;
        if (movement == null) movement = FindAnyObjectByType<PlayerMovement>();
        if (movement == null) return;

        movement.Teleport(playerSpawn.position, playerSpawn.forward);
        started = true;
        MangaDialog.Show(speaker, introLines, () => { started = false; boss.BeginFight(); });
    }

    /// <summary>Ladenie: preskočí dialóg a spustí boj.</summary>
    public void DebugStartImmediately()
    {
        if (movement == null) movement = FindAnyObjectByType<PlayerMovement>();
        if (movement != null) movement.Teleport(playerSpawn.position, playerSpawn.forward);
        boss.BeginFight();
    }
}
