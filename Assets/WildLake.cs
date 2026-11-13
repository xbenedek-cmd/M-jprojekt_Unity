using UnityEngine;

/// <summary>
/// Značka jazera v divočine (vytvára ju Editor menu terénu). Drží základný polomer, hĺbku dna a polomer hladiny, aby sa jazero
/// pri opakovanom generovaní terénu nezväčšovalo a aby sa dal overiť, či objekt nestojí vo vode.
/// </summary>
public class WildLake : MonoBehaviour
{
    [Tooltip("Pôvodný polomer rybníka (m), z ktorého sa počíta kotlina")]
    public float baseRadius;
    [Tooltip("Hĺbka dna pod okolitou zemou (m)")]
    public float depth;
    [Tooltip("Polomer hladiny (m); za ním je breh")]
    public float waterRadius;
    [Tooltip("Polomer celej kotliny aj s brehom (m)")]
    public float basinRadius;

    /// <summary>Je bod (vodorovne) vo vode alebo na strmom brehu?</summary>
    public bool Covers(Vector3 p, float extra)
    {
        Vector3 d = p - transform.position;
        d.y = 0f;
        return d.sqrMagnitude < (waterRadius + extra) * (waterRadius + extra);
    }
}
