using UnityEngine;

/// <summary>Svetlomet v aréne Lady Kuro (náhrada silného svetelného zdroja, kým taký nie je v hre): [E] zažiari a rozpustí ilúzie a neviditeľnosť.</summary>
public class LightStation : MonoBehaviour
{
    [SerializeField] private int maxCharges = 2;
    [SerializeField] private float rechargeSeconds = 25f;
    [SerializeField] private float useRadius = 3.5f;

    public int Charges { get; private set; }

    private PlayerMovement movement;
    private float nextRecharge;

    void OnEnable() { Charges = maxCharges; }

    void Update()
    {
        if (Charges < maxCharges && Time.time >= nextRecharge)
        {
            Charges++;
            nextRecharge = Time.time + rechargeSeconds;
        }

        KuroBoss boss = BossCore.Current as KuroBoss;
        if (boss == null || !boss.InFight || boss.FinisherRunning) return;
        if (movement == null) { movement = FindAnyObjectByType<PlayerMovement>(); if (movement == null) return; }
        if (MangaPanels.Active || MangaDialog.Active) return;

        Vector3 d = movement.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude > useRadius) return;
        GameHUD.Prompt(Charges > 0 ? $"[E] Svetlomet ({Charges}/{maxCharges})" : "Svetlomet sa dobíja...");
        if (GameInput.GetKeyDown(KeyCode.E)) Fire();
    }

    /// <summary>Verejné pre testy: zažiari rovnako ako hráč. Náboj sa minie len ak svetlo niečo odhalilo.</summary>
    public bool Fire()
    {
        KuroBoss boss = BossCore.Current as KuroBoss;
        if (Charges <= 0 || boss == null) return false;
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 16f, new Color(1f, 0.95f, 0.7f, 0.5f), 0.5f);
        GameAudio.Play(GameAudio.Sfx.Portal, transform.position);
        if (!boss.Reveal("Svetlo rozpustilo ilúzie a odhalilo Kuro!"))
        {
            GameHUD.Message("Svetlo nič neodhalilo.", 2f);
            return false;
        }
        Charges--;
        nextRecharge = Time.time + rechargeSeconds;
        return true;
    }
}
