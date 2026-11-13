using UnityEngine;

/// <summary>Dravý skok (strom schopností, vetva Démon): rýchly výpad dopredu v podobe démona. Kláves R (prepisovateľný), čakanie 6 s, stojí výdrž.</summary>
public class DemonLeap : MonoBehaviour
{
    private const float Cooldown = 6f, Speed = 19f, Duration = 0.26f, StaminaCost = 25f;

    private PlayerStats stats;
    private PlayerMovement movement;
    private Transform cam;
    private float readyAt;
    private bool hinted;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        movement = GetComponent<PlayerMovement>();
    }

    void Update()
    {
        if (stats == null || movement == null || stats.IsDead || !stats.IsDemon) return;
        if (SkillTree.Sum(SkillStat.Leap) <= 0f) return;

        if (!hinted)
        {
            hinted = true;
            GameHUD.Message($"Dravý skok: [{KeyBindings.Get("leap", KeyCode.H)}] rýchly výpad dopredu.", 4f);
        }

        if (!GameInput.GetKeyDown(KeyBindings.Get("leap", KeyCode.H)) || Time.time < readyAt) return;
        if (PauseMenu.IsOpen || Healer.MenuOpen || PlotManager.MenuOpen) return;
        if (!stats.UseStamina(StaminaCost)) { GameHUD.WorldText(transform.position + Vector3.up * 1.8f, "Vyčerpaný!", Color.gray); return; }

        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        Vector3 dir = cam != null ? cam.forward : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = transform.forward;

        readyAt = Time.time + Cooldown;
        stats.InvulnerableUntil = Mathf.Max(stats.InvulnerableUntil, Time.time + Duration);
        movement.Dash(dir.normalized, Speed, Duration);
    }
}
