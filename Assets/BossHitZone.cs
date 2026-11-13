using UnityEngine;

/// <summary>Zásahová zóna na modeli (ventily, kokpit...): násobí poškodenie od útočníka, ktorý stojí pri zóne. Stačí ju dať na kotvu v modeli.</summary>
public class BossHitZone : MonoBehaviour
{
    public string zoneId = "vent";
    public float radius = 2.6f;
    public float multiplier = 3f;
    public bool active;
}
