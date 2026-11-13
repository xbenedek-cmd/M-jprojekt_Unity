using UnityEngine;

/// <summary>Očistný stôl doktorky Yumi v aréne Carmilly (náhrada očistného séra, kým také nie je v hre): [E] vypiješ sérum a tvoja krv je jed pre Carmillu.</summary>
public class SerumStation : MonoBehaviour
{
    [SerializeField] private int maxCharges = 2;
    [SerializeField] private float rechargeSeconds = 30f;
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

        CarmillaBoss boss = BossCore.Current as CarmillaBoss;
        if (boss == null || !boss.InFight || boss.FinisherRunning) return;
        if (movement == null) { movement = FindAnyObjectByType<PlayerMovement>(); if (movement == null) return; }
        if (MangaPanels.Active || MangaDialog.Active) return;

        Vector3 d = movement.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude > useRadius) return;
        GameHUD.Prompt(boss.Purified ? "Už si očistený" : Charges > 0 ? $"[E] Očistné sérum ({Charges}/{maxCharges})" : "Sérum sa dopĺňa...");
        if (GameInput.GetKeyDown(KeyCode.E)) Use();
    }

    /// <summary>Verejné pre testy: vypije sérum rovnako ako hráč.</summary>
    public bool Use()
    {
        CarmillaBoss boss = BossCore.Current as CarmillaBoss;
        if (Charges <= 0 || boss == null || boss.Purified) return false;
        Charges--;
        nextRecharge = Time.time + rechargeSeconds;
        boss.Purify(0f);
        return true;
    }
}
