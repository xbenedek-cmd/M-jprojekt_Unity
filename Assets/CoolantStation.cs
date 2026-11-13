using UnityEngine;

/// <summary>Chladiaca stanica v aréne Ignisa (náhrada vodného kanónu, kým taký nie je v hre): [E] vystrelí prúd vody a zmrazí jeho lávu na bazalt.</summary>
public class CoolantStation : MonoBehaviour
{
    [SerializeField] private int maxCharges = 2;
    [SerializeField] private float rechargeSeconds = 25f;
    [SerializeField] private float useRadius = 3.5f;

    public int Charges { get; private set; }

    private PlayerMovement movement;
    private IgnisBoss boss;
    private float nextRecharge;

    void OnEnable() { Charges = maxCharges; }

    void Update()
    {
        if (Charges < maxCharges && Time.time >= nextRecharge)
        {
            Charges++;
            nextRecharge = Time.time + rechargeSeconds;
        }

        if (boss == null) boss = IgnisBoss.Current as IgnisBoss;
        if (boss == null || !boss.InFight || boss.FinisherRunning) return;
        if (movement == null) { movement = FindAnyObjectByType<PlayerMovement>(); if (movement == null) return; }
        if (MangaPanels.Active || MangaDialog.Active) return;

        Vector3 d = movement.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude > useRadius) return;
        GameHUD.Prompt(Charges > 0 ? $"[E] Vodný kanón ({Charges}/{maxCharges})" : "Vodný kanón sa dobíja...");
        if (GameInput.GetKeyDown(KeyCode.E)) Fire();
    }

    /// <summary>Verejné pre testy: vystrelí rovnako ako hráč.</summary>
    public bool Fire()
    {
        if (Charges <= 0 || boss == null) return false;
        Vfx.Beam(transform.position + Vector3.up * 2f, boss.transform.position + Vector3.up * 4f, new Color(0.5f, 0.8f, 1f, 0.9f), 0.5f, 0.5f);
        GameAudio.Play(GameAudio.Sfx.TowerShot, transform.position);
        if (!boss.Cool())
        {
            GameHUD.Message("Ignis je ešte tuhý od posledného zmrazenia.", 2.5f);   // náboj sa neminie
            return false;
        }
        Charges--;
        nextRecharge = Time.time + rechargeSeconds;
        return true;
    }

    /// <summary>Obnova pri novom pokuse.</summary>
    public void Refill() { Charges = maxCharges; }
}
