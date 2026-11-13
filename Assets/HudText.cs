/// <summary>
/// Cache pre text v OnGUI: reťazec sa zloží nanovo len keď sa zmení kľúč (zaokrúhlená hodnota), inak sa vráti ten istý objekt.
/// OnGUI beží aspoň 2× za snímku (Layout + Repaint), takže bez cache sa každý interpolovaný reťazec alokuje zbytočne.
/// Použitie: if (cache.Stale(a, b)) cache.Text = $"..."; ... cache.Text
/// </summary>
public sealed class HudText
{
    public string Text = "";
    private int k1 = int.MinValue, k2 = int.MinValue, k3 = int.MinValue;

    /// <summary>True, keď sa kľúč zmenil (a zároveň si ho zapamätá): treba text prepísať.</summary>
    public bool Stale(int a, int b = 0, int c = 0)
    {
        if (a == k1 && b == k2 && c == k3) return false;
        k1 = a; k2 = b; k3 = c;
        return true;
    }
}
