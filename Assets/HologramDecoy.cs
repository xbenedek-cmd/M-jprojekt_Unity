using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holografický klam (Rebelanti): odláka nepriateľov od hráča.
/// Vzhľad = zmrazená kópia postavy hráča v jej aktuálnej póze, kreslená hologramovým shaderom
/// (modrý prízrak, skenovacie linky, trhanie); bez modelu sa použije kapsula.
/// </summary>
public class HologramDecoy : MonoBehaviour
{
    public static HologramDecoy Current { get; private set; }

    private static readonly List<Renderer> rendererBuffer = new List<Renderer>(16);
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

    private readonly List<Mesh> bakedMeshes = new List<Mesh>(4);
    private Material material;
    private float until, bornAt;
    private int hitsLeft = 3;

    public static HologramDecoy Spawn(Vector3 pos, Quaternion rot, float seconds, Transform source = null)
    {
        if (Current != null) Destroy(Current.gameObject);

        GameObject go = new GameObject("HologramDecoy");
        go.transform.SetPositionAndRotation(pos, rot);

        HologramDecoy d = go.AddComponent<HologramDecoy>();
        d.until = Time.time + seconds;
        d.bornAt = Time.time;
        Current = d;

        Shader shader = Resources.Load<Shader>("Hologram");
        d.material = new Material(shader != null ? shader : Shader.Find("Standard")) { hideFlags = HideFlags.HideAndDontSave };

        if (source == null || !d.CopyPose(source)) d.BuildCapsule();
        CombatFx.HitSpark(pos, Vector3.up, new Color(0.3f, 0.9f, 1f), 0.5f);
        return d;
    }

    // Každú časť modelu zapečie v aktuálnej póze do statickej siete (hologram sa nehýbe, len sa chveje v shaderi)
    bool CopyPose(Transform source)
    {
        source.GetComponentsInChildren(rendererBuffer);
        Quaternion inverse = Quaternion.Inverse(source.rotation);
        bool any = false;

        for (int i = 0; i < rendererBuffer.Count; i++)
        {
            Renderer r = rendererBuffer[i];
            if (!r.enabled) continue;

            // Časti, ktoré nie sú pri postave (napr. odložený meč), do hologramu nepatria
            if ((r.bounds.center - source.position).sqrMagnitude > 9f) continue;

            Mesh mesh;
            if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                skinned.BakeMesh(mesh, false);
                mesh.RecalculateBounds();
                bakedMeshes.Add(mesh);
            }
            else if (r is MeshRenderer && r.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) mesh = filter.sharedMesh;
            else continue;

            // Mierka a poloha sa berú zo skutočných hraníc časti vo svete: kostra modelu má vlastnú mierku,
            // takže zapečená sieť by inak vyšla stonásobne menšia alebo väčšia
            float meshSize = mesh.bounds.size.magnitude;
            if (meshSize < 0.0001f) continue;
            var part = new GameObject("Časť");
            part.transform.SetParent(transform, false);
            part.transform.localRotation = inverse * r.transform.rotation;
            part.transform.localScale = Vector3.one * (r.bounds.size.magnitude / meshSize);
            Vector3 wanted = transform.position + transform.rotation * (inverse * (r.bounds.center - source.position));
            part.transform.position += wanted - part.transform.TransformPoint(mesh.bounds.center);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            AddRenderer(part, mesh.subMeshCount);
            any = true;
        }
        rendererBuffer.Clear();
        return any;
    }

    void BuildCapsule()
    {
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.transform.SetParent(transform, false);
        Destroy(body.GetComponent<Collider>());
        AddRenderer(body, 1);
    }

    void AddRenderer(GameObject part, int subMeshes)
    {
        MeshRenderer mr = part.GetComponent<MeshRenderer>();
        if (mr == null) mr = part.AddComponent<MeshRenderer>();
        var materials = new Material[Mathf.Max(1, subMeshes)];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        mr.sharedMaterials = materials;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void Update()
    {
        if (Time.time >= until)
        {
            Destroy(gameObject);
            return;
        }

        // Nábeh pri vzniku a zhasínanie pred koncom
        float fadeIn = Mathf.Clamp01((Time.time - bornAt) / 0.25f);
        float fadeOut = Mathf.Clamp01((until - Time.time) / 0.5f);
        material.SetFloat(AlphaId, Mathf.Min(fadeIn, fadeOut));
    }

    /// <summary>Nepriateľ do neho udrel - po troch zásahoch zmizne.</summary>
    public void Absorb()
    {
        hitsLeft--;
        CombatFx.HitSpark(transform.position, Vector3.up, new Color(0.3f, 0.9f, 1f), 0.4f);
        if (hitsLeft <= 0) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        for (int i = 0; i < bakedMeshes.Count; i++) Destroy(bakedMeshes[i]);
        if (material != null) Destroy(material);
    }
}
