using UnityEngine;

/// <summary>
/// Trasa cesty, chodníka alebo stopy v divočine (body vo svetových súradniciach). Slúži NPC (drevorubači, hliadky) a ladeniu;
/// tvar cesty je namaľovaný v teréne, tento komponent drží len stredovú čiaru.
/// </summary>
public class WildRoadPath : MonoBehaviour
{
    [SerializeField] private string roadId;
    [SerializeField] private char kind;          // W hlavná, T chodník, H skrytá odbočka, A zvieracia stopa
    [SerializeField] private float width = 2f;
    [SerializeField] private Vector3[] points = new Vector3[0];

    public string RoadId => roadId;
    public char Kind => kind;
    public float Width => width;
    public Vector3[] Points => points;

    public void Set(string id, char roadKind, float roadWidth, Vector3[] worldPoints)
    {
        roadId = id;
        kind = roadKind;
        width = roadWidth;
        points = worldPoints;
    }

    /// <summary>Najbližší bod trasy k danému bodu (vodorovne).</summary>
    public Vector3 Nearest(Vector3 p)
    {
        Vector3 best = points.Length > 0 ? points[0] : transform.position;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 d = points[i] - p;
            d.y = 0f;
            float s = d.sqrMagnitude;
            if (s < bestSqr) { bestSqr = s; best = points[i]; }
        }
        return best;
    }
}
