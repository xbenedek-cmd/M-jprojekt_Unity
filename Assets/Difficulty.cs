using UnityEngine;

public enum GameDifficulty { Easy, Medium, Hard }

/// <summary>Obtiažnosť hry (nastavenia v menu). Zatiaľ ovplyvňuje nočné útoky na mesto a čistenie kontaminácie.</summary>
public static class Difficulty
{
    private const string Key = "difficulty";
    private static int cached = -1;

    public static readonly string[] Names = { "Ľahká", "Stredná", "Ťažká" };

    public static GameDifficulty Level
    {
        get
        {
            if (cached < 0) cached = Mathf.Clamp(PlayerPrefs.GetInt(Key, 1), 0, 2);
            return (GameDifficulty)cached;
        }
        set
        {
            cached = (int)value;
            PlayerPrefs.SetInt(Key, cached);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() => cached = -1;

    private static T Pick<T>(T easy, T medium, T hard) => Level == GameDifficulty.Easy ? easy : Level == GameDifficulty.Hard ? hard : medium;

    /// <summary>Veľkosť skupín a počet cieľov nočného útoku.</summary>
    public static float RaidScale => Pick(0.6f, 1f, 1.6f);
    /// <summary>Škody útočníkov na stavbách.</summary>
    public static float RaidDamage => Pick(0.6f, 1f, 1.5f);
    /// <summary>Koľko útokov na tú istú parcelu (ktoré skončia zničením stavby) kontaminuje jej pôdu.</summary>
    public static int ContaminationNights => Pick(3, 2, 1);
    /// <summary>Cena očistného rituálu.</summary>
    public static float CleanseCost => Pick(0.6f, 1f, 1.5f);
    /// <summary>Prvý deň, od ktorého démoni útočia na mesto.</summary>
    public static int FirstRaidDay => Pick(4, 3, 2);
}
