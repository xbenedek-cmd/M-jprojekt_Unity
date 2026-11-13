using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Kryt v aréne (chladiaca veža, potrubie): blokuje laser a Ren ho postupne ničí.</summary>
public class ArenaCover : MonoBehaviour
{
    public static readonly List<ArenaCover> All = new List<ArenaCover>();

    [SerializeField] private float health = 150f;
    private float maxHealth;
    private bool collapsed;

    public bool Alive => !collapsed;

    void OnEnable() { maxHealth = health; collapsed = false; All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public void Hit(float damage)
    {
        if (collapsed) return;
        health -= damage;
        if (health <= 0f) Collapse();
    }

    public void Collapse()
    {
        if (collapsed) return;
        collapsed = true;
        Vfx.Disc(transform.position + Vector3.up * 0.2f, 4f, new Color(0.6f, 0.55f, 0.5f, 0.6f), 0.7f);
        GameAudio.Play(GameAudio.Sfx.Build, transform.position);
        gameObject.SetActive(false);
    }

    /// <summary>Pre nový pokus: kryt sa obnoví.</summary>
    public void Restore()
    {
        health = maxHealth > 0f ? maxHealth : health;
        collapsed = false;
        gameObject.SetActive(true);
    }
}
