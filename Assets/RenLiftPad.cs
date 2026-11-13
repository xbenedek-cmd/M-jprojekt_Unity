using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Výťah na strechu prekladiska (vstup) alebo späť dole (výstup po boji).</summary>
public class RenLiftPad : MonoBehaviour
{
    [SerializeField] private bool isExit;
    [SerializeField] private Transform destination;
    [SerializeField] private float useRadius = 3f;
    private PlayerMovement movement;

    void Update()
    {
        if (movement == null) { movement = FindAnyObjectByType<PlayerMovement>(); if (movement == null) return; }
        if (MangaPanels.Active || MangaDialog.Active) return;

        Vector3 d = movement.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude > useRadius) return;

        if (isExit)
        {
            if (!BossRegistry.IsDefeated("ren") || destination == null) return;
            GameHUD.Prompt("[E] Výťah dole");
            if (GameInput.GetKeyDown(KeyCode.E)) movement.Teleport(destination.position, destination.forward);
        }
        else
        {
            if (BossRegistry.IsDefeated("ren") || RenArena.Instance == null) return;
            GameHUD.Prompt("[E] Výťah na strechu prekladiska (Veliteľ Ren)");
            if (GameInput.GetKeyDown(KeyCode.E)) RenArena.Instance.StartEncounter();
        }
    }
}
