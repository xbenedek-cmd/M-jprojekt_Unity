using System;
using UnityEngine;

public class PerceptionToggle : MonoBehaviour
{
    // Ostatné systémy (NPC, démoni, UI) sa môžu pripojiť na túto udalosť
    public static event Action<bool> OnPerceptionChanged;
    public static bool IsDemonVision { get; private set; }

    [Header("Odkazy")]
    public DayNightCycle dayNight;        // GameManager s DayNightCycle
    public Light directionalLight;        // použije sa len ak dayNight chýba

    void Awake()
    {
        IsDemonVision = false;
    }

    void Start()
    {
        if (dayNight == null)
            dayNight = FindAnyObjectByType<DayNightCycle>();
    }

    void Update()
    {
        // Po stlačení klávesy E prepneme vnímanie
        if (GameInput.GetKeyDown(KeyBindings.Get("perception", KeyCode.E)))
            SetDemonVision(!IsDemonVision);
    }

    /// <summary>Nastaví vnímanie (volá E aj premena Človek/Démon).</summary>
    public void SetDemonVision(bool value)
    {
        if (IsDemonVision == value) return;
        IsDemonVision = value;

        if (IsDemonVision)
            Debug.Log("DEMONICKÉ VNÍMANIE ZAPNUTÉ: Svet sa mení, krvavý pach ožíva...");
        else
            Debug.Log("ĽUDSKÉ VNÍMANIE: Všetko sa vracia do normálu.");

        if (dayNight != null)
        {
            // Svetlo rieši DayNightCycle, aby sa nebili o farbu
            dayNight.SetReversedPerception(IsDemonVision);
        }
        else if (directionalLight != null)
        {
            // Záloha, ak v scéne cyklus dňa a noci nie je
            directionalLight.color = IsDemonVision ? new Color(0.8f, 0.1f, 0.1f) : Color.white;
        }

        OnPerceptionChanged?.Invoke(IsDemonVision);
    }
}
