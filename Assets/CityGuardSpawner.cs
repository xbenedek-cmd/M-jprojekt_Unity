using UnityEngine;

public class CityGuardSpawner : MonoBehaviour
{
    public GameObject guardPrefab;
    public DistrictWard district = DistrictWard.Military;
    public int count = 4;
    public Vector3 spawnArea = new Vector3(12f, 0f, 12f);
    public bool spawnOnStart = true;

    void Start()
    {
        if (spawnOnStart)
            Spawn();
    }

    [ContextMenu("Spawn guards")]
    public void Spawn()
    {
        if (guardPrefab == null)
        {
            Debug.LogWarning("CityGuardSpawner: missing guardPrefab.");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new Vector3(
                Random.Range(-spawnArea.x, spawnArea.x),
                0f,
                Random.Range(-spawnArea.z, spawnArea.z));

            Vector3 spawnPos = transform.position + offset;
            GameObject guard = Instantiate(guardPrefab, spawnPos, Quaternion.identity, transform);

            var grounding = guard.GetComponent<NPCGrounding>();
            if (grounding == null)
                grounding = guard.AddComponent<NPCGrounding>();
            grounding.SnapToGround();

            var npc = guard.GetComponent<CityGuardNPC>();
            if (npc == null)
                npc = guard.AddComponent<CityGuardNPC>();

            npc.district = district;

            var outfit = guard.GetComponent<DistrictOutfit>();
            if (outfit == null)
                outfit = guard.AddComponent<DistrictOutfit>();

            outfit.district = district;
            outfit.ApplyDistrictColors();
        }
    }
}
