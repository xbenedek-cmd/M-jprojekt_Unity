using UnityEngine;

/// <summary>
/// Sada animačných klipov pre démonov (Mixamo: kostra mixamorig:*). Jedna sada sa používa pre všetky úrovne démonov.
/// Vytvorí ju menu Progeny → Animácie démona → Vytvoriť sadu z Mixamo klipov.
/// </summary>
[CreateAssetMenu(fileName = "DemonAnimationSet", menuName = "Progeny/Demon Animation Set")]
public class DemonAnimationSet : ScriptableObject
{
    public AnimationClip idle;
    [Tooltip("Beh alebo chôdza. Ak chýba Idle, použije sa zmrazené prvé okno tohto klipu.")]
    public AnimationClip run;
    [Tooltip("Voliteľné: rozbeh (Start Walking). Prehrá sa len raz pri rozbehu z miesta, potom plynule prejde do Run/Walking, ktorá beží do zastavenia.")]
    public AnimationClip startWalk;
    public AnimationClip attack;
    [Tooltip("Druhý, silnejší útok (Illegal Elbow Punch). Strieda sa s bežným úderom podľa DemonEnemy.elbowChance.")]
    public AnimationClip attackElbow;
    [Tooltip("Tretí variant útoku (kop, napr. Chapa-Giratoria). Používa ho hráč.")]
    public AnimationClip attackKick;
    [Tooltip("Blok: prehráva sa, kým hráč drží blok (napr. 'block idle')")]
    public AnimationClip block;
    [Tooltip("Vytasenie / zasunutie zbrane (jednorazové)")]
    public AnimationClip drawWeapon;
    public AnimationClip sheathWeapon;
    [Tooltip("Čupnutie (Mixamo: Crouch Idle / Crouched Walking). Bez nich sa postava pri čupnutí len nakloní.")]
    public AnimationClip crouchIdle;
    public AnimationClip crouchWalk;

    [Tooltip("Skok. Používa ho len hráč; NPC a démoni nemajú klip skoku, takže skákať nevedia.")]
    public AnimationClip jump;

    [Tooltip("Rýchly beh (šprint, Shift). Používa ho len hráč; bez klipu sa pri šprinte len zrýchli Run.")]
    public AnimationClip sprint;
    [Tooltip("Rýchlosť (m/s), pri ktorej sa Sprint prehráva v normálnom tempe")]
    public float sprintReferenceSpeed = 7f;

    [Tooltip("Vstávanie (jednorazové). Hráč ho prehrá po prepustení z väzenia.")]
    public AnimationClip standUp;

    [Tooltip("Vyšplhanie na rímsu (jednorazové). Používa ho len hráč.")]
    public AnimationClip climb;
    [Tooltip("Krytie pri stene (jednorazové, výsledná póza sa drží). Používa ho len hráč.")]
    public AnimationClip coverTurn;

    [Tooltip("Dopad na zem po skoku alebo páde (jednorazové, Jumping Down). Používa ho len hráč.")]
    public AnimationClip land;

    [Tooltip("Útoky s mečom: kratší klik = ľahký útok (náhodne jeden z klipov), dlhší klik = ťažký. Používa ich len hráč; prázdne = varianty útoku vyššie.")]
    public AnimationClip[] lightAttacks;
    public AnimationClip[] heavyAttacks;

    [HideInInspector] public int builderVersion;
    [Tooltip("Smrť: pád dopredu. Pri smrti sa náhodne vyberie jedna z dvoch (50/50); ak je len jedna, použije sa tá.")]
    public AnimationClip deathForward;
    public AnimationClip deathBack;
    [Tooltip("Voliteľné: premena vyššieho démona z človeka po skúmaní (T)")]
    public AnimationClip transformation;

    [Tooltip("Rýchlosť (m/s), pri ktorej sa Run prehráva v normálnom tempe")]
    public float runReferenceSpeed = 3.2f;
    public float crossFadeTime = 0.15f;

    public bool HasAnyClip => idle != null || run != null || startWalk != null || attack != null || attackElbow != null
                              || attackKick != null || deathForward != null || deathBack != null || transformation != null
                              || crouchIdle != null || crouchWalk != null;
}
