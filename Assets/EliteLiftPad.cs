using UnityEngine;

/// <summary>Výťah do arény elity (vstup) alebo späť dole po víťazstve (výstup).</summary>
public class EliteLiftPad : MonoBehaviour
{
    [SerializeField] private EliteArena arena;
    [SerializeField] private string label = "Lord Raizen";
    [SerializeField] private bool isExit;
    [SerializeField] private Transform destination;
    [SerializeField] private float useRadius = 3f;
    private PlayerMovement movement;

    void Update()
    {
        if (arena == null) return;
        if (movement == null) { movement = FindAnyObjectByType<PlayerMovement>(); if (movement == null) return; }
        if (MangaPanels.Active || MangaDialog.Active) return;

        Vector3 d = movement.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude > useRadius) return;

        bool defeated = BossRegistry.IsDefeated(arena.BossId);
        if (isExit)
        {
            if (!defeated || destination == null) return;
            GameHUD.Prompt("[E] Výťah dole");
            if (GameInput.GetKeyDown(KeyCode.E)) movement.Teleport(destination.position, destination.forward);
        }
        else
        {
            if (defeated) return;
            GameHUD.Prompt($"[E] Výťah do arény ({label})");
            if (GameInput.GetKeyDown(KeyCode.E)) arena.StartEncounter();
        }
    }
}
