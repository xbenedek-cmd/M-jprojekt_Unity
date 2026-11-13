using UnityEngine;

/// <summary>Kotvy modelu bossa: pri výmene primitív za skutočný model stačí priradiť tieto odkazy (kód bossa na nich nič iné nepredpokladá).</summary>
public class BossModelRig : MonoBehaviour
{
    public Transform muzzleLeft, muzzleRight, cockpit, flame, fist;
    public GameObject shieldVisual;
    [Tooltip("Svietia červeno pri otvorených ventiloch")]
    public Renderer[] ventRenderers;
    [Tooltip("Blikajú pri telegrafovaní útoku (prázdne = všetky renderery v modeli)")]
    public Renderer[] flashRenderers;
}
