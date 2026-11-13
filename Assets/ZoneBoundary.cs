using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class ZoneBoundary : MonoBehaviour
{
    public float wallHeight = 3f;
    public float wallThickness = 0.5f;
    public float entranceWidth = 3.5f;
    public bool createEntrance = true;
    public Color wallColor = new Color(0.25f, 0.28f, 0.32f);

    private readonly List<GameObject> walls = new List<GameObject>();

    void Start()
    {
        Build();
    }

    void OnDestroy()
    {
        foreach (GameObject wall in walls)
            if (wall != null) Destroy(wall);
    }

    void Build()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        Bounds bounds = box.bounds;
        Vector3 center = transform.InverseTransformPoint(bounds.center);
        Vector3 size = new Vector3(
            bounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x)),
            wallHeight / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y)),
            bounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.z)));
        float halfX = size.x * 0.5f;
        float halfZ = size.z * 0.5f;

        bool militaryEntrance = createEntrance && GetComponent<MilitaryZone>() != null;
        if (militaryEntrance)
        {
            float halfEntrance = Mathf.Min(entranceWidth, size.x * 0.4f) * 0.5f;
            float sideWidth = (size.x - halfEntrance * 2f) * 0.5f;
            CreateWall("Boundary_North_Left", center + new Vector3(-(halfEntrance + sideWidth) * 0.5f, wallHeight * 0.5f, halfZ), new Vector3(sideWidth, wallHeight, wallThickness));
            CreateWall("Boundary_North_Right", center + new Vector3((halfEntrance + sideWidth) * 0.5f, wallHeight * 0.5f, halfZ), new Vector3(sideWidth, wallHeight, wallThickness));
        }
        else
        {
            CreateWall("Boundary_North", center + new Vector3(0f, wallHeight * 0.5f, halfZ), new Vector3(size.x, wallHeight, wallThickness));
        }
        CreateWall("Boundary_South", center + new Vector3(0f, wallHeight * 0.5f, -halfZ), new Vector3(size.x, wallHeight, wallThickness));
        CreateWall("Boundary_East", center + new Vector3(halfX, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, size.z));
        CreateWall("Boundary_West", center + new Vector3(-halfX, wallHeight * 0.5f, 0f), new Vector3(wallThickness, wallHeight, size.z));
    }

    void CreateWall(string wallName, Vector3 localPosition, Vector3 localScale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = wallName;
        wall.transform.SetParent(transform, false);
        wall.transform.localPosition = localPosition;
        wall.transform.localScale = localScale;

        Renderer renderer = wall.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = new Material(renderer.sharedMaterial);
            material.color = wallColor;
            renderer.sharedMaterial = material;
        }
        walls.Add(wall);
    }
}
