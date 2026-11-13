using UnityEngine;
using System.Collections;

/// <summary>
/// Špeciálny dron pre Megakorporáciu. 
/// Vyletí, zakrúži okolo hráča, naskenuje okolie a odhalí všetkých démonov.
/// </summary>
public class CorpDrone : MonoBehaviour
{
    private const float ScanRadius = 35f;

    public static CorpDrone Spawn(Vector3 playerPos)
    {
        GameObject go = new GameObject("CorpDrone");
        go.transform.position = playerPos + Vector3.up * 1f;

        CorpDrone drone = go.AddComponent<CorpDrone>();
        drone.StartCoroutine(drone.FlyAndScanRoutine(playerPos));
        return drone;
    }

    private IEnumerator FlyAndScanRoutine(Vector3 playerPos)
    {
        // Vytvorenie vizuálu (biela guľa s červeným svetlom)
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(body.GetComponent<Collider>());
        body.transform.SetParent(transform);
        body.transform.localScale = Vector3.one * 0.4f;
        body.GetComponent<Renderer>().material.color = new Color(0.8f, 0.9f, 1f);

        GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(eye.GetComponent<Collider>());
        eye.transform.SetParent(transform);
        eye.transform.localPosition = new Vector3(0, 0, 0.2f);
        eye.transform.localScale = Vector3.one * 0.15f;
        eye.GetComponent<Renderer>().material.color = Color.red;

        // 1. Fáza: Vzlietnutie a rozhliadnutie (1 sekunda)
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime;
            transform.Rotate(Vector3.up, 360f * Time.deltaTime);
            transform.position = playerPos + Vector3.up * (1f + t * 2f); // Stúpa nahor
            yield return null;
        }

        // 2. Fáza: Skenovanie (vizuálny pulz)
        Vfx.Disc(playerPos + Vector3.down * 0.45f, ScanRadius, new Color(0.3f, 0.8f, 1f), 0.6f);
        GameHUD.Message("Dron skenuje okolie...", 2f);

        int revealedCount = 0;
        int markedCount = 0;

        // Nájde všetkých démonov v okolí (ScanRadius)
        foreach (DemonEnemy e in FindObjectsByType<DemonEnemy>())
        {
            if (e == null || e.Damage == null || e.Damage.IsDead) continue;

            float dist = Vector3.Distance(playerPos, e.transform.position);
            if (dist <= ScanRadius)
            {
                NPCPerception npc = e.GetComponent<NPCPerception>();
                if (npc != null)
                {
                    // Odhalí démona (ak ešte nebol)
                    if (!npc.Revealed)
                    {
                        npc.Reveal();
                        revealedCount++;
                    }
                }

                // Zachová pôvodnú funkciu Corporate Strike (označenie na +25% dmg)
                e.Damage.Mark(6f, 1.25f);
                Vfx.Marker(e.transform, new Color(1f, 0.3f, 0.2f), 6f);
                ScanHighlight.Apply(e.transform, new Color(1f, 0.4f, 0.1f), 12f);   // bliká a je vidieť aj za budovou
                markedCount++;
            }
        }

        if (revealedCount > 0) GameHUD.Message($"Dron odhalil {revealedCount} maskovaných démonov!", 4f);
        else if (markedCount > 0) GameHUD.Message($"Dron označil {markedCount} démonov (+25 % poškodenie)!", 4f);
        else GameHUD.Message("Dron nenašiel žiadnych démonov.", 3f);

        // 3. Fáza: Odlet preč (1 sekunda)
        t = 0f;
        Vector3 startPos = transform.position;
        while (t < 1f)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(startPos, startPos + Vector3.up * 10f, t);
            yield return null;
        }

        Destroy(gameObject);
    }
}
