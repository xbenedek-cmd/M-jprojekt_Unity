using UnityEngine;

/// <summary>
/// Vyhýbanie pevným predmetom pre postavy, ktoré sa hýbu kinematicky (civilisti, vojaci, démoni, stráže): pred krokom sa kváder
/// v pásovej výške (nízke obrubníky a schody sa ignorujú) pozrie dopredu a ak je v ceste stena, sklo, strom alebo stavba,
/// krok sa stočí o 40° alebo 80° na stranu; ak sú blokované všetky smery, postava ostane stáť (nepreskočí cez predmet).
/// Postavy, hráč, spúšťače a podlaha sa za prekážku nepovažujú.
/// </summary>
public static class NpcSteering
{
    private static readonly RaycastHit[] hits = new RaycastHit[8];
    private static readonly float[] angles = { 0f, 40f, -40f, 80f, -80f };

    /// <summary>Krok `delta` upravený tak, aby nevošiel do pevného predmetu (smer môže byť stočený); nulový vektor = zablokované.</summary>
    public static Vector3 Adjust(Vector3 position, Vector3 delta, Transform self, float radius = 0.35f)
    {
        Vector3 flat = delta;
        flat.y = 0f;
        float distance = flat.magnitude;
        if (distance < 0.0001f) return delta;

        Vector3 direction = flat / distance;
        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 candidate = angles[i] == 0f ? direction : Quaternion.Euler(0f, angles[i], 0f) * direction;
            if (!Blocked(position, candidate, distance, self, radius)) return candidate * distance + Vector3.up * delta.y;
        }
        return Vector3.zero;
    }

    static bool Blocked(Vector3 position, Vector3 direction, float distance, Transform self, float radius)
    {
        // Kapsula od 0,35 m nad zemou po 1,4 m: zachytí aj nízke skaly (obrubníky a schody pod 0,35 m sa ignorujú)
        Vector3 lower = position + Vector3.up * (0.35f + radius);
        Vector3 upper = position + Vector3.up * Mathf.Max(1.4f, 0.35f + radius);
        int count = Physics.CapsuleCastNonAlloc(lower, upper, radius, direction, hits, distance + 0.25f, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            Collider c = hit.collider;

            if (hit.distance < 0.01f) continue;          // už sme v ňom (neuviazneme)
            if (hit.normal.y > 0.7f) continue;           // podlaha a mierne svahy
            if (c.transform == self || c.transform.IsChildOf(self)) continue;
            if (c.GetComponentInParent<NPCPerception>() != null || c.GetComponentInParent<PlayerStats>() != null) continue;
            if (c.GetComponentInParent<Damageable>() != null || c.GetComponentInParent<BloodSource>() != null) continue;
            return true;
        }
        return false;
    }
}
