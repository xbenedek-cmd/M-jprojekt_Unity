using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Prepisovateľné klávesy. Skripty si klávesu pýtajú cez Get(id, predvolená); hráč ju mení v Nastaveniach → Ovládanie.
/// Uložené v PlayerPrefs ("POA.Key.&lt;id&gt;"). Cache v slovníku, aby sa PlayerPrefs nečítali každý snímok.
/// </summary>
public static class KeyBindings
{
    public readonly struct Entry
    {
        public readonly string id, label;
        public readonly KeyCode def;
        public Entry(string id, string label, KeyCode def) { this.id = id; this.label = label; this.def = def; }
    }

    // Poradie = poradie v menu
    public static readonly Entry[] All =
    {
        new Entry("sprint", "Šprint", KeyCode.LeftShift),
        new Entry("dash", "Úhyb (dash)", KeyCode.LeftAlt),
        new Entry("jump", "Skok / šplhanie", KeyCode.Space),
        new Entry("crouch", "Čupnutie", KeyCode.LeftControl),
        new Entry("crouch2", "Čupnutie (druhá)", KeyCode.C),
        new Entry("cover", "Krytie pri stene", KeyCode.Y),
        new Entry("use", "Použiť / zbierať / skryť telo", KeyCode.F),
        new Entry("weapon", "Vytasiť / zasunúť zbraň", KeyCode.Q),
        new Entry("feed", "Napiť sa krvi (démon)", KeyCode.Q),
        new Entry("examine", "Skúmať postavu (drž)", KeyCode.T),
        new Entry("help", "Pomôcť civilistovi", KeyCode.L),
        new Entry("trade", "Obchod / rozhovor", KeyCode.G),
        new Entry("leap", "Dravý skok (démon, strom schopností)", KeyCode.H),
        new Entry("perception", "Démonické vnímanie", KeyCode.E),
        new Entry("senses", "Zmysly démona", KeyCode.V),
        new Entry("ability1", "Schopnosť frakcie 1", KeyCode.X),
        new Entry("ability2", "Schopnosť frakcie 2", KeyCode.C),
        new Entry("shadowstep", "Tieniaci skok", KeyCode.Z),
        new Entry("veil", "Závoj (Arakani)", KeyCode.Z),
        new Entry("inventory", "Inventár", KeyCode.I),
        new Entry("potion", "Rýchly lektvar", KeyCode.B),
        new Entry("potionPrev", "Predošlý lektvar", KeyCode.LeftBracket),
        new Entry("potionNext", "Ďalší lektvar", KeyCode.RightBracket),
        new Entry("wardrobe", "Šatník", KeyCode.O),
        new Entry("appearance", "Vzhľad", KeyCode.U),
        new Entry("plot", "Parcela (stavanie)", KeyCode.P),
        new Entry("lair", "Skrýša démona", KeyCode.F7),
        new Entry("popHud", "Panel populácie", KeyCode.F2),
        new Entry("save", "Rýchle uloženie", KeyCode.F9),
    };

    private static readonly Dictionary<string, KeyCode> cache = new Dictionary<string, KeyCode>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() => cache.Clear();

    public static KeyCode Get(string id, KeyCode fallback)
    {
        if (cache.TryGetValue(id, out KeyCode key)) return key;

        key = (KeyCode)PlayerPrefs.GetInt("POA.Key." + id, (int)fallback);
        cache[id] = key;
        return key;
    }

    public static KeyCode Get(string id)
    {
        foreach (Entry e in All)
            if (e.id == id) return Get(id, e.def);
        return KeyCode.None;
    }

    public static void Set(string id, KeyCode key)
    {
        cache[id] = key;
        PlayerPrefs.SetInt("POA.Key." + id, (int)key);
        PlayerPrefs.Save();
    }

    public static void ResetAll()
    {
        foreach (Entry e in All) PlayerPrefs.DeleteKey("POA.Key." + e.id);
        PlayerPrefs.Save();
        cache.Clear();
    }

    // Názvy pre menu: stručnejšie než KeyCode.ToString()
    public static string Name(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.LeftShift: return "L-Shift";
            case KeyCode.RightShift: return "R-Shift";
            case KeyCode.LeftControl: return "L-Ctrl";
            case KeyCode.RightControl: return "R-Ctrl";
            case KeyCode.LeftAlt: return "L-Alt";
            case KeyCode.RightAlt: return "R-Alt";
            case KeyCode.LeftBracket: return "[";
            case KeyCode.RightBracket: return "]";
            case KeyCode.Space: return "Medzerník";
            case KeyCode.None: return "—";
            default:
                string s = key.ToString();
                return s.StartsWith("Alpha") ? s.Substring(5) : s;
        }
    }
}
